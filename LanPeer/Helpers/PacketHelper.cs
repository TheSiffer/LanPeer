using LanPeer.DataModels;
using LanPeer.DataModels.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.Helpers
{
    public static class PacketHelper
    {
        /// <summary>
        /// Serializes a ChunkPacket into byte array
        /// </summary>
        /// <param name="packet">The packet to serialize</param>
        /// <returns>Byte array</returns>
        public static byte[] Serialize(this ChunkPacket packet)
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
                using (var finalWriter = new BinaryWriter(finalStream))
                {
                    finalWriter.Write(payload.Length);
                    finalWriter.Write(payload);
                    return finalStream.ToArray();
                }
            }
        }
        /// <summary>
        /// Serializes an AckPacket into byte array
        /// </summary>
        /// <param name="packet">The packet to serialize</param>
        /// <returns>Byte array</returns>
        public static byte[] SerializeAck(this AckPacket packet)
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
        /// <summary>
        /// Deserializes a Stream into a Chunk packet.
        /// For ChunkPacket Only!!!
        /// </summary>
        /// <param name="stream">The stream to Deserialize</param>
        /// <returns>ChunkPacket</returns>
        public static ChunkPacket Deserialize(this Stream stream)
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
        /// <summary>
        /// Deserializes a Stream into an AckPacket.
        /// For AckPacket Only!!!
        /// </summary>
        /// <param name="stream">The stream to Deserialize</param>
        /// <returns>ChunkPacket</returns>
        public static AckPacket DeserializeAck(this Stream stream)
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
    }
}
