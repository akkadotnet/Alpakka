## Akka.Streams.SignalR.AspNetCore

This project brings integration of Akka.Streams and SignalR ASP.NET Core API.

SignalR integration can be achieved by inheriting from `StreamHub` and `StreamConnector`. 
`StreamConnector` allows you to wire-up the Source and Sink to send/receive messages to/from server/client.
`StreamHub` is a special version of SignalR Hub, it simply passes incoming messages to the `StreamConnector`

SignalR for ASP.NET Core makes a new instance of the Hub on each incoming invocation. It also doesn't have the 
concept of a persistent connection unlike the previous version. As such, the design of this connector follows 
the same design decision. There is **one single stream instance for each Hub type** as defined by the subclass of 
StreamConnector, and many instances of StreamHub will receive calls that dispatches to StreamConnector. The 
dispatcher maintains the list of streams. Note that only one client-called method on the hub is dispatched, 
namely the "Send" method which takes a single 'message' argument from the client.


### Example

Example echo websocket connection. Basic workflow is to take stream of incoming SignalR events, filter only those 
send by client and broadcast them back to the SignalR sink.

```csharp
public class EchoStream : StreamConnector
{
    public EchoStream(IHubClients clients, ConnectionSourceSettings sourceSettings = null, ConnectionSinkSettings sinkSettings = null) 
        : base(clients, sourceSettings, sinkSettings)
    {
        this.Source
            .Collect(x => x as Received) // filter out lifecycle events
            .Select(x => Signals.Broadcast(x.Data))
            .To(this.Sink)
            .Run(system.Materializer(ActorMaterializerSettings.Create(system)
                .WithSupervisionStrategy(ex => Directive.Resume)));
    }
}

public class EchoHub : StreamHub<EchoStream>
{
    public EchoHub(IStreamDispatcher dispatcher)
        : base(dispatcher)
    { }
}

// Startup.cs
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSignalRAkkaStream(); // Makes IStreamDispatcher available
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseSignalR(routes => {
            routes.MapHub<EchoStreamHub>("/echo");
        });
    }
}
```
For actual example take a look at [sample project](./src/SignalRSample).

Some extra considerations you may need to take:

- Because there's only one stream per hub type, choose an appropriate supervision strategy so, for example, that if 
any processing stage throws an exception, the stream stays up and continues processing other client's messages
- SignalR has its own serialization mechanisms that this connector is agnostic to. While server-to-client messages 
will have all the necessary CLR type to carry out serializtion, client-to-server messages may not have all the 
information required for deserialization.

### StreamConnector.Source

`Source` is an Akka.Streams source for events comming from the client side. Source can contain both user-defined data and 
connection lifecycle events which maps to those available in SignalR:

- `Received(HubCallerContext request, object data)` for messages send by the client.
- `Connected(HubCallerContext request)` when new client connects.
- `Disconnected(HubCallerContext request, Exception ex)` when existing client disconnects.

Note that current version of SignalR no longer supports automatic reconnect. Downstream stages must be able to 
handle multiple connections from the same browser instance in the event of a disconnect.

### StreamConnector.Sink

`Sink` is an Akka.Streams sink for messages to be sent back to the client. Sink can accept following messages, which maps 
to a subset of those offered by SignalR:

- `Signals.Send(string connectionId, object data)` or `Signals.SendToGroup(string group, object data, IList<string> excluded)` 
to send to a specific connection, or entire group with excluded connection ids.
- `Signals.Broadcast(object data, string[] excluded = null)` used to send data to all corresponding connections.

Note the lack of a "send to caller" message since there's only 1 stream instance for potentially many different clients. The 
downstream stages of the Source is required to keep track of `request.ConnectionId` to construct a `Signals.Send` in order to 
reply to caller.

### Configuration

```hocon
akka.streams.signalr {
	source {
		# Since SignalR doesn't support reactive streams protocol, the only way 
		# we can deal with faster upstream is to buffer messages. In this case
		# buffer-capacity describes maximum allowed size of a buffer.
		buffer-capacity = 128

		# If a buffer will be overflown in result of incoming client events,
		# an overflow strategy will define, how to deal with this situation:
		#
		# - `drop-head` is default strategy. It means, that the oldest message
		#   will be dropped from the buffer.
		# - `drop-tail` means, that the newest message will be dropped.
		# - `drop-buffer` will clear whole buffer, allowing to fill it from scratch.
		# - `drop-new` will drop an incoming message, that didn't fit into buffer.
		# - `fail` will cause Source to throw `BufferOverflowException` and close 
		#	current stage.
		overflow-strategy = drop-head
	}

	sink {
	
	}
}
```