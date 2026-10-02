using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TsMap.Common;
using TsMap.Helpers;
using TsMap.TsItem;

namespace TsMap.Map
{
    /// <summary>
    /// Exportiert die Kartengeometrie als Vektor-Kacheln für eine Leaflet-Karte.
    ///
    /// Aufbau: &lt;out&gt;/Vector/{z}/{x}/{y}.json für z = 0..maxZoom (gleiches Kachelraster wie die PNG-Kacheln).
    /// Jede Kachel enthält die Formen, die sie schneiden, in Pixelkoordinaten der Kachel (0..256):
    ///   {"f":[[ebene, farbe, zIndex, flags, breitePx, [x0,y0,x1,y1,...]], ...]}
    ///   ebene: 0 Gebiet, 1 Prefab, 2 Straße, 3 Fähre; farbe: 0 Straße, 1 hell, 2 dunkel, 3 grün, 4 Fähre
    ///   flags: 1 = Fläche (sonst Linie), 2 = geheim/gestrichelt
    /// Über maxZoom zeichnet der Browser die Kacheln von maxZoom vergrößert (weiterhin scharf).
    /// Auf niedrigen Zoomstufen wird vereinfacht und Kleinstes weggelassen.
    /// </summary>
    public class VectorExporter
    {
        private const int TileSize = 256;

        private class Feature
        {
            public byte Layer;
            public byte Color;
            public int ZIndex;
            public bool Polygon;
            public bool Dashed;
            public float Width;     // Weltmaß (nur Linien)
            public float[] Points;  // x0,z0,x1,z1,... in Weltkoordinaten
            public float MinX, MinZ, MaxX, MaxZ;
        }

        private readonly TsMapper _mapper;
        private readonly List<Feature> _features = new List<Feature>();

        public VectorExporter(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public int FeatureCount => _features.Count;

        /// <summary>Sammelt alle Formen (berücksichtigt aktive DLC-Guards, wie der Renderer).</summary>
        public void Collect(bool includeSecret = true)
        {
            _features.Clear();
            var activeDlcGuards = new HashSet<byte>(_mapper.GetDlcGuardsForCurrentGame().Where(x => x.Enabled).Select(x => x.Index));

            CollectMapAreas(activeDlcGuards, includeSecret);
            CollectPrefabs(activeDlcGuards, includeSecret);
            CollectRoads(activeDlcGuards, includeSecret);
            CollectFerries();
        }

        /// <param name="originX">Weltkoordinate der linken oberen Ecke von Kachel 0/0/0</param>
        /// <param name="originZ">Weltkoordinate der linken oberen Ecke von Kachel 0/0/0</param>
        /// <param name="scale0">Pixel pro Welteinheit auf Zoomstufe 0</param>
        /// <returns>Anzahl geschriebener Kacheln</returns>
        public long Export(string outDir, float originX, float originZ, float scale0, int maxZoom, Action<string> progress = null)
        {
            var root = Path.Combine(outDir, "Vector");
            if (Directory.Exists(root)) Directory.Delete(root, true);

            long written = 0;
            for (var z = 0; z <= maxZoom; z++)
            {
                double scale = scale0 * Math.Pow(2, z);
                int tilesPerSide = 1 << z;
                bool simplify = z < maxZoom;
                double minPx = simplify ? 0.6 : 0.0;     // kleiner als ~1/2 Pixel -> weglassen
                double tolerance = simplify ? 0.35 / scale : 0.0;

                var tiles = new Dictionary<long, List<int>>();
                var geometry = new float[_features.Count][];

                for (var i = 0; i < _features.Count; i++)
                {
                    var f = _features[i];
                    var w = (f.MaxX - f.MinX) * scale;
                    var h = (f.MaxZ - f.MinZ) * scale;
                    if (w < minPx && h < minPx) continue;

                    var pts = simplify ? Simplify(f.Points, tolerance, f.Polygon) : f.Points;
                    if (pts.Length < (f.Polygon ? 6 : 4)) continue;
                    geometry[i] = pts;

                    // Linien ragen um ihre halbe Breite über die Mittellinie hinaus
                    var pad = f.Polygon ? 0 : f.Width / 2;
                    int tx0 = Clamp((int)Math.Floor((f.MinX - pad - originX) * scale / TileSize), tilesPerSide);
                    int tx1 = Clamp((int)Math.Floor((f.MaxX + pad - originX) * scale / TileSize), tilesPerSide);
                    int ty0 = Clamp((int)Math.Floor((f.MinZ - pad - originZ) * scale / TileSize), tilesPerSide);
                    int ty1 = Clamp((int)Math.Floor((f.MaxZ + pad - originZ) * scale / TileSize), tilesPerSide);

                    for (var tx = tx0; tx <= tx1; tx++)
                    for (var ty = ty0; ty <= ty1; ty++)
                    {
                        var key = ((long)tx << 32) | (uint)ty;
                        List<int> list;
                        if (!tiles.TryGetValue(key, out list))
                        {
                            list = new List<int>();
                            tiles.Add(key, list);
                        }
                        list.Add(i);
                    }
                }

                foreach (var tile in tiles)
                {
                    int tx = (int)(tile.Key >> 32);
                    int ty = (int)(tile.Key & 0xFFFFFFFF);
                    double ox = originX + tx * TileSize / scale;
                    double oz = originZ + ty * TileSize / scale;

                    var dir = Path.Combine(root, z.ToString(), tx.ToString());
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, ty + ".json"), TileJson(tile.Value, geometry, ox, oz, scale));
                    written++;
                }

                progress?.Invoke($"Vektor-Zoomstufe {z}: {tiles.Count} Kacheln");
            }
            return written;
        }

        private static int Clamp(int v, int tilesPerSide)
        {
            return v < 0 ? 0 : v >= tilesPerSide ? tilesPerSide - 1 : v;
        }

        private string TileJson(List<int> featureIndices, float[][] geometry, double ox, double oz, double scale)
        {
            var sb = new StringBuilder("{\"f\":[");
            var first = true;
            foreach (var i in featureIndices
                         .OrderBy(i => _features[i].Layer)
                         .ThenBy(i => _features[i].ZIndex))
            {
                var f = _features[i];
                var pts = geometry[i];
                if (!first) sb.Append(',');
                first = false;

                var flags = (f.Polygon ? 1 : 0) | (f.Dashed ? 2 : 0);
                sb.Append('[').Append(f.Layer).Append(',').Append(f.Color).Append(',').Append(f.ZIndex)
                  .Append(',').Append(flags).Append(',').Append(Num(f.Width * scale)).Append(",[");
                for (var p = 0; p < pts.Length; p += 2)
                {
                    if (p > 0) sb.Append(',');
                    sb.Append(Num((pts[p] - ox) * scale)).Append(',').Append(Num((pts[p + 1] - oz) * scale));
                }
                sb.Append("]]");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Num(double v)
        {
            return Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Vereinfachung (Douglas-Peucker für Linien, Radialabstand für Flächen)
        // ------------------------------------------------------------------

        private static float[] Simplify(float[] pts, double tolerance, bool polygon)
        {
            var n = pts.Length / 2;
            if (n <= 2 || tolerance <= 0) return pts;

            if (polygon)
            {
                var result = new List<float> { pts[0], pts[1] };
                float lx = pts[0], lz = pts[1];
                var tol2 = tolerance * tolerance;
                for (var i = 1; i < n; i++)
                {
                    var dx = pts[i * 2] - lx;
                    var dz = pts[i * 2 + 1] - lz;
                    if (dx * dx + dz * dz < tol2) continue;
                    lx = pts[i * 2];
                    lz = pts[i * 2 + 1];
                    result.Add(lx);
                    result.Add(lz);
                }
                return result.Count >= 6 ? result.ToArray() : pts;
            }

            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<Tuple<int, int>>();
            stack.Push(Tuple.Create(0, n - 1));
            while (stack.Count > 0)
            {
                var seg = stack.Pop();
                int a = seg.Item1, b = seg.Item2;
                double maxDist = 0;
                int index = -1;
                for (var i = a + 1; i < b; i++)
                {
                    var d = PointLineDistance(pts, i, a, b);
                    if (d > maxDist)
                    {
                        maxDist = d;
                        index = i;
                    }
                }
                if (index >= 0 && maxDist > tolerance)
                {
                    keep[index] = true;
                    stack.Push(Tuple.Create(a, index));
                    stack.Push(Tuple.Create(index, b));
                }
            }

            var outPts = new List<float>();
            for (var i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                outPts.Add(pts[i * 2]);
                outPts.Add(pts[i * 2 + 1]);
            }
            return outPts.ToArray();
        }

        private static double PointLineDistance(float[] p, int i, int a, int b)
        {
            double x = p[i * 2], z = p[i * 2 + 1];
            double x1 = p[a * 2], z1 = p[a * 2 + 1], x2 = p[b * 2], z2 = p[b * 2 + 1];
            double dx = x2 - x1, dz = z2 - z1;
            var len2 = dx * dx + dz * dz;
            if (len2 == 0) return Math.Sqrt((x - x1) * (x - x1) + (z - z1) * (z - z1));
            var t = Math.Max(0, Math.Min(1, ((x - x1) * dx + (z - z1) * dz) / len2));
            double px = x1 + t * dx, pz = z1 + t * dz;
            return Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }

        // ------------------------------------------------------------------
        // Geometrie sammeln (entspricht TsMapRenderer.Render)
        // ------------------------------------------------------------------

        private void Add(byte layer, byte color, int zIndex, bool polygon, bool dashed, float width, IList<PointF> points)
        {
            if (points == null || points.Count < (polygon ? 3 : 2)) return;
            var arr = new float[points.Count * 2];
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y)) return;
                arr[i * 2] = p.X;
                arr[i * 2 + 1] = p.Y;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minZ) minZ = p.Y;
                if (p.Y > maxZ) maxZ = p.Y;
            }
            _features.Add(new Feature
            {
                Layer = layer, Color = color, ZIndex = zIndex, Polygon = polygon, Dashed = dashed, Width = width,
                Points = arr, MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ,
            });
        }

        private void CollectMapAreas(HashSet<byte> activeDlcGuards, bool includeSecret)
        {
            foreach (var mapArea in _mapper.MapAreas)
            {
                if (!activeDlcGuards.Contains(mapArea.DlcGuard) || mapArea.IsSecret && !includeSecret) continue;

                var points = new List<PointF>();
                foreach (var uid in mapArea.NodeUids)
                {
                    var node = _mapper.GetNodeByUid(uid);
                    if (node != null) points.Add(new PointF(node.X, node.Z));
                }

                byte color = 0;
                var zIndex = mapArea.DrawOver ? 10 : 0;
                if ((mapArea.ColorIndex & 0x03) == 3) { color = 3; zIndex = mapArea.DrawOver ? 13 : 3; }
                else if ((mapArea.ColorIndex & 0x02) == 2) { color = 2; zIndex = mapArea.DrawOver ? 12 : 2; }
                else if ((mapArea.ColorIndex & 0x01) == 1) { color = 1; zIndex = mapArea.DrawOver ? 11 : 1; }

                Add(0, color, zIndex, true, false, 0, points);
            }
        }

        private void CollectPrefabs(HashSet<byte> activeDlcGuards, bool includeSecret)
        {
            foreach (var prefabItem in _mapper.Prefabs)
            {
                if (!activeDlcGuards.Contains(prefabItem.DlcGuard) || prefabItem.IsSecret && !includeSecret) continue;
                if (prefabItem.Prefab?.PrefabNodes == null || prefabItem.Prefab.MapPoints == null) continue;
                if (prefabItem.Nodes == null || prefabItem.Nodes.Count == 0 || prefabItem.Origin >= prefabItem.Prefab.PrefabNodes.Count) continue;

                var originNode = _mapper.GetNodeByUid(prefabItem.Nodes[0]);
                if (originNode == null) continue;

                var mapPointOrigin = prefabItem.Prefab.PrefabNodes[prefabItem.Origin];
                var rot = (float)(originNode.Rotation - Math.PI - Math.Atan2(mapPointOrigin.RotZ, mapPointOrigin.RotX) + Math.PI / 2);
                var prefabStartX = originNode.X - mapPointOrigin.X;
                var prefabStartZ = originNode.Z - mapPointOrigin.Z;
                var mapPoints = prefabItem.Prefab.MapPoints;
                var pointsDrawn = new HashSet<int>();

                for (var i = 0; i < mapPoints.Count; i++)
                {
                    var mapPoint = mapPoints[i];
                    pointsDrawn.Add(i);

                    if (mapPoint.LaneCount == -1) // Fläche (kein Straßenstück)
                    {
                        var polyPoints = new Dictionary<int, PointF>();
                        var nextPoint = i;
                        do
                        {
                            if (mapPoints[nextPoint].Neighbours.Count == 0) break;
                            foreach (var neighbour in mapPoints[nextPoint].Neighbours)
                            {
                                if (!polyPoints.ContainsKey(neighbour))
                                {
                                    nextPoint = neighbour;
                                    var p = RenderHelper.RotatePoint(prefabStartX + mapPoints[nextPoint].X,
                                        prefabStartZ + mapPoints[nextPoint].Z, rot, originNode.X, originNode.Z);
                                    polyPoints.Add(nextPoint, p);
                                    break;
                                }
                                nextPoint = -1;
                            }
                        } while (nextPoint != -1);

                        if (polyPoints.Count < 2) continue;

                        var visualFlag = mapPoints[polyPoints.First().Key].PrefabColorFlags;
                        byte color = 1;
                        var roadOver = MemoryHelper.IsBitSet(visualFlag, 0);
                        var zIndex = roadOver ? 10 : 0;
                        if (MemoryHelper.IsBitSet(visualFlag, 1)) color = 1;
                        else if (MemoryHelper.IsBitSet(visualFlag, 2)) { color = 2; zIndex = roadOver ? 11 : 1; }
                        else if (MemoryHelper.IsBitSet(visualFlag, 3)) { color = 3; zIndex = roadOver ? 12 : 2; }

                        Add(1, color, zIndex, true, false, 0, polyPoints.Values.ToList());
                        continue;
                    }

                    var mapPointLaneCount = mapPoint.LaneCount;
                    if (mapPointLaneCount == -2 && i < prefabItem.Prefab.PrefabNodes.Count && mapPoint.ControlNodeIndex != -1)
                        mapPointLaneCount = prefabItem.Prefab.PrefabNodes[mapPoint.ControlNodeIndex].LaneCount;

                    foreach (var neighbourPointIndex in mapPoint.Neighbours)
                    {
                        if (pointsDrawn.Contains(neighbourPointIndex)) continue;
                        var neighbourPoint = mapPoints[neighbourPointIndex];

                        if ((mapPoint.Hidden || neighbourPoint.Hidden) && prefabItem.Prefab.PrefabNodes.Count + 1 < mapPoints.Count) continue;

                        var roadYaw = Math.Atan2(neighbourPoint.Z - mapPoint.Z, neighbourPoint.X - mapPoint.X);

                        var neighbourLaneCount = neighbourPoint.LaneCount;
                        if (neighbourLaneCount == -2 && neighbourPointIndex < prefabItem.Prefab.PrefabNodes.Count && neighbourPoint.ControlNodeIndex != -1)
                            neighbourLaneCount = prefabItem.Prefab.PrefabNodes[neighbourPoint.ControlNodeIndex].LaneCount;

                        var laneA = mapPointLaneCount;
                        var laneB = neighbourLaneCount;
                        if (laneA == -2 && laneB != -2) laneA = laneB;
                        else if (laneB == -2 && laneA != -2) laneB = laneA;
                        else if (laneA == -2 && laneB == -2) laneA = laneB = 1;

                        var corners = new List<PointF>();
                        var c = RenderHelper.GetCornerCoords(prefabStartX + mapPoint.X, prefabStartZ + mapPoint.Z,
                            (Consts.LaneWidth * laneA + mapPoint.LaneOffset) / 2f, roadYaw + Math.PI / 2);
                        corners.Add(RenderHelper.RotatePoint(c.X, c.Y, rot, originNode.X, originNode.Z));
                        c = RenderHelper.GetCornerCoords(prefabStartX + neighbourPoint.X, prefabStartZ + neighbourPoint.Z,
                            (Consts.LaneWidth * laneB + neighbourPoint.LaneOffset) / 2f, roadYaw + Math.PI / 2);
                        corners.Add(RenderHelper.RotatePoint(c.X, c.Y, rot, originNode.X, originNode.Z));
                        c = RenderHelper.GetCornerCoords(prefabStartX + neighbourPoint.X, prefabStartZ + neighbourPoint.Z,
                            (Consts.LaneWidth * laneB + mapPoint.LaneOffset) / 2f, roadYaw - Math.PI / 2);
                        corners.Add(RenderHelper.RotatePoint(c.X, c.Y, rot, originNode.X, originNode.Z));
                        c = RenderHelper.GetCornerCoords(prefabStartX + mapPoint.X, prefabStartZ + mapPoint.Z,
                            (Consts.LaneWidth * laneA + mapPoint.LaneOffset) / 2f, roadYaw - Math.PI / 2);
                        corners.Add(RenderHelper.RotatePoint(c.X, c.Y, rot, originNode.X, originNode.Z));

                        Add(1, 0, MemoryHelper.IsBitSet(mapPoint.PrefabColorFlags, 0) ? 13 : 3, true, false, 0, corners);
                    }
                }
            }
        }

        private void CollectRoads(HashSet<byte> activeDlcGuards, bool includeSecret)
        {
            foreach (var road in _mapper.Roads)
            {
                if (!activeDlcGuards.Contains(road.DlcGuard) || road.IsSecret && !includeSecret) continue;

                IList<PointF> points;
                if (road.HasPoints())
                {
                    points = road.GetPoints();
                }
                else
                {
                    var startNode = road.GetStartNode();
                    var endNode = road.GetEndNode();
                    if (startNode == null || endNode == null) continue;

                    float sx = startNode.X, sz = startNode.Z, ex = endNode.X, ez = endNode.Z;
                    var radius = Math.Sqrt(Math.Pow(sx - ex, 2) + Math.Pow(sz - ez, 2));
                    var tanSx = Math.Cos(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                    var tanEx = Math.Cos(-(Math.PI * 0.5f - endNode.Rotation)) * radius;
                    var tanSz = Math.Sin(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                    var tanEz = Math.Sin(-(Math.PI * 0.5f - endNode.Rotation)) * radius;

                    var list = new List<PointF>();
                    for (var i = 0; i < 8; i++)
                    {
                        var s = i / (float)(8 - 1);
                        list.Add(new PointF((float)TsRoadLook.Hermite(s, sx, ex, tanSx, tanEx),
                            (float)TsRoadLook.Hermite(s, sz, ez, tanSz, tanEz)));
                    }
                    points = list;
                }

                Add(2, 0, 0, false, road.IsSecret, road.RoadLook?.GetWidth() ?? Consts.LaneWidth * 2, points);
            }
        }

        private void CollectFerries()
        {
            var done = new HashSet<string>();
            foreach (var ferryConnection in _mapper.FerryConnections)
            {
                foreach (var conn in _mapper.LookupFerryConnection(ferryConnection.FerryPortId))
                {
                    // Jede Verbindung nur einmal (Hin- und Rückrichtung)
                    var a = conn.StartPortToken;
                    var b = conn.EndPortToken;
                    var key = a < b ? a + "-" + b : b + "-" + a;
                    if (!done.Add(key)) continue;

                    Add(3, 4, 0, false, true, 50, FerryPoints(conn));
                }
            }
        }

        private static List<PointF> FerryPoints(TsFerryConnection conn)
        {
            if (conn.Connections.Count == 0)
                return new List<PointF> { conn.StartPortLocation, conn.EndPortLocation };

            // Gleiche Bezier-Kontrollpunkte wie TsMapRenderer, dann abgetastet
            var startYaw = Math.Atan2(conn.Connections[0].Z - conn.StartPortLocation.Y, conn.Connections[0].X - conn.StartPortLocation.X);
            var bez = RenderHelper.GetBezierControlNodes(conn.StartPortLocation.X, conn.StartPortLocation.Y, startYaw,
                conn.Connections[0].X, conn.Connections[0].Z, conn.Connections[0].Rotation);
            var ctrl = new List<PointF>
            {
                conn.StartPortLocation,
                new PointF(conn.StartPortLocation.X + bez.Item1.X, conn.StartPortLocation.Y + bez.Item1.Y),
                new PointF(conn.Connections[0].X - bez.Item2.X, conn.Connections[0].Z - bez.Item2.Y),
                new PointF(conn.Connections[0].X, conn.Connections[0].Z),
            };

            for (var i = 0; i < conn.Connections.Count - 1; i++)
            {
                var p = conn.Connections[i];
                var n = conn.Connections[i + 1];
                bez = RenderHelper.GetBezierControlNodes(p.X, p.Z, p.Rotation, n.X, n.Z, n.Rotation);
                ctrl.Add(new PointF(p.X + bez.Item1.X, p.Z + bez.Item1.Y));
                ctrl.Add(new PointF(n.X - bez.Item2.X, n.Z - bez.Item2.Y));
                ctrl.Add(new PointF(n.X, n.Z));
            }

            var last = conn.Connections[conn.Connections.Count - 1];
            var endYaw = Math.Atan2(conn.EndPortLocation.Y - last.Z, conn.EndPortLocation.X - last.X);
            bez = RenderHelper.GetBezierControlNodes(last.X, last.Z, last.Rotation, conn.EndPortLocation.X, conn.EndPortLocation.Y, endYaw);
            ctrl.Add(new PointF(last.X + bez.Item1.X, last.Z + bez.Item1.Y));
            ctrl.Add(new PointF(conn.EndPortLocation.X - bez.Item2.X, conn.EndPortLocation.Y - bez.Item2.Y));
            ctrl.Add(conn.EndPortLocation);

            var result = new List<PointF> { ctrl[0] };
            for (var s = 0; s + 3 < ctrl.Count; s += 3)
            {
                for (var k = 1; k <= 16; k++)
                {
                    var t = k / 16f;
                    var u = 1 - t;
                    var x = u * u * u * ctrl[s].X + 3 * u * u * t * ctrl[s + 1].X + 3 * u * t * t * ctrl[s + 2].X + t * t * t * ctrl[s + 3].X;
                    var z = u * u * u * ctrl[s].Y + 3 * u * u * t * ctrl[s + 1].Y + 3 * u * t * t * ctrl[s + 2].Y + t * t * t * ctrl[s + 3].Y;
                    result.Add(new PointF(x, z));
                }
            }
            return result;
        }
    }
}
