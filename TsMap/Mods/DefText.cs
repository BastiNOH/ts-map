using System;
using System.Text;
using TsMap.FileSystem;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap.Mods
{
    /// <summary>
    /// Liest Definitionsdateien (.sii/.sui/.mat) als Text - auch wenn Mods sie verschlüsselt
    /// (ScsC) oder 3nK-verschleiert ablegen. Binäre BSII-Dateien werden nicht unterstützt.
    /// </summary>
    public static class DefText
    {
        private const uint SignatureScsC = 0x43736353; // "ScsC"
        private const uint SignatureBsii = 0x49495342; // "BSII"
        private const uint Signature3nK = 0x014B6E33;  // "3nK\x01"

        public static string Read(UberFile file, string path)
        {
            if (file == null) return null;
            return Decode(file.Entry.Read(), path);
        }

        public static string Decode(byte[] data, string path)
        {
            if (data == null) return null;
            if (data.Length < 4) return Encoding.UTF8.GetString(data);

            try
            {
                var signature = BitConverter.ToUInt32(data, 0);
                if (signature == SignatureScsC)
                {
                    data = SiiFile.Decrypt(data);
                    if (data.Length >= 4) signature = BitConverter.ToUInt32(data, 0);
                }

                if (signature == Signature3nK)
                {
                    var text = MemoryHelper.Decrypt3Nk(data);
                    if (text != null) return text;
                }

                if (signature == SignatureBsii)
                {
                    Logger.Instance.Warning($"'{path}' is a binary SII file (not supported), skipped");
                    return "";
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Could not decode '{path}': {e.Message}");
                return "";
            }

            return Encoding.UTF8.GetString(data);
        }
    }
}
