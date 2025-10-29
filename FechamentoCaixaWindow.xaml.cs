using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class FechamentoCaixaWindow : Window
    {
        // Informações adicionais de fechamento
        public string? ClosedByUser { get; set; }
        public string? SupervisorUser { get; set; }

        public FechamentoCaixaWindow()
        {
            InitializeComponent();
            this.Loaded += FechamentoCaixaWindow_Loaded;
            this.PreviewTextInput += Monetary_PreviewTextInput;
            this.LostFocus += Monetary_LostFocus;
        }

        private string GetConnectionString() => DbHelper.GetConnectionString();

        private class PagamentoResumo
        {
            public string Meio { get; set; } = string.Empty;
            public decimal Total { get; set; }
            public string TotalFmt => Total.ToString("C", CultureInfo.CurrentCulture);
        }

        private void FechamentoCaixaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Carregar configuração padrão de conferência às cegas
                try
                {
                    using var connCfg = new SqliteConnection(GetConnectionString());
                    connCfg.Open();
                    using var cmdCfg = connCfg.CreateCommand();
                    cmdCfg.CommandText = "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);";
                    cmdCfg.ExecuteNonQuery();
                    using var cmdGet = connCfg.CreateCommand();
                    cmdGet.CommandText = "SELECT Value FROM Settings WHERE Key='BlindCashClosureEnabled' LIMIT 1";
                    var v = cmdGet.ExecuteScalar();
                    bool enabled = v != null && v != DBNull.Value && (string.Equals(Convert.ToString(v), "1") || string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase));
                    CbBlindMode.IsChecked = enabled;
                    ApplyBlindMode(enabled);
                }
                catch { }

                // Exibir painel de supervisor se exigido
                if (this.Owner is MainWindow mw)
                {
                    SupervisorPanel.Visibility = mw.RequiresSupervisorForClosingCash ? Visibility.Visible : Visibility.Collapsed;
                }

                TxtPeriodo.Text = $"{DateTime.Today:dd/MM/yyyy}";

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();

                // Sales: total e quantidade do dia
                decimal totalVendas = 0m; int qtdVendas = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COALESCE(SUM(TotalFinal),0), COUNT(1) FROM Sales WHERE DATE(Date) = DATE('now')";
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        totalVendas = r.IsDBNull(0) ? 0m : Convert.ToDecimal(r.GetDouble(0));
                        qtdVendas = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    }
                }
                TxtTotalVendas.Text = totalVendas.ToString("C", CultureInfo.CurrentCulture);
                TxtQtdVendas.Text = qtdVendas.ToString(CultureInfo.InvariantCulture);

                // NFC-e: total e quantidade do dia
                decimal totalNFCe = 0m; int qtdNFCe = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COALESCE(SUM(Total),0), COUNT(1) FROM NFCe WHERE DATE(DataEmissao) = DATE('now')";
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        // SQLite pode devolver como double
                        totalNFCe = r.IsDBNull(0) ? 0m : Convert.ToDecimal(r.GetDouble(0));
                        qtdNFCe = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    }
                }
                TxtTotalNFCe.Text = totalNFCe.ToString("C", CultureInfo.CurrentCulture);
                TxtQtdNFCe.Text = qtdNFCe.ToString(CultureInfo.InvariantCulture);

                // Movimentos: abertura, suprimento e sangria do dia
                decimal totalAbertura = 0m, totalSupr = 0m, totalSang = 0m;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM CashMovements WHERE Type='ABERTURA' AND DATE(CreatedAt)=DATE('now')";
                    var v = cmd.ExecuteScalar();
                    if (v != null && v != DBNull.Value) totalAbertura = Convert.ToDecimal(v);
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM CashMovements WHERE Type='SUPRIMENTO' AND DATE(CreatedAt)=DATE('now')";
                    var v = cmd.ExecuteScalar();
                    if (v != null && v != DBNull.Value) totalSupr = Convert.ToDecimal(v);
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM CashMovements WHERE Type='SANGRIA' AND DATE(CreatedAt)=DATE('now')";
                    var v = cmd.ExecuteScalar();
                    if (v != null && v != DBNull.Value) totalSang = Convert.ToDecimal(v);
                }
                TxtTotalAbertura.Text = totalAbertura.ToString("C", CultureInfo.CurrentCulture);
                TxtTotalSuprimento.Text = totalSupr.ToString("C", CultureInfo.CurrentCulture);
                TxtTotalSangria.Text = totalSang.ToString("C", CultureInfo.CurrentCulture);

                // NFC-e: pagamentos por tPag
                var lista = new List<PagamentoResumo>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT p.tPag, COALESCE(SUM(p.vPag),0) FROM NFCePagamento p JOIN NFCe n ON n.Id = p.NFCeId WHERE DATE(n.DataEmissao) = DATE('now') GROUP BY p.tPag";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var code = r.IsDBNull(0) ? string.Empty : r.GetString(0);
                        var total = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetDouble(1));
                        lista.Add(new PagamentoResumo { Meio = MapTPag(code), Total = total });
                    }
                }
                LstPagamentosNFCe.ItemsSource = lista.OrderBy(x => x.Meio).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar fechamento: {ex.Message}", "Fechamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyBlindMode(bool enabled)
        {
            SummaryGrid.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            BlindPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (enabled)
            {
                try
                {
                    // Limpar quaisquer valores visíveis do resumo para mitigar inspeção visual
                    TxtTotalVendas.Text = "";
                    TxtQtdVendas.Text = "";
                    TxtTotalNFCe.Text = "";
                    TxtQtdNFCe.Text = "";
                    LstPagamentosNFCe.ItemsSource = null;
                }
                catch { }
            }
        }

        private void CbBlindMode_Checked(object sender, RoutedEventArgs e)
        {
            ApplyBlindMode(true);
            // Persistir preferência
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmdUp = conn.CreateCommand();
                cmdUp.CommandText = "UPDATE Settings SET Value='1' WHERE Key='BlindCashClosureEnabled'";
                int rows = cmdUp.ExecuteNonQuery();
                if (rows == 0)
                {
                    using var cmdIns = conn.CreateCommand();
                    cmdIns.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('BlindCashClosureEnabled','1')";
                    cmdIns.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private void CbBlindMode_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyBlindMode(false);
            // Persistir preferência
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmdUp = conn.CreateCommand();
                cmdUp.CommandText = "UPDATE Settings SET Value='0' WHERE Key='BlindCashClosureEnabled'";
                int rows = cmdUp.ExecuteNonQuery();
                if (rows == 0)
                {
                    using var cmdIns = conn.CreateCommand();
                    cmdIns.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('BlindCashClosureEnabled','0')";
                    cmdIns.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private string MapTPag(string code)
        {
            switch (code)
            {
                case "01": return "Dinheiro (01)";
                case "02": return "Cheque (02)";
                case "03": return "Cartão Crédito (03)";
                case "04": return "Cartão Débito (04)";
                case "05": return "Crédito Loja (05)";
                case "10": return "PIX (10)";
                case "11": return "Boleto (11)";
                default: return string.IsNullOrWhiteSpace(code) ? "—" : $"Código {code}";
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Coleta dados da tela
                var periodo = TxtPeriodo.Text;
                var totalVendas = TxtTotalVendas.Text;
                var qtdVendas = TxtQtdVendas.Text;
                var totalNFCe = TxtTotalNFCe.Text;
                var qtdNFCe = TxtQtdNFCe.Text;
                var totalSupr = TxtTotalSuprimento.Text;
                var totalSang = TxtTotalSangria.Text;
                var totalAbertura = TxtTotalAbertura.Text;
                var pagamentos = LstPagamentosNFCe.Items.Cast<PagamentoResumo>().Select(p => (p.Meio, p.Total)).ToList();

                PrintingService.PrintFechamentoCaixa(periodo, totalVendas, qtdVendas, totalNFCe, qtdNFCe, totalAbertura, totalSupr, totalSang, pagamentos);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao imprimir fechamento: {ex.Message}", "Fechamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFechar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Restringir acesso: requer permissão de vendas (operadores e perfis superiores)
                if (!PDV_MedusaX8.Services.SessionManager.HasPermission("sales"))
                {
                    MessageBox.Show("Acesso restrito a operadores de caixa.", "Permissão", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Validação de supervisor, se exigido
                if (this.Owner is MainWindow mwReq && mwReq.RequiresSupervisorForClosingCash)
                {
                    // Se não houver credenciais configuradas nas opções, usar autenticação por usuário (AuthWindow)
                    if (string.IsNullOrWhiteSpace(mwReq.SupervisorCode) || string.IsNullOrWhiteSpace(mwReq.SupervisorPassword))
                    {
                        var auth = new LoginWindow(authorizationMode: true) { Owner = mwReq };
                        var ok = auth.ShowDialog();
                        if (ok == true && (string.Equals(auth.LoggedRole, "admin", StringComparison.OrdinalIgnoreCase) || string.Equals(auth.LoggedRole, "fiscal", StringComparison.OrdinalIgnoreCase)))
                        {
                            SupervisorUser = auth.LoggedUser ?? "Supervisor";
                        }
                        else
                        {
                            MessageBox.Show("Acesso de supervisor negado ou cancelado.", "Autorização", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else
                    {
                        // Mantém suporte ao fluxo antigo via campos locais, se estiver configurado
                        var code = (TxtSupervisorCode.Text ?? string.Empty).Trim();
                        var pass = (TxtSupervisorPassword.Password ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pass) ||
                            !string.Equals(code, mwReq.SupervisorCode, StringComparison.Ordinal) ||
                            !string.Equals(pass, mwReq.SupervisorPassword, StringComparison.Ordinal))
                        {
                            MessageBox.Show("Código ou senha do supervisor inválidos.", "Autorização", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        // Em caso de sucesso, garante SupervisorUser preenchido
                        if (string.IsNullOrWhiteSpace(SupervisorUser) && !string.IsNullOrWhiteSpace(mwReq.SupervisorName))
                        {
                            SupervisorUser = mwReq.SupervisorName;
                        }
                    }
                }

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using (var cmdCreate = conn.CreateCommand())
                {
                    cmdCreate.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CashClosures (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            PeriodDate TEXT,
                            SalesTotal REAL,
                            SalesCount INTEGER,
                            NFCeTotal REAL,
                            NFCeCount INTEGER,
                            SuprimentoTotal REAL,
                            SangriaTotal REAL,
                            CashRegisterNumber INTEGER,
                            SessionId INTEGER,
                            CountedCash REAL,
                            CountedCardDebit REAL,
                            CountedCardCredit REAL,
                            CountedCheques REAL,
                            CountedPix REAL,
                            BlindMode INTEGER,
                            Operator TEXT,
                            Observations TEXT,
                            Signature TEXT,
                            RequiresSupervisorReview INTEGER,
                            DivergenceAmount REAL
                        );";
                    cmdCreate.ExecuteNonQuery();
                }
                // Garantir Settings e ConfiguracoesNFCe existem
                using (var cmdInit = conn.CreateCommand())
                {
                    cmdInit.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Settings (
                            Key TEXT PRIMARY KEY,
                            Value TEXT
                        );
                        CREATE TABLE IF NOT EXISTS ConfiguracoesNFCe (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            TpAmb INTEGER NOT NULL DEFAULT 2,
                            CSCId TEXT,
                            CSC TEXT,
                            cUF INTEGER,
                            Serie INTEGER NOT NULL DEFAULT 1,
                            ProximoNumero INTEGER NOT NULL DEFAULT 1,
                            UltimaAutorizacao TEXT,
                            ContingenciaAtiva INTEGER NOT NULL DEFAULT 0,
                            MotivoContingencia TEXT
                        );
                    ";
                    cmdInit.ExecuteNonQuery();
                }
                // Garantir CashSessions existe
                using (var cmdSessInit = conn.CreateCommand())
                {
                    cmdSessInit.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CashSessions (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            CashRegisterNumber INTEGER,
                            OpenedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            OpeningAmount REAL NOT NULL DEFAULT 0,
                            ClosedAt TEXT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_CashSessions_ClosedAt ON CashSessions(ClosedAt);
                    ";
                    cmdSessInit.ExecuteNonQuery();
                }

                // Migrar colunas de CashClosures de forma defensiva
                using (var cmdMig = conn.CreateCommand())
                {
                    cmdMig.CommandText = @"CREATE TABLE IF NOT EXISTS CashClosures (
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
                    cmdMig.ExecuteNonQuery();
                }

                void AddClosureColIfMissing(string col, string type)
                {
                    try
                    {
                        using var c = conn.CreateCommand();
                        c.CommandText = "PRAGMA table_info('CashClosures')";
                        using var rinfo = c.ExecuteReader();
                        bool exists = false;
                        while (rinfo.Read())
                        {
                            var n = rinfo.GetString(1);
                            if (string.Equals(n, col, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                        }
                        if (!exists)
                        {
                            using var calt = conn.CreateCommand();
                            calt.CommandText = $"ALTER TABLE CashClosures ADD COLUMN {col} {type}";
                            calt.ExecuteNonQuery();
                        }
                    }
                    catch { /* ignore */ }
                }

                AddClosureColIfMissing("CashRegisterNumber", "INTEGER");
                AddClosureColIfMissing("SessionId", "INTEGER");
                AddClosureColIfMissing("ClosedByUser", "TEXT");
                AddClosureColIfMissing("SupervisorUser", "TEXT");
                AddClosureColIfMissing("ClosedAt", "TEXT");
                AddClosureColIfMissing("CountedCash", "REAL");
                AddClosureColIfMissing("CountedCardDebit", "REAL");
                AddClosureColIfMissing("CountedCardCredit", "REAL");
                AddClosureColIfMissing("CountedCheques", "REAL");
                AddClosureColIfMissing("CountedPix", "REAL");
                AddClosureColIfMissing("BlindMode", "INTEGER");
                AddClosureColIfMissing("Operator", "TEXT");
                AddClosureColIfMissing("Observations", "TEXT");
                AddClosureColIfMissing("Signature", "TEXT");
                AddClosureColIfMissing("RequiresSupervisorReview", "INTEGER");
                AddClosureColIfMissing("DivergenceAmount", "REAL");
                // Obter sessão ativa (se houver)
                int? sessionId = null; int cashNumber = 0;
                using (var cmdFind = conn.CreateCommand())
                {
                    cmdFind.CommandText = @"SELECT Id, COALESCE(CashRegisterNumber,0) FROM CashSessions WHERE ClosedAt IS NULL ORDER BY OpenedAt DESC LIMIT 1";
                    using var r = cmdFind.ExecuteReader();
                    if (r.Read())
                    {
                        sessionId = r.IsDBNull(0) ? (int?)null : r.GetInt32(0);
                        cashNumber = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    }
                }
                // Se não houver sessão ativa, tentar obter número do caixa das configurações
                if (cashNumber == 0)
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
                // Preparar dados de conferência às cegas
                bool blind = CbBlindMode.IsChecked == true;
                decimal countedCash = 0m, countedDebit = 0m, countedCredit = 0m, countedCheques = 0m, countedPix = 0m;
                string observations = string.Empty;
                if (blind)
                {
                    countedCash = TryParseDecimal(TxtBlindCash.Text);
                    countedDebit = TryParseDecimal(TxtBlindCardDebit.Text);
                    countedCredit = TryParseDecimal(TxtBlindCardCredit.Text);
                    countedCheques = TryParseDecimal(TxtBlindCheques.Text);
                    countedPix = TryParseDecimal(TxtBlindPix.Text);
                    observations = TxtBlindObs.Text ?? string.Empty;
                }

                // Calcular divergência (apenas caixa físico vs saldo de caixa do sistema)
                decimal divergence = 0m; int requiresReview = 0;
                if (blind)
                {
                    try
                    {
                        using var cmdBal = conn.CreateCommand();
                        cmdBal.CommandText = "SELECT Value FROM Settings WHERE Key='CashBalance' LIMIT 1";
                        var vbal = cmdBal.ExecuteScalar();
                        if (vbal != null && vbal != DBNull.Value && decimal.TryParse(Convert.ToString(vbal), NumberStyles.Any, CultureInfo.InvariantCulture, out var sysBal))
                        {
                            divergence = countedCash - sysBal;
                            requiresReview = divergence != 0m ? 1 : 0;
                        }
                    }
                    catch { }
                }

                // Assinatura digital (hash SHA-256 do conteúdo)
                string signature = string.Empty;
                if (blind)
                {
                    var receipt = $"FechamentoCego|Data={DateTime.Now:O}|Operador={SessionManager.CurrentUser}|Dinheiro={countedCash}|Debito={countedDebit}|Credito={countedCredit}|Cheques={countedCheques}|PIX={countedPix}|Obs={observations}";
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(receipt));
                    signature = BitConverter.ToString(hash).Replace("-", string.Empty);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO CashClosures (PeriodDate, SalesTotal, SalesCount, NFCeTotal, NFCeCount, SuprimentoTotal, SangriaTotal, CashRegisterNumber, SessionId, ClosedByUser, SupervisorUser, ClosedAt,
                                        CountedCash, CountedCardDebit, CountedCardCredit, CountedCheques, CountedPix, BlindMode, Operator, Observations, Signature, RequiresSupervisorReview, DivergenceAmount)
                                        VALUES ($period, $salesTot, $salesCnt, $nfTot, $nfCnt, $supTot, $sgTot, $cash, $sid, $closedBy, $supervisor, CURRENT_TIMESTAMP,
                                        $ccash, $cdeb, $ccred, $cchq, $cpix, $blind, $op, $obs, $sig, $rev, $div)";
                    cmd.Parameters.AddWithValue("$period", DateTime.Today.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$salesTot", ParseCurrency(TxtTotalVendas.Text));
                    cmd.Parameters.AddWithValue("$salesCnt", int.TryParse(TxtQtdVendas.Text, out var sc) ? sc : 0);
                    cmd.Parameters.AddWithValue("$nfTot", ParseCurrency(TxtTotalNFCe.Text));
                    cmd.Parameters.AddWithValue("$nfCnt", int.TryParse(TxtQtdNFCe.Text, out var nc) ? nc : 0);
                    cmd.Parameters.AddWithValue("$supTot", ParseCurrency(TxtTotalSuprimento.Text));
                    cmd.Parameters.AddWithValue("$sgTot", ParseCurrency(TxtTotalSangria.Text));
                    cmd.Parameters.AddWithValue("$cash", cashNumber);
                    cmd.Parameters.AddWithValue("$sid", (object?)sessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$closedBy", (object?)(ClosedByUser ?? SessionManager.CurrentUser ?? ""));
                    cmd.Parameters.AddWithValue("$supervisor", (object?)(SupervisorUser ?? (object)DBNull.Value) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$ccash", (double)countedCash);
                    cmd.Parameters.AddWithValue("$cdeb", (double)countedDebit);
                    cmd.Parameters.AddWithValue("$ccred", (double)countedCredit);
                    cmd.Parameters.AddWithValue("$cchq", (double)countedCheques);
                    cmd.Parameters.AddWithValue("$cpix", (double)countedPix);
                    cmd.Parameters.AddWithValue("$blind", blind ? 1 : 0);
                    cmd.Parameters.AddWithValue("$op", SessionManager.CurrentUser ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$obs", string.IsNullOrWhiteSpace(observations) ? (object)DBNull.Value : observations);
                    cmd.Parameters.AddWithValue("$sig", string.IsNullOrWhiteSpace(signature) ? (object)DBNull.Value : signature);
                    cmd.Parameters.AddWithValue("$rev", blind ? requiresReview : 0);
                    cmd.Parameters.AddWithValue("$div", (double)divergence);
                    cmd.ExecuteNonQuery();
                }
                // Encerrar sessão ativa, se existir
                if (sessionId.HasValue)
                {
                    using var cmdClose = conn.CreateCommand();
                    cmdClose.CommandText = "UPDATE CashSessions SET ClosedAt = CURRENT_TIMESTAMP WHERE Id = $id";
                    cmdClose.Parameters.AddWithValue("$id", sessionId.Value);
                    cmdClose.ExecuteNonQuery();
                }
                MessageBox.Show("Fechamento registrado.", "Fechamento", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao registrar fechamento: {ex.Message}", "Fechamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Monetary_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Permite apenas dígitos e separador decimal
            char ch = e.Text.FirstOrDefault();
            if (!char.IsDigit(ch) && ch != ',' && ch != '.')
            {
                e.Handled = true;
            }
        }

        private void Monetary_LostFocus(object? sender, RoutedEventArgs e)
        {
            // Formatar campos monetários quando perder o foco
            void FormatBox(System.Windows.Controls.TextBox? tb)
            {
                if (tb == null) return;
                if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var d))
                {
                    tb.Text = d.ToString("N2", CultureInfo.CurrentCulture);
                }
            }
            FormatBox(TxtBlindCash);
            FormatBox(TxtBlindCardDebit);
            FormatBox(TxtBlindCardCredit);
            FormatBox(TxtBlindCheques);
            FormatBox(TxtBlindPix);
        }

        private decimal TryParseDecimal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var d)) return d;
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var di)) return di;
            return 0m;
        }

        private double ParseCurrency(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0.0;
            if (decimal.TryParse(text, NumberStyles.Currency, CultureInfo.CurrentCulture, out var d))
                return (double)d;
            // fallback: try invariant
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2))
                return (double)d2;
            return 0.0;
        }

        private void BtnSair_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}