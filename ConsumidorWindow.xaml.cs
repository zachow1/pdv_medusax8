using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class ConsumidorWindow : Window
    {
        public string? ConsumerName { get; private set; }
        public string? ConsumerCPF { get; private set; }
        public int? SelectedCustomerId { get; private set; }

        private class ClienteResultado
        {
            public int Id { get; set; }
            public string Nome { get; set; } = string.Empty;
            public string? CPF { get; set; }
            public override string ToString() => string.IsNullOrWhiteSpace(CPF) ? $"{Nome}" : $"{Nome} - {FormatCpfString(CPF)}";
        }

        public ConsumidorWindow()
        {
            InitializeComponent();
            this.Loaded += ConsumidorWindow_Loaded;
            TxtFiltro.Focus();
            TxtFiltro.TextChanged += TxtFiltro_TextChanged;
            DataObject.AddPastingHandler(TxtFiltro, OnFiltroPasting);
        }

        private void ConsumidorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAllCustomers();
        }

        private void TxtFiltro_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = TxtFiltro.Text.Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                LoadAllCustomers();
            }
            else
            {
                BuscarClientesPorFiltro(q);
            }
        }

        private void GridResultados_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridResultados.SelectedItem is ClienteResultado cr)
            {
                TxtFiltro.Text = cr.Nome;
                // removido: atualização de TxtCPF
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ConsumerName = TxtFiltro.Text.Trim();
            ConsumerCPF = null; // sem campo CPF dedicado
            if (GridResultados.SelectedItem is ClienteResultado cr)
            {
                SelectedCustomerId = cr.Id;
                ConsumerName = cr.Nome;
                ConsumerCPF = cr.CPF;
            }
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CadastrarCliente_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cw = new ClienteWindow();
                cw.Owner = this;
                var ok = cw.ShowDialog();
                if (ok == true)
                {
                    LoadAllCustomers();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao abrir cadastro de cliente: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAllCustomers()
        {
            try
            {
                using var con = new SqliteConnection(GetConnectionString());
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, CPF_CNPJ FROM Customers ORDER BY Name LIMIT 500";
                using var rd = cmd.ExecuteReader();
                var list = new List<ClienteResultado>();
                while (rd.Read())
                {
                    var id = rd.GetInt32(0);
                    var nm = rd.GetString(1);
                    var cpfCnpj = rd.IsDBNull(2) ? null : rd.GetString(2);
                    string? cpfOnly = cpfCnpj;
                    if (!string.IsNullOrWhiteSpace(cpfOnly))
                    {
                        cpfOnly = OnlyDigits(cpfOnly);
                        if (cpfOnly!.Length != 11)
                        {
                            cpfOnly = null;
                        }
                    }
                    list.Add(new ClienteResultado { Id = id, Nome = nm, CPF = cpfOnly });
                }
                GridResultados.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao carregar clientes: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BuscarClientesPorFiltro(string filtro)
        {
            try
            {
                using var con = new SqliteConnection(GetConnectionString());
                con.Open();
                using var cmd = con.CreateCommand();
                var digits = OnlyDigits(filtro);
                var where = "Name LIKE $f OR CPF_CNPJ LIKE $f OR CAST(Id AS TEXT) LIKE $f";
                if (!string.IsNullOrEmpty(digits))
                {
                    where += " OR REPLACE(REPLACE(REPLACE(CPF_CNPJ,'.',''),'-',''),'/','') LIKE $fd";
                }
                cmd.CommandText = $"SELECT Id, Name, CPF_CNPJ FROM Customers WHERE {where} ORDER BY Name LIMIT 100";
                cmd.Parameters.AddWithValue("$f", "%" + filtro + "%");
                if (!string.IsNullOrEmpty(digits))
                {
                    cmd.Parameters.AddWithValue("$fd", "%" + digits + "%");
                }
                using var rd = cmd.ExecuteReader();
                var list = new List<ClienteResultado>();
                while (rd.Read())
                {
                    var id = rd.GetInt32(0);
                    var nm = rd.GetString(1);
                    var cpfCnpj = rd.IsDBNull(2) ? null : rd.GetString(2);
                    string? cpfOnly = cpfCnpj;
                    if (!string.IsNullOrWhiteSpace(cpfOnly))
                    {
                        cpfOnly = OnlyDigits(cpfOnly);
                        if (cpfOnly!.Length != 11)
                        {
                            cpfOnly = null;
                        }
                    }
                    list.Add(new ClienteResultado { Id = id, Nome = nm, CPF = cpfOnly });
                }
                GridResultados.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao buscar clientes: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private static string OnlyDigits(string s)
        {
            var chars = new List<char>(11);
            foreach (var ch in s)
            {
                if (char.IsDigit(ch)) chars.Add(ch);
            }
            return new string(chars.ToArray());
        }

        private static string FormatCpfString(string? cpf)
        {
            if (string.IsNullOrWhiteSpace(cpf)) return string.Empty;
            var digits = OnlyDigits(cpf);
            if (digits.Length != 11) return cpf!;
            return string.Concat(
                digits.Substring(0, 3), ".",
                digits.Substring(3, 3), ".",
                digits.Substring(6, 3), "-",
                digits.Substring(9, 2)
            );
        }

        private static string FormatCnpjString(string? cnpj)
        {
            if (string.IsNullOrWhiteSpace(cnpj)) return string.Empty;
            var digits = OnlyDigits(cnpj);
            if (digits.Length != 14) return cnpj!;
            return string.Concat(
                digits.Substring(0, 2), ".",
                digits.Substring(2, 3), ".",
                digits.Substring(5, 3), "/",
                digits.Substring(8, 4), "-",
                digits.Substring(12, 2)
            );
        }

        private void OnFiltroPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var txt = e.DataObject.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    var digits = OnlyDigits(txt);
                    if (digits.Length == 11)
                    {
                        e.DataObject.SetData(DataFormats.Text, FormatCpfString(digits));
                    }
                    else if (digits.Length == 14)
                    {
                        e.DataObject.SetData(DataFormats.Text, FormatCnpjString(digits));
                    }
                }
            }
        }
    }
}