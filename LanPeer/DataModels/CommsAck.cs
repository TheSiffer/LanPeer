using System;

namespace LanPeer.DataModels
{
    public class CommsAck
    {
        public string? Id { get; set; }
        public string? IpAddress { get; set; }
        public string? IpPort { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
