using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using LanPeer.Interfaces;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LanPeer.Workers
{
    internal class ConnectionManager : BackgroundService , IConnectionManager //Add logic to initiate an auth request with a peer.
    {
        //private static readonly Lazy<ConnectionManager> _instance = new(() => new ConnectionManager());
        //public static ConnectionManager Instance = _instance.Value;
        //public static ConnectionManager? Instance { get; private set; }
        private readonly ICodeManager _codeManager;
        private readonly IDataHandler _dataHandler;
        private readonly IQueueManager _queueManager;
        private readonly IDiscoveryWorker _discoveryWorker;
        private readonly int authPort = 50001;
        private readonly int commsPort = 50002; //confirm this before use
        public event Action<bool>? IsConnected;
        public delegate void IsConnectedEventHandler();

        public ConnectionManager(ICodeManager codeManager, IDataHandler dataHandler, IQueueManager queueManager, IDiscoveryWorker discoveryWorker) 
        {
            _codeManager = codeManager;
            _dataHandler = dataHandler;
            _queueManager = queueManager;
            _discoveryWorker = discoveryWorker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = ListenForIncoming(stoppingToken);
            await Task.WhenAll(listener);
        }

        public async Task<bool> InitiateAuth(string address, int code) //port is set in stone.
        {
            if (!string.IsNullOrEmpty(address))
            {
                try {
                    var client = new TcpClient();
                    await client.ConnectAsync(address, authPort); //connect to auth port

                    using NetworkStream stream = client.GetStream();

                    RequestToken token = new RequestToken()
                    {
                        Type = RequestType.Authenticating,
                        SenderId = _discoveryWorker.GetMyId(),
                        DeviceName = Environment.MachineName,
                        ReceiverId = Guid.NewGuid().ToString(), //very bad hack fix this, broadcast more data to fill this in
                        CommsPort = commsPort,
                        Code = code,
                        IpAddress = address,
                    };

                    await JsonSerializer.SerializeAsync(stream, token);
                    await stream.FlushAsync();

                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string? response = await reader.ReadLineAsync();
                    if (response != null)
                    {
                        var result = JsonSerializer.Deserialize<AuthResponse>(response);
                        if (string.Equals(result.Status, "AUTH_OK"))
                        {
                            Console.WriteLine($"Received: {result}");
                            Peer peer = new Peer()
                            {
                                Id = Guid.NewGuid().ToString(),
                                //set name
                                IpAddress = address,
                                CommsPort = result.CommsPort
                            };
                            return await ConnectAndSetStream(address, result.CommsPort); //if auth successful open a connection on the peer defined comms port
                        }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auth request to {address} failed: {ex.Message}");
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Initiates the connection request to a trusted peer.
        /// Sets the stream on the Datahandler if connection is made.
        /// </summary>
        /// <param name="peer">The peer to connect to</param>
        /// <returns>IsConnected</returns>
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
                                        var response = new AuthResponse()
                                        {
                                            Status = "AUTH_OK",
                                            CommsPort = commsPort
                                        };
                                        var auth = JsonSerializer.SerializeAsync<AuthResponse>(stream, response);

                                        IsConnected?.Invoke(true); //fire event
                                        Peer newPeer = new Peer
                                        {
                                            Id = requestToken.SenderId,
                                            Name = requestToken.DeviceName,
                                            IpAddress = requestToken.IpAddress,
                                            CommsPort = requestToken.CommsPort
                                        };
                                        _queueManager.AddPeer(newPeer);
                                        await stream.DisposeAsync();
                                    }
                                    else
                                    {
                                        var response = new AuthResponse()
                                        {
                                            Status = "AUTH_FAIL",
                                            CommsPort = 0
                                        };
                                        var auth = JsonSerializer.SerializeAsync<AuthResponse>(stream, response);

                                        IsConnected?.Invoke(false);
                                        await stream.DisposeAsync(); //eh? why close the stream, we need this
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

        private async Task<bool> ConnectAndSetStream(string address, int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(address, port);
            using NetworkStream stream = client.GetStream();
            if (stream != null)
            {
                _dataHandler.SetStream(stream);
                return true;
            }
            return false;
        }
    }
}
