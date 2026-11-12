using Projects;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

var builder = DistributedApplication.CreateBuilder(args);

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator(emulator => emulator.WithImageTag("2.0.0")
        .WithConfiguration(config =>
        {
            // An explicit catch-all rule also routes messages in the emulator.
            config["UserConfig"]!["Namespaces"]![0]!["Topics"]![0]!["Subscriptions"]![0]!["Rules"] = JsonNode.Parse("""
                [{"Name":"all-messages","Properties":{"FilterType":"Sql","SqlFilter":{"SqlExpression":"1=1"}}}]
                """);
        }));

if (builder.ExecutionContext.IsRunMode)
{
    var sql = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "messaging-mssql");
    builder.CreateResourceBuilder(sql)
        .WithImageSHA256("cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718");
}

var topic = serviceBus.AddServiceBusTopic("orders");

var subscription = topic.AddServiceBusSubscription("shipping-subscriber");

if (builder.Configuration.GetValue<bool>("Testing"))
{
    serviceBus.AddServiceBusQueue("test-queue", "queue.1");
}
else
{
    builder.AddProject<ServiceBusProducer>("producer")
        .WaitFor(topic)
        .WithReference(topic);

    builder.AddProject<ServiceBusConsumer>("consumer")
        .WaitFor(subscription)
        .WithReference(topic);
}

builder.Build().Run();
