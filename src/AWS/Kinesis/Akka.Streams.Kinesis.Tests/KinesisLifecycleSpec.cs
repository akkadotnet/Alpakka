using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Record = Amazon.Kinesis.Model.Record;

namespace Akka.Streams.Kinesis.Tests;

public sealed class KinesisLifecycleSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    [Fact]
    public void A_terminal_shard_response_drains_every_record_before_completion()
    {
        var client = SourceClient(new GetRecordsResponse
        {
            Records = new List<Record> { new() { SequenceNumber = "1" }, new() { SequenceNumber = "2" } },
            NextShardIterator = null
        });
        var probe = KinesisSource.Basic(ShardSettings.Create("stream", "shard"), () => client)
            .RunWith(this.SinkProbe<Record>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            Assert.Equal("1", probe.Request(1).ExpectNext().SequenceNumber);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            Assert.Equal("2", probe.Request(1).ExpectNext().SequenceNumber);
            probe.ExpectComplete();
        }
        finally { probe.Cancel(); }
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("error")]
    public async Task The_source_disposes_its_owned_client_once_on_every_termination(string termination)
    {
        var response = new GetRecordsResponse { Records = new List<Record>(), NextShardIterator = termination == "complete" ? null : "next" };
        var client = SourceClient(response);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("injected read error");
        client.GetRecordsAsync(Arg.Any<GetRecordsRequest>()).Returns(_ =>
        {
            started.TrySetResult(true);
            return termination == "error" ? Task.FromException<GetRecordsResponse>(error) : Task.FromResult(response);
        });
        var probe = KinesisSource.Basic(ShardSettings.Create("stream", "shard"), () => client)
            .RunWith(this.SinkProbe<Record>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            await AwaitAssertAsync(() => Assert.True(started.Task.IsCompleted));
            if (termination == "complete") probe.ExpectComplete();
            else if (termination == "error") Assert.Same(error, probe.ExpectError());
            else probe.Cancel();
            await AwaitAssertAsync(() => client.Received(1).Dispose());
        }
        finally { probe.Cancel(); }
    }

    [Fact]
    public async Task A_finite_publish_completes_after_its_last_pending_sdk_result()
    {
        var client = Substitute.For<IAmazonKinesis>();
        var pending = new TaskCompletionSource<PutRecordsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PutRecordsAsync(Arg.Any<PutRecordsRequest>()).Returns(pending.Task);
        var probe = Source.Single(Entry("last")).Via(KinesisFlow.Create("stream",
                KinesisFlowSettings.Default.WithMaxBatchSize(1), () => client))
            .RunWith(this.SinkProbe<PutRecordsResultEntry>(), Sys.Materializer());
        probe.EnsureSubscription().Request(1);
        try
        {
            await AwaitAssertAsync(() => Assert.Single(client.ReceivedCalls(), call => call.GetMethodInfo().Name == "PutRecordsAsync"));
            pending.SetResult(Success("3"));
            Assert.Equal("3", probe.ExpectNext().SequenceNumber);
            probe.ExpectComplete();
        }
        finally { probe.Cancel(); }
    }

    [Fact]
    public async Task Concurrent_failed_batches_retry_every_record()
    {
        var client = Substitute.For<IAmazonKinesis>();
        var firstAttempts = new ConcurrentDictionary<string, TaskCompletionSource<PutRecordsResponse>>();
        var attempts = new ConcurrentDictionary<string, int>();
        client.PutRecordsAsync(Arg.Any<PutRecordsRequest>()).Returns(call =>
        {
            var entry = Assert.Single(((PutRecordsRequest)call[0]).Records);
            var payload = Encoding.UTF8.GetString(entry.Data.ToArray());
            if (attempts.AddOrUpdate(payload, 1, (_, count) => count + 1) > 1)
                return Task.FromResult(Success(payload == "first" ? "1" : "2"));
            return firstAttempts.GetOrAdd(payload, _ => new TaskCompletionSource<PutRecordsResponse>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        });
        var settings = KinesisFlowSettings.Default.WithMaxBatchSize(1).WithParallelism(2)
            .WithPRetryInitialTimeout(TimeSpan.FromMilliseconds(100)).WithMaxRetries(1);
        var probe = Source.From(new[] { Entry("first"), Entry("second") })
            .Via(KinesisFlow.Create("stream", settings, () => client))
            .RunWith(this.SinkProbe<PutRecordsResultEntry>(), Sys.Materializer());
        probe.EnsureSubscription().Request(2);
        try
        {
            await AwaitAssertAsync(() => Assert.Equal(2, firstAttempts.Count));
            foreach (var pending in firstAttempts.Values)
                pending.SetResult(new PutRecordsResponse
                {
                    FailedRecordCount = 1,
                    Records = new List<PutRecordsResultEntry> { new() { ErrorCode = "ProvisionedThroughputExceededException" } }
                });
            var received = new[] { probe.ExpectNext().SequenceNumber, probe.ExpectNext().SequenceNumber };
            Assert.Equal(new[] { "1", "2" }, received.OrderBy(value => value));
            Assert.Equal(2, attempts["first"]);
            Assert.Equal(2, attempts["second"]);
            probe.ExpectComplete();
        }
        finally { probe.Cancel(); }
    }

    private static IAmazonKinesis SourceClient(GetRecordsResponse response)
    {
        var client = Substitute.For<IAmazonKinesis>();
        client.GetShardIteratorAsync(Arg.Any<GetShardIteratorRequest>()).Returns(Task.FromResult(new GetShardIteratorResponse { ShardIterator = "initial" }));
        client.GetRecordsAsync(Arg.Any<GetRecordsRequest>()).Returns(Task.FromResult(response));
        return client;
    }
    private static PutRecordsRequestEntry Entry(string payload) => new()
        { PartitionKey = "key", Data = new MemoryStream(Encoding.UTF8.GetBytes(payload)) };
    private static PutRecordsResponse Success(string sequence) => new()
        { FailedRecordCount = 0, Records = new List<PutRecordsResultEntry> { new() { SequenceNumber = sequence } } };
}
