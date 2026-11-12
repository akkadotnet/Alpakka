using System;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR;
using Akka.Streams.SignalR.Internals;
using Microsoft.Extensions.DependencyInjection;

internal static class Program
{
    private static void Main()
    {
        var defaults = ConnectionSourceSettings.Default;
        var changed = defaults.WithBufferSize(64).WithOverflowStrategy(OverflowStrategy.Fail);
        if (defaults.BufferCapacity != 128 || defaults.OverflowStrategy != OverflowStrategy.DropHead
            || changed.BufferCapacity != 64 || changed.OverflowStrategy != OverflowStrategy.Fail)
            throw new InvalidOperationException("Retained source settings changed.");
        var payload = new object();
        var send = (Send)Signals.Send("client", payload);
        var group = (Send)Signals.SendToGroup("workers", payload, new[] { "excluded" });
        var broadcast = (Broadcast)Signals.Broadcast(payload);
        if (send.ConnectionId != "client" || send.Group != null || !ReferenceEquals(send.Data, payload)
            || group.Group != "workers" || group.ExcludedConnectionIds[0] != "excluded"
            || broadcast.ExcludedConnectionIds.Count != 0 || !ReferenceEquals(broadcast.Data, payload))
            throw new InvalidOperationException("Retained routing payload/defaults changed.");
        var error = new InvalidOperationException("disconnect");
        if (!new Disconnected(null, null).StopCalled || new Disconnected(null, error).StopCalled
            || !ReferenceEquals(new Received(null, payload).Data, payload) || new Connected(null).Request != null)
            throw new InvalidOperationException("Retained event API changed.");
        var services = new ServiceCollection();
        if (!ReferenceEquals(services, services.AddSignalRAkkaStream()))
            throw new InvalidOperationException("Retained DI extension changed.");
        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IStreamDispatcher>();
        StreamHub<Connector> hub = new CompatibleHub(dispatcher);
        // Construct the retained graphs without materializing them or issuing network calls.
        var connector = new Connector();
        Source<ISignalREvent, NotUsed> source = connector.Source;
        Sink<ISignalRResult, NotUsed> sink = connector.Sink;
        if (source == null || sink == null || hub == null || !(dispatcher is DefaultStreamDispatcher)
            || ConnectionSinkSettings.Default == null)
            throw new InvalidOperationException("Retained hub/connector factories changed.");
        Console.WriteLine("Previously compiled SignalR consumer passed (settings, events, routing, DI and graph factories; no network).");
    }

    private sealed class Connector : StreamConnector
    {
        public Connector() : base(null) { }
    }

    private sealed class CompatibleHub : StreamHub<Connector>
    {
        public CompatibleHub(IStreamDispatcher dispatcher) : base(dispatcher) { }
    }
}
