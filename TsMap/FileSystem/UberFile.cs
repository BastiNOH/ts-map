namespace TsMap.FileSystem
{
    /// <summary>
    /// Represents a file in the filesystem
    /// <para>Currently nothing more than just an <see cref="FileSystem.Entry"/> with how the workings of the filesystem changed,
    /// just keeping this in case I want to add something specific to files</para>
    /// <para>Is unaware of it's own location/path</para>
    /// </summary>
    public class UberFile
    {
        public Entry Entry { get; }

        /// <summary>
        /// Die Datei gleichen Namens aus einem früher eingehängten Archiv (niedrigere Priorität).
        /// Wird benutzt, wenn <see cref="Entry"/> nicht entpackt werden kann.
        /// </summary>
        public UberFile Fallback { get; set; }

        /// <summary>Erste lesbare Version dieser Datei (sie selbst oder ein Fallback), sonst sie selbst</summary>
        public UberFile Resolve()
        {
            for (var f = this; f != null; f = f.Fallback)
                if (f.Entry == null || f.Entry.IsReadable()) return f;
            return this;
        }

        public UberFile(Entry entry)
        {
            Entry = entry;
        }
    }
}
