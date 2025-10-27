namespace PDV_MedusaX8.Services.TEF
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Windows;

    public class SitefProvider : ITEFProvider
    {
        public bool Initialize()
        {
            try
            {
                var tef = Services.TEFManager.Instance;
                // Garantir pastas de troca
                var basePath = tef.TEFExchangePath;
                var reqDir = Path.Combine(basePath, "REQ");
                var respDir = Path.Combine(basePath, "RESP");
                Directory.CreateDirectory(reqDir);
                Directory.CreateDirectory(respDir);

                // Parâmetros SiTef disponíveis (para futura integração ACBr)
                var ip = tef.SitefIP;
                var loja = tef.SitefLoja;
                var terminal = tef.SitefTerminal;
                var scope = tef.TEFScope;

                // TODO: Inicializar ACBrTEFD para SiTef (via pasta de troca)
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar SiTef: {ex.Message}", "Erro TEF", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public TEFResult ProcessPayment(decimal amount, string paymentType)
        {
            try
            {
                var tef = Services.TEFManager.Instance;
                var basePath = tef.TEFExchangePath;
                var reqDir = Path.Combine(basePath, "REQ");
                var respDir = Path.Combine(basePath, "RESP");
                Directory.CreateDirectory(reqDir);
                Directory.CreateDirectory(respDir);

                // Nome padrão conforme intpos (padrão) — aqui usamos sempre 001
                var reqFile = Path.Combine(reqDir, "intpos.001");
                var respFile = Path.Combine(respDir, "intpos.001");

                // Limpar resíduos anteriores
                SafeDelete(reqFile);
                SafeDelete(respFile);

                // Montar conteúdo da requisição (campos comuns do padrão de troca)
                var valorCentavos = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
                var data = DateTime.Now.ToString("yyyyMMdd");
                var hora = DateTime.Now.ToString("HHmmss");

                // Campos básicos; provedores podem esperar chaves específicas.
                // Estes são genéricos para facilitar a integração com automações.
                var reqLines = new[]
                {
                    $"OPERACAO=VENDA",
                    $"TIPO={(string.Equals(paymentType, "Debito", StringComparison.OrdinalIgnoreCase) ? "DEBITO" : "CREDITO")}",
                    $"VALOR={valorCentavos}",
                    $"DATA={data}",
                    $"HORA={hora}",
                    $"LOJA={tef.SitefLoja}",
                    $"TERMINAL={tef.SitefTerminal}",
                    $"IP={tef.SitefIP}",
                    $"SCOPE={tef.TEFScope}"
                };
                File.WriteAllLines(reqFile, reqLines);

                // Se modo debug estiver ativo, gerar resposta local imediatamente
                if (tef.TEFDebugMode)
                {
                    var debugResp = new[]
                    {
                        "STATUS=APROVADO",
                        "MENSAGEM=OK",
                        $"NSU={DateTime.Now:yyyyMMddHHmmss}",
                        "CODAUTORIZACAO=DBG123",
                        $"LOJA={tef.SitefLoja}",
                        $"TERMINAL={tef.SitefTerminal}"
                    };
                    File.WriteAllLines(respFile, debugResp);
                }

                // Aguardar resposta do TEF (arquivo em RESP)
                var timeoutMs = 120_000; // 120s
                var intervalMs = 500;
                var waited = 0;
                while (waited < timeoutMs)
                {
                    if (File.Exists(respFile)) break;
                    Thread.Sleep(intervalMs);
                    waited += intervalMs;
                }

                if (!File.Exists(respFile))
                {
                    return new TEFResult { Success = false, Message = "Timeout aguardando resposta do TEF (arquivo de retorno)." };
                }

                // Ler e interpretar resposta: chaves comuns
                var dict = File.ReadAllLines(respFile)
                                .Select(l => l.Split('=', 2))
                                .Where(p => p.Length == 2)
                                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

                var status = dict.TryGetValue("STATUS", out var st) ? st : string.Empty;
                var msg = dict.TryGetValue("MENSAGEM", out var m) ? m : string.Empty;
                var nsu = dict.TryGetValue("NSU", out var n) ? n : string.Empty;
                var auth = dict.TryGetValue("CODAUTORIZACAO", out var a) ? a : string.Empty;

                var ok = string.Equals(status, "APROVADO", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "0", StringComparison.OrdinalIgnoreCase);

                return new TEFResult
                {
                    Success = ok,
                    Message = string.IsNullOrWhiteSpace(msg) ? (ok ? "Transação aprovada" : "Transação não aprovada") : msg,
                    TransactionId = nsu,
                    AuthorizationCode = auth
                };
            }
            catch (Exception ex)
            {
                return new TEFResult { Success = false, Message = ex.Message };
            }
        }

        private void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }
    }
}