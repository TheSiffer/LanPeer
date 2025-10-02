using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels.Data
{
    public enum TransferState
    {
        Pending = 1,
        InProgress = 2,
        Completed = 3,
        Verified = 4,
        Failed = 5
    }
}
