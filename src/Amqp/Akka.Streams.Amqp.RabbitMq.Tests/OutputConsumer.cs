// //-----------------------------------------------------------------------
// // <copyright file="OutputConsumer.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Configurations;

namespace Akka.Streams.Amqp.Tests;

public static class OutputConsumerExtensions
{
    /// <summary>
    ///     Waits for marker strings to be found inside the standard output stream of a running Docker container.
    ///     The marker string is the last output string emitted by the Docker image that marks it as ready and running.
    /// </summary>
    /// <param name="output">
    ///     The output stream, usually an instance of <see cref="OutputConsumer"/>
    /// </param>
    /// <param name="markerString">
    ///     The marker string we're waiting for
    /// </param>
    /// <param name="repeats">
    ///     How many times the marker string should appear before the Docker image is considered running
    /// </param>
    /// <param name="timeout">
    ///     How long we need to wait for the marker string before we timed out
    /// </param>
    /// <exception cref="TimeoutException">
    ///     Thrown when the marker string was not found inside the output stream within the timeout time window
    /// </exception>
    public static async Task WaitUntilReadyAsync(
        this IOutputConsumer output,
        string markerString,
        int repeats,
        TimeSpan timeout)
    {
        var found = 0;
        using var reader = new StreamReader(output.Stdout);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var lastPosition = 0L;
            var complete = false;
            while (!complete)
            {
                await output.Stdout.FlushAsync(cts.Token);
                
                output.Stdout.Position = lastPosition;
                do
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                        break;
                    
                    lastPosition = output.Stdout.Position;
                    if (line.Contains(markerString))
                        found++;
                    complete = found >= repeats;
                } while (!complete);
                output.Stdout.Position = output.Stdout.Length;
                
                await Task.Delay(30, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Timed out waiting for container to be ready (5 minutes)");
        }
    }
}

/// <summary>
/// TODO: Should probably put this into a shared library for internal use
/// </summary>
public sealed class OutputConsumer: IOutputConsumer, IAsyncDisposable
{
    public OutputConsumer()
    {
    }

    public bool Enabled => true;
    public Stream Stdout { get; } = new MemoryStream();
    public Stream Stderr { get; } = new MemoryStream();

    public void Dispose()
    {
        Stdout.Dispose();
        Stderr.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        #if NET5_0_OR_GREATER
        await Stdout.DisposeAsync();
        await Stderr.DisposeAsync();
        #else
        Stdout.Dispose();
        Stderr.Dispose();
        #endif
    }
}