using LanPeer.DataModels.Data;


namespace LanPeer.DataModels
{
    public class FileTransferItem
    {
        public string FileName { get; set; }          // "A.txt"
        public string? DestPath { get; set; } = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "LanPeer");     // Where the file is headed
        public string FullPath { get; set; }          // Sender absolute path
        public long FileSize { get; set; }
        public byte[] Hash { get; set; }
        public bool IsSent { get; set; }
        public bool IsVerified { get; set; }
        public TransferState State { get; set; }
        public int FileCount { get; set; }
        public string FileManifest { get; set; }
        public List<ManifestNode>? SubFiles { get; set; }
    }
}
