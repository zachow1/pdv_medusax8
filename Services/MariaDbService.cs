using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;

namespace PDV_MedusaX8.Services
{
    public class MariaDbService
    {
        private readonly string _connectionString;

        public MariaDbService(string host = "localhost", int port = 3307, string database = "medusaX8", string user = "root", string password = "")
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = (uint)port,
                Database = database,
                UserID = user,
                Password = password,
                SslMode = MySqlSslMode.None,
                AllowUserVariables = true,
                CharacterSet = "utf8mb4",
                ConnectionTimeout = 5
            };
            _connectionString = builder.ConnectionString;
        }

        public async Task<MySqlConnection> OpenAsync()
        {
            var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            return conn;
        }

        public async Task<bool> TableExistsAsync(string table)
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t";
            cmd.Parameters.AddWithValue("@t", table);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return count > 0;
        }

        public async Task<DataTable> GetSchemaAsync(string table)
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_KEY, COLUMN_DEFAULT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION";
            cmd.Parameters.AddWithValue("@t", table);
            await using var reader = await cmd.ExecuteReaderAsync();
            var dt = new DataTable();
            dt.Load(reader);
            return dt;
        }

        public async Task<DataTable> QueryAsync(string sql, IDictionary<string, object>? parameters = null)
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (parameters != null)
            {
                foreach (var kv in parameters)
                    cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
            }
            await using var reader = await cmd.ExecuteReaderAsync();
            var dt = new DataTable();
            dt.Load(reader);
            return dt;
        }
    }
}