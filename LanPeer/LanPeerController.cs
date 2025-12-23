using LanPeer.DataModels;
using LanPeer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System;


namespace LanPeer
{
    [ApiController]
    [Route("api/lanpeer")]
    public sealed class LanPeerController : ControllerBase
    {
        private readonly IDataHandler dataHandler;
        private readonly IConnectionManager connManager;
        private readonly ICodeManager codeManager;
        private readonly IQueueManager queueManager;

        //need interfaces here to call the independentely running services.
        public LanPeerController(IDataHandler _dataHandler, IConnectionManager _connManager, ICodeManager _codeManager, IQueueManager _queueManager)
        {
            dataHandler = _dataHandler;
            connManager = _connManager;
            codeManager = _codeManager;
            queueManager = _queueManager;
        }
        #region AuthCode
        

        [HttpGet("code")]
        public IActionResult GetActiveCode()
        {
            var code = codeManager.GetCode();
            return Ok(code);
        }

        [HttpGet("forceregen")]
        public async Task<IActionResult> ForceRegenerateCode()
        {
            string code = string.Empty;
            try
            {
                code = await codeManager.ForceRegenerate();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return BadRequest(ex.Message);
            }
            return Ok(code);
        }

        [HttpGet("validate")]
        public IActionResult Validate(string code)
        {
            if (codeManager.Validate(code))
            {
                return Ok(code);
            }
            return BadRequest();
        }
        #endregion

        [HttpPost("{id:guid}")]
        public async Task<ActionResult> ConnectToPeer(Guid id)
        {
            var peer = queueManager.GetPeerFromId(id.ToString());
            if (peer != null && await connManager.ConnectToPeer(peer))
            {
                return Ok(true);
            }
            return BadRequest();
        }

        #region DataHandler

        [HttpGet("transferid")]
        public IActionResult GetActiveTransferId()
        {
            var code = dataHandler.GetActiveTransferId();
            if (!string.IsNullOrEmpty(code))
            {
                return Ok(code);
            }
            return BadRequest();
        }

        [HttpPost("size")]
        public IActionResult SetBufferSize([FromBody] int size)
        {
            dataHandler.SetBufferSize(size);
            return Ok();
        }

        [HttpGet("buffersize")]
        public IActionResult GetBufferSize()
        {
            int size = dataHandler.GetBufferSize();
            return Ok(size);
        }

        [HttpPost("begintransfer")]
        public async Task<IActionResult> SendFilesAsync()
        {
            await dataHandler.SendFilesAsync();
            return Ok();
        }

        #endregion

        #region Queue
        [HttpPost("path")]
        public async Task<IActionResult> EnqueueFile(string path)
        {
            await queueManager.EnQueue(path);
            return Ok();
        }

        [HttpGet("queue")]
        public IActionResult GetQueuedFiles()
        {
            var queue = queueManager.GetFileQueue();
            return Ok(queue);
        }

        [HttpGet("peer/{id:guid}")]
        public IActionResult PeerExists(Guid id)
        {
            var peer = queueManager.GetPeerFromId(id.ToString());
            if (peer != null)
            {
                return Ok(peer);
            }
            return BadRequest();
        }

        [HttpGet("peers")]
        public IActionResult GetPeers()
        {
            return Ok(queueManager.GetSavedPeers());
        }

        [HttpPost("peer")]
        public IActionResult AddPeer(Peer peer)
        {
            queueManager.AddPeer(peer);
            return Ok(peer);
        }

        [HttpDelete("peer")]
        public IActionResult RemovePeer(Peer peer)
        {
            queueManager.DeletePeer(peer);
            return Ok(peer);
        }
        #endregion
    }

}
