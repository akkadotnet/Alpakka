//-----------------------------------------------------------------------
// <copyright file="GuardianActor.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace Akka.Streams.Azure.EventHub.Examples.Multiple
{
    public class EventHubProcessorActor: ReceiveActor
    {
        public static Props Props(int processorCount, bool batched)
            => Actor.Props.Create(() => new EventHubProcessorActor(processorCount, batched));

        private readonly int _processorCount;
        private readonly bool _batched;
        private bool _started;
        private readonly List<IActorRef> _children = new List<IActorRef>();
        private readonly ILoggingAdapter _log;
        private IActorRef? _stopRequester;

        public EventHubProcessorActor(int processorCount, bool batched)
        {
            _processorCount = processorCount;
            _batched = batched;
            _log = Context.GetLogger();

            Receive<Start>(_ =>
            {
                if (_started)
                    return;
                _started = true;
                
                _log.Info("Starting {0} EventHubs processor workers. Batched: {1}", _processorCount, _batched);
                for (var i = 0; i < _processorCount; ++i)
                {
                    var child = Context.ActorOf(StreamProcessorActor.Props(_batched), $"processor-{i}");
                    Context.Watch(child);
                    _children.Add(child);
                }
            });

            Receive<Stop>(_ =>
            {
                _log.Info("Stopping all EventHubs processor workers");
                _stopRequester = Sender;
                foreach (var child in _children)
                {
                    child.Tell(Stop.Instance);
                }
            });

            Receive<Terminated>(msg =>
            {
                if (_children.Contains(msg.ActorRef))
                    _children.Remove(msg.ActorRef);

                if (_children.Count == 0)
                {
                    _log.Info("All process workers have stopped. Stopping.");
                    _stopRequester.Tell(Stopped.Instance);
                    Context.Stop(Self);
                }
            });
        }
    }
}