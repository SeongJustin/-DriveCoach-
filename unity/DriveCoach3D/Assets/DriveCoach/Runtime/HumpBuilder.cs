// 담당: S (11/9 새벽, 조사 P1 "과속방지턱 높이대로 차가 튐")
// 과속방지턱: 원호형 단면(길이 3.6 m · 높이 10 cm, 작은 것 7.5 cm)을
// 실제로 솟게 그린다. 윗면은 노랑 바탕에 흰 빗금(우리나라 방지턱 도색), 양 옆은 단면 모양으로 막는다.
// 노면 칠(평평한 방지턱 무늬)은 그 밑에 그대로 있지만 솟은 면이 덮는다(가장자리는 아스팔트보다 2 cm 위 — 칠과 겹쳐 깜빡이지 않게).
// 차가 넘을 때 튀는 것: SceneRig(내 차: 앞·뒤 바퀴 높이로 들림·기울기), TrafficView(다른 차: 가운데 높이).
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class HumpBuilder
    {
        const int Seg = 10;
        const float Lift = 0.05f;   // 아스팔트(h + 0.03)·칠(h + 0.046) 위
        static Texture2D tex;

        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null || d.humps.Count == 0) return;
            var top = new MeshKit();
            var side = new MeshKit();
            var surf = RoadSurface.Load(d.surfaceFile);
            for (int hi = 0; hi < d.humps.Count; hi++)
            {
                var hp = d.humps[hi];
                var ax = hp.dir;                       // 진행 방향
                var ay = new V2(-ax.y, ax.x);          // 가로
                // (11/11 낮, 바닥 검사) 자료의 자리·폭이 길과 어긋나 한쪽은 보도 위로 올라가고 한쪽은 길이 비었다
                //   → 가운데에서 양옆으로 아스팔트가 끝나는 곳을 찾아, 연석에서 12 cm 안쪽까지 길 폭에 맞춘다(자리·폭을 고쳐 차가 들리는 계산도 같이 맞는다).
                if (surf != null && surf.Asphalt(hp.c))
                {
                    float Reach(float sgn)
                    {
                        float k = 0f;
                        while (k < hp.W / 2f + 4f && surf.Asphalt(hp.c + ay * (sgn * (k + 0.1f)))) k += 0.1f;
                        return Mathf.Max(0.3f, k - 0.12f);
                    }
                    float rp = Reach(1f), rm = Reach(-1f);
                    if (rp + rm > 1.5f)
                    {
                        hp.c = hp.c + ay * ((rp - rm) / 2f);
                        hp.W = rp + rm;
                        d.humps[hi] = hp;
                    }
                }
                float L = hp.L, W = hp.W, H = hp.H;
                Vector3 P(float a, float b, float y) => Space3D.W(hp.c + ax * a + ay * b, y);
                for (int i = 0; i < Seg; i++)
                {
                    float u0 = -1f + 2f * i / Seg, u1 = -1f + 2f * (i + 1) / Seg;
                    float a0 = u0 * L / 2f, a1 = u1 * L / 2f;
                    float y0 = h + Lift + H * (1f - u0 * u0), y1 = h + Lift + H * (1f - u1 * u1);
                    // 윗면(UV = 미터: 가로 b, 진행 a — 빗금 그림이 1.8 m 마다 되풀이)
                    top.Quad(P(a0, -W / 2f, y0), P(a0, W / 2f, y0), P(a1, W / 2f, y1), P(a1, -W / 2f, y1), Vector3.up,
                        new Vector2(-W / 2f / 1.8f, a0 / 1.8f), new Vector2(W / 2f / 1.8f, a0 / 1.8f), new Vector2(W / 2f / 1.8f, a1 / 1.8f), new Vector2(-W / 2f / 1.8f, a1 / 1.8f));
                    // 양 옆 단면(위 곡선 ~ 바닥)
                    foreach (var sb in new[] { -1f, 1f })
                    {
                        var outward = Space3D.W(ay * sb, 0f) - Space3D.W(V2.Zero, 0f);
                        side.Quad(P(a0, sb * W / 2f, h + 0.02f), P(a1, sb * W / 2f, h + 0.02f), P(a1, sb * W / 2f, y1), P(a0, sb * W / 2f, y0), outward,
                            Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                }
            }
            top.ToObject("humps", DcAssets.LitTex("hump", Tex(), 0.25f), node, false, true);
            side.ToObject("hump-sides", DcAssets.Lit(DcAssets.Hex("#3E4146"), 0.15f), node, false, true);
            Debug.Log($"HumpBuilder: 과속방지턱 {d.humps.Count}");
        }

        /// <summary>노랑 바탕 + 흰 빗금(0.9 m 마다 절반이 흰색, 비스듬히) — 1.8 m × 1.8 m 한 장, 되풀이</summary>
        static Texture2D Tex()
        {
            if (tex != null) return tex;
            const int S = 128;
            var px = new Color32[S * S];
            var yellow = new Color32(226, 179, 60, 255);
            var white = new Color32(236, 236, 230, 255);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float mx = x / (float)S * 1.8f, my = y / (float)S * 1.8f;
                    // 빗금: (x + y/2) 를 0.9 m 로 나눈 나머지가 0.45 m 밑이면 흰색(1.8 m 에서 끊김 없이 이어진다)
                    float t = (mx + my * 0.5f) % 0.9f;
                    px[y * S + x] = t < 0.45f ? white : yellow;
                }
            tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "dc-hump" };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
