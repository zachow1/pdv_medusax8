namespace PDV_MedusaX8.Models
{
    // Representa um item na lista de produtos (DataGrid)
    public class CartItem
    {
        public int Item { get; set; }
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public double Qt { get; set; }
        public decimal VlUnit { get; set; }
        public decimal Total { get; set; }
        public bool IsCancellation { get; set; } // marca linhas de cancelamento

        // Campos de desconto aplicados ao item
        public decimal DiscountApplied { get; set; } // valor absoluto de desconto aplicado
        public bool DiscountIsPercent { get; set; } // indica se o desconto foi aplicado por percentual
        public decimal DiscountPercent { get; set; } // percentual aplicado (0-100)
        public string? DiscountReason { get; set; } // motivo do desconto
    }
}