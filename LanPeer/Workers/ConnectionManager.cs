using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using LanPeer.Interfaces;
using LanPeer.Managers;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using static System.Net.WebRequestMethods;
using FluFTP = FluentFTP;

namespace LanPeer.Workers
{
    /// <summary>
    /// Manages and controls all incoming and outgoing connection requests and authentication. 
    /// Controls and monitors the ftp server.
    /// </summary>
    internal class ConnectionManager : BackgroundService , IConnectionManager 
    {
        #region Variable declarations
        private readonly ICodeManager _codeManager;
        private readonly IDataHandler _dataHandler;
        private readonly IQueueManager _queueManager;
        private readonly IDiscoveryWorker _discoveryWorker;
        private readonly IServiceProvider _serviceProvider;
        private readonly IFtpServer _IftpServer;
        private readonly int authPort = 50001;  //handshake happens here 
        private readonly int commsPort = 50002; //communicate on this like state change etc
        private readonly int transPort = 50003; //move data on this
        private int retryAttempts = 5;
        private readonly FtpServer? _ftpServer;
        public event Action<bool>? IsConnected; //only for the file transfer connection.
        public delegate void IsConnectedEventHandler();
        private TransferMode transferMode;
        private Peer? activePeer;
        private string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "LanPeer"); //very temporary. this will be loaded from save data
        private string tempPath;
        #endregion

        public ConnectionManager(ICodeManager codeManager, IDataHandler dataHandler, 
            IQueueManager queueManager, IDiscoveryWorker discoveryWorker, IServiceProvider serviceProvider, IFtpServer IftpServer) 
        {
            _codeManager = codeManager;
            _dataHandler = dataHandler;
            _queueManager = queueManager;
            _discoveryWorker = discoveryWorker;
            _serviceProvider = serviceProvider;
            _IftpServer = IftpServer;
            try
            {
                _ftpServer = ((FtpServer)_IftpServer);
            }
            catch
            {
                Console.WriteLine($"[Exception] A cast to Ftpserver failed. Object is null");
            }

            tempPath = Path.Combine(downloadsPath, "temp");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = ListenForIncoming(stoppingToken);
            var commsListener = ListenForComms(stoppingToken);
            var tarListener = ListenForTarTransfer(stoppingToken); //need this here to route data.
            var ftpMonitor = MonitorFtpServer(stoppingToken);
            await Task.WhenAll(listener, commsListener, tarListener, ftpMonitor);
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
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(address, authPort); //connect to auth port

                    using NetworkStream stream = client.GetStream();

                    RequestToken token = new RequestToken() //send our info
                    {
                        Type = RequestType.Authenticating,
                        SenderId = _discoveryWorker.GetMyId(),
                        DeviceName = Environment.MachineName,
                        ReceiverId = _discoveryWorker.GetPeerFromAddress(address).Id, //very bad hack fix this, broadcast more data to fill this in
                        CommsPort = commsPort,
                        TransPort = transPort,
                        Code = code,
                        IpAddress = address,
                    };

                    await JsonSerializer.SerializeAsync(stream, token);
                    await stream.FlushAsync();

                    var result = await JsonSerializer.DeserializeAsync<AuthResponse>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (result != null)
                    {
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
        /// Starts the ftp server if ftp is selected.
        /// </summary>
        /// <param name="peer">The peer to connect to</param>
        /// <param name="mode">The transfer mode to use</param>
        /// <returns>IsConnected</returns>
        public async Task<bool> ConnectToPeer(Peer peer, TransferMode mode) //update to add connection based on transfer mode
        {
            bool isConnected;
            FtpCreds creds;
            transferMode = mode; //fix this everywhere
            if (!_queueManager.PeerExists(peer)) // check if the peer is in trusted before connecting
            {
                Console.WriteLine("Peer is not trusted! Perform auth first");
                return false;
            }
            if (mode == TransferMode.TAR) //In tar either peer can connect in true peer-to-peer fashion
            {
                isConnected  = await ConnectAndSetStream(peer.IpAddress, peer.TransPort);
            }
            else //In Ftp, start the server and let the peer connect to it.
            {
                creds = await StartFtpServerForPeer(peer, downloadsPath);
                if (creds != null && _IftpServer.Ready)
                {
                    try
                    {
                        var ack = new CommsData
                        {
                            Creds = creds
                        };
                        await SendConnectionResponse<CommsData>(peer, ack, true);

                        //TcpClient client = new TcpClient();
                        //await client.ConnectAsync(peer.IpAddress, peer.TransPort);
                        //var stream = client.GetStream();
                        //await JsonSerializer.SerializeAsync(stream, creds);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[Exception] An exception occured. Details: {e.Message}");
                        isConnected = false;
                    }
                    isConnected = true;
                }
                else
                {
                    isConnected = false;
                }
            }

            if (isConnected)
                activePeer = peer;

            IsConnected?.Invoke(isConnected);
            return isConnected;
        }

        public List<IFtpConnection> GetActiveConnections()
        {
            if (_ftpServer != null)
            {
                return _ftpServer.GetConnections().ToList();
            }
            return new List<IFtpConnection>();
        }

        #region Listeners
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

                                        //IsConnected?.Invoke(true); //fire event
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

                                        //IsConnected?.Invoke(false);
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
        private async Task ListenForComms(CancellationToken token) //fix this asap!
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
                                var commsData = await JsonSerializer.DeserializeAsync<CommsData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); //check if peer exists
                                var peer = _queueManager.GetPeerFromId(commsData.Id);
                                if (activePeer == null) 
                                {
                                    if (peer == null)
                                    {
                                        return;
                                    }
                                    else if (commsData != null && commsData.Mode != transferMode && commsData.Mode == TransferMode.FTP) //connect to the peer (commsdata is there & transfermode is diff & its ftp now)
                                    {
                                        FluFTP.FtpClient client = new FluFTP.FtpClient(commsData.Creds.IpAddress, (int)commsData.Creds.Port);
                                        client.Credentials = new NetworkCredential(commsData.Creds.UserName, commsData.Creds.Password);
                                        int attempts = 0;
                                        do
                                        {
                                            client.Connect();
                                            attempts++;
                                            await Task.Delay(2000); //wait 2 seconds 
                                        }
                                        while (!client.IsConnected && attempts <= retryAttempts);

                                        if (client.IsConnected)
                                            IsConnected?.Invoke(true);

                                        else
                                        {
                                            IsConnected?.Invoke(false);
                                            Console.WriteLine($"[Info] Unable to connect to the server.");
                                        }
                                    }
                                    else if (commsData != null && commsData.Mode != transferMode && commsData.Mode == TransferMode.TAR) //(commsdata is there & transfermode is diff & its ttar now)
                                    {
                                        _dataHandler.DisposeFtpClient(); //don't need to do nothin, peer will connect to us.
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Error] An error occurred in the connection manager. Details: {ex.Message}");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] An error occurred. Details: {ex.Message}");
            }
        }

        private async Task ListenForTarTransfer(CancellationToken token) //fix this
        {
            var listener = new TcpListener(IPAddress.Any, transPort);
            if (transferMode == TransferMode.TAR)
            {
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
                                    var stream = client.GetStream();
                                    var ftr = await JsonSerializer.DeserializeAsync<FileTransferRequest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                    if (ftr != null && _queueManager.PeerExists(ftr.PeerId)) //check hash too 
                                    {
                                        var ack = new FileTransferAck
                                        {
                                            Id = _discoveryWorker.GetMyId(),
                                            response = "OK",
                                        };
                                        activePeer = _queueManager.GetPeerFromId(ftr.PeerId);
                                        await JsonSerializer.SerializeAsync(stream, ack);
                                        await stream.FlushAsync();

                                        _dataHandler.SetStream(stream);
                                        await _dataHandler.ReceiveFilesAsync();
                                    }
                                    else
                                    {
                                        await stream.DisposeAsync();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Exception] An exception was handled in connection manager. Details: {ex.Message}");
                                }
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Exception] An exception was handled in connection manager. Details: {ex.Message}");
                }
            }
        }

        private async Task MonitorFtpServer(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _IftpServer.Ready) //only run when the server is on.
            {
                    var connections = GetActiveConnections();

                    if (connections.Count() > 0)
                    {
                        foreach (var con in connections)
                        {
                            if (activePeer != null && con.RemoteEndPoint.Address.ToString() != activePeer.IpAddress)
                            {
                                await con.StopAsync(); //drop any retard who is not supposed to be here.
                            }
                            else
                            {
                                IsConnected?.Invoke(true);
                                activePeer = _queueManager.GetPeerFromId(con.RemoteEndPoint.Address.ToString());
                                Console.WriteLine($"[Info] Peer connected to ftp server. Details: {activePeer}");
                            }
                        }
                    }
                    else
                    {
                        //IsConnected?.Invoke(false);
                    }
                await Task.Delay(500, token);
            }
        }
        #endregion

        private async Task<FtpCreds> StartFtpServerForPeer(Peer peer, string downloadFolder) //no need to add to the interface
        {
            string username = $"user_{Guid.NewGuid():N}".Substring(0, 8);
            string password = Guid.NewGuid().ToString("N").Substring(0, 12);
            int port = GetAvailablePort();
            string IpAddress = GetLocalIpForFtp(peer.IpAddress);

            var ftpOptions = _serviceProvider.GetRequiredService<IOptions<FtpServerOptions>>().Value;
            ftpOptions.ServerAddress = IpAddress;
            ftpOptions.Port = port;
            ftpOptions.MaxActiveConnections = 2;

            var ftpServerHost = _serviceProvider.GetRequiredService<IFtpServerHost>();

            if (_IftpServer.Status != FtpServiceStatus.Running)
                await ftpServerHost.StartAsync();

            var membership = _serviceProvider.GetRequiredService<MembershipManager>();
            membership.AddUser(username, password);

            Console.WriteLine("[Info] Ftp Server started.");
            Console.WriteLine(ftpServerHost.ToString());
            return new FtpCreds
            {
                UserName = username,
                Password = password,
                Port = port,
                IpAddress = IpAddress,
            };
        }
                    
        /// <summary>
        /// Informs the peer of the new transfer mode, awaits response then connects to peer.
        /// </summary>
        /// <param name="mode">The mode to switch to</param>
        /// <param name="peer">The peer to switch with</param>
        /// <returns>True if switch successful, False if something went wrong</returns>
        public async Task<bool> ChangeTransferMode(TransferMode mode, Peer peer) //this is broken //not anymore!
        {
            CommsAck response = new();
            try
            {
                if (mode == TransferMode.TAR)
                {
                    var commsData = new CommsData
                    {
                        Id = _discoveryWorker.GetMyId(),
                        IpAddress = peer.IpAddress,
                        TransferPort = peer.TransPort,
                        Creds = new(),
                        Mode = TransferMode.TAR,
                    };

                    await SendConnectionResponse<CommsData>(peer, commsData, true);

                    if (await ConnectToPeer(peer, TransferMode.TAR))
                    {
                        //shut down ftp server
                        if (_IftpServer.Status == FtpServiceStatus.Running)
                            await _IftpServer.StopAsync(CancellationToken.None);
                    }
                }
                else if (mode == TransferMode.FTP)
                {
                    var ftpCreds = await StartFtpServerForPeer(peer, downloadsPath);
                    var commsData = new CommsData
                    {
                        Id = _discoveryWorker.GetMyId(),
                        IpAddress = peer.IpAddress,
                        TransferPort = peer.TransPort,
                        Creds = new FtpCreds
                        {
                            UserName = ftpCreds.UserName,
                            Password = ftpCreds.Password,
                            IpAddress = ftpCreds.IpAddress,
                            Port = ftpCreds.Port,
                        },
                        Mode = TransferMode.FTP,
                    };

                    await SendConnectionResponse<CommsData>(peer, commsData, true);

                    //the peer may or may not be connected at this point.
                    //close the connection anyways. the server isn't going anywhere
                    if (_IftpServer.Ready)
                    {
                        _dataHandler.DisposeStream();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Exception] An exception was caught. Details: {e.Message}");
            }
            //connect to peer and inform
            //try
            //{
            //    using var client = new TcpClient();
            //    await client.ConnectAsync(peer.IpAddress, peer.CommsPort); //communicate to peer that the transfer mode has been changed
            //    using NetworkStream stream = client.GetStream();
            
            //    var commsData = new CommsData()
            //    {
            //        Id = _discoveryWorker.GetMyId(),
            //        IpAddress = _discoveryWorker.GetMyAddress(),
            //        TransferPort = transPort,
            //        Mode = mode,
            //    };

            //    await JsonSerializer.SerializeAsync(stream, commsData);
            //    await stream.FlushAsync();

            //    response = await JsonSerializer.DeserializeAsync<CommsAck>(stream) ?? new CommsAck();
            //    if (response == null)
            //    {
            //        Console.WriteLine($"[Error] An error occurred. No response from Peer: {peer.Id}");
            //        return false;
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"[Exception] An error occurred when communcating with the peer. Details: {ex.Message}");
            //    return false;
            //}
            //if (mode == TransferMode.FTP)
            //{
            //    //fix this nightmare
            //    var creds = await StartFtpServerForPeer(peer, downloadsPath); //start your own server
            //    var data = new CommsData
            //    {
            //        Id = _discoveryWorker.GetMyId(),
            //        IpAddress = _discoveryWorker.GetMyAddress(),
            //        TransferPort = transPort,
            //        Mode = TransferMode.FTP,
            //        Creds = creds
            //    };
            //    await SendConnectionResponse<CommsData>(peer, data, true);
            //    ////add validation for model here
            //    //var ftpClient = new FtpClient(response.Creds.IpAddress, response.Creds.UserName, response.Creds.Password);
            //    //await Task.Run(() =>
            //    //{
            //    //    ftpClient.Connect();
            //    //});
            //    //if (ftpClient.IsConnected && ftpClient.IsAuthenticated)
            //    //{
            //    //    await _dataHandler.DisposeStream();
            //    //    transferMode = TransferMode.FTP;
            //    //    _ftpClient = ftpClient;
            //    //    IsConnected?.Invoke(true);
            //    //    return true;
            //    //}
            //}
            //else
            //{
            //    if (await ConnectToPeer(peer, TransferMode.TAR))
            //    {
            //        if (_ftpServer != null && _ftpServer.Status == FtpServiceStatus.Running)
            //        {
            //            await _ftpServer.StopAsync(CancellationToken.None); //shut down ftp
            //        }
            //        return true;
            //    }
            //}
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
        #region Utility
        private int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
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
        /// <summary>
        /// Sends the provided data to the peer through the comms port.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="peer"></param>
        /// <param name="data"></param>
        /// <param name="useCommsPort"></param>
        /// <returns></returns>
        private static async Task SendConnectionResponse<T>(Peer peer, T data, bool useCommsPort) //confused unga bunga
        {
            if (data != null)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(peer.IpAddress, peer.CommsPort);
                    using var stream = client.GetStream();
                    await JsonSerializer.SerializeAsync(stream, data, data.GetType());
                    await stream.FlushAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Exception] An Exception Occurred! Details:{e.Message}");
                }
            }
        }
        #endregion
    }
}
