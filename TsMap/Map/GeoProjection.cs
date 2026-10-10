using System;

namespace TsMap.Map
{
    /// <summary>
    /// Rechnet Spielkoordinaten (x, z) in geografische Länge/Breite (WGS84) um.
    ///
    /// Beide Spiele benutzen eine Lambert-Kegelprojektion (konform, zwei Standardparallelen) auf einer Kugel.
    /// Die Parameter stehen im Spiel in def/climate.sii (map_projection, standard_paralel1/2, map_origin,
    /// map_factor, bei ETS2 zusätzlich map_offset). Der Kugelradius kürzt sich heraus.
    /// </summary>
    public sealed class GeoProjection
    {
        private const double EarthRadius = 6370997.0;
        private static readonly double Deg = EarthRadius * Math.PI / 180.0;

        private readonly double _factorX, _factorZ, _offsetX, _offsetZ;
        private readonly double _lon0, _n, _f, _rho0;
        private readonly bool _ukScaling;

        public static readonly GeoProjection Ats = new GeoProjection(
            parallel1: 33, parallel2: 45, originLat: 39, originLon: -96,
            factorZ: -0.00017706234, factorX: 0.000176689948, offsetX: 0, offsetZ: 0, ukScaling: false);

        public static readonly GeoProjection Ets2 = new GeoProjection(
            parallel1: 37, parallel2: 65, originLat: 50, originLon: 15,
            factorZ: -0.000171570875, factorX: 0.0001729241463, offsetX: 16660, offsetZ: 4150, ukScaling: true);

        public static GeoProjection For(bool isEts2) => isEts2 ? Ets2 : Ats;

        private GeoProjection(double parallel1, double parallel2, double originLat, double originLon,
            double factorZ, double factorX, double offsetX, double offsetZ, bool ukScaling)
        {
            _factorX = factorX;
            _factorZ = factorZ;
            _offsetX = offsetX;
            _offsetZ = offsetZ;
            _ukScaling = ukScaling;

            double p1 = Rad(parallel1), p2 = Rad(parallel2), p0 = Rad(originLat);
            _lon0 = Rad(originLon);
            _n = Math.Log(Math.Cos(p1) / Math.Cos(p2)) / Math.Log(Math.Tan(Math.PI / 4 + p2 / 2) / Math.Tan(Math.PI / 4 + p1 / 2));
            _f = Math.Cos(p1) * Math.Pow(Math.Tan(Math.PI / 4 + p1 / 2), _n) / _n;
            _rho0 = EarthRadius * _f / Math.Pow(Math.Tan(Math.PI / 4 + p0 / 2), _n);
        }

        /// <summary>Spielkoordinaten (x, z) -> (Länge, Breite) in Grad.</summary>
        public (double lon, double lat) ToLonLat(double x, double z) => ToLonLat(x, z, RegionOf(x, z));

        /// <summary>
        /// Bereich mit eigener Umrechnung (nur ETS2): 0 Festland, 1 Britische Inseln, 2 Island, 3 Färöer, 4 Grönland.
        /// </summary>
        public int RegionOf(double x, double z)
        {
            if (!_ukScaling) return Mainland;
            if (x < -110000) return Greenland;
            if (x < -60000 && z < -84000) return Iceland;
            if (x > -58000 && x < -44000 && z < -76000) return Faroe;
            return IsUk(x, z) ? Uk : Mainland;
        }

        public const int Mainland = 0, Uk = 1, Iceland = 2, Faroe = 3, Greenland = 4;

        // Island, Färöer, Grönland (ProMods) liegen im Spiel nordwestlich von Großbritannien, aber weder im Maßstab
        // des Festlands noch Großbritanniens. Ausgeglichen an den Städten (echte Koordinaten): affine Umrechnung der
        // Spielkoordinaten, danach wie Festland. Abweichung meist unter 15 km.
        private const double IslA = 0.83286, IslB = 0.06311, IslC = -0.06715, IslD = 0.83801;
        private static readonly (double x, double z)[] IslOffset =
        {
            (0, 0), (0, 0), (10772.7, -22932.9), (7163.2, -7578.3), (-7149.9, -31776.4),
        };

        /// <summary>
        /// Wie <see cref="ToLonLat(double, double)"/>, aber mit vorgegebenem Bereich (<see cref="RegionOf"/>).
        /// Für Linien, die über die Grenze laufen (Fähren), damit sie nicht mitten im Meer springen.
        /// </summary>
        public (double lon, double lat) ToLonLat(double x, double z, int region)
        {
            if (_ukScaling && region >= Iceland)
            {
                var (ox, oz) = IslOffset[region];
                (x, z) = (IslA * x + IslB * z + ox, IslC * x + IslD * z + oz);
            }

            x -= _offsetX;
            z -= _offsetZ;

            if (_ukScaling && region == Uk)
            {
                // Großbritannien ist in ETS2 in größerem Maßstab gebaut als das Festland
                // (ca. 1:14,4 statt 1:19,1) und an Calais angesetzt.
                const double ukScale = 0.75, calaisX = -31100, calaisZ = -5500;
                x = (x + calaisX / 2) * ukScale;
                z = (z + calaisZ / 2) * ukScale;
            }

            // Spielmeter -> Meter auf der Projektionsebene
            double px = x * _factorX * Deg;
            double py = z * _factorZ * Deg;

            // Umkehrung der Lambert-Kegelprojektion (Kugel)
            double dy = _rho0 - py;
            double rho = Math.Sign(_n) * Math.Sqrt(px * px + dy * dy);
            double theta = Math.Atan2(Math.Sign(_n) * px, Math.Sign(_n) * dy);
            double lat = 2 * Math.Atan(Math.Pow(EarthRadius * _f / rho, 1 / _n)) - Math.PI / 2;
            double lon = _lon0 + theta / _n;
            return (lon * 180 / Math.PI, lat * 180 / Math.PI);
        }

        // Britische Inseln: Sektoren (4000 m) links oberhalb des Sektors mit Calais
        private static bool IsUk(double x, double z)
        {
            int sx = (int)Math.Floor(x / 4000), sz = (int)Math.Floor(z / 4000);
            return sx <= -8 && sz <= -2 && !(sx == -8 && sz == -2);
        }

        private static double Rad(double deg) => deg * Math.PI / 180;
    }
}
