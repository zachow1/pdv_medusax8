using System;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Windows;
using PDV_MedusaX8.Services.TEF;

namespace PDV_MedusaX8.Services
{
    public enum TEFIntegrationType
    {
        None,
        Sitef,
        PayGo,
        Cappta,
        VeSPague,
        BinCard,
        Credsystem,
        FoxWin,
        GoodCard,
        PagFor,
        CliSiTef,
        Dedicado,
        Hiper,
        Ticket,
        VeriFone
    }

    public class TEFResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string AuthorizationCode { get; set; } = string.Empty;
    }

    public class TEFManager
    {
        private static TEFManager _instance;
        private TEFIntegrationType _currentTEFType;
        private bool _isInitialized = false;
        private ITEFProvider? _provider;
        
        // Propriedades para configurações específicas de cada TEF
        public string TEFScope { get; set; } = string.Empty;
        public string TEFExchangePath { get; set; } = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEF");
        public bool TEFDebugMode { get; set; } = false;
        public string SitefIP { get; set; } = "127.0.0.1";
        public string SitefLoja { get; set; } = "00000000";
        public string SitefTerminal { get; set; } = "00000001";
        public string PayGoUser { get; set; } = "";
        public string PayGoPassword { get; set; } = "";
        public string CapptaCNPJ { get; set; } = "";
        public string CapptaPDV { get; set; } = "001";

        public static TEFManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new TEFManager();
                return _instance;
            }
        }

        private TEFManager()
        {
            LoadTEFConfiguration();
        }

        public void LoadTEFConfiguration()
        {
            try
            {
                App.Log("TEF Load configuration");
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medusax8.db");
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    // Carrega o tipo de TEF configurado
                    string query = "SELECT Value FROM Settings WHERE Key = 'TEFIntegrationType'";
                    using (var command = new SqliteCommand(query, connection))
                    {
                        var result = command.ExecuteScalar()?.ToString();
                        if (!string.IsNullOrEmpty(result) && Enum.TryParse<TEFIntegrationType>(result, out var tefType))
                        {
                            _currentTEFType = tefType;
                        }
                        else
                        {
                            _currentTEFType = TEFIntegrationType.None;
                        }
                        App.Log($"TEF type={_currentTEFType}");
                    }
                    // Carrega configurações específicas do TEF (se existirem)
                    LoadTEFSpecificSettings(connection);
                }
            }
            catch (Exception ex)
            {
                App.Log("Erro ao carregar configuração TEF", ex);
                MessageBox.Show($"Erro ao carregar configuração TEF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentTEFType = TEFIntegrationType.None;
            }
        }

        private void LoadTEFSpecificSettings(SqliteConnection connection)
        {
            try
            {
                // Campos genéricos compartilhados por integrações
                TEFScope = GetSetting(connection, "TEFScope", string.Empty);
                TEFExchangePath = GetSetting(connection, "TEFExchangePath", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEF"));
                var debugRaw = GetSetting(connection, "TEFDebugMode", "0");
                TEFDebugMode = debugRaw == "1" || string.Equals(debugRaw, "true", StringComparison.OrdinalIgnoreCase);
                App.Log($"TEF settings DebugMode={TEFDebugMode} ExchangePath={TEFExchangePath}");
                // Carrega configurações específicas baseadas no tipo de TEF
                switch (_currentTEFType)
                {
                    case TEFIntegrationType.Sitef:
                    case TEFIntegrationType.CliSiTef:
                        SitefIP = GetSetting(connection, "SitefIP", "127.0.0.1");
                        SitefLoja = GetSetting(connection, "SitefLoja", "00000000");
                        SitefTerminal = GetSetting(connection, "SitefTerminal", "00000001");
                        break;
                    case TEFIntegrationType.PayGo:
                        PayGoUser = GetSetting(connection, "PayGoUser", "");
                        PayGoPassword = GetSetting(connection, "PayGoPassword", "");
                        break;
                    case TEFIntegrationType.Cappta:
                        CapptaCNPJ = GetSetting(connection, "CapptaCNPJ", "");
                        CapptaPDV = GetSetting(connection, "CapptaPDV", "001");
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configurações específicas do TEF: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string GetSetting(SqliteConnection connection, string key, string defaultValue)
        {
            string query = "SELECT Value FROM Settings WHERE Key = @key";
            using (var command = new SqliteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@key", key);
                var result = command.ExecuteScalar()?.ToString();
                return string.IsNullOrEmpty(result) ? defaultValue : result;
            }
        }

        public bool InitializeTEF()
        {
            try
            {
                App.Log("TEF Initialize start");
                if (_currentTEFType == TEFIntegrationType.None)
                {
                    _isInitialized = false;
                    App.Log("TEF not configured (None), proceeding without TEF");
                    return true;
                }
                bool success = InitializeTEFComponent();
                _isInitialized = success;
                App.Log($"TEF Initialize result success={success} type={_currentTEFType}");
                return success;
            }
            catch (Exception ex)
            {
                App.Log("Erro ao inicializar TEF", ex);
                MessageBox.Show($"Erro ao inicializar TEF: {ex.Message}", "Erro TEF", MessageBoxButton.OK, MessageBoxImage.Error);
                _isInitialized = false;
                return false;
            }
        }

        private bool InitializeTEFComponent()
        {
            _provider = CreateProvider(_currentTEFType);
            App.Log($"TEF provider={_provider?.GetType().Name ?? "null"}");
            if (_provider == null) return false;
            return _provider.Initialize();
        }

        private ITEFProvider? CreateProvider(TEFIntegrationType type)
        {
            switch (type)
            {
                case TEFIntegrationType.Sitef:
                    App.Log("CreateProvider: Sitef");
                    return new SitefProvider();
                case TEFIntegrationType.PayGo:
                    App.Log("CreateProvider: PayGo");
                    return new PayGoProvider();
                case TEFIntegrationType.Cappta:
                    App.Log("CreateProvider: Cappta");
                    return new CapptaProvider();
                case TEFIntegrationType.None:
                    App.Log("CreateProvider: None");
                    return null;
                default:
                    App.Log("CreateProvider: default->Sitef");
                    return new SitefProvider();
            }
        }

        #region Métodos de Inicialização Específicos

        private bool InitializeSitef()
        {
            try
            {
                // TODO: Implementar inicialização do Sitef
                // ACBrTEFD.TEFSiTef.Inicializar(SitefIP, SitefLoja, SitefTerminal);
                
                MessageBox.Show($"TEF Sitef inicializado\nIP: {SitefIP}\nLoja: {SitefLoja}\nTerminal: {SitefTerminal}", 
                    "TEF Sitef", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Sitef: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeCliSitef()
        {
            try
            {
                // TODO: Implementar inicialização do CliSiTef
                // ACBrTEFD.TEFCliSiTef.Inicializar(SitefIP, SitefLoja, SitefTerminal);
                
                MessageBox.Show($"TEF CliSiTef inicializado\nIP: {SitefIP}\nLoja: {SitefLoja}\nTerminal: {SitefTerminal}", 
                    "TEF CliSiTef", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar CliSiTef: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializePayGo()
        {
            try
            {
                // TODO: Implementar inicialização do PayGo
                // ACBrTEFD.TEFPayGo.Inicializar(PayGoUser, PayGoPassword);
                
                MessageBox.Show($"TEF PayGo inicializado\nUsuário: {PayGoUser}", 
                    "TEF PayGo", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar PayGo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeCappta()
        {
            try
            {
                // TODO: Implementar inicialização do Cappta
                // ACBrTEFD.TEFCappta.Inicializar(CapptaCNPJ, CapptaPDV);
                
                MessageBox.Show($"TEF Cappta inicializado\nCNPJ: {CapptaCNPJ}\nPDV: {CapptaPDV}", 
                    "TEF Cappta", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Cappta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeVeSPague()
        {
            try
            {
                // TODO: Implementar inicialização do VeSPague
                MessageBox.Show("TEF VeSPague inicializado", "TEF VeSPague", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar VeSPague: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeBinCard()
        {
            try
            {
                // TODO: Implementar inicialização do BinCard
                MessageBox.Show("TEF BinCard inicializado", "TEF BinCard", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar BinCard: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeCredsystem()
        {
            try
            {
                // TODO: Implementar inicialização do Credsystem
                MessageBox.Show("TEF Credsystem inicializado", "TEF Credsystem", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Credsystem: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeFoxWin()
        {
            try
            {
                // TODO: Implementar inicialização do FoxWin
                MessageBox.Show("TEF FoxWin inicializado", "TEF FoxWin", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar FoxWin: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeGoodCard()
        {
            try
            {
                // TODO: Implementar inicialização do GoodCard
                MessageBox.Show("TEF GoodCard inicializado", "TEF GoodCard", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar GoodCard: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializePagFor()
        {
            try
            {
                // TODO: Implementar inicialização do PagFor
                MessageBox.Show("TEF PagFor inicializado", "TEF PagFor", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar PagFor: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeDedicado()
        {
            try
            {
                // TODO: Implementar inicialização do Dedicado
                MessageBox.Show("TEF Dedicado inicializado", "TEF Dedicado", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Dedicado: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeHiper()
        {
            try
            {
                // TODO: Implementar inicialização do Hiper
                MessageBox.Show("TEF Hiper inicializado", "TEF Hiper", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Hiper: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeTicket()
        {
            try
            {
                // TODO: Implementar inicialização do Ticket
                MessageBox.Show("TEF Ticket inicializado", "TEF Ticket", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Ticket: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InitializeVeriFone()
        {
            try
            {
                // TODO: Implementar inicialização do VeriFone
                MessageBox.Show("TEF VeriFone inicializado", "TEF VeriFone", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar VeriFone: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        #endregion

        public TEFResult ProcessPayment(decimal amount, string paymentType = "Credito")
        {
            if (!_isInitialized)
            {
                App.Log("TEF ProcessPayment called but not initialized");
                return new TEFResult { Success = false, Message = "TEF não foi inicializado. Verifique as configurações." };
            }
            if (_currentTEFType == TEFIntegrationType.None)
            {
                var manualId = "MANUAL_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                App.Log($"TEF ProcessPayment manual amount={amount} type={paymentType} nsu={manualId}");
                return new TEFResult { Success = true, Message = "Processamento manual - TEF não configurado", TransactionId = manualId };
            }
            if (_provider == null)
            {
                App.Log("TEF ProcessPayment provider is null");
                return new TEFResult { Success = false, Message = "Provider TEF ausente." };
            }
            try
            {
                App.Log($"TEF ProcessPayment start amount={amount} type={paymentType}");
                var res = _provider.ProcessPayment(amount, paymentType);
                App.Log($"TEF ProcessPayment result success={res.Success} msg={res.Message} nsu={res.TransactionId} auth={res.AuthorizationCode}");
                return res;
            }
            catch (Exception ex)
            {
                App.Log("TEF ProcessPayment error", ex);
                return new TEFResult { Success = false, Message = ex.Message };
            }
        }

        public TEFIntegrationType GetCurrentTEFType()
        {
            return _currentTEFType;
        }

        public bool IsInitialized()
        {
            return _isInitialized;
        }

        public void Dispose()
        {
            try
            {
                App.Log("TEF Dispose");
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                App.Log("Erro ao finalizar TEF", ex);
                MessageBox.Show($"Erro ao finalizar TEF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}