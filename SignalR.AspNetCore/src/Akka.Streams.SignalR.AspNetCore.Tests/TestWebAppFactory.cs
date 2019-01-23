using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using static Akka.Streams.SignalR.AspNetCore.Tests.TestServerAppFactory;

namespace Akka.Streams.SignalR.AspNetCore.Tests
{
    public interface IPublishSinkSource
    {
        void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink);
    }

    public class TestServerAppFactory : WebApplicationFactory<Startup>, IPublishSinkSource
    {
        private readonly ActorSystem _system;
        private readonly TestKitBase _testKit;
        private readonly int _flows;

        public TestSubscriber.Probe<ISignalREvent> FromClient { get; private set; }
        public TestPublisher.Probe<ISignalRResult> ToClient { get; private set; }
        public TestSubscriber.Probe<ISignalREvent> FromClient2 { get; private set; }
        public TestPublisher.Probe<ISignalRResult> ToClient2 { get; private set; }

        public TestServerAppFactory(ActorSystem system, TestKitBase testKit, int flows = 1)
        {
            _system = system;
            _testKit = testKit;
            _flows = flows;

            CreateClient(); // Need this to trigger test server creation
        }

        public void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink)
        {
            FromClient = source.RunWith(_testKit.SinkProbe<ISignalREvent>(), _system.Materializer());
            ToClient = sink.RunWith(_testKit.SourceProbe<ISignalRResult>(), _system.Materializer());

            if (_flows > 1) {
                FromClient2 = source.RunWith(_testKit.SinkProbe<ISignalREvent>(), _system.Materializer());
                ToClient2 = sink.RunWith(_testKit.SourceProbe<ISignalRResult>(), _system.Materializer());
            }
        }

        protected override IWebHostBuilder CreateWebHostBuilder()
        {
            return WebHost.CreateDefaultBuilder()
                .ConfigureServices(services => {
                    services.AddSignalRAkkaStream();
                    services.AddSignalR(opt => opt.EnableDetailedErrors = true);
                    services.Add(new ServiceDescriptor(typeof(IPublishSinkSource), this));
                    services.Add(new ServiceDescriptor(typeof(ActorSystem), _system));
                    services.Add(new ServiceDescriptor(typeof(TestKitBase), _testKit));
                })
                .Configure(app => {

                    app.UseSignalR(routes => {
                        routes.MapHub<TestStreamHub>("/test");
                    });
                });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <summary>
        /// Make a client connection to SignalR hub
        /// </summary>
        /// <returns></returns>
        public HubConnection CreateHubConnection()
        {
            var server = Server ?? CreateServer(CreateWebHostBuilder());

            var client = new HubConnectionBuilder()
                .WithUrl($"{server.BaseAddress}test", opt => {
                    opt.HttpMessageHandlerFactory = _ => server.CreateHandler();
                })
                .Build();

            return client;
        }


        public class Startup
        {
            public void ConfigureServices(IServiceCollection services) { }
            public void Configure(IApplicationBuilder app) { }
        }
    }


}
