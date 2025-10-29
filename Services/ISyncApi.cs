using System;
using System.Threading.Tasks;

namespace PDV_MedusaX8.Services
{
    public interface ISyncApi
    {
        Task<bool> TestConnectionAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null);

        Task<int> SyncParticipantesAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null);

        Task<int> SyncProdutosAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null);

        Task<int> SyncUsuariosAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null);

        Task<int> SyncEmpresaAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null);

        Task<int> SyncContadorAsync(string host, int port, string database, string user, string password, IProgress<string>? progress = null);
    }
}