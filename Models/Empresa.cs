namespace PDV_MedusaX8.Models
{
    public class Empresa
    {
        public int Id { get; set; }
        public string? RazaoSocial { get; set; }
        public string? NomeFantasia { get; set; }
        public string? CNPJ { get; set; }
        public string? IE { get; set; }
        public string? IM { get; set; }
        public string? RegimeTributario { get; set; } // CRT
        public string? CNAE { get; set; }
        public string? Email { get; set; }
        public string? Telefone { get; set; }
        public string? Website { get; set; }
        public string? CEP { get; set; }
        public string? Logradouro { get; set; }
        public string? Numero { get; set; }
        public string? Complemento { get; set; }
        public string? Bairro { get; set; }
        public string? MunicipioCodigo { get; set; } // IBGE
        public string? MunicipioNome { get; set; }
        public string? UF { get; set; }
    }
}