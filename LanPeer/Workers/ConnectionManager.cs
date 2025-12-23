using FluentFTP;
using FubarDev.FtpServer;
using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using LanPeer.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
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
        private readonly IServiceProvider _serviceProvider;
        private readonly int authPort = 50001; //handshake happens here 
        private readonly int commsPort = 50002; //communicate on this like state change etc
        private readonly int transPort = 50003; //move data on this
        public event Action<bool>? IsConnected;
        public delegate void IsConnectedEventHandler();
        private TransferMode transferMode;
        private FtpClient _ftpClient;
        private Peer activePeer;

        public ConnectionManager(ICodeManager codeManager, IDataHandler dataHandler, 
            IQueueManager queueManager, IDiscoveryWorker discoveryWorker, IServiceProvider serviceProvider) 
        {
            _codeManager = codeManager;
            _dataHandler = dataHandler;
            _queueManager = queueManager;
            _discoveryWorker = discoveryWorker;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = ListenForIncoming(stoppingToken);
            var commsListener = ListenForComms(stoppingToken);
            await Task.WhenAll(listener);
        }
        /// <summary>
        /// Starts authentication with a discovered peer.
        /// </summary>
        /// <param name="address">Ip address of the peer</param>
        /// <param name="code">Authentication code</param>
        /// <returns></returns>
        public async Task<bool> InitiateAuth(string address, int code) //port is set in stone.
        {
            if (!string.IsNullOrEmpty(address))
            {
                try {
                    var client = new TcpClient();
                    await client.ConnectAsync(address, authPort); //connect to auth port

                    using NetworkStream stream = client.GetStream();

                    RequestToken token = new RequestToken() //send our info
                    {
                        Type = RequestType.Authenticating,
                        SenderId = _discoveryWorker.GetMyId(),
                        DeviceName = Environment.MachineName,
                        ReceiverId = Guid.NewGuid().ToString(), //very bad hack fix this, broadcast more data to fill this in
                        CommsPort = commsPort,
                        TransPort = transPort,
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
                            Peer peer = new Peer() //receive peer's info
                            {
                                Id = Guid.NewGuid().ToString(),
                                //set name
                                Name = result.Name,
                                IpAddress = address,
                                CommsPort = result.CommsPort,
                                TransPort = result.TransPort,
                            };
                            return await ConnectAndSetStream(address, result.TransPort); //if auth successful open a connection on the peer defined comms port
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
        public async Task<bool> ConnectToPeer(Peer peer) //update to add connection based on transfer mode
        {
            if (!_queueManager.PeerExists(peer)) // check if the peer is in trusted before connecting
            {
                Console.WriteLine("Peer is not trusted! Perform auth first");
                return false;
            }
            var isConnected = await ConnectAndSetStream(peer.IpAddress, peer.TransPort);

            if (isConnected)
                activePeer = peer;

            IsConnected?.Invoke(isConnected);
            return isConnected;
        }

        //listens only on the auth port.
        /// <summary>
        /// Listens for incoming authentication requests and authenticates on the auth port
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task ListenForIncoming(CancellationToken token) //this is just for authentication! connection to peer is manually done.
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

                                var requestToken = await JsonSerializer.DeserializeAsync<RequestToken>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                
                                if (requestToken != null)
                                {
                                    if (_codeManager.Validate(requestToken.Code.ToString()))
                                    {
                                        var response = new AuthResponse()
                                        {
                                            Name = Environment.MachineName,
                                            Status = "AUTH_OK",
                                            CommsPort = commsPort,
                                            TransPort = transPort,
                                        };
                                        await JsonSerializer.SerializeAsync(stream, response);

                                        IsConnected?.Invoke(true); //fire event
                                        Peer newPeer = new Peer
                                        {
                                            Id = requestToken.SenderId,
                                            Name = requestToken.DeviceName,
                                            IpAddress = requestToken.IpAddress,
                                            CommsPort = requestToken.CommsPort,
                                            TransPort = requestToken.TransPort,
                                        };
                                        _queueManager.AddPeer(newPeer);
                                        //await stream.DisposeAsync(); disposes automatically
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
                                        //await stream.DisposeAsync(); //eh? why close the stream, we need this
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
        /// <summary>
        /// Listens for any incoming requests on the comms port for state changes
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task ListenForComms(CancellationToken token)
        {
            //add peer validation here.
            var listener = new TcpListener(IPAddress.Any, commsPort);
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
                                var commsData = await JsonSerializer.DeserializeAsync<CommsData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (commsData != null && commsData.Mode == TransferMode.FTP) //start the server
                                {
                                    await ChangeTransferMode(commsData.Mode, activePeer);
                                    if (transferMode == TransferMode.FTP)
                                    {

                                    }
                                }
                            }
                            catch (Exception)
                            {

                            }
                        }
                    });
                }
            }
            catch (Exception e)
            {

            }
        }
        public async Task StartFtpServerForPeer(Peer peer, string downloadFolder)
        {
            string username = $"user_{Guid.NewGuid():N}".Substring(0, 8);
            string password = Guid.NewGuid().ToString("N").Substring(0, 12);
            int port = GetAvailablePort();

            var ftpOptions = _serviceProvider.GetRequiredService<IOptions<FtpServerOptions>>().Value;
            ftpOptions.ServerAddress = GetLocalIpForFtp(peer.IpAddress);

            var ftpServerHost = _serviceProvider.GetRequiredService<IFtpServerHost>();

            await ftpServerHost.StartAsync();

            Console.WriteLine("[Info] Ftp Server started.");
            Console.WriteLine(ftpServerHost.ToString());
        }
                    
        /// <summary>
        /// Switches the transfer mode, reconnects and informs the peer
        /// </summary>
        /// <param name="mode">The mode to switch to</param>
        /// <param name="peer">The peer to switch with</param>
        /// <returns>True if switch successful, False if something went wrong</returns>
        public async Task<bool> ChangeTransferMode(TransferMode mode, Peer peer)
        {
            CommsAck response = new();
            //connect to peer and inform
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(peer.IpAddress, peer.CommsPort); //communicate to peer that the transfer mode has been changed
                using NetworkStream stream = client.GetStream();
            
                var commsData = new CommsData()
                {
                    Id = _discoveryWorker.GetMyId(),
                    IpAddress = _discoveryWorker.GetMyAddress(),
                    TransferPort = transPort,
                    Mode = mode,
                };
            

                await JsonSerializer.SerializeAsync(stream, commsData);
                await stream.FlushAsync();

                response = await JsonSerializer.DeserializeAsync<CommsAck>(stream) ?? new CommsAck();
                if (response == null)
                {
                    Console.WriteLine($"[Error] An error occurred. No response from Peer: {peer.Id}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Exception] An error occurred when communcating with the peer. Details: {ex.Message}");
                return false;
            }
            if (mode == TransferMode.FTP)
            {
                //add validation for model here
                var ftpClient = new FtpClient(response.IpAddress, response.Username, response.Password);
                await Task.Run(() =>
                {
                    ftpClient.Connect();
                });
                if (ftpClient.IsConnected && ftpClient.IsAuthenticated)
                {
                    await _dataHandler.DisposeStream();
                    transferMode = TransferMode.FTP;
                    _ftpClient = ftpClient;
                    IsConnected?.Invoke(true);
                    return true;
                }
            }
            else
            {
                if (await ConnectToPeer(peer))
                {
                    if (_ftpClient != null && _ftpClient.IsConnected)
                    {
                        _ftpClient.Disconnect();
                    } 
                    return true;
                }   
            }
            return false;
        }

        /// <summary>
        /// Connects to a client and sets the stream
        /// </summary>
        /// <param name="address">Address of the client</param>
        /// <param name="port">The port to connect on</param>
        /// <returns></returns>
        private async Task<bool> ConnectAndSetStream(string address, int port)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(address, port);
                NetworkStream stream = client.GetStream(); //dont dispose this shit with using.
                if (stream != null)
                {
                    _dataHandler.SetStream(stream);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] An error occured while connecting. Details: {ex.Message}");
            }
            return false;
        }
        private int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        private static string GetLocalIpForFtp(string peerIp)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect(peerIp, 65530);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        } 
    }
}
