using System.IO;

namespace Akka.Streams.SignalR
{
    public delegate void Serialize<in T>(T msg, TextWriter writer);

    public delegate T Deserialize<out T>(TextReader reader);

    public class ConnectionSinkSettings<T>
    {
        public static ConnectionSinkSettings<T> Default = new ConnectionSinkSettings<T>(
            serializer: (msg, writer) => {},
            dispatcherId: "akka.dispatchers.default");

        public ConnectionSinkSettings(Serialize<T> serializer, string dispatcherId)
        {
            Serializer = serializer;
            DispatcherId = dispatcherId;
        }

        public Serialize<T> Serializer { get; }
        public string DispatcherId { get; }
    }

    public class ConnectionSourceSettings<T>
    {
        public static ConnectionSourceSettings<T> Default = new ConnectionSourceSettings<T>(
            deserializer: reader => default(T),
            dispatcherId: "akka.dispatchers.default");

        public ConnectionSourceSettings(Deserialize<T> deserializer, string dispatcherId)
        {
            Deserializer = deserializer;
            DispatcherId = dispatcherId;
        }

        public Deserialize<T> Deserializer { get; }
        public string DispatcherId { get; }
    }
}