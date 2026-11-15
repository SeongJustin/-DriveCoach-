// 담당: S (11/8 밤, 대표: "표지판 신호등 이런 것도 운전에 영향을 미치는 건 다 들어가야겠지")
// 내 동네 지도: 서울 T-GIS 의 실제 자리 안전표지·신호등을 세운다.
// 표지: 기둥(회색) + 표지판.
//         원·삼각형·역삼각형·사각형·보조판 모양 그대로 면을 잘라 만들어 투명 처리 없이 그린다. 뒷면은 회색.
//         위에서부터 본표지 → 보조표지(4xx). 그림 모음에 없는 번호는 빼고, 남는 게 없으면 기둥도 세우지 않는다.
//   신호등: 가로 팔(측주식 횡형) 끝에 가로 3·4색 등, 세로(측주식 종형)는 기둥에 세로 등, 보행등은 2색.
//         가장 가까운 게임 신호(같은 쪽을 보는 것)와 짝지어 매 프레임 불을 켠다(Tick). 보행등은 같은 방향 차 신호가 초록일 때 초록.
// 모두 메시를 종류별로 합쳐 그리기 횟수를 줄인다(신호 불빛만 신호 묶음마다 따로).
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class SignBuilder
    {
        const float Size = 0.6f;      // 표지판 한 칸(m): 원 지름 약 0.56 m
        const float Top = 3.0f;       // 맨 위 표지 윗 끝 높이(보도 위)

        static Dictionary<string, (int cell, string shape)> meta;
        static int cols = 8;

        static bool LoadMeta()
        {
            if (meta != null) return meta.Count > 0;
            meta = new Dictionary<string, (int, string)>();
            var ta = Resources.Load<TextAsset>("DC/Signs/signs");
            if (ta == null) return false;
            var o = MiniJson.ParseObject(ta.text);
            if (o == null) return false;
            cols = (int)MiniJson.Num(o, "cols", 8);
            if (o.TryGetValue("signs", out var so) && so is Dictionary<string, object> sd)
                foreach (var kv in sd)
                    if (kv.Value is List<object> a && a.Count >= 2)
                        meta[kv.Key] = (System.Convert.ToInt32(a[0]), System.Convert.ToString(a[1]));
            return meta.Count > 0;
        }

        /// <summary>모양 윤곽(칸 안 0..1 좌표, 위가 0)</summary>
        static List<Vector2> Outline(string shape)
        {
            var o = new List<Vector2>();
            switch (shape)
            {
                case "tri":
                    o.Add(new Vector2(0.5f, 0.0716f)); o.Add(new Vector2(0.96f, 0.8683f)); o.Add(new Vector2(0.04f, 0.8683f));
                    break;
                case "inv":
                    o.Add(new Vector2(0.04f, 0.0816f)); o.Add(new Vector2(0.96f, 0.0816f)); o.Add(new Vector2(0.5f, 0.8783f));
                    break;
                case "sq":
                    o.Add(new Vector2(0.05f, 0.05f)); o.Add(new Vector2(0.95f, 0.05f)); o.Add(new Vector2(0.95f, 0.95f)); o.Add(new Vector2(0.05f, 0.95f));
                    break;
                case "aux":
                    o.Add(new Vector2(0.02f, 0.25f)); o.Add(new Vector2(0.98f, 0.25f)); o.Add(new Vector2(0.98f, 0.75f)); o.Add(new Vector2(0.02f, 0.75f));
                    break;
                case "rh":   // 가로 직사각(일방통행 좌·우)
                    o.Add(new Vector2(0.03f, 0.27f)); o.Add(new Vector2(0.97f, 0.27f)); o.Add(new Vector2(0.97f, 0.73f)); o.Add(new Vector2(0.03f, 0.73f));
                    break;
                case "oct":  // 팔각형(일시정지, 현행)
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (22.5f + 45f * i) * Mathf.Deg2Rad;
                        o.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.47f, 0.5f + Mathf.Sin(a) * 0.47f));
                    }
                    break;
                case "rv":   // 세로 직사각(일방통행 직진)
                    o.Add(new Vector2(0.27f, 0.03f)); o.Add(new Vector2(0.73f, 0.03f)); o.Add(new Vector2(0.73f, 0.97f)); o.Add(new Vector2(0.27f, 0.97f));
                    break;
                default:   // circle
                    for (int i = 0; i < 28; i++)
                    {
                        float a = i / 28f * Mathf.PI * 2f;
                        o.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.47f, 0.5f - Mathf.Sin(a) * 0.47f));
                    }
                    break;
            }
            return o;
        }

        static (float top, float bottom) Extent(string shape)
        {
            switch (shape)
            {
                case "tri": return (0.0716f, 0.8683f);
                case "inv": return (0.0816f, 0.8783f);
                case "sq": return (0.05f, 0.95f);
                case "aux": return (0.25f, 0.75f);
                case "rh": return (0.27f, 0.73f);
                case "oct": return (0.066f, 0.934f);
                case "rv": return (0.03f, 0.97f);
                default: return (0.03f, 0.97f);
            }
        }

        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null || d.signs.Count == 0 || !LoadMeta()) return;
            var tex = Resources.Load<Texture2D>("DC/Signs/signs");
            if (tex == null) return;
            var face = new MeshKit();
            var back = new MeshKit();
            var poles = new MeshKit();
            var surf = CityBuilder.Current;
            int n = 0;
            float cellUV = 1f / cols;
            foreach (var s in d.signs)
            {
                var known = new List<string>();
                foreach (var c in s.codes) if (meta.ContainsKey(c)) known.Add(c);
                if (known.Count == 0) continue;
                var pos = s.p;
                float y0 = h;
                if (surf != null)
                {
                    pos = surf.PushOff(pos, 0.3f);
                    if (surf.Sidewalk(pos)) y0 = h + CityBuilder.WalkH;
                }
                float a = s.faceDeg * Mathf.Deg2Rad;
                var f2 = new V2(Mathf.Cos(a), Mathf.Sin(a));               // 2D 바라보는 쪽
                var fw = new Vector3(f2.x, 0f, -f2.y);                     // 월드
                // 표지를 마주 본 사람의 오른쪽. (11/11) 예전에는 Cross(up, fw) = 마주 본 사람의 왼쪽이라 표지 그림·글자가 좌우로 뒤집혀 보였다(사진에서 "50"이 거울상)
                var right = Vector3.Cross(fw, Vector3.up).normalized;
                var basePos = Space3D.W(pos, y0) + fw * 0.06f;
                float yTop = y0 + Top;
                foreach (var code in known)
                {
                    var (cell, shape) = meta[code];
                    var (et, eb) = Extent(shape);
                    float hgt = (eb - et) * Size;
                    // 칸 윗 끝(0)이 yTop + et*Size 에 오게
                    float cellTop = yTop + et * Size;
                    int cx = cell % cols, cy = cell / cols;
                    var outline = Outline(shape);
                    var pts = new List<Vector3>();
                    var uvs = new List<Vector2>();
                    foreach (var q in outline)
                    {
                        pts.Add(basePos + right * ((q.x - 0.5f) * Size) + Vector3.up * (cellTop - q.y * Size));
                        uvs.Add(new Vector2((cx + q.x) * cellUV, 1f - (cy + q.y) * cellUV));
                    }
                    var cen = Vector3.zero; var cuv = Vector2.zero;
                    foreach (var p in pts) cen += p;
                    foreach (var u in uvs) cuv += u;
                    cen /= pts.Count; cuv /= uvs.Count;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        int j = (i + 1) % pts.Count;
                        // 앞면(fw 쪽) / 뒷면(조금 뒤, 반대쪽)
                        face.Tri(cen, pts[i], pts[j], cuv, uvs[i], uvs[j], fw);
                        back.Tri(cen - fw * 0.012f, pts[j] - fw * 0.012f, pts[i] - fw * 0.012f, Vector2.zero, Vector2.zero, Vector2.zero, -fw);
                    }
                    yTop -= hgt + 0.05f;
                }
                float poleH = (y0 + Top + 0.05f) - y0;
                poles.Box(Space3D.W(pos, y0 + poleH / 2f), new Vector3(0.07f, poleH, 0.07f), 0f);
                n++;
            }
            if (n == 0) return;
            face.ToObject("signs", DcAssets.LitTex("signs-atlas", tex, 0.35f), node, false, true);
            back.ToObject("signs-back", DcAssets.Lit(DcAssets.Hex("#8C9096"), 0.3f, 0.4f), node, false, true);
            poles.ToObject("sign-poles", DcAssets.Lit(DcAssets.Hex("#9EA3A8"), 0.4f, 0.6f), node, true, true);
            Debug.Log($"SignBuilder: 표지 기둥 {n}개");
        }
    }

    /// <summary>T-GIS 신호등(실제 자리). 게임 신호와 짝지어 불을 켠다.</summary>
    public class SignalHeads
    {
        public static SignalHeads Current;
        readonly List<(int sig, bool ped, Renderer[] lamps)> groups = new List<(int, bool, Renderer[])>();
        static Material[] onM, offM, pedOn, pedOff;

        public static bool Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            Current = null;
            if (d == null || d.signalHeads.Count == 0) return false;
            var sh = new SignalHeads();
            var hous = new MeshKit();
            var poles = new MeshKit();
            var surf = CityBuilder.Current;
            // 게임 신호 묶음(sig) × 등 색(0 빨강 1 노랑 2 초록 3 왼쪽 화살표) 마다 메시
            var lampMk = new Dictionary<(int, bool, int), MeshKit>();
            int placed = 0;
            foreach (var hd in d.signalHeads)
            {
                bool ped = hd.lamp == 7;
                int sig = Nearest(r, hd, ped);
                var pole = hd.pole;
                float y0 = h;
                if (surf != null)
                {
                    pole = surf.PushOff(pole, 0.3f);
                    if (surf.Sidewalk(pole)) y0 = h + CityBuilder.WalkH;
                }
                float a = hd.faceDeg * Mathf.Deg2Rad;
                var fw = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                // 신호등을 마주 본 사람의 오른쪽. (11/11) 예전에는 반대라 가로 신호등이 왼쪽부터 초록·화살표·노랑·빨강으로 뒤집혀 있었다(실제: 왼쪽부터 빨강·노랑·화살표·초록)
                var right = Vector3.Cross(fw, Vector3.up).normalized;
                bool arm = hd.kind != 2 && !ped && hd.pole.DistanceTo(hd.head) > 1.0f;
                float poleH = arm ? 5.6f : (ped ? 2.9f : 4.2f);
                // 11/9 대표 "최상급으로": 둥근 기둥(지름 0.22 m, 16각) + 밑판(0.5 m 네모·두께 5 cm)
                poles.Prism(Ring(pole, 0.11f, 16), y0, y0 + poleH);
                poles.Box(Space3D.W(pole, y0 + 0.025f), new Vector3(0.5f, 0.05f, 0.5f), hd.faceDeg);
                Vector3 hc;
                int nl = hd.lamp == 3 ? 4 : (ped ? 2 : 3);
                bool horizontal = arm;
                if (arm)
                {
                    var p0 = Space3D.W(pole, y0 + 5.4f);
                    var p1 = Space3D.W(hd.head, y0 + 5.4f);
                    var mid = (p0 + p1) / 2f;
                    float len = Vector3.Distance(p0, p1);
                    float yaw = Mathf.Atan2(p1.x - p0.x, p1.z - p0.z) * Mathf.Rad2Deg;
                    poles.Box(mid, new Vector3(0.12f, 0.12f, len), yaw);
                    hc = p1 + Vector3.down * 0.45f;
                }
                else
                {
                    hc = Space3D.W(pole, y0 + (ped ? 2.3f : 3.2f)) + fw * 0.2f;
                }
                float lampR = ped ? 0.11f : 0.13f, pitch = ped ? 0.36f : lampR * 2.5f;
                var size = horizontal ? new Vector3(pitch * nl + 0.12f, 0.38f, 0.28f) : new Vector3(0.38f, pitch * nl + 0.12f, 0.28f);
                // 상자: 바라보는 쪽 기준(가로 = right, 세로 = up)
                float yawH = Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg;
                hous.Box(hc, horizontal ? new Vector3(size.x, size.y, size.z) : new Vector3(size.x, size.y, size.z), yawH);
                for (int i = 0; i < nl; i++)
                {
                    // 가로: 왼쪽부터 빨강·노랑·(화살표)·초록 / 세로: 위부터 / 보행등: 위 빨강·아래 초록
                    float o = (i - (nl - 1) / 2f) * pitch;
                    var lc = hc + fw * (size.z / 2f + 0.01f) + (horizontal ? right * o : Vector3.up * -o);
                    int col = ped ? (i == 0 ? 0 : 2) : (nl == 4 ? new[] { 0, 1, 3, 2 }[i] : i);
                    var key = (sig, ped, col);
                    if (!lampMk.TryGetValue(key, out var mk)) lampMk[key] = mk = new MeshKit();
                    if (ped) Square(mk, lc, fw, right, 0.15f);   // 보행등: 네모 등 안에 사람 그림(빨강 선 사람·초록 걷는 사람)
                    else Disc(mk, lc, fw, right, lampR);
                    // 차양(등마다 위쪽을 덮는 반원 갓, 앞으로 0.2 m) — 낮에 불빛이 햇빛에 씻기지 않게 하는 우리나라 신호등 모양
                    Visor(hous, lc, fw, right, lampR + 0.025f, ped ? 0.14f : 0.2f);
                }
                placed++;
            }
            hous.ToObject("signal-housings", DcAssets.Lit(DcAssets.Hex("#15171A"), 0.5f, 0.3f), node, true, true);
            poles.ToObject("signal-poles", DcAssets.Lit(DcAssets.Hex("#A2A7AC"), 0.4f, 0.6f), node, true, true);
            if (onM == null)
            {
                // (11/11 대표: "불도 안 켜진 것 같다") 켜진 등은 1 을 넘는 밝기로 → 빛 번짐(블룸)이 걸려 멀리서도 켜진 것이 보인다. 꺼진 등·모양은 그대로
                const float G = 2.4f;
                onM = new[] { DcAssets.Unlit(new Color(1f * G, 0.12f * G, 0.08f * G)), DcAssets.Unlit(new Color(1f * G, 0.75f * G, 0.1f * G)), DcAssets.Unlit(new Color(0.1f * G, 1f * G, 0.55f * G)), DcAssets.Unlit(new Color(0.1f * G, 1f * G, 0.55f * G)) };
                offM = new[] { DcAssets.Unlit(new Color(0.22f, 0.04f, 0.04f)), DcAssets.Unlit(new Color(0.22f, 0.17f, 0.04f)), DcAssets.Unlit(new Color(0.04f, 0.2f, 0.12f)), DcAssets.Unlit(new Color(0.04f, 0.2f, 0.12f)) };
            }
            var bySig = new Dictionary<(int, bool), Renderer[]>();
            if (pedOn == null)
            {
                pedOn = new[] { DcAssets.UnlitTex("DC/Signs/ped_stop", new Color(1f, 0.15f, 0.1f)), null, DcAssets.UnlitTex("DC/Signs/ped_walk", new Color(0.15f, 1f, 0.6f)), null };
                pedOff = new[] { DcAssets.UnlitTex("DC/Signs/ped_stop", new Color(0.2f, 0.05f, 0.05f)), null, DcAssets.UnlitTex("DC/Signs/ped_walk", new Color(0.04f, 0.18f, 0.11f)), null };
            }
            foreach (var kv in lampMk)
            {
                var (sig, ped, col) = kv.Key;
                var go = kv.Value.ToObject($"lamps-{sig}-{(ped ? "p" : "c")}-{col}", ped && pedOff[col] != null ? pedOff[col] : offM[col], node, false, false);
                if (!bySig.TryGetValue((sig, ped), out var arr)) bySig[(sig, ped)] = arr = new Renderer[4];
                arr[col] = go.GetComponent<Renderer>();
            }
            foreach (var kv in bySig) sh.groups.Add((kv.Key.Item1, kv.Key.Item2, kv.Value));
            Current = sh;
            Debug.Log($"SignalHeads: 신호등 {placed}개, 불빛 묶음 {sh.groups.Count}");
            return placed > 0;
        }

        /// <summary>같은 쪽을 보는(보행등은 같은 방향 또는 반대 방향) 가장 가까운 게임 신호(45 m 안). 없으면 -1(늘 빨강 깜빡임 없이 꺼짐)</summary>
        static int Nearest(RoadDef r, RegionSignalHead hd, bool ped)
        {
            int best = -1;
            float bd = 45f;
            for (int i = 0; i < r.signals.Count; i++)
            {
                var sg = r.signals[i];
                float da = Mathf.Abs(Mathf.DeltaAngle(sg.facingDeg, hd.faceDeg));
                if (ped) da = Mathf.Min(da, 180f - da);
                if (da > 50f) continue;
                float dist = sg.pos.DistanceTo(hd.pole);
                if (dist < bd) { bd = dist; best = i; }
            }
            return best;
        }

        static V2[] Ring(V2 c, float r, int n)
        {
            var o = new V2[n];
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; o[i] = c + new V2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
            return o;
        }

        /// <summary>등 위 반원 갓: 앞쪽(fw)으로 depth 만큼 나온 얇은 반원통(양면)</summary>
        static void Visor(MeshKit mk, Vector3 c, Vector3 fw, Vector3 right, float r, float depth)
        {
            const int seg = 8;
            var up = Vector3.Cross(fw, right).normalized;
            if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI, a1 = (i + 1) / (float)seg * Mathf.PI;
                var d0 = right * Mathf.Cos(a0) + up * Mathf.Sin(a0);
                var d1 = right * Mathf.Cos(a1) + up * Mathf.Sin(a1);
                var b0 = c + d0 * r; var b1 = c + d1 * r;
                var f0 = b0 + fw * depth; var f1 = b1 + fw * depth;
                var nrm = ((d0 + d1) * 0.5f).normalized;
                mk.Quad(b0, b1, f1, f0, nrm, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                mk.Quad(b1, b0, f0, f1, -nrm, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
        }

        /// <summary>등 면 네모(그림 uv 0..1, 바라보는 쪽에서 바로 보이게)</summary>
        static void Square(MeshKit mk, Vector3 c, Vector3 n, Vector3 right, float h)
        {
            var up = Vector3.Cross(n, right).normalized;
            if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
            var l = right;    // right = 마주 본 사람의 오른쪽(11/11 부터)
            mk.Quad(c - l * h - up * h, c + l * h - up * h, c + l * h + up * h, c - l * h + up * h, n,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        static void Disc(MeshKit mk, Vector3 c, Vector3 n, Vector3 right, float r)
        {
            const int seg = 14;
            var up = Vector3.Cross(n, right).normalized;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                var p0 = c + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * r;
                var p1 = c + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * r;
                mk.Tri(c, p0, p1, Vector2.zero, Vector2.zero, Vector2.zero, n);
            }
        }

        public void Tick(World w)
        {
            var sigs = w.st.road != null ? w.st.road.signals : null;
            foreach (var (sig, ped, lamps) in groups)
            {
                SignalColor c = SignalColor.Red, lc = SignalColor.Red;
                bool blinkOff = false;
                if (sigs != null && sig >= 0 && sig < sigs.Count)
                {
                    var sgl = sigs[sig];
                    c = sgl.At(w.t);
                    lc = sgl.LeftAt(w.t);
                    // 보행등: 초록 끝 무렵(남은 초록이 초록의 40 % 밑)이면 1초에 한 번 깜빡(우리나라 보행 녹색 점멸)
                    if (ped && c == SignalColor.Green && sgl.GreenLeft(w.t) < sgl.green * 0.4f) blinkOff = (w.t % 1f) > 0.5f;
                }
                for (int k = 0; k < 4; k++)
                {
                    if (lamps[k] == null) continue;
                    bool on;
                    if (ped) on = (k == 2 && c == SignalColor.Green && !blinkOff) || (k == 0 && c != SignalColor.Green);
                    else if (sig < 0) on = false;
                    // 4색(빨강·노랑·좌회전 화살표·초록): 화살표 단계에는 빨강과 화살표가 함께 켜진다
                    else on = (k == 0 && c == SignalColor.Red) || (k == 1 && (c == SignalColor.Yellow || (c == SignalColor.Red && lc == SignalColor.Yellow)))
                              || (k == 2 && c == SignalColor.Green) || (k == 3 && lc == SignalColor.Green);
                    var m = ped && pedOn[k] != null ? (on ? pedOn[k] : pedOff[k]) : (on ? onM[k] : offM[k]);
                    if (lamps[k].sharedMaterial != m) lamps[k].sharedMaterial = m;
                }
            }
        }
    }
}
