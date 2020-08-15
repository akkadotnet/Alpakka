# Akka.NET Streams connectors for RabbitMQ AMQP brokers

## RabbitMQ V 0.9.1 Brokers

Example:
```
var connectionSettings = AmqpConnectionDetails
    .Create("localhost", 5672)
    .WithCredentials(AmqpCredentials.Create("username", "password"))
    .WithAutomaticRecoveryEnabled(true)
    .WithNetworkRecoveryInterval(TimeSpan.FromSeconds(1));

var queueName = "myQueue";
//queue declaration
var queueDeclaration = QueueDeclaration
    .Create(queueName)
    .WithDurable(false)
    .WithAutoDelete(true);

//create sink
var amqpSink = AmqpSink.CreateSimple( 
    AmqpSinkSettings
        .Create(connectionSettings)
        .WithRoutingKey(queueName)
        .WithDeclarations(queueDeclaration));

//create source
var amqpSource = AmqpSource.AtMostOnceSource( 
    NamedQueueSourceSettings
        .Create(connectionSettings, queueName)
        .WithDeclarations(queueDeclaration),bufferSize: 10);

//run sink
var input = new[] {"one", "two", "three", "four", "five"};
Source
    .From(input)
    .Select(ByteString.FromString)
    .RunWith(amqpSink, _mat).Wait();

//run source
var result = await amqpSource
    .Select(m => m.Bytes.ToString(Encoding.UTF8))
    .Take(input.Length)
    .RunWith(Sink.Seq<string>(), _mat);
```