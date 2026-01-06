using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class BroadcastData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OS { get; set; }
        public string address { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}
