using System;
using System.Collections.Generic;
using System.IO;
using TsMap.Common;
using TsMap.FileSystem.Hash;
using TsMap.FileSystem.Zip;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap.FileSystem
{
    public class UberFileSystem
    {
        private static readonly Lazy<UberFileSystem> _instance = new Lazy<UberFileSystem>(() => new UberFileSystem());
        public static UberFileSystem Instance => _instance.Value;

        internal Dictionary<ulong, UberFile> Files { get; } = new Dictionary<ulong, UberFile>();
        internal Dictionary<ulong, UberDirectory> Directories { get; } = new Dictionary<ulong, UberDirectory>();
        private readonly List<ArchiveFile> _archiveFiles = new List<ArchiveFile>();

        /// <summary>Archive, die nicht gelesen werden konnten (Pfad, Grund) - werden übersprungen.</summary>
        public List<KeyValuePair<string, string>> FailedSources { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Reads and adds a single file to the filesystem
        /// Checks if file is an SCS Hash file, if not, assumes it's a zip file
        /// </summary>
        /// <param name="path">Path for the archive file to add</param>
        /// <returns>Whether or not the file was parsed correctly</returns>
        public bool AddSourceFile(string path)
        {
            try
            {
                if (AddSourceFileCore(path)) return true;
                FailedSources.Add(new KeyValuePair<string, string>(path, "nicht lesbar (Details im Log)"));
            }
            catch (Exception e)
            {
                // z.B. "geschützte" Mods mit absichtlich beschädigten Tabellen: überspringen statt abbrechen
                Logger.Instance.Error($"Could not load '{path}': {e.GetType().Name}: {e.Message}");
                FailedSources.Add(new KeyValuePair<string, string>(path, e.GetType().Name + ": " + e.Message));
            }
            return false;
        }

        private bool AddSourceFileCore(string path)
        {
            if (!File.Exists(path))
            {
                Logger.Instance.Error($"Could not find file '{path}'");
                return false;
            }
            var buff = new byte[4];
            using (var f = File.OpenRead(path))
            {
                f.Seek(0, SeekOrigin.Begin);
                MemoryHelper.ReadExactly(f, buff, 0, 4); // read magic bytes (first 4 bytes of file)
            }
            if (BitConverter.ToUInt32(buff, 0) == Consts.ScsMagic)
            {
                var hashFile = new HashArchiveFile(path);
                if (hashFile.Parse())
                {
                    _archiveFiles.Add(hashFile);
                    return true;
                }
                else
                {
                    Logger.Instance.Error($"Could not load hash file '{path}'");
                    return false;
                }
            }
            else
            {
                var zipFile = new ZipArchiveFile(path);
                if (zipFile.Parse())
                {
                    _archiveFiles.Add(zipFile);
                    return true;
                }
                else
                {
                    Logger.Instance.Error($"Could not load zip file '{path}'");
                    return false;
                }
            }

        }

        /// <summary>
        /// Adds all files from the specified directory matching the filter to the filesystem
        /// </summary>
        /// <param name="path">Path to the directory where to find the files to include</param>
        /// <param name="searchPattern">Search pattern to select specific files eg. "*.scs"</param>
        /// <returns>Whether or not all files were added successfully</returns>
        /// <summary>
        /// Adds an unpacked mod folder (containing def/, map/, ...) as a source
        /// </summary>
        public bool AddSourceFolder(string path)
        {
            var folder = new Folder.FolderArchiveFile(path);
            if (!folder.Parse()) return false;
            _archiveFiles.Add(folder);
            return true;
        }

        public bool AddSourceDirectory(string path, string searchPattern = "*.scs")
        {
            if (!Directory.Exists(path))
            {
                Logger.Instance.Error($"Could not find directory '{path}'");
                return false;
            }
            var scsFilesPaths = Directory.GetFiles(path, searchPattern);

            var result = true;

            foreach (var scsFilePath in scsFilesPaths)
            {
                var fileResult = AddSourceFile(scsFilePath);
                if (!fileResult) result = false;
            }
            return result;
        }

        /// <summary>
        /// Tries to find the directory by the given path
        /// </summary>
        /// <param name="path">Path for the wanted directory</param>
        /// <returns>
        /// <see cref="UberDirectory"/> for the given path
        /// <para>Null if path was not found</para>
        /// </returns>
        public UberDirectory GetDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return null; // z.B. Material ohne Textur
            UberDirectory first = null;
            List<UberDirectory> all = null;
            foreach (var hash in CandidateHashes(path))
            {
                UberDirectory dir;
                if (!Directories.TryGetValue(hash, out dir)) continue;
                if (first == null) first = dir;
                else (all ?? (all = new List<UberDirectory> { first })).Add(dir);
            }
            // Gleicher Ordner in Archiven mit und ohne Salt: Inhalte zusammenführen
            return all == null ? first : UberDirectory.Merge(all);
        }

        private readonly List<ushort> _salts = new List<ushort>();

        /// <summary>
        /// HashFS-Archive können einen Salt haben: er wird als Dezimalzahl vor den Pfad gesetzt, bevor
        /// gehasht wird (u.a. von geschützten Mods genutzt). Das Spiel berücksichtigt ihn - wir auch.
        /// </summary>
        internal void RegisterSalt(ushort salt)
        {
            if (salt != 0 && !_salts.Contains(salt)) _salts.Add(salt);
        }

        /// <summary>Hashes eines Pfads: zuerst mit Salts (später geladene Archive zuerst), dann ohne.</summary>
        private IEnumerable<ulong> CandidateHashes(string path)
        {
            var local = PathHelper.EnsureLocalPath(path);
            for (var i = _salts.Count - 1; i >= 0; i--)
                yield return CityHash.CityHash64(_salts[i].ToString(System.Globalization.CultureInfo.InvariantCulture) + local);
            yield return CityHash.CityHash64(local);
        }

        /// <summary>
        /// Tries to find the directory by the given hash
        /// </summary>
        /// <param name="pathHash">Hash for the wanted directory</param>
        /// <returns>
        /// <see cref="UberDirectory"/> for the given hash
        /// <para>Null if hash was not found</para>
        /// </returns>
        public UberDirectory GetDirectory(ulong pathHash)
        {
            if (Directories.ContainsKey(pathHash))
            {
                return Directories[pathHash];
            }
            return null;
        }

        /// <summary>
        /// Tries to find the file by a given path
        /// </summary>
        /// <param name="path">Path for the wanted file</param>
        /// <returns>
        /// <see cref="UberFile"/> for the given path
        /// <para>Null if path was not found</para>
        /// </returns>
        public UberFile GetFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null; // z.B. Material ohne Textur
            foreach (var hash in CandidateHashes(path))
            {
                UberFile file;
                if (Files.TryGetValue(hash, out file)) return file;
            }
            return null;
        }

        /// <summary>
        /// Tries to find the file by a given hash
        /// </summary>
        /// <param name="pathHash">Hash for the wanted file</param>
        /// <returns>
        /// <see cref="UberFile"/> for the given hash
        /// <para>Null if hash was not found</para>
        /// </returns>
        public UberFile GetFile(ulong pathHash)
        {
            if (Files.ContainsKey(pathHash))
            {
                return Files[pathHash];
            }
            return null;
        }
    }
}
