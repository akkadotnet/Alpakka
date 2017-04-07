using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Sql.Internal;

namespace Akka.Streams.Sql
{
    /// <summary>
    /// A static class that may be used to run SQL non-query statements. 
    /// Objects comming from upstream are executed as SQL command parameters 
    /// using Dapper micro ORM.
    /// </summary>
    public static class SqlSink
    {
        /// <summary>
        /// <para>
        /// Creates a sink which uses existing SQL connection to insert series 
        /// of objects comming from upstream. Objects are inserted as command 
        /// params using Dapper micro ORM. I.e. <see cref="IEnumerable{T}"/> 
        /// objects will be translated into bulk inserts.
        /// 
        /// This implementation will only execute SQL statements one by one and 
        /// will block upstream from sending overwhelming amoung of requests.
        /// 
        /// Materialized value will complete after both upstream and the last 
        /// executed statement will finish and it will return total number of 
        /// records affected by executed commands during graph lifetime.
        /// </para>
        /// <para>
        /// Fails when upstream fails or connection executing SQL statement 
        /// fails.
        /// </para>
        /// <para>
        /// Finished when both upstream finishes and connection completed SQL 
        /// statement execution.
        /// </para>
        /// </summary>
        /// <typeparam name="T">
        /// Plain old class object type translated into SQL command params.
        /// </typeparam>
        /// <param name="connection">
        /// Opened connection to a database where <paramref name="sqlCommand"/> 
        /// is going to be executed.
        /// </param>
        /// <param name="sqlCommand">
        /// Non-query sql statement with params matching <typeparamref name="T"/> 
        /// type fields.
        /// </param>
        /// <returns></returns>
        public static Sink<T, Task<long>> Sink<T>(DbConnection connection, string sqlCommand)
        {
            return Dsl.Sink.FromGraph(new SqlSinkStage<T>(connection, sqlCommand));
        }
    }
}