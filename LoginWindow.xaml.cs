using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class LoginWindow : Window
    {
        public string LoggedUser { get; private set; }
        public string LoggedRole { get; private set; } = string.Empty;

        // Quando verdadeiro, funciona como janela de autorização (não altera a sessão, apenas valida acesso)
        public bool AuthorizationMode { get; }

        // Construtor padrão exigido pelo carregamento via XAML
        public LoginWindow() : this(false) { }

        public LoginWindow(bool authorizationMode = false)
        {
            AuthorizationMode = authorizationMode;
            InitializeComponent();
            try { EnsureUsersTableAndSeed(); } catch { }
            TxtUsuario.Focus();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            PerformLogin();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformLogin();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (AuthorizationMode)
            {
                this.DialogResult = false;
                this.Close();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private void PerformLogin()
        {
            string usuario = TxtUsuario.Text.Trim();
            string senha = TxtSenha.Password;

            // Limpar mensagem anterior
            TxtMensagem.Visibility = Visibility.Collapsed;

            // Validar campos vazios
            if (string.IsNullOrEmpty(usuario))
            {
                ShowError("Por favor, digite o usuário.");
                TxtUsuario.Focus();
                return;
            }

            if (string.IsNullOrEmpty(senha))
            {
                ShowError("Por favor, digite a senha.");
                TxtSenha.Focus();
                return;
            }

            // Validar credenciais
            if (ValidateCredentials(usuario, senha))
            {
                LoggedUser = usuario;

                if (AuthorizationMode)
                {
                    // Apenas autoriza a ação, não altera a sessão atual
                    this.DialogResult = true;
                    this.Close();
                    return;
                }

                // Iniciar sessão
                SessionManager.StartSession(usuario);

                // Verificar sessão de caixa ativa antes de abrir o PDV
                try
                {
                    bool hasActiveSession = false;
                    using (var conn = new SqliteConnection(DbHelper.GetConnectionString()))
                    {
                        conn.Open();
                        // Garantir schema completo (evita erros nas consultas/índices posteriores)
                        using (var cmdInit = conn.CreateCommand())
                        {
                            cmdInit.CommandText = @"
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
                                    BalanceAfter REAL,
                                    CashRegisterNumber INTEGER
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
                            ";
                            cmdInit.ExecuteNonQuery();
                        }
                        // Migração defensiva: adicionar colunas ausentes, se necessário
                        var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        using (var cmdInfo = conn.CreateCommand())
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
                                using var cmdAlter = conn.CreateCommand();
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
                        // Índices usados em consultas
                        using (var cmdIdx = conn.CreateCommand())
                        {
                            cmdIdx.CommandText = @"
                                CREATE INDEX IF NOT EXISTS idx_CashMovements_Type_CreatedAt ON CashMovements(Type, CreatedAt);
                                CREATE INDEX IF NOT EXISTS idx_CashMovements_Method_CreatedAt ON CashMovements(PaymentMethodCode, CreatedAt);
                                CREATE INDEX IF NOT EXISTS idx_CashMovements_Ref ON CashMovements(ReferenceType, ReferenceId);
                            ";
                            cmdIdx.ExecuteNonQuery();
                        }
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = @"SELECT COUNT(1) FROM CashSessions WHERE ClosedAt IS NULL";
                        var obj = cmd.ExecuteScalar();
                        int count = Convert.ToInt32(obj ?? 0);
                        hasActiveSession = count > 0;
                    }

                    if (!hasActiveSession)
                    {
                        var ab = new AberturaCaixaWindow { Owner = this };
                        var ok = ab.ShowDialog();
                        if (ok != true)
                        {
                            ShowError("É obrigatório abrir o caixa para entrar no PDV.");
                            SessionManager.EndSession();
                            return;
                        }
                    }
                }
                catch
                {
                    // Em caso de erro ao verificar, impedir prosseguir
                    ShowError("Falha ao verificar abertura de caixa. Tente novamente.");
                    SessionManager.EndSession();
                    return;
                }

                // Abrir janela principal
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();

                // Fechar janela de login
                this.Close();
            }
            else
            {
                ShowError("Usuário ou senha incorretos!");
                TxtSenha.Password = "";
                TxtUsuario.Focus();
            }
        }

        private bool ValidateCredentials(string usuario, string senha)
        {
            try
            {
                using var conn = new SqliteConnection(DbHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Username, PasswordHash, Role, Active FROM Users WHERE LOWER(Username) = LOWER($u) LIMIT 1";
                cmd.Parameters.AddWithValue("$u", usuario);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var active = reader.GetInt32(3) == 1;
                    if (!active) return false;
                    var pwdHash = reader.GetString(1);
                    var role = reader.GetString(2);
                    var inputHash = ComputeSha256(senha);
                    if (string.Equals(pwdHash, inputHash, StringComparison.OrdinalIgnoreCase))
                    {
                        LoggedRole = role;
                        return true;
                    }
                }
            }
            catch
            {
                // falha silenciosa para não travar o login se DB indisponível
            }
            return false;
        }

        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private void EnsureUsersTableAndSeed()
        {
            using var conn = new SqliteConnection(DbHelper.GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Active INTEGER NOT NULL DEFAULT 1,
                    ExternalId TEXT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT NULL
                );
            ";
            cmd.ExecuteNonQuery();

            // Seed usuários básicos se não existirem
            SeedUser(conn, "admin", "123456", "admin");
            SeedUser(conn, "fiscal", "fiscal123", "fiscal");
            SeedUser(conn, "operador", "operador123", "operador");
            SeedUser(conn, "gerente", "gerente123", "gerente");
        }

        private void SeedUser(SqliteConnection conn, string user, string password, string role)
        {
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM Users WHERE LOWER(Username) = LOWER($u)";
            check.Parameters.AddWithValue("$u", user);
            var count = Convert.ToInt32(check.ExecuteScalar());
            if (count == 0)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO Users (Username, PasswordHash, Role, Active) VALUES ($u, $p, $r, 1)";
                ins.Parameters.AddWithValue("$u", user);
                ins.Parameters.AddWithValue("$p", ComputeSha256(password));
                ins.Parameters.AddWithValue("$r", role);
                ins.ExecuteNonQuery();
            }
        }

        private void ShowError(string message)
        {
            TxtMensagem.Text = message;
            TxtMensagem.Visibility = Visibility.Visible;
        }

        // Permitir arrastar a janela
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}