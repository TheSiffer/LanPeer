using LanPeer.DataModels;
using System.Security.Cryptography;

using System.Text.Json;


namespace LanPeer
{
    public static class ManifestBuilder
    {
        public static ManifestNode Build(string inputPath)
        {
            if (File.Exists(inputPath))
            {
                var fileInfo = new FileInfo(inputPath);
                return CreateFileNode(fileInfo, fileInfo.DirectoryName ?? "");
            }
            else if (Directory.Exists(inputPath))
            {
                var root = new DirectoryInfo(inputPath);
                return BuildRecursive(root, root.FullName);
            }
            else
            {
                throw new FileNotFoundException($"Path not found: {inputPath}");
            }
        }

        private static ManifestNode BuildRecursive(DirectoryInfo dir, string rootPath)
        {
            var relativePath = Path.GetRelativePath(rootPath, dir.FullName);
            if (!string.IsNullOrEmpty(relativePath))
                relativePath = ".";

            var folderNode = new ManifestNode
            {
                Name = dir.Name,
                RelativePath = relativePath.Replace("\\", "/"),
                IsFolder = true,
                Children = new List<ManifestNode>()
            };

            foreach (var file in dir.GetFiles())
            {
                folderNode.Children!.Add(CreateFileNode(file, rootPath));
            }

            foreach (var subDir in dir.GetDirectories())
            {
                folderNode.Children!.Add(BuildRecursive(subDir, rootPath));
            }
            return folderNode;
        }

        private static ManifestNode CreateFileNode(FileInfo file, string rootPath)
        {
            var relativePath = Path.GetRelativePath(rootPath, file.FullName);

            return new ManifestNode
            {
                Name = file.Name,
                RelativePath = relativePath.Replace("\\", "/"),
                IsFolder = false,
                Size = file.Length,
                Checksum = ComputeChecksum(file.FullName)
            };
        }

        private static string ComputeChecksum(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static void SaveToJson(ManifestNode manifestRoot, string outputFile)
        {
            var json = JsonSerializer.Serialize(manifestRoot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outputFile, json);
        }
        public static List<ManifestNode> Flatten(ManifestNode root)
        {
            var flatList = new List<ManifestNode>();
            FlattenRecursive(root, flatList);
            return flatList;
        }

        private static void FlattenRecursive(ManifestNode node, List<ManifestNode> list)
        {
            if (!node.IsFolder)
            {
                list.Add(node);
            }
            else if (node.Children != null)
            {
                foreach (var child in node.Children)
                    FlattenRecursive(child, list);
            }
        }
    }
}
