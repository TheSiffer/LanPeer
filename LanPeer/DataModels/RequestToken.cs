using LanPeer.DataModels.Data;

namespace LanPeer.DataModels
{
    public class RequestToken
    {
        public RequestType Type { get; set; }

        public string? SenderId { get; set; } //sender

        public string? DeviceName { get; set; }

        public string? ReceiverId { get; set; } //receiver

        public int? CommsPort { get; set; }

        public int? Code { get; set; }
    }
}
