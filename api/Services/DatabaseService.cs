using MySqlConnector;
using api.DataAccess;

namespace MyApp.Namespace.Services
{
    public class DatabaseQuotaExceededException : Exception
    {
        public DatabaseQuotaExceededException(string message) : base(message) { }
    }

    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(Database database)
        {
            _connectionString = database.connectionString 
                ?? throw new InvalidOperationException("Connection string not found in Database class.");
        }

        private void HandleMySqlException(MySqlException ex)
        {
            if (ex.Message.Contains("exceeded the 'max_questions' resource"))
            {
                throw new DatabaseQuotaExceededException(
                    "Database query quota exceeded. Please wait for the hourly limit to reset.");
            }
            // If it's not a quota exception, let it propagate (will be rethrown in catch block)
        }

        /// <summary>
        /// Execute a query that returns data (SELECT statements)
        /// </summary>
        public async Task<List<Dictionary<string, object>>> QueryAsync(string sql, params object[] parameters)
        {
            try
            {
                var results = new List<Dictionary<string, object>>();

                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

            using var command = new MySqlCommand(sql, connection);
            
            // Add parameters if provided
            for (int i = 0; i < parameters.Length; i++)
            {
                command.Parameters.AddWithValue($"@p{i}", parameters[i]);
            }

                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }

                return results;
            }
            catch (MySqlException ex)
            {
                HandleMySqlException(ex);
                throw;
            }
        }

        /// <summary>
        /// Execute a query that returns a single value
        /// </summary>
        public async Task<object?> QueryScalarAsync(string sql, params object[] parameters)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                
                // Add parameters if provided
                for (int i = 0; i < parameters.Length; i++)
                {
                    command.Parameters.AddWithValue($"@p{i}", parameters[i]);
                }

                return await command.ExecuteScalarAsync();
            }
            catch (MySqlException ex)
            {
                HandleMySqlException(ex);
                throw;
            }
        }

        /// <summary>
        /// Execute a non-query command (INSERT, UPDATE, DELETE)
        /// </summary>
        public async Task<int> ExecuteAsync(string sql, params object[] parameters)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                
                // Add parameters if provided
                for (int i = 0; i < parameters.Length; i++)
                {
                    command.Parameters.AddWithValue($"@p{i}", parameters[i]);
                }

                return await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                HandleMySqlException(ex);
                throw;
            }
        }

        /// <summary>
        /// Get a raw connection for advanced scenarios
        /// </summary>
        public MySqlConnection GetConnection()
        {
            return new MySqlConnection(_connectionString);
        }

        /// <summary>
        /// Get a raw connection asynchronously for advanced scenarios
        /// </summary>
        public async Task<MySqlConnection> GetConnectionAsync()
        {
            try
            {
                var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                return connection;
            }
            catch (MySqlException ex)
            {
                HandleMySqlException(ex);
                throw;
            }
        }
    }
}

