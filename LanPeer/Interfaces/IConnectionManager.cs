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
        public bool ConnectToPeer(Peer peer);
    }
}
