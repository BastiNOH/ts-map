using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using TsMap.Mods;

namespace TsMap.Cli
{
    /// <summary>
    /// ts-map ohne GUI: liest die aktiven Mods inkl. Reihenfolge aus dem Spielerprofil
    /// und exportiert Kacheln + Cities/Countries/Overlays/TileMapInfo.json.
    ///
    ///   TsMap.Cli --game ats --out D:\www\html\maps\ats
    ///   TsMap.Cli --game ets2 --profile "Basti" --list
    /// </summary>
    internal static class Program
    {
        private const int TileSize = 256;
        private const int MapPadding = 500;

        private class Options
        {
            public Game? Game;
            public string GameDir;
            public string DocumentsDir;
            public string Profile;
            public string GameVersion;
            public string OutDir;
            public int MinZoom = 0;
            public int MaxZoom = 9;
            public bool Tiles = true;
            public bool Png;
            public bool Vector = true;
            public int VectorZoom = 8;
            public bool ListOnly;
            public bool Verbose;
            public bool NoMods;
            public bool DlcAuto = true;
            public readonly List<string> DlcOn = new List<string>();
            public readonly List<string> DlcOff = new List<string>();
            public readonly List<string> Exclude = new List<string>();
            public readonly List<string> ExtraWorkshopDirs = new List<string>();
            public readonly List<string> Analyse = new List<string>();
            public readonly List<string> AnalyseMaps = new List<string>();
        }

        private static int Main(string[] args)
        {
            Options o;
            try
            {
                o = ParseArgs(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                PrintUsage();
                return 2;
            }

            if (o == null)
            {
                PrintUsage();
                return 0;
            }

            if (o.Analyse.Count > 0) return RunAnalyse(o);

            // ts-map protokolliert jede fehlende Kleinigkeit; das gehört in die Logdatei, nicht auf die Konsole
            TsMap.Helpers.Logger.Logger.ConsoleOutput = o.Verbose;

            var game = o.Game.Value;
            var gameDir = o.GameDir ?? SteamLocator.FindGameDir(game);
            if (gameDir == null || !Directory.Exists(gameDir))
            {
                Console.Error.WriteLine($"Spielordner nicht gefunden, bitte mit --game-dir angeben.");
                return 1;
            }
            var searchedDirs = new List<string>();
            var documentsDir = o.DocumentsDir ?? GameInfo.ResolveDocumentsDir(game, out searchedDirs);

            Console.WriteLine($"Spiel:       {GameInfo.Name(game)}");
            Console.WriteLine($"Spielordner: {gameDir}");
            Console.WriteLine($"Spieldaten:  {documentsDir}");

            var mods = new List<Mod>();
            if (!o.NoMods)
            {
                ProfileInfo profile;
                try
                {
                    profile = ProfileReader.SelectProfile(game, o.Profile, documentsDir);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Profil konnte nicht gelesen werden: {e.Message}");
                    return 1;
                }

                if (profile == null)
                {
                    var verfuegbar = ProfileReader.FindProfiles(game, documentsDir);
                    if (verfuegbar.Count == 0)
                    {
                        Console.Error.WriteLine("Kein Profil gefunden. Gesucht in:");
                        foreach (var d in searchedDirs.DefaultIfEmpty(documentsDir)) Console.Error.WriteLine($"  - {d}");
                        Console.Error.WriteLine("Liegen die Spieldaten woanders, mit --documents <Ordner> angeben (z.B. \"E:\\Game_Data\\Euro Truck Simulator 2\").");
                    }
                    else
                    {
                        Console.Error.WriteLine($"Profil '{o.Profile}' nicht gefunden. Verfügbar:");
                        foreach (var p in verfuegbar) Console.Error.WriteLine($"  - {p}");
                    }
                    return 1;
                }

                Console.WriteLine($"Profil:      {profile} (zuletzt benutzt {profile.LastWrite:g})");

                var workshopDirs = o.ExtraWorkshopDirs.Concat(SteamLocator.FindWorkshopDirs(game)).ToList();
                var warnings = new List<string>();
                var active = profile.ActiveMods
                    .Where(m => !o.Exclude.Any(x => m.DisplayName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0
                                                 || m.PackageName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();
                mods = ModResolver.Resolve(active, Path.Combine(documentsDir, "mod"), workshopDirs, o.GameVersion, warnings);

                Console.WriteLine();
                Console.WriteLine($"Aktive Mods laut Profil: {profile.ActiveMods.Count} (oben = höchste Priorität)");
                for (var i = 0; i < mods.Count; i++)
                    Console.WriteLine($"  {i + 1,3}. {mods[i]}  ->  {mods[i].ModPath}");
                foreach (var w in warnings) Console.WriteLine($"  WARNUNG: {w}");
                Console.WriteLine();
            }

            if (o.ListOnly)
            {
                // DLC-Prüfung braucht nur den Spielordner, nicht die geladene Karte
                PrintDlcReport(DlcDetector.Apply(
                    game == Game.Ets2 ? DlcGuardsEts2() : DlcGuardsAts(), gameDir, o.DlcOn, o.DlcOff), o);
                return 0;
            }

            if (string.IsNullOrEmpty(o.OutDir))
            {
                Console.Error.WriteLine("--out fehlt.");
                return 2;
            }

            var outDir = Path.GetFullPath(o.OutDir);
            Directory.CreateDirectory(outDir);

            // TsMapper sucht custom_resources.zip im aktuellen Verzeichnis.
            Environment.CurrentDirectory = AppContext.BaseDirectory;

            var sw = Stopwatch.StartNew();
            var mapper = new TsMapper(gameDir, mods);
            mapper.Parse();
            if (mapper.Cities.Count == 0 && mapper.minX == float.MaxValue)
            {
                Console.Error.WriteLine("Karte konnte nicht geladen werden (Details im ts-map Log unter %LOCALAPPDATA%\\ts-map\\TsMap.log).");
                return 1;
            }
            foreach (var failed in TsMap.FileSystem.UberFileSystem.Instance.FailedSources)
            {
                if (failed.Key.EndsWith("custom_resources.zip", StringComparison.OrdinalIgnoreCase)) continue;
                Console.WriteLine($"  WARNUNG: übersprungen (nicht lesbar): {Path.GetFileName(failed.Key)} - {failed.Value}");
            }
            Console.WriteLine($"Karte geladen in {sw.Elapsed.TotalSeconds:0.0}s ({mapper.Cities.Count} Städte, {mapper.Roads.Count} Straßen, {mapper.Prefabs.Count} Prefabs).");
            if (mapper.HiddenSectorCount > 0)
                Console.WriteLine($"  Hinweis: {mapper.HiddenSectorCount} Kartensektoren ohne Ordnerliste gefunden (geschützte Mods) - mitgezeichnet.");
            if (mapper.SectorsByArchive.Count > 0)
                Console.WriteLine("  Kartensektoren je Archiv: " + string.Join(", ", mapper.SectorsByArchive
                    .OrderByDescending(a => a.Value.Sectors)
                    .Select(a => $"{a.Key} {a.Value.Sectors} ({a.Value.Items} Objekte)")));
            if (mapper.MissingRoadLookCount > 0)
                Console.WriteLine($"  Hinweis: {mapper.MissingRoadLookCount} Straßentypen ohne Definition (z.B. aus Mods) - mit Standardbreite gezeichnet.");
            PrintLogSummary();

            CheckDlcs(mapper, gameDir, o);

            mapper.ExportInfo(ExportFlags.All, outDir);

            var renderer = new TsMapRenderer(mapper);
            var palette = new SimpleMapPalette();
            GenerateTiles(mapper, renderer, palette, outDir, o.MinZoom, o.MaxZoom, o.Tiles && o.Png);
            if (o.Tiles && o.Vector) GenerateVectorTiles(mapper, outDir, o.VectorZoom);

            Console.WriteLine($"Fertig in {sw.Elapsed.TotalMinutes:0.0} min -> {outDir}");
            PrintLogSummary();
            return 0;
        }

        private static void PrintLogSummary()
        {
            var log = TsMap.Helpers.Logger.Logger.Instance;
            if (log.ErrorCount + log.WarningCount == 0) return;
            Console.WriteLine($"  ts-map Log: {log.ErrorCount} Fehler, {log.WarningCount} Warnungen (meist fehlende Icons/Definitionen aus Mods) -> {log.LogFilePath}");
        }

        private static List<TsMap.Common.DlcGuard> DlcGuardsEts2() => TsMap.Common.Consts.DefaultEts2DlcGuards;
        private static List<TsMap.Common.DlcGuard> DlcGuardsAts() => TsMap.Common.Consts.DefaultAtsDlcGuards;

        private static void CheckDlcs(TsMapper mapper, string gameDir, Options o)
        {
            if (!o.DlcAuto && o.DlcOn.Count == 0 && o.DlcOff.Count == 0) return;
            var report = DlcDetector.Apply(mapper.GetDlcGuardsForCurrentGame(), o.DlcAuto ? gameDir : null, o.DlcOn, o.DlcOff);
            PrintDlcReport(report, o);
        }

        private static void PrintDlcReport(DlcDetector.Report report, Options o)
        {
            Console.WriteLine();
            Console.WriteLine($"DLC-Prüfung: {report.InstalledDlcs.Count} DLC-Archive im Spielordner");
            if (!o.DlcAuto)
                Console.WriteLine("  Modus 'alle': DLC-Guards auf ts-map-Standard.");
            else if (report.DetectionSkipped)
                Console.WriteLine("  Keine dlc_*.scs gefunden – DLC-Guards bleiben auf Standard.");

            var aktiv = report.Guards.Where(g => g.Enabled && g.Guard.Index != 0).ToList();
            var inaktiv = report.Guards.Where(g => !g.Enabled).ToList();
            Console.WriteLine($"  Aktiv ({aktiv.Count}):   {string.Join(", ", aktiv.Select(g => g.Guard.Name))}");
            Console.WriteLine($"  Inaktiv ({inaktiv.Count}): {string.Join(", ", inaktiv.Select(g => $"{g.Guard.Name} [{g.Reason}]"))}");
            if (report.UnmatchedDlcs.Count > 0)
                Console.WriteLine($"  Ohne Karten-Guard (Fahrzeuge/Ladung oder neu): {string.Join(", ", report.UnmatchedDlcs)}");
            Console.WriteLine();
        }

        private static void GenerateTiles(TsMapper mapper, TsMapRenderer renderer, MapPalette palette, string outDir, int minZoom, int maxZoom, bool createTiles)
        {
            // Entspricht TsMapCanvas.GenerateTileMap / SaveTileImage
            ZoomOutAndCenterMap(mapper, TileSize, TileSize, out var pos0, out var zoom0);
            JsonHelper.SaveTileMapInfo(outDir, pos0.X, pos0.X + TileSize / zoom0, pos0.Y, pos0.Y + TileSize / zoom0, minZoom, maxZoom);

            if (!createTiles) return;

            long total = 0, done = 0;
            for (var z = minZoom; z <= maxZoom; z++) total += (long)Math.Pow(4, z);

            var lastReport = DateTime.MinValue;
            for (var z = minZoom; z <= maxZoom; z++)
            {
                var size = (int)Math.Pow(2, z);
                ZoomOutAndCenterMap(mapper, size * TileSize, size * TileSize, out var pos, out var zoom);

                for (var x = 0; x < size; x++)
                {
                    for (var y = 0; y < size; y++)
                    {
                        SaveTile(renderer, palette, z, x, y, pos, zoom, outDir);
                        done++;
                        if ((DateTime.Now - lastReport).TotalSeconds >= 2 || done == total)
                        {
                            Console.Write($"\rKacheln: {done}/{total} ({done * 100.0 / total:0.0}%)   ");
                            lastReport = DateTime.Now;
                        }
                    }
                }
            }
            Console.WriteLine();
        }

        private static void GenerateVectorTiles(TsMapper mapper, string outDir, int maxZoom)
        {
            ZoomOutAndCenterMap(mapper, TileSize, TileSize, out var pos0, out var zoom0);
            var exporter = new TsMap.Map.VectorExporter(mapper);
            exporter.Collect();
            Console.WriteLine($"Vektorkarte: {exporter.FeatureCount} Formen, Zoomstufen 0-{maxZoom}");
            var guardNames = mapper.GetDlcGuardsForCurrentGame().GroupBy(g => g.Index).ToDictionary(g => g.Key, g => g.First().Name);
            foreach (var hidden in exporter.HiddenByGuard.OrderByDescending(h => h.Value))
                Console.WriteLine($"  Ausgeblendet (DLC fehlt): {hidden.Value} Objekte mit Guard {guardNames[hidden.Key]} ({hidden.Key})");
            if (exporter.UnknownGuards.Count > 0)
                Console.WriteLine("  Unbekannte DLC-Guards (werden gezeichnet): " +
                                  string.Join(", ", exporter.UnknownGuards.OrderBy(u => u.Key).Select(u => $"{u.Key}: {u.Value} Objekte")));
            var count = exporter.Export(outDir, pos0.X, pos0.Y, zoom0, maxZoom, msg => Console.WriteLine("  " + msg));

            // Leaflet liest daraus, bis zu welcher Stufe Vektor-Kacheln vorliegen (darüber wird vergrößert)
            File.WriteAllText(Path.Combine(outDir, "VectorInfo.json"),
                $"{{\"format\":1,\"tileSize\":{TileSize},\"maxZoom\":{maxZoom},\"tiles\":{count},\"created\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"}}");
        }

        private static void SaveTile(TsMapRenderer renderer, MapPalette palette, int z, int x, int y, PointF pos, float zoom, string outDir)
        {
            using (var bitmap = new Bitmap(TileSize, TileSize))
            using (var g = Graphics.FromImage(bitmap))
            {
                pos.X += TileSize / zoom * x;
                pos.Y += TileSize / zoom * y;

                renderer.Render(g, new Rectangle(0, 0, TileSize, TileSize), zoom, pos, palette, RenderFlags.All & ~RenderFlags.TextOverlay);

                var dir = Path.Combine(outDir, "Tiles", z.ToString(), x.ToString());
                Directory.CreateDirectory(dir);
                bitmap.Save(Path.Combine(dir, $"{y}.png"), ImageFormat.Png);
            }
        }

        private static void ZoomOutAndCenterMap(TsMapper mapper, float targetWidth, float targetHeight, out PointF pos, out float zoom)
        {
            var mapWidth = mapper.maxX - mapper.minX + MapPadding * 2;
            var mapHeight = mapper.maxZ - mapper.minZ + MapPadding * 2;
            if (mapWidth > mapHeight)
            {
                zoom = targetWidth / mapWidth;
                var z = mapper.minZ - MapPadding + -(targetHeight / zoom) / 2f + mapHeight / 2f;
                pos = new PointF(mapper.minX - MapPadding, z);
            }
            else
            {
                zoom = targetHeight / mapHeight;
                var x = mapper.minX - MapPadding + -(targetWidth / zoom) / 2f + mapWidth / 2f;
                pos = new PointF(x, mapper.minZ - MapPadding);
            }
        }

        private static Options ParseArgs(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) return null;

            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                string Next()
                {
                    if (i + 1 >= args.Length) throw new ArgumentException($"Wert fehlt für {args[i]}");
                    return args[++i];
                }

                switch (args[i].ToLowerInvariant())
                {
                    case "--game":
                        var g = Next().ToLowerInvariant();
                        o.Game = g == "ets2" || g == "eut2" ? Game.Ets2 : g == "ats" ? Game.Ats : throw new ArgumentException($"Unbekanntes Spiel '{g}' (ets2 oder ats)");
                        break;
                    case "--game-dir": o.GameDir = Next(); break;
                    case "--documents": o.DocumentsDir = Next(); break;
                    case "--profile": o.Profile = Next(); break;
                    case "--game-version": o.GameVersion = Next(); break;
                    case "--out": o.OutDir = Next(); break;
                    case "--zoom":
                        var range = Next().Split('-');
                        o.MinZoom = int.Parse(range[0]);
                        o.MaxZoom = int.Parse(range.Length > 1 ? range[1] : range[0]);
                        break;
                    case "--no-tiles": o.Tiles = false; break;
                    case "--format":
                        var fmt = Next().ToLowerInvariant();
                        if (fmt == "vector" || fmt == "vektor") { o.Vector = true; o.Png = false; }
                        else if (fmt == "png") { o.Vector = false; o.Png = true; }
                        else if (fmt == "both" || fmt == "beide") { o.Vector = true; o.Png = true; }
                        else throw new ArgumentException("--format vector|png|beide");
                        break;
                    case "--vector-zoom": o.VectorZoom = int.Parse(Next()); break;
                    case "--list": o.ListOnly = true; break;
                    case "--verbose": o.Verbose = true; break;
                    case "--no-mods": o.NoMods = true; break;
                    case "--exclude": o.Exclude.Add(Next()); break;
                    case "--workshop-dir": o.ExtraWorkshopDirs.Add(Next()); break;
                    case "--dlc":
                        var mode = Next().ToLowerInvariant();
                        if (mode == "auto") o.DlcAuto = true;
                        else if (mode == "alle" || mode == "all") o.DlcAuto = false;
                        else throw new ArgumentException("--dlc auto|alle");
                        break;
                    case "--dlc-an": o.DlcOn.Add(Next()); break;
                    case "--dlc-aus": o.DlcOff.Add(Next()); break;
                    case "--analyse":
                    case "--analyze": o.Analyse.Add(Next()); break;
                    case "--analyse-karte": o.AnalyseMaps.Add(Next()); break;
                    default: throw new ArgumentException($"Unbekannte Option '{args[i]}'");
                }
            }

            if (o.Analyse.Count > 0) return o;
            if (o.Game == null) throw new ArgumentException("--game fehlt (ets2 oder ats)");
            if (o.MinZoom < 0 || o.MaxZoom > 18 || o.MinZoom > o.MaxZoom) throw new ArgumentException("--zoom muss im Bereich 0-18 liegen, z.B. 0-8");
            if (o.VectorZoom < 0 || o.VectorZoom > 12) throw new ArgumentException("--vector-zoom muss im Bereich 0-12 liegen");
            return o;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"TsMap.Cli - rendert die ETS2/ATS-Karte mit den Mods aus dem Spielerprofil

  TsMap.Cli --game <ets2|ats> --out <ordner> [optionen]

  --game <ets2|ats>      Spiel (Pflicht)
  --out <ordner>         Zielordner (Vector/ bzw. Tiles/, TileMapInfo.json, Cities.json, Overlays.json, ...)
  --profile <name>       Profilname oder Profilordner (Standard: zuletzt benutztes Profil)
  --game-dir <pfad>      Spielordner (Standard: automatisch über Steam)
  --documents <pfad>     Dokumente-Ordner des Spiels (Standard: Eigene Dokumente\<Spiel>)
  --game-version <ver>   z.B. 1.61 – wählt bei Workshop-Mods das passende Paket (Standard: neuestes)
  --workshop-dir <pfad>  zusätzlicher Workshop-Ordner (steamapps\workshop\content\<appid>)
  --exclude <text>       Mod überspringen, deren Name/Paket den Text enthält (mehrfach möglich)
  --format <art>         vector: Vektor-Kacheln für Leaflet (Standard), png: Bild-Kacheln, beide
  --zoom <von-bis>       Zoomstufen der PNG-Kacheln (Standard: 0-9)
  --vector-zoom <n>      höchste Zoomstufe der Vektor-Kacheln, darüber wird scharf vergrößert (Standard: 8)
  --dlc <auto|alle>      auto: nur installierte DLCs rendern (Standard), alle: ts-map-Standard
  --dlc-an <guard>       DLC-Guard erzwingen, z.B. dlc_wa_and_or (mehrfach möglich)
  --dlc-aus <guard>      DLC-Guard abschalten (mehrfach möglich)
  --no-tiles             nur JSON-Dateien exportieren
  --no-mods              ohne Mods rendern
  --list                 nur erkannte Mods, Reihenfolge und DLCs anzeigen
  --verbose              ts-map-Log auch auf der Konsole ausgeben (sonst nur in der Logdatei)

  TsMap.Cli --analyse <datei> [--analyse <datei> ...] [--analyse-karte <name>]
                         untersucht Mod-Archive (Platzhalter erlaubt, z.B. ""...\mod\ROEX*.scs"") und
                         schreibt das Ergebnis zusätzlich nach %LOCALAPPDATA%\ts-map\Analyse.txt;
                         --analyse-karte: Kartenname für die Sektorsuche (Standard: europe und usa)");
        }

        private static int RunAnalyse(Options o)
        {
            var files = new List<string>();
            foreach (var pattern in o.Analyse)
            {
                var dir = Path.GetDirectoryName(pattern);
                var name = Path.GetFileName(pattern);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                if (name.IndexOfAny(new[] { '*', '?' }) >= 0 && Directory.Exists(dir))
                    files.AddRange(Directory.GetFiles(dir, name).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                else if (File.Exists(pattern)) files.Add(pattern);
                else Console.Error.WriteLine($"Nicht gefunden: {pattern}");
            }
            if (files.Count == 0) return 1;

            var maps = o.AnalyseMaps.Count > 0 ? o.AnalyseMaps : new List<string> { "europe", "usa" };
            var outFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ts-map", "Analyse.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            using (var writer = new StreamWriter(outFile, false, new System.Text.UTF8Encoding(true)))
            {
                foreach (var file in files)
                {
                    Console.Error.WriteLine($"Analysiere {Path.GetFileName(file)} ...");
                    var text = TsMap.FileSystem.ArchiveAnalyzer.Analyse(file, maps);
                    Console.Write(text);
                    writer.Write(text);
                }
            }
            Console.WriteLine($"Gespeichert: {outFile}");
            return 0;
        }
    }

    /// <summary>Gleiche Farben wie TsMap.Canvas.SimpleMapPalette.</summary>
    internal class SimpleMapPalette : MapPalette
    {
        public SimpleMapPalette()
        {
            Background = new SolidBrush(Color.FromArgb(72, 78, 102));
            Road = Brushes.White;
            PrefabRoad = Brushes.White;
            PrefabLight = new SolidBrush(Color.FromArgb(236, 203, 153));
            PrefabDark = new SolidBrush(Color.FromArgb(225, 163, 56));
            PrefabGreen = new SolidBrush(Color.FromArgb(170, 203, 150));
            CityName = Brushes.LightCoral;
            FerryLines = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            Error = Brushes.LightCoral;
        }
    }
}
