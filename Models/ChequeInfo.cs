using System;

namespace PDV_MedusaX8.Models
{
    public class ChequeInfo
    {
        public decimal Valor { get; set; }
        public DateTime? BomPara { get; set; }
        public string? Emitente { get; set; }
        public string? BancoCodigo { get; set; }
        public string? Agencia { get; set; }
        public string? Conta { get; set; }
        public string? NumeroCheque { get; set; }
        public string? CpfCnpjEmitente { get; set; }
        public string? CidadeCodigo { get; set; }
    }
}