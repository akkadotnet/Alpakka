## Akka.Streams.SignalR

This project brings integration of Akka.Streams and SignalR API.

SignalR integration can be achieved by inheriting from `StreamConnection`. This is a special version of SignalR PersistentConnection, which exposes two additional properties: Source and Sink.

### Example

Example echo websocket connection. Basic workflow is to take stream of incoming SignalR events, filter only those send by client and broadcast them back to the SignalR sink.

```csharp
public class EchoConnection : StreamConnection
{
    public EchoConnection()
    {
        this.Source
            .Collect(x => x as Received) // filter out lifecycle events
            .Select(x => Signals.Broadcast(x.Data))
            .To(this.Sink)
            .Run(App.Materializer);
    }
}

// Startup.cs: use connection
[assembly: OwinStartup(typeof(Startup))]
public class Startup
{
    public void Configuration(IAppBuilder app)
    {
        app.MapSignalR<EchoConnection>("/echo");
    }
}
```

For actual example take a look at [sample project](https://github.com/akkadotnet/Alpakka/tree/dev/src/SignalIR/Examples/Akka.Streams.SignalIR.Examples).

### StreamConnection.Source

`Source` is an Akka.Streams source for events comming from the client side. Source can contain both user-defined data and connection lifecycle events:

- `Received(IRequest request, string connectionId, string data)` for messages send by the client.
- `Connected(IRequest request, string connectionId)` when new client connects.
- `Disconnected(IRequest request, string connectionId, bool stopCalled)` when existing client disconnects.
- `Reconnected(IRequest request, string connectionId)` when disconnected client has reconnected again.

### StreamConnection.Sink

`Sink` is an Akka.Streams sink for messages send back to the client. Sink can accept following messages:

- `Signals.Send(string signal, object data)` or `Signals.Send(IList<string> signals, object data, IList<string> excluded = null)` mapped to Connection.Send method.
- `Signals.Broadcast(object data, string[] excluded = null)` mapped to Connection.Broadcast method, used to send data to all corresponding connections.

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