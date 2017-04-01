using Microsoft.Owin.Testing;

namespace Akka.Streams.SignalR.Tests.Internals
{
    internal static class TestHelpers
    {
        public static TestServer CreateServer()
        {
            return TestServer.Create<Startup>();
        }
    }
}