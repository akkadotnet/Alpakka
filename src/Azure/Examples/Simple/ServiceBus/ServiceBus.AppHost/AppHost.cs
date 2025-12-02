using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

var topic = serviceBus.AddServiceBusTopic("orders");

var subscription = topic.AddServiceBusSubscription("shipping-subscriber");

var producer = builder.AddProject<ServiceBusProducer>("producer")
    .WaitFor(topic)
    .WithReference(topic);

var consumer = builder.AddProject<ServiceBusConsumer>("consumer")
    .WaitFor(subscription)
    .WithReference(topic);

builder.Build().Run();
