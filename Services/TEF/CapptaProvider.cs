namespace PDV_MedusaX8.Services.TEF
{
    using System;
    using System.Windows;

    public class CapptaProvider : ITEFProvider
    {
        public bool Initialize()
        {
            try
            {
                // TODO: Inicializar ACBrTEFD para Cappta (DLL/serviço, automação)
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar Cappta: {ex.Message}", "Erro TEF", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public TEFResult ProcessPayment(decimal amount, string paymentType)
        {
            try
            {
                var nsu = "CAPPTA_" + DateTime.Now.ToString("yyyyMMddHHmmss");
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