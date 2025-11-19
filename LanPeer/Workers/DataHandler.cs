using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using LanPeer.Helpers;
using LanPeer.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LanPeer.Workers
{
    internal sealed class DataHandler : BackgroundService , IDataHandler
    {
        /// <summary>
        /// Stream will only be set by the connection manager
        /// Only the connection manager listens for incoming requests and sends connection requests.
        /// </summary>
        private Stream? _stream;
        private readonly object _lock = new();
        private int bufferSize = 81920; // defaulted to ~80kb
        private string transferId = string.Empty;
        private string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "LanPeer");
        private string tempPath; //asign default here
        private string zipPrefix = "Compressed - ";
        private string[] compressedExts = { ".zip", ".exe", ".jpg", ".png", ".mp4", ".avi" };
        public event Action<bool>? IsZipped;
        public event Action<bool>? IsExtracted;

        private readonly IQueueManager _queueManager;

        private static Queue<FileTransferItem> fileQueue = new Queue<FileTransferItem>();

        private CancellationToken token;

        public DataHandler(IQueueManager queueManager)
        {
            _queueManager = queueManager;
            tempPath = Path.Combine(downloadsPath, "temp");
            //Instance = this;
        }

        public void SetStream(Stream stream)
        {
            lock (_lock)
            {
                if(_stream != null)
                {
                    try
                    {
                        _stream.Dispose();
                    }
                    catch { }
                }
                _stream = stream;
            }
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

        /// <summary>
        /// Starts sending all files one at a time from the queue.
        /// </summary>
        /// <returns>Task</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task SendFilesAsync()
        {
            //if (_stream == null)
            //{
            //    throw new InvalidOperationException("stream is not initialized");
            //}
            FileTransferItem item;

            //iterate over items in queue
            foreach (FileTransferItem file in _queueManager.GetFileQueue())
            {
                if (file.IsSent)
                {
                    continue;
                }
                file.State = TransferState.InProgress; //may not need to do this leave it at pending until after sending metadata

                item = file;

                int chunkIndex = 0;
                //string fileName = file.FileName;
                //string relPath = file.DestPath; // where the file is headed
                //string fullPath = file.FullPath; //full path to the actual file/folder
                //TransferState fileState = file.State;
                //string fileHash;
                Directory.CreateDirectory(tempPath);
                string zipName = string.Concat(zipPrefix, file.FileName, ".zip");
                string zipPath = Path.Combine(tempPath, zipName); //temp\zipname.zip
                //long fileSize = file.FileSize;
                long totalChunks = (file.FileSize + bufferSize - 1) / bufferSize; //number of chunks

                //start zipping
                await CreateZip(file.FullPath, zipPath);

                var fileInfo = new FileInfo(zipPath);

                //compute hash
                //fileHash = 

                Metadata metadata = new Metadata()
                {
                    FileName = file.FileName,
                    FileSize = file.FileSize,
                    FileHash = CreateHash(file.FullPath),
                    TotalChunks = totalChunks,
                    State = TransferState.InProgress,
                    ZipName = fileInfo.Name,
                    ZipHash = CreateHash(zipPath),
                    ZipSize = fileInfo.Length
                    //Manifest = file.SubFiles,
                };
                var json = JsonSerializer.Serialize(metadata);
                //var metadata = $"{fileName}|{fileSize}|{fileHash}|{totalChunks}|{fileState}";

                byte[] metaBytes = Encoding.UTF8.GetBytes(json);  // + "\n" removed

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
                    using (var fileStream = File.OpenRead(zipPath))
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
                    Console.WriteLine($"[SendFile] Completed sending {file.FileName} ({file.FileSize} bytes");// Hash: {file.Hash}");
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

        public async Task ReceiveFilesAsync()
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("stream is not initialized");
            }
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);

                    using var memStream = new MemoryStream();
                    byte[] metaBuffer = new byte[8192];
                    int bytesRead = await _stream.ReadAsync(metaBuffer, 0, metaBuffer.Length, token);
                    if (bytesRead == 0)
                        return;

                    memStream.Write(metaBuffer, 0, bytesRead);
                    memStream.Position = 0;

                    //retrieve metadata
                    var metadataJson = Encoding.UTF8.GetString(memStream.ToArray()); 
                    var metadata = JsonSerializer.Deserialize<Metadata>(metadataJson);
                    //string savePath;
                    //var manifestNode = metadata.Manifest; //iterate over this and create folders

                    //create folder
                    Console.WriteLine($"[Received] Metadata received for {metadata.FileName}");
                    //downloadsPath = Path.Combine(downloadsPath, "Lanpeer");
                    //savePath = Path.Combine(tempPath, metadata.FileName); //create a folder 
                    //Directory.CreateDirectory(savePath);

                    //create filestream to dest and open file
                    string localPath = Path.Combine(tempPath, metadata.ZipName); //includes name! //string.Concat(tempPath, @"\", metadata.ZipName); 
                    await using var fileStream = File.Create(localPath);
                    int expectedChunk = 0;
                    bool completed = false;
                    
                    while (!token.IsCancellationRequested)
                    {
                        var packet = _stream.Deserialize();
                        //string targetFile = Path.Combine(basePath, packet.)

                        if (packet == null)
                            break;

                        if (packet.state == TransferState.Failed)
                        {
                            Console.WriteLine("[Receiver] Sender aborted transfer");
                            break;
                        }

                        if (packet.Data == null || packet.Data.Length == 0)
                        {
                            completed = true;
                            break;
                        }
                        if (packet.ChunkIndex != expectedChunk)
                        {
                            Console.WriteLine($"[Receiver] Out of order chunk. Expected {expectedChunk}, received {packet.ChunkIndex}");
                        }
                        await fileStream.WriteAsync(packet.Data, 0, packet.Data.Length, token);
                        expectedChunk++;
                    }
                    await fileStream.FlushAsync(token);

                    // Zip Hash validation
                    bool verified = ValidateHash(localPath, metadata.ZipHash);

                    await ExtractZip(localPath, downloadsPath); //should include the parent folder and create that automatically.

                    // validate folder hash
                    if (verified) {
                        verified = ValidateHash(localPath, metadata.FileHash);
                    }

                    //send ack 
                    var ackPacket = new AckPacket
                    {
                        TransferId = Guid.NewGuid().ToString(),
                        State = verified ? TransferState.Verified : TransferState.Failed
                    };

                    byte[] ackBytes = ackPacket.SerializeAck();
                    await _stream.WriteAsync(ackBytes, 0, ackBytes.Length, token);
                    await _stream.FlushAsync();

                    Console.WriteLine($"[Receiver] Ack sent for file {metadata.FileName}");
                } 
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"[Receiver] Error occured when receiving file: {ex.Message}");
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
        private async Task CreateZip(string path, string dest)
        {
            await Task.Run(() => {
                if (Directory.Exists(path))
                {
                    ZipFile.CreateFromDirectory(path, dest, CompressionLevel.NoCompression, includeBaseDirectory: true);
                }
                else if (File.Exists(path)) // stupid shit because Zipfile cant archive a single file without bitching.
                {
                    if (compressedExts.Contains(Path.GetExtension(path).ToLower())) //no need to waste cpu on already bundled files
                    {
                        using (FileStream fs = new FileStream(dest, FileMode.Create))
                        {
                            using (ZipArchive arch = new ZipArchive(fs, ZipArchiveMode.Create))
                            {
                                arch.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.NoCompression); //takes a while otherwise
                            }
                        }
                    }
                    else
                    {
                        using (FileStream fs = new FileStream(dest, FileMode.Create))
                        {
                            using (ZipArchive arch = new ZipArchive(fs, ZipArchiveMode.Create))
                            {
                                arch.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.NoCompression); // this might take a little while
                            }
                        }
                    }
                }
            });
            IsZipped?.Invoke(true);
        }
        
        private async Task ExtractZip(string path, string dest)
        {
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(path, dest);
            });
            IsExtracted?.Invoke(true);
        }

        private string CreateHash(string fullPath) 
        {
            string fileHash;
            using (var sha = SHA256.Create())
            {
                using (var fs = File.OpenRead(fullPath))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            return fileHash;
        }
        private bool ValidateHash(string path, string fileHash)
        {
            bool verified = false;
            using (var sha = SHA256.Create())
            {
                using (var fs = File.OpenRead(path))
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    verified = hash == fileHash;
                    Console.WriteLine($"[Receiver] File verification: {(verified ? "Success" : "Failed)}")}");
                }
            }
            return verified;
        }
    }
}
