// 담당: S (11/8 밤, 대표: "고가도로나 지하도로 … 최대한 다 반영해봐")
// 고가도로: 길 모양 그대로 높이를 따라 오르내리는 상판(아스팔트),
//   양옆 콘크리트 방호벽(높이 1 m), 상판 아래 두께 1.2 m 보, 높이 2.5 m 넘는 곳은 25 m 마다 기둥(교각), 그보다 낮은 경사로는
//   상판 가장자리에서 땅까지 옹벽(흙을 쌓은 진입로처럼). 상판 위 칠: 차로 경계 흰 점선, 양방향이면 가운데 황색 복선, 바깥 흰 실선.
// 땅의 노면(make_surface)은 고가 길 바닥 자리를 비워 둔다(그 밑을 지나는 길만 남긴다). 지하차도(z < 0)는 땅을 파야 해서 다음 단계 — 지금은 땅 높이.
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class GradeBuilder
    {
        const float MinZ = 0.25f;

        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null) return;
            var deck = new MeshKit { uvScale = 5f };
            var concrete = new MeshKit { uvScale = 3f };
            var white = new MeshKit();
            var yellow = new MeshKit();
            int n = 0;
            var decks = new List<(RegionRoad rd, float wl, float wr)>();
            ground.Clear();
            // 땅 길(높이 없음) 조각과 반폭: 교각이 그 위(차로)에 서지 않게(고가 검토: 바우뫼로 차로 한가운데 교각)
            float gx0 = float.MaxValue, gy0 = float.MaxValue, gx1 = float.MinValue, gy1 = float.MinValue;
            foreach (var rd in d.roads)
                if (rd.z != null) foreach (var p in rd.pts) { gx0 = Mathf.Min(gx0, p.x); gy0 = Mathf.Min(gy0, p.y); gx1 = Mathf.Max(gx1, p.x); gy1 = Mathf.Max(gy1, p.y); }
            foreach (var rd in d.roads)
            {
                if (rd.pts.Length < 2) continue;
                bool flat = rd.z == null;
                if (!flat) { flat = true; foreach (var z in rd.z) if (Mathf.Abs(z) > 0.3f) { flat = false; break; } }
                if (!flat) continue;
                float half = rd.wl > 0f || rd.wr > 0f ? Mathf.Max(rd.wl, rd.wr) : Mathf.Max(3f, rd.lanes * 1.65f);
                for (int i = 0; i + 1 < rd.pts.Length; i++)
                {
                    var a = rd.pts[i]; var b = rd.pts[i + 1];
                    if (Mathf.Max(a.x, b.x) < gx0 - 40f || Mathf.Min(a.x, b.x) > gx1 + 40f || Mathf.Max(a.y, b.y) < gy0 - 40f || Mathf.Min(a.y, b.y) > gy1 + 40f) continue;
                    ground.Add((a, b, half));
                }
            }
            foreach (var rd in d.roads)
            {
                if (rd.z == null || rd.pts.Length < 2) continue;
                bool any = false;
                foreach (var z in rd.z) if (z > MinZ) { any = true; break; }
                if (!any) continue;
                n++;
                float wl = rd.wl > 0f ? rd.wl : Mathf.Max(3f, rd.lanes * 3.2f / 2f);
                float wr = rd.wr > 0f ? rd.wr : Mathf.Max(3f, rd.lanes * 3.2f / 2f);
                if (rd.oneway && rd.wl <= 0f) { wl = wr = Mathf.Max(3.5f, rd.lanes * 3.3f / 2f); }
                decks.Add((rd, wl, wr));
            }
            // (11/11 낮 대표: "길이 끊어지는 곳") 고가 길이 다른 길로 이어지는 이음에서 폭이 계단처럼 바뀌었다 →
            //   끝이 맞닿은 고가 길끼리는 이음 폭을 두 길의 평균으로 맞추고 30 m 에 걸쳐 제 폭으로 돌아간다
            var joint = new Dictionary<(int, int), List<(int k, bool start)>>();
            (int, int) Key(V2 p) => (Mathf.RoundToInt(p.x * 5f), Mathf.RoundToInt(p.y * 5f));
            for (int k = 0; k < decks.Count; k++)
            {
                var pp = decks[k].rd.pts;
                foreach (var st in new[] { true, false })
                {
                    var key = Key(st ? pp[0] : pp[pp.Length - 1]);
                    if (!joint.TryGetValue(key, out var l)) joint[key] = l = new List<(int, bool)>();
                    l.Add((k, st));
                }
            }
            all.Clear(); all.AddRange(decks);
            for (int k = 0; k < decks.Count; k++)
            {
                var (rd, wl, wr) = decks[k];
                int m = rd.pts.Length;
                var WL = new float[m]; var WR = new float[m];
                var s = new float[m];
                for (int i = 1; i < m; i++) s[i] = s[i - 1] + rd.pts[i].DistanceTo(rd.pts[i - 1]);
                float len = s[m - 1], T = Mathf.Min(30f, len / 2f);
                float jl0 = wl, jr0 = wr, jl1 = wl, jr1 = wr;
                foreach (var st in new[] { true, false })
                {
                    var l = joint[Key(st ? rd.pts[0] : rd.pts[m - 1])];
                    if (l.Count != 2) continue;
                    var o = l[0].k == k && l[0].start == st ? l[1] : l[0];
                    if (o.k == k) continue;
                    bool flip = o.start == st;                 // 둘 다 시작점(또는 둘 다 끝점)이면 방향이 반대 → 왼쪽·오른쪽이 바뀐다
                    float ol = flip ? decks[o.k].wr : decks[o.k].wl, or_ = flip ? decks[o.k].wl : decks[o.k].wr;
                    if (st) { jl0 = (wl + ol) / 2f; jr0 = (wr + or_) / 2f; } else { jl1 = (wl + ol) / 2f; jr1 = (wr + or_) / 2f; }
                }
                for (int i = 0; i < m; i++)
                {
                    float a = T > 0.5f ? Mathf.Clamp01(s[i] / T) : 1f, b = T > 0.5f ? Mathf.Clamp01((len - s[i]) / T) : 1f;
                    a = a * a * (3f - 2f * a); b = b * b * (3f - 2f * b);
                    WL[i] = Mathf.Lerp(jl1, Mathf.Lerp(jl0, wl, a), b);
                    WR[i] = Mathf.Lerp(jr1, Mathf.Lerp(jr0, wr, a), b);
                }
                Deck(rd, h, WL, WR, deck, concrete, white, yellow);
            }
            if (n == 0) return;
            deck.ToObject("overpass-deck", DcAssets.Pbr("clean_asphalt", DcAssets.Hex("#4C5056"), new Color(1.05f, 1.05f, 1.08f), 0.9f), node, true, true);   // (11/11 낮) 갈색 아스팔트 질감이라 위에서 보면 보도처럼 보였다 → 땅 큰길과 같은 질감
            concrete.ToObject("overpass-concrete", DcAssets.Pbr("concrete", DcAssets.Hex("#A9A9A4"), Color.white), node, true, true);
            if (!white.IsEmpty) white.ToObject("overpass-paint-w", DcAssets.Unlit(DcAssets.Paint), node);
            if (!yellow.IsEmpty) yellow.ToObject("overpass-paint-y", DcAssets.Unlit(DcAssets.Hex("#E2B33C")), node);
            Debug.Log($"GradeBuilder: 고가 길 {n}, 땅 길 위라 뺀 교각 {piersSkipped}");
        }

        static Vector3 P(V2 p, float y) => Space3D.W(p, y);

        // (11/11 밤 대표: "클로버형 … 실제로 도로가 이어져야해") 연결로가 본선에 붙는 곳: 두 상판이 겹치는 자리에는
        //   방호벽·가장자리 실선을 그리지 않는다(예전에는 길마다 양옆에 벽을 세워 합류 자리가 벽으로 막혔다).
        static readonly List<(RegionRoad rd, float wl, float wr)> all = new List<(RegionRoad, float, float)>();

        /// <summary>점 q(높이 z)가 다른 고가 길의 상판 안(가장자리에서 pad 만큼 더)인가. wider: 나보다 넓은 길만</summary>
        static bool InOther(RegionRoad self, float selfW, V2 q, float z, float pad, bool wider)
        {
            foreach (var (rd, wl, wr) in all)
            {
                if (rd == self) continue;
                if (wider && wl + wr < selfW + 1f) continue;
                var p = rd.pts; var zz = rd.z;
                float lim = Mathf.Max(wl, wr) + pad;
                for (int i = 0; i + 1 < p.Length; i++)
                {
                    if (zz[i] <= MinZ && zz[i + 1] <= MinZ) continue;
                    var a = p[i]; var b = p[i + 1];
                    if (q.x < Mathf.Min(a.x, b.x) - lim || q.x > Mathf.Max(a.x, b.x) + lim || q.y < Mathf.Min(a.y, b.y) - lim || q.y > Mathf.Max(a.y, b.y) + lim) continue;
                    var ab = b - a; float L2 = ab.Dot(ab);
                    if (L2 < 1e-6f) continue;
                    float t = Mathf.Clamp01((q - a).Dot(ab) / L2);
                    var c = a + ab * t;
                    var d = q - c;
                    float side = ab.x * d.y - ab.y * d.x;          // 길 방향의 오른쪽(+) / 왼쪽(−): nr = (−dir.y, dir.x) 와 같은 쪽
                    float w = (side >= 0f ? wr : wl) + pad;
                    if (d.Dot(d) > w * w) continue;
                    if (Mathf.Abs(zz[i] + (zz[i + 1] - zz[i]) * t - z) > 1.0f) continue;
                    return true;
                }
            }
            return false;
        }

        static readonly List<(V2 a, V2 b, float half)> ground = new List<(V2, V2, float)>();
        public static int piersSkipped;

        /// <summary>교각 자리 q(반지름 r)가 땅 길 차도(반폭 + 0.5 m) 밖인가</summary>
        static bool PierClear(V2 q, float r)
        {
            foreach (var (a, b, half) in ground)
            {
                float lim = half + r + 0.5f;
                if (q.x < Mathf.Min(a.x, b.x) - lim || q.x > Mathf.Max(a.x, b.x) + lim || q.y < Mathf.Min(a.y, b.y) - lim || q.y > Mathf.Max(a.y, b.y) + lim) continue;
                if (Geom.ClosestPointOnSegment(q, a, b).DistanceTo(q) < lim) return false;
            }
            return true;
        }

        static void Deck(RegionRoad rd, float h, float[] WL, float[] WR, MeshKit deck, MeshKit concrete, MeshKit white, MeshKit yellow)
        {
            var pts = rd.pts;
            var z = rd.z;
            int n = pts.Length;
            // 꼭짓점 법선(오른쪽 +), 이음 부분은 양쪽 평균
            var nr = new V2[n];
            for (int i = 0; i < n; i++)
            {
                V2 dir;
                if (i == 0) dir = (pts[1] - pts[0]).Normalized;
                else if (i == n - 1) dir = (pts[n - 1] - pts[n - 2]).Normalized;
                else dir = ((pts[i] - pts[i - 1]).Normalized + (pts[i + 1] - pts[i]).Normalized).Normalized;
                nr[i] = new V2(-dir.y, dir.x);
            }
            float along = 0f, nextPier = 12f;
            for (int i = 0; i + 1 < n; i++)
            {
                float za = z[i], zb = z[i + 1];
                if (za <= MinZ && zb <= MinZ) { along += pts[i].DistanceTo(pts[i + 1]); continue; }
                za = Mathf.Max(za, 0.02f); zb = Mathf.Max(zb, 0.02f);
                float wlA = WL[i], wrA = WR[i], wlB = WL[i + 1], wrB = WR[i + 1], wl = (wlA + wlB) / 2f, wr = (wrA + wrB) / 2f;
                V2 aL = pts[i] - nr[i] * wlA, aR = pts[i] + nr[i] * wrA, bL = pts[i + 1] - nr[i + 1] * wlB, bR = pts[i + 1] + nr[i + 1] * wrB;
                float ya = h + za + 0.03f, yb = h + zb + 0.03f;
                float zm = (za + zb) / 2f, selfW = wl + wr;
                V2 mL = (aL + bL) * 0.5f, mR = (aR + bR) * 0.5f;
                // 상판 윗면(더 넓은 길의 상판 안에 다 들어가면 그리지 않는다 — 본선에 합쳐진 연결로 끝)
                bool buried = InOther(rd, selfW, mL, zm, 0f, true) && InOther(rd, selfW, mR, zm, 0f, true);
                if (!buried) deck.Quad(P(aL, ya), P(aR, ya), P(bR, yb), P(bL, yb), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                // 방호벽(0.3 m 폭, 1.0 m 높이) 양쪽
                foreach (var side in new[] { -1f, 1f })
                {
                    float w0a = side < 0 ? wlA : wrA, w0b = side < 0 ? wlB : wrB;
                    V2 a0 = pts[i] + nr[i] * (side * w0a), b0 = pts[i + 1] + nr[i + 1] * (side * w0b);
                    V2 a1 = pts[i] + nr[i] * (side * (w0a + 0.3f)), b1 = pts[i + 1] + nr[i + 1] * (side * (w0b + 0.3f));
                    if (InOther(rd, selfW, (a0 + b0 + a1 + b1) * 0.25f, zm, 0.3f, false)) continue;      // 다른 상판과 맞닿은 쪽: 벽 없음
                    var inward = -side;
                    var nIn = (P(nr[i] * inward, 0f) - P(V2.Zero, 0f)).normalized;
                    concrete.Quad(P(a0, ya), P(b0, yb), P(b0, yb + 1.0f), P(a0, ya + 1.0f), nIn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    concrete.Quad(P(a0, ya + 1.0f), P(b0, yb + 1.0f), P(b1, yb + 1.0f), P(a1, ya + 1.0f), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    // 바깥면: 높으면 보(두께 1.2 m)까지, 낮으면(옹벽) 땅까지
                    float da = za > 2.5f ? 1.2f : za + 0.03f, db = zb > 2.5f ? 1.2f : zb + 0.03f;
                    concrete.Quad(P(a1, ya - da), P(b1, yb - db), P(b1, yb + 1.0f), P(a1, ya + 1.0f), -nIn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
                // 상판 밑면(보 아래)
                if (!buried)
                {
                    float da = za > 2.5f ? 1.2f : za, db = zb > 2.5f ? 1.2f : zb;
                    V2 aL1 = pts[i] - nr[i] * (wlA + 0.3f), aR1 = pts[i] + nr[i] * (wrA + 0.3f), bL1 = pts[i + 1] - nr[i + 1] * (wlB + 0.3f), bR1 = pts[i + 1] + nr[i + 1] * (wrB + 0.3f);
                    concrete.Quad(P(aL1, ya - da), P(bL1, yb - db), P(bR1, yb - db), P(aR1, ya - da), Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
                // 기둥: 높이 2.5 m 넘는 곳 25 m 마다(가운데 한 줄, 넓으면 두 줄)
                float segL = pts[i].DistanceTo(pts[i + 1]);
                while (nextPier < along + segL)
                {
                    float t = (nextPier - along) / segL;
                    float zc = za + (zb - za) * t;
                    if (zc > 2.5f)
                    {
                        var c = pts[i] + (pts[i + 1] - pts[i]) * t;
                        var nn = nr[i] + (nr[i + 1] - nr[i]) * t;
                        float w = wl + wr;
                        float mid = (wr - wl) / 2f;
                        var cols = w > 14f ? new[] { mid - w * 0.25f, mid + w * 0.25f } : new[] { mid };
                        float yaw = Space3D.Yaw(Mathf.Atan2(pts[i + 1].y - pts[i].y, pts[i + 1].x - pts[i].x));
                        foreach (var o in cols)
                        {
                            var q = c + nn * o;
                            if (!PierClear(q, 0.7f)) { piersSkipped++; continue; }     // 밑의 땅 길 차도 위면 세우지 않는다(긴 경간)
                            concrete.Box(P(q, h + (zc - 1.2f) / 2f), new Vector3(1.4f, zc - 1.2f, 1.4f), yaw);
                            concrete.Box(P(q, h + zc - 1.2f - 0.3f), new Vector3(Mathf.Min(w * 0.45f, 6f), 0.6f, 1.6f), yaw + 90f);
                        }
                    }
                    nextPier += 25f;
                }
                // 칠: 차로 경계(lr·ll 사이) 흰 점선, 양방향 가운데 황색 복선, 바깥 흰 실선(0.4 m 안쪽)
                float yP = Mathf.Min(ya, yb) + 0.012f;
                float ypa = ya + 0.012f, ypb = yb + 0.012f;
                void Line(float off, float w, MeshKit mk, bool dashed)
                {
                    V2 a = pts[i] + nr[i] * off, b = pts[i + 1] + nr[i + 1] * off;
                    float L = a.DistanceTo(b);
                    if (L < 0.2f) return;
                    if (InOther(rd, selfW, (a + b) * 0.5f, zm, 0.2f, dashed)) return;       // 합류 자리: 가장자리 실선 끊김, 묻힌 연결로의 칠 없음
                    var dd = (b - a) / L;
                    var o = new V2(-dd.y, dd.x) * (w / 2f);
                    if (!dashed)
                    {
                        mk.Quad(P(a + o, ypa), P(a - o, ypa), P(b - o, ypb), P(b + o, ypb), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                        return;
                    }
                    for (float s0 = 0f; s0 < L; s0 += 8f)
                    {
                        float s1 = Mathf.Min(L, s0 + 3f);
                        float y0 = Mathf.Lerp(ypa, ypb, s0 / L), y1 = Mathf.Lerp(ypa, ypb, s1 / L);
                        var p0 = a + dd * s0; var p1 = a + dd * s1;
                        mk.Quad(P(p0 + o, y0), P(p0 - o, y0), P(p1 - o, y1), P(p1 + o, y1), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                }
                Line(-wl + 0.4f, 0.15f, white, false);
                Line(wr - 0.4f, 0.15f, white, false);
                if (!rd.oneway) { Line(-0.15f, 0.15f, yellow, false); Line(0.15f, 0.15f, yellow, false); }
                foreach (var offs in new[] { rd.laneR, rd.laneL })
                {
                    if (offs == null || offs.Length < 2) continue;
                    for (int k = 0; k + 1 < offs.Length; k++) Line((offs[k] + offs[k + 1]) / 2f, 0.15f, white, true);
                }
                along += segL;
            }
        }
    }
}
