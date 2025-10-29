using System;
using System.IO;
using System.Text.Json;
using SupportSyncTool.Models;

namespace SupportSyncTool.Services
{
    public class ConfigManager
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "SupportSyncTool"
        );
        private static readonly string ConfigFile = Path.Combine(ConfigDirectory, "config.enc");
        private static readonly string MachineKey = EncryptionService.GenerateMachineKey();
        
        public static void SaveConfig(DatabaseConfig config)
        {
            try
            {
                // Criar diretório se não existir
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }
                
                // Criptografar apenas a senha
                var configToSave = new DatabaseConfig
                {
                    Host = config.Host,
                    Port = config.Port,
                    Database = config.Database,
                    Username = config.Username,
                    Password = EncryptionService.Encrypt(config.Password, MachineKey)
                };
                
                var json = JsonSerializer.Serialize(configToSave, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Erro ao salvar configuração: {ex.Message}", ex);
            }
        }
        
        public static DatabaseConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigFile))
                {
                    return new DatabaseConfig(); // Retorna configuração vazia
                }
                
                var json = File.ReadAllText(ConfigFile);
                var config = JsonSerializer.Deserialize<DatabaseConfig>(json);
                
                if (config != null)
                {
                    // Descriptografar a senha
                    config.Password = EncryptionService.Decrypt(config.Password, MachineKey);
                }
                
                return config ?? new DatabaseConfig();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Erro ao carregar configuração: {ex.Message}", ex);
            }
        }
        
        public static bool ConfigExists()
        {
            return File.Exists(ConfigFile);
        }
        
        public static void DeleteConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    File.Delete(ConfigFile);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Erro ao deletar configuração: {ex.Message}", ex);
            }
        }
        
        public static string GetConfigPath()
        {
            return ConfigFile;
        }
    }
}