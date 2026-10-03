using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TsMap.Helpers;

namespace TsMap.FileSystem
{
    /// <summary>
    /// Untersucht ein einzelnes Mod-Archiv (HashFS v1/v2 oder Zip) unabhängig vom übrigen Dateisystem
    /// und beschreibt Aufbau, Einträge und gefundene Kartensektoren - zur Fehlersuche bei geschützten Mods.
    /// </summary>
    public static class ArchiveAnalyzer
    {
        private const uint ScsMagic = 592659283; // "SCS#"
        private static readonly string[] SectorExtensions = { ".base", ".aux", ".data", ".snd", ".desc" };
        private static readonly string[] KnownPaths =
        {
            "", "map", "map/europe", "map/usa", "map/europe.mbd", "map/usa.mbd", "def", "def/world", "def/city.sii",
            "def/country.sii", "manifest.sii", "material", "model", "prefab", "vehicle", "locale"
        };

        public static string Analyse(string path, ICollection<string> mapNames, int sectorRange = 256)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {Path.GetFileName(path)} ===");
            try
            {
                var info = new FileInfo(path);
                sb.AppendLine($"Größe: {info.Length:N0} Bytes");
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    var magic = fs.Length >= 4 ? br.ReadUInt32() : 0;
                    if (magic == ScsMagic) AnalyseHashFs(br, sb, mapNames, sectorRange);
                    else
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                        AnalyseZip(fs, sb, mapNames);
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine($"FEHLER: {e.GetType().Name}: {e.Message}");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private class RawEntry
        {
            public ulong Hash;
            public uint Flags;
            public bool Directory;
            public bool DirectoryMetadataOnly;
            public uint Size;
            public uint CompressedSize;
            public ulong Offset;
            public int Compression; // v2: Methode, v1: 1 = komprimiert
            public bool HasData;
        }

        private static void AnalyseHashFs(BinaryReader br, StringBuilder sb, ICollection<string> mapNames, int sectorRange)
        {
            var version = MemoryHelper.ReadUInt16(br, 0x04);
            var salt = MemoryHelper.ReadUInt16(br, 0x06);
            var hashMethod = MemoryHelper.ReadUInt32(br, 0x08);
            var entryCount = MemoryHelper.ReadUInt32(br, 0x0C);
            sb.AppendLine($"Format: HashFS v{version}, Salt {salt}, Hash-Methode 0x{hashMethod:X8}, {entryCount:N0} Einträge");

            var entries = new List<RawEntry>();
            if (version == 1) ReadV1(br, entryCount, entries, sb);
            else if (version == 2) ReadV2(br, entryCount, entries, sb);
            else
            {
                sb.AppendLine("Unbekannte Version");
                return;
            }

            var byHash = new Dictionary<ulong, RawEntry>();
            var duplicates = 0;
            foreach (var e in entries)
            {
                if (byHash.ContainsKey(e.Hash)) duplicates++;
                byHash[e.Hash] = e;
            }

            sb.AppendLine($"Einträge gelesen: {entries.Count:N0}, davon Ordner: {entries.Count(e => e.Directory):N0}, " +
                          $"doppelte Hashes: {duplicates:N0}");
            sb.AppendLine("Flags (häufigste): " + string.Join(", ", entries.GroupBy(e => e.Flags).OrderByDescending(g => g.Count()).Take(8)
                .Select(g => $"0x{g.Key:X8} x{g.Count()}")));
            sb.AppendLine("Kompression: " + string.Join(", ", entries.GroupBy(e => e.Compression).OrderBy(g => g.Key)
                .Select(g => $"{g.Key}: {g.Count()}")));

            Func<string, ulong> hash = p =>
            {
                var local = p.TrimStart('/');
                return CityHash.CityHash64(salt != 0 ? salt.ToString(CultureInfo.InvariantCulture) + local : local);
            };

            sb.AppendLine("Bekannte Pfade:");
            foreach (var p in KnownPaths.Concat(mapNames.Select(m => "map/" + m)).Concat(mapNames.Select(m => $"map/{m}.mbd")).Distinct())
            {
                RawEntry e;
                if (!byHash.TryGetValue(hash(p), out e)) continue;
                sb.AppendLine($"  '{p}': {Describe(e)}");
                if (e.Directory) AppendListing(br, e, version, sb);
            }

            foreach (var mapName in mapNames)
            {
                var found = SectorExtensions.ToDictionary(x => x, x => new List<KeyValuePair<string, RawEntry>>());
                for (var x = -sectorRange; x < sectorRange; x++)
                for (var z = -sectorRange; z < sectorRange; z++)
                {
                    var name = $"map/{mapName}/sec{x.ToString("+0000;-0000", CultureInfo.InvariantCulture)}{z.ToString("+0000;-0000", CultureInfo.InvariantCulture)}";
                    foreach (var ext in SectorExtensions)
                    {
                        RawEntry e;
                        if (byHash.TryGetValue(hash(name + ext), out e)) found[ext].Add(new KeyValuePair<string, RawEntry>(name + ext, e));
                    }
                }

                var total = found.Values.Sum(v => v.Count);
                sb.AppendLine($"Sektoren in map/{mapName} (Raster ±{sectorRange}): " +
                              string.Join(", ", SectorExtensions.Select(x => $"{x} {found[x].Count}")));
                if (total == 0) continue;

                var bases = found[".base"];
                if (bases.Count > 0)
                {
                    sb.AppendLine($"  .base-Größen: min {bases.Min(b => b.Value.Size):N0}, max {bases.Max(b => b.Value.Size):N0}, " +
                                  $"Ø {bases.Average(b => (double) b.Value.Size):N0} Bytes; " +
                                  $"leer (≤ 28 Bytes): {bases.Count(b => b.Value.Size <= 28)}");
                    foreach (var b in bases.OrderByDescending(b => b.Value.Size).Take(3))
                    {
                        sb.AppendLine($"  Beispiel {b.Key}: {Describe(b.Value)}");
                        var data = ReadEntry(br, b.Value, version);
                        if (data != null)
                            sb.AppendLine($"    entpackt {data.Length:N0} Bytes, Anfang {BitConverter.ToString(data, 0, Math.Min(24, data.Length))}" +
                                          (data.Length >= 0x14 ? $", Version {BitConverter.ToInt32(data, 0)}, Objekte {BitConverter.ToUInt32(data, 0x10)}" : ""));
                    }
                }
            }
        }

        private static string Describe(RawEntry e)
        {
            return (e.Directory ? "Ordner" : "Datei") + (e.DirectoryMetadataOnly ? " (nur Ordner-Metadaten, kein Flag)" : "") +
                   $", Flags 0x{e.Flags:X8}, Größe {e.Size:N0}, gepackt {e.CompressedSize:N0}, Kompression {e.Compression}" +
                   (e.HasData ? "" : ", KEINE Daten-Metadaten");
        }

        private static void AppendListing(BinaryReader br, RawEntry e, int version, StringBuilder sb)
        {
            var data = ReadEntry(br, e, version);
            if (data == null) return;
            var names = new List<string>();
            if (version == 1)
            {
                names.AddRange(Encoding.UTF8.GetString(data).Split('\n').Where(l => l.Length > 0));
            }
            else if (data.Length >= 4)
            {
                var count = BitConverter.ToUInt32(data, 0);
                var pos = 4 + (long) count;
                for (var i = 0; i < count && 4 + i < data.Length; i++)
                {
                    var len = data[4 + i];
                    if (pos + len > data.Length) break;
                    names.Add(Encoding.UTF8.GetString(data, (int) pos, len));
                    pos += len;
                }
            }
            sb.AppendLine($"    Inhalt ({names.Count}): " + string.Join(", ", names.Take(25)) + (names.Count > 25 ? ", ..." : ""));
        }

        private static byte[] ReadEntry(BinaryReader br, RawEntry e, int version)
        {
            try
            {
                if (!e.HasData || e.CompressedSize > 64 * 1024 * 1024) return null;
                br.BaseStream.Seek((long) e.Offset, SeekOrigin.Begin);
                var raw = br.ReadBytes((int) e.CompressedSize);
                if (e.Size == e.CompressedSize) return raw;
                // zlib (mit Kopf) bzw. rohes Deflate
                foreach (var skip in new[] { 2, 0 })
                {
                    try
                    {
                        using (var ms = new MemoryStream(raw, skip, raw.Length - skip))
                        using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                        {
                            var dest = new byte[e.Size];
                            var read = 0;
                            while (read < dest.Length)
                            {
                                var n = ds.Read(dest, read, dest.Length - read);
                                if (n <= 0) break;
                                read += n;
                            }
                            if (read == dest.Length) return dest;
                        }
                    }
                    catch (InvalidDataException)
                    {
                    }
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ReadV1(BinaryReader br, uint entryCount, List<RawEntry> entries, StringBuilder sb)
        {
            var start = MemoryHelper.ReadUInt32(br, 0x10);
            sb.AppendLine($"Eintragstabelle ab 0x{start:X}");
            br.BaseStream.Seek(start, SeekOrigin.Begin);
            var raw = br.ReadBytes((int) Math.Min(entryCount * 0x20L, br.BaseStream.Length - start));
            for (var i = 0; i + 0x20 <= raw.Length; i += 0x20)
            {
                var flags = BitConverter.ToUInt32(raw, i + 0x10);
                var size = BitConverter.ToUInt32(raw, i + 0x18);
                var compressed = BitConverter.ToUInt32(raw, i + 0x1C);
                entries.Add(new RawEntry
                {
                    Hash = BitConverter.ToUInt64(raw, i),
                    Offset = BitConverter.ToUInt64(raw, i + 0x08),
                    Flags = flags,
                    Directory = (flags & 1) != 0,
                    Size = size,
                    CompressedSize = compressed,
                    Compression = (flags & 2) != 0 ? 1 : 0,
                    HasData = true
                });
            }
        }

        private static void ReadV2(BinaryReader br, uint entryCount, List<RawEntry> entries, StringBuilder sb)
        {
            var entryTableSize = MemoryHelper.ReadUInt32(br, 0x10);
            var metadataCount = MemoryHelper.ReadUInt32(br, 0x14);
            var metadataTableSize = MemoryHelper.ReadUInt32(br, 0x18);
            var entryTableOffset = MemoryHelper.ReadInt64(br, 0x1C);
            var metadataTableOffset = MemoryHelper.ReadInt64(br, 0x24);
            sb.AppendLine($"Eintragstabelle 0x{entryTableOffset:X} ({entryTableSize:N0} Bytes), Metadaten 0x{metadataTableOffset:X} " +
                          $"({metadataTableSize:N0} Bytes, {metadataCount:N0} Werte)");

            br.BaseStream.Seek(entryTableOffset, SeekOrigin.Begin);
            var rawEntries = MemoryHelper.InflateZlibTolerant(br.ReadBytes((int) entryTableSize), entryCount * 0x10L, "Analyse Eintragstabelle");
            br.BaseStream.Seek(metadataTableOffset, SeekOrigin.Begin);
            var rawMeta = MemoryHelper.InflateZlibTolerant(br.ReadBytes((int) metadataTableSize), metadataCount * 4L, "Analyse Metadaten");
            sb.AppendLine($"Tabellen entpackt: Einträge {rawEntries.Length:N0} Bytes, Metadaten {rawMeta.Length:N0} Bytes");

            var metaTypes = new Dictionary<uint, int>();
            for (var i = 0; i + 0x10 <= rawEntries.Length; i += 0x10)
            {
                var e = new RawEntry
                {
                    Hash = BitConverter.ToUInt64(rawEntries, i),
                    Flags = BitConverter.ToUInt32(rawEntries, i + 0x0C)
                };
                var flagDir = ((e.Flags >> 16) & 1) != 0;
                var metaStart = BitConverter.ToInt32(rawEntries, i + 0x08);
                var metaCount = BitConverter.ToUInt16(rawEntries, i + 0x0C);
                var dirMeta = false;
                for (var j = 0; j < metaCount; j++)
                {
                    var idx = (metaStart + j) * 4L;
                    if (idx < 0 || idx + 4 > rawMeta.Length) break;
                    var m0 = BitConverter.ToUInt32(rawMeta, (int) idx);
                    var type = m0 >> 24;
                    metaTypes[type] = metaTypes.TryGetValue(type, out var c) ? c + 1 : 1;
                    var refOffset = (long) (m0 & 0x00FFFFFF) * 4;
                    if ((type == 0x80 || type == 0x81 || type == 0x82 || type == 0x83 || type == 0x84) && refOffset + 16 <= rawMeta.Length)
                    {
                        var d0 = BitConverter.ToUInt32(rawMeta, (int) refOffset);
                        var d1 = BitConverter.ToUInt32(rawMeta, (int) refOffset + 4);
                        var d3 = BitConverter.ToUInt32(rawMeta, (int) refOffset + 12);
                        e.CompressedSize = d0 & 0x0FFFFFFF;
                        e.Compression = (int) (d0 >> 28);
                        e.Size = d1 & 0x0FFFFFFF;
                        e.Offset = d3 * 0x10UL;
                        e.HasData = true;
                        if (type == 0x81) dirMeta = true;
                    }
                }
                e.Directory = flagDir || dirMeta;
                e.DirectoryMetadataOnly = dirMeta && !flagDir;
                entries.Add(e);
            }
            sb.AppendLine("Metadaten-Typen: " + string.Join(", ", metaTypes.OrderBy(m => m.Key).Select(m => $"0x{m.Key:X2} x{m.Value}")));
            sb.AppendLine($"Ordner-Metadaten ohne Flag: {entries.Count(e => e.DirectoryMetadataOnly):N0}, ohne Daten-Metadaten: {entries.Count(e => !e.HasData):N0}");
        }

        /// <summary>Zip ohne System.IO.Compression.ZipArchive (fehlt unter .NET Framework als Referenz): Zentralverzeichnis lesen</summary>
        private static void AnalyseZip(Stream fs, StringBuilder sb, ICollection<string> mapNames)
        {
            var tailLength = (int) Math.Min(fs.Length, 0x10000 + 22);
            var tail = new byte[tailLength];
            fs.Seek(fs.Length - tailLength, SeekOrigin.Begin);
            MemoryHelper.ReadExactly(fs, tail, 0, tailLength);
            var eocd = -1;
            for (var i = tailLength - 22; i >= 0; i--)
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0)
            {
                sb.AppendLine("Format: weder HashFS noch Zip (kein Zip-Endeintrag gefunden)");
                return;
            }

            var count = BitConverter.ToUInt16(tail, eocd + 10);
            var cdSize = BitConverter.ToUInt32(tail, eocd + 12);
            var cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
            var cd = new byte[cdSize];
            fs.Seek(cdOffset, SeekOrigin.Begin);
            MemoryHelper.ReadExactly(fs, cd, 0, cd.Length);

            var entries = new List<KeyValuePair<string, uint>>();
            var pos = 0;
            while (pos + 46 <= cd.Length && BitConverter.ToUInt32(cd, pos) == 0x02014b50)
            {
                var size = BitConverter.ToUInt32(cd, pos + 24);
                var nameLength = BitConverter.ToUInt16(cd, pos + 28);
                var extraLength = BitConverter.ToUInt16(cd, pos + 30);
                var commentLength = BitConverter.ToUInt16(cd, pos + 32);
                if (pos + 46 + nameLength > cd.Length) break;
                entries.Add(new KeyValuePair<string, uint>(Encoding.UTF8.GetString(cd, pos + 46, nameLength).Replace('\\', '/'), size));
                pos += 46 + nameLength + extraLength + commentLength;
            }

            sb.AppendLine($"Format: Zip, {entries.Count:N0} Einträge (laut Endeintrag {count:N0})");
            sb.AppendLine("Ordner (oberste Ebene): " + string.Join(", ", entries.Select(e => e.Key.Split('/')[0]).GroupBy(n => n)
                .Select(g => $"{g.Key} ({g.Count()})")));
            var mbds = entries.Where(e => e.Key.StartsWith("map/") && e.Key.EndsWith(".mbd")).Select(e => e.Key).ToList();
            if (mbds.Count > 0) sb.AppendLine("Karten (.mbd): " + string.Join(", ", mbds));
            foreach (var mapName in mapNames)
            {
                var prefix = $"map/{mapName}/";
                var sectors = entries.Where(e => e.Key.StartsWith(prefix)).ToList();
                if (sectors.Count == 0) continue;
                sb.AppendLine($"Sektoren in map/{mapName}: " + string.Join(", ", SectorExtensions.Select(x =>
                    $"{x} {sectors.Count(e => e.Key.EndsWith(x, StringComparison.OrdinalIgnoreCase))}")));
                var bases = sectors.Where(e => e.Key.EndsWith(".base", StringComparison.OrdinalIgnoreCase)).ToList();
                if (bases.Count > 0)
                    sb.AppendLine($"  .base-Größen: min {bases.Min(b => b.Value):N0}, max {bases.Max(b => b.Value):N0}, leer (≤ 28 Bytes): {bases.Count(b => b.Value <= 28)}");
            }
        }
    }
}
