//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Streams.Azure.EventHub.Examples.Multiple;
using Akka.Streams.Azure.EventHub.Examples.Single;

namespace Akka.Streams.Azure.EventHub.Examples
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // Uncomment the sample you wish to run
            
            // Event processing using one processor to process all partitions
            // await SingleProcessorExample.Run(batched: false);
            
            // Batched event processing using one processor to process all partitions
            // await SingleProcessorExample.Run(batched: true);
            
            // Event processing using multiple processor to process multiple partitions in parallel
            await MultiProcessorExample.Run(batched: false);
            
            // Batch event processing using multiple processor to process multiple partitions in parallel
            // await MultiProcessorExample.Run(batched: true);
        }
    }
}