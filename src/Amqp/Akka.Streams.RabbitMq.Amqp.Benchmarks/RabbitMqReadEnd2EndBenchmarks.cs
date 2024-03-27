﻿//-----------------------------------------------------------------------
// <copyright file="RabbitMqReadEnd2EndBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.IO;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Amqp.Tests;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Akka.Streams.RabbitMq.Amqp.Benchmarks;

[SimpleJob(RunStrategy.ColdStart, iterationCount:10, warmupCount:0)]
[MemoryDiagnoser]
public sealed class RabbitMqReadEnd2EndBenchmarks
{
    private IMaterializer? _materializer;
    private AmqpFixture _fixture = new();
    private ActorSystem? _sys;
    
    
    private AmqpConnectionDetails? _connectionDetails;
    private QueueDeclaration? _writeQueueDeclaration;
    private AmqpSinkSettings? _settings;
    private NamedQueueSourceSettings? _namedQueueSourceSettings;

    public const int WRITE_COUNT = 100_000;

    private readonly int[] _payloads = Enumerable.Range(0, WRITE_COUNT).ToArray();
    private readonly ByteString _byteString = ByteString.FromString("a");

    [GlobalSetup]
    public async Task Setup()
    {
        _sys = ActorSystem.Create("RabbitMqReadEnd2EndBenchmarks");
        _materializer = _sys.Materializer();
        await _fixture.InitializeAsync();
        _connectionDetails = 
            AmqpConnectionDetails
                .Create(_fixture.HostName, _fixture.AmqpPort)
                .WithCredentials(AmqpCredentials.Create(_fixture.UserName, _fixture.Password))
                .WithAutomaticRecoveryEnabled(true)
                .WithNetworkRecoveryInterval(TimeSpan.FromSeconds(1));
        
        _writeQueueDeclaration = QueueDeclaration.Create("read-queue");
        _settings = AmqpSinkSettings.Create(_connectionDetails)
            .WithRoutingKey("read-queue")
            .WithDeclarations(_writeQueueDeclaration);
        
        _namedQueueSourceSettings = NamedQueueSourceSettings.Create(_connectionDetails, "read-queue");
    }

    [IterationSetup]
    public async Task IterationSetup()
    {
        await Source.From(_payloads)
            .Select(_ => _byteString)
            .Select(c => (new OutgoingMessage(c, true, true), 1))
            .Via(AmqpFlow.Create<int>(_settings))
            .RunSum((i, i1) => i + i1, _materializer);
    }

    [Benchmark(OperationsPerInvoke = WRITE_COUNT)]
    public Task<int> RabbitMqReadFlow()
    {
        return AmqpSource.CommittableSource(_namedQueueSourceSettings, 10)
            .SelectAsync(10, async c =>
            {
                await c.Ack();
                return 1;
            })
            .Take(WRITE_COUNT) // used to force the stream to complete
            .RunSum((i, i1) => i + i1, _materializer);
    }
    
    [GlobalCleanup]
    public async Task Cleanup()
    {
        if(_sys is not null)
            await _sys.Terminate();
        await _fixture.DisposeAsync();
    }
}