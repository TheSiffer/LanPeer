using LanPeer.DataModels.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class AckPacket
    {
        public string TransferId { get; set; }
        public TransferState State { get; set; }
    }
}
