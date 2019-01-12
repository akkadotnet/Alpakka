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

        public abstract void WhenConnected();

        public abstract void OnFailure(Exception ex);

        public override void PreStart()
        {
            try
            {
                Connection = Settings.ConnectionProvider.Get();
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
                Connection.ConnectionShutdown -= OnConnectionShutdown;
                Settings.ConnectionProvider.Release(Connection);
                Connection = null;
            }
        }
    }
}
