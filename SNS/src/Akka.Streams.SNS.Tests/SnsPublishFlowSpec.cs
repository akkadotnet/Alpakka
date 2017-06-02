using Xunit.Abstractions;

namespace Akka.Streams.SignalR.Tests
{
    public class SnsPublishFlowSpec: Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer materializer;

        public SnsPublishFlowSpec(ITestOutputHelper output)
            : base(output: output)
        {
            materializer = Sys.Materializer();
        }

        
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            materializer.Dispose();
        }
        }
}