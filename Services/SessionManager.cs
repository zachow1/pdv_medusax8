using System;

namespace PDV_MedusaX8.Services
{
    public static class SessionManager
    {
        private static string _currentUser;
        private static DateTime _loginTime;
        private static bool _isLoggedIn;

        /// <summary>
        /// Usuário atualmente logado no sistema
        /// </summary>
        public static string CurrentUser 
        { 
            get => _currentUser; 
            private set => _currentUser = value; 
        }

        /// <summary>
        /// Horário do login
        /// </summary>
        public static DateTime LoginTime 
        { 
            get => _loginTime; 
            private set => _loginTime = value; 
        }

        /// <summary>
        /// Indica se há um usuário logado
        /// </summary>
        public static bool IsLoggedIn 
        { 
            get => _isLoggedIn; 
            private set => _isLoggedIn = value; 
        }

        /// <summary>
        /// Tempo de sessão ativa
        /// </summary>
        public static TimeSpan SessionDuration => IsLoggedIn ? DateTime.Now - LoginTime : TimeSpan.Zero;

        /// <summary>
        /// Inicia uma nova sessão para o usuário
        /// </summary>
        /// <param name="username">Nome do usuário</param>
        public static void StartSession(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Nome de usuário não pode ser vazio", nameof(username));

            CurrentUser = username;
            LoginTime = DateTime.Now;
            IsLoggedIn = true;
        }

        /// <summary>
        /// Encerra a sessão atual
        /// </summary>
        public static void EndSession()
        {
            CurrentUser = null;
            LoginTime = default;
            IsLoggedIn = false;
        }

        /// <summary>
        /// Verifica se a sessão ainda é válida
        /// </summary>
        /// <param name="maxSessionHours">Máximo de horas para a sessão (padrão: 8 horas)</param>
        /// <returns>True se a sessão ainda é válida</returns>
        public static bool IsSessionValid(int maxSessionHours = 8)
        {
            if (!IsLoggedIn)
                return false;

            return SessionDuration.TotalHours < maxSessionHours;
        }

        /// <summary>
        /// Obtém informações da sessão atual
        /// </summary>
        /// <returns>String com informações da sessão</returns>
        public static string GetSessionInfo()
        {
            if (!IsLoggedIn)
                return "Nenhum usuário logado";

            return $"Usuário: {CurrentUser} | Login: {LoginTime:dd/MM/yyyy HH:mm:ss} | Duração: {SessionDuration:hh\\:mm\\:ss}";
        }

        /// <summary>
        /// Verifica se o usuário atual tem permissão específica
        /// </summary>
        /// <param name="permission">Permissão a ser verificada</param>
        /// <returns>True se o usuário tem a permissão</returns>
        public static bool HasPermission(string permission)
        {
            if (!IsLoggedIn)
                return false;

            // Implementação básica de permissões baseada no tipo de usuário
            switch (CurrentUser?.ToLower())
            {
                case "admin":
                    return true; // Admin tem todas as permissões

                case "fiscal":
                    // Fiscal pode acessar configurações do sistema e operações fiscais
                    return permission == "system_config" || permission == "fiscal_ops" || permission == "sales" || permission == "products";

                case "gerente":
                    return permission != "system_config"; // Gerente tem quase todas, exceto configurações do sistema

                case "operador":
                    return permission == "sales" || permission == "products"; // Operador só pode vender e consultar produtos

                default:
                    return false;
            }
        }
    }
}