#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Testcontainers.Floci;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SQS.Tests;

public sealed class SqsIntegrationTests(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output), IAsyncLifetime
{
    private readonly FlociContainer _broker = new FlociBuilder("floci/floci:1.5.13").Build();
    private AmazonSQSClient? _client;
    private string _queueUrl = null!;
    private AmazonSQSClient Client => _client ?? throw new InvalidOperationException("Fixture has not started.");

    public async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _broker.StartAsync(startup.Token);
            _client = new AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig
            {
                ServiceURL = _broker.GetConnectionString(), AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(10)
            });
            var queue = await Client.CreateQueueAsync(new CreateQueueRequest("alpakka-" + Guid.NewGuid().ToString("N"))
            {
                Attributes = new Dictionary<string, string> { ["VisibilityTimeout"] = "30" }
            }, startup.Token);
            _queueUrl = queue.QueueUrl;
        }
        catch (Exception startupException)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupException)
            {
                throw new AggregateException("SQS startup and cleanup both failed.", startupException, cleanupException);
            }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            var termination = Sys.Terminate();
            await AwaitAssertAsync(() => Assert.True(termination.IsCompleted,
                "ActorSystem did not terminate during SQS fixture cleanup."), TimeSpan.FromSeconds(15));
            await termination;
        }
        finally
        {
            try { _client?.Dispose(); }
            finally { await _broker.DisposeAsync(); }
        }
    }

    [Theory]
    [InlineData("single")]
    [InlineData("grouped")]
    [InlineData("batch")]
    public async Task Publish_results_match_the_native_messages_for_each_mode(string mode)
    {
        var expected = new[] { "first", "café", "last" };
        var requests = expected.Select(body => new SendMessageRequest(_queueUrl, body)).ToArray();
        var source = Source.From(requests);
        IReadOnlyList<(SendMessageRequest Request, string Id)> results;
        switch (mode)
        {
            case "single":
                results = await source.Via(SqsPublishFlow.Default(Client, _queueUrl))
                    .Select(result => (result.Request, result.Response.MessageId))
                    .RunWith(Sink.Seq<(SendMessageRequest, string)>(), Sys.Materializer());
                break;
            case "grouped":
                results = await source.Via(SqsPublishFlow.Grouped(Client, _queueUrl,
                        SqsPublishGroupedSettings.Default.WithMaxBatchSize(3)))
                    .Select(result => (result.Request, result.Result.MessageId))
                    .RunWith(Sink.Seq<(SendMessageRequest, string)>(), Sys.Materializer());
                break;
            case "batch":
                results = await Source.Single<IEnumerable<SendMessageRequest>>(requests)
                    .Via(SqsPublishFlow.Batch(Client, _queueUrl)).SelectMany(batch => batch)
                    .Select(result => (result.Request, result.Result.MessageId))
                    .RunWith(Sink.Seq<(SendMessageRequest, string)>(), Sys.Materializer());
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var received = new List<Message>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (received.Count < expected.Length)
        {
            var response = await Client.ReceiveMessageAsync(new ReceiveMessageRequest(_queueUrl)
                { MaxNumberOfMessages = 10, WaitTimeSeconds = 1 }, deadline.Token);
            received.AddRange(response.Messages);
        }
        Assert.Equal(expected.OrderBy(body => body), received.Select(message => message.Body).OrderBy(body => body));
        Assert.Equal(expected.Length, results.Count);
        Assert.Equal(expected.Length, results.Select(result => result.Id).Distinct().Count());
        foreach (var result in results)
            Assert.Equal(Assert.Single(received, message => message.Body == result.Request.MessageBody).MessageId, result.Id);

        await AssertStoredCounts(visible: 0, invisible: expected.Length);
        var deleted = await Source.From(received).Select(message => new Delete(message))
            .Via(SqsAckFlow.GroupedDelete(Client, _queueUrl))
            .RunWith(Sink.Seq<SqsDeleteResultEntry>(), Sys.Materializer());
        Assert.Equal(expected.Length, deleted.Count);
        await AssertStoredCounts(visible: 0, invisible: 0);
        var empty = await Client.ReceiveMessageAsync(new ReceiveMessageRequest(_queueUrl) { WaitTimeSeconds = 0 });
        Assert.Empty(empty.Messages);
        await Client.GetQueueAttributesAsync(_queueUrl, new List<string> { "QueueArn" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_and_acknowledgment_flows_preserve_attributes_visibility_and_deletion(bool grouped)
    {
        var sent = await Client.SendMessageAsync(new SendMessageRequest(_queueUrl, "native-payload")
        {
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["kind"] = new MessageAttributeValue { DataType = "String", StringValue = "native" }
            }
        });
        var received = await SqsSource.Create(Client, _queueUrl, SqsSourceSettings.Default
                .WithWaitTime(TimeSpan.Zero).WithMaxBatchSize(1).WithMaxBufferSize(1)
                .WithMessageAttributes(new MessageAttributeName("kind")))
            .Take(1).RunWith(Sink.Seq<Message>(), Sys.Materializer());
        var message = Assert.Single(received);
        Assert.Equal(sent.MessageId, message.MessageId);
        Assert.Equal("native-payload", message.Body);
        Assert.Equal("native", message.MessageAttributes["kind"].StringValue);
        await AssertStoredCounts(visible: 0, invisible: 1);
        Assert.Empty((await NativeReceive()).Messages);

        await Apply(MessageAction.Ignore(message));
        Assert.Empty((await NativeReceive()).Messages);
        await Apply(MessageAction.ChangeVisibility(message, TimeSpan.Zero));
        var redelivered = Assert.Single((await NativeReceive()).Messages);
        Assert.Equal(message.MessageId, redelivered.MessageId);
        Assert.Equal(message.Body, redelivered.Body);
        Assert.NotEqual(message.ReceiptHandle, redelivered.ReceiptHandle);
        await AssertStoredCounts(visible: 0, invisible: 1);
        await Apply(MessageAction.Delete(redelivered));
        await AssertStoredCounts(visible: 0, invisible: 0);
        Assert.Empty((await NativeReceive()).Messages);
        await Client.GetQueueAttributesAsync(_queueUrl, new List<string> { "QueueArn" });

        Task<ReceiveMessageResponse> NativeReceive() => Client.ReceiveMessageAsync(new ReceiveMessageRequest(_queueUrl)
            { MaxNumberOfMessages = 1, WaitTimeSeconds = 0 });

        Task<Done> Apply(MessageAction action) => grouped
            ? Source.Single(action).Via(SqsAckFlow.Grouped(Client, _queueUrl))
                .RunWith(Sink.Ignore<ISqsAckResultEntry>(), Sys.Materializer())
            : Source.Single(action).Via(SqsAckFlow.Default(Client, _queueUrl))
                .RunWith(Sink.Ignore<ISqsAckResult>(), Sys.Materializer());
    }

    [Fact]
    public async Task Close_on_empty_receive_completes_without_disposing_the_borrowed_client()
    {
        var result = await SqsSource.Create(Client, _queueUrl, SqsSourceSettings.Default
                .WithWaitTime(TimeSpan.Zero).WithCloseOnEmptyReceive(true))
            .RunWith(Sink.Seq<Message>(), Sys.Materializer());
        Assert.Empty(result);
        await Client.GetQueueAttributesAsync(_queueUrl, new List<string> { "QueueArn" });
    }

    [Fact]
    public async Task Missing_queue_fails_the_source_without_disposing_the_borrowed_client()
    {
        await Assert.ThrowsAsync<QueueDoesNotExistException>(async () =>
            await SqsSource.Create(Client, _queueUrl + "-missing", SqsSourceSettings.Default.WithWaitTime(TimeSpan.Zero))
                .RunWith(Sink.Seq<Message>(), Sys.Materializer()));
        await Client.GetQueueAttributesAsync(_queueUrl, new List<string> { "QueueArn" });
    }

    private async Task AssertStoredCounts(int visible, int invisible)
    {
        var counts = await Client.GetQueueAttributesAsync(_queueUrl, new List<string>
            { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" });
        Assert.Equal(visible.ToString(System.Globalization.CultureInfo.InvariantCulture), counts.Attributes["ApproximateNumberOfMessages"]);
        Assert.Equal(invisible.ToString(System.Globalization.CultureInfo.InvariantCulture), counts.Attributes["ApproximateNumberOfMessagesNotVisible"]);
    }
}
