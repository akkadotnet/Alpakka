using System;
using System.Data.Common;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using Dapper;

namespace Akka.Streams.Sql.Internal
{
    internal sealed class SqlSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<long>>
    {
        private readonly DbConnection connection;
        private readonly string sql;
        private readonly Inlet<T> inlet = new Inlet<T>("dapper.in");
        private readonly TaskCompletionSource<long> completion;

        public SqlSinkStage(DbConnection connection, string sql)
        {
            this.connection = connection;
            this.sql = sql;
            completion = new TaskCompletionSource<long>();
            Shape = new SinkShape<T>(inlet);
        }

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Task<long>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            return new LogicAndMaterializedValue<Task<long>>(new Logic(this), completion.Task);
        }

        #region logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly SqlSinkStage<T> stage;
            private readonly Action<int> onSuccess;
            private readonly Action<Exception> onFailure;
            private long executedResult = 0;
            
            // those 2 flags are required in order to graceful stream completion
            // for that to happen both shutdown must have been called
            // and the last operation must completed (this way we prevent from
            // a premature completion of materialized value)
            private bool isShutdownInProgress = false;
            private bool isOperationInProgress = false;

            public Logic(SqlSinkStage<T> stage) : base(stage.Shape)
            {
                this.stage = stage;
                this.onSuccess = GetAsyncCallback<int>(HandleSuccess);
                this.onFailure = GetAsyncCallback<Exception>(HandleFailure);

                SetHandler(stage.inlet, this);
            }

            public override void PreStart()
            {
                // this stage must keep running after upstream finished
                // it's necessary to complete the last request first before calling for completion
                // all stream completion events must be called manually this way
                SetKeepGoing(true);
                Pull(stage.inlet);
            }

            public override void OnPush()
            {
                var msg = Grab(stage.inlet);
                isOperationInProgress = true;
                stage.connection
                    .ExecuteAsync(stage.sql, msg)
                    .ContinueWith(HandleContinuation);
            }

            public override void OnUpstreamFailure(Exception cause)
            {
                isShutdownInProgress = true;
                FailStage(cause);
                stage.completion.SetException(cause);
            }

            public override void OnUpstreamFinish()
            {
                isShutdownInProgress = true;
                TryShutdown();
            }

            private void HandleContinuation(Task<int> task)
            {
                if (task.IsFaulted || task.IsCanceled)
                    onFailure(task.Exception);
                else
                    onSuccess(task.Result);
            }

            private void HandleFailure(Exception cause)
            {
                isOperationInProgress = false;
                FailStage(cause);
                stage.completion.SetException(cause);
            }

            private void HandleSuccess(int scalar)
            {
                executedResult += scalar;
                isOperationInProgress = false;
                TryShutdown();
                TryPull(stage.inlet);
            }
            
            private void TryShutdown()
            {
                if (isShutdownInProgress && !isOperationInProgress)
                {
                    CompleteStage();
                    stage.completion.SetResult(executedResult);
                }
            }
        }

        #endregion
    }
}
