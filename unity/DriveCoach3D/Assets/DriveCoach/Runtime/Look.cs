// 담당: S (11/8 대표 요청 "유로트럭 정도의 느낌"): 화면 후처리(톤매핑·색·블룸·비네트), 카메라 품질, 하늘·안개·해.
// 그래픽 설정 3단계(Prefs.Quality): 0 가볍게(후처리·그림자 끔) · 1 보통(후처리, 그림자) · 2 높게(+ 그늘(SSAO)·블룸·SMAA·거울 그림자).
// 거울·후방·지도 카메라는 가벼운 렌더러(1번, 그늘 없음)로, 후처리 없이 그린다.
using UnityEngine;
using UnityEngine.Rendering;
#if DC_URP
using UnityEngine.Rendering.Universal;
#endif

namespace DriveCoach.Unity
{
    public static class Look
    {
        /// <summary>낮 하늘(Poly Haven CC0, 땅 없는 하늘만). 없으면 예전 하늘.</summary>
        public const string SkyName = "sky_kloofendal_48d_2k";
        public const string OldSky = "sky_rooftop_day_1k";

        public static Material Sky() => DcAssets.Sky(SkyName) ?? DcAssets.Sky(OldSky);
        public static Cubemap SkyCube() => DcAssets.SkyCube(SkyName) ?? DcAssets.SkyCube(OldSky);

        /// <summary>하늘 지평선 색(안개를 이 색으로: 먼 건물이 하늘에 녹아든다)</summary>
        public static readonly Color Horizon = new Color(0.74f, 0.80f, 0.87f);

#if DC_URP
        static int RendererCount()
        {
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a == null) return 0;
            try { return a.rendererDataList.Length; } catch { return 1; }
        }

        static void UseRenderer(UniversalAdditionalCameraData d, int i)
        {
            if (i < RendererCount()) d.SetRenderer(i);
        }
#endif

        /// <summary>화면 해상도 배율(느린 기기에서 낮춘다). 거울·지도처럼 그림판(RenderTexture)에 그리는 카메라는 영향 없음.</summary>
        public static void RenderScale(float s)
        {
#if DC_URP
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a != null) a.renderScale = Mathf.Clamp(s, 0.4f, 1f);
#endif
        }

        /// <summary>후처리만 켜고 끈다(Perf)</summary>
        public static void Post(Camera cam, bool on)
        {
#if DC_URP
            var d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = on && Prefs.Quality >= 1;
            if (!on) { d.antialiasing = AntialiasingMode.None; UseRenderer(d, 1); }
#endif
        }

        /// <summary>운전 화면 카메라: 후처리·안티에일리어싱·렌더러(높게만 그늘)</summary>
        public static void MainCamera(Camera cam)
        {
            cam.allowHDR = true;
#if DC_URP
            var d = cam.GetUniversalAdditionalCameraData();
            int q = Prefs.Quality;
            d.renderPostProcessing = q >= 1;
            d.antialiasing = q >= 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : (q == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None);
            d.antialiasingQuality = AntialiasingQuality.Medium;
            d.renderShadows = q >= 1;
            d.stopNaN = false;
            UseRenderer(d, q >= 2 ? 0 : 1);
#endif
        }

        /// <summary>거울·후방·지도 카메라: 가볍게(후처리 없음, 높게일 때만 그림자)</summary>
        public static void AuxCamera(Camera cam)
        {
            cam.allowHDR = false;
#if DC_URP
            var d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = false;
            d.antialiasing = AntialiasingMode.None;
            d.renderShadows = Prefs.Quality >= 2;
            UseRenderer(d, 1);
#endif
        }

        /// <summary>전체 후처리 볼륨(장면마다 하나). dark = 지하 주차장(조금 밝게, 따뜻하게 안 함)</summary>
        public static GameObject PostVolume(Transform parent, bool dark, float exposureAdd = 0f, float satAdd = 0f)
        {
#if DC_URP
            int q = Prefs.Quality;
            if (q < 1) return null;
            var go = new GameObject("PostVolume");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<UnityEngine.Rendering.Volume>();
            v.isGlobal = true;
            v.priority = 10f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = p.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override((dark ? 0.55f : 0.45f) + exposureAdd);
            ca.contrast.Override(14f);
            ca.saturation.Override((dark ? 0f : 10f) + satAdd);
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.Override(dark ? -4f : 5f);
            var vg = p.Add<Vignette>(true);
            vg.intensity.Override(0.22f);
            vg.smoothness.Override(0.5f);
            if (q >= 2 || !dark)
            {
                var bl = p.Add<Bloom>(true);
                bl.threshold.Override(1.05f);
                bl.intensity.Override(q >= 2 ? 0.45f : 0.25f);
                bl.scatter.Override(0.65f);
                bl.highQualityFiltering.Override(false);
            }
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.97f, 0.99f, 1.04f, 0f));   // 그늘을 조금 푸르게(하늘빛)
            smh.highlights.Override(new Vector4(1.03f, 1.0f, 0.96f, 0f)); // 햇빛은 조금 따뜻하게
            v.sharedProfile = p;
            return go;
#else
            return null;
#endif
        }

        /// <summary>낮 환경: 하늘·안개·주변광·반사. 해(Light)는 호출한 쪽이 넘긴다.</summary>
        public static void Day(Camera cam, Light sun)
        {
            var sky = Sky();
            if (sky != null) { RenderSettings.skybox = sky; cam.clearFlags = CameraClearFlags.Skybox; }
            else { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Horizon; }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.64f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.33f, 0.31f);
            // 먼 곳이 하늘색으로 흐려진다(유로트럭의 공기감). 600 m 에서 거의 하늘색
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0024f;
            RenderSettings.fogColor = Horizon;
            sun.intensity = 2.1f;
            sun.color = new Color(1f, 0.93f, 0.84f);
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            var cube = SkyCube();
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 0.9f;
            }
        }

        /// <summary>
        /// 바깥 환경(11/8 밤): 시간(0 아침·1 낮·2 해질녘·3 밤) × 날씨(0 맑음·1 흐림·2 비·3 안개·4 눈).
        /// 해의 높이·색·세기, 하늘 밝기·색, 주변광, 안개 짙기·색, 반사 세기를 정한다. 비·눈 알갱이와 젖은 노면은 SceneRig·DcAssets 가.
        /// 돌려주는 것: 후처리 노출 보정(밤은 조금 밝게 보정해 화면이 새까맣지 않게)
        /// </summary>
        public static float Outdoor(Camera cam, Light sun, int time, int weather)
        {
            Day(cam, sun);
            var sky = RenderSettings.skybox;
            // 시간
            float sunH = 38f, sunYaw = -35f, sunI = 2.1f, skyExp = 1f, refl = 0.9f, expo = 0f;
            Color sunC = new Color(1f, 0.93f, 0.84f), tint = new Color(0.5f, 0.5f, 0.5f), fogC = Horizon;
            Color aSky = new Color(0.66f, 0.74f, 0.86f), aEq = new Color(0.62f, 0.64f, 0.66f), aGr = new Color(0.34f, 0.33f, 0.31f);
            switch (time)
            {
                case 0:   // 아침: 낮은 해, 맑고 조금 푸른 빛
                    sunH = 16f; sunYaw = 80f; sunI = 1.6f; sunC = new Color(1f, 0.88f, 0.74f); skyExp = 0.85f;
                    tint = new Color(0.50f, 0.52f, 0.56f); fogC = new Color(0.78f, 0.80f, 0.84f);
                    aSky = new Color(0.60f, 0.68f, 0.80f); aEq = new Color(0.58f, 0.58f, 0.60f);
                    break;
                case 2:   // 해질녘: 아주 낮은 주황 해, 붉은 하늘
                    sunH = 7f; sunYaw = -100f; sunI = 1.3f; sunC = new Color(1f, 0.62f, 0.36f); skyExp = 0.6f; refl = 0.6f;
                    tint = new Color(0.62f, 0.46f, 0.40f); fogC = new Color(0.80f, 0.62f, 0.52f);
                    aSky = new Color(0.55f, 0.50f, 0.58f); aEq = new Color(0.62f, 0.48f, 0.40f); aGr = new Color(0.26f, 0.22f, 0.20f);
                    expo = 0.15f;
                    break;
                case 3:   // 밤: 달빛(푸르고 약함), 어두운 하늘. 전조등·신호등·차 불빛이 주인공
                    // 도시의 밤(11/9 새벽 사진 검수 "너무 어둡다"): 하늘이 도시 불빛으로 옅게 밝고(주황빛 감도는 남색), 건물 윤곽이 보일 만큼
                    sunH = 35f; sunYaw = 20f; sunI = 0.16f; sunC = new Color(0.62f, 0.72f, 1f); skyExp = 0.09f; refl = 0.3f;
                    tint = new Color(0.34f, 0.34f, 0.46f); fogC = new Color(0.09f, 0.09f, 0.12f);
                    aSky = new Color(0.17f, 0.18f, 0.26f); aEq = new Color(0.16f, 0.14f, 0.15f); aGr = new Color(0.08f, 0.075f, 0.07f);
                    expo = 0.75f;
                    break;
            }
            // 날씨
            float fog = 0.0024f;
            switch (weather)
            {
                case 1:   // 흐림: 해가 약하고 그림자가 옅다, 하늘 회색
                    sunI *= 0.45f; skyExp *= 0.7f; fog = 0.0045f; tint = Color.Lerp(tint, new Color(0.45f, 0.46f, 0.48f), 0.6f);
                    fogC = Color.Lerp(fogC, new Color(0.62f, 0.64f, 0.66f) * (time == 3 ? 0.12f : 1f), 0.7f);
                    break;
                case 2:   // 비: 어둡고 젖은 공기
                    sunI *= 0.3f; skyExp *= 0.5f; fog = 0.009f; tint = Color.Lerp(tint, new Color(0.40f, 0.42f, 0.45f), 0.7f);
                    fogC = Color.Lerp(fogC, new Color(0.50f, 0.53f, 0.56f) * (time == 3 ? 0.12f : 1f), 0.8f); refl = Mathf.Max(refl, 0.7f);
                    break;
                case 3:   // 안개: 100 m 쯤에서 거의 안 보임
                    sunI *= 0.35f; skyExp *= 0.6f; fog = 0.028f;
                    fogC = Color.Lerp(fogC, new Color(0.72f, 0.74f, 0.76f) * (time == 3 ? 0.15f : 1f), 0.85f);
                    break;
                case 4:   // 눈: 밝은 회색 하늘, 하얀 공기
                    sunI *= 0.5f; skyExp *= 0.8f; fog = 0.012f; tint = Color.Lerp(tint, new Color(0.52f, 0.53f, 0.56f), 0.6f);
                    fogC = Color.Lerp(fogC, new Color(0.82f, 0.84f, 0.88f) * (time == 3 ? 0.15f : 1f), 0.8f);
                    break;
            }
            if (time == 3) aSky *= weather == 0 ? 1f : 0.8f;
            sun.intensity = sunI;
            sun.color = sunC;
            sun.transform.rotation = Quaternion.Euler(sunH, sunYaw, 0f);
            sun.shadowStrength = weather == 0 ? 0.85f : (weather == 1 ? 0.45f : 0.3f);
            RenderSettings.ambientSkyColor = aSky;
            RenderSettings.ambientEquatorColor = aEq;
            RenderSettings.ambientGroundColor = aGr;
            RenderSettings.fogDensity = fog;
            RenderSettings.fogColor = fogC;
            RenderSettings.reflectionIntensity = refl;
            if (sky != null)
            {
                // 하늘 그림 밝기·색(Skybox/Panoramic·Cubemap 공통 속성). 원본 재질을 바꾸지 않게 복사본
                var m = new Material(sky);
                if (m.HasProperty("_Exposure")) m.SetFloat("_Exposure", (m.GetFloat("_Exposure") > 0f ? m.GetFloat("_Exposure") : 1f) * skyExp);
                if (m.HasProperty("_Tint")) m.SetColor("_Tint", tint);
                RenderSettings.skybox = m;
            }
            if (weather != 0)
            {
                // 흐림·비·눈·안개: 파란 하늘·해가 든 하늘 사진 대신 안개 색(흐린 회색) 바탕 — 비 오는 낮이 어두운 남색 하늘로 보이던 것(11/9 새벽 검수)
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = fogC;
            }
            DynamicGI.UpdateEnvironment();
            return expo;
        }

        /// <summary>지하 주차장: 하늘·안개 없음, 고르게 밝은 실내등</summary>
        public static void Underground(Camera cam, Light sun)
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DcAssets.Hex("#1C1F24");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.64f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.51f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.30f, 0.32f);
            sun.intensity = 0.7f;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(80f, -20f, 0f);
            sun.shadowStrength = 0.45f;
            var cube = SkyCube();
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 0.35f;
            }
        }
    }
}
