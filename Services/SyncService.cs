using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PDV_MedusaX8.Services
{
    public class SyncService
    {
        private readonly MariaDbService _mariaDb;

        public SyncService(MariaDbService mariaDb)
        {
            _mariaDb = mariaDb;
        }

        private static string MapMySqlToSqlite(string mysqlType)
        {
            mysqlType = mysqlType.ToLowerInvariant();
            if (mysqlType.Contains("int")) return "INTEGER";
            if (mysqlType.Contains("decimal") || mysqlType.Contains("numeric") || mysqlType.Contains("double") || mysqlType.Contains("float")) return "REAL";
            if (mysqlType.Contains("date") || mysqlType.Contains("time")) return "TEXT"; // ISO8601
            if (mysqlType.Contains("char") || mysqlType.Contains("text") || mysqlType.Contains("blob")) return "TEXT";
            return "TEXT";
        }

        private static async Task EnsureSqliteTableAsync(SqliteConnection sqlite, string table, DataTable mysqlSchema, HashSet<string>? exclude = null)
        {
            exclude ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var pragmaCmd = sqlite.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA table_info('{table}')";
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await pragmaCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    existingCols.Add(reader.GetString(1)); // name
            }

            if (existingCols.Count == 0)
            {
                // Create table
                var cols = new List<string>();
                foreach (DataRow row in mysqlSchema.Rows)
                {
                    var name = row["COLUMN_NAME"].ToString()!;
                    if (exclude.Contains(name)) continue;
                    var type = MapMySqlToSqlite(row["DATA_TYPE"].ToString()!);
                    cols.Add($"'{name}' {type}");
                }
                var createSql = $"CREATE TABLE IF NOT EXISTS '{table}' (" + string.Join(", ", cols) + ")";
                await using var createCmd = sqlite.CreateCommand();
                createCmd.CommandText = createSql;
                await createCmd.ExecuteNonQueryAsync();
            }
            else
            {
                // Add missing columns
                foreach (DataRow row in mysqlSchema.Rows)
                {
                    var name = row["COLUMN_NAME"].ToString()!;
                    if (exclude.Contains(name) || existingCols.Contains(name)) continue;
                    var type = MapMySqlToSqlite(row["DATA_TYPE"].ToString()!);
                    await using var alter = sqlite.CreateCommand();
                    alter.CommandText = $"ALTER TABLE '{table}' ADD COLUMN '{name}' {type}";
                    await alter.ExecuteNonQueryAsync();
                }
            }
        }

        private static async Task UpsertRowsAsync(SqliteConnection sqlite, string table, DataTable rows, HashSet<string>? exclude = null)
        {
            exclude ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rows.Rows.Count == 0) return;

            // Pick a reasonable key: prefer 'Id' or 'ID' or first column
            var cols = rows.Columns.Cast<DataColumn>().Select(c => c.ColumnName).Where(c => !exclude.Contains(c)).ToList();
            if (!cols.Any()) return;
            var key = cols.FirstOrDefault(c => string.Equals(c, "Id", StringComparison.OrdinalIgnoreCase)) ?? cols.First();

            foreach (DataRow r in rows.Rows)
            {
                var colNames = cols.ToList();
                var placeholders = cols.Select(c => "@" + c).ToList();
                var updates = cols.Where(c => !string.Equals(c, key, StringComparison.OrdinalIgnoreCase))
                                  .Select(c => $"'{c}' = excluded.'{c}'");
                var sql = $"INSERT INTO '{table}' (" + string.Join(", ", colNames.Select(c => $"'{c}'")) + ") VALUES (" + string.Join(", ", placeholders) + ") " +
                          $"ON CONFLICT('{key}') DO UPDATE SET " + string.Join(", ", updates);
                await using var cmd = sqlite.CreateCommand();
                cmd.CommandText = sql;
                foreach (var c in cols)
                {
                    var val = r[c];
                    cmd.Parameters.AddWithValue("@" + c, val == DBNull.Value ? null : val);
                }
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<int> MirrorTableAsync(string table, string? where = null, IDictionary<string, object>? parameters = null, HashSet<string>? exclude = null)
        {
            var schema = await _mariaDb.GetSchemaAsync(table);
            var sql = $"SELECT * FROM `{table}`" + (string.IsNullOrWhiteSpace(where) ? string.Empty : $" WHERE {where}");
            var data = await _mariaDb.QueryAsync(sql, parameters);

            await using var sqlite = new SqliteConnection(DbHelper.GetConnectionString());
            await sqlite.OpenAsync();
            await EnsureSqliteTableAsync(sqlite, table, schema, exclude);
            await UpsertRowsAsync(sqlite, table, data, exclude);
            return data.Rows.Count;
        }
    }
}