using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Xml.Tests.Dsl;

public abstract class XmlSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    protected async Task<T> CompletedWithin<T>(Task<T> task)
    {
        await AwaitAssertAsync(() => Assert.True(task.IsCompleted), TimeSpan.FromSeconds(3));
        return await task;
    }
}
