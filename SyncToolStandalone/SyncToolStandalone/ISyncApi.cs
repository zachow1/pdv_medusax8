using System;
using System.Threading.Tasks;

namespace SyncToolStandalone
{
    public interface ISyncApi
    {
        Task<(bool success, string message)> TestConnectionAsync(string host, int port, string database, string user, string password);
        Task<(bool success, int count, string message)> SyncParticipantesAsync(string host, int port, string database, string user, string password);
        Task<(bool success, int count, string message)> SyncProdutosAsync(string host, int port, string database, string user, string password);
    }
}