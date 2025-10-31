using PDV_MedusaX8.Services;

namespace PDV_MedusaX8.Services
{
    public class NFCeEditPolicy
    {
        public bool CanToggleEditing(string requiredPermission = "system_config")
            => SessionManager.HasPermission(requiredPermission);

        public record Policy(
            bool CanEditEnvironment,
            bool CanEditCashRegister,
            bool CanEditSerie,
            bool CanEditNextNumber,
            bool CanEditCSCId,
            bool CanEditCSC,
            bool CanSelectCertificate
        );

        public Policy GetPolicy(bool editingEnabled)
        {
            // CSC ID e CSC podem ser editados quando a edição está habilitada
            // Apenas o certificado permanece sempre protegido por segurança
            return new Policy(
                CanEditEnvironment: editingEnabled,
                CanEditCashRegister: editingEnabled,
                CanEditSerie: editingEnabled,
                CanEditNextNumber: editingEnabled,
                CanEditCSCId: editingEnabled,
                CanEditCSC: editingEnabled,
                CanSelectCertificate: false // Certificado sempre protegido
            );
        }
    }
}