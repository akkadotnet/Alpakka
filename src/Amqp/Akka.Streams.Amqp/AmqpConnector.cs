using System;
using System.Linq;
using Akka.Streams.Stage;
using Akka.Util.Internal;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Internal API
    /// </summary>
    internal class AmqpConnector
    {
        public static IConnectionFactory ConnectionFactoryFrom(IAmqpConnectionSettings settings)
        {
            var factory = new ConnectionFactory();
            switch (settings)
            {
                case AmqpConnectionUri connectionUri:
                    factory.Uri = connectionUri.Uri;
                    break;
                case AmqpConnectionDetails details:
                {
                    if (details.Credentials.HasValue)
                    {
                        factory.UserName = details.Credentials.Value.Username;
                        factory.Password = details.Credentials.Value.Password;
                    }
                    if (!string.IsNullOrEmpty(details.VirtualHost))
                        factory.VirtualHost = details.VirtualHost;
                    if (details.Ssl != null)
                        factory.Ssl = details.Ssl;
                    if (details.AutomaticRecoveryEnabled.HasValue)
                        factory.AutomaticRecoveryEnabled = details.AutomaticRecoveryEnabled.Value;
                    if (details.RequestedHeartbeat.HasValue)
                        factory.RequestedHeartbeat = details.RequestedHeartbeat.Value;
                    if (details.NetworkRecoveryInterval.HasValue)
                        factory.NetworkRecoveryInterval = details.NetworkRecoveryInterval.Value;
                    if (details.TopologyRecoveryEnabled.HasValue)
                        factory.TopologyRecoveryEnabled = details.TopologyRecoveryEnabled.Value;
                    if (details.ConnectionTimeout.HasValue)
                        factory.ContinuationTimeout = details.ConnectionTimeout.Value;
                    if (details.HandshakeTimeout.HasValue)
                        factory.HandshakeContinuationTimeout = details.HandshakeTimeout.Value;
                    break;
                }
                case DefaultAmqpConnection defaultConnection:
                    //leave it be as is
                    break;
            }
            return factory;
        }

        public static IConnection NewConnection(IConnectionFactory factory, IAmqpConnectionSettings settings)
        {
            switch (settings)
            {
                case AmqpConnectionDetails details:
                {
                    if (details.HostAndPortList.Count > 0)
                    {
                        return factory.CreateConnection(details.HostAndPortList
                            .Select(pair => new AmqpTcpEndpoint(pair.host, pair.port)).ToList());
                    }
                    else
                    {
                        throw new ArgumentException("You need to supply at least one host/port pair.");
                    }
                }
                default:
                    return factory.CreateConnection();
            }
        }
    }

    /// <summary>
    /// Internal API
    /// </summary>
    internal abstract class AmqpConnectorLogic : GraphStageLogic
    {
        protected IConnection Connection;
        protected IModel Channel;
        protected Action<ShutdownEventArgs> ShutdownCallback;

        protected AmqpConnectorLogic(Shape shape) 
            : base(shape)
        {
            
        }

        public abstract IAmqpConnectorSettings Settings { get; }

        public abstract IConnectionFactory ConnectionFactoryFrom(IAmqpConnectionSettings settings);

        public abstract IConnection NewConnection(IConnectionFactory factory, IAmqpConnectionSettings settings);

        public abstract void WhenConnected();

        public abstract void OnFailure(Exception ex);

        public override void PreStart()
        {
            try
            {
                var factory = ConnectionFactoryFrom(Settings.ConnectionSettings);
                Connection = NewConnection(factory, Settings.ConnectionSettings);
                Channel = Connection.CreateModel();
                ShutdownCallback = GetAsyncCallback<ShutdownEventArgs>(args =>
                {
                    if (args.Initiator != ShutdownInitiator.Application)
                        FailStage(ShutdownSignalException.FromArgs(args));
                });
                Connection.ConnectionShutdown += OnConnectionShutdown;
                Channel.ModelShutdown += OnChannelShutdown;

                foreach (var declaration in Settings.Declarations)
                {
                    switch (declaration)
                    {
                        case QueueDeclaration queueDeclaration:
                            Channel.QueueDeclare(queueDeclaration.Name, queueDeclaration.Durable,
                                queueDeclaration.Exclusive,
                                queueDeclaration.AutoDelete, queueDeclaration.Arguments.ToDictionary(key=> key.Key,val=> val.Value));
                            break;
                        case BindingDeclaration bindingDeclaration:
                            Channel.QueueBind(bindingDeclaration.Queue, bindingDeclaration.Exchange,
                                bindingDeclaration.RoutingKey ?? "", bindingDeclaration.Arguments.ToDictionary(key => key.Key, val => val.Value));
                            break;
                        case ExchangeDeclaration exchangeDeclaration:
                            Channel.ExchangeDeclare(exchangeDeclaration.Name, exchangeDeclaration.ExchangeType,
                                exchangeDeclaration.Durable, exchangeDeclaration.AutoDelete,
                                exchangeDeclaration.Arguments.ToDictionary(key => key.Key, val => val.Value));
                            break;

                    }
                }

                WhenConnected();
            }
            catch (Exception e)
            {
                OnFailure(e);
                throw;
            }
        }

        private void OnChannelShutdown(object sender, ShutdownEventArgs shutdownEventArgs)
        {
            ShutdownCallback?.Invoke(shutdownEventArgs);
        }

        private void OnConnectionShutdown(object sender, ShutdownEventArgs shutdownEventArgs)
        {
            ShutdownCallback?.Invoke(shutdownEventArgs);
        }

        /// <summary>
        /// remember to call if overriding!
        /// </summary>
        public override void PostStop()
        {
            if (Channel != null)
            {
                if(Channel.IsOpen)
                    Channel.Close();
                Channel.ModelShutdown -= OnChannelShutdown;
                Channel = null;
            }
            if (Connection != null)
            {
                if(Connection.IsOpen)
                    Connection.Close();
                Connection.ConnectionShutdown -= OnConnectionShutdown;
                Connection = null;
            }
        }
    }
}
