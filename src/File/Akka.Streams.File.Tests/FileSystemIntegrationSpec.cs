using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.File.Impl;
using Akka.Streams.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.File.Tests;

public sealed class FileSystemIntegrationSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "alpakka-file-" + Guid.NewGuid().ToString("N"))).FullName;

    [Theory]
    [InlineData(FileMode.Create)]
    [InlineData(FileMode.Append)]
    [InlineData(FileMode.OpenOrCreate)]
    public async Task Rotation_preserves_utf8_bytes_file_modes_and_closes_every_file(FileMode mode)
    {
        var payloads = new[] { "a", "café", "last" };
        var paths = Enumerable.Range(0, payloads.Length).Select(i => Path.Combine(_directory, i + ".log")).ToArray();
        foreach (var path in paths) System.IO.File.WriteAllText(path, "old", new UTF8Encoding(false));
        var index = 0;
        var completion = Source.From(payloads.Select(ByteString.FromString))
            .RunWith(LogRotatorSink.Create(_ => Option<string>.Create(paths[index++]), mode), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        await completion;
        Assert.Equal(payloads.Length, index);
        for (var i = 0; i < paths.Length; i++)
        {
            var expected = mode == FileMode.Append ? "old" + payloads[i]
                : mode == FileMode.OpenOrCreate && i == 0 ? "ald" : payloads[i];
            Assert.Equal(Encoding.UTF8.GetBytes(expected), System.IO.File.ReadAllBytes(paths[i]));
            using var exclusive = new FileStream(paths[i], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    [Fact]
    public async Task A_real_watcher_reports_rename_and_cancellation_terminates_the_source()
    {
        var (terminated, probe) = DirectoryChangesSource.Create(_directory, 100)
            .WatchTermination((_, done) => done)
            .ToMaterialized(this.SinkProbe<(string, DirectoryChange)>(), Keep.Both).Run(Sys.Materializer());
        probe.EnsureSubscription().Request(100);
        var original = Path.Combine(_directory, "original");
        var renamed = Path.Combine(_directory, "renamed");
        System.IO.File.WriteAllText(original, "payload");
        ExpectChange(probe, original, DirectoryChange.Creation);
        System.IO.File.Move(original, renamed);
        ExpectChange(probe, renamed, DirectoryChange.Modification);
        probe.Cancel();
        await AwaitAssertAsync(() => Assert.True(terminated.IsCompleted));
        await terminated;
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void A_missing_watch_directory_fails_instead_of_silently_completing()
    {
        var absent = Path.Combine(_directory, "absent");
        Assert.Throws<DirectoryNotFoundException>(() =>
        {
            _ = DirectoryChangesSource.Create(absent, 10)
                .RunWith(Sink.Ignore<(string, DirectoryChange)>(), Sys.Materializer());
        });
    }

#if NET10_0
    [Fact]
    [Trait("Platform", "Linux")]
    public async Task Cancellation_releases_the_native_inotify_watch_for_the_directory()
    {
        Assert.True(OperatingSystem.IsLinux(), "Run this Linux-only native resource check with the Linux platform filter.");
        using var stat = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("stat")
        {
            ArgumentList = { "-c", "%i", "--", _directory },
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
        var inodeText = await stat.StandardOutput.ReadToEndAsync(deadline.Token);
        await stat.WaitForExitAsync(deadline.Token);
        Assert.Equal(0, stat.ExitCode);
        var inode = ulong.Parse(inodeText.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var marker = "ino:" + inode.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + " ";
        bool HasNativeWatch()
        {
            foreach (var file in Directory.EnumerateFiles("/proc/self/fdinfo"))
            {
                string text;
                try { text = System.IO.File.ReadAllText(file); }
                catch (FileNotFoundException) { continue; } // Other runtime descriptors may close during enumeration.
                if (text.Contains(marker, StringComparison.Ordinal)) return true;
            }
            return false;
        }
        Assert.False(HasNativeWatch());
        var (terminated, probe) = DirectoryChangesSource.Create(_directory, 100)
            .WatchTermination((_, done) => done)
            .ToMaterialized(this.SinkProbe<(string, DirectoryChange)>(), Keep.Both).Run(Sys.Materializer());
        probe.EnsureSubscription();
        await AwaitAssertAsync(() => Assert.True(HasNativeWatch()));
        probe.Cancel();
        await AwaitAssertAsync(() => Assert.True(terminated.IsCompleted));
        await terminated;
        await AwaitAssertAsync(() => Assert.False(HasNativeWatch()));
    }
#endif

    private void ExpectChange
(TestSubscriber.Probe<(string, DirectoryChange)> probe, string path, DirectoryChange change)
    {
        Within(TimeSpan.FromSeconds(3), () =>
        {
            while (true)
            {
                var actual = probe.ExpectNext();
                if (actual.Item1 == path && actual.Item2 == change) return;
            }
        });
    }

    protected override void AfterAll()
    {
        try { base.AfterAll(); }
        finally { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
