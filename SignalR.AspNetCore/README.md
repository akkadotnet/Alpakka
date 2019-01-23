## Akka.Streams.SignalR.AspNetCore

This project brings integration of Akka.Streams and SignalR ASP.NET Core API.

SignalR integration can be achieved by inheriting from `StreamHub` and `StreamConnector`. 
`StreamConnector` allows you to wire-up the Source and Sink to send/receive messages to/from server/client.
`StreamHub` is a special version of SignalR Hub, it simply passes incoming messages to the `StreamConnector`

SignalR makes a new instance of the Hub on each incoming invocation, nor does it have the concept of a 
persistent connection, unlike the previous version. As such, the design of this connector follows the same 
design decision. There is one single stream instance for each Hub type.

### Example

Example echo websocket connection. Basic workflow is to take stream of incoming SignalR events, filter only those 
send by client and broadcast them back to the SignalR sink.  You will want to consider choosing a 
suitable supervision strategy so that if any processing stage throws an exception, it will continue to process 
other messages.

```csharp
public class EchoStreamHub : StreamConnector
{
    public EchoStreamHub()
    {
        this.Source
            .Collect(x => x as Received) // filter out lifecycle events
            .Select(x => Signals.Broadcast(x.Data))
            .To(this.Sink)
            .Run(system.Materializer(ActorMaterializerSettings.Create(system)
                .WithSupervisionStrategy(ex => Directive.Resume)));
    }
}

// Startup.cs
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSignalRAkkaStream(); // Makes the dispatcher available
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

### StreamHub.Connect - source

`Source` is an Akka.Streams source for events comming from the client side. Source can contain both user-defined data and connection lifecycle events:

- `Received(HubCallerContext request, object data)` for messages send by the client.
- `Connected(HubCallerContext request)` when new client connects.
- `Disconnected(HubCallerContext request, Exception ex)` when existing client disconnects.

### StreamHub.Connect - sink

`Sink` is an Akka.Streams sink for messages send back to the client. Sink can accept following messages:

- `Signals.Send(string connectionId, object data)` or `Signals.SendToGroup(string group, object data, IList<string> excluded)` to send to a specific connection, or entire group with excluded connection ids.
- `Signals.Broadcast(object data, string[] excluded = null)` used to send data to all corresponding connections.

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