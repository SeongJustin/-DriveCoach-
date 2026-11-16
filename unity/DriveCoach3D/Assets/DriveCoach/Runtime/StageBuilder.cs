// 3D 주차 연습 — 한 판의 움직이지 않는 것을 3D 로: 바닥·칠·벽·기둥·주차된 차·경사로·서킷·도로
// 모양과 크기는 전부 Core(Stage) 의 실제 규격(m) 그대로. 3D 모델은 Kenney·Quaternius(CC0), 바닥 질감은 Poly Haven(CC0).
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    public static class StageBuilder
    {
        const float PaintY = 0.012f;       // 바닥 칠은 바닥보다 조금 위(깜빡임 방지)
        const float LineW = 0.12f;

        /// <summary>주차된 차로 쓰는 Kenney 모델(색 번호 순서대로 돌려 쓴다)</summary>
        static readonly string[] ParkedModels = { "sedan", "suv", "hatchback-sports", "sedan-sports", "suv-luxury", "van", "sedan", "taxi" };

        public static GameObject Build(Stage st, Elevation el, Transform parent)
        {
            var root = new GameObject("Stage " + st.id);
            root.transform.SetParent(parent, false);
            SignalHeads.Current = null;   // 앞 판(내 동네)의 실제 신호등이 남지 않게
            CountryStyle.Current = CountryStyle.KR;   // 지역 판이면 BuildRoad 가 그 나라로 바꾼다
            NightLights.Reset();
            Dc.Lap(null);
            for (int fi = 0; fi < st.floors.Count; fi++) BuildFloor(st, st.floors[fi], el.floor[fi], root.transform);
            Dc.Lap("floors");
            BuildRamps(st, el, root.transform);
            if (st.circuit != null) BuildCircuit(st.circuit, el.floor[0], root.transform);
            if (st.road != null) BuildRoad(st.road, el.floor[0], root.transform);
            return root;
        }

        /// <summary>바닥 네모를 20 m 칸 줄로 깔되 빠진 칸(도랑 둘레)은 건너뛰고, 그 칸들은 미리 잘라 둔 땅 삼각형으로 채운다</summary>
        static void GroundWithHoles(MeshKit mk, Core.Rect r, float h, Vector2[] cells, Vector2[] gt)
        {
            const float C = 20f;
            var skip = new HashSet<long>();
            foreach (var c in cells) skip.Add(((long)Mathf.RoundToInt(c.x) << 32) ^ (uint)Mathf.RoundToInt(c.y));
            int ix0 = Mathf.FloorToInt(r.x / C), ix1 = Mathf.CeilToInt((r.x + r.w) / C) - 1;
            int iy0 = Mathf.FloorToInt(r.y / C), iy1 = Mathf.CeilToInt((r.y + r.h) / C) - 1;
            for (int iy = iy0; iy <= iy1; iy++)
            {
                // "이어지는 칸 없음" 표시는 int.MinValue. 예전에는 -1 / "run < 0" 이었는데 칸 번호가 지역 원점 서쪽에서는 음수라
                //   서쪽 절반(x < 0)에는 땅이 한 칸도 깔리지 않았다 — 길 옆이 하늘빛으로 비어 보이고 건물·신호등이 떠 보이던 원인.
                const int None = int.MinValue;
                int run = None;
                for (int ix = ix0; ix <= ix1 + 1; ix++)
                {
                    bool hole = ix > ix1 || skip.Contains(((long)ix << 32) ^ (uint)iy);
                    if (!hole && run == None) run = ix;
                    if (hole && run != None)
                    {
                        float x0 = Mathf.Max(r.x, run * C), x1 = Mathf.Min(r.x + r.w, ix * C);
                        float y0 = Mathf.Max(r.y, iy * C), y1 = Mathf.Min(r.y + r.h, (iy + 1) * C);
                        // 40 m 씩 끊어 깐다(수 km 짜리 긴 네모는 안개가 먼 꼭짓점에서만 계산돼 색이 틀어진다)
                        if (y1 > y0)
                            for (float xa = x0; xa < x1 - 0.01f; xa += 2f * C)
                                mk.Rect(new Core.Rect(xa, y0, Mathf.Min(2f * C, x1 - xa), y1 - y0), h);
                        run = None;
                    }
                }
            }
            for (int i = 0; i + 2 < gt.Length; i += 3)
                mk.TriUp(Space3D.W(new V2(gt[i].x, gt[i].y), h), Space3D.W(new V2(gt[i + 1].x, gt[i + 1].y), h), Space3D.W(new V2(gt[i + 2].x, gt[i + 2].y), h));
        }

        static Material SurfaceMat(Floor f)
        {
            switch (f.surface)
            {
                case Surface.Concrete: return DcAssets.Ground("garage_floor", DcAssets.Hex("#B9BDC4"), DcAssets.Hex("#E4E6EA"));
                case Surface.Grass: return DcAssets.Lit(DcAssets.Hex("#6F8F4E"), 0.05f);
                case Surface.Block:
                    // 동네 땅(건물 사이): 무광 단색 회색. 하늘빛으로 비어 보이던 것은 재질 탓이 아니라 땅이 서쪽 절반에 안 깔린 탓이었다(GroundWithHoles 참고).
                    // 단색 회색은 햇빛에서 하얗게 떠서 "빈 땅"처럼 보였다 → 닳은 콘크리트 질감(조금 어둡게)
                    // 독립 평가: "땅색과 차도색이 거의 같아 도로 경계를 인도 띠로만 알 수 있다" → 땅을 밝고 따뜻한 모래빛으로(아스팔트와 뚜렷이 구분)
                    return DcAssets.Pbr("concrete_floor_worn_001", DcAssets.Hex("#B9B19E"), new Color(1.0f, 0.95f, 0.84f), 0.5f);
                default: return DcAssets.Ground("asphalt_03", DcAssets.Hex("#55595F"), Color.white);
            }
        }

        static void BuildFloor(Stage st, Floor f, float h, Transform root)
        {
            var node = new GameObject("Floor " + f.name).transform;
            node.SetParent(root, false);

            var ground = new MeshKit { uvScale = 4f };
            // 지하차도 도랑이 있는 지역은 땅에 구멍 — 20 m 칸으로 깔되 도랑과 닿는 칸(GCEL)은 빼고 그 칸의 땅(GRND)으로
            var surf = st.road?.region != null ? RoadSurface.Load(st.road.region.surfaceFile) : null;
            if (surf != null && surf.tris.TryGetValue("GCEL", out var cells) && cells.Length > 0 && surf.tris.TryGetValue("GRND", out var gt))
                GroundWithHoles(ground, f.ground, h, cells, gt);
            else if (f.surface == Surface.Block)
                GroundWithHoles(ground, f.ground, h, new Vector2[0], new Vector2[0]);   // 동네(수 km): 구멍이 없어도 잘게 깐다(안개 계산)
            else
                ground.Rect(f.ground, h);
            var groundGo = ground.ToObject("ground", SurfaceMat(f), node);
            // 동네 땅(점 15만 개)은 지은 뒤 다시 읽지 않는다 → 그래픽 장치에만 두고 일반 메모리 사본은 내린다
            if (surf != null || f.surface == Surface.Block) groundGo.GetComponent<MeshFilter>().sharedMesh.UploadMeshData(true);

            // 보도(살짝 높고 밝은 콘크리트)·연석
            if (f.walks.Count > 0)
            {
                var walk = new MeshKit { uvScale = 2f };
                foreach (var w in f.walks)
                    walk.Prism(new[] { new V2(w.x, w.y), new V2(w.x + w.w, w.y), new V2(w.x + w.w, w.y + w.h), new V2(w.x, w.y + w.h) }, h, h + 0.15f);
                walk.ToObject("walks", DcAssets.Ground("garage_floor", DcAssets.Hex("#D5D8DD"), DcAssets.Hex("#F4F5F7")), node);
            }
            if (f.curbs.Count > 0)
            {
                var curb = new MeshKit();
                foreach (var c in f.curbs) curb.Prism(c, h, h + 0.15f);
                curb.ToObject("curbs", DcAssets.Lit(DcAssets.Hex("#C9CCD2"), 0.2f), node, true);
            }

            // 바닥 칠: 흰 주차선, 노란 선, 횡단보도, 화살표
            var paint = new MeshKit();
            foreach (var ln in f.lines) paint.Line(ln.a, ln.b, LineW, h + PaintY);
            foreach (var cw in f.crosswalks) Crosswalk(paint, cw, h + PaintY);
            foreach (var a in f.arrows) Arrow(paint, a.p, a.h * Geom.Deg2Rad, h + PaintY);
            if (!paint.IsEmpty) paint.ToObject("paint", DcAssets.Lit(DcAssets.Paint, 0.35f), node);
            if (f.yellow.Count > 0)
            {
                var y = new MeshKit();
                foreach (var ln in f.yellow) y.Line(ln.a, ln.b, LineW * 1.4f, h + PaintY);
                y.ToObject("yellow", DcAssets.Lit(DcAssets.Yellow, 0.35f), node);
            }

            // 벽·기둥: 지하는 천장까지(2.6 m), 바깥은 낮은 콘크리트 방호벽(0.9 m)
            float wallH = f.dark ? 2.6f : 0.9f;
            if (st.mode == StageMode.Circuit) wallH = 0.75f;
            if (f.walls.Count > 0)
            {
                var wall = new MeshKit { uvScale = 3f };
                foreach (var w in f.walls) wall.Prism(w, h, h + wallH);
                var wallMat = st.mode == StageMode.Circuit
                    ? DcAssets.Lit(DcAssets.Hex("#2B2F36"), 0.3f)
                    : DcAssets.Ground("garage_floor", DcAssets.Hex("#9AA0A8"), DcAssets.Hex(f.dark ? "#B8BDC5" : "#D7DADF"));
                wall.ToObject("walls", wallMat, node, true);
            }
            if (f.pillars.Count > 0)
            {
                var pil = new MeshKit { uvScale = 3f };
                var band = new MeshKit();
                foreach (var p in f.pillars)
                {
                    pil.Prism(p, h, h + 2.6f);
                    band.Prism(Grow(p, 0.01f), h, h + 0.9f);   // 아래쪽 노란 띠(지하주차장 기둥처럼)
                }
                pil.ToObject("pillars", DcAssets.Ground("garage_floor", DcAssets.Hex("#A8ADB5"), DcAssets.Hex("#D9DCE1")), node, true);
                band.ToObject("pillar-bands", DcAssets.Lit(DcAssets.Hex("#F2C200"), 0.3f), node, true);
            }
            if (f.dark) Ceiling(f, h + 2.62f, node);

            // 주차된 차
            for (int i = 0; i < f.cars.Count; i++)
            {
                var oc = f.cars[i];
                var model = ParkedModels[oc.col % ParkedModels.Length];
                var car = ModelCar("Kenney/CarKit/" + model, oc.len, oc.wid, node, StageColor(oc.col));
                car.transform.position = Space3D.W(oc.c, h);
                car.transform.rotation = Space3D.Rot(oc.h * Geom.Deg2Rad);
            }

            // 바깥 주차장: 가로등 몇 개(분위기). 지하는 천장 등으로 대신
            if (!f.dark && st.mode == StageMode.Parking) StreetLights(f, h, node);
        }

        /// <summary>지하 천장: 위에서 보는 카메라를 가리지 않게 등 줄만(빛나는 막대) 띄운다.</summary>
        static void Ceiling(Floor f, float y, Transform node)
        {
            var lamps = new MeshKit();
            var g = f.ground;
            for (float x = g.x + 4f; x < g.x + g.w - 2f; x += 7.5f)
                for (float z = g.y + 3f; z < g.y + g.h - 2f; z += 6f)
                    lamps.Box(Space3D.W(new V2(x, z), y), new Vector3(1.2f, 0.06f, 0.18f), 0f);
            if (!lamps.IsEmpty)
            {
                var go = lamps.ToObject("ceiling-lamps", DcAssets.Unlit(DcAssets.Hex("#FFF6DD")), node);
                go.layer = 0;
            }
        }

        static void StreetLights(Floor f, float h, Transform node)
        {
            var proto = DcAssets.Model("Quaternius/ModularStreet/Streetlight_Single");
            if (proto == null) return;
            var g = f.ground;
            int count = 0;
            for (float x = g.x + 3f; x < g.x + g.w && count < 6; x += 16f, count++)
            {
                var go = Object.Instantiate(proto, node);
                go.name = "streetlight";
                go.transform.position = Space3D.W(new V2(x, g.y + 1.2f), h);
                FitHeight(go, 6f);
            }
        }

        static V2[] Grow(V2[] p, float m)
        {
            var c = Geom.PolyCenter(p);
            var o = new V2[p.Length];
            for (int i = 0; i < p.Length; i++) o[i] = p[i] + (p[i] - c).Normalized * m;
            return o;
        }

        static void Crosswalk(MeshKit mk, V2[] p, float y)
        {
            var e1 = p[1] - p[0];
            var e2 = p[3] - p[0];
            var bars = e1.Length < e2.Length ? e1 : e2;
            var along = bars == e1 ? e2 : e1;
            int count = (int)(along.Length / 0.9f);
            var u = along.Normalized;
            var start = p[0] + u * ((along.Length - count * 0.9f) / 2f + 0.2f);
            for (int i = 0; i < count; i++)
            {
                var q0 = start + u * (i * 0.9f);
                mk.Polygon(new[] { q0, q0 + bars, q0 + bars + u * 0.5f, q0 + u * 0.5f }, y);
            }
        }

        static void Arrow(MeshKit mk, V2 p, float h, float y)
        {
            var d = V2.FromAngle(h);
            var n = d.Orthogonal;
            // 바닥 화살표: 막대 + 머리(두 다각형으로 나눠 볼록하게)
            mk.Polygon(new[] { p - d * 1.2f + n * 0.16f, p + d * 0.3f + n * 0.16f, p + d * 0.3f - n * 0.16f, p - d * 1.2f - n * 0.16f }, y);
            mk.Polygon(new[] { p + d * 0.3f + n * 0.45f, p + d * 1.1f, p + d * 0.3f - n * 0.45f }, y);
        }

        static void BuildRamps(Stage st, Elevation el, Transform root)
        {
            if (st.ramps.Count == 0 && st.pads.Count == 0) return;
            var node = new GameObject("Ramps").transform;
            node.SetParent(root, false);
            var mat = DcAssets.Ground("garage_floor", DcAssets.Hex("#B3B8BF"), DcAssets.Hex("#E9EBEE"));
            for (int i = 0; i < st.ramps.Count; i++)
            {
                var r = st.ramps[i];
                var ax = (r.b - r.a).Normalized;
                var n = ax.Orthogonal * (r.w / 2f);
                var a0 = Space3D.W(r.a + n, el.rampStart[i]); var a1 = Space3D.W(r.a - n, el.rampStart[i]);
                var b0 = Space3D.W(r.b + n, el.rampEnd[i]); var b1 = Space3D.W(r.b - n, el.rampEnd[i]);
                var go = new GameObject("ramp" + i);
                go.transform.SetParent(node, false);
                var mesh = new Mesh();
                mesh.vertices = new[] { a0, a1, b1, b0 };
                float len = r.a.DistanceTo(r.b);
                mesh.uv = new[] { new Vector2(0, 0), new Vector2(r.w / 4f, 0), new Vector2(r.w / 4f, len / 4f), new Vector2(0, len / 4f) };
                // 위에서 보이는 쪽으로 감기
                var up = Vector3.Cross(a1 - a0, b0 - a0).y > 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
                mesh.triangles = up;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                // 옆 벽(낮은 턱)
                var side = new MeshKit();
                for (int s = -1; s <= 1; s += 2)
                {
                    var off = ax.Orthogonal * (s * (r.w / 2f + 0.15f));
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / 2f));
                    for (int k = 0; k < steps; k++)
                    {
                        float t0 = k / (float)steps, t1 = (k + 1) / (float)steps;
                        var p0 = r.a + (r.b - r.a) * t0 + off; var p1 = r.a + (r.b - r.a) * t1 + off;
                        float y0 = Mathf.Lerp(el.rampStart[i], el.rampEnd[i], t0), y1 = Mathf.Lerp(el.rampStart[i], el.rampEnd[i], t1);
                        var c = (Space3D.W(p0, y0) + Space3D.W(p1, y1)) / 2f + Vector3.up * 0.45f;
                        side.Box(c, new Vector3(0.3f, 0.9f + Mathf.Abs(y1 - y0), Vector3.Distance(Space3D.W(p0, y0), Space3D.W(p1, y1))), Space3D.Yaw(ax.Angle) - 90f + 90f);
                    }
                }
                side.ToObject("ramp-sides" + i, DcAssets.Lit(DcAssets.Hex("#C5C9CF"), 0.2f), node, true);
            }
            if (st.pads.Count > 0)
            {
                var pad = new MeshKit();
                for (int p = 0; p < st.pads.Count; p++) pad.Polygon(st.pads[p], el.padHeight[p]);
                pad.ToObject("pads", mat, node);
            }
        }

        // ───────────────────────── 서킷 ─────────────────────────

        static void BuildCircuit(CircuitDef c, float h, Transform root)
        {
            var node = new GameObject("Circuit").transform;
            node.SetParent(root, false);
            var track = new MeshKit { uvScale = 5f };
            track.Ribbon(c.center, c.width, h + 0.02f, true);
            track.ToObject("track", DcAssets.Ground("asphalt_floor", DcAssets.Hex("#4C5056"), Color.white), node);
            // 가장자리 흰 선
            var edge = new MeshKit();
            edge.Ribbon(c.center, 0.22f, h + 0.03f, true, c.width / 2f - 0.15f);
            edge.Ribbon(c.center, 0.22f, h + 0.03f, true, -(c.width / 2f - 0.15f));
            edge.ToObject("edges", DcAssets.Lit(DcAssets.Paint, 0.4f), node);
            // 코너 연석(빨강·흰색 번갈아): 굽은 곳 바깥·안쪽
            var red = new MeshKit(); var white = new MeshKit();
            int n = c.center.Length;
            for (int i = 0; i < n; i++)
            {
                if (c.curvature[i] < 1f / 30f) continue;
                var mk = (i / 2) % 2 == 0 ? red : white;
                mk.Ribbon(c.center, 0.9f, h + 0.035f, true, c.width / 2f + 0.45f, i, i + 1);
                mk.Ribbon(c.center, 0.9f, h + 0.035f, true, -(c.width / 2f + 0.45f), i, i + 1);
            }
            if (!red.IsEmpty) red.ToObject("curbs-red", DcAssets.Lit(DcAssets.Hex("#D63B2F"), 0.3f), node);
            if (!white.IsEmpty) white.ToObject("curbs-white", DcAssets.Lit(DcAssets.Hex("#F4F4F4"), 0.3f), node);
            // 출발선(체크무늬)
            var t0 = c.Tangent(0);
            var nn = new V2(t0.y, -t0.x);
            var chkA = new MeshKit(); var chkB = new MeshKit();
            int cells = Mathf.Max(4, Mathf.RoundToInt(c.width / 0.6f));
            float cw = c.width / cells;
            for (int k = 0; k < cells; k++)
                for (int r = 0; r < 2; r++)
                {
                    var o = c.center[0] + nn * (c.width / 2f - cw * (k + 0.5f)) + t0 * (cw * (r - 0.5f));
                    var mk = (k + r) % 2 == 0 ? chkA : chkB;
                    mk.Polygon(Geom.RectPoly(o, cw, cw, t0.Angle * Geom.Rad2Deg), h + 0.04f);
                }
            chkA.ToObject("start-a", DcAssets.Lit(Color.white, 0.4f), node);
            chkB.ToObject("start-b", DcAssets.Lit(DcAssets.Hex("#1B1B1B"), 0.4f), node);
            // 코너 꼭짓점 고깔(Kenney)
            var cone = DcAssets.Model("Kenney/CityRoads/construction-cone");
            if (cone != null)
            {
                for (int i = 2; i < n - 2; i++)
                {
                    bool apex = c.curvature[i] > 1f / 18f && c.curvature[i] >= c.curvature[i - 1] && c.curvature[i] >= c.curvature[i + 1];
                    if (!apex) continue;
                    var tan = c.Tangent(i);
                    var left = new V2(tan.y, -tan.x);
                    // 안쪽 = 굽는 방향(다음 점 쪽 법선)
                    var turn = (c.center[(i + 3) % n] - c.center[i]).Dot(left) > 0 ? 1f : -1f;
                    var p = c.center[i] + left * turn * (c.width / 2f + 1.3f);
                    var go = Object.Instantiate(cone, node);
                    go.transform.position = Space3D.W(p, h);
                    FitHeight(go, 0.7f);
                }
            }
        }

        // ───────────────────────── 도로 ─────────────────────────

        static void BuildRoad(RoadDef r, float h, Transform root)
        {
            var node = new GameObject("Road").transform;
            node.SetParent(root, false);
            CountryStyle.Current = CountryStyle.For(r.region);      // 나라별 모양(칠·표지·신호…) — 지역이 아니면 한국
            if (r.region != null)
            {
                // 내 동네(지역 지도): 길·칠·건물·가로수·가로등은 CityBuilder, 신호등만 여기서
                Dc.Lap(null);
                CityBuilder.Build(r, h, node); Dc.Lap("city");
                // 건물·길 말고 나머지(신호등·표지·볼라드·고가·지하차도…)의 메시도 지은 뒤 다시 읽지 않는다 →
                //   그래픽 장치에만 두고 일반 메모리의 사본을 내린다(Android 가이드 "Read/Write 는 꺼 둔다 — 켜 두면 메모리가 두 배").
                //   큰 동네를 짓는 동안 생긴 임시 배열은 단계 사이에 바로 치워, 관리 힙이 최고치까지 부풀지 않게 한다.
                System.GC.Collect();
                MeshKit.Built = new System.Collections.Generic.List<Mesh>();
                try
                {
                    // 서울 T-GIS 실제 자리 신호등·안전표지가 있으면 그것으로(없으면 예전처럼 교차로마다 Kenney 신호등)
                    if (!SignalHeads.Build(r, h, node)) PlaceSignals(r, h, node); Dc.Lap("signals");
                    SignBuilder.Build(r, h, node); Dc.Trim(); Dc.Lap("SignBuilder");
                    FacilityBuilder.Build(r, h, node); Dc.Lap("FacilityBuilder");   // 주차장(칸·세운 차)·주유소·충전소
                    BusStopBuilder.Build(r, h, node); Dc.Lap("BusStopBuilder");    // 버스정류장(길가 쉘터·표지, 중앙 섬 위 승강장)
                    StreetFurniture.Build(r, h, node); Dc.Trim(); Dc.Lap("StreetFurniture");   // 횡단보도 볼라드·큰길 보행자 울타리(어린이보호구역은 노랑)
                    GradeBuilder.Build(r, h, node); Dc.Lap("GradeBuilder");      // 고가도로(상판·방호벽·교각·칠)
                    HumpBuilder.Build(r, h, node); Dc.Lap("HumpBuilder");       // 과속방지턱(솟은 원호형, 노랑·흰 빗금)
                    SchoolZoneBuilder.Build(r, h, node); Dc.Lap("SchoolZoneBuilder"); // 어린이보호구역 입구 빨간 노면
                    UnderpassBuilder.Build(r, h, node); Dc.Lap("UnderpassBuilder");  // 지하차도(파인 길·옹벽·난간·덮개 밑면)
                }
                finally
                {
                    var built = MeshKit.Built; MeshKit.Built = null;
                    long vtx = 0;
                    foreach (var m in built) if (m != null) { vtx += m.vertexCount; if (m.isReadable) m.UploadMeshData(true); }   // 대부분 ToObject 에서 이미 내림
                    // MeshKit 를 거치지 않고 만든 메시(밤 창문 겹, 나무 등)도: 실행 중에 만든 것(InstanceID < 0)만 — 가져온 모델 메시는 다음 동네를 지을 때 다시 읽는다
                    int extra = 0;
                    foreach (var mf in node.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = mf.sharedMesh;
                        if (m != null && m.isReadable && m.GetInstanceID() < 0) { vtx += m.vertexCount; m.UploadMeshData(true); extra++; }
                    }
                    Debug.Log("DC3D region extra meshes " + built.Count + " + " + extra + ", vertices " + vtx + " (사본 내림)");
                    System.GC.Collect();
                    Dc.Lap("region-upload");
                    Dc.Census("region");   // 물체 수·메시 크기 한 줄(기기에서 무엇이 큰지)
                    // 노면 자료(삼각형 60만 점·8 m 격자, 스크립트 힙 약 15 MB)는 짓는 동안에만 쓴다 → 다 지었으면 잊는다.
                    //   같은 동네를 다시 열면 파일에서 다시 읽는다(1초 안쪽).
                    CityBuilder.Forget();
                    RoadSurface.Forget(r.region.surfaceFile);
                    // 짓는 데만 쓴 파일(동네 JSON·노면·나무·차 모델 .bytes TextAsset)은 읽은 자리에서 바로 내린다
                    //   (RegionCatalog.ReadText/ReadBytes, TreeKit.Load, FleetModels.Proto). 여기서 Resources.UnloadUnusedAssets() 를 부르면
                    //   장면 전체를 훑는 일(수백 ms)이 가림막이 걷힌 뒤 첫 주행 프레임들에 걸린다(화면 수 관리기 Governor 가 화질을 내리는 원인 후보).
                }
                return;
            }
            var pave = new MeshKit { uvScale = 5f };
            foreach (var p in r.pavement) pave.Rect(p, h + 0.02f);
            pave.ToObject("pavement", DcAssets.Ground("asphalt_floor", DcAssets.Hex("#4C5056"), Color.white), node);
            var yellow = new MeshKit();
            foreach (var s in r.centerLines)
            {
                var d = (s.b - s.a).Normalized; var o = new V2(-d.y, d.x) * 0.12f;
                yellow.Line(s.a + o, s.b + o, 0.12f, h + 0.03f);
                yellow.Line(s.a - o, s.b - o, 0.12f, h + 0.03f);
            }
            yellow.ToObject("centerline", DcAssets.Lit(DcAssets.Yellow, 0.4f), node);
            var white = new MeshKit();
            foreach (var s in r.laneLines)
            {
                float len = s.a.DistanceTo(s.b); var d = (s.b - s.a).Normalized;
                for (float x = 0; x < len; x += 8f) white.Line(s.a + d * x, s.a + d * Mathf.Min(len, x + 3f), 0.12f, h + 0.03f);
            }
            foreach (var sl in r.stopLines) white.Line(sl.a, sl.b, 0.45f, h + 0.031f);
            if (!white.IsEmpty) white.ToObject("lanelines", DcAssets.Lit(DcAssets.Paint, 0.4f), node);
            // 블록(건물 덩어리): 도로가 아닌 곳에 낮은 상자 몇 개 — 동네 느낌
            var blocks = new MeshKit();
            var rnd = new System.Random(7);
            for (int i = 0; i < 18; i++)
            {
                var c = new V2(-52f + (i % 6) * 21f, i < 6 ? -14f : (i < 12 ? 14f : -60f + (i % 2) * 120f));
                if (i >= 12) c = new V2(-40f + (i - 12) * 16f, (i % 2 == 0) ? -52f : 52f);
                float bw = 8f + (float)rnd.NextDouble() * 6f, bd = 7f + (float)rnd.NextDouble() * 5f, bh = 4f + (float)rnd.NextDouble() * 10f;
                blocks.Box(Space3D.W(c, h + bh / 2f), new Vector3(bw, bh, bd), 0f);
            }
            blocks.ToObject("blocks", DcAssets.Lit(DcAssets.Hex("#D8D2C8"), 0.15f), node, true);
            PlaceSignals(r, h, node);
        }

        /// <summary>신호등(Kenney) — 불빛은 Overlays 가 매 프레임 바꾼다</summary>
        static void PlaceSignals(RoadDef r, float h, Transform node)
        {
            var proto = DcAssets.Model("Kenney/CityRoads/traffic-light");
            foreach (var sg in r.signals)
            {
                if (proto == null) break;
                var go = Object.Instantiate(proto, node);
                go.name = "signal";
                // 실제 노면이 있으면 기둥이 아스팔트 위에 서지 않게 가장 가까운 보도로 옮긴다
                var pos = sg.pos;
                float y = h;
                var surf = CityBuilder.Current;
                if (surf != null)
                {
                    pos = surf.PushOff(pos, 0.35f);
                    if (surf.Sidewalk(pos)) y = h + CityBuilder.WalkH;
                }
                go.transform.position = Space3D.W(pos, y);
                go.transform.rotation = Quaternion.Euler(0f, 90f + sg.facingDeg, 0f);
                FitHeight(go, 4.2f);
            }
        }

        // ───────────────────────── 모델 도구 ─────────────────────────

        /// <summary>Kenney 차 모델을 길이·폭에 맞춰 놓는다(높이는 폭 비율). 모델이 없으면 상자 차.</summary>
        /// <param name="height">차 높이(m). 0 이하면 길이·폭 배율의 평균(예전 방식, 다른 차들). 내 차는 실제 높이</param>
        public static GameObject ModelCar(string path, float len, float wid, Transform parent, Color fallback, float height = -1f)
        {
            var proto = DcAssets.Model(path);
            var holder = new GameObject("car");
            holder.transform.SetParent(parent, false);
            if (proto == null) { BoxCar(holder.transform, len, wid, fallback); return holder; }
            var inst = Object.Instantiate(proto, holder.transform);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            FitCar(inst, len, wid, height);
            Glossy(inst);
            return holder;
        }

        static readonly System.Collections.Generic.Dictionary<Material, Material> glossy = new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>차 도장에 하늘이 비치게(매끈함·약한 금속감). 원래 재질마다 한 번만 복사.</summary>
        public static void Glossy(GameObject inst)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i];
                    if (m == null) continue;
                    if (!glossy.TryGetValue(m, out var g))
                    {
                        g = new Material(m) { name = m.name + "-gloss" };
                        if (g.HasProperty("_Smoothness")) g.SetFloat("_Smoothness", 0.66f);
                        if (g.HasProperty("_Metallic")) g.SetFloat("_Metallic", 0.12f);
                        if (g.HasProperty("_Glossiness")) g.SetFloat("_Glossiness", 0.66f);
                        glossy[m] = g;
                    }
                    ms[i] = g;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>
        /// 모델을 앞이 +Z(Unity)·길이 len·폭 wid 로 맞춘다. 앞뒤는 이름에 front 가 든 바퀴 위치로 판단(없으면 +Z 가 앞).
        /// 바닥에 닿게(아래 끝 = 0) 하고 가운데를 원점에 둔다.
        /// </summary>
        public static void FitCar(GameObject inst, float len, float wid, float height = -1f)
        {
            var t = inst.transform;
            var b = LocalBounds(inst);
            bool longX = b.size.x > b.size.z;
            float yaw = longX ? 90f : 0f;
            // 앞쪽 판단
            Transform frontWheel = null;
            foreach (var c in inst.GetComponentsInChildren<Transform>())
            {
                var n = c.name.ToLowerInvariant();
                if (n.Contains("wheel") && n.Contains("front")) { frontWheel = c; break; }
            }
            if (frontWheel != null)
            {
                var lp = inst.transform.InverseTransformPoint(frontWheel.position) - b.center;
                float along = longX ? lp.x : lp.z;
                if (longX) yaw = along > 0 ? -90f : 90f;   // +X 가 앞이면 -90° 돌려 +Z 로
                else yaw = along > 0 ? 0f : 180f;
            }
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float modelLen = longX ? b.size.x : b.size.z;
            float modelWid = longX ? b.size.z : b.size.x;
            float sx = wid / Mathf.Max(0.01f, modelWid), sz = len / Mathf.Max(0.01f, modelLen);
            float sy = height > 0f ? height / Mathf.Max(0.01f, b.size.y) : (sx + sz) / 2f;
            t.localScale = longX ? new Vector3(sz, sy, sx) : new Vector3(sx, sy, sz);
            // 다시 재서 가운데·바닥 맞춤
            var b2 = WorldBoundsRelative(inst, inst.transform.parent);
            t.localPosition -= new Vector3(b2.center.x, b2.min.y, b2.center.z);
        }

        public static Bounds LocalBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var inv = go.transform.worldToLocalMatrix;
            bool first = true;
            var b = new Bounds();
            foreach (var r in rs)
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null) continue;
                var m = inv * r.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static Bounds WorldBoundsRelative(GameObject go, Transform space)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            bool first = true;
            var b = new Bounds();
            var inv = space != null ? space.worldToLocalMatrix : Matrix4x4.identity;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = inv * r.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>모델 높이를 height(m) 로 맞추고 바닥을 0 에</summary>
        public static void FitHeight(GameObject go, float height)
        {
            var b = LocalBounds(go);
            float s = height / Mathf.Max(0.01f, b.size.y);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition -= new Vector3(0f, b.min.y * s, 0f);
        }

        /// <summary>모델이 없을 때의 상자 차(몸통 + 유리 + 바퀴)</summary>
        public static void BoxCar(Transform parent, float len, float wid, Color body)
        {
            var mk = new MeshKit();
            mk.Box(new Vector3(0, 0.55f, 0), new Vector3(wid, 0.7f, len), 0f);
            mk.ToObject("body", DcAssets.Lit(body, 0.6f, 0.2f), parent, true);
            var cab = new MeshKit();
            cab.Box(new Vector3(0, 1.15f, -len * 0.05f), new Vector3(wid * 0.86f, 0.55f, len * 0.5f), 0f);
            cab.ToObject("cabin", DcAssets.Lit(DcAssets.Hex("#2A3442"), 0.85f), parent, true);
            var wh = new MeshKit();
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    wh.Box(new Vector3(sx * (wid / 2f - 0.12f), 0.33f, sz * (len / 2f - 0.85f)), new Vector3(0.24f, 0.66f, 0.66f), 0f);
            wh.ToObject("wheels", DcAssets.Lit(DcAssets.Hex("#1A1A1A"), 0.2f), parent, true);
        }

        static Color StageColor(int i)
        {
            var c = Stages.CarColors[((i % Stages.CarColors.Length) + Stages.CarColors.Length) % Stages.CarColors.Length];
            return new Color(c[0], c[1], c[2]);
        }
    }
}
