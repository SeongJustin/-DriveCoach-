// 담당: S (11/8 밤, 대표: "주차장 … 주유소 휴게소 이런 것도 넣어야겠지. 너무 단조롭지 않게")
// 내 동네 지도의 시설:
//   주차장: 바닥(아스팔트 질감, 길보다 1 cm 아래라 길이 덮는다), 흰 주차칸 선, 세워 둔 차(낮은 다각형 차 — 색마다 한 메시로 합쳐 가볍게)
//   주유소·충전소: 콘크리트 바닥, 기둥 4개 캐노피(흰 판 + 회색 띠, 상표 없음), 주유기 섬, 길가 가격판(주유소 그림 칸)
// 모두 메시를 종류별로 합쳐 그리기 횟수를 줄인다.
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class FacilityBuilder
    {
        // 세운 차 색: 다니는 차와 같은 비율(TrafficView 승용 색, 11/9 대표: "도색 여러가지 색으로")
        static (Color c, float w)[] CarColors => TrafficView.PaletteOf(0);

        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null || (d.facilities.Count == 0 && d.parked.Count == 0)) return;
            var lot = new MeshKit { uvScale = 4f };
            var conc = new MeshKit { uvScale = 3f };
            var lines = new MeshKit();
            var bodies = new MeshKit[CarColors.Length];
            for (int i = 0; i < bodies.Length; i++) bodies[i] = new MeshKit();
            var glass = new MeshKit();
            var canopyTop = new MeshKit();
            var canopyBand = new MeshKit();
            var steel = new MeshKit();
            var pumps = new MeshKit();
            var signFace = new MeshKit();
            var rnd = new System.Random(23);
            var fleet = new ParkedFleet();
            var surf = CityBuilder.Current;
            int nCars = 0, nLots = 0, nFuel = 0;
            foreach (var f in d.facilities)
            {
                if (f.kind == "parking")
                {
                    nLots++;
                    lot.PolygonEar(f.poly, h + 0.022f);
                    for (int i = 0; i + 1 < f.stalls.Length; i += 2)
                        lines.Line(f.stalls[i], f.stalls[i + 1], 0.12f, h + 0.027f);
                    foreach (var (p, ang) in f.cars)
                    {
                        if (surf != null && surf.TouchesAsphalt(p, 1.2f)) continue;     // 길 위로 나온 칸에는 세우지 않는다
                        if (!fleet.Add(rnd, p, ang, h + 0.022f))
                            ParkedCar(bodies[TrafficView.PickIndex(CarColors, rnd.Next(10000))], glass, p, ang, h + 0.022f, rnd);
                        nCars++;
                    }
                }
                else if (f.kind == "fuel" || f.kind == "charging" || f.kind == "services")
                {
                    conc.PolygonEar(f.poly, h + 0.024f);
                    if (f.canopy == null) continue;
                    nFuel++;
                    var c = new V2(f.canopy[0], f.canopy[1]);
                    float ang = f.canopy[2], L = f.canopy[3], W = f.canopy[4];
                    var ax = new V2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var ay = new V2(-ax.y, ax.x);
                    float yaw = Space3D.Yaw(ang);
                    float top = h + 5.2f;
                    // 캐노피: 판(두께 0.9 m) + 옆 띠
                    canopyTop.Box(Space3D.W(c, top), new Vector3(W, 0.9f, L), yaw + 90f);
                    canopyBand.Box(Space3D.W(c, top - 0.15f), new Vector3(W + 0.06f, 0.42f, L + 0.06f), yaw + 90f);
                    // 기둥 넷
                    foreach (var sx in new[] { -0.32f, 0.32f })
                        foreach (var sy in new[] { -0.3f, 0.3f })
                        {
                            var q = c + ax * (sx * L) + ay * (sy * W);
                            steel.Box(Space3D.W(q, h + 2.4f), new Vector3(0.45f, 4.8f, 0.45f), yaw);
                        }
                    // 주유기 섬(캐노피 긴 쪽을 따라 2~3개): 받침 + 주유기 둘
                    int islands = L > 18f ? 3 : 2;
                    for (int k = 0; k < islands; k++)
                    {
                        float t = (k + 0.5f) / islands - 0.5f;
                        var q = c + ax * (t * L * 0.8f);
                        pumps.Box(Space3D.W(q, h + 0.12f), new Vector3(1.1f, 0.24f, 4.2f), yaw + 90f);
                        foreach (var s in new[] { -1f, 1f })
                            pumps.Box(Space3D.W(q + ay * (s * 1.0f), h + 1.0f), new Vector3(0.55f, 1.7f, 0.9f), yaw + 90f);
                    }
                    // 가격판: 넓이에서 가장 가까운 길 쪽 모서리(없으면 캐노피 한쪽 끝)
                    var sp = c + ax * (L * 0.5f + 3f);
                    if (surf != null) sp = surf.PushOff(sp, 0.5f);
                    steel.Box(Space3D.W(sp, h + 2.4f), new Vector3(0.25f, 4.8f, 0.25f), yaw);
                    SignPanel(signFace, sp, h + 4.4f, ang, f.kind);
                }
            }
            // 이면도로 가장자리에 세워 둔 차(11/9)
            int nStreet = 0;
            foreach (var pc in d.parked)
            {
                if (!fleet.Add(rnd, pc.c, pc.ang, h + 0.03f))
                    ParkedCar(bodies[TrafficView.PickIndex(CarColors, rnd.Next(10000))], glass, pc.c, pc.ang, h + 0.03f, rnd);
                nStreet++;
            }
            if (nLots > 0 || nStreet > 0)
            {
                if (!lot.IsEmpty) lot.ToObject("parking-lots", DcAssets.Ground("asphalt_floor", DcAssets.Hex("#5A5E63"), new Color(0.93f, 0.93f, 0.95f)), node, false, true);
                if (!lines.IsEmpty) lines.ToObject("parking-stalls", DcAssets.Unlit(DcAssets.Hex("#E8E8E2")), node);
                for (int i = 0; i < bodies.Length; i++)
                    if (!bodies[i].IsEmpty) bodies[i].ToObject("parked-cars-" + i, DcAssets.Lit(CarColors[i].c, 0.75f, 0.35f), node, true, true);
                if (!glass.IsEmpty) glass.ToObject("parked-glass", DcAssets.Lit(DcAssets.Hex("#1A2026"), 0.9f, 0.2f), node, false, true);
            }
            int nFleet = fleet.Emit(node);
            if (!conc.IsEmpty) conc.ToObject("station-ground", DcAssets.Ground("garage_floor", DcAssets.Hex("#9A9C9E"), Color.white), node, false, true);
            if (nFuel > 0)
            {
                canopyTop.ToObject("canopy", DcAssets.Lit(DcAssets.Hex("#F2F3F4"), 0.4f), node, true, true);
                canopyBand.ToObject("canopy-band", DcAssets.Lit(DcAssets.Hex("#5E6670"), 0.5f, 0.3f), node, false, true);
                steel.ToObject("canopy-posts", DcAssets.Lit(DcAssets.Hex("#B9BEC4"), 0.6f, 0.7f), node, true, true);
                pumps.ToObject("pumps", DcAssets.Lit(DcAssets.Hex("#D9DCDF"), 0.5f, 0.2f), node, true, true);
                var tex = Resources.Load<Texture2D>("DC/Signs/station");
                if (tex != null && !signFace.IsEmpty) signFace.ToObject("station-sign", DcAssets.LitTex("station-sign", tex, 0.3f), node, false, true);
            }
            Debug.Log($"FacilityBuilder: 주차장 {nLots}(세운 차 {nCars}), 주유소·충전소 {nFuel}, 길가 세운 차 {nStreet}, 차 모델로 그린 것 {nFleet}");
        }

        /// <summary>
        /// 세워 둔 차를 다니는 차와 같은 모델(FleetModels: 세단·SUV·해치백·승합·1톤 트럭·택시)로(11/9 대표: "최대한 외부 리소스 … 최상급으로").
        /// 160 m 칸·재질마다 한 메시로 합친다(1,500 대도 그리기 횟수는 칸 수 × 재질 수). 모델이 없으면 false(상자 차로).
        /// </summary>
        class ParkedFleet
        {
            const float Tile = 160f;
            // 세워 둔 차 비율: 세단 46 · SUV 26 · 해치백 12 · 승합 7 · 1톤 트럭 7 · 택시 2 (이면도로·주차장)
            static readonly (int kind, float w)[] Mix = { (0, 46f), (1, 26f), (2, 12f), (4, 7f), (5, 7f), (3, 2f) };
            static readonly string[] Name = { "sedan", "suv", "hatch", "taxi", "van", "truck" };
            // (11/11) 예전에는 칸·재질마다 차의 점을 전부 복사해 한 메시로 합쳤다(동네 하나에 244 MB). 이제 차종마다 메시 두 개(차체 / 나머지)만 만들어 두고
            //   세워 둔 차마다 그 메시를 가리키는 물체 두 개를 둔다. 모양·색은 그대로.
            class Kind { public Mesh body, rest; public Material[] restMats; public bool bodyShadow, restShadow; }
            static readonly Dictionary<string, Kind> kinds = new Dictionary<string, Kind>();
            readonly Dictionary<(int tx, int ty), List<(Kind k, Material paint, Vector3 pos, Quaternion rot)>> cars = new Dictionary<(int, int), List<(Kind, Material, Vector3, Quaternion)>>();
            public int count;

            static Kind KindOf(string name)
            {
                if (kinds.TryGetValue(name, out var k) && (k == null || k.rest != null || k.body != null)) return k;
                var proto = FleetModels.Proto(name);
                if (proto == null) { kinds[name] = null; return null; }
                k = new Kind();
                var inv = proto.transform.worldToLocalMatrix;
                var bodyParts = new List<CombineInstance>();
                var byMat = new Dictionary<Material, List<CombineInstance>>();
                var order = new List<Material>();
                foreach (var mf in proto.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    var ci = new CombineInstance { mesh = mf.sharedMesh, transform = inv * mf.transform.localToWorldMatrix };
                    if (mf.gameObject.name == "fleet-0") { bodyParts.Add(ci); k.bodyShadow = mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off; continue; }
                    var mat = mr.sharedMaterial;
                    if (mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) k.restShadow = true;
                    if (!byMat.TryGetValue(mat, out var l)) { byMat[mat] = l = new List<CombineInstance>(); order.Add(mat); }
                    l.Add(ci);
                }
                if (bodyParts.Count > 0)
                {
                    k.body = new Mesh { name = "parked-body-" + name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    k.body.CombineMeshes(bodyParts.ToArray(), true, true);
                    k.body.RecalculateBounds();
                    k.body.UploadMeshData(true);
                }
                if (order.Count > 0)
                {
                    var subs = new CombineInstance[order.Count];
                    for (int i = 0; i < order.Count; i++)
                    {
                        var one = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        one.CombineMeshes(byMat[order[i]].ToArray(), true, true);
                        subs[i] = new CombineInstance { mesh = one, transform = Matrix4x4.identity };
                    }
                    k.rest = new Mesh { name = "parked-rest-" + name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    k.rest.CombineMeshes(subs, false, false);
                    k.rest.RecalculateBounds();
                    foreach (var c in subs) Dc.Kill(c.mesh);
                    k.rest.UploadMeshData(true);
                    k.restMats = order.ToArray();
                    foreach (var m in k.restMats) if (m != null && !m.enableInstancing) m.enableInstancing = true;
                }
                kinds[name] = k;
                return k;
            }

            public bool Add(System.Random rnd, V2 p, float ang, float y)
            {
                float total = 0f;
                foreach (var m in Mix) total += m.w;
                float x = (float)rnd.NextDouble() * total;
                int kind = Mix[Mix.Length - 1].kind;
                foreach (var m in Mix) { if (x < m.w) { kind = m.kind; break; } x -= m.w; }
                var k = KindOf(Name[kind]);
                if (k == null) return false;
                var paint = DcAssets.Lit(TrafficView.Pick(TrafficView.PaletteOf(kind), rnd.Next(10000)), 0.72f, 0.35f);
                int tx = Mathf.FloorToInt(p.x / Tile), ty = Mathf.FloorToInt(p.y / Tile);
                if (!cars.TryGetValue((tx, ty), out var l)) cars[(tx, ty)] = l = new List<(Kind, Material, Vector3, Quaternion)>();
                l.Add((k, paint, Space3D.W(p, y), Space3D.Rot(ang)));
                count++;
                return true;
            }

            public int Emit(Transform node)
            {
                if (count == 0) return 0;
                var root = new GameObject("parked-fleet").transform;
                root.SetParent(node, false);
                foreach (var kv in cars)
                {
                    var tile = new GameObject($"parked-{kv.Key.tx}_{kv.Key.ty}");
                    tile.transform.SetParent(root, false);
                    var rs = new List<Renderer>(kv.Value.Count * 2);
                    foreach (var (k, paint, pos, rot) in kv.Value)
                    {
                        var car = new GameObject("parked");
                        car.transform.SetParent(tile.transform, false);
                        car.transform.localPosition = pos;
                        car.transform.localRotation = rot;
                        if (k.body != null)
                        {
                            if (!paint.enableInstancing) paint.enableInstancing = true;
                            car.AddComponent<MeshFilter>().sharedMesh = k.body;
                            var r = car.AddComponent<MeshRenderer>();
                            r.sharedMaterial = paint;
                            r.shadowCastingMode = k.bodyShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                            rs.Add(r);
                        }
                        if (k.rest != null)
                        {
                            var g2 = new GameObject("rest");
                            g2.transform.SetParent(car.transform, false);
                            g2.AddComponent<MeshFilter>().sharedMesh = k.rest;
                            var r2 = g2.AddComponent<MeshRenderer>();
                            r2.sharedMaterials = k.restMats;
                            r2.shadowCastingMode = k.restShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                            rs.Add(r2);
                        }
                    }
                    // 멀리 있는 칸의 세워 둔 차는 그리지 않는다(약 350 m 밖, 화질 가볍게면 약 200 m)
                    var lg = tile.AddComponent<LODGroup>();
                    int q = Prefs.Quality;
                    lg.SetLODs(new[] { new LOD(q == 0 ? 0.043f : (q == 2 ? 0.018f : 0.025f), rs.ToArray()) });
                    lg.size = 10f;
                    lg.localReferencePoint = Space3D.W(new V2((kv.Key.tx + 0.5f) * Tile, (kv.Key.ty + 0.5f) * Tile), 0f);
                }
                return count;
            }
        }

        /// <summary>세워 둔 차(모델이 없을 때): 낮은 몸통 + 유리 지붕(길이 4.6 m, 폭 1.8 m, 조금씩 다른 크기)</summary>
        static void ParkedCar(MeshKit body, MeshKit glass, V2 p, float ang, float y, System.Random rnd)
        {
            float L = 4.3f + (float)rnd.NextDouble() * 0.6f, W = 1.78f + (float)rnd.NextDouble() * 0.1f;
            float yaw = Space3D.Yaw(ang);
            var ax = new V2(Mathf.Cos(ang), Mathf.Sin(ang));
            body.Box(Space3D.W(p, y + 0.45f), new Vector3(W, 0.72f, L), yaw);
            glass.Box(Space3D.W(p - ax * 0.15f, y + 1.06f), new Vector3(W * 0.86f, 0.5f, L * 0.52f), yaw);
            body.Box(Space3D.W(p - ax * 0.15f, y + 1.33f), new Vector3(W * 0.80f, 0.06f, L * 0.46f), yaw);
        }

        /// <summary>가격판(앞뒤 두 면): 그림 칸 0 = 주유소, 1 = 전기차 충전소</summary>
        static void SignPanel(MeshKit mk, V2 p, float y, float ang, string kind)
        {
            float w = 1.6f, hh = 2.4f;
            var n2 = new V2(Mathf.Cos(ang), Mathf.Sin(ang));
            var face = new Vector3(n2.x, 0f, -n2.y);
            var right = Vector3.Cross(face, Vector3.up).normalized;      // 판을 마주 본 사람의 오른쪽((11/11) 반대라 그림이 좌우로 뒤집혀 있었다)
            var c = Space3D.W(p, y);
            float u0 = kind == "charging" ? 0.5f : 0f, u1 = u0 + 0.5f;
            foreach (var s in new[] { 1f, -1f })
            {
                var f = face * s;
                var r2 = right * s;
                var o = c + f * 0.08f;
                mk.Quad(o - r2 * (w / 2f) - Vector3.up * (hh / 2f), o + r2 * (w / 2f) - Vector3.up * (hh / 2f), o + r2 * (w / 2f) + Vector3.up * (hh / 2f), o - r2 * (w / 2f) + Vector3.up * (hh / 2f), f,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
            }
        }
    }
}
