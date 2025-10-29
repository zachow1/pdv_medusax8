using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PDV_MedusaX8.Services
{
    public class PdvStatusReportService
    {
        private static string GetConn() => DbHelper.GetConnectionString();

        private static string? GetSetting(string key)
        {
            try
            {
                using var conn = new SqliteConnection(GetConn());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $k LIMIT 1";
                cmd.Parameters.AddWithValue("$k", key);
                var val = cmd.ExecuteScalar();
                return val?.ToString();
            }
            catch { return null; }
        }

        private static (bool Exists, string TpAmb, string? CscId, string? Csc, string? Serie, string? ProximoNumero) GetNFCeConfig()
        {
            try
            {
                using var conn = new SqliteConnection(GetConn());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT TpAmb, CSCId, CSC, Serie, ProximoNumero FROM ConfiguracoesNFCe WHERE Id = 1 LIMIT 1";
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    var tpAmb = rd[0]?.ToString() ?? "";
                    var cscId = rd[1]?.ToString();
                    var csc = rd[2]?.ToString();
                    var serie = rd[3]?.ToString();
                    var prox = rd[4]?.ToString();
                    return (true, tpAmb, cscId, csc, serie, prox);
                }
                return (false, "", null, null, null, null);
            }
            catch { return (false, "", null, null, null, null); }
        }

        private static long CountOrZero(string table)
        {
            try
            {
                using var conn = new SqliteConnection(GetConn());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(1) FROM {table}";
                var val = cmd.ExecuteScalar();
                return (val == null || val is DBNull) ? 0 : Convert.ToInt64(val);
            }
            catch { return 0; }
        }

        private static string ReadRecentSyncLogHtml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ul>");
            try
            {
                using var conn = new SqliteConnection(GetConn());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Date, Area, Status, Detail FROM AutoSyncLog ORDER BY Id DESC LIMIT 10";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var date = rd[0]?.ToString();
                    var area = rd[1]?.ToString();
                    var status = rd[2]?.ToString();
                    var det = rd[3]?.ToString();
                    sb.AppendLine($"<li><b>{date}</b> — {area}: <i>{status}</i> {System.Net.WebUtility.HtmlEncode(det)}</li>");
                }
            }
            catch
            {
                sb.AppendLine("<li>Sem logs de AutoSync.</li>");
            }
            sb.AppendLine("</ul>");
            return sb.ToString();
        }

        public async Task GenerateHtmlAsync(string outputPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var caixa = GetSetting("CashRegisterNumber");
                var exigeSupSangria = (GetSetting("RequireSupervisorForSangria") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                var exigeF2 = (GetSetting("RequireF2ToStartSale") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                var maxDesc = GetSetting("MaxDiscountPercent") ?? "N/D";

                var nfce = GetNFCeConfig();
                var clientes = CountOrZero("Customers");
                var produtos = CountOrZero("Products");
                var recebiveis = CountOrZero("Receivables");
                var nfces = CountOrZero("NFCe");

                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html lang=\"pt-BR\">");
                sb.AppendLine("<head>");
                sb.AppendLine("  <meta charset=\"utf-8\" />");
                sb.AppendLine("  <title>Status e Funções do PDV</title>");
                sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
                sb.AppendLine("  <style>");
                sb.AppendLine("    body {{ font-family: Arial, sans-serif; margin: 24px; color: #222; }}");
                sb.AppendLine("    h1 {{ margin: 0 0 16px 0; }}");
                sb.AppendLine("    h2 {{ margin-top: 24px; }}");
                sb.AppendLine("    .badge {{ display:inline-block; padding:2px 8px; border-radius:12px; font-size:12px; margin-left:8px; }}");
                sb.AppendLine("    .ok {{ background:#e6ffed; color:#067d12; border:1px solid #9ae6b4; }}");
                sb.AppendLine("    .off {{ background:#fff5f5; color:#991b1b; border:1px solid #fecaca; }}");
                sb.AppendLine("    .pending {{ background:#fffbea; color:#92400e; border:1px solid #fde68a; }}");
                sb.AppendLine("    code {{ background:#f5f5f5; padding:2px 4px; border-radius:4px; }}");
                sb.AppendLine("    ul {{ margin: 6px 0 16px 20px; }}");
                sb.AppendLine("    .small {{ color:#666; font-size:12px; }}");
                sb.AppendLine("  </style>");
                sb.AppendLine($"  <meta http-equiv=\"refresh\" content=\"0; url=file:///{System.Net.WebUtility.HtmlEncode(outputPath.Replace("\\", "/"))}\" />");
                sb.AppendLine("  <script>/* noop */</script>");
                sb.AppendLine("  <!-- Abra este arquivo diretamente em seu navegador se o redirecionamento não funcionar. -->");
                sb.AppendLine($"  <noscript><div class=\"small\">Se o redirecionamento não ocorrer, abra manualmente: {System.Net.WebUtility.HtmlEncode(outputPath)}</div></noscript>");
                sb.AppendLine("  <meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self' 'unsafe-inline' data:;\" />");
                sb.AppendLine("  <meta name=\"referrer\" content=\"no-referrer\" />");
                sb.AppendLine("  <meta name=\"color-scheme\" content=\"light only\" />");
                sb.AppendLine("  <meta name=\"robots\" content=\"noindex, nofollow\" />");
                sb.AppendLine("  <meta name=\"x-content-type-options\" content=\"nosniff\" />");
                sb.AppendLine("  <meta name=\"x-frame-options\" content=\"SAMEORIGIN\" />");
                sb.AppendLine("  <meta name=\"x-xss-protection\" content=\"1; mode=block\" />");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("  <h1>Status e Funções do PDV</h1>");
                var nowStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                sb.AppendLine("  <p class=\"small\">Gerado em " + nowStr + "</p>");

                sb.AppendLine("  <h2>Visão Geral</h2>");
                sb.AppendLine("  <ul>");
                sb.AppendLine("    <li>Caixa: <b>" + System.Net.WebUtility.HtmlEncode(caixa ?? "N/D") + "</b></li>");
                sb.AppendLine("    <li>Clientes cadastrados: <b>" + clientes + "</b></li>");
                sb.AppendLine("    <li>Produtos cadastrados: <b>" + produtos + "</b></li>");
                sb.AppendLine("    <li>Recebíveis: <b>" + recebiveis + "</b></li>");
                sb.AppendLine("    <li>NFC-e emitidas: <b>" + nfces + "</b></li>");
                sb.AppendLine("  </ul>");

                sb.AppendLine("  <h2>Vendas e Operação</h2>");
                sb.AppendLine("  <ul>");
                var sangriaBadge = exigeSupSangria ? "<span class='badge ok'>Ativado</span>" : "<span class='badge off'>Desativado</span>";
                var f2Badge = exigeF2 ? "<span class='badge ok'>Ativado</span>" : "<span class='badge off'>Desativado</span>";
                sb.AppendLine("    <li>Exigir supervisor para sangria: " + sangriaBadge + "</li>");
                sb.AppendLine("    <li>Exigir F2 para iniciar venda: " + f2Badge + "</li>");
                sb.AppendLine("    <li>Desconto máximo por item: <b>" + System.Net.WebUtility.HtmlEncode(maxDesc) + "%</b></li>");
                sb.AppendLine("  </ul>");

                sb.AppendLine("  <h2>NFC-e</h2>");
                sb.AppendLine("  <ul>");
                var nfceConfigBadge = nfce.Exists ? "<span class='badge ok'>Encontrada</span>" : "<span class='badge off'>Não configurada</span>";
                sb.AppendLine("    <li>Configuração: " + nfceConfigBadge + "</li>");
                var amb = nfce.TpAmb == "2" ? "Homologação" : (nfce.TpAmb == "1" ? "Produção" : "N/D");
                sb.AppendLine("    <li>Ambiente: <b>" + amb + "</b></li>");
                var cscIdSafe = System.Net.WebUtility.HtmlEncode(nfce.CscId ?? "N/D");
                sb.AppendLine("    <li>CSC Id: <code>" + cscIdSafe + "</code></li>");
                var cscMask = string.IsNullOrWhiteSpace(nfce.Csc) ? "N/D" : "••••••••";
                sb.AppendLine("    <li>CSC: <code>" + cscMask + "</code></li>");
                var serieSafe = System.Net.WebUtility.HtmlEncode(nfce.Serie ?? "?");
                var proxNumSafe = System.Net.WebUtility.HtmlEncode(nfce.ProximoNumero ?? "?");
                sb.AppendLine("    <li>Série/Próximo número: <b>" + serieSafe + "/" + proxNumSafe + "</b></li>");
                sb.AppendLine("  </ul>");

                sb.AppendLine("  <h2>Sincronização</h2>");
                sb.AppendLine("  <ul>");
                sb.AppendLine("    <li>Clientes: <span class=\"badge ok\">AutoSync habilitado</span></li>");
                sb.AppendLine("    <li>Produtos: <span class=\"badge ok\">AutoSync habilitado</span></li>");
                sb.AppendLine("    <li>Financeiro: <span class=\"badge pending\">Em desenvolvimento</span></li>");
                sb.AppendLine("    <li>NFC-e: <span class=\"badge pending\">Em desenvolvimento</span></li>");
                sb.AppendLine("  </ul>");
                sb.AppendLine("  <div class=\"small\">Configure credenciais em <i>Configurações &gt; Empresa</i> para habilitar a sincronização automática com o ERP.</div>");

                sb.AppendLine("  <h3>Últimos eventos de AutoSync</h3>");
                sb.AppendLine(ReadRecentSyncLogHtml());

                sb.AppendLine("  <h2>Como funciona</h2>");
                sb.AppendLine("  <ul>");
                sb.AppendLine("    <li>Ao salvar um novo <b>Cliente</b>, o PDV dispara a sincronização de clientes.</li>");
                sb.AppendLine("    <li>Ao emitir uma <b>NFC-e</b> ou registrar um <b>Recebimento</b>, eventos são registrados e a sincronização dedicada, quando disponível, é acionada.</li>");
                sb.AppendLine("    <li>Para sincronização manual, utilize a ferramenta dedicada <b>SupportSyncTool</b>.</li>");
                sb.AppendLine("  </ul>");

                sb.AppendLine("  <h2>O que precisa fazer</h2>");
                sb.AppendLine("  <ul>");
                sb.AppendLine("    <li>Preencher conexão do ERP (host, porta, base, usuário, senha) em <i>Configurações</i>.</li>");
                sb.AppendLine("    <li>Conferir a configuração da NFC-e (CSC, Série, Ambiente, Certificado).</li>");
                sb.AppendLine("    <li>Realizar um teste de conexão e sincronização inicial de <b>Clientes</b> e <b>Produtos</b>.</li>");
                sb.AppendLine("  </ul>");

                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore errors to not block app startup
            }
        }
    }
}