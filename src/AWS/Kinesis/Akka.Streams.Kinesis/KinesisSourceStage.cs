#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisSourceStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Stage;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;

namespace Akka.Streams.Kinesis
{
    internal sealed class KinesisSourceStage : GraphStage<SourceShape<Record>>
    {
        #region messages

        sealed class GetShardIteratorSuccess
        {
            public readonly GetShardIteratorResponse Response;

            public GetShardIteratorSuccess(GetShardIteratorResponse response)
            {
                Response = response;
            }
        }

        sealed class GetShardIteratorFailure
        {
            public readonly Exception Cause;

            public GetShardIteratorFailure(Exception cause)
            {
                Cause = cause;
            }
        }

        sealed class GetRecordsSuccess
        {
            public readonly GetRecordsResponse Response;

            public GetRecordsSuccess(GetRecordsResponse response)
            {
                Response = response;
            }
        }

        sealed class GetRecordsFailure
        {
            public readonly Exception Cause;

            public GetRecordsFailure(Exception cause)
            {
                Cause = cause;
            }
        }

        sealed class Pump
        {
            public static readonly Pump Instance = new Pump();
            private Pump() { }
        }

        #endregion

        #region logic

        sealed class Logic : TimerGraphStageLogic
        {
            private readonly ShardSettings _settings;
            private readonly Outlet<Record> _out;
            private readonly IAmazonKinesis _kinesisClient;

            private string _iterator;
            private readonly Queue<Record> _buffer = new Queue<Record>();
            private StageActor _actor;
            private IActorRef _self;
            private const string _timerKey = "GET_RECORDS";

            public Logic(KinesisSourceStage stage) : base(stage.Shape)
            {
                _settings = stage.Settings;
                _out = stage.Outlet;
                _kinesisClient = stage._clientFactory();

                SetHandler(_out, onPull: () => _self.Tell(Pump.Instance));
            }

            public override void PreStart()
            {
                _actor = GetStageActor(AwaitingShardIterator);
                _self = _actor.Ref;
                GetShardIterator();
            }

            private void GetShardIterator()
            {
                var useSequenceNumberCondition = (_settings.ShardIteratorType == ShardIteratorType.AFTER_SEQUENCE_NUMBER ||
                         _settings.ShardIteratorType == ShardIteratorType.AT_SEQUENCE_NUMBER) && !string.IsNullOrEmpty(_settings.StartingSequenceNumber);
                
                var request = new GetShardIteratorRequest
                {
                    ShardId = _settings.ShardId,
                    StreamName = _settings.StreamName,
                    ShardIteratorType = _settings.ShardIteratorType,
                    StartingSequenceNumber = useSequenceNumberCondition ? _settings.StartingSequenceNumber : null
                };

                if (_settings.AtTimestamp.HasValue)
                    request.Timestamp = _settings.AtTimestamp.Value;

                _kinesisClient.GetShardIteratorAsync(request)
                    .PipeTo(_self,
                        success: result => new GetShardIteratorSuccess(result),
                        failure: ex => new GetShardIteratorFailure(ex));
            }

            private void AwaitingShardIterator((IActorRef actorRef, object message) args)
            {
                switch (args.message)
                {
                    case GetShardIteratorSuccess s:
                        _iterator = s.Response.ShardIterator;
                        _actor.Become(AwaitingRecords);
                        RequestRecords(_self);
                        break;
                    case GetShardIteratorFailure f:
                        Log.Error(f.Cause, "Failed to get a shard iterator for shard {0}", _settings.ShardId);
                        FailStage(f.Cause);
                        break;
                }
            }

            private void RequestRecords(IActorRef self)
            {
                _kinesisClient.GetRecordsAsync(new GetRecordsRequest
                {
                    Limit = _settings.Limit,
                    ShardIterator = _iterator
                }).PipeTo(self, 
                    success: result => new GetRecordsSuccess(result),
                    failure: ex => new GetRecordsFailure(ex));
            }

            private void AwaitingRecords((IActorRef actorRef, object message) args)
            {
                switch (args.message)
                {
                    case GetRecordsSuccess s:
                        var records = s.Response.Records;
                        if (string.IsNullOrEmpty(s.Response.NextShardIterator))
                        {
                            Log.Info("Shard {0} returned a null iterator and will now complete.", _settings.ShardId);
                            CompleteStage();
                        }
                        else
                        {
                            _iterator = s.Response.NextShardIterator;
                        }

                        if (records.Count > 0)
                        {
                            foreach (var record in records)
                                _buffer.Enqueue(record);

                            _actor.Become(Ready);
                            _self.Tell(Pump.Instance);
                        }
                        else
                        {
                            ScheduleOnce(_timerKey, _settings.RefreshInterval);
                        }
                        break;
                    case GetRecordsFailure f:
                        Log.Error(f.Cause, "Failed to fetch records from Kinesis for shard {0}", _settings.ShardId);
                        FailStage(f.Cause);
                        break;
                }
            }

            private void Ready((IActorRef actorRef, object message) args)
            {
                if (args.message is Pump)
                {
                    if (IsAvailable(_out))
                    {
                        Push(_out, _buffer.Dequeue());
                        _self.Tell(Pump.Instance);
                    }

                    if (_buffer.Count == 0)
                    {
                        _actor.Become(AwaitingRecords);
                        RequestRecords(_self);
                    }
                }
            }

            protected override void OnTimer(object timerKey)
            {
                if (_timerKey == (string)timerKey)
                    RequestRecords(_self);
            }
        }

        #endregion

        public ShardSettings Settings { get; }
        private readonly Func<IAmazonKinesis> _clientFactory;

        public KinesisSourceStage(ShardSettings settings, Func<IAmazonKinesis> clientFactory)
        {
            Settings = settings;
            _clientFactory = clientFactory;
            Shape = new SourceShape<Record>(Outlet);
        }

        public Outlet<Record> Outlet { get; } = new Outlet<Record>("Records.out");

        public override SourceShape<Record> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) =>
            new Logic(this);
    }
}