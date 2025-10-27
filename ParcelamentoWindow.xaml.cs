using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PDV_MedusaX8
{
    public partial class ParcelamentoWindow : Window
    {
        private readonly decimal valorTotal;
        public int NumeroParcelas { get; private set; } = 2;
        public decimal ValorParcela { get; private set; }

        public ParcelamentoWindow(decimal valorTotal)
        {
            InitializeComponent();
            this.valorTotal = valorTotal;
            TxtValorTotal.Text = valorTotal.ToString("C", CultureInfo.CurrentCulture);
            // Seleção padrão: 2 parcelas
            CmbParcelas.SelectedIndex = 0; // corresponde ao item "2"
            AtualizarValorParcela();
        }

        private void CmbParcelas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AtualizarValorParcela();
        }

        private void AtualizarValorParcela()
        {
            if (CmbParcelas.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var n))
            {
                NumeroParcelas = n;
                if (n > 0)
                {
                    ValorParcela = Math.Round(valorTotal / n, 2);
                    TxtValorParcela.Text = ValorParcela.ToString("C", CultureInfo.CurrentCulture);
                }
            }
        }

        private void Confirmar_Click(object sender, RoutedEventArgs e)
        {
            if (NumeroParcelas <= 0)
            {
                MessageBox.Show("Selecione o número de parcelas.", "Parcelamento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            this.DialogResult = true;
            this.Close();
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}