using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class RecebimentoWindow : Window
    {
        private List<ReceivableItem> _receivables = new();
        private List<InstallmentItem> _installments = new();
        private List<PaymentMethodOption> _paymentMethods = new();

        public RecebimentoWindow()
        {
            InitializeComponent();
            Loaded += RecebimentoWindow_Loaded;
        }

        private void RecebimentoWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            LoadPaymentMethods();
            LoadOpenReceivables();
            TxtFiltro.Focus();
        }

        private void LoadPaymentMethods()
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Code, Name FROM PaymentMethods WHERE IsEnabled = 1 ORDER BY DisplayOrder, Name";
                using var rd = cmd.ExecuteReader();
                _paymentMethods.Clear();
                while (rd.Read())
                {
                    _paymentMethods.Add(new PaymentMethodOption
                    {
                        Code = rd.GetString(0),
                        Name = rd.GetString(1)
                    });
                }
                CmbPaymentMethod.ItemsSource = _paymentMethods;
                if (_paymentMethods.Count > 0)
                {
                    CmbPaymentMethod.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void LoadOpenReceivables(string? filter = null)
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT Id, CustomerName, CustomerDoc, Type, DocumentNumber, DueDate, OriginalAmount, PaidAmount, Status
                                    FROM Receivables WHERE Status IN ('ABERTO','PARCIAL') ORDER BY DueDate";
                using var rd = cmd.ExecuteReader();
                var list = new List<ReceivableItem>();
                while (rd.Read())
                {
                    var item = new ReceivableItem
                    {
                        Id = rd.GetInt32(0),
                        CustomerName = rd.IsDBNull(1) ? string.Empty : rd.GetString(1),
                        CustomerDoc = rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
                        Type = rd.IsDBNull(3) ? string.Empty : rd.GetString(3),
                        DocumentNumber = rd.IsDBNull(4) ? string.Empty : rd.GetString(4),
                        DueDate = rd.IsDBNull(5) ? string.Empty : rd.GetString(5),
                        OriginalAmount = rd.IsDBNull(6) ? 0m : Convert.ToDecimal(rd.GetDouble(6)),
                        PaidAmount = rd.IsDBNull(7) ? 0m : Convert.ToDecimal(rd.GetDouble(7)),
                        Status = rd.IsDBNull(8) ? string.Empty : rd.GetString(8)
                    };
                    list.Add(item);
                }
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    var f = filter.Trim();
                    list = list.Where(r => (r.CustomerName?.IndexOf(f, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                                            || (r.CustomerDoc?.IndexOf(f, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                                            || (r.DocumentNumber?.IndexOf(f, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0).ToList();
                }
                _receivables = list;
                DgReceivables.ItemsSource = _receivables;
            }
            catch { }
        }

        private void LoadInstallmentsFor(int receivableId)
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT Id, ParcelNumber, DueDate, Amount, PaidAmount, Status
                                    FROM ReceivableInstallments WHERE ReceivableId = $id ORDER BY ParcelNumber";
                cmd.Parameters.AddWithValue("$id", receivableId);
                using var rd = cmd.ExecuteReader();
                var list = new List<InstallmentItem>();
                while (rd.Read())
                {
                    list.Add(new InstallmentItem
                    {
                        Id = rd.GetInt32(0),
                        ParcelNumber = rd.GetInt32(1),
                        DueDate = rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
                        Amount = rd.IsDBNull(3) ? 0m : Convert.ToDecimal(rd.GetDouble(3)),
                        PaidAmount = rd.IsDBNull(4) ? 0m : Convert.ToDecimal(rd.GetDouble(4)),
                        Status = rd.IsDBNull(5) ? string.Empty : rd.GetString(5)
                    });
                }
                _installments = list;
                DgInstallments.ItemsSource = _installments;
            }
            catch { }
        }

        private void DgReceivables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = DgReceivables.SelectedItem as ReceivableItem;
            if (sel == null)
            {
                DgInstallments.ItemsSource = null;
                TxtAmount.Text = string.Empty;
                TxtName.Text = string.Empty;
                TxtDoc.Text = string.Empty;
                return;
            }
            TxtName.Text = sel.CustomerName;
            TxtDoc.Text = sel.CustomerDoc;
            if (string.Equals(sel.Type, "CARNE", StringComparison.OrdinalIgnoreCase))
            {
                LoadInstallmentsFor(sel.Id);
                // Valor sugerido: selecione primeira parcela em aberto
                var firstOpen = _installments.FirstOrDefault(i => !string.Equals(i.Status, "QUITADO", StringComparison.OrdinalIgnoreCase));
                if (firstOpen != null)
                {
                    DgInstallments.SelectedItem = firstOpen;
                    var pend = Math.Max(0m, firstOpen.Amount - firstOpen.PaidAmount);
                    TxtAmount.Text = pend.ToString("N2", CultureInfo.CurrentCulture);
                }
            }
            else
            {
                DgInstallments.ItemsSource = null;
                var pend = Math.Max(0m, sel.OriginalAmount - sel.PaidAmount);
                TxtAmount.Text = pend.ToString("N2", CultureInfo.CurrentCulture);
            }
        }

        private void DgInstallments_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selParc = DgInstallments.SelectedItem as InstallmentItem;
            if (selParc != null)
            {
                var pend = Math.Max(0m, selParc.Amount - selParc.PaidAmount);
                TxtAmount.Text = pend.ToString("N2", CultureInfo.CurrentCulture);
            }
        }

        private void TxtFiltro_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            LoadOpenReceivables(TxtFiltro.Text);
        }

        private void BtnReceber_Click(object sender, RoutedEventArgs e)
        {
            var sel = DgReceivables.SelectedItem as ReceivableItem;
            if (sel == null)
            {
                MessageBox.Show("Selecione um título (Carnê/Boleto) para receber.", "Recebimento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pm = CmbPaymentMethod.SelectedItem as PaymentMethodOption;
            if (pm == null)
            {
                MessageBox.Show("Selecione a forma de pagamento.", "Recebimento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!decimal.TryParse(TxtAmount.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                MessageBox.Show("Informe um valor válido para receber.", "Recebimento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int? installmentId = null;
            decimal pending;
            if (string.Equals(sel.Type, "CARNE", StringComparison.OrdinalIgnoreCase))
            {
                var selParc = DgInstallments.SelectedItem as InstallmentItem;
                if (selParc == null)
                {
                    MessageBox.Show("Selecione a parcela do Carnê a receber.", "Recebimento", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                installmentId = selParc.Id;
                pending = Math.Max(0m, selParc.Amount - selParc.PaidAmount);
            }
            else
            {
                pending = Math.Max(0m, sel.OriginalAmount - sel.PaidAmount);
            }
            if (amount > pending)
            {
                if (MessageBox.Show($"Valor supera o pendente (R$ {pending:N2}). Deseja receber mesmo assim?", "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var notes = TxtNotes.Text?.Trim();
            var name = string.IsNullOrWhiteSpace(TxtName.Text) ? sel.CustomerName : TxtName.Text.Trim();
            var doc = string.IsNullOrWhiteSpace(TxtDoc.Text) ? sel.CustomerDoc : TxtDoc.Text.Trim();

            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var tx = conn.BeginTransaction();

                // 1) Caixa
                long cashId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO CashMovements (Type, Amount, Reason, Operator, CreatedAt, PaymentMethodCode, CounterpartyName, CounterpartyDoc, ReferenceType, ReferenceId, DocumentNumber, Notes, CashRegisterNumber)
                                        VALUES ('RECEBIMENTO', $amount, $reason, $op, CURRENT_TIMESTAMP, $pm, $name, $doc, 'RECEIVABLE', $refId, $docnum, $notes, $cashreg);";
                    cmd.Parameters.AddWithValue("$amount", amount);
                    cmd.Parameters.AddWithValue("$reason", string.Equals(sel.Type, "CARNE", StringComparison.OrdinalIgnoreCase) ? $"Recebimento Carnê {sel.DocumentNumber}" : $"Recebimento Boleto {sel.DocumentNumber}");
                    cmd.Parameters.AddWithValue("$op", PDV_MedusaX8.Services.SessionManager.CurrentUser ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$pm", pm.Code);
                    cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? (object)DBNull.Value : name);
                    cmd.Parameters.AddWithValue("$doc", string.IsNullOrWhiteSpace(doc) ? (object)DBNull.Value : doc);
                    cmd.Parameters.AddWithValue("$refId", sel.Id);
                    cmd.Parameters.AddWithValue("$docnum", string.IsNullOrWhiteSpace(sel.DocumentNumber) ? (object)DBNull.Value : sel.DocumentNumber);
                    cmd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes);
                    cmd.Parameters.AddWithValue("$cashreg", GetCashRegisterNumber(conn));
                    cmd.ExecuteNonQuery();
                }
                using (var cmdId = conn.CreateCommand())
                {
                    cmdId.Transaction = tx;
                    cmdId.CommandText = "SELECT last_insert_rowid()";
                    cashId = (long)(cmdId.ExecuteScalar() ?? 0L);
                }

                // 2) Recebimento do título/parcela
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO ReceivableReceipts (ReceivableId, InstallmentId, Date, Amount, PaymentMethodCode, Operator, CounterpartyName, CounterpartyDoc, CashMovementId, Notes)
                                        VALUES ($rid, $iid, CURRENT_TIMESTAMP, $amount, $pm, $op, $name, $doc, $cash, $notes);";
                    cmd.Parameters.AddWithValue("$rid", sel.Id);
                    cmd.Parameters.AddWithValue("$iid", installmentId.HasValue ? (object)installmentId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("$amount", amount);
                    cmd.Parameters.AddWithValue("$pm", pm.Code);
                    cmd.Parameters.AddWithValue("$op", PDV_MedusaX8.Services.SessionManager.CurrentUser ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? (object)DBNull.Value : name);
                    cmd.Parameters.AddWithValue("$doc", string.IsNullOrWhiteSpace(doc) ? (object)DBNull.Value : doc);
                    cmd.Parameters.AddWithValue("$cash", cashId);
                    cmd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes);
                    cmd.ExecuteNonQuery();
                }

                // 3) Atualizações de parcela e cabeçalho
                if (installmentId.HasValue)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"UPDATE ReceivableInstallments
                                            SET PaidAmount = PaidAmount + $amount,
                                                Status = CASE WHEN PaidAmount + $amount >= Amount THEN 'QUITADO' ELSE 'PARCIAL' END
                                            WHERE Id = $iid";
                        cmd.Parameters.AddWithValue("$amount", amount);
                        cmd.Parameters.AddWithValue("$iid", installmentId.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"UPDATE Receivables
                                        SET PaidAmount = PaidAmount + $amount,
                                            Status = CASE WHEN PaidAmount + $amount >= OriginalAmount THEN 'QUITADO' ELSE 'PARCIAL' END
                                        WHERE Id = $rid";
                    cmd.Parameters.AddWithValue("$amount", amount);
                    cmd.Parameters.AddWithValue("$rid", sel.Id);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                try { AutoSyncManager.Instance.TriggerFinancialSync(); } catch { }
                MessageBox.Show("Recebimento registrado com sucesso.", "Recebimento", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao registrar recebimento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int GetCashRegisterNumber(SqliteConnection conn)
        {
            int cashNumber = 1;
            try
            {
                using var cmdGet = conn.CreateCommand();
                cmdGet.CommandText = "SELECT Value FROM Settings WHERE Key='CashRegisterNumber' LIMIT 1";
                var v = cmdGet.ExecuteScalar();
                if (v != null && v != DBNull.Value && int.TryParse(v.ToString(), out var n)) cashNumber = n;
                if (cashNumber == 0)
                {
                    using var cmdSerie = conn.CreateCommand();
                    cmdSerie.CommandText = "SELECT Serie FROM ConfiguracoesNFCe LIMIT 1";
                    var sv = cmdSerie.ExecuteScalar();
                    if (sv != null && sv != DBNull.Value && int.TryParse(sv.ToString(), out var s)) cashNumber = s;
                }
                if (cashNumber == 0) cashNumber = 1;
            }
            catch { }
            return cashNumber;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private class PaymentMethodOption
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
        private class ReceivableItem
        {
            public int Id { get; set; }
            public string? CustomerName { get; set; }
            public string? CustomerDoc { get; set; }
            public string? Type { get; set; }
            public string? DocumentNumber { get; set; }
            public string? DueDate { get; set; }
            public decimal OriginalAmount { get; set; }
            public decimal PaidAmount { get; set; }
            public string? Status { get; set; }
        }
        private class InstallmentItem
        {
            public int Id { get; set; }
            public int ParcelNumber { get; set; }
            public string? DueDate { get; set; }
            public decimal Amount { get; set; }
            public decimal PaidAmount { get; set; }
            public string? Status { get; set; }
        }
    }
}