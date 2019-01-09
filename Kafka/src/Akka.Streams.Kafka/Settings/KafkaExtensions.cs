using Confluent.Kafka;

namespace Akka.Streams.Kafka.Settings
{
    public static class KafkaExtensions
    {
        public static bool IsBrokerErrorRetriable(this Error error)
        {
            switch (error.Code)
            {
                case ErrorCode.InvalidMsg:
                case ErrorCode.UnknownTopicOrPart:
                case ErrorCode.LeaderNotAvailable:
                case ErrorCode.NotLeaderForPartition:
                case ErrorCode.RequestTimedOut:
                case ErrorCode.GroupLoadInProress:
                case ErrorCode.GroupCoordinatorNotAvailable:
                case ErrorCode.NotCoordinatorForGroup:
                case ErrorCode.NotEnoughReplicas:
                case ErrorCode.NotEnoughReplicasAfterAppend:
                    return true;
            }

            return false;
        }

        public static bool IsLocalErrorRetriable(this Error error)
        {
            switch (error.Code)
            {
                case ErrorCode.Local_Transport:
                case ErrorCode.Local_AllBrokersDown:
                    return false;
            }

            return true;
        }
    }
}
