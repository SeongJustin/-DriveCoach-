// 담당: S (11/8 밤, 대표: "설정에서 날씨랑 시간 이런 거 다 조절할 수 있게 … 비슷한 게임보다 디테일하게")
// 밤(해질녘 궂은 날 포함) 불빛: 가로등 아래 노면에 번지는 빛 웅덩이, 다른 차 전조등·전조등이 비추는 노면, 미등.
// 실제 조명(Light)을 수백 개 켜면 차 안 기기에서 느려지므로, 빛이 닿는 자리를 투명한 판(가운데가 밝고 가장자리로 흐려지는 그림)
// 한 덩이로 그려 흉내 낸다. 켜고 끄는 건 SceneRig.SetEnvironment 가 Set(…) 으로.
using System.Collections.Generic;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class NightLights
    {
        public static bool On { get; private set; }
        static readonly List<GameObject> objs = new List<GameObject>();
        static Texture2D glowTex;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// <summary>판을 새로 짓기 전에(지난 판의 것 잊기)</summary>
        public static void Reset() { objs.Clear(); }

        /// <summary>밤에만 보이는 것 등록(지금 상태로 맞춘다)</summary>
        public static void Add(GameObject go)
        {
            if (go == null) return;
            objs.Add(go);
            go.SetActive(On);
        }

        public static void Set(bool on)
        {
            On = on;
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (objs[i] == null) { objs.RemoveAt(i); continue; }
                objs[i].SetActive(on);
            }
        }

        /// <summary>가운데가 1, 가장자리로 갈수록 0 이 되는 둥근 그림(알파). 64×64, 코드로 한 번 만든다.</summary>
        public static Texture2D GlowTex()
        {
            if (glowTex != null) return glowTex;
            const int S = 64;
            glowTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "dc-glow" };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);    // 부드럽게
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            glowTex.SetPixels32(px);
            glowTex.Apply(false, true);
            return glowTex;
        }

        /// <summary>빛 웅덩이 재질(조명 안 받음, 투명, 그림 = GlowTex, 색 c(알파 = 가장 밝은 곳 세기))</summary>
        public static Material Glow(Color c)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c);
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(DcAssets.Unlit(new Color(1f, 1f, 1f, 0.5f)));
            DcAssets.Tint(m, c);
            var t = GlowTex();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
            m.renderQueue = 3001;
            mats[key] = m;
            return m;
        }

        /// <summary>바닥에 눕힌 빛 판(가운데 c, 반지름 rx·rz, 방향 yaw). u 0..1 × v v0..v1 범위의 그림을 쓴다.</summary>
        public static void Pool(MeshKit mk, Vector3 c, float rx, float rz, float yawDeg, float v0 = 0f, float v1 = 1f)
        {
            var q = Quaternion.Euler(0f, yawDeg, 0f);
            var ax = q * Vector3.right * rx;
            var az = q * Vector3.forward * rz;
            mk.Quad(c - ax - az, c + ax - az, c + ax + az, c - ax + az, Vector3.up,
                new Vector2(0f, v0), new Vector2(1f, v0), new Vector2(1f, v1), new Vector2(0f, v1));
        }
    }
}
