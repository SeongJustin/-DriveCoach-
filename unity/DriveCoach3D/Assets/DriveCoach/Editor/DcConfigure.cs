// 담당: S (3D 주차 연습 — 편집기 화면 없이 명령줄(-batchmode)로 프로젝트 준비 2단계: URP·재질·가져오기 설정·장면·안드로이드 설정)
// 실행: Unity.exe -batchmode -quit -buildTarget Android -projectPath <프로젝트> -executeMethod DriveCoach.EditorTools.DcConfigure.Run
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if DC_URP
using UnityEngine.Rendering.Universal;
#endif

namespace DriveCoach.EditorTools
{
    public static class DcConfigure
    {
        public const string Settings = "Assets/DriveCoach/Settings";
        public const string MatDir = "Assets/Resources/DC/Materials";
        public const string ScenePath = "Assets/DriveCoach/Scenes/Main.unity";

        public static void Run()
        {
            int code = 0;
            try
            {
                Directory.CreateDirectory(Settings);
                Directory.CreateDirectory(MatDir);
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                SetupPipeline();
                SetupImporters();
                SetupMaterials();
                SetupScene();
                SetupPlayer();
                AssetDatabase.SaveAssets();
                Debug.Log("DCSETUP configure done");
            }
            catch (Exception e)
            {
                Debug.LogError("DCSETUP configure failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        // ───────────────────────── 렌더 파이프라인(URP) ─────────────────────────

        static void SetupPipeline()
        {
#if DC_URP
            UniversalRenderPipelineAsset asset = null;
            // 우리 것(DC_URP)만 쓴다: 템플릿 것은 설정이 다르다
            asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Settings + "/DC_URP.asset");
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Settings + "/DC_Renderer.asset");
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, Settings + "/DC_Renderer.asset");
            }
            // 1번: 가벼운 렌더러(거울·후방·지도, 그래픽 보통 이하) — 그늘(SSAO) 없음
            var rdLite = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Settings + "/DC_RendererLite.asset");
            if (rdLite == null)
            {
                rdLite = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rdLite, Settings + "/DC_RendererLite.asset");
            }
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(asset, Settings + "/DC_URP.asset");
            }
            // 후처리 셰이더 묶음(없으면 빌드에서 후처리가 빠진다)
            var ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            foreach (var r in new[] { rd, rdLite })
            {
                if (r.postProcessData == null && ppd != null) r.postProcessData = ppd;
                EditorUtility.SetDirty(r);
            }
            // 0번 렌더러에 그늘(화면 공간 AO): 건물 모서리·차 밑이 어두워져 입체감(11/8 그래픽 높게)
            // (URP 판마다 이 형식의 공개 여부가 달라 이름으로 찾는다)
            var aoType = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            bool hasAo = aoType == null;
            foreach (var f in rd.rendererFeatures) if (f != null && f.GetType() == aoType) hasAo = true;
            if (!hasAo)
            {
                var ao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(aoType);
                ao.name = "DC_SSAO";
                AssetDatabase.AddObjectToAsset(ao, rd);
                rd.rendererFeatures.Add(ao);
                var so = new SerializedObject(ao);
                var st = so.FindProperty("m_Settings");
                if (st != null)
                {
                    var it = st.FindPropertyRelative("Intensity"); if (it != null) it.floatValue = 1.1f;
                    var ra = st.FindPropertyRelative("Radius"); if (ra != null) ra.floatValue = 0.35f;
                    var ds = st.FindPropertyRelative("Downsample"); if (ds != null) ds.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                // 렌더러가 기능 목록을 직렬화한 맵(m_RendererFeatureMap)도 맞춘다
                var rso = new SerializedObject(rd);
                var map = rso.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ao, out _, out long lid))
                {
                    map.arraySize = rd.rendererFeatures.Count;
                    map.GetArrayElementAtIndex(rd.rendererFeatures.Count - 1).longValue = lid;
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(rd);
            }
            {
                var aso = new SerializedObject(asset);
                var list = aso.FindProperty("m_RendererDataList");
                if (list != null)
                {
                    list.arraySize = 2;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = rd;
                    list.GetArrayElementAtIndex(1).objectReferenceValue = rdLite;
                    var di = aso.FindProperty("m_DefaultRendererIndex"); if (di != null) di.intValue = 0;
                    var soft = aso.FindProperty("m_SoftShadowsSupported"); if (soft != null) soft.boolValue = true;
                    aso.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            // 그림자: 2단(가까운 곳 선명하게), 100 m 까지. 11/8 "유로트럭 느낌"(예전 45 m·1단)
            asset.shadowDistance = 100f;
            asset.shadowCascadeCount = 2;
            asset.cascade2Split = 0.22f;
            asset.mainLightShadowmapResolution = 2048;
            asset.msaaSampleCount = 2;
            asset.renderScale = 1f;
            asset.supportsHDR = true;
            EditorUtility.SetDirty(asset);
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            // 안개 셰이더를 빌드에 남긴다(장면 파일에는 안개가 꺼져 있어 자동이면 빠진다)
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs != null)
            {
                var gso = new SerializedObject(gs);
                var fs = gso.FindProperty("m_FogStripping"); if (fs != null) fs.intValue = 1;
                foreach (var k in new[] { "m_FogKeepLinear", "m_FogKeepExp", "m_FogKeepExp2" }) { var pp = gso.FindProperty(k); if (pp != null) pp.boolValue = true; }
                gso.ApplyModifiedPropertiesWithoutUndo();
            }
            Debug.Log("DCSETUP urp " + AssetDatabase.GetAssetPath(asset));
#else
            Debug.LogWarning("DCSETUP URP 패키지가 없어 기본 렌더러로 진행");
#endif
        }

        // ───────────────────────── 가져오기 설정 ─────────────────────────

        static void SetupImporters()
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/Resources/DC/")))
            {
                var imp = AssetImporter.GetAtPath(path);
                if (imp is TextureImporter ti)
                {
                    bool changed = false;
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (path.EndsWith(".exr"))
                    {
                        // 하늘(HDRI) → 큐브맵
                        ti.textureShape = TextureImporterShape.TextureCube;
                        ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                        ti.maxTextureSize = 1024;
                        ti.mipmapEnabled = true;
                        changed = true;
                    }
                    else if (name.EndsWith("_normal"))
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.maxTextureSize = 1024;
                        ti.wrapMode = TextureWrapMode.Repeat;
                        ti.anisoLevel = 8;   // 비스듬히 보이는 길·벽이 뭉개지지 않게
                        changed = true;
                    }
                    else if (name.EndsWith("_metalsmooth"))
                    {
                        ti.sRGBTexture = false;
                        ti.maxTextureSize = 512;
                        ti.alphaSource = TextureImporterAlphaSource.FromInput;
                        ti.wrapMode = TextureWrapMode.Repeat;
                        changed = true;
                    }
                    else if (name.EndsWith("_leaves") || name.EndsWith("_cutout"))
                    {
                        // 나무 잎(알파): 멀리서 잎이 사라지지 않게 밉맵이 알파 덮임을 지킨다
                        ti.alphaSource = TextureImporterAlphaSource.FromInput;
                        ti.alphaIsTransparency = true;
                        ti.mipmapEnabled = true;
                        ti.mipMapsPreserveCoverage = true;
                        ti.alphaTestReferenceValue = 0.5f;
                        ti.wrapMode = name.EndsWith("_leaves") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;   // 울타리 판은 길이 방향으로 되풀이
                        ti.maxTextureSize = 1024;
                        changed = true;
                    }
                    else if (name.EndsWith("_albedo"))
                    {
                        ti.wrapMode = TextureWrapMode.Repeat;
                        ti.maxTextureSize = 1024;
                        ti.anisoLevel = 8;
                        changed = true;
                    }
                    else if (name == "colormap")
                    {
                        // Kenney 색표: 칸 사이 색이 섞이지 않게
                        ti.filterMode = FilterMode.Point;
                        ti.mipmapEnabled = false;
                        changed = true;
                    }
                    if (changed) ti.SaveAndReimport();
                }
                else if (imp is ModelImporter mi)
                {
                    // URP 를 켠 뒤 다시 가져와야 재질이 URP 재질로 만들어진다
                    mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    // 거리 소품(Poly Haven)은 실행 중 한 메시로 합치므로 읽을 수 있게(11/8)
                    // 가로등(Quaternius)도 실제 노면에서는 수백 개라 합친다(11/8)
                    mi.isReadable = path.Contains("/PolyHaven/Models/") || path.Contains("/Streetlight");
                    mi.importAnimation = false;
                    mi.importCameras = false;
                    mi.importLights = false;
                    mi.SaveAndReimport();
                }
            }
        }

        // ───────────────────────── 재질 ─────────────────────────

        static Shader Find(params string[] names)
        {
            foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
            throw new Exception("셰이더 없음: " + string.Join(", ", names));
        }

        static void SaveMat(string name, Material m)
        {
            var path = MatDir + "/" + name + ".mat";
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old != null) { old.shader = m.shader; old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; old.renderQueue = m.renderQueue; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(m, path);
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void SetupMaterials()
        {
            var lit = Find("Universal Render Pipeline/Lit", "Standard");
            var unlit = Find("Universal Render Pipeline/Unlit", "Unlit/Color");
            var sky = Find("Skybox/Cubemap");

            SaveMat("DC_Lit", new Material(lit));
            SaveMat("DC_LitNormal", MakeLitNormal(lit));
            var unlitM = new Material(unlit);
            SaveMat("DC_Unlit", unlitM);
            var unlitT = new Material(unlit);
            MakeTransparent(unlitT);
            SaveMat("DC_UnlitTransparent", unlitT);
            var litT = new Material(lit);
            MakeTransparent(litT);
            SaveMat("DC_LitTransparent", litT);
            // 11/9: 잎(EZ-Tree)·울타리 그물 — 알파 자르기 + 양면. 그림자 패스도 자르기(_ALPHATEST_ON 변형이 빌드에 남게)
            var litC = new Material(lit);
            litC.SetFloat("_AlphaClip", 1f);
            litC.SetFloat("_Cutoff", 0.5f);
            litC.SetFloat("_Cull", 0f);
            litC.EnableKeyword("_ALPHATEST_ON");
            litC.SetOverrideTag("RenderType", "TransparentCutout");
            litC.renderQueue = (int)RenderQueue.AlphaTest;
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/DC/Trees/ash_medium_leaves.png");
            if (leafTex != null) litC.SetTexture("_BaseMap", leafTex);
            SaveMat("DC_LitCutout", litC);
            // (11/11) 쓰는 하늘 한 장만. 예전 하늘 두 장(rooftop_day, homecoming_rooftop)은 쓰이지 않는데도 앱에 들어가 용량만 차지했다(release APK 106 MB → 100 MB 아래로)
            foreach (var hdri in new[] { "sky_kloofendal_48d_2k" })
            {
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Resources/DC/PolyHaven/HDRI/" + hdri + ".exr");
                if (cube == null) { Debug.LogWarning("DCSETUP 하늘 없음 " + hdri); continue; }
                var m = new Material(sky);
                m.SetTexture("_Tex", cube);
                m.SetFloat("_Exposure", 1.0f);
                SaveMat("DC_Sky_" + hdri, m);
            }
        }

        /// <summary>노멀·금속도/매끈함 맵을 쓰는 Lit 변형(키워드가 빌드에 들어가게 미리 만들어 둔다)</summary>
        static Material MakeLitNormal(Shader lit)
        {
            var m = new Material(lit);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/DC/PolyHaven/Textures/asphalt_03_normal.png");
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/DC/PolyHaven/Textures/asphalt_03_metalsmooth.png");
            var al = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/DC/PolyHaven/Textures/asphalt_03_albedo.jpg");
            if (al != null) m.SetTexture("_BaseMap", al);
            if (n != null) m.SetTexture("_BumpMap", n);
            if (ms != null) m.SetTexture("_MetallicGlossMap", ms);
            return m;
        }

        // ───────────────────────── 장면 ─────────────────────────

        static void SetupScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("DriveCoachBridge");
            var t = Type.GetType("DriveCoach.Unity.GameMain, DriveCoach.Runtime");
            if (t == null) throw new Exception("GameMain 형식을 찾지 못함(DriveCoach.Runtime 컴파일 확인)");
            go.AddComponent(t);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ───────────────────────── 안드로이드 ─────────────────────────

        static void SetupPlayer()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "DriveCoach";
            PlayerSettings.productName = "DriveCoach3D";
            PlayerSettings.SetApplicationIdentifier(android, "kr.drivecoach.app.game3d");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            // 실제 차량(arm64) + 에뮬레이터(x86_64)
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            // 에뮬레이터에서 불안정할 수 있는 Vulkan 대신 OpenGL ES 3 하나만
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            PlayerSettings.runInBackground = false;
            // 옛 입력(Input)과 새 입력 시스템 둘 다 허용(화면 버튼·뒤로 가기)
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var p = so.FindProperty("activeInputHandler");
                if (p != null) { p.intValue = 2; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
        }
    }
}
