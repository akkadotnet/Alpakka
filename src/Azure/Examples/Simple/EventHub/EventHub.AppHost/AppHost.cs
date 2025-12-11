var builder = DistributedApplication.CreateBuilder(args);

var eventHub = builder.AddAzureEventHubs("eventhubs")
    .RunAsEmulator();

var hub = eventHub.AddHub("orders");
hub.AddConsumerGroup("orders-consumer");


var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

var blob = storage.AddBlobs("checkpoint");

var consumer = builder.AddProject<Projects.EventHub_Consumer>("consumer")
    .WithReference(eventHub)
    .WithReference(blob)
    .WaitFor(blob)
    .WaitFor(eventHub);

var producer = builder.AddProject<Projects.EventHub_Producer>("producer")
    .WithReference(eventHub)
    .WaitFor(eventHub);

builder.Build().Run();
