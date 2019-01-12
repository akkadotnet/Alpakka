using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Akka.Streams.Amqp.Tests
{
    public class AmqpConnectionProvidersTest : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _mat;
        public AmqpConnectionProvidersTest()
        {
            _mat = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void
            The_AMQP_default_connection_providers_should_create_new_a_new_connection_per_invocation_of_LocalAmqpConnection()
        {
            var connectionProvider = AmqpLocalConnectionProvider.Instance;
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            Assert.NotEqual(connection1, connection2);
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Not_error_if_releasing_already_closed_LocalAmqpCOnnection()
        {
            var connectionProvider = AmqpLocalConnectionProvider.Instance;
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Create_new_connection_per_invocation_of_AmqpConnectionUri()
        {
            var connectionProvider = AmqpUriConnectionProvider.Create("amqp://localhost:5672");
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            Assert.NotEqual(connection1, connection2);
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Not_error_if_releasing_already_closed_AmqpConnectionUri()
        {
            var connectionProvider = AmqpUriConnectionProvider.Create("amqp://localhost:5672");
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Create_new_connection_per_invocation_of_AmqpConnectionDetails()
        {
            var connectionProvider = AmqpDetailsConnectionProvider.Create("localhost", 5672);
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            Assert.NotEqual(connection1, connection2);
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Not_error_if_releasing_already_closed_AmqpConnectionDetails()
        {
            var connectionProvider = AmqpDetailsConnectionProvider.Create("localhost", 5672);
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Create_new_connection_per_invocation_of_AmqpConnectionFactory()
        {
            var connectionFactory = new ConnectionFactory();
            var connectionProvider = AmqpConnectionFactoryConnectionProvider.Create(connectionFactory).WithHostsAndPorts(("localhost", 5672));
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            Assert.NotEqual(connection1, connection2);
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void Not_error_if_releasing_already_closed_AmqpConnectionFactory()
        {
            var connectionFactory = new ConnectionFactory();
            var connectionProvider = AmqpConnectionFactoryConnectionProvider.Create(connectionFactory).WithHostsAndPorts(("localhost", 5672));
            var connection1 = connectionProvider.Get();
            var connection2 = connectionProvider.Get();
            connectionProvider.Release(connection1);
            connectionProvider.Release(connection2);
        }

        [Fact]
        public void
            Reusable_connection_provider_with_automatic_release_should_reuse_the_same_connection_from_LocalAmqpConnection_and_release_it_when_last_client_disconnects()
        {
            var connectionProvider = AmqpLocalConnectionProvider.Instance;
            var reusableConnectionProvider = AmqpCachedConnectionProvider.Create(connectionProvider);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection1);
            Assert.True(connection1.IsOpen);
            Assert.True(connection2.IsOpen);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Reuse_the_same_connection_from_AmqpConnectionUri_and_release_it_when_last_client_disconnects()
        {
            var connectionProvider = AmqpUriConnectionProvider.Create("amqp://localhost:5672");
            var reusableConnectionProvider = AmqpCachedConnectionProvider.Create(connectionProvider);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection1);
            Assert.True(connection1.IsOpen);
            Assert.True(connection2.IsOpen);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Reuse_the_same_connection_from_AmqpConnectionDetails_and_release_it_when_last_client_disconnects()
        {
            var connectionProvider = AmqpDetailsConnectionProvider.Create("localhost", 5672);
            var reusableConnectionProvider = AmqpCachedConnectionProvider.Create(connectionProvider);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection1);
            Assert.True(connection1.IsOpen);
            Assert.True(connection2.IsOpen);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Reuse_the_same_connection_from_AmqpConnectionFactory_and_release_it_when_last_client_disconnects()
        {
            var connectionFactory = new ConnectionFactory();
            var connectionProvider = AmqpConnectionFactoryConnectionProvider.Create(connectionFactory)
                .WithHostsAndPorts(("localhost", 5672));
            var reusableConnectionProvider = AmqpCachedConnectionProvider.Create(connectionProvider);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection1);
            Assert.True(connection1.IsOpen);
            Assert.True(connection2.IsOpen);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }


        [Fact]
        public void
            The_AMQP_reusable_connection_provider_without_automatic_release_should_reuse_same_connection_from_LocalAmqpConnection()
        {
            var connectionProvider = AmqpLocalConnectionProvider.Instance;
            var reusableConnectionProvider =
                AmqpCachedConnectionProvider.Create(connectionProvider, automaticRelease: false);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Reuse_the_same_connection_from_AmqpConnectionUri()
        {
            var connectionProvider = AmqpUriConnectionProvider.Create("amqp://localhost:5672");
            var reusableConnectionProvider =
                AmqpCachedConnectionProvider.Create(connectionProvider, automaticRelease: false);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Reuse_the_same_connection_from_AmqpConnectionDetails()
        {
            var connectionProvider = AmqpDetailsConnectionProvider.Create("localhost", 5672);
            var reusableConnectionProvider =
                AmqpCachedConnectionProvider.Create(connectionProvider, automaticRelease: false);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Reuse_the_same_connection_from_AmqpConnectionFactory()
        {
            var connectionFactory = new ConnectionFactory();
            var connectionProvider = AmqpConnectionFactoryConnectionProvider.Create(connectionFactory)
                .WithHostsAndPorts(("localhost", 5672));
            var reusableConnectionProvider =
                AmqpCachedConnectionProvider.Create(connectionProvider, automaticRelease: false);
            var connection1 = reusableConnectionProvider.Get();
            var connection2 = reusableConnectionProvider.Get();
            Assert.Equal(connection1, connection2);
            reusableConnectionProvider.Release(connection2);
            Assert.False(connection1.IsOpen);
            Assert.False(connection2.IsOpen);
        }

        [Fact]
        public void Not_leave_the_provider_in_an_invalid_state_if_getting_connection_fails()
        {
            var connectionProvider = AmqpDetailsConnectionProvider.Create("localhost", 5673);
            var reusableConnectionProvider = AmqpCachedConnectionProvider.Create(connectionProvider, false);
            try
            {
                reusableConnectionProvider.Get();
            }
            catch (Exception e)
            {
                e.Should().BeOfType<BrokerUnreachableException>();
            }
            try
            {
                reusableConnectionProvider.Get();
            }
            catch (Exception e)
            {
                e.Should().BeOfType<BrokerUnreachableException>();
            }
        }
    }
}
