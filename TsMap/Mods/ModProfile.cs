using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TsMap.Helpers.Logger;

namespace TsMap.Mods
{
    public enum Game
    {
        Ets2,
        Ats,
    }

    public static class GameInfo
    {
        public static string Name(Game game)
        {
            return game == Game.Ets2 ? "Euro Truck Simulator 2" : "American Truck Simulator";
        }

        public static string SteamAppId(Game game)
        {
            return game == Game.Ets2 ? "227300" : "270880";
        }

        public static string DocumentsDir(Game game)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Name(game));
        }
    }

    /// <summary>
    /// Finds the Steam installation, its library folders and the game/workshop directories.
    /// </summary>
    public static class SteamLocator
    {
        public static List<string> GetLibraryFolders()
        {
            var result = new List<string>();
            var steamPath = GetSteamPath();
            if (steamPath == null) return result;

            result.Add(steamPath);

            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var path = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (!result.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                        result.Add(path);
                }
            }
            return result.Where(Directory.Exists).ToList();
        }

        public static string FindGameDir(Game game)
        {
            return GetLibraryFolders()
                .Select(lib => Path.Combine(lib, "steamapps", "common", GameInfo.Name(game)))
                .FirstOrDefault(Directory.Exists);
        }

        public static List<string> FindWorkshopDirs(Game game)
        {
            return GetLibraryFolders()
                .Select(lib => Path.Combine(lib, "steamapps", "workshop", "content", GameInfo.SteamAppId(game)))
                .Where(Directory.Exists)
                .ToList();
        }

        private static string GetSteamPath()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var path = key?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path.Replace('/', Path.DirectorySeparatorChar);
                }
            }
            catch (Exception)
            {
                // no registry (e.g. not on Windows)
            }

            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(fallback) ? fallback : null;
        }
    }

    /// <summary>
    /// One entry of "active_mods" in profile.sii, e.g. "mod_workshop_package.00000000BAADF00D|ProMods".
    /// </summary>
    public class ActiveMod
    {
        public string RawEntry { get; }
        public string PackageName { get; }
        public string DisplayName { get; }

        /// <summary>
        /// Steam Workshop item id for workshop mods, null for local mods
        /// </summary>
        public ulong? WorkshopId { get; }

        public ActiveMod(string rawEntry)
        {
            RawEntry = rawEntry;
            var sep = rawEntry.IndexOf('|');
            PackageName = sep < 0 ? rawEntry : rawEntry.Substring(0, sep);
            DisplayName = sep < 0 ? PackageName : rawEntry.Substring(sep + 1);

            const string workshopPrefix = "mod_workshop_package.";
            ulong id;
            if (PackageName.StartsWith(workshopPrefix, StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(PackageName.Substring(workshopPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id))
            {
                WorkshopId = id;
            }
        }

        public override string ToString()
        {
            return WorkshopId.HasValue ? $"{DisplayName} (Workshop {WorkshopId})" : $"{DisplayName} ({PackageName})";
        }
    }

    public class ProfileInfo
    {
        public string Directory { get; set; }
        public string FolderName { get; set; }
        public string Name { get; set; }
        public bool IsSteamProfile { get; set; }
        public DateTime LastWrite { get; set; }

        /// <summary>
        /// Active mods, highest priority (top of the in-game list) first
        /// </summary>
        public List<ActiveMod> ActiveMods { get; set; } = new List<ActiveMod>();

        public override string ToString()
        {
            return $"{Name} ({(IsSteamProfile ? "Steam" : "lokal")}, {FolderName})";
        }
    }

    public static class ProfileReader
    {
        /// <summary>
        /// All profiles of a game, most recently used first.
        /// </summary>
        public static List<ProfileInfo> FindProfiles(Game game, string documentsDir = null)
        {
            documentsDir = documentsDir ?? GameInfo.DocumentsDir(game);
            var result = new List<ProfileInfo>();

            foreach (var sub in new[] { "steam_profiles", "profiles" })
            {
                var root = Path.Combine(documentsDir, sub);
                if (!System.IO.Directory.Exists(root)) continue;

                foreach (var dir in System.IO.Directory.GetDirectories(root))
                {
                    var file = Path.Combine(dir, "profile.sii");
                    if (!File.Exists(file)) continue;
                    result.Add(new ProfileInfo
                    {
                        Directory = dir,
                        FolderName = Path.GetFileName(dir),
                        Name = DecodeFolderName(Path.GetFileName(dir)),
                        IsSteamProfile = sub == "steam_profiles",
                        LastWrite = File.GetLastWriteTime(file),
                    });
                }
            }
            return result.OrderByDescending(p => p.LastWrite).ToList();
        }

        /// <summary>
        /// Selects a profile by name or folder name (case insensitive), or the most recently used one.
        /// </summary>
        public static ProfileInfo SelectProfile(Game game, string nameOrFolder = null, string documentsDir = null)
        {
            var profiles = FindProfiles(game, documentsDir);
            ProfileInfo profile;
            if (string.IsNullOrEmpty(nameOrFolder))
            {
                profile = profiles.FirstOrDefault();
            }
            else if (System.IO.Directory.Exists(nameOrFolder))
            {
                profile = new ProfileInfo
                {
                    Directory = nameOrFolder,
                    FolderName = Path.GetFileName(nameOrFolder.TrimEnd('\\', '/')),
                    Name = DecodeFolderName(Path.GetFileName(nameOrFolder.TrimEnd('\\', '/'))),
                    LastWrite = File.GetLastWriteTime(Path.Combine(nameOrFolder, "profile.sii")),
                };
            }
            else
            {
                profile = profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, nameOrFolder, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.FolderName, nameOrFolder, StringComparison.OrdinalIgnoreCase));
            }

            if (profile != null) Load(profile);
            return profile;
        }

        public static void Load(ProfileInfo profile)
        {
            var units = SiiFile.Read(Path.Combine(profile.Directory, "profile.sii"));
            var userProfile = units.FirstOrDefault(u => u.ClassName == "user_profile") ?? units.FirstOrDefault(u => u.Fields.ContainsKey("active_mods"));
            if (userProfile == null) throw new InvalidDataException("profile.sii enthält keine user_profile-Einheit");

            var name = userProfile.GetString("profile_name");
            if (!string.IsNullOrEmpty(name)) profile.Name = name;

            profile.ActiveMods = userProfile.GetArray("active_mods")
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => new ActiveMod(e))
                .ToList();
        }

        /// <summary>
        /// Profile folders are named after the hex encoded (UTF-8) profile name.
        /// </summary>
        public static string DecodeFolderName(string folderName)
        {
            if (folderName.Length % 2 != 0 || !folderName.All(Uri.IsHexDigit)) return folderName;
            try
            {
                var bytes = new byte[folderName.Length / 2];
                for (var i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(folderName.Substring(i * 2, 2), 16);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return folderName;
            }
        }
    }

    /// <summary>
    /// Turns the active mods of a profile into mod sources (files or unpacked folders) for <see cref="TsMapper"/>.
    /// </summary>
    public static class ModResolver
    {
        /// <param name="gameVersion">e.g. "1.61" or "1.61.1.6"; used to pick the right package of multi-version Workshop mods. Null = newest package.</param>
        /// <returns>Mods with <see cref="Mod.Load"/> = true, highest priority first (same order as the in-game list)</returns>
        public static List<Mod> Resolve(IEnumerable<ActiveMod> activeMods, string localModDir, IEnumerable<string> workshopDirs,
            string gameVersion, List<string> warnings)
        {
            var result = new List<Mod>();
            var workshop = (workshopDirs ?? Enumerable.Empty<string>()).ToList();

            foreach (var activeMod in activeMods)
            {
                var paths = activeMod.WorkshopId.HasValue
                    ? ResolveWorkshop(activeMod, workshop, gameVersion, warnings)
                    : ResolveLocal(activeMod, localModDir);

                if (paths.Count == 0)
                {
                    warnings?.Add($"Mod nicht gefunden: {activeMod}");
                    continue;
                }

                foreach (var path in paths)
                    result.Add(new Mod(path) { Load = true, DisplayName = paths.Count == 1 ? activeMod.DisplayName : $"{activeMod.DisplayName} [{Path.GetFileName(path)}]" });
            }
            return result;
        }

        private static List<string> ResolveLocal(ActiveMod mod, string localModDir)
        {
            if (string.IsNullOrEmpty(localModDir)) return new List<string>();
            foreach (var candidate in new[] { mod.PackageName + ".scs", mod.PackageName + ".zip", mod.PackageName })
            {
                var path = Path.Combine(localModDir, candidate);
                if (File.Exists(path)) return new List<string> { path };
                if (Directory.Exists(path)) return ExpandPackage(path);
            }
            return new List<string>();
        }

        private static List<string> ResolveWorkshop(ActiveMod mod, List<string> workshopDirs, string gameVersion, List<string> warnings)
        {
            var itemDir = workshopDirs
                .Select(d => Path.Combine(d, mod.WorkshopId.Value.ToString(CultureInfo.InvariantCulture)))
                .FirstOrDefault(Directory.Exists);
            if (itemDir == null) return new List<string>();

            var versionsFile = Path.Combine(itemDir, "versions.sii");
            if (!File.Exists(versionsFile)) return ExpandPackage(itemDir);

            List<SiiUnit> packages;
            try
            {
                packages = SiiFile.Read(versionsFile).Where(u => u.ClassName == "package_version_info").ToList();
            }
            catch (Exception e)
            {
                warnings?.Add($"versions.sii von {mod} nicht lesbar: {e.Message}");
                return ExpandPackage(itemDir);
            }

            var package = SelectPackage(packages, gameVersion);
            if (package == null)
            {
                warnings?.Add($"Kein passendes Paket in versions.sii für {mod}");
                return new List<string>();
            }

            var packageName = package.GetString("package_name") ?? "";
            foreach (var candidate in new[] { packageName, packageName + ".scs", packageName + ".zip" })
            {
                var path = Path.Combine(itemDir, candidate);
                if (Directory.Exists(path)) return ExpandPackage(path);
                if (File.Exists(path)) return new List<string> { path };
            }
            return new List<string>();
        }

        /// <summary>
        /// Like the game: the first package whose compatible_versions match the game version,
        /// otherwise the package without compatible_versions (universal). Without a game version the newest package wins.
        /// </summary>
        private static SiiUnit SelectPackage(List<SiiUnit> packages, string gameVersion)
        {
            var universal = packages.FirstOrDefault(p => p.GetArray("compatible_versions").Count == 0);

            if (!string.IsNullOrEmpty(gameVersion))
            {
                var match = packages.FirstOrDefault(p => p.GetArray("compatible_versions").Any(v => VersionMatches(v, gameVersion)));
                return match ?? universal;
            }

            var newest = packages
                .Where(p => p.GetArray("compatible_versions").Count > 0)
                .OrderByDescending(p => p.GetArray("compatible_versions").Select(ParseVersion).Max())
                .FirstOrDefault();
            return newest ?? universal ?? packages.FirstOrDefault();
        }

        private static bool VersionMatches(string pattern, string version)
        {
            var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "$";
            // "1.61" should also match "1.61.*" style patterns and vice versa
            return Regex.IsMatch(version, regex) || Regex.IsMatch(version + ".0", regex);
        }

        private static Version ParseVersion(string pattern)
        {
            var parts = pattern.Replace("*", "9999").Split('.')
                .Select(p => { int n; return int.TryParse(p, out n) ? n : 0; })
                .Concat(Enumerable.Repeat(0, 4))
                .Take(4)
                .ToArray();
            return new Version(parts[0], parts[1], parts[2], parts[3]);
        }

        /// <summary>
        /// A package folder either contains archives (*.scs / *.zip) or is an unpacked mod itself.
        /// </summary>
        private static List<string> ExpandPackage(string dir)
        {
            var isUnpacked = File.Exists(Path.Combine(dir, "manifest.sii"))
                             || Directory.Exists(Path.Combine(dir, "def"))
                             || Directory.Exists(Path.Combine(dir, "map"));
            if (isUnpacked) return new List<string> { dir };

            var archives = Directory.GetFiles(dir, "*.scs").Concat(Directory.GetFiles(dir, "*.zip"))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (archives.Count == 0) Logger.Instance.Warning($"Keine Mod-Dateien in '{dir}'");
            return archives;
        }
    }
}
