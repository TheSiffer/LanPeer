using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace LanPeer
{
    internal sealed class DataHandler : BackgroundService
    {
        private readonly Stream _stream;

        private static readonly object _lock = new object();

        private int bufferSize = 81920; // defaulted to ~80kb

        private string transferId;
        public static DataHandler? Instance { get; private set; }

        private static Queue<FileTransferItem> fileQueue = new Queue<FileTransferItem>();

        private CancellationToken token;

        private DataHandler(Stream stream)
        {
            _stream = stream;
        }

        public static DataHandler GetDataHandler(Stream stream)
        {
            lock (_lock)
            {
                if (Instance == null)
                {
                    Instance = new DataHandler(stream);
                }
                return Instance;
            }
        }

        public string GetActiveTranferId()
        {
            return transferId;
        }

        public void SetBufferSize(int size)
        {
            bufferSize = size;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            token = stoppingToken;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        public async Task SendFilesAsync()
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("stream is not initialized");
            }
            FileTransferItem item;

            //iterate over items in queue
            foreach (FileTransferItem file in fileQueue)
            {
                if (file.IsSent)
                {
                    continue;
                }
                file.State = TransferState.InProgress; //may not need to do this leave it at pending until after sending metadata

                item = file;

                int chunkIndex = 0;
                string fileName = file.FileName;
                string relPath = file.RelativePath;
                string fullPath = file.FullPath;
                TransferState fileState = file.State;
                string fileHash;
                long fileSize = file.Size;
                long totalChunks = (file.Size + bufferSize - 1) / bufferSize; //number of chunks

                //compute hash
                using (var sha = SHA256.Create())
                {
                    using (var fs = File.OpenRead(fullPath))
                    {
                        byte[] hash = sha.ComputeHash(fs);
                        fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }

                var metadata = $"{fileName}|{fileSize}|{fileHash}|{totalChunks}|{fileState}";


                byte[] metaBytes = Encoding.UTF8.GetBytes(metadata + "\n");

                //send metadata
                await _stream.WriteAsync(metaBytes, 0, metaBytes.Length);
                await _stream.FlushAsync();
                Console.WriteLine($"[SendFile] Metadata sent: {metadata}");

                transferId = Guid.NewGuid().ToString();

                byte[] buffer = new byte[bufferSize];

                try
                {
                    //set filestate to InProgress
                    file.State = TransferState.InProgress;
                    using (var fileStream = File.OpenRead(fullPath))
                    {
                        int bytesRead;
                        ChunkPacket packet;
                        bool IsCancelled = false;
                        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0) //removed token passing into read
                        {
                            if (token.IsCancellationRequested)
                            {
                                CreateFailPacket(out packet);
                                IsCancelled = true;
                            }
                            else
                            {
                                packet = new ChunkPacket
                                {
                                    TransferId = transferId,
                                    ChunkIndex = chunkIndex,
                                    TotalChunks = totalChunks,
                                    state = file.State,
                                    Data = buffer.Take(bytesRead).ToArray()
                                };
                            }
                            byte[] packed = Serialize(packet);
                            await _stream.WriteAsync(packed, 0, packed.Length); //removed token passing into write
                            if (IsCancelled)
                            {
                                throw new OperationCanceledException();
                            }
                            chunkIndex++;
                        }

                    }

                    file.State = TransferState.Completed;
                    var endPacket = new ChunkPacket
                    {
                        TransferId = transferId,
                        ChunkIndex = chunkIndex,
                        TotalChunks = totalChunks,
                        state = file.State,
                        Data = Array.Empty<byte>()
                    };
                    byte[] endPacked = Serialize(endPacket);
                    await _stream.WriteAsync(endPacked, 0, endPacked.Length, token);
                    await _stream.FlushAsync();
                    file.IsSent = true;

                    using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
                    string? ack = await reader.ReadLineAsync();

                    if(ack?.StartsWith("ACK") == true)
                    {
                        Console.WriteLine($"[SendFile] Receiver confirmed file {file.FileName}");
                        file.IsVerified = true;
                        file.State = TransferState.Verified;
                    }
                    else
                    {
                        Console.WriteLine($"[SendFile] Transfer failed for file: {file.FileName}");
                        file.IsVerified = false;
                        file.State = TransferState.Failed;
                    }
                    Console.WriteLine($"[SendFile] Completed sending {fileName} ({fileSize} bytes). Hash: {fileHash}");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Application exit requested. Failure packet sent. Exiting...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occured in writing to stream. Details:{ex.Message}");
                }
                finally
                {
                    await _stream.FlushAsync();
                }
            }
        }

        private void CreateFailPacket(out ChunkPacket packet)
        {
            packet = new ChunkPacket
            {
                TransferId = Guid.Empty.ToString(),
                ChunkIndex = 0,
                TotalChunks = 0,
                state = TransferState.Failed,
                Data = Array.Empty<byte>(),
            };
        }
        public static byte[] Serialize(AckPacket packet)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write(packet.TransferId);
                bw.Write((int)packet.State);

                byte[] payload = ms.ToArray();

                using (var finalStream = new MemoryStream())
                using (var finalWriter = new BinaryWriter(finalStream))
                {
                    finalWriter.Write(payload.Length);
                    finalWriter.Write(payload);
                    return finalStream.ToArray();
                }
            }
        }
        public static AckPacket DeserializeAck(NetworkStream stream)
        {
            using (var br = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int length = br.ReadInt32();
                byte[] payload = br.ReadBytes(length);

                using (var ms = new MemoryStream(payload))
                using (var pr = new BinaryReader(ms, Encoding.UTF8, true))
                {
                    return new AckPacket
                    {
                        TransferId = pr.ReadString(),
                        State = (TransferState)pr.ReadInt32()
                    };
                }
            }
        }
        public static byte[] Serialize(ChunkPacket packet)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write(packet.TransferId);
                bw.Write(packet.ChunkIndex);
                bw.Write(packet.TotalChunks);
                bw.Write((int)packet.state);

                bw.Write(packet.Data.Length);
                bw.Write(packet.Data);

                byte[] payload = ms.ToArray();

                using (var finalStream = new MemoryStream())
                using(var finalWriter = new BinaryWriter(finalStream))
                {
                    finalWriter.Write(payload.Length);
                    finalWriter.Write(payload);
                    return finalStream.ToArray();
                }
            }
        }
        public static ChunkPacket Deserialize(NetworkStream stream)
        {
            using (var br = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int length = br.ReadInt32();

                byte[] payload = br.ReadBytes(length);

                using (var ms = new MemoryStream(payload))
                using (var pr = new BinaryReader(ms, Encoding.UTF8, true))
                {
                    var packet = new ChunkPacket
                    {
                        TransferId = pr.ReadString(),
                        ChunkIndex = br.ReadInt32(),
                        TotalChunks = br.ReadInt32(),
                        state = (TransferState)pr.ReadInt32()
                    };

                    int dataLength = pr.ReadInt32();
                    packet.Data = pr.ReadBytes(dataLength);

                    return packet;
                }
            }
        }
    }
}
