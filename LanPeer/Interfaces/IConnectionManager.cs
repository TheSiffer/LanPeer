using FubarDev.FtpServer;
using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.Interfaces
{
    public interface IConnectionManager
    {
        public Task<bool> InitiateAuth(string address, int code);
        public Task<bool> ConnectToPeer(Peer peer, TransferMode mode);
        public Task<bool> ChangeTransferMode(TransferMode mode, Peer peer);
        public List<IFtpConnection> GetActiveConnections();
    }
}
