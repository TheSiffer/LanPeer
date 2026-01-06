using FluentFTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.Interfaces
{
    public interface IDataHandler
    {
        public void SetStream(Stream stream);
        public bool DisposeStream();
        public void SetFtpClient(FtpClient client);
        public bool DisposeFtpClient();
        public string GetActiveTransferId();
        public void SetBufferSize(int size);
        public int GetBufferSize();
        public Task SendFilesAsync();
        public Task ReceiveFilesAsync();
    }
}
