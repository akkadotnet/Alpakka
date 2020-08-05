using System;
using System.Collections.Generic;
using System.Linq;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public interface IAmqpConnectorSettings
    {
        IAmqpConnectionSettings ConnectionSettings { get; }
        IReadOnlyList<IDeclaration> Declarations { get; }
    }

    public interface IAmqpSourceSettings : IAmqpConnectorSettings
    {
    }

    public sealed class NamedQueueSourceSettings : IAmqpSourceSettings
    {
        private NamedQueueSourceSettings(
            IAmqpConnectionSettings connectionSettings, 
            string queue,
            IReadOnlyList<IDeclaration> declarations = null, 
            bool noLocal = false, 
            bool exclusive = false,
            bool ackRequired = true,
            string consumerTag = null,
            IReadOnlyDictionary<string, object> arguments = null)
        {
            ConnectionSettings = connectionSettings;
            Queue = queue;
            Declarations = declarations ?? new List<IDeclaration>();
            NoLocal = noLocal;
            Exclusive = exclusive;
            AckRequired = ackRequired;
            ConsumerTag = consumerTag ?? "default";
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public string Queue { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }
        public bool NoLocal { get; }
        public bool Exclusive { get; }
        public bool AckRequired { get; }
        public string ConsumerTag { get; }
        public IReadOnlyDictionary<string, object> Arguments { get; }

        public static NamedQueueSourceSettings Create(IAmqpConnectionSettings connectionSettings, string queue)
        {
            return new NamedQueueSourceSettings(connectionSettings, queue);
        }

        public NamedQueueSourceSettings WithDeclarations(params IDeclaration[] declarations)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, declarations, NoLocal, Exclusive, AckRequired, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithNoLocal(bool noLocal)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, noLocal, Exclusive, AckRequired, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithExclusive(bool exclusive)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, exclusive, AckRequired, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithAckRequired(bool ackRequired)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, ackRequired, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithConsumerTag(string consumerTag)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, AckRequired, consumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, AckRequired, ConsumerTag,
                arguments.ToDictionary(key => key.Key, val => val.Value));
        }

        public NamedQueueSourceSettings WithArguments(params (string paramName, object paramValue)[] arguments)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, AckRequired, ConsumerTag,
                arguments.ToDictionary(key => key.paramName, val => val.paramValue));
        }

        public NamedQueueSourceSettings WithArguments(string key, object value)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, AckRequired, ConsumerTag,
                new Dictionary<string, object> {{key, value}});
        }

        public override string ToString() => 
            $"NamedQueueSourceSettings(ConnectionSettings={ConnectionSettings}, Queue={Queue}, Declarations={Declarations.Count}, NoLocal={NoLocal}, Exclusive={Exclusive}, Arguments={Arguments.Count})";
    }

    public sealed class TemporaryQueueSourceSettings : IAmqpSourceSettings
    {
        private TemporaryQueueSourceSettings(IAmqpConnectionSettings connectionSettings, string exchange,
            IReadOnlyList<IDeclaration> declarations = null, string routingKey = null)
        {
            ConnectionSettings = connectionSettings;
            Exchange = exchange;
            Declarations = declarations ?? new List<IDeclaration>();
            RoutingKey = routingKey;
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public string Exchange { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }
        public string RoutingKey { get; }

        public static TemporaryQueueSourceSettings Create(IAmqpConnectionSettings connectionSettings, string exchange) => 
            new TemporaryQueueSourceSettings(connectionSettings, exchange);

        public TemporaryQueueSourceSettings WithRoutingKey(string routingKey) => 
            new TemporaryQueueSourceSettings(ConnectionSettings, Exchange, Declarations, routingKey);

        public TemporaryQueueSourceSettings WithDeclarations(params IDeclaration[] declarations) => 
            new TemporaryQueueSourceSettings(ConnectionSettings, Exchange, declarations, RoutingKey);

        public override string ToString() => 
            $"TemporaryQueueSourceSettings(ConnectionSettings={ConnectionSettings},Exchange={Exchange}, Declarations={Declarations.Count}, RoutingKey={RoutingKey})";
    }

    public sealed class AmqpReplyToSinkSettings : IAmqpConnectorSettings
    {
        private AmqpReplyToSinkSettings(IAmqpConnectionSettings connectionSettings, bool failIfReplyToMissing = true)
        {
            ConnectionSettings = connectionSettings;
            FailIfReplyToMissing = failIfReplyToMissing;
            Declarations = new List<IDeclaration>();
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public bool FailIfReplyToMissing { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }

        public static AmqpReplyToSinkSettings Create(IAmqpConnectionSettings connectionSettings, bool failIfReplyToMissing = true)
        {
            return new AmqpReplyToSinkSettings(connectionSettings, failIfReplyToMissing);
        }
    }

    public sealed class AmqpSinkSettings : IAmqpConnectorSettings
    {
        private AmqpSinkSettings(IAmqpConnectionSettings connectionSettings, string exchange = null,
            string routingKey = null, IReadOnlyList<IDeclaration> declarations = null)
        {
            ConnectionSettings = connectionSettings;
            Exchange = exchange;
            RoutingKey = routingKey;
            Declarations = declarations ?? new List<IDeclaration>();
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public string Exchange { get; }
        public string RoutingKey { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }

        public static AmqpSinkSettings Create(IAmqpConnectionSettings connectionSettings = null) => 
            new AmqpSinkSettings(connectionSettings ?? DefaultAmqpConnection.Instance);

        public AmqpSinkSettings WithExchange(string exchange) => new AmqpSinkSettings(ConnectionSettings, exchange, RoutingKey, Declarations);

        public AmqpSinkSettings WithRoutingKey(string routingKey) => new AmqpSinkSettings(ConnectionSettings, Exchange, routingKey, Declarations);

        public AmqpSinkSettings WithDeclarations(params IDeclaration[] declarations) => 
            new AmqpSinkSettings(ConnectionSettings, Exchange, RoutingKey, declarations);

        public override string ToString() => 
            $"AmqpSinkSettings(ConnectionSettings={ConnectionSettings}, Exchange={Exchange}, RoutingKey={RoutingKey}, Delcarations={Declarations.Count})";
    }

    /// <summary>
    /// Only for internal implementations
    /// </summary>
    public interface IAmqpConnectionSettings
    {
    }

    /// <summary>
    /// Connects to a local AMQP broker at the default port with no password.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    public class DefaultAmqpConnection : IAmqpConnectionSettings
    {
        public static IAmqpConnectionSettings Instance => new DefaultAmqpConnection();
    }

    public sealed class AmqpConnectionUri : IAmqpConnectionSettings
    {
        private AmqpConnectionUri(Uri uri)
        {
            Uri = uri;
        }

        public Uri Uri { get; }

        public static AmqpConnectionUri Create(string uri) => new AmqpConnectionUri(new Uri(uri));
        public static AmqpConnectionUri Create(Uri uri) => new AmqpConnectionUri(uri);

        public override string ToString() => $"AmqpConnectionUri(Uri={Uri})";
    }

    public sealed class AmqpConnectionDetails : IAmqpConnectionSettings
    {
        private AmqpConnectionDetails(IReadOnlyList<(string host, int port)> hostAndPortList,
            AmqpCredentials? credentials = null,
            string virtualHost = null,
            SslOption ssl = null,
            ushort? requestedHeartbeat = null,
            TimeSpan? connectionTimeout = null,
            TimeSpan? handshakeTimeout = null,
            TimeSpan? networkRecoveryInterval = null,
            bool? automaticRecoveryEnabled = null,
            bool? topologyRecoveryEnabled = null)
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

        public static AmqpConnectionDetails Create(string host, int port) => 
            new AmqpConnectionDetails(new List<(string host, int port)> {(host, port)});

        public AmqpConnectionDetails WithHostsAndPorts((string host, int port) hostAndPort,
            params (string host, int port)[] hostAndPortList)
        {
            return new AmqpConnectionDetails(new List<(string host, int port)>(hostAndPortList.ToList()) {hostAndPort},
                Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithCredentials(AmqpCredentials credentials)
        {
            return new AmqpConnectionDetails(HostAndPortList, credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithVirtualHost(string virtualHost)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, virtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithSsl(SslOption sslOption)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, sslOption, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithRequestedHeartbeat(ushort requestedHeartbeat)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, Ssl, requestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithConnectionTimeout(TimeSpan connectionTimeout)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                connectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithHandshakeTimeout(TimeSpan handshakeTimeout)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, handshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithNetworkRecoveryInterval(TimeSpan networkRecoveryInterval)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, networkRecoveryInterval, AutomaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithAutomaticRecoveryEnabled(bool automaticRecoveryEnabled)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, automaticRecoveryEnabled,
                TopologyRecoveryEnabled);
        }

        public AmqpConnectionDetails WithTopologyRecoveryEnabled(bool topologyRecoveryEnabled)
        {
            return new AmqpConnectionDetails(HostAndPortList, Credentials, VirtualHost, Ssl, RequestedHeartbeat,
                ConnectionTimeout, HandshakeTimeout, NetworkRecoveryInterval, AutomaticRecoveryEnabled,
                topologyRecoveryEnabled);
        }


        public override string ToString()
        {
            return
                $"AmqpConnectionDetails(HostAndPortList=({HostAndPortList.Select(x => $"[{x.host}:{x.port}]").Aggregate((left, right) => $"{right}, {left}")}), Credentials={Credentials}, VirtualHost={VirtualHost})";
        }
    }

    public struct AmqpCredentials : IEquatable<AmqpCredentials>
    {
        public string Username { get; }
        public string Password { get; }

        private AmqpCredentials(string username, string password)
        {
            Username = username;
            Password = password;
        }

        public static AmqpCredentials Create(string username, string password) => new AmqpCredentials(username, password);

        public override string ToString() => $"AmqpCredentials(Username={Username}, Password=********)";

        public override bool Equals(object obj) => obj is AmqpCredentials credentials && Equals(credentials);

        public bool Equals(AmqpCredentials other) => Username == other.Username && Password == other.Password;

        public override int GetHashCode()
        {
            var hashCode = 568732665;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Username);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Password);
            return hashCode;
        }

        public static bool operator ==(AmqpCredentials credentials1, AmqpCredentials credentials2)
        {
            return credentials1.Equals(credentials2);
        }

        public static bool operator !=(AmqpCredentials credentials1, AmqpCredentials credentials2)
        {
            return !(credentials1 == credentials2);
        }
    }

    public interface IDeclaration
    {
    }

    public sealed class QueueDeclaration : IDeclaration
    {
        private QueueDeclaration(string name, bool durable = false, bool exclusive = false, bool autoDelete = false,
            IReadOnlyDictionary<string, object> arguments = null)
        {
            Name = name;
            Durable = durable;
            Exclusive = exclusive;
            AutoDelete = autoDelete;
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public string Name { get; }

        public bool Durable { get; }

        public bool Exclusive { get; }

        public bool AutoDelete { get; }

        public IReadOnlyDictionary<string, object> Arguments { get; }

        public static QueueDeclaration Create(string name) => new QueueDeclaration(name);

        public QueueDeclaration WithDurable(bool durable) => new QueueDeclaration(Name, durable, Exclusive, AutoDelete, Arguments);

        public QueueDeclaration WithExclusive(bool exclusive) => new QueueDeclaration(Name, Durable, exclusive, AutoDelete, Arguments);

        public QueueDeclaration WithAutoDelete(bool autoDelete) => new QueueDeclaration(Name, Durable, Exclusive, autoDelete, Arguments);

        public QueueDeclaration WithArguments(string key, object value)
        {
            return new QueueDeclaration(Name, Durable, Exclusive, AutoDelete,
                new Dictionary<string, object> {{key, value}});
        }

        public QueueDeclaration WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            return new QueueDeclaration(Name, Durable, Exclusive, AutoDelete,
                arguments.ToDictionary(k => k.Key, val => val.Value));
        }

        public override string ToString() => 
            $"QueueDeclaration(Name={Name}, Durable={Durable}, Exclusive={Exclusive}, AutoDelete={AutoDelete}, Arguments={Arguments.Count})";
    }

    public sealed class BindingDeclaration : IDeclaration
    {
        private BindingDeclaration(string queue, string exchange, string routingKey = null,
            IReadOnlyDictionary<string, object> arguments = null)
        {
            Queue = queue;
            Exchange = exchange;
            RoutingKey = routingKey;
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public string Queue { get; }

        public string Exchange { get; }

        public string RoutingKey { get; }

        public IReadOnlyDictionary<string, object> Arguments { get; }

        public static BindingDeclaration Create(string queue, string exchange) => new BindingDeclaration(queue, exchange);

        public BindingDeclaration WithRoutingKey(string routingKey) => new BindingDeclaration(Queue, Exchange, routingKey, Arguments);

        public BindingDeclaration WithArguments(string key, object value) => 
            new BindingDeclaration(Queue, Exchange, RoutingKey, new Dictionary<string, object> {{key, value}});

        public BindingDeclaration WithArguments(KeyValuePair<string, object> argument) => 
            new BindingDeclaration(Queue, Exchange, RoutingKey, new Dictionary<string, object> {{argument.Key, argument.Value}});

        public BindingDeclaration WithArguments(params KeyValuePair<string, object>[] arguments) => 
            new BindingDeclaration(Queue, Exchange, RoutingKey, arguments.ToDictionary(k => k.Key, val => val.Value));

        public override string ToString() => 
            $"BindingDeclaration(Queue={Queue}, Exchange={Exchange}, RoutingKey={RoutingKey}, Arguments={Arguments.Count})";
    }

    public sealed class ExchangeDeclaration : IDeclaration
    {
        private ExchangeDeclaration(string name, string exchangeType, bool durable = false, bool autoDelete = false,
            bool @internal = false, IReadOnlyDictionary<string, object> arguments = null)
        {
            Name = name;
            ExchangeType = exchangeType;
            Durable = durable;
            AutoDelete = autoDelete;
            Internal = @internal;
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public string Name { get; }

        public string ExchangeType { get; }

        public bool Durable { get; }

        public bool AutoDelete { get; }

        public bool Internal { get; }

        public IReadOnlyDictionary<string, object> Arguments { get; }

        public static ExchangeDeclaration Create(string name, string exchangeType) => new ExchangeDeclaration(name, exchangeType);

        public ExchangeDeclaration WithDurable(bool durable) => new ExchangeDeclaration(Name, ExchangeType, durable, AutoDelete, Internal, Arguments);

        public ExchangeDeclaration WithAutoDelete(bool autoDelete) => new ExchangeDeclaration(Name, ExchangeType, Durable, autoDelete, Internal, Arguments);

        public ExchangeDeclaration WithInternal(bool @internal) => new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, @internal, Arguments);

        public ExchangeDeclaration WithArguments(string key, object value)
        {
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, Internal,
                new Dictionary<string, object> {{key, value}});
        }

        public ExchangeDeclaration WithArguments(KeyValuePair<string, object> argument)
        {
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, Internal,
                new Dictionary<string, object> {{argument.Key, argument.Value}});
        }

        public ExchangeDeclaration WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, Internal,
                arguments.ToDictionary(k => k.Key, val => val.Value));
        }

        public override string ToString() => 
            $"ExchangeDeclaration(Name={Name}, ExchangeType={ExchangeType}, Durable={Durable}, AutoDelete={AutoDelete}, Internal={Internal}, Arguments={Arguments.Count})";
    }
}