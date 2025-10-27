using System;
using System.Globalization;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace PDV_MedusaX8
{
    public partial class SaleDiscountWindow : Window
    {
        private readonly decimal _originalTotal;
        private readonly decimal _maxPercent;

        public decimal AppliedAmount { get; private set; } = 0m;
        public bool IsPercent { get; private set; } = false;
        public decimal PercentValue { get; private set; } = 0m;
        public string Reason { get; private set; } = string.Empty;

        public SaleDiscountWindow(decimal originalTotal, decimal maxPercent)
        {
            InitializeComponent();
            _originalTotal = originalTotal;
            _maxPercent = maxPercent;
            TxtTotalBase.Text = $"Total base: {_originalTotal.ToString("C", CultureInfo.CurrentCulture)}";
        }

        private void Confirm_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var txt = TxtDiscountValue.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(txt))
                {
                    MessageBox.Show("Informe um valor para o desconto.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var isPercent = ChkPercent.IsChecked == true;
                if (isPercent)
                {
                    if (!decimal.TryParse(txt, NumberStyles.Any, CultureInfo.CurrentCulture, out var perc))
                    {
                        MessageBox.Show("Informe um percentual válido.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (perc < 0) perc = 0;
                    if (perc > _maxPercent)
                    {
                        MessageBox.Show($"Percentual acima do limite: {_maxPercent:N2}%.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    PercentValue = perc;
                    AppliedAmount = Math.Round(_originalTotal * (perc / 100m), 2);
                    IsPercent = true;
                }
                else
                {
                    if (!decimal.TryParse(txt.Replace("R$", ""), NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
                    {
                        MessageBox.Show("Informe um valor válido.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (value < 0) value = 0;
                    if (value > _originalTotal) value = _originalTotal;
                    AppliedAmount = Math.Round(value, 2);
                    PercentValue = _originalTotal > 0 ? (AppliedAmount / _originalTotal) * 100m : 0m;
                    if (PercentValue > _maxPercent)
                    {
                        MessageBox.Show($"Valor corresponde a {_maxPercent:N2}%+ do total. Ajuste ou use percentual.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    IsPercent = false;
                }

                Reason = (CmbReason.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty;

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao validar desconto: {ex.Message}", "Desconto", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}