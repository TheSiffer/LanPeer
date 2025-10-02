using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels.Data
{
    public enum RequestType
    {   
        NewConnection = 1,
        Handshake = 2,
        Authentication = 3,
        Authenticating = 4,
        Authenticated = 5,
        FileTransfer = 6,
    }
}
