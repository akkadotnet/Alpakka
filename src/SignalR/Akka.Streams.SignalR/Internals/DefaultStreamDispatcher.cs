using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.Streams.SignalR.Internals
{
    public sealed class DefaultStreamDispatcher : IStreamDispatcher
    {
        private readonly IServiceProvider _provider;
        private readonly ConcurrentDictionary<Type, StreamConnector> _connectors
            = new ConcurrentDictionary<Type, StreamConnector>();

        private readonly ConnectionSourceSettings _sourceSettings;
        private readonly ConnectionSinkSettings _sinkSettings;

        public DefaultStreamDispatcher(
            IServiceProvider provider,
            ConnectionSourceSettings sourceSettings = null,
            ConnectionSinkSettings sinkSettings = null)
        {
            _provider = provider;
            _sourceSettings = sourceSettings;
            _sinkSettings = sinkSettings;
        }

        public void Send<TStream>(ISignalREvent message, Type hubType)
            where TStream : StreamConnector
        {
            GetStream<TStream>(hubType).OnEvents(message);
        }

        private StreamConnector GetStream<TStream>(Type hubType)
            where TStream : StreamConnector
        {
            return _connectors.GetOrAdd(typeof(TStream), _ => {

                var hubContextType = typeof(IHubContext<>).MakeGenericType(hubType);
                var hubContext = _provider.GetService(hubContextType);
                var hubClients = (IHubClients)hubContextType.GetProperty(nameof(IHubContext<Hub>.Clients)).GetValue(hubContext);

                var stream = ActivatorUtilities.CreateInstance<TStream>(_provider,
                    hubClients,
                    _sourceSettings ?? new ConnectionSourceSettings(100, OverflowStrategy.DropTail),
                    _sinkSettings ?? ConnectionSinkSettings.Default);

                return stream;
            });
        }
    }

}
