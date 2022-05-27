//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Streams.Azure.EventHub.Examples.Multiple
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

    public sealed class ProcessorStopped
    {
        public static readonly ProcessorStopped Instance = new ProcessorStopped();
        private ProcessorStopped() {}
    }

    
}