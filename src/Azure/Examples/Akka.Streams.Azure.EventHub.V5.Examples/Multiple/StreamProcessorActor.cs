// //-----------------------------------------------------------------------
// // <copyright file="ProcessorActor.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;

namespace Akka.Streams.Azure.EventHub.V5.Examples.Multiple
{
    internal sealed class ProcessorStopped
    {
        public static readonly ProcessorStopped Instance = new ProcessorStopped();
        private ProcessorStopped() {}
    }
    
    public class StreamProcessorActor: ReceiveActor
    {
        private readonly ILoggingAdapter _log;
        private EventProcessorClient _processor;
        private readonly ConcurrentDictionary<string, int> _partitionEventCount; 
        
        public StreamProcessorActor()
        {
            _partitionEventCount = new ConcurrentDictionary<string, int>();
            _log = Context.GetLogger();

            Receive<Stop>(_ =>
            {
                _processor.StopProcessingAsync()
                    .PipeTo(Self, Self, success: () => ProcessorStopped.Instance);
            });

            Receive<ProcessorStopped>(_ => Context.Stop(Self));
        }

        protected override void PreStart()
        {
            var factory = new ProcessorFactory.EventProcessorFactory();
            _processor = Source.FromGraph(new EventHubSource(factory))
                .SelectAsync(1, async t =>
                {
                    _log.Debug("Message from Partition: {0}", t.Partition.PartitionId);
                    await Business(t);
                    return Done.Instance;
                })
                .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
                .Run(Context.System.Materializer());
        }
        
        private async Task Business(EventHubSource.ProcessContext context)
        {
            var body = Encoding.UTF8.GetString(context.Event.Body.Span);
            _log.Info("Processing message: {0}", body);
            
            var totalEvents = _partitionEventCount.AddOrUpdate(
                key: context.Partition.PartitionId,
                addValue: 1,
                updateValueFactory: (_, currentCount) => currentCount + 1);

            if (totalEvents % 50 == 0)
            {
                _log.Info("Updating checkpoint for partition [{0}]. Total events: [{1}]", context.Partition, totalEvents);
                await context.UpdateCheckpointAsync();
            }
        }

    }
}