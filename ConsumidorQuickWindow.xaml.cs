using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace PDV_MedusaX8
{
    public partial class ConsumidorQuickWindow : Window
    {
        public string ResultName { get; private set; } = string.Empty;
        public string ResultCPF { get; private set; } = string.Empty;

        public ConsumidorQuickWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => { TxtNome.Focus(); };
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            var nome = TxtNome.Text?.Trim() ?? string.Empty;
            var cpfRaw = TxtCPF.Text?.Trim() ?? string.Empty;
            var cpfDigits = new string(cpfRaw.Where(char.IsDigit).ToArray());

            if (string.IsNullOrWhiteSpace(nome))
            {
                MessageBox.Show("Informe o nome do consumidor.", "Consumidor", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNome.Focus();
                return;
            }
            if (cpfDigits.Length != 11 && cpfDigits.Length != 14)
            {
                MessageBox.Show("Informe um CPF válido (11 dígitos) ou CNPJ (14 dígitos).", "Consumidor", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCPF.Focus();
                return;
            }

            ResultName = nome;
            ResultCPF = cpfDigits;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}