//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Azure.Messaging.EventHubs;

namespace Akka.Streams.Azure.EventHub
{
    public static class Extensions
    {
        /// <summary>
        ///   Throws an exception of the requested type if cancellation has been requested
        ///   of the <paramref name="instance" />.
        /// </summary>
        ///
        /// <typeparam name="T">The type of exception to throw; the type must have a parameterless constructor.</typeparam>
        ///
        /// <param name="instance">The instance that this method was invoked on.</param>
        ///
        public static void ThrowIfCancellationRequested<T>(this CancellationToken instance) where T : Exception, new()
        {
            if (instance.IsCancellationRequested)
            {
                throw new T();
            }
        }
        
        /// <summary>
        ///   Creates a new copy of the current <see cref="EventHubConnectionOptions" />, cloning its attributes into a new instance.
        /// </summary>
        ///
        /// <param name="instance">The instance that this method was invoked on.</param>
        ///
        /// <returns>A new copy of <see cref="EventHubConnectionOptions" />.</returns>
        ///
        public static EventHubConnectionOptions Clone(this EventHubConnectionOptions instance) =>
            new EventHubConnectionOptions
            {
                TransportType = instance.TransportType,
                ConnectionIdleTimeout = instance.ConnectionIdleTimeout,
                Proxy = instance.Proxy,
                CustomEndpointAddress = instance.CustomEndpointAddress,
                SendBufferSizeInBytes = instance.SendBufferSizeInBytes,
                ReceiveBufferSizeInBytes = instance.ReceiveBufferSizeInBytes,
                CertificateValidationCallback = instance.CertificateValidationCallback
            };

        /// <summary>
        ///   Creates a new copy of the current <see cref="EventHubsRetryOptions" />, cloning its attributes into a new instance.
        /// </summary>
        ///
        /// <param name="instance">The instance that this method was invoked on.</param>
        ///
        /// <returns>A new copy of <see cref="EventHubsRetryOptions" />.</returns>
        ///
        public static EventHubsRetryOptions Clone(this EventHubsRetryOptions instance) =>
            new EventHubsRetryOptions
            {
                Mode = instance.Mode,
                CustomRetryPolicy = instance.CustomRetryPolicy,
                MaximumRetries = instance.MaximumRetries,
                Delay = instance.Delay,
                MaximumDelay = instance.MaximumDelay,
                TryTimeout = instance.TryTimeout
            };
        
    }
}