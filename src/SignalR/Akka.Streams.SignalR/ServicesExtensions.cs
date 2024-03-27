using Akka.Streams.SignalR.Internals;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.Streams.SignalR
{
    public static class AkkaSignalRDependencyInjectionExtensions
    {
        /// <summary>
        /// Add SignalR Akka Stream connector
        /// </summary>
        /// <param name="services"></param>
        public static IServiceCollection AddSignalRAkkaStream(this IServiceCollection services)
        {
            services.AddSingleton<IStreamDispatcher, DefaultStreamDispatcher>();
            return services;
        }
    }
}
