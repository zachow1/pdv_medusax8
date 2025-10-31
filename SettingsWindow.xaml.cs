using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Printing;
using System.IO;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Models;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class SettingsWindow : Window
    {
        private List<PaymentMethod> _methods = new List<PaymentMethod>();
        public List<string> LinkTypes { get; } = new List<string> { "VENDA", "FISCAL", "FINANCEIRO", "OUTROS" };

        public static bool EnableBlindCashClosure
        {
            get
            {
                using (var conn = new SqliteConnection(GetStaticConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "EnableBlindCashClosure");
                        var obj = cmd.ExecuteScalar();
                        string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        bool enableBlind = false;
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            enableBlind = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        return enableBlind;
                    }
                }
            }
        }

        private static string GetStaticConnectionString()
        {
            return "Data Source=config.db";
        }

        private Point? _dragStartPoint = null;
        private PaymentMethod? _dragSourceItem = null;

        private string? _currentCertThumb;
        private string _certStoreLocation = "CurrentUser";

        public SettingsWindow()
        {
            InitializeComponent();
            this.Loaded += SettingsWindow_Loaded;
            this.SizeChanged += SettingsWindow_SizeChanged;
            LoadPaymentMethods();
            // Aplicar estado persistido de edição NFC-e na sessão
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key='NFCeEditEnabled' LIMIT 1;";
                var obj = cmd.ExecuteScalar();
                var s = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                Services.SessionManager.NFCeEditEnabled = (!string.IsNullOrWhiteSpace(s)) && (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase));
            }
            catch { Services.SessionManager.NFCeEditEnabled = false; }
            LoadNFCeConfig();
            LoadOptions();
            LoadGeneralOptions();
            LoadTEFConfig();
            LoadProdutoFacilConfig();
            LoadLogoSettings();

#if DEBUG
            try { Services.NFCeEditPolicy policy = new Services.NFCeEditPolicy(); var pFalse = policy.GetPolicy(false); var pTrue = policy.GetPolicy(true); System.Diagnostics.Debug.Assert(pFalse.CanEditEnvironment == false && pFalse.CanEditCashRegister == false && pFalse.CanEditCSC == false); System.Diagnostics.Debug.Assert(pTrue.CanEditEnvironment == true && pTrue.CanEditCashRegister == true && pTrue.CanEditCSC == false); } catch { }
#endif
        }

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                double workW = SystemParameters.WorkArea.Width;
                double workH = SystemParameters.WorkArea.Height;

                // Ajuste inicial proporcional à resolução (mantém visível em telas menores)
                double targetW = Math.Min(800, workW * 0.9);
                double targetH = Math.Min(600, workH * 0.9);

                this.MaxWidth = workW;
                this.MaxHeight = workH;
                this.Width = targetW;
                this.Height = targetH;

                double left = SystemParameters.WorkArea.Left + (workW - targetW) / 2;
                double top = SystemParameters.WorkArea.Top + (workH - targetH) / 2;
                this.Left = left;
                this.Top = top;
            }
            catch { }

            // Carrega impressoras (locais e conexões de rede) e aplica seleção salva
            LoadPrinters();
            LoadPrinterSettings();

            // Carregar dados de Empresa/Contador para os grids da aba Empresa
            try { LoadEmpresaAndContadorFromDb(); } catch { }

            // Aplicar estado de edição inicial dos dados do contador (sempre habilitado)
            try
            {
                ApplyContadorEditState(true);
            }
            catch { }

#if DEBUG
            try { RunSystemSmokeTest(); } catch { }
#endif
        }

        private void SettingsWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            try
            {
                double w = e.NewSize.Width;
                // Mantém tamanho base fixo; reduz apenas em telas pequenas
                double baseFont = 12;
                if (w < 800) baseFont = 11;
                if (w < 700) baseFont = 10;
                this.Resources["BaseFontSize"] = baseFont;

                // Ajusta padding levemente para caber melhor em telas menores
                var pad = w < 800 ? new Thickness(8, 8, 8, 8) : new Thickness(10, 10, 10, 10);
                this.Resources["BasePadding"] = pad;
            }
            catch { }
        }

        private string GetConnectionString()
        {
            return DbHelper.GetConnectionString();
        }

        // --- Empresa / Contador: carregar e salvar ---
        private void LoadEmpresaAndContadorFromDb()
        {
            using var conn = new SqliteConnection(GetConnectionString());
            conn.Open();

            // Empresa
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, RazaoSocial, NomeFantasia, CNPJ, IE, IM, RegimeTributario, CNAE, Email, Telefone, Website, CEP, Logradouro, Numero, Complemento, Bairro, MunicipioCodigo, MunicipioNome, UF FROM Empresa LIMIT 1";
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    (FindName("TxtEmpresaRazaoSocial") as TextBox)!.Text = rd.IsDBNull(1) ? string.Empty : rd.GetString(1);
                    (FindName("TxtEmpresaNomeFantasia") as TextBox)!.Text = rd.IsDBNull(2) ? string.Empty : rd.GetString(2);
                    (FindName("TxtEmpresaCNPJ") as TextBox)!.Text = rd.IsDBNull(3) ? string.Empty : rd.GetString(3);
                    (FindName("TxtEmpresaIE") as TextBox)!.Text = rd.IsDBNull(4) ? string.Empty : rd.GetString(4);
                    (FindName("TxtEmpresaIM") as TextBox)!.Text = rd.IsDBNull(5) ? string.Empty : rd.GetString(5);
                    var _crtVal = rd.IsDBNull(6) ? string.Empty : rd.GetString(6);
                    var _crtCmb = FindName("TxtEmpresaRegimeTributario") as ComboBox;
                    if (_crtCmb != null)
                    {
                        // Tenta selecionar um item igual; se não houver, apenas define o texto (IsEditable)
                        bool matched = false;
                        foreach (var it in _crtCmb.Items)
                        {
                            if (it is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), _crtVal, StringComparison.OrdinalIgnoreCase))
                            {
                                _crtCmb.SelectedItem = cbi;
                                matched = true;
                                break;
                            }
                        }
                        if (!matched)
                        {
                            _crtCmb.Text = _crtVal ?? string.Empty;
                        }
                    }
                    (FindName("TxtEmpresaCNAE") as TextBox)!.Text = rd.IsDBNull(7) ? string.Empty : rd.GetString(7);
                    (FindName("TxtEmpresaEmail") as TextBox)!.Text = rd.IsDBNull(8) ? string.Empty : rd.GetString(8);
                    (FindName("TxtEmpresaTelefone") as TextBox)!.Text = rd.IsDBNull(9) ? string.Empty : rd.GetString(9);
                    (FindName("TxtEmpresaWebsite") as TextBox)!.Text = rd.IsDBNull(10) ? string.Empty : rd.GetString(10);
                    (FindName("TxtEmpresaCEP") as TextBox)!.Text = rd.IsDBNull(11) ? string.Empty : rd.GetString(11);
                    (FindName("TxtEmpresaLogradouro") as TextBox)!.Text = rd.IsDBNull(12) ? string.Empty : rd.GetString(12);
                    (FindName("TxtEmpresaNumero") as TextBox)!.Text = rd.IsDBNull(13) ? string.Empty : rd.GetString(13);
                    (FindName("TxtEmpresaComplemento") as TextBox)!.Text = rd.IsDBNull(14) ? string.Empty : rd.GetString(14);
                    (FindName("TxtEmpresaBairro") as TextBox)!.Text = rd.IsDBNull(15) ? string.Empty : rd.GetString(15);
                    (FindName("TxtEmpresaMunicipioCodigo") as TextBox)!.Text = rd.IsDBNull(16) ? string.Empty : rd.GetString(16);
                    (FindName("TxtEmpresaMunicipioNome") as TextBox)!.Text = rd.IsDBNull(17) ? string.Empty : rd.GetString(17);
                    (FindName("TxtEmpresaUF") as TextBox)!.Text = rd.IsDBNull(18) ? string.Empty : rd.GetString(18);
                }
                else
                {
                    (FindName("TxtEmpresaRazaoSocial") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaNomeFantasia") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaCNPJ") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaIE") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaIM") as TextBox)!.Text = string.Empty;
                    var _crtCmb2 = FindName("TxtEmpresaRegimeTributario") as ComboBox;
                    if (_crtCmb2 != null) _crtCmb2.Text = string.Empty;
                    (FindName("TxtEmpresaCNAE") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaEmail") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaTelefone") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaWebsite") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaCEP") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaLogradouro") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaNumero") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaComplemento") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaBairro") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaMunicipioCodigo") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaMunicipioNome") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaUF") as TextBox)!.Text = string.Empty;
                }
            }

            // Contador
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Nome, CRC, CNPJ, CPF, Email, Telefone, Celular, CEP, Logradouro, Numero, Complemento, Bairro, MunicipioCodigo, MunicipioNome, UF, CRCEstado, CRCTipo FROM Contador LIMIT 1";
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    (FindName("TxtContadorNome") as TextBox)!.Text = rd.IsDBNull(1) ? string.Empty : rd.GetString(1);
                    (FindName("TxtContadorCRC") as TextBox)!.Text = rd.IsDBNull(2) ? string.Empty : rd.GetString(2);
                    (FindName("TxtContadorCPF") as TextBox)!.Text = rd.IsDBNull(4) ? string.Empty : rd.GetString(4);
                    (FindName("TxtContadorEmail") as TextBox)!.Text = rd.IsDBNull(5) ? string.Empty : rd.GetString(5);
                    (FindName("TxtContadorTelefone") as TextBox)!.Text = rd.IsDBNull(6) ? string.Empty : rd.GetString(6);
                    (FindName("TxtContadorCelular") as TextBox)!.Text = rd.IsDBNull(7) ? string.Empty : rd.GetString(7);
                    (FindName("TxtContadorCEP") as TextBox)!.Text = rd.IsDBNull(8) ? string.Empty : rd.GetString(8);
                    (FindName("TxtContadorLogradouro") as TextBox)!.Text = rd.IsDBNull(9) ? string.Empty : rd.GetString(9);
                    (FindName("TxtContadorNumero") as TextBox)!.Text = rd.IsDBNull(10) ? string.Empty : rd.GetString(10);
                    (FindName("TxtContadorComplemento") as TextBox)!.Text = rd.IsDBNull(11) ? string.Empty : rd.GetString(11);
                    (FindName("TxtContadorBairro") as TextBox)!.Text = rd.IsDBNull(12) ? string.Empty : rd.GetString(12);
                    (FindName("TxtContadorMunicipioCodigo") as TextBox)!.Text = rd.IsDBNull(13) ? string.Empty : rd.GetString(13);
                    (FindName("TxtContadorMunicipioNome") as TextBox)!.Text = rd.IsDBNull(14) ? string.Empty : rd.GetString(14);
                    (FindName("TxtContadorUF") as TextBox)!.Text = rd.IsDBNull(15) ? string.Empty : rd.GetString(15);

                    // Preencher Estado CRC e Tipo CRC na aba Dados do contador
                    var cmbEstadoCRCCont = (FindName("CmbContadorEstadoCRC") as ComboBox);
                    if (cmbEstadoCRCCont != null)
                    {
                        var est = rd.IsDBNull(16) ? string.Empty : rd.GetString(16);
                        cmbEstadoCRCCont.SelectedValue = est;
                        if (string.IsNullOrWhiteSpace(est)) cmbEstadoCRCCont.Text = string.Empty;
                    }
                    var cmbTipoCRCCont = (FindName("CmbContadorCRCTipo") as ComboBox);
                    if (cmbTipoCRCCont != null)
                    {
                        var tipo = rd.IsDBNull(17) ? string.Empty : rd.GetString(17);
                        cmbTipoCRCCont.SelectedValue = tipo;
                        if (string.IsNullOrWhiteSpace(tipo)) cmbTipoCRCCont.Text = string.Empty;
                    }

                    // Espelhar dados na aba Empresa Contábil
                    (FindName("TxtEmpresaContabilNome") as TextBox)!.Text = rd.IsDBNull(1) ? string.Empty : rd.GetString(1);
                    (FindName("TxtEmpresaContabilCRC") as TextBox)!.Text = rd.IsDBNull(2) ? string.Empty : rd.GetString(2);
                    (FindName("TxtEmpresaContabilEmail") as TextBox)!.Text = rd.IsDBNull(5) ? string.Empty : rd.GetString(5);
                    (FindName("TxtEmpresaContabilTelefone") as TextBox)!.Text = rd.IsDBNull(6) ? string.Empty : rd.GetString(6);
                    (FindName("TxtEmpresaContabilCEP") as TextBox)!.Text = rd.IsDBNull(8) ? string.Empty : rd.GetString(8);
                    (FindName("TxtEmpresaContabilLogradouro") as TextBox)!.Text = rd.IsDBNull(9) ? string.Empty : rd.GetString(9);
                    (FindName("TxtEmpresaContabilNumero") as TextBox)!.Text = rd.IsDBNull(10) ? string.Empty : rd.GetString(10);
                    (FindName("TxtEmpresaContabilComplemento") as TextBox)!.Text = rd.IsDBNull(11) ? string.Empty : rd.GetString(11);
                    (FindName("TxtEmpresaContabilBairro") as TextBox)!.Text = rd.IsDBNull(12) ? string.Empty : rd.GetString(12);
                    (FindName("TxtEmpresaContabilMunicipioCodigo") as TextBox)!.Text = rd.IsDBNull(13) ? string.Empty : rd.GetString(13);
                    (FindName("TxtEmpresaContabilMunicipioNome") as TextBox)!.Text = rd.IsDBNull(14) ? string.Empty : rd.GetString(14);
                    var cmbUfEmpCont = (FindName("TxtEmpresaContabilUF") as ComboBox);
                    if (cmbUfEmpCont != null)
                    {
                        var ufEmp = rd.IsDBNull(15) ? string.Empty : rd.GetString(15);
                        cmbUfEmpCont.SelectedValue = ufEmp;
                        if (string.IsNullOrWhiteSpace(ufEmp)) cmbUfEmpCont.Text = string.Empty;
                    }
                    var cmbEstadoCRC = (FindName("CmbEmpresaContabilEstadoCRC") as ComboBox);
                    if (cmbEstadoCRC != null)
                    {
                        var est = rd.IsDBNull(16) ? string.Empty : rd.GetString(16);
                        cmbEstadoCRC.SelectedValue = est;
                        if (string.IsNullOrWhiteSpace(est)) cmbEstadoCRC.Text = string.Empty;
                    }
                    var cmbTipoCRC = (FindName("CmbEmpresaContabilCRCTipo") as ComboBox);
                    if (cmbTipoCRC != null)
                    {
                        var tipo = rd.IsDBNull(17) ? string.Empty : rd.GetString(17);
                        cmbTipoCRC.SelectedValue = tipo;
                        if (string.IsNullOrWhiteSpace(tipo)) cmbTipoCRC.Text = string.Empty;
                    }
                }
                else
                {
                    (FindName("TxtContadorNome") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorCRC") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorCPF") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorEmail") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorTelefone") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorCelular") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorCEP") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorLogradouro") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorNumero") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorComplemento") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorBairro") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorMunicipioCodigo") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorMunicipioNome") as TextBox)!.Text = string.Empty;
                    (FindName("TxtContadorUF") as TextBox)!.Text = string.Empty;

                    var cmbEstadoCRCCont2 = (FindName("CmbContadorEstadoCRC") as ComboBox);
                    if (cmbEstadoCRCCont2 != null) cmbEstadoCRCCont2.Text = string.Empty;
                    var cmbTipoCRCCont2 = (FindName("CmbContadorCRCTipo") as ComboBox);
                    if (cmbTipoCRCCont2 != null) cmbTipoCRCCont2.Text = string.Empty;

                    (FindName("TxtEmpresaContabilNome") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilCRC") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilEmail") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilTelefone") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilCEP") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilLogradouro") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilNumero") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilComplemento") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilBairro") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilMunicipioCodigo") as TextBox)!.Text = string.Empty;
                    (FindName("TxtEmpresaContabilMunicipioNome") as TextBox)!.Text = string.Empty;
                    var cmbUfEmpCont2 = (FindName("TxtEmpresaContabilUF") as ComboBox);
                    if (cmbUfEmpCont2 != null) cmbUfEmpCont2.Text = string.Empty;
                    var cmbEstadoCRC2 = (FindName("CmbEmpresaContabilEstadoCRC") as ComboBox);
                    if (cmbEstadoCRC2 != null) cmbEstadoCRC2.Text = string.Empty;
                    var cmbTipoCRC2 = (FindName("CmbEmpresaContabilCRCTipo") as ComboBox);
                    if (cmbTipoCRC2 != null) cmbTipoCRC2.Text = string.Empty;
                }
            }

            // Estado do checkbox Empresa Contábil (persistência em Settings)
            bool empresaContabilEnabled = true; // padrão: habilitado
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                cmd.Parameters.AddWithValue("$key", "EmpresaContabilEnabled");
                var obj = cmd.ExecuteScalar();
                var s = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    empresaContabilEnabled = (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase));
                }
            }
            var chk = (FindName("ChkEmpresaContabil") as CheckBox)!;
            chk.IsChecked = empresaContabilEnabled;
            var tabEmpCont = (FindName("TabEmpresaContabil") as System.Windows.Controls.TabItem);
            if (tabEmpCont != null) tabEmpCont.Visibility = empresaContabilEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveEmpresa_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rs = (FindName("TxtEmpresaRazaoSocial") as TextBox)!.Text?.Trim();
                var nf = (FindName("TxtEmpresaNomeFantasia") as TextBox)!.Text?.Trim();
                var cnpj = (FindName("TxtEmpresaCNPJ") as TextBox)!.Text?.Trim();
                var ie = (FindName("TxtEmpresaIE") as TextBox)!.Text?.Trim();
                var im = (FindName("TxtEmpresaIM") as TextBox)!.Text?.Trim();
                string? crt = null;
                var _cmbCrt = FindName("TxtEmpresaRegimeTributario") as ComboBox;
                if (_cmbCrt != null)
                {
                    crt = _cmbCrt.SelectedValue?.ToString();
                    if (string.IsNullOrWhiteSpace(crt)) crt = _cmbCrt.Text?.Trim();
                }
                var cnae = (FindName("TxtEmpresaCNAE") as TextBox)!.Text?.Trim();
                var email = (FindName("TxtEmpresaEmail") as TextBox)!.Text?.Trim();
                var tel = (FindName("TxtEmpresaTelefone") as TextBox)!.Text?.Trim();
                var web = (FindName("TxtEmpresaWebsite") as TextBox)!.Text?.Trim();
                var cep = (FindName("TxtEmpresaCEP") as TextBox)!.Text?.Trim();
                var log = (FindName("TxtEmpresaLogradouro") as TextBox)!.Text?.Trim();
                var num = (FindName("TxtEmpresaNumero") as TextBox)!.Text?.Trim();
                var comp = (FindName("TxtEmpresaComplemento") as TextBox)!.Text?.Trim();
                var bai = (FindName("TxtEmpresaBairro") as TextBox)!.Text?.Trim();
                var munCod = (FindName("TxtEmpresaMunicipioCodigo") as TextBox)!.Text?.Trim();
                var munNome = (FindName("TxtEmpresaMunicipioNome") as TextBox)!.Text?.Trim();
                string? uf = null;
                var cmbUfEmpresa = FindName("TxtEmpresaUF") as ComboBox;
                if (cmbUfEmpresa != null)
                {
                    uf = cmbUfEmpresa.SelectedValue?.ToString();
                    if (string.IsNullOrWhiteSpace(uf)) uf = cmbUfEmpresa.Text?.Trim();
                }

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Empresa (Id, RazaoSocial, NomeFantasia, CNPJ, IE, IM, RegimeTributario, CNAE, Email, Telefone, Website, CEP, Logradouro, Numero, Complemento, Bairro, MunicipioCodigo, MunicipioNome, UF)
                    VALUES ($id,$rs,$nf,$cnpj,$ie,$im,$crt,$cnae,$email,$tel,$web,$cep,$log,$num,$comp,$bai,$munCod,$munNome,$uf)
                    ON CONFLICT(Id) DO UPDATE SET
                        RazaoSocial=excluded.RazaoSocial,
                        NomeFantasia=excluded.NomeFantasia,
                        CNPJ=excluded.CNPJ,
                        IE=excluded.IE,
                        IM=excluded.IM,
                        RegimeTributario=excluded.RegimeTributario,
                        CNAE=excluded.CNAE,
                        Email=excluded.Email,
                        Telefone=excluded.Telefone,
                        Website=excluded.Website,
                        CEP=excluded.CEP,
                        Logradouro=excluded.Logradouro,
                        Numero=excluded.Numero,
                        Complemento=excluded.Complemento,
                        Bairro=excluded.Bairro,
                        MunicipioCodigo=excluded.MunicipioCodigo,
                        MunicipioNome=excluded.MunicipioNome,
                        UF=excluded.UF;";
                cmd.Parameters.AddWithValue("$id", 1);
                cmd.Parameters.AddWithValue("$rs", string.IsNullOrWhiteSpace(rs) ? (object)DBNull.Value : rs);
                cmd.Parameters.AddWithValue("$nf", string.IsNullOrWhiteSpace(nf) ? (object)DBNull.Value : nf);
                cmd.Parameters.AddWithValue("$cnpj", string.IsNullOrWhiteSpace(cnpj) ? (object)DBNull.Value : OnlyDigits(cnpj));
                cmd.Parameters.AddWithValue("$ie", string.IsNullOrWhiteSpace(ie) ? (object)DBNull.Value : ie);
                cmd.Parameters.AddWithValue("$im", string.IsNullOrWhiteSpace(im) ? (object)DBNull.Value : im);
                cmd.Parameters.AddWithValue("$crt", string.IsNullOrWhiteSpace(crt) ? (object)DBNull.Value : crt);
                cmd.Parameters.AddWithValue("$cnae", string.IsNullOrWhiteSpace(cnae) ? (object)DBNull.Value : cnae);
                cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(email) ? (object)DBNull.Value : email);
                cmd.Parameters.AddWithValue("$tel", string.IsNullOrWhiteSpace(tel) ? (object)DBNull.Value : OnlyDigits(tel));
                cmd.Parameters.AddWithValue("$web", string.IsNullOrWhiteSpace(web) ? (object)DBNull.Value : web);
                cmd.Parameters.AddWithValue("$cep", string.IsNullOrWhiteSpace(cep) ? (object)DBNull.Value : OnlyDigits(cep));
                cmd.Parameters.AddWithValue("$log", string.IsNullOrWhiteSpace(log) ? (object)DBNull.Value : log);
                cmd.Parameters.AddWithValue("$num", string.IsNullOrWhiteSpace(num) ? (object)DBNull.Value : OnlyDigits(num));
                cmd.Parameters.AddWithValue("$comp", string.IsNullOrWhiteSpace(comp) ? (object)DBNull.Value : comp);
                cmd.Parameters.AddWithValue("$bai", string.IsNullOrWhiteSpace(bai) ? (object)DBNull.Value : bai);
                cmd.Parameters.AddWithValue("$munCod", string.IsNullOrWhiteSpace(munCod) ? (object)DBNull.Value : OnlyDigits(munCod));
                cmd.Parameters.AddWithValue("$munNome", string.IsNullOrWhiteSpace(munNome) ? (object)DBNull.Value : munNome);
                cmd.Parameters.AddWithValue("$uf", string.IsNullOrWhiteSpace(uf) ? (object)DBNull.Value : uf);
                cmd.ExecuteNonQuery();

                MessageBox.Show("Empresa salva.", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar empresa: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveContador_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nome = (FindName("TxtContadorNome") as TextBox)!.Text?.Trim();
                var crc = (FindName("TxtContadorCRC") as TextBox)!.Text?.Trim();
                var cnpj = string.Empty; // campo CNPJ removido da UI
                var cpf = (FindName("TxtContadorCPF") as TextBox)!.Text?.Trim();
                var email = (FindName("TxtContadorEmail") as TextBox)!.Text?.Trim();
                var tel = (FindName("TxtContadorTelefone") as TextBox)!.Text?.Trim();
                var cel = (FindName("TxtContadorCelular") as TextBox)!.Text?.Trim();
                var cep = (FindName("TxtContadorCEP") as TextBox)!.Text?.Trim();
                var log = (FindName("TxtContadorLogradouro") as TextBox)!.Text?.Trim();
                var num = (FindName("TxtContadorNumero") as TextBox)!.Text?.Trim();
                var comp = (FindName("TxtContadorComplemento") as TextBox)!.Text?.Trim();
                var bai = (FindName("TxtContadorBairro") as TextBox)!.Text?.Trim();
                var munCod = (FindName("TxtContadorMunicipioCodigo") as TextBox)!.Text?.Trim();
                var munNome = (FindName("TxtContadorMunicipioNome") as TextBox)!.Text?.Trim();
                string? uf = null;
                var cmbUfCont = FindName("TxtContadorUF") as ComboBox;
                if (cmbUfCont != null)
                {
                    uf = cmbUfCont.SelectedValue?.ToString();
                    if (string.IsNullOrWhiteSpace(uf)) uf = cmbUfCont.Text?.Trim();
                }

                // Campos da aba Dados do contador (Estado CRC / Tipo)
                var cmbEstadoCRC = FindName("CmbContadorEstadoCRC") as ComboBox;
                string? crcEstado = null;
                if (cmbEstadoCRC != null)
                {
                    crcEstado = cmbEstadoCRC.SelectedValue?.ToString();
                    if (string.IsNullOrWhiteSpace(crcEstado)) crcEstado = cmbEstadoCRC.Text?.Trim();
                }
                var crcTipoCmb = FindName("CmbContadorCRCTipo") as ComboBox;
                string? crcTipo = null;
                if (crcTipoCmb != null)
                {
                    crcTipo = crcTipoCmb.SelectedValue?.ToString();
                    if (string.IsNullOrWhiteSpace(crcTipo)) crcTipo = crcTipoCmb.Text?.Trim();
                }

                // Obrigatoriedade quando Empresa Contábil estiver habilitado (campos sempre editáveis)
                var chkEmpCont = FindName("ChkEmpresaContabil") as CheckBox;
                bool empresaContabilEnabled = chkEmpCont?.IsChecked == true;
                if (empresaContabilEnabled)
                {
                    var missing = new List<string>();
                    if (string.IsNullOrWhiteSpace(nome)) missing.Add("Nome");
                    if (string.IsNullOrWhiteSpace(crc)) missing.Add("CRC");
                    if (string.IsNullOrWhiteSpace(crcEstado)) missing.Add("Estado (CRC)");
                    if (string.IsNullOrWhiteSpace(crcTipo)) missing.Add("Tipo CRC");
                    if (missing.Count > 0)
                    {
                        MessageBox.Show($"Preencha os campos obrigatórios: {string.Join(", ", missing)}.", "Dados do contador", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Contador (Id, Nome, CRC, CNPJ, CPF, Email, Telefone, Celular, CEP, Logradouro, Numero, Complemento, Bairro, MunicipioCodigo, MunicipioNome, UF, CRCEstado, CRCTipo)
                    VALUES ($id,$nome,$crc,$cnpj,$cpf,$email,$tel,$cel,$cep,$log,$num,$comp,$bai,$munCod,$munNome,$uf,$crcEstado,$crcTipo)
                    ON CONFLICT(Id) DO UPDATE SET
                        Nome=excluded.Nome,
                        CRC=excluded.CRC,
                        CNPJ=excluded.CNPJ,
                        CPF=excluded.CPF,
                        Email=excluded.Email,
                        Telefone=excluded.Telefone,
                        Celular=excluded.Celular,
                        CEP=excluded.CEP,
                        Logradouro=excluded.Logradouro,
                        Numero=excluded.Numero,
                        Complemento=excluded.Complemento,
                        Bairro=excluded.Bairro,
                        MunicipioCodigo=excluded.MunicipioCodigo,
                        MunicipioNome=excluded.MunicipioNome,
                        UF=excluded.UF,
                        CRCEstado=excluded.CRCEstado,
                        CRCTipo=excluded.CRCTipo;";
                cmd.Parameters.AddWithValue("$id", 1);
                cmd.Parameters.AddWithValue("$nome", string.IsNullOrWhiteSpace(nome) ? (object)DBNull.Value : nome);
                cmd.Parameters.AddWithValue("$crc", string.IsNullOrWhiteSpace(crc) ? (object)DBNull.Value : crc);
                cmd.Parameters.AddWithValue("$cnpj", string.IsNullOrWhiteSpace(cnpj) ? (object)DBNull.Value : OnlyDigits(cnpj));
                cmd.Parameters.AddWithValue("$cpf", string.IsNullOrWhiteSpace(cpf) ? (object)DBNull.Value : OnlyDigits(cpf));
                cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(email) ? (object)DBNull.Value : email);
                cmd.Parameters.AddWithValue("$tel", string.IsNullOrWhiteSpace(tel) ? (object)DBNull.Value : OnlyDigits(tel));
                cmd.Parameters.AddWithValue("$cel", string.IsNullOrWhiteSpace(cel) ? (object)DBNull.Value : OnlyDigits(cel));
                cmd.Parameters.AddWithValue("$cep", string.IsNullOrWhiteSpace(cep) ? (object)DBNull.Value : OnlyDigits(cep));
                cmd.Parameters.AddWithValue("$log", string.IsNullOrWhiteSpace(log) ? (object)DBNull.Value : log);
                cmd.Parameters.AddWithValue("$num", string.IsNullOrWhiteSpace(num) ? (object)DBNull.Value : OnlyDigits(num));
                cmd.Parameters.AddWithValue("$comp", string.IsNullOrWhiteSpace(comp) ? (object)DBNull.Value : comp);
                cmd.Parameters.AddWithValue("$bai", string.IsNullOrWhiteSpace(bai) ? (object)DBNull.Value : bai);
                cmd.Parameters.AddWithValue("$munCod", string.IsNullOrWhiteSpace(munCod) ? (object)DBNull.Value : OnlyDigits(munCod));
                cmd.Parameters.AddWithValue("$munNome", string.IsNullOrWhiteSpace(munNome) ? (object)DBNull.Value : munNome);
                cmd.Parameters.AddWithValue("$uf", string.IsNullOrWhiteSpace(uf) ? (object)DBNull.Value : uf);
                cmd.Parameters.AddWithValue("$crcEstado", string.IsNullOrWhiteSpace(crcEstado) ? (object)DBNull.Value : crcEstado);
                cmd.Parameters.AddWithValue("$crcTipo", string.IsNullOrWhiteSpace(crcTipo) ? (object)DBNull.Value : crcTipo);
                cmd.ExecuteNonQuery();

                MessageBox.Show("Contador salvo.", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar contador: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyContadorEditState(bool enabled)
        {
            try
            {
                // Campos de Dados do contador
                (FindName("TxtContadorNome") as TextBox)!.IsReadOnly = !enabled;
                (FindName("TxtContadorCRC") as TextBox)!.IsReadOnly = !enabled;
                (FindName("TxtContadorCPF") as TextBox)!.IsReadOnly = !enabled;
                (FindName("TxtContadorEmail") as TextBox)!.IsReadOnly = !enabled;
                (FindName("TxtContadorTelefone") as TextBox)!.IsReadOnly = !enabled;
                var cmbEst = (FindName("CmbContadorEstadoCRC") as ComboBox);
                if (cmbEst != null) cmbEst.IsEnabled = enabled;
                var cmbTipo = (FindName("CmbContadorCRCTipo") as ComboBox);
                if (cmbTipo != null) cmbTipo.IsEnabled = enabled;

                // Botão removido - campos sempre habilitados
            }
            catch { }
        }



#if DEBUG
        private void RunSystemSmokeTest()
        {
            try
            {
                var dbPath = Services.DbHelper.GetDbPath();
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();

                int settingsCount = 0;
                int empresaCount = 0;
                int contadorCount = 0;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Settings;";
                    var obj = cmd.ExecuteScalar();
                    settingsCount = (obj == null || obj == DBNull.Value) ? 0 : Convert.ToInt32(obj);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Empresa;";
                    var obj = cmd.ExecuteScalar();
                    empresaCount = (obj == null || obj == DBNull.Value) ? 0 : Convert.ToInt32(obj);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Contador;";
                    var obj = cmd.ExecuteScalar();
                    contadorCount = (obj == null || obj == DBNull.Value) ? 0 : Convert.ToInt32(obj);
                }

                // Upsert um valor de teste em Settings
                SaveOrUpdateSetting(conn, "SmokeTestTimestamp", DateTime.Now.ToString("s"));

                MessageBox.Show(
                    $"Teste de sistema\n\n" +
                    $"Banco atual: {dbPath}\n" +
                    $"Settings: {settingsCount} registro(s)\n" +
                    $"Empresa: {empresaCount} registro(s)\n" +
                    $"Contador: {contadorCount} registro(s)",
                    "Smoke Test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha no teste de sistema: " + ex.Message, "Smoke Test", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
#endif

        private void ChkEmpresaContabil_Checked(object sender, RoutedEventArgs e)
        {
            var tab = FindName("TabEmpresaContabil") as System.Windows.Controls.TabItem;
            if (tab != null) tab.Visibility = Visibility.Visible;
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "EmpresaContabilEnabled", "1");
            }
            catch { }
        }

        private void ChkEmpresaContabil_Unchecked(object sender, RoutedEventArgs e)
        {
            var tab = FindName("TabEmpresaContabil") as System.Windows.Controls.TabItem;
            if (tab != null) tab.Visibility = Visibility.Collapsed;
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "EmpresaContabilEnabled", "0");
            }
            catch { }
        }

        private void SelectEmpresaMunicipio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new MunicipioSearchWindow();
                win.Owner = this;
                var ok = win.ShowDialog();
                if (ok == true && win.SelectedMunicipio != null)
                {
                    (FindName("TxtEmpresaMunicipioNome") as TextBox)!.Text = win.SelectedMunicipio.Nome;
                    var cmbUf = FindName("TxtEmpresaUF") as ComboBox;
                    if (cmbUf != null)
                    {
                        // Tenta selecionar o item; caso não exista, define como texto
                        cmbUf.SelectedValue = win.SelectedMunicipio.UF;
                        if (!Equals(cmbUf.SelectedValue?.ToString(), win.SelectedMunicipio.UF))
                        {
                            cmbUf.Text = win.SelectedMunicipio.UF;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(win.SelectedMunicipio.Codigo))
                    {
                        (FindName("TxtEmpresaMunicipioCodigo") as TextBox)!.Text = win.SelectedMunicipio.Codigo;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao buscar município: " + ex.Message, "Municípios", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectContadorMunicipio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new MunicipioSearchWindow();
                win.Owner = this;
                var ok = win.ShowDialog();
                if (ok == true && win.SelectedMunicipio != null)
                {
                    (FindName("TxtContadorMunicipioNome") as TextBox)!.Text = win.SelectedMunicipio.Nome;
                    var cmbUf = FindName("TxtContadorUF") as ComboBox;
                    if (cmbUf != null)
                    {
                        cmbUf.SelectedValue = win.SelectedMunicipio.UF;
                        if (!Equals(cmbUf.SelectedValue?.ToString(), win.SelectedMunicipio.UF))
                        {
                            cmbUf.Text = win.SelectedMunicipio.UF;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(win.SelectedMunicipio.Codigo))
                    {
                        (FindName("TxtContadorMunicipioCodigo") as TextBox)!.Text = win.SelectedMunicipio.Codigo;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao buscar município: " + ex.Message, "Municípios", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPaymentMethods()
        {
            _methods.Clear();
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, Code, Name, IsEnabled, DisplayOrder, LinkType FROM PaymentMethods ORDER BY DisplayOrder, Name;";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                _methods.Add(new PaymentMethod
                                {
                                    Id = reader.GetInt32(0),
                                    Code = reader.GetString(1),
                                    Name = reader.GetString(2),
                                    IsEnabled = reader.GetInt32(3) == 1,
                                    DisplayOrder = reader.GetInt32(4),
                                    LinkType = reader.IsDBNull(5) ? null : reader.GetString(5)
                                });
                            }
                        }
                    }

                    DgPaymentMethods.ItemsSource = null;
                    DgPaymentMethods.ItemsSource = _methods;
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(DgPaymentMethods.ItemsSource);
                    view.SortDescriptions.Clear();
                    view.SortDescriptions.Add(new SortDescription(nameof(PaymentMethod.DisplayOrder), ListSortDirection.Ascending));
                    view.Refresh();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar formas de pagamento: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadNFCeConfig()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();

                    // Carrega ConfiguracoesNFCe (Id=1)
                    int tpAmb = 2; // 2=Homolog
                    string? cscId = null;
                    string? csc = null;
                    int serie = 1;
                    int next = 1;

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT TpAmb, CSCId, CSC, Serie, ProximoNumero FROM ConfiguracoesNFCe WHERE Id=1;";
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                tpAmb = r.IsDBNull(0) ? 2 : r.GetInt32(0);
                                cscId = r.IsDBNull(1) ? null : r.GetString(1);
                                csc = r.IsDBNull(2) ? null : r.GetString(2);
                                serie = r.IsDBNull(3) ? 1 : r.GetInt32(3);
                                next = r.IsDBNull(4) ? 1 : r.GetInt32(4);
                            }
                        }
                    }

                    // Carrega número do caixa (Settings)
                    int cash = 1;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "CashRegisterNumber");
                        var obj = cmd.ExecuteScalar();
                        if (obj != null && obj != DBNull.Value && int.TryParse(Convert.ToString(obj), out var parsed))
                        {
                            cash = parsed;
                        }
                    }

                    // Carrega certificado selecionado (Settings)
                    _currentCertThumb = null;
                    _certStoreLocation = "CurrentUser";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='CertThumbprint' LIMIT 1;";
                        var thumb = cmd.ExecuteScalar();
                        if (thumb != null && thumb != DBNull.Value)
                        {
                            _currentCertThumb = Convert.ToString(thumb);
                        }
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='CertStoreLocation' LIMIT 1;";
                        var store = cmd.ExecuteScalar();
                        if (store != null && store != DBNull.Value)
                        {
                            _certStoreLocation = Convert.ToString(store)!;
                        }
                    }

                    var env = tpAmb == 1 ? "Producao" : "Homolog";
                    var thumbText = string.IsNullOrWhiteSpace(_currentCertThumb) ? "Nenhum certificado selecionado" : _currentCertThumb;

                    // Atualiza UI
                    (FindName("TxtCashRegister") as TextBox)!.Text = cash.ToString();
                    (FindName("TxtSerie") as TextBox)!.Text = serie.ToString();
                    (FindName("TxtNextNumber") as TextBox)!.Text = next.ToString();
                    (FindName("CmbEnvironment") as ComboBox)!.SelectedValue = env;
                    (FindName("TxtCert") as TextBox)!.Text = thumbText;
                    (FindName("TxtCSCId") as TextBox)!.Text = cscId ?? string.Empty;
                    (FindName("TxtCSC") as TextBox)!.Text = csc ?? string.Empty;

                    ApplyNFCeEditState();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configuração NFC-e: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyNFCeEditState()
        {
            try
            {
                var policySvc = new Services.NFCeEditPolicy();
                bool enabled = Services.SessionManager.NFCeEditEnabled;
                var pol = policySvc.GetPolicy(enabled);

                var cmbEnv = (FindName("CmbEnvironment") as ComboBox)!;
                var txtCash = (FindName("TxtCashRegister") as TextBox)!;
                var txtSerie = (FindName("TxtSerie") as TextBox)!;
                var txtNext = (FindName("TxtNextNumber") as TextBox)!;
                var txtCSCId = (FindName("TxtCSCId") as TextBox)!;
                var txtCSC = (FindName("TxtCSC") as TextBox)!;
                var btnCert = (FindName("BtnSelectCert") as Button)!;
                var btnToggle = (FindName("BtnToggleNFCeEdit") as Button)!;

                cmbEnv.IsEnabled = pol.CanEditEnvironment;
                txtCash.IsReadOnly = !pol.CanEditCashRegister;
                txtSerie.IsReadOnly = !pol.CanEditSerie;
                txtNext.IsReadOnly = !pol.CanEditNextNumber;
                txtCSCId.IsReadOnly = !pol.CanEditCSCId;
                txtCSC.IsReadOnly = !pol.CanEditCSC;
                btnCert.IsEnabled = pol.CanSelectCertificate;

                btnToggle.Content = enabled ? "Desabilitar Edição" : "Habilitar Edição";
                var brushConverter = new System.Windows.Media.BrushConverter();
                var cancelBrush = TryFindResource("BrCancelar") as System.Windows.Media.Brush
                                   ?? (brushConverter.ConvertFromString("#dc3545") as System.Windows.Media.Brush
                                       ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)));
                var okBrush = brushConverter.ConvertFromString("#28a745") as System.Windows.Media.Brush
                              ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
                btnToggle.Background = enabled ? okBrush : cancelBrush;
            }
            catch { }
        }

        private void ToggleNFCeEdit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var policySvc = new Services.NFCeEditPolicy();
                if (!policySvc.CanToggleEditing())
                {
                    MessageBox.Show("Você não tem permissão para habilitar a edição de NFC-e.", "Permissão", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool willEnable = !Services.SessionManager.NFCeEditEnabled;
                if (willEnable)
                {
                    var confirm = MessageBox.Show(
                        "Deseja habilitar a edição dos campos da NFC-e? Campos sensíveis permanecerão protegidos.",
                        "Confirmar",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;
                }

                Services.SessionManager.NFCeEditEnabled = willEnable;
                try
                {
                    using var conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    SaveOrUpdateSetting(conn, "NFCeEditEnabled", willEnable ? "1" : "0");
                }
                catch { }
                ApplyNFCeEditState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao alternar edição: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePayments(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        foreach (var m in _methods)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "UPDATE PaymentMethods SET Name=$name, IsEnabled=$enabled, DisplayOrder=$order, LinkType=$link WHERE Id=$id;";
                                cmd.Parameters.AddWithValue("$name", m.Name);
                                cmd.Parameters.AddWithValue("$enabled", m.IsEnabled ? 1 : 0);
                                cmd.Parameters.AddWithValue("$order", m.DisplayOrder);
                                cmd.Parameters.AddWithValue("$link", string.IsNullOrWhiteSpace(m.LinkType) ? (object)DBNull.Value : m.LinkType!);
                                cmd.Parameters.AddWithValue("$id", m.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
                MessageBox.Show("Formas de pagamento atualizadas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar alterações: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseSettings(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void DgPaymentMethods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            var row = ItemsControl.ContainerFromElement(DgPaymentMethods, e.OriginalSource as DependencyObject) as DataGridRow;
            _dragSourceItem = row?.Item as PaymentMethod;
        }

        private void DgPaymentMethods_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue)
            {
                Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (_dragSourceItem != null)
                    {
                        DragDrop.DoDragDrop(DgPaymentMethods, _dragSourceItem, DragDropEffects.Move);
                    }
                }
            }
        }

        private void DgPaymentMethods_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void DgPaymentMethods_Drop(object sender, DragEventArgs e)
        {
            var targetRow = ItemsControl.ContainerFromElement(DgPaymentMethods, e.OriginalSource as DependencyObject) as DataGridRow;
            var targetItem = targetRow?.Item as PaymentMethod;

            var dragged = e.Data.GetData(typeof(PaymentMethod)) as PaymentMethod ?? _dragSourceItem;
            if (dragged != null)
            {
                int oldIndex = _methods.IndexOf(dragged);
                int newIndex = targetItem != null ? _methods.IndexOf(targetItem) : _methods.Count - 1;
                if (oldIndex >= 0)
                {
                    _methods.RemoveAt(oldIndex);
                    if (newIndex < 0) newIndex = 0;
                    if (newIndex > _methods.Count) newIndex = _methods.Count;
                    _methods.Insert(newIndex, dragged);
                    RenumberAndRefresh();
                }
            }

            _dragStartPoint = null;
            _dragSourceItem = null;
        }

        private void DgPaymentMethods_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _methods = _methods
                        .OrderBy(m => m.DisplayOrder)
                        .ToList();
                    RenumberAndRefresh();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void RenumberAndRefresh()
        {
            for (int i = 0; i < _methods.Count; i++)
            {
                _methods[i].DisplayOrder = i + 1;
            }
            DgPaymentMethods.ItemsSource = null;
            DgPaymentMethods.ItemsSource = _methods;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(DgPaymentMethods.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(PaymentMethod.DisplayOrder), ListSortDirection.Ascending));
            view.Refresh();
        }

        private void LoadTEFConfig()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                string Get(string key, string def)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                    cmd.Parameters.AddWithValue("$key", key);
                    var obj = cmd.ExecuteScalar();
                    return (obj == null || obj == DBNull.Value) ? def : Convert.ToString(obj) ?? def;
                }
                var tefType = Get("TEFIntegrationType", "Nenhum");
                (FindName("CmbTEFType") as ComboBox)!.SelectedValue = string.IsNullOrWhiteSpace(tefType) ? "Nenhum" : tefType;
                // Scope
                (FindName("TxtTEFScope") as TextBox)!.Text = Get("TEFScope", string.Empty);
                // SiTef
                (FindName("TxtSitefIP") as TextBox)!.Text = Get("SitefIP", "127.0.0.1");
                (FindName("TxtSitefLoja") as TextBox)!.Text = Get("SitefLoja", "00000000");
                (FindName("TxtSitefTerminal") as TextBox)!.Text = Get("SitefTerminal", "00000001");
                // Pasta de troca
                var defExchange = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEF");
                (FindName("TxtTEFExchangePath") as TextBox)!.Text = Get("TEFExchangePath", defExchange);
                // Debug mode
                var debugRaw = Get("TEFDebugMode", "0");
                (FindName("ChkTEFDebug") as CheckBox)!.IsChecked = debugRaw == "1" || string.Equals(debugRaw, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configuração de TEF: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveTEFConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = (FindName("CmbTEFType") as ComboBox)!.SelectedValue?.ToString() ?? "Nenhum";
                var scope = (FindName("TxtTEFScope") as TextBox)!.Text ?? string.Empty;
                var sitefIP = (FindName("TxtSitefIP") as TextBox)!.Text ?? string.Empty;
                var sitefLoja = (FindName("TxtSitefLoja") as TextBox)!.Text ?? string.Empty;
                var sitefTerminal = (FindName("TxtSitefTerminal") as TextBox)!.Text ?? string.Empty;
                var exchangePath = (FindName("TxtTEFExchangePath") as TextBox)!.Text ?? string.Empty;
                var debugFlag = (FindName("ChkTEFDebug") as CheckBox)!.IsChecked == true ? "1" : "0";

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "TEFIntegrationType", selected);
                SaveOrUpdateSetting(conn, "TEFScope", scope);
                SaveOrUpdateSetting(conn, "SitefIP", string.IsNullOrWhiteSpace(sitefIP) ? "127.0.0.1" : sitefIP);
                SaveOrUpdateSetting(conn, "SitefLoja", string.IsNullOrWhiteSpace(sitefLoja) ? "00000000" : sitefLoja);
                SaveOrUpdateSetting(conn, "SitefTerminal", string.IsNullOrWhiteSpace(sitefTerminal) ? "00000001" : sitefTerminal);
                SaveOrUpdateSetting(conn, "TEFExchangePath", string.IsNullOrWhiteSpace(exchangePath) ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEF") : exchangePath);
                SaveOrUpdateSetting(conn, "TEFDebugMode", debugFlag);

                MessageBox.Show("Configuração de TEF salva.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configuração de TEF: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void SaveOrUpdateSetting(SqliteConnection conn, string key, string value)
        {
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                sel.Parameters.AddWithValue("$key", key);
                var countObj = sel.ExecuteScalar();
                int count = Convert.ToInt32(countObj);
                
                if (count > 0)
                {
                    using (var upd = conn.CreateCommand())
                    {
                        upd.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                        upd.Parameters.AddWithValue("$val", value);
                        upd.Parameters.AddWithValue("$key", key);
                        upd.ExecuteNonQuery();
                    }
                }
                else
                {
                    using (var ins = conn.CreateCommand())
                    {
                        ins.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                        ins.Parameters.AddWithValue("$key", key);
                        ins.Parameters.AddWithValue("$val", value);
                        ins.ExecuteNonQuery();
                    }
                }
            }
        }

        // --- Impressoras ---
        private void LoadPrinters()
        {
            try
            {
                var printers = new List<string>();
                var server = new LocalPrintServer();
                var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });
                foreach (var q in queues)
                {
                    if (!string.IsNullOrWhiteSpace(q.Name))
                        printers.Add(q.Name);
                }
                printers.Sort(StringComparer.OrdinalIgnoreCase);

                (FindName("CmbPrinterBoleto") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterConfissao") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterNFCe") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterCarne80") as ComboBox)!.ItemsSource = printers;
                (FindName("CmbPrinterCarneA4") as ComboBox)!.ItemsSource = printers;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao listar impressoras: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }




        private void EnsureErpImportsTable(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ErpSalesOrderImports (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerName TEXT NOT NULL,
                    OrderNumber TEXT NOT NULL,
                    OrderValue REAL NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ErpSalesOrderImports_OrderNumber ON ErpSalesOrderImports(OrderNumber);
            ";
            cmd.ExecuteNonQuery();
        }


        private void LoadOptions()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                        var obj = cmd.ExecuteScalar();
                        string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        bool enabled = true;
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            enabled = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkEnableF9") as CheckBox)!.IsChecked = enabled;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                        var obj2 = cmd.ExecuteScalar();
                        string? val2 = (obj2 == null || obj2 == DBNull.Value) ? null : Convert.ToString(obj2);
                        bool prompt = true;
                        if (!string.IsNullOrWhiteSpace(val2))
                        {
                            prompt = val2 == "1" || string.Equals(val2, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkPromptConsumer") as CheckBox)!.IsChecked = prompt;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "RequireF2ToStartSale");
                        var obj3 = cmd.ExecuteScalar();
                        string? val3 = (obj3 == null || obj3 == DBNull.Value) ? null : Convert.ToString(obj3);
                        bool requireF2 = false;
                        if (!string.IsNullOrWhiteSpace(val3))
                        {
                            requireF2 = val3 == "1" || string.Equals(val3, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkRequireF2ToStartSale") as CheckBox)!.IsChecked = requireF2;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "EnableBlindCashClosure");
                        var obj4 = cmd.ExecuteScalar();
                        string? val4 = (obj4 == null || obj4 == DBNull.Value) ? null : Convert.ToString(obj4);
                        bool enableBlind = false;
                        if (!string.IsNullOrWhiteSpace(val4))
                        {
                            enableBlind = val4 == "1" || string.Equals(val4, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkEnableBlindCashClosure") as CheckBox)!.IsChecked = enableBlind;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "MaxDiscountPercent");
                        var objMax = cmd.ExecuteScalar();
                        string? valMax = (objMax == null || objMax == DBNull.Value) ? null : Convert.ToString(objMax);
                        (FindName("TxtMaxDiscountPercent") as TextBox)!.Text = string.IsNullOrWhiteSpace(valMax) ? string.Empty : valMax;

                        // Supervisor flags
                        bool requireSupervisorSangria = false;
                        bool requireSupervisorOpening = false;
                        bool requireSupervisorClosing = false;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "RequireSupervisorForSangria");
                        var obj5 = cmd.ExecuteScalar();
                        string? val5 = (obj5 == null || obj5 == DBNull.Value) ? null : Convert.ToString(obj5);
                        if (!string.IsNullOrWhiteSpace(val5))
                        {
                            requireSupervisorSangria = val5 == "1" || string.Equals(val5, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkRequireSupervisorForSangria") as CheckBox)!.IsChecked = requireSupervisorSangria;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "RequireSupervisorForOpeningCash");
                        var objOpen = cmd.ExecuteScalar();
                        string? valOpen = (objOpen == null || objOpen == DBNull.Value) ? null : Convert.ToString(objOpen);
                        if (!string.IsNullOrWhiteSpace(valOpen))
                        {
                            requireSupervisorOpening = valOpen == "1" || string.Equals(valOpen, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkRequireSupervisorForOpeningCash") as CheckBox)!.IsChecked = requireSupervisorOpening;

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "RequireSupervisorForClosingCash");
                        var objClose = cmd.ExecuteScalar();
                        string? valClose = (objClose == null || objClose == DBNull.Value) ? null : Convert.ToString(objClose);
                        if (!string.IsNullOrWhiteSpace(valClose))
                        {
                            requireSupervisorClosing = valClose == "1" || string.Equals(valClose, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkRequireSupervisorForClosingCash") as CheckBox)!.IsChecked = requireSupervisorClosing;

                        // Importar pedido ERP habilitado
                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "ImportERPOrdersEnabled");
                        var objImp = cmd.ExecuteScalar();
                        string? valImp = (objImp == null || objImp == DBNull.Value) ? null : Convert.ToString(objImp);
                        bool importEnabled = false;
                        if (!string.IsNullOrWhiteSpace(valImp))
                        {
                            importEnabled = valImp == "1" || string.Equals(valImp, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkImportERPOrders") as CheckBox)!.IsChecked = importEnabled;

                        // Removido: dados de supervisor agora fazem parte dos usuários (sem inputs na tela).
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar opções: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveNFCeConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Services.SessionManager.NFCeEditEnabled)
                {
                    MessageBox.Show("Edição desabilitada. Habilite a edição para salvar.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var txtCash = (FindName("TxtCashRegister") as TextBox)!;
                var txtSerie = (FindName("TxtSerie") as TextBox)!;
                var txtNext = (FindName("TxtNextNumber") as TextBox)!;
                var cmbEnv = (FindName("CmbEnvironment") as ComboBox)!;
                var txtCSCId = (FindName("TxtCSCId") as TextBox)!;
                var txtCSC = (FindName("TxtCSC") as TextBox)!;

                int cash = int.TryParse(txtCash.Text, out var c1) ? c1 : 1;
                int serie = int.TryParse(txtSerie.Text, out var s1) ? s1 : 1;
                int next = int.TryParse(txtNext.Text, out var n1) ? n1 : 1;
                string env = (cmbEnv.SelectedValue as string) ?? "Homolog";
                int tpAmb = string.Equals(env, "Producao", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                string cscId = (txtCSCId.Text ?? string.Empty).Trim();
                string csc = (txtCSC.Text ?? string.Empty).Trim();

                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();

                    SaveOrUpdateSetting(conn, "CashRegisterNumber", cash.ToString());
                    SaveOrUpdateSetting(conn, "CertThumbprint", _currentCertThumb ?? string.Empty);
                    SaveOrUpdateSetting(conn, "CertStoreLocation", _certStoreLocation);

                    int count = 0;
                    using (var sel = conn.CreateCommand())
                    {
                        sel.CommandText = "SELECT COUNT(*) FROM ConfiguracoesNFCe WHERE Id=1;";
                        var obj = sel.ExecuteScalar();
                        count = Convert.ToInt32(obj);
                    }

                    if (count > 0)
                    {
                        using (var upd = conn.CreateCommand())
                        {
                            upd.CommandText = @"UPDATE ConfiguracoesNFCe SET TpAmb=$tpAmb, CSCId=$cscId, CSC=$csc, Serie=$serie, ProximoNumero=$next WHERE Id=1;";
                            upd.Parameters.AddWithValue("$tpAmb", tpAmb);
                            upd.Parameters.AddWithValue("$cscId", string.IsNullOrWhiteSpace(cscId) ? (object)DBNull.Value : cscId);
                            upd.Parameters.AddWithValue("$csc", string.IsNullOrWhiteSpace(csc) ? (object)DBNull.Value : csc);
                            upd.Parameters.AddWithValue("$serie", serie);
                            upd.Parameters.AddWithValue("$next", next);
                            upd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var ins = conn.CreateCommand())
                        {
                            ins.CommandText = @"INSERT INTO ConfiguracoesNFCe (Id, TpAmb, CSCId, CSC, Serie, ProximoNumero) VALUES (1, $tpAmb, $cscId, $csc, $serie, $next);";
                            ins.Parameters.AddWithValue("$tpAmb", tpAmb);
                            ins.Parameters.AddWithValue("$cscId", string.IsNullOrWhiteSpace(cscId) ? (object)DBNull.Value : cscId);
                            ins.Parameters.AddWithValue("$csc", string.IsNullOrWhiteSpace(csc) ? (object)DBNull.Value : csc);
                            ins.Parameters.AddWithValue("$serie", serie);
                            ins.Parameters.AddWithValue("$next", next);
                            ins.ExecuteNonQuery();
                        }
                    }
                }
                MessageBox.Show("Configuração NFC-e salva.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configuração NFC-e: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void SaveGeneral_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var chk = (FindName("ChkImportERPOrders") as CheckBox)!;
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "ImportERPOrdersEnabled", chk.IsChecked == true ? "1" : "0");
                MessageBox.Show("Configurações gerais salvas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configurações gerais: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadGeneralOptions()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                        var obj = cmd.ExecuteScalar();
                        string? val = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        bool enabled = false;
                        if (obj != null && obj != DBNull.Value)
                        {
                            var s = Convert.ToString(obj);
                            enabled = s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        (FindName("ChkEnableF9") as CheckBox)!.IsChecked = enabled;
                    }
                }
            }
            catch
            {
                // ignore load errors to avoid breaking UI
            }
        }

        private void LoadPrinterSettings()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                string Get(string key)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                    cmd.Parameters.AddWithValue("$key", key);
                    var obj = cmd.ExecuteScalar();
                    return (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj) ?? string.Empty;
                }

                (FindName("CmbPrinterBoleto") as ComboBox)!.SelectedItem = Get("PrinterBoleto");
                (FindName("CmbPrinterConfissao") as ComboBox)!.SelectedItem = Get("PrinterConfissao");
                (FindName("CmbPrinterNFCe") as ComboBox)!.SelectedItem = Get("PrinterNFCe");
                (FindName("CmbPrinterCarne80") as ComboBox)!.SelectedItem = Get("PrinterCarne80");
                (FindName("CmbPrinterCarneA4") as ComboBox)!.SelectedItem = Get("PrinterCarneA4");
                var formato = Get("CarneFormatoPadrao");
                (FindName("CmbCarneFormatoPadrao") as ComboBox)!.SelectedValue = string.IsNullOrWhiteSpace(formato) ? "80mm" : formato;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar impressoras: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SavePrinterSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                SaveOrUpdateSetting(conn, "PrinterBoleto", (FindName("CmbPrinterBoleto") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterConfissao", (FindName("CmbPrinterConfissao") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterNFCe", (FindName("CmbPrinterNFCe") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterCarne80", (FindName("CmbPrinterCarne80") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                SaveOrUpdateSetting(conn, "PrinterCarneA4", (FindName("CmbPrinterCarneA4") as ComboBox)!.SelectedItem?.ToString() ?? string.Empty);
                var formatoPadrao = (FindName("CmbCarneFormatoPadrao") as ComboBox)!.SelectedValue?.ToString() ?? "80mm";
                SaveOrUpdateSetting(conn, "CarneFormatoPadrao", formatoPadrao);
                MessageBox.Show("Impressoras salvas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar impressoras: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- Produto Fácil ---
        private class ProdutoFacilLink
        {
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string PriceFormatted => $"R$ {Price:N2}";
        }

        private void LoadProdutoFacilConfig()
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();

                // Carrega lista de códigos do settings
                var codes = GetProdutoFacilCodes(conn);
                var items = new List<ProdutoFacilLink>();

                if (codes.Count > 0)
                {
                    foreach (var code in codes)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT Description, UnitPrice FROM Products WHERE Code=$c LIMIT 1;";
                        cmd.Parameters.AddWithValue("$c", code);
                        using var r = cmd.ExecuteReader();
                        if (r.Read())
                        {
                            items.Add(new ProdutoFacilLink
                            {
                                Code = code,
                                Description = r.IsDBNull(0) ? string.Empty : r.GetString(0),
                                Price = r.IsDBNull(1) ? 0m : r.GetDecimal(1)
                            });
                        }
                        else
                        {
                            // Produto não encontrado mais no catálogo; ainda manter na lista para remoção
                            items.Add(new ProdutoFacilLink { Code = code, Description = "(não encontrado)", Price = 0m });
                        }
                    }
                }

                var lst = FindName("LstProdutoFacil") as ListView;
                if (lst != null)
                {
                    lst.ItemsSource = items;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar Produto Fácil: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private HashSet<string> GetProdutoFacilCodes(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key='ProdutoFacilCodes' LIMIT 1;";
            var obj = cmd.ExecuteScalar();
            var raw = (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj) ?? string.Empty;
            return raw
                .Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private void SaveProdutoFacilCodes(SqliteConnection conn, IEnumerable<string> codes)
        {
            var value = string.Join(",", codes.Select(c => c.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)));
            SaveOrUpdateSetting(conn, "ProdutoFacilCodes", value);
        }

        private void AddProdutoFacil_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var w = new ProdutosSearchWindow();
                w.Owner = this;
                var ok = w.ShowDialog();
                if (ok == true && w.SelectedEntry != null)
                {
                    using var conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    var codes = GetProdutoFacilCodes(conn);
                    if (!codes.Contains(w.SelectedEntry.Code))
                    {
                        codes.Add(w.SelectedEntry.Code);
                        SaveProdutoFacilCodes(conn, codes);
                        LoadProdutoFacilConfig();
                    }
                    else
                    {
                        MessageBox.Show("Produto já vinculado.", "Produto Fácil", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao adicionar produto: {ex.Message}", "Produto Fácil", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveProdutoFacil_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lst = FindName("LstProdutoFacil") as ListView;
                if (lst?.SelectedItem is ProdutoFacilLink link)
                {
                    using var conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    var codes = GetProdutoFacilCodes(conn);
                    if (codes.Remove(link.Code))
                    {
                        SaveProdutoFacilCodes(conn, codes);
                        LoadProdutoFacilConfig();
                    }
                }
                else
                {
                    MessageBox.Show("Selecione um item para remover.", "Produto Fácil", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao remover produto: {ex.Message}", "Produto Fácil", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void SelectCert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var hasPrivate = new X509Certificate2Collection();
                foreach (var cert in store.Certificates)
                {
                    if (cert.HasPrivateKey)
                    {
                        hasPrivate.Add(cert);
                    }
                }
                var selected = X509Certificate2UI.SelectFromCollection(hasPrivate, "Selecione certificado", "Escolha o certificado com chave privada para assinar NFC-e/NFe", X509SelectionFlag.SingleSelection);
                if (selected != null && selected.Count > 0)
                {
                    var cert = selected[0];
                    _currentCertThumb = cert.Thumbprint;
                    _certStoreLocation = "CurrentUser";
                    (FindName("TxtCert") as TextBox)!.Text = $"{cert.Subject} ({cert.Thumbprint})";
                }
                store.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao selecionar certificado: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DebugTEF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Salvar antes de testar
                SaveTEFConfig_Click(sender, e);

                var tef = TEFManager.Instance;
                var ok = tef.InitializeTEF();
                if (!ok)
                {
                    MessageBox.Show("Falha ao inicializar TEF. Verifique tipo e parâmetros.", "TEF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = tef.ProcessPayment(1.00m, "Credito");
                var status = result.Success ? "APROVADO" : "NEGADO";
                var msg = result.Message ?? string.Empty;
                var nsu = result.TransactionId ?? string.Empty;
                var auth = result.AuthorizationCode ?? string.Empty;
                MessageBox.Show($"Status: {status}\nMensagem: {msg}\nNSU: {nsu}\nAutorização: {auth}", "Teste TEF (Debug)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro no teste de TEF: {ex.Message}", "TEF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveOptions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var chk = (FindName("ChkEnableF9") as CheckBox)!;
                int flag = (chk.IsChecked == true) ? 1 : 0;

                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();

                    using (var sel = conn.CreateCommand())
                    {
                        sel.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                        sel.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                        var countObj = sel.ExecuteScalar();
                        int count = Convert.ToInt32(countObj);
                        if (count > 0)
                        {
                            using (var upd = conn.CreateCommand())
                            {
                                upd.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                                upd.Parameters.AddWithValue("$val", flag.ToString());
                                upd.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                                upd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var ins = conn.CreateCommand())
                            {
                                ins.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                                ins.Parameters.AddWithValue("$key", "EnableF9PriceChange");
                                ins.Parameters.AddWithValue("$val", flag.ToString());
                                ins.ExecuteNonQuery();
                            }
                        }
                    }

                    // Atualiza/insere opção: Solicitar consumidor ao iniciar a venda?
                    var chkPrompt = (FindName("ChkPromptConsumer") as CheckBox)!;
                    int promptFlag = (chkPrompt.IsChecked == true) ? 1 : 0;

                    using (var sel2 = conn.CreateCommand())
                    {
                        sel2.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                        sel2.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                        var countObj2 = sel2.ExecuteScalar();
                        int count2 = Convert.ToInt32(countObj2);
                        if (count2 > 0)
                        {
                            using (var upd2 = conn.CreateCommand())
                            {
                                upd2.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                                upd2.Parameters.AddWithValue("$val", promptFlag.ToString());
                                upd2.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                                upd2.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var ins2 = conn.CreateCommand())
                            {
                                ins2.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                                ins2.Parameters.AddWithValue("$key", "PromptConsumerOnFirstItem");
                                ins2.Parameters.AddWithValue("$val", promptFlag.ToString());
                                ins2.ExecuteNonQuery();
                            }
                        }
                    }

                    // Atualiza/insere opção: Habilitar Conferência às Cegas
                    var chkBlind = (FindName("ChkEnableBlindCashClosure") as CheckBox)!;
                    int blindFlag = (chkBlind.IsChecked == true) ? 1 : 0;

                    using (var sel3 = conn.CreateCommand())
                    {
                        sel3.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key=$key;";
                        sel3.Parameters.AddWithValue("$key", "EnableBlindCashClosure");
                        var countObj3 = sel3.ExecuteScalar();
                        int count3 = Convert.ToInt32(countObj3);
                        if (count3 > 0)
                        {
                            using (var upd3 = conn.CreateCommand())
                            {
                                upd3.CommandText = "UPDATE Settings SET Value=$val WHERE Key=$key;";
                                upd3.Parameters.AddWithValue("$val", blindFlag.ToString());
                                upd3.Parameters.AddWithValue("$key", "EnableBlindCashClosure");
                                upd3.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var ins3 = conn.CreateCommand())
                            {
                                ins3.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $val);";
                                ins3.Parameters.AddWithValue("$key", "EnableBlindCashClosure");
                                ins3.Parameters.AddWithValue("$val", blindFlag.ToString());
                                ins3.ExecuteNonQuery();
                            }
                        }
                    }

                    // Salva configurações do supervisor
                    var chkRequireSupervisorSangria = (FindName("ChkRequireSupervisorForSangria") as CheckBox)!;
                    int requireSupervisorSangriaFlag = (chkRequireSupervisorSangria.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "RequireSupervisorForSangria", requireSupervisorSangriaFlag.ToString());

                    var chkRequireSupervisorOpening = (FindName("ChkRequireSupervisorForOpeningCash") as CheckBox)!;
                    int requireSupervisorOpeningFlag = (chkRequireSupervisorOpening.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "RequireSupervisorForOpeningCash", requireSupervisorOpeningFlag.ToString());

                    var chkRequireSupervisorClosing = (FindName("ChkRequireSupervisorForClosingCash") as CheckBox)!;
                    int requireSupervisorClosingFlag = (chkRequireSupervisorClosing.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "RequireSupervisorForClosingCash", requireSupervisorClosingFlag.ToString());

                    // Salva configuração: Exigir F2 para iniciar venda
                    var chkRequireF2 = (FindName("ChkRequireF2ToStartSale") as CheckBox)!;
                    int requireF2Flag = (chkRequireF2.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "RequireF2ToStartSale", requireF2Flag.ToString());

                    // Removido: persistência de dados de supervisor. Autorização via usuários (AuthWindow).

                    // Salva desconto máximo (%)
                    var txtMax = (FindName("TxtMaxDiscountPercent") as TextBox)!;
                    var raw = (txtMax.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        SaveOrUpdateSetting(conn, "MaxDiscountPercent", raw);
                    }

                    // Salva opção: importar pedidos do ERP
                    var chkImport = (FindName("ChkImportERPOrders") as CheckBox)!;
                    int importFlag = (chkImport.IsChecked == true) ? 1 : 0;
                    SaveOrUpdateSetting(conn, "ImportERPOrdersEnabled", importFlag.ToString());

                    // Salva configurações do logo
                    var txtLogoPath = (FindName("TxtLogoPath") as TextBox)!;
                    var chkShowLogo = (FindName("ChkShowLogo") as CheckBox)!;
                    string logoPath = (txtLogoPath.Text ?? string.Empty).Trim();
                    bool showLogo = chkShowLogo.IsChecked == true;
                    
                    SaveOrUpdateSetting(conn, "LogoImagePath", logoPath);
                    SaveOrUpdateSetting(conn, "ShowLogo", showLogo ? "1" : "0");
                }
                
                // Atualizar logo na MainWindow se ela estiver aberta
                try
                {
                    var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                    mainWindow?.RefreshCustomLogo();
                }
                catch { /* ignore */ }
                
                MessageBox.Show("Opções salvas.", "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar opções: {ex.Message}", "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- Handlers para aba Empresa (conexão e sincronização) ---
        private (string host, int port, string db, string user, string pass) ReadEmpresaInputs()
        {
            var host = (FindName("TxtDbHost") as TextBox)?.Text?.Trim() ?? "localhost";
            var db = (FindName("TxtDbName") as TextBox)?.Text?.Trim() ?? "medusaX8";
            var user = (FindName("TxtDbUser") as TextBox)?.Text?.Trim() ?? "root";
            var pass = (FindName("PwdDbPass") as PasswordBox)?.Password ?? string.Empty;
            int port = 3307;
            int.TryParse((FindName("TxtDbPort") as TextBox)?.Text, out port);
            return (host, port, db, user, pass);
        }

        private async void ConnectAndListEmpresas_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadEmpresaInputs();
                AutoSyncManager.Instance.Configure(host, port, db, user, pass);
                var api = new Services.SyncApi();
                bool ok = await api.TestConnectionAsync(host, port, db, user, pass);
                if (ok)
                {
                    MessageBox.Show("Conexão validada. (Listagem de empresas não implementada)", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Falha na conexão ou tabelas não encontradas.", "Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SyncParticipantesClientes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadEmpresaInputs();
                AutoSyncManager.Instance.Configure(host, port, db, user, pass);
                var api = new Services.SyncApi();
                var count = await api.SyncParticipantesAsync(host, port, db, user, pass);
                MessageBox.Show($"Clientes sincronizados: {count}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SyncProdutos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadEmpresaInputs();
                AutoSyncManager.Instance.Configure(host, port, db, user, pass);
                var api = new Services.SyncApi();
                var count = await api.SyncProdutosAsync(host, port, db, user, pass);
                MessageBox.Show($"Produtos sincronizados: {count}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SelectEmpresaAndSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadEmpresaInputs();
                AutoSyncManager.Instance.Configure(host, port, db, user, pass);
                var api = new Services.SyncApi();
                await api.SyncParticipantesAsync(host, port, db, user, pass);
                await api.SyncProdutosAsync(host, port, db, user, pass);
                MessageBox.Show("Sincronização concluída.", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SyncEmpresa_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadEmpresaInputs();
                AutoSyncManager.Instance.Configure(host, port, db, user, pass);
                var api = new Services.SyncApi();
                await api.SyncEmpresaAsync(host, port, db, user, pass);
                LoadEmpresaAndContadorFromDb();
                MessageBox.Show("Empresa sincronizada e carregada.", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SyncContador_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (host, port, db, user, pass) = ReadEmpresaInputs();
                AutoSyncManager.Instance.Configure(host, port, db, user, pass);
                var api = new Services.SyncApi();
                await api.SyncContadorAsync(host, port, db, user, pass);
                LoadEmpresaAndContadorFromDb();
                MessageBox.Show("Contador sincronizado e carregado.", "Empresa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Empresa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // ====== Máscaras e Validações (CNPJ/CPF/CEP/Telefone) ======
        private bool _isFormattingInput = false;

        private static string OnlyDigits(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var arr = input.Where(char.IsDigit).ToArray();
            return new string(arr);
        }

        private static string FormatWithParts(string digits, int[] parts, string[] seps)
        {
            // seps.Length deve ser parts.Length - 1
            var sb = new System.Text.StringBuilder();
            int pos = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int take = Math.Min(parts[i], Math.Max(0, digits.Length - pos));
                if (take <= 0) break;
                sb.Append(digits.Substring(pos, take));
                pos += take;
                if (i < seps.Length && pos < digits.Length)
                {
                    sb.Append(seps[i]);
                }
            }
            return sb.ToString();
        }

        private static string FormatCnpj(string digits)
        {
            digits = OnlyDigits(digits);
            if (digits.Length > 14) digits = digits.Substring(0, 14);
            return FormatWithParts(digits, new[] { 2, 3, 3, 4, 2 }, new[] { ".", ".", "/", "-" });
        }

        private static string FormatCpf(string digits)
        {
            digits = OnlyDigits(digits);
            if (digits.Length > 11) digits = digits.Substring(0, 11);
            return FormatWithParts(digits, new[] { 3, 3, 3, 2 }, new[] { ".", ".", "-" });
        }

        private static string FormatCep(string digits)
        {
            digits = OnlyDigits(digits);
            if (digits.Length > 8) digits = digits.Substring(0, 8);
            return FormatWithParts(digits, new[] { 5, 3 }, new[] { "-" });
        }

        private static string FormatPhone(string digits)
        {
            digits = OnlyDigits(digits);
            if (digits.Length > 11) digits = digits.Substring(0, 11);
            if (digits.Length <= 2) return digits; // DDD parcial
            var ddd = digits.Substring(0, 2);
            var rest = digits.Substring(2);
            if (rest.Length <= 4) return $"({ddd}) {rest}";
            if (rest.Length <= 8) return $"({ddd}) {rest.Substring(0, 4)}-{rest.Substring(4)}";
            // 9 dígitos (celular) ou 8+1 parcial
            int first = (rest.Length >= 9) ? 5 : 4;
            if (rest.Length <= first) return $"({ddd}) {rest}";
            return $"({ddd}) {rest.Substring(0, first)}-{rest.Substring(first)}";
        }

        private static bool IsValidCpf(string? input)
        {
            var d = OnlyDigits(input);
            if (d.Length != 11) return false;
            if (new string(d[0], d.Length) == d) return false; // todos iguais

            int[] mult1 = { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] mult2 = { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };
            string temp = d.Substring(0, 9);
            int sum = 0;
            for (int i = 0; i < 9; i++) sum += (temp[i] - '0') * mult1[i];
            int r = sum % 11;
            int dg1 = r < 2 ? 0 : 11 - r;
            temp += dg1.ToString();
            sum = 0;
            for (int i = 0; i < 10; i++) sum += (temp[i] - '0') * mult2[i];
            r = sum % 11;
            int dg2 = r < 2 ? 0 : 11 - r;
            return d.EndsWith(dg1.ToString() + dg2.ToString());
        }

        private static bool IsValidCnpj(string? input)
        {
            var d = OnlyDigits(input);
            if (d.Length != 14) return false;
            if (new string(d[0], d.Length) == d) return false;
            int[] mult1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] mult2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            string temp = d.Substring(0, 12);
            int sum = 0;
            for (int i = 0; i < 12; i++) sum += (temp[i] - '0') * mult1[i];
            int r = sum % 11;
            int dg1 = r < 2 ? 0 : 11 - r;
            temp += dg1.ToString();
            sum = 0;
            for (int i = 0; i < 13; i++) sum += (temp[i] - '0') * mult2[i];
            r = sum % 11;
            int dg2 = r < 2 ? 0 : 11 - r;
            return d.EndsWith(dg1.ToString() + dg2.ToString());
        }

        private void ApplyFormatted(TextBox tb, string formatted)
        {
            if (tb == null) return;
            if (_isFormattingInput) return;
            try
            {
                _isFormattingInput = true;
                int sel = tb.SelectionStart;
                tb.Text = formatted;
                tb.SelectionStart = Math.Min(formatted.Length, sel + 1);
            }
            finally
            {
                _isFormattingInput = false;
            }
        }

        private void CNPJ_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            var formatted = FormatCnpj(tb.Text ?? string.Empty);
            ApplyFormatted(tb, formatted);
        }

        private void CPF_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            var formatted = FormatCpf(tb.Text ?? string.Empty);
            ApplyFormatted(tb, formatted);
        }

        private void CEP_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            var formatted = FormatCep(tb.Text ?? string.Empty);
            ApplyFormatted(tb, formatted);
        }

        private void Telefone_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            var formatted = FormatPhone(tb.Text ?? string.Empty);
            ApplyFormatted(tb, formatted);
        }

        private void MarkInvalid(TextBox tb, string message)
        {
            if (tb == null) return;
            tb.BorderBrush = Brushes.Red;
            tb.ToolTip = message;
        }

        private void ClearInvalid(TextBox tb)
        {
            if (tb == null) return;
            tb.ClearValue(BorderBrushProperty);
            // Mantém ToolTip pré-existente se houver; se for mensagem de erro, limpa
            if (tb.ToolTip is string s && (s.Contains("inválid", StringComparison.OrdinalIgnoreCase) || s.Contains("invalido", StringComparison.OrdinalIgnoreCase)))
            {
                tb.ToolTip = null;
            }
        }

        private void CNPJ_Validate_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            var d = OnlyDigits(tb.Text);
            if (string.IsNullOrEmpty(d)) { ClearInvalid(tb); return; }
            if (d.Length == 14 && IsValidCnpj(d)) ClearInvalid(tb); else MarkInvalid(tb, "CNPJ inválido");
        }

        private void CPF_Validate_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            var d = OnlyDigits(tb.Text);
            if (string.IsNullOrEmpty(d)) { ClearInvalid(tb); return; }
            if (d.Length == 11 && IsValidCpf(d)) ClearInvalid(tb); else MarkInvalid(tb, "CPF inválido");
        }

        // --- Métodos para funcionalidade de Logo ---
        private void SelectLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Selecionar Imagem/Logo",
                    Filter = "Arquivos de Imagem|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff|Todos os arquivos|*.*",
                    FilterIndex = 1
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedPath = openFileDialog.FileName;
                    
                    // Atualiza o campo de texto com o caminho
                    (FindName("TxtLogoPath") as TextBox)!.Text = selectedPath;
                    
                    // Carrega e exibe o preview
                    LoadLogoPreview(selectedPath);
                    
                    // Salva a configuração no banco
                    SaveLogoSettings(selectedPath, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao selecionar imagem: {ex.Message}", "Logo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Limpa o campo de texto
                (FindName("TxtLogoPath") as TextBox)!.Text = string.Empty;
                
                // Remove o preview
                (FindName("LogoPreview") as System.Windows.Controls.Image)!.Source = null;
                
                // Desmarca o checkbox
                (FindName("ChkShowLogo") as CheckBox)!.IsChecked = false;
                
                // Salva a configuração no banco (remove)
                SaveLogoSettings(string.Empty, false);
                
                MessageBox.Show("Logo removido com sucesso.", "Logo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao remover logo: {ex.Message}", "Logo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLogoPreview(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    
                    (FindName("LogoPreview") as System.Windows.Controls.Image)!.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar preview da imagem: {ex.Message}", "Logo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveLogoSettings(string logoPath, bool showLogo)
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    
                    // Salva o caminho da imagem
                    SaveOrUpdateSetting(conn, "LogoImagePath", logoPath);
                    
                    // Salva se deve exibir o logo
                    SaveOrUpdateSetting(conn, "ShowLogo", showLogo ? "1" : "0");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configurações do logo: {ex.Message}", "Logo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLogoSettings()
        {
            try
            {
                using (var conn = new SqliteConnection(GetConnectionString()))
                {
                    conn.Open();
                    
                    // Carrega o caminho da imagem
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "LogoImagePath");
                        var obj = cmd.ExecuteScalar();
                        string? logoPath = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        
                        if (!string.IsNullOrWhiteSpace(logoPath))
                        {
                            (FindName("TxtLogoPath") as TextBox)!.Text = logoPath;
                            LoadLogoPreview(logoPath);
                        }
                    }
                    
                    // Carrega se deve exibir o logo
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=$key LIMIT 1;";
                        cmd.Parameters.AddWithValue("$key", "ShowLogo");
                        var obj = cmd.ExecuteScalar();
                        string? showLogoValue = (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
                        
                        bool showLogo = false;
                        if (!string.IsNullOrWhiteSpace(showLogoValue))
                        {
                            showLogo = showLogoValue == "1" || string.Equals(showLogoValue, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        
                        (FindName("ChkShowLogo") as CheckBox)!.IsChecked = showLogo;
                    }
                }
            }
            catch (Exception ex)
            {
                // Não exibe erro aqui para não interromper o carregamento da janela
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar configurações do logo: {ex.Message}");
            }
        }

    }
}
