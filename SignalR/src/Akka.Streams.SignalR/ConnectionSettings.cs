using System;
using System.IO;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Streams.SignalR
{
    public class ConnectionSinkSettings
    {
        public static readonly ConnectionSinkSettings Default = new ConnectionSinkSettings();

        public ConnectionSinkSettings()
        {
        }
    }

    public class ConnectionSourceSettings
    {
        public static readonly ConnectionSourceSettings Default = 
            new ConnectionSourceSettings(
                bufferCapacity: 128,
                overflowStrategy: OverflowStrategy.DropHead);

        public static ConnectionSourceSettings Create(ActorSystem system)
        {
            return Create(system.Settings.Config.GetConfig("akka.streams.signalr.source"));
        }

        public static ConnectionSourceSettings Create(Config config)
        {
            if (config == null) throw new ArgumentException("Config for SignalR stream connection source must be provided. Default HOCON config path is: 'akka.streams.signalr.source'.");

            Streams.OverflowStrategy overflowStrategy;
            var overflowConfig = config.GetString("overflow-strategy", "drop-head");
            switch (overflowConfig)
            {
                case "drop-head": overflowStrategy = OverflowStrategy.DropHead; break;
                case "drop-tail": overflowStrategy = OverflowStrategy.DropTail; break;
                case "drop-buffer": overflowStrategy = OverflowStrategy.DropBuffer; break;
                case "drop-new": overflowStrategy = OverflowStrategy.DropNew; break;
                case "fail": overflowStrategy = OverflowStrategy.Fail; break;
                case "backpressure": overflowStrategy = OverflowStrategy.Backpressure; break; // it's parsable, but an invalid option at the moment
                default: throw new ArgumentException($"{overflowConfig} is not valid overflow strategy setting. Available options: drop-head, drop-tail, drop-new, drop-buffer, fail.");
            }

            return new ConnectionSourceSettings(
                bufferCapacity: config.GetInt("buffer-capacity", 128),
                overflowStrategy: overflowStrategy);
        }

        /// <summary>
        /// Determines capacity of the buffer used in case, when SignalR messages are 
        /// comming faster than they can be processed by the downstream.
        /// </summary>
        public int BufferCapacity { get; }

        /// <summary>
        /// Determines the reaction of the buffer in case, when arriving messages
        /// will overflow <see cref="BufferCapacity"/>.
        /// 
        /// Keep in mind that <see cref="Akka.Streams.OverflowStrategy.Backpressure"/> is 
        /// not allowed option, as it's not supported by SignalR in its current version.
        /// </summary>
        public OverflowStrategy OverflowStrategy { get; }

        public ConnectionSourceSettings(int bufferCapacity, OverflowStrategy overflowStrategy)
        {
            if (overflowStrategy == OverflowStrategy.Backpressure) 
                throw new ArgumentException($"OverflowStrategy.Backpressure cannot be used with current version of SignalR connection source", nameof(overflowStrategy));

            BufferCapacity = bufferCapacity;
            OverflowStrategy = overflowStrategy;
        }

        public ConnectionSourceSettings WithBufferSize(int bufferSize) => Copy(bufferSize: bufferSize);
        public ConnectionSourceSettings WithOverflowStrategy(OverflowStrategy overflowStrategy) => Copy(overflowStrategy: overflowStrategy);

        private ConnectionSourceSettings Copy(
            int? bufferSize = null,
            OverflowStrategy? overflowStrategy = null) =>
            new ConnectionSourceSettings(
                bufferCapacity: bufferSize ?? this.BufferCapacity,
                overflowStrategy: overflowStrategy ?? this.OverflowStrategy);
    }
}