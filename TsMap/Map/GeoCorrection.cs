using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TsMap.Map
{
    /// <summary>
    /// Feinkorrektur nach <see cref="GeoProjection"/>: Die Spielkarte ist nicht überall gleich verzerrt (ETS2:
    /// Festland ~40 km zu weit westlich, Nahost ~80 km zu weit nördlich, ...). Aus Städten mit bekannten echten
    /// Koordinaten (<see cref="GeoAnchors"/>) wird ein Versatzfeld berechnet (gewichtetes Mittel, weich zwischen den
    /// Städten) und als Raster abgelegt. Dasselbe Raster nutzt die Webseite (GeoCorrection.json) für Route/Städte.
    /// </summary>
    public sealed class GeoCorrection
    {
        // Glättung in km: Städte näher als das mitteln sich, weiter weg zählt die nächste Stadt am meisten
        private const double SmoothKm = 40;

        public double Lon0 { get; private set; }
        public double Lat0 { get; private set; }
        public double Step { get; private set; }
        public int Nx { get; private set; }
        public int Ny { get; private set; }
        private float[] _dLon, _dLat;

        public int AnchorCount { get; private set; }
        /// <summary>Größte Abweichung einer Stadt vor/nach der Korrektur in km.</summary>
        public double MaxErrorBeforeKm { get; private set; }
        public double MaxErrorAfterKm { get; private set; }
        public string MaxErrorAfterCity { get; private set; }

        public static GeoCorrection Build(GeoProjection projection,
            IList<(string name, double x, double z, double lon, double lat)> anchors, double step = 0.5)
        {
            if (anchors.Count == 0) return null;

            var pts = anchors.Select(a =>
            {
                var (lon, lat) = projection.ToLonLat(a.x, a.z);
                return (a.name, lon, lat, dLon: a.lon - lon, dLat: a.lat - lat);
            }).ToList();

            var c = new GeoCorrection { Step = step, AnchorCount = pts.Count };
            const double margin = 6;
            c.Lon0 = Math.Floor((pts.Min(p => p.lon) - margin) / step) * step;
            c.Lat0 = Math.Floor((pts.Min(p => p.lat) - margin) / step) * step;
            c.Nx = (int)Math.Ceiling((pts.Max(p => p.lon) + margin - c.Lon0) / step) + 1;
            c.Ny = (int)Math.Ceiling((Math.Min(89, pts.Max(p => p.lat) + margin) - c.Lat0) / step) + 1;
            c._dLon = new float[c.Nx * c.Ny];
            c._dLat = new float[c.Nx * c.Ny];

            for (var j = 0; j < c.Ny; j++)
            {
                var lat = c.Lat0 + j * step;
                var kmLon = 111.32 * Math.Cos(lat * Math.PI / 180);
                for (var i = 0; i < c.Nx; i++)
                {
                    var lon = c.Lon0 + i * step;
                    double sw = 0, sLon = 0, sLat = 0;
                    foreach (var p in pts)
                    {
                        var dx = (lon - p.lon) * kmLon;
                        var dy = (lat - p.lat) * 110.57;
                        var d2 = dx * dx + dy * dy + SmoothKm * SmoothKm;
                        var w = 1 / (d2 * d2);
                        sw += w;
                        sLon += w * p.dLon;
                        sLat += w * p.dLat;
                    }
                    c._dLon[j * c.Nx + i] = (float)(sLon / sw);
                    c._dLat[j * c.Nx + i] = (float)(sLat / sw);
                }
            }

            foreach (var p in pts)
            {
                double realLon = p.lon + p.dLon, realLat = p.lat + p.dLat;
                c.MaxErrorBeforeKm = Math.Max(c.MaxErrorBeforeKm, DistanceKm(p.lon, p.lat, realLon, realLat));
                double lon = p.lon, lat = p.lat;
                c.Apply(ref lon, ref lat);
                var after = DistanceKm(lon, lat, realLon, realLat);
                if (after > c.MaxErrorAfterKm)
                {
                    c.MaxErrorAfterKm = after;
                    c.MaxErrorAfterCity = p.name;
                }
            }
            return c;
        }

        /// <summary>Versatz an (lon, lat) bilinear aus dem Raster addieren (außerhalb: Randwert).</summary>
        public void Apply(ref double lon, ref double lat)
        {
            var fx = Math.Max(0, Math.Min(Nx - 1.000001, (lon - Lon0) / Step));
            var fy = Math.Max(0, Math.Min(Ny - 1.000001, (lat - Lat0) / Step));
            int i = (int)fx, j = (int)fy;
            double tx = fx - i, ty = fy - j;
            lon += Bilinear(_dLon, i, j, tx, ty);
            lat += Bilinear(_dLat, i, j, tx, ty);
        }

        private double Bilinear(float[] g, int i, int j, double tx, double ty)
        {
            var k = j * Nx + i;
            return (g[k] * (1 - tx) + g[k + 1] * tx) * (1 - ty) + (g[k + Nx] * (1 - tx) + g[k + Nx + 1] * tx) * ty;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"lon0\":").Append(Num(Lon0)).Append(",\"lat0\":").Append(Num(Lat0))
              .Append(",\"step\":").Append(Num(Step)).Append(",\"nx\":").Append(Nx).Append(",\"ny\":").Append(Ny)
              .Append(",\"dlon\":[").Append(string.Join(",", _dLon.Select(v => Num(Math.Round(v, 4))))).Append(']')
              .Append(",\"dlat\":[").Append(string.Join(",", _dLat.Select(v => Num(Math.Round(v, 4))))).Append("]}");
            return sb.ToString();
        }

        private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static double DistanceKm(double lon1, double lat1, double lon2, double lat2)
        {
            var dx = (lon2 - lon1) * 111.32 * Math.Cos((lat1 + lat2) / 2 * Math.PI / 180);
            var dy = (lat2 - lat1) * 110.57;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
