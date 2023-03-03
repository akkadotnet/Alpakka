#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisFlowStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using Akka.Streams.Stage;
using Amazon.Kinesis.Model;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Amazon.Kinesis;

namespace Akka.Streams.Kinesis
{
    public class PublishingRecordsException : Exception
    {
        public int Attempt { get; }
        public IEnumerable<(PutRecordsRequestEntry, PutRecordsResultEntry)> Errors { get; }

        public PublishingRecordsException(int attempt, IEnumerable<(PutRecordsRequestEntry, PutRecordsResultEntry)> errors)
        {
            Attempt = attempt;
            Errors = errors;
        }
    }

    internal sealed class KinesisFlowStage : GraphStage<FlowShape<ImmutableQueue<PutRecordsRequestEntry>, Task<IEnumerable<PutRecordsResultEntry>>>>
    {
        #region internal messages

        internal sealed class Job
        {
            public readonly int Attempt;
            public readonly IEnumerable<PutRecordsRequestEntry> Records;

            public Job(int attempt, IEnumerable<PutRecordsRequestEntry> records)
            {
                Attempt = attempt;
                Records = records;
            }
        }

        internal sealed class Result
        {
            public readonly int Attempt;
            public readonly bool WasSuccessful;
            public readonly IEnumerable<(PutRecordsRequestEntry, PutRecordsResultEntry)> RecordsToRetry;

            public Result(int attempt, bool wasSuccessful, IEnumerable<(PutRecordsRequestEntry, PutRecordsResultEntry)> recordsToRetry)
            {
                Attempt = attempt;
                RecordsToRetry = recordsToRetry;
                WasSuccessful = wasSuccessful;
            }
        }
        #endregion

        private readonly string _streamName;
        private readonly int _maxRetries;
        private readonly RetryBackoffStrategy _backoffStrategy;
        private readonly TimeSpan _retryInitialTimeout;
        private readonly Func<IAmazonKinesis> _clientFactory;
        private Inlet<ImmutableQueue<PutRecordsRequestEntry>> Inlet { get; } = new Inlet<ImmutableQueue<PutRecordsRequestEntry>>("KinesisFlowStage.in");
        private Outlet<Task<IEnumerable<PutRecordsResultEntry>>> Outlet { get; } = new Outlet<Task<IEnumerable<PutRecordsResultEntry>>>("KinesisFlowStage.out");

        #region logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly string _streamName;
            private readonly Inlet<ImmutableQueue<PutRecordsRequestEntry>> _in;
            private readonly Outlet<Task<IEnumerable<PutRecordsResultEntry>>> _out;
            private readonly IAmazonKinesis _kinesisClient;

            private readonly Queue<Job> _pendingRequests = new Queue<Job>();
            private Action<Result> _resultCallback;
            private readonly int _maxRetries;
            private readonly RetryBackoffStrategy _backoffStrategy;
            private readonly TimeSpan _retryInitialTimeout;
            private int _inFlight;
            private object _completionState;

            private readonly Dictionary<int, Job> _waitingRetries = new Dictionary<int, Job>();
            private int _retryToken;

            public Logic(KinesisFlowStage stage) : base(stage.Shape)
            {
                _streamName = stage._streamName;
                _maxRetries = stage._maxRetries;
                _backoffStrategy = stage._backoffStrategy;
                _retryInitialTimeout = stage._retryInitialTimeout;
                _in = stage.Inlet;
                _out = stage.Outlet;
                _kinesisClient = stage._clientFactory();

                SetHandler(_in, this);
                SetHandler(_out, this);
            }

            private void TryToExecute()
            {
                if (_pendingRequests.Count != 0 && IsAvailable(_out))
                {
                    _inFlight++;
                    var job = _pendingRequests.Dequeue();
                    Push(_out, PutRecords(_streamName, job.Records, recordsToRetry => _resultCallback(new Result(job.Attempt, recordsToRetry.Length == 0, recordsToRetry))));
                } else if (Log.IsDebugEnabled && !IsAvailable(_out))
                    Log.Debug("Found pending requests for stream [{0}] but outlet [{1}] is not available.", _streamName, _out.Name);
            }

            private async Task<IEnumerable<PutRecordsResultEntry>> PutRecords(
                string streamName, 
                IEnumerable<PutRecordsRequestEntry> recordEntries, 
                Action<(PutRecordsRequestEntry, PutRecordsResultEntry)[]> retryRecordsCallback)
            {
                var request = new PutRecordsRequest
                {
                    StreamName = streamName,
                    Records = recordEntries.ToList()
                };
                if (Log.IsDebugEnabled)
                    Log.Debug("Attempting to put {0} records into stream [{1}].", request.Records.Count, request.StreamName);
                var result = await _kinesisClient.PutRecordsAsync(request);

                if (result.FailedRecordCount > 0)
                {
                    if (Log.IsDebugEnabled)
                        Log.Debug(
                            "PutRecords call successfully put {0} records and failed to put {1} records into stream [{2}]. Failed shard ids: [{3}]",
                            result.Records.Count - result.FailedRecordCount,
                            result.FailedRecordCount,
                            _streamName,
                            string.Join(", ", result.Records
                                .Where(r => r.ErrorCode != null)
                                .Select(r => r.ShardId)
                                .Distinct()));

                    var correlatedRequestResult = request.Records.Zip(result.Records, (req, rep) => (req, rep));
                    retryRecordsCallback(correlatedRequestResult.Where(rep => !(rep.Item2.ErrorCode is null)).ToArray());
                }
                else
                {
                    if (Log.IsDebugEnabled)
                        Log.Debug("PutRecords call successfully put {0} records into stream [{1}].",
                            result.Records.Count - result.FailedRecordCount,
                            _streamName);
                    retryRecordsCallback(Array.Empty<(PutRecordsRequestEntry, PutRecordsResultEntry)>());
                }

                return result.Records.Where(rep => string.IsNullOrEmpty(rep.ErrorCode));
            }

            private void HandleResult(Result result)
            {
                if (result.WasSuccessful)
                {
                    if(Log.IsDebugEnabled)
                        Log.Debug("PutRecords call finished successfully on stream [{0}]. Total attempts: {1}", _streamName, result.Attempt);

                    _inFlight--;
                    TryToExecute();
                    if (!HasBeenPulled(_in)) TryPull(_in);
                }
                else if (result.Attempt > _maxRetries)
                {
                    Log.Warning("PutRecords call finished with partial errors after {0} attempts on stream [{1}], failing stream. Failed count: {2}", 
                        result.Attempt, 
                        _streamName,
                        result.RecordsToRetry.Count());
                    FailStage(new PublishingRecordsException(result.Attempt, result.RecordsToRetry));
                }
                else
                {
                    _inFlight--;
                    _waitingRetries[_retryToken] = new Job(result.Attempt+1, result.RecordsToRetry.Select(t => t.Item1));
                    TimeSpan delay;
                    switch (_backoffStrategy)
                    {
                        case RetryBackoffStrategy.Exponential:
                            delay = new TimeSpan((long)(_retryInitialTimeout.Ticks * Math.Pow(2, result.Attempt -1)));
                            break;
                        case RetryBackoffStrategy.Linear:
                            delay = new TimeSpan(_retryInitialTimeout.Ticks * result.Attempt);
                            break;
                        default:
                            throw new NotSupportedException($"Unknown backoff strategy: {_backoffStrategy}. Supported backoff strategies are {nameof(RetryBackoffStrategy.Exponential)} and {nameof(RetryBackoffStrategy.Linear)}");
                    }
                    if (Log.IsDebugEnabled)
                        Log.Debug("Retrying to put {0} records into stream {1} in {2} seconds.", 
                            result.RecordsToRetry.Count(),
                            _streamName,
                            delay.TotalSeconds, 
                            result.Attempt + 1);

                    ScheduleOnce(_retryToken, delay);
                }
            }

            private void CheckForCompletion()
            {
                if (_inFlight == 0 && _pendingRequests.Count == 0 && _waitingRetries.Count == 0 && IsClosed(_in))
                {
                    switch (_completionState)
                    {
                        case Status.Success _:
                            if (Log.IsDebugEnabled)
                                Log.Debug("Stage completed on stream [{0}]", _streamName);
                            CompleteStage(); 
                            break;
                        case Status.Failure f:
                            if (Log.IsDebugEnabled)
                                Log.Debug("Stage failed on stream [{0}]. Reason: {1}", _streamName, f.Cause);
                            FailStage(f.Cause); 
                            break;
                        case null: FailStage(new IllegalStateException($"Stage completed on stream [{_streamName}], but there is no info about status")); break;
                    }
                }
            }

            protected override void OnTimer(object timerKey)
            {
                var key = (int) timerKey;
                if (_waitingRetries.TryGetValue(key, out var job))
                {
                    _waitingRetries.Remove(key);
                    if (Log.IsDebugEnabled)
                        Log.Debug("Retrying to PutRecords again. Attempt: {0}", job.Attempt);
                    _pendingRequests.Enqueue(job);
                    TryToExecute();
                }
            }

            public override void PreStart()
            {
                _completionState = null;
                _inFlight = 0;
                _retryToken = 0;
                _resultCallback = this.GetAsyncCallback<Result>(HandleResult);
                Pull(_in);
            }

            public override void PostStop()
            {
                _pendingRequests.Clear();
                _waitingRetries.Clear();
                _kinesisClient.Dispose();
            }

            public void OnPush()
            {
                var job = new Job(1, Grab(_in));
                if(Log.IsDebugEnabled)
                    Log.Debug("Enqueuing a new job with {0} records for stream {1}", job.Records.Count(), _streamName);
                _pendingRequests.Enqueue(job);
                TryToExecute();
            }

            public void OnUpstreamFinish()
            {
                _completionState = new Status.Success(null);
                CheckForCompletion();
            }

            public void OnUpstreamFailure(Exception e)
            {
                _completionState = new Status.Failure(e);
                CheckForCompletion();
            }

            public void OnPull()
            {
                TryToExecute();
                if (_waitingRetries.Count == 0 && !HasBeenPulled(_in)) TryPull(_in);
            }

            public void OnDownstreamFinish(Exception e)
            {
                CompleteStage();
            }
        }

        #endregion
        
        public KinesisFlowStage(
            string streamName, 
            int maxRetries, 
            RetryBackoffStrategy backoffStrategy, 
            TimeSpan retryInitialTimeout, 
            Func<IAmazonKinesis> clientFactory)
        {
            _streamName = streamName;
            _maxRetries = maxRetries;
            _backoffStrategy = backoffStrategy;
            _retryInitialTimeout = retryInitialTimeout;
            _clientFactory = clientFactory;
            Shape = new FlowShape<ImmutableQueue<PutRecordsRequestEntry>, Task<IEnumerable<PutRecordsResultEntry>>>(Inlet, Outlet);
        }

        public override FlowShape<ImmutableQueue<PutRecordsRequestEntry>, Task<IEnumerable<PutRecordsResultEntry>>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) =>
            new Logic(this);
    }
}