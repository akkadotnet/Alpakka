
namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Encapsulates a group of parameters used for AMQP's Basic methods
    /// </summary>
    public sealed class Envelope
    {
        /// <summary>
        /// Get the delivery tag included in this parameter envelope
        /// </summary>
        public ulong DeliveryTag { get; }
        /// <summary>
        /// Get the redelivery flag included in this parameter envelope. This is a
        /// hint as to whether this message may have been delivered before (but not
        /// acknowledged). If the flag is not set, the message definitely has not
        /// been delivered before. If it is set, it may have been delivered before.
        /// </summary>
        public bool Redeliver { get; }
        /// <summary>
        /// Get the name of the exchange included in this parameter envelope
        /// </summary>
        public string Exchange { get; }
        /// <summary>
        /// Get the routing key included in this parameter envelope
        /// </summary>
        public string RoutingKey { get; }
        /// <summary>
        /// Construct an <see cref="Envelope"/> with the specified construction parameters
        /// </summary>
        /// <param name="deliveryTag">the delivery tag</param>
        /// <param name="redeliver">true if this is a redelivery following a failed ack</param>
        /// <param name="exchange">the exchange used for the current operation</param>
        /// <param name="routingKey">the associated routing key</param>
        private Envelope(ulong deliveryTag, bool redeliver, string exchange, string routingKey)
        {
            DeliveryTag = deliveryTag;
            Redeliver = redeliver;
            Exchange = exchange;
            RoutingKey = routingKey;
        }
        public override string ToString()
        {
            return
                $"Envelope(DeliveryTag={DeliveryTag}, Redeliver={Redeliver}, Exchange={Exchange}, RoutingKey={RoutingKey})";
        }
        /// <summary>
        /// Construct an <see cref="Envelope"/> with the specified construction parameters
        /// </summary>
        /// <param name="deliveryTag">the delivery tag</param>
        /// <param name="redeliver">true if this is a redelivery following a failed ack</param>
        /// <param name="exchange">the exchange used for the current operation</param>
        /// <param name="routingKey">the associated routing key</param>
        /// <returns>Envelope with the specified construction parameters</returns>
        public static Envelope Create(ulong deliveryTag, bool redeliver, string exchange, string routingKey)
        {
            return new Envelope(deliveryTag, redeliver, exchange, routingKey);
        }
    }
}
