using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;
using System.Collections.ObjectModel;

namespace PDV_MedusaX8
{
    public class MunicipioDto
    {
        public string? Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string UF { get; set; } = string.Empty;
    }

    public partial class MunicipioSearchWindow : Window
    {
        public ObservableCollection<MunicipioDto> Municipios { get; } = new ObservableCollection<MunicipioDto>();
        public MunicipioDto? SelectedMunicipio { get; private set; }

        public MunicipioSearchWindow()
        {
            InitializeComponent();
            GridMunicipios.ItemsSource = Municipios;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (GridMunicipios.SelectedItem is MunicipioDto sel)
            {
                SelectedMunicipio = sel;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Selecione uma cidade.", "Municípios", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Buscar_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtQuery.Text?.Trim() ?? string.Empty;
            LoadMunicipios(q);
        }

        private void LoadMunicipios(string query)
        {
            Municipios.Clear();
            // Removed unused list of MunicipioInfo
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        string like = string.IsNullOrWhiteSpace(query) ? "%" : $"%{query}%";
                        cmd.CommandText = @"SELECT Codigo, Nome, UF FROM Municipios WHERE Nome LIKE $q OR Codigo LIKE $q ORDER BY Nome LIMIT 200;";
                        cmd.Parameters.AddWithValue("$q", like);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var m = new MunicipioDto
                                {
                                    Codigo = r.IsDBNull(0) ? null : r.GetString(0),
                                    Nome = r.GetString(1),
                                    UF = r.GetString(2)
                                };
                                Municipios.Add(m);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar municípios: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }
    }
}