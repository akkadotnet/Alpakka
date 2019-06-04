#region copyright
//-----------------------------------------------------------------------
// <copyright file="AttributeName.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Akka.Streams.SQS
{
    
    /// <summary>
    /// Message attribure names described at
    /// https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_ReceiveMessage.html#API_ReceiveMessage_RequestParameters
    /// </summary>
    public readonly struct MessageAttributeName : IEquatable<MessageAttributeName>, IComparable<MessageAttributeName>, IComparable
    {
        private static readonly Regex alphanumeric = new Regex("[0-9a-zA-Z_\\-.*]+", RegexOptions.Compiled);
        private static readonly Regex dots = new Regex("(^\\.[^*].*)|(.*\\.\\..*)|(.*\\.$)", RegexOptions.Compiled);
        public string Name { get; }

        public MessageAttributeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (!alphanumeric.IsMatch(name))
                throw new ArgumentException("MessageAttributeNames may only contain alphanumeric characters and the underscore (_), hyphen (-), period (.), or star (*)");
            if (dots.IsMatch(name))
                throw new ArgumentException("MessageAttributeNames cannot start or end with a period (.) or have multiple periods in succession (..)");
            if (name.Length > 256)
                throw new ArgumentException("MessageAttributeNames may not be longer than 256 characters");
            Name = name;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator MessageAttributeName(string attributeName) => new MessageAttributeName(attributeName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator string(in MessageAttributeName attribute) => attribute.Name;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in MessageAttributeName a, in MessageAttributeName b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(MessageAttributeName a, MessageAttributeName b) => !(a == b);

        public bool Equals(MessageAttributeName other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        public int CompareTo(MessageAttributeName other) => string.Compare(Name, other.Name, StringComparison.Ordinal);

        public int CompareTo(object obj)
        {
            if (obj is MessageAttributeName attributeName) return CompareTo(attributeName);
            else throw new InvalidOperationException(
                $"Cannot compare {nameof(MessageAttributeName)} with {obj?.GetType().FullName ?? "null"}");
        }

        public override bool Equals(object obj) => obj is MessageAttributeName a && Equals(a);
        public override int GetHashCode() => Name.GetHashCode();
        public override string ToString() => Name;
    }
    
    /// <summary>
    /// Source parameters as described at
    /// https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_ReceiveMessage.html#API_ReceiveMessage_RequestParameters
    /// </summary>
    public readonly struct AttributeName : IEquatable<AttributeName>, IComparable<AttributeName>, IComparable
    {
        public static AttributeName All = new AttributeName("All");
        public static AttributeName ApproximateFirstReceiveTimestamp = new AttributeName("ApproximateFirstReceiveTimestamp");
        public static AttributeName ApproximateReceiveCount = new AttributeName("ApproximateReceiveCount");
        public static AttributeName SenderId = new AttributeName("SenderId");
        public static AttributeName SentTimestamp = new AttributeName("SentTimestamp");
        public static AttributeName MessageDeduplicationId = new AttributeName("MessageDeduplicationId");
        public static AttributeName MessageGroupId = new AttributeName("MessageGroupId");
        public static AttributeName SequenceNumber = new AttributeName("SequenceNumber");
        public static AttributeName Policy = new AttributeName("Policy");
        public static AttributeName VisibilityTimeout = new AttributeName("VisibilityTimeout");
        public static AttributeName MaximumMessageSize = new AttributeName("MaximumMessageSize");
        public static AttributeName MessageRetentionPeriod = new AttributeName("MessageRetentionPeriod");
        public static AttributeName ApproximateNumberOfMessages = new AttributeName("ApproximateNumberOfMessages");
        public static AttributeName ApproximateNumberOfMessagesNotVisible = new AttributeName("ApproximateNumberOfMessagesNotVisible");
        public static AttributeName CreatedTimestamp = new AttributeName("CreatedTimestamp");
        public static AttributeName LastModifiedTimestamp = new AttributeName("LastModifiedTimestamp");
        public static AttributeName QueueArn = new AttributeName("QueueArn");
        public static AttributeName ApproximateNumberOfMessagesDelayed = new AttributeName("ApproximateNumberOfMessagesDelayed");
        public static AttributeName DelaySeconds = new AttributeName("DelaySeconds");
        public static AttributeName ReceiveMessageWaitTimeSeconds = new AttributeName("ReceiveMessageWaitTimeSeconds");
        public static AttributeName RedrivePolicy = new AttributeName("RedrivePolicy");
        public static AttributeName FifoQueue = new AttributeName("FifoQueue");
        public static AttributeName ContentBasedDeduplication = new AttributeName("ContentBasedDeduplication");
        public static AttributeName KmsMasterKeyId = new AttributeName("KmsMasterKeyId");
        public static AttributeName KmsDataKeyReusePeriodSeconds = new AttributeName("KmsDataKeyReusePeriodSeconds");
        
        public string Name { get; }
        
        private AttributeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            
            Name = name;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator AttributeName(string attributeName) => new AttributeName(attributeName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator string(in AttributeName attribute) => attribute.Name;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in AttributeName a, in AttributeName b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(AttributeName a, AttributeName b) => !(a == b);

        public bool Equals(AttributeName other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is AttributeName other && Equals(other);

        public override int GetHashCode() => Name.GetHashCode();
        public int CompareTo(object obj)
        {
            if (obj is AttributeName attr) return CompareTo(attr);
            else throw new InvalidOperationException(
                $"Cannot compare {nameof(AttributeName)} with {obj?.GetType().FullName ?? "null"}");
        }

        public int CompareTo(AttributeName other) => string.Compare(Name, other.Name, StringComparison.Ordinal);
        public override string ToString() => Name;
    }
}