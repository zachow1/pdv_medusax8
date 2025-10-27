using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace PDV_MedusaX8
{
    public partial class AlterarPrecoWindow : Window
    {
        public decimal NovoPreco { get; private set; }
        public string CodigoInformado { get; private set; } = string.Empty;

        public AlterarPrecoWindow()
        {
            InitializeComponent();
            TxtDescricao.Text = string.Empty;
            TxtPreco.Text = string.Empty;
            Loaded += (s, e) => { TxtCodigoInput.Focus(); };
        }

        public AlterarPrecoWindow(string descricao, string codigo, decimal precoAtual)
        {
            InitializeComponent();
            TxtDescricao.Text = descricao;
            TxtCodigoInput.Text = codigo;
            CodigoInformado = codigo;
            NovoPreco = precoAtual;
            TxtPreco.Text = precoAtual.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
            Loaded += (s, e) => { TxtPreco.Focus(); TxtPreco.SelectAll(); };
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9.,]+$");
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            var code = (TxtCodigoInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show("Informe o código.", "Alterar Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var text = (TxtPreco.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("Informe o novo preço.", "Alterar Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var normalized = text.Replace(',', '.');
            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
            {
                MessageBox.Show("Preço inválido.", "Alterar Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CodigoInformado = code;
            NovoPreco = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
            DialogResult = true;
        }
    }
}