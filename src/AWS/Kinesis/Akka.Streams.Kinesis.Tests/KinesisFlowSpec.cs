#region copyright
// -----------------------------------------------------------------------
// <copyright file="KinesisFlowSpec.cs" company="Bartosz Sypytkowski">
//     Copyright (C) 2019-2019 Bartosz Sypytkowski <b.sypytkowski@gmail.com>
// </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using NSubstitute;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Akka.Event;
using Amazon;
using Amazon.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Kinesis.Tests
{
    public class KinesisFlowSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly IAmazonKinesis _kinesisClient;
        private readonly TestPublisher.ManualProbe<string> _sourceProbe;
        private readonly TestSubscriber.ManualProbe<string> _sinkProbe;

        public KinesisFlowSpec(ITestOutputHelper output) : base("akka.loglevel = DEBUG", output)
        {
            _materializer = Sys.Materializer();
            _kinesisClient = NSubstitute.Substitute.For<IAmazonKinesis>();
            _sourceProbe = this.CreateManualPublisherProbe<string>();
            _sinkProbe = this.CreateManualSubscriberProbe<string>();

            Source.FromPublisher(_sourceProbe)
                .Select(x => new PutRecordsRequestEntry
                {
                    PartitionKey = "partition-1",
                    Data = new MemoryStream(Encoding.UTF8.GetBytes(x))
                })
                .Via(KinesisFlow.Create("test-stream", KinesisFlowSettings.Default, () => _kinesisClient))
                .Select(x => x.SequenceNumber)
                .To(Sink.FromSubscriber(_sinkProbe))
                .Run(_materializer);
        }

        [Fact]
        public void KinesisFlow_must_publish_records()
        {
            var input = new[] { "apple", "banana", "mango", "orange", "strawberry" };
            WithPutRecordsSuccess();

            var source = _sourceProbe.ExpectSubscription();
            var sink = _sinkProbe.ExpectSubscription();
            sink.Request(100);

            foreach (var record in input)
            {
                source.SendNext(record);
            }

            foreach (var result in input)
            {
                _sinkProbe.ExpectNext(result);
            }

            source.SendComplete();
            _sinkProbe.ExpectComplete();
        }

        private void WithPutRecordsSuccess()
        {
            _kinesisClient.PutRecordsAsync(Arg.Any<PutRecordsRequest>())
                .Returns(async info => new PutRecordsResponse
                {
                    EncryptionType = EncryptionType.NONE,
                    HttpStatusCode = HttpStatusCode.OK,
                    FailedRecordCount = 0,
                    Records = ((PutRecordsRequest)info[0]).Records.Select(r => new PutRecordsResultEntry
                    {
                        SequenceNumber = Encoding.UTF8.GetString(r.Data.ToArray())
                    }).ToList()
                });
        }

        [Fact]
        public async Task KinesisFlow_must_publish_records_with_retries()
        {
            var input = "apple";
            var retryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRetry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            WithPutRecordsInitialErrorsSuccessfulRetry(retryStarted, releaseRetry);

            var source = _sourceProbe.ExpectSubscription();
            var sink = _sinkProbe.ExpectSubscription();
            sink.Request(100);

            source.SendNext(input);

            try
            {
                await AwaitAssertAsync(() => Assert.True(retryStarted.Task.IsCompleted));
                Assert.Equal(2, _kinesisClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "PutRecordsAsync"));
                _sinkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                releaseRetry.SetResult(true);
                var retry = KinesisFlowSettings.Default.RetryInitialTimeout;
                _sinkProbe.ExpectNext(retry + retry, input);
                source.SendComplete();
                _sinkProbe.ExpectComplete();
            }
            finally { releaseRetry.TrySetResult(true); }
        }

        private void WithPutRecordsInitialErrorsSuccessfulRetry(TaskCompletionSource<bool> retryStarted, TaskCompletionSource<bool> releaseRetry)
        {
            _kinesisClient.PutRecordsAsync(Arg.Any<PutRecordsRequest>())
                .Returns(async info =>
                {
                    var req = ((PutRecordsRequest) info[0]);
                    return new PutRecordsResponse
                    {
                        FailedRecordCount = req.Records.Count,
                        Records = req.Records.Select(r => new PutRecordsResultEntry
                        {
                            ErrorCode = "error-code",
                            ErrorMessage = "error-message",
                            SequenceNumber = Encoding.UTF8.GetString(r.Data.ToArray())
                        }).ToList()
                    };
                }, async info =>
                {
                    retryStarted.TrySetResult(true);
                    await releaseRetry.Task;
                    return new PutRecordsResponse
                    {
                        EncryptionType = EncryptionType.NONE,
                        HttpStatusCode = HttpStatusCode.OK,
                        FailedRecordCount = 0,
                        Records = ((PutRecordsRequest)info[0]).Records.Select(r => new PutRecordsResultEntry
                        {
                            SequenceNumber = Encoding.UTF8.GetString(r.Data.ToArray())
                        }).ToList()
                    };
                });
        }
        
        private void WithPutRecordsWithPartialErrors()
        {
            _kinesisClient.PutRecordsAsync(Arg.Any<PutRecordsRequest>())
                .Returns(async info =>
                {
                    var req = ((PutRecordsRequest)info[0]);
                    return new PutRecordsResponse
                    {
                        FailedRecordCount = req.Records.Count,
                        Records = req.Records.Select(r => new PutRecordsResultEntry
                        {
                            ErrorCode = "error-code",
                            ErrorMessage = "error-message",
                            SequenceNumber = Encoding.UTF8.GetString(r.Data.ToArray())
                        }).ToList()
                    };
                });
        }

        [Fact]
        public void KinesisFlow_must_fail_after_trying_to_publish_records_with_several_retries()
        {
            WithPutRecordsWithPartialErrors();

            var source = _sourceProbe.ExpectSubscription();
            var sink = _sinkProbe.ExpectSubscription();
            sink.Request(100);

            source.SendNext("apple");

            var error = Assert.IsType<TestSubscriber.OnError>(_sinkProbe.ExpectEvent(TimeSpan.FromMinutes(1)));
            var failure = Assert.IsType<PublishingRecordsException>(error.Cause);
            Assert.Equal(KinesisFlowSettings.Default.MaxRetries + 1, failure.Attempt);
        }

        [Fact]
        public void KinesisFlow_must_fail_when_request_returns_an_error()
        {
            WithPutRecordsFailure();

            var source = _sourceProbe.ExpectSubscription();
            var sink = _sinkProbe.ExpectSubscription();
            sink.Request(100);

            source.SendNext("apple");

            var exception = _sinkProbe.ExpectError();
            Assert.True(ExceptionMessageContains(exception, "kinesis-error"));
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

        private void WithPutRecordsFailure()
        {
            _kinesisClient.PutRecordsAsync(Arg.Any<PutRecordsRequest>())
                .Returns(async info => throw new System.Exception("kinesis-error"));
        }
    }
}
