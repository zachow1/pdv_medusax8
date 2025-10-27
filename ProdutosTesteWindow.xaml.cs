using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

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
            var list = new List<CatalogEntry>();
            foreach (var c in catalog)
            {
                list.Add(new CatalogEntry { Code = c.Code, Description = c.Description, Price = c.Price });
            }
            LstProdutos.ItemsSource = list;
        }

        private void BtnAdicionar_Click(object sender, RoutedEventArgs e)
        {
            if (LstProdutos.SelectedItem is CatalogEntry entry)
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
            else
            {
                MessageBox.Show("Selecione um produto.", "Validação", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}