using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using System.Printing;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8.Services
{
    public static class PrintingService
    {
        private static string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private static string GetSetting(string key)
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                cmd.Parameters.AddWithValue("$key", key);
                var obj = cmd.ExecuteScalar();
                return (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static PrintQueue? GetPrintQueueByName(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return null;
            try
            {
                var server = new LocalPrintServer();
                var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });
                foreach (var q in queues)
                {
                    if (string.Equals(q.Name, printerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return q;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private static void PrintVisualTo(string printerName, Visual visual, string description)
        {
            var queue = GetPrintQueueByName(printerName);
            if (queue == null)
            {
                MessageBox.Show($"Impressora não encontrada: {printerName}", "Impressão", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var pd = new PrintDialog();
            pd.PrintQueue = queue;
            try
            {
                pd.PrintVisual(visual, description);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao enviar impressão: {ex.Message}", "Impressão", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void PrintConfissao(PDV_MedusaX8.MainWindow mw, decimal totalOriginal, decimal totalFinal, decimal descontoValor, bool descontoPercentual, decimal descontoPercent, string descontoMotivo, IEnumerable<PDV_MedusaX8.AppliedItem> itens)
        {
            // Dados do cliente
            var nome = string.IsNullOrWhiteSpace(mw.ConsumerName) ? "(Cliente não informado)" : mw.ConsumerName;
            var cpf = string.IsNullOrWhiteSpace(mw.ConsumerCPF) ? "—" : mw.ConsumerCPF;

            // Texto simples para início: gerar uma confissão com dados básicos
            var sp = new StackPanel { Margin = new Thickness(40), Orientation = Orientation.Vertical };
            sp.Children.Add(new TextBlock { Text = "Confissão de Dívida", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
            sp.Children.Add(new TextBlock { Text = $"Cliente: {nome}", FontSize = 16, Margin = new Thickness(0,0,0,4) });
            sp.Children.Add(new TextBlock { Text = $"CPF/CNPJ: {cpf}", FontSize = 16, Margin = new Thickness(0,0,0,12) });
            sp.Children.Add(new TextBlock { Text = $"Data: {DateTime.Now:dd/MM/yyyy HH:mm}", FontSize = 14, Margin = new Thickness(0,0,0,12) });
            sp.Children.Add(new TextBlock { Text = $"Total original: {totalOriginal:C}", FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = $"Desconto: {descontoValor:C}" + (descontoPercentual ? $" ({descontoPercent:N2}%)" : string.Empty), FontSize = 14 });
            if (!string.IsNullOrWhiteSpace(descontoMotivo))
                sp.Children.Add(new TextBlock { Text = $"Motivo: {descontoMotivo}", FontSize = 14 });

            sp.Children.Add(new TextBlock { Text = "Pagamentos:", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,6) });
            foreach (var it in itens)
            {
                sp.Children.Add(new TextBlock { Text = $"- {it.Name}: {it.Value:C}", FontSize = 14 });
            }
            sp.Children.Add(new TextBlock { Text = $"Total da venda: {totalFinal:C}", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,10,0,0) });

            // Seleciona impressora configurada
            var printerName = GetSetting("PrinterConfissao");
            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show("Nenhuma impressora configurada para Confissão de Dívida.", "Impressão", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            PrintVisualTo(printerName, sp, "Confissão de Dívida");
        }

        public static void PrintCarne(PDV_MedusaX8.MainWindow mw, decimal valorCredito, IEnumerable<PDV_MedusaX8.AppliedItem> itens)
        {
            // Decide formato padrão e impressora
            var formato = GetSetting("CarneFormatoPadrao");
            if (string.IsNullOrWhiteSpace(formato)) formato = "80mm";
            string printerName = string.Empty;
            if (string.Equals(formato, "A4", StringComparison.OrdinalIgnoreCase))
                printerName = GetSetting("PrinterCarneA4");
            else
                printerName = GetSetting("PrinterCarne80");

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show($"Nenhuma impressora configurada para Carnê ({formato}).", "Impressão", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var nome = string.IsNullOrWhiteSpace(mw.ConsumerName) ? "(Cliente não informado)" : mw.ConsumerName;
            var cpf = string.IsNullOrWhiteSpace(mw.ConsumerCPF) ? "—" : mw.ConsumerCPF;

            var sp = new StackPanel { Margin = new Thickness(20), Orientation = Orientation.Vertical };
            sp.Children.Add(new TextBlock { Text = $"Carnê de Pagamento ({formato})", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
            sp.Children.Add(new TextBlock { Text = $"Cliente: {nome}", FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = $"CPF/CNPJ: {cpf}", FontSize = 14, Margin = new Thickness(0,0,0,8) });
            sp.Children.Add(new TextBlock { Text = $"Data: {DateTime.Now:dd/MM/yyyy}", FontSize = 12, Margin = new Thickness(0,0,0,8) });

            sp.Children.Add(new TextBlock { Text = $"Crédito total: {valorCredito:C}", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,6) });
            sp.Children.Add(new TextBlock { Text = "Itens de crédito:", FontSize = 14, FontWeight = FontWeights.Bold });
            foreach (var it in itens)
            {
                sp.Children.Add(new TextBlock { Text = $"- {it.Name}: {it.Value:C}", FontSize = 12 });
            }

            PrintVisualTo(printerName, sp, "Carnê de Pagamento");
        }

        public static void PrintFechamentoCaixa(string periodo, string totalVendas, string qtdVendas, string totalNFCe, string qtdNFCe, string totalAbertura, string totalSupr, string totalSang, IEnumerable<(string Meio, decimal Total)> pagamentos)
        {
            var printerName = GetSetting("PrinterFechamento");
            if (string.IsNullOrWhiteSpace(printerName))
            {
                // Fallback para impressora de Confissão se não houver configuração específica
                printerName = GetSetting("PrinterConfissao");
            }
            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show("Nenhuma impressora configurada para Fechamento de Caixa.", "Impressão", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sp = new StackPanel { Margin = new Thickness(40), Orientation = Orientation.Vertical };
            sp.Children.Add(new TextBlock { Text = "Fechamento de Caixa", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
            sp.Children.Add(new TextBlock { Text = $"Período: {periodo}", FontSize = 14, Margin = new Thickness(0,0,0,6) });

            sp.Children.Add(new TextBlock { Text = "Vendas (Sales)", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,10,0,4) });
            sp.Children.Add(new TextBlock { Text = $"Total: {totalVendas}", FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = $"Quantidade: {qtdVendas}", FontSize = 14 });

            sp.Children.Add(new TextBlock { Text = "NFC-e", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,10,0,4) });
            sp.Children.Add(new TextBlock { Text = $"Total: {totalNFCe}", FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = $"Quantidade: {qtdNFCe}", FontSize = 14 });

            sp.Children.Add(new TextBlock { Text = "Movimentos de Caixa", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,10,0,4) });
            sp.Children.Add(new TextBlock { Text = $"Abertura: {totalAbertura}", FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = $"Suprimento: {totalSupr}", FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = $"Sangria: {totalSang}", FontSize = 14 });

            sp.Children.Add(new TextBlock { Text = "Pagamentos NFC-e (tPag)", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,10,0,4) });
            foreach (var p in pagamentos)
            {
                sp.Children.Add(new TextBlock { Text = $"- {p.Meio}: {p.Total:C}", FontSize = 14 });
            }

            PrintVisualTo(printerName, sp, "Fechamento de Caixa");
        }
    }
}