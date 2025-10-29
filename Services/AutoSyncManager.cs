using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PDV_MedusaX8.Services
{
    public class AutoSyncManager
    {
        private static readonly Lazy<AutoSyncManager> _lazy = new Lazy<AutoSyncManager>(() => new AutoSyncManager());
        public static AutoSyncManager Instance => _lazy.Value;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, DateTime> _lastRunByArea = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private string? _host;
        private int _port;
        private string? _db;
        private string? _user;
        private string? _pass;

        private AutoSyncManager() { }

        private static string GetConnectionString() => DbHelper.GetConnectionString();

        public void Configure(string host, int port, string db, string user, string pass)
        {
            _host = host;
            _port = port;
            _db = db;
            _user = user;
            _pass = pass;
            Log("CONFIG", "OK", $"Credenciais configuradas para {_host}:{_port}/{_db}");
        }

        public void EnsureLogTable()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS AutoSyncLog (
                                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        Date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                        Area TEXT NOT NULL,
                                        Status TEXT NOT NULL,
                                        Detail TEXT
                                    );";
                cmd.ExecuteNonQuery();
            }
            catch { /* ignore */ }
        }

        private void Log(string area, string status, string detail)
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO AutoSyncLog (Area, Status, Detail) VALUES ($a, $s, $d);";
                cmd.Parameters.AddWithValue("$a", area);
                cmd.Parameters.AddWithValue("$s", status);
                cmd.Parameters.AddWithValue("$d", string.IsNullOrWhiteSpace(detail) ? (object)DBNull.Value : detail);
                cmd.ExecuteNonQuery();
            }
            catch { /* ignore */ }
        }

        private bool IsConfigured() => !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_db) && !string.IsNullOrWhiteSpace(_user);

        private bool ShouldDebounce(string area, TimeSpan minInterval)
        {
            var now = DateTime.UtcNow;
            var last = _lastRunByArea.GetOrAdd(area, DateTime.MinValue);
            if (now - last < minInterval) return true;
            _lastRunByArea[area] = now;
            return false;
        }

        public void TriggerCustomersSync()
        {
            _ = TriggerInternalAsync("CLIENTES", async () =>
            {
                if (!IsConfigured()) { Log("CLIENTES", "SKIP", "Credenciais não configuradas"); return; }
                var api = new SyncApi();
                var count = await api.SyncParticipantesAsync(_host!, _port, _db!, _user!, _pass!);
                Log("CLIENTES", "OK", $"Sincronizados: {count}");
            });
        }

        public void TriggerProductsSync()
        {
            _ = TriggerInternalAsync("PRODUTOS", async () =>
            {
                if (!IsConfigured()) { Log("PRODUTOS", "SKIP", "Credenciais não configuradas"); return; }
                var api = new SyncApi();
                var count = await api.SyncProdutosAsync(_host!, _port, _db!, _user!, _pass!);
                Log("PRODUTOS", "OK", $"Sincronizados: {count}");
            });
        }

        public void TriggerFinancialSync()
        {
            // Placeholder: integração financeira ainda não implementada no SyncApi.
            _ = TriggerInternalAsync("FINANCEIRO", async () =>
            {
                await Task.CompletedTask;
                Log("FINANCEIRO", "PENDING", "Sincronização financeira não implementada neste build");
            });
        }

        public void TriggerNFCeSync()
        {
            // Placeholder: sincronização de NFC-e com ERP não implementada.
            _ = TriggerInternalAsync("NFCE", async () =>
            {
                await Task.CompletedTask;
                Log("NFCE", "PENDING", "Sincronização NFC-e não implementada neste build");
            });
        }

        private async Task TriggerInternalAsync(string area, Func<Task> action)
        {
            try
            {
                if (ShouldDebounce(area, TimeSpan.FromSeconds(2))) return;
                await _gate.WaitAsync();
                try
                {
                    await action();
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex)
            {
                Log(area, "ERR", ex.Message);
            }
        }
    }
}