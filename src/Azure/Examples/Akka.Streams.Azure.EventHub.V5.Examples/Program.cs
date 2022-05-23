// //-----------------------------------------------------------------------
// // <copyright file="Program.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Streams.Azure.EventHub.V5.Examples.Multiple;
using Akka.Streams.Azure.EventHub.V5.Examples.Single;

namespace Akka.Streams.Azure.EventHub.V5.Examples
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // Uncomment the sample you wish to run
            await SingleProcessorExample.Run();
            // await MultiProcessorExample.Run();
        }
    }
}