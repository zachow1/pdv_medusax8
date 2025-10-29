using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace PDV_MedusaX8.Services
{
    public class SyncApi : ISyncApi
    {
        public async Task<bool> TestConnectionAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report($"Conectando a {host}:{port}/{database}...");
                var maria = new MariaDbService(host, port, database, user, password);
                var okParticipantes = await maria.TableExistsAsync("Participantes");
                var okProdutos = await maria.TableExistsAsync("Produtos");
                progress?.Report($"Tabelas: Participantes={(okParticipantes ? "OK" : "NÃO ENCONTRADA")}, Produtos={(okProdutos ? "OK" : "NÃO ENCONTRADA")}");
                return okParticipantes && okProdutos;
            }
            catch (Exception ex)
            {
                progress?.Report($"Falha na conexão: {ex.Message}");
                return false;
            }
        }

        public async Task<int> SyncParticipantesAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null)
        {
            progress?.Report("Iniciando sincronização de Participantes (clientes)...");
            var maria = new MariaDbService(host, port, database, user, password);
            var sync = new SyncService(maria);
            var count = await sync.MirrorTableAsync("Participantes", "ISclientes = 1");
            progress?.Report($"Participantes sincronizados: {count}");
            return count;
        }

        public async Task<int> SyncProdutosAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null)
        {
            progress?.Report("Iniciando sincronização de Produtos...");
            var maria = new MariaDbService(host, port, database, user, password);
            var sync = new SyncService(maria);
            var count = await sync.MirrorTableAsync("Produtos");
            progress?.Report($"Produtos sincronizados: {count}");
            return count;
        }

        public async Task<int> SyncUsuariosAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null)
        {
            progress?.Report("Iniciando sincronização de Usuários...");
            var maria = new MariaDbService(host, port, database, user, password);
            string? srcTable = null;
            if (await maria.TableExistsAsync("Usuarios")) srcTable = "Usuarios";
            else if (await maria.TableExistsAsync("Users")) srcTable = "Users";

            if (srcTable == null)
            {
                progress?.Report("Tabela 'Usuarios'/'Users' não encontrada no ERP.");
                return 0;
            }

            var sync = new SyncService(maria);
            var imported = await sync.MirrorTableAsync(srcTable);
            progress?.Report($"Registros importados do ERP ({srcTable}): {imported}");

            var updated = await MapUsuariosToLocalUsersAsync(srcTable, progress);
            progress?.Report($"Usuários atualizados no PDV: {updated}");
            return updated;
        }

        public async Task<int> SyncEmpresaAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null)
        {
            progress?.Report("Sincronizando cadastro da Empresa...");
            var maria = new MariaDbService(host, port, database, user, password);
            string? srcTable = null;
            if (await maria.TableExistsAsync("Empresa")) srcTable = "Empresa";
            else if (await maria.TableExistsAsync("Empresas")) srcTable = "Empresas";

            if (srcTable == null)
            {
                progress?.Report("Tabela 'Empresa/Empresas' não encontrada no ERP.");
                return 0;
            }

            var sync = new SyncService(maria);
            var imported = await sync.MirrorTableAsync(srcTable);
            progress?.Report($"Registros importados do ERP ({srcTable}): {imported}");
            var updated = await MapEmpresaToLocalAsync(srcTable, progress);
            progress?.Report($"Empresa atualizada no PDV: {updated}");
            return updated;
        }

        public async Task<int> SyncContadorAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null)
        {
            progress?.Report("Sincronizando cadastro do Contador...");
            var maria = new MariaDbService(host, port, database, user, password);
            string? srcTable = null;
            if (await maria.TableExistsAsync("Contador")) srcTable = "Contador";
            else if (await maria.TableExistsAsync("Contadores")) srcTable = "Contadores";

            if (srcTable == null)
            {
                progress?.Report("Tabela 'Contador/Contadores' não encontrada no ERP.");
                return 0;
            }

            var sync = new SyncService(maria);
            var imported = await sync.MirrorTableAsync(srcTable);
            progress?.Report($"Registros importados do ERP ({srcTable}): {imported}");
            var updated = await MapContadorToLocalAsync(srcTable, progress);
            progress?.Report($"Contador atualizado no PDV: {updated}");
            return updated;
        }

        private static async Task<int> MapUsuariosToLocalUsersAsync(string srcTable, IProgress<string>? progress)
        {
            await using var sqlite = new SqliteConnection(DbHelper.GetConnectionString());
            await sqlite.OpenAsync();

            // Garantir índice único em Username para UPSERT por Username
            await using (var idx = sqlite.CreateCommand())
            {
                idx.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_users_username ON Users(Username)";
                await idx.ExecuteNonQueryAsync();
            }

            // Ler todos os registros da tabela espelhada
            await using var cmdRead = sqlite.CreateCommand();
            cmdRead.CommandText = $"SELECT * FROM '{srcTable}'";
            await using var reader = await cmdRead.ExecuteReaderAsync();

            int affected = 0;
            while (await reader.ReadAsync())
            {
                var username = ReadFirst(reader, "Username", "Login", "usuario", "email", "user");
                if (string.IsNullOrWhiteSpace(username)) continue;

                var passwordRaw = ReadFirst(reader, "PasswordHash", "Senha", "password");
                var passwordHash = NormalizePasswordHash(passwordRaw);
                var roleRaw = ReadFirst(reader, "Role", "Perfil", "tipo", "nivel");
                var role = NormalizeRole(roleRaw);
                var activeRaw = ReadFirst(reader, "Active", "Ativo", "Status", "situacao", "ativo");
                var active = ParseActive(activeRaw);
                var externalId = ReadFirst(reader, "Id", "ID", "ExternalId");

                await using var upsert = sqlite.CreateCommand();
                upsert.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, Role, Active, ExternalId, CreatedAt, UpdatedAt)
                    VALUES ($u, $p, $r, $a, $e, COALESCE($created, CURRENT_TIMESTAMP), CURRENT_TIMESTAMP)
                    ON CONFLICT(Username) DO UPDATE SET
                        PasswordHash = COALESCE(excluded.PasswordHash, Users.PasswordHash),
                        Role = excluded.Role,
                        Active = excluded.Active,
                        ExternalId = COALESCE(excluded.ExternalId, Users.ExternalId),
                        UpdatedAt = CURRENT_TIMESTAMP";
                upsert.Parameters.AddWithValue("$u", username);
                upsert.Parameters.AddWithValue("$p", string.IsNullOrEmpty(passwordHash) ? (object)DBNull.Value : passwordHash);
                upsert.Parameters.AddWithValue("$r", role);
                upsert.Parameters.AddWithValue("$a", active ? 1 : 0);
                upsert.Parameters.AddWithValue("$e", string.IsNullOrEmpty(externalId) ? (object)DBNull.Value : externalId);
                upsert.Parameters.AddWithValue("$created", DBNull.Value);

                affected += await upsert.ExecuteNonQueryAsync();
            }

            return affected;
        }

        private static string? ReadFirst(SqliteDataReader reader, params string[] names)
        {
            foreach (var n in names)
            {
                var idx = GetOrdinalSafe(reader, n);
                if (idx >= 0 && !reader.IsDBNull(idx))
                {
                    try { return reader.GetValue(idx)?.ToString(); } catch { return null; }
                }
            }
            return null;
        }

        private static async Task<int> MapEmpresaToLocalAsync(string srcTable, IProgress<string>? progress)
        {
            await using var sqlite = new SqliteConnection(DbHelper.GetConnectionString());
            await sqlite.OpenAsync();

            await using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"SELECT * FROM '{srcTable}' LIMIT 1";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return 0;

            string? razao = ReadFirst(reader, "RazaoSocial", "razao_social", "razao", "Nome", "nome");
            string? fantasia = ReadFirst(reader, "NomeFantasia", "fantasia");
            string? cnpj = ReadFirst(reader, "CNPJ", "cnpj");
            string? ie = ReadFirst(reader, "IE", "InscricaoEstadual", "ie");
            string? im = ReadFirst(reader, "IM", "InscricaoMunicipal", "im");
            string? crt = ReadFirst(reader, "RegimeTributario", "CRT", "crt");
            string? cnae = ReadFirst(reader, "CNAE", "cnae");
            string? email = ReadFirst(reader, "Email", "email");
            string? tel = ReadFirst(reader, "Telefone", "Fone", "telefone");
            string? site = ReadFirst(reader, "Website", "Site", "website");
            string? cep = ReadFirst(reader, "CEP", "cep");
            string? log = ReadFirst(reader, "Logradouro", "Endereco", "Rua", "logradouro");
            string? num = ReadFirst(reader, "Numero", "numero");
            string? comp = ReadFirst(reader, "Complemento", "complemento");
            string? bai = ReadFirst(reader, "Bairro", "bairro");
            string? munCod = ReadFirst(reader, "MunicipioCodigo", "CodMunicipio", "codigo_municipio", "ibge");
            string? munNome = ReadFirst(reader, "MunicipioNome", "Cidade", "municipio", "cidade");
            string? uf = ReadFirst(reader, "UF", "Estado", "uf");

            await using var up = sqlite.CreateCommand();
            up.CommandText = @"
                INSERT INTO Empresa (Id, RazaoSocial, NomeFantasia, CNPJ, IE, IM, RegimeTributario, CNAE, Email, Telefone, Website, CEP, Logradouro, Numero, Complemento, Bairro, MunicipioCodigo, MunicipioNome, UF)
                VALUES (1,$rs,$nf,$cnpj,$ie,$im,$crt,$cnae,$email,$tel,$web,$cep,$log,$num,$comp,$bai,$munCod,$munNome,$uf)
                ON CONFLICT(Id) DO UPDATE SET
                    RazaoSocial=excluded.RazaoSocial,
                    NomeFantasia=excluded.NomeFantasia,
                    CNPJ=excluded.CNPJ,
                    IE=excluded.IE,
                    IM=excluded.IM,
                    RegimeTributario=excluded.RegimeTributario,
                    CNAE=excluded.CNAE,
                    Email=excluded.Email,
                    Telefone=excluded.Telefone,
                    Website=excluded.Website,
                    CEP=excluded.CEP,
                    Logradouro=excluded.Logradouro,
                    Numero=excluded.Numero,
                    Complemento=excluded.Complemento,
                    Bairro=excluded.Bairro,
                    MunicipioCodigo=excluded.MunicipioCodigo,
                    MunicipioNome=excluded.MunicipioNome,
                    UF=excluded.UF;";
            up.Parameters.AddWithValue("$rs", (object?)razao ?? DBNull.Value);
            up.Parameters.AddWithValue("$nf", (object?)fantasia ?? DBNull.Value);
            up.Parameters.AddWithValue("$cnpj", (object?)cnpj ?? DBNull.Value);
            up.Parameters.AddWithValue("$ie", (object?)ie ?? DBNull.Value);
            up.Parameters.AddWithValue("$im", (object?)im ?? DBNull.Value);
            up.Parameters.AddWithValue("$crt", (object?)crt ?? DBNull.Value);
            up.Parameters.AddWithValue("$cnae", (object?)cnae ?? DBNull.Value);
            up.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
            up.Parameters.AddWithValue("$tel", (object?)tel ?? DBNull.Value);
            up.Parameters.AddWithValue("$web", (object?)site ?? DBNull.Value);
            up.Parameters.AddWithValue("$cep", (object?)cep ?? DBNull.Value);
            up.Parameters.AddWithValue("$log", (object?)log ?? DBNull.Value);
            up.Parameters.AddWithValue("$num", (object?)num ?? DBNull.Value);
            up.Parameters.AddWithValue("$comp", (object?)comp ?? DBNull.Value);
            up.Parameters.AddWithValue("$bai", (object?)bai ?? DBNull.Value);
            up.Parameters.AddWithValue("$munCod", (object?)munCod ?? DBNull.Value);
            up.Parameters.AddWithValue("$munNome", (object?)munNome ?? DBNull.Value);
            up.Parameters.AddWithValue("$uf", (object?)uf ?? DBNull.Value);
            return await up.ExecuteNonQueryAsync();
        }

        private static async Task<int> MapContadorToLocalAsync(string srcTable, IProgress<string>? progress)
        {
            await using var sqlite = new SqliteConnection(DbHelper.GetConnectionString());
            await sqlite.OpenAsync();

            await using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"SELECT * FROM '{srcTable}' LIMIT 1";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return 0;

            string? nome = ReadFirst(reader, "Nome", "nome");
            string? crc = ReadFirst(reader, "CRC", "crc");
            string? cnpj = ReadFirst(reader, "CNPJ", "cnpj");
            string? cpf = ReadFirst(reader, "CPF", "cpf");
            string? email = ReadFirst(reader, "Email", "email");
            string? tel = ReadFirst(reader, "Telefone", "Fone", "telefone");
            string? cel = ReadFirst(reader, "Celular", "celular");
            string? cep = ReadFirst(reader, "CEP", "cep");
            string? log = ReadFirst(reader, "Logradouro", "Endereco", "Rua", "logradouro");
            string? num = ReadFirst(reader, "Numero", "numero");
            string? comp = ReadFirst(reader, "Complemento", "complemento");
            string? bai = ReadFirst(reader, "Bairro", "bairro");
            string? munCod = ReadFirst(reader, "MunicipioCodigo", "CodMunicipio", "codigo_municipio", "ibge");
            string? munNome = ReadFirst(reader, "MunicipioNome", "Cidade", "municipio", "cidade");
            string? uf = ReadFirst(reader, "UF", "Estado", "uf");

            await using var up = sqlite.CreateCommand();
            up.CommandText = @"
                INSERT INTO Contador (Id, Nome, CRC, CNPJ, CPF, Email, Telefone, Celular, CEP, Logradouro, Numero, Complemento, Bairro, MunicipioCodigo, MunicipioNome, UF)
                VALUES (1,$nome,$crc,$cnpj,$cpf,$email,$tel,$cel,$cep,$log,$num,$comp,$bai,$munCod,$munNome,$uf)
                ON CONFLICT(Id) DO UPDATE SET
                    Nome=excluded.Nome,
                    CRC=excluded.CRC,
                    CNPJ=excluded.CNPJ,
                    CPF=excluded.CPF,
                    Email=excluded.Email,
                    Telefone=excluded.Telefone,
                    Celular=excluded.Celular,
                    CEP=excluded.CEP,
                    Logradouro=excluded.Logradouro,
                    Numero=excluded.Numero,
                    Complemento=excluded.Complemento,
                    Bairro=excluded.Bairro,
                    MunicipioCodigo=excluded.MunicipioCodigo,
                    MunicipioNome=excluded.MunicipioNome,
                    UF=excluded.UF;";
            up.Parameters.AddWithValue("$nome", (object?)nome ?? DBNull.Value);
            up.Parameters.AddWithValue("$crc", (object?)crc ?? DBNull.Value);
            up.Parameters.AddWithValue("$cnpj", (object?)cnpj ?? DBNull.Value);
            up.Parameters.AddWithValue("$cpf", (object?)cpf ?? DBNull.Value);
            up.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
            up.Parameters.AddWithValue("$tel", (object?)tel ?? DBNull.Value);
            up.Parameters.AddWithValue("$cel", (object?)cel ?? DBNull.Value);
            up.Parameters.AddWithValue("$cep", (object?)cep ?? DBNull.Value);
            up.Parameters.AddWithValue("$log", (object?)log ?? DBNull.Value);
            up.Parameters.AddWithValue("$num", (object?)num ?? DBNull.Value);
            up.Parameters.AddWithValue("$comp", (object?)comp ?? DBNull.Value);
            up.Parameters.AddWithValue("$bai", (object?)bai ?? DBNull.Value);
            up.Parameters.AddWithValue("$munCod", (object?)munCod ?? DBNull.Value);
            up.Parameters.AddWithValue("$munNome", (object?)munNome ?? DBNull.Value);
            up.Parameters.AddWithValue("$uf", (object?)uf ?? DBNull.Value);
            return await up.ExecuteNonQueryAsync();
        }

        private static int GetOrdinalSafe(SqliteDataReader reader, string name)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static string NormalizePasswordHash(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw.Trim();
            bool isSha256 = s.Length == 64 && s.All(c => Uri.IsHexDigit(c));
            if (isSha256) return s.ToLowerInvariant();
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string NormalizeRole(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "operator";
            var s = raw.Trim().ToLowerInvariant();
            if (s.Contains("admin") || s.Contains("gerente") || s.Contains("gestor")) return "admin";
            if (s.Contains("fiscal") || s.Contains("auditor")) return "fiscal";
            if (s.Contains("caixa") || s.Contains("operador") || s.Contains("operator")) return "operator";
            if (s.Contains("visual") || s.Contains("viewer") || s.Contains("consulta")) return "viewer";
            // numérico: 2=admin, 1=fiscal, 0=operator (fallback)
            if (int.TryParse(s, out var n))
            {
                if (n >= 2) return "admin";
                if (n == 1) return "fiscal";
                return "operator";
            }
            return "operator";
        }

        private static bool ParseActive(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var s = raw.Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "ativo" || s == "a" || s == "s" || s == "yes") return true;
            if (s == "0" || s == "false" || s == "inativo" || s == "i" || s == "n" || s == "no") return false;
            return true;
        }
    }
}