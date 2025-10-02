using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer
{
    internal class CliWorker : BackgroundService
    {

        private readonly PeerHandshake peerHandshake;

        public CliWorker(PeerHandshake _peerHandshake)
        {
            peerHandshake = _peerHandshake;
        }
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("CLI ready. Type 'help' for commands.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLower();

                switch (cmd)
                {
                    case "help":
                        Console.WriteLine("Available commands:");
                        //Console.WriteLine("  discover     - Show peers discovered");
                        Console.WriteLine("  connect X    - Start handshake with peer X");
                        Console.WriteLine("  quit         - Exit program");
                        break;

                    //case "discover":
                    //    //var peers = _discovery.GetKnownPeers(); // you’d add a method for this
                    //    foreach (var peer in peers)
                    //        Console.WriteLine($"Peer: {peer.Id} ({peer.DeviceName})");
                    //    break;

                    case "connect":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: connect <peerId>");
                            break;
                        }
                        var peerId = parts[1];
                        //await _handshake.ConnectAsync(peerId, stoppingToken);
                        break;

                    case "quit":
                        Console.WriteLine("Exiting...");
                        Environment.Exit(0);
                        break;

                    default:
                        Console.WriteLine($"Unknown command: {cmd}");
                        break;
                }
            }
        }
    }
    }
}
