using LanPeer.DataModels.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class FileTransferItem
    {
        public string FileName { get; set; }          // "A.txt"
        public string RelativePath { get; set; }      // "Sub1\B.txt"
        public string FullPath { get; set; }          // Sender absolute path
        public long Size { get; set; }
        public byte[] Hash { get; set; }
        public bool IsSent { get; set; }
        public bool IsVerified { get; set; }
        public TransferState State { get; set; }
    }
}
