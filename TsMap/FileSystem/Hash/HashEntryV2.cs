using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using TsMap.FileSystem.libdeflate;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap.FileSystem.Hash
{
    public class HashEntryV2 : Entry
    {
        internal ImgMetadata? _imgMetadata;
        internal PlainMetadata _plainMetadata;
        internal SampleMetadata? _sampleMetadata;

        // Ergebnis der Lesbarkeitsprüfung bei unbekannter Kompression: -1 = ungeprüft, 0 = nein, 1 = ja
        private int _readable = -1;
        private byte[] _probeData;

        // Pro Archiv und Methode nur einmal melden, wie (bzw. dass nicht) entpackt werden konnte
        private static readonly ConcurrentDictionary<string, bool> Reported = new ConcurrentDictionary<string, bool>();

        public HashEntryV2(HashArchiveFile fsFile) : base(fsFile)
        {
        }

        internal uint Flags { get; set; }

        /// <summary>Metadaten-Typ "Directory" (geschützte Archive löschen teils das Flag)</summary>
        internal bool DirectoryMetadata { get; set; }

        /// <summary>Ordner-Flag im Eintrag selbst</summary>
        internal bool DirectoryFlag => MemoryHelper.IsBitSet(Flags >> 16, 0);

        public override byte[] Read()
        {
            var probe = _probeData;
            if (probe != null)
            {
                _probeData = null;
                return probe;
            }
            var buff = MemoryHelper.ReadBytes(GetArchiveFile().Br, (long) GetOffset(), (int) GetCompressedSize());
            return IsCompressed() ? Inflate(buff) : buff;
        }

        protected override byte[] Inflate(byte[] buff)
        {
            try
            {
                var dest = new byte[(int) GetSize()];
                if (_plainMetadata.CompressionMethod == HashFsCompressionMethod.Zlib)
                {
                    var decompressor = LibdeflateWrapper.libdeflate_alloc_decompressor();
                    if (decompressor == IntPtr.Zero)
                    {
                        Logger.Instance.Error(
                            $"Could not init zlib decompressor for entry {GetHash()} (0x{GetHash():x}) of '{GetArchiveFile().GetPath()}'");
                        return Array.Empty<byte>();
                    }

                    var result = LibdeflateWrapper.libdeflate_zlib_decompress(decompressor, buff,
                        (UIntPtr) GetCompressedSize(),
                        dest,
                        (UIntPtr) GetSize(), out var bytesWritten);

                    LibdeflateWrapper.libdeflate_free_decompressor(decompressor);

                    if (result != libdeflate_result.LIBDEFLATE_SUCCESS)
                    {
                        Logger.Instance.Error(
                            $"Could not extract zlib entry {GetHash()} (0x{GetHash():x}) of '{GetArchiveFile().GetPath()}'");
                        return Array.Empty<byte>();
                    }

                    if (GetSize() != bytesWritten.ToUInt32())
                    {
                        Logger.Instance.Error(
                            $"Possible incorrect zlib inflate for entry {GetHash()} (0x{GetHash():x}) of '{GetArchiveFile().GetPath()}', {bytesWritten} bytes written out of {GetSize()}");
                    }
                }
                else if (_plainMetadata.CompressionMethod == HashFsCompressionMethod.Deflate)
                {
                    // sonst die übrigen Verfahren durchprobieren (meldet sich einmal je Archiv)
                    if (!TryDeflate(buff, 0, dest)) return InflateUnknown(buff);
                }
                else if (_plainMetadata.CompressionMethod == HashFsCompressionMethod.Gdeflate)
                {
                    var result = Gdeflate.Inflate(ref dest, GetSize(), buff, GetCompressedSize());

                    if (result != libdeflate_result.LIBDEFLATE_SUCCESS)
                    {
                        Logger.Instance.Error(
                            $"Could not extract gdeflate entry {GetHash()} (0x{GetHash():x}) of '{GetArchiveFile().GetPath()}'");
                        return Array.Empty<byte>();
                    }
                }
                else
                {
                    return InflateUnknown(buff);
                }

                return dest;
            }
            catch (Exception e)
            {
                Logger.Instance.Error(
                    $"Could not inflate hash entry: 0x{GetHash():X}, of '{GetArchiveFile().GetPath()}', reason: {e.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Unbekannte Methode (geschützte Mods nutzen z.B. 2): bekannte Verfahren durchprobieren und
        /// nur ein Ergebnis mit exakt der erwarteten Größe akzeptieren.
        /// </summary>
        private byte[] InflateUnknown(byte[] buff)
        {
            var method = (int) _plainMetadata.CompressionMethod;
            var archive = GetArchiveFile().GetPath();
            var size = (int) GetSize();

            string how = null;
            var dest = new byte[size];
            if (TryZlib(buff, dest)) how = "zlib";
            else if (TryDeflate(buff, 0, dest)) how = "raw deflate";
            else if (TryDeflate(buff, 2, dest)) how = "deflate ohne zlib-Kopf";
            else if (TryLz4Block(buff, dest)) how = "LZ4";

            var key = archive + "|" + method + "|" + (how ?? "-");
            if (how != null)
            {
                if (Reported.TryAdd(key, true))
                    Logger.Instance.Info($"Compression method {method} in '{Path.GetFileName(archive)}' decoded as {how}");
                return dest;
            }

            var head = BitConverter.ToString(buff, 0, Math.Min(16, buff.Length));
            var msg = $"Unsupported compression method {method} for entry 0x{GetHash():x} of '{Path.GetFileName(archive)}' " +
                      $"({GetCompressedSize()} -> {size} bytes, starts with {head}); using lower-priority version if available";
            if (Reported.TryAdd(key, true)) Logger.Instance.Warning(msg);
            else Logger.Instance.Debug(msg);
            return Array.Empty<byte>();
        }

        private static bool TryZlib(byte[] src, byte[] dest)
        {
            try
            {
                var decompressor = LibdeflateWrapper.libdeflate_alloc_decompressor();
                if (decompressor == IntPtr.Zero) return false;
                var result = LibdeflateWrapper.libdeflate_zlib_decompress(decompressor, src, (UIntPtr) src.Length,
                    dest, (UIntPtr) dest.Length, out var bytesWritten);
                LibdeflateWrapper.libdeflate_free_decompressor(decompressor);
                return result == libdeflate_result.LIBDEFLATE_SUCCESS && bytesWritten.ToUInt64() == (ulong) dest.Length;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        private static bool TryDeflate(byte[] src, int skip, byte[] dest)
        {
            if (src.Length <= skip) return false;
            try
            {
                using (var ms = new MemoryStream(src, skip, src.Length - skip))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    var read = 0;
                    while (read < dest.Length)
                    {
                        var n = ds.Read(dest, read, dest.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return read == dest.Length && ds.Read(new byte[1], 0, 1) == 0;
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is NotSupportedException || e is IOException)
            {
                return false;
            }
        }

        /// <summary>LZ4-Block (ohne Frame) - muss genau <paramref name="dest"/> füllen</summary>
        private static bool TryLz4Block(byte[] src, byte[] dest)
        {
            int s = 0, d = 0;
            while (s < src.Length)
            {
                int token = src[s++];
                var lit = token >> 4;
                if (lit == 15)
                {
                    int b;
                    do
                    {
                        if (s >= src.Length) return false;
                        b = src[s++];
                        lit += b;
                    } while (b == 255);
                }
                if (s + lit > src.Length || d + lit > dest.Length) return false;
                Buffer.BlockCopy(src, s, dest, d, lit);
                s += lit;
                d += lit;
                if (s >= src.Length) break; // letzte Sequenz hat nur Literale

                if (s + 2 > src.Length) return false;
                var offset = src[s] | (src[s + 1] << 8);
                s += 2;
                if (offset == 0 || offset > d) return false;
                var len = token & 15;
                if (len == 15)
                {
                    int b;
                    do
                    {
                        if (s >= src.Length) return false;
                        b = src[s++];
                        len += b;
                    } while (b == 255);
                }
                len += 4;
                if (d + len > dest.Length) return false;
                for (var k = 0; k < len; k++, d++) dest[d] = dest[d - offset];
            }
            return d == dest.Length;
        }

        /// <summary>
        /// Lässt sich der Eintrag entpacken? Bei unbekannter Kompression wird es einmal ausprobiert
        /// (das Ergebnis wird für den nächsten <see cref="Read"/> aufgehoben).
        /// </summary>
        public override bool IsReadable()
        {
            if (!IsCompressed()) return true;
            var method = _plainMetadata.CompressionMethod;
            if (method == HashFsCompressionMethod.Zlib || method == HashFsCompressionMethod.Gdeflate) return true;
            if (_readable < 0)
            {
                var data = Read();
                var ok = data.Length == GetSize() && data.Length > 0;
                if (ok) _probeData = data;
                _readable = ok ? 1 : 0;
            }
            return _readable == 1;
        }

        public override bool IsDirectory()
        {
            return DirectoryFlag || DirectoryMetadata;
        }

        public override bool IsCompressed()
        {
            return GetSize() != GetCompressedSize();
        }

        public override uint GetSize()
        {
            return _plainMetadata.Size;
        }

        public override uint GetCompressedSize()
        {
            return _plainMetadata.CompressedSize;
        }

        public override ulong GetOffset()
        {
            return _plainMetadata.Offset;
        }
    }
}
