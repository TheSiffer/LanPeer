using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class AuthResponse
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int CommsPort { get; set; }
        public int TransPort { get; set; }
    }
}
