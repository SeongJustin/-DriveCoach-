// 담당: S (11/9 새벽, 조사 P1 "횡단보도 잔여시간 표시기")
// 서울 T-GIS 잔여시간표시기 자리(지역 JSON "cd")에 기둥 + 검은 함(앞면에 빨간 LED 두 자리 숫자).
// 그 앞 횡단보도의 보행 신호(= 그 길 차 신호 빨간불) 동안 남은 초를 보여 주고, 아니면 꺼진다(어두운 칸).
// 운전자에게도 보인다(신호가 곧 바뀔지 가늠 — 실제 도로처럼). 숫자는 한 메시의 UV 만 바꿔 가볍게.
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class CountdownView
    {
        readonly List<int> signal = new List<int>();
        readonly Mesh digits;
        readonly Vector2[] uv;
        readonly int[] shown;
        readonly RoadDef road;
        float tick;
        static Texture2D atlas;

        public CountdownView(Transform parent, RoadDef r, float h)
        {
            road = r;
            var d = r.region;
            var node = new GameObject("Countdowns").transform;
            node.SetParent(parent, false);
            var body = new MeshKit();
            var face = new List<Vector3>();
            var cp = new CrossPeds(r, 0f);      // 횡단보도 ↔ 신호 짝(사람은 만들지 않는다)
            foreach (var (p, ang) in d.countdowns)
            {
                // 가장 가까운 횡단보도(25 m 안)의 신호
                int sig = -1; float best = 25f;
                foreach (var c in cp.crossings) { float dd = c.c.DistanceTo(p); if (dd < best) { best = dd; sig = c.signal; } }
                signal.Add(sig);
                float yaw = Space3D.Yaw(ang);
                var fwd = Space3D.Rot(ang) * Vector3.forward;
                var right = Vector3.Cross(Vector3.up, -fwd).normalized;     // 앞에서 보는 사람의 오른쪽
                var c0 = Space3D.W(p, h);
                body.Box(c0 + Vector3.up * 1.4f, new Vector3(0.1f, 2.8f, 0.1f), yaw);                // 기둥
                var box = c0 + Vector3.up * 2.25f + fwd * 0.08f;
                body.Box(box, new Vector3(0.42f, 0.5f, 0.14f), yaw);                                 // 함
                // 숫자 판 두 칸(앞면 1 cm 앞)
                var fc = box + fwd * 0.075f;
                foreach (var sx in new[] { -0.095f, 0.095f })
                {
                    var q = fc + right * sx;
                    face.Add(q - right * 0.085f - Vector3.up * 0.15f);
                    face.Add(q + right * 0.085f - Vector3.up * 0.15f);
                    face.Add(q + right * 0.085f + Vector3.up * 0.15f);
                    face.Add(q - right * 0.085f + Vector3.up * 0.15f);
                }
            }
            if (signal.Count == 0) return;
            body.ToObject("countdown-body", DcAssets.Lit(DcAssets.Hex("#1E2023"), 0.35f, 0.2f), node, true, true);
            digits = new Mesh { name = "countdown-digits" };
            if (face.Count > 65000) digits.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            digits.SetVertices(face);
            var tri = new int[face.Count / 4 * 6];
            for (int i = 0, v = 0; v < face.Count; v += 4)
            {
                tri[i++] = v; tri[i++] = v + 2; tri[i++] = v + 1; tri[i++] = v; tri[i++] = v + 3; tri[i++] = v + 2;
            }
            uv = new Vector2[face.Count];
            shown = new int[signal.Count];
            for (int k = 0; k < shown.Length; k++) { shown[k] = -2; SetDigits(k, -1); }
            digits.uv = uv;
            digits.triangles = tri;
            digits.RecalculateNormals();
            digits.RecalculateBounds();
            var go = new GameObject("countdown-digits");
            go.transform.SetParent(node, false);
            go.AddComponent<MeshFilter>().sharedMesh = digits;
            var mr = go.AddComponent<MeshRenderer>();
            var m = new Material(DcAssets.Unlit(Color.white));
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Atlas());
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Atlas());
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>값 -1 = 꺼짐(두 칸 다 어두운 칸 10), 0..99 = 두 자리(한 자리면 앞 칸 어둡게)</summary>
        void SetDigits(int k, int value)
        {
            int a = value < 0 ? 10 : (value >= 10 ? value / 10 : 10), b = value < 0 ? 10 : value % 10;
            Cell(k * 8, a);
            Cell(k * 8 + 4, b);
        }

        void Cell(int v, int digit)
        {
            float u0 = digit / 11f, u1 = (digit + 1) / 11f;
            uv[v] = new Vector2(u0, 0f); uv[v + 1] = new Vector2(u1, 0f); uv[v + 2] = new Vector2(u1, 1f); uv[v + 3] = new Vector2(u0, 1f);
        }

        public void Tick(float t, float dt)
        {
            if (digits == null) return;
            tick -= dt;
            if (tick > 0f) return;
            tick = 0.25f;
            bool changed = false;
            for (int k = 0; k < signal.Count; k++)
            {
                int s = signal[k];
                int val = -1;
                if (s >= 0 && s < road.signals.Count)
                {
                    float left = road.signals[s].PedLeft(t);
                    if (left > 0.5f) val = Mathf.Min(99, Mathf.CeilToInt(left));
                }
                if (val != shown[k]) { shown[k] = val; SetDigits(k, val); changed = true; }
            }
            if (changed) digits.uv = uv;
        }

        /// <summary>숫자 그림: 0~9 + 꺼진 칸(11칸 가로). 7분절 빨간 LED, 꺼진 분절은 아주 어둡게</summary>
        static Texture2D Atlas()
        {
            if (atlas != null) return atlas;
            const int W = 32, H = 56;
            var px = new Color32[W * 11 * H];
            var on = new Color32(255, 54, 40, 255); var off = new Color32(40, 12, 10, 255); var bg = new Color32(8, 8, 8, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            // 분절: a 위, b 오른쪽 위, c 오른쪽 아래, d 아래, e 왼쪽 아래, f 왼쪽 위, g 가운데
            int[] segs = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F, 0x00 };
            for (int dgt = 0; dgt < 11; dgt++)
            {
                int ox = dgt * W;
                for (int sg = 0; sg < 7; sg++)
                {
                    bool lit = (segs[dgt] & (1 << sg)) != 0;
                    var col = lit ? on : off;
                    int x0, y0, x1, y1;
                    switch (sg)
                    {
                        case 0: x0 = 8; x1 = 24; y0 = 46; y1 = 50; break;
                        case 1: x0 = 23; x1 = 27; y0 = 29; y1 = 47; break;
                        case 2: x0 = 23; x1 = 27; y0 = 8; y1 = 26; break;
                        case 3: x0 = 8; x1 = 24; y0 = 5; y1 = 9; break;
                        case 4: x0 = 5; x1 = 9; y0 = 8; y1 = 26; break;
                        case 5: x0 = 5; x1 = 9; y0 = 29; y1 = 47; break;
                        default: x0 = 8; x1 = 24; y0 = 26; y1 = 30; break;
                    }
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) px[y * W * 11 + ox + x] = col;
                }
            }
            atlas = new Texture2D(W * 11, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "dc-countdown" };
            atlas.SetPixels32(px);
            atlas.Apply(false, true);
            return atlas;
        }
    }
}
