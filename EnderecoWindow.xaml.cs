using System;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class EnderecoWindow : Window
    {
        public AddressEntry? ResultAddress { get; private set; }

        public EnderecoWindow()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultAddress = new AddressEntry
            {
                AddressType = TxtTipo.Text?.Trim(),
                Street = TxtRua.Text?.Trim(),
                Number = TxtNumero.Text?.Trim(),
                Complement = TxtComplemento.Text?.Trim(),
                District = TxtBairro.Text?.Trim(),
                City = TxtCidade.Text?.Trim(),
                State = TxtEstado.Text?.Trim(),
                ZipCode = TxtCep.Text?.Trim()
            };
            this.DialogResult = true;
            this.Close();
        }

        private void BuscarCidade_Click(object sender, RoutedEventArgs e)
        {
            var sw = new MunicipioSearchWindow();
            sw.Owner = this;
            if (sw.ShowDialog() == true && sw.SelectedMunicipio != null)
            {
                TxtCidade.Text = sw.SelectedMunicipio.Nome;
                TxtEstado.Text = sw.SelectedMunicipio.UF;
            }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }
    }
}