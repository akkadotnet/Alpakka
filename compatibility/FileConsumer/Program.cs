using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.File;
using Akka.Streams.File.Impl;
using Akka.Util;

internal static class Program
{
    private static async Task Main()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "alpakka-old-file-" + Guid.NewGuid().ToString("N"))).FullName;
        var system = ActorSystem.Create("old-file-consumer");
        try
        {
            var path = Path.Combine(directory, "output.log");
            var opened = false;
            Sink<ByteString, Task<Done>> sink = LogRotatorSink.Create(_ =>
            {
                if (opened) return Option<string>.None;
                opened = true;
                return Option<string>.Create(path);
            }, FileMode.Create);
            await Source.From(new[] { ByteString.FromString("first\n"), ByteString.FromString("café\n") })
                .RunWith(sink, system.Materializer());
            if (System.IO.File.ReadAllText(path, Encoding.UTF8) != "first\ncafé\n")
                throw new InvalidOperationException("Retained file output changed.");
            Source<(string, DirectoryChange), NotUsed> source = DirectoryChangesSource.Create(directory, 100);
            Sink<ByteString, Task<Done>> custom = LogRotatorSink.WithSinkFactory<Done, Done>(_ => Option<Done>.Create(Done.Instance), _ => Sink.Ignore<ByteString>());
            Sink<string, Task<Done>> typed = LogRotatorSink.WithTypedSinkFactory<string, Done, Done>(_ => Option<Done>.Create(Done.Instance), _ => Sink.Ignore<string>());
            if (source is null || custom is null || typed is null || (int)DirectoryChange.Modification != 0
                || (int)DirectoryChange.Creation != 1 || (int)DirectoryChange.Deletion != 2)
                throw new InvalidOperationException("Retained public factories/enum changed.");
            Console.WriteLine("Previously compiled File consumer passed (real UTF-8 file output and retained factories).");
        }
        finally
        {
            await system.Terminate();
            Directory.Delete(directory, true);
        }
    }
}
