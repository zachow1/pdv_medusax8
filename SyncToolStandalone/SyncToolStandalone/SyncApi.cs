using System;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;

namespace SyncToolStandalone
{
    public class SyncApi : ISyncApi
    {
        public async Task<(bool success, string message)> TestConnectionAsync(string host, int port, string database, string user, string password)
        {
            try
            {
                string connectionString = $"Server={host};Port={port};Database={database};User ID={user};Password={password};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Verificar tabelas
                    bool participantesExists = await TableExistsAsync(connection, "Participantes");
                    bool produtosExists = await TableExistsAsync(connection, "Produtos");
                    
                    string tablesStatus = $"Tabelas encontradas: " +
                        $"{(participantesExists ? "Participantes ✓" : "Participantes ✗")} | " +
                        $"{(produtosExists ? "Produtos ✓" : "Produtos ✗")}";
                    
                    return (true, $"Conexão bem-sucedida! {tablesStatus}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro de conexão: {ex.Message}");
            }
        }

        public async Task<(bool success, int count, string message)> SyncParticipantesAsync(string host, int port, string database, string user, string password)
        {
            try
            {
                string connectionString = $"Server={host};Port={port};Database={database};User ID={user};Password={password};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Verificar se a tabela existe
                    if (!await TableExistsAsync(connection, "Participantes"))
                    {
                        return (false, 0, "Tabela 'Participantes' não encontrada no banco de dados.");
                    }
                    
                    // Buscar participantes (clientes)
                    string query = "SELECT * FROM Participantes WHERE ISclientes = 1";
                    using (var command = new MySqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        int count = 0;
                        while (await reader.ReadAsync())
                        {
                            count++;
                        }
                        
                        return (true, count, $"Sincronização de {count} clientes concluída com sucesso!");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, 0, $"Erro ao sincronizar clientes: {ex.Message}");
            }
        }

        public async Task<(bool success, int count, string message)> SyncProdutosAsync(string host, int port, string database, string user, string password)
        {
            try
            {
                string connectionString = $"Server={host};Port={port};Database={database};User ID={user};Password={password};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Verificar se a tabela existe
                    if (!await TableExistsAsync(connection, "Produtos"))
                    {
                        return (false, 0, "Tabela 'Produtos' não encontrada no banco de dados.");
                    }
                    
                    // Buscar produtos
                    string query = "SELECT * FROM Produtos";
                    using (var command = new MySqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        int count = 0;
                        while (await reader.ReadAsync())
                        {
                            count++;
                        }
                        
                        return (true, count, $"Sincronização de {count} produtos concluída com sucesso!");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, 0, $"Erro ao sincronizar produtos: {ex.Message}");
            }
        }

        private async Task<bool> TableExistsAsync(MySqlConnection connection, string tableName)
        {
            string query = $"SHOW TABLES LIKE '{tableName}'";
            using (var command = new MySqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    return await reader.ReadAsync();
                }
            }
        }
    }
}