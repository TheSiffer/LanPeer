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
        public Task<bool> DisposeStream();
        public string GetActiveTransferId();
        public void SetBufferSize(int size);
        public int GetBufferSize();
        public Task SendFilesAsync();
    }
}
