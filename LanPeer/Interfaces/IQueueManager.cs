using LanPeer.DataModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.Interfaces
{
    public interface IQueueManager
    {
        public Task EnQueue(string filePath);
        public ConcurrentQueue<FileTransferItem> GetFileQueue();
        public bool PeerExists(Peer peer);
        public List<Peer> GetSavedPeers();
        public void LoadPeersfromSave(List<Peer> peers);
        public void AddPeer(Peer peer);
        public bool DeletePeer(Peer peer);
    }
}
