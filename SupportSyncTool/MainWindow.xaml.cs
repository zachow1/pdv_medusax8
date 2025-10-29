using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using PDV_MedusaX8.Services;
using SupportSyncTool.Models;
using SupportSyncTool.Services;

namespace SupportSyncTool
{
    public partial class MainWindow : Window
    {
        private readonly ISyncApi _api = new SyncApi();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfigurationOnStartup();
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
            BtnSyncUsuarios.IsEnabled = !busy;
            BtnSyncTudo.IsEnabled = !busy;
            BtnSaveConfig.IsEnabled = !busy;
            BtnLoadConfig.IsEnabled = !busy;
            BtnClearConfig.IsEnabled = !busy;
            Progress.IsIndeterminate = busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            TxtStatus.Text = string.IsNullOrWhiteSpace(status) ? (busy ? "Executando..." : "Pronto") : status;
        }

        private void AppendLog(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        private async Task RunSafeAsync(Func<IProgress<string>, Task> action, string startMsg, string doneMsg)
        {
            var progress = new Progress<string>(msg => AppendLog(msg));
            try
            {
                SetBusy(true, startMsg);
                AppendLog(startMsg);
                await action(progress);
                AppendLog(doneMsg);
                SetBusy(false, doneMsg);
            }
            catch (Exception ex)
            {
                AppendLog($"Erro: {ex.Message}");
                SetBusy(false, "Erro");
                MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            await RunSafeAsync(async p =>
            {
                var ok = await _api.TestConnectionAsync(host, port, db, user, pass, p);
                if (!ok) throw new InvalidOperationException("Tabelas necessárias não encontradas ou conexão inválida.");
            }, "Testando conexão...", "Conexão OK");
        }

        private async void BtnSyncClientes_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            await RunSafeAsync(async p =>
            {
                await _api.SyncParticipantesAsync(host, port, db, user, pass, p);
            }, "Sincronizando clientes...", "Clientes sincronizados");
        }

        private async void BtnSyncProdutos_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            await RunSafeAsync(async p =>
            {
                await _api.SyncProdutosAsync(host, port, db, user, pass, p);
            }, "Sincronizando produtos...", "Produtos sincronizados");
        }

        private async void BtnSyncTudo_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            await RunSafeAsync(async p =>
            {
                var ok = await _api.TestConnectionAsync(host, port, db, user, pass, p);
                if (!ok) throw new InvalidOperationException("Conexão inválida.");
                await _api.SyncParticipantesAsync(host, port, db, user, pass, p);
                await _api.SyncProdutosAsync(host, port, db, user, pass, p);
                await _api.SyncUsuariosAsync(host, port, db, user, pass, p);
            }, "Sincronizando tudo...", "Sincronização completa");
        }

        private async void BtnSyncUsuarios_Click(object sender, RoutedEventArgs e)
        {
            var (host, port, db, user, pass) = ReadInputs();
            await RunSafeAsync(async p =>
            {
                await _api.SyncUsuariosAsync(host, port, db, user, pass, p);
            }, "Sincronizando usuários...", "Usuários sincronizados");
        }

        private void LoadConfigurationOnStartup()
        {
            try
            {
                if (ConfigManager.ConfigExists())
                {
                    var config = ConfigManager.LoadConfig();
                    TxtHost.Text = config.Host;
                    TxtPort.Text = config.Port.ToString();
                    TxtDb.Text = config.Database;
                    TxtUser.Text = config.Username;
                    PwdPass.Password = config.Password;
                    AppendLog("Configuração carregada automaticamente.");
                }
                else
                {
                    // Valores padrão seguros
                    TxtHost.Text = "localhost";
                    TxtPort.Text = "3307";
                    TxtDb.Text = "medusaX8";
                    TxtUser.Text = "root";
                    AppendLog("Nenhuma configuração salva encontrada. Use valores padrão e salve a configuração.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Erro ao carregar configuração: {ex.Message}");
            }
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadInputs();
                var config = new DatabaseConfig
                {
                    Host = host,
                    Port = port,
                    Database = db,
                    Username = user,
                    Password = pass
                };

                if (!config.IsValid())
                {
                    MessageBox.Show("Por favor, preencha todos os campos obrigatórios (Host, Banco, Usuário).", 
                                  "Configuração Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ConfigManager.SaveConfig(config);
                AppendLog("Configuração salva com segurança.");
                MessageBox.Show("Configuração salva com sucesso!", "Sucesso", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"Erro ao salvar configuração: {ex.Message}");
                MessageBox.Show($"Erro ao salvar configuração: {ex.Message}", "Erro", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                TxtHost.Text = config.Host;
                TxtPort.Text = config.Port.ToString();
                TxtDb.Text = config.Database;
                TxtUser.Text = config.Username;
                PwdPass.Password = config.Password;
                
                AppendLog("Configuração carregada com sucesso.");
                MessageBox.Show("Configuração carregada com sucesso!", "Sucesso", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"Erro ao carregar configuração: {ex.Message}");
                MessageBox.Show($"Erro ao carregar configuração: {ex.Message}", "Erro", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("Tem certeza que deseja limpar a configuração salva?", 
                                           "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    ConfigManager.DeleteConfig();
                    
                    // Limpar campos
                    TxtHost.Text = "";
                    TxtPort.Text = "";
                    TxtDb.Text = "";
                    TxtUser.Text = "";
                    PwdPass.Password = "";
                    
                    AppendLog("Configuração removida com sucesso.");
                    MessageBox.Show("Configuração removida com sucesso!", "Sucesso", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Erro ao remover configuração: {ex.Message}");
                MessageBox.Show($"Erro ao remover configuração: {ex.Message}", "Erro", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}