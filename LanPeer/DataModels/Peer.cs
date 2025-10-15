using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class Peer
    {
        public string Id { get; set; }  = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public int CommsPort { get; set; } 
    }
}
