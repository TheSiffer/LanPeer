using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LanPeer
{
    class PeerHandshake : BackgroundService
    {
        //private readonly UdpClient udp;
        private readonly int listenPort = 15000; // communicate handshake over tcp on this port
        private readonly string peerId;
        private TcpListener? _listener;
        private int CommsPort; //data transfer port
        //private readonly ConcurrentDictionary<string, string> challenges = new();
        private readonly PeerHandshake _handshake;



        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await StartAsync(stoppingToken);
        }

        public PeerHandshake() // add peerid parameter
        {
            peerId = Guid.NewGuid().ToString();
           //udp = new UdpClient(listenPort);
           //_handshake = new PeerHandshake();
        }

        public async Task StartAsync(CancellationToken token)
        {
            _listener = new TcpListener(IPAddress.Any, listenPort);
            _listener.Start();
            Console.WriteLine($"[Handshake] Listening on port {listenPort}");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    //DataHandler client.GetStream();
                    //_ = HandleClientAsync(client, token); //fire and forget
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task SendConnectionRequestAsync(IPAddress address, int port, CancellationToken token) //we get the peer ip and port from discovery
        {
            try
            {
                //IPEndPoint endPoint = new IPEndPoint(long.Parse(address.ToString()), port);
                TcpClient client = new TcpClient();
                int retry = 0;

                do 
                {
                    Console.WriteLine($"[Attempt {retry + 1}] Connecting to {address}:{port}...");
                    await client.ConnectAsync(address, port);
                    retry++;
                } 
                while (!client.Connected || retry >= 10); //break if connection is made or the retry count is over

                if (client.Connected)
                {
                    Console.WriteLine($"Connection request to {address} successful. Connected on port {port}");
                    // call the method to receive or send data.
                }
                else
                {
                    Console.WriteLine("Connection failed after 10 tries");
                }
            }
            catch (Exception ex )
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                using (var stream = client.GetStream())
                {
                    //read incoming request
                    var buffer = new byte[4096];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);

                    if (bytesRead > 0)
                    {
                        var json = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        var request = JsonSerializer.Deserialize<RequestToken>(json);

                        if(request != null)
                        {
                            Console.WriteLine($"[Handshake] Received {request.Type}");

                            //route to handshake logic
                            ProcessRequestAsync(request, stream, token);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// for both inbound and outbound connections
        /// </summary>
        /// <param name="request"></param>
        /// <param name="stream"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private void ProcessRequestAsync(RequestToken request, NetworkStream stream, CancellationToken token)
        {
            RequestType reqType = RequestType.NewConnection;
            bool inbound = false;
            string challengecode;
            challengecode = CodeManager.instance.GetCode();

            switch (request.Type)
            {
                //if this pc is the receiver
                case RequestType.NewConnection:
                    reqType = RequestType.Handshake;

                    //prompt user the code to enter
;
                    inbound = true;
                    Console.WriteLine($"New connection request from [{request.DeviceName}], type: [{request.Type}], Challenge Code: [{challengecode}]");  
                    break;

                //if this pc is the sender
                case RequestType.Handshake:
                    //prompt user to enter the code
                    reqType = RequestType.Authentication; //only send when code is entered
                    inbound = false;
                    Console.WriteLine($"Handshake request sent to [{request.DeviceName}], type: [{request.Type}]");
                    //try to authenticate
                    break;
                
                case RequestType.Authentication:
                    //check code against base 
                    if(String.Equals(request.Code.ToString(), challengecode))
                    {
                        reqType = RequestType.Authenticated;

                        var listener = new TcpListener(IPAddress.Any, 0); // 0 = OS assigns free port
                        listener.Start();
                        CommsPort = ((IPEndPoint)listener.LocalEndpoint).Port;

                        Console.WriteLine($"Authenticated pc: [{request.DeviceName}], type: [{request.Type}, Port: [{CommsPort}]");
                    }
                    else
                    {
                        Console.WriteLine($"Authenticating pc: [{request.DeviceName}], incorrect challenge code.");
                        reqType = RequestType.Handshake;
                    }
                    break;

                //case RequestType.Authenticated:

                //    reqType = RequestType.Authenticated;
                //    Console.WriteLine($"Pc: [{request.DeviceName}] Authenticated.");
                //    break;

                ////if this pc is the sender.
                //case RequestType.FileTransfer:
                //    Console.WriteLine($"Pc [{request.DeviceName}] authenticated. Ready for transfer. Type: [{request.Type}]");
                //    break;

            }

            var response = new RequestToken
            {
                //SenderId = peerId,             //id of the requesting pc
                Type = reqType,                 //type of request
                ReceiverId = request.ReceiverId,  //device id of this pc, the sender will know this due to broadcasts
                DeviceName = Environment.MachineName,
                CommsPort = CommsPort
            };
        }

    }
}
