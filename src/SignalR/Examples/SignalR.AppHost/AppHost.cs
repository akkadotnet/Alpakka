var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.SignalRServer>("signalr", launchProfileName: null)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health");
builder.Build().Run();
