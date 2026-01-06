using System;

namespace LanPeer.DataModels
{
    public class CommsAck
    {
        public string? Id { get; set; }
        //public string? IpAddress { get; set; }
        //public int? Port { get; set; }
        //public string? Username { get; set; }
        //public string? Password { get; set; }
        public FtpCreds Creds = new FtpCreds();
    }
}
