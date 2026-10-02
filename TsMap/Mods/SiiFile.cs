using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TsMap.Mods
{
    /// <summary>
    /// A unit from a .sii file (e.g. "user_profile : _nameless.1234 { ... }").
    /// Only string-like values are kept; everything else is skipped while reading.
    /// </summary>
    public class SiiUnit
    {
        public string ClassName { get; }
        public string Id { get; }

        /// <summary>
        /// Field name -> values. Single values have one entry, arrays have one entry per item.
        /// </summary>
        public Dictionary<string, List<string>> Fields { get; } = new Dictionary<string, List<string>>();

        public SiiUnit(string className, string id)
        {
            ClassName = className;
            Id = id;
        }

        public string GetString(string field)
        {
            List<string> values;
            return Fields.TryGetValue(field, out values) && values.Count > 0 ? values[0] : null;
        }

        public List<string> GetArray(string field)
        {
            List<string> values;
            return Fields.TryGetValue(field, out values) ? values : new List<string>();
        }
    }

    /// <summary>
    /// Reader for SCS .sii files (profile.sii, versions.sii, ...).
    /// Supports encrypted (ScsC), binary (BSII) and plain text (SiiNunit) files.
    /// Format reference: https://github.com/TheLazyTomcat/SII_Decrypt (Documents/Binary SII - *.txt)
    /// </summary>
    public static class SiiFile
    {
        private const uint SignatureEncrypted = 0x43736353; // ScsC
        private const uint SignatureText = 0x4e696953;      // SiiN
        private const uint SignatureBinary = 0x49495342;    // BSII

        // Public key used by the games for save/profile encryption (see SII_Decrypt)
        private static readonly byte[] Key =
        {
            0x2a, 0x5f, 0xcb, 0x17, 0x91, 0xd2, 0x2f, 0xb6, 0x02, 0x45, 0xb3, 0xd8, 0x36, 0x9e, 0xd0, 0xb2,
            0xc2, 0x73, 0x71, 0x56, 0x3f, 0xbf, 0x1f, 0x3c, 0x9e, 0xdf, 0x6b, 0x11, 0x82, 0x5a, 0x5d, 0x0a,
        };

        public static List<SiiUnit> Read(string path)
        {
            return Read(File.ReadAllBytes(path));
        }

        public static List<SiiUnit> Read(byte[] data)
        {
            if (data.Length < 4) throw new InvalidDataException("File too small");

            var signature = BitConverter.ToUInt32(data, 0);
            if (signature == SignatureEncrypted) return Read(Decrypt(data));
            if (signature == SignatureBinary) return BinaryReaderSii.Read(data);

            // Text, with or without UTF-8 BOM
            var text = Encoding.UTF8.GetString(data).TrimStart('﻿');
            if (text.TrimStart().StartsWith("SiiNunit", StringComparison.Ordinal) || signature == SignatureText)
                return TextReaderSii.Read(text);

            throw new InvalidDataException("Unknown .sii format (3nK or unsupported)");
        }

        /// <summary>
        /// Decrypts an "ScsC" file: header (signature, HMAC[32], IV[16], size) + AES-256-CBC + zlib.
        /// </summary>
        public static byte[] Decrypt(byte[] data)
        {
            const int headerSize = 4 + 32 + 16 + 4;
            if (data.Length < headerSize) throw new InvalidDataException("Encrypted .sii header too small");

            var iv = new byte[16];
            Array.Copy(data, 4 + 32, iv, 0, 16);
            var size = BitConverter.ToUInt32(data, 4 + 32 + 16);

            byte[] compressed;
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                using (var dec = aes.CreateDecryptor())
                {
                    var length = (data.Length - headerSize) / 16 * 16;
                    compressed = dec.TransformFinalBlock(data, headerSize, length);
                }
            }

            // zlib: 2 byte header, then raw deflate
            var result = new byte[size];
            using (var ms = new MemoryStream(compressed, 2, compressed.Length - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                var read = 0;
                while (read < result.Length)
                {
                    var n = ds.Read(result, read, result.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != result.Length) throw new InvalidDataException("Decompressed size mismatch");
            }
            return result;
        }

        /// <summary>
        /// Decodes a 64 bit "encoded string" (token) into its text form.
        /// </summary>
        public static string DecodeToken(ulong value)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz_";
            value &= 0x7FFFFFFFFFFFFFFF;
            var sb = new StringBuilder();
            while (value > 0)
            {
                var idx = (int)(value % 38);
                value /= 38;
                if (idx > 0) sb.Append(chars[idx - 1]);
            }
            return sb.ToString();
        }

        private static class BinaryReaderSii
        {
            private class Field
            {
                public uint Type;
                public string Name;
                public Dictionary<uint, string> Ordinals;
            }

            private class Structure
            {
                public string Name;
                public List<Field> Fields = new List<Field>();
            }

            public static List<SiiUnit> Read(byte[] data)
            {
                var units = new List<SiiUnit>();
                var structures = new Dictionary<uint, Structure>();

                using (var br = new BinaryReader(new MemoryStream(data)))
                {
                    br.ReadUInt32(); // signature
                    var version = br.ReadUInt32();
                    if (version < 1 || version > 3)
                        throw new InvalidDataException($"Unsupported BSII version {version}");

                    try
                    {
                        while (br.BaseStream.Position < br.BaseStream.Length)
                        {
                            var blockType = br.ReadUInt32();
                            if (blockType == 0)
                            {
                                if (br.ReadByte() == 0) break; // end of file

                                var id = br.ReadUInt32();
                                var structure = new Structure { Name = ReadString(br) };
                                while (true)
                                {
                                    var type = br.ReadUInt32();
                                    if (type == 0) break;
                                    var field = new Field { Type = type, Name = ReadString(br) };
                                    if (type == 0x37)
                                    {
                                        field.Ordinals = new Dictionary<uint, string>();
                                        var count = br.ReadUInt32();
                                        for (var i = 0; i < count; i++)
                                        {
                                            var ord = br.ReadUInt32();
                                            field.Ordinals[ord] = ReadString(br);
                                        }
                                    }
                                    structure.Fields.Add(field);
                                }
                                structures[id] = structure;
                            }
                            else
                            {
                                Structure structure;
                                if (!structures.TryGetValue(blockType, out structure))
                                    throw new InvalidDataException($"Unknown structure {blockType}");

                                var unit = new SiiUnit(structure.Name, ReadId(br));
                                foreach (var field in structure.Fields)
                                {
                                    var values = ReadValue(br, field, version);
                                    if (values != null) unit.Fields[field.Name] = values;
                                }
                                units.Add(unit);
                            }
                        }
                    }
                    catch (InvalidDataException) when (units.Count > 0)
                    {
                        // Unknown value type in a later unit (newer game version):
                        // keep everything read so far, the profile data comes first anyway.
                    }
                    catch (EndOfStreamException) when (units.Count > 0)
                    {
                    }
                }
                return units;
            }

            private static string ReadString(BinaryReader br)
            {
                var len = br.ReadUInt32();
                return Encoding.UTF8.GetString(br.ReadBytes((int)len));
            }

            private static string ReadId(BinaryReader br)
            {
                var parts = br.ReadByte();
                if (parts == 0xFF) return "_nameless." + br.ReadUInt64().ToString("x");
                var names = new string[parts];
                for (var i = 0; i < parts; i++) names[i] = DecodeToken(br.ReadUInt64());
                return string.Join(".", names);
            }

            private static void Skip(BinaryReader br, long bytes)
            {
                br.BaseStream.Seek(bytes, SeekOrigin.Current);
            }

            private static void SkipArray(BinaryReader br, int itemSize)
            {
                var count = br.ReadUInt32();
                Skip(br, (long)count * itemSize);
            }

            /// <returns>String values for string-like types, null for skipped types</returns>
            private static List<string> ReadValue(BinaryReader br, Field field, uint version)
            {
                var vec8Size = version == 1 ? 28 : 32;
                switch (field.Type)
                {
                    case 0x01: return new List<string> { ReadString(br) };
                    case 0x02:
                    {
                        var count = br.ReadUInt32();
                        var list = new List<string>((int)Math.Min(count, 4096));
                        for (var i = 0; i < count; i++) list.Add(ReadString(br));
                        return list;
                    }
                    case 0x03: return new List<string> { DecodeToken(br.ReadUInt64()) };
                    case 0x04:
                    {
                        var count = br.ReadUInt32();
                        var list = new List<string>();
                        for (var i = 0; i < count; i++) list.Add(DecodeToken(br.ReadUInt64()));
                        return list;
                    }
                    case 0x05: Skip(br, 4); return null;
                    case 0x06: SkipArray(br, 4); return null;
                    case 0x07: Skip(br, 8); return null;
                    case 0x08: SkipArray(br, 8); return null;
                    case 0x09: Skip(br, 12); return null;
                    case 0x0A: SkipArray(br, 12); return null;
                    case 0x11: Skip(br, 12); return null;
                    case 0x12: SkipArray(br, 12); return null;
                    case 0x17: Skip(br, 16); return null;
                    case 0x18: SkipArray(br, 16); return null;
                    case 0x19: Skip(br, vec8Size); return null;
                    case 0x1A: SkipArray(br, vec8Size); return null;
                    case 0x25:
                    case 0x27:
                    case 0x2F:
                        return new List<string> { br.ReadUInt32().ToString() };
                    case 0x26:
                    case 0x28:
                        SkipArray(br, 4); return null;
                    case 0x29:
                    case 0x2B:
                        Skip(br, 2); return null;
                    case 0x2A:
                    case 0x2C:
                        SkipArray(br, 2); return null;
                    case 0x31:
                    case 0x33:
                        Skip(br, 8); return null;
                    case 0x32:
                    case 0x34:
                        SkipArray(br, 8); return null;
                    case 0x35: Skip(br, 1); return null;
                    case 0x36: SkipArray(br, 1); return null;
                    case 0x37:
                    {
                        var ord = br.ReadUInt32();
                        string value;
                        return new List<string> { field.Ordinals != null && field.Ordinals.TryGetValue(ord, out value) ? value : ord.ToString() };
                    }
                    case 0x39:
                    case 0x3B:
                    case 0x3D:
                        return new List<string> { ReadId(br) };
                    case 0x3A:
                    case 0x3C:
                    {
                        var count = br.ReadUInt32();
                        var list = new List<string>();
                        for (var i = 0; i < count; i++) list.Add(ReadId(br));
                        return list;
                    }
                    default:
                        throw new InvalidDataException($"Unknown BSII value type 0x{field.Type:X} ({field.Name})");
                }
            }
        }

        private static class TextReaderSii
        {
            public static List<SiiUnit> Read(string text)
            {
                var units = new List<SiiUnit>();
                SiiUnit current = null;

                foreach (var rawLine in text.Split('\n'))
                {
                    var line = StripComment(rawLine).Trim();
                    if (line.Length == 0 || line == "SiiNunit" || line == "{") continue;

                    if (line == "}")
                    {
                        current = null;
                        continue;
                    }

                    var colon = IndexOfOutsideQuotes(line, ':');
                    if (colon < 0) continue;

                    var left = line.Substring(0, colon).Trim();
                    var right = line.Substring(colon + 1).Trim();

                    if (current == null)
                    {
                        // "class_name : unit.id {"
                        if (right.EndsWith("{")) right = right.Substring(0, right.Length - 1).Trim();
                        current = new SiiUnit(left, right);
                        units.Add(current);
                        continue;
                    }

                    var value = Unquote(right);
                    var bracket = left.IndexOf('[');
                    if (bracket < 0)
                    {
                        // Plain value, or the element count of an array ("active_mods: 3")
                        List<string> existing;
                        if (!current.Fields.TryGetValue(left, out existing))
                            current.Fields[left] = new List<string> { value };
                        continue;
                    }

                    var name = left.Substring(0, bracket);
                    var indexText = left.Substring(bracket + 1).TrimEnd(']');
                    List<string> values;
                    if (!current.Fields.TryGetValue(name, out values) || IsCountOnly(values))
                    {
                        values = new List<string>();
                        current.Fields[name] = values;
                    }

                    int index;
                    if (indexText.Length > 0 && int.TryParse(indexText, out index))
                    {
                        while (values.Count <= index) values.Add(null);
                        values[index] = value;
                    }
                    else
                    {
                        values.Add(value); // "name[]: value"
                    }
                }

                foreach (var unit in units)
                    foreach (var key in unit.Fields.Keys.ToList())
                        unit.Fields[key] = unit.Fields[key].Where(v => v != null).ToList();

                return units;
            }

            private static bool IsCountOnly(List<string> values)
            {
                int dummy;
                return values.Count == 1 && int.TryParse(values[0], out dummy);
            }

            private static string StripComment(string line)
            {
                var inQuotes = false;
                for (var i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inQuotes = !inQuotes;
                    if (inQuotes) continue;
                    if (line[i] == '#') return line.Substring(0, i);
                    if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/') return line.Substring(0, i);
                }
                return line;
            }

            private static int IndexOfOutsideQuotes(string line, char c)
            {
                var inQuotes = false;
                for (var i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"') inQuotes = !inQuotes;
                    else if (!inQuotes && line[i] == c) return i;
                }
                return -1;
            }

            private static string Unquote(string value)
            {
                if (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"') return value;

                // Escapes like \x c3\x a4 are UTF-8 bytes, so decode everything as one byte stream.
                var bytes = new List<byte>();
                var plain = new StringBuilder();
                var s = value.Substring(1, value.Length - 2);
                for (var i = 0; i < s.Length; i++)
                {
                    if (s[i] != '\\' || i + 1 >= s.Length)
                    {
                        plain.Append(s[i]);
                        continue;
                    }

                    bytes.AddRange(Encoding.UTF8.GetBytes(plain.ToString()));
                    plain.Clear();

                    var n = s[i + 1];
                    if (n == 'x' && i + 3 < s.Length)
                    {
                        bytes.Add(Convert.ToByte(s.Substring(i + 2, 2), 16));
                        i += 3;
                        continue;
                    }
                    plain.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                    i++;
                }
                bytes.AddRange(Encoding.UTF8.GetBytes(plain.ToString()));
                return Encoding.UTF8.GetString(bytes.ToArray());
            }
        }
    }
}
