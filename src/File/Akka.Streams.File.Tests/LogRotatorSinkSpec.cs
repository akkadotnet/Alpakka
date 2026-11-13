// -----------------------------------------------------------------------
//  <copyright file="LogRotatorSinkSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.File.Tests
{
    public class LogRotatorSinkSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "alpakka-rotator-" + Guid.NewGuid().ToString("N"))).FullName;
        private readonly List<string> _testLines = new List<string>();
        private readonly List<ByteString> _testByteStrings;

        private (Func<ByteString, Option<string>>, Func<List<string>>) FileLengthTriggerCreator()
        {
            var files = new List<string>();
            const int max = 2002;
            var size = (long)max;

            Option<string> TestFunction(ByteString element)
            {
                if (size + element.Count > max)
                {
                    var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".log");
                    files.Add(path);
                    size = element.Count;
                    return path;
                }

                size += element.Count;
                return Option<string>.None;
            }

            List<string> ListFiles() => files;
            return (TestFunction, ListFiles);
        }

        public LogRotatorSinkSpec(ITestOutputHelper output)
            : base(config:"{akka.loglevel = DEBUG}", output: output)
        {
            _materializer = Sys.Materializer();

            foreach (var character in new[] { "a", "b", "c", "d", "e", "f" })
            {
                var line = "";
                for (var i = 0; i < 1000; i++)
                    line += character;
                // don't use Environment.NewLine - it can contain more than one byte length marker, 
                // causing tests to fail due to incorrect number of bytes in input string
                line += "\n";
                _testLines.Add(line);
            }

            _testByteStrings = _testLines.Select(ByteString.FromString).ToList();
        }

        [Fact]
        public async Task LogRotatorSink_must_complete_when_consuming_an_empty_source()
        {
            static Option<string> TriggerCreator(ByteString element) => throw new Exception("trigger creator should not be called");

            var rotatorSink = LogRotatorSink.Create(TriggerCreator);

            var completion = Source.Empty<ByteString>().RunWith(rotatorSink, _materializer);
            await ShouldCompleteWithin(completion, TimeSpan.FromSeconds(3));
            Assert.Equal(Done.Instance, await completion);
        }

        [Fact]
        public async Task LogRotatorSink_must_work_for_size_based_rotation()
        {
            const int max = 10 * 1024 * 1024;
            var size = (long)max;
            
            Option<string> FileSizeTriggerCreator(ByteString element)
            {
                if (size + element.Count > max)
                {
                    var path = Path.Combine(_directory, "out-" + DateTime.UtcNow.Ticks + ".log");
                    size = element.Count;
                    return path;
                }

                size += element.Count;
                return Option<string>.None;
            }

            var sizeRotatorSink = LogRotatorSink.Create(FileSizeTriggerCreator);
            var fileSizeCompletion = Source.From(new[] { "test1", "test2", "test3", "test4", "test5", "test6" })
                .Select(ByteString.FromString)
                .RunWith(sizeRotatorSink, _materializer);

            await ShouldCompleteWithin(fileSizeCompletion, TimeSpan.FromSeconds(3));
            Assert.Equal(Done.Instance, await fileSizeCompletion);
        }

        [Fact]
        public async Task LogRotatorSink_must_work_for_time_based_rotation()
        {
            var destinationDir = _directory;
            var currentFilename = Option<string>.None;

            Option<string> TimeBasedTriggerCreator(ByteString element)
            {
                var newName = $"stream-{DateTime.UtcNow:yyyy-MM-dd_HH}.log";
                if (currentFilename.HasValue && currentFilename.Value.Contains(newName))
                {
                    return Option<string>.None;
                }

                currentFilename = newName;
                return Option<string>.Create(Path.Combine(destinationDir, newName));
            }

            var timeBasedRotatorSink = LogRotatorSink.Create(TimeBasedTriggerCreator);
            var timeBaseCompletion = Source.From(new[] { "test1", "test2", "test3", "test4", "test5", "test6" })
                .Select(ByteString.FromString)
                .RunWith(timeBasedRotatorSink, _materializer);

            await ShouldCompleteWithin(timeBaseCompletion, TimeSpan.FromSeconds(3));
            Assert.Equal(Done.Instance, await timeBaseCompletion);
        }

        [Fact]
        public async Task LogRotatorSink_must_write_lines_to_a_single_file()
        {
            var files = new List<string>();
            string fileName = null;

            Option<string> TriggerFunctionCreator(ByteString element)
            {
                if (!string.IsNullOrEmpty(fileName))
                    return Option<string>.None;
                
                var path = Path.Combine(_directory, "test.log");
                files.Add(path);
                fileName = path;
                return path;
            }

            var completion = Source.From(_testByteStrings).RunWith(LogRotatorSink.Create(TriggerFunctionCreator), _materializer);
            await ShouldCompleteWithin(completion, TimeSpan.FromSeconds(3));

            var (contents, sizes) = await ReadUpFilesAndSizesThenClean(files);
            Assert.Equal(new long[] { 6006L }, sizes.OrderBy(x => x));
            Assert.Equal(new[] { string.Join("", _testLines) }, contents.OrderBy(x => x));
        }

        [Fact]
        public async Task LogRotatorSink_must_write_lines_to_multiple_files_due_to_fileSize()
        {
            var (triggerFunctionCreator, files) = FileLengthTriggerCreator();
            var completion = Source.From(_testByteStrings).RunWith(LogRotatorSink.Create(triggerFunctionCreator), _materializer);
            await ShouldCompleteWithin(completion, TimeSpan.FromSeconds(3));

            var (contents, sizes) = await ReadUpFilesAndSizesThenClean(files());
            Assert.Equal(new long[] { 2002L, 2002L, 2002L }, sizes.OrderBy(x => x));
            Assert.Equal(Slide(_testLines).Select(tuple => tuple.Item1 + tuple.Item2).OrderBy(x => x), contents.OrderBy(x => x));
        }

        [Fact]
        public async Task LogRotatorSink_must_correctly_close_sinks()
        {
            var test = Enumerable.Range(1, 3).Select(m => m.ToString()).ToList();
            var @out = ImmutableList<string>.Empty;

            void Add(ByteString e) => @out = @out.Add(e.ToString());

            var completion = Source.From(test.Select(ByteString.FromString))
                .RunWith(LogRotatorSink.WithSinkFactory(
                    _ => Option<Task>.Create(Task.CompletedTask),
                    _ => Flow.Create<ByteString>()
                        .ToMaterialized(new StrangeSlowSink<ByteString>(Add, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)), Keep.Right)), _materializer);

            await ShouldCompleteWithin(completion, TimeSpan.FromSeconds(3));
            Assert.Equal(test.OrderBy(x => x), @out.OrderBy(x => x));
        }

        [Fact]
        public async Task LogRotatorSink_must_upstream_fail_before_first_file_creation()
        {
            var (triggerFunctionCreator, files) = FileLengthTriggerCreator();
            var (probe, completion) = this.SourceProbe<ByteString>()
                .ToMaterialized(LogRotatorSink.Create(triggerFunctionCreator), Keep.Both)
                .Run(_materializer);

            var ex = new Exception("my-exception");
            probe.SendError(ex);

            var exception = await ShouldThrowWithin<Exception>(completion, TimeSpan.FromSeconds(3));
            Assert.Same(ex, exception);
            Assert.Equal(ex.GetType(), exception.GetType());
            Assert.Equal(ex.Message, exception.Message);
            Assert.Empty(files());
        }

        [Fact]
        public async Task LogRotatorSink_must_upstream_fail_after_first_file_creation()
        {
            var (triggerFunctionCreator, files) = FileLengthTriggerCreator();
            var (probe, completion) = this.SourceProbe<ByteString>()
                .ToMaterialized(LogRotatorSink.Create(triggerFunctionCreator), Keep.Both)
                .Run(_materializer);

            var ex = new Exception("my-exception");
            probe.SendNext(ByteString.FromString("test"));
            await AwaitAssertAsync(() => Assert.Single(Directory.GetFiles(_directory, "*.log")), TimeSpan.FromSeconds(3));
            probe.SendError(ex);

            var exception = await ShouldThrowWithin<Exception>(completion, TimeSpan.FromSeconds(3));
            Assert.Same(ex, exception);
            Assert.Equal(ex.GetType(), exception.GetType());
            Assert.Equal(ex.Message, exception.Message);
            Assert.Single(files());
            await ReadUpFilesAndSizesThenClean(files());
        }

        [Fact]
        public async Task LogRotatorSink_must_function_fail_on_path_creation()
        {
            var ex = new Exception("my-exception");
            Option<string> TriggerFunctionCreator(ByteString element) => throw ex;

            var (probe, completion) = this.SourceProbe<ByteString>()
                .ToMaterialized(LogRotatorSink.Create(TriggerFunctionCreator), Keep.Both)
                .Run(_materializer);

            probe.SendNext(ByteString.FromString("test"));

            var exception = await ShouldThrowWithin<Exception>(completion, TimeSpan.FromSeconds(3));
            Assert.Same(ex, exception);
            Assert.Equal(ex.GetType(), exception.GetType());
            Assert.Equal(ex.Message, exception.Message);
        }

        [Fact]
        public async Task LogRotatorSink_must_downstream_fail_on_file_write()
        {
            var path = Path.Combine(_directory, "out-" + DateTime.UtcNow.Ticks + ".log");
            Option<string> TriggerFunctionCreator(ByteString element) => Option<string>.Create(path);

            var (probe, completion) = this.SourceProbe<ByteString>()
                .ToMaterialized(LogRotatorSink.Create(TriggerFunctionCreator, FileMode.Open), Keep.Both)
                .Run(_materializer);

            probe.SendNext(ByteString.FromString("test"));
            probe.SendNext(ByteString.FromString("test"));
            probe.ExpectCancellation();

            await ShouldThrowWithin<FileNotFoundException>(completion, TimeSpan.FromSeconds(3));
        }

        protected override void AfterAll()
        {
            try { base.AfterAll(); }
            finally { Directory.Delete(_directory, true); }
        }

        private static async Task<(List<string>, List<long>)> ReadUpFilesAndSizesThenClean(IEnumerable<string> files)
        {
            var (bytes, sizes) = await ReadUpFileBytesAndSizesThenClean(files);
            return (bytes.Select(b => b.ToString()).ToList(), sizes);
        }

        private static async Task<(List<ByteString>, List<long>)> ReadUpFileBytesAndSizesThenClean(IEnumerable<string> files)
        {
            var sizes = new List<long>();
            var data = new List<ByteString>();

            foreach (var path in files)
            {
                sizes.Add(new FileInfo(path).Length);
                var retry = 5;
                var success = false;
                while (!success)
                {
                    try
                    {
                        data.Add(ByteString.FromBytes(System.IO.File.ReadAllBytes(path)));
                    }
                    catch (IOException e)
                    {
                        retry--;
                        if (retry == 0)
                            throw new Exception($"Unable to read file [{path}] after 5 retries", e);
                        await Task.Delay(100);
                        continue;
                    }

                    success = true;
                }
                System.IO.File.Delete(path);
            }

            return (data, sizes);
        }

        private static IEnumerable<(T, T)> Slide<T>(IEnumerable<T> source)
        {
            using var iterator = source.GetEnumerator();
            while (iterator.MoveNext())
            {
                var first = iterator.Current;
                var second = iterator.MoveNext() ? iterator.Current : default;
                yield return (first, second);
            }
        }

        private static async Task ShouldCompleteWithin(Task task, TimeSpan timeout)
        {
            Assert.Same(task, await Task.WhenAny(task, Task.Delay(timeout)));
            await task;
        }

        private static async Task<T> ShouldThrowWithin<T>(Task task, TimeSpan timeout) where T : Exception
            => await Assert.ThrowsAsync<T>(async () => await ShouldCompleteWithin(task, timeout));

        private class StrangeSlowSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<Done>>
        {
            private readonly Action<T> _callback;
            private readonly TimeSpan _waitBeforePull;
            private readonly TimeSpan _waitAfterComplete;

            private Inlet<T> In { get; } = new Inlet<T>("StrangeSlowSink.In");
            public override SinkShape<T> Shape { get; }

            public StrangeSlowSink(Action<T> callback, TimeSpan waitBeforePull, TimeSpan waitAfterComplete)
            {
                _callback = callback;
                _waitBeforePull = waitBeforePull;
                _waitAfterComplete = waitAfterComplete;

                Shape = new SinkShape<T>(In);
            }

            public override ILogicAndMaterializedValue<Task<Done>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            {
                var logic = new StrangeSlowSinkLogic(this);
                return new LogicAndMaterializedValue<Task<Done>>(logic, logic.Task);
            }

            private sealed class StrangeSlowSinkLogic : GraphStageLogic
            {
                private readonly Action _startCallback;
                private readonly StrangeSlowSink<T> _sink;
                private readonly TaskCompletionSource<Done> _promise;

                public Task<Done> Task => _promise.Task;

                public override void PreStart()
                {
                    base.PreStart();
#pragma warning disable CS4014
                    // Task is intentionally not awaited
                    DelayedStart();
#pragma warning restore CS4014
                }

                public StrangeSlowSinkLogic(StrangeSlowSink<T> sink) : base(sink.Shape)
                {
                    _sink = sink;
                    _promise = new TaskCompletionSource<Done>();
                    _startCallback = GetAsyncCallback(() => Pull(_sink.In));

                    SetHandler(sink.In,
                        onPush: () =>
                        {
                            sink._callback(Grab(sink.In));
                            Pull(sink.In);
                        },
                        onUpstreamFinish: () =>
                        {
#pragma warning disable CS4014
                            // Task is intentionally not awaited
                            DelayedStop();
#pragma warning restore CS4014
                        });
                }

                private async Task DelayedStop()
                {
                    await System.Threading.Tasks.Task.Delay(_sink._waitAfterComplete);
                    _promise.TrySetResult(Done.Instance);
                }

                private async Task DelayedStart()
                {
                    await System.Threading.Tasks.Task.Delay(_sink._waitBeforePull);
                    _startCallback();
                }
            }
        }
    }
}
