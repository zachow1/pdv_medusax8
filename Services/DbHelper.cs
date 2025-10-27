using System;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PDV_MedusaX8.Services
{
    public static class DbHelper
    {
        public static string DatabaseFileName => "medusax8.db";
        private static string AppFolderName => "PDV_MedusaX8";

        public static string GetSecureDbFolder()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(localAppData, AppFolderName, "data");
            return folder;
        }

        public static string GetDbPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dbPath = Path.Combine(baseDir, DatabaseFileName);
            return dbPath;
        }

        public static string GetConnectionString()
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = GetDbPath() };
            return builder.ToString();
        }

        public static void Initialize()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dbPath = Path.Combine(baseDir, DatabaseFileName);

                if (!File.Exists(dbPath))
                {
                    using var conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
                    cmd.ExecuteNonQuery();
                }

                TryHardenPermissions(baseDir, dbPath);
            }
            catch
            {
                // Avoid crashing the app if hardening fails
            }
        }

        private static void TryHardenPermissions(string folder, string dbPath)
        {
            try
            {
                var user = WindowsIdentity.GetCurrent().User;
                if (user == null) return;

                // Restrict directory ACL to current user
                var dirSec = new DirectorySecurity();
                dirSec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                dirSec.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));
                new DirectoryInfo(folder).SetAccessControl(dirSec);

                // Restrict file ACL to current user
                var fileSec = new FileSecurity();
                fileSec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                fileSec.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow
                ));
                new FileInfo(dbPath).SetAccessControl(fileSec);

                // Hide the DB file to reduce accidental exposure
                File.SetAttributes(dbPath, File.GetAttributes(dbPath) | FileAttributes.Hidden);
            }
            catch
            {
                // Non-fatal if permissions cannot be set (e.g., limited environment)
            }
        }
    }
}