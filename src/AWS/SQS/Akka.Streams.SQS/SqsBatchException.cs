#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsBatchException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion


using System;
using System.Runtime.Serialization;

namespace Akka.Streams.SQS
{
    public class SqsBatchException : Exception
    {
        protected SqsBatchException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public SqsBatchException(string message) : base(message)
        {
        }
    }
}