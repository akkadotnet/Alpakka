using Akka.Streams.Dsl;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using System.Net;
using System.Text.Json;
using Testcontainers.Floci;
using Xunit;
using Xunit.Abstractions;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

namespace Akka.Streams.Sns.IntegrationTests;

public sealed class SnsIntegrationSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output), IAsyncLifetime
{
    private readonly FlociContainer _broker = new FlociBuilder("floci/floci:1.5.13").Build();
    private AmazonSimpleNotificationServiceClient? _sns;
    private AmazonSQSClient? _sqs;
    private string _topicArn = null!;
    private string _queueUrl = null!;
    private string _subscriptionArn = null!;
    private AmazonSimpleNotificationServiceClient Sns => _sns ?? throw new InvalidOperationException("SNS fixture has not started.");
    private AmazonSQSClient Sqs => _sqs ?? throw new InvalidOperationException("SQS observer has not started.");

    public async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _broker.StartAsync(startup.Token);
            var credentials = new BasicAWSCredentials("test", "test");
            _sns = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = _broker.GetConnectionString(), AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(10)
            });
            _sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig
            {
                ServiceURL = _broker.GetConnectionString(), AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(10)
            });
            var suffix = Guid.NewGuid().ToString("N");
            _topicArn = (await Sns.CreateTopicAsync(new SnsModel.CreateTopicRequest { Name = "alpakka-" + suffix }, startup.Token)).TopicArn;
            _queueUrl = (await Sqs.CreateQueueAsync(new SqsModel.CreateQueueRequest { QueueName = "observer-" + suffix }, startup.Token)).QueueUrl;
            var attributes = await Sqs.GetQueueAttributesAsync(new SqsModel.GetQueueAttributesRequest
                { QueueUrl = _queueUrl, AttributeNames = ["QueueArn"] }, startup.Token);
            var queueArn = attributes.Attributes["QueueArn"];
            var policy = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[] { new
                {
                    Effect = "Allow", Principal = new { Service = "sns.amazonaws.com" }, Action = "sqs:SendMessage", Resource = queueArn,
                    Condition = new Dictionary<string, object> { ["ArnEquals"] = new Dictionary<string, string> { ["aws:SourceArn"] = _topicArn } }
                } }
            });
            await Sqs.SetQueueAttributesAsync(new SqsModel.SetQueueAttributesRequest
                { QueueUrl = _queueUrl, Attributes = new Dictionary<string, string> { ["Policy"] = policy } }, startup.Token);
            _subscriptionArn = (await Sns.SubscribeAsync(new SnsModel.SubscribeRequest
                { TopicArn = _topicArn, Protocol = "sqs", Endpoint = queueArn, ReturnSubscriptionArn = true }, startup.Token)).SubscriptionArn;
        }
        catch (Exception startupException)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupException)
            {
                throw new AggregateException("SNS startup and cleanup both failed.", startupException, cleanupException);
            }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            try { _sns?.Dispose(); }
            finally
            {
                try { _sqs?.Dispose(); }
                finally { await _broker.DisposeAsync(); }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plain_flow_delivers_exact_payloads_to_native_subscribers_and_keeps_the_borrowed_client_open(bool raw)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (raw)
            await Sns.SetSubscriptionAttributesAsync(new SnsModel.SetSubscriptionAttributesRequest
                { SubscriptionArn = _subscriptionArn, AttributeName = "RawMessageDelivery", AttributeValue = "true" }, deadline.Token);
        var expected = new[] { "first", "café", "last" };
        var published = await Source.From(expected).Via(SnsPublisher.PlainFlow(_topicArn, Sns))
            .RunWith(Sink.Seq<SnsModel.PublishResponse>(), Sys.Materializer()).WaitAsync(deadline.Token);
        Assert.Equal(expected.Length, published.Count);
        Assert.All(published, response => Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode));
        Assert.Equal(expected.Length, published.Select(response => response.MessageId).Distinct().Count());

        var received = await Receive(expected.Length, deadline.Token);
        var actual = new List<string>();
        var envelopeIds = new List<string>();
        foreach (var message in received)
        {
            if (raw)
                actual.Add(message.Body);
            else
            {
                using var envelope = JsonDocument.Parse(message.Body);
                Assert.Equal(_topicArn, envelope.RootElement.GetProperty("TopicArn").GetString());
                actual.Add(envelope.RootElement.GetProperty("Message").GetString()!);
                envelopeIds.Add(envelope.RootElement.GetProperty("MessageId").GetString()!);
            }
        }
        Assert.Equal(expected.OrderBy(payload => payload), actual.OrderBy(payload => payload));
        if (!raw)
            Assert.Equal(published.Select(response => response.MessageId).OrderBy(id => id), envelopeIds.OrderBy(id => id));
        await DeleteAndAssertEmpty(received, deadline.Token);
        await Sns.GetTopicAttributesAsync(new SnsModel.GetTopicAttributesRequest { TopicArn = _topicArn }, deadline.Token);
    }

    [Fact]
    public async Task Plain_sink_awaits_publication_and_preserves_the_borrowed_client()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Source.Single("sink-payload").RunWith(SnsPublisher.PlainSink(_topicArn, Sns), Sys.Materializer()).WaitAsync(deadline.Token);
        var received = Assert.Single(await Receive(1, deadline.Token));
        using var envelope = JsonDocument.Parse(received.Body);
        Assert.Equal("sink-payload", envelope.RootElement.GetProperty("Message").GetString());
        await DeleteAndAssertEmpty([received], deadline.Token);
        await Sns.GetTopicAttributesAsync(new SnsModel.GetTopicAttributesRequest { TopicArn = _topicArn }, deadline.Token);
    }

    [Fact]
    public async Task Missing_topic_fails_with_the_sdk_error_and_preserves_the_borrowed_client()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await Source.Single("missing-topic").RunWith(SnsPublisher.PlainSink(_topicArn + "-missing", Sns), Sys.Materializer()).WaitAsync(deadline.Token));
        Assert.IsAssignableFrom<AmazonSimpleNotificationServiceException>(error.GetBaseException());
        await Sns.GetTopicAttributesAsync(new SnsModel.GetTopicAttributesRequest { TopicArn = _topicArn }, deadline.Token);
    }

    private async Task<List<SqsModel.Message>> Receive(int count, CancellationToken token)
    {
        var messages = new List<SqsModel.Message>();
        while (messages.Count < count)
        {
            var response = await Sqs.ReceiveMessageAsync(new SqsModel.ReceiveMessageRequest
                { QueueUrl = _queueUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 1 }, token);
            messages.AddRange(response.Messages ?? []);
        }
        return messages;
    }

    private async Task DeleteAndAssertEmpty(IEnumerable<SqsModel.Message> messages, CancellationToken token)
    {
        foreach (var message in messages)
            await Sqs.DeleteMessageAsync(new SqsModel.DeleteMessageRequest
                { QueueUrl = _queueUrl, ReceiptHandle = message.ReceiptHandle }, token);
        var counts = await Sqs.GetQueueAttributesAsync(new SqsModel.GetQueueAttributesRequest
            { QueueUrl = _queueUrl, AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"] }, token);
        Assert.Equal("0", counts.Attributes["ApproximateNumberOfMessages"]);
        Assert.Equal("0", counts.Attributes["ApproximateNumberOfMessagesNotVisible"]);
    }
}
