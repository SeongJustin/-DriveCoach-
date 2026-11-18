// 서울 보·차도 시설(위치 공개 자료가 없어 설치 기준으로 놓는다):
//   - 볼라드(차량 진입 억제용 말뚝): 횡단보도 양 끝 보도 쪽, 높이 0.9 m·지름 0.2 m·간격 약 1.5 m (교통약자법 기준 80~100 cm, 10~20 cm)
// 회색 돌 질감 기둥 + 위쪽 노란 반사 띠
//   - 중앙선 무단횡단 방지 울타리: 지역 JSON("medf")에 담긴 황색 실선 겹선 위(흰 살 판 + 노란 위 난간)
//   - 보행자용 방호울타리: 큰길(건너는 거리 12 m 넘는) 횡단보도 양옆 연석을 따라 14 m, 연석 면에서 0.3 m(지침: 250 mm 안팎), 높이 1.0 m,
//     기둥 2 m 마다 + 살 판(알파 자르기). 어린이보호구역 안은 노란색(서울 어린이보호구역 울타리)
//   - 점형블록(시각장애인 유도블록, 노랑): 횡단보도 진입부 연석에서 0.3 m 띄워 횡단보도 폭만큼 깊이 0.6 m(블록 2줄)
//     (교통약자의 이동편의 증진법 시행규칙 별표1·서울시 보도 설계 매뉴얼). 볼라드는 그 뒤 0.3 m(“볼라드 0.3 m 전면에 점형블록”)
// 보도 위가 아니거나 아스팔트에 닿는 자리는 건너뛴다. 종류·색별로 한 메시.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class StreetFurniture
    {
        const float BollardH = 0.9f, BollardR = 0.1f, BollardGap = 1.5f;
        const float FenceH = 1.0f, FenceOff = 0.3f, FenceLen = 14f, PostGap = 2.0f;

        static readonly Dictionary<string, Material> tactileMats = new Dictionary<string, Material>();

        /// <summary>
        /// 점형블록 재질. 한국: 예전 그대로(노란 사진 질감 tactile_dots). 다른 나라: 같은 재질(점 요철 법선 지도)을 복사해 바탕 그림만 흰색으로 바꾸고 나라 색을 곱한다
        /// (사진 질감이 노란색이라 색 곱으로는 흰·회색이 안 나온다).
        /// </summary>
        static Material TactileMat(CountryStyle cs)
        {
            var kr = DcAssets.Pbr("tactile_dots", DcAssets.Hex("#E9B92C"), Color.white, 0.5f);
            if (cs.IsKorea) return kr;
            if (tactileMats.TryGetValue(cs.code, out var m) && m != null) return m;
            m = new Material(kr) { name = "tactile-" + cs.code };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cs.tactile);
            if (m.HasProperty("_Color")) m.SetColor("_Color", cs.tactile);
            tactileMats[cs.code] = m;
            return m;
        }

        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            var s = CityBuilder.Current;
            if (d == null || s == null || (d.crosswalks.Count == 0 && d.medianFences.Count == 0)) return;
            float y = h + CityBuilder.WalkH;
            var stone = new MeshKit();
            var band = new MeshKit();
            var posts = new MeshKit();
            var postsY = new MeshKit();
            var panel = new MeshKit();
            var panelY = new MeshKit();
            var tactile = new MeshKit();
            var curbIdx = new CurbIndex(s.curbs);
            int nB = 0, nF = 0, nT = 0;
            foreach (var (c, n, L, W) in d.crosswalks)
            {
                if (L < 6f || W < 2f) continue;
                var t = new V2(-n.y, n.x);
                foreach (var side in new[] { -1f, 1f })
                {
                    // 횡단보도 끝에서 바깥으로 보도가 나오는 곳
                    var e = c + n * (side * L / 2f);
                    V2? walk = null;
                    for (float k = -0.5f; k <= 3f; k += 0.25f)
                    {
                        var q = e + n * (side * k);
                        if (s.Sidewalk(q) && !s.TouchesAsphalt(q, 0.1f)) { walk = q; break; }
                    }
                    if (walk == null) continue;
                    var w0 = walk.Value;   // 연석에서 약 0.1~0.35 m 보도 쪽
                    // 점형블록: 0.3 m 칸마다(네 귀가 보도 위인 칸만), 연석에서 약 0.3 m ~ 0.9 m
                    int cells = Math.Max(1, (int)Math.Floor(W / 0.3f));
                    for (int ci = 0; ci < cells; ci++)
                    {
                        float a0 = -cells * 0.15f + ci * 0.3f, a1 = a0 + 0.3f;
                        for (int row = 0; row < 2; row++)
                        {
                            float d0 = 0.15f + row * 0.3f, d1 = d0 + 0.3f;
                            var q0 = w0 + n * (side * d0) + t * a0; var q1 = w0 + n * (side * d0) + t * a1;
                            var q2 = w0 + n * (side * d1) + t * a1; var q3 = w0 + n * (side * d1) + t * a0;
                            bool ok = true;
                            foreach (var q in new[] { q0, q1, q2, q3 }) if (!s.Sidewalk(q) || s.TouchesAsphalt(q, 0.05f)) { ok = false; break; }
                            if (!ok) continue;
                            var W0 = Space3D.W(q0, y + 0.006f); var W1 = Space3D.W(q1, y + 0.006f);
                            var W2 = Space3D.W(q2, y + 0.006f); var W3 = Space3D.W(q3, y + 0.006f);
                            tactile.Quad(W0, W1, W2, W3, Vector3.up, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                            nT++;
                        }
                    }
                    // 볼라드: 점형블록 뒤 0.3 m(연석에서 약 1.2 m), 횡단보도 폭을 따라 1.5 m 마다
                    int nb = Math.Max(2, (int)Math.Floor((W - 0.8f) / BollardGap) + 1);
                    for (int i = 0; i < nb; i++)
                    {
                        float off = nb == 1 ? 0f : -(W - 0.8f) / 2f + i * (W - 0.8f) / (nb - 1);
                        var p = w0 + n * (side * 1.05f) + t * off;
                        if (!s.Sidewalk(p) || s.TouchesAsphalt(p, 0.2f)) continue;
                        Bollard(stone, band, p, y);
                        nB++;
                    }
                    // 울타리: 큰길만, 횡단보도 입구(폭 + 1.2 m)를 비우고 양쪽으로 연석을 따라
                    if (L < 12f) continue;
                    bool school = d.SchoolZoneAt(c) >= 0;
                    foreach (var dir in new[] { -1f, 1f })
                    {
                        var start = w0 + t * (dir * (W / 2f + 0.6f));
                        var path = curbIdx.Follow(start, t * dir, FenceLen, s);
                        if (path == null || path.Count < 2) continue;
                        Fence(school ? postsY : posts, school ? panelY : panel, path, y);
                        nF++;
                    }
                }
            }
            // 중앙선 무단횡단 방지 울타리(흰 살 판 + 노란 위 난간, 높이 1 m), 황색 실선 겹선 위
            var medPosts = new MeshKit();
            var medPanel = new MeshKit();
            var medTop = new MeshKit();
            int nM = 0;
            foreach (var line in d.medianFences)
            {
                var path = new List<V2>(line);
                Fence(medPosts, medPanel, path, h + 0.02f);
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    var a = path[i]; var b = path[i + 1];
                    float len = a.DistanceTo(b);
                    if (len < 0.05f) continue;
                    var dv = (b - a) / len;
                    medTop.Box(Space3D.W((a + b) * 0.5f, h + 0.02f + FenceH + 0.02f), new Vector3(0.09f, 0.08f, len + 0.02f), Space3D.Yaw(Mathf.Atan2(dv.y, dv.x)));
                }
                nM++;
            }
            if (!medPosts.IsEmpty) medPosts.ToObject("median-fence-posts", DcAssets.Lit(DcAssets.Hex("#E9EBEE"), 0.5f, 0.2f), node, true);
            if (!medPanel.IsEmpty) medPanel.ToObject("median-fence-panels", DcAssets.Cutout("DC/CC0/fence_bars_cutout", new Color(1.1f, 1.1f, 1.1f), DcAssets.Hex("#E9EBEE")), node, true);
            if (!medTop.IsEmpty) medTop.ToObject("median-fence-top", DcAssets.Lit(DcAssets.Hex("#F2C200"), 0.6f, 0.1f), node, true);
            var metal = DcAssets.Lit(DcAssets.Hex("#B8BCC2"), 0.55f, 0.6f);
            var yellow = DcAssets.Lit(DcAssets.Hex("#E8B400"), 0.45f, 0.2f);
            if (!tactile.IsEmpty) tactile.ToObject("tactile-paving", TactileMat(CountryStyle.Current), node);   // 색은 나라 표(한국: 노란 사진 질감 그대로, 독일 흰·밝은 회색)
            if (!stone.IsEmpty) stone.ToObject("bollards", DcAssets.Pbr("brushed_concrete", DcAssets.Hex("#9A9DA2"), new Color(0.82f, 0.83f, 0.85f), 0.5f), node, true);
            if (!band.IsEmpty) band.ToObject("bollard-bands", DcAssets.Lit(DcAssets.Hex("#F2C200"), 0.7f, 0.1f), node);
            if (!posts.IsEmpty) posts.ToObject("fence-posts", metal, node, true);
            if (!postsY.IsEmpty) postsY.ToObject("fence-posts-school", yellow, node, true);
            if (!panel.IsEmpty) panel.ToObject("fence-panels", DcAssets.Cutout("DC/CC0/fence_bars_cutout", Color.white, DcAssets.Hex("#B8BCC2")), node, true);
            if (!panelY.IsEmpty) panelY.ToObject("fence-panels-school", DcAssets.Cutout("DC/CC0/fence_bars_cutout", new Color(1.05f, 0.82f, 0.1f), DcAssets.Hex("#E8B400")), node, true);
            Debug.Log($"StreetFurniture: 점형블록 {nT}장, 볼라드 {nB}, 보행자 울타리 {nF}구간, 중앙선 울타리 {nM}구간");
        }

        static void Bollard(MeshKit stone, MeshKit band, V2 p, float y)
        {
            var ring = new V2[12];
            for (int i = 0; i < 12; i++) { float a = i * Mathf.PI * 2f / 12f; ring[i] = p + new V2(Mathf.Cos(a), Mathf.Sin(a)) * BollardR; }
            stone.Prism(ring, y, y + BollardH - 0.12f);
            var ring2 = new V2[12];
            for (int i = 0; i < 12; i++) ring2[i] = p + (ring[i] - p) * 1.04f;
            band.Prism(ring2, y + BollardH - 0.30f, y + BollardH - 0.22f);    // 노란 반사 띠
            var cap = new V2[12];
            for (int i = 0; i < 12; i++) cap[i] = p + (ring[i] - p) * 0.75f;
            stone.Prism(cap, y + BollardH - 0.12f, y + BollardH);              // 둥근 머리(좁아지는 윗단)
        }

        /// <summary>점 목록을 따라 울타리: 기둥(2 m 마다) + 위 난간 + 살 판(양면 알파 자르기)</summary>
        static void Fence(MeshKit posts, MeshKit panel, List<V2> path, float y)
        {
            float along = 0f, nextPost = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = path[i]; var b = path[i + 1];
                float len = a.DistanceTo(b);
                if (len < 0.05f) continue;
                var dirv = (b - a) / len;
                float yaw = Space3D.Yaw(Mathf.Atan2(dirv.y, dirv.x));
                while (nextPost <= along + len + 1e-3f)
                {
                    var pp = a + dirv * (nextPost - along);
                    posts.Box(Space3D.W(pp, y + FenceH / 2f), new Vector3(0.07f, FenceH, 0.07f), yaw);
                    nextPost += PostGap;
                }
                var mid = (a + b) * 0.5f;
                posts.Box(Space3D.W(mid, y + FenceH - 0.02f), new Vector3(0.05f, 0.05f, len + 0.02f), yaw);   // 위 난간
                var wa = Space3D.W(a, 0f); var wb = Space3D.W(b, 0f);
                var outv = Vector3.Cross(Vector3.up, wb - wa).normalized;
                float u0 = along, u1 = along + len;
                var p0 = new Vector3(wa.x, y + 0.12f, wa.z); var p1 = new Vector3(wb.x, y + 0.12f, wb.z);
                var p2 = new Vector3(wb.x, y + FenceH - 0.05f, wb.z); var p3 = new Vector3(wa.x, y + FenceH - 0.05f, wa.z);
                panel.Quad(p0, p1, p2, p3, outv, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
                along += len;
            }
        }

        /// <summary>연석 선 찾기: 가까운 연석을 따라 len m(연석이 35° 넘게 꺾이거나 보도를 벗어나면 멈춤), 보도 쪽으로 FenceOff 띄운 점들</summary>
        class CurbIndex
        {
            const float G = 20f;
            readonly List<V2[]> curbs;
            readonly Dictionary<long, List<(int c, int i)>> grid = new Dictionary<long, List<(int, int)>>();

            public CurbIndex(List<V2[]> curbs)
            {
                this.curbs = curbs;
                for (int c = 0; c < curbs.Count; c++)
                    for (int i = 0; i + 1 < curbs[c].Length; i++)
                    {
                        var m = (curbs[c][i] + curbs[c][i + 1]) * 0.5f;
                        long k = Key(m);
                        if (!grid.TryGetValue(k, out var l)) { l = new List<(int, int)>(); grid[k] = l; }
                        l.Add((c, i));
                    }
            }

            static long Key(V2 p) => ((long)Mathf.FloorToInt(p.x / G) << 32) ^ (uint)Mathf.FloorToInt(p.y / G);

            public List<V2> Follow(V2 start, V2 dir, float len, RoadSurface s)
            {
                int bc = -1, bi = -1; float bd = 2.5f, bt = 0f;
                int gx = Mathf.FloorToInt(start.x / G), gy = Mathf.FloorToInt(start.y / G);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        long k = ((long)(gx + dx) << 32) ^ (uint)(gy + dy);
                        if (!grid.TryGetValue(k, out var l)) continue;
                        foreach (var (c, i) in l)
                        {
                            var a = curbs[c][i]; var b = curbs[c][i + 1];
                            var ab = b - a; float L2 = ab.x * ab.x + ab.y * ab.y;
                            if (L2 < 1e-6f) continue;
                            float tt = Mathf.Clamp01(((start.x - a.x) * ab.x + (start.y - a.y) * ab.y) / L2);
                            var q = a + ab * tt;
                            float dd = q.DistanceTo(start);
                            if (dd < bd) { bd = dd; bc = c; bi = i; bt = tt; }
                        }
                    }
                if (bc < 0) return null;
                var pts = curbs[bc];
                var seg = pts[bi + 1] - pts[bi];
                int step = (seg.x * dir.x + seg.y * dir.y) >= 0f ? 1 : -1;
                var cur = pts[bi] + seg * bt;
                var outp = new List<V2>();
                float walked = 0f;
                V2 prevDir = seg.Normalized * step;
                int idx = step > 0 ? bi + 1 : bi;
                // 보도 쪽(연석에서 FenceOff)
                Func<V2, V2, V2> off = (p, dv) =>
                {
                    var nn = new V2(-dv.y, dv.x);
                    var a = p + nn * FenceOff; var b = p - nn * FenceOff;
                    return s.Sidewalk(a) && !s.TouchesAsphalt(a, 0.12f) ? a : (s.Sidewalk(b) && !s.TouchesAsphalt(b, 0.12f) ? b : new V2(float.NaN, float.NaN));
                };
                var o0 = off(cur, prevDir);
                if (float.IsNaN(o0.x)) return null;
                outp.Add(o0);
                while (walked < len && idx >= 0 && idx < pts.Length)
                {
                    var nxt = pts[idx];
                    var dv = nxt - cur;
                    float sl = dv.Length;
                    if (sl < 1e-4f) { idx += step; continue; }
                    dv = dv / sl;
                    if (dv.x * prevDir.x + dv.y * prevDir.y < Mathf.Cos(35f * Mathf.Deg2Rad)) break;   // 모퉁이에서 멈춤
                    float take = Mathf.Min(sl, len - walked);
                    var end = cur + dv * take;
                    var o = off(end, dv);
                    if (float.IsNaN(o.x)) break;
                    outp.Add(o);
                    walked += take;
                    cur = end;
                    prevDir = dv;
                    if (take < sl) break;
                    idx += step;
                }
                return outp.Count >= 2 && walked > 3f ? outp : null;
            }
        }
    }
}
