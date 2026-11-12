using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.File.Tests;

public sealed class LogRotatorLifecycleSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_or_canceled_final_sink_fails_the_materialized_task(bool cancel)
    {
        var consumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Source.Single("record").RunWith(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(
            value => Option<string>.Create(value),
            _ => Sink.ForEach<string>(_ => consumed.TrySetResult(true)).MapMaterializedValue(_ => finish.Task)), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(consumed.Task.IsCompleted));
        Assert.False(completion.IsCompleted);
        var failure = new InvalidOperationException("final sink failed");
        if (cancel) finish.SetCanceled(); else finish.SetException(failure);
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await completion);
        else Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await completion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Early_current_sink_completion_still_waits_for_every_older_sink(bool failOlder)
    {
        var consumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (source, completion) = this.SourceProbe<string>()
            .ToMaterialized(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(value => Option<string>.Create(value), key =>
                key == "first"
                    ? Sink.ForEach<string>(_ => consumed.TrySetResult(true)).MapMaterializedValue(_ => older.Task)
                    : Sink.First<string>().MapMaterializedValue(async first => { await first; return Done.Instance; })), Keep.Both).Run(Sys.Materializer());
        try
        {
            source.SendNext("first");
            await AwaitAssertAsync(() => Assert.True(consumed.Task.IsCompleted));
            source.SendNext("second");
            source.ExpectCancellation();
            Assert.False(completion.IsCompleted);
            var failure = new InvalidOperationException("older sink failed");
            if (failOlder) older.SetException(failure); else older.SetResult(Done.Instance);
            await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
            if (failOlder) Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await completion));
            else Assert.Equal(Done.Instance, await completion);
        }
        finally { older.TrySetResult(Done.Instance); }
    }

    [Fact]

    public async Task A_current_sink_that_cancels_before_its_first_pull_completes_successfully()
    {
        var completion = Source.Single("first").RunWith(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(
            value => Option<string>.Create(value), _ => Sink.Cancelled<string>().MapMaterializedValue(_ => Task.FromResult(Done.Instance))), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        Assert.Equal(Done.Instance, await completion);
    }

    [Fact]
    public async Task A_rotated_factory_failure_terminates_the_previous_child()
    {
        var firstConsumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Done> firstCompletion = null;
        var failure = new InvalidOperationException("second factory failed");
        var (source, completion) = this.SourceProbe<string>()
            .ToMaterialized(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(value => Option<string>.Create(value), key =>
            {
                if (key == "second") throw failure;
                return Sink.ForEach<string>(_ => firstConsumed.TrySetResult(true)).MapMaterializedValue(task => firstCompletion = task);
            }), Keep.Both).Run(Sys.Materializer());
        source.SendNext("first");
        await AwaitAssertAsync(() => Assert.True(firstConsumed.Task.IsCompleted));
        source.SendNext("second");
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted && firstCompletion.IsCompleted));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await completion));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await firstCompletion));
    }

    [Fact]
    public async Task Materializer_shutdown_fails_a_pending_rotation_task()
    {
        var consumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Source.Single("first").RunWith(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(
            value => Option<string>.Create(value), _ => Sink.ForEach<string>(_ => consumed.TrySetResult(true)).MapMaterializedValue(_ => finish.Task)), Sys.Materializer());
        try
        {
            await AwaitAssertAsync(() => Assert.True(consumed.Task.IsCompleted));
            await Sys.Terminate();
            await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
            await Assert.ThrowsAsync<AbruptStageTerminationException>(async () => await completion);
        }
        finally { finish.TrySetResult(Done.Instance); }
    }

    [Fact]

    public async Task A_current_sink_that_completes_early_cancels_upstream_successfully()
    {
        var (source, completion) = this.SourceProbe<string>()
            .ToMaterialized(LogRotatorSink.WithTypedSinkFactory<string, string, string>(
                value => Option<string>.Create(value), _ => Sink.First<string>()), Keep.Both).Run(Sys.Materializer());
        source.SendNext("first");
        source.ExpectCancellation();
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        Assert.Equal(Done.Instance, await completion);
    }

    [Fact]

    public async Task Completing_a_rotated_sink_does_not_drop_other_sinks_or_stop_upstream()
    {
        var finishes = new ConcurrentDictionary<string, TaskCompletionSource<Done>>();
        var received = new ConcurrentQueue<string>();
        var (source, completion) = this.SourceProbe<string>()
            .ToMaterialized(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(
                value => Option<string>.Create(value),
                key =>
                {
                    var finish = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Assert.True(finishes.TryAdd(key, finish));
                    return Sink.ForEach<string>(received.Enqueue).MapMaterializedValue(_ => finish.Task);
                }), Keep.Both).Run(Sys.Materializer());
        try
        {
            source.SendNext("first");
            source.SendNext("second");
            await AwaitAssertAsync(() => Assert.Equal(new[] { "first", "second" }, received.ToArray()));
            finishes["first"].SetResult(Done.Instance);
            source.SendNext("third");
            await AwaitAssertAsync(() => Assert.Equal(new[] { "first", "second", "third" }, received.ToArray()));
            source.SendComplete();
            finishes["third"].SetResult(Done.Instance);
            await Task.Delay(100);
            Assert.False(completion.IsCompleted);
            finishes["second"].SetResult(Done.Instance);
            await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
            Assert.Equal(Done.Instance, await completion);
        }
        finally
        {
            foreach (var finish in finishes.Values) finish.TrySetResult(Done.Instance);
        }
    }

    [Fact]
    public async Task A_throwing_sink_factory_fails_the_materialized_task()
    {
        var failure = new InvalidOperationException("factory failed");
        var completion = Source.Single("record").RunWith(LogRotatorSink.WithTypedSinkFactory<string, string, Done>(
            value => Option<string>.Create(value), _ => throw failure), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await completion));
    }
}
