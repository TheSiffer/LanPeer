using LanPeer.DataModels;
using LanPeer.Interfaces;
using LanPeer.Workers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LanPeer
{
    internal class ConnectionManager : BackgroundService , IConnectionManager
    {
        //private static readonly Lazy<ConnectionManager> _instance = new(() => new ConnectionManager());
        //public static ConnectionManager Instance = _instance.Value;
        //public static ConnectionManager? Instance { get; private set; }
        private readonly ICodeManager _codeManager;
        private readonly IDataHandler _dataHandler;
        private readonly IQueueManager _queueManager;
        private readonly int authPort = 50001;
        public event Action<bool>? IsConnected;
        public delegate void IsConnectedEventHandler();

        public ConnectionManager(ICodeManager codeManager, IDataHandler dataHandler, IQueueManager queueManager) 
        {
            _codeManager = codeManager;
            _dataHandler = dataHandler;
            _queueManager = queueManager;
            //Instance = this;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = ListenForIncoming(stoppingToken);
            await Task.WhenAll(listener);
        }

        public bool ConnectToPeer(Peer peer)
        {
            if (!_queueManager.PeerExists(peer)) // check if the peer is in trusted before connecting
            {
                Console.WriteLine("Peer is not trusted! Perform auth first");
                return false;
            }
            var client = new TcpClient(peer.IpAddress, peer.CommsPort);
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    _dataHandler.SetStream(stream);
                    if (stream != null)
                    {
                        IsConnected?.Invoke(true);
                        return true;
                    }
                    else
                    {
                        IsConnected?.Invoke(false);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Attempt to connect to Peer: {peer.Name} failed with error: " + ex.ToString());
                    return false;
                }
            }
        }
        //listens only on the auth port.
        private async Task ListenForIncoming(CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Any ,authPort);
            listener.Start();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync();

                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                using var stream = client.GetStream();
                                using var reader = new StreamReader(stream, Encoding.UTF8);
                                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                                var requestToken = await JsonSerializer.DeserializeAsync<RequestToken>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                
                                if (requestToken != null)
                                {
                                    if (_codeManager.Validate(requestToken.Code.ToString()))
                                    {
                                        _dataHandler.SetStream(stream); //connect the data handler to the client
                                        IsConnected?.Invoke(true); //fire event
                                        Peer newPeer = new Peer
                                        {
                                            Id = requestToken.SenderId,
                                            Name = requestToken.DeviceName,
                                            IpAddress = requestToken.IpAddress,
                                            CommsPort = requestToken.CommsPort
                                        };
                                        _queueManager.AddPeer(newPeer);
                                    }
                                    else
                                    {
                                        IsConnected?.Invoke(false);
                                        stream.Close();
                                    }
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                Console.WriteLine($"Invalid JSON received: {jsonEx.Message}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error handling client: {ex.Message}");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Listener error: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

    }
}
