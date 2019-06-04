#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsModel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Internal;
using Amazon.SQS.Model;

namespace Akka.Streams.SQS
{
    public abstract class MessageAction
    {
        public static MessageAction Delete(Message message) => new Delete(message);
        public static MessageAction Ignore(Message message) => new Ignore(message);
        public static MessageAction ChangeVisibility(Message message, TimeSpan visibilityTimeout) => 
            new ChangeMessageVisibility(message, visibilityTimeout);
        
        Message Message { get; }
    }

    /// <summary>
    /// Delete the message from the queue.
    /// See: https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_DeleteMessage.html
    /// </summary>
    public sealed class Delete : MessageAction
    {
        public Delete(Message message)
        {
            Message = message;
        }

        public Message Message { get; }
        public override string ToString() => $"Delete({Message})";
    }
    
    public sealed class Ignore : MessageAction
    {
        public Ignore(Message message)
        {
            Message = message;
        }

        public Message Message { get; }
        public override string ToString() => $"Ignore({Message})";
    }
    
    /// <summary>
    /// Change the visibility timeout of the message.
    /// The maximum allowed timeout value is 12 hours.
    /// See: https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_ChangeMessageVisibility.html
    /// </summary>
    public sealed class ChangeMessageVisibility  : MessageAction
    {
        public static TimeSpan MaxVisibilityTimeout { get; } = TimeSpan.FromHours(12);
        
        public ChangeMessageVisibility(Message message, TimeSpan visibilityTimeout)
        {
            if (visibilityTimeout > MaxVisibilityTimeout)
                throw new ArgumentException($"{nameof(visibilityTimeout)} must be less than or equal to {MaxVisibilityTimeout}", nameof(visibilityTimeout));
            
            Message = message;
            VisibilityTimeout = visibilityTimeout;
        }


        public Message Message { get; }
        public TimeSpan VisibilityTimeout { get; }
        public override string ToString() => $"ChangeMessageVisibility({VisibilityTimeout}, {Message})";
    }

    /// <summary>
    /// Result contained in a Sqs Response.
    /// </summary>
    public interface ISqsResult
    {
        /// <summary>
        /// The SQS response metadata (AWS request ID, ...)
        /// </summary>
        ResponseMetadata ResponseMetadata { get; }
        object Result { get; }
    }

    /// <summary>
    /// Messages returned by a <see cref="SqsPublishFlow"/>.
    /// </summary>
    public sealed class SqsPublishResult : ISqsResult
    {
        public ResponseMetadata ResponseMetadata => Response.ResponseMetadata;
        public object Result => Response;
        public AmazonSQSRequest Request { get; }
        public SendMessageResponse Response { get; }

        public SqsPublishResult(SendMessageRequest request, SendMessageResponse response)
        {
            Request = request;
            Response = response;
        }
    }

    /// <summary>
    /// Messages returned by a <see cref="SqsPublishFlow.Grouped"/> or <see cref="SqsPublishFlow.Batched"/>.
    /// </summary>
    public sealed class SqsPublishResultEntry : ISqsResult
    {
        public SqsPublishResultEntry(SendMessageRequest request, SendMessageBatchResultEntry result, ResponseMetadata responseMetadata)
        {
            Request = request;
            Result = result;
            ResponseMetadata = responseMetadata;
        }

        public SendMessageRequest Request { get; }
        public ResponseMetadata ResponseMetadata { get; }
        public SendMessageBatchResultEntry Result { get; }
        object ISqsResult.Result => Result;
    }

    /// <summary>
    /// Messages returned by a <see cref="SqsAckFlow"/>.
    /// </summary>
    public interface ISqsAckResult : ISqsResult
    {
        MessageAction Action { get; }
    }

    /// <summary>
    /// Delete acknowledgment.
    /// </summary>
    public sealed class SqsDeleteResult : ISqsAckResult
    {
        public Delete Action { get; }

        public DeleteMessageResponse Result { get; }

        public SqsDeleteResult(Delete action, DeleteMessageResponse result)
        {
            Action = action;
            Result = result;
        }

        public ResponseMetadata ResponseMetadata => Result.ResponseMetadata;
        MessageAction ISqsAckResult.Action => Action;
        object ISqsResult.Result => Result;
    }

    /// <summary>
    /// Ignore acknowledgment
    /// No requests are executed on the SQS service for ignore messageAction.
    /// Its result is <see cref="NotUsed"/> and the responseMetadata is always empty
    /// </summary>
    public sealed class SqsIgnoreResult : ISqsAckResult
    {
        public SqsIgnoreResult(Ignore action)
        {
            Action = action;
        }

        public ResponseMetadata ResponseMetadata => new Amazon.Runtime.ResponseMetadata();
        public object Result => NotUsed.Instance;
        public MessageAction Action { get; }
    }

    /// <summary>
    /// ChangeMessageVisibility acknowledgement.
    /// </summary>
    public sealed class SqsChangeMessageVisibilityResult : ISqsAckResult
    {
        public ChangeMessageVisibility Action { get; }

        public ChangeMessageVisibilityResponse Result { get; }

        public SqsChangeMessageVisibilityResult(ChangeMessageVisibility action, ChangeMessageVisibilityResponse result)
        {
            Action = action;
            Result = result;
        }

        public ResponseMetadata ResponseMetadata => Result.ResponseMetadata;
        object ISqsResult.Result => Result;
        MessageAction ISqsAckResult.Action => Action;
    }

    /// <summary>
    /// Messages returned by a <see cref="SqsAckFlow"/>.
    /// </summary>
    public interface ISqsAckResultEntry : ISqsResult
    {
        MessageAction Action { get; }
    }

    /// <summary>
    /// Delete acknowledgement within a batch.
    /// </summary>
    public sealed class SqsDeleteResultEntry : ISqsAckResultEntry
    {
        public Delete Action { get; }
        public DeleteMessageBatchResultEntry Result { get; }
        public ResponseMetadata ResponseMetadata { get; }

        public SqsDeleteResultEntry(Delete action, DeleteMessageBatchResultEntry result, ResponseMetadata responseMetadata)
        {
            Action = action;
            Result = result;
            ResponseMetadata = responseMetadata;
        }

        MessageAction ISqsAckResultEntry.Action => Action;
        object ISqsResult.Result => Result;
    }
    
    /// <summary>
    /// Ignore acknowledgment within a batch
    /// No requests are executed on the SQS service for ignore messageAction.
    /// Its result is <see cref="NotUsed"/> and the responseMetadata is always empty
    /// </summary>
    public sealed class SqsIgnoreResultEntry : ISqsAckResultEntry
    {
        public Ignore Action { get; }

        public SqsIgnoreResultEntry(Ignore action)
        {
            Action = action;
        }

        ResponseMetadata ISqsResult.ResponseMetadata => new ResponseMetadata();
        object ISqsResult.Result => NotUsed.Instance;
        MessageAction ISqsAckResultEntry.Action => Action;
    }
    
    /// <summary>
    /// ChangeMessageVisibility acknowledgement within a batch
    /// </summary>
    public sealed class SqsChangeMessageVisibilityResultEntry : ISqsAckResultEntry
    {
        public ChangeMessageVisibility Action { get; }
        public ChangeMessageVisibilityBatchResultEntry Result { get; }
        public ResponseMetadata ResponseMetadata { get; }

        public SqsChangeMessageVisibilityResultEntry(ChangeMessageVisibility action, ChangeMessageVisibilityBatchResultEntry result, ResponseMetadata responseMetadata)
        {
            Action = action;
            Result = result;
            ResponseMetadata = responseMetadata;
        }
        object ISqsResult.Result => Result;
        MessageAction ISqsAckResultEntry.Action => Action;
    }
}