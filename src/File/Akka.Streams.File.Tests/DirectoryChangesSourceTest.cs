// -----------------------------------------------------------------------
//  <copyright file="DirectoryChangesSourceSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;
using Akka.Streams.Dsl;
using Akka.Streams.File.Impl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.File.Tests
{
    public class DirectoryChangesSourceSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly string _tempDir;

        public DirectoryChangesSourceSpec(ITestOutputHelper output)
            : base(output: output)
        {
            _materializer = Sys.Materializer();
            _tempDir = GetTmpDirectory();
        }

        [Fact]
        public void DirectoryChangesSource_should_emit_on_directory_changes()
        {
            var probe = this.CreateSubscriberProbe<(string, DirectoryChange)>();

            DirectoryChangesSource.Create(_tempDir, 200)
                .RunWith(Sink.FromSubscriber(probe), _materializer);

            probe.Request(1);

            var createdFile = System.IO.File.Create(Path.Combine(_tempDir, "test1file1.sample"));
            var pair1 = probe.ExpectNext();
            pair1.Item2.Should().Be(DirectoryChange.Creation);
            pair1.Item1.Should().Be(createdFile.Name);

            // Flush to disk after writing, otherwise FileSystemWatcher won't pick up the changes
            createdFile.Write(Encoding.UTF8.GetBytes("Some data"));
            createdFile.Flush(flushToDisk: true);

            var pair2 = probe.RequestNext();
            pair2.Item2.Should().Be(DirectoryChange.Modification);
            pair2.Item1.Should().Be(createdFile.Name);

            // Close file before attempting to delete it
            createdFile.Close();
            System.IO.File.Delete(createdFile.Name);

            // HACK: events can be raised more than once, see: https://github.com/dotnet/runtime/issues/24079
            (string, DirectoryChange) pair3;
            do
            {
                pair3 = probe.RequestNext();
            } while (!(pair3.Item2 is DirectoryChange.Deletion));

            pair3.Item2.Should().Be(DirectoryChange.Deletion);
            pair3.Item1.Should().Be(createdFile.Name);

            probe.Cancel();
        }

        [Fact]
        public void DirectoryChangesSource_should_emit_multiple_changes()
        {
            const int numberOfChanges = 50;
            var probe = this.CreateSubscriberProbe<(string, DirectoryChange)>();

            DirectoryChangesSource.Create(_tempDir, numberOfChanges * 2)
                .RunWith(Sink.FromSubscriber(probe), _materializer);

            probe.Request(numberOfChanges);

            var halfRequested = numberOfChanges / 2;
            var files = new List<FileStream>();

            for (var i = 0; i < halfRequested; i++)
            {
                var file = System.IO.File.Create(Path.Combine(_tempDir, "test2files" + i));
                files.Add(file);
            }

            for (var i = 0; i < halfRequested; i++)
                probe.ExpectNext();

            for (var i = 0; i < halfRequested; i++)
            {
                var file = files[i];
                file.Close();
                System.IO.File.Delete(file.Name);
            }

            for (var i = 0; i < halfRequested; i++)
                probe.ExpectNext();

            probe.Cancel();
        }

        protected override void AfterAll()
        {
            Directory.Delete(_tempDir, true);
            base.AfterAll();
        }

        private string GetTmpDirectory()
        {
            string tmpDirectory;

            do
            {
                tmpDirectory = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));
            } while (Directory.Exists(tmpDirectory));

            Directory.CreateDirectory(tmpDirectory);
            return tmpDirectory;
        }
    }
}
