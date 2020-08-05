#region copyright
// -----------------------------------------------------------------------
// <copyright file="KinesisSourceSpec.cs" company="Bartosz Sypytkowski">
//     Copyright (C) 2019-2019 Bartosz Sypytkowski <b.sypytkowski@gmail.com>
// </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using FluentAssertions.Extensions;
using NSubstitute;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Record = Amazon.Kinesis.Model.Record;

namespace Akka.Streams.Kinesis.Tests
{
    public class KinesisSourceSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly IAmazonKinesis _kinesisClient;
        private readonly ShardSettings _settings;

        private int _nextShardIterator = 1;

        public KinesisSourceSpec(ITestOutputHelper output) : base("akka.loglevel=DEBUG", output)
        {
            _materializer = Sys.Materializer();
            _kinesisClient = NSubstitute.Substitute.For<IAmazonKinesis>();
            _settings = ShardSettings.Create("test-stream", "shard-id")
                .WithShardIteratorType(ShardIteratorType.TRIM_HORIZON)
                .WithRefreshInterval(1.Seconds())
                .WithLimit(500);
        }

        [Fact]
        public void KinesisSource_must_poll_for_records()
        {
            var data = new[] {"a", "b"};
            WithGetShardIteratorSuccess();
            WithGetRecordsSuccess(data);

            var probe = this.CreateManualSubscriberProbe<string>();
            KinesisSource.Basic(_settings, () => _kinesisClient)
                .Select(x => Encoding.UTF8.GetString(x.Data.ToArray()))
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(10);

            probe.ExpectNext("a");
            probe.ExpectNext("b");
            probe.ExpectNext("a");
            probe.ExpectNext("b");
        }

        private void WithGetShardIteratorSuccess()
        {
            _kinesisClient.GetShardIteratorAsync(Arg.Any<GetShardIteratorRequest>())
                .Returns(async info => new GetShardIteratorResponse
                {
                    ShardIterator = "next-" + (_nextShardIterator-1).ToString()
                });
        }

        private void WithGetRecordsSuccess(params string[] payload)
        {
            _kinesisClient.GetRecordsAsync(Arg.Any<GetRecordsRequest>())
                .Returns(async info => new GetRecordsResponse
                {
                    Records = payload
                        .Select(x => new Record { Data = new MemoryStream(Encoding.UTF8.GetBytes(x)) })
                        .ToList(),
                    NextShardIterator = _nextShardIterator < 0 ? null : "next-" + (Interlocked.Increment(ref _nextShardIterator).ToString())
                });
        }

        [Fact]
        public void KinesisSource_must_poll_for_records_with_multiple_requests()
        {
            var data = new[] { "a", "b" };
            WithGetShardIteratorSuccess();
            WithGetRecordsSuccess(data);

            var probe = this.CreateManualSubscriberProbe<string>();
            KinesisSource.Basic(_settings, () => _kinesisClient)
                .Select(x => Encoding.UTF8.GetString(x.Data.ToArray()))
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();

            subscription.Request(2);
            probe.ExpectNext("a");
            probe.ExpectNext("b");

            probe.ExpectNoMsg(1.Seconds());

            subscription.Request(2);
            probe.ExpectNext("a");
            probe.ExpectNext("b");
        }

        [Fact]
        public void KinesisSource_must_wait_for_request_before_passing_downstream()
        {
            var data = new[] { "a", "b", "c", "d", "e", "f" };
            WithGetShardIteratorSuccess();
            WithGetRecordsSuccess(data);

            var probe = this.CreateManualSubscriberProbe<string>();
            KinesisSource.Basic(_settings, () => _kinesisClient)
                .Select(x => Encoding.UTF8.GetString(x.Data.ToArray()))
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();

            subscription.Request(1);
            probe.ExpectNext("a");

            subscription.Request(6);
            probe.ExpectNext("b");
            probe.ExpectNext("c");
            probe.ExpectNext("d");
            probe.ExpectNext("e");
            probe.ExpectNext("f");
            probe.ExpectNext("a");
        }

        [Fact]
        public void KinesisSource_must_complete_stage_when_shard_iterator_is_null()
        {
            WithGetShardIteratorSuccess();
            WithGetRecordsSuccess("a");

            var probe = this.CreateManualSubscriberProbe<string>();
            KinesisSource.Basic(_settings, () => _kinesisClient)
                .Select(x => Encoding.UTF8.GetString(x.Data.ToArray()))
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();

            subscription.Request(1);
            probe.ExpectNext("a");

            _nextShardIterator = -10;
            subscription.Request(1);
            probe.ExpectNext();
            probe.ExpectComplete();
        }

        private void WithGetRecordsFailure()
        {
            _kinesisClient.GetRecordsAsync(Arg.Any<GetRecordsRequest>())
                .Returns(async info => throw new System.Exception("kinesis-records-error"));
        }

        [Fact]
        public void KinesisSource_must_fail_when_kinesis_client_fails()
        {
            WithGetShardIteratorSuccess();
            WithGetRecordsFailure();

            var probe = this.CreateManualSubscriberProbe<string>();
            KinesisSource.Basic(_settings, () => _kinesisClient)
                .Select(x => Encoding.UTF8.GetString(x.Data.ToArray()))
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(1);
            var exception = probe.ExpectError();
            ExceptionMessageContains(exception, "kinesis-records-error").Should().BeTrue();
        }

        private static bool ExceptionMessageContains(Exception ex, string expected)
        {
            while (ex != null)
            {
                if (ex.Message.Contains(expected))
                    return true;
                ex = ex.InnerException;
            }

            return false;
        }
    }
}