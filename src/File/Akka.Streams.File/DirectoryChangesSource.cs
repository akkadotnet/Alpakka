//-----------------------------------------------------------------------
// <copyright file="DirectoryChangesSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;
using Akka.Streams.File.Impl;

namespace Akka.Streams.File
{
    public static class DirectoryChangesSource
    {
        private static readonly Func<string, DirectoryChange, (string, DirectoryChange)> _tupler = (t, u) => (t, u);

        /// <summary>
        /// Watch directory and emit changes as a stream of tuples containing the path and type of change.
        /// </summary>
        /// <param name="directoryPath">Directory to watch</param>
        /// <param name="maxBufferSize">Maximum number of buffered directory changes before the stage fails</param>
        public static Source<(string, DirectoryChange), NotUsed> Create(string directoryPath, int maxBufferSize) =>
            Source.FromGraph(new DirectoryChangesSource<(string, DirectoryChange)>(directoryPath, maxBufferSize, _tupler));
    }
}
