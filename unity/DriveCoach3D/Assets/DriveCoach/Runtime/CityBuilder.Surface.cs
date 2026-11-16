// CityBuilder 의 노면(실제 노면, 길 띠, 보도, 교차로) 부분(CityBuilder.cs 에서 나눈 파일). 전체 설명은 CityBuilder.cs 맨 위.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static partial class CityBuilder
    {
        // ───────────────────────── 실제 노면(서울 T-GIS) ─────────────────────────

        /// <summary>맨홀 뚜껑을 놓을지. 끔.</summary>
        const bool Manholes = false;
        const float CurbH = 0.15f;
        public const float WalkH = 0.14f;

        /// <summary>
        /// 노면 파일(<지역>_surface.bytes)을 그대로: 아스팔트(큰길·동네길), 보도(연석 높이로 올림 + 바깥 옆면), 연석(윗면 + 길 쪽 면),
        /// 실제 칠(흰·노랑·파랑, 색마다 높이를 조금 달리해 겹친 곳이 깜빡이지 않게). 160 m 칸으로 나눠 먼 칸은 그리지 않는다.
        /// </summary>
        static void BuildRealSurface(RoadSurface s, float h, Transform node, List<(V2 c, V2 n, float L, float W)> xw)
        {
            var tiles = new Tiles();
            int nGrate = 0;
            bool NearCrosswalk(V2 p)
            {
                foreach (var (c, n, L, W) in xw)
                {
                    var q = p - c;
                    if (Math.Abs(q.x * n.x + q.y * n.y) < L / 2f + 2.5f && Math.Abs(-q.x * n.y + q.y * n.x) < W / 2f + 3f) return true;
                }
                return false;
            }
            void Add(string tag, string mat, float y, float uv)
            {
                if (!s.tris.TryGetValue(tag, out var v)) return;
                for (int i = 0; i + 2 < v.Length; i += 3)
                {
                    var c = new V2((v[i].x + v[i + 1].x + v[i + 2].x) / 3f, (v[i].y + v[i + 1].y + v[i + 2].y) / 3f);
                    var mk = tiles.Get(mat, c, uv);
                    mk.weld = true;   // 이웃 삼각형과 같은 자리 점은 하나로(모양 같음, 점 수 절반 아래)
                    mk.TriUp(Space3D.W(new V2(v[i].x, v[i].y), y), Space3D.W(new V2(v[i + 1].x, v[i + 1].y), y), Space3D.W(new V2(v[i + 2].x, v[i + 2].y), y));
                }
            }
            // 재질마다 다 모으면 바로 메시로 내보내고 점 목록을 치운다(예전: 여섯 가지를 다 모은 뒤 한꺼번에 → 스크립트 힙 최고치)
            void AddEmit(string tag, string mat, float y, float uv) { Add(tag, mat, y, uv); tiles.Emit(node, SurfaceMat, k => false, false, mat); Dc.Trim(); }
            AddEmit("AMAJ", "asphalt-major", h + 0.03f, 5f);
            AddEmit("AMIN", "asphalt-minor", h + 0.03f, 4f);
            AddEmit("SIDE", "sidewalk", h + WalkH, 1.6f);
            AddEmit("PNTW", "paint-w", h + 0.043f, 4f);
            AddEmit("PNTY", "paint-y", h + 0.046f, 4f);
            AddEmit("PNTB", "paint-b", h + 0.049f, 4f);
            Dc.Lap("rs-tris");
            // 연석(화강석 경계석 흉내): 선의 (−dy, dx) 쪽이 길. 윗면 0.18 m(보도보다 1 cm 높게) + 길 쪽 모서리 2.5 cm 모따기 + 길 쪽 면
            foreach (var line in s.curbs)
            {
                if (line.Length < 2) continue;
                var mk = tiles.Get("curb", line[line.Length / 2], 2f);
                float along = 0f;
                float nextGrate = 9f;   // 빗물받이: 연석을 따라 25 m 마다(도로 배수시설 지침 20~30 m), 첫 자리는 9 m
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    var a = line[i]; var b = line[i + 1];
                    float len = a.DistanceTo(b);
                    if (len < 0.02f) continue;
                    var d = (b - a) / len;
                    var road = new V2(-d.y, d.x);
                    var wa = Space3D.W(a, 0f); var wb = Space3D.W(b, 0f);
                    var oa = Space3D.W(a - road * 0.18f, 0f); var ob = Space3D.W(b - road * 0.18f, 0f);
                    var ca = Space3D.W(a - road * 0.025f, 0f); var cb = Space3D.W(b - road * 0.025f, 0f);
                    var roadDir = (Space3D.W(road, 0f) - Space3D.W(V2.Zero, 0f)).normalized;
                    // 질감 좌표를 1 m = 1 로: 연석 질감(CityTextures.Curb)이 1 m 마다 이음매가 있는 밝은 화강석 경계석
                    float u0 = along, u1 = along + len;
                    const float Ch = 0.025f;
                    mk.Quad(Lift(ca, h + CurbH), Lift(cb, h + CurbH), Lift(ob, h + CurbH), Lift(oa, h + CurbH), Vector3.up,
                        new Vector2(u0, 0.03f), new Vector2(u1, 0.03f), new Vector2(u1, 0.185f), new Vector2(u0, 0.185f));
                    mk.Quad(Lift(wa, h + CurbH - Ch), Lift(wb, h + CurbH - Ch), Lift(cb, h + CurbH), Lift(ca, h + CurbH), (Vector3.up + roadDir).normalized,
                        new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.03f), new Vector2(u0, 0.03f));
                    mk.Quad(Lift(wa, h + 0.02f), Lift(wb, h + 0.02f), Lift(wb, h + CurbH - Ch), Lift(wa, h + CurbH - Ch), roadDir,
                        new Vector2(u0, 0.3f), new Vector2(u1, 0.3f), new Vector2(u1, 0.42f), new Vector2(u0, 0.42f));
                    // 빗물받이 덮개(0.4 × 0.9 m, 연석 바로 옆 아스팔트, 칠보다 아래): 한 연석 토막 안(양 끝 0.5 m 빼고)·횡단보도 밖·아래가 아스팔트일 때
                    while (nextGrate <= along + len - 0.5f)
                    {
                        float k = Math.Max(nextGrate - along, 0.5f);
                        var gp = a + d * k;
                        var g0 = gp + road * 0.02f; var g1 = gp + road * 0.42f;
                        if (s.Asphalt(g0 + d * 0.45f) && s.Asphalt(g0 - d * 0.45f) && s.Asphalt(g1 + d * 0.45f) && s.Asphalt(g1 - d * 0.45f) && !NearCrosswalk(gp))
                        {
                            var gk = tiles.Get("grate", gp, 1f);
                            float gy = h + 0.036f;
                            gk.Quad(Space3D.W(g0 - d * 0.45f, gy), Space3D.W(g0 + d * 0.45f, gy), Space3D.W(g1 + d * 0.45f, gy), Space3D.W(g1 - d * 0.45f, gy), Vector3.up,
                                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                            nGrate++;
                            nextGrate = along + k + 25f;
                        }
                        else nextGrate = along + k + 3f;   // 안 맞으면 3 m 뒤에서 다시
                    }
                    along += len;
                }
            }
            Dc.Lap("rs-curbs");
            // 보도 바깥 옆면(공원·빈 땅 쪽으로 떠 보이지 않게): 선의 (−dy, dx) 쪽이 보도
            if (s.tris.ContainsKey("SIDE"))
                foreach (var line in s.walkEdges)
                {
                    if (line.Length < 2) continue;
                    var mk = tiles.Get("curb", line[line.Length / 2], 2f);
                    for (int i = 0; i + 1 < line.Length; i++)
                    {
                        var a = line[i]; var b = line[i + 1];
                        float len = a.DistanceTo(b);
                        if (len < 0.02f) continue;
                        var d = (b - a) / len;
                        var walk = new V2(-d.y, d.x);
                        var outDir = (Space3D.W(-walk, 0f) - Space3D.W(V2.Zero, 0f)).normalized;
                        var wa = Space3D.W(a, 0f); var wb = Space3D.W(b, 0f);
                        mk.Quad(Lift(wa, h), Lift(wb, h), Lift(wb, h + WalkH), Lift(wa, h + WalkH), outDir,
                            new Vector2(0f, 0.3f), new Vector2(len, 0.3f), new Vector2(len, 0.44f), new Vector2(0f, 0.44f));
                    }
                }
            Dc.Lap("rs-edges");
            tiles.Emit(node, SurfaceMat, k => false);
            Dc.Lap("rs-emit");
            Debug.Log($"노면: 빗물받이 {nGrate}");
        }

        static Material SurfaceMat(string key)
        {
            switch (key)
            {
                case "asphalt-major": return DcAssets.Pbr("clean_asphalt", DcAssets.Hex("#4C5056"), new Color(1.05f, 1.05f, 1.08f), 0.9f);
                // (바닥 검사) 골목 질감(asphalt_02)은 긴 금이 대각선으로 되풀이돼 줄무늬처럼 보였다 → 큰길과 같은 질감에 조금 어둡게
                case "asphalt-minor": return DcAssets.Pbr("clean_asphalt", DcAssets.Hex("#4C5056"), new Color(0.96f, 0.96f, 0.99f), 0.9f);   // 큰길과 너무 다르면 교차로에 밝은 조각처럼 보인다(2차 검토)
                case "sidewalk": return DcAssets.Pbr("square_concrete_pavers", DcAssets.Hex("#CFCBC2"), new Color(1f, 0.98f, 0.96f), 0.8f);
                case "curb": return DcAssets.LitTex("curb-granite", CityTextures.Curb(), 0.22f);   // 밝은 화강석 경계석 + 1 m 이음매(예전: 어두운 콘크리트 질감이 길게 늘어져 얼룩 띠처럼 보였다)
                case "grate": return DcAssets.Pbr("storm_grate", DcAssets.Hex("#3A3C3E"), Color.white, 0.7f);
                case "paint-w": return DcAssets.Lit(DcAssets.Hex("#E6E6E1"), 0.4f);
                case "paint-y": return DcAssets.Lit(DcAssets.Hex("#DDA92B"), 0.4f);
                case "paint-b": return DcAssets.Lit(DcAssets.Hex("#2C5FC2"), 0.4f);
            }
            return null;
        }

        // ───────────────────────── 노면 ─────────────────────────

        /// <summary>교차로 원 반지름: 만나는 길 중 가장 넓은 폭의 절반 + 1 m</summary>
        static Dictionary<int, (V2 p, float r)> JunctionRadii(RoadDef r)
        {
            var res = new Dictionary<int, (V2, float)>();
            var rg = r.region;
            if (rg == null) return res;
            foreach (var road in rg.roads)
            {
                if (road.tunnel) continue;
                float w = RoadWidth(road);
                for (int i = 0; i < road.pts.Length; i++)
                {
                    int j = road.jn[i];
                    if (j < 0) continue;
                    float rad = w / 2f + 1f;
                    if (!res.TryGetValue(j, out var cur) || cur.Item2 < rad) res[j] = (road.pts[i], rad);
                }
            }
            // 길이 하나만 지나는 점(곡선 중간)은 원이 필요 없다: 두 길 이상 만나는 점만 남긴다
            var count = new Dictionary<int, int>();
            foreach (var road in rg.roads) foreach (var j in road.jn) if (j >= 0) count[j] = count.TryGetValue(j, out var c) ? c + 1 : 1;
            var keep = new Dictionary<int, (V2 p, float r)>();
            foreach (var kv in res) if (count.TryGetValue(kv.Key, out var c) && c >= 2) keep[kv.Key] = kv.Value;
            return keep;
        }

        static float RoadWidth(RegionRoad road)
        {
            if (road.width > 0f && road.lanes == 1 && !road.oneway) return road.width;
            float lw = Stages.LaneWidth(road.hw);
            int n = Math.Max(1, road.lanes);
            if (road.oneway) return n * lw;
            if (n == 1) return 2f * Math.Min(lw, 2.75f);
            return n * lw + (n >= 4 ? 0.5f : 0f);
        }

        /// <summary>공원·숲(초록)·물(파랑) 바닥. 보도·길보다 낮게 깐다. 숲·공원에는 나무를 드문드문.</summary>
        static void BuildAreas(RoadDef r, float h, Transform node)
        {
            var rg = r.region;
            if (rg == null || rg.areas.Count == 0) return;
            var park = new MeshKit { uvScale = 4f };
            var forest = new MeshKit { uvScale = 4f };
            var water = new MeshKit();
            var trunks = new MeshKit();
            var leaves = new MeshKit();
            var rnd = new System.Random(21);
            int trees = 0;
            var tk = TreeKit.Available ? new TreeKit() : null;
            var bank = new MeshKit { uvScale = 3f };
            foreach (var a in rg.areas)
            {
                var mk = a.kind == "water" ? water : (a.kind == "forest" ? forest : park);
                // 하천(양재천 등)은 땅을 판 둑 아래 낮은 물 — 노면 파일의 땅 구멍과 짝. 둘레에 돌 옹벽
                if (a.kind == "water" && rg.waterCut > 0f && Math.Abs(PolyArea(a.poly)) > 300f)
                {
                    float depth = rg.waterCut;
                    mk.PolygonEar(a.poly, h - depth + 0.4f);
                    bool ccw = PolyArea(a.poly) > 0f;
                    for (int i = 0; i < a.poly.Length; i++)
                    {
                        var p0 = a.poly[i]; var p1 = a.poly[(i + 1) % a.poly.Length];
                        float len = p0.DistanceTo(p1);
                        if (len < 0.05f) continue;
                        var w0 = Space3D.W(p0, 0f); var w1 = Space3D.W(p1, 0f);
                        // 물 쪽(안쪽)을 바라보는 면
                        var inward = Vector3.Cross(Vector3.up, w1 - w0).normalized * (ccw ? 1f : -1f);
                        bank.Quad(Lift(w0, h - depth), Lift(w1, h - depth), Lift(w1, h + 0.01f), Lift(w0, h + 0.01f), inward,
                            new Vector2(0f, 0f), new Vector2(len / 3f, 0f), new Vector2(len / 3f, depth / 3f), new Vector2(0f, depth / 3f));
                    }
                    continue;
                }
                mk.PolygonEar(a.poly, h + (a.kind == "water" ? 0.004f : 0.006f));
                // 실제 노면이 있으면 공원 나무는 노면 파일에 하나씩 검사해 넣어 둔 자리(소품)를 쓴다
                if (a.kind == "water" || (Current != null && Current.props.Count > 0)) continue;
                // 나무: 넓이 600 m² 마다 하나(숲은 250), 길 가까운 쪽 위주로 최대 1500그루
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in a.poly) { x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); }
                float areaM2 = (x1 - x0) * (y1 - y0) * 0.6f;
                int want = (int)Math.Min(400, areaM2 / (a.kind == "forest" ? 250f : 600f));
                for (int k = 0; k < want * 3 && want > 0 && trees < 1500; k++)
                {
                    var p = new V2(x0 + (float)rnd.NextDouble() * (x1 - x0), y0 + (float)rnd.NextDouble() * (y1 - y0));
                    if (!Geom.PointInPolygon(p, a.poly) || NearBuilding(r, p, 2f)) continue;
                    if (tk == null || !tk.Add(p, h, TreeKit.BaseHeight * (0.85f + (float)rnd.NextDouble() * 0.4f), (float)rnd.NextDouble() * 360f, rnd.Next(), false))
                        Tree(trunks, leaves, p, h, rnd);
                    trees++;
                    if (--want <= 0) break;
                }
            }
            // 큰 공원·숲·물은 삼각형이 수백 m — 안개가 꼭짓점에서 계산돼 가까운 풀밭까지 뿌옇게 보이지 않도록 60 m 아래로 자른다
            park.SplitLong(60f); forest.SplitLong(60f); water.SplitLong(60f);
            if (!park.IsEmpty) park.ToObject("parks", DcAssets.Pbr("leafy_grass", DcAssets.Hex("#7FA35A"), new Color(0.95f, 1f, 0.9f), 0.6f), node);
            if (!forest.IsEmpty) forest.ToObject("forests", DcAssets.Pbr("leafy_grass", DcAssets.Hex("#5E8746"), new Color(0.72f, 0.8f, 0.68f), 0.6f), node);
            // 물(양재천 등)이 거울처럼(매끈함 0.85) 하늘·건물을 거꾸로 비춰 땅이 뚫린 것처럼 보였다 → 짙은 물빛, 덜 매끈하게
            if (!water.IsEmpty) water.ToObject("water", DcAssets.Lit(DcAssets.Hex("#2E4A57"), 0.12f), node);
            if (!bank.IsEmpty) bank.ToObject("river-banks", DcAssets.Pbr("brushed_concrete", DcAssets.Hex("#8E8C86"), new Color(0.78f, 0.77f, 0.74f), 0.4f), node, true);
            tk?.Emit(node, "park-trees");
            if (!trunks.IsEmpty) trunks.ToObject("park-trunks", DcAssets.Lit(DcAssets.Hex("#6B4F3A"), 0.1f), node, true);
            if (!leaves.IsEmpty) leaves.ToObject("park-leaves", DcAssets.Lit(DcAssets.Hex("#4A7536"), 0.05f), node, true);
        }

        static void BuildSurfaces(RoadDef r, float h, Transform node, Dictionary<int, (V2 p, float r)> jr, Func<V2, bool> inJunction)
        {
            var walk = new MeshKit { uvScale = 1.6f };
            var major = new MeshKit { uvScale = 5f };
            var minor = new MeshKit { uvScale = 4f };
            var curb = new MeshKit();
            foreach (var s in r.strips)
            {
                if (s.sidewalk) walk.Ribbon(s.pts, s.width + 2f * SidewalkW, h + 0.012f, false);
                (Major(s.hw) ? major : minor).Ribbon(s.pts, s.width, h + 0.03f, false);
                // 연석: 보도가 있는 길 양쪽 가장자리에 높이 14 cm 띠(교차로 안은 끊는다)
                if (s.sidewalk)
                {
                    Curb(curb, Stages.Offset(s.pts, s.width / 2f + 0.1f), 1f, h, inJunction);
                    Curb(curb, Stages.Offset(s.pts, -(s.width / 2f + 0.1f)), -1f, h, inJunction);
                }
            }
            foreach (var kv in jr.Values)
            {
                var ring = new List<V2>();
                for (int i = 0; i < 14; i++) ring.Add(kv.p + V2.FromAngle(i / 14f * Geom.Tau) * kv.r);
                (kv.r > 5.5f ? major : minor).Polygon(ring, h + 0.034f);
            }
            if (!walk.IsEmpty) walk.ToObject("sidewalks", DcAssets.Pbr("square_concrete_pavers", DcAssets.Hex("#CFCBC2"), new Color(1f, 0.98f, 0.96f), 0.8f), node);
            if (!major.IsEmpty) major.ToObject("roads-major", DcAssets.Pbr("clean_asphalt", DcAssets.Hex("#4C5056"), new Color(1.05f, 1.05f, 1.08f), 0.9f), node);
            if (!minor.IsEmpty) minor.ToObject("roads-minor", DcAssets.Pbr("asphalt_02", DcAssets.Hex("#55595F"), new Color(0.82f, 0.82f, 0.85f), 0.9f), node);
            if (!curb.IsEmpty) curb.ToObject("curbs", DcAssets.Pbr("brushed_concrete", DcAssets.Hex("#BDBBB5"), Color.white, 0.6f), node, false);
        }

        /// <summary>
        /// 연석 띠: 폭 0.2 m 윗면(높이 0.14 m) + 길 쪽 옆면. side = +1 이면 진행 방향 오른쪽 가장자리(길은 왼쪽), -1 이면 반대.
        /// 4 m 조각으로 나눠 교차로 안 조각은 뺀다.
        /// </summary>
        static void Curb(MeshKit mk, V2[] pts, float side, float h, Func<V2, bool> skip)
        {
            const float W = 0.2f, H = 0.14f;
            float yTop = h + H, yRoad = h + 0.025f;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                float len = a.DistanceTo(b);
                if (len < 0.05f) continue;
                var d = (b - a) / len;
                var right = new V2(-d.y, d.x);
                var outV = right * side;            // 길 반대쪽(보도 쪽)
                var roadDir = Space3D.W(-outV) - Space3D.W(V2.Zero);
                int pieces = Math.Max(1, (int)Math.Ceiling(len / 4f));
                for (int k = 0; k < pieces; k++)
                {
                    var p0 = a + d * (len * k / pieces);
                    var p1 = a + d * (len * (k + 1) / pieces);
                    if (skip((p0 + p1) / 2f)) continue;
                    var i0 = Space3D.W(p0 - outV * (W / 2f), 0f); var i1 = Space3D.W(p1 - outV * (W / 2f), 0f);
                    var o0 = Space3D.W(p0 + outV * (W / 2f), 0f); var o1 = Space3D.W(p1 + outV * (W / 2f), 0f);
                    float u0 = (float)k / pieces * len / 2f, u1 = (float)(k + 1) / pieces * len / 2f;
                    // 윗면
                    mk.Quad(Lift(i0, yTop), Lift(i1, yTop), Lift(o1, yTop), Lift(o0, yTop), Vector3.up,
                        new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.1f), new Vector2(u0, 0.1f));
                    // 길 쪽 옆면
                    mk.Quad(Lift(i0, yRoad), Lift(i1, yRoad), Lift(i1, yTop), Lift(i0, yTop), roadDir.normalized,
                        new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.07f), new Vector2(u0, 0.07f));
                }
            }
        }

        /// <summary>교차로 원 안인가(20 m 칸 격자)</summary>
        static Func<V2, bool> JunctionTest(Dictionary<int, (V2 p, float r)> jr)
        {
            var grid = new Dictionary<long, List<(V2 p, float r)>>();
            long Key(int gx, int gy) => ((long)gx << 32) ^ (uint)gy;
            foreach (var j in jr.Values)
            {
                var k = Key((int)Math.Floor(j.p.x / 20f), (int)Math.Floor(j.p.y / 20f));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(V2, float)>();
                l.Add(j);
            }
            return p =>
            {
                int gx = (int)Math.Floor(p.x / 20f), gy = (int)Math.Floor(p.y / 20f);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid.TryGetValue(Key(gx + dx, gy + dy), out var l))
                            foreach (var j in l) if (j.p.DistanceTo(p) < j.r + 0.5f) return true;
                return false;
            };
        }
    }
}
