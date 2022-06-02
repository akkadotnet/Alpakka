//-----------------------------------------------------------------------
// <copyright file="DirectoryChangesSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using Akka.Annotations;
using Akka.Streams.Stage;

namespace Akka.Streams.File.Impl
{
    /// <summary>
    /// Watches a file system directory and streams change events from it.
    /// </summary>
    [InternalApi]
    internal class DirectoryChangesSource<T> : GraphStage<SourceShape<T>>
    {
        private readonly string _directoryPath;
        private readonly int _maxBufferSize;
        private readonly Func<string, DirectoryChange, T> _combiner;

        private Outlet<T> Out { get; } = new Outlet<T>("DirectoryChangesSource.Out");
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// Creates an instance of the <see cref="DirectoryChangesSource{T}"/>.
        /// </summary>
        /// <param name="directoryPath">Directory to watch</param>
        /// <param name="maxBufferSize">Maximum number of buffered directory changes before the stage fails</param>
        /// <param name="combiner">A function that combines a string and a <see cref="DirectoryChange"/> into an element that will be emitted downstream</param>
        public DirectoryChangesSource(string directoryPath, int maxBufferSize, Func<string, DirectoryChange, T> combiner)
        {
            _directoryPath = directoryPath;
            _maxBufferSize = maxBufferSize;
            _combiner = combiner;

            Shape = new SourceShape<T>(Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            if (!Directory.Exists(_directoryPath))
                throw new DirectoryNotFoundException($"The path: '{_directoryPath}' does not exist or is not a directory");

            return new DirectoryChangesSourceLogic(this);
        }

        #region Logic

        private sealed class DirectoryChangesSourceLogic : OutGraphStageLogic
        {
            private readonly Queue<T> _buffer = new Queue<T>();
            private readonly DirectoryChangesSource<T> _stage;
            private readonly FileSystemWatcher _watcher;
            private readonly Action<T> _onEventReceived;

            public DirectoryChangesSourceLogic(DirectoryChangesSource<T> stage)
                : base(stage.Shape)
            {
                _stage = stage;
                _watcher = new FileSystemWatcher(stage._directoryPath, "*.*")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };

                _onEventReceived = GetAsyncCallback<T>(e =>
                {
                    if (IsAvailable(_stage.Out))
                    {
                        Push(_stage.Out, e);
                        return;
                    }

                    _buffer.Enqueue(e);
                    if (_buffer.Count > _stage._maxBufferSize)
                        FailStage(new BufferOverflowException(
                            $"Max event buffer size {_stage._maxBufferSize} reached for {_stage._directoryPath}"));
                });

                SetHandler(stage.Out, this);
            }

            public override void PreStart()
            {
                base.PreStart();

                _watcher.Changed += EventReceived;
                _watcher.Created += EventReceived;
                _watcher.Deleted += EventReceived;
                _watcher.Renamed += EventReceived;
                _watcher.EnableRaisingEvents = true;
            }

            public override void PostStop()
            {
                _watcher.Changed -= EventReceived;
                _watcher.Created -= EventReceived;
                _watcher.Deleted -= EventReceived;
                _watcher.Renamed += EventReceived;
                _watcher.Dispose();

                base.PostStop();
            }

            public override void OnPull()
            {
                if (_buffer.TryDequeue(out var head))
                    Push(_stage.Out, head);
            }

            public override string ToString() => $"DirectoryChangesSource('{_stage._directoryPath}')";

            private void EventReceived(object sender, FileSystemEventArgs args)
            {
                DirectoryChange KindToChange(WatcherChangeTypes changeType) => changeType switch
                {
                    WatcherChangeTypes.Created => DirectoryChange.Creation,
                    WatcherChangeTypes.Renamed => DirectoryChange.Modification,
                    WatcherChangeTypes.Changed => DirectoryChange.Modification,
                    WatcherChangeTypes.Deleted => DirectoryChange.Deletion,
                    _ => throw new ArgumentException($"Unexpected kind of event gotten from watching path '{_stage._directoryPath}': {changeType}")
                };

                var change = KindToChange(args.ChangeType);
                _onEventReceived(_stage._combiner(args.FullPath, change));
            }
        }

        #endregion
    }

    /// <summary>
    /// Enumeration of the possible changes that can happen to a directory
    /// </summary>
    public enum DirectoryChange
    {
        Modification,
        Creation,
        Deletion
    }
}
