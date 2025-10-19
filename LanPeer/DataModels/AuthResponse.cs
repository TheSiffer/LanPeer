using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class AuthResponse
    {
        public string Status { get; set; } = string.Empty;
        public int CommsPort { get; set; }
    }
}
