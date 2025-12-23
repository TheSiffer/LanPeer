using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LanPeer.DataModels
{
    public class ParentFile
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public string FileManifest { get; set; }
        public long FileSize { get; set; }
        List<ManifestNode>? subFiles {  get; set; }
    }
}
