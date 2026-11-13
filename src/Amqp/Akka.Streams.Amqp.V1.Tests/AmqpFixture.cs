using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Amqp.V1.Tests;
using DotNet.Testcontainers;
using Microsoft.Extensions.Logging;
using Testcontainers.ActiveMq;
using Xunit;

#nullable enable
namespace Akka.Streams.Amqp.Tests;

[CollectionDefinition("AmqpSpec")]
public sealed class AmqpSpecFixture : ICollectionFixture<AmqpFixture>
{
}

public class AmqpFixture : IAsyncLifetime
{
    protected readonly string BrokerContainerName = $"alpakka-amqp-v1-{Guid.NewGuid():N}";
    public string? ConnectionString { get; private set; }
    public string HostName => Container!.Hostname;
    public int AmqpPort => Container!.GetMappedPublicPort(ArtemisBuilder.ArtemisMainPort);

    public string UserName => "guest";
    public string Password => "guest";

    public ArtemisContainer? Container { get; protected set; }

    public async Task InitializeAsync()
    {
        var startupLog = new StartupLog();
        Container = new ArtemisBuilder("apache/activemq-artemis:2.42.0")
            .WithName(BrokerContainerName)
            .WithUsername(UserName)
            .WithPassword(Password)
            .WithLogger(startupLog)
            .Build();
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Container.StartAsync(startup.Token);
            ConnectionString = new UriBuilder("amqp", HostName, AmqpPort)
            {
                UserName = UserName,
                Password = Password
            }.Uri.ToString();
        }
        catch (Exception startupException)
        {
            // Snapshot before cleanup so its messages cannot replace the pull failure.
            var diagnostics = startupLog.GetDiagnostics();
            var details = diagnostics.Length == 0
                ? string.Empty
                : $"{Environment.NewLine}Docker startup diagnostics:{Environment.NewLine}{diagnostics}";
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("AMQP broker startup and cleanup both failed." + details, startupException, cleanupException);
            }

            if (diagnostics.Length != 0)
                throw new InvalidOperationException("AMQP broker startup failed." + details, startupException);

            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var container = Container;
        Container = null;
        ConnectionString = null;
        if (container is not null)
            await container.DisposeAsync();
    }

    private sealed class StartupLog : ILogger
    {
        private readonly ILogger _console = ConsoleLogger.Instance;
        private readonly object _sync = new();
        private readonly Queue<string> _messages = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _console.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) =>
            (logLevel >= LogLevel.Warning && logLevel != LogLevel.None) || _console.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning && logLevel != LogLevel.None)
            {
                var message = formatter(state, exception);
                if (exception is not null)
                    message += Environment.NewLine + exception;
                if (message.Length > 2048)
                    message = message.Substring(0, 2048) + " [truncated]";
                lock (_sync)
                {
                    if (_messages.Count == 8)
                        _messages.Dequeue();
                    _messages.Enqueue($"{logLevel}: {message}");
                }
            }

            _console.Log(logLevel, eventId, state, exception, formatter);
        }

        public string GetDiagnostics()
        {
            lock (_sync)
                return string.Join(Environment.NewLine, _messages);
        }
    }
}
