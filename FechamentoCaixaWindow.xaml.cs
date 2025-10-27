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
        public FechamentoCaixaWindow()
        {
            InitializeComponent();
            this.Loaded += FechamentoCaixaWindow_Loaded;
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
                            SangriaTotal REAL
                        );";
                    cmdCreate.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO CashClosures (PeriodDate, SalesTotal, SalesCount, NFCeTotal, NFCeCount, SuprimentoTotal, SangriaTotal)
                                         VALUES ($period, $salesTot, $salesCnt, $nfTot, $nfCnt, $supTot, $sgTot)";
                    cmd.Parameters.AddWithValue("$period", DateTime.Today.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$salesTot", ParseCurrency(TxtTotalVendas.Text));
                    cmd.Parameters.AddWithValue("$salesCnt", int.TryParse(TxtQtdVendas.Text, out var sc) ? sc : 0);
                    cmd.Parameters.AddWithValue("$nfTot", ParseCurrency(TxtTotalNFCe.Text));
                    cmd.Parameters.AddWithValue("$nfCnt", int.TryParse(TxtQtdNFCe.Text, out var nc) ? nc : 0);
                    cmd.Parameters.AddWithValue("$supTot", ParseCurrency(TxtTotalSuprimento.Text));
                    cmd.Parameters.AddWithValue("$sgTot", ParseCurrency(TxtTotalSangria.Text));
                    cmd.ExecuteNonQuery();
                }
                MessageBox.Show("Fechamento registrado.", "Fechamento", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao registrar fechamento: {ex.Message}", "Fechamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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