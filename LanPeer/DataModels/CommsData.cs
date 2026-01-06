using LanPeer.DataModels.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class CommsData
    {
        public string? Id { get; set; } = string.Empty;
        public string? IpAddress { get; set; } = string.Empty;
        public int? TransferPort { get; set; }

        public FtpCreds Creds = new();
        public TransferMode Mode { get; set; }
    }
}
