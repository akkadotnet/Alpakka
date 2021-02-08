using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Streams.Amqp.V1.Internal
{
    internal class ConnectResult
    {
        public static readonly ConnectResult Success = new ConnectResult(null);

        public bool IsSuccessful => Exception == null;

        public Exception Exception { get; }

        public ConnectResult(Exception exception)
        {
            Exception = exception;
        }
    }
}
