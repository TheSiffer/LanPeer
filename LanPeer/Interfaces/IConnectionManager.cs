using LanPeer.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.Interfaces
{
    public interface IConnectionManager
    {
        public Task<bool> ConnectToPeer(Peer peer);
        public Task StartFtpServerForPeer(Peer peer, string downloadFolder);
    }
}
