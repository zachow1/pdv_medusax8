using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Models;
using System.Windows.Input;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public class AppliedItem
    {
        public string Name { get; set; } = "";
        public decimal Value { get; set; }
        public string ValueFormatted => Value.ToString("C", CultureInfo.CurrentCulture);
        public PDV_MedusaX8.Models.ChequeInfo? Cheque { get; set; }
        public string PaymentCode { get; set; } = "";
    }

    public partial class PaymentWindow : Window
    {
        private decimal SaleTotal;
        private decimal AppliedTotal;
        private PaymentMethod? SelectedPayment;
        private readonly List<AppliedItem> appliedItems = new();
        private ChequeInfo? pendingChequeFromSearch = null;

        // Desconto geral da venda
        private decimal OriginalSaleTotal;
        private decimal SaleDiscountAmount = 0m;
        private bool SaleDiscountIsPercent = false;
        private decimal SaleDiscountPercent = 0m;
        private string SaleDiscountReason = string.Empty;

        public PaymentWindow(decimal saleTotal)
        {
            InitializeComponent();
            this.Loaded += PaymentWindow_Loaded;
            SaleTotal = saleTotal;
            OriginalSaleTotal = saleTotal;
            AppliedTotal = 0m;
            LoadEnabledPaymentMethods();
            UpdateTotalsLabels();
        }

        public PaymentWindow() : this(0m) { }

        public List<AppliedItem> GetAppliedPaymentsSnapshot()
        {
            return appliedItems.Select(ai => new AppliedItem
            {
                Name = ai.Name,
                Value = ai.Value,
                Cheque = ai.Cheque,
                PaymentCode = ai.PaymentCode
            }).ToList();
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void LoadEnabledPaymentMethods()
        {
            var list = new List<PaymentMethod>();
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, Code, Name, IsEnabled, DisplayOrder FROM PaymentMethods WHERE IsEnabled=1 ORDER BY DisplayOrder, Name;";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new PaymentMethod
                                {
                                    Id = reader.GetInt32(0),
                                    Code = reader.GetString(1),
                                    Name = reader.GetString(2),
                                    IsEnabled = reader.GetInt32(3) == 1,
                                    DisplayOrder = reader.GetInt32(4)
                                });
                            }
                        }
                    }
                }
                PaymentItems.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar formas de pagamento: {ex.Message}", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectPayment(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PaymentMethod pm)
            {
                SelectedPayment = pm;
                TxtSelectedMethod.Text = pm.Name;

                // Exigir cliente para métodos específicos
                if (RequiresCustomer(pm.Code))
                {
                    if (!EnsureConsumerSelectedForPayment())
                    {
                        MessageBox.Show("Esta forma de pagamento exige selecionar um cliente.", "Cliente obrigatório", MessageBoxButton.OK, MessageBoxImage.Information);
                        PaymentItems.SelectedItem = null;
                        SelectedPayment = null;
                        TxtSelectedMethod.Text = "(nenhum)";
                        GbParcelamento.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                GbParcelamento.Visibility =
                    (pm.Code == "CARTAO_CREDITO" ||
                     pm.Name.Contains("Crédito", StringComparison.OrdinalIgnoreCase) ||
                     pm.Name.Contains("Prazo", StringComparison.OrdinalIgnoreCase))
                    ? Visibility.Visible : Visibility.Collapsed;

                if (SaleTotal > AppliedTotal)
                {
                    TxtValueToApply.Text = (SaleTotal - AppliedTotal).ToString("N2");
                }
            }
        }

        // Novo: seleção via ListBox
        private void PaymentItems_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PaymentItems.SelectedItem is PaymentMethod pm)
            {
                SelectedPayment = pm;
                TxtSelectedMethod.Text = pm.Name;

                // Exigir cliente para métodos específicos
                if (RequiresCustomer(pm.Code))
                {
                    if (!EnsureConsumerSelectedForPayment())
                    {
                        MessageBox.Show("Esta forma de pagamento exige selecionar um cliente.", "Cliente obrigatório", MessageBoxButton.OK, MessageBoxImage.Information);
                        PaymentItems.SelectedItem = null;
                        SelectedPayment = null;
                        TxtSelectedMethod.Text = "(nenhum)";
                        GbParcelamento.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                GbParcelamento.Visibility =
                    (pm.Code == "CARTAO_CREDITO" ||
                     pm.Name.Contains("Crédito", StringComparison.OrdinalIgnoreCase) ||
                     pm.Name.Contains("Prazo", StringComparison.OrdinalIgnoreCase))
                    ? Visibility.Visible : Visibility.Collapsed;

                var remaining = SaleTotal - AppliedTotal;
                if (remaining > 0)
                {
                    TxtValueToApply.Text = remaining.ToString("N2");
                }

                // Cheque não oferece busca aqui; cadastro sempre será aberto no momento de aplicar.
            }
        }

        private bool RequiresCustomer(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            // Códigos que obrigam cliente: 02 (Cheque), 15 (Boleto), 16 (Depósito), 21 (Crédito loja), 05 (Crédito loja – seed atual)
            return code == "02" || code == "15" || code == "16" || code == "21" || code == "05";
        }

        private bool EnsureConsumerSelectedForPayment()
        {
            if (this.Owner is MainWindow mw)
            {
                // Exige cliente REAL (registro) quando necessário
                if (mw.HasCustomerSelected) return true;
                var cw = new ConsumidorWindow();
                cw.Owner = this.Owner;
                var ok = cw.ShowDialog();
                if (ok == true)
                {
                    mw.SetConsumer(cw.ConsumerName, cw.ConsumerCPF, cw.SelectedCustomerId);
                    return mw.HasCustomerSelected;
                }
                return false;
            }
            else
            {
                // Sem MainWindow como Owner: tenta abrir seleção de cliente, mas só valida com ID
                var cw = new ConsumidorWindow();
                cw.Owner = this;
                var ok = cw.ShowDialog();
                if (ok == true && this.Owner is MainWindow mw2)
                {
                    mw2.SetConsumer(cw.ConsumerName, cw.ConsumerCPF, cw.SelectedCustomerId);
                    return mw2.HasCustomerSelected;
                }
                return false;
            }
        }

        private void ApplyValue(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedPayment == null)
                {
                    MessageBox.Show("Selecione uma forma de pagamento.", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!decimal.TryParse(TxtValueToApply.Text.Replace("R$",""), NumberStyles.Any, CultureInfo.CurrentCulture, out var value) || value <= 0)
                {
                    MessageBox.Show("Informe um valor válido para aplicar.", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Reforço: cliente obrigatório para métodos específicos
                if (RequiresCustomer(SelectedPayment.Code))
                {
                    if (this.Owner is MainWindow mwc && !mwc.HasCustomerSelected)
                    {
                        MessageBox.Show("Esta forma de pagamento exige selecionar um cliente.", "Cliente obrigatório", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                var remaining = SaleTotal - AppliedTotal;
                if (value > remaining) value = remaining;

                var itemName = SelectedPayment.Name;
                if (GbParcelamento.Visibility == Visibility.Visible)
                {
                    if (int.TryParse(TxtNumParcela.Text, out var n) && n > 0 &&
                        decimal.TryParse(TxtValorParcela.Text.Replace("R$",""), NumberStyles.Any, CultureInfo.CurrentCulture, out var vParc))
                    {
                        itemName = $"{SelectedPayment.Name} ({n}x de {vParc.ToString("C", CultureInfo.CurrentCulture)})";
                    }
                }

                PDV_MedusaX8.Models.ChequeInfo? chequeInfo = null;
                if (SelectedPayment.Code == "02")
                {
                    // Sempre cadastrar novo cheque ao aplicar pagamento
                    var chk = new ChequeWindow();
                    chk.Owner = this;
                    var ok = chk.ShowDialog();
                    if (ok != true || chk.ResultCheque == null)
                    {
                        // Usuário cancelou ou inválido; não aplica
                        return;
                    }
                    chequeInfo = chk.ResultCheque;
                }

                // Processamento TEF para pagamentos com cartão (códigos 03=Débito, 04=Crédito)
                if (SelectedPayment.Code == "03" || SelectedPayment.Code == "04")
                {
                    try
                    {
                        var tefManager = TEFManager.Instance;
                        if (tefManager.IsInitialized())
                        {
                            var paymentType = SelectedPayment.Code == "03" ? "Débito" : "Crédito";
                            var result = tefManager.ProcessPayment(value, paymentType);
                            
                            if (!result.Success)
                            {
                                MessageBox.Show($"Falha no processamento TEF: {result.Message}", 
                                    "TEF", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            
                            // Atualizar nome do item com informações do TEF
                            if (!string.IsNullOrEmpty(result.TransactionId))
                            {
                                itemName = $"{SelectedPayment.Name} - NSU: {result.TransactionId}";
                            }
                        }
                        else
                        {
                            var fallback = MessageBox.Show(
                                "TEF não está inicializado. Deseja processar manualmente?", 
                                "TEF", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            
                            if (fallback != MessageBoxResult.Yes)
                            {
                                return;
                            }
                            
                            itemName = $"{SelectedPayment.Name} (Manual)";
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro no processamento TEF: {ex.Message}", 
                            "TEF", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                appliedItems.Add(new AppliedItem { Name = itemName, Value = value, Cheque = chequeInfo, PaymentCode = SelectedPayment!.Code });
                AppliedList.ItemsSource = null;
                AppliedList.ItemsSource = appliedItems;

                AppliedTotal += value;
                UpdateTotalsLabels();

                TxtValueToApply.Text = string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar valor: {ex.Message}", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearValue(object sender, RoutedEventArgs e)
        {
            TxtValueToApply.Text = string.Empty;
        }

        private void UpdateTotalsLabels()
        {
            TxtSaleTotal.Text = SaleTotal.ToString("C", CultureInfo.CurrentCulture);

            TxtTotalVenda.Text = SaleTotal.ToString("C", CultureInfo.CurrentCulture);
            TxtValorPago.Text = AppliedTotal.ToString("C", CultureInfo.CurrentCulture);
            try { TxtDescontoVenda.Text = SaleDiscountAmount.ToString("C", CultureInfo.CurrentCulture); } catch { }
            var falta = SaleTotal - AppliedTotal;
            if (falta > 0)
            {
                LblFaltaPagar.Visibility = Visibility.Visible;
                TxtFaltaPagar.Visibility = Visibility.Visible;
                LblVendaFechada.Visibility = Visibility.Collapsed;
                TxtVendaFechada.Visibility = Visibility.Collapsed;
                TxtFaltaPagar.Text = falta.ToString("C", CultureInfo.CurrentCulture);
            }
            else
            {
                LblFaltaPagar.Visibility = Visibility.Collapsed;
                TxtFaltaPagar.Visibility = Visibility.Collapsed;
                LblVendaFechada.Visibility = Visibility.Visible;
                TxtVendaFechada.Visibility = Visibility.Visible;
                TxtVendaFechada.Text = SaleTotal.ToString("C", CultureInfo.CurrentCulture);
            }
            BtnConfirm.IsEnabled = AppliedTotal >= SaleTotal;
        }

        private void OpenSettings(object sender, RoutedEventArgs e)
        {
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
                LoadEnabledPaymentMethods();
                return;
            }
            else
            {
                MessageBox.Show("Acesso negado. Necessário usuário administrador ou fiscal.", "Autorização", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseModal(object sender, RoutedEventArgs e)
        {
            this.DialogResult = AppliedTotal >= SaleTotal;
            this.Close();
        }

        private void CancelarPagamentos(object sender, RoutedEventArgs e)
        {
            var resp = MessageBox.Show(
                "Deseja cancelar e retornar? Os pagamentos não serão salvos.",
                "Cancelar Pagamentos",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
        
            if (resp == MessageBoxResult.Yes)
            {
                this.DialogResult = false;
                this.Close();
            }
        }

        private void ConfirmarPagamentos(object sender, RoutedEventArgs e)
        {
            if (AppliedTotal < SaleTotal)
            {
                var falta = (SaleTotal - AppliedTotal).ToString("C", CultureInfo.CurrentCulture);
                MessageBox.Show($"Ainda falta pagar: {falta}", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Persiste cheques recebidos no banco de dados
            try
            {
                PersistReceivedCheques();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar cheques recebidos: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Persistir venda com desconto
            try
            {
                PersistSale();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar venda: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Impressão de documentos conforme códigos de pagamento (15,16,21)
            try
            {
                if (this.Owner is MainWindow mw)
                {
                    var itensConf = appliedItems.Where(ai => ai.PaymentCode == "15" || ai.PaymentCode == "16" || ai.PaymentCode == "21").ToList();
                    if (itensConf.Any())
                    {
                        PrintingService.PrintConfissao(mw, OriginalSaleTotal, SaleTotal, SaleDiscountAmount, SaleDiscountIsPercent, SaleDiscountPercent, SaleDiscountReason, itensConf);
                    }

                    var itensCarne = appliedItems.Where(ai => ai.PaymentCode == "21").ToList();
                    if (itensCarne.Any())
                    {
                        var totalCredito = itensCarne.Sum(i => i.Value);
                        PrintingService.PrintCarne(mw, totalCredito, itensCarne);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao imprimir documentos: {ex.Message}", "Impressão", MessageBoxButton.OK, MessageBoxImage.Error);
                // Continua mesmo se a impressão falhar
            }

            // Fechar modal como sucesso
            this.DialogResult = true;
            this.Close();
        }

        private void RemoveAppliedItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is AppliedItem ai)
                {
                    appliedItems.Remove(ai);
                    AppliedTotal = appliedItems.Sum(x => x.Value);

                    AppliedList.ItemsSource = null;
                    AppliedList.ItemsSource = appliedItems;

                    UpdateTotalsLabels();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao remover pagamento: {ex.Message}", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BuscarCheque_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var search = new ChequeSearchWindow();
                search.Owner = this;
                var ok = search.ShowDialog();
                if (ok == true && search.SelectedCheque != null)
                {
                    pendingChequeFromSearch = search.SelectedCheque;
                    TxtValueToApply.Text = pendingChequeFromSearch!.Valor.ToString("N2");
                    MessageBox.Show("Cheque selecionado. Agora clique em 'Adicionar Pagamento' para aplicar.", "Cheque", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao buscar cheques: {ex.Message}", "Cheque", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PaymentWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            double targetWidth = workArea.Width * 0.8;
            double targetHeight = workArea.Height * 0.8;
        
            this.MaxWidth = workArea.Width;
            this.MaxHeight = workArea.Height;
            this.Width = targetWidth;
            this.Height = targetHeight;
        
            if (this.Owner != null)
            {
                double ownerLeft = this.Owner.Left;
                double ownerTop = this.Owner.Top;
                double ownerWidth = this.Owner.ActualWidth > 0 ? this.Owner.ActualWidth : this.Owner.Width;
                double ownerHeight = this.Owner.ActualHeight > 0 ? this.Owner.ActualHeight : this.Owner.Height;
        
                this.Left = ownerLeft + (ownerWidth - this.Width) / 2;
                this.Top = ownerTop + (ownerHeight - this.Height) / 2;
            }
            else
            {
                this.Left = workArea.Left + (workArea.Width - this.Width) / 2;
                this.Top = workArea.Top + (workArea.Height - this.Height) / 2;
            }
        
            if (this.Top < workArea.Top) this.Top = workArea.Top;
            if (this.Left < workArea.Left) this.Left = workArea.Left;
        }

        private void Header_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* ignore */ }
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenParcelamento_Click(object sender, RoutedEventArgs e)
        {
            var remaining = SaleTotal - AppliedTotal;
            if (remaining <= 0)
            {
                MessageBox.Show("Nada a parcelar. Valor restante é zero.", "Parcelamento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        
            var pw = new ParcelamentoWindow(remaining);
            pw.Owner = this;
            var ok = pw.ShowDialog();
            if (ok == true)
            {
                TxtNumParcela.Text = pw.NumeroParcelas.ToString();
                TxtValorParcela.Text = pw.ValorParcela.ToString("C", CultureInfo.CurrentCulture);
            }
        }

        private void OpenSaleDiscountWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var maxPercent = LoadMaxDiscountPercentOrDefault();
                var win = new SaleDiscountWindow(OriginalSaleTotal, maxPercent);
                win.Owner = this;
                var ok = win.ShowDialog();
                if (ok == true)
                {
                    SaleDiscountIsPercent = win.IsPercent;
                    SaleDiscountPercent = win.PercentValue;
                    SaleDiscountReason = win.Reason;
                    SaleDiscountAmount = win.AppliedAmount;
                    if (SaleDiscountAmount < 0) SaleDiscountAmount = 0;
                    if (SaleDiscountAmount > OriginalSaleTotal) SaleDiscountAmount = OriginalSaleTotal;
                    SaleTotal = OriginalSaleTotal - SaleDiscountAmount;
                    UpdateTotalsLabels();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar desconto: {ex.Message}", "Desconto", MessageBoxButton.OK, MessageBoxImage.Error);
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
            return 10m;
        }

        private void PersistReceivedCheques()
        {
            var chequesToSave = appliedItems.Where(item => item.Cheque != null).ToList();
            
            if (!chequesToSave.Any())
                return; // Nenhum cheque para salvar

            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                
                // Garantir tabela Cheques (usada pela busca e cadastro)
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = @"
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
                            Utilizado INTEGER DEFAULT 0,
                            CidadeCodigo TEXT
                        );";
                    cmdCheck.ExecuteNonQuery();
                }

                // Garantir coluna CidadeCodigo (migração)
                using (var cmdInfo = conn.CreateCommand())
                {
                    cmdInfo.CommandText = "PRAGMA table_info(Cheques)";
                    bool hasCidade = false;
                    using (var rd = cmdInfo.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            var colName = rd.GetString(1);
                            if (string.Equals(colName, "CidadeCodigo", StringComparison.OrdinalIgnoreCase))
                            {
                                hasCidade = true;
                                break;
                            }
                        }
                    }
                    if (!hasCidade)
                    {
                        using (var cmdAlter = conn.CreateCommand())
                        {
                            cmdAlter.CommandText = "ALTER TABLE Cheques ADD COLUMN CidadeCodigo TEXT";
                            cmdAlter.ExecuteNonQuery();
                        }
                    }
                }
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (var item in chequesToSave)
                        {
                            var cheque = item.Cheque!;
                            
                            // Primeiro, tentar marcar como utilizado se já existir (cheque selecionado pela busca)
                            int rowsAffected = 0;
                            if (!string.IsNullOrWhiteSpace(cheque.NumeroCheque) && !string.IsNullOrWhiteSpace(cheque.BancoCodigo))
                            {
                                using (var cmdUpdate = conn.CreateCommand())
                                {
                                    cmdUpdate.Transaction = transaction;
                                    cmdUpdate.CommandText = @"
                                        UPDATE Cheques 
                                        SET Utilizado = 1 
                                        WHERE NumeroCheque = $numero 
                                          AND BancoCodigo = $banco 
                                          AND Utilizado = 0";
                                    
                                    cmdUpdate.Parameters.AddWithValue("$numero", cheque.NumeroCheque);
                                    cmdUpdate.Parameters.AddWithValue("$banco", cheque.BancoCodigo);
                                    rowsAffected = cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            
                            // Se não existia, inserir como utilizado (cheque cadastrado agora)
                            if (rowsAffected == 0)
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.Transaction = transaction;
                                    cmd.CommandText = @"
                                        INSERT INTO Cheques 
                                        (Valor, BomPara, Emitente, BancoCodigo, Agencia, Conta, NumeroCheque, CpfCnpjEmitente, Utilizado, CidadeCodigo) 
                                        VALUES 
                                        ($valor, $bomPara, $emitente, $bancoCodigo, $agencia, $conta, $numeroCheque, $cpfCnpjEmitente, 1, $cidadeCodigo)";
                                    
                                    cmd.Parameters.AddWithValue("$valor", cheque.Valor);
                                    cmd.Parameters.AddWithValue("$bomPara", cheque.BomPara?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$emitente", cheque.Emitente ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$bancoCodigo", cheque.BancoCodigo ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$agencia", cheque.Agencia ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$conta", cheque.Conta ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$numeroCheque", cheque.NumeroCheque ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$cpfCnpjEmitente", cheque.CpfCnpjEmitente ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("$cidadeCodigo", string.IsNullOrWhiteSpace(cheque.CidadeCodigo) ? (object)DBNull.Value : cheque.CidadeCodigo);
                                    
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

        }

        private void PersistSale()
        {
            using var conn = new SqliteConnection(GetConnectionString());
            conn.Open();

            using (var cmdCreate = conn.CreateCommand())
            {
                cmdCreate.CommandText = @"
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
                        TotalFinal REAL NOT NULL,
                        Operator TEXT,
                        CashRegisterNumber INTEGER
                    );";
                cmdCreate.ExecuteNonQuery();
            }

            // Migração defensiva: garantir colunas Operator e CashRegisterNumber
            try
            {
                using var cmdInfo = conn.CreateCommand();
                cmdInfo.CommandText = "PRAGMA table_info('Sales')";
                using var rd = cmdInfo.ExecuteReader();
                var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (rd.Read()) cols.Add(rd.GetString(1));
                if (!cols.Contains("Operator"))
                {
                    using var cmdAlter = conn.CreateCommand();
                    cmdAlter.CommandText = "ALTER TABLE Sales ADD COLUMN Operator TEXT";
                    cmdAlter.ExecuteNonQuery();
                }
                if (!cols.Contains("CashRegisterNumber"))
                {
                    using var cmdAlter2 = conn.CreateCommand();
                    cmdAlter2.CommandText = "ALTER TABLE Sales ADD COLUMN CashRegisterNumber INTEGER";
                    cmdAlter2.ExecuteNonQuery();
                }
            }
            catch { }

            string? customerName = null;
            string? customerCPF = null;
            if (this.Owner is MainWindow mw)
            {
                customerName = mw.ConsumerName;
                customerCPF = mw.ConsumerCPF;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Sales (Date, CustomerName, CustomerCPF, TotalOriginal, DiscountAmount, DiscountIsPercent, DiscountPercent, DiscountReason, TotalFinal, Operator, CashRegisterNumber)
                    VALUES (CURRENT_TIMESTAMP, $name, $cpf, $orig, $discAmt, $discIsPct, $discPct, $reason, $final, $op, $cash);
                ";
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(customerName) ? (object)DBNull.Value : customerName);
                cmd.Parameters.AddWithValue("$cpf", string.IsNullOrWhiteSpace(customerCPF) ? (object)DBNull.Value : customerCPF);
                cmd.Parameters.AddWithValue("$orig", OriginalSaleTotal);
                cmd.Parameters.AddWithValue("$discAmt", SaleDiscountAmount);
                cmd.Parameters.AddWithValue("$discIsPct", SaleDiscountIsPercent ? 1 : 0);
                cmd.Parameters.AddWithValue("$discPct", SaleDiscountPercent);
                cmd.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(SaleDiscountReason) ? (object)DBNull.Value : SaleDiscountReason);
                cmd.Parameters.AddWithValue("$final", SaleTotal);
                cmd.Parameters.AddWithValue("$op", PDV_MedusaX8.Services.SessionManager.CurrentUser ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$cash", GetCashRegisterNumber(conn));
                cmd.ExecuteNonQuery();
            }
        }

        private static int GetCashRegisterNumber(SqliteConnection conn)
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

    }
}