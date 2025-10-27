using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using PDV_MedusaX8.Models;

namespace PDV_MedusaX8
{
    public partial class ItemDiscountWindow : Window
    {
        private readonly decimal _maxPercent;
        public CartItem? SelectedItem { get; private set; }
        public bool IsPercent { get; private set; }
        public decimal PercentValue { get; private set; }
        public decimal AppliedAmount { get; private set; }
        public string? Reason { get; private set; }

        public ItemDiscountWindow(List<CartItem> items, decimal maxPercent)
        {
            InitializeComponent();
            _maxPercent = maxPercent;
            ItemsList.ItemsSource = items;
            if (items.Count > 0) ItemsList.SelectedIndex = items.Count - 1; // ultimo item como padrão
            TxtHint.Text = $"Desconto máximo permitido: {_maxPercent}%";
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (ItemsList.SelectedItem is not CartItem item)
            {
                MessageBox.Show("Selecione um item.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var raw = (TxtValue.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
            {
                MessageBox.Show("Informe o valor do desconto.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            raw = raw.Replace("%", string.Empty).Replace(',', '.');
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) || val <= 0)
            {
                MessageBox.Show("Valor inválido para desconto.", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reasonItem = CmbReason.SelectedItem as System.Windows.Controls.ComboBoxItem;
            Reason = reasonItem?.Content?.ToString();

            var gross = item.VlUnit * (decimal)item.Qt;
            if (ChkPercent.IsChecked == true)
            {
                IsPercent = true;
                PercentValue = val;
                if (PercentValue > _maxPercent)
                {
                    MessageBox.Show($"Percentual acima do máximo permitido ({_maxPercent}%).", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                AppliedAmount = Math.Round(gross * (PercentValue / 100m), 2);
            }
            else
            {
                IsPercent = false;
                PercentValue = 0m;
                var maxAbs = Math.Round(gross * (_maxPercent / 100m), 2);
                if (val > maxAbs)
                {
                    MessageBox.Show($"Valor acima do máximo permitido (R$ {maxAbs:N2}).", "Desconto", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                AppliedAmount = Math.Round(val, 2);
            }

            SelectedItem = item;
            DialogResult = true;
            Close();
        }
    }
}