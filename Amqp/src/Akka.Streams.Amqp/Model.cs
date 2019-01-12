using System;
using System.Collections.Generic;
using System.Linq;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public interface IAmqpConnectorSettings
    {
        AmqpConnectionProvider ConnectionProvider { get; }
        IReadOnlyList<IDeclaration> Declarations { get; }
    }

    public interface IAmqpSourceSettings : IAmqpConnectorSettings
    {
    }

    public sealed class NamedQueueSourceSettings : IAmqpSourceSettings
    {
        private NamedQueueSourceSettings(AmqpConnectionProvider connectionProvider, string queue,
            IReadOnlyList<IDeclaration> declarations = null, bool noLocal = false, bool exclusive = false,
            string consumerTag = null,
            IReadOnlyDictionary<string, object> arguments = null)
        {
            ConnectionProvider = connectionProvider;
            Queue = queue;
            Declarations = declarations ?? new List<IDeclaration>();
            NoLocal = noLocal;
            Exclusive = exclusive;
            ConsumerTag = consumerTag ?? "default";
            Arguments = arguments ?? new Dictionary<string, object>();
        }

        public AmqpConnectionProvider ConnectionProvider { get; }
        public string Queue { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }
        public bool NoLocal { get; }
        public bool Exclusive { get; }
        public string ConsumerTag { get; }
        public IReadOnlyDictionary<string, object> Arguments { get; }

        public static NamedQueueSourceSettings Create(AmqpConnectionProvider connectionProvider, string queue)
        {
            return new NamedQueueSourceSettings(connectionProvider, queue);
        }

        public NamedQueueSourceSettings WithDeclarations(params IDeclaration[] declarations)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, declarations, NoLocal, Exclusive, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithNoLocal(bool noLocal)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, Declarations, noLocal, Exclusive, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithExclusive(bool exclusive)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, Declarations, NoLocal, exclusive, ConsumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithConsumerTag(string consumerTag)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, Declarations, NoLocal, Exclusive, consumerTag, Arguments);
        }

        public NamedQueueSourceSettings WithArguments(params KeyValuePair<string, object>[] arguments)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, Declarations, NoLocal, Exclusive, ConsumerTag,
                arguments.ToDictionary(key => key.Key, val => val.Value));
        }

        public NamedQueueSourceSettings WithArguments(params (string paramName, object paramValue)[] arguments)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, Declarations, NoLocal, Exclusive, ConsumerTag,
                arguments.ToDictionary(key => key.paramName, val => val.paramValue));
        }

        public NamedQueueSourceSettings WithArguments(string key, object value)
        {
            return new NamedQueueSourceSettings(ConnectionProvider, Queue, Declarations, NoLocal, Exclusive,
                ConsumerTag,
                new Dictionary<string, object> {{key, value}});
        }

        public override string ToString() => 
            $"NamedQueueSourceSettings(ConnectionSettings={ConnectionProvider}, Queue={Queue}, Declarations={Declarations.Count}, NoLocal={NoLocal}, Exclusive={Exclusive}, Arguments={Arguments.Count})";
    }

    public sealed class TemporaryQueueSourceSettings : IAmqpSourceSettings
    {
        private TemporaryQueueSourceSettings(AmqpConnectionProvider connectionProvider, string exchange,
            IReadOnlyList<IDeclaration> declarations = null, string routingKey = null)
        {
            ConnectionProvider = connectionProvider;
            Exchange = exchange;
            Declarations = declarations ?? new List<IDeclaration>();
            RoutingKey = routingKey;
        }

        public AmqpConnectionProvider ConnectionProvider { get; }
        public string Exchange { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }
        public string RoutingKey { get; }

        public static TemporaryQueueSourceSettings Create(AmqpConnectionProvider connectionProvider, string exchange) => 
            new TemporaryQueueSourceSettings(connectionProvider, exchange);

        public TemporaryQueueSourceSettings WithRoutingKey(string routingKey) => 
            new TemporaryQueueSourceSettings(ConnectionProvider, Exchange, Declarations, routingKey);

        public TemporaryQueueSourceSettings WithDeclarations(params IDeclaration[] declarations) => 
            new TemporaryQueueSourceSettings(ConnectionProvider, Exchange, declarations, RoutingKey);

        public override string ToString() => 
            $"TemporaryQueueSourceSettings(ConnectionSettings={ConnectionProvider},Exchange={Exchange}, Declarations={Declarations.Count}, RoutingKey={RoutingKey})";
    }

    public sealed class AmqpReplyToSinkSettings : IAmqpConnectorSettings
    {
        private AmqpReplyToSinkSettings(AmqpConnectionProvider connectionProvider, bool failIfReplyToMissing = true)
        {
            ConnectionProvider = connectionProvider;
            FailIfReplyToMissing = failIfReplyToMissing;
            Declarations = new List<IDeclaration>();
        }

        public AmqpConnectionProvider ConnectionProvider { get; }
        public bool FailIfReplyToMissing { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }

        public static AmqpReplyToSinkSettings Create(AmqpConnectionProvider connectionProvider, bool failIfReplyToMissing = true)
        {
            return new AmqpReplyToSinkSettings(connectionProvider, failIfReplyToMissing);
        }
    }

    public sealed class AmqpSinkSettings : IAmqpConnectorSettings
    {
        private AmqpSinkSettings(AmqpConnectionProvider connectionProvider, string exchange = null,
            string routingKey = null, IReadOnlyList<IDeclaration> declarations = null)
        {
            ConnectionProvider = connectionProvider;
            Exchange = exchange;
            RoutingKey = routingKey;
            Declarations = declarations ?? new List<IDeclaration>();
        }

        public AmqpConnectionProvider ConnectionProvider { get; }
        public string Exchange { get; }
        public string RoutingKey { get; }
        public IReadOnlyList<IDeclaration> Declarations { get; }

        public static AmqpSinkSettings Create(AmqpConnectionProvider connectionProvider) => 
            new AmqpSinkSettings(connectionProvider);

        public AmqpSinkSettings WithExchange(string exchange) => new AmqpSinkSettings(ConnectionProvider, exchange, RoutingKey, Declarations);

        public AmqpSinkSettings WithRoutingKey(string routingKey) => new AmqpSinkSettings(ConnectionProvider, Exchange, routingKey, Declarations);

        public AmqpSinkSettings WithDeclarations(params IDeclaration[] declarations) => 
            new AmqpSinkSettings(ConnectionProvider, Exchange, RoutingKey, declarations);

        public override string ToString() => 
            $"AmqpSinkSettings(ConnectionSettings={ConnectionProvider}, Exchange={Exchange}, RoutingKey={RoutingKey}, Delcarations={Declarations.Count})";
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