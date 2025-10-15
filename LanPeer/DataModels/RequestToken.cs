using LanPeer.DataModels.Data;

namespace LanPeer.DataModels
{
    public class RequestToken
    {
        public RequestType Type { get; set; }

        public string SenderId { get; set; } = string.Empty;//sender

        public string DeviceName { get; set; } = string.Empty;

        public string ReceiverId { get; set; } = string.Empty;//receiver

        public int CommsPort { get; set; }

        public int Code { get; set; }

        public string IpAddress { get; set; } = string.Empty;
    }
}
