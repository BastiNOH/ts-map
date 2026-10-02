using System.IO;

namespace TsMap.FileSystem
{
    /// <summary>
    /// Represents an archive file (*.scs, *.zip)
    /// </summary>
    public abstract class ArchiveFile
    {
        protected readonly string _path;

        public BinaryReader Br { get; protected set; }

        private static int _mountCounter;

        /// <summary>Reihenfolge des Einhängens: später eingehängt = höhere Priorität (wie im Spiel).</summary>
        internal int MountOrder { get; }

        public ArchiveFile(string path)
        {
            _path = path;
            MountOrder = System.Threading.Interlocked.Increment(ref _mountCounter);
        }

        public abstract bool Parse();

        public string GetPath()
        {
            return _path;
        }

        ~ArchiveFile()
        {
            if (Br == null) return;
            Br.Close();
            Br = null;
        }
    }
}
