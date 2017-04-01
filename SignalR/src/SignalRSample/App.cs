using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;

namespace SignalRSample
{
    public static class App
    {
        public static ActorSystem System;
        public static IMaterializer Materializer;
        
    }
}