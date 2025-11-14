var builder = DistributedApplication.CreateBuilder(args);

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

var topic = serviceBus.AddServiceBusTopic("orders");

var subscription = topic.AddServiceBusSubscription("shipping-subscriber");

builder.Build().Run();
