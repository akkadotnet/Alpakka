//-----------------------------------------------------------------------
// <copyright file="ProcessorActor.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs.Primitives;

namespace Akka.Streams.Azure.EventHub.Examples.Multiple
{
    public class StreamProcessorActor: ReceiveActor
    {
        public static Props Props(bool batched)
            => Actor.Props.Create(() => new StreamProcessorActor(batched));
        
        private readonly ILoggingAdapter _log;
        private readonly bool _batched;
        private EventProcessor<EventProcessorPartition>? _processor;
        private readonly ConcurrentDictionary<string, int> _partitionEventCount; 
        
        public StreamProcessorActor(bool batched)
        {
            _batched = batched;
            _partitionEventCount = new ConcurrentDictionary<string, int>();
            _log = Context.GetLogger();

            Receive<Stop>(_ =>
            {
                _log.Debug("Stopping actor");
                Task.Run(() =>
                {
                    if (_processor == null)
                        return;
                    if (!_processor.StopProcessingAsync().Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("EventHub EventProcessor failed to stop");
                }).PipeTo(Self, Self, 
                    success: () => ProcessorStopped.Instance, 
                    failure: ex => new Status.Failure(ex));
            });

            Receive<ProcessorStopped>(_ => Context.Stop(Self));

            Receive<Status.Failure>(f =>
            {
                _log.Error(f.Cause, "Stream Actor failed, stopping.");
                Context.Stop(Self);
            });
        }

        protected override void PreStart()
        {
            if (_batched)
            {
                var factory = new ProcessorFactory.BatchedEventProcessorFactory();
                _processor = BatchedEventHubSource.Create(factory)
                    .SelectAsync(1, async t =>
                    {
                        _log.Debug("Message from Partition: {0}", t.Partition.PartitionId);
                        await Business(t);
                        return Done.Instance;
                    })
                    .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
                    .Run(Context.System.Materializer());
            }
            else
            {
                var factory = new ProcessorFactory.EventProcessorFactory();
                _processor = EventHubSource.Create(factory)
                    .SelectAsync(1, async t =>
                    {
                        _log.Debug("Message from Partition: {0}", t.Partition.PartitionId);
                        await Business(t);
                        return Done.Instance;
                    })
                    .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
                    .Run(Context.System.Materializer());
            }
            
            _log.Debug("Actor started");
        }

        protected override void PostStop()
        {
            _log.Debug("Actor stopped");
        }

        private async Task Business(ProcessContext context)
        {
            if (context.Data == null)
            {
                _log.Debug("Empty event received from Partition: {0}", context.Partition.PartitionId);
                return;
            }
            
            var body = Encoding.UTF8.GetString(context.Data.Body.Span);
            _log.Info("Processing message: {0}", body);
            
            var totalEvents = _partitionEventCount.AddOrUpdate(
                key: context.Partition.PartitionId,
                addValue: 1,
                updateValueFactory: (_, currentCount) => currentCount + 1);

            if (totalEvents % 50 == 0)
            {
                _log.Info("Updating checkpoint for partition [{0}]. Total events: [{1}]", context.Partition.PartitionId, totalEvents);
                await context.UpdateCheckpointAsync();
            }
        }

        private async Task Business(BatchProcessContext context)
        {
            if (context.Data == null)
            {
                _log.Debug("Empty batch received from Partition: {0}", context.Partition.PartitionId);
                return;
            }
            
            foreach (var data in context.Data)
            {
                var body = Encoding.UTF8.GetString(data.Body.Span);
                
                _log.Info($"Processing message: {body}");
            }

            _log.Info("Updating checkpoint for partition [{0}]. Total events: [{1}]", context.Partition.PartitionId, context.Data.Count);
            await context.UpdateCheckpointAsync();
        }
    }
}