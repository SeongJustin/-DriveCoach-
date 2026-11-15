// CityBuilder 의 거리 소품(가로수, 가로등, 신호등 등) 부분(11/15 코드 정리로 CityBuilder.cs 에서 나눈 파일). 전체 설명은 CityBuilder.cs 맨 위.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static partial class CityBuilder
    {
        // ───────────────────────── 거리 ─────────────────────────

        static void BuildStreet(RoadDef r, float h, Transform node)
        {
            if (Current != null && Current.props.Count > 0) { BuildSurfaceProps(Current, h, node); return; }
            // 가로수: 큰길 보도 가장자리에 14 m 마다(양쪽), 교차로·건물과 겹치면 뺀다. 줄기 + 잎(팔면체) 두 메시.
            var trunks = new MeshKit();
            var leaves = new MeshKit();
            var rnd = new System.Random(5);
            int trees = 0;
            var lightsAt = new List<(V2 p, float yaw)>();
            foreach (var s in r.strips)
            {
                if (!s.sidewalk) continue;
                bool major = Major(s.hw);
                float spacing = major ? 14f : 0f;
                float lightSpacing = major ? 36f : 60f;
                float acc = 0f, nextTree = 6f, nextLight = 10f;
                int side = 0;
                for (int i = 0; i + 1 < s.pts.Length; i++)
                {
                    var a = s.pts[i]; var b = s.pts[i + 1];
                    float len = a.DistanceTo(b);
                    if (len < 0.1f) continue;
                    var d = (b - a) / len;
                    var right = new V2(-d.y, d.x);
                    while (spacing > 0f && nextTree < acc + len && trees < 1800)
                    {
                        var p = a + d * (nextTree - acc);
                        foreach (var sx in new[] { -1f, 1f })
                        {
                            var tp = p + right * sx * (s.width / 2f + SidewalkW - 0.6f);
                            if (NearBuilding(r, tp, 1.5f)) continue;
                            Tree(trunks, leaves, tp, h, rnd);
                            trees++;
                        }
                        nextTree += spacing;
                    }
                    while (nextLight < acc + len && lightsAt.Count < 260)
                    {
                        var p = a + d * (nextLight - acc);
                        float sx = side++ % 2 == 0 ? 1f : -1f;
                        var lp = p + right * sx * (s.width / 2f + 0.5f);
                        if (!NearBuilding(r, lp, 1.0f))
                        {
                            var face = right * -sx;   // 길 쪽을 보게
                            lightsAt.Add((lp, Space3D.Yaw((float)Math.Atan2(face.y, face.x))));
                        }
                        nextLight += lightSpacing;
                    }
                    acc += len;
                }
            }
            if (!trunks.IsEmpty) trunks.ToObject("tree-trunks", DcAssets.Lit(DcAssets.Hex("#6B4F3A"), 0.1f), node, true);
            if (!leaves.IsEmpty) leaves.ToObject("tree-leaves", DcAssets.Lit(DcAssets.Hex("#4E7A3A"), 0.05f), node, true);
            BuildProps(r, h, node);
            var proto = DcAssets.Model("Quaternius/ModularStreet/Streetlight_Single");
            if (proto != null)
            {
                var ln = new GameObject("street-lights").transform;
                ln.SetParent(node, false);
                foreach (var (p, yaw) in lightsAt)
                {
                    var go = UnityEngine.Object.Instantiate(proto, ln);
                    go.transform.position = Space3D.W(p, h);
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    StageBuilder.FitHeight(go, 7.5f);
                }
            }
        }

        /// <summary>
        /// 실제 노면의 소품(노면 파일을 만들 때 하나씩 놓고 아스팔트·건물·횡단보도·서로 겹침을 검사한 자리) 그대로.
        /// 보도 위 소품은 보도 높이(WalkH)에, 맨홀은 아스팔트 위에, 공원 나무·벤치는 땅에.
        /// 혹시 아스팔트에 닿는 것(데이터가 어긋난 경우)은 놓지 않는다(맨홀 빼고).
        /// </summary>
        static void BuildSurfaceProps(RoadSurface s, float h, Transform node)
        {
            var trunks = new MeshKit();
            var leaves = new MeshKit();
            var rnd = new System.Random(5);
            var kit = PropKit.Has("utility_box_01") ? new PropKit() : null;
            var trees = TreeKit.Available ? new TreeKit() : null;
            var lights = new List<(V2 p, float y, float yaw, float size)>();
            int skipped = 0;
            foreach (var (kind, p, ang, size) in s.props)
            {
                float yaw = Space3D.Yaw(ang * Mathf.Deg2Rad);
                bool onWalk = s.Sidewalk(p);
                float y = onWalk ? h + WalkH : h;
                if (kind != "water_manhole_cover" && s.TouchesAsphalt(p, kind == "tree" || kind == "park_tree" ? 0.3f : 0.15f)) { skipped++; continue; }
                if (kind == "tree" || kind == "park_tree") Solids.Add((p, 0.18f * Mathf.Max(0.8f, size), "tree"));
                else if (kind == "light") Solids.Add((p, 0.12f, "lamp"));
                else if (kind != "water_manhole_cover" && kit != null) Solids.Add((p, (kind.StartsWith("utility_box") ? 0.4f : (kind == "metal_trash_can" ? 0.25f : 0.4f)) * Mathf.Max(0.8f, size), "prop"));
                switch (kind)
                {
                    case "tree":
                    case "park_tree":
                        // 11/9: EZ-Tree 나무(외부 모델, LOD) — 없으면 예전 코드 나무
                        int tseed = Mathf.FloorToInt(p.x * 7.13f) * 73856093 ^ Mathf.FloorToInt(p.y * 5.31f) * 19349663;
                        if (trees == null || !trees.Add(p, y, TreeKit.BaseHeight * size, ang, tseed, kind == "tree"))
                            Tree(trunks, leaves, p, y, rnd, size);
                        // 공원 나무 셋 중 하나 옆에 관목(EZ-Tree bush, 높이 1.2~1.8 m) — 보도·아스팔트가 아닌 땅에만
                        else if (kind == "park_tree" && ((tseed >> 3) & 3) == 0)
                        {
                            float ba = ((tseed >> 6) & 255) / 255f * Mathf.PI * 2f;
                            var bp = p + new V2(Mathf.Cos(ba), Mathf.Sin(ba)) * 2.6f;
                            if (!s.Sidewalk(bp) && !s.TouchesAsphalt(bp, 1.2f))
                                trees.AddBush(bp, h, 1.2f + ((tseed >> 14) & 7) * 0.09f, ang * 1.7f, tseed);
                        }
                        break;
                    case "light":
                        lights.Add((p, y, yaw, size));
                        break;
                    case "water_manhole_cover":
                        // (11/11 대표) 맨홀은 전부 뺀다
                        if (Manholes && s.Asphalt(p)) kit?.Add(kind, Space3D.W(p, h + 0.034f), yaw, size, true);
                        break;
                    default:
                        kit?.Add(kind, Space3D.W(p, y + 0.005f), yaw, size);
                        break;
                }
            }
            Dc.Lap("props-loop");
            if (skipped > 0) Debug.Log("DC3D surface props skipped (asphalt): " + skipped);
            if (trees != null) { trees.Emit(node); Debug.Log("DC3D trees (EZ-Tree): " + trees.count); }
            Dc.Lap("props-trees-emit");
            if (!trunks.IsEmpty) trunks.ToObject("tree-trunks", DcAssets.Lit(DcAssets.Hex("#6B4F3A"), 0.1f), node, true);
            if (!leaves.IsEmpty) leaves.ToObject("tree-leaves", DcAssets.Lit(DcAssets.Hex("#4E7A3A"), 0.05f), node, true);
            // 밤 불빛(11/8 밤): 가로등 아래 노면에 번지는 따뜻한 빛 웅덩이(반지름 7 m) — 밤에만 켠다(NightLights)
            if (lights.Count > 0)
            {
                var pools = new MeshKit();
                foreach (var (p, y, yaw, size) in lights)
                    NightLights.Pool(pools, Space3D.W(p, h + 0.035f), 8.5f, 8.5f, 0f);
                NightLights.Add(pools.ToObject("night-light-pools", NightLights.Glow(new Color(1f, 0.84f, 0.58f, 0.42f)), node, false, false));
            }
            // 가로등: 수백 개라 합쳐 그린다(모델을 읽을 수 있을 때). 못 합치면 하나씩
            const string LightId = "Quaternius/ModularStreet/Streetlight_Single";
            if (kit != null && PropKit.Has(LightId))
            {
                foreach (var (p, y, yaw, size) in lights) kit.Add(LightId, Space3D.W(p, y), yaw, size);
                lights.Clear();
            }
            kit?.Emit(node);
            var proto = DcAssets.Model(LightId);
            if (proto != null && lights.Count > 0)
            {
                var ln = new GameObject("street-lights").transform;
                ln.SetParent(node, false);
                foreach (var (p, y, yaw, size) in lights)
                {
                    var go = UnityEngine.Object.Instantiate(proto, ln);
                    go.transform.position = Space3D.W(p, y);
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    StageBuilder.FitHeight(go, size);
                }
            }
        }

        /// <summary>
        /// 11/8 "유로트럭 느낌": 보도의 변압기함·통신함(건물 쪽), 쓰레기통·화분(큰길 연석 쪽), 차도의 맨홀(차로 가운데),
        /// 공원 가장자리 벤치. Poly Haven(CC0) 모델을 칸마다 한 메시로.
        /// </summary>
        static void BuildProps(RoadDef r, float h, Transform node)
        {
            if (!PropKit.Has("utility_box_01")) return;
            var kit = new PropKit();
            var rnd = new System.Random(31);
            int count = 0;
            foreach (var s in r.strips)
            {
                bool major = Major(s.hw);
                float acc = 0f;
                float nextBox = 20f + (float)rnd.NextDouble() * 40f, nextBin = 30f + (float)rnd.NextDouble() * 60f, nextHole = 10f + (float)rnd.NextDouble() * 20f;
                for (int i = 0; i + 1 < s.pts.Length && count < 2500; i++)
                {
                    var a = s.pts[i]; var b = s.pts[i + 1];
                    float len = a.DistanceTo(b);
                    if (len < 0.1f) continue;
                    var d = (b - a) / len;
                    var right = new V2(-d.y, d.x);
                    float along = Space3D.Yaw((float)Math.Atan2(d.y, d.x));
                    // 맨홀: 차로 가운데쯤(35~70 m 마다)
                    while (nextHole < acc + len)
                    {
                        var p = a + d * (nextHole - acc);
                        float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                        var mp = p + right * side * Math.Min(s.width / 2f - 1f, s.laneWidth * 0.5f) * (float)rnd.NextDouble();
                        float holeYaw = (float)rnd.NextDouble() * 360f;      // 난수 순서는 그대로 둔다(다른 소품 자리가 바뀌지 않게)
                        if (Manholes) kit.Add("water_manhole_cover", Space3D.W(mp, h + 0.034f), holeYaw, 0.85f, true);
                        count++;
                        nextHole += 35f + (float)rnd.NextDouble() * 35f;
                    }
                    if (s.sidewalk)
                    {
                        // 변압기함·통신함: 보도 바깥(건물 쪽), 60~140 m 마다
                        while (nextBox < acc + len)
                        {
                            var p = a + d * (nextBox - acc);
                            float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                            var bp = p + right * side * (s.width / 2f + SidewalkW - 0.55f);
                            if (!NearBuilding(r, bp, 0.6f))
                            {
                                string id = rnd.NextDouble() < 0.5 ? "utility_box_01" : "utility_box_02";
                                kit.Add(id, Space3D.W(bp, h + 0.012f), along + (side > 0 ? 90f : -90f), 1.25f + (float)rnd.NextDouble() * 0.3f);
                                count++;
                            }
                            nextBox += 60f + (float)rnd.NextDouble() * 80f;
                        }
                        // 큰길: 쓰레기통·화분(연석 가까이), 50~120 m 마다
                        while (major && nextBin < acc + len)
                        {
                            var p = a + d * (nextBin - acc);
                            float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                            var bp = p + right * side * (s.width / 2f + 0.75f);
                            if (!NearBuilding(r, bp, 0.6f))
                            {
                                if (rnd.NextDouble() < 0.6) kit.Add("metal_trash_can", Space3D.W(bp, h + 0.012f), (float)rnd.NextDouble() * 360f, 0.95f);
                                else kit.Add("planter_box_01", Space3D.W(bp, h + 0.012f), along, 0.75f);
                                count++;
                            }
                            nextBin += 50f + (float)rnd.NextDouble() * 70f;
                        }
                    }
                    acc += len;
                }
            }
            // 공원 가장자리 벤치(공원마다 최대 3개)
            if (r.region != null)
            {
                foreach (var a in r.region.areas)
                {
                    if (a.kind != "park" || a.poly.Length < 3) continue;
                    for (int k = 0; k < 3 && count < 2600; k++)
                    {
                        int i = rnd.Next(a.poly.Length);
                        var p0 = a.poly[i]; var p1 = a.poly[(i + 1) % a.poly.Length];
                        if (p0.DistanceTo(p1) < 4f) continue;
                        var mid = (p0 + p1) / 2f;
                        var dd = (p1 - p0).Normalized;
                        var inward = new V2(-dd.y, dd.x);
                        var bp = mid + inward * 1.2f;
                        if (!Geom.PointInPolygon(bp, a.poly)) { inward = inward * -1f; bp = mid + inward * 1.2f; }
                        if (!Geom.PointInPolygon(bp, a.poly) || NearBuilding(r, bp, 1f)) continue;
                        kit.Add("painted_wooden_bench", Space3D.W(bp, h + 0.01f), Space3D.Yaw((float)Math.Atan2(inward.y, inward.x)) + 180f, 0.85f);
                        count++;
                    }
                }
            }
            kit.Emit(node);
        }

        static Dictionary<long, List<RegionBuilding>> bgrid;
        static RoadDef bgridOf;

        /// <summary>건물과 겹치거나 margin 안인가(50 m 칸 격자로 가까운 건물만 본다)</summary>
        static bool NearBuilding(RoadDef r, V2 p, float margin)
        {
            const float G = 50f;
            if (bgridOf != r)
            {
                bgridOf = r;
                bgrid = new Dictionary<long, List<RegionBuilding>>();
                foreach (var b in r.buildings)
                {
                    float rad = 0f;
                    foreach (var q in b.poly) rad = Math.Max(rad, q.DistanceTo(b.center));
                    int x0 = (int)Math.Floor((b.center.x - rad) / G), x1 = (int)Math.Floor((b.center.x + rad) / G);
                    int y0 = (int)Math.Floor((b.center.y - rad) / G), y1 = (int)Math.Floor((b.center.y + rad) / G);
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                        {
                            long k = ((long)gx << 32) ^ (uint)gy;
                            if (!bgrid.TryGetValue(k, out var l)) bgrid[k] = l = new List<RegionBuilding>();
                            l.Add(b);
                        }
                }
            }
            long key = ((long)(int)Math.Floor(p.x / G) << 32) ^ (uint)(int)Math.Floor(p.y / G);
            if (!bgrid.TryGetValue(key, out var list)) return false;
            foreach (var b in list)
                if (Geom.PointInPolygon(p, b.poly) || Geom.DistPointPoly(p, b.poly, out _) < margin) return true;
            return false;
        }

        static void Tree(MeshKit trunks, MeshKit leaves, V2 p, float h, System.Random rnd, float scale = -1f)
        {
            float s = scale > 0f ? scale : 0.85f + (float)rnd.NextDouble() * 0.4f;
            trunks.Box(Space3D.W(p, h + 1.4f * s), new Vector3(0.22f, 2.8f * s, 0.22f), (float)rnd.NextDouble() * 90f);
            // 잎: 납작한 팔면체 두 겹(낮은 다각형 나무)
            var c = Space3D.W(p, h + 3.6f * s);
            float r = 1.5f * s, hh = 1.6f * s;
            for (int layer = 0; layer < 2; layer++)
            {
                var cc = c + Vector3.up * (layer * 0.9f * s);
                float rr = r * (layer == 0 ? 1f : 0.7f);
                float yaw = (float)rnd.NextDouble() * Mathf.PI;
                var ring = new Vector3[6];
                for (int i = 0; i < 6; i++) ring[i] = cc + new Vector3(Mathf.Cos(yaw + i * Mathf.PI / 3f) * rr, 0f, Mathf.Sin(yaw + i * Mathf.PI / 3f) * rr);
                var topP = cc + Vector3.up * hh * 0.6f;
                var botP = cc - Vector3.up * hh * 0.35f;
                for (int i = 0; i < 6; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % 6];
                    var mid = (a + b + topP) / 3f - cc;
                    leaves.Quad(a, b, topP, topP, mid.normalized, Vector2.zero, Vector2.right, Vector2.up, Vector2.up);
                    var mid2 = (a + b + botP) / 3f - cc;
                    leaves.Quad(a, b, botP, botP, mid2.normalized, Vector2.zero, Vector2.right, Vector2.up, Vector2.up);
                }
            }
        }
    }
}
