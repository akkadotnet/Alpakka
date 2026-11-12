using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Azure.Messaging.ServiceBus;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.ServiceBus.Tests;

public sealed class ServiceBusSourceContractSpec : Akka.TestKit.Xunit2.TestKit
{
    public ServiceBusSourceContractSpec(ITestOutputHelper output) : base(output: output)
    {
    }

    [Fact]
    public async Task Raw_source_only_receives_on_demand_and_leaves_settlement_to_the_caller()
    {
        var receiver = new ControlledReceiver();
        var running = ServiceBusSource.Create(receiver)
            .WatchTermination(Keep.Right)
            .ToMaterialized(this.SinkProbe<ServiceBusReceivedMessage>(), Keep.Both)
            .Run(Sys.Materializer());
        var probe = running.Item2;
        probe.EnsureSubscription();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, receiver.ReceiveCalls);

        probe.Request(1).ExpectNext(receiver.Message);
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, receiver.ReceiveCalls);
        Assert.Equal(0, receiver.CompleteCalls);

        probe.Cancel();
        await running.Item1.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, receiver.DisposeCalls);
    }

    [Fact]
    public async Task Canceling_during_settlement_stops_the_stage_without_disposing_the_receiver()
    {
        var receiver = new ControlledReceiver();
        var running = ServiceBusSource.Create<Payload>(receiver)
            .WatchTermination(Keep.Right)
            .ToMaterialized(this.SinkProbe<Payload>(), Keep.Both)
            .Run(Sys.Materializer());

        running.Item2.Request(1);
        await receiver.Completing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        running.Item2.Cancel();
        await running.Item1.WaitAsync(TimeSpan.FromSeconds(3));

        // The caller still owns the in-flight SDK operation after stream cancellation.
        receiver.Completion.SetResult(true);
        Assert.Equal(1, receiver.ReceiveCalls);
        Assert.Equal(1, receiver.CompleteCalls);
        Assert.Equal(0, receiver.DisposeCalls);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("canceled")]
    public async Task Typed_source_observes_settlement_before_emitting(string outcome)
    {
        var receiver = new ControlledReceiver();
        var probe = ServiceBusSource.Create<Payload>(receiver)
            .RunWith(this.SinkProbe<Payload>(), Sys.Materializer());
        probe.EnsureSubscription();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, receiver.ReceiveCalls);

        probe.Request(1);
        await receiver.Completing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, receiver.CompleteCalls);
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        if (outcome == "failed")
        {
            var failure = new InvalidOperationException("settlement failed");
            receiver.Completion.SetException(failure);
            Assert.Same(failure, probe.ExpectError());
        }
        else if (outcome == "canceled")
        {
            receiver.Completion.SetCanceled();
            Assert.IsAssignableFrom<OperationCanceledException>(probe.ExpectError());
        }
        else
        {
            receiver.Completion.SetResult(true);
            Assert.Equal("payload", probe.ExpectNext().Value);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            Assert.Equal(1, receiver.ReceiveCalls);
            Assert.Equal(1, receiver.CompleteCalls);
            probe.Cancel();
        }

        Assert.Equal(0, receiver.DisposeCalls);
    }

    [Theory]
    [InlineData(Directive.Stop)]
    [InlineData(Directive.Resume)]
    [InlineData(Directive.Restart)]
    public void Typed_source_keeps_deserialization_errors_observable(Directive directive)
    {
        var receiver = new ControlledReceiver(BinaryData.FromString("not JSON"));
        var probe = ServiceBusSource.Create<Payload>(receiver)
            .WithAttributes(ActorAttributes.CreateSupervisionStrategy(_ => directive))
            .RunWith(this.SinkProbe<Payload>(), Sys.Materializer());

        probe.Request(1);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(probe.ExpectError());
        Assert.Equal(0, receiver.CompleteCalls);
        Assert.Equal(0, receiver.DisposeCalls);
    }

    public sealed record Payload(string Value);

    private sealed class ControlledReceiver : ServiceBusReceiver
    {
        private int _receiveCalls;
        private int _completeCalls;
        private int _disposeCalls;

        public int ReceiveCalls => Volatile.Read(ref _receiveCalls);
        public int CompleteCalls => Volatile.Read(ref _completeCalls);
        public int DisposeCalls => Volatile.Read(ref _disposeCalls);
        public ServiceBusReceivedMessage Message { get; }
        private ServiceBusReceivedMessage NextMessage { get; } = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromObjectAsJson(new Payload("second")));
        public TaskCompletionSource<bool> Completing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledReceiver(BinaryData? body = null)
        {
            Message = ServiceBusModelFactory.ServiceBusReceivedMessage(body ?? BinaryData.FromObjectAsJson(new Payload("payload")));
        }

        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
            int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ServiceBusReceivedMessage> messages = Interlocked.Increment(ref _receiveCalls) == 1
                ? [Message, NextMessage]
                : [];
            return Task.FromResult(messages);
        }

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Assert.Same(Message, message);
            Interlocked.Increment(ref _completeCalls);
            Completing.TrySetResult(true);
            return Completion.Task;
        }

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCalls);
            return ValueTask.CompletedTask;
        }
    }
}
