using System;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public class ShutdownSignalException : Exception
    {
        public ShutdownInitiator ShutdownInitiator { get; }
        public object Cause { get; }
        public ushort ClassId { get; }
        public ushort MethodId { get; }
        public ushort ReplyCode { get; }
        public string ReplyText { get; }

        public ShutdownSignalException(ShutdownInitiator shutdownInitiator, object cause, ushort classId,
            ushort methodId, ushort replyCode, string replyText)
            : base(GenerateExceptionMessage(shutdownInitiator, cause, classId, methodId, replyCode, replyText))
        {
            ShutdownInitiator = shutdownInitiator;
            Cause = cause;
            ClassId = classId;
            MethodId = methodId;
            ReplyCode = replyCode;
            ReplyText = replyText;
        }

        private static string GenerateExceptionMessage(ShutdownInitiator shutdownInitiator, object cause, ushort classId,
            ushort methodId, ushort replyCode, string replyText)
        {
            return
                $"ShutdownSignal has been received, ShutdownInitiator={shutdownInitiator}, Cause={cause}, ClassId={classId}, MethodId={methodId}, ReplyCode={replyCode}, ReplyText={replyText}";
        }

        public static ShutdownSignalException FromArgs(ShutdownEventArgs args)
        {
            return new ShutdownSignalException(args.Initiator, args.Cause, args.ClassId, args.MethodId, args.ReplyCode, args.ReplyText);
        }
    }
}
