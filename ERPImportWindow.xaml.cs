using System;
using System.Globalization;
using System.Threading.Tasks;
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
            this.Loaded += (_, __) => TxtCustomerName.Focus();
            UpdateSyncStatus("Pronto para importação");
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

        private void UpdateSyncStatus(string status)
        {
            TxtSyncStatus.Text = status;
        }

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var nome = TxtCustomerName.Text?.Trim() ?? string.Empty;
            var numero = TxtOrderNumber.Text?.Trim() ?? string.Empty;
            var valorRaw = TxtOrderValue.Text?.Trim() ?? string.Empty;

            // Validações
            if (string.IsNullOrWhiteSpace(nome))
            {
                MessageBox.Show("Informe o nome do cliente.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCustomerName.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(numero))
            {
                MessageBox.Show("Informe o número do pedido.", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtOrderNumber.Focus();
                return;
            }
            if (!decimal.TryParse(valorRaw, NumberStyles.Number, CultureInfo.CurrentCulture, out var valor) || valor <= 0)
            {
                MessageBox.Show("Informe o valor da compra (maior que zero).", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtOrderValue.Focus();
                return;
            }

            // Desabilitar botão durante processamento
            BtnImport.IsEnabled = false;
            UpdateSyncStatus("Processando importação...");

            try
            {
                // Simular sincronização com API ERP
                await Task.Delay(1000); // Simula chamada à API
                UpdateSyncStatus("Sincronizando com ERP...");

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

                UpdateSyncStatus("Importação concluída com sucesso!");
                await Task.Delay(500); // Mostrar mensagem de sucesso

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                UpdateSyncStatus("Erro na importação");
                MessageBox.Show($"Falha ao importar pedido: {ex.Message}", "Importação ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnImport.IsEnabled = true;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtCustomerName.Text = string.Empty;
            TxtOrderNumber.Text = string.Empty;
            TxtOrderValue.Text = string.Empty;
            UpdateSyncStatus("Formulário limpo - Pronto para nova importação");
            TxtCustomerName.Focus();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}