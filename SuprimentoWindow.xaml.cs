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

            // Migração defensiva: garantir coluna CashRegisterNumber
            var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmdInfo = con.CreateCommand())
            {
                cmdInfo.CommandText = "PRAGMA table_info(CashMovements)";
                using var rd = cmdInfo.ExecuteReader();
                while (rd.Read())
                {
                    cols.Add(rd.GetString(1));
                }
            }
            if (!cols.Contains("CashRegisterNumber"))
            {
                using var cmdAlter = con.CreateCommand();
                cmdAlter.CommandText = "ALTER TABLE CashMovements ADD COLUMN CashRegisterNumber INTEGER";
                cmdAlter.ExecuteNonQuery();
            }
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
            string operador = PDV_MedusaX8.Services.SessionManager.CurrentUser ?? Environment.UserName;

            try
            {
                using var con = new SqliteConnection(GetConnectionString());
                con.Open();
                EnsureTables(con);

                // Inserir suprimento
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO CashMovements (Type, Amount, Reason, Operator, CashRegisterNumber) VALUES ($type, $amount, $reason, $operator, $cash)";
                    cmd.Parameters.AddWithValue("$type", "SUPRIMENTO");
                    cmd.Parameters.AddWithValue("$amount", (double)valor);
                    cmd.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(motivo) ? (object)DBNull.Value : motivo);
                    cmd.Parameters.AddWithValue("$operator", operador);
                    cmd.Parameters.AddWithValue("$cash", GetCashRegisterNumber(con));
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
        private static int GetCashRegisterNumber(SqliteConnection con)
        {
            int cashNumber = 1;
            try
            {
                using var cmdGet = con.CreateCommand();
                cmdGet.CommandText = "SELECT Value FROM Settings WHERE Key='CashRegisterNumber' LIMIT 1";
                var v = cmdGet.ExecuteScalar();
                if (v != null && v != DBNull.Value && int.TryParse(v.ToString(), out var n)) cashNumber = n;
                if (cashNumber == 0)
                {
                    using var cmdSerie = con.CreateCommand();
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