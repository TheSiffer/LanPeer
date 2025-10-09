using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class ManifestNode
    {
        //for files and folders
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public bool IsFolder { get; set; }

        // Only for files
        public long? Size { get; set; }
        public string? Checksum { get; set; }

        public List<ManifestNode>? Children { get; set; }
    }
}
