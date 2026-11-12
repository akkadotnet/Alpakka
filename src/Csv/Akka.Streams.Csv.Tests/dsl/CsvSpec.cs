using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.Streams.Csv.Tests.dsl
{
    public abstract class CsvSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected CsvSpec(ITestOutputHelper output) : base(output: output)
        {
            Materializer = Sys.Materializer();
        }

        protected async Task<T> CompletedWithin<T>(Task<T> task)
        {
            await AwaitAssertAsync(() => Assert.True(task.IsCompleted), TimeSpan.FromSeconds(3));
            return await task;
        }

        protected override void AfterAll()
        {
            Shutdown(Sys);
        }
    }
}
