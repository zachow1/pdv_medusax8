using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Models;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class ChequeWindow : Window
    {
        public ChequeInfo? ResultCheque { get; private set; }
        private string? _cidadeCodigo;

        private class BankItem
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string Display => string.IsNullOrWhiteSpace(Name) ? Code : $"{Code} - {Name}";
        }

        public ChequeWindow()
        {
            InitializeComponent();
            this.Loaded += ChequeWindow_Loaded;
        }

        private void ChequeWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            LoadBanks();
            TxtValor.Focus();
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void LoadBanks()
        {
            var list = new List<BankItem>();
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Code, Name FROM Banks ORDER BY Code";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    list.Add(new BankItem { Code = rd.GetString(0), Name = rd.GetString(1) });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao carregar bancos: " + ex.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            CmbBanco.ItemsSource = list;
        }

        private void BtnBuscarCidade_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new MunicipioSearchWindow();
                win.Owner = this;
                var ok = win.ShowDialog();
                if (ok == true && win.SelectedMunicipio != null)
                {
                    _cidadeCodigo = win.SelectedMunicipio.Codigo;
                    TxtCidadeBanco.Text = $"{win.SelectedMunicipio.Nome} - {win.SelectedMunicipio.UF}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao buscar cidade: " + ex.Message, "Cidade", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!decimal.TryParse(TxtValor.Text.Replace("R$",""), NumberStyles.Any, CultureInfo.CurrentCulture, out var valor) || valor <= 0)
                {
                    MessageBox.Show("Informe o valor do cheque.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtValor.Focus();
                    return;
                }

                string? bancoCodigo = CmbBanco.SelectedValue?.ToString();
                if (string.IsNullOrWhiteSpace(bancoCodigo) || bancoCodigo.Length != 3)
                {
                    MessageBox.Show("Selecione o banco (código de 3 dígitos).", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CmbBanco.Focus();
                    return;
                }

                var numero = TxtNumeroCheque.Text?.Trim();
                if (string.IsNullOrWhiteSpace(numero))
                {
                    MessageBox.Show("Informe o número do cheque.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtNumeroCheque.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_cidadeCodigo))
                {
                    MessageBox.Show("Selecione a cidade da agência.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ResultCheque = new ChequeInfo
                {
                    Valor = valor,
                    BomPara = DtBomPara.SelectedDate,
                    Emitente = TxtEmitente.Text?.Trim(),
                    BancoCodigo = bancoCodigo,
                    Agencia = TxtAgencia.Text?.Trim(),
                    Conta = TxtConta.Text?.Trim(),
                    NumeroCheque = numero,
                    CpfCnpjEmitente = TxtCpfCnpj.Text?.Trim(),
                    CidadeCodigo = _cidadeCodigo
                };

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao confirmar cheque: " + ex.Message, "Cheque", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}