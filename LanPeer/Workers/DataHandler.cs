using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using LanPeer.Helpers;
using LanPeer.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace LanPeer.Workers
{
    internal sealed class DataHandler : BackgroundService , IDataHandler
    {
        private Stream? _stream;

        private static readonly object _lock = new object();

        private int bufferSize = 81920; // defaulted to ~80kb

        private string transferId;
        public static DataHandler? Instance { get; private set; }

        private static Queue<FileTransferItem> fileQueue = new Queue<FileTransferItem>();

        private CancellationToken token;

        public DataHandler() { }

        public void SetStream(Stream stream)
        {
            _stream = stream;
        }

        public string GetActiveTransferId()
        {
            return transferId;
        }

        public void SetBufferSize(int size)
        {
            bufferSize = size;
        }

        public int GetBufferSize()
        {
            return bufferSize;
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
            foreach (FileTransferItem file in QueueManager.Instance.GetFileQueue())
            {
                if (file.IsSent)
                {
                    continue;
                }
                file.State = TransferState.InProgress; //may not need to do this leave it at pending until after sending metadata

                item = file;

                int chunkIndex = 0;
                string fileName = file.FileName;
                string relPath = file.DestPath;
                string fullPath = file.FullPath;
                TransferState fileState = file.State;
                string fileHash;
                long fileSize = file.FileSize;
                long totalChunks = (file.FileSize + bufferSize - 1) / bufferSize; //number of chunks

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
                            byte[] packed = packet.Serialize();
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
                    byte[] endPacked = endPacket.Serialize();
                    await _stream.WriteAsync(endPacked, 0, endPacked.Length, token);
                    await _stream.FlushAsync();
                    file.IsSent = true;

                    using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
                    var ackPacket = _stream.DeserializeAck();

                    if(ackPacket.State == TransferState.Verified)
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
    }
}
