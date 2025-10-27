namespace PDV_MedusaX8.Services.TEF
{
    using PDV_MedusaX8.Services;

    public interface ITEFProvider
    {
        bool Initialize();
        TEFResult ProcessPayment(decimal amount, string paymentType);
    }
}