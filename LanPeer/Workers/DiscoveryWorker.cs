using LanPeer.DataModels;
using LanPeer.Interfaces;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LanPeer.Workers
{
    /// <summary>
    /// Background service runs discovery on port 50000.
    /// </summary>
    public class DiscoveryWorker : BackgroundService, IDiscoveryWorker
    {
        private const int DiscoveryPort = 50000; //this is the discovery port and it is set in stone.
        //private string DiscoveryMessage;
        private BroadcastData DiscoveryMessage;
        private readonly List<BroadcastData> _peerHeartbeats = new(); //keyvalue pairs Ip-timestamp
        //private readonly List<BroadcastData> peers = new();
        public event Action<BroadcastData>? PeerDiscovered;
        public event Action<BroadcastData>? PeerLost;

        public readonly string myId;

        //for debugging
        public DiscoveryWorker()
        {
            myId = Guid.NewGuid().ToString();
            DiscoveryMessage = new BroadcastData()
            {
                Id = myId,
                Name = Environment.MachineName,
                OS = Environment.OSVersion.ToString(),
                address = GetLocalAddress() ?? new string("0.0.0.0")
            };

            //DiscoveryMessage = $"LanTransfer-Discovery {myId}";
        }

        public List<BroadcastData> GetPeers()
        {
            return _peerHeartbeats;
        }

        public string GetMyId()
        {
            return myId;
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
            using var udpClient = new UdpClient() { EnableBroadcast = true };

            try
            {
                var json = JsonSerializer.Serialize(DiscoveryMessage);
                var data = Encoding.UTF8.GetBytes(json);

                while (!token.IsCancellationRequested)
                {
                    await udpClient.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); //add guid here
                    Console.WriteLine("Broadcasted presence");
                    //for debug
                    await udpClient.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Loopback, 9000));

                    await Task.Delay(TimeSpan.FromSeconds(10), token); //annoying 3 second interval before this
                }
                udpClient.Close();
            }
            catch (Exception ex)
            {

            }
        }

        private async Task ListenForPeers(CancellationToken token)
        {
            using var udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync(token);

                    var json = Encoding.UTF8.GetString(result.Buffer);

                    var message = JsonSerializer.Deserialize<BroadcastData>(json);

                    if (message != null && message.Id == DiscoveryMessage.Id) // Inverse this check to prevent it from detecting itself
                    {
                        var peerAddress = message.address.ToString();

                        if (IsLocalAddress(peerAddress)) //check removed for debugging
                        {
                            lock (_peerHeartbeats)
                            {
                                var existing = _peerHeartbeats.FirstOrDefault(x => x.Id == message.Id);

                                if (existing == null)
                                {
                                    message.TimeStamp = DateTime.UtcNow;
                                    _peerHeartbeats.Add(message);
                                    PeerDiscovered?.Invoke(message);
                                    Console.WriteLine($"Discovered new peer: {DiscoveryMessage.Id}, {DiscoveryMessage.Name}, {DiscoveryMessage.OS}");
                                }
                                else
                                {
                                    existing.TimeStamp = DateTime.UtcNow;
                                    existing.address = message.address;
                                    Console.WriteLine($"Updated peer: {DiscoveryMessage.Id}, {DiscoveryMessage.Name}, {DiscoveryMessage.OS}");
                                }
                            }
                        }
                    }
                }
                catch (SocketException)
                {
                    udpClient.Close();
                }
                catch (ObjectDisposedException)
                {
                    udpClient.Close();
                }
            }
            udpClient.Close();
        }

        private async Task CleanupPeers(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);

                lock (_peerHeartbeats)
                {
                    var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(30);
                    var inactivePeers = _peerHeartbeats.Where(p => p.TimeStamp < cutoff).ToList();

                    foreach (var peer in inactivePeers)
                    {
                        _peerHeartbeats.Remove(peer);
                        PeerLost?.Invoke(peer);
                        Console.WriteLine($"Peer removed (inactive): {peer.Id}, {peer.Name}, {peer.OS}");
                    }
                }
            }
        }

        private bool IsLocalAddress(string address)
        {
            var host = Dns.GetHostAddresses(Dns.GetHostName());
            return host.Any(ip => ip.ToString() == address);
        }

        private static string? GetLocalAddress()
        {
            string localIP;
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIP = endPoint.Address.ToString();
                }
                if (!string.IsNullOrEmpty(localIP))
                {
                    return localIP;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Retrieving Ip failed. Details: " + ex.Message);
            }
            return null;
        }
    }
}
