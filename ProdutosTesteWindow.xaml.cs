using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class ProdutosTesteWindow : Window
    {
        public class CatalogEntry
        {
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string PriceFormatted => Price.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
        }

        public CatalogEntry? SelectedEntry { get; private set; }
        public decimal SelectedQty { get; private set; } = 1m;

        public ProdutosTesteWindow(IEnumerable<(string Code, string Description, decimal Price)> catalog)
        {
            InitializeComponent();
            var configured = GetConfiguredCodes();
            var filtered = catalog
                .Where(c => configured.Contains(c.Code))
                .Select(c => new CatalogEntry { Code = c.Code, Description = c.Description, Price = c.Price })
                .ToList();

            ItemsProdutos.ItemsSource = filtered;

            if (filtered.Count == 0 && configured.Count == 0)
            {
                // Sem produtos vinculados ainda
                // Mantemos a tela vazia para o usuário saber que precisa configurar
                // em Configurações > Produto Fácil.
            }
        }

        private HashSet<string> GetConfiguredCodes()
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key='ProdutoFacilCodes' LIMIT 1;";
                var obj = cmd.ExecuteScalar();
                var raw = (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj) ?? string.Empty;
                return raw
                    .Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void ProdutoTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CatalogEntry entry)
            {
                SelectedEntry = entry;
                if (!decimal.TryParse(TxtQtd.Text.Replace('.', ','), NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var qty) || qty <= 0)
                {
                    MessageBox.Show("Quantidade inválida.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedQty = qty;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}