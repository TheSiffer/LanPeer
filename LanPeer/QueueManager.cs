using LanPeer.DataModels;
using System.Collections.Concurrent;
using System.Text.Json;

namespace LanPeer
{
    public class QueueManager 
    { 
        private static readonly Lazy<QueueManager> _instance = new(() => new QueueManager());
        public static QueueManager Instance => _instance.Value; //global accessor
        private readonly ConcurrentQueue<FileTransferItem> _fileQueue = new();

        private QueueManager() { }

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
    }
}
