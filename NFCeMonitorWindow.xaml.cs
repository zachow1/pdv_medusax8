using System;
using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace PDV_MedusaX8
{
    public partial class NFCeMonitorWindow : Window
    {
        public class NFCeRow
        {
            public long Id { get; set; }
            public int Numero { get; set; }
            public int Serie { get; set; }
            public string? Chave { get; set; }
            public string? DataEmissao { get; set; }
            public string? Status { get; set; }
            public string? Protocolo { get; set; }
            public string? XmlPath { get; set; }
            public decimal Total { get; set; }
            public string? ConsumidorCPF { get; set; }
            public string? ConsumidorNome { get; set; }
            public string? UltimoStatus { get; set; }
        }

        private ObservableCollection<NFCeRow> _rows = new ObservableCollection<NFCeRow>();

        public NFCeMonitorWindow()
        {
            InitializeComponent();
            DgvNFCe.ItemsSource = _rows;
            LoadRows();
        }

        private string GetConnectionString() => PDV_MedusaX8.Services.DbHelper.GetConnectionString();

        private void LoadRows()
        {
            try
            {
                _rows.Clear();
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT n.Id, n.Numero, n.Serie, n.Chave, n.DataEmissao, n.Status, n.Protocolo, n.XmlPath, n.Total, n.ConsumidorCPF, n.ConsumidorNome,
                                      (SELECT s.Codigo || ' - ' || s.Mensagem FROM NFCeStatus s WHERE s.NFCeId = n.Id ORDER BY s.Id DESC LIMIT 1) AS UltimoStatus
                                      FROM NFCe n ORDER BY n.Id DESC LIMIT 200";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    _rows.Add(new NFCeRow
                    {
                        Id = r.IsDBNull(0) ? 0 : r.GetInt64(0),
                        Numero = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                        Serie = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                        Chave = r.IsDBNull(3) ? null : r.GetString(3),
                        DataEmissao = r.IsDBNull(4) ? null : r.GetString(4),
                        Status = r.IsDBNull(5) ? null : r.GetString(5),
                        Protocolo = r.IsDBNull(6) ? null : r.GetString(6),
                        XmlPath = r.IsDBNull(7) ? null : r.GetString(7),
                        Total = r.IsDBNull(8) ? 0m : r.GetDecimal(8),
                        ConsumidorCPF = r.IsDBNull(9) ? null : r.GetString(9),
                        ConsumidorNome = r.IsDBNull(10) ? null : r.GetString(10),
                        UltimoStatus = r.IsDBNull(11) ? null : r.GetString(11)
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar NFC-e: {ex.Message}", "NFC-e", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadRows();
        }

        private void OpenXml_Click(object sender, RoutedEventArgs e)
        {
            if (DgvNFCe.SelectedItem is NFCeRow row)
            {
                if (!string.IsNullOrWhiteSpace(row.XmlPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = row.XmlPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Não foi possível abrir o XML: {ex.Message}", "NFC-e", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Esta NFC-e não possui caminho XML registrado.", "NFC-e", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Selecione uma NFC-e na lista.", "NFC-e", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}