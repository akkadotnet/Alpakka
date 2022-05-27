//-----------------------------------------------------------------------
// <copyright file="BatchedEventProcessorClient.cs" company="Akka.NET Project">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;

namespace Akka.Streams.Azure.EventHub
{
    public class BatchedEventProcessorClient : EventProcessor<EventProcessorPartition>
    {
        private const string CannotCreateCheckpointForEmptyEvent =
            "A checkpoint cannot be created or updated using an empty event.";

        private const string CannotStartEventProcessorWithoutHandler =
            "Cannot begin processing without the {0} handler set.";

        private const string HandlerHasAlreadyBeenAssigned =
            "Another handler has already been assigned to this event and there can be only one.";

        private const string HandlerHasNotBeenAssigned = "This handler has not been previously assigned to this event.";

        private const string CannotReadLastEnqueuedEventPropertiesWithoutEvent =
            "The last enqueued event properties cannot be read when an event is not available.";

        private const string RunningEventProcessorCannotPerformOperation =
            "The event processor is already running and needs to be stopped in order to perform this operation.";

        
        /// <summary>The delegate to invoke when attempting to update a checkpoint using an empty event.</summary>
        private static readonly Func<CancellationToken, Task> EmptyEventUpdateCheckpoint = cancellationToken 
            => throw new InvalidOperationException(CannotCreateCheckpointForEmptyEvent);

        /// <summary>The set of default options for the processor.</summary>
        private static readonly EventProcessorClientOptions DefaultClientOptions = new EventProcessorClientOptions();

        /// <summary>The default starting position for the processor.</summary>
        private readonly EventPosition _defaultStartingPosition = new EventProcessorOptions().DefaultStartingPosition;

        /// <summary>
        /// The set of default starting positions for partitions being processed;
        /// these are collected at initialization and are surfaced as checkpoints to override defaults
        /// on a partition-specific basis.
        /// </summary>
        private readonly ConcurrentDictionary<string, EventPosition> _partitionStartingPositionDefaults 
            = new ConcurrentDictionary<string, EventPosition>();

        /// <summary>The primitive for synchronizing access during start and set handler operations.</summary>
        private readonly SemaphoreSlim _processorStatusGuard = new SemaphoreSlim(1, 1);

        /// <summary>The client provided to perform storage operations related to checkpoints and ownership.</summary>
        private readonly BlobContainerClient? _containerClient;

        /// <summary>The handler to be called just before event processing starts for a given partition.</summary>
        private Func<PartitionInitializingEventArgs, Task>? _partitionInitializingAsync;

        /// <summary>The handler to be called once event processing stops for a given partition.</summary>
        private Func<PartitionClosingEventArgs, Task>? _partitionClosingAsync;

        /// <summary>Responsible for processing events received from the Event Hubs service.</summary>
        private Func<ProcessEventBatchArgs, Task>? _processEventBatchAsync;

        /// <summary>Responsible for processing unhandled exceptions thrown while this processor is running.</summary>
        private Func<ProcessErrorEventArgs, Task>? _processErrorAsync;

        /// <summary>
        ///   Performs the tasks to initialize a partition, and its associated context, for event processing.
        ///
        ///   It is not recommended that the state of the processor be managed directly from within this method; requesting to start or stop the processor may result in a deadlock scenario, especially if using the synchronous form of the call.
        /// </summary>
        ///
        /// <exception cref="ArgumentException">If an attempt is made to remove a handler that doesn't match the current handler registered.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add or remove a handler while the processor is running.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add a handler when one is currently registered.</exception>
        ///
        public event Func<PartitionInitializingEventArgs, Task> PartitionInitializingAsync
        {
            add
            {
                Argument.AssertNotNull(value, nameof(PartitionInitializingAsync));

                if (_partitionInitializingAsync != default)
                    throw new NotSupportedException(HandlerHasAlreadyBeenAssigned);

                EnsureNotRunningAndInvoke(() => _partitionInitializingAsync = value);
            }

            remove
            {
                Argument.AssertNotNull(value, nameof(PartitionInitializingAsync));

                if (_partitionInitializingAsync != value)
                    throw new ArgumentException(HandlerHasNotBeenAssigned);

                EnsureNotRunningAndInvoke(() => _partitionInitializingAsync = default);
            }
        }

        /// <summary>
        ///   Performs the tasks needed when processing for a partition is being stopped.  This commonly occurs when the partition is claimed by another event processor instance or when
        ///   the current event processor instance is shutting down.
        ///
        ///   <para>It is not recommended that the state of the processor be managed directly from within this method; requesting to start or stop the processor may result in
        ///   a deadlock scenario, especially if using the synchronous form of the call.</para>
        /// </summary>
        ///
        /// <exception cref="ArgumentException">If an attempt is made to remove a handler that doesn't match the current handler registered.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add or remove a handler while the processor is running.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add a handler when one is currently registered.</exception>
        ///
        public event Func<PartitionClosingEventArgs, Task> PartitionClosingAsync
        {
            add
            {
                Argument.AssertNotNull(value, nameof(PartitionClosingAsync));

                if (_partitionClosingAsync != default)
                    throw new NotSupportedException(HandlerHasAlreadyBeenAssigned);

                EnsureNotRunningAndInvoke(() => _partitionClosingAsync = value);
            }

            remove
            {
                Argument.AssertNotNull(value, nameof(PartitionClosingAsync));

                if (_partitionClosingAsync != value)
                    throw new ArgumentException(HandlerHasNotBeenAssigned);

                EnsureNotRunningAndInvoke(() => _partitionClosingAsync = default);
            }
        }

        /// <summary>
        ///  Performs the tasks needed to process a batch of events for a given partition as they are read from the Event Hubs service. Implementation is mandatory.
        ///
        ///   Should an exception occur within the code for this handler, the <see cref="EventProcessorClient" /> will allow it to bubble and will not surface to the error handler or attempt to handle
        ///   it in any way.  Developers are strongly encouraged to take exception scenarios into account, including the need to retry processing, and guard against them using try/catch blocks and other means,
        ///   as appropriate.
        ///
        ///   It is not recommended that the state of the processor be managed directly from within this handler; requesting to start or stop the processor may result in
        ///   a deadlock scenario, especially if using the synchronous form of the call.
        /// </summary>
        ///
        /// <exception cref="ArgumentException">If an attempt is made to remove a handler that doesn't match the current handler registered.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add or remove a handler while the processor is running.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add a handler when one is currently registered.</exception>
        ///
        public event Func<ProcessEventArgs, Task> ProcessEventAsync
        {
            add
            {
                throw new ArgumentException("BatchedEventProcessorClient does not support single event processing, please use ProcessEventBatchAsync instead.");
            }

            remove
            {
                throw new ArgumentException("BatchedEventProcessorClient does not support single event processing, please use ProcessEventBatchAsync instead.");
            }
        }

        /// <summary>
        ///  Performs the tasks needed to process a batch of events for a given partition as they are read from the Event Hubs service. Implementation is mandatory.
        ///
        ///   Should an exception occur within the code for this handler, the <see cref="EventProcessorClient" /> will allow it to bubble and will not surface to the error handler or attempt to handle
        ///   it in any way.  Developers are strongly encouraged to take exception scenarios into account, including the need to retry processing, and guard against them using try/catch blocks and other means,
        ///   as appropriate.
        ///
        ///   It is not recommended that the state of the processor be managed directly from within this handler; requesting to start or stop the processor may result in
        ///   a deadlock scenario, especially if using the synchronous form of the call.
        /// </summary>
        ///
        /// <exception cref="ArgumentException">If an attempt is made to remove a handler that doesn't match the current handler registered.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add or remove a handler while the processor is running.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add a handler when one is currently registered.</exception>
        ///
        public event Func<ProcessEventBatchArgs, Task> ProcessEventBatchAsync
        {
            add
            {
                Argument.AssertNotNull(value, nameof(ProcessEventBatchAsync));

                if (_processEventBatchAsync != default)
                    throw new NotSupportedException(HandlerHasAlreadyBeenAssigned);

                EnsureNotRunningAndInvoke(() => _processEventBatchAsync = value);
            }

            remove
            {
                Argument.AssertNotNull(value, nameof(ProcessEventBatchAsync));

                if (_processEventBatchAsync != value)
                    throw new ArgumentException(HandlerHasNotBeenAssigned);

                EnsureNotRunningAndInvoke(() => _processEventBatchAsync = default);
            }
        }
        
        /// <summary>
        ///   Performs the tasks needed when an unexpected exception occurs within the operation of the event processor infrastructure.  Implementation is mandatory.
        ///
        ///   This error handler is invoked when there is an exception observed within the <see cref="EventProcessorClient" /> itself; it is not invoked for exceptions in
        ///   code that has been implemented to process events or other event handlers and extension points that execute developer code.  The <see cref="EventProcessorClient" /> will
        ///   make every effort to recover from exceptions and continue processing.  Should an exception that cannot be recovered from be encountered, the processor will attempt to forfeit
        ///   ownership of all partitions that it was processing so that work may be redistributed.
        ///
        ///   The exceptions surfaced to this method may be fatal or non-fatal; because the processor may not be able to accurately predict whether an
        ///   exception was fatal or whether its state was corrupted, this method has responsibility for making the determination as to whether processing
        ///   should be terminated or restarted.  The method may do so by calling Stop on the processor instance and then, if desired, calling Start on the processor.
        ///
        ///   It is recommended that, for production scenarios, the decision be made by considering observations made by this error handler, the method invoked
        ///   when initializing processing for a partition, and the method invoked when processing for a partition is stopped.  Many developers will also include
        ///   data from their monitoring platforms in this decision as well.
        ///
        ///   As with event processing, should an exception occur in the code for the error handler, the event processor will allow it to bubble and will not attempt to handle
        ///   it in any way.  Developers are strongly encouraged to take exception scenarios into account and guard against them using try/catch blocks and other means as appropriate.
        /// </summary>
        ///
        /// <exception cref="ArgumentException">If an attempt is made to remove a handler that doesn't match the current handler registered.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add or remove a handler while the processor is running.</exception>
        /// <exception cref="NotSupportedException">If an attempt is made to add a handler when one is currently registered.</exception>
        ///
        public event Func<ProcessErrorEventArgs, Task> ProcessErrorAsync
        {
            add
            {
                Argument.AssertNotNull(value, nameof(ProcessErrorAsync));

                if (_processErrorAsync != default)
                    throw new NotSupportedException(HandlerHasAlreadyBeenAssigned);

                EnsureNotRunningAndInvoke(() => _processErrorAsync = value);
            }

            remove
            {
                Argument.AssertNotNull(value, nameof(ProcessErrorAsync));

                if (_processErrorAsync != value)
                    throw new ArgumentException(HandlerHasNotBeenAssigned);

                EnsureNotRunningAndInvoke(() => _processErrorAsync = default);
            }
        }

        /// <summary>
        ///   Responsible for creation of checkpoints and for ownership claim.
        /// </summary>
        ///
        private CheckpointStore CheckpointStore { get; }

        #region constructors

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">The client responsible for persisting checkpoints and processor state to durable storage. The associated container is expected to exist.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="connectionString">The connection string to use for connecting to the Event Hubs namespace; it is expected that the Event Hub name and the shared key properties are contained in this connection string.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   <para>The container associated with the <paramref name="checkpointStore" /> is expected to exist; the <see cref="EventProcessorClient" />
        ///   does not assume the ability to manage the storage account and is safe to run with only read/write permission for blobs in the container.</para>
        ///
        ///   <para>If the connection string is copied from the Event Hubs namespace, it will likely not contain the name of the desired Event Hub,
        ///   which is needed.  In this case, the name can be added manually by adding ";EntityPath=[[ EVENT HUB NAME ]]" to the end of the
        ///   connection string.  For example, ";EntityPath=telemetry-hub".
        ///
        ///   If you have defined a shared access policy directly on the Event Hub itself, then copying the connection string from that
        ///   Event Hub will result in a connection string that contains the name.</para>
        /// </remarks>
        ///
        /// <seealso href="https://docs.microsoft.com/azure/event-hubs/event-hubs-get-connection-string">How to get an Event Hubs connection string</seealso>
        ///
        public BatchedEventProcessorClient(
            BlobContainerClient checkpointStore,
            string consumerGroup,
            string connectionString,
            EventProcessorClientOptions clientOptions) 
            : this(
                checkpointStore: checkpointStore,
                consumerGroup: consumerGroup,
                connectionString: connectionString,
                eventHubName: null,
                clientOptions: clientOptions)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">The client responsible for persisting checkpoints and processor state to durable storage. The associated container is expected to exist.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="connectionString">The connection string to use for connecting to the Event Hubs namespace; it is expected that the shared key properties are contained in this connection string, but not the Event Hub name.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        ///
        /// <remarks>
        ///   <para>The container associated with the <paramref name="checkpointStore" /> is expected to exist; the <see cref="EventProcessorClient" />
        ///   does not assume the ability to manage the storage account and is safe to run with only read/write permission for blobs in the container.</para>
        ///
        ///   <para>If the connection string is copied from the Event Hub itself, it will contain the name of the desired Event Hub,
        ///   and can be used directly without passing the <paramref name="eventHubName" />.  The name of the Event Hub should be
        ///   passed only once, either as part of the connection string or separately.</para>
        /// </remarks>
        ///
        /// <seealso href="https://docs.microsoft.com/azure/event-hubs/event-hubs-get-connection-string">How to get an Event Hubs connection string</seealso>
        ///
        public BatchedEventProcessorClient(
            BlobContainerClient checkpointStore,
            string consumerGroup,
            string connectionString,
            string? eventHubName) 
            : this(
                checkpointStore: checkpointStore,
                consumerGroup: consumerGroup,
                connectionString: connectionString,
                eventHubName: eventHubName,
                clientOptions: null)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">The client responsible for persisting checkpoints and processor state to durable storage. The associated container is expected to exist.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="connectionString">The connection string to use for connecting to the Event Hubs namespace; it is expected that the shared key properties are contained in this connection string, but not the Event Hub name.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   <para>The container associated with the <paramref name="checkpointStore" /> is expected to exist; the <see cref="EventProcessorClient" />
        ///   does not assume the ability to manage the storage account and is safe to run with only read/write permission for blobs in the container.</para>
        ///
        ///   <para>If the connection string is copied from the Event Hub itself, it will contain the name of the desired Event Hub,
        ///   and can be used directly without passing the <paramref name="eventHubName" />.  The name of the Event Hub should be
        ///   passed only once, either as part of the connection string or separately.</para>
        /// </remarks>
        ///
        /// <seealso href="https://docs.microsoft.com/azure/event-hubs/event-hubs-get-connection-string">How to get an Event Hubs connection string</seealso>
        ///
        public BatchedEventProcessorClient(
            BlobContainerClient checkpointStore,
            string consumerGroup,
            string connectionString,
            string? eventHubName = null,
            EventProcessorClientOptions? clientOptions = null) 
            : base(
                eventBatchMaximumCount: (clientOptions ?? DefaultClientOptions).CacheEventCount, 
                consumerGroup: consumerGroup, 
                connectionString: connectionString,
                eventHubName: eventHubName,
                options: CreateOptions(clientOptions))
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _containerClient = checkpointStore;
            CheckpointStore = new BlobCheckpointStore(checkpointStore);
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">The client responsible for persisting checkpoints and processor state to durable storage. The associated container is expected to exist.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace to connect to.  This is likely to be similar to <c>{your-namespace}.servicebus.windows.net</c>.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="credential">The shared access key credential to use for authorization.  Access controls may be specified by the Event Hubs namespace or the requested Event Hub, depending on Azure configuration.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   The container associated with the <paramref name="checkpointStore" /> is expected to exist; the <see cref="EventProcessorClient" />
        ///   does not assume the ability to manage the storage account and is safe to run with only read/write permission for blobs in the container.
        /// </remarks>
        ///
        public BatchedEventProcessorClient(
            BlobContainerClient checkpointStore,
            string consumerGroup,
            string fullyQualifiedNamespace,
            string eventHubName,
            AzureNamedKeyCredential credential,
            EventProcessorClientOptions? clientOptions = default) 
            : base(
                eventBatchMaximumCount: (clientOptions ?? DefaultClientOptions).CacheEventCount,
                consumerGroup: consumerGroup, 
                fullyQualifiedNamespace: fullyQualifiedNamespace,
                eventHubName: eventHubName,
                credential: credential,
                options: CreateOptions(clientOptions))
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _containerClient = checkpointStore;
            CheckpointStore = new BlobCheckpointStore(checkpointStore);
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">The client responsible for persisting checkpoints and processor state to durable storage. The associated container is expected to exist.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace to connect to.  This is likely to be similar to <c>{your-namespace}.servicebus.windows.net</c>.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="credential">The shared access signature credential to use for authorization.  Access controls may be specified by the Event Hubs namespace or the requested Event Hub, depending on Azure configuration.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   The container associated with the <paramref name="checkpointStore" /> is expected to exist; the <see cref="EventProcessorClient" />
        ///   does not assume the ability to manage the storage account and is safe to run with only read/write permission for blobs in the container.
        /// </remarks>
        ///
        public BatchedEventProcessorClient(
            BlobContainerClient checkpointStore,
            string consumerGroup,
            string fullyQualifiedNamespace,
            string eventHubName,
            AzureSasCredential credential,
            EventProcessorClientOptions? clientOptions = default) 
            : base(
                eventBatchMaximumCount: (clientOptions ?? DefaultClientOptions).CacheEventCount,
                consumerGroup: consumerGroup,
                fullyQualifiedNamespace: fullyQualifiedNamespace,
                eventHubName: eventHubName,
                credential: credential,
                options: CreateOptions(clientOptions))
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _containerClient = checkpointStore;
            CheckpointStore = new BlobCheckpointStore(checkpointStore);
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">The client responsible for persisting checkpoints and processor state to durable storage. The associated container is expected to exist.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace to connect to.  This is likely to be similar to <c>{your-namespace}.servicebus.windows.net</c>.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="credential">The Azure identity credential to use for authorization.  Access controls may be specified by the Event Hubs namespace or the requested Event Hub, depending on Azure configuration.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   The container associated with the <paramref name="checkpointStore" /> is expected to exist; the <see cref="EventProcessorClient" />
        ///   does not assume the ability to manage the storage account and is safe to run with only read/write permission for blobs in the container.
        /// </remarks>
        ///
        public BatchedEventProcessorClient(
            BlobContainerClient checkpointStore,
            string consumerGroup,
            string fullyQualifiedNamespace,
            string eventHubName,
            TokenCredential credential,
            EventProcessorClientOptions? clientOptions = default) 
            : base(
                eventBatchMaximumCount: (clientOptions ?? DefaultClientOptions).CacheEventCount,
                consumerGroup: consumerGroup,
                fullyQualifiedNamespace: fullyQualifiedNamespace,
                eventHubName: eventHubName,
                credential: credential,
                options: CreateOptions(clientOptions))
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _containerClient = checkpointStore;
            CheckpointStore = new BlobCheckpointStore(checkpointStore);
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">Responsible for creation of checkpoints and for ownership claim.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace to connect to.  This is likely to be similar to <c>{your-namespace}.servicebus.windows.net</c>.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="cacheEventCount">The maximum number of events that will be read from the Event Hubs service and held in a local memory cache when reading is active and events are being emitted to an enumerator for processing.</param>
        /// <param name="credential">An Azure identity credential to satisfy base class requirements; this credential may not be <c>null</c> but will only be used in the case that <see cref="EventProcessor{TPartition}.CreateConnection" /> has not been overridden.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   This constructor is intended only to support functional testing and mocking; it should not be used for production scenarios.
        /// </remarks>
        ///
        internal BatchedEventProcessorClient(
            CheckpointStore checkpointStore,
            string consumerGroup,
            string fullyQualifiedNamespace,
            string eventHubName,
            int cacheEventCount,
            TokenCredential credential,
            EventProcessorOptions? clientOptions) 
            : base(
                eventBatchMaximumCount: cacheEventCount,
                consumerGroup: consumerGroup,
                fullyQualifiedNamespace: fullyQualifiedNamespace,
                eventHubName: eventHubName,
                credential: credential,
                options: clientOptions)
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _defaultStartingPosition = clientOptions?.DefaultStartingPosition ?? _defaultStartingPosition;
            CheckpointStore = checkpointStore;
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">Responsible for creation of checkpoints and for ownership claim.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace to connect to.  This is likely to be similar to <c>{your-namespace}.servicebus.windows.net</c>.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="cacheEventCount">The maximum number of events that will be read from the Event Hubs service and held in a local memory cache when reading is active and events are being emitted to an enumerator for processing.</param>
        /// <param name="credential">A shared access key credential to satisfy base class requirements; this credential may not be <c>null</c> but will only be used in the case that <see cref="EventProcessor{TPartition}.CreateConnection" /> has not been overridden.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   This constructor is intended only to support functional testing and mocking; it should not be used for production scenarios.
        /// </remarks>
        ///
        internal BatchedEventProcessorClient(
            CheckpointStore checkpointStore,
            string consumerGroup,
            string fullyQualifiedNamespace,
            string eventHubName,
            int cacheEventCount,
            AzureNamedKeyCredential credential,
            EventProcessorOptions? clientOptions) 
            : base(
                eventBatchMaximumCount: cacheEventCount,
                consumerGroup: consumerGroup,
                fullyQualifiedNamespace: fullyQualifiedNamespace,
                eventHubName: eventHubName,
                credential: credential,
                options: clientOptions)
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _defaultStartingPosition = clientOptions?.DefaultStartingPosition ?? _defaultStartingPosition;
            CheckpointStore = checkpointStore;
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="EventProcessorClient" /> class.
        /// </summary>
        ///
        /// <param name="checkpointStore">Responsible for creation of checkpoints and for ownership claim.</param>
        /// <param name="consumerGroup">The name of the consumer group this processor is associated with.  Events are read in the context of this group.</param>
        /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace to connect to.  This is likely to be similar to <c>{your-namespace}.servicebus.windows.net</c>.</param>
        /// <param name="eventHubName">The name of the specific Event Hub to associate the processor with.</param>
        /// <param name="cacheEventCount">The maximum number of events that will be read from the Event Hubs service and held in a local memory cache when reading is active and events are being emitted to an enumerator for processing.</param>
        /// <param name="credential">A shared access signature credential to satisfy base class requirements; this credential may not be <c>null</c> but will only be used in the case that <see cref="EventProcessor{TPartition}.CreateConnection" /> has not been overridden.</param>
        /// <param name="clientOptions">The set of options to use for this processor.</param>
        ///
        /// <remarks>
        ///   This constructor is intended only to support functional testing and mocking; it should not be used for production scenarios.
        /// </remarks>
        ///
        internal BatchedEventProcessorClient(
            CheckpointStore checkpointStore,
            string consumerGroup,
            string fullyQualifiedNamespace,
            string eventHubName,
            int cacheEventCount,
            AzureSasCredential credential,
            EventProcessorOptions? clientOptions) 
            : base(
                eventBatchMaximumCount: cacheEventCount,
                consumerGroup: consumerGroup,
                fullyQualifiedNamespace: fullyQualifiedNamespace,
                eventHubName: eventHubName,
                credential: credential,
                options: clientOptions)
        {
            Argument.AssertNotNull(checkpointStore, nameof(checkpointStore));

            _defaultStartingPosition = (clientOptions?.DefaultStartingPosition ?? _defaultStartingPosition);
            CheckpointStore = checkpointStore;
        }

        #endregion

        public override async Task StartProcessingAsync(CancellationToken cancellationToken = default) =>
             await StartProcessingInternalAsync(true, cancellationToken).ConfigureAwait(false);

        public override void StartProcessing(CancellationToken cancellationToken = default) =>
            StartProcessingInternalAsync(false, cancellationToken).GetAwaiter().GetResult();

        protected override Task UpdateCheckpointAsync(
            string partitionId,
            long offset,
            long? sequenceNumber,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested<TaskCanceledException>();

            Argument.AssertNotNull(partitionId, nameof(partitionId));
            Argument.AssertInRange(offset, long.MinValue + 1, long.MaxValue, nameof(offset));

            return CheckpointStore.UpdateCheckpointAsync(
                fullyQualifiedNamespace: FullyQualifiedNamespace, 
                eventHubName: EventHubName,
                consumerGroup: ConsumerGroup,
                partitionId: partitionId,
                offset: offset, sequenceNumber: sequenceNumber, cancellationToken: cancellationToken);
        }

        protected override async Task<EventProcessorCheckpoint> GetCheckpointAsync(
            string partitionId,
            CancellationToken cancellationToken)
        {
            var checkpoint = await CheckpointStore.GetCheckpointAsync(
                fullyQualifiedNamespace: FullyQualifiedNamespace,
                eventHubName: EventHubName,
                consumerGroup: ConsumerGroup,
                partitionId: partitionId,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // If there was no initialization handler, no custom starting positions
            // could have been specified.  Return the checkpoint without further processing.

            if (_partitionInitializingAsync == null)
                return checkpoint;

            // Process the checkpoints to inject mock checkpoints for partitions that
            // specify a custom default and do not have an actual checkpoint.

            return checkpoint ?? CreateCheckpointWithDefaultStartingPosition(partitionId);
        }

        protected override Task<IEnumerable<EventProcessorPartitionOwnership>> ListOwnershipAsync(CancellationToken cancellationToken) 
            => CheckpointStore.ListOwnershipAsync(FullyQualifiedNamespace, EventHubName, ConsumerGroup, cancellationToken);

        protected override Task<IEnumerable<EventProcessorPartitionOwnership>> ClaimOwnershipAsync(
            IEnumerable<EventProcessorPartitionOwnership> desiredOwnership,
            CancellationToken cancellationToken) 
            => CheckpointStore.ClaimOwnershipAsync(desiredOwnership, cancellationToken);

        protected override async Task OnProcessingEventBatchAsync(
            IEnumerable<EventData> events,
            EventProcessorPartition partition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested<TaskCanceledException>();
            if (_processEventBatchAsync == null)
                throw new ArgumentException(string.Format(
                    CultureInfo.CurrentCulture,
                    CannotStartEventProcessorWithoutHandler,
                    nameof(ProcessEventBatchAsync)));

            try
            {
                // Attempt to process the batch.

                var internalEvents = new List<EventData>(events);
                if (internalEvents.Count == 0)
                {
                    var eventArgs = new ProcessEventBatchArgs(
                        partition: new EmptyPartitionContext(
                            fullyQualifiedNamespace: FullyQualifiedNamespace,
                            eventHubName: EventHubName,
                            consumerGroup: ConsumerGroup,
                            partitionId: partition.PartitionId), 
                        events: null,
                        updateCheckpointImplementation: EmptyEventUpdateCheckpoint,
                        cancellationToken: cancellationToken);
                    await _processEventBatchAsync(eventArgs).ConfigureAwait(false);
                }
                else
                {
                    var context = new ProcessorPartitionContext(
                        fullyQualifiedNamespace: FullyQualifiedNamespace,
                        eventHubName: EventHubName,
                        consumerGroup: ConsumerGroup,
                        partitionId: partition.PartitionId,
                        readLastEnqueuedEventProperties: () => ReadLastEnqueuedEventProperties(partition.PartitionId));

                    var lastEvent = internalEvents.Aggregate(
                        (last, current) => last == null ? current : current.Offset > last.Offset ? current : last);
                    
                    var eventArgs = new ProcessEventBatchArgs(
                        partition: context,
                        events: internalEvents,
                        updateCheckpointImplementation: updateToken => 
                            UpdateCheckpointAsync(
                                partitionId: partition.PartitionId,
                                offset: lastEvent.Offset,
                                sequenceNumber: lastEvent.SequenceNumber,
                                cancellationToken: updateToken), 
                        cancellationToken: cancellationToken);

                    await _processEventBatchAsync(eventArgs).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!(ex is TaskCanceledException))
            {
                // This exception was either not related to processing events or was the result of sending an empty batch to be
                // processed.
                // Since there would be no other caught exceptions, tread this like a single case.
                throw;
            }
        }

        protected override async Task OnProcessingErrorAsync(
            Exception exception,
            EventProcessorPartition? partition,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            if (_processErrorAsync != null)
            {
                var eventArgs = new ProcessErrorEventArgs(partition?.PartitionId, operationDescription, exception, cancellationToken);
                await _processErrorAsync(eventArgs).ConfigureAwait(false);
            }
        }

        protected override async Task OnInitializingPartitionAsync(
            EventProcessorPartition partition,
            CancellationToken cancellationToken)
        {
            // Handlers cannot be changed while the processor is running; it is safe to check and call
            // without capturing a local reference.

            if (_partitionInitializingAsync != null)
            {
                var eventArgs = new PartitionInitializingEventArgs(partition.PartitionId, _defaultStartingPosition, cancellationToken);
                await _partitionInitializingAsync(eventArgs).ConfigureAwait(false);

                _partitionStartingPositionDefaults[partition.PartitionId] = eventArgs.DefaultStartingPosition;
            }
        }

        protected override async Task OnPartitionProcessingStoppedAsync(
            EventProcessorPartition partition,
            ProcessingStoppedReason reason,
            CancellationToken cancellationToken)
        {
            // Handlers cannot be changed while the processor is running; it is safe to check and call
            // without capturing a local reference.

            if (_partitionClosingAsync != null)
            {
                var eventArgs = new PartitionClosingEventArgs(partition.PartitionId, reason, cancellationToken);
                await _partitionClosingAsync(eventArgs).ConfigureAwait(false);
            }

            _partitionStartingPositionDefaults.TryRemove(partition.PartitionId, out _);
        }

        /// <summary>
        ///   Signals the <see cref="EventProcessorClient" /> to begin processing events.  If the processor is already
        ///   running when this is called, no action is taken.
        /// </summary>
        ///
        /// <param name="async">When <c>true</c>, the method will be executed asynchronously; otherwise, it will execute synchronously.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> instance to signal the request to cancel the start operation.  This won't affect the <see cref="EventProcessor{TPartition}" /> once it starts running.</param>
        ///
        private async Task StartProcessingInternalAsync(
            bool async,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested<TaskCanceledException>();

            var capturedValidationException = default(Exception);
            var releaseGuard = false;

            try
            {
                // Acquire the semaphore used to synchronize processor starts and stops, respecting
                // the async flag.  When this is held, the state of the processor is stable.

                if (async)
                {
                    await _processorStatusGuard.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // ReSharper disable once MethodHasAsyncOverload
                    _processorStatusGuard.Wait(cancellationToken);
                }

                releaseGuard = true;

                // Validate that the required handlers are assigned.

                if (_processEventBatchAsync == null)
                {
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.CurrentCulture, 
                        CannotStartEventProcessorWithoutHandler, 
                        nameof(ProcessEventAsync)));
                }

                if (_processErrorAsync == null)
                {
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.CurrentCulture, 
                        CannotStartEventProcessorWithoutHandler,
                        nameof(ProcessErrorAsync)));
                }

                // Allow the base class to perform its startup operation; this will include validation for
                // the basic Event Hubs and storage configuration.

                if (async)
                {
                    await base.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // ReSharper disable once MethodHasAsyncOverload
                    base.StartProcessing(cancellationToken);
                }

                // Because the base class has no understanding of what concrete storage type is in use and
                // does not directly make use of some of its operations, such as writing a checkpoint.  Validate
                // these additional needs if a storage client is available.

                if (_containerClient != null)
                {
                    try
                    {
                        if (async)
                        {
                            await ValidateStartupAsync(async, _containerClient, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            ValidateStartupAsync(async, _containerClient, cancellationToken).GetAwaiter().GetResult();
                        }
                    }
                    catch (AggregateException ex)
                    {
                        // Capture the validation exception and log, but do not throw.  Because this is
                        // a fatal exception and the processing task was already started, StopProcessing
                        // will need to be called, which requires the semaphore.  The validation exception
                        // will be handled after the start operation has officially completed and the
                        // semaphore has been released.

                        capturedValidationException = ex.Flatten();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException();
            }
            finally
            {
                if (releaseGuard)
                {
                    _processorStatusGuard.Release();
                }
            }

            // If there was a validation exception captured, then stop the processor now
            // that it is safe to do so.

            if (capturedValidationException != null)
            {
                try
                {
                    if (async)
                    {
                        await StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        // ReSharper disable once MethodHasAsyncOverload
                        StopProcessing(CancellationToken.None);
                    }
                }
                catch
                {
                    // An exception is expected here, as the processor configuration was invalid and
                    // processing was canceled.  It will have already been logged; ignore it here.
                }

                ExceptionDispatchInfo.Capture(capturedValidationException).Throw();
            }
        }

        /// <summary>
        ///   Creates a checkpoint with a default starting position set.
        /// </summary>
        ///
        /// <param name="partitionId">The partition id.</param>
        ///
        /// <returns>Returns an artificial checkpoint for a provided partition with a starting position set to <see cref="_defaultStartingPosition"/>.</returns>
        ///
        private EventProcessorCheckpoint CreateCheckpointWithDefaultStartingPosition(string partitionId)
        {
            return new EventProcessorCheckpoint
            {
                FullyQualifiedNamespace = FullyQualifiedNamespace,
                EventHubName = EventHubName,
                ConsumerGroup = ConsumerGroup,
                PartitionId = partitionId,
                StartingPosition = _partitionStartingPositionDefaults.TryGetValue(partitionId, out var position) 
                    ? position 
                    : _defaultStartingPosition
            };
        }

        /// <summary>
        ///   Performs the tasks needed to validate basic configuration and permissions of the dependencies needed for
        ///   the processor to function.
        /// </summary>
        ///
        /// <param name="async">When <c>true</c>, the method will be executed asynchronously; otherwise, it will execute synchronously.</param>
        /// <param name="containerClient">The <see cref="BlobContainerClient" /> to use for validating storage operations.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> instance to signal the request to cancel the start operation.</param>
        ///
        /// <exception cref="AggregateException">Any validation failures will result in an aggregate exception.</exception>
        ///
        private static async Task ValidateStartupAsync(
            bool async,
            BlobContainerClient containerClient,
            CancellationToken cancellationToken = default)
        {
            var blobClient = containerClient.GetBlobClient($"EventProcessorPermissionCheck/{ Guid.NewGuid():N}");

            // Write an blob with metadata, simulating the approach used for checkpoint and ownership
            // data creation.

            try
            {
                using var blobContent = new MemoryStream(Array.Empty<byte>());
                var blobMetadata = new Dictionary<string, string> {{ "name", blobClient.Name }};

                if (async)
                {
                    await blobClient.UploadAsync(blobContent, metadata: blobMetadata, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // ReSharper disable once MethodHasAsyncOverload
                    blobClient.Upload(blobContent, metadata: blobMetadata, cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AggregateException(ex);
            }
            finally
            {
                // Remove the test blob if written; do so without respecting a cancellation request to
                // ensure that the container is left in a consistent state.

                try
                {
                    if (async)
                    {
                        await blobClient.DeleteIfExistsAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        // ReSharper disable once MethodHasAsyncOverload
                        blobClient.DeleteIfExists(cancellationToken: CancellationToken.None);
                    }
                }
                catch
                {
                    // no-op, maybe we need to log this?
                }
            }
        }

        /// <summary>
        ///   Invokes a specified action only if this <see cref="EventProcessorClient" /> instance is not running.
        /// </summary>
        ///
        /// <param name="action">The action to invoke.</param>
        ///
        /// <exception cref="InvalidOperationException">Occurs when this method is invoked while the event processor is running.</exception>
        ///
        private void EnsureNotRunningAndInvoke(Action? action)
        {
            if (IsRunning) 
                throw new InvalidOperationException(RunningEventProcessorCannotPerformOperation);
            
            var releaseGuard = false;
            try
            {
                _processorStatusGuard.Wait();
                releaseGuard = true;

                if (!IsRunning)
                {
                    action?.Invoke();
                    return;
                }
            }
            finally
            {
                if (releaseGuard)
                    _processorStatusGuard.Release();
            }

            throw new InvalidOperationException(RunningEventProcessorCannotPerformOperation);
        }

        /// <summary>
        ///   Creates the set of options to pass to the base <see cref="EventProcessorClient" />.
        /// </summary>
        ///
        /// <param name="clientOptions">The set of client options for the <see cref="EventProcessorClient" /> instance.</param>
        ///
        /// <returns>The set of options to use for the base processor.</returns>
        ///
        private static EventProcessorOptions CreateOptions(EventProcessorClientOptions? clientOptions)
        {
            clientOptions ??= DefaultClientOptions;

            return new EventProcessorOptions
            {
                ConnectionOptions = clientOptions.ConnectionOptions.Clone(),
                RetryOptions = clientOptions.RetryOptions.Clone(),
                Identifier = clientOptions.Identifier,
                MaximumWaitTime = clientOptions.MaximumWaitTime,
                TrackLastEnqueuedEventProperties = clientOptions.TrackLastEnqueuedEventProperties,
                LoadBalancingStrategy = clientOptions.LoadBalancingStrategy,
                PrefetchCount = clientOptions.PrefetchCount,
                PrefetchSizeInBytes = clientOptions.PrefetchSizeInBytes,
                LoadBalancingUpdateInterval = clientOptions.LoadBalancingUpdateInterval,
                PartitionOwnershipExpirationInterval = clientOptions.PartitionOwnershipExpirationInterval
            };
        }

        /// <summary>
        ///   Represents a basic partition context for event processing within the processor client.
        /// </summary>
        ///
        /// <seealso cref="PartitionContext" />
        ///
        private class ProcessorPartitionContext : PartitionContext
        {
            /// <summary>A function that can be used to read the last enqueued event properties for the partition.</summary>
            private readonly Func<LastEnqueuedEventProperties> _readLastEnqueuedEventProperties;

            /// <summary>
            ///   Initializes a new instance of the <see cref="EmptyPartitionContext" /> class.
            /// </summary>
            ///
            /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace this context is associated with.</param>
            /// <param name="eventHubName">The name of the Event Hub partition this context is associated with.</param>
            /// <param name="consumerGroup">The name of the consumer group this context is associated with.</param>
            /// <param name="partitionId">The identifier of the Event Hub partition this context is associated with.</param>
            /// <param name="readLastEnqueuedEventProperties">A function that can be used to read the last enqueued event properties for the partition.</param>
            ///
            public ProcessorPartitionContext(string fullyQualifiedNamespace,
                                             string eventHubName,
                                             string consumerGroup,
                                             string partitionId,
                                             Func<LastEnqueuedEventProperties> readLastEnqueuedEventProperties) : base(fullyQualifiedNamespace, eventHubName, consumerGroup, partitionId)
            {
                _readLastEnqueuedEventProperties = readLastEnqueuedEventProperties;
            }

            /// <summary>
            ///   A set of information about the last enqueued event of a partition, not available for the
            ///   empty context.
            /// </summary>
            ///
            /// <returns>The set of properties for the last event that was enqueued to the partition.</returns>
            ///
            public override LastEnqueuedEventProperties ReadLastEnqueuedEventProperties() => _readLastEnqueuedEventProperties();
        }

        /// <summary>
        ///   Represents a basic partition context for event processing when the
        ///   full context was not available.
        /// </summary>
        ///
        /// <seealso cref="PartitionContext" />
        ///
        private class EmptyPartitionContext : PartitionContext
        {
            /// <summary>
            ///   Initializes a new instance of the <see cref="EmptyPartitionContext" /> class.
            /// </summary>
            ///
            /// <param name="fullyQualifiedNamespace">The fully qualified Event Hubs namespace this context is associated with.</param>
            /// <param name="eventHubName">The name of the Event Hub partition this context is associated with.</param>
            /// <param name="consumerGroup">The name of the consumer group this context is associated with.</param>
            /// <param name="partitionId">The identifier of the Event Hub partition this context is associated with.</param>
            ///
            public EmptyPartitionContext(
                string fullyQualifiedNamespace,
                string eventHubName,
                string consumerGroup,
                string partitionId) : base(fullyQualifiedNamespace, eventHubName, consumerGroup, partitionId)
            {
            }

            /// <summary>
            ///   A set of information about the last enqueued event of a partition, not available for the
            ///   empty context.
            /// </summary>
            ///
            /// <returns>The set of properties for the last event that was enqueued to the partition.</returns>
            ///
            /// <exception cref="InvalidOperationException">The method call is not available on the <see cref="EmptyPartitionContext" />.</exception>
            ///
            public override LastEnqueuedEventProperties ReadLastEnqueuedEventProperties() =>
                throw new InvalidOperationException(CannotReadLastEnqueuedEventPropertiesWithoutEvent);
        }    
    }
}