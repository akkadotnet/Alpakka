using System.Data.SqlClient;

namespace Akka.Streams.Sql.Tests
{
    public static class DbHelper
    {
        public static readonly string ConnectionString = "Server=.;Database=StreamsTest;Trusted_Connection=True;";
        public static void InitDb()
        {
            using (var conn = new SqlConnection(ConnectionString))
            using (var command = conn.CreateCommand())
            {
                conn.Open();

                command.CommandText = @"CREATE TABLE People (Id INT PRIMARY KEY, Name NVARCHAR(100) NOT NULL)";
                command.ExecuteNonQuery();
            }
        }

        public static void Cleanup()
        {
            using (var conn = new SqlConnection(ConnectionString))
            using (var command = conn.CreateCommand())
            {
                conn.Open();

                command.CommandText = @"DROP TABLE People";
                command.ExecuteNonQuery();
            }
        }

    }
}