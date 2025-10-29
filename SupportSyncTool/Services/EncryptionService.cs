using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SupportSyncTool.Services
{
    public static class EncryptionService
    {
        private static readonly byte[] Salt = Encoding.UTF8.GetBytes("SyncTool2024Salt");
        
        public static string Encrypt(string plainText, string password)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;
                
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            
            using (var aes = Aes.Create())
            {
                var key = new Rfc2898DeriveBytes(password, Salt, 10000, HashAlgorithmName.SHA256);
                aes.Key = key.GetBytes(32);
                aes.IV = key.GetBytes(16);
                
                using (var encryptor = aes.CreateEncryptor())
                using (var msEncrypt = new MemoryStream())
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    csEncrypt.Write(plainTextBytes, 0, plainTextBytes.Length);
                    csEncrypt.FlushFinalBlock();
                    return Convert.ToBase64String(msEncrypt.ToArray());
                }
            }
        }
        
        public static string Decrypt(string cipherText, string password)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;
                
            try
            {
                byte[] cipherTextBytes = Convert.FromBase64String(cipherText);
                
                using (var aes = Aes.Create())
                {
                    var key = new Rfc2898DeriveBytes(password, Salt, 10000, HashAlgorithmName.SHA256);
                    aes.Key = key.GetBytes(32);
                    aes.IV = key.GetBytes(16);
                    
                    using (var decryptor = aes.CreateDecryptor())
                    using (var msDecrypt = new MemoryStream(cipherTextBytes))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }
        
        public static string GenerateMachineKey()
        {
            // Gera uma chave baseada na máquina para criptografia local
            var machineId = Environment.MachineName + Environment.UserName;
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineId));
                return Convert.ToBase64String(hash);
            }
        }
    }
}