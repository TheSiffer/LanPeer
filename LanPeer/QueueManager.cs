using LanPeer.DataModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer
{
    internal class QueueManager 
    { 
        private static readonly Lazy<QueueManager> _instance = new Lazy<QueueManager>(() => new QueueManager());

        private readonly ConcurrentQueue<FileTransferItem> _fileQueue = new ConcurrentQueue<FileTransferItem>();

        private QueueManager()
        {
            
        }
    }
}
