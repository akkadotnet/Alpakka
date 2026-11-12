using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR;
using Akka.Streams.SignalR.Internals;
using Microsoft.AspNetCore.SignalR;

namespace SignalRSample;

public class EchoHub(IStreamDispatcher dispatcher) : StreamHub<EchoStream>(dispatcher)
{
    public Task JoinGroup(string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);
    public Task SendToConnection(string connectionId, string data) => Send(new DirectMessage(connectionId, data));
    public Task SendToGroup(string group, string data, string[] excluded) => Send(new GroupMessage(group, data, excluded));
    public Task BroadcastExcept(string data, string[] excluded) => Send(new BroadcastMessage(data, excluded));
}

public class EchoStream : StreamConnector
{
    public EchoStream(IHubClients clients, ConnectionSourceSettings sourceSettings = null, ConnectionSinkSettings sinkSettings = null)
        : base(clients, sourceSettings, sinkSettings)
    {
        Source.Where(value => value is Received).Select(value => ((Received)value).Data)
            .Select(data => data switch
            {
                DirectMessage direct => Signals.Send(direct.ConnectionId, direct.Data),
                GroupMessage group => Signals.SendToGroup(group.Group, group.Data, group.Excluded),
                BroadcastMessage broadcast => Signals.Broadcast(broadcast.Data, broadcast.Excluded),
                _ => Signals.Broadcast(data)
            })
            .To(Sink).Run(App.Materializer);
    }
}

internal sealed record DirectMessage(string ConnectionId, string Data);
internal sealed record GroupMessage(string Group, string Data, string[] Excluded);
internal sealed record BroadcastMessage(string Data, string[] Excluded);
