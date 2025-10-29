using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SyncToolStandalone
{
    public partial class MainWindow : Window
    {
        private readonly ISyncApi _api = new SyncApi();

        public MainWindow()
        {
            InitializeComponent();
            AppendLog("Ferramenta de sincronização iniciada. Configure a conexão e clique em 'Testar Conexão'.");
        }

        private (string host, int port, string db, string user, string pass) ReadInputs()
        {
            var host = TxtHost.Text?.Trim() ?? "localhost";
            var db = TxtDb.Text?.Trim() ?? "medusaX8";
            var user = TxtUser.Text?.Trim() ?? "root";
            var pass = PwdPass.Password ?? string.Empty;
            int port = 3307;
            int.TryParse(TxtPort.Text, out port);
            return (host, port, db, user, pass);
        }

        private void SetBusy(bool busy, string status = "")
        {
            BtnTest.IsEnabled = !busy;
            BtnSyncClientes.IsEnabled = !busy;
            BtnSyncProdutos.IsEnabled = !busy;
            BtnSyncTudo.IsEnabled = !busy;
            Progress.IsIndeterminate = busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            TxtStatus.Text = string.IsNullOrWhiteSpace(status) ? (busy ? "Executando..." : "Pronto") : status;
        }

        private void AppendLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            TxtLog.AppendText($"[{timestamp}] {message}\n");
            TxtLog.ScrollToEnd();
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            SetBusy(true, "Testando conexão...");
            AppendLog($"Testando conexão com {host}:{port}/{db}...");

            try
            {
                var result = await _api.TestConnectionAsync(host, port, db, user, pass);
                AppendLog(result.message);
                SetBusy(false, result.success ? "Conexão OK" : "Falha na conexão");
            }
            catch (Exception ex)
            {
                AppendLog($"Erro: {ex.Message}");
                SetBusy(false, "Erro");
            }
        }

        private async void BtnSyncClientes_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            SetBusy(true, "Sincronizando clientes...");
            AppendLog("Iniciando sincronização de clientes...");

            try
            {
                var result = await _api.SyncParticipantesAsync(host, port, db, user, pass);
                AppendLog(result.message);
                SetBusy(false, result.success ? $"{result.count} clientes sincronizados" : "Falha na sincronização");
            }
            catch (Exception ex)
            {
                AppendLog($"Erro: {ex.Message}");
                SetBusy(false, "Erro");
            }
        }

        private async void BtnSyncProdutos_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            SetBusy(true, "Sincronizando produtos...");
            AppendLog("Iniciando sincronização de produtos...");

            try
            {
                var result = await _api.SyncProdutosAsync(host, port, db, user, pass);
                AppendLog(result.message);
                SetBusy(false, result.success ? $"{result.count} produtos sincronizados" : "Falha na sincronização");
            }
            catch (Exception ex)
            {
                AppendLog($"Erro: {ex.Message}");
                SetBusy(false, "Erro");
            }
        }

        private async void BtnSyncTudo_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            SetBusy(true, "Sincronizando tudo...");
            AppendLog("Iniciando sincronização completa...");

            try
            {
                // Sincronizar clientes
                AppendLog("Sincronizando clientes...");
                var clientesResult = await _api.SyncParticipantesAsync(host, port, db, user, pass);
                AppendLog(clientesResult.message);

                // Sincronizar produtos
                AppendLog("Sincronizando produtos...");
                var produtosResult = await _api.SyncProdutosAsync(host, port, db, user, pass);
                AppendLog(produtosResult.message);

                // Resultado final
                bool success = clientesResult.success && produtosResult.success;
                string status = success 
                    ? $"Sincronização completa: {clientesResult.count} clientes, {produtosResult.count} produtos" 
                    : "Falha na sincronização";
                
                SetBusy(false, status);
            }
            catch (Exception ex)
            {
                AppendLog($"Erro: {ex.Message}");
                SetBusy(false, "Erro");
            }
        }
    }
}