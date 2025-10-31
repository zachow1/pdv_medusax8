using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PDV_MedusaX8
{
    public partial class ConsumidorQuickWindow : Window
    {
        public string ResultName { get; private set; } = string.Empty;
        public string ResultCPF { get; private set; } = string.Empty;

        private bool _userInteracted = false;
        private DispatcherTimer _autoCloseTimer = new DispatcherTimer();

        public ConsumidorQuickWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => { TxtNome.Focus(); };
            PreviewKeyDown += ConsumidorQuickWindow_PreviewKeyDown;

            _autoCloseTimer.Interval = TimeSpan.FromSeconds(2);
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;
            _autoCloseTimer.Start();

            TxtNome.TextChanged += AnyInteraction;
            TxtCPF.TextChanged += AnyInteraction;
            PreviewMouseDown += AnyInteraction;
        }

        private void ConsumidorQuickWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
                e.Handled = true;
            }
            else
            {
                _userInteracted = true;
            }
        }

        private void AnyInteraction(object? sender, EventArgs e)
        {
            _userInteracted = true;
        }

        private void AutoCloseTimer_Tick(object? sender, EventArgs e)
        {
            if (!_userInteracted && string.IsNullOrWhiteSpace(TxtNome.Text) && string.IsNullOrWhiteSpace(TxtCPF.Text))
            {
                _autoCloseTimer.Stop();
                this.DialogResult = false;
                this.Close();
            }
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