using System;

namespace SupportSyncTool.Models
{
    public class DatabaseConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 3307;
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        
        public string GetConnectionString()
        {
            return $"Server={Host};Port={Port};Database={Database};Uid={Username};Pwd={Password};";
        }
        
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Host) &&
                   Port > 0 &&
                   !string.IsNullOrWhiteSpace(Database) &&
                   !string.IsNullOrWhiteSpace(Username);
        }
    }
}