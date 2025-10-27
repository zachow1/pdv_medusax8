using System;
using System.Globalization;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class SangriaWindow : Window
    {
        public SangriaWindow()
        {
            InitializeComponent();
            TxtValor.Focus();
            this.Loaded += SangriaWindow_Loaded;
        }

        private void SangriaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.Owner is MainWindow mw)
            {
                SupervisorPanel.Visibility = mw.RequiresSupervisorForSangria ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void BtnBuscarCheque_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var search = new ChequeSearchWindow(onlyUnused: false);
                search.Owner = this;
                var ok = search.ShowDialog();
                if (ok == true && search.SelectedCheque != null)
                {
                    TxtValor.Text = search.SelectedCheque.Valor.ToString("N2");
                    if (string.IsNullOrWhiteSpace(TxtMotivo.Text))
                    {
                        TxtMotivo.Text = "Sangria de cheque recebido";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao buscar cheque: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
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

            try
            {
                using var con = new SqliteConnection(GetConnectionString());
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"INSERT INTO CashMovements (Type, Amount, Reason, Operator) VALUES ($type, $amount, $reason, $operator)";
                cmd.Parameters.AddWithValue("$type", "SANGRIA");
                cmd.Parameters.AddWithValue("$amount", (double)valor);
                cmd.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(motivo) ? (object)DBNull.Value : motivo);

                string operador = Environment.UserName;
                if (this.Owner is MainWindow mw2 && mw2.RequiresSupervisorForSangria)
                {
                    if (!string.IsNullOrWhiteSpace(mw2.SupervisorName))
                        operador = $"{operador} (Supervisor: {mw2.SupervisorName})";
                }

                cmd.Parameters.AddWithValue("$operator", operador);
                cmd.ExecuteNonQuery();
                // Recalcular saldo de caixa (incluindo ABERTURA + SUPRIMENTO - SANGRIA) e persistir em Settings
                double saldo = 0.0;
                using (var cmdSum = con.CreateCommand())
                {
                    cmdSum.CommandText = @"SELECT COALESCE(SUM(CASE WHEN Type='ABERTURA' THEN Amount WHEN Type='SUPRIMENTO' THEN Amount WHEN Type='SANGRIA' THEN -Amount ELSE 0 END), 0) FROM CashMovements";
                    var obj = cmdSum.ExecuteScalar();
                    if (obj is double d) saldo = d;
                    else if (obj is long l) saldo = l;
                    else if (obj is int i) saldo = i;
                }

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
                MessageBox.Show("Erro ao salvar sangria: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Sangria registrada com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
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