using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SignalR.Tests;

public sealed class SignalRSinkContractSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    [Theory]
    [InlineData("connection")]
    [InlineData("group")]
    [InlineData("broadcast")]
    public async Task A_pending_send_does_not_block_upstream_failure(string target)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (clients, proxy) = Clients();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(true); return release.Task; });
        var (source, termination) = this.SourceProbe<ISignalRResult>().WatchTermination(Keep.Both)
            .ToMaterialized(new Connector(clients).Sink, Keep.Left).Run(Sys.Materializer());
        try
        {
            source.SendNext(Command(target, "pending"));
            await AwaitAssertAsync(() => Assert.True(started.Task.IsCompleted));
            var failure = new InvalidOperationException("upstream failed while send was pending");
            source.SendError(failure);
            await AwaitAssertAsync(() => Assert.True(termination.IsCompleted));
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await termination));
        }
        finally { release.TrySetResult(true); }
    }

    [Theory]
    [InlineData("connection", false)]
    [InlineData("connection", true)]
    [InlineData("group", false)]
    [InlineData("group", true)]
    [InlineData("broadcast", false)]
    [InlineData("broadcast", true)]
    public async Task Failed_and_canceled_transport_tasks_fail_the_open_stream(string target, bool cancel)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (clients, proxy) = Clients();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(true); return release.Task; });
        var (source, termination) = this.SourceProbe<ISignalRResult>().WatchTermination(Keep.Both)
            .ToMaterialized(new Connector(clients).Sink, Keep.Left).Run(Sys.Materializer());
        try
        {
            source.SendNext(Command(target, "payload"));
            await AwaitAssertAsync(() => Assert.True(started.Task.IsCompleted));
            var failure = new InvalidOperationException("transport failed");
            if (cancel) release.SetCanceled(); else release.SetException(failure);
            await AwaitAssertAsync(() => Assert.True(termination.IsCompleted));
            if (cancel) await Assert.ThrowsAsync<TaskCanceledException>(async () => await termination);
            else
            {
                var error = await Assert.ThrowsAsync<AggregateException>(async () => await termination);
                Assert.Same(failure, Assert.Single(error.InnerExceptions));
            }
            source.ExpectCancellation();
        }
        finally { release.TrySetResult(true); }
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("group")]
    [InlineData("broadcast")]
    public async Task Sends_keep_their_target_payload_and_sequential_order(string target)
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = CreateTestProbe();
        var (clients, proxy) = Clients();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.Equal(nameof(IClientSink.Receive), call.ArgAt<string>(0));
            var payload = Assert.IsType<string>(Assert.Single(call.ArgAt<object[]>(1)));
            calls.Ref.Tell(payload);
            return payload == "first" ? release.Task : Task.CompletedTask;
        });
        Source.From(new[] { Command(target, "first"), Command(target, "second") })
            .RunWith(new Connector(clients).Sink, Sys.Materializer());
        try
        {
            calls.ExpectMsg("first");
            calls.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            release.SetResult(true);
            calls.ExpectMsg("second");
            if (target == "connection") ((IHubClients<IClientProxy>)clients).Received(2).Client("connection-1");
            else if (target == "group") clients.Received(2).GroupExcept("workers", Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "excluded" })));
            else clients.Received(2).AllExcept(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "excluded" })));
        }
        finally { release.TrySetResult(true); }
    }

    private static (IHubClients Clients, IClientProxy Proxy) Clients()
    {
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        var routing = (IHubClients<IClientProxy>)clients;
        routing.Client(Arg.Any<string>()).Returns(proxy);
        routing.GroupExcept(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(proxy);
        routing.AllExcept(Arg.Any<IReadOnlyList<string>>()).Returns(proxy);
        return (clients, proxy);
    }

    private static ISignalRResult Command(string target, string payload) => target switch
    {
        "connection" => Signals.Send("connection-1", payload),
        "group" => Signals.SendToGroup("workers", payload, new[] { "excluded" }),
        "broadcast" => Signals.Broadcast(payload, new[] { "excluded" }),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    private sealed class Connector(IHubClients clients) : StreamConnector(clients);
}
