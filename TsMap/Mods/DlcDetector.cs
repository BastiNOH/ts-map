using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsMap.Common;

namespace TsMap.Mods
{
    /// <summary>
    /// Prüft, welche DLCs im Spielordner installiert sind (dlc_*.scs), und aktiviert nur die
    /// DLC-Guards, deren DLCs alle vorhanden sind. Inhalte nicht gekaufter DLCs (z.B. Grenzübergänge)
    /// werden so nicht gezeichnet.
    /// </summary>
    public static class DlcDetector
    {
        public class GuardResult
        {
            public DlcGuard Guard;
            public bool Enabled;
            public List<string> MissingDlcs = new List<string>();
            public string Reason;
        }

        public class Report
        {
            public List<string> InstalledDlcs = new List<string>();
            public List<GuardResult> Guards = new List<GuardResult>();

            /// <summary>Installierte DLC-Archive, die zu keinem bekannten Guard passen (Fahrzeug-/Ladungs-DLCs oder neue Karten-DLCs)</summary>
            public List<string> UnmatchedDlcs = new List<string>();

            /// <summary>true, wenn keine DLC-Datei erkannt wurde und deshalb nichts geändert wurde</summary>
            public bool DetectionSkipped;
        }

        // Guard-Kürzel -> mögliche Dateinamen (ohne .scs), falls diese vom Guard-Namen abweichen
        private static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "dlc_blke", new[] { "dlc_blke", "dlc_balkan_e", "dlc_balkan_east" } },
            { "dlc_blkw", new[] { "dlc_blkw", "dlc_balkan_w", "dlc_balkan_west" } },
            { "dlc_nevada", new[] { "dlc_nevada", "dlc_nv" } },
            { "dlc_arizona", new[] { "dlc_arizona", "dlc_az" } },
            { "dlc_polar", new[] { "dlc_polar", "dlc_nordic" } },
        };

        public static List<string> FindInstalledDlcs(string gameDir)
        {
            if (!Directory.Exists(gameDir)) return new List<string>();
            return Directory.GetFiles(gameDir, "dlc_*.scs")
                .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>
        /// "dlc_wa_and_or" -> ["dlc_wa", "dlc_or"]
        /// </summary>
        public static List<string> GetRequiredDlcs(string guardName)
        {
            return guardName.Split(new[] { "_and_" }, StringSplitOptions.None)
                .Select(p => p.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase) ? p.ToLowerInvariant() : "dlc_" + p.ToLowerInvariant())
                .ToList();
        }

        /// <param name="forceEnable">Guard-Namen, die immer aktiv sein sollen</param>
        /// <param name="forceDisable">Guard-Namen, die immer inaktiv sein sollen</param>
        public static Report Apply(List<DlcGuard> guards, string gameDir, ICollection<string> forceEnable = null, ICollection<string> forceDisable = null)
        {
            var report = new Report { InstalledDlcs = FindInstalledDlcs(gameDir) };
            var used = new HashSet<string>();

            // Ohne einzige DLC-Datei ist die Erkennung unzuverlässig (z.B. falscher Ordner) -> nichts ändern
            report.DetectionSkipped = report.InstalledDlcs.Count == 0;

            foreach (var guard in guards)
            {
                var result = new GuardResult { Guard = guard };

                if (guard.Index == 0)
                {
                    result.Enabled = true;
                    result.Reason = "immer aktiv";
                }
                else if (forceEnable != null && forceEnable.Contains(guard.Name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Enabled = true;
                    result.Reason = "manuell aktiviert";
                }
                else if (forceDisable != null && forceDisable.Contains(guard.Name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Enabled = false;
                    result.Reason = "manuell deaktiviert";
                }
                else if (report.DetectionSkipped)
                {
                    result.Enabled = guard.Enabled;
                    result.Reason = "Standard (keine DLC-Dateien gefunden)";
                }
                else
                {
                    foreach (var dlc in GetRequiredDlcs(guard.Name))
                    {
                        var match = FindDlcFile(dlc, report.InstalledDlcs);
                        if (match == null) result.MissingDlcs.Add(dlc);
                        else used.Add(match);
                    }
                    result.Enabled = result.MissingDlcs.Count == 0;
                    result.Reason = result.Enabled ? "installiert" : "fehlt: " + string.Join(", ", result.MissingDlcs);
                }

                guard.Enabled = result.Enabled;
                report.Guards.Add(result);
            }

            report.UnmatchedDlcs = report.InstalledDlcs.Where(d => !used.Contains(d)).ToList();
            return report;
        }

        private static string FindDlcFile(string dlc, List<string> installed)
        {
            string[] names;
            if (!Aliases.TryGetValue(dlc, out names)) names = new[] { dlc };

            foreach (var name in names)
            {
                // exakt "dlc_or" oder mit Zusatz "dlc_or_xyz" – aber nicht "dlc_ok" für "dlc_o"
                var hit = installed.FirstOrDefault(f => f == name || f.StartsWith(name + "_", StringComparison.Ordinal));
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
