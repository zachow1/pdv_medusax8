using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.ComponentModel;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Models;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class MainWindow : Window
    {
        private decimal currentSaleTotal = 0m;
        private bool _isParsingCodeInput = false;
        private bool _modoAlteracaoPrecoF9 = false;
        private bool _f9Enabled = true;
        private bool _promptConsumerOnFirstItem = true;
        // Controla se já foi exibido o prompt de consumidor nesta venda
        private bool _consumerPromptedAlready = false;
        
        // Exigir F2 para iniciar venda
        private bool _requireF2ToStartSale = false;
        private bool _saleStarted = false;
        
        // Carrinho e Catálogo
        private readonly List<CartItem> _cartItems = new List<CartItem>();
        private readonly Dictionary<string, (string Description, decimal UnitPrice)> _catalog = new Dictionary<string, (string, decimal)>(StringComparer.OrdinalIgnoreCase);

        // Dados do consumidor (para NFC-e)
        private string? _consumerName;
        private string? _consumerCPF;
        private int? _consumerCustomerId;

        // Expor status/inclusão de consumidor para outras janelas
        public bool HasConsumer => !string.IsNullOrWhiteSpace(_consumerName);
        public string? ConsumerName => _consumerName;
        public string? ConsumerCPF => _consumerCPF;
        public int? ConsumerCustomerId => _consumerCustomerId;

        // Novo: cliente real selecionado (ID de cliente)
        public bool HasCustomerSelected => _consumerCustomerId.HasValue && _consumerCustomerId.Value > 0;

        // Configurações do supervisor
        private bool _requireSupervisorForSangria = false;
        private bool _requireSupervisorForOpeningCash = false;
        private bool _requireSupervisorForClosingCash = false;
        private string _supervisorCode = string.Empty;
        private string _supervisorPassword = string.Empty;
        private string _supervisorName = string.Empty;
        
        // Expor configuração e validação de supervisor
        public bool RequiresSupervisorForSangria => _requireSupervisorForSangria;
        public bool RequiresSupervisorForOpeningCash => _requireSupervisorForOpeningCash;
        public bool RequiresSupervisorForClosingCash => _requireSupervisorForClosingCash;
        public string SupervisorCode => _supervisorCode;
        public string SupervisorPassword => _supervisorPassword;
        public string SupervisorName => _supervisorName;
        public bool ValidateSupervisor(string code, string password)
        {
            var c = code?.Trim() ?? string.Empty;
            var p = password ?? string.Empty;
            return string.Equals(_supervisorCode, c, StringComparison.Ordinal) &&
                   string.Equals(_supervisorPassword, p, StringComparison.Ordinal);
        }

        private void TrySwitchOperator()
        {
            // Fluxo correto: manter sessão e caixa ativos, abrir login modal
            var loginWindow = new LoginWindow(authorizationMode: true) { Owner = this };
            var loginResult = loginWindow.ShowDialog();

            if (loginResult == true && !string.IsNullOrWhiteSpace(loginWindow.LoggedUser))
            {
                var newUser = loginWindow.LoggedUser;

                // Troca de usuário mantendo sessão operacional e caixa aberto
                Services.SessionManager.StartSession(newUser);

                // Atualiza status visual (rodapé)
                try
                {
                    int cash = GetCurrentCashRegisterNumber();
                    var tb = this.FindName("TxtOperatorStatus") as System.Windows.Controls.TextBlock;
                    if (tb != null)
                    {
                        tb.Text = $"Operador: {newUser} | Caixa: {cash:00}";
                    }
                }
                catch { }
            }
            else
            {
                // Cancelado: não altera nada
                return;
            }
        }

        private int GetCurrentCashRegisterNumber()
        {
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(PDV_MedusaX8.Services.DbHelper.GetConnectionString());
                conn.Open();
                return GetCurrentCashRegisterNumber(conn);
            }
            catch { }
            return 1;
        }

        private int GetCurrentCashRegisterNumber(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            int cashNumber = 1;
            try
            {
                using var cmdGet = conn.CreateCommand();
                cmdGet.CommandText = "SELECT Value FROM Settings WHERE Key='CashRegisterNumber' LIMIT 1";
                var v = cmdGet.ExecuteScalar();
                if (v != null && v != DBNull.Value && int.TryParse(v.ToString(), out var n)) cashNumber = n;
                if (cashNumber == 0)
                {
                    using var cmdSerie = conn.CreateCommand();
                    cmdSerie.CommandText = "SELECT Serie FROM ConfiguracoesNFCe LIMIT 1";
                    var sv = cmdSerie.ExecuteScalar();
                    if (sv != null && sv != DBNull.Value && int.TryParse(sv.ToString(), out var s)) cashNumber = s;
                }
                if (cashNumber == 0) cashNumber = 1;
            }
            catch { }
            return cashNumber;
        }

        private int GetOpenCashSessionId(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id FROM CashSessions WHERE ClosedAt IS NULL ORDER BY OpenedAt DESC LIMIT 1";
                var v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value && int.TryParse(v.ToString(), out var id)) return id;
            }
            catch { }
            return 0;
        }

        public MainWindow()
        {
            InitializeComponent();
            
            // Verificar se há uma sessão válida
            if (!SessionManager.IsLoggedIn || !SessionManager.IsSessionValid())
            {
                MessageBox.Show("Sessão expirada. Faça login novamente.", "Sessão Expirada", MessageBoxButton.OK, MessageBoxImage.Warning);
                SessionManager.EndSession();
                
                // Abrir tela de login
                LoginWindow loginWindow = new LoginWindow();
                loginWindow.Show();
                this.Close();
                return;
            }
            
            // Atualizar título da janela com usuário logado
            this.Title = $"PDV MedusaX8 - Usuário: {SessionManager.CurrentUser}";
            
            InitializeDatabase();
            LoadCartItems();
            LoadF9OptionFlag();
            LoadPromptConsumerFlag();
            LoadSupervisorSettings();
            // Carrega flag: exigir F2 para iniciar venda
            LoadRequireF2StartSaleFlag();
            InitProductCatalog();
            
            // Popula tabela de produtos a partir do catálogo, se estiver vazia
            EnsureProductTableSeededFromCatalog();

            // Inicializa o TEF
            InitializeTEF();

            // Ajusta a visibilidade do atalho de Importar ERP conforme configuração
            UpdateERPImportShortcutVisibility();

            // Carrega logo personalizado se configurado
            LoadCustomLogo();

            // Exigir abertura de caixa ao iniciar o PDV
            try
            {
                if (!HasActiveCashSession())
                {
                    var ab = new AberturaCaixaWindow { Owner = this };
                    var ok = ab.ShowDialog();
                    if (ok != true)
                    {
                        MessageBox.Show("Abertura de caixa é obrigatória. O PDV será encerrado.", "Abertura obrigatória", MessageBoxButton.OK, MessageBoxImage.Warning);
                        this.Close();
                        return;
                    }
                    // Após abertura, atualizar aviso de situação
                    try { RefreshCashSessionStatus(); } catch { }
                }
            }
            catch { /* ignore */ }
            // Sincroniza UI inicial conforme exigência de F2, sem alerta modal
            UpdateRequireF2UiState(showPrompt: false);

            // Coloca o foco no campo de código ao carregar a janela, sem alertas automáticos
            this.Loaded += (sender, e) => {
                if (_requireF2ToStartSale) { _saleStarted = false; }
                InputCodeField.Focus();
                // Atualiza aviso de situação do caixa ao carregar a janela
                try { RefreshCashSessionStatus(); } catch { }
                
                // Sincroniza alturas dos borders quando a janela carrega
                try { SyncBorderHeights(); } catch { }
                
                // Adiciona evento para sincronizar quando o RightTopBorder mudar de tamanho
                if (RightTopBorder != null)
                {
                    RightTopBorder.SizeChanged += (s, args) => {
                        try { SyncBorderHeights(); } catch { }
                    };
                }
            };
            // Ajustes responsivos simples: alinhar topo dos boxes 65%/35% em telas grandes
            this.SizeChanged += MainWindow_SizeChanged;
        }

        private void UpdateERPImportShortcutVisibility()
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$k LIMIT 1;";
                cmd.Parameters.AddWithValue("$k", "ImportERPOrdersEnabled");
                var v = cmd.ExecuteScalar();
                bool enabled = false;
                if (v != null && v != DBNull.Value)
                {
                    var s = Convert.ToString(v);
                    enabled = s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                }

                try { if (BtnERPImportShortcut != null) BtnERPImportShortcut.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; } catch { }
            }
            catch
            {
                try { if (BtnERPImportShortcut != null) BtnERPImportShortcut.Visibility = Visibility.Collapsed; } catch { }
            }
        }

        // Breakpoints de largura (em DIPs) para responsividade
        private const double BreakpointLarge = 1600;   // ~17"+
        private const double BreakpointXL = 1920;      // telas muito largas

        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            try
            {
                ApplyResponsiveLayout();
                SyncBorderHeights();
            }
            catch { /* ajustes visuais não devem quebrar */ }
        }

        // Aplica ajustes progressivos de layout com base na largura efetiva da janela
        private void ApplyResponsiveLayout()
        {
            double w = this.ActualWidth;

            // Margens finas no topo para manter alinhamento visual
            if (LeftTopBorder != null && RightTopBorder != null)
            {
                if (w >= BreakpointLarge)
                {
                    LeftTopBorder.Margin = new Thickness(0, 5, 0, 0);
                    RightTopBorder.Margin = new Thickness(0, 5, 0, 5);
                }
                else
                {
                    LeftTopBorder.Margin = new Thickness(0, 0, 0, 0);
                    RightTopBorder.Margin = new Thickness(0, 0, 0, 5);
                }
            }

            // Densidade do DataGrid (altura da linha) por breakpoint
            try
            {
                if (DgvProdutos != null)
                {
                    if (w >= BreakpointXL)
                        DgvProdutos.RowHeight = 44; // aumenta altura para conforto em telas muito largas
                    else if (w >= BreakpointLarge)
                        DgvProdutos.RowHeight = 40; // leve incremento
                    else
                        DgvProdutos.RowHeight = 36; // padrão atual
                }
            }
            catch { /* ajustes visuais não devem quebrar */ }
        }

        // Sincroniza alturas dos painéis superiores das colunas esquerda e direita
        private void SyncBorderHeights()
        {
            if (RightTopBorder != null && LeftTopBorder != null)
            {
                // Remover bindings caso existam
                BindingOperations.ClearBinding(LeftTopBorder, Border.HeightProperty);
                BindingOperations.ClearBinding(RightTopBorder, Border.HeightProperty);

                // Define uma altura comum para ambos (usa o menor para não estourar o layout)
                var hLeft = LeftTopBorder.ActualHeight;
                var hRight = RightTopBorder.ActualHeight;
                var desired = Math.Min(hLeft > 0 ? hLeft : hRight, hRight > 0 ? hRight : hLeft);

                if (desired > 0)
                {
                    LeftTopBorder.Height = desired;
                    RightTopBorder.Height = desired;
                }
            }
        }

        public void SetConsumer(string? name, string? cpf, int? customerId)
        {
            _consumerName = name ?? string.Empty;
            _consumerCPF = cpf ?? string.Empty;
            _consumerCustomerId = customerId;
            UpdateConsumerLabels();
            UpdateWindowCloseState();
            // Atualiza estado visual (habilitado/desabilitado) dos atalhos conforme venda/carrinho
            UpdateShortcutUiEnabledStates();
        }

        // Solicita seleção de consumidor (janela rápida) quando necessário
        public bool EnsureConsumerSelected()
        {
            if (HasConsumer) return true;
            var qw = new ConsumidorQuickWindow();
            qw.Owner = this;
            var ok = qw.ShowDialog();
            if (ok == true)
            {
                SetConsumer(qw.ResultName, qw.ResultCPF, null);
                return true;
            }
            return false;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_cartItems.Count > 0)
            {
                e.Cancel = true;
                MessageBox.Show("Não é possível fechar enquanto há itens na venda.", "Fechamento bloqueado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Perguntar fechamento do caixa ao encerrar a janela
            try
            {
                if (HasActiveCashSession())
                {
                    var ask = MessageBox.Show("Deseja realizar o fechamento do caixa agora?", "Fechar Caixa", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (ask == MessageBoxResult.Yes)
                    {
                        string? supervisor = null;
                        var askSup = MessageBox.Show("Deseja incluir um supervisor na validação?", "Supervisor", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (askSup == MessageBoxResult.Yes)
                        {
                            var auth = new LoginWindow(authorizationMode: true) { Owner = this };
                            var ok = auth.ShowDialog();
                            if (ok == true && (string.Equals(auth.LoggedRole, "admin", StringComparison.OrdinalIgnoreCase) || string.Equals(auth.LoggedRole, "fiscal", StringComparison.OrdinalIgnoreCase)))
                            {
                                supervisor = auth.LoggedUser;
                            }
                            else
                            {
                                MessageBox.Show("Acesso de supervisor negado ou cancelado.", "Supervisor", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        var fc = new FechamentoCaixaWindow();
                        fc.Owner = this;
                        fc.ClosedByUser = SessionManager.CurrentUser;
                        fc.SupervisorUser = supervisor;
                        fc.ShowDialog();
                        // Atualiza aviso ao retornar do fechamento
                        try { RefreshCashSessionStatus(); } catch { }
                        // Se ainda houver sessão ativa, confirmar se deve continuar encerrando
                        if (HasActiveCashSession())
                        {
                            var cont = MessageBox.Show("Fechamento não concluído. Deseja sair mesmo assim?", "Encerrar", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (cont == MessageBoxResult.No)
                            {
                                e.Cancel = true;
                                return;
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            // Apenas delega para o fluxo centralizado no evento Closing
            // para evitar prompts duplicados quando o usuário clica no botão X.
            if (_cartItems.Count > 0)
            {
                MessageBox.Show("Não é possível fechar enquanto há itens na venda.", "Fechamento bloqueado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            this.Close();
        }

        private void UpdateWindowCloseState()
        {
            bool hasItems = _cartItems.Count > 0;
            try
            {
                if (BtnCloseWindow != null)
                {
                    BtnCloseWindow.IsEnabled = !hasItems;
                }
            }
            catch { /* ignore */ }
        }

        private void LoadCartItems()
        {
            DgvProdutos.ItemsSource = _cartItems;
            currentSaleTotal = _cartItems.Sum(i => i.Total);
            LblFinalTotal.Text = $"R$ {currentSaleTotal:N2}";
            UpdateWindowCloseState();
        }


        private void ShortcutAction(object sender, RoutedEventArgs e)
        {
            string? tag = null;
            string? key = null;
            if (sender is FrameworkElement fe)
            {
                tag = fe.Tag as string;
                if (fe is Button btn)
                {
                    key = btn.Content?.ToString()?.Split('\n')[0].Trim();
                }
            }
            string mode = !string.IsNullOrWhiteSpace(tag) ? tag : key ?? string.Empty;
            if (!string.IsNullOrEmpty(mode))
            {
                HandleShortcutMode(mode);
            }
        }

        private void MenuShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe)
                {
                    var mode = fe.Tag as string;
                    if (string.IsNullOrWhiteSpace(mode))
                    {
                        return;
                    }
                    HandleShortcutMode(mode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao executar atalho: {ex.Message}", "Atalhos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSettingsHeader_Click(object sender, RoutedEventArgs e)
        {
            // Clique esquerdo: abre Configurações (F4)
            try { HandleShortcutMode("F4"); } catch { }
        }

        private void BtnSettingsHeader_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Apenas abre o menu se estiver com CTRL pressionado
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (sender is Button btn && btn.ContextMenu != null)
                    {
                        btn.ContextMenu.PlacementTarget = btn;
                        btn.ContextMenu.IsOpen = true;
                    }
                }
                // Bloqueia o comportamento padrão de abrir menu no clique direito
                e.Handled = true;
            }
            catch { /* ignore */ }
        }

        private void MainGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                if (sender is Grid grid && grid.ContextMenu != null)
                {
                    // Restrição: venda ativa apenas quando F2 é exigido e já iniciada; ou há itens no carrinho
                    bool restrict = (_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0;
                    foreach (var obj in grid.ContextMenu.Items)
                    {
                        if (obj is MenuItem mi)
                        {
                            var tag = mi.Tag?.ToString();
                            if (tag == "F4" || tag == "CTRL+L" || tag == "Ctrl+L" ||
                                tag == "CTRL+S" || tag == "Ctrl+S" ||
                                tag == "CTRL+F" || tag == "Ctrl+F" ||
                                tag == "NFCe" || tag == "CTRL+R" || tag == "Ctrl+R" ||
                                tag == "CTRL+O" || tag == "Ctrl+O")
                            {
                                mi.IsEnabled = !restrict;
                                mi.ToolTip = restrict ? "Indisponível com venda aberta ou itens no carrinho" : null;
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        private void HandleShortcutMode(string mode)
        {
            App.Log($"Shortcut: {mode}");
            switch (mode)
            {
                case "F1":
                    App.Log("Shortcut F1: Abrir Cancelar Item");
                    OpenCancelItemWindow();
                    break;
                case "F2":
                    if (_requireF2ToStartSale && !_saleStarted)
                    {
                        _saleStarted = true;
                        try { InputCodeField.IsEnabled = true; } catch { }
                        InputCodeField.Focus();
                        try { TxtStatusBar.Text = string.Empty; } catch { }
                        MessageBox.Show("Venda iniciada.", "Iniciar venda", MessageBoxButton.OK, MessageBoxImage.Information);
                        // Atualiza visibilidade dos atalhos imediatamente ao iniciar a venda
                        UpdateShortcutUiEnabledStates();
                        // Ao iniciar a venda com F2, solicitar consumidor imediatamente se configurado e ainda não houver
                        if (_promptConsumerOnFirstItem && !HasConsumer && !_consumerPromptedAlready)
                        {
                            EnsureConsumerSelected();
                            _consumerPromptedAlready = true;
                        }
                        // Reflete nova condição na UI de atalhos
                        UpdateShortcutUiEnabledStates();
                    }
                    else if (_requireF2ToStartSale)
                    {
                        MessageBox.Show("Venda já iniciada.", "Iniciar venda", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    break;
                case "F3":
                    // Abre a tela de pagamento com o total atual da venda
                    if (_cartItems.Count > 0)
                    {
                        var pw = new PaymentWindow(currentSaleTotal);
                        pw.Owner = this;
                        var okPay = pw.ShowDialog();
                        if (okPay == true)
                        {
                            try
                            {
                                var payments = pw.GetAppliedPaymentsSnapshot();
                                var svc = new Services.NFCeService();
                                svc.EmitirNFCe(this, GetCartItemsSnapshot(), currentSaleTotal, _consumerName, _consumerCPF, payments);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Falha ao preparar NFC-e: {ex.Message}", "NFC-e", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            FinalizeSale();
                        }
                    }
                    else
                    {
                        MessageBox.Show("Não é possível abrir o pagamento sem produtos no carrinho.", "Carrinho vazio", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    break;
                case "F4":
                    // Bloquear Configurações quando houver venda aberta ou itens
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    // Exigir autorização de administrador ou fiscal antes de abrir as Configurações
                    var auth = new LoginWindow(authorizationMode: true) { Owner = this };
                    var okAuth = auth.ShowDialog();
                    if (okAuth == true &&
                        (string.Equals(auth.LoggedRole, "admin", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(auth.LoggedRole, "fiscal", StringComparison.OrdinalIgnoreCase)))
                    {
                        var sw = new SettingsWindow();
                        sw.Owner = this;
                        sw.ShowDialog();
                        // Após fechar Configurações, recarrega flags e sincroniza UI
                        LoadRequireF2StartSaleFlag();
                        LoadPromptConsumerFlag();
                        LoadF9OptionFlag();
                        LoadSupervisorSettings();
                        UpdateERPImportShortcutVisibility();
                        UpdateRequireF2UiState(showPrompt: true);
                    }
                    else
                    {
                        MessageBox.Show("Acesso negado. Necessário usuário administrador ou fiscal.", "Autorização", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;
                case "F5":
                    CancelSale();
                    UpdateWindowCloseState();
                    break;
                case "F6":
                case "consultar":
                    var psw = new ProdutosSearchWindow();
                    psw.Owner = this;
                    var psResult = psw.ShowDialog();
                    if (psResult == true && psw.SelectedEntry != null)
                    {
                        if (_modoAlteracaoPrecoF9)
                        {
                            var codeSel = psw.SelectedEntry.Code;
                            var item = _cartItems.FirstOrDefault(i => i.Codigo == codeSel && !i.IsCancellation);
                            if (item == null)
                            {
                                MessageBox.Show($"Código não encontrado no carrinho: {codeSel}", "Alterar preço", MessageBoxButton.OK, MessageBoxImage.Warning);
                                try { InputCodeField.Focus(); InputCodeField.SelectAll(); } catch { }
                                try { TxtStatusBar.Text = "Modo alteração de preço ativo"; } catch { }
                            }
                            else
                            {
                                var win = new AlterarPrecoWindow(item.Descricao, item.Codigo, item.VlUnit);
                                win.Owner = this;
                                var ok = win.ShowDialog();
                                if (ok == true)
                                {
                                    item.VlUnit = win.NovoPreco;
                                    item.Total = item.VlUnit * (decimal)item.Qt - item.DiscountApplied;
                                    try { DgvProdutos.Items.Refresh(); } catch { }
                                    UpdateTotalsAndRefresh();
                                    _modoAlteracaoPrecoF9 = false;
                                    try { TxtStatusBar.Text = string.Empty; } catch { }
                                    InputCodeField.Text = string.Empty;
                                    InputQtyField.Text = string.Empty;
                                }
                                else
                                {
                                    try { InputCodeField.Focus(); InputCodeField.SelectAll(); } catch { }
                                }
                            }
                        }
                        else
                        {
                            AddOrMergeCartItem(psw.SelectedEntry.Code, psw.SelectedEntry.Description, (double)psw.SelectedQty, psw.SelectedEntry.Price);
                        }
                    }
                    break;
                case "cliente":
                case "F7":
                    var cw = new ConsumidorWindow();
                    cw.Owner = this;
                    var res = cw.ShowDialog();
                    if (res == true)
                    {
                        SetConsumer(cw.ConsumerName, cw.ConsumerCPF, cw.SelectedCustomerId);
                    }
                    break;
                case "consumidor":
                    var qw = new ConsumidorQuickWindow();
                    qw.Owner = this;
                    var qok = qw.ShowDialog();
                    if (qok == true)
                    {
                        SetConsumer(qw.ResultName, qw.ResultCPF, null);
                    }
                    break;
                case "F12":
                    var qwin = new ConsumidorQuickWindow();
                    qwin.Owner = this;
                    var qres = qwin.ShowDialog();
                    if (qres == true)
                    {
                        SetConsumer(qwin.ResultName, qwin.ResultCPF, null);
                    }
                    break;
                case "F8":
                    var data = _catalog.Select(kv => (Code: kv.Key, Description: kv.Value.Description, Price: kv.Value.UnitPrice)).ToList();
                    var pt = new ProdutosTesteWindow(data);
                    pt.Owner = this;
                    var pok = pt.ShowDialog();
                    if (pok == true && pt.SelectedEntry != null)
                    {
                        AddOrMergeCartItem(pt.SelectedEntry.Code, pt.SelectedEntry.Description, (double)pt.SelectedQty, pt.SelectedEntry.Price);
                    }
                    break;
                case "CTRL+L":
                case "Ctrl+L":
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Sangria", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    var dlg = new SangriaWindow();
                    dlg.Owner = this;
                    dlg.ShowDialog();
                    break;
                case "CTRL+S":
                case "Ctrl+S":
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Suprimento", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    var sup = new SuprimentoWindow();
                    sup.Owner = this;
                    sup.ShowDialog();
                    break;
                case "CTRL+R":
                case "Ctrl+R":
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Pagamento de Contas", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    var rw = new RecebimentoWindow();
                    rw.Owner = this;
                    rw.ShowDialog();
                    break;
                case "CTRL+O":
                case "Ctrl+O":
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Trocar Operador", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    TrySwitchOperator();
                    break;
                case "CTRL+F":
                case "Ctrl+F":
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Fechamento de Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    var fc = new FechamentoCaixaWindow();
                    fc.Owner = this;
                    fc.ShowDialog();
                    // Atualiza aviso ao retornar do fechamento
                    try { RefreshCashSessionStatus(); } catch { }
                    break;
                case "alterar_preco":
                    _modoAlteracaoPrecoF9 = true;
                    try { InputCodeField.IsEnabled = true; } catch { }
                    try { InputCodeField.Focus(); } catch { }
                    try { TxtStatusBar.Text = "Modo alteração de preço ativo"; } catch { }
                    // Removido pop-up informativo para fluxo direto
                    break;
                case "F9":
                    if (!_f9Enabled)
                    {
                        MessageBox.Show("Atalho F9 desabilitado nas opções.", "Alterar preço", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    }
                    // Iniciar modo alteração de preço F9
            // RECADO: NÃO MEXER - fluxo F9 validado com o cliente
            _modoAlteracaoPrecoF9 = true;
                    try 
                    { 
                        InputCodeField.IsEnabled = true;
                        InputQtyField.IsEnabled = true;
                        InputCodeField.Focus(); 
                        InputCodeField.SelectAll();
                        TxtStatusBar.Text = "Modo alteração de preço ativo - Digite quantidade e código do produto";
                    } 
                    catch { }
                    break;
                case "SHIFT+D":
                    OpenItemDiscountWindow();
                    break;
                case "NFCe":
                    if ((_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0)
                    {
                        MessageBox.Show("Indisponível com venda aberta ou itens no carrinho.", "Consulta NFC-e", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }
                    var nfceWin = new NFCeMonitorWindow();
                    nfceWin.Owner = this;
                    nfceWin.ShowDialog();
                    break;
            }
        }

        private void OpenCancelItemWindow()
        {
            App.Log("Abrindo CancelItemWindow");
            if (_cartItems.Count == 0)
            {
                MessageBox.Show("Não há itens para cancelar.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var options = BuildCancelOptionsFromCart();
            App.Log($"Cancel options count={options.Count()}");
            if (!options.Any())
            {
                MessageBox.Show("Nenhuma quantidade disponível para cancelar.", "Cancelar Item", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var win = new CancelItemWindow(options);
            win.Owner = this;
            var ok = win.ShowDialog();
            var results = win.Result ?? new List<(string Code, string Description, double Qty, decimal UnitPrice)>();
            App.Log($"CancelItemWindow result ok={(ok == true)} itens={results.Count}");
            if (ok == true && results.Count > 0)
            {
                foreach (var it in results)
                {
                    CancelCartItem(it.Code, it.Description, it.Qty, it.UnitPrice);
                    App.Log($"Cancel aplicado Code={it.Code} Qty={it.Qty}");
                }
                UpdateTotalsAndRefresh();
            }
        }

        private IEnumerable<CancelItemWindow.CancelOption> BuildCancelOptionsFromCart()
        {
            var groups = _cartItems
                .GroupBy(i => new { i.Codigo, i.Descricao })
                .Select(g => new {
                    Code = g.Key.Codigo,
                    Desc = g.Key.Descricao,
                    UnitPrice = g.Where(x => !x.IsCancellation).Select(x => x.VlUnit).LastOrDefault(),
                    Available = g.Where(x => !x.IsCancellation).Sum(x => x.Qt) - g.Where(x => x.IsCancellation).Sum(x => -x.Qt)
                })
                .Where(x => x.Available > 0.00001)
                .ToList();
        
            foreach (var g in groups)
            {
                yield return new CancelItemWindow.CancelOption
                {
                    Code = g.Code,
                    Description = g.Desc,
                    AvailableQty = g.Available,
                    UnitPrice = g.UnitPrice,
                    Cancel = false,
                    QtyToCancel = 0d
                };
            }
        }

        private void CancelCartItem(string code, string description, double qty, decimal unitPrice)
        {
            var cancelQty = Math.Abs(qty);
            var item = new CartItem
            {
                Item = _cartItems.Count + 1,
                Codigo = code,
                Descricao = description,
                Qt = -cancelQty,
                VlUnit = unitPrice,
                Total = unitPrice * (decimal)(-cancelQty),
                IsCancellation = true
            };
            _cartItems.Add(item);

            // Ajusta proporcionalmente o desconto do item original se houver
            var original = _cartItems.LastOrDefault(i => i.Codigo == code && !i.IsCancellation && i.Qt > 0);
            if (original != null && original.DiscountApplied > 0 && original.Qt > 0)
            {
                var perUnitDiscount = original.DiscountApplied / (decimal)original.Qt;
                var reduce = perUnitDiscount * (decimal)cancelQty;
                if (reduce < 0) reduce = -reduce;
                original.DiscountApplied = original.DiscountApplied - reduce;
                if (original.DiscountApplied < 0) original.DiscountApplied = 0;
                original.Total = original.VlUnit * (decimal)original.Qt - original.DiscountApplied;
            }
        }

        private void UpdateTotalsAndRefresh()
        {
            // Calcula subtotal (sem cancelamentos) e descontos por item
            var subtotal = _cartItems.Where(i => !i.IsCancellation)
                                     .Sum(i => i.VlUnit * (decimal)i.Qt);
            var discounts = _cartItems.Where(i => !i.IsCancellation)
                                      .Sum(i => i.DiscountApplied);
            var cancellations = _cartItems.Where(i => i.IsCancellation)
                                        .Sum(i => i.Total);
        
            currentSaleTotal = subtotal - discounts + cancellations;
        
            try { LblSubtotal.Text = $"R$ {subtotal:N2}"; } catch { }
            try { LblDescontoTotal.Text = $"R$ {discounts:N2}"; } catch { }
            try { LblFinalTotal.Text = $"R$ {currentSaleTotal:N2}"; } catch { }
            try { DgvProdutos.Items.Refresh(); } catch { }
            UpdateWindowCloseState();
            UpdateShortcutUiEnabledStates();
        }

        // Atualiza habilitação dos botões/atalhos visuais conforme estado atual
        private void UpdateShortcutUiEnabledStates()
        {
            bool restrict = (_requireF2ToStartSale && _saleStarted) || _cartItems.Count > 0;

            // Topo: Configurações e Logout devem ficar invisíveis durante restrição
            try { BtnSettingsHeader.Visibility = restrict ? Visibility.Collapsed : Visibility.Visible; BtnSettingsHeader.IsEnabled = !restrict; } catch { }
            try { BtnLogout.Visibility = restrict ? Visibility.Collapsed : Visibility.Visible; BtnLogout.IsEnabled = !restrict; } catch { }

            // Rodapé: manter visíveis, apenas desabilitar quando em restrição
            try { BtnSangria.IsEnabled = !restrict; BtnSangria.Visibility = Visibility.Visible; } catch { }
            try { BtnSuprimento.IsEnabled = !restrict; BtnSuprimento.Visibility = Visibility.Visible; } catch { }
            try { BtnReceberTitulo.IsEnabled = !restrict; BtnReceberTitulo.Visibility = Visibility.Visible; } catch { }
            try { BtnFecharCaixa.IsEnabled = !restrict; BtnFecharCaixa.Visibility = Visibility.Visible; } catch { }
            try { BtnNFCe.IsEnabled = !restrict; BtnNFCe.Visibility = Visibility.Visible; } catch { }
        }

        private bool HasActiveCashSession()
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                // Garantir schema mínimo para consulta
                using (var cmdInit = conn.CreateCommand())
                {
                    cmdInit.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CashSessions (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            CashRegisterNumber INTEGER,
                            OpenedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            OpeningAmount REAL NOT NULL DEFAULT 0,
                            ClosedAt TEXT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_CashSessions_ClosedAt ON CashSessions(ClosedAt);
                        CREATE INDEX IF NOT EXISTS idx_CashSessions_Open ON CashSessions(CashRegisterNumber, OpenedAt);
                    ";
                    cmdInit.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COUNT(1) FROM CashSessions WHERE ClosedAt IS NULL";
                    var obj = cmd.ExecuteScalar();
                    int count = Convert.ToInt32(obj ?? 0);
                    return count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void RefreshCashSessionStatus()
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                // Garantir schema
                using (var cmdInit = conn.CreateCommand())
                {
                    cmdInit.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CashSessions (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            CashRegisterNumber INTEGER,
                            OpenedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            OpeningAmount REAL NOT NULL DEFAULT 0,
                            ClosedAt TEXT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_CashSessions_ClosedAt ON CashSessions(ClosedAt);
                        CREATE INDEX IF NOT EXISTS idx_CashSessions_Open ON CashSessions(CashRegisterNumber, OpenedAt);
                    ";
                    cmdInit.ExecuteNonQuery();
                }
                int cashNumber = 0;
                bool isOpen = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT CashRegisterNumber FROM CashSessions WHERE ClosedAt IS NULL ORDER BY OpenedAt DESC LIMIT 1";
                    var v = cmd.ExecuteScalar();
                    if (v != null && v != DBNull.Value)
                    {
                        isOpen = true;
                        int.TryParse(v.ToString(), out cashNumber);
                    }
                }
                // Atualiza badge visual e barra de status
                try
                {
                    if (isOpen)
                    {
                        if (LblCaixaStatus != null) LblCaixaStatus.Text = "Caixa Aberto";
                        if (BadgeCaixaStatus != null)
                        {
                            BadgeCaixaStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x28, 0xA7, 0x45));
                            BadgeCaixaStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x7E, 0x34));
                            BadgeCaixaStatus.Visibility = Visibility.Visible;
                        }
                        if (TxtStatusBar != null) TxtStatusBar.Text = "Caixa Aberto";
                    }
                    else
                    {
                        if (LblCaixaStatus != null) LblCaixaStatus.Text = "Caixa Fechado";
                        if (BadgeCaixaStatus != null)
                        {
                            BadgeCaixaStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6C, 0x75, 0x7D));
                            BadgeCaixaStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x54, 0x5B, 0x62));
                            BadgeCaixaStatus.Visibility = Visibility.Visible;
                        }
                        if (TxtStatusBar != null) TxtStatusBar.Text = "";
                    }
                }
                catch { /* ignore UI errors */ }
            }
            catch { /* ignore db errors */ }
        }

        private void InputCodeField_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_requireF2ToStartSale && !_saleStarted && !_modoAlteracaoPrecoF9)
                {
                    MessageBox.Show("Pressione F2 para iniciar a venda.", "Iniciar venda", MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Handled = true; // evita focar o campo via clique
                }
                // Quando F2 está desativado, não perguntar consumidor no clique; apenas no Enter
            }
            catch { }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            App.Log($"KeyDown: {e.Key} Modifiers={Keyboard.Modifiers}");
            if (e.Key == Key.F1)
            {
                HandleShortcutMode("F1");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F3)
            {
                if (_cartItems.Count > 0)
                {
                    HandleShortcutMode("F3");
                }
                else
                {
                    MessageBox.Show("Não é possível abrir o pagamento sem produtos no carrinho.", "Carrinho vazio", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F4)
            {
                HandleShortcutMode("F4");
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                HandleShortcutMode("F2");
                e.Handled = true;
            }
            else if (e.Key == Key.F6)
            {
                HandleShortcutMode("F6");
                e.Handled = true;
            }
            else if (e.Key == Key.F7)
            {
                HandleShortcutMode("F7");
                e.Handled = true;
            }
            else if (e.Key == Key.F9)
            {
                HandleShortcutMode("F9");
                e.Handled = true;
            }
            else if (e.Key == Key.F12)
            {
                HandleShortcutMode("F12");
                e.Handled = true;
            }
            else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                HandleShortcutMode("SHIFT+D");
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                HandleShortcutMode("CTRL+S");
                e.Handled = true;
            }
            else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                HandleShortcutMode("CTRL+R");
                e.Handled = true;
            }
        }

        private void MinimizeWindow(object sender, RoutedEventArgs e)
        {
            try { this.WindowState = WindowState.Minimized; } catch { }
        }

        private void MaximizeRestoreWindow(object sender, RoutedEventArgs e)
        {
            try { this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; } catch { }
        }

        private void InputCodeField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                // Ao confirmar código com Enter, solicitar consumidor uma única vez por venda (facultativo)
                // Somente quando F2 está desativado
                if (!_requireF2ToStartSale && _promptConsumerOnFirstItem && !HasConsumer && !_consumerPromptedAlready)
                {
                    EnsureConsumerSelected();
                    _consumerPromptedAlready = true;
                }
                HandleCodeInput();
            }
            else if (e.Key == Key.Tab)
            {
                // Tratar TAB como confirmação do código para leitores que não enviam Enter/Return
                e.Handled = true; // evita mudança de foco padrão do Tab
                if (!_requireF2ToStartSale && _promptConsumerOnFirstItem && !HasConsumer && !_consumerPromptedAlready)
                {
                    EnsureConsumerSelected();
                    _consumerPromptedAlready = true;
                }
                HandleCodeInput();
                try { InputCodeField.Focus(); } catch { }
            }
            else if (e.Key == Key.Multiply)
            {
                // Tratar '*' como atalho de quantidade quando o conteúdo atual é numérico
                var raw = InputCodeField.Text?.Trim() ?? string.Empty;
                var numeric = Regex.IsMatch(raw, @"^\d+[\.,]?\d*$");
                if (numeric)
                {
                    var qtyStr = raw.Replace(',', '.');
                    if (double.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) && qty > 0)
                    {
                        _isParsingCodeInput = true;
                        try
                        {
                            InputQtyField.Text = qty.ToString(CultureInfo.InvariantCulture);
                            InputCodeField.Text = string.Empty; // evita TextChanged limpar QTD
                        }
                        finally
                        {
                            _isParsingCodeInput = false;
                        }
                        try { InputCodeField.Focus(); } catch { }
                        e.Handled = true;
                        return;
                    }
                }

                // Caso não seja apenas quantidade, tratar '*' como Enter para confirmar o código
                e.Handled = true;
                HandleCodeInput();
            }
            else if (e.Key == Key.Escape)
            {
                InputCodeField.Text = string.Empty;
                InputQtyField.Text = string.Empty;
                e.Handled = true;
            }
        }

        private void InputCodeField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isParsingCodeInput) return;
            
            // Bloquear processamento se F2 for obrigatório e a venda não foi iniciada
            if (_requireF2ToStartSale && !_saleStarted && !_modoAlteracaoPrecoF9)
            {
                return;
            }
            
            var text = InputCodeField.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                InputQtyField.Text = string.Empty;
                return;
            }

            // Caso 0: valor* (sem código) => mover para QTD e limpar campo código
            var onlyQty = Regex.Match(text, @"^(?<qty>\d+[\.,]?\d*)\s*[xX\*]\s*$");
            if (onlyQty.Success)
            {
                var qtyOnly = onlyQty.Groups["qty"].Value.Replace(',', '.');
                if (double.TryParse(qtyOnly, NumberStyles.Any, CultureInfo.InvariantCulture, out var q))
                {
                    _isParsingCodeInput = true;
                    try
                    {
                        InputQtyField.Text = q.ToString(CultureInfo.InvariantCulture);
                        // Limpa imediatamente para o fluxo quantidade* + código, sem disparar limpeza de QTD
                        InputCodeField.Text = string.Empty;
                    }
                    finally
                    {
                        _isParsingCodeInput = false;
                    }
                    try { InputCodeField.Focus(); } catch { }
                }
                return;
            }

            // Padrão original mantido: quantidade*codigo
            var m = Regex.Match(text, @"^(?<qty>\d+[\.,]?\d*)\s*[xX\*]\s*(?<code>\S+)$");
            if (m.Success)
            {
                var qtyStr = m.Groups["qty"].Value.Replace(',', '.');
                if (double.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
                {
                    InputQtyField.Text = qty.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                InputQtyField.Text = string.Empty;
            }
        }

        private void RedirectQtyClickToCode(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Não focar no campo se F2 for obrigatório e a venda não foi iniciada
                if (!(_requireF2ToStartSale && !_saleStarted && !_modoAlteracaoPrecoF9))
                {
                    InputCodeField.Focus();
                }
                e.Handled = true;
            }
            catch { }
        }

        private void HandleCodeInput()
        {
            if (_requireF2ToStartSale && !_saleStarted && !_modoAlteracaoPrecoF9)
            {
                // Bloqueia processamento sem exibir alerta automático; alerta só no clique do input
                return;
            }

            var text = InputCodeField.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            _isParsingCodeInput = true;
            try
            {
                double qty = 1.0;
                string code = text;

                // 1) quantidade*codigo  ou  2) codigo*quantidade
                var mQtyFirst = Regex.Match(text, @"^(?<qty>\d+[\.,]?\d*)\s*[xX\*]\s*(?<code>\S+)$");
                var mCodeFirst = Regex.Match(text, @"^(?<code>\S+)\s*[xX\*]\s*(?<qty>\d+[\.,]?\d*)$");
                bool qtyFromText = false;
                if (mQtyFirst.Success)
                {
                    var qtyStr = mQtyFirst.Groups["qty"].Value.Replace(',', '.');
                    double.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out qty);
                    code = mQtyFirst.Groups["code"].Value;
                    qtyFromText = true;
                }
                else if (mCodeFirst.Success)
                {
                    var qtyStr = mCodeFirst.Groups["qty"].Value.Replace(',', '.');
                    double.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out qty);
                    code = mCodeFirst.Groups["code"].Value;
                    qtyFromText = true;
                }

                // Se não houver quantidade explícita no texto, usar a do campo QTD (quando válida)
                if (!qtyFromText)
                {
                    var qtyFieldStr = (InputQtyField.Text ?? string.Empty).Trim().Replace(',', '.');
                    if (double.TryParse(qtyFieldStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var qField) && qField > 0)
                    {
                        qty = qField;
                    }
                }

                // Se estiver no modo de alteração de preço F9, validar produto no catálogo e abrir modal
                if (_modoAlteracaoPrecoF9)
                {
                    // Validar se o produto existe no catálogo
                    if (!_catalog.TryGetValue(code, out var prod))
                    {
                        MessageBox.Show($"Código de produto não encontrado no catálogo: {code}", "Produto não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                        InputCodeField.SelectAll();
                        return;
                    }

                    // Abrir modal para alteração de preço
                    var win = new AlterarPrecoWindow(prod.Description, code, prod.UnitPrice);
                    win.Owner = this;
                    var ok = win.ShowDialog();
                    if (ok == true)
                    {
                        // Adicionar produto ao grid com o novo preço
                        AddOrMergeCartItem(code, prod.Description, qty, win.NovoPreco);
                        
                        // Encerrar modo F9
                        _modoAlteracaoPrecoF9 = false;
                        try { TxtStatusBar.Text = string.Empty; } catch { }
                        InputCodeField.Text = string.Empty;
                        InputQtyField.Text = string.Empty;
                        try { InputCodeField.Focus(); } catch { }
                        return;
                    }
                    else
                    {
                        // Cancelado - manter foco no campo
                        InputCodeField.SelectAll();
                        return;
                    }
                }
                else
                {
                    if (_catalog.TryGetValue(code, out var prod))
                    {
                        AddOrMergeCartItem(code, prod.Description, qty, prod.UnitPrice);
                        InputCodeField.Text = string.Empty;
                        InputQtyField.Text = string.Empty;
                    }
                    else
                    {
                        MessageBox.Show($"Código de produto não encontrado: {code}", "Produto não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            finally
            {
                _isParsingCodeInput = false;
            }
        }

        private void AddOrMergeCartItem(string code, string description, double qty, decimal unitPrice)
        {
            if (qty <= 0) qty = 1;

            // Detecta se já havia algum item de venda (não cancelamento) antes desta inclusão
            bool hadSaleItemBefore = _cartItems.Any(i => !i.IsCancellation);

            // Não perguntar consumidor aqui quando F2 está desativado; fluxo ficou exclusivo no Enter

            var existing = _cartItems.FirstOrDefault(i => string.Equals(i.Codigo, code, StringComparison.OrdinalIgnoreCase) && !i.IsCancellation);
            if (existing != null)
            {
                existing.Qt += qty;
                existing.Total = existing.VlUnit * (decimal)existing.Qt;
            }
            else
            {
                var item = new CartItem
                {
                    Item = _cartItems.Count + 1,
                    Codigo = code,
                    Descricao = description,
                    Qt = qty,
                    VlUnit = unitPrice,
                    Total = unitPrice * (decimal)qty,
                    IsCancellation = false
                };
                _cartItems.Add(item);
                try { App.Log($"Item incluído code={code} firstSaleBefore={(hadSaleItemBefore ? 1 : 0)} promptFlag={( _promptConsumerOnFirstItem ? 1 : 0)} requireF2={( _requireF2ToStartSale ? 1 : 0)} hasConsumer={(HasConsumer ? 1 : 0)}"); } catch { }
            }

            UpdateTotalsAndRefresh();
        }

        private void UpdateConsumerLabels()
        {
            // Nesta versão, não há rótulos nomeados para consumidor; manter método para compatibilidade.
        }

        private void FinalizeSale()
        {
            MessageBox.Show("Venda finalizada.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            _cartItems.Clear();
            currentSaleTotal = 0m;
            try { DgvProdutos.Items.Refresh(); } catch { }
            try { LblFinalTotal.Text = $"R$ {currentSaleTotal:N2}"; } catch { }
            _consumerName = null;
            _consumerCPF = null;
            _consumerCustomerId = null;
            _consumerPromptedAlready = false;
            UpdateWindowCloseState();

            if (_requireF2ToStartSale)
            {
                _saleStarted = false;
                try { InputCodeField.IsEnabled = false; } catch { }
            }
            UpdateShortcutUiEnabledStates();
        }

        private void CancelSale()
        {
            if (_cartItems.Count == 0) return;
            var confirm = MessageBox.Show("Deseja realmente cancelar a venda?", "Cancelar venda", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                _cartItems.Clear();
                currentSaleTotal = 0m;
                try { DgvProdutos.Items.Refresh(); } catch { }
                try { LblFinalTotal.Text = $"R$ {currentSaleTotal:N2}"; } catch { }
                UpdateWindowCloseState();
                if (_requireF2ToStartSale)
                {
                    _saleStarted = false;
                    try { InputCodeField.IsEnabled = false; } catch { }
                }
                _consumerPromptedAlready = false;
                UpdateShortcutUiEnabledStates();
            }
        }

        private void InitProductCatalog()
        {
            _catalog.Clear();
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Code, Description, Price FROM Products";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var code = reader.GetString(0);
                    var desc = reader.GetString(1);
                    var price = reader.GetDecimal(2);
                    _catalog[code] = (desc, price);
                }
            }
            catch
            {
                // Se falhar, adiciona alguns itens padrão
                _catalog["1001"] = ("Arroz 5kg", 25.00m);
                _catalog["1002"] = ("Feijão 1kg", 6.50m);
                _catalog["1003"] = ("Açúcar 1kg", 4.20m);
                _catalog["1004"] = ("Café 500g", 12.90m);
            }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void InitializeDatabase()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT
                    );
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        Active INTEGER NOT NULL DEFAULT 1,
                        ExternalId TEXT NULL,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT NULL
                    );
                    CREATE TABLE IF NOT EXISTS Products (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Code TEXT UNIQUE,
                        Ean TEXT,
                        Description TEXT,
                        Price REAL
                    );

                    -- NFC-e: Emitente
                    CREATE TABLE IF NOT EXISTS Emitente (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RazaoSocial TEXT NOT NULL,
                        NomeFantasia TEXT,
                        CNPJ TEXT NOT NULL,
                        IE TEXT,
                        IM TEXT,
                        CRT INTEGER NOT NULL,
                        EnderecoLogradouro TEXT,
                        EnderecoNumero TEXT,
                        EnderecoBairro TEXT,
                        EnderecoMunicipio TEXT,
                        EnderecoUF TEXT,
                        EnderecoCEP TEXT,
                        Telefone TEXT
                    );

                    -- NFC-e: Certificado
                    CREATE TABLE IF NOT EXISTS Certificado (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Tipo TEXT NOT NULL, -- 'A1' ou 'A3'
                        CaminhoPFX TEXT,
                        SenhaPFX TEXT,
                        SerieA3 TEXT
                    );

                    -- NFC-e: Configurações e sequência
                    CREATE TABLE IF NOT EXISTS ConfiguracoesNFCe (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TpAmb INTEGER NOT NULL DEFAULT 2, -- 1=Produção, 2=Homologação
                        CSCId TEXT,
                        CSC TEXT,
                        cUF INTEGER,
                        Serie INTEGER NOT NULL DEFAULT 1,
                        ProximoNumero INTEGER NOT NULL DEFAULT 1,
                        UltimaAutorizacao TEXT,
                        ContingenciaAtiva INTEGER NOT NULL DEFAULT 0,
                        MotivoContingencia TEXT
                    );

                    -- NFC-e: Cabeçalho
                    CREATE TABLE IF NOT EXISTS NFCe (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Numero INTEGER NOT NULL,
                        Serie INTEGER NOT NULL,
                        Chave TEXT,
                        DataEmissao TEXT,
                        Status TEXT,
                        Protocolo TEXT,
                        XmlPath TEXT,
                        TotalProdutos REAL,
                        Total REAL,
                        ConsumidorCPF TEXT,
                        ConsumidorNome TEXT
                    );

                    -- NFC-e: Itens
                    CREATE TABLE IF NOT EXISTS NFCeItem (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NFCeId INTEGER NOT NULL,
                        Codigo TEXT,
                        Ean TEXT,
                        Descricao TEXT,
                        NCM TEXT,
                        CFOP TEXT,
                        Unidade TEXT,
                        Qt REAL,
                        VlUnit REAL,
                        VlTotal REAL,
                        Orig INTEGER,
                        CST TEXT,
                        CSOSN TEXT,
                        pICMS REAL,
                        vICMS REAL,
                        vBC REAL,
                        pPIS REAL,
                        vPIS REAL,
                        pCOFINS REAL,
                        vCOFINS REAL,
                        FOREIGN KEY (NFCeId) REFERENCES NFCe(Id)
                    );

                    -- NFC-e: Pagamentos
                    CREATE TABLE IF NOT EXISTS NFCePagamento (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NFCeId INTEGER NOT NULL,
                        tPag TEXT NOT NULL,
                        vPag REAL NOT NULL,
                        CNPJ_INTERMED TEXT,
                        TID TEXT,
                        NSU TEXT,
                        Bandeira TEXT,
                        FOREIGN KEY (NFCeId) REFERENCES NFCe(Id)
                    );

                    -- NFC-e: Status/retornos SEFAZ
                    CREATE TABLE IF NOT EXISTS NFCeStatus (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NFCeId INTEGER NOT NULL,
                        Codigo TEXT,
                        Mensagem TEXT,
                        Detalhe TEXT,
                        Data TEXT,
                        FOREIGN KEY (NFCeId) REFERENCES NFCe(Id)
                    );

                    -- Clientes
                    CREATE TABLE IF NOT EXISTS Customers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Type TEXT,
                        Name TEXT NOT NULL,
                        FantasyName TEXT,
                        CPF_CNPJ TEXT,
                        IE TEXT,
                        IsentoIE INTEGER NOT NULL DEFAULT 0,
                        BirthDate TEXT,
                        Phone TEXT,
                        Email TEXT,
                        Street TEXT,
                        Number TEXT,
                        Complement TEXT,
                        District TEXT,
                        City TEXT,
                        State TEXT,
                        ZipCode TEXT,
                        LimitCredit REAL NOT NULL DEFAULT 0
                    );

                    -- Endereços de clientes (múltiplos endereços por cliente)
                    CREATE TABLE IF NOT EXISTS CustomerAddresses (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        CustomerId INTEGER NOT NULL,
                        AddressType TEXT,
                        Street TEXT,
                        Number TEXT,
                        Complement TEXT,
                        District TEXT,
                        City TEXT,
                        State TEXT,
                        ZipCode TEXT,
                        FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
                    );

                    -- Municipios (códigos IBGE para busca de cidades)
                    CREATE TABLE IF NOT EXISTS Municipios (
                        Codigo TEXT PRIMARY KEY,
                        Nome TEXT NOT NULL,
                        UF TEXT NOT NULL
                    );

                    -- Cheques (cadastro e busca centralizados)
                    CREATE TABLE IF NOT EXISTS Cheques (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Valor REAL NOT NULL,
                        BomPara TEXT,
                        Emitente TEXT,
                        BancoCodigo TEXT,
                        Agencia TEXT,
                        Conta TEXT,
                        NumeroCheque TEXT,
                        CpfCnpjEmitente TEXT,
                        DataCadastro TEXT DEFAULT CURRENT_TIMESTAMP,
                        Utilizado INTEGER NOT NULL DEFAULT 0,
                        CidadeCodigo TEXT
                    );

                    -- Movimentações de caixa (abertura, suprimento, sangria)
                    CREATE TABLE IF NOT EXISTS CashMovements (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Type TEXT,
                        Amount REAL,
                        Reason TEXT,
                        Operator TEXT,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        PaymentMethodCode TEXT,
                        ReferenceType TEXT,
                        ReferenceId INTEGER,
                        DocumentNumber TEXT,
                        Notes TEXT,
                        BalanceAfter REAL
                    );

                    -- Fechamento de caixa (totais consolidados por período)
                    CREATE TABLE IF NOT EXISTS CashClosures (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        PeriodDate TEXT,
                        SalesTotal REAL,
                        SalesCount INTEGER,
                        NFCeTotal REAL,
                        NFCeCount INTEGER,
                        SuprimentoTotal REAL,
                        SangriaTotal REAL
                    );";

                cmd.ExecuteNonQuery();

                // Índices para consultas de Sangria e fluxo de caixa
                using (var cmdIdx = conn.CreateCommand())
                {
                    cmdIdx.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_CashMovements_Type_CreatedAt ON CashMovements(Type, CreatedAt);
                        CREATE INDEX IF NOT EXISTS idx_CashMovements_Method_CreatedAt ON CashMovements(PaymentMethodCode, CreatedAt);
                        CREATE INDEX IF NOT EXISTS idx_CashMovements_Ref ON CashMovements(ReferenceType, ReferenceId);
                        CREATE INDEX IF NOT EXISTS idx_CashClosures_PeriodDate ON CashClosures(PeriodDate);
                    ";
                    cmdIdx.ExecuteNonQuery();
                }

                // Garantir tabela de Vendas (Sales)
                using (var cmdSales = conn.CreateCommand())
                {
                    cmdSales.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Sales (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            CustomerName TEXT,
                            CustomerCPF TEXT,
                            TotalOriginal REAL NOT NULL,
                            DiscountAmount REAL NOT NULL,
                            DiscountIsPercent INTEGER NOT NULL,
                            DiscountPercent REAL NOT NULL,
                            DiscountReason TEXT,
                            TotalFinal REAL NOT NULL
                        );";
                    cmdSales.ExecuteNonQuery();
                }

                // Garantir tabela de Formas de Pagamento (PaymentMethods)
                using var cmdPM = conn.CreateCommand();
                cmdPM.CommandText = @"CREATE TABLE IF NOT EXISTS PaymentMethods (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL UNIQUE,
                    Name TEXT NOT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    DisplayOrder INTEGER NOT NULL DEFAULT 0,
                    LinkType TEXT
                );";
                cmdPM.ExecuteNonQuery();

                // Migração: adicionar coluna LinkType se não existir
                using var cmdInfoPM = conn.CreateCommand();
                cmdInfoPM.CommandText = "PRAGMA table_info(PaymentMethods)";
                bool hasLinkType = false;
                using (var rd = cmdInfoPM.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var colName = rd.GetString(1);
                        if (string.Equals(colName, "LinkType", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLinkType = true;
                            break;
                        }
                    }
                }
                if (!hasLinkType)
                {
                    using var cmdAlterPM = conn.CreateCommand();
                    cmdAlterPM.CommandText = "ALTER TABLE PaymentMethods ADD COLUMN LinkType TEXT";
                    cmdAlterPM.ExecuteNonQuery();
                }

                // Seed inicial se tabela estiver vazia
                using var cmdCountPM = conn.CreateCommand();
                cmdCountPM.CommandText = "SELECT COUNT(*) FROM PaymentMethods";
                var pmCount = Convert.ToInt32(cmdCountPM.ExecuteScalar());
                if (pmCount == 0)
                {
                    using var txPM = conn.BeginTransaction();
                    var defaults = new (string Code, string Name, int Enabled, int Order, string? Link)[]
                    {
                        ("DINHEIRO", "Dinheiro", 1, 1, "VENDA"),
                        ("CARTAO_CREDITO", "Cartão de Crédito", 1, 2, "FINANCEIRO"),
                        ("CARTAO_DEBITO", "Cartão de Débito", 1, 3, "FINANCEIRO"),
                        ("PIX", "PIX", 1, 4, "FINANCEIRO"),
                        ("CHEQUE", "Cheque", 0, 5, "FINANCEIRO")
                    };
                    foreach (var d in defaults)
                    {
                        using var insPM = conn.CreateCommand();
                        insPM.CommandText = "INSERT INTO PaymentMethods (Code, Name, IsEnabled, DisplayOrder, LinkType) VALUES ($code,$name,$enabled,$order,$link);";
                        insPM.Parameters.AddWithValue("$code", d.Code);
                        insPM.Parameters.AddWithValue("$name", d.Name);
                        insPM.Parameters.AddWithValue("$enabled", d.Enabled);
                        insPM.Parameters.AddWithValue("$order", d.Order);
                        insPM.Parameters.AddWithValue("$link", string.IsNullOrWhiteSpace(d.Link) ? (object)DBNull.Value : d.Link!);
                        insPM.ExecuteNonQuery();
                    }
                    txPM.Commit();
                }

                // Migração: adicionar coluna DataCadastro em Cheques, se não existir
                using (var cmdInfoChq = conn.CreateCommand())
                {
                    cmdInfoChq.CommandText = "PRAGMA table_info(Cheques)";
                    bool hasDataCadastro = false;
                    using (var rd = cmdInfoChq.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            var colName = rd.GetString(1);
                            if (string.Equals(colName, "DataCadastro", StringComparison.OrdinalIgnoreCase))
                            {
                                hasDataCadastro = true;
                                break;
                            }
                        }
                    }
                    if (!hasDataCadastro)
                    {
                        using (var cmdAlterChq = conn.CreateCommand())
                        {
                            cmdAlterChq.CommandText = "ALTER TABLE Cheques ADD COLUMN DataCadastro TEXT DEFAULT CURRENT_TIMESTAMP";
                            cmdAlterChq.ExecuteNonQuery();
                        }
                    }
                }

                // Migração: adicionar colunas de contraparte em CashMovements (Recebimento/Pagamento)
                using (var cmdInfoCM = conn.CreateCommand())
                {
                    cmdInfoCM.CommandText = "PRAGMA table_info(CashMovements)";
                    var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var rd = cmdInfoCM.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            cols.Add(rd.GetString(1));
                        }
                    }
                    if (!cols.Contains("CounterpartyName"))
                    {
                        using (var cmdAlter = conn.CreateCommand())
                        {
                            cmdAlter.CommandText = "ALTER TABLE CashMovements ADD COLUMN CounterpartyName TEXT";
                            cmdAlter.ExecuteNonQuery();
                        }
                    }
                    if (!cols.Contains("CounterpartyDoc"))
                    {
                        using (var cmdAlter = conn.CreateCommand())
                        {
                            cmdAlter.CommandText = "ALTER TABLE CashMovements ADD COLUMN CounterpartyDoc TEXT";
                            cmdAlter.ExecuteNonQuery();
                        }
                    }
                }

                // Índice para consultas por contraparte e período
                using (var cmdIdx2 = conn.CreateCommand())
                {
                    cmdIdx2.CommandText = "CREATE INDEX IF NOT EXISTS idx_CashMovements_Counterparty_CreatedAt ON CashMovements (CounterpartyName, CreatedAt)";
                    cmdIdx2.ExecuteNonQuery();
                }

                // Contas a Receber: cabeçalho, parcelas (Carnê) e recebimentos
                using (var cmdAR = conn.CreateCommand())
                {
                    cmdAR.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Receivables (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            CustomerId INTEGER,
                            CustomerName TEXT,
                            CustomerDoc TEXT,
                            Type TEXT, -- 'CARNE' ou 'BOLETO'
                            DocumentNumber TEXT,
                            IssueDate TEXT,
                            DueDate TEXT,
                            OriginalAmount REAL,
                            PaidAmount REAL DEFAULT 0,
                            Status TEXT, -- 'ABERTO','PARCIAL','QUITADO','CANCELADO'
                            Notes TEXT
                        );

                        CREATE TABLE IF NOT EXISTS ReceivableInstallments (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            ReceivableId INTEGER NOT NULL,
                            ParcelNumber INTEGER NOT NULL,
                            DueDate TEXT NOT NULL,
                            Amount REAL NOT NULL,
                            PaidAmount REAL NOT NULL DEFAULT 0,
                            Status TEXT NOT NULL DEFAULT 'ABERTO',
                            Notes TEXT,
                            FOREIGN KEY (ReceivableId) REFERENCES Receivables(Id)
                        );

                        CREATE TABLE IF NOT EXISTS ReceivableReceipts (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            ReceivableId INTEGER,
                            InstallmentId INTEGER,
                            Date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            Amount REAL NOT NULL,
                            PaymentMethodCode TEXT NOT NULL,
                            Operator TEXT,
                            CounterpartyName TEXT,
                            CounterpartyDoc TEXT,
                            CashMovementId INTEGER,
                            Notes TEXT,
                            FOREIGN KEY (ReceivableId) REFERENCES Receivables(Id),
                            FOREIGN KEY (InstallmentId) REFERENCES ReceivableInstallments(Id),
                            FOREIGN KEY (CashMovementId) REFERENCES CashMovements(Id)
                        );
                    ";
                    cmdAR.ExecuteNonQuery();
                }

                // Índices para consultas eficientes de Contas a Receber
                using (var cmdARIdx = conn.CreateCommand())
                {
                    cmdARIdx.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_Receivables_Customer_DueDate ON Receivables(CustomerDoc, DueDate);
                        CREATE INDEX IF NOT EXISTS idx_ReceivableInstallments_ReceivableId_DueDate ON ReceivableInstallments(ReceivableId, DueDate);
                        CREATE INDEX IF NOT EXISTS idx_ReceivableReceipts_ReceivableId_Date ON ReceivableReceipts(ReceivableId, Date);
                        CREATE INDEX IF NOT EXISTS idx_ReceivableReceipts_Method_Date ON ReceivableReceipts(PaymentMethodCode, Date);
                    ";
                    cmdARIdx.ExecuteNonQuery();
                }

                // Empresa e Contador
                using (var cmdEmp = conn.CreateCommand())
                {
                    cmdEmp.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Empresa (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            RazaoSocial TEXT,
                            NomeFantasia TEXT,
                            CNPJ TEXT,
                            IE TEXT,
                            IM TEXT,
                            RegimeTributario TEXT,
                            CNAE TEXT,
                            Email TEXT,
                            Telefone TEXT,
                            Website TEXT,
                            CEP TEXT,
                            Logradouro TEXT,
                            Numero TEXT,
                            Complemento TEXT,
                            Bairro TEXT,
                            MunicipioCodigo TEXT,
                            MunicipioNome TEXT,
                            UF TEXT
                        );

                        CREATE TABLE IF NOT EXISTS Contador (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Nome TEXT,
                            CRC TEXT,
                            CNPJ TEXT,
                            CPF TEXT,
                            Email TEXT,
                            Telefone TEXT,
                            Celular TEXT,
                            CEP TEXT,
                            Logradouro TEXT,
                            Numero TEXT,
                            Complemento TEXT,
                            Bairro TEXT,
                            MunicipioCodigo TEXT,
                            MunicipioNome TEXT,
                            UF TEXT
                        );
                    ";
                    cmdEmp.ExecuteNonQuery();
                }

                // Migração leve: garantir colunas CRCEstado e CRCTipo em Contador
                using (var chkCols = conn.CreateCommand())
                {
                    chkCols.CommandText = "PRAGMA table_info('Contador');";
                    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var rd = chkCols.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            var name = rd.GetString(1);
                            existing.Add(name);
                        }
                    }
                    if (!existing.Contains("CRCEstado"))
                    {
                        using var add1 = conn.CreateCommand();
                        add1.CommandText = "ALTER TABLE Contador ADD COLUMN CRCEstado TEXT;";
                        add1.ExecuteNonQuery();
                    }
                    if (!existing.Contains("CRCTipo"))
                    {
                        using var add2 = conn.CreateCommand();
                        add2.CommandText = "ALTER TABLE Contador ADD COLUMN CRCTipo TEXT;";
                        add2.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao inicializar o banco de dados: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadF9OptionFlag()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'EnableF9PriceChange' LIMIT 1";
                var obj = cmd.ExecuteScalar();
                string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                _f9Enabled = string.IsNullOrWhiteSpace(val) ? true : (val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase));
            }
            catch { _f9Enabled = true; }
        }

        private void LoadPromptConsumerFlag()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'PromptConsumerOnFirstItem' LIMIT 1";
                var obj = cmd.ExecuteScalar();
                var s = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                // Default: verdadeiro quando chave ausente/nula (alinha com SettingsWindow)
                if (string.IsNullOrWhiteSpace(s))
                {
                    _promptConsumerOnFirstItem = true;
                }
                else
                {
                    _promptConsumerOnFirstItem = (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { _promptConsumerOnFirstItem = true; }
        }

        private void LoadSupervisorSettings()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Key, Value FROM Settings WHERE Key IN ('RequireSupervisorForSangria','RequireSupervisorForOpeningCash','RequireSupervisorForClosingCash','SupervisorCode','SupervisorPassword','SupervisorName')";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    var val = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    switch (key)
                    {
                        case "RequireSupervisorForSangria":
                            _requireSupervisorForSangria = string.Equals(val, "1", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "RequireSupervisorForOpeningCash":
                            _requireSupervisorForOpeningCash = string.Equals(val, "1", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "RequireSupervisorForClosingCash":
                            _requireSupervisorForClosingCash = string.Equals(val, "1", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "SupervisorCode":
                            _supervisorCode = val;
                            break;
                        case "SupervisorPassword":
                            _supervisorPassword = val;
                            break;
                        case "SupervisorName":
                            _supervisorName = val;
                            break;
                    }
                }
            }
            catch { }
        }

        private void LoadRequireF2StartSaleFlag()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'RequireF2ToStartSale'";
                var obj = cmd.ExecuteScalar();
                string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                _requireF2ToStartSale = !string.IsNullOrWhiteSpace(val) &&
                    (string.Equals(val, "1", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(val, "true", StringComparison.OrdinalIgnoreCase));
            }
            catch { _requireF2ToStartSale = false; }
        }

        private void UpdateRequireF2UiState(bool showPrompt = false)
        {
            try
            {
                if (_requireF2ToStartSale)
                {
                    if (_cartItems.Count == 0)
                    {
                        _saleStarted = false;
                        try { InputCodeField.IsEnabled = false; } catch { }
                        // Exibir dica fixa na barra de status quando F2 é exigido e não há itens
                        try { TxtStatusBar.Text = "PARA INICIAR VENDA TECLE F2"; } catch { }
                    }
                    else
                    {
                        _saleStarted = true;
                        try { InputCodeField.IsEnabled = true; } catch { }
                        try { TxtStatusBar.Text = string.Empty; } catch { }
                    }
                }
                else
                {
                    _saleStarted = true;
                    try { InputCodeField.IsEnabled = true; } catch { }
                    try { TxtStatusBar.Text = string.Empty; } catch { }
                }
                // Garante que aparência dos atalhos acompanhe o estado (_saleStarted/itens)
                UpdateShortcutUiEnabledStates();
            }
            catch { /* ignore */ }
        }

        private void InitializeTEF()
        {
            try
            {
                // Inicializa o TEF Manager
                var tefManager = TEFManager.Instance;
                bool success = tefManager.InitializeTEF();
                
                if (success)
                {
                    var tefType = tefManager.GetCurrentTEFType();
                    if (tefType != TEFIntegrationType.None)
                    {
                        // TEF inicializado com sucesso - mensagem já exibida pelo TEFManager
                    }
                }
                else
                {
                    MessageBox.Show("Falha ao inicializar TEF. O sistema funcionará sem integração TEF.", 
                        "Aviso TEF", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar TEF: {ex.Message}\nO sistema funcionará sem integração TEF.", 
                    "Erro TEF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureProductTableSeededFromCatalog()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(1) FROM Products";
                    var countObj = cmd.ExecuteScalar();
                    var count = 0;
                    if (countObj is long l) count = (int)l;
                    else if (countObj is int i) count = i;

                    if (count == 0)
                    {
                        var seed = new List<(string Code, string Desc, decimal Price)>
                        {
                            ("1001","Arroz 5kg", 25.00m),
                            ("1002","Feijão 1kg", 6.50m),
                            ("1003","Açúcar 1kg", 4.20m),
                            ("1004","Café 500g", 12.90m),
                            ("2001","Leite 1L", 5.80m),
                            ("2002","Pão de Forma", 8.90m)
                        };
                        foreach (var (code, desc, price) in seed)
                        {
                            using var ins = conn.CreateCommand();
                            ins.CommandText = "INSERT OR IGNORE INTO Products (Code, Description, Price) VALUES ($c,$d,$p)";
                            ins.Parameters.AddWithValue("$c", code);
                            ins.Parameters.AddWithValue("$d", desc);
                            ins.Parameters.AddWithValue("$p", price);
                            ins.ExecuteNonQuery();
                        }
                    }
                }

                // Após garantir o seed, recarrega catálogo da base
                InitProductCatalog();
            }
            catch { }
        }

    private void OpenPriceAlterationWindow()
    {
        if (_cartItems.Count == 0)
        {
            MessageBox.Show("Não há itens para alterar preço.", "Alterar preço", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = DgvProdutos.SelectedItem as CartItem;
        if (selected == null)
        {
            selected = _cartItems.LastOrDefault(i => !i.IsCancellation);
        }
        if (selected == null)
        {
            MessageBox.Show("Nenhum item válido para alterar preço.", "Alterar preço", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new AlterarPrecoWindow(selected.Descricao, selected.Codigo, selected.VlUnit);
        win.Owner = this;
        var ok = win.ShowDialog();
        if (ok == true)
        {
            selected.VlUnit = win.NovoPreco;
            selected.Total = selected.VlUnit * (decimal)selected.Qt - selected.DiscountApplied;
            try { DgvProdutos.Items.Refresh(); } catch { }
            UpdateTotalsAndRefresh();
        }
    }

    private void OpenItemDiscountWindow()
    {
        if (_cartItems.Count == 0)
        {
            MessageBox.Show("Não há itens para aplicar desconto.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var items = _cartItems.Where(i => !i.IsCancellation).ToList();
        if (items.Count == 0)
        {
            MessageBox.Show("Nenhum item válido para desconto.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var win = new ItemDiscountWindow(items, LoadMaxDiscountPercentOrDefault());
        win.Owner = this;
        var ok = win.ShowDialog();
        if (ok == true && win.SelectedItem != null)
        {
            var item = _cartItems.FirstOrDefault(i => ReferenceEquals(i, win.SelectedItem));
            if (item == null)
            {
                item = _cartItems.FirstOrDefault(i => i.Codigo == win.SelectedItem.Codigo && !i.IsCancellation);
            }
            if (item != null)
            {
                item.DiscountIsPercent = win.IsPercent;
                item.DiscountPercent = win.PercentValue;
                item.DiscountReason = win.Reason;
                item.DiscountApplied = win.AppliedAmount;
                item.Total = item.VlUnit * (decimal)item.Qt - item.DiscountApplied;
                UpdateTotalsAndRefresh();
            }
        }
    }

    private decimal LoadMaxDiscountPercentOrDefault()
    {
        try
    {
        using var conn = new SqliteConnection(GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'MaxDiscountPercent'";
        var val = cmd.ExecuteScalar() as string;
        if (decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
        {
            if (p < 0) p = 0;
            if (p > 100) p = 100;
            return p;
        }
    }
    catch { }

    // Fallback: tentar no banco pdv.db para compatibilidade
    try
    {
            using var conn2 = new SqliteConnection(DbHelper.GetConnectionString());
        conn2.Open();
        using var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT Value FROM Settings WHERE Key = 'MaxDiscountPercent'";
        var val2 = cmd2.ExecuteScalar() as string;
        if (decimal.TryParse(val2, NumberStyles.Any, CultureInfo.InvariantCulture, out var p2))
        {
            if (p2 < 0) p2 = 0;
            if (p2 > 100) p2 = 100;
            return p2;
        }
    }
    catch { }

    return 10m; // padrão
    }

    public List<CartItem> GetCartItemsSnapshot()
    {
        return _cartItems.Select(ci => new CartItem
        {
            Item = ci.Item,
            Codigo = ci.Codigo,
            Descricao = ci.Descricao,
            Qt = ci.Qt,
            VlUnit = ci.VlUnit,
            Total = ci.Total,
            IsCancellation = ci.IsCancellation,
            DiscountApplied = ci.DiscountApplied,
            DiscountIsPercent = ci.DiscountIsPercent,
            DiscountPercent = ci.DiscountPercent,
            DiscountReason = ci.DiscountReason
        }).ToList();
    }

        private void OpenERPImport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$k LIMIT 1;";
                cmd.Parameters.AddWithValue("$k", "ImportERPOrdersEnabled");
                var v = cmd.ExecuteScalar();
                bool enabled = false;
                if (v != null && v != DBNull.Value)
                {
                    var s = Convert.ToString(v);
                    enabled = s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (!enabled)
                {
                    MessageBox.Show("Habilite 'Importar pedido de venda do ERP' nas Configurações (aba Opções).", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var win = new ERPImportWindow();
                win.Owner = this;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir importação ERP: {ex.Message}", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Confirmar logout
                var result = MessageBox.Show("Deseja realmente fazer logout do sistema?", 
                                           "Confirmar Logout", 
                                           MessageBoxButton.YesNo, 
                                           MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Perguntar fechamento do caixa antes do logout
                    try
                    {
                        if (HasActiveCashSession())
                        {
                            var ask = MessageBox.Show("Deseja realizar o fechamento do caixa agora?", "Fechar Caixa", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (ask == MessageBoxResult.Yes)
                            {
                                string? supervisor = null;
                                var askSup = MessageBox.Show("Deseja incluir um supervisor na validação?", "Supervisor", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (askSup == MessageBoxResult.Yes)
                                {
                                    var auth = new LoginWindow(authorizationMode: true) { Owner = this };
                                    var ok = auth.ShowDialog();
                                    if (ok == true && (string.Equals(auth.LoggedRole, "admin", StringComparison.OrdinalIgnoreCase) || string.Equals(auth.LoggedRole, "fiscal", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        supervisor = auth.LoggedUser;
                                    }
                                    else
                                    {
                                        MessageBox.Show("Acesso de supervisor negado ou cancelado.", "Supervisor", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    }
                                }
                                var fc = new FechamentoCaixaWindow();
                                fc.Owner = this;
                                fc.ClosedByUser = SessionManager.CurrentUser;
                                fc.SupervisorUser = supervisor;
                                fc.ShowDialog();
                                // Atualiza aviso ao retornar do fechamento
                                try { RefreshCashSessionStatus(); } catch { }
                            }
                        }
                    }
                    catch { /* ignore */ }
                    // Encerrar sessão
                    SessionManager.EndSession();
                    
                    // Fechar janela atual
                    this.Close();
                    
                    // Abrir janela de login
                    var loginWindow = new LoginWindow();
                    loginWindow.Show();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao fazer logout: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadCustomLogo()
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                
                // Verificar se deve exibir o logo
                using var cmdShow = conn.CreateCommand();
                cmdShow.CommandText = "SELECT Value FROM Settings WHERE Key = 'ShowLogo' LIMIT 1;";
                var showLogoValue = cmdShow.ExecuteScalar();
                var showLogoStr = showLogoValue?.ToString();
                bool showLogo = string.Equals(showLogoStr, "1", StringComparison.Ordinal) ||
                                string.Equals(showLogoStr, "true", StringComparison.OrdinalIgnoreCase);
                
                if (!showLogo)
                {
                    CustomLogoImage.Visibility = Visibility.Collapsed;
                    ExampleLogoPlaceholder.Visibility = Visibility.Collapsed;
                    ProductImageDisplay.Visibility = Visibility.Visible;
                    return;
                }
                
                // Obter caminho da imagem
                using var cmdPath = conn.CreateCommand();
                cmdPath.CommandText = "SELECT Value FROM Settings WHERE Key = 'LogoImagePath' LIMIT 1;";
                var logoPath = cmdPath.ExecuteScalar()?.ToString();
                
                if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
                {
                    CustomLogoImage.Visibility = Visibility.Collapsed;
                    ProductImageDisplay.Visibility = Visibility.Collapsed;
                    ExampleLogoPlaceholder.Visibility = Visibility.Visible;
                    return;
                }
                
                // Carregar e exibir a imagem
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                CustomLogoImage.Source = bitmap;
                CustomLogoImage.Visibility = Visibility.Visible;
                ExampleLogoPlaceholder.Visibility = Visibility.Collapsed;
                ProductImageDisplay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                // Em caso de erro, ocultar o logo personalizado
                CustomLogoImage.Visibility = Visibility.Collapsed;
                ExampleLogoPlaceholder.Visibility = Visibility.Collapsed;
                ProductImageDisplay.Visibility = Visibility.Visible;
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar logo personalizado: {ex.Message}");
            }
        }

        public void RefreshCustomLogo()
        {
            LoadCustomLogo();
        }
}
}