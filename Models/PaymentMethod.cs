namespace PDV_MedusaX8.Models
{
    public class PaymentMethod
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public int DisplayOrder { get; set; }
        public string? LinkType { get; set; }
    }
}