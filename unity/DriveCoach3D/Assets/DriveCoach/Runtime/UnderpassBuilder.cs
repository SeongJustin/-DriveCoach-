// 담당: S (11/9 새벽, 대표: "고가도로나 지하도로 … 최대한 다 반영해봐")
// 지하차도: 땅을 판 길. make_surface 가 땅·노면·보도에 도랑 구멍을 내 두고(엇갈리는 땅 길 자리는 덮개로 남김),
// 여기서는 도랑 안을 그린다:
//   바닥 아스팔트(길 모양 그대로 높이를 따라), 양옆 콘크리트 옹벽(바닥에서 땅 + 1.0 m 난간 높이까지), 벽 위 난간 띠,
//   칠(가장자리 흰 실선·양방향이면 황색 복선·차로 경계 흰 점선),
//   덮개 밑면: 도랑 길 가운데가 땅 높이 아스팔트(엇갈리는 땅 길)에 덮인 곳은 두께 0.9 m 콘크리트 판 밑면과 옆면(안에서 하늘이 보이지 않게).
// 높이 0.6 m 밑(입구 경사 끝)은 그리지 않는다(땅 노면이 그대로).
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class UnderpassBuilder
    {
        const float MinDepth = -0.6f;

        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null) return;
            var floor = new MeshKit { uvScale = 5f };
            var wall = new MeshKit { uvScale = 3f };
            var white = new MeshKit();
            var yellow = new MeshKit();
            coverRoad = new MeshKit { uvScale = 5f }; coverGround = new MeshKit { uvScale = 6f };
            var surf = CityBuilder.Current;
            int n = 0, lids = 0;
            all.Clear();
            foreach (var rd in d.roads)
            {
                if (rd.z == null || rd.pts.Length < 2) continue;
                bool any0 = false;
                foreach (var z in rd.z) if (z < MinDepth) { any0 = true; break; }
                if (any0)
                {
                    float hf = Mathf.Max(rd.wl > 0f ? rd.wl : Mathf.Max(3f, rd.lanes * 3.2f / 2f), rd.wr > 0f ? rd.wr : Mathf.Max(3f, rd.lanes * 3.2f / 2f));
                    // (11/13) 좌우 따로 준 길은 옆 판정도 좌우 따로 — 넓은 쪽 값을 양쪽에 쓰면 좁은 쪽 옆의 남의 벽까지 빠져 벽에 구멍이 난다
                    if (rd.twl > 0f || rd.twr > 0f) all.Add((rd, Mathf.Max(0.3f, rd.twl), Mathf.Max(0.3f, rd.twr))); else all.Add((rd, hf, hf));
                }
            }
            foreach (var rd in d.roads)
            {
                if (rd.z == null || rd.pts.Length < 2) continue;
                bool any = false;
                foreach (var z in rd.z) if (z < MinDepth) { any = true; break; }
                if (!any) continue;
                n++;
                // 도랑 구멍(make_surface)과 같은 폭: 좌우 중 넓은 쪽 반폭을 양쪽에(벽과 땅 구멍 사이 틈이 없게)
                float half = Mathf.Max(rd.wl > 0f ? rd.wl : Mathf.Max(3f, rd.lanes * 3.2f / 2f), rd.wr > 0f ? rd.wr : Mathf.Max(3f, rd.lanes * 3.2f / 2f));
                float wl = half, wr = half;
                if (rd.twl > 0f || rd.twr > 0f) { wl = Mathf.Max(0.3f, rd.twl); wr = Mathf.Max(0.3f, rd.twr); }      // 좌우 따로 준 도랑 폭
                lids += Trench(rd, h, wl, wr, floor, wall, white, yellow, surf);
            }
            if (n == 0) return;
            // (11/12 밤) 독립 평가 두 번: "도로 위에 두꺼운 흰 판", "높이 차가 안 읽혀 평면 교차로로 보인다"
            //   → 옹벽을 밝은 크림색에서 어두운 콘크리트로, 도랑 바닥은 땅 위 아스팔트보다 어둡게(아래에 있다는 것이 색으로 읽히게),
            //     터널(위에 차도가 없어도 덮인 곳)은 땅 높이에 덮개 윗면을 그린다(전에는 밑면만 있어 위에서 도랑 안 벽이 들여다보였다).
            floor.ToObject("underpass-floor", DcAssets.Pbr("clean_asphalt", DcAssets.Hex("#4C5056"), new Color(0.72f, 0.72f, 0.75f), 0.9f), node, false, true);
            wall.ToObject("underpass-walls", DcAssets.Pbr("concrete", DcAssets.Hex("#8B8C88"), new Color(0.80f, 0.80f, 0.80f)), node, true, true);
            if (!coverRoad.IsEmpty) coverRoad.ToObject("underpass-cover-road", DcAssets.Pbr("clean_asphalt", DcAssets.Hex("#4C5056"), new Color(1.0f, 1.0f, 1.03f), 0.9f), node, false, true);
            if (!coverGround.IsEmpty) coverGround.ToObject("underpass-cover-ground", DcAssets.Pbr("concrete_floor_worn_001", DcAssets.Hex("#B9B19E"), new Color(1.0f, 0.95f, 0.84f), 0.5f), node, false, true);
            if (!white.IsEmpty) white.ToObject("underpass-paint-w", DcAssets.Unlit(DcAssets.Paint), node);
            if (!yellow.IsEmpty) yellow.ToObject("underpass-paint-y", DcAssets.Unlit(DcAssets.Hex("#E2B33C")), node);
            Debug.Log($"UnderpassBuilder: 지하차도 {n}, 덮개 조각 {lids}");
        }

        static Vector3 P(V2 p, float y) => Space3D.W(p, y);
        static MeshKit coverRoad, coverGround;

        // (11/11 밤) 연결로 도랑이 지하차도 도랑에 붙는 곳: 다른 도랑 안에 들어가는 벽·가장자리 실선은 그리지 않는다(합류 자리가 벽으로 막히지 않게)
        static readonly System.Collections.Generic.List<(RegionRoad rd, float wl, float wr)> all = new System.Collections.Generic.List<(RegionRoad, float, float)>();

        static bool InOther(RegionRoad self, V2 q, float z, float pad)
        {
            foreach (var (rd, hwl, hwr) in all)
            {
                if (rd == self) continue;
                // (11/12 밤) 전에는 연결로가 낀 이음만 벽을 뺐다. 나란한 두 방향 도랑의 바닥이 겹치는 곳에도 가운데 벽이 서서
                //   위에서 보면 차도 한가운데 흰 줄(옹벽 윗면)이 여러 가닥 지나갔다(독립 평가 "도로 위 두꺼운 흰 판") → 같은 깊이로 맞닿은 도랑은 모두 한 도랑으로.
                var p = rd.pts; var zz = rd.z; float lim = Mathf.Max(hwl, hwr) + pad;
                for (int i = 0; i + 1 < p.Length; i++)
                {
                    if (zz[i] >= MinDepth && zz[i + 1] >= MinDepth) continue;
                    var a = p[i]; var b = p[i + 1];
                    if (q.x < Mathf.Min(a.x, b.x) - lim || q.x > Mathf.Max(a.x, b.x) + lim || q.y < Mathf.Min(a.y, b.y) - lim || q.y > Mathf.Max(a.y, b.y) + lim) continue;
                    var ab = b - a; float L2 = ab.Dot(ab);
                    if (L2 < 1e-6f) continue;
                    float t = Mathf.Clamp01((q - a).Dot(ab) / L2);
                    var dd = q - (a + ab * t);
                    float sl = (dd.x * -ab.y + dd.y * ab.x) >= 0f ? hwr + pad : hwl + pad;      // 오른쪽(+)·왼쪽 폭
                    if (dd.Dot(dd) > sl * sl) continue;
                    if (Mathf.Abs(zz[i] + (zz[i + 1] - zz[i]) * t - z) > 1.2f) continue;
                    return true;
                }
            }
            return false;
        }

        static bool InMouth(RegionRoad self, V2 q, float z)
        {
            foreach (var (rd, hwl, hwr) in all)
            {
                if (rd == self || !rd.tunnel) continue;
                var p = rd.pts; var zz = rd.z; float pad = 0.5f; float lim = Mathf.Max(hwl, hwr) + pad;
                for (int i = 0; i + 1 < p.Length; i++)
                {
                    var a = p[i]; var b = p[i + 1];
                    var ab = b - a; float L2 = ab.Dot(ab);
                    if (L2 < 1e-6f) continue;
                    float t = Mathf.Clamp01((q - a).Dot(ab) / L2);
                    var dd = q - (a + ab * t);
                    float sl = (dd.x * -ab.y + dd.y * ab.x) >= 0f ? hwr + pad : hwl + pad;      // 오른쪽(+)·왼쪽 폭
                    if (dd.Dot(dd) > sl * sl) continue;
                    if (Mathf.Abs(zz[i] + (zz[i + 1] - zz[i]) * t - z) > 1.2f) continue;
                    return true;
                }
            }
            return false;
        }

        static void Wings(RegionRoad rd, V2 e, V2 nrm, V2 outDir, float wl, float wr, float yFloor, float yTop, float z, MeshKit wall)
        {
            var o3 = P(outDir, 0f) - P(V2.Zero, 0f);
            foreach (var side in new[] { -1f, 1f })
            {
                float s0 = side < 0 ? wl : wr;
                for (float s = s0; s < s0 + 14f; s += 1f)
                {
                    V2 qa = e + nrm * (side * s), qb = e + nrm * (side * (s + 1f));
                    if (InMouth(rd, (qa + qb) * 0.5f, z)) continue;
                    // (11/13) 옆 도랑·옆 터널 바닥 위에는 날개벽을 세우지 않는다(입구 앞 차도에 기둥처럼 선 벽 조각 — 대표: "수직벽이 있으면 사고")
                    if (InOther(rd, (qa + qb) * 0.5f + outDir * 0.6f, z, 0.3f) || InOther(rd, (qa + qb) * 0.5f - outDir * 0.6f, z, 0.3f)) continue;
                    wall.Quad(P(qa, yFloor), P(qb, yFloor), P(qb, yTop), P(qa, yTop), o3, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
            }
        }

        static int Trench(RegionRoad rd, float h, float wl, float wr, MeshKit floor, MeshKit wall, MeshKit white, MeshKit yellow, RoadSurface surf)
        {
            var pts = rd.pts;
            var z = rd.z;
            int n = pts.Length, lids = 0;
            var nr = new V2[n];
            for (int i = 0; i < n; i++)
            {
                V2 dir;
                if (i == 0) dir = (pts[1] - pts[0]).Normalized;
                else if (i == n - 1) dir = (pts[n - 1] - pts[n - 2]).Normalized;
                else dir = ((pts[i] - pts[i - 1]).Normalized + (pts[i + 1] - pts[i]).Normalized).Normalized;
                nr[i] = new V2(-dir.y, dir.x);
            }
            const float Wall = 0.5f, Rail = 1.0f;
            for (int i = 0; i + 1 < n; i++)
            {
                float za = z[i], zb = z[i + 1];
                if (za >= MinDepth && zb >= MinDepth) continue;
                za = Mathf.Min(za, -0.02f); zb = Mathf.Min(zb, -0.02f);
                float ya = h + za + 0.03f, yb = h + zb + 0.03f;
                V2 aL = pts[i] - nr[i] * wl, aR = pts[i] + nr[i] * wr, bL = pts[i + 1] - nr[i + 1] * wl, bR = pts[i + 1] + nr[i + 1] * wr;
                floor.Quad(P(aL, ya), P(aR, ya), P(bR, yb), P(bL, yb), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                // 덮개(엇갈리는 땅 길이 위를 지나는 곳): 이 조각 가운데 위 땅 높이에 아스팔트가 있으면 — 벽은 덮개 밑면까지만, 난간 없음
                var mid = (pts[i] + pts[i + 1]) / 2f;
                // 11/12: 터널로 표시된 길(공원·하천 밑을 지나는 지하차도)은 위에 아스팔트가 없어도 덮개 — 난간·벽 윗면이 공원 위로 솟아 흰 줄처럼 보였다
                bool lid = surf != null && Mathf.Min(za, zb) < -3f && (rd.tunnel || surf.Asphalt(mid));
                // 옹벽: 안쪽 면(바닥 → 땅 + 난간), 위 띠(두께 0.5 m), 바깥 면은 땅에 묻혀 안 보이니 난간 높이만
                foreach (var side in new[] { -1f, 1f })
                {
                    float w0 = side < 0 ? wl : wr;
                    V2 a0 = pts[i] + nr[i] * (side * w0), b0 = pts[i + 1] + nr[i + 1] * (side * w0);
                    V2 a1 = pts[i] + nr[i] * (side * (w0 + Wall)), b1 = pts[i + 1] + nr[i + 1] * (side * (w0 + Wall));
                    if (InOther(rd, (a0 + b0 + a1 + b1) * 0.25f, (za + zb) / 2f, 0.35f)) continue;        // 다른 도랑과 맞닿은 쪽: 벽 없음
                    var nIn = (P(nr[i] * -side, 0f) - P(V2.Zero, 0f)).normalized;
                    float top = lid ? h - 0.9f : h + Rail;
                    wall.Quad(P(a0, ya), P(b0, yb), P(b0, top), P(a0, top), nIn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    if (lid) continue;
                    wall.Quad(P(a0, top), P(b0, top), P(b1, top), P(a1, top), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    wall.Quad(P(a1, h), P(b1, h), P(b1, top), P(a1, top), -nIn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
                // 덮개 밑면: 콘크리트 판(두께 0.9 m)
                if (lid)
                {
                    lids++;
                    float yl = h - 0.9f;
                    V2 lL = pts[i] - nr[i] * (wl + Wall), lR = pts[i] + nr[i] * (wr + Wall), mL = pts[i + 1] - nr[i + 1] * (wl + Wall), mR = pts[i + 1] + nr[i + 1] * (wr + Wall);
                    // 덮개 윗면(11/12 밤): 위에 땅 차도가 없는 터널 구간 — 양옆이 차도면 아스팔트, 아니면 땅(공원·녹지 밑 터널)
                    if (!surf.Asphalt(mid))
                    {
                        var nm = (nr[i] + nr[i + 1]).Normalized;
                        bool road = surf.Asphalt(mid - nm * (wl + Wall + 1.5f)) && surf.Asphalt(mid + nm * (wr + Wall + 1.5f));
                        float yt = h + 0.02f;
                        (road ? coverRoad : coverGround).Quad(P(lL, yt), P(lR, yt), P(mR, yt), P(mL, yt), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                    wall.Quad(P(lL, yl), P(mL, yl), P(mR, yl), P(lR, yl), Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    // 들어가는 쪽·나오는 쪽 앞면(이 조각 앞뒤가 덮개 밖이면)
                    var dir = (pts[i + 1] - pts[i]).Normalized;
                    if (i == 0 || !(rd.tunnel || surf.Asphalt((pts[i - 1] + pts[i]) / 2f)))
                        wall.Quad(P(lL, yl), P(lR, yl), P(lR, h + 0.03f), P(lL, h + 0.03f), P(-dir, 0f) - P(V2.Zero, 0f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    if (i + 2 >= n || !(rd.tunnel || surf.Asphalt((pts[i + 1] + pts[i + 2]) / 2f)))
                        wall.Quad(P(mL, yl), P(mR, yl), P(mR, h + 0.03f), P(mL, h + 0.03f), P(dir, 0f) - P(V2.Zero, 0f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    // (11/13) 터널 입구 옆 날개벽: 입구 앞 열린 도랑이 터널보다 넓으면 입구 양옆이 뚫려 하늘이 비쳤다(운전석에서 흰 틈).
                    //   입구 면을 좌우 14 m 까지 바닥~땅 높이로 막는다(다른 터널 입구 자리는 비움, 땅속 부분은 어차피 안 보임)
                    if (rd.tunnel)
                    {
                        if (i == 0) Wings(rd, pts[i], nr[i], -dir, wl + Wall, wr + Wall, ya, h + 0.03f, za, wall);
                        if (i + 2 >= n) Wings(rd, pts[i + 1], nr[i + 1], dir, wl + Wall, wr + Wall, yb, h + 0.03f, zb, wall);
                    }
                }
                // 칠
                float ypa = ya + 0.012f, ypb = yb + 0.012f;
                void Line(float off, float w, MeshKit mk, bool dashed)
                {
                    V2 a = pts[i] + nr[i] * off, b = pts[i + 1] + nr[i + 1] * off;
                    float L = a.DistanceTo(b);
                    if (L < 0.2f) return;
                    if (!dashed && InOther(rd, (a + b) * 0.5f, (za + zb) / 2f, -0.2f)) return;
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
            }
            return lids;
        }
    }
}
