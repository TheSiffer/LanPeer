using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.Interfaces
{
    public interface ICodeManager
    {
        public Task<string> ForceRegenerate();
        public bool Validate(string code);
        public string? GetCode();
    }
}
