// CityBuilder 의 건물 부분(CityBuilder.cs 에서 나눈 파일). 전체 설명은 CityBuilder.cs 맨 위.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static partial class CityBuilder
    {
        // ───────────────────────── 건물 ─────────────────────────

        /// <summary>재질 이름 × 160 m 칸 마다 한 메시(멀거나 화면 밖 칸은 그리지 않는다)</summary>
        class Tiles
        {
            const float Size = 160f;
            public readonly Dictionary<(string mat, int tx, int ty), MeshKit> kits = new Dictionary<(string, int, int), MeshKit>();
            public MeshKit Get(string mat, V2 p, float uvScale = 4f)
            {
                var k = (mat, (int)Math.Floor(p.x / Size), (int)Math.Floor(p.y / Size));
                if (!kits.TryGetValue(k, out var mk)) kits[k] = mk = new MeshKit { uvScale = uvScale };
                return mk;
            }
            /// <summary>칸(tx, ty)의 모든 재질을 내보내고 목록에서 뺀다(건물: 그 칸의 마지막 건물을 지은 바로 뒤)</summary>
            public void EmitTile(int tx, int ty, Transform node, Func<string, Material> mat, Func<string, bool> shadows, bool nightWindows)
            {
                var keys = new List<(string, int, int)>();
                foreach (var k in kits.Keys) if (k.tx == tx && k.ty == ty) keys.Add(k);
                foreach (var k in keys)
                {
                    var mk = kits[k];
                    kits.Remove(k);
                    if (mk.IsEmpty) continue;
                    var m = mat(k.Item1);
                    if (m == null) continue;
                    MeshKit.KeepReadable = nightWindows && NightWindowsOn();
                    GameObject go;
                    try { go = mk.ToObject(k.Item1 + "@" + k.Item2 + "," + k.Item3, m, node, shadows(k.Item1)); }
                    finally { MeshKit.KeepReadable = false; }
                    if (nightWindows) NightWindows(go, k.Item1, node);
                    mk.Release();
                }
            }

            public static (int, int) TileOf(V2 p) => ((int)Math.Floor(p.x / Size), (int)Math.Floor(p.y / Size));

            /// <summary>only 가 있으면 그 재질 칸만 내보내고 목록에서 뺀다(노면은 재질마다 바로 내보내 임시 점 목록이 한꺼번에 쌓이지 않게)</summary>
            public void Emit(Transform node, Func<string, Material> mat, Func<string, bool> shadows, bool nightWindows = false, string only = null)
            {
                List<(string, int, int)> done = only != null ? new List<(string, int, int)>() : null;
                foreach (var kv in kits)
                {
                    if (only != null) { if (kv.Key.mat != only) continue; done.Add(kv.Key); }
                    if (kv.Value.IsEmpty) continue;
                    var m = mat(kv.Key.mat);
                    if (m == null) continue;
                    // 밤 창 불빛은 외벽 메시를 다시 읽어 사본을 뜬다 → 그때만 사본 내리기를 끝으로 미룬다
                    MeshKit.KeepReadable = nightWindows && NightWindowsOn();
                    GameObject go;
                    try { go = kv.Value.ToObject(kv.Key.mat + "@" + kv.Key.tx + "," + kv.Key.ty, m, node, shadows(kv.Key.mat)); }
                    finally { MeshKit.KeepReadable = false; }
                    if (nightWindows) NightWindows(go, kv.Key.mat, node);
                    kv.Value.Release();   // 다 지은 칸의 점 목록은 바로 놓는다
                }
                if (done != null) foreach (var k in done) kits.Remove(k);
            }
        }

        /// <summary>
        /// 밤 창 불빛: 사진 외벽 메시를 바깥으로 4 cm 띄운 사본에 facade_*_night 그림(켜진 창만 불투명한
        /// 따뜻한 빛, 나머지 투명)을 조명 없이 덮는다. 밤(NightLights)에만 켠다. 그림은 
        /// </summary>
        static void NightWindows(GameObject wall, string key, Transform node)
        {
            // 밤 불빛이 켜지는 설정(밤, 해질녘 궂은 날, 안개 — SceneRig 과 같은 조건)일 때만 만든다. 예전에는 낮에도 외벽 메시를
            //   통째로 한 벌 더 만들어 꺼 두었다(여는 시간·메모리만 씀). 시간·날씨는 판을 여는 동안 바뀌지 않는다(설정은 목록 화면에서만).
            if (!NightWindowsOn()) return;
            string kind = key.StartsWith("apt") ? "apt" : key.StartsWith("other") ? "other" : key;
            if (kind != "apt" && kind != "office" && kind != "house" && kind != "house2" && kind != "other" && kind != "public" && kind != "low") return;
            var tex = Resources.Load<Texture2D>("DC/CC0/facade_" + kind + "_night");
            var src = wall != null ? wall.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (tex == null || src == null) return;
            if (!nightMats.TryGetValue(kind, out var m) || m == null)
            {
                m = new Material(DcAssets.Unlit(new Color(1f, 1f, 1f, 0.5f)));
                DcAssets.Tint(m, new Color(1.25f, 1.15f, 1.05f, 1f));     // HDR 로 조금 밝게(번짐 효과가 있으면 은은히 번진다)
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                m.renderQueue = 3000;
                nightMats[kind] = m;
            }
            var v = src.vertices;
            var nrm = src.normals;
            if (nrm == null || nrm.Length != v.Length) return;
            for (int i = 0; i < v.Length; i++) v[i] += nrm[i] * 0.04f;
            var mesh = new Mesh { name = wall.name + "-night", indexFormat = src.indexFormat };
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.uv = src.uv;
            mesh.triangles = src.triangles;
            mesh.RecalculateBounds();
            var go = new GameObject(wall.name + "-night");
            go.transform.SetParent(node, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            NightLights.Add(go);
        }

        static readonly Dictionary<string, Material> nightMats = new Dictionary<string, Material>();

        /// <summary>밤 불빛이 켜지는 설정인가(밤, 해질녘 궂은 날, 안개 — SceneRig 과 같은 조건)</summary>
        static bool NightWindowsOn() { int tm = Prefs.TimeOfDay, wx = Prefs.Weather; return tm == 3 || (tm == 2 && wx != 0) || wx == 3; }

        /// <summary>사진 외벽(4칸 × 4층 한 장) 재질 이름 → 재질</summary>
        static Material BuildingMat(string key)
        {
            switch (key)
            {
                case "apt-w": return DcAssets.Facade("apt", Color.white);
                case "apt-c": return DcAssets.Facade("apt", new Color(1f, 0.95f, 0.86f));
                case "office": return DcAssets.Facade("office", Color.white);
                case "house": return DcAssets.Facade("house", Color.white);
                case "house2": return DcAssets.Facade("house2", Color.white);
                case "other-a": return DcAssets.Facade("other", Color.white);
                case "other-b": return DcAssets.Facade("other", new Color(1f, 0.92f, 0.82f));
                case "public": return DcAssets.Facade("public", Color.white);
                case "low": return DcAssets.Facade("low", Color.white);
                case "roof-g": return DcAssets.Pbr("concrete_floor_worn_001", DcAssets.Hex("#7E9C84"), new Color(0.55f, 0.78f, 0.6f), 0.7f);
                case "roof-s": return DcAssets.Pbr("concrete_floor_worn_001", DcAssets.Hex("#9C9C98"), new Color(0.95f, 0.95f, 0.93f), 0.6f);
                case "parapet": return DcAssets.Pbr("brushed_concrete", DcAssets.Hex("#D8D4CC"), new Color(1.05f, 1.03f, 1f), 0.5f);
                case "roofbox": return DcAssets.Pbr("brushed_concrete", DcAssets.Hex("#CFCBC4"), new Color(0.98f, 0.97f, 0.95f), 0.5f);
                case "tank": return DcAssets.Lit(DcAssets.Hex("#7FA7C9"), 0.55f, 0.1f);
            }
            if (key.StartsWith("shop-"))
            {
                var cs = CountryStyle.Current;
                if (cs.IsKorea || string.IsNullOrEmpty(cs.shopTex)) return DcAssets.Pbr("shop_" + key.Substring(5), DcAssets.Hex("#3B3F45"), Color.white);
                return ShopMat(cs.shopTex, key.Substring(5));
            }
            return null;
        }

        static readonly Dictionary<string, Material> shopMats = new Dictionary<string, Material>();

        /// <summary>
        /// 다른 나라 상가 간판: 한국 상가 재질(사진·법선·매끈함)을 복사해 바탕 그림·매끈함만 그 나라 것으로(DC/CC0/&lt;prefix&gt;&lt;n&gt;_albedo·_metalsmooth —
        /// 간판 띠 글자만 다르고 칸 배치는 같아 UV 그대로). 그림이 없으면 한국 재질.
        /// </summary>
        static Material ShopMat(string prefix, string n)
        {
            string k = prefix + n;
            if (shopMats.TryGetValue(k, out var m) && m != null) return m;
            var kr = DcAssets.Pbr("shop_" + n, DcAssets.Hex("#3B3F45"), Color.white);
            var al = Resources.Load<Texture2D>("DC/CC0/" + k + "_albedo");
            if (kr == null || al == null) return kr;
            m = new Material(kr) { name = k };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", al);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", al);
            var ms = Resources.Load<Texture2D>("DC/CC0/" + k + "_metalsmooth");
            if (ms != null && m.HasProperty("_MetallicGlossMap")) m.SetTexture("_MetallicGlossMap", ms);
            shopMats[k] = m;
            return m;
        }

        static string WallKey(string kind, System.Random rnd)
        {
            switch (kind)
            {
                case "apt": return rnd.NextDouble() < 0.5 ? "apt-w" : "apt-c";
                case "office": return "office";
                case "house": return rnd.NextDouble() < 0.5 ? "house" : "house2";
                case "public": return "public";
                case "low": return "low";
                default: return rnd.NextDouble() < 0.55 ? "other-a" : (rnd.NextDouble() < 0.5 ? "other-b" : "house2");
            }
        }

        static void BuildBuildings(RoadDef r, float h, Transform node)
        {
            if (r.buildings.Count == 0) return;
            if (DcAssets.Facade("apt", Color.white) == null) { BuildBuildingsCode(r, h, node); return; }
            var tiles = new Tiles();
            var rnd = new System.Random(11);
            // 칸마다 그 칸의 마지막 건물을 지은 바로 뒤에 내보낸다(예전: 모든 칸의 점 목록을 다 모은 뒤 한꺼번에 → 스크립트 힙 +45 MB).
            //   건물 순서·난수 순서는 그대로라 모양·무늬는 같다.
            var lastOf = new Dictionary<(int, int), int>();
            for (int i = 0; i < r.buildings.Count; i++) if (r.buildings[i].poly.Length >= 3) lastOf[Tiles.TileOf(r.buildings[i].center)] = i;
            int bi = -1;
            foreach (var b in r.buildings)
            {
                bi++;
                var poly = b.poly;
                int n = poly.Length;
                if (n < 3) continue;
                float area = 0f;
                for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
                bool ccw = area > 0f;
                area = Math.Abs(area) / 2f;
                string kind = Array.IndexOf(Kinds, b.kind) >= 0 ? b.kind : "other";
                string wall = WallKey(kind, rnd);
                float top = h + b.height;
                float bay = CityTextures.Bay(kind == "shop" ? "other" : kind);
                bool shopFloor = kind == "shop" || (kind == "other" && b.levels >= 2 && b.levels <= 6 && rnd.NextDouble() < 0.45);
                float baseH = shopFloor ? 4.0f : 0f;
                string shop = "shop-" + rnd.Next(CityTextures.ShopColors);
                // 칸·층 시작 위치를 건물마다 달리(같은 무늬가 나란히 반복되지 않게)
                int bx = rnd.Next(4), fy = rnd.Next(4), sx = rnd.Next(4);
                float along = 0f;
                var wallMk = tiles.Get(wall, b.center);
                var shopMk = tiles.Get(shop, b.center);
                bool parapet = kind != "low" && b.levels >= 2;
                var parMk = tiles.Get("parapet", b.center, 3f);
                for (int i = 0; i < n; i++)
                {
                    var a = poly[i]; var c = poly[(i + 1) % n];
                    float len = a.DistanceTo(c);
                    if (len < 0.2f) continue;
                    var wa = Space3D.W(a, 0f); var wc = Space3D.W(c, 0f);
                    var edge = wc - wa;
                    var outward = Vector3.Cross(Vector3.up, edge).normalized * (ccw ? -1f : 1f);
                    if (shopFloor)
                    {
                        // 1층 상가: 한 칸 4 m × 4 m, 한 장에 4칸
                        float s0 = (along / 4f + sx) / 4f, s1 = ((along + len) / 4f + sx) / 4f;
                        shopMk.Quad(Lift(wa, h), Lift(wc, h), Lift(wc, h + baseH), Lift(wa, h + baseH), outward,
                            new Vector2(s0, 0f), new Vector2(s1, 0f), new Vector2(s1, 1f), new Vector2(s0, 1f));
                    }
                    if (top > h + baseH + 0.1f)
                    {
                        float u0 = (along / bay + bx) / 4f, u1 = ((along + len) / bay + bx) / 4f;
                        float v0 = fy / 4f, v1 = v0 + (b.height - baseH) / FloorH / 4f;
                        wallMk.Quad(Lift(wa, h + baseH), Lift(wc, h + baseH), Lift(wc, top), Lift(wa, top), outward,
                            new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                    }
                    if (parapet)
                    {
                        // 옥상 난간 0.9 m: 바깥·안쪽 면(두께 없음)
                        float pu0 = along / 3f, pu1 = (along + len) / 3f;
                        parMk.Quad(Lift(wa, top), Lift(wc, top), Lift(wc, top + 0.9f), Lift(wa, top + 0.9f), outward,
                            new Vector2(pu0, 0f), new Vector2(pu1, 0f), new Vector2(pu1, 0.3f), new Vector2(pu0, 0.3f));
                        parMk.Quad(Lift(wa, top), Lift(wc, top), Lift(wc, top + 0.9f), Lift(wa, top + 0.9f), -outward,
                            new Vector2(pu0, 0f), new Vector2(pu1, 0f), new Vector2(pu1, 0.3f), new Vector2(pu0, 0.3f));
                    }
                    along += len;
                }
                // 지붕: 한국 동네 옥상은 초록 방수 페인트가 많다
                bool green = kind != "office" && kind != "low" && kind != "public" && rnd.NextDouble() < CountryStyle.Current.greenRoof;   // 비율은 나라 표(한국 0.6, 독일 0 — 난수 순서는 그대로)
                tiles.Get(green ? "roof-g" : "roof-s", b.center, 3f).PolygonEar(poly, top);
                // 옥상 계단실·물탱크(3층 이상, 넓은 건물)
                if (b.levels >= 3 && area > 140f && Geom.PointInPolygon(b.center, poly) && Geom.DistPointPoly(b.center, poly, out _) > 3f)
                {
                    float yaw = LongestEdgeYaw(poly);
                    var boxMk = tiles.Get("roofbox", b.center);
                    boxMk.Box(Space3D.W(b.center, top + 1.4f), new Vector3(3.2f, 2.8f, 3.8f), yaw);
                    boxMk.Box(Space3D.W(b.center, top + 2.85f), new Vector3(3.6f, 0.15f, 4.2f), yaw);   // 지붕 처마
                    if (rnd.NextDouble() < 0.45)   // 계단실 위 물탱크
                        tiles.Get("tank", b.center).Box(Space3D.W(b.center, top + 3.6f), new Vector3(2.0f, 1.5f, 2.0f), yaw + 45f);
                }
                var tk = Tiles.TileOf(b.center);
                if (lastOf.TryGetValue(tk, out int li) && li == bi) tiles.EmitTile(tk.Item1, tk.Item2, node, BuildingMat, k => !k.StartsWith("roof-"), true);
            }
            Dc.Lap("bld-loop");
            tiles.Emit(node, BuildingMat, k => !k.StartsWith("roof-"), true);
            Dc.Lap("bld-emit");
        }

        static float LongestEdgeYaw(V2[] poly)
        {
            float best = 0f, yaw = 0f;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var c = poly[(i + 1) % poly.Length];
                float l = a.DistanceTo(c);
                if (l > best) { best = l; var d = c - a; yaw = Space3D.Yaw((float)Math.Atan2(d.y, d.x)); }
            }
            return yaw;
        }

        /// <summary>사진 질감이 없을 때(예전): 코드로 그린 외벽 무늬</summary>
        static void BuildBuildingsCode(RoadDef r, float h, Transform node)
        {
            if (r.buildings.Count == 0) return;
            var walls = new Dictionary<string, MeshKit>();
            var shops = new MeshKit[CityTextures.ShopColors];
            for (int i = 0; i < shops.Length; i++) shops[i] = new MeshKit();
            var roofs = new MeshKit { uvScale = 3f };
            foreach (var k in Kinds) walls[k] = new MeshKit();
            var rnd = new System.Random(11);
            foreach (var b in r.buildings)
            {
                var poly = b.poly;
                int n = poly.Length;
                if (n < 3) continue;
                float area = 0f;
                for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
                bool ccw = area > 0f;
                string kind = Array.IndexOf(Kinds, b.kind) >= 0 ? b.kind : "other";
                float top = h + b.height;
                float bay = CityTextures.Bay(kind);
                bool shopFloor = kind == "shop" || (kind == "other" && b.levels >= 2 && b.levels <= 6 && rnd.NextDouble() < 0.45);
                float baseH = shopFloor ? 4.0f : 0f;
                var shopMk = shops[rnd.Next(shops.Length)];
                float along = (float)rnd.NextDouble() * bay;   // 건물마다 창 위치를 조금씩 다르게
                for (int i = 0; i < n; i++)
                {
                    var a = poly[i]; var c = poly[(i + 1) % n];
                    float len = a.DistanceTo(c);
                    if (len < 0.2f) continue;
                    var wa = Space3D.W(a, 0f); var wc = Space3D.W(c, 0f);
                    var edge = wc - wa;
                    var outward = Vector3.Cross(Vector3.up, edge).normalized * (ccw ? -1f : 1f);
                    float u0 = along / bay, u1 = (along + len) / bay;
                    if (shopFloor)
                    {
                        // 1층: 상가 유리 + 간판 띠(한 판이 한 층·한 칸)
                        shopMk.Quad(Lift(wa, h), Lift(wc, h), Lift(wc, h + baseH), Lift(wa, h + baseH), outward,
                            new Vector2(u0 * bay / 4f, 0f), new Vector2(u1 * bay / 4f, 0f), new Vector2(u1 * bay / 4f, 1f), new Vector2(u0 * bay / 4f, 1f));
                    }
                    if (top > h + baseH + 0.1f)
                    {
                        float v0 = 0f, v1 = (b.height - baseH) / FloorH;
                        walls[kind].Quad(Lift(wa, h + baseH), Lift(wc, h + baseH), Lift(wc, top), Lift(wa, top), outward,
                            new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                    }
                    along += len;
                }
                roofs.PolygonEar(poly, top);
            }
            foreach (var k in Kinds)
                if (!walls[k].IsEmpty) walls[k].ToObject("buildings-" + k, DcAssets.LitTex("facade-" + k, CityTextures.Facade(k), k == "office" ? 0.55f : 0.15f, k == "office" ? 0.2f : 0f), node, true);
            for (int i = 0; i < shops.Length; i++)
                if (!shops[i].IsEmpty) shops[i].ToObject("shopfronts-" + i, DcAssets.LitTex("shop-" + i, CityTextures.Shopfront(i), 0.35f), node, true);
            roofs.ToObject("roofs", DcAssets.LitTex("roof", CityTextures.Roof(), 0.1f), node, false);
        }

        static Vector3 Lift(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        /// <summary>부호 있는 넓이(V2 좌표: x 동쪽·y 남쪽)</summary>
        static float PolyArea(V2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++) { var q = p[i]; var r = p[(i + 1) % p.Length]; a += q.x * r.y - r.x * q.y; }
            return a / 2f;
        }
    }
}
