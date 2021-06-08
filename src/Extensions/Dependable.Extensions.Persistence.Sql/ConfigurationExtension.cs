using System;

namespace Dependable.Extensions.Persistence.Sql
{
    public static class ConfigurationExtension
    {
        public static DependableConfiguration UseSqlPersistenceProvider(
            this DependableConfiguration configuration,
            string connectionString,
            string instanceName)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException($"A valid {nameof(connectionString)} is required.");

            if (string.IsNullOrWhiteSpace(instanceName))
                throw new ArgumentException($"A valid {nameof(instanceName)} is required.");


            return configuration.UsePersistenceProvider(new SqlPersistenceProvider(connectionString, instanceName));
        }
    }
}