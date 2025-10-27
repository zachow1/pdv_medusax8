using System;
using System.Globalization;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class SuprimentoWindow : Window
    {
        public SuprimentoWindow()
        {
            InitializeComponent();
            TxtValor.Focus();
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void EnsureTables(SqliteConnection con)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CashMovements (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Type TEXT,
                    Amount REAL,
                    Reason TEXT,
                    Operator TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );";
            cmd.ExecuteNonQuery();
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtValor.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var valor) || valor <= 0)
            {
                MessageBox.Show("Informe um valor válido.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtValor.Focus();
                return;
            }

            var motivo = TxtMotivo.Text?.Trim();
            string operador = Environment.UserName;

            try
            {
                using var con = new SqliteConnection(GetConnectionString());
                con.Open();
                EnsureTables(con);

                // Inserir suprimento
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO CashMovements (Type, Amount, Reason, Operator) VALUES ($type, $amount, $reason, $operator)";
                    cmd.Parameters.AddWithValue("$type", "SUPRIMENTO");
                    cmd.Parameters.AddWithValue("$amount", (double)valor);
                    cmd.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(motivo) ? (object)DBNull.Value : motivo);
                    cmd.Parameters.AddWithValue("$operator", operador);
                    cmd.ExecuteNonQuery();
                }

                // Recalcular saldo de caixa (SUPRIMENTO soma, SANGRIA subtrai) e persistir em Settings
                double saldo = 0.0;
                using (var cmdSum = con.CreateCommand())
                {
                // ...
                    cmdSum.CommandText = @"SELECT COALESCE(SUM(CASE WHEN Type='ABERTURA' THEN Amount WHEN Type='SUPRIMENTO' THEN Amount WHEN Type='SANGRIA' THEN -Amount ELSE 0 END), 0) FROM CashMovements";
                    var obj = cmdSum.ExecuteScalar();
                    if (obj is double d) saldo = d;
                    else if (obj is long l) saldo = l;
                    else if (obj is int i) saldo = i;
                }

                // Upsert em Settings
                using (var cmdUp = con.CreateCommand())
                {
                    cmdUp.CommandText = "UPDATE Settings SET Value = $val WHERE Key = 'CashBalance'";
                    cmdUp.Parameters.AddWithValue("$val", saldo.ToString(CultureInfo.InvariantCulture));
                    int rows = cmdUp.ExecuteNonQuery();
                    if (rows == 0)
                    {
                        using var cmdIns = con.CreateCommand();
                        cmdIns.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('CashBalance', $val)";
                        cmdIns.Parameters.AddWithValue("$val", saldo.ToString(CultureInfo.InvariantCulture));
                        cmdIns.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao salvar suprimento: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Suprimento registrado com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}