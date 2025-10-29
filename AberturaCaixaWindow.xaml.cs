using System;
using System.Globalization;
using System.Windows;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class AberturaCaixaWindow : Window
    {
        public AberturaCaixaWindow()
        {
            InitializeComponent();
            TxtValor.Focus();
            this.Loaded += AberturaCaixaWindow_Loaded;
        }

        private void AberturaCaixaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.Owner is MainWindow mw)
            {
                SupervisorPanel.Visibility = mw.RequiresSupervisorForOpeningCash ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private string GetConnectionString() => DbHelper.GetConnectionString();

        private void EnsureTables(SqliteConnection con)
        {
            // Criar tabelas base
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS CashMovements (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Type TEXT,
                        Amount REAL,
                        Reason TEXT,
                        Operator TEXT,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        PaymentMethodCode TEXT,
                        ReferenceType TEXT,
                        ReferenceId INTEGER,
                        DocumentNumber TEXT,
                        Notes TEXT,
                        BalanceAfter REAL
                    );
                    CREATE TABLE IF NOT EXISTS Settings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT
                    );
                    CREATE TABLE IF NOT EXISTS CashSessions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        CashRegisterNumber INTEGER,
                        OpenedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        OpeningAmount REAL NOT NULL DEFAULT 0,
                        ClosedAt TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_CashSessions_ClosedAt ON CashSessions(ClosedAt);
                    CREATE INDEX IF NOT EXISTS idx_CashSessions_Open ON CashSessions(CashRegisterNumber, OpenedAt);
                    CREATE TABLE IF NOT EXISTS ConfiguracoesNFCe (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TpAmb INTEGER NOT NULL DEFAULT 2,
                        CSCId TEXT,
                        CSC TEXT,
                        cUF INTEGER,
                        Serie INTEGER NOT NULL DEFAULT 1,
                        ProximoNumero INTEGER NOT NULL DEFAULT 1,
                        UltimaAutorizacao TEXT,
                        ContingenciaAtiva INTEGER NOT NULL DEFAULT 0,
                        MotivoContingencia TEXT
                    );
                ";
                cmd.ExecuteNonQuery();
            }

            // Garantir colunas extras em CashMovements (migração defensiva)
            var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmdInfo = con.CreateCommand())
            {
                cmdInfo.CommandText = "PRAGMA table_info(CashMovements)";
                using var rd = cmdInfo.ExecuteReader();
                while (rd.Read())
                {
                    cols.Add(rd.GetString(1));
                }
            }
            void AddColIfMissing(string name, string decl)
            {
                if (!cols.Contains(name))
                {
                    using var cmdAlter = con.CreateCommand();
                    cmdAlter.CommandText = $"ALTER TABLE CashMovements ADD COLUMN {name} {decl}";
                    cmdAlter.ExecuteNonQuery();
                }
            }
            AddColIfMissing("PaymentMethodCode", "TEXT");
            AddColIfMissing("ReferenceType", "TEXT");
            AddColIfMissing("ReferenceId", "INTEGER");
            AddColIfMissing("DocumentNumber", "TEXT");
            AddColIfMissing("Notes", "TEXT");
            AddColIfMissing("BalanceAfter", "REAL");
            AddColIfMissing("CashRegisterNumber", "INTEGER");

            // Índices usados nas consultas
            using (var cmdIdx = con.CreateCommand())
            {
                cmdIdx.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_CashMovements_Type_CreatedAt ON CashMovements(Type, CreatedAt);
                    CREATE INDEX IF NOT EXISTS idx_CashMovements_Method_CreatedAt ON CashMovements(PaymentMethodCode, CreatedAt);
                    CREATE INDEX IF NOT EXISTS idx_CashMovements_Ref ON CashMovements(ReferenceType, ReferenceId);
                ";
                cmdIdx.ExecuteNonQuery();
            }
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            // Abertura deve registrar o valor informado (currency/local)
            var valor = 0m;
            var txt = TxtValor.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(txt))
            {
                if (!decimal.TryParse(txt, NumberStyles.Currency, CultureInfo.CurrentCulture, out valor))
                {
                    // fallback: tenta número padrão
                    if (!decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out valor))
                    {
                        valor = 0m;
                    }
                }
            }

            var motivo = TxtMotivo.Text?.Trim();
            string operador = PDV_MedusaX8.Services.SessionManager.CurrentUser ?? Environment.UserName;

            // Validação de supervisor, se exigido
            if (this.Owner is MainWindow mwReq && mwReq.RequiresSupervisorForOpeningCash)
            {
                // Se não houver credenciais configuradas nas opções, usar autenticação por usuário (AuthWindow)
                if (string.IsNullOrWhiteSpace(mwReq.SupervisorCode) || string.IsNullOrWhiteSpace(mwReq.SupervisorPassword))
                {
                    var auth = new LoginWindow(authorizationMode: true) { Owner = mwReq };
                    var ok = auth.ShowDialog();
                    if (ok == true && (string.Equals(auth.LoggedRole, "admin", StringComparison.OrdinalIgnoreCase) || string.Equals(auth.LoggedRole, "fiscal", StringComparison.OrdinalIgnoreCase)))
                    {
                        var supervisor = auth.LoggedUser ?? "Supervisor";
                        operador = $"{operador} (Supervisor: {supervisor})";
                    }
                    else
                    {
                        MessageBox.Show("Acesso de supervisor negado ou cancelado.", "Autorização", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    // Mantém suporte ao fluxo antigo via campos locais, se estiver configurado
                    var code = (TxtSupervisorCode.Text ?? string.Empty).Trim();
                    var pass = (TxtSupervisorPassword.Password ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pass) ||
                        !string.Equals(code, mwReq.SupervisorCode, StringComparison.Ordinal) ||
                        !string.Equals(pass, mwReq.SupervisorPassword, StringComparison.Ordinal))
                    {
                        MessageBox.Show("Código ou senha do supervisor inválidos.", "Autorização", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(mwReq.SupervisorName))
                    {
                        operador = $"{operador} (Supervisor: {mwReq.SupervisorName})";
                    }
                }
            }

            try
            {
                using var con = new SqliteConnection(GetConnectionString());
                con.Open();
                EnsureTables(con);

                // Impedir nova abertura sem fechamento: checar sessão ativa
                using (var cmdCheck = con.CreateCommand())
                {
                    cmdCheck.CommandText = @"SELECT COUNT(1) FROM CashSessions WHERE ClosedAt IS NULL";
                    var cntObj = cmdCheck.ExecuteScalar();
                    int cnt = Convert.ToInt32(cntObj ?? 0);
                    if (cnt > 0)
                    {
                        MessageBox.Show("O caixa já está aberto. Feche o caixa antes de abrir novamente.", "Abertura de Caixa", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                // Obter número do caixa (Settings.CashRegisterNumber ou ConfiguracoesNFCe.Serie)
                int cashNumber = 0;
                using (var cmdGet = con.CreateCommand())
                {
                    cmdGet.CommandText = "SELECT Value FROM Settings WHERE Key='CashRegisterNumber' LIMIT 1";
                    var v = cmdGet.ExecuteScalar();
                    if (v != null && v != DBNull.Value && int.TryParse(v.ToString(), out var n)) cashNumber = n;
                }
                if (cashNumber == 0)
                {
                    using var cmdSerie = con.CreateCommand();
                    cmdSerie.CommandText = "SELECT Serie FROM ConfiguracoesNFCe LIMIT 1";
                    var sv = cmdSerie.ExecuteScalar();
                    if (sv != null && sv != DBNull.Value && int.TryParse(sv.ToString(), out var s)) cashNumber = s;
                }
                if (cashNumber == 0) cashNumber = 1; // fallback

                // Inserir abertura
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO CashMovements (Type, Amount, Reason, Operator, CashRegisterNumber) VALUES ($type, $amount, $reason, $operator, $cash)";
                    cmd.Parameters.AddWithValue("$type", "ABERTURA");
                    cmd.Parameters.AddWithValue("$amount", (double)valor);
                    cmd.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(motivo) ? (object)DBNull.Value : motivo);
                    cmd.Parameters.AddWithValue("$operator", operador);
                    cmd.Parameters.AddWithValue("$cash", cashNumber);
                    cmd.ExecuteNonQuery();
                }

                // Criar sessão de caixa (aberta)
                using (var cmdSess = con.CreateCommand())
                {
                    cmdSess.CommandText = @"INSERT INTO CashSessions (CashRegisterNumber, OpeningAmount) VALUES ($cash, $amount)";
                    cmdSess.Parameters.AddWithValue("$cash", cashNumber);
                    cmdSess.Parameters.AddWithValue("$amount", (double)valor);
                    cmdSess.ExecuteNonQuery();
                }

                // Recalcular saldo de caixa: ABERTURA + SUPRIMENTO - SANGRIA
                double saldo = 0.0;
                using (var cmdSum = con.CreateCommand())
                {
                    cmdSum.CommandText = @"SELECT COALESCE(SUM(CASE WHEN Type='ABERTURA' THEN Amount WHEN Type='SUPRIMENTO' THEN Amount WHEN Type='SANGRIA' THEN -Amount ELSE 0 END), 0) FROM CashMovements";
                    var obj = cmdSum.ExecuteScalar();
                    if (obj is double d) saldo = d;
                    else if (obj is long l) saldo = l;
                    else if (obj is int i) saldo = i;
                }

                // Upsert em Settings
                using (var cmdUp = con.CreateCommand())
                {
                    cmdUp.CommandText = "UPDATE Settings SET Value = $val WHERE Key = 'CashBalance'";
                    cmdUp.Parameters.AddWithValue("$val", saldo.ToString(CultureInfo.InvariantCulture));
                    int rows = cmdUp.ExecuteNonQuery();
                    if (rows == 0)
                    {
                        using var cmdIns = con.CreateCommand();
                        cmdIns.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('CashBalance', $val)";
                        cmdIns.Parameters.AddWithValue("$val", saldo.ToString(CultureInfo.InvariantCulture));
                        cmdIns.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao salvar abertura: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Abertura registrada com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}