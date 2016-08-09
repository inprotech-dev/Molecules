using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace Dependable.Extensions.Persistence.Sql
{
    public static class DependableJobsTable
    {
        public static void Create(string connectionString)
        {
            var stream =
                Assembly.GetExecutingAssembly().GetManifestResourceStream("Dependable.Extensions.Persistence.Sql.DependableJobsTable.sql");

            if (stream == null)
                throw new InvalidOperationException("Unable to read DependableJobsTable resource.");

            using (var reader = new StreamReader(stream))
            using (var connection = new SqlConnection(connectionString))
            {
                connection.InfoMessage += connection_InfoMessage;
                var sql = reader.ReadToEnd();

                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        public static void Clean(string connectionString, string instanceName = null, Guid jobId = default (Guid))
        {
            const string commandText = "Delete from DependableJobs where {0} RootId in ( select Id from DependableJobs where ParentId is NULL and Status in ('Completed', 'Poisoned', 'Cancelled'))";
            var additionalConditions = string.Empty;
            var command = new SqlCommand();
            
            if (!string.IsNullOrWhiteSpace(instanceName))
            {
                additionalConditions = "InstanceName = @InstanceName and ";
                command.Parameters.AddWithValue("InstanceName", instanceName);
            }

            if (jobId != default (Guid))
            {
                additionalConditions += "RootId = @JobId and";
                command.Parameters.AddWithValue("JobId", jobId);
            }

            command.CommandText = string.Format(commandText, additionalConditions);

            var connection = new SqlConnection(connectionString);
            connection.InfoMessage += connection_InfoMessage;

            connection.Open();
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
                transaction.Commit();
            }
         }

        static void connection_InfoMessage(object sender, SqlInfoMessageEventArgs e)
        {
            Console.WriteLine(e.Message);
        }
    }
}