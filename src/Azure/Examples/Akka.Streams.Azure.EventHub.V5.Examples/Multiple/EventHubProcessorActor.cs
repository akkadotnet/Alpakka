// //-----------------------------------------------------------------------
// // <copyright file="GuardianActor.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace Akka.Streams.Azure.EventHub.V5.Examples.Multiple
{
    public sealed class Start
    {
        public static readonly Start Instance = new Start();
        private Start(){}
    }

    public sealed class Stop
    {
        public static readonly Stop Instance = new Stop();
        private Stop(){}
    }
    
    public sealed class Stopped
    {
        public static readonly Stopped Instance = new Stopped();
        private Stopped(){}
    }
    
    public class EventHubProcessorActor: ReceiveActor
    {
        public static Props Props(int processorCount)
            => Actor.Props.Create(() => new EventHubProcessorActor(processorCount));

        private readonly int _processorCount;
        private bool _started;
        private readonly List<IActorRef> _children = new List<IActorRef>();
        private readonly ILoggingAdapter _log;
        private IActorRef _stopRequester;

        public EventHubProcessorActor(int processorCount)
        {
            _processorCount = processorCount;
            _log = Context.GetLogger();

            Receive<Start>(_ =>
            {
                if (_started)
                    return;
                _started = true;
                
                _log.Info("Starting {0} EventHubs processor workers", _processorCount);
                for (var i = 0; i < _processorCount; ++i)
                {
                    var child = Context.ActorOf(Actor.Props.Create(() => new StreamProcessorActor()), $"processor-{i}");
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