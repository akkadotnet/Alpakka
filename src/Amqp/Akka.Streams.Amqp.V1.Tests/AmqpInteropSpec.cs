using System;
using System.Threading.Tasks;
using Akka.Serialization;
using Akka.Streams.Amqp.Tests;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Extensions;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using Xunit;
using Xunit.Abstractions;
using StreamSource = Akka.Streams.Dsl.Source;

#nullable enable

namespace Akka.Streams.Amqp.V1.Tests;

[Collection("AmqpSpec")]
public sealed class AmqpInteropSpec : Akka.TestKit.Xunit2.TestKit
{
    private readonly AmqpFixture _fixture;

    public AmqpInteropSpec(AmqpFixture fixture, ITestOutputHelper output) : base(output: output)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Named_and_address_settings_interoperate_with_native_peers_and_close_their_links(bool manageConnection)
    {
        var address = new Address(_fixture.HostName, _fixture.AmqpPort, _fixture.UserName, _fixture.Password, scheme: "AMQP");
        var connection = new Connection(address);
        var session = new Session(connection);
        var serializer = Sys.Serialization.FindSerializerForType(typeof(string));
        var inputQueue = $"native-input-{Guid.NewGuid():N}";
        var outputQueue = $"native-output-{Guid.NewGuid():N}";
        var nativeSender = new SenderLink(session, "native-sender", new Target
        {
            Address = inputQueue, Capabilities = new[] { new Symbol("queue") }
        }, null);
        var nativeReceiver = CreateReceiver(session, "native-receiver", outputQueue);
        var source = new ObservedSourceSettings(manageConnection
            ? new AddressSourceSettings<string>(address, "connector-source", inputQueue, 1, serializer)
            : new NamedQueueSourceSettings<string>(session, "connector-source", inputQueue, 1, serializer));
        var sink = new ObservedSinkSettings(manageConnection
            ? new AddressSinkSettings<string>(address, "connector-sink", outputQueue, serializer)
            : new NamedQueueSinkSettings<string>(session, "connector-sink", outputQueue, serializer));

        try
        {
            // Establish that this broker releases an unaccepted delivery when its link closes.
            await nativeSender.SendAsync(new Message(serializer.ToBinary("settlement-control")));
            var heldReceiver = CreateReceiver(session, "held-control", inputQueue);
            var held = await heldReceiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(held);
            Assert.Equal("settlement-control", serializer.FromBinary<string>(held.GetBody<byte[]>()));
            await heldReceiver.CloseAsync();
            var releasedReceiver = CreateReceiver(session, "released-control", inputQueue);
            var released = await releasedReceiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(released);
            Assert.Equal("settlement-control", serializer.FromBinary<string>(released.GetBody<byte[]>()));
            releasedReceiver.Accept(released);
            await releasedReceiver.CloseAsync();

            var expected = new[] { "first", "café", "last" };
            foreach (var value in expected)
                await nativeSender.SendAsync(new Message(serializer.ToBinary(value)));

            var actual = await Dsl.AmqpSource.Create(source).Take(expected.Length)
                .RunWith(Sink.Seq<string>(), Sys.Materializer())
                .WithTimeout(TimeSpan.FromSeconds(10));
            Assert.Equal(expected, actual);
            await AwaitAssertAsync(() => Assert.True(source.Link?.IsClosed));
            Assert.False(session.IsClosed);
            Assert.False(connection.IsClosed);

            // A source that omitted Accept would redeliver these messages after closing its link.
            var observer = CreateReceiver(session, "settlement-observer", inputQueue);
            Assert.Null(await observer.ReceiveAsync(TimeSpan.FromSeconds(5)));
            await observer.CloseAsync();

            var sinkCompletion = StreamSource.From(expected).RunWith(Dsl.AmqpSink.Create(sink), Sys.Materializer());
            Assert.True(await sinkCompletion.AwaitWithTimeout(TimeSpan.FromSeconds(10)));
            await sinkCompletion;
            await AwaitAssertAsync(() => Assert.True(sink.Link?.IsClosed));
            Assert.False(session.IsClosed);
            Assert.False(connection.IsClosed);

            foreach (var value in expected)
            {
                var message = await nativeReceiver.ReceiveAsync(TimeSpan.FromSeconds(5));
                Assert.NotNull(message);
                Assert.Equal(value, serializer.FromBinary<string>(message.GetBody<byte[]>()));
                nativeReceiver.Accept(message);
            }
            Assert.Null(await nativeReceiver.ReceiveAsync(TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            await nativeSender.CloseAsync();
            await nativeReceiver.CloseAsync();
            if (manageConnection)
            {
                await source.CloseConnectionAsync();
                await sink.CloseConnectionAsync();
            }
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }

    private static ReceiverLink CreateReceiver(Session session, string name, string queue) =>
        new(session, name, new global::Amqp.Framing.Source
        {
            Address = queue, Capabilities = new[] { new Symbol("queue") }
        }, null);

    [Fact]
    public async Task A_closed_sender_faults_the_materialized_task_instead_of_leaving_it_pending()
    {
        var address = new Address(_fixture.HostName, _fixture.AmqpPort, _fixture.UserName, _fixture.Password, scheme: "AMQP");
        var connection = new Connection(address);
        var session = new Session(connection);
        var queue = $"disconnect-{Guid.NewGuid():N}";
        var receiver = CreateReceiver(session, "disconnect-observer", queue);
        var settings = new ObservedSinkSettings(new NamedQueueSinkSettings<string>(
            session, "disconnect-sender", queue, Sys.Serialization.FindSerializerForType(typeof(string))));
        var (publisher, completion) = this.SourceProbe<string>()
            .ToMaterialized(Dsl.AmqpSink.Create(settings), Keep.Both)
            .Run(Sys.Materializer());

        try
        {
            publisher.SendNext("warm-up");
            var message = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(message);
            receiver.Accept(message);
            Assert.False(completion.IsCompleted);
            Assert.NotNull(settings.Link);

            await settings.Link.CloseAsync(TimeSpan.FromSeconds(5));

            publisher.ExpectCancellation();
            Assert.True(completion.IsFaulted, "The stage terminated but its materialized task remained pending.");
            var failure = await Assert.ThrowsAsync<DisconnectedException>(() => completion);
            Assert.Same(settings.Link, failure.Sender);
            Assert.Null(failure.Error);
        }
        finally
        {
            await receiver.CloseAsync();
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }

    [Fact]
    public async Task A_closed_receiver_fails_the_source_and_keeps_the_borrowed_session_open()
    {
        var address = new Address(_fixture.HostName, _fixture.AmqpPort, _fixture.UserName, _fixture.Password, scheme: "AMQP");
        var connection = new Connection(address);
        var session = new Session(connection);
        var queue = $"source-disconnect-{Guid.NewGuid():N}";
        var serializer = Sys.Serialization.FindSerializerForType(typeof(string));
        var sender = new SenderLink(session, "source-disconnect-publisher", new Target
        {
            Address = queue, Capabilities = new[] { new Symbol("queue") }
        }, null);
        var settings = new ObservedSourceSettings(new NamedQueueSourceSettings<string>(
            session, "source-disconnect-receiver", queue, 1, serializer));

        try
        {
            await sender.SendAsync(new Message(serializer.ToBinary("warm-up")));
            var probe = Dsl.AmqpSource.Create(settings)
                .RunWith(this.SinkProbe<string>(), Sys.Materializer());
            probe.Request(1).ExpectNext("warm-up");
            Assert.NotNull(settings.Link);
            await settings.Link.CloseAsync(TimeSpan.FromSeconds(5));

            var failure = Assert.IsType<DisconnectedException>(probe.ExpectError());
            Assert.Same(settings.Link, failure.Sender);
            Assert.Null(failure.Error);
            Assert.False(session.IsClosed);
            Assert.False(connection.IsClosed);
        }
        finally
        {
            await sender.CloseAsync();
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }

    [Fact]
    public async Task A_serialization_failure_faults_the_materialized_task_with_the_original_error()
    {
        var address = new Address(_fixture.HostName, _fixture.AmqpPort, _fixture.UserName, _fixture.Password, scheme: "AMQP");
        var connection = new Connection(address);
        var session = new Session(connection);
        var failure = new InvalidOperationException("serialization failed");
        var queue = $"serialize-{Guid.NewGuid():N}";
        var receiver = CreateReceiver(session, "serializer-observer", queue);
        var serializer = Sys.Serialization.FindSerializerForType(typeof(string));
        var inner = new NamedQueueSinkSettings<string>(session, "failing-serializer", queue, serializer);
        var settings = new ObservedSinkSettings(inner,
            value => value == "warm-up" ? inner.GetBytes(value) : throw failure);
        var (publisher, completion) = this.SourceProbe<string>()
            .ToMaterialized(Dsl.AmqpSink.Create(settings), Keep.Both)
            .Run(Sys.Materializer());

        try
        {
            publisher.SendNext("warm-up");
            var message = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(message);
            Assert.Equal("warm-up", serializer.FromBinary<string>(message.GetBody<byte[]>()));
            receiver.Accept(message);
            Assert.False(completion.IsCompleted);
            publisher.SendNext("fails conversion");
            publisher.ExpectCancellation();
            await AwaitAssertAsync(() => Assert.True(settings.Link?.IsClosed));
            Assert.True(completion.IsFaulted, "Conversion failed but the materialized task remained pending.");
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => completion));
        }
        finally
        {
            await receiver.CloseAsync();
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }

    private sealed class ObservedSourceSettings(IAmqpSourceSettings<string> inner) : IAmqpSourceSettings<string>
    {
        public ReceiverLink? Link { get; private set; }
        public int Credit => inner.Credit;
        public bool ManageConnection => inner.ManageConnection;
        public ReceiverLink GetReceiverLink() => Link = inner.GetReceiverLink();
        public string Convert(Message message) => inner.Convert(message);
        public void CloseConnection() => inner.CloseConnection();
        public Task CloseConnectionAsync() => inner.CloseConnectionAsync();
    }

    private sealed class ObservedSinkSettings(IAmqpSinkSettings<string> inner, Func<string, byte[]>? getBytes = null) : IAmqpSinkSettings<string>
    {
        public SenderLink? Link { get; private set; }
        public bool ManageConnection => inner.ManageConnection;
        public SenderLink GetSenderLink() => Link = inner.GetSenderLink();
        public byte[] GetBytes(string value) => getBytes is null ? inner.GetBytes(value) : getBytes(value);
        public void CloseConnection() => inner.CloseConnection();
        public Task CloseConnectionAsync() => inner.CloseConnectionAsync();
    }
}
