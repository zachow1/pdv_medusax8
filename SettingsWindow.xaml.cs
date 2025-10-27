using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Printing;
using System.IO;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Models;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class SettingsWindow : Window
    {
        private List<PaymentMethod> _methods = new List<PaymentMethod>();
        public List<string> LinkTypes { get; } = new List<string> { "VENDA", "FISCAL", "FINANCEIRO", "OUTROS" };

        private Point? _dragStartPoint = null;
        private PaymentMethod? _dragSourceItem = null;

        private string? _currentCertThumb;
        private string _certStoreLocation = "CurrentUser";

        public SettingsWindow()
        {
            InitializeComponent();
            this.Loaded += SettingsWindow_Loaded;
            LoadPaymentMethods();
            LoadNFCeConfig();
            LoadOptions();
            LoadGeneralOptions();
            LoadTEFConfig();
        }

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                double workW = SystemParameters.WorkArea.Width;
                double workH = SystemParameters.WorkArea.Height;

                double targetW = workW * 0.5;
                double targetH = workH * 0.5;

                this.MaxWidth = workW;
                this.MaxHeight = workH;
                this.Width = targetW;
                this.Height = targetH;

                double left = SystemParameters.WorkArea.Left + (workW - targetW) / 2;
                double top = SystemParameters.WorkArea.Top + (workH - targetH) / 2;
                this.Left = left;
                this.Top = top;
            }
            catch { }

            // Carrega impressoras (locais e conexões de rede) e aplica seleção salva
            LoadPrinters();
            LoadPrinterSettings();
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void LoadPaymentMethods()
        {
            _methods.Clear();
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        foreach (var m in _methods)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "UPDATE PaymentMethods SET Name=$name, IsEnabled=$enabled, DisplayOrder=$order, LinkType=$link WHERE Id=$id;";
                                cmd.Parameters.AddWithValue("$name", m.Name);
                                cmd.Parameters.AddWithValue("$enabled", m.IsEnabled ? 1 : 0);
                                cmd.Parameters.AddWithValue("$order", m.DisplayOrder);
                                cmd.Parameters.AddWithValue("$link", string.IsNullOrWhiteSpace(m.LinkType) ? (object)DBNull.Value : m.LinkType!);
                                cmd.Parameters.AddWithValue("$id", m.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
                MessageBox.Show("Formas de pagamento atualizadas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar alterações: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadNFCeConfig()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();

                    // Carrega ConfiguracoesNFCe (Id=1)
                    int tpAmb = 2; // 2=Homolog
                    string? cscId = null;
                    string? csc = null;
                    int serie = 1;
                    int next = 1;

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT TpAmb, CSCId, CSC, Serie, ProximoNumero FROM ConfiguracoesNFCe WHERE Id=1;";
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                tpAmb = r.IsDBNull(0) ? 2 : r.GetInt32(0);
                                cscId = r.IsDBNull(1) ? null : r.GetString(1);
                                csc = r.IsDBNull(2) ? null : r.GetString(2);
                                serie = r.IsDBNull(3) ? 1 : r.GetInt32(3);
                                next = r.IsDBNull(4) ? 1 : r.GetInt32(4);
                            }
                        }
                    }

                    // Carrega número do caixa (Settings)
                    int cash = 1;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "CashRegisterNumber");
                        var obj = cmd.ExecuteScalar();
                        if (obj != null && obj != DBNull.Value && int.TryParse(Convert.ToString(obj), out var parsed))
                        {
                            cash = parsed;
                        }
                    }

                    // Carrega certificado selecionado (Settings)
                    _currentCertThumb = null;
                    _certStoreLocation = "CurrentUser";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='CertThumbprint' LIMIT 1;";
                        var thumb = cmd.ExecuteScalar();
                        if (thumb != null && thumb != DBNull.Value)
                        {
                            _currentCertThumb = Convert.ToString(thumb);
                        }
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='CertStoreLocation' LIMIT 1;";
                        var store = cmd.ExecuteScalar();
                        if (store != null && store != DBNull.Value)
                        {
                            _certStoreLocation = Convert.ToString(store)!;
                        }
                    }

                    var env = tpAmb == 1 ? "Producao" : "Homolog";
                    var thumbText = string.IsNullOrWhiteSpace(_currentCertThumb) ? "Nenhum certificado selecionado" : _currentCertThumb;

                    // Atualiza UI
                    (FindName("TxtCashRegister") as TextBox)!.Text = cash.ToString();
                    (FindName("TxtSerie") as TextBox)!.Text = serie.ToString();
                    (FindName("TxtNextNumber") as TextBox)!.Text = next.ToString();
                    (FindName("CmbEnvironment") as ComboBox)!.SelectedValue = env;
                    (FindName("TxtCert") as TextBox)!.Text = thumbText;
                    (FindName("TxtCSCId") as TextBox)!.Text = cscId ?? string.Empty;
                    (FindName("TxtCSC") as TextBox)!.Text = csc ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configuração NFC-e: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePayments(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        foreach (var m in _methods)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "UPDATE PaymentMethods SET Name=$name, IsEnabled=$enabled, DisplayOrder=$order, LinkType=$link WHERE Id=$id;";
                                cmd.Parameters.AddWithValue("$name", m.Name);
                                cmd.Parameters.AddWithValue("$enabled", m.IsEnabled ? 1 : 0);
                                cmd.Parameters.AddWithValue("$order", m.DisplayOrder);
                                cmd.Parameters.AddWithValue("$link", string.IsNullOrWhiteSpace(m.LinkType) ? (object)DBNull.Value : m.LinkType!);
                                cmd.Parameters.AddWithValue("$id", m.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
                MessageBox.Show("Formas de pagamento atualizadas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar alterações: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseSettings(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void DgPaymentMethods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            var row = ItemsControl.ContainerFromElement(DgPaymentMethods, e.OriginalSource as DependencyObject) as DataGridRow;
            _dragSourceItem = row?.Item as PaymentMethod;
        }

        private void DgPaymentMethods_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue)
            {
                Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (_dragSourceItem != null)
                    {
                        DragDrop.DoDragDrop(DgPaymentMethods, _dragSourceItem, DragDropEffects.Move);
                    }
                }
            }
        }

        private void DgPaymentMethods_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void DgPaymentMethods_Drop(object sender, DragEventArgs e)
        {
            var targetRow = ItemsControl.ContainerFromElement(DgPaymentMethods, e.OriginalSource as DependencyObject) as DataGridRow;
            var targetItem = targetRow?.Item as PaymentMethod;

            var dragged = e.Data.GetData(typeof(PaymentMethod)) as PaymentMethod ?? _dragSourceItem;
            if (dragged != null)
            {
                int oldIndex = _methods.IndexOf(dragged);
                int newIndex = targetItem != null ? _methods.IndexOf(targetItem) : _methods.Count - 1;
                if (oldIndex >= 0)
                {
                    _methods.RemoveAt(oldIndex);
                    if (newIndex < 0) newIndex = 0;
                    if (newIndex > _methods.Count) newIndex = _methods.Count;
                    _methods.Insert(newIndex, dragged);
                    RenumberAndRefresh();
                }
            }

            _dragStartPoint = null;
            _dragSourceItem = null;
        }

        private void DgPaymentMethods_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _methods = _methods
                        .OrderBy(m => m.DisplayOrder)
                        .ToList();
                    RenumberAndRefresh();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void RenumberAndRefresh()
        {
            for (int i = 0; i < _methods.Count; i++)
            {
                _methods[i].DisplayOrder = i + 1;
            }
            DgPaymentMethods.ItemsSource = null;
            DgPaymentMethods.ItemsSource = _methods;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(DgPaymentMethods.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(PaymentMethod.DisplayOrder), ListSortDirection.Ascending));
            view.Refresh();
        }

        private void LoadTEFConfig()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                string Get(string key, string def)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                    cmd.Parameters.AddWithValue("$key", key);
                    var obj = cmd.ExecuteScalar();
                    return (obj == null || obj == DBNull.Value) ? def : Convert.ToString(obj) ?? def;
                }
                var tefType = Get("TEFIntegrationType", "Nenhum");
                (FindName("CmbTEFType") as ComboBox)!.SelectedValue = string.IsNullOrWhiteSpace(tefType) ? "Nenhum" : tefType;
                // Scope
                (FindName("TxtTEFScope") as TextBox)!.Text = Get("TEFScope", string.Empty);
                // SiTef
                (FindName("TxtSitefIP") as TextBox)!.Text = Get("SitefIP", "127.0.0.1");
                (FindName("TxtSitefLoja") as TextBox)!.Text = Get("SitefLoja", "00000000");
                (FindName("TxtSitefTerminal") as TextBox)!.Text = Get("SitefTerminal", "00000001");
                // Pasta de troca
                var defExchange = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEF");
                (FindName("TxtTEFExchangePath") as TextBox)!.Text = Get("TEFExchangePath", defExchange);
                // Debug mode
                var debugRaw = Get("TEFDebugMode", "0");
                (FindName("ChkTEFDebug") as CheckBox)!.IsChecked = debugRaw == "1" || string.Equals(debugRaw, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configuração de TEF: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveTEFConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = (FindName("CmbTEFType") as ComboBox)!.SelectedValue?.ToString() ?? "Nenhum";
                var scope = (FindName("TxtTEFScope") as TextBox)!.Text ?? string.Empty;
                var sitefIP = (FindName("TxtSitefIP") as TextBox)!.Text ?? string.Empty;
                var sitefLoja = (FindName("TxtSitefLoja") as TextBox)!.Text ?? string.Empty;
                var sitefTerminal = (FindName("TxtSitefTerminal") as TextBox)!.Text ?? string.Empty;
                var exchangePath = (FindName("TxtTEFExchangePath") as TextBox)!.Text ?? string.Empty;
                var debugFlag = (FindName("ChkTEFDebug") as CheckBox)!.IsChecked == true ? "1" : "0";

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "TEFIntegrationType", selected);
                SaveOrUpdateSetting(conn, "TEFScope", scope);
                SaveOrUpdateSetting(conn, "SitefIP", string.IsNullOrWhiteSpace(sitefIP) ? "127.0.0.1" : sitefIP);
                SaveOrUpdateSetting(conn, "SitefLoja", string.IsNullOrWhiteSpace(sitefLoja) ? "00000000" : sitefLoja);
                SaveOrUpdateSetting(conn, "SitefTerminal", string.IsNullOrWhiteSpace(sitefTerminal) ? "00000001" : sitefTerminal);
                SaveOrUpdateSetting(conn, "TEFExchangePath", string.IsNullOrWhiteSpace(exchangePath) ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEF") : exchangePath);
                SaveOrUpdateSetting(conn, "TEFDebugMode", debugFlag);

                MessageBox.Show("Configuração de TEF salva.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configuração de TEF: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void SaveOrUpdateSetting(SqliteConnection conn, string key, string value)
        {
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                sel.Parameters.AddWithValue("$key", key);
                var countObj = sel.ExecuteScalar();
                int count = Convert.ToInt32(countObj);
                
                if (count > 0)
                {
                    using (var upd = conn.CreateCommand())
                    {
                        upd.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                        upd.Parameters.AddWithValue("$val", value);
                        upd.Parameters.AddWithValue("$key", key);
                        upd.ExecuteNonQuery();
                    }
                }
                else
                {
                    using (var ins = conn.CreateCommand())
                    {
                        ins.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                        ins.Parameters.AddWithValue("$key", key);
                        ins.Parameters.AddWithValue("$val", value);
                        ins.ExecuteNonQuery();
                    }
                }
            }
        }

        // --- Impressoras ---
        private void LoadPrinters()
        {
            try
            {
                var printers = new List<string>();
                var server = new LocalPrintServer();
                var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });
                foreach (var q in queues)
                {
                    if (!string.IsNullOrWhiteSpace(q.Name))
                        printers.Add(q.Name);
                }
                printers.Sort(StringComparer.OrdinalIgnoreCase);

                (FindName("CmbPrinterBoleto") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterConfissao") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterNFCe") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterCarne80") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterCarneA4") as ComboBox)!.ItemsSource = printers;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao listar impressoras: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }


        private void ImportERPInOptions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nome = (FindName("TxtOptCliente") as TextBox)?.Text?.Trim() ?? string.Empty;
                var numero = (FindName("TxtOptNumeroPedido") as TextBox)?.Text?.Trim() ?? string.Empty;
                var valorRaw = (FindName("TxtOptValorCompra") as TextBox)?.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(nome))
                {
                    MessageBox.Show("Informe o nome do cliente.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    (FindName("TxtOptCliente") as TextBox)?.Focus();
                    return;
                }
                if (string.IsNullOrWhiteSpace(numero))
                {
                    MessageBox.Show("Informe o número do pedido.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    (FindName("TxtOptNumeroPedido") as TextBox)?.Focus();
                    return;
                }
                if (!decimal.TryParse(valorRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var valor) || valor <= 0)
                {
                    MessageBox.Show("Informe o valor da compra (maior que zero).", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    (FindName("TxtOptValorCompra") as TextBox)?.Focus();
                    return;
                }

                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(GetConnectionString());
                conn.Open();
                EnsureErpImportsTable(conn);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO ErpSalesOrderImports (CustomerName, OrderNumber, OrderValue) VALUES ($c, $n, $v);";
                cmd.Parameters.AddWithValue("$c", nome);
                cmd.Parameters.AddWithValue("$n", numero);
                cmd.Parameters.AddWithValue("$v", valor);
                cmd.ExecuteNonQuery();

                MessageBox.Show("Pedido importado com sucesso.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao importar pedido: {ex.Message}", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearERPForm_Click(object sender, RoutedEventArgs e)
        {
            (FindName("TxtOptCliente") as TextBox)!.Text = string.Empty;
            (FindName("TxtOptNumeroPedido") as TextBox)!.Text = string.Empty;
            (FindName("TxtOptValorCompra") as TextBox)!.Text = string.Empty;
        }

        private void EnsureErpImportsTable(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ErpSalesOrderImports (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerName TEXT NOT NULL,
                    OrderNumber TEXT NOT NULL,
                    OrderValue REAL NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ErpSalesOrderImports_OrderNumber ON ErpSalesOrderImports(OrderNumber);
            ";
            cmd.ExecuteNonQuery();
        }


        private void LoadOptions()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                        var obj = cmd.ExecuteScalar();
                        string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        bool enabled = true;
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            enabled = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkEnableF9") as CheckBox)!.IsChecked = enabled;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                        var obj2 = cmd.ExecuteScalar();
                        string? val2 = (obj2 == null || obj2 == DBNull.Value) ? null : Convert.ToString(obj2);
                        bool prompt = true;
                        if (!string.IsNullOrWhiteSpace(val2))
                        {
                            prompt = val2 == "1" || string.Equals(val2, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkPromptConsumer") as CheckBox)!.IsChecked = prompt;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "RequireF2ToStartSale");
                        var obj4 = cmd.ExecuteScalar();
                        string? val4 = (obj4 == null || obj4 == DBNull.Value) ? null : Convert.ToString(obj4);
                        bool requireF2 = false;
                        if (!string.IsNullOrWhiteSpace(val4))
                        {
                            requireF2 = val4 == "1" || string.Equals(val4, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkRequireF2ToStartSale") as CheckBox)!.IsChecked = requireF2;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "MaxDiscountPercent");
                        var objMax = cmd.ExecuteScalar();
                        string? valMax = (objMax == null || objMax == DBNull.Value) ? null : Convert.ToString(objMax);
                        (FindName("TxtMaxDiscountPercent") as TextBox)!.Text = string.IsNullOrWhiteSpace(valMax) ? string.Empty : valMax;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "RequireSupervisorForSangria");
                        var obj3 = cmd.ExecuteScalar();
                        string? val3 = (obj3 == null || obj3 == DBNull.Value) ? null : Convert.ToString(obj3);
                        bool requireSupervisor = false;
                        if (!string.IsNullOrWhiteSpace(val3))
                        {
                            requireSupervisor = val3 == "1" || string.Equals(val3, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkRequireSupervisorForSangria") as CheckBox)!.IsChecked = requireSupervisor;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar opções: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveNFCeConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var txtCash = (FindName("TxtCashRegister") as TextBox)!;
                var txtSerie = (FindName("TxtSerie") as TextBox)!;
                var txtNext = (FindName("TxtNextNumber") as TextBox)!;
                var cmbEnv = (FindName("CmbEnvironment") as ComboBox)!;
                var txtCSCId = (FindName("TxtCSCId") as TextBox)!;
                var txtCSC = (FindName("TxtCSC") as TextBox)!;

                int cash = int.TryParse(txtCash.Text, out var c1) ? c1 : 1;
                int serie = int.TryParse(txtSerie.Text, out var s1) ? s1 : 1;
                int next = int.TryParse(txtNext.Text, out var n1) ? n1 : 1;
                string env = (cmbEnv.SelectedValue as string) ?? "Homolog";
                int tpAmb = string.Equals(env, "Producao", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                string cscId = (txtCSCId.Text ?? string.Empty).Trim();
                string csc = (txtCSC.Text ?? string.Empty).Trim();

                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();

                    SaveOrUpdateSetting(conn, "CashRegisterNumber", cash.ToString());
                    SaveOrUpdateSetting(conn, "CertThumbprint", _currentCertThumb ?? string.Empty);
                    SaveOrUpdateSetting(conn, "CertStoreLocation", _certStoreLocation);

                    int count = 0;
                    using (var sel = conn.CreateCommand())
                    {
                        sel.CommandText = "SELECT COUNT(*) FROM ConfiguracoesNFCe WHERE Id=1;";
                        var obj = sel.ExecuteScalar();
                        count = Convert.ToInt32(obj);
                    }

                    if (count > 0)
                    {
                        using (var upd = conn.CreateCommand())
                        {
                            upd.CommandText = @"UPDATE ConfiguracoesNFCe SET TpAmb=$tpAmb, CSCId=$cscId, CSC=$csc, Serie=$serie, ProximoNumero=$next WHERE Id=1;";
                            upd.Parameters.AddWithValue("$tpAmb", tpAmb);
                            upd.Parameters.AddWithValue("$cscId", string.IsNullOrWhiteSpace(cscId) ? (object)DBNull.Value : cscId);
                            upd.Parameters.AddWithValue("$csc", string.IsNullOrWhiteSpace(csc) ? (object)DBNull.Value : csc);
                            upd.Parameters.AddWithValue("$serie", serie);
                            upd.Parameters.AddWithValue("$next", next);
                            upd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var ins = conn.CreateCommand())
                        {
                            ins.CommandText = @"INSERT INTO ConfiguracoesNFCe (Id, TpAmb, CSCId, CSC, Serie, ProximoNumero) VALUES (1, $tpAmb, $cscId, $csc, $serie, $next);";
                            ins.Parameters.AddWithValue("$tpAmb", tpAmb);
                            ins.Parameters.AddWithValue("$cscId", string.IsNullOrWhiteSpace(cscId) ? (object)DBNull.Value : cscId);
                            ins.Parameters.AddWithValue("$csc", string.IsNullOrWhiteSpace(csc) ? (object)DBNull.Value : csc);
                            ins.Parameters.AddWithValue("$serie", serie);
                            ins.Parameters.AddWithValue("$next", next);
                            ins.ExecuteNonQuery();
                        }
                    }
                }
                MessageBox.Show("Configuração NFC-e salva.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configuração NFC-e: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void SaveGeneral_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var chk = (FindName("ChkImportERPOrders") as CheckBox)!;
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "ImportERPOrdersEnabled", chk.IsChecked == true ? "1" : "0");
                MessageBox.Show("Configurações gerais salvas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configurações gerais: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadGeneralOptions()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                        var obj = cmd.ExecuteScalar();
                        string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        bool enabled = false;
                        if (obj != null && obj != DBNull.Value)
                        {
                            var s = Convert.ToString(obj);
                            enabled = s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkEnableF9") as CheckBox)!.IsChecked = enabled;
                    }
                }
            }
            catch
            {
                // ignore load errors to avoid breaking UI
            }
        }

        private void LoadPrinterSettings()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                string Get(string key)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                    cmd.Parameters.AddWithValue("$key", key);
                    var obj = cmd.ExecuteScalar();
                    return (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj) ?? string.Empty;
                }

                (FindName("CmbPrinterBoleto") as ComboBox)!.SelectedItem = Get("PrinterBoleto");
                (FindName("CmbPrinterConfissao") as ComboBox)!.SelectedItem = Get("PrinterConfissao");
                (FindName("CmbPrinterNFCe") as ComboBox)!.SelectedItem = Get("PrinterNFCe");
                (FindName("CmbPrinterCarne80") as ComboBox)!.SelectedItem = Get("PrinterCarne80");
                (FindName("CmbPrinterCarneA4") as ComboBox)!.SelectedItem = Get("PrinterCarneA4");
                var formato = Get("CarneFormatoPadrao");
                (FindName("CmbCarneFormatoPadrao") as ComboBox)!.SelectedValue = string.IsNullOrWhiteSpace(formato) ? "80mm" : formato;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar impressoras: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SavePrinterSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "PrinterBoleto", (FindName("CmbPrinterBoleto") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterConfissao", (FindName("CmbPrinterConfissao") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterNFCe", (FindName("CmbPrinterNFCe") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterCarne80", (FindName("CmbPrinterCarne80") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterCarneA4", (FindName("CmbPrinterCarneA4") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                var formatoPadrao = (FindName("CmbCarneFormatoPadrao") as ComboBox)!.SelectedValue?.ToString() ?? "80mm";
                SaveOrUpdateSetting(conn, "CarneFormatoPadrao", formatoPadrao);
                MessageBox.Show("Impressoras salvas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar impressoras: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void SelectCert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var hasPrivate = new X509Certificate2Collection();
                foreach (var cert in store.Certificates)
                {
                    if (cert.HasPrivateKey)
                    {
                        hasPrivate.Add(cert);
                    }
                }
                var selected = X509Certificate2UI.SelectFromCollection(hasPrivate, "Selecione certificado", "Escolha o certificado com chave privada para assinar NFC-e/NFe", X509SelectionFlag.SingleSelection);
                if (selected != null && selected.Count > 0)
                {
                    var cert = selected[0];
                    _currentCertThumb = cert.Thumbprint;
                    _certStoreLocation = "CurrentUser";
                    (FindName("TxtCert") as TextBox)!.Text = $"{cert.Subject} ({cert.Thumbprint})";
                }
                store.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao selecionar certificado: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DebugTEF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Salvar antes de testar
                SaveTEFConfig_Click(sender, e);

                var tef = TEFManager.Instance;
                var ok = tef.InitializeTEF();
                if (!ok)
                {
                    MessageBox.Show("Falha ao inicializar TEF. Verifique tipo e parâmetros.", "TEF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = tef.ProcessPayment(1.00m, "Credito");
                var status = result.Success ? "APROVADO" : "NEGADO";
                var msg = result.Message ?? string.Empty;
                var nsu = result.TransactionId ?? string.Empty;
                var auth = result.AuthorizationCode ?? string.Empty;
                MessageBox.Show($"Status: {status}\nMensagem: {msg}\nNSU: {nsu}\nAutorização: {auth}", "Teste TEF (Debug)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro no teste de TEF: {ex.Message}", "TEF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveOptions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var chk = (FindName("ChkEnableF9") as CheckBox)!;
                int flag = (chk.IsChecked == true) ? 1 : 0;

                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();

                    using (var sel = conn.CreateCommand())
                    {
                        sel.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                        sel.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                        var countObj = sel.ExecuteScalar();
                        int count = Convert.ToInt32(countObj);
                        if (count > 0)
                        {
                            using (var upd = conn.CreateCommand())
                            {
                                upd.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                                upd.Parameters.AddWithValue("$val", flag.ToString());
                                upd.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                                upd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var ins = conn.CreateCommand())
                            {
                                ins.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                                ins.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                                ins.Parameters.AddWithValue("$val", flag.ToString());
                                ins.ExecuteNonQuery();
                            }
                        }
                    }

                    // Atualiza/insere opção: Solicitar consumidor ao iniciar a venda?
                    var chkPrompt = (FindName("ChkPromptConsumer") as CheckBox)!;
                    int promptFlag = (chkPrompt.IsChecked == true) ? 1 : 0;

                    using (var sel2 = conn.CreateCommand())
                    {
                        sel2.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                        sel2.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                        var countObj2 = sel2.ExecuteScalar();
                        int count2 = Convert.ToInt32(countObj2);
                        if (count2 > 0)
                        {
                            using (var upd2 = conn.CreateCommand())
                            {
                                upd2.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                                upd2.Parameters.AddWithValue("$val", promptFlag.ToString());
                                upd2.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                                upd2.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var ins2 = conn.CreateCommand())
                            {
                                ins2.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                                ins2.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                                ins2.Parameters.AddWithValue("$val", promptFlag.ToString());
                                ins2.ExecuteNonQuery();
                            }
                        }
                    }

                    // Salva configurações do supervisor
                    // RequireSupervisorForSangria
                    var chkRequireSupervisor = (FindName("ChkRequireSupervisorForSangria") as CheckBox)!;
                    int requireSupervisorFlag = (chkRequireSupervisor.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "RequireSupervisorForSangria", requireSupervisorFlag.ToString());

                    // Salva configuração: Exigir F2 para iniciar venda
                    var chkRequireF2 = (FindName("ChkRequireF2ToStartSale") as CheckBox)!;
                    int requireF2Flag = (chkRequireF2.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "RequireF2ToStartSale", requireF2Flag.ToString());

                    // Campos de cadastro do supervisor removidos: não salvar código/senha/nome do supervisor aqui.

                    // Salva desconto máximo (%)
                    var txtMax = (FindName("TxtMaxDiscountPercent") as TextBox)!;
                    var raw = (txtMax.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        SaveOrUpdateSetting(conn, "MaxDiscountPercent", raw);
                    }
                }
                MessageBox.Show("Opções salvas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar opções: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }
}