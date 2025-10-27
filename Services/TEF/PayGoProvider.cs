namespace PDV_MedusaX8.Services.TEF
{
    using System;
    using System.Windows;

    public class PayGoProvider : ITEFProvider
    {
        public bool Initialize()
        {
            try
            {
                // TODO: Inicializar ACBrTEFD para PayGo (automação, executáveis, pasta de troca)
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar PayGo: {ex.Message}", "Erro TEF", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public TEFResult ProcessPayment(decimal amount, string paymentType)
        {
            try
            {
                var nsu = "PAYGO_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                var auth = "AUTH_" + new Random().Next(100000, 999999);
                return new TEFResult { Success = true, Message = "OK", TransactionId = nsu, AuthorizationCode = auth };
            }
            catch (Exception ex)
            {
                return new TEFResult { Success = false, Message = ex.Message };
            }
        }
    }
}