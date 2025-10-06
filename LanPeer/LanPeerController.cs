using LanPeer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer
{
    [ApiController]
    public sealed class LanPeerController : ControllerBase
    {
        private readonly PeerHandshake peerHandshake;

        private readonly IDataHandler dataHandler;

        //need interfaces here to call the independentely running services.
        public LanPeerController(IDataHandler _dataHandler)
        {
            dataHandler = _dataHandler;
        }

        public IActionResult GetActiveCode()
        {
            return Ok();
        }
    }
    
}
