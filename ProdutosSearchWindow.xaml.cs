using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class ProdutosSearchWindow : Window
    {
        public class ProductEntry
        {
            public int Id { get; set; }
            public string Code { get; set; } = string.Empty;
            public string? EAN { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string PriceFormatted => $"R$ {Price:N2}";
        }

        public ProductEntry? SelectedEntry { get; private set; }
        public decimal SelectedQty { get; private set; } = 1m;
        private List<ProductEntry> _allProducts = new List<ProductEntry>();

        public ProdutosSearchWindow()
        {
            InitializeComponent();
            LoadProducts();
            TxtFiltro.Focus();
        }

        private void LoadProducts()
        {
            try
            {
                _allProducts.Clear();
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, Code, EAN, Description, UnitPrice FROM Products ORDER BY Description;";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                _allProducts.Add(new ProductEntry
                                {
                                    Id = reader.GetInt32(0),
                                    Code = reader.GetString(1),
                                    EAN = reader.IsDBNull(2) ? null : reader.GetString(2),
                                    Description = reader.GetString(3),
                                    Price = reader.GetDecimal(4)
                                });
                            }
                        }
                    }
                }
                LstProdutos.ItemsSource = _allProducts;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar produtos: {ex.Message}", "Consulta de Produtos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void TxtFiltro_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtFiltro.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(filter))
            {
                LstProdutos.ItemsSource = _allProducts;
                return;
            }

            var filtered = _allProducts.Where(p => 
                p.Code.ToLower().Contains(filter) || 
                (p.EAN != null && p.EAN.ToLower().Contains(filter)) || 
                p.Description.ToLower().Contains(filter)
            ).ToList();
            
            LstProdutos.ItemsSource = filtered;
        }

        private void BtnAdicionar_Click(object sender, RoutedEventArgs e)
        {
            if (LstProdutos.SelectedItem is ProductEntry entry)
            {
                SelectedEntry = entry;
                if (decimal.TryParse(TxtQtd.Text.Replace('.', ','), out decimal qty) && qty > 0)
                {
                    SelectedQty = Math.Round(qty, 5, MidpointRounding.AwayFromZero);
                }
                else
                {
                    SelectedQty = 1m;
                }
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Selecione um produto primeiro.", "Consulta de Produtos", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnFechar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void LstProdutos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstProdutos.SelectedItem is ProductEntry)
            {
                BtnAdicionar_Click(sender, e);
            }
        }
    }
}