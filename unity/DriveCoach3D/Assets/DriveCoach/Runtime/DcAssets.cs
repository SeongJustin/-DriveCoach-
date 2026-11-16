// 3D 주차 연습 — 재질·모델·글꼴·하늘 불러오기. Resources/DC 아래, 없으면 단순한 대체물
using System.Collections.Generic;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class DcAssets
    {
        // 앱(CoachTheme)과 같은 색
        public static readonly Color Blue = Hex("#1E6FE8");
        public static readonly Color Red = Hex("#D93025");
        public static readonly Color Green = Hex("#1E8E3E");
        public static readonly Color Orange = Hex("#F29900");
        public static readonly Color Yellow = Hex("#E2B33C");
        public static readonly Color Text = Hex("#111111");
        public static readonly Color Outline = Hex("#D9DEE6");
        public static readonly Color Paint = Hex("#F2F2EE");

        // 화면(메뉴·HUD)은 검정·흰색 바탕에 중요한 곳만 라임 그린(#0B0B0C · #FFFFFF · #C6F432). 다른 색은 쓰지 않는다.
        // 흰색과 검정 중 더 넓게 쓰는 색은 흰색 → 흰 바탕·검정 글씨. 라임은 채운 면에만(글씨 강조는 검정 굵게).
        public static readonly Color Bg = Hex("#FFFFFF");
        public static readonly Color Card = Hex("#F4F4F5");
        public static readonly Color Lime = Hex("#C6F432");
        public static readonly Color OnLime = Hex("#0B0B0C");
        public static readonly Color UiText = Hex("#0B0B0C");
        public static readonly Color UiSub = Hex("#5F5F66");
        public static readonly Color UiLine = Hex("#D8D8DD");

        static Sprite glow;
        /// <summary>라임 빛이 한쪽 구석에서 번지는 바탕(검정 → 라임 그라데이션). 메뉴 바탕에 옅게 깐다.</summary>
        public static Sprite Glow()
        {
            if (glow != null) return glow;
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (N - 1 - x) / (float)N, dy = y / (float)N;      // 오른쪽 아래 구석이 가장 밝다
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d / 0.95f);
                    a = a * a * a * 0.5f;
                    // 8비트 투명도 계단(띠)이 보이지 않게 픽셀마다 아주 작은 흔들림을 섞는다
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    a = Mathf.Clamp01(a + ((h & 0xFFFF) / 65535f - 0.5f) / 255f);
                    px[y * N + x] = new Color(Lime.r, Lime.g, Lime.b, a);
                }
            t.SetPixels32(px); t.Apply(false, true);
            glow = Sprite.Create(t, new UnityEngine.Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return glow;
        }

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static T Load<T>(string path) where T : Object
        {
            if (cache.TryGetValue(path, out var o)) return o as T;
            var r = Resources.Load<T>(path);
            cache[path] = r;
            return r;
        }

        static Material Base(string name, string fallbackShader, bool transparent = false)
        {
            var m = Load<Material>("DC/Materials/" + name);
            if (m != null) return m;
            var sh = Shader.Find(fallbackShader) ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var nm = new Material(sh);
            if (transparent) nm.renderQueue = 3000;
            cache["DC/Materials/" + name] = nm;
            return nm;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>단색 Lit(조명 받는) 재질. smooth = 매끈함(0..1)</summary>
        public static Material Lit(Color c, float smooth = 0.25f, float metal = 0f)
        {
            string key = "lit" + ColorUtility.ToHtmlStringRGBA(c) + smooth.ToString("0.00") + metal.ToString("0.00");
            if (mats.TryGetValue(key, out var m)) return m;
            m = new Material(Base("DC_Lit", "Universal Render Pipeline/Lit"));
            SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            mats[key] = m;
            return m;
        }

        /// <summary>질감(코드로 만든 건물 외벽 등)을 입힌 조명 재질. key 로 한 번만 만든다.</summary>
        public static Material LitTex(string key, Texture2D tex, float smooth = 0.2f, float metal = 0f)
        {
            if (mats.TryGetValue("tex" + key, out var m)) return m;
            m = new Material(Base("DC_Lit", "Universal Render Pipeline/Lit"));
            SetColor(m, Color.white);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            mats["tex" + key] = m;
            return m;
        }

        /// <summary>Poly Haven 바닥 질감(색·노멀·매끈함). name = garage_floor | asphalt_03 | asphalt_floor. 없으면 단색.</summary>
        public static Material Ground(string name, Color fallback, Color tint)
        {
            string key = "ground" + name + ColorUtility.ToHtmlStringRGBA(tint);
            if (mats.TryGetValue(key, out var m)) return m;
            var al = Load<Texture2D>("DC/PolyHaven/Textures/" + name + "_albedo");
            if (al == null) { m = Lit(fallback, 0.15f); mats[key] = m; return m; }
            m = new Material(Base("DC_LitNormal", "Universal Render Pipeline/Lit"));
            SetColor(m, tint);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", al);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", al);
            var n = Load<Texture2D>("DC/PolyHaven/Textures/" + name + "_normal");
            if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            var ms = Load<Texture2D>("DC/PolyHaven/Textures/" + name + "_metalsmooth");
            if (ms != null && m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            mats[key] = m;
            return m;
        }

        /// <summary>
        /// Poly Haven(CC0) 사진 질감 세트(DC/CC0/이름_albedo·_normal·_metalsmooth). 없으면 단색 fallback.
        /// smooth = 매끈함 배율(질감의 매끈함 × smooth), tint = 색 곱.
        /// </summary>
        public static Material Pbr(string name, Color fallback, Color tint, float smooth = 1f)
        {
            string key = "pbr" + name + ColorUtility.ToHtmlStringRGBA(tint) + smooth.ToString("0.00");
            if (mats.TryGetValue(key, out var m)) return m;
            var al = Load<Texture2D>("DC/CC0/" + name + "_albedo");
            if (al == null) { m = Lit(fallback, 0.2f * smooth); mats[key] = m; return m; }
            m = new Material(Base("DC_LitNormal", "Universal Render Pipeline/Lit"));
            SetColor(m, tint);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", al);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", al);
            var n = Load<Texture2D>("DC/CC0/" + name + "_normal");
            if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            var ms = Load<Texture2D>("DC/CC0/" + name + "_metalsmooth");
            if (ms != null && m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            // 금속도 지도가 있으면 매끈함 = 지도 알파 × _Smoothness(배율)
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", ms != null ? smooth : 0.2f * smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            mats[key] = m;
            return m;
        }

        /// <summary>
        /// 투명 자르기(잎·울타리 그물) 재질: 질감 알파 0.5 밑은 버리고 앞뒤 두 면을 그린다(DC_LitCutout — 빌드에 변형이 들어가게 미리 만든 재질).
        /// 질감이 없으면 fallback 단색.
        /// </summary>
        public static Material Cutout(string texPath, Color tint, Color fallback)
        {
            string key = "cut" + texPath + ColorUtility.ToHtmlStringRGBA(tint);
            if (mats.TryGetValue(key, out var m)) return m;
            var tex = Load<Texture2D>(texPath);
            if (tex == null) { m = Lit(fallback, 0.05f); mats[key] = m; return m; }
            m = new Material(Base("DC_LitCutout", "Universal Render Pipeline/Lit"));
            SetColor(m, tint);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = 2450;
            mats[key] = m;
            return m;
        }

        /// <summary>건물 외벽(사진 벽 + 창, 4칸 × 4층 한 장): apt | office | house | house2 | other | public | low. 없으면 코드 무늬.</summary>
        public static Material Facade(string kind, Color tint)
        {
            if (Load<Texture2D>("DC/CC0/facade_" + kind + "_albedo") == null) return null;
            return Pbr("facade_" + kind, Color.gray, tint);
        }

        /// <summary>조명을 받지 않는 단색(칠·안내선)</summary>
        public static Material Unlit(Color c)
        {
            string key = "unlit" + ColorUtility.ToHtmlStringRGBA(c);
            if (mats.TryGetValue(key, out var m)) return m;
            bool t = c.a < 0.999f;
            m = new Material(t ? Base("DC_UnlitTransparent", "Universal Render Pipeline/Unlit", true) : Base("DC_Unlit", "Universal Render Pipeline/Unlit"));
            SetColor(m, c);
            mats[key] = m;
            return m;
        }

        /// <summary>조명을 받지 않는 그림(검은 바탕 흰 그림 × 색): 보행 신호 사람 그림 등. 그림이 없으면 단색</summary>
        public static Material UnlitTex(string texPath, Color c)
        {
            string key = "unlittex" + texPath + ColorUtility.ToHtmlStringRGBA(c);
            if (mats.TryGetValue(key, out var m)) return m;
            var tex = Load<Texture2D>(texPath);
            if (tex == null) { m = Unlit(c); mats[key] = m; return m; }
            m = new Material(Base("DC_Unlit", "Universal Render Pipeline/Unlit"));
            SetColor(m, c);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            mats[key] = m;
            return m;
        }

        /// <summary>매 프레임 색을 바꾸는 안내용(공유하지 않는 새 재질)</summary>
        public static Material UnlitInstance(Color c)
        {
            var m = new Material(c.a < 0.999f ? Base("DC_UnlitTransparent", "Universal Render Pipeline/Unlit", true) : Base("DC_Unlit", "Universal Render Pipeline/Unlit"));
            SetColor(m, c);
            return m;
        }

        public static void Tint(Material m, Color c) => SetColor(m, c);

        static readonly Dictionary<Material, float> dryGloss = new Dictionary<Material, float>();

        /// <summary>
        /// 젖은 노면(비·눈): 아스팔트 재질의 매끈함을 올려 하늘·불빛이 비치게. wet 0 = 마름, 1 = 비. 원래 값은 기억해 둔다.
        /// </summary>
        public static void SetWet(float wet)
        {
            foreach (var kv in mats)
            {
                if (!kv.Key.Contains("asphalt") || kv.Value == null || !kv.Value.HasProperty("_Smoothness")) continue;
                var m = kv.Value;
                if (!dryGloss.TryGetValue(m, out var dry)) dryGloss[m] = dry = m.GetFloat("_Smoothness");
                m.SetFloat("_Smoothness", Mathf.Lerp(dry, Mathf.Max(dry, 0.82f), wet));
            }
        }

        public static Material Sky(string name) => Load<Material>("DC/Materials/DC_Sky_" + name);

        public static Cubemap SkyCube(string name) => Load<Cubemap>("DC/PolyHaven/HDRI/" + name);

        public static GameObject Model(string path) => Load<GameObject>("DC/" + path);

        public static Font Font(bool bold)
        {
            // Pleos 디자인 가이드: 시스템 UI 글꼴 Asta Sans(42dot, OFL) — 차량 화면과 같은 글꼴. 자주 쓰는 글자만 남긴 부분 글꼴(약 480 KB)
            var f = Load<Font>(bold ? "DC/Fonts/AstaSans-Bold" : "DC/Fonts/AstaSans-Regular");
            if (f != null) return f;
            return UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
