#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Runtime;
using Testcontainers.Floci;
using Xunit;
using Xunit.Abstractions;
using Record = Amazon.Kinesis.Model.Record;

namespace Akka.Streams.Kinesis.Tests;

public sealed class KinesisIntegrationSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output), IAsyncLifetime
{
    private readonly FlociContainer _broker = new FlociBuilder("floci/floci:1.5.13").Build();
    private readonly ConcurrentBag<OwnedClient> _owned = new();
    private AmazonKinesisClient? _native;
    private string _stream = null!;
    private string _shard = null!;
    private AmazonKinesisClient Native => _native ?? throw new InvalidOperationException("Kinesis fixture has not started.");

    public async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _broker.StartAsync(startup.Token);
            _native = new AmazonKinesisClient(new BasicAWSCredentials("test", "test"), Config(_broker.GetConnectionString()));
            _stream = "alpakka-" + Guid.NewGuid().ToString("N");
            await Native.CreateStreamAsync(new CreateStreamRequest { StreamName = _stream, ShardCount = 1 }, startup.Token);
            while (true)
            {
                var summary = await Native.DescribeStreamSummaryAsync(new DescribeStreamSummaryRequest { StreamName = _stream }, startup.Token);
                if (summary.StreamDescriptionSummary.StreamStatus == StreamStatus.ACTIVE) break;
                await Task.Delay(100, startup.Token);
            }
            _shard = Assert.Single((await Native.ListShardsAsync(new ListShardsRequest { StreamName = _stream }, startup.Token)).Shards).ShardId;
        }
        catch (Exception startupException)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Kinesis startup and cleanup both failed.", startupException, cleanupException);
            }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            var termination = Sys.Terminate();
            await AwaitAssertAsync(() => Assert.True(termination.IsCompleted), TimeSpan.FromSeconds(15));
            await termination;
        }
        finally
        {
            try
            {
                foreach (var client in _owned.Where(client => client.DisposeCount == 0)) client.Dispose();
            }
            finally
            {
                try { _native?.Dispose(); }
                finally { await _broker.DisposeAsync(); }
            }
        }
    }

    [Theory]
    [InlineData("records")]
    [InlineData("bytes")]
    [InlineData("data")]
    public async Task Publishing_modes_match_native_payloads_and_sequence_numbers_and_dispose_their_client(string mode)
    {
        var expected = new[] { "first", "café", "last" };
        var settings = KinesisFlowSettings.Default.WithParallelism(1).WithMaxBatchSize(3);
        var source = Source.From(expected);
        IReadOnlyList<PutRecordsResultEntry> published;
        switch (mode)
        {
            case "records":
                published = await Completed(source.Select(Entry).Via(KinesisFlow.Create(_stream, settings, CreateOwned))
                    .RunWith(Sink.Seq<PutRecordsResultEntry>(), Sys.Materializer()));
                break;
            case "bytes":
                published = await Completed(source.Select(payload => ("key", ByteString.FromString(payload)))
                    .Via(KinesisFlow.ByPartitionAndBytes(_stream, settings, CreateOwned))
                    .RunWith(Sink.Seq<PutRecordsResultEntry>(), Sys.Materializer()));
                break;
            case "data":
                published = await Completed(source.Select(payload => ("key", new MemoryStream(Encoding.UTF8.GetBytes(payload))))
                    .Via(KinesisFlow.ByPartitionAndData(_stream, settings, CreateOwned))
                    .RunWith(Sink.Seq<PutRecordsResultEntry>(), Sys.Materializer()));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
        await AssertDisposedOnce();
        Assert.Equal(expected.Length, published.Count);
        var records = await NativeRead(expected.Length);
        Assert.Equal(expected, records.Select(record => Encoding.UTF8.GetString(record.Data.ToArray())));
        Assert.Equal(published.Select(record => record.SequenceNumber), records.Select(record => record.SequenceNumber));
        Assert.All(records, record => Assert.Equal("key", record.PartitionKey));
    }

    [Theory]
    [InlineData("trim", 0)]
    [InlineData("at", 0)]
    [InlineData("after", 1)]
    public async Task Source_reads_native_records_from_the_requested_position_and_disposes_its_client(string position, int skip)
    {
        var expected = new[] { "first", "café", "last" };
        var seeded = await Native.PutRecordsAsync(new PutRecordsRequest { StreamName = _stream, Records = expected.Select(Entry).ToList() });
        var iteratorType = position == "trim" ? ShardIteratorType.TRIM_HORIZON
            : position == "at" ? ShardIteratorType.AT_SEQUENCE_NUMBER : ShardIteratorType.AFTER_SEQUENCE_NUMBER;
        var settings = new ShardSettings(_stream, _shard, iteratorType, TimeSpan.FromMilliseconds(100), 1,
            position == "trim" ? null : seeded.Records[0].SequenceNumber);
        var records = await Completed(KinesisSource.Basic(settings, CreateOwned).Take(expected.Length - skip)
            .RunWith(Sink.Seq<Record>(), Sys.Materializer()));
        Assert.Equal(expected.Skip(skip), records.Select(record => Encoding.UTF8.GetString(record.Data.ToArray())));
        Assert.Equal(seeded.Records.Skip(skip).Select(record => record.SequenceNumber), records.Select(record => record.SequenceNumber));
        await AssertDisposedOnce();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_stream_fails_and_disposes_the_owned_client(bool publisher)
    {
        if (publisher)
        {
            // Characterize Floci's per-record InternalFailure for a missing stream,
            // rather than claiming AWS's request-level error shape was exercised.
            var control = await Native.PutRecordsAsync(new PutRecordsRequest
                { StreamName = _stream + "-missing", Records = new List<PutRecordsRequestEntry> { Entry("control") } });
            Assert.Equal(1, control.FailedRecordCount);
            var nativeError = Assert.Single(control.Records);
            Assert.Equal("InternalFailure", nativeError.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(nativeError.ErrorMessage));
            var error = await Assert.ThrowsAsync<PublishingRecordsException>(() => Completed(Source.Single(Entry("missing"))
                .Via(KinesisFlow.Create(_stream + "-missing", KinesisFlowSettings.Default.WithMaxRetries(0), CreateOwned))
                .RunWith(Sink.Seq<PutRecordsResultEntry>(), Sys.Materializer())));
            Assert.Equal(1, error.Attempt);
            var failed = Assert.Single(error.Errors);
            Assert.Equal(nativeError.ErrorCode, failed.Item2.ErrorCode);
            Assert.Equal(nativeError.ErrorMessage, failed.Item2.ErrorMessage);
            Assert.Equal("missing", Encoding.UTF8.GetString(failed.Item1.Data.ToArray()));
        }
        else
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Completed(KinesisSource.Basic(
                    ShardSettings.Create(_stream + "-missing", _shard), CreateOwned)
                .RunWith(Sink.Seq<Record>(), Sys.Materializer())));
        await AssertDisposedOnce();
    }

    [Fact]
    public async Task The_sink_publishes_native_records_and_disposes_its_client_after_completion()
    {
        Source.Single(Entry("sink-payload")).RunWith(KinesisSink.Create(_stream, client: CreateOwned), Sys.Materializer());
        await AssertDisposedOnce();
        var record = Assert.Single(await NativeRead(1));
        Assert.Equal("sink-payload", Encoding.UTF8.GetString(record.Data.ToArray()));
    }

    private async Task<List<Record>> NativeRead(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var iterator = (await Native.GetShardIteratorAsync(new GetShardIteratorRequest
            { StreamName = _stream, ShardId = _shard, ShardIteratorType = ShardIteratorType.TRIM_HORIZON }, deadline.Token)).ShardIterator;
        var records = new List<Record>();
        while (records.Count < count)
        {
            var response = await Native.GetRecordsAsync(new GetRecordsRequest { ShardIterator = iterator, Limit = 100 }, deadline.Token);
            records.AddRange(response.Records ?? new List<Record>());
            iterator = response.NextShardIterator;
            if (response.Records == null || response.Records.Count == 0)
                await Task.Delay(200, deadline.Token);
        }
        return records;
    }

    private async Task<T> Completed<T>(Task<T> task)
    {
        await AwaitAssertAsync(() => Assert.True(task.IsCompleted, "Kinesis stream did not terminate."), TimeSpan.FromSeconds(30));
        return await task;
    }
    private async Task AssertDisposedOnce()
    {
        await AwaitAssertAsync(() => Assert.Single(_owned));
        var client = Assert.Single(_owned);
        await AwaitAssertAsync(() => Assert.Equal(1, client.DisposeCount));
    }
    private IAmazonKinesis CreateOwned()
    {
        var client = new OwnedClient(_broker.GetConnectionString());
        _owned.Add(client);
        return client;
    }
    private static PutRecordsRequestEntry Entry(string payload) => new()
        { PartitionKey = "key", Data = new MemoryStream(Encoding.UTF8.GetBytes(payload)) };
    private static AmazonKinesisConfig Config(string url) => new()
        { ServiceURL = url, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(10) };
    private sealed class OwnedClient(string url) : AmazonKinesisClient(new BasicAWSCredentials("test", "test"), Config(url))
    {
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) Interlocked.Increment(ref _disposeCount);
        }
    }
}
