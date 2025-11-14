var builder = DistributedApplication.CreateBuilder(args);

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();
var queue = serviceBus.AddQueue("orders");

builder.Build().Run();
