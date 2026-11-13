using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var eventHub = builder.AddAzureEventHubs("eventhubs")
    .RunAsEmulator(emulator => emulator.WithImageTag("2.2.0"));

var hub = eventHub.AddHub("orders");
hub.AddConsumerGroup("orders-consumer");


var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithImageTag("3.35.0"));

var blob = storage.AddBlobs("checkpoint");

if (builder.Configuration.GetValue<bool>("Testing"))
{
    hub.Resource.PartitionCount = 2;
}
else
{
    builder.AddProject<Projects.EventHub_Consumer>("consumer")
        .WithReference(eventHub)
        .WithReference(blob)
        .WaitFor(blob)
        .WaitFor(eventHub);

    builder.AddProject<Projects.EventHub_Producer>("producer")
        .WithReference(eventHub)
        .WaitFor(eventHub);
}

foreach (var resource in builder.Resources)
{
    foreach (var image in resource.Annotations.OfType<ContainerImageAnnotation>()
                 .Where(image => image.Image == "azure-storage/azurite"))
        image.Tag = "3.35.0";
}

builder.Build().Run();
