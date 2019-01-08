#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;

namespace Akka.Streams.Kinesis
{
    public static class KinesisSource
    {
        public static Source<Record, NotUsed> Basic(ShardSettings settings, Func<IAmazonKinesis> client = null) =>
            Source.FromGraph(new KinesisSourceStage(settings, client ?? KinesisFlow.DefaultClientFactory));
    }
}