using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

var topic = serviceBus.AddServiceBusTopic("orders");

var subscription = topic.AddServiceBusSubscription("shipping-subscriber");

var producer = builder.AddProject<ServiceBusProducer>("producer")
    .WaitFor(topic)
    .WithReference(topic);

builder.Build().Run();
