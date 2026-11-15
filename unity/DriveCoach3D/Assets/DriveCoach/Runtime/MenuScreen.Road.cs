// MenuScreen 의 도로 주행(동네 지도에서 출발 자리 고르기) 부분(11/15 코드 정리로 MenuScreen.cs 에서 나눈 파일). 전체 설명은 MenuScreen.cs 맨 위.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    public partial class MenuScreen
    {
        // ───────────────────────── 도로 주행: 동네 지도에서 출발 자리 고르기 ─────────────────────────

        Texture2D mapTex; int mapFor = -1; float mapX0, mapY0, mapX1, mapY1;
        V2? startAt; string startName = ""; bool startDemo; float startHeading = float.NaN;

        void BuildStartMap(int ri)
        {
            var rd = RegionCatalog.Load(ri);
            if (rd == null) { Hint("지도를 읽지 못했습니다"); return; }
            if (mapFor != ri || mapTex == null)
            {
                if (mapTex != null) UnityEngine.Object.Destroy(mapTex);
                mapTex = RegionMapTexture(rd, out mapX0, out mapY0, out mapX1, out mapY1);
                mapFor = ri; startAt = null; startName = ""; startDemo = false; startHeading = float.NaN;
            }
            float W = root.rect.width, H = root.rect.height;
            if (W < 100f) { W = 1080f * Screen.width / Mathf.Max(1, Screen.height); H = 1080f; }
            float cw = W - 96f, ch = H - 276f;
            float aspect = (mapX1 - mapX0) / Mathf.Max(1f, mapY1 - mapY0);
            float mh = ch - 8f, mw = mh * aspect;
            if (mw > cw * 0.66f) { mw = cw * 0.66f; mh = mw / aspect; }
            var frame = UiKit.Card(content, "mapframe", DcAssets.Card, DcAssets.UiLine);
            UiKit.Place(frame.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(mw + 8f, mh + 8f));
            var img = UiKit.Node("map", frame.transform).gameObject.AddComponent<RawImage>();
            img.texture = mapTex;
            UiKit.Stretch(img.rectTransform, 4, 4, 4, 4);
            var mrt = img.rectTransform;
            Func<V2, Vector2> toUi = p => new Vector2((p.x - mapX0) / (mapX1 - mapX0) * mw, -(p.y - mapY0) / (mapY1 - mapY0) * mh);   // 지도 왼쪽 위 기준
            // (11/15 대표: "모든 출발은 주차장 칸에서") 지도 위 P 표시 = 출발할 수 있는 주차장(길로 바로 나갈 수 있는 빈 칸). 지도를 누르면 가장 가까운 P 를 고른다
            var lots = rd.ParkingStarts();
            {
                // (11/15 베타 6g: 가까운 두 주차장의 P 가 겹쳐 하나를 골라 누르기 어렵다) 지도에서 P 크기보다 가까운 것은 하나만 둔다
                var shownLots = new List<RegionData.ParkStart>();
                foreach (var lp in lots)
                {
                    var u = toUi(lp.c); bool near = false;
                    foreach (var s0 in shownLots) if ((toUi(s0.c) - u).magnitude < 48f) { near = true; break; }
                    if (!near) shownLots.Add(lp);
                }
                lots = shownLots;
            }
            Action<RegionData.ParkStart> pick = ps => { startAt = ps.c; startHeading = ps.headingDeg; startName = ps.name; startDemo = false; Refresh(); };
            var click = img.gameObject.AddComponent<MapClick>();
            click.clicked = uv =>
            {
                // uv: 지도 그림 안 0..1(왼쪽 아래가 0,0) → 동네 좌표(동쪽 +x, 남쪽 +y)
                var p = new V2(mapX0 + uv.x * (mapX1 - mapX0), mapY1 - uv.y * (mapY1 - mapY0));
                if (lots.Count > 0)
                {
                    RegionData.ParkStart best = null; float bd = 250f;
                    foreach (var q0 in lots) { float d = q0.c.DistanceTo(p); if (d < bd) { bd = d; best = q0; } }
                    if (best != null) pick(best);
                    return;
                }
                // 주차장이 없는 동네: 가장 가까운 길
                var q = NearestRoad(rd, p);
                if (!q.HasValue) return;
                startAt = q; startHeading = float.NaN; startName = "지도에서 고른 곳"; startDemo = false;
                Refresh();
            };
            foreach (var lp in lots)
            {
                var lv = lp;
                ParkPin(mrt, toUi(lp.c), () => pick(lv));
            }
            // 이 동네에 짜 둔 자동 운전 경로(시연)가 있으면 그 출발 자리도 고를 수 있다
            V2? demoAt = null; string demoName = "";
            {
                var rta = Resources.Load<TextAsset>("DC/Routes/" + RegionCatalog.Entries[ri].file + "_demo1");
                var ro = rta != null ? MiniJson.ParseObject(rta.text) : null;
                if (ro != null && ro.TryGetValue("start", out var sv) && sv is List<object> sl && sl.Count >= 3)
                {
                    demoAt = new V2(Convert.ToSingle(sl[0]), Convert.ToSingle(sl[1]));
                    demoName = (ro.TryGetValue("name", out var nm) ? nm as string : null) ?? "시연 경로";
                }
            }
            if (startAt.HasValue) Pin(mrt, toUi(startAt.Value), "", true, null);
            // 오른쪽: 안내·고른 곳·출발
            float px = mw + 40f, pw = Mathf.Max(260f, cw - px);
            var t1 = UiKit.Label(content, lots.Count > 0 ? "지도에서 출발할 주차장(P)을 누르십시오" : "지도에서 출발할 곳을 누르십시오", 30, DcAssets.UiText, true, TextAnchor.UpperLeft);
            UiKit.Place(t1.rectTransform, new Vector2(0, 1), new Vector2(px, 0), new Vector2(pw, 90));
            // (11/15 대표: "출발 자리를 골라야만 시작") 고르기 전에는 출발이 눌리지 않는다
            var t2 = UiKit.Label(content, startAt.HasValue ? "고른 곳: " + startName : "출발 자리를 고르면 '출발'을 누를 수 있습니다", 28, DcAssets.UiText, false, TextAnchor.UpperLeft);
            UiKit.Place(t2.rectTransform, new Vector2(0, 1), new Vector2(px, -100), new Vector2(pw, 80));
            var go = UiKit.Btn(content, "출발", 34, startAt.HasValue ? UiKit.Kind.Primary : UiKit.Kind.Plain, () =>
            {
                if (!startAt.HasValue) return;
                if (startDemo) { onPick?.Invoke("region:" + ri + ":99"); return; }
                var p = startAt.Value;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                onPick?.Invoke("region:" + ri + ":@" + p.x.ToString("0.00", ci) + "," + p.y.ToString("0.00", ci) + (float.IsNaN(startHeading) ? "" : "," + startHeading.ToString("0.0", ci)));
            }, out _);
            UiKit.Place((RectTransform)go.transform, new Vector2(0, 1), new Vector2(px, -190), new Vector2(Mathf.Min(pw, 360f), 96));
            if (demoAt.HasValue)
            {
                var dv = demoAt.Value; var dn = demoName;
                var db = UiKit.Btn(content, "자동 운전 경로", 28, startDemo ? UiKit.Kind.Primary : UiKit.Kind.Plain, () => { startAt = dv; startHeading = float.NaN; startName = dn + " (자동 운전)"; startDemo = true; Refresh(); }, out _);
                UiKit.Place((RectTransform)db.transform, new Vector2(0, 1), new Vector2(px, -306), new Vector2(Mathf.Min(pw, 360f), 80));
            }
            var at = UiKit.Label(content, "지도 © OpenStreetMap contributors", 20, DcAssets.UiText, false, TextAnchor.LowerLeft);
            UiKit.Place(at.rectTransform, new Vector2(0, 0), new Vector2(px, 0), new Vector2(pw, 40));
        }

        /// <summary>지도 위 표시: 고른 자리(라임 점) 또는 이름 있는 자리(흰 바탕 이름표 + 라임 점, 누를 수 있다)</summary>
        void Pin(RectTransform map, Vector2 at, string label, bool selected, Action onClick)
        {
            if (selected)
            {
                // 고른 곳: 주차장 P 표시보다 큰 라임 칸(위에 겹친다)
                var big = UiKit.Card(map, "pick", DcAssets.Lime, DcAssets.UiText);
                big.raycastTarget = false;
                var bt = UiKit.Label(big.transform, "P", 34, DcAssets.UiText, true, TextAnchor.MiddleCenter);
                bt.raycastTarget = false;
                UiKit.Stretch(bt.rectTransform);
                UiKit.Place(big.rectTransform, new Vector2(0, 1), at - new Vector2(28f, -28f), new Vector2(56f, 56f));
                return;
            }
            var tag = UiKit.Card(map, "spot " + label, new Color(1f, 1f, 1f, 0.94f), DcAssets.UiText);
            var b = tag.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var tx = UiKit.Label(tag.transform, label, 22, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = Mathf.Max(80f, tx.preferredWidth + 28f);
            UiKit.Stretch(tx.rectTransform);
            UiKit.Place(tag.rectTransform, new Vector2(0, 1), at + new Vector2(-w / 2f, 46f), new Vector2(w, 38f));
            var dot2 = UiKit.Card(map, "spotdot " + label, DcAssets.Lime, DcAssets.UiText);
            dot2.raycastTarget = false;
            UiKit.Place(dot2.rectTransform, new Vector2(0, 1), at - new Vector2(9f, -9f), new Vector2(18f, 18f));
        }

        /// <summary>지도 위 주차장 표시: 라임 네모에 검정 P(누르면 그 주차장)</summary>
        void ParkPin(RectTransform map, Vector2 at, Action onClick)
        {
            var tag = UiKit.Card(map, "lot", DcAssets.Lime, DcAssets.UiText);
            var b = tag.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => onClick());
            var tx = UiKit.Label(tag.transform, "P", 22, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(tx.rectTransform);
            tx.raycastTarget = false;
            UiKit.Place(tag.rectTransform, new Vector2(0, 1), at - new Vector2(17f, -17f), new Vector2(34f, 34f));
        }

        /// <summary>p 에서 가장 가까운 길 위의 점(동 경계 안, 땅 높이 구간). 없으면 null</summary>
        static V2? NearestRoad(RegionData rd, V2 p)
        {
            float best = float.MaxValue; V2 bp = V2.Zero; bool found = false;
            bool hasB = rd.boundary != null && rd.boundary.Length >= 3;
            foreach (var r in rd.roads)
            {
                if (r.pts == null || r.pts.Length < 2 || r.tunnel || r.bridge) continue;
                if (r.hw == "motorway" || r.hw == "trunk" || r.hw.EndsWith("_link")) continue;
                for (int i = 0; i + 1 < r.pts.Length; i++)
                {
                    if (r.z != null && (Math.Abs(r.z[i]) > 0.05f || Math.Abs(r.z[i + 1]) > 0.05f)) continue;
                    var q = Geom.ClosestPointOnSegment(p, r.pts[i], r.pts[i + 1]);
                    if (hasB && !Geom.PointInPolygon(q, rd.boundary)) continue;
                    float d = q.DistanceTo(p);
                    if (d < best) { best = d; bp = q; found = true; }
                }
            }
            if (!found || best > 120f) return null;
            return bp;
        }

        /// <summary>동네 지도 그림: 동 경계(라임)와 길(큰길 진하게, 동네길 옅게)을 위에서 본 평면으로. 북쪽이 위</summary>
        static Texture2D RegionMapTexture(RegionData rd, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue; x1 = y1 = float.MinValue;
            bool hasB = rd.boundary != null && rd.boundary.Length >= 3;
            if (hasB) foreach (var p in rd.boundary) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            else foreach (var r in rd.roads) if (r.pts != null) foreach (var p in r.pts) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            if (x0 > x1) { x0 = y0 = -500f; x1 = y1 = 500f; }
            const float pad = 60f;
            x0 -= pad; y0 -= pad; x1 += pad; y1 += pad;
            const int N = 1024;
            float sc = N / Mathf.Max(x1 - x0, y1 - y0);
            int tw = Mathf.Max(64, Mathf.RoundToInt((x1 - x0) * sc)), th = Mathf.Max(64, Mathf.RoundToInt((y1 - y0) * sc));
            var px = new Color32[tw * th];
            var bg = new Color32(246, 246, 246, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            float fx0 = x0, fy1 = y1;
            Func<V2, Vector2> T = p => new Vector2((p.x - fx0) * sc, (fy1 - p.y) * sc);   // 그림 좌표(아래쪽이 0)
            // (11/15 대표: 지도를 색으로) ColorMap 을 false 로 바꾸면 예전 회색 지도로 돌아간다
            if (ColorMap)
            {
                var land = new Color32(240, 236, 226, 255);
                for (int i = 0; i < px.Length; i++) px[i] = land;
                foreach (var ar in rd.areas)
                {
                    if (ar.poly == null || ar.poly.Length < 3) continue;
                    string k = ar.kind ?? "";
                    Color32 c = k.Contains("water") || k.Contains("river") || k.Contains("stream") ? new Color32(160, 200, 232, 255)
                              : k.Contains("parking") ? new Color32(226, 226, 230, 255)
                              : k.Contains("school") || k.Contains("pitch") || k.Contains("sand") ? new Color32(236, 226, 190, 255)
                              : new Color32(190, 222, 170, 255);
                    Fill(px, tw, th, ar.poly, T, c);
                }
                var bcol = new Color32(214, 204, 192, 255);
                foreach (var b in rd.buildings) if (b.poly != null && b.poly.Length >= 3) Fill(px, tw, th, b.poly, T, bcol);
            }
            for (int pass = 0; pass < 2; pass++)
                foreach (var r in rd.roads)
                {
                    if (r.pts == null || r.pts.Length < 2) continue;
                    bool major = r.hw.StartsWith("primary") || r.hw.StartsWith("secondary") || r.hw.StartsWith("trunk") || r.hw.StartsWith("motorway") || r.hw.StartsWith("tertiary");
                    if ((pass == 1) != major) continue;
                    float wm = r.width > 0f ? r.width : Stages.LaneWidth(r.hw) * Math.Max(1, r.lanes);
                    float w = Mathf.Clamp(wm * sc, major ? 3f : 2f, 10f);
                    var col = ColorMap ? (major ? new Color32(246, 190, 96, 255) : new Color32(255, 255, 255, 255)) : (major ? new Color32(60, 63, 68, 255) : new Color32(150, 154, 160, 255));
                    if (ColorMap) for (int i = 0; i + 1 < r.pts.Length; i++) Stroke(px, tw, th, T(r.pts[i]), T(r.pts[i + 1]), w + 2f, major ? new Color32(190, 130, 50, 255) : new Color32(170, 170, 170, 255));
                    for (int i = 0; i + 1 < r.pts.Length; i++) Stroke(px, tw, th, T(r.pts[i]), T(r.pts[i + 1]), w, col);
                }
            if (hasB)
            {
                var lime = (Color32)DcAssets.Lime;
                for (int i = 0; i < rd.boundary.Length; i++) Stroke(px, tw, th, T(rd.boundary[i]), T(rd.boundary[(i + 1) % rd.boundary.Length]), 5f, lime);
            }
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>(11/15 대표) 색 지도(땅, 녹지, 물, 건물, 길). false = 예전 회색 선 지도</summary>
        const bool ColorMap = true;

        /// <summary>다각형 채우기(가로줄마다 변과 만나는 곳을 찾아 사이를 칠한다)</summary>
        static void Fill(Color32[] px, int tw, int th, V2[] poly, Func<V2, Vector2> T, Color32 c)
        {
            int n = poly.Length;
            var p = new Vector2[n];
            float ymin = float.MaxValue, ymax = float.MinValue;
            for (int i = 0; i < n; i++) { p[i] = T(poly[i]); ymin = Mathf.Min(ymin, p[i].y); ymax = Mathf.Max(ymax, p[i].y); }
            int ya = Mathf.Max(0, Mathf.CeilToInt(ymin)), yb = Mathf.Min(th - 1, Mathf.FloorToInt(ymax));
            var xs = new List<float>();
            for (int y = ya; y <= yb; y++)
            {
                xs.Clear();
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float y0 = p[j].y, y1 = p[i].y;
                    if ((y0 <= y && y1 > y) || (y1 <= y && y0 > y)) xs.Add(p[j].x + (y - y0) / (y1 - y0) * (p[i].x - p[j].x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Mathf.Max(0, Mathf.CeilToInt(xs[k])), xb = Mathf.Min(tw - 1, Mathf.FloorToInt(xs[k + 1]));
                    int row = y * tw;
                    for (int x = xa; x <= xb; x++) px[row + x] = c;
                }
            }
        }

        static void Stroke(Color32[] px, int tw, int th, Vector2 a, Vector2 b, float w, Color32 c)
        {
            float r = w / 2f; int ri = Mathf.CeilToInt(r);
            float len = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, r * 0.7f)));
            float r2 = r * r + 0.25f;
            for (int k = 0; k <= n; k++)
            {
                var p = Vector2.Lerp(a, b, k / (float)n);
                int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
                for (int dy = -ri; dy <= ri; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= th) continue;
                    for (int dx = -ri; dx <= ri; dx++)
                    {
                        int x = cx + dx; if (x < 0 || x >= tw) continue;
                        if (dx * dx + dy * dy <= r2) px[y * tw + x] = c;
                    }
                }
            }
        }

        void Row(List<(string id, string name, string desc, string badge)> items, float height, int slots = 3)
        {
            int n = Mathf.Max(slots, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var rt = BigCard(content, it.id, it.name, it.desc, it.badge);
                rt.anchorMin = new Vector2((float)i / n, 1f - height);
                rt.anchorMax = new Vector2((float)(i + 1) / n, 1f);
                rt.offsetMin = new Vector2(i == 0 ? 0f : 10f, 0f);
                rt.offsetMax = new Vector2(i == n - 1 ? 0f : -10f, 0f);
            }
        }

        /// <summary>내용 맨 아래 한 줄(검정). 지도 출처 표시(OpenStreetMap 이용 조건)에 쓴다</summary>
        /// <summary>(11/15 대표: 취급설명서 표기) "직각 후진 주차(양옆 차)" → 두 줄(종류 / 조건)</summary>
        static string TwoLines(string t)
        {
            int i = t.IndexOf('(');
            return i > 0 && t.EndsWith(")") ? t.Substring(0, i) + "\n" + t.Substring(i + 1, t.Length - i - 2) : t;
        }

        void Hint(string text)
        {
            var t = UiKit.Label(content, text, 20, DcAssets.UiText, false, TextAnchor.LowerLeft);
            UiKit.Frac(t.rectTransform, 0f, 0f, 1f, 0.06f);
        }

        void BuildParking()
        {
            float W = root.rect.width;
            if (W < 100f) W = 1080f * Screen.width / Mathf.Max(1, Screen.height);   // 캔버스 크기가 아직 안 잡혔을 때
            W -= 96f;
            // (11/15 대표) 왼쪽 '따라 하기' 카드는 뺐다(배우기는 앱의 주차 연습 화면에서 영상으로)
            float gx = 0f;
            var grid = UiKit.Node("grid", content);
            UiKit.Stretch(grid, gx, 0, 0, 0);
            const float gap = 14f;
            for (int i = 0; i < Stages.COUNT; i++)
            {
                int n = i + 1, col = i % 5, row = i / 5;
                var c = UiKit.Card(grid, "stage " + n, DcAssets.Card, DcAssets.UiLine);
                var b = c.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => onPick?.Invoke("stage:" + n));
                var rt = c.rectTransform;
                rt.anchorMin = new Vector2(col / 5f, 1f - (row + 1) / 5f);
                rt.anchorMax = new Vector2((col + 1) / 5f, 1f - row / 5f);
                rt.offsetMin = new Vector2(col == 0 ? 0 : gap / 2f, row == 4 ? 0 : gap / 2f);
                rt.offsetMax = new Vector2(col == 4 ? 0 : -gap / 2f, row == 0 ? 0 : -gap / 2f);
                // (11/15 대표) 칸에는 번호만
                var num = UiKit.Label(c.transform, n.ToString(), 48, DcAssets.UiText, true, TextAnchor.MiddleCenter);
                UiKit.Stretch(num.rectTransform, 0, 0, 0, 0);
            }
        }
    }
}
