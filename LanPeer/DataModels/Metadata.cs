using LanPeer.DataModels.Data;


namespace LanPeer.DataModels
{
    public class Metadata
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public long TotalChunks { get; set; } = 0;
        public long ZipSize { get; set; } 
        public string ZipHash { get; set; } = string.Empty;
        public string ZipName { get; set; } = string.Empty;
        //public List<ManifestNode> Manifest { get; set; } = new();
        public TransferState State;
    }
}
