using LanPeer.DataModels;
using LanPeer.Interfaces;
using LanPeer.Utility;
using System.Collections.Concurrent;
using System.Text.Json;

namespace LanPeer
{
    /// <summary>
    /// File Queue and Peer List Manager Singleton class with Lazy init. Contains global access points to both Data Structures.
    /// Implements IQueueManager
    /// </summary>
    public class QueueManager : IQueueManager
    {
        //private static QueueManager? _instance; //= new(() => new QueueManager());
        //public static QueueManager Instance => _instance ?? throw new InvalidOperationException("Not Instantiated"); //=> _instance.Value; //global accessor
        private readonly ConcurrentQueue<FileTransferItem> _fileQueue = new();
        private List<Peer> _peers = new();

        //public QueueManager() { }

        /// <summary>
        /// Adds a file or folder to the send queue
        /// </summary>
        /// <param name="filePath">Location of the file to enqueue</param>
        /// <returns></returns>
        public async Task EnQueue(string filePath)
        {
            var manifest = ManifestBuilder.Build(filePath);
            //ManifestBuilder.SaveToJson(manifest, filePath); //adds json file to the folder. json extension and broken for files without a folder
            List<ManifestNode> files = ManifestBuilder.Flatten(manifest);

            var fileInfo = new FileInfo(filePath);
            FileTransferItem root = new FileTransferItem
            {
                FileName = fileInfo.FullName,
                FullPath = filePath,
                FileCount = new DirectoryInfo(filePath).GetFiles().Length,
                FileSize = fileInfo.Length,
                FileManifest = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                SubFiles = files,
                State = DataModels.Data.TransferState.Pending
            };
            _fileQueue.Enqueue(root);
            //var dataHandler = DataHandler.GetDataHandler();
            //_fileQueue.Enqueue(item);
        }

        public ConcurrentQueue<FileTransferItem> GetFileQueue()
        {
            return _fileQueue;
        }

        public bool PeerExists(Peer peer)
        {
            return _peers.Contains(peer);
        }

        public List<Peer> GetSavedPeers()
        {
            return _peers;
        }

        public void LoadPeersfromSave(List<Peer> peers)
        {
            _peers = peers;
        }
        
        public void AddPeer(Peer peer)
        {
            if(peer != null)
                _peers.Add(peer);   
        }
        
        public bool DeletePeer(Peer peer)
        {
            if (_peers.Remove(peer))
            {
                return true;
            }
            return false;
        }
    }
}
