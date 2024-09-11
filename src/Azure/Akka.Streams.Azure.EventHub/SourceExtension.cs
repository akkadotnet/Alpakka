//-----------------------------------------------------------------------
// <copyright file="SourceExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

namespace Akka.Streams.Azure.EventHub
{
    public static class SourceExtension
    {
        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a <see cref="EventHubSink"/>.
        /// The returned <see cref="Task"/> will be completed with Success when reaching the
        /// normal end of the stream, or completed with Failure if there is a failure signaled in the stream.
        /// </summary>
        public static Task ToEventHub<TMat>(this Source<IEnumerable<EventData>, TMat> source, EventHubProducerClient producer, IMaterializer materializer)
        {
            return source.RunWith(new EventHubSink(producer), materializer);
        }
    }
}
