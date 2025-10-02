using LanPeer.DataModels.Data;
using System.ComponentModel.DataAnnotations;

namespace LanPeer.DataModels
{
    public class ChunkPacket
    {
        public string TransferId { get; set; }
        public int ChunkIndex { get; set; }
        public long TotalChunks { get; set; }
        public TransferState state { get; set; }   
        public byte[]? Data { get; set; }
    }
}
