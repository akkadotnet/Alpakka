using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Util;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Only for internal implementations
    /// </summary>
    public abstract class AmqpConnectionProvider
    {
        public abstract IConnection Get();

        public virtual void Release(IConnection connection)
        {
            if(connection.IsOpen)
                connection.Close();
        }
    }

    /// <summary>
    /// Connects to a local AMQP broker at the default port with no password.
    /// </summary>
    public sealed class AmqpLocalConnectionProvider : AmqpConnectionProvider
    {
        private AmqpLocalConnectionProvider()
        {
            
        }
        public static AmqpLocalConnectionProvider Instance=> new AmqpLocalConnectionProvider();
        public override IConnection Get() => new ConnectionFactory().CreateConnection();
    }

    public sealed class AmqpUriConnectionProvider : AmqpConnectionProvider
    {
        private readonly Uri _uri;

        public static AmqpUriConnectionProvider Create(Uri uri)=> new AmqpUriConnectionProvider(uri);

        public static AmqpConnectionProvider Create(string uri)=> new AmqpUriConnectionProvider(new Uri(uri));

        private AmqpUriConnectionProvider(Uri uri)
        {
            _uri = uri;
        }
        public override IConnection Get()
        {
            var factory = new ConnectionFactory();
            factory.Uri = _uri;
            return factory.CreateConnection();
        }
    }

    public sealed class AmqpDetailsConnectionProvider : AmqpConnectionProvider
    {
        private AmqpDetailsConnectionProvider(IReadOnlyList<(string host, int port)> hostAndPortList,
            AmqpCredentials? credentials = null,
            string virtualHost = null,
            SslOption ssl = null,
            ushort? requestedHeartbeat = null,
            TimeSpan? connectionTimeout = null,
            TimeSpan? handshakeTimeout = null,
            TimeSpan? networkRecoveryInterval = null,
            bool? automaticRecoveryEnabled = null,
            bool? topologyRecoveryEnabled = null,
            string connectionName = null)
        {
            HostAndPortList = hostAndPortList;
            Credentials = credentials;
            VirtualHost = virtualHost;
            Ssl = ssl;
            RequestedHeartbeat = requestedHeartbeat;
            ConnectionTimeout = connectionTimeout;
            HandshakeTimeout = handshakeTimeout;
            NetworkRecoveryInterval = networkRecoveryInterval;
            AutomaticRecoveryEnabled = automaticRecoveryEnabled;
            TopologyRecoveryEnabled = topologyRecoveryEnabled;
            ConnectionName = connectionName;
        }

        public IReadOnlyList<(string host, int port)> HostAndPortList { get; }
        public AmqpCredentials? Credentials { get; }
        public string VirtualHost { get; }
        public SslOption Ssl { get; }
        public ushort? RequestedHeartbeat { get; }
        public TimeSpan? ConnectionTimeout { get; }
        public TimeSpan? HandshakeTimeout { get; }
        public TimeSpan? NetworkRecoveryInterval { get; }
        public bool? AutomaticRecoveryEnabled { get; }
        public bool? TopologyRecoveryEnabled { get; }
        public string ConnectionName { get; }

        public static AmqpDetailsConnectionProvider Create(string host, int port) =>
            new AmqpDetailsConnectionProvider(new List<(string host, int port)> { (host, port) });

        public AmqpDetailsConnectionProvider WithHostsAndPorts((string host, int port) hostAndPort,
           params (string host, int port)[] hostAndPortList)
        {
            return new AmqpDetailsConnectionProvider(new List<(string host, int port)>(hostAndPortList.ToList()) { hostAndPort },
                Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithCredentials(AmqpCredentials credentials)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithVirtualHost(string virtualHost)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, virtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithSsl(SslOption sslOption)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, sslOption, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithRequestedHeartbeat(ushort requestedHeartbeat)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, requestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithConnectionTimeout(TimeSpan connectionTimeout)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                connectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithHandshakeTimeout(TimeSpan handshakeTimeout)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, handshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithNetworkRecoveryInterval(TimeSpan networkRecoveryInterval)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, networkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithAutomaticRecoveryEnabled(bool automaticRecoveryEnabled)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, automaticRecoveryEnabled,
                TopologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithTopologyRecoveryEnabled(bool topologyRecoveryEnabled)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                topologyRecoveryEnabled, ConnectionName);
        }

        public AmqpDetailsConnectionProvider WithConnectionName(string connectionName)
        {
            return new AmqpDetailsConnectionProvider(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled, connectionName);
        }

        public override IConnection Get()
        {
            var factory = new ConnectionFactory();
            if (Credentials.HasValue)
            {
                factory.UserName = Credentials.Value.Username;
                factory.Password = Credentials.Value.Password;
            }
            if (!string.IsNullOrEmpty(VirtualHost))
                factory.VirtualHost = VirtualHost;
            if (Ssl != null)
                factory.Ssl = Ssl;
            if (AutomaticRecoveryEnabled.HasValue)
                factory.AutomaticRecoveryEnabled = AutomaticRecoveryEnabled.Value;
            if (RequestedHeartbeat.HasValue)
                factory.RequestedHeartbeat = RequestedHeartbeat.Value;
            if (NetworkRecoveryInterval.HasValue)
                factory.NetworkRecoveryInterval = NetworkRecoveryInterval.Value;
            if (TopologyRecoveryEnabled.HasValue)
                factory.TopologyRecoveryEnabled = TopologyRecoveryEnabled.Value;
            if (ConnectionTimeout.HasValue)
                factory.ContinuationTimeout = ConnectionTimeout.Value;
            if (HandshakeTimeout.HasValue)
                factory.HandshakeContinuationTimeout = HandshakeTimeout.Value;
            return factory.CreateConnection(
                new DefaultEndpointResolver(HostAndPortList.Select(hp => new AmqpTcpEndpoint(hp.host, hp.port))),
                ConnectionName ?? "");
        }

        public override string ToString()
        {
            return
                $"AmqpDetailsConnectionProvider(HostAndPortList=({HostAndPortList.Select(x => $"[{x.host}:{x.port}]").Aggregate((left, right) => $"{right}, {left}")}), Credentials={Credentials}, VirtualHost={VirtualHost})";
        }
    }

    public sealed class AmqpConnectionFactoryConnectionProvider : AmqpConnectionProvider
    {
        private readonly ConnectionFactory _factory;
        private readonly IReadOnlyList<(string host, int port)> _hostAndPortList;

        public static AmqpConnectionFactoryConnectionProvider Create(ConnectionFactory factory) =>
            new AmqpConnectionFactoryConnectionProvider(factory);

        private AmqpConnectionFactoryConnectionProvider(ConnectionFactory factory,
            IReadOnlyList<(string host, int port)> hostAndPortList = null)
        {
            _factory = factory;
            _hostAndPortList = hostAndPortList?? new List<(string host, int port)>();
        }

        public IReadOnlyList<(string host, int port)> HostAndPortList => _hostAndPortList.Any()
            ? _hostAndPortList.ToList()
            : new List<(string host, int port)> {(_factory.HostName, _factory.Port)};

        public AmqpConnectionFactoryConnectionProvider WithHostsAndPorts((string host, int port) hostAndPort,
            params (string host, int port)[] hostAndPortList)
        {
            return new AmqpConnectionFactoryConnectionProvider(_factory,
                new List<(string host, int port)>(hostAndPortList.ToList()) {hostAndPort});
        }

        public override IConnection Get()
        {
            return _factory.CreateConnection(HostAndPortList.Select(hp => new AmqpTcpEndpoint(hp.host, hp.port))
                .ToList());
        }
    }

    public sealed class AmqpCachedConnectionProvider : AmqpConnectionProvider
    {
        private AtomicReference<IState> _state = new AtomicReference<IState>(Empty.Instance);
        
        public AmqpConnectionProvider Provider { get; }
        public bool AutomaticRelease { get; }

        public static AmqpCachedConnectionProvider
            Create(AmqpConnectionProvider provider, bool automaticRelease = true) =>
            new AmqpCachedConnectionProvider(provider, automaticRelease);

        private AmqpCachedConnectionProvider(AmqpConnectionProvider provider, bool automaticRelease = true)
        {
            Provider = provider;
            AutomaticRelease = automaticRelease;
        }

        public override IConnection Get()
        {
            var state = _state.Value;
            switch (state)
            {
                case Empty x:
                {
                    if (_state.CompareAndSet(Empty.Instance, Connecting.Instance))
                    {
                        try
                        {
                            var connection = Provider.Get();
                            if (!_state.CompareAndSet(Connecting.Instance, new Connected(connection, 1)))
                                throw new InvalidOperationException(
                                    "Unexpected concurrent modification while creating the connection.");
                            return connection;
                        }
                        catch (InvalidOperationException)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            _state.CompareAndSet(Connecting.Instance, Empty.Instance);
                            throw;
                        }
                    }
                    return Get();
                }
                case Connecting x:
                {
                    return Get();
                }
                case Connected c:
                {
                    if (_state.CompareAndSet(c, new Connected(c.Connection, c.Clients + 1)))
                        return c.Connection;
                    return Get();
                }
                case Closing x:
                    return Get();
                default:
                    throw new Exception("invalid state!");
            }
        }

        public override void Release(IConnection connection)
        {
            var state = _state.Value;
            switch (state)
            {
                case Empty x:
                {
                    throw new InvalidOperationException("There is no connection to release.");
                }
                case Connecting x:
                    Release(connection);
                    break;
                case Connected c:
                {
                    if(!c.Connection.Equals(connection))
                            throw new InvalidOperationException("Can't release a connection that's not owned by this provider");
                    if (c.Clients == 1 || !AutomaticRelease)
                    {
                        if (_state.CompareAndSet(c, Closing.Instance))
                        {
                            Provider.Release(connection);
                            if (!_state.CompareAndSet(Closing.Instance, Empty.Instance))
                                throw new InvalidOperationException(
                                    "Unexpected concurrent modification while closing the connection.");
                        }
                        break;
                    }
                    else
                    {
                        if(!_state.CompareAndSet(c, new Connected(c.Connection, c.Clients - 1)))
                                Release(connection);
                        break;
                    }
                }
                case Closing x:
                {
                    Release(connection);
                        break;
                }
                default:
                    throw new Exception("invalid state!");
            }
        }


        private interface IState { }

        private sealed class Empty : IState
        {
            public static Empty Instance { get; } = new Empty();
        }

        private sealed class Connecting : IState
        {
            public static Connecting Instance { get; } = new Connecting();
        }

        private sealed class Connected : IState, IEquatable<Connected>
        {
            public IConnection Connection { get; }
            public int Clients { get; }

            public Connected(IConnection connection, int clients)
            {
                Connection = connection;
                Clients = clients;
            }

            public bool Equals(Connected other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Connection, other.Connection) && Clients == other.Clients;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Connected && Equals((Connected) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Connection != null ? Connection.GetHashCode() : 0) * 397) ^ Clients;
                }
            }

            public static bool operator ==(Connected left, Connected right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(Connected left, Connected right)
            {
                return !Equals(left, right);
            }
        }

        private sealed class Closing : IState
        {
            public static Closing Instance { get; } = new Closing();
        }

    }


}
