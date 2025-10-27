using System;
using System.Globalization;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class ERPImportWindow : Window
    {
        public string CustomerName { get; private set; } = string.Empty;
        public string OrderNumber { get; private set; } = string.Empty;
        public decimal OrderValue { get; private set; }

        public ERPImportWindow()
        {
            InitializeComponent();
            this.Loaded += (_, __) => TxtCliente.Focus();
        }

        private string GetConnectionString() => DbHelper.GetConnectionString();

        private void EnsureTables(SqliteConnection conn)
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

        private void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var nome = TxtCliente.Text?.Trim() ?? string.Empty;
            var numero = TxtNumeroPedido.Text?.Trim() ?? string.Empty;
            var valorRaw = TxtValor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nome))
            {
                MessageBox.Show("Informe o nome do cliente.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCliente.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(numero))
            {
                MessageBox.Show("Informe o número do pedido.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNumeroPedido.Focus();
                return;
            }
            if (!decimal.TryParse(valorRaw, NumberStyles.Number, CultureInfo.CurrentCulture, out var valor) || valor <= 0)
            {
                MessageBox.Show("Informe o valor da compra (maior que zero).", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtValor.Focus();
                return;
            }

            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                EnsureTables(conn);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO ErpSalesOrderImports (CustomerName, OrderNumber, OrderValue) VALUES ($c, $n, $v);";
                cmd.Parameters.AddWithValue("$c", nome);
                cmd.Parameters.AddWithValue("$n", numero);
                cmd.Parameters.AddWithValue("$v", valor);
                cmd.ExecuteNonQuery();

                CustomerName = nome;
                OrderNumber = numero;
                OrderValue = valor;

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao importar pedido: {ex.Message}", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}