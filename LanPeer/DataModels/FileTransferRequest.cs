using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class FileTransferRequest
    {
        public string Filename { get; set; } = string.Empty;
        public long Filesize { get; set; }
        public string Path { get; set; } = string.Empty;
        public byte[] Hash { get; set; }
        public string PeerId { get; set; } = string.Empty;
    }
}
