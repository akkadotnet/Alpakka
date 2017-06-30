using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.Streams.Csv.Tests.dsl
{
    public abstract class CsvSpec:Akka.TestKit.Xunit.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected CsvSpec(ITestOutputHelper output) : base(output: output)
        {
            Materializer = Sys.Materializer();
        }

        protected override void AfterAll()
        {
            Shutdown(Sys);
        }
    }
}
