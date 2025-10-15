using LanPeer.Interfaces;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LanPeer.Workers
{
    /// <summary>
    /// Background service runs discovery on port 50000.
    /// </summary>
   public class DiscoveryWorker : BackgroundService , IDiscoveryWorker
    {
        private const int DiscoveryPort = 50000; //this is the discovery port and it is set in stone.
        private string DiscoveryMessage;
        private readonly Dictionary<string, DateTime> _peerHeartbeats = new(); //keyvalue pairs Ip-timestamp
        public event Action<string>? PeerDiscovered;
        public event Action<string>? PeerLost;

        private string myId;

        //for debugging
        public DiscoveryWorker()
        {
            myId = Guid.NewGuid().ToString();
            DiscoveryMessage = $"LanTransfer-Discovery {myId}";
        }

        public Dictionary<string, DateTime> GetPeers()
        {
            return _peerHeartbeats;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("LanPeer Service started.");

            var broadcaster = BroadcastPresence(stoppingToken);

            var listener = ListenForPeers(stoppingToken);

            var cleaner = CleanupPeers(stoppingToken);

            Console.WriteLine($"Current Session id for this pc: {myId}");

            await Task.WhenAll(broadcaster, listener, cleaner);
        }

        private async Task BroadcastPresence(CancellationToken token)
        {
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;

            var data = Encoding.UTF8.GetBytes(DiscoveryMessage);

            while (!token.IsCancellationRequested)
            {
                await udpClient.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                Console.WriteLine("Broadcasted presence");
                //for debug
                await udpClient.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Loopback, 9000));

                await Task.Delay(TimeSpan.FromSeconds(10), token); //annoying 3 second interval before this
            }
        }

        private async Task ListenForPeers(CancellationToken token)
        {
            using var udpClient = new UdpClient(DiscoveryPort);

            while (!token.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync(token);
                var message = Encoding.UTF8.GetString(result.Buffer);

                if (message == DiscoveryMessage)
                {
                    var peerAddress = result.RemoteEndPoint.Address.ToString();

                    if (IsLocalAddress(peerAddress)) //check removed for debugging
                    {
                        lock (_peerHeartbeats)
                        {
                            bool isNew = !_peerHeartbeats.ContainsKey(peerAddress);
                            _peerHeartbeats[peerAddress] = DateTime.UtcNow;

                            if (isNew)
                            {
                                PeerDiscovered?.Invoke(peerAddress);
                                Console.WriteLine($"Discovered new peer: {peerAddress}, {DiscoveryMessage}");
                            }
                        }
                    }
                }
            }
        }

        private async Task CleanupPeers(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);

                lock (_peerHeartbeats)
                {
                    var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(10);
                    var inactivePeers = _peerHeartbeats.Where(p => p.Value < cutoff).Select(p => p.Key).ToList();

                    foreach (var peer in inactivePeers)
                    {
                        _peerHeartbeats.Remove(peer);
                        PeerLost?.Invoke(peer);
                        Console.WriteLine($"Peer removed (inactive): {peer}");
                    }
                }
            }
        }

        private bool IsLocalAddress(string address)
        {
            var host = Dns.GetHostAddresses(Dns.GetHostName());
            return host.Any(ip => ip.ToString() == address);
        }
    }
}
