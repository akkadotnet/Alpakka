using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Util.Internal;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{

    public interface IAmqpConnectorSettings
    {
        IAmqpConnectionSettings ConnectionSettings { get; }

        IList<IDeclaration> Declarations { get; }
    }

    public interface IAmqpSourceSettings : IAmqpConnectorSettings
    {
    }

    public sealed class NamedQueueSourceSettings : IAmqpSourceSettings
    {
        private NamedQueueSourceSettings(IAmqpConnectionSettings connectionSettings, string queue,
            IList<IDeclaration> declarations = null, bool noLocal = false, bool exclusive = false,
            string consumerTag = null,
            IDictionary<string, object> arguments = null)
        {
            ConnectionSettings = connectionSettings;
            Queue = queue;
            Declarations = declarations ?? new List<IDeclaration>();
            NoLocal = noLocal;
            Exclusive = exclusive;
            ConsumerTag = consumerTag ?? "default";
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public string Queue { get; }
        public IList<IDeclaration> Declarations { get; }
        public bool NoLocal { get; }

        public bool Exclusive { get; }

        public string ConsumerTag { get; }

        public IDictionary<string, object> Arguments { get; }

        public static NamedQueueSourceSettings Create(IAmqpConnectionSettings connectionSettings, string queue)
        {
            return new NamedQueueSourceSettings(connectionSettings, queue);
        }

        public NamedQueueSourceSettings WithDeclarations(params IDeclaration[] declarations)
        {
            declarations.ForEach(Declarations.Add);
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, ConsumerTag,
                Arguments);
        }

        public NamedQueueSourceSettings WithNoLocal(bool noLocal)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, noLocal, Exclusive, ConsumerTag,
                Arguments);
        }

        public NamedQueueSourceSettings WithExclusive(bool exclusive)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, exclusive, ConsumerTag,
                Arguments);
        }

        public NamedQueueSourceSettings WithConsumerTag(string consumerTag)
        {
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, consumerTag,
                Arguments);
        }

        public NamedQueueSourceSettings WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            arguments.ForEach(Arguments.Add);
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, ConsumerTag,
                Arguments);
        }

        public NamedQueueSourceSettings WithArguments(params (string paramName, object paramValue)[] arguments)
        {
            arguments.ForEach(pair=> Arguments.Add(pair.paramName,pair.paramValue));
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, ConsumerTag,
                Arguments);
        }

        public NamedQueueSourceSettings WithArguments(string key, object value)
        {
            Arguments.Add(key, value);
            return new NamedQueueSourceSettings(ConnectionSettings, Queue, Declarations, NoLocal, Exclusive, ConsumerTag,
                Arguments);
        }

        public override string ToString()
        {
            return
                $"NamedQueueSourceSettings(ConnectionSettings={ConnectionSettings}, Queue={Queue}, Declarations={Declarations.Count}, NoLocal={NoLocal}, Exclusive={Exclusive}, Arguments={Arguments.Count})";
        }
    }

    public sealed class TemporaryQueueSourceSettings : IAmqpSourceSettings
    {
        private TemporaryQueueSourceSettings(IAmqpConnectionSettings connectionSettings, string exchange,
            IList<IDeclaration> declarations = null, string routingKey = null)
        {
            ConnectionSettings = connectionSettings;
            Exchange = exchange;
            Declarations = declarations ?? new List<IDeclaration>();
            RoutingKey = routingKey;
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public string Exchange { get; }
        public IList<IDeclaration> Declarations { get; }
        public string RoutingKey { get; }

        public static TemporaryQueueSourceSettings Create(IAmqpConnectionSettings connectionSettings, string exchange)
        {
            return new TemporaryQueueSourceSettings(connectionSettings, exchange);
        }

        public TemporaryQueueSourceSettings WithRoutingKey(string routingKey)
        {
            return new TemporaryQueueSourceSettings(ConnectionSettings, Exchange, Declarations, routingKey);
        }

        public TemporaryQueueSourceSettings WithDeclarations(params IDeclaration[] declarations)
        {
            declarations.ForEach(Declarations.Add);
            return new TemporaryQueueSourceSettings(ConnectionSettings, Exchange, Declarations, RoutingKey);
        }

        public override string ToString()
        {
            return
                $"TemporaryQueueSourceSettings(ConnectionSettings={ConnectionSettings},Exchange={Exchange}, Declarations={Declarations.Count}, RoutingKey={RoutingKey})";
        }
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
        public IList<IDeclaration> Declarations { get; }

        public static AmqpReplyToSinkSettings Create(IAmqpConnectionSettings connectionSettings,
            bool failIfReplyToMissing = true)
        {
            return new AmqpReplyToSinkSettings(connectionSettings, failIfReplyToMissing);
        }
    }

    public sealed class AmqpSinkSettings : IAmqpConnectorSettings
    {
        private AmqpSinkSettings(IAmqpConnectionSettings connectionSettings, string exchange = null,
            string routingKey = null, IList<IDeclaration> declarations = null)
        {
            ConnectionSettings = connectionSettings;
            Exchange = exchange;
            RoutingKey = routingKey;
            Declarations = declarations ?? new List<IDeclaration>();
        }

        public IAmqpConnectionSettings ConnectionSettings { get; }
        public string Exchange { get; }
        public string RoutingKey { get; }
        public IList<IDeclaration> Declarations { get; }
        
        public static AmqpSinkSettings Create(IAmqpConnectionSettings connectionSettings = null)
        {
            return new AmqpSinkSettings(connectionSettings?? DefaultAmqpConnection.Instance);
        }

        public AmqpSinkSettings WithExchange(string exchange)
        {
            return new AmqpSinkSettings(ConnectionSettings, exchange, RoutingKey, Declarations);
        }

        public AmqpSinkSettings WithRoutingKey(string routingKey)
        {
            return new AmqpSinkSettings(ConnectionSettings, Exchange, routingKey, Declarations);
        }

        public AmqpSinkSettings WithDeclarations(params IDeclaration[] declarations)
        {
            declarations.ForEach(Declarations.Add);
            return new AmqpSinkSettings(ConnectionSettings, Exchange, RoutingKey, Declarations);
        }

        public override string ToString()
        {

            return
                $"AmqpSinkSettings(ConnectionSettings={ConnectionSettings}, Exchange={Exchange}, RoutingKey={RoutingKey}, Delcarations={Declarations.Count})";
        }
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

        public AmqpConnectionUri Create(string uri)
        {
            return new AmqpConnectionUri(new Uri(uri));
        }

        public override string ToString()
        {
            return $"AmqpConnectionUri(Uri={Uri})";
        }
    }

    public sealed class AmqpConnectionDetails : IAmqpConnectionSettings
    {
        private AmqpConnectionDetails(IList<(string host, int port)> hostAndPortList, 
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

        public IList<(string host, int port)> HostAndPortList { get; }
        public AmqpCredentials? Credentials { get; }
        public string VirtualHost { get; }
        public SslOption Ssl { get; }
        public ushort? RequestedHeartbeat { get; }
        public TimeSpan? ConnectionTimeout { get; }
        public TimeSpan? HandshakeTimeout { get; }
        public TimeSpan? NetworkRecoveryInterval { get; }
        public bool? AutomaticRecoveryEnabled { get; }
        public bool? TopologyRecoveryEnabled { get; }

        public static AmqpConnectionDetails Create(string host, int port)
        {
            return new AmqpConnectionDetails(new List<(string host, int port)> {(host, port)});
        }

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

        public static AmqpCredentials Create(string username, string password)
        {
            return new AmqpCredentials(username, password);
        }

        public override string ToString()
        {
            return $"AmqpCredentials(Username={Username}, Password=********)";
        }

        public override bool Equals(object obj)
        {
            return obj is AmqpCredentials && Equals((AmqpCredentials)obj);
        }

        public bool Equals(AmqpCredentials other)
        {
            return Username == other.Username &&
                   Password == other.Password;
        }

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
            IDictionary<string, object> arguments = null)
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

        public IDictionary<string, object> Arguments { get; }

        public static QueueDeclaration Create(string name)
        {
            return new QueueDeclaration(name);
        }

        public QueueDeclaration WithDurable(bool durable)
        {
            return new QueueDeclaration(Name, durable, Exclusive, AutoDelete, Arguments);
        }

        public QueueDeclaration WithExclusive(bool exclusive)
        {
            return new QueueDeclaration(Name, Durable, exclusive, AutoDelete, Arguments);
        }

        public QueueDeclaration WithAutoDelete(bool autoDelete)
        {
            return new QueueDeclaration(Name, Durable, Exclusive, autoDelete, Arguments);
        }

        public QueueDeclaration WithArguments(KeyValuePair<string, object> argument)
        {
            Arguments.Add(argument);
            return new QueueDeclaration(Name, Durable, Exclusive, AutoDelete, Arguments);
        }

        public QueueDeclaration WithArguments(string key, object value)
        {
            Arguments.Add(key, value);
            return new QueueDeclaration(Name, Durable, Exclusive, AutoDelete, Arguments);
        }

        public QueueDeclaration WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            arguments.ForEach(Arguments.Add);
            return new QueueDeclaration(Name, Durable, Exclusive, AutoDelete, Arguments);
        }

        public override string ToString()
        {
            return
                $"QueueDeclaration(Name={Name}, Durable={Durable}, Exclusive={Exclusive}, AutoDelete={AutoDelete}, Arguments={Arguments.Count})";
        }
    }

    public sealed class BindingDeclaration : IDeclaration
    {
        private BindingDeclaration(string queue, string exchange, string routingKey = null,
            IDictionary<string, object> arguments = null)
        {
            Queue = queue;
            Exchange = exchange;
            RoutingKey = routingKey;
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public string Queue { get; }

        public string Exchange { get; }

        public string RoutingKey { get; }

        public IDictionary<string, object> Arguments { get; }

        public static BindingDeclaration Create(string queue, string exchange)
        {
            return new BindingDeclaration(queue, exchange);
        }

        public BindingDeclaration WithRoutingKey(string routingKey)
        {
            return new BindingDeclaration(Queue, Exchange, routingKey, Arguments);
        }

        public BindingDeclaration WithArguments(string key, object value)
        {
            Arguments.Add(key, value);
            return new BindingDeclaration(Queue, Exchange, RoutingKey, Arguments);
        }

        public BindingDeclaration WithArguments(KeyValuePair<string, object> argument)
        {
            Arguments.Add(argument);
            return new BindingDeclaration(Queue, Exchange, RoutingKey, Arguments);
        }

        public BindingDeclaration WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            arguments.ForEach(Arguments.Add);
            return new BindingDeclaration(Queue, Exchange, RoutingKey, Arguments);
        }

        public override string ToString()
        {
            return
                $"BindingDeclaration(Queue={Queue}, Exchange={Exchange}, RoutingKey={RoutingKey}, Arguments={Arguments.Count})";
        }
    }

    public sealed class ExchangeDeclaration : IDeclaration
    {
        private ExchangeDeclaration(string name, string exchangeType, bool durable = false, bool autoDelete = false,
            bool @internal = false, IDictionary<string, object> arguments = null)
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

        public IDictionary<string, object> Arguments { get; }

        public static ExchangeDeclaration Create(string name, string exchangeType)
        {
            return new ExchangeDeclaration(name, exchangeType);
        }

        public ExchangeDeclaration WithDurable(bool durable)
        {
            return new ExchangeDeclaration(Name, ExchangeType, durable, AutoDelete, Internal, Arguments);
        }

        public ExchangeDeclaration WithAutoDelete(bool autoDelete)
        {
            return new ExchangeDeclaration(Name, ExchangeType, Durable, autoDelete, Internal, Arguments);
        }

        public ExchangeDeclaration WithInternal(bool @internal)
        {
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, @internal, Arguments);
        }

        public ExchangeDeclaration WithArguments(string key, object value)
        {
            Arguments.Add(key, value);
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, Internal, Arguments);
        }

        public ExchangeDeclaration WithArguments(KeyValuePair<string, object> argument)
        {
            Arguments.Add(argument);
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, Internal, Arguments);
        }

        public ExchangeDeclaration WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            arguments.ForEach(Arguments.Add);
            return new ExchangeDeclaration(Name, ExchangeType, Durable, AutoDelete, Internal, Arguments);
        }

        public override string ToString()
        {
            return
                $"ExchangeDeclaration(Name={Name}, ExchangeType={ExchangeType}, Durable={Durable}, AutoDelete={AutoDelete}, Internal={Internal}, Arguments={Arguments.Count})";
        }
    }

}
