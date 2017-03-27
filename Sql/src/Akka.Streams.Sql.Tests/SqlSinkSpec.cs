using System;
using System.Collections.Immutable;
using System.Data.Common;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Sql.Internal;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Sql.Tests
{
    public sealed class Person
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class SqlSinkSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer materializer;

        public SqlSinkSpec(ITestOutputHelper output) : base(output: output)
        {
            materializer = Sys.Materializer();
            DbHelper.InitDb();
        }

        [Fact]
        public void SqlSink_should_execute_insert_commands()
        {
            var sql = @"INSERT INTO People(Id, Name) VALUES(@Id, @Name)";
            using (var connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                
                var sink = CreateTestProbe();
                var input = new[] {
                    new Person { Id = 1, Name = "John" },
                    new Person { Id = 2, Name = "Amy" },
                    new Person { Id = 3, Name = "Billy" },
                };
                var completed = Source.From(input)
                    .ToMaterialized(TestSink<Person>(sink, connection, sql), Keep.Right)
                    .Run(materializer);

                sink.ExpectMsg<GraphStageMessages.Push>();
                sink.ExpectMsg<GraphStageMessages.Push>();
                sink.ExpectMsg<GraphStageMessages.Push>();
                sink.ExpectMsg<GraphStageMessages.UpstreamFinish>();

                completed.Wait(TimeSpan.FromSeconds(5));
                completed.IsFaulted.Should().BeFalse();
                completed.IsCanceled.Should().BeFalse();
                completed.Result.Should().Be(3L);
            }
        }

        [Fact]
        public void SqlSink_should_turn_conflate_into_bulk_insert_commands()
        {
            var sql = @"INSERT INTO People(Id, Name) VALUES(@Id, @Name)";
            using (var connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();

                var sink = CreateTestProbe();
                var input = new[] {
                    new Person { Id = 1, Name = "John" },
                    new Person { Id = 2, Name = "Amy" },
                    new Person { Id = 3, Name = "Billy" },
                    new Person { Id = 4, Name = "Marceline" },
                };
                var completed = Source.From(input)
                    .ConflateWithSeed(ImmutableList.Create, (acc, person) => acc.Add(person))
                    .ToMaterialized(TestSink<ImmutableList<Person>>(sink, connection, sql), Keep.Right)
                    .Run(materializer);

                sink.ExpectMsg<GraphStageMessages.Push>(); // 1st push is immediate
                sink.ExpectMsg<GraphStageMessages.Push>(); // 3 pushes are conflated into a single one, 
                                                           //   since we're waiting for insert to complete
                sink.ExpectMsg<GraphStageMessages.UpstreamFinish>();

                completed.Wait(TimeSpan.FromSeconds(5));
                completed.Result.Should().Be(4);
            }
        }

        protected override void Dispose(bool disposing)
        {
            materializer.Dispose();
            base.Dispose(disposing);
            DbHelper.Cleanup();
        }

        private TestSinkStage<T, Task<long>> TestSink<T>(TestProbe probe, DbConnection connection, string sql) =>
            TestSinkStage<T, Task<long>>.Create(new SqlSinkStage<T>(connection, sql), probe);
    }
}