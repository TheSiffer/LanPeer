using LanPeer.DataModels;
using LanPeer.Interfaces;
using Microsoft.AspNetCore.Mvc;


namespace LanPeer
{
    [ApiController]
    [Route("api/lanpeer")]
    public sealed class LanPeerController : ControllerBase
    {
        private readonly IDataHandler dataHandler;
        private readonly IConnectionManager connManager;
        private readonly ICodeManager codeManager;

        //need interfaces here to call the independentely running services.
        public LanPeerController(IDataHandler _dataHandler, IConnectionManager _connManager, ICodeManager _codeManager)
        {
            dataHandler = _dataHandler;
            connManager = _connManager;
            codeManager = _codeManager;
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

        [HttpPost("peer")]
        public IActionResult ConnectToPeer(Peer peer)
        {
            if (connManager.ConnectToPeer(peer))
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
        public IActionResult SetBufferSize(int size)
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
    }

}
