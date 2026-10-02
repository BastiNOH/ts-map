using System.IO;
using System.IO.Compression;
using TsMap.Helpers;

namespace TsMap.FileSystem.Zip
{
    public class ZipEntry : Entry
    {
        public ZipEntry(ZipArchiveFile fsFile) : base(fsFile)
        {
        }

        public override byte[] Read()
        {
            var buff = MemoryHelper.ReadBytes(GetArchiveFile().Br, (long)GetOffset(), (int)GetCompressedSize());
            return IsCompressed() ? Inflate(buff) : buff;
        }

        protected override byte[] Inflate(byte[] buff)
        {
            var inflatedBytes = new byte[GetSize()];
            using (var ms = new MemoryStream(buff))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                // Stream.Read darf weniger liefern als angefordert (unter .NET Core/.NET 5+ bei
                // DeflateStream die Regel) -> bis zum Ende lesen, sonst bleibt der Rest leer.
                MemoryHelper.ReadExactly(ds, inflatedBytes, 0, inflatedBytes.Length);

                return inflatedBytes;
            }
        }

        public override bool IsDirectory()
        {
            return GetCompressedSize() == 0;
        }

        public override bool IsCompressed()
        {
            return GetCompressedSize() != GetSize();
        }
    }
}
