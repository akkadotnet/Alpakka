using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Kafka.Stages;
using Confluent.Kafka;
using Akka.Streams.Kafka.Messages;

namespace Akka.Streams.Kafka.Dsl
{
    public static class Consumer
    {
        public static Source<ConsumerRecord<K, V>, Task> PlainSource<K, V>(ConsumerSettings<K, V> settings, ISubscription subscription)
        {
            return Source.FromGraph(new KafkaSourceStage<K, V>(settings, subscription));
        }

        public static Source<CommittableMessage<K, V>, Task> CommittableSource<K, V>(ConsumerSettings<K, V> settings, ISubscription subscription)
        {
            return Source.FromGraph(new CommittableConsumerStage<K, V>(settings, subscription));
        }
    }
}
