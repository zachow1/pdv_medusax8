using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class ClienteWindow : Window
    {
        public ObservableCollection<AddressEntry> Addresses { get; } = new ObservableCollection<AddressEntry>();

        private bool _formattingCpfCnpj;

        public ClienteWindow()
        {
            InitializeComponent();
            GridEnderecos.ItemsSource = Addresses;
        }

        private void AddAddress_Click(object sender, RoutedEventArgs e)
        {
            var ew = new EnderecoWindow();
            ew.Owner = this;
            if (ew.ShowDialog() == true && ew.ResultAddress != null)
            {
                Addresses.Add(ew.ResultAddress);
            }
        }

        private void RemoveSelecionado_Click(object sender, RoutedEventArgs e)
        {
            if (GridEnderecos.SelectedItem is AddressEntry sel)
            {
                Addresses.Remove(sel);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void TxtCpfCnpj_LostFocus(object sender, RoutedEventArgs e)
        {
            var doc = (TxtCpfCnpj.Text ?? string.Empty).Trim();
            var digits = new string(System.Text.RegularExpressions.Regex.Replace(doc, "\\D", "").ToCharArray());
            bool ok = false;
            string tipoInferido = "";
            if (digits.Length == 11)
            {
                ok = ValidateCpf(digits);
                tipoInferido = "PF";
            }
            else if (digits.Length == 14)
            {
                ok = ValidateCnpj(digits);
                tipoInferido = "PJ";
            }
            if (!ok)
            {
                MessageBox.Show("CPF/CNPJ inválido.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                TxtCpfCnpj.ToolTip = $"Documento válido ({tipoInferido}).";
            }
        }

        private void TxtCpfCnpj_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text.FirstOrDefault());
        }

        private void TxtCpfCnpj_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_formattingCpfCnpj) return;
            _formattingCpfCnpj = true;
            var digits = new string((TxtCpfCnpj.Text ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length > 14) digits = digits.Substring(0, 14);
            string formatted = digits.Length <= 11 ? FormatCpf(digits) : FormatCnpj(digits);
            if (TxtCpfCnpj.Text != formatted)
            {
                TxtCpfCnpj.Text = formatted;
                TxtCpfCnpj.CaretIndex = TxtCpfCnpj.Text.Length;
            }
            _formattingCpfCnpj = false;

            // Atualiza feedback inline do tipo
            if (digits.Length == 11)
                LblTipoDoc.Text = "PF";
            else if (digits.Length == 14)
                LblTipoDoc.Text = "PJ";
            else
                LblTipoDoc.Text = string.Empty;
        }

        private void OnCpfCnpjPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
                var digits = new string(text.Where(char.IsDigit).ToArray());
                TxtCpfCnpj.Text = digits;
                TxtCpfCnpj.CaretIndex = TxtCpfCnpj.Text.Length;
            }
            e.CancelCommand();
        }

        private string FormatCpf(string digits)
        {
            // 000.000.000-00
            if (string.IsNullOrEmpty(digits)) return string.Empty;
            if (digits.Length > 11) digits = digits.Substring(0, 11);
            if (digits.Length <= 3) return digits;
            if (digits.Length <= 6) return $"{digits.Substring(0,3)}.{digits.Substring(3)}";
            if (digits.Length <= 9) return $"{digits.Substring(0,3)}.{digits.Substring(3,3)}.{digits.Substring(6)}";
            return $"{digits.Substring(0,3)}.{digits.Substring(3,3)}.{digits.Substring(6,3)}-{digits.Substring(9)}";
        }

        private string FormatCnpj(string digits)
        {
            // 00.000.000/0000-00
            if (string.IsNullOrEmpty(digits)) return string.Empty;
            if (digits.Length > 14) digits = digits.Substring(0, 14);
            if (digits.Length <= 2) return digits;
            if (digits.Length <= 5) return $"{digits.Substring(0,2)}.{digits.Substring(2)}";
            if (digits.Length <= 8) return $"{digits.Substring(0,2)}.{digits.Substring(2,3)}.{digits.Substring(5)}";
            if (digits.Length <= 12) return $"{digits.Substring(0,2)}.{digits.Substring(2,3)}.{digits.Substring(5,3)}/{digits.Substring(8)}";
            return $"{digits.Substring(0,2)}.{digits.Substring(2,3)}.{digits.Substring(5,3)}/{digits.Substring(8,4)}-{digits.Substring(12)}";
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nome = TxtNome.Text?.Trim();
                if (string.IsNullOrWhiteSpace(nome))
                {
                    MessageBox.Show("Informe o Nome do cliente.", "Cliente", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var nomeFantasia = TxtNomeFantasia.Text?.Trim();
                var doc = (TxtCpfCnpj.Text ?? string.Empty).Trim();
                var digits = new string(System.Text.RegularExpressions.Regex.Replace(doc, "\\D", "").ToCharArray());
                string tipo = "";
                bool docValido = false;
                if (digits.Length == 11)
                {
                    docValido = ValidateCpf(digits);
                    tipo = "PF";
                }
                else if (digits.Length == 14)
                {
                    docValido = ValidateCnpj(digits);
                    tipo = "PJ";
                }
                if (!docValido)
                {
                    MessageBox.Show("CPF/CNPJ inválido.", "Cliente", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var ie = TxtIE.Text?.Trim();
                var isentoIE = ChkIsentoIE.IsChecked == true ? 1 : 0;
                var nascimento = DtNascimento.SelectedDate?.ToString("yyyy-MM-dd") ?? null;
                var telefone = TxtTelefone.Text?.Trim();
                var email = TxtEmail.Text?.Trim();

                decimal limiteCredito = 0;
                if (!string.IsNullOrWhiteSpace(TxtLimiteCredito.Text))
                {
                    var raw = TxtLimiteCredito.Text.Replace("R$", "");
                    decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out limiteCredito);
                }

                var rua = TxtRua.Text?.Trim();
                var numero = TxtNumero.Text?.Trim();
                var complemento = TxtComplemento.Text?.Trim();
                var bairro = TxtBairro.Text?.Trim();
                var cidade = TxtCidade.Text?.Trim();
                var estado = TxtEstado.Text?.Trim();
                var cep = TxtCep.Text?.Trim();

                if (string.IsNullOrWhiteSpace(cidade) || string.IsNullOrWhiteSpace(estado))
                {
                    MessageBox.Show("Informe Cidade e UF (Estado).", "Cliente", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!string.IsNullOrWhiteSpace(cep) && !CepMatchesUf(cep, estado))
                {
                    MessageBox.Show("CEP inconsistente com UF informada.", "Cliente", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        long customerId;
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"INSERT INTO Customers (Type, Name, FantasyName, CPF_CNPJ, IE, IsentoIE, BirthDate, Phone, Email, Street, Number, Complement, District, City, State, ZipCode, LimitCredit)
                                                VALUES ($type,$name,$fantasy,$cpfcnpj,$ie,$isento,$birth,$phone,$email,$street,$number,$complement,$district,$city,$state,$zip,$limit);";
                            cmd.Parameters.AddWithValue("$type", tipo);
                            cmd.Parameters.AddWithValue("$name", nome);
                            cmd.Parameters.AddWithValue("$fantasy", string.IsNullOrWhiteSpace(nomeFantasia) ? (object)DBNull.Value : nomeFantasia);
                            cmd.Parameters.AddWithValue("$cpfcnpj", string.IsNullOrWhiteSpace(doc) ? (object)DBNull.Value : doc);
                            cmd.Parameters.AddWithValue("$ie", string.IsNullOrWhiteSpace(ie) ? (object)DBNull.Value : ie);
                            cmd.Parameters.AddWithValue("$isento", isentoIE);
                            cmd.Parameters.AddWithValue("$birth", string.IsNullOrWhiteSpace(nascimento) ? (object)DBNull.Value : nascimento);
                            cmd.Parameters.AddWithValue("$phone", string.IsNullOrWhiteSpace(telefone) ? (object)DBNull.Value : telefone);
                            cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(email) ? (object)DBNull.Value : email);
                            cmd.Parameters.AddWithValue("$street", string.IsNullOrWhiteSpace(rua) ? (object)DBNull.Value : rua);
                            cmd.Parameters.AddWithValue("$number", string.IsNullOrWhiteSpace(numero) ? (object)DBNull.Value : numero);
                            cmd.Parameters.AddWithValue("$complement", string.IsNullOrWhiteSpace(complemento) ? (object)DBNull.Value : complemento);
                            cmd.Parameters.AddWithValue("$district", string.IsNullOrWhiteSpace(bairro) ? (object)DBNull.Value : bairro);
                            cmd.Parameters.AddWithValue("$city", string.IsNullOrWhiteSpace(cidade) ? (object)DBNull.Value : cidade);
                            cmd.Parameters.AddWithValue("$state", string.IsNullOrWhiteSpace(estado) ? (object)DBNull.Value : estado);
                            cmd.Parameters.AddWithValue("$zip", string.IsNullOrWhiteSpace(cep) ? (object)DBNull.Value : cep);
                            cmd.Parameters.AddWithValue("$limit", limiteCredito);
                            cmd.ExecuteNonQuery();
                        }
                        using (var idCmd = conn.CreateCommand())
                        {
                            idCmd.CommandText = "SELECT last_insert_rowid();";
                            customerId = Convert.ToInt64(idCmd.ExecuteScalar());
                        }

                        foreach (var addr in Addresses)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"INSERT INTO CustomerAddresses (CustomerId, AddressType, Street, Number, Complement, District, City, State, ZipCode)
                                                    VALUES ($cid,$type,$street,$number,$complement,$district,$city,$state,$zip);";
                                cmd.Parameters.AddWithValue("$cid", customerId);
                                cmd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(addr.AddressType) ? (object)DBNull.Value : addr.AddressType);
                                cmd.Parameters.AddWithValue("$street", string.IsNullOrWhiteSpace(addr.Street) ? (object)DBNull.Value : addr.Street);
                                cmd.Parameters.AddWithValue("$number", string.IsNullOrWhiteSpace(addr.Number) ? (object)DBNull.Value : addr.Number);
                                cmd.Parameters.AddWithValue("$complement", string.IsNullOrWhiteSpace(addr.Complement) ? (object)DBNull.Value : addr.Complement);
                                cmd.Parameters.AddWithValue("$district", string.IsNullOrWhiteSpace(addr.District) ? (object)DBNull.Value : addr.District);
                                cmd.Parameters.AddWithValue("$city", string.IsNullOrWhiteSpace(addr.City) ? (object)DBNull.Value : addr.City);
                                cmd.Parameters.AddWithValue("$state", string.IsNullOrWhiteSpace(addr.State) ? (object)DBNull.Value : addr.State);
                                cmd.Parameters.AddWithValue("$zip", string.IsNullOrWhiteSpace(addr.ZipCode) ? (object)DBNull.Value : addr.ZipCode);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        tx.Commit();
                    }
                }

                MessageBox.Show("Cliente salvo com sucesso.", "Cliente", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar cliente: {ex.Message}", "Cliente", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CepMatchesUf(string cepRaw, string uf)
        {
            var digits = new string((cepRaw ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length < 5 || string.IsNullOrWhiteSpace(uf)) return true; // não bloqueia se CEP muito curto
            int prefix2 = int.Parse(digits.Substring(0, 2));
            int prefix3 = int.Parse(digits.Substring(0, Math.Min(3, digits.Length)));
            switch ((uf ?? string.Empty).ToUpperInvariant())
            {
                case "SP": return prefix2 >= 1 && prefix2 <= 19;
                case "RJ": return prefix2 >= 20 && prefix2 <= 28;
                case "ES": return prefix2 == 29;
                case "MG": return prefix2 >= 30 && prefix2 <= 39;
                case "BA": return prefix2 >= 40 && prefix2 <= 48;
                case "SE": return prefix2 == 49;
                case "PE": return prefix2 >= 50 && prefix2 <= 56;
                case "AL": return prefix2 == 57;
                case "PB": return prefix2 == 58;
                case "RN": return prefix2 == 59;
                case "CE": return prefix2 >= 60 && prefix2 <= 63;
                case "PI": return prefix2 == 64;
                case "MA": return prefix2 == 65;
                case "PA": return (prefix2 >= 66 && prefix2 <= 68) && prefix3 != 689;
                case "AP": return prefix3 == 689;
                case "RR": return prefix3 == 693;
                case "AM": return (prefix3 >= 690 && prefix3 <= 692) || (prefix3 >= 694 && prefix3 <= 698);
                case "AC": return prefix3 == 699;
                case "DF": return prefix2 >= 70 && prefix2 <= 73;
                case "GO": return (prefix2 >= 74 && prefix2 <= 76) || (prefix3 >= 728 && prefix3 <= 767);
                case "RO": return prefix3 >= 768 && prefix3 <= 769;
                case "TO": return prefix3 >= 770 && prefix3 <= 779;
                case "MT": return prefix3 >= 780 && prefix3 <= 788;
                case "MS": return prefix3 >= 790 && prefix3 <= 799;
                case "PR": return prefix2 >= 80 && prefix2 <= 87;
                case "SC": return prefix2 >= 88 && prefix2 <= 89;
                case "RS": return prefix2 >= 90 && prefix2 <= 99;
                default: return true;
            }
        }
        private bool ValidateCpf(string cpf)
        {
            // rejeita sequências iguais
            if (new string(cpf[0], cpf.Length) == cpf) return false;
            int[] d = new int[11];
            for (int i = 0; i < 11; i++) d[i] = cpf[i] - '0';
            int sum = 0;
            for (int i = 0; i < 9; i++) sum += d[i] * (10 - i);
            int r = sum % 11; int dv1 = r < 2 ? 0 : 11 - r;
            if (d[9] != dv1) return false;
            sum = 0;
            for (int i = 0; i < 10; i++) sum += d[i] * (11 - i);
            r = sum % 11; int dv2 = r < 2 ? 0 : 11 - r;
            return d[10] == dv2;
        }
        private bool ValidateCnpj(string cnpj)
        {
            if (new string(cnpj[0], cnpj.Length) == cnpj) return false;
            int[] d = new int[14];
            for (int i = 0; i < 14; i++) d[i] = cnpj[i] - '0';
            int[] w1 = {5,4,3,2,9,8,7,6,5,4,3,2};
            int sum = 0; for (int i = 0; i < 12; i++) sum += d[i] * w1[i];
            int r = sum % 11; int dv1 = r < 2 ? 0 : 11 - r; if (d[12] != dv1) return false;
            int[] w2 = {6,5,4,3,2,9,8,7,6,5,4,3,2};
            sum = 0; for (int i = 0; i < 13; i++) sum += d[i] * w2[i];
            r = sum % 11; int dv2 = r < 2 ? 0 : 11 - r; return d[13] == dv2;
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }
    }

    public class AddressEntry
    {
        public string? AddressType { get; set; }
        public string? Street { get; set; }
        public string? Number { get; set; }
        public string? Complement { get; set; }
        public string? District { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
    }
}