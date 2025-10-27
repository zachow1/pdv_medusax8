using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PDV_MedusaX8.Services;

namespace PDV_MedusaX8
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Handlers globais para capturar falhas em execução por duplo clique
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            Log("App starting");
            
            // Inicializa pasta segura e migra/fortalece banco
            DbHelper.Initialize();

            base.OnStartup(e);
        }

        public static string LogFile => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", DateTime.Now.ToString("yyyy-MM-dd") + ".log");

        public static void Log(string message, Exception? ex = null)
        {
            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);
                var file = System.IO.Path.Combine(logPath, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var details = ex == null ? string.Empty : (" | EX: " + ex.ToString());
                var full = DateTime.Now.ToString("HH:mm:ss.fff") + " | " + message + details;
                System.IO.File.AppendAllText(file, full + Environment.NewLine);
            }
            catch { /* ignore */ }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("DispatcherUnhandledException", e.Exception);
            try
            {
                MessageBox.Show($"Erro inesperado: {e.Exception.Message}\n\nLog: {LogFile}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true;
            Shutdown(-1);
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            Log("UnhandledException", e.ExceptionObject as Exception);
            try
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show($"Erro crítico: {ex?.Message}\n\nLog: {LogFile}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }
    }
}