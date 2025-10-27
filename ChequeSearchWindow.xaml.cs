using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;
using System.Windows.Controls;
using PDV_MedusaX8.Models;
using System.Globalization;

namespace PDV_MedusaX8
{
    public partial class ChequeSearchWindow : Window
    {
        public ChequeInfo? SelectedCheque { get; private set; }
        private bool _onlyUnused;

        public ChequeSearchWindow() : this(onlyUnused: false) {}

        public ChequeSearchWindow(bool onlyUnused)
        {
            InitializeComponent();
            _onlyUnused = onlyUnused;
            this.Loaded += ChequeSearchWindow_Loaded;
        }
        private void ChequeSearchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Carregar cheques iniciais (opcional)
            BuscarCheques();
            TxtClienteNome.Focus();
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            BuscarCheques();
        }

        private void BuscarCheques()
        {
            var cheques = new List<ChequeInfo>();
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                
                // Verificar se a tabela existe, se não, criar
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Cheques (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Valor REAL NOT NULL,
                            BomPara TEXT,
                            Emitente TEXT,
                            BancoCodigo TEXT,
                            Agencia TEXT,
                            Conta TEXT,
                            NumeroCheque TEXT,
                            CpfCnpjEmitente TEXT,
                            DataCadastro TEXT DEFAULT CURRENT_TIMESTAMP,
                            Utilizado INTEGER DEFAULT 0,
                            CidadeCodigo TEXT
                        );";
                    cmdCheck.ExecuteNonQuery();
                }

                using var cmd = conn.CreateCommand();
                var whereClause = new List<string>();
                var parameters = new List<(string name, object value)>();

                // Filtros
                if (!string.IsNullOrWhiteSpace(TxtClienteNome.Text))
                {
                    whereClause.Add("Emitente LIKE @nome");
                    parameters.Add(("@nome", $"%{TxtClienteNome.Text}%"));
                }

                if (!string.IsNullOrWhiteSpace(TxtNumeroCheque.Text))
                {
                    whereClause.Add("NumeroCheque LIKE @numero");
                    parameters.Add(("@numero", $"%{TxtNumeroCheque.Text}%"));
                }

                if (!string.IsNullOrWhiteSpace(TxtValorCheque.Text))
                {
                    var valorText = TxtValorCheque.Text.Replace("R$", "").Trim();
                    if (decimal.TryParse(valorText, NumberStyles.Number, CultureInfo.CurrentCulture, out var valorFiltro))
                    {
                        whereClause.Add("Valor = @valor");
                        parameters.Add(("@valor", valorFiltro));
                    }
                }

                if (DtVencimento.SelectedDate.HasValue)
                {
                    whereClause.Add("date(BomPara) = date(@vencimento)");
                    parameters.Add(("@vencimento", DtVencimento.SelectedDate.Value.ToString("yyyy-MM-dd")));
                }

                // Filtrar por status de utilização conforme contexto
                whereClause.Add($"Utilizado = {(_onlyUnused ? 0 : 1)}");

                // Construir a consulta
                cmd.CommandText = $@"
                    SELECT Id, Valor, BomPara, Emitente, BancoCodigo, Agencia, Conta, NumeroCheque, CpfCnpjEmitente
                    FROM Cheques
                    {(whereClause.Count > 0 ? "WHERE " + string.Join(" AND ", whereClause) : "")}
                    ORDER BY BomPara, Emitente";

                // Adicionar parâmetros
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.name, param.value);
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cheque = new ChequeInfo
                    {
                        Valor = reader.GetDecimal(1),
                        BomPara = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                        Emitente = reader.IsDBNull(3) ? null : reader.GetString(3),
                        BancoCodigo = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Agencia = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Conta = reader.IsDBNull(6) ? null : reader.GetString(6),
                        NumeroCheque = reader.IsDBNull(7) ? null : reader.GetString(7),
                        CpfCnpjEmitente = reader.IsDBNull(8) ? null : reader.GetString(8)
                    };
                    cheques.Add(cheque);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao buscar cheques: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            DgCheques.ItemsSource = cheques;
            BtnSelecionar.IsEnabled = false;
        }

        private void DgCheques_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnSelecionar.IsEnabled = DgCheques.SelectedItem != null;
        }

        private void BtnSelecionar_Click(object sender, RoutedEventArgs e)
        {
            if (DgCheques.SelectedItem is ChequeInfo cheque)
            {
                SelectedCheque = cheque;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}