using System;
using System.IO;
using TsMap.Helpers.Logger;

namespace TsMap.FileSystem.Folder
{
    /// <summary>
    /// An unpacked mod (a plain directory with def/, map/, ... inside) used as a source,
    /// e.g. Steam Workshop packages or mods that were never packed into an .scs file.
    /// </summary>
    public class FolderArchiveFile : ArchiveFile
    {
        public FolderArchiveFile(string path) : base(path) { }

        public override bool Parse()
        {
            if (!Directory.Exists(_path))
            {
                Logger.Instance.Error($"Could not find directory {_path}");
                return false;
            }

            var root = Path.GetFullPath(_path).TrimEnd('\\', '/');
            var count = 0;

            foreach (var dirPath in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                var name = ToLocalPath(root, dirPath);
                var hash = CityHash.CityHash64(name);
                if (!UberFileSystem.Instance.Directories.ContainsKey(hash))
                {
                    UberFileSystem.Instance.Directories.Add(hash, new UberDirectory());
                }

                var parentDir = GetOrCreateDirectory(GetParentPath(name));
                if (!parentDir.GetSubDirectoryNames().Contains(Path.GetFileName(name)))
                {
                    parentDir.AddSubDirName(Path.GetFileName(name));
                }
                count++;
            }

            foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = ToLocalPath(root, filePath);
                var entry = new FolderEntry(this, filePath)
                {
                    Hash = CityHash.CityHash64(name),
                };
                var parentDir = GetOrCreateDirectory(GetParentPath(name));

                if (UberFileSystem.Instance.Files.ContainsKey(entry.GetHash()))
                {
                    UberFileSystem.Instance.Files[entry.GetHash()] = new UberFile(entry);
                }
                else
                {
                    parentDir.AddSubFileName(Path.GetFileName(name));
                    UberFileSystem.Instance.Files.Add(entry.GetHash(), new UberFile(entry));
                }
                count++;
            }

            Logger.Instance.Info($"Mounted folder '{_path}' with {count} entries");
            return true;
        }

        private static UberDirectory GetOrCreateDirectory(string path)
        {
            var hash = CityHash.CityHash64(path);
            UberDirectory dir;
            if (!UberFileSystem.Instance.Directories.TryGetValue(hash, out dir))
            {
                dir = new UberDirectory();
                UberFileSystem.Instance.Directories.Add(hash, dir);
            }
            return dir;
        }

        private static string ToLocalPath(string root, string fullPath)
        {
            return fullPath.Substring(root.Length).Replace('\\', '/').TrimStart('/');
        }

        private static string GetParentPath(string localPath)
        {
            var idx = localPath.LastIndexOf('/');
            return idx < 0 ? "" : localPath.Substring(0, idx);
        }
    }

    public class FolderEntry : Entry
    {
        private readonly string _filePath;

        public FolderEntry(FolderArchiveFile fsFile, string filePath) : base(fsFile)
        {
            _filePath = filePath;
        }

        public override byte[] Read()
        {
            return File.ReadAllBytes(_filePath);
        }

        protected override byte[] Inflate(byte[] buff)
        {
            throw new NotSupportedException("Files in folders are never compressed");
        }

        public override uint GetSize()
        {
            return (uint)new FileInfo(_filePath).Length;
        }

        public override uint GetCompressedSize()
        {
            return GetSize();
        }

        public override bool IsCompressed()
        {
            return false;
        }

        public override bool IsDirectory()
        {
            return false;
        }
    }
}
