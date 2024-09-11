//-----------------------------------------------------------------------
// <copyright file="ProcessorFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Azure.Messaging.EventHubs.Primitives;

namespace Akka.Streams.Azure.EventHub
{
    public interface IProcessorFactory<out T> where T: EventProcessor<EventProcessorPartition>
    {
        public T CreateProcessor();
    }
}