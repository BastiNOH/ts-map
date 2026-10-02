using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using TsMap.FileSystem.libdeflate;

namespace TsMap.Helpers
{
    internal class MemoryHelper
    {
        /// <summary>
        /// Liest genau <paramref name="count"/> Bytes. Stream.Read darf weniger zurückgeben als angefordert.
        /// </summary>
        internal static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                var read = stream.Read(buffer, offset, count);
                if (read <= 0) throw new EndOfStreamException($"Unerwartetes Ende: {count} Bytes fehlen");
                offset += read;
                count -= read;
            }
        }

        internal static ushort ReadUInt16(BinaryReader br, long offset, SeekOrigin so = SeekOrigin.Begin)
        {
            br.BaseStream.Seek(offset, so);
            return br.ReadUInt16();
        }

        internal static uint ReadUInt32(BinaryReader br, long offset, SeekOrigin so = SeekOrigin.Begin)
        {
            br.BaseStream.Seek(offset, so);
            return br.ReadUInt32();
        }
        internal static int ReadInt32(BinaryReader br, long offset, SeekOrigin so = SeekOrigin.Begin)
        {
            br.BaseStream.Seek(offset, so);
            return br.ReadInt32();
        }

        internal static ulong ReadUInt64(BinaryReader br, long offset, SeekOrigin so = SeekOrigin.Begin)
        {
            br.BaseStream.Seek(offset, so);
            return br.ReadUInt64();
        }
        internal static long ReadInt64(BinaryReader br, long offset, SeekOrigin so = SeekOrigin.Begin)
        {
            br.BaseStream.Seek(offset, so);
            return br.ReadInt64();
        }

        internal static byte[] ReadBytes(BinaryReader br, long offset, int length, SeekOrigin so = SeekOrigin.Begin)
        {
            br.BaseStream.Seek(offset, so);
            return br.ReadBytes(length);
        }

        internal static string ReadString(BinaryReader br, long offset, int length, SeekOrigin so = SeekOrigin.Begin)
        {
            return Encoding.UTF8.GetString(ReadBytes(br, offset, length, so));
        }

        internal static byte ReadUint8(byte[] s, int pos)
        {
            return s[pos];
        }
        internal static sbyte ReadInt8(byte[] s, int pos)
        {
            return (sbyte)s[pos];
        }

        internal static unsafe ushort ReadUInt16(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(ushort*)(p + pos);
            }
        }
        internal static unsafe short ReadInt16(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(short*)(p + pos);
            }
        }

        internal static unsafe uint ReadUInt32(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(uint*)(p + pos);
            }
        }
        internal static unsafe int ReadInt32(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(int*)(p + pos);
            }
        }

        internal static unsafe float ReadSingle(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(float*)(p + pos);
            }
        }

        internal static unsafe ulong ReadUInt64(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(ulong*)(p + pos);
            }
        }
        internal static unsafe long ReadInt64(byte[] s, int pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(long*)(p + pos);
            }
        }

        internal static byte ReadUint8(byte[] s, uint pos)
        {
            return s[pos];
        }
        internal static sbyte ReadInt8(byte[] s, uint pos)
        {
            return (sbyte)s[pos];
        }

        internal static unsafe ushort ReadUInt16(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(ushort*)(p + pos);
            }
        }
        internal static unsafe short ReadInt16(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(short*)(p + pos);
            }
        }

        internal static unsafe uint ReadUInt32(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(uint*)(p + pos);
            }
        }
        internal static unsafe int ReadInt32(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(int*)(p + pos);
            }
        }

        internal static unsafe float ReadSingle(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(float*)(p + pos);
            }
        }

        internal static unsafe ulong ReadUInt64(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(ulong*)(p + pos);
            }
        }
        internal static unsafe long ReadInt64(byte[] s, uint pos)
        {
            fixed (byte* p = &s[0])
            {
                return *(long*)(p + pos);
            }
        }

        internal static string ReadString(byte[] s, int pos, int length)
        {
            return Encoding.UTF8.GetString(s, pos, length);
        }

        internal static string Decrypt3Nk(byte[] src) // from quickbms scsgames.bms script
        {
            if (src.Length < 0x05 || src[0] != 0x33 && src[1] != 0x6E && src[2] != 0x4B) return null;
            var decrypted = new byte[src.Length - 6];
            var key = src[5];

            for (var i = 6; i < src.Length; i++)
            {
                decrypted[i - 6] = (byte)(((((key << 2) ^ (key ^ 0xff)) << 3) ^ key) ^ src[i]);
                key++;
            }
            return Encoding.UTF8.GetString(decrypted);
        }

        internal static bool IsBitSet(uint flags, byte pos)
        {
            return (flags & (1 << pos)) != 0;
        }

        /// <summary>
        /// Wie <see cref="InflateZlib"/>, aber tolerant gegenüber "geschützten" Archiven: bei Fehler
        /// wird ohne zlib-Kopf/Prüfsumme entpackt (das Spiel prüft sie offenbar nicht) und notfalls
        /// werden die Daten als unkomprimiert angenommen. Liefert evtl. weniger als <paramref name="size"/> Bytes.
        /// </summary>
        internal static byte[] InflateZlibTolerant(byte[] data, long size, string what)
        {
            var decompressor = IntPtr.Zero;
            try
            {
                decompressor = LibdeflateWrapper.libdeflate_alloc_decompressor();
            }
            catch (DllNotFoundException)
            {
                // libdeflate fehlt (nur Windows-DLL beigelegt) -> unten mit .NET entpacken
            }
            if (decompressor != IntPtr.Zero)
            {
                var dest = new byte[size];
                var result = LibdeflateWrapper.libdeflate_zlib_decompress(decompressor, data,
                    (UIntPtr)data.Length, dest, (UIntPtr)size, out var bytesWritten);
                LibdeflateWrapper.libdeflate_free_decompressor(decompressor);
                if (result == libdeflate_result.LIBDEFLATE_SUCCESS && bytesWritten.ToUInt64() == (ulong)size) return dest;
                Logger.Logger.Instance.Warning($"{what}: zlib {result}, {bytesWritten} of {size} bytes - trying raw deflate");
            }

            // Rohes Deflate ab Byte 2 (zlib-Kopf und Adler-Prüfsumme ignorieren)
            if (data.Length > 2)
            {
                try
                {
                    using (var ms = new MemoryStream(data, 2, data.Length - 2))
                    using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                    {
                        var dest = new byte[size];
                        var read = 0;
                        while (read < dest.Length)
                        {
                            var n = ds.Read(dest, read, dest.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read > 0)
                        {
                            if (read < dest.Length)
                            {
                                Logger.Logger.Instance.Warning($"{what}: raw deflate gave {read} of {size} bytes");
                                Array.Resize(ref dest, read);
                            }
                            return dest;
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Logger.Instance.Warning($"{what}: raw deflate failed: {e.Message}");
                }
            }

            // Unkomprimiert abgelegt?
            if (data.Length >= size)
            {
                Logger.Logger.Instance.Warning($"{what}: using block as uncompressed data");
                var dest = new byte[size];
                Array.Copy(data, dest, size);
                return dest;
            }

            Logger.Logger.Instance.Error($"{what}: could not decompress block ({data.Length} bytes, expected {size})");
            return Array.Empty<byte>();
        }

        internal static byte[] InflateZlib(byte[] data, long compressedSize, long size)
        {
            var dest = new byte[size];

            var decompressor = LibdeflateWrapper.libdeflate_alloc_decompressor();
            if (decompressor == IntPtr.Zero)
            {
                Logger.Logger.Instance.Error("Could not init zlib decompressor");
                return Array.Empty<byte>();
            }

            var result = LibdeflateWrapper.libdeflate_zlib_decompress(decompressor, data,
                (UIntPtr)compressedSize,
                dest,
                (UIntPtr)size, out var bytesWritten);

            LibdeflateWrapper.libdeflate_free_decompressor(decompressor);

            if (result != libdeflate_result.LIBDEFLATE_SUCCESS)
            {
                Logger.Logger.Instance.Error($"Zlib extraction was not successful: {result}");
                return Array.Empty<byte>();
            }

            if (size != bytesWritten.ToUInt32())
                Logger.Logger.Instance.Error($"Possible incorrect zlib inflate, {bytesWritten} bytes written, expected {size}");

            return dest;
        }

        internal static uint MakeFourCc(char ch0, char ch1, char ch2, char ch3)
        {
            return ((uint) (byte) (ch0) | ((uint) (byte) (ch1) << 8) | ((uint) (byte) (ch2) << 16) |
             ((uint) (byte) (ch3) << 24));
        }
    }
}
