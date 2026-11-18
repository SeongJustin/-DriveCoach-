// 3D 주차 연습: 편집기 화면 없이 명령줄(-batchmode)로 3단계: 안드로이드 라이브러리 내보내기, 4단계: 화면 캡처
// 내보내기: Unity.exe -batchmode -quit -buildTarget Android -projectPath <프로젝트> -executeMethod DriveCoach.EditorTools.DcBuild.ExportAndroid -dcOut <폴더>
// 캡처:     Unity.exe -batchmode -quit -buildTarget Android -projectPath <프로젝트> -executeMethod DriveCoach.EditorTools.DcBuild.Shots -dcOut <폴더>
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DriveCoach.Core;
using DriveCoach.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
// UnityEditor.SceneManagement 에도 Stage 가 있어 우리 것(판 정의)을 이름으로 묶는다
using Stage = DriveCoach.Core.Stage;

namespace DriveCoach.EditorTools
{
    public static class DcBuild
    {
        static string Arg(string name, string def)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return def;
        }

        public static void ExportAndroid()
        {
            int code = 0;
            try
            {
                string outDir = Path.GetFullPath(Arg("-dcOut", "../export"));
                EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { DcConfigure.ScenePath },
                    locationPathName = outDir,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                Debug.Log($"DCSETUP export {s.result} errors={s.totalErrors} size={s.totalSize} time={s.totalTime} -> {outDir}");
                if (s.result != BuildResult.Succeeded) code = 1;
            }
            catch (Exception e) { Debug.LogError("DCSETUP export failed: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        class Plan
        {
            public string name;
            public Func<Stage> st;
            public CamMode cam = CamMode.Chase;
            public float steer, sim, look;
            public float[] eye;      // 공중에서 비스듬히: { 뒤로 거리 m, 높이 m }, at 의 자리를 at[2] 방향으로 바라본다
            public Gear gear = Gear.D;
            public bool hud, mirrors;
            public string tag = "", kind = "stage";
            public int weather = -1, time = -1;   // 날씨·시간 사진(-1 = 설정 그대로)
            public float[] at;                     // 차를 이 자리(지역 좌표 x, y, 차 가운데)·방향(도)에 두고 찍는다(없으면 출발 자리)
            public int spawnKind = -1;             // 내 차 앞(18 m)에 이 종류 차를 한 대(버스가 정류장에 서는 사진)
            public float spawnAhead = 18f;
            /// <summary>앞에 세운 차 지붕에 빨강·파랑 경광등을 얹는다(긴급자동차 설명 그림)</summary>
            public bool emergency;
            /// <summary>앞에 세울 차를 내 차 왼쪽으로 이만큼(m) 옮겨 놓는다(옆 차로로 지나가는 긴급자동차)</summary>
            public float spawnLeft;
            // drive > 0 이면 그 초만큼 자동 운전으로 달리며 every 초마다 한 장(drive/이름_번호.jpg). route = 갈림길 글자(S·L·R), kmh = 순항 속도
            public float drive, every = 2f, kmh = 40f;
            /// <summary>정해 둔 시연 경로(Editor/Resources/DC/Routes/이름.json, 편집기 전용이라 앱에는 들어가지 않는다)를 따라 달린다. 출발 자리도 경로에서</summary>
            public string routeFile;
            /// <summary>0 보다 크면 달리는 동안 초당 이만큼 영상 장면을 쓴다(unity/video/이름/d_·c_번호.jpg = 운전석·따라가기, 1280x720)</summary>
            public int video;
            public string route = "";
            /// <summary>(일러스트를 3D 장면으로) 바닥에 그릴 굵은 화살표(지역 좌표 x,y 쌍). 설명 그림 견본용</summary>
            public float[] arrow;
            /// <summary>앞 정지선 신호가 빨간불이고 가까운 횡단보도에 사람이 건너는 순간까지 시간을 돌린 뒤 찍는다</summary>
            public bool untilRedPed;
            /// <summary>공중 사진이 바라볼 자리(지역 좌표 x, y). 없으면 at</summary>
            public float[] aim;
            /// <summary>공중 사진의 카메라 자리를 차와 상관없이 고정: { x, y, 높이 m }. 직각 주차 7장을 같은 자리에서 찍는다</summary>
            public float[] eyeAbs;
            /// <summary>설명 그림: 화살표 여러 개(각각 x,y 쌍)와 모양(0 = 검정, 1 = 빨강, 2 = 검정 점선)</summary>
            public float[][] arrows; public int[] arrowStyle;
            /// <summary>앞 정지선 신호가 빨간불이 될 때까지만(보행자는 안 봄)</summary>
            public bool untilRed;
        }

        /// <summary>시연 경로(Editor/Resources/DC/Routes/yeoksam1_demo1.json)의 점들(m). 경로를 다시 만들어도 사진이 따라오게 파일에서 읽는다</summary>
        static float[] RouteArrow
        {
            get
            {
                var ta = Resources.Load<TextAsset>("DC/Routes/yeoksam1_demo1");
                var ro = ta != null ? MiniJson.ParseObject(ta.text) : null;
                if (ro == null || !ro.TryGetValue("pts", out var pv) || !(pv is System.Collections.Generic.List<object> pl)) return null;
                var o = new float[pl.Count];
                for (int i = 0; i < pl.Count; i++) o[i] = Convert.ToSingle(pl[i]) / 10f;
                return o;
            }
        }
        const int ShotW = 1600, ShotH = 900;

        /// <summary>
        /// 3D 장면(+ 게임 화면 글자·버튼, 첫 화면)을 편집기 안에서 만들어 사진으로 남긴다(에뮬레이터 없이 모양 확인).
        /// 결과: &lt;dcOut&gt;/shot_*.png
        /// </summary>
        public static void Shots()
        {
            int code = 0;
            try
            {
                string outDir = Path.GetFullPath(Arg("-dcOut", "../shots"));
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // 사진은 그래픽 "높게"로(기기에서는 설정대로). 소리는 내지 않는다.
                Prefs.QualityOverride = int.TryParse(Arg("-dcQuality", "2"), out var q) ? q : 2;
                AudioListener.volume = 0f;
                AudioListener.pause = true;
                var plans = new[]
                {
                    new Plan { name = "stage07_chase", st = () => Stages.Get(7), hud = true, tag = "7" },
                    new Plan { name = "stage07_top", st = () => Stages.Get(7), cam = CamMode.Top, steer = 300f },
                    new Plan { name = "stage08_driver_hud", st = () => Stages.Get(8), cam = CamMode.Driver, gear = Gear.R, hud = true, mirrors = true, tag = "8" },
                    new Plan { name = "stage08_driver_look", st = () => Stages.Get(8), cam = CamMode.Driver, gear = Gear.R, steer = -240f, look = 70f },
                    new Plan { name = "stage14_parallel", st = () => Stages.Get(14), steer = -200f, gear = Gear.R, hud = true, tag = "14" },
                    new Plan { name = "stage22_ramp", st = () => Stages.Get(22), sim = 2.5f },
                    new Plan { name = "tutorial1_steer", st = () => Stages.Tutorial(1), steer = 480f, hud = true, kind = "tutorial" },
                    new Plan { name = "circuit1", st = () => Stages.Circuit(1), hud = true, kind = "circuit" },
                    new Plan { name = "circuit1_driver", st = () => Stages.Circuit(1), cam = CamMode.Driver, steer = 90f, hud = true, mirrors = true, kind = "circuit" },
                    new Plan { name = "circuit2_top", st = () => Stages.Circuit(2), cam = CamMode.Overview },
                    // 일러스트 견본: 역삼역 교차로 빨간불 우회전(테헤란로 동행 → 논현로 남행), 비스듬히 내려다봄
                    new Plan { name = "illust_right_turn", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, untilRedPed = true,
                        at = new[] { 104.2f, 32.7f, -20f }, eye = new[] { 20f, 36f }, aim = new[] { 119f, 33f },
                        arrow = new[] { 107.6f, 31.4f, 114.0f, 29.2f, 120.5f, 28.4f, 125.6f, 31.6f, 128.6f, 38.0f, 130.9f, 44.0f, 133.0f, 49.5f, 136.0f, 57.5f } },
                    new Plan { name = "illust_crosswalk", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, untilRedPed = true, at = new[] { 240.8f, -75.4f, 67f }, eye = new[] { 9f, 11f }, aim = new[] { 246.0f, -62.5f } },
                    new Plan { name = "illust_signal", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, untilRed = true, at = new[] { 100.9f, 23.3f, -20f }, eye = new[] { 20f, 28f }, aim = new[] { 117f, 18f } },
                    new Plan { name = "illust_u_turn", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 99.0f, 20.8f, -20f }, eye = new[] { 18f, 26f }, aim = new[] { 113f, 12f },
                        arrows = new[] { new[] { 104.5f, 18.7f, 116.3f, 14.4f, 118.9f, 12.2f, 118.8f, 9.1f, 116.4f, 6.8f, 113.5f, 6.6f, 100.5f, 11.4f } } },
                    new Plan { name = "illust_unprotected_left", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 102.0f, 19.7f, -21f }, eye = new[] { 22f, 32f }, aim = new[] { 122f, 6f },
                        arrows = new[] { RouteSlice(372f, 422f), new[] { 160.0f, -17.7f, 142.0f, -10.8f, 125.4f, -4.4f, 104.0f, 3.9f } }, arrowStyle = new[] { 2, 1 } },
                    new Plan { name = "illust_lanes", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -99.9f, 96.4f, -22f }, eye = new[] { 6f, 30f }, aim = new[] { -90f, 92f } },
                    new Plan { name = "illust_school_zone", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -654.6f, -827.2f, 37f }, eye = new[] { 8f, 10f }, aim = new[] { -640f, -816.5f } },
                    new Plan { name = "illust_roundabout", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -567.0f, -771.5f, -90f }, eye = new[] { 24f, 38f }, aim = new[] { -573f, -796f },
                        arrows = new[] { new[] { -567.0f, -774.0f, -566.5f, -781.0f, -565.8f, -787.7f, -559.7f, -793.8f, -557.5f, -802.0f, -559.7f, -810.2f, -565.8f, -816.3f, -574.0f, -818.5f, -582.2f, -816.3f, -588.3f, -810.2f, -596.0f, -808.5f, -606.0f, -812.0f } } },
                    new Plan { name = "illust_highway", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -684.8f, -1566.1f, 53.2f }, eye = new[] { 30f, 34f }, aim = new[] { -664.4f, -1541.4f },
                        arrows = new[] { new[] { -681.2f, -1561.3f, -676.5f, -1554.9f, -665.2f, -1545.8f, -655.6f, -1533.0f, -650.1f, -1519.6f, -641.7f, -1508.4f } } },
                    // 주정차 금지(정류장·횡단보도), 긴급자동차(경광등 단 승합차에 길을 내줌), 기본 준수사항(운전석·제한 속도)
                    new Plan { name = "illust_parking_ban", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 12f, spawnKind = 6, spawnAhead = 16f, at = new[] { -794.0f, 16.4f, -110.0f }, eye = new[] { 12f, 12f }, aim = new[] { -804.3f, 11.6f },
                        arrows = new[] { new[] { -790.9f, 15.3f, -800.7f, -11.9f } }, arrowStyle = new[] { 1 } },
                    new Plan { name = "illust_emergency", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, spawnKind = 4, spawnAhead = 5f, spawnLeft = 5.5f, emergency = true, at = new[] { -97.4f, 102.5f, -22f }, eye = new[] { 12f, 9f }, aim = new[] { -95.2f, 91.9f } },
                    new Plan { name = "illust_basics", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", cam = CamMode.Driver, weather = 0, time = 1, gear = Gear.D, at = new[] { -99.9f, 96.4f, -22f } },
                    new Plan { name = "illust_right_turn_d", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", cam = CamMode.Driver, weather = 0, time = 1, gear = Gear.P, untilRedPed = true,
                        at = new[] { 104.2f, 32.7f, -20f } },
                    // 시연 경로 점검: 경로를 바닥에 그려 놓고 곳곳을 위에서 본다(차선 안으로 다니는지)
                    new Plan { name = "routeair_00", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -183.9f, 128.8f, -22f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_01", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -53.2f, 78.5f, -20f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_02", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 57.0f, 36.4f, -20f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_03", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 82.3f, 27.1f, -21f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_04", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 123.9f, -16.6f, -112f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_05", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 91.0f, -110.8f, -108f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_06", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 89.7f, -146.8f, -20f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_07", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 163.9f, -176.1f, -24f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_08", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 212.4f, -142.9f, 67f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_09", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 243.4f, -69.1f, 67f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_10", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 231.1f, -49.5f, 159f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_11", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 137.5f, -14.4f, 159f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_12", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 55.1f, 17.9f, 159f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_13", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 17.9f, 32.4f, 158f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_14", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -22.2f, -41.4f, -110f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    new Plan { name = "routeair_15", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -10.2f, -88.0f, -68f }, eye = new[] { 14f, 38f }, arrow = RouteArrow },
                    // 청계산로 동쪽 끝 여의천 다리(6 m 로 솟아 있던 곳) 전후 비교
                    new Plan { name = "fix_cg_bridge", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 195f, -990f, 15f }, eye = new[] { 70f, 40f } },
                    new Plan { name = "fix_cg_bridge2", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 205f, -1010f, -70f }, eye = new[] { 55f, 30f } },
                    new Plan { name = "road_preview", st = () => Stages.RoadPreview(), hud = true, kind = "road" },
                    // 내 동네(OSM 양재2동): 따라가기·운전석(화면 거울)·위에서
                    new Plan { name = "region_chase", st = () => Stages.Region(RegionCatalog.Load(0)), hud = true, kind = "region" },
                    new Plan { name = "region_driver", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, hud = true, mirrors = true, kind = "region" },
                    new Plan { name = "region_top", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Top, kind = "region" },
                    new Plan { name = "region_look", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, look = -60f, kind = "region" },
                    // 날씨·시간(밤 가로등·다른 차 전조등, 비 젖은 노면·빗줄기, 해질녘 안개)
                    new Plan { name = "region_night_chase", st = () => Stages.Region(RegionCatalog.Load(0)), hud = true, kind = "region", weather = 0, time = 3 },
                    new Plan { name = "region_night_driver", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, hud = true, mirrors = true, kind = "region", weather = 0, time = 3 },
                    new Plan { name = "region_rain_driver", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, hud = true, mirrors = true, kind = "region", weather = 2, time = 1 },
                    new Plan { name = "region_sunset_fog", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 3, time = 2 },
                    new Plan { name = "region_snow_chase", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 4, time = 0 },
                    // 과속방지턱(양재2동 T-GIS 험프 하나 앞 9 m, 오른쪽 차로)
                    new Plan { name = "region_hump_chase", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, at = new[] { 456.9f, -202.4f, 143.5f } },
                    // 어린이보호구역 입구 빨간 노면(양재2동 동산로)
                    new Plan { name = "region_school_chase", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, at = new[] { -392.85f, -2052.29f, -39.3f } },
                    // 횡단보도 보행자(역삼1동 역삼로 신호 횡단보도 16 m 앞, 25초 흘려 사람이 생기게, 차는 P)
                    new Plan { name = "region_peds_chase", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 25f, at = new[] { 301.9f, 568.3f, -22.3f } },
                    // 고가도로(경부고속도로 밑 바우뫼로, 양재대로12길 고가 밑 청계산로), 높이 데이터가 들어간 노면부터 보인다
                    new Plan { name = "region_overpass_chase", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, at = new[] { -1405.0f, -2461.6f, -35.7f } },
                    new Plan { name = "region_overpass2_driver", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, hud = true, kind = "region", weather = 0, time = 1, at = new[] { 309.3f, -854.3f, -125.0f } },
                    // 지하차도 입구(양재2동 신원지하차도): 높이 데이터가 들어간 노면부터
                    // 버스정류장(역삼1동 신논현역, 강남대로 길가): 시내버스가 오른쪽 깜빡이 켜고 정류장에 선 모습, 다른 차 종류
                    new Plan { name = "region_busstop_chase", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 12f, spawnKind = 6, spawnAhead = 16f, at = new[] { -794.0f, 16.4f, -110.0f } },
                    // 조사용: 버스정류장·고가 자리를 위에서, 길 옆 땅이 하늘색으로 비어 보이는 원인 찾기
                    new Plan { name = "diag_busstop_top", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -561.0f, -2014.9f, -116.4f } },
                    new Plan { name = "diag_overpass_top", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -1405.0f, -2461.6f, -35.7f } },
                    // 양재2동 경부고속도로 고가(경부고속도로 본선이 양재대로 위로): 북서 진입·남동 진입·양재대로에서 밑으로·옆에서
                    new Plan { name = "d6_nw", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 4f, at = new[] { -639.6f, -1506.5f, 52.7f } },
                    new Plan { name = "d6_se", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 4f, at = new[] { -409.4f, -1235.5f, -127.3f } },
                    new Plan { name = "d6_under", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 4f, at = new[] { -475.4f, -1430.5f, 142.7f } },
                    new Plan { name = "d6_side", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 4f, at = new[] { -607.2f, -1405.4f, -2.3f } },
                    new Plan { name = "d6_air1", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -525.4f, -1372.3f, -17.3f }, eye = new[] { 300f, 110f } },
                    new Plan { name = "d6_air2", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -525.4f, -1372.3f, 167.7f }, eye = new[] { 300f, 110f } },
                    new Plan { name = "d6_air3", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -525.4f, -1372.3f, -107.3f }, eye = new[] { 230f, 60f } },
                    // 동네 전체를 4 × 4 칸으로 위에서(가 고칠 자리를 칸 번호로 짚을 수 있게)
                    // 지하차도·진입로·옆길·이은 길을 차 높이에서(뒤따라·운전석). at 넷째 값 = 차 높이(m)
                    new Plan { name = "up_cg_exit_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -502.4f, -775.3f, 5.2f, -6.50f } },
                    new Plan { name = "up_cg_exit_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -502.4f, -775.3f, 5.2f, -6.50f } },
                    new Plan { name = "up_cg_mid_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -375.6f, -784.9f, -5.2f, -6.50f } },
                    new Plan { name = "up_cg_mid_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -375.6f, -784.9f, -5.2f, -6.50f } },
                    new Plan { name = "up_cg_east_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -260.4f, -795.8f, -5.8f, -6.50f } },
                    new Plan { name = "up_cg_east_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -260.4f, -795.8f, -5.8f, -6.50f } },
                    new Plan { name = "up_sw_in_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -99.6f, -809.1f, -4.6f, -6.50f } },
                    new Plan { name = "up_sw_in_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -99.6f, -809.1f, -4.6f, -6.50f } },
                    new Plan { name = "up_sw_out_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 66.8f, -826.0f, -11.0f, -6.50f } },
                    new Plan { name = "up_sw_out_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 66.8f, -826.0f, -11.0f, -6.50f } },
                    new Plan { name = "up_sw_ramp_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 160.3f, -847.3f, -15.0f, -3.12f } },
                    new Plan { name = "up_sw_ramp_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 160.3f, -847.3f, -15.0f, -3.12f } },
                    new Plan { name = "up_cg_side_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -422.0f, -772.9f, -5.1f, 0.00f } },
                    new Plan { name = "up_cg_side_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -422.0f, -772.9f, -5.1f, 0.00f } },
                    new Plan { name = "up_cg_sideramp_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -340.6f, -782.8f, -12.7f, -4.02f } },
                    new Plan { name = "up_cg_sideramp_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -340.6f, -782.8f, -12.7f, -4.02f } },
                    new Plan { name = "up_cg_wramp_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -671.5f, -870.3f, 45.4f, -2.32f } },
                    new Plan { name = "up_cg_wramp_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -671.5f, -870.3f, 45.4f, -2.32f } },
                    new Plan { name = "up_yg_ramp_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -101.1f, -1447.1f, -130.9f, -5.07f } },
                    new Plan { name = "up_yg_ramp_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -101.1f, -1447.1f, -130.9f, -5.07f } },
                    new Plan { name = "up_yg_in_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -245.7f, -1612.9f, -131.3f, -13.00f } },
                    new Plan { name = "up_yg_in_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -245.7f, -1612.9f, -131.3f, -13.00f } },
                    new Plan { name = "up_yg_ew_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -126.2f, -1732.2f, 138.2f, -2.95f } },
                    new Plan { name = "up_yg_ew_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -126.2f, -1732.2f, 138.2f, -2.95f } },
                    new Plan { name = "up_yg_side_c", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -117.5f, -1480.4f, -131.0f, 0.00f } },
                    new Plan { name = "up_yg_side_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -117.5f, -1480.4f, -131.0f, 0.00f } },
                    new Plan { name = "up_yj_ramp_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 913.4f, -1643.6f, -10.4f, -2.53f } },
                    new Plan { name = "up_yj_ramp_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 913.4f, -1643.6f, -10.4f, -2.53f } },
                    new Plan { name = "up_yj_in_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 1051.7f, -1668.2f, -9.8f, -6.50f } },
                    new Plan { name = "up_yj_in_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 1051.7f, -1668.2f, -9.8f, -6.50f } },
                    new Plan { name = "up_yj_out_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 1163.6f, -1688.0f, -10.3f, -4.18f } },
                    new Plan { name = "up_yj_out_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 1163.6f, -1688.0f, -10.3f, -4.18f } },
                    new Plan { name = "up_yj_side_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 931.3f, -1637.7f, -11.0f, 0.00f } },
                    new Plan { name = "up_yj_side_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 931.3f, -1637.7f, -11.0f, 0.00f } },
                    new Plan { name = "up_sa_ramp_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -282.5f, 1080.2f, -34.8f, -2.85f } },
                    new Plan { name = "up_sa_ramp_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -282.5f, 1080.2f, -34.8f, -2.85f } },
                    new Plan { name = "up_hw_north_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 288.9f, -1678.9f, 78.5f, 6.00f } },
                    new Plan { name = "up_hw_north_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 288.9f, -1678.9f, 78.5f, 6.00f } },
                    new Plan { name = "up_nb_gap_c", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 596.4f, -1585.9f, -8.0f, 0.00f } },
                    new Plan { name = "up_nb_gap_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 596.4f, -1585.9f, -8.0f, 0.00f } },
                    // 주행 점검: 지하차도·이은 길을 자동 운전으로 달려 보며 2초마다 한 장(drive/)
                    new Plan { name = "drive_cg", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, drive = 80f, every = 2f, route = "", at = new[] { -725.7f, -925.8f, 45.8f, 0.00f } },
                    new Plan { name = "drive_cg_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, drive = 80f, every = 2f, route = "", at = new[] { -725.7f, -925.8f, 45.8f, 0.00f } },
                    new Plan { name = "drive_cgw", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, drive = 50f, every = 2f, route = "", at = new[] { -238.0f, -805.4f, 174.9f, -6.50f } },
                    new Plan { name = "drive_yg", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, drive = 70f, every = 2f, route = "", at = new[] { -28.9f, -1378.0f, -147.3f, 0.00f } },
                    new Plan { name = "drive_yg_d", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, drive = 70f, every = 2f, route = "", at = new[] { -28.9f, -1378.0f, -147.3f, 0.00f } },
                    new Plan { name = "drive_ygew", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, drive = 60f, every = 2f, route = "", at = new[] { 36.3f, -1881.1f, 135.4f, 0.00f } },
                    new Plan { name = "drive_yj", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, drive = 70f, every = 2f, route = "", at = new[] { 822.4f, -1622.2f, -15.2f, 0.00f } },
                    new Plan { name = "drive_yj_d", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1, drive = 70f, every = 2f, route = "", at = new[] { 822.4f, -1622.2f, -15.2f, 0.00f } },
                    new Plan { name = "drive_yjw", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, drive = 60f, every = 2f, route = "", at = new[] { 1252.2f, -1713.5f, 167.3f, 0.00f } },
                    new Plan { name = "drive_sa", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, drive = 50f, every = 2f, route = "", at = new[] { -346.5f, 1133.5f, -36.1f, 0.00f } },
                    new Plan { name = "drive_nb", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, drive = 60f, every = 2f, route = "", at = new[] { 273.8f, -1532.1f, -8.6f, 0.00f } },
                    new Plan { name = "drive_hw", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, drive = 40f, every = 2f, route = "", at = new[] { 277.3f, -1735.7f, 78.5f, 6.00f } },
                    new Plan { name = "drive_route1", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, drive = 320f, every = 2.5f, kmh = 50f, routeFile = "yeoksam1_demo1" },
                    new Plan { name = "drive_route1_d", st = () => Stages.Region(RegionCatalog.Load(1)), cam = CamMode.Driver, hud = true, kind = "region", weather = 0, time = 1, drive = 320f, every = 5f, kmh = 50f, routeFile = "yeoksam1_demo1" },
                    // 실제 차 직각 주차 안내 7단계: 게임 주차장에서 단계마다 차를 그 자리에 놓고 같은 자리에서 내려다본다
                    new Plan { name = "illust_park1", st = () => Stages.ParkingGuide(), gear = Gear.D, steer = 0f, at = new[] { 7.38f, 1.07f, 0f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "illust_park2", st = () => Stages.ParkingGuide(), gear = Gear.D, steer = -480f, at = new[] { 9.64f, -0.93f, -45f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "illust_park3", st = () => Stages.ParkingGuide(), gear = Gear.R, steer = 480f, at = new[] { 9.64f, -0.93f, -45f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "illust_park4", st = () => Stages.ParkingGuide(), gear = Gear.R, steer = 480f, at = new[] { 6.91f, 1.52f, -70f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "illust_park5", st = () => Stages.ParkingGuide(), gear = Gear.R, steer = 0f, at = new[] { 6.25f, 2.72f, -90f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "illust_park6", st = () => Stages.ParkingGuide(), gear = Gear.R, steer = 0f, at = new[] { 6.25f, 4.32f, -90f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "illust_park7", st = () => Stages.ParkingGuide(), gear = Gear.P, steer = 0f, at = new[] { 6.25f, 5.50f, -90f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f } },
                    new Plan { name = "video_park", st = () => Stages.ParkingGuide(), gear = Gear.D, at = new[] { -1.72f, 1.07f, 0f }, eyeAbs = new[] { 6.8f, -7.5f, 11f }, aim = new[] { 6.8f, 2.0f }, video = 15 },
                    // (실제 차 평행 주차) 평행 주차 7단계 영상(앱 parking_parNN)
                    new Plan { name = "video_ppark", st = () => Stages.ParallelGuide(), gear = Gear.D, at = new[] { 6.25f, -0.47f, 0f }, eyeAbs = new[] { 11.8f, -6.2f, 8.6f }, aim = new[] { 11.8f, 1.0f }, video = 15 },
                    new Plan { name = "video_route1", st = () => Stages.Region(RegionCatalog.Load(1)), cam = CamMode.Driver, hud = true, kind = "region", weather = 0, time = 1, drive = 400f, every = 5f, kmh = 50f, routeFile = "yeoksam1_demo1", video = 10 },
                    new Plan { name = "drive_rand0", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, drive = 120f, every = 3f },
                    new Plan { name = "drive_rand1", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, drive = 120f, every = 3f },
                    new Plan { name = "drive_rand2", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, drive = 120f, every = 3f },
                    new Plan { name = "grid_s0", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "grid_s1", st = () => Stages.Region(RegionCatalog.Load(1)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "grid_s2", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "grid_r0", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "grid_r1", st = () => Stages.Region(RegionCatalog.Load(1)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "grid_r2", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P },
                    // 바닥 검사: 어린이보호구역 빨간 노면, 과속방지턱, 버스정류장 큰길, 골목(세 동네)
                    new Plan { name = "floor_school", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -392.85f, -2052.29f, -39.3f } },
                    new Plan { name = "floor_hump", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 456.9f, -202.4f, 143.5f } },
                    new Plan { name = "fleet_show", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -561.0f, -2014.9f, -116.4f } },
                    new Plan { name = "floor_bus", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -561.0f, -2014.9f, -116.4f } },
                    new Plan { name = "floor_alley0", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "floor_alley1", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P },
                    new Plan { name = "floor_alley2", st = () => Stages.Region(RegionCatalog.Load(2)), kind = "region", weather = 0, time = 1, gear = Gear.P },
                    // 나머지 두 동네도 눈으로 확인
                    new Plan { name = "region1_chase", st = () => Stages.Region(RegionCatalog.Load(1)), hud = true, kind = "region", weather = 0, time = 1 },
                    new Plan { name = "region1_driver", st = () => Stages.Region(RegionCatalog.Load(1)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1 },
                    new Plan { name = "region1_top", st = () => Stages.Region(RegionCatalog.Load(1)), cam = CamMode.Top, kind = "region", weather = 0, time = 1 },
                    new Plan { name = "region2_chase", st = () => Stages.Region(RegionCatalog.Load(2)), hud = true, kind = "region", weather = 0, time = 1 },
                    new Plan { name = "region2_driver", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Driver, kind = "region", weather = 0, time = 1 },
                    new Plan { name = "region2_top", st = () => Stages.Region(RegionCatalog.Load(2)), cam = CamMode.Top, kind = "region", weather = 0, time = 1 },
                    // 다니는 차(역삼1동 강남대로, 30초 흘림)
                    new Plan { name = "region_traffic_chase", st = () => Stages.Region(RegionCatalog.Load(1)), kind = "region", weather = 0, time = 1, gear = Gear.P, sim = 30f, at = new[] { -454.5f, 961.5f, 69.7f } },
                    new Plan { name = "region_underpass_chase", st = () => Stages.Region(RegionCatalog.Load(0)), kind = "region", weather = 0, time = 1, at = new[] { 40.7f, -830.2f, 174.5f } },
                    new Plan { name = "region_hump_driver", st = () => Stages.Region(RegionCatalog.Load(0)), cam = CamMode.Driver, hud = true, kind = "region", weather = 0, time = 1, at = new[] { 456.9f, -202.4f, 143.5f } },
                    // 첫 해외 지역(독일, 목록의 country 로 찾음, 없으면 건너뜀): 아데나워 광장 지하차도 진입로(브란덴부르크 거리 오른쪽 차로 +2.9 m),
                    //   도랑 안(높이 -3.5 m), 위에서·공중에서, 독일 표지가 많은 기둥 셋(우선 도로·주차·50, 양보, 우선 도로·50)
                    new Plan { name = "de_ramp_driver", st = DeStage, cam = CamMode.Driver, hud = true, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -5.1f, 303.6f, -102.1f } },
                    new Plan { name = "de_ramp_chase", st = DeStage, hud = true, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -5.1f, 303.6f, -102.1f } },
                    new Plan { name = "de_trench_driver", st = DeStage, cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -21.8f, 225.4f, -102.1f, -3.5f } },
                    new Plan { name = "de_trench_top", st = DeStage, cam = CamMode.Top, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { -21.8f, 225.4f, -102.1f, -3.5f }, eye = new[] { 70f, 45f } },
                    new Plan { name = "de_signs_driver", st = DeStage, cam = CamMode.Driver, hud = true, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 14.3f, 355.9f, -101.4f },
                        eyeAbs = new[] { 13.3f, 337.7f, 2.4f }, aim = new[] { 9.5f, 318.1f } },
                    new Plan { name = "de_signs2_driver", st = DeStage, cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 365.9f, -318.6f, -84.1f } },
                    new Plan { name = "de_signs3_driver", st = DeStage, cam = CamMode.Driver, kind = "region", weather = 0, time = 1, gear = Gear.P, at = new[] { 331.9f, 48.9f, -69.6f } },
                };
                // 조사를 빨리 돌리려고: 사진 폴더에 only.txt 가 있으면 이름에 그 글자(줄마다 하나)가 든 사진만 찍는다(메뉴 사진은 건너뜀)
                string onlyFile = Path.Combine(outDir, "only.txt");
                string[] only = File.Exists(onlyFile) ? File.ReadAllLines(onlyFile).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray() : null;
                // 시연 영상: 실행 인자 -dcVideo 1 이면 영상 장면(video_*)만, 아니면 영상 장면은 건너뛴다
                //   값이 이름이면(-dcVideo park) 그 영상만: video_park = 직각 주차 7단계, video_route1 = 시연 경로
                string videoArg = Arg("-dcVideo", "");
                bool videoRun = videoArg != "";
                if (videoRun) only = new[] { videoArg == "1" ? "video_route" : "video_" + videoArg };
                foreach (var plan in plans)
                {
                    if (!videoRun && plan.video > 0) continue;
                    if (only != null && only.Length > 0 && !only.Any(o => plan.name.Contains(o))) continue;
                    if (plan.name.StartsWith("de_") && DeIndex() < 0) { Debug.Log("DCSETUP shot " + plan.name + " skip (독일 지역 없음)"); continue; }
                    try { Shoot(plan, outDir); }
                    catch (Exception e) { Debug.LogError("DCSETUP shot " + plan.name + " failed: " + e); code = 1; }
                }
                try { if (only == null || only.Length == 0 || only.Any(o => o.StartsWith("menu"))) MenuShots(outDir); }   // only.txt 에 menu… 가 있으면 메뉴 사진도
                catch (Exception e) { Debug.LogError("DCSETUP menu shots failed: " + e); code = 1; }
            }
            catch (Exception e) { Debug.LogError("DCSETUP shots failed: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>목록에서 나라가 독일인 첫 지역(나라 값으로 찾는다, 없으면 -1)</summary>
        static int DeIndex() => RegionCatalog.Entries.FindIndex(e => CountryStyle.For(e.country).code == "DE");
        static Stage DeStage() => Stages.Region(RegionCatalog.Load(DeIndex()));

        static void Shoot(Plan plan, string outDir)
        {
            int oldW = Prefs.Weather, oldT = Prefs.TimeOfDay;
            if (plan.weather >= 0) Prefs.Weather = plan.weather;
            if (plan.time >= 0) Prefs.TimeOfDay = plan.time;
            try { ShootNow(plan, outDir); }
            finally { Prefs.Weather = oldW; Prefs.TimeOfDay = oldT; }
        }

        static void ShootNow(Plan plan, string outDir)
        {
            Dc.Lap(null);
            var stg = plan.st(); Dc.Lap("stage-data");
            var world = new World(stg, CarSpec.Default()); Dc.Lap("world");
            world.car.gear = plan.gear;
            world.car.steerDeg = plan.steer;
            if (!string.IsNullOrEmpty(plan.routeFile))
            {
                var ta = Resources.Load<TextAsset>("DC/Routes/" + plan.routeFile);
                var ro = ta != null ? MiniJson.ParseObject(ta.text) : null;
                if (ro != null && ro.TryGetValue("start", out var sv) && sv is System.Collections.Generic.List<object> sl && sl.Count >= 3)
                    plan.at = new[] { Convert.ToSingle(sl[0]), Convert.ToSingle(sl[1]), Convert.ToSingle(sl[2]) };
            }
            if (plan.at != null && plan.at.Length >= 3)
            {
                var car = world.car;
                car.heading = plan.at[2] * Geom.Deg2Rad;
                car.pos = new V2(plan.at[0], plan.at[1]) - car.Dir * ((car.wheelbase + car.frontOh - car.rearOh) / 2f);
                if (plan.at.Length >= 4) car.z = plan.at[3];     // 지하·고가 길 위에 세울 때: 그 높이에서 시작해야 길 높이를 따라간다
            }
            TrafficCar spawned = null;
            if (plan.spawnKind >= 0)
            {
                world.Update(1f / 60f, plan.steer, false, 0f);     // 다른 차가 먼저 생기게
                spawned = world.traffic?.SpawnKindNear(world.car.Center - new V2(-world.car.Dir.y, world.car.Dir.x) * plan.spawnLeft, world.car.Dir, plan.spawnAhead, plan.spawnKind);
            }
            for (float t = 0f; t < plan.sim; t += 1f / 60f) world.Update(1f / 60f, plan.steer, false, 0f);
            if ((plan.untilRedPed || plan.untilRed) && world.st.road != null)
            {
                var car = world.car; var rd = world.st.road; var fr = car.FrontBumperCenter;
                TrafficSignal sg = null; float bd = 25f;
                foreach (var sl in rd.stopLines) { if (sl.signal < 0 || sl.approach.Dot(car.Dir) < 0.8f) continue; float d = ((sl.a + sl.b) / 2f).DistanceTo(fr); if (d < bd) { bd = d; sg = rd.signals[sl.signal]; } }
                bool ok = false;
                for (float t = 0f; t < 400f && !ok; t += 1f / 30f)
                {
                    world.Update(1f / 30f, 0f, true, 0f, 1f);
                    bool red = sg == null || (sg.At(world.t) == SignalColor.Red && sg.At(world.t - 4f) == SignalColor.Red);
                    bool ped = false;
                    if (world.crossPeds != null) foreach (var pp in world.crossPeds.peds) if (pp.onRoad && pp.t > 0.25f && pp.t < 0.75f && pp.pos.DistanceTo(fr) < 34f && pp.pos.DistanceTo(fr) > 9f) ped = true;
                    ok = red && (ped || plan.untilRed);
                }
                Debug.Log("DCSHOT " + plan.name + " untilRedPed " + (ok ? "맞춤" : "못 맞춤") + " t=" + world.t.ToString("0.0") + " signal=" + (sg != null));
            }
            var scene = SceneRig.Build(world, null);
            if (plan.emergency && spawned != null)
            {
                // 경광등: 지붕 위 가로 막대(왼쪽 빨강·오른쪽 파랑)
                var bar = new GameObject("emergency-bar");
                bar.transform.SetParent(scene.root.transform, false);
                bar.transform.position = Space3D.W(spawned.pos, spawned.z + (spawned.kind == 4 ? 2.02f : 1.5f));
                bar.transform.rotation = Quaternion.Euler(0f, spawned.heading * Mathf.Rad2Deg, 0f);
                for (int bi = 0; bi < 2; bi++)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(bar.transform, false);
                    cube.transform.localPosition = new Vector3(0.2f, 0f, bi == 0 ? 0.32f : -0.32f);
                    cube.transform.localScale = new Vector3(0.28f, 0.14f, 0.6f);
                    cube.GetComponent<Renderer>().sharedMaterial = DcAssets.Unlit(bi == 0 ? new Color(1f, 0.12f, 0.1f) : new Color(0.15f, 0.35f, 1f));
                }
            }
            if (plan.arrow != null && plan.arrow.Length >= 4) GroundArrow(plan.arrow, scene.root.transform);
            if (plan.arrows != null) for (int ai = 0; ai < plan.arrows.Length; ai++) GroundArrow(plan.arrows[ai], scene.root.transform, plan.arrowStyle != null && ai < plan.arrowStyle.Length ? plan.arrowStyle[ai] : 0, ai);
            scene.cameraRig.Snap(plan.cam, world);
            scene.cameraRig.lookYaw = plan.look;
            scene.hudMirrors = plan.mirrors;
            scene.info = plan.kind == "circuit" ? "1/2 바퀴\n0:12.3" : (plan.kind == "region" ? "제한 30\n자동" : "");
            scene.Tick(world, 0.016f, true);
            var cam = scene.cameraRig.cam;
            var rt = new RenderTexture(ShotW, ShotH, 24);
            cam.targetTexture = rt;
            Hud hud = null;
            if (plan.hud)
            {
                hud = new Hud(null, cam);
                UiKit.FixScale(hud.canvas, ShotH / 1080f);
                hud.Bind(scene);
                var m = new HudModel
                {
                    w = world, tag = plan.tag, kind = plan.kind, cam = plan.cam, mirrors = plan.mirrors, driverView = plan.cam == CamMode.Driver, brakeOn = true,
                    info = plan.kind == "circuit" ? "자동 보통, 0 km/h, 1/2 바퀴, 0:00.0" : (plan.kind == "road" || plan.kind == "region" ? "자동 보통, 0 / 제한 30 km/h" : ""),
                };
                for (int i = 0; i < 2; i++) { Canvas.ForceUpdateCanvases(); hud.Tick(m); }
                Canvas.ForceUpdateCanvases();
                vidHud = hud; vidModel = m;
            }
            if (plan.name == "video_park") { ParkVideo(plan, world, scene, cam, rt, outDir); }
            if (plan.name == "video_ppark") { ParVideo(plan, world, scene, cam, rt, outDir); }
            if (plan.drive > 0f) { DriveShots(plan, world, scene, cam, rt, outDir); }
            scene.RenderAuxNow(world);
            Canvas.ForceUpdateCanvases();
            Save(cam, rt, Path.Combine(outDir, "shot_" + plan.name + ".png"));
            if (plan.name == "region_chase" || plan.name == "stage08_driver_hud") MemReport(plan.name);
            if (plan.name.StartsWith("diag_")) GroundDiag(plan, world, cam, rt, outDir);
            if (plan.name.StartsWith("floor_")) FloorShots(plan, world, cam, rt, outDir);
            if (plan.name.StartsWith("grid_")) GridShots(plan, world, cam, outDir);
            if (plan.name.StartsWith("fleet_")) FleetShots(plan, world, cam, rt, outDir);
            if ((plan.eye != null || plan.eyeAbs != null) && plan.at != null) AirShot(plan, cam, rt, outDir);
            if (hud != null) Dc.Kill(hud.canvas.gameObject);
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            scene.Dispose();
            // 사진마다 지은 맵의 메시·텍스처를 바로 내린다(안 내리면 22장 찍는 동안 92 GB 까지 불어나 메모리 부족으로 꺼짐)
            EditorUtility.UnloadUnusedAssetsImmediate(true);
            GC.Collect();
            Debug.Log("DCSETUP shot " + plan.name + " | mem " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20) + " MB, gfx " + (UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20) + " MB");
        }

        /// <summary>장면 하나가 메모리를 어디에 쓰는지: 종류별 합계와 큰 것 25개. 에뮬레이터(3 GB)에서 실제 동네가 메모리 부족으로 죽는 원인 찾기.</summary>
        /// <summary>
        /// 조사용: 길 옆 땅이 하늘빛으로 비어 보이는 자리에서 땅(Floor/ground)이 실제로 그려지는지.
        /// 땅의 재질·셰이더·키워드를 적고, 차 오른쪽 25 m 점을 덮는 삼각형이 있는지와 앞면 방향을 보고,
        /// </summary>
        static void GroundDiag(Plan plan, World world, Camera cam, RenderTexture rt, string outDir)
        {
            var c3 = Space3D.W(world.car.Center, 0f);
            var fw = Space3D.W(world.car.Center + world.car.Dir, 0f) - c3;
            var q = c3 + Vector3.Cross(Vector3.up, fw).normalized * 25f;
            foreach (var mr in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (mr.name != "ground") continue;
                var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                var mat = mr.sharedMaterial;
                int cover = 0, up = 0;
                if (mesh != null && mesh.isReadable)
                {
                    var vs = mesh.vertices; var ts = mesh.triangles;
                    var l2w = mr.transform.localToWorldMatrix;
                    for (int i = 0; i + 2 < ts.Length; i += 3)
                    {
                        Vector3 a = l2w.MultiplyPoint3x4(vs[ts[i]]), b = l2w.MultiplyPoint3x4(vs[ts[i + 1]]), c = l2w.MultiplyPoint3x4(vs[ts[i + 2]]);
                        float d1 = (q.x - b.x) * (a.z - b.z) - (a.x - b.x) * (q.z - b.z);
                        float d2 = (q.x - c.x) * (b.z - c.z) - (b.x - c.x) * (q.z - c.z);
                        float d3 = (q.x - a.x) * (c.z - a.z) - (c.x - a.x) * (q.z - a.z);
                        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                        if (neg && pos) continue;
                        cover++;
                        if (Vector3.Cross(b - a, c - a).y > 0f) up++;
                    }
                }
                Debug.Log($"DCMEM {plan.name} ground: parent={mr.transform.parent?.name} pos={mr.transform.position} bounds={mr.bounds.center}/{mr.bounds.size} tris={(mesh != null && mesh.isReadable ? mesh.triangles.Length / 3 : -1)} readable={(mesh != null && mesh.isReadable)} cover@25m={cover} crossUp={up}"
                    + $" mat={(mat != null ? mat.name : "-")} shader={(mat != null ? mat.shader.name : "-")} kw=[{(mat != null ? string.Join(",", mat.shaderKeywords) : "")}]"
                    + $" metal={(mat != null && mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : -1f)} smooth={(mat != null && mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : -1f)}"
                    + $" color={(mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "-")} queue={(mat != null ? mat.renderQueue : -1)} recv={mr.receiveShadows} layer={mr.gameObject.layer} camMask={cam.cullingMask} camY={cam.transform.position.y:0.0} far={cam.farClipPlane}");
                // 땅 확인 사진은 찍지 않고 기록 줄만 남긴다.
            }
        }

        /// <summary>
        /// 바닥 검사 사진.
        /// 차 둘레의 바닥을 가까이서 여러 각도로 찍는다: 오른쪽 연석, 왼쪽 연석, 앞 노면(비스듬히), 앞 노면(위에서), 뒤 노면.
        /// </summary>
        /// <summary>다른 차 8종을 내 차 앞 12 m 에 하나씩 세워 앞·뒤 비스듬히, 옆에서 찍는다</summary>
        static void FleetShots(Plan plan, World world, Camera cam, RenderTexture rt, string outDir)
        {
            var c = Space3D.W(world.car.Center, 0f);
            var fw = (Space3D.W(world.car.Center + world.car.Dir, 0f) - c).normalized;
            var rightV = Vector3.Cross(Vector3.up, fw).normalized;
            string[] names = { "sedan", "suv", "hatch", "taxi", "van", "truck", "bus", "townbus" };
            float keepFov = cam.fieldOfView;
            cam.fieldOfView = 40f;
            for (int k = 0; k < names.Length; k++)
            {
                var go = FleetModels.Make(names[k], null, TrafficView.Pick(TrafficView.PaletteOf(k), k * 7 + 1));
                if (go == null) continue;
                var at = c + fw * 14f;
                go.transform.position = at;
                go.transform.rotation = Quaternion.LookRotation(fw, Vector3.up);
                float d = k >= 5 ? 13f : 7.5f;
                var views = new (string n, float f, float r, float h)[] { ("front", 0.8f, 0.6f, 1.5f), ("rear", -0.8f, 0.6f, 1.5f), ("side", 0f, 1f, 1.1f) };
                foreach (var v in views)
                {
                    var eye = at + (fw * v.f + rightV * v.r).normalized * d + Vector3.up * (k >= 5 ? v.h + 0.8f : v.h);
                    var tgt = at + Vector3.up * (k >= 5 ? 1.3f : 0.7f);
                    cam.transform.position = eye;
                    cam.transform.rotation = Quaternion.LookRotation((tgt - eye).normalized, Vector3.up);
                    Save(cam, rt, Path.Combine(outDir, "shot_" + plan.name + "_" + names[k] + "_" + v.n + ".png"));
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            cam.fieldOfView = keepFov;
        }

        static void FloorShots(Plan plan, World world, Camera cam, RenderTexture rt, string outDir)
        {
            var c = Space3D.W(world.car.Center, 0f);
            var fw = (Space3D.W(world.car.Center + world.car.Dir, 0f) - c).normalized;
            var rightV = Vector3.Cross(Vector3.up, fw).normalized;
            // (이름, 눈 자리: 앞·오른쪽·높이, 보는 곳: 앞·오른쪽)
            var views = new (string n, float ef, float er, float eh, float tf, float tr)[]
            {
                ("curbR", -1f, 1.2f, 1.5f, 5f, 4.2f),
                ("curbL", -1f, -1.2f, 1.5f, 5f, -4.2f),
                ("ahead", 3f, 0f, 2.4f, 11f, 0f),
                ("down", 8f, 0f, 9f, 8.5f, 0f),
                ("back", -3f, 0f, 2.4f, -11f, 0f),
                ("sideR", 2f, 2.5f, 0.6f, 9f, 3.6f),
            };
            float keepFov = cam.fieldOfView;
            cam.fieldOfView = 55f;
            foreach (var v in views)
            {
                var eye = c + fw * v.ef + rightV * v.er + Vector3.up * v.eh;
                var tgt = c + fw * v.tf + rightV * v.tr;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation((tgt - eye).normalized, v.n == "down" ? fw : Vector3.up);
                Save(cam, rt, Path.Combine(outDir, "shot_" + plan.name + "_" + v.n + ".png"));
            }
            cam.fieldOfView = keepFov;
        }

        /// <summary>
        /// 동네 경계를 감싼 네모를 4 × 4 칸으로 나눠 곧바로 위에서 찍는다(북쪽이 위).
        /// shot_grid_이름_all.png(전체) + shot_grid_이름_A1 … D4.png(줄 A~D = 북→남, 칸 1~4 = 서→동). 칸 자리는 grid_이름.txt 에 적는다.
        /// </summary>
        /// <summary>설명 그림용: 바닥에 굵은 화살표(흰 테두리 + 검정). 점들을 부드럽게 이어 0.5 m 간격으로 띠를 깐다</summary>
        static float[] RouteSlice(float s0, float s1)
        {
            var a = RouteArrow; if (a == null) return null;
            var o = new System.Collections.Generic.List<float>(); float s = 0f;
            for (int i = 0; i + 1 < a.Length; i += 2)
            {
                if (i >= 2) s += Mathf.Sqrt((a[i] - a[i - 2]) * (a[i] - a[i - 2]) + (a[i + 1] - a[i - 1]) * (a[i + 1] - a[i - 1]));
                if (s >= s0 && s <= s1) { o.Add(a[i]); o.Add(a[i + 1]); }
            }
            return o.ToArray();
        }

        static void GroundArrow(float[] xy, Transform parent, int style = 0, int layer = 0)
        {
            if (xy == null || xy.Length < 4) return;
            var pts = new System.Collections.Generic.List<V2>();
            for (int i = 0; i + 1 < xy.Length; i += 2) pts.Add(new V2(xy[i], xy[i + 1]));
            var sm = new System.Collections.Generic.List<V2>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                V2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                int n = Mathf.Max(2, Mathf.CeilToInt(p1.DistanceTo(p2) / 0.5f));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    sm.Add((p1 * 2f + (p2 - p0) * t + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f);
                }
            }
            sm.Add(pts[pts.Count - 1]);
            void Ribbon(float half, float head, float y, Color col, string name)
            {
                var v = new System.Collections.Generic.List<Vector3>(); var tr = new System.Collections.Generic.List<int>();
                float headLen = head * 1.25f; float total = 0f; for (int i = 1; i < sm.Count; i++) total += sm[i].DistanceTo(sm[i - 1]);
                float acc = 0f; int last = 0;
                for (int i = 0; i < sm.Count; i++)
                {
                    if (i > 0) acc += sm[i].DistanceTo(sm[i - 1]);
                    if (total - acc < headLen && i > 1) break;
                    var d = (sm[Mathf.Min(sm.Count - 1, i + 1)] - sm[Mathf.Max(0, i - 1)]).Normalized; var nr = new V2(-d.y, d.x);
                    v.Add(Space3D.W(sm[i] - nr * half, y)); v.Add(Space3D.W(sm[i] + nr * half, y)); last = i;
                    bool gap = style == 2 && ((int)(acc / 2.2f)) % 2 == 1 && total - acc > headLen + 2.5f;      // 점선: 2.2 m 칠하고 2.2 m 비움(머리 앞은 이어서)
                    if (i > 0 && !gap) { int b = v.Count - 4; tr.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3, b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
                }
                var tip = sm[sm.Count - 1]; var dd = (tip - sm[last]).Normalized; var nn = new V2(-dd.y, dd.x); var baseP = sm[last];
                int h0 = v.Count; v.Add(Space3D.W(baseP - nn * head, y)); v.Add(Space3D.W(baseP + nn * head, y)); v.Add(Space3D.W(tip + dd * (half * 0.6f), y));
                tr.AddRange(new[] { h0, h0 + 1, h0 + 2, h0, h0 + 2, h0 + 1 });
                var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(tr, 0); m.RecalculateBounds(); m.RecalculateNormals();
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = DcAssets.UnlitInstance(col); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            float y0 = 0.10f + layer * 0.08f;
            Ribbon(0.95f, 2.1f, y0, Color.white, "illust-arrow-edge");
            Ribbon(0.62f, 1.7f, y0 + 0.04f, style == 1 ? new Color(0.86f, 0.12f, 0.10f) : new Color(0.07f, 0.07f, 0.07f), "illust-arrow");
        }

        /// <summary>입체 교차를 비스듬히 내려다본 사진</summary>
        static void AirShot(Plan plan, Camera cam, RenderTexture rt, string outDir)
        {
            float h = plan.at[2] * Mathf.Deg2Rad;
            var target = plan.aim != null ? Space3D.W(new V2(plan.aim[0], plan.aim[1]), 0f) : Space3D.W(new V2(plan.at[0], plan.at[1]), 3f);
            var eye = plan.eyeAbs != null ? Space3D.W(new V2(plan.eyeAbs[0], plan.eyeAbs[1]), plan.eyeAbs[2]) : Space3D.W(new V2(plan.at[0] - Mathf.Cos(h) * plan.eye[0], plan.at[1] - Mathf.Sin(h) * plan.eye[0]), plan.eye[1]);
            bool fog = RenderSettings.fog; float far = cam.farClipPlane, fov = cam.fieldOfView;
            RenderSettings.fog = false; cam.farClipPlane = 2500f; cam.fieldOfView = 50f;
            cam.transform.position = eye; cam.transform.LookAt(target);
            Save(cam, rt, Path.Combine(outDir, "shot_" + plan.name + "_air.png"));
            RenderSettings.fog = fog; cam.farClipPlane = far; cam.fieldOfView = fov;
        }

        /// <summary>주행 점검: 자동 운전(AutoDrive + AutoSpeed)으로 plan.drive 초를 달리며 every 초마다 사진. 끝에 기록(거리·차로 벗어남·부딪힘·막다른 곳).</summary>
        static Hud vidHud; static HudModel vidModel;

        /// <summary>
        /// 실제 차 직각 주차 7단계를 단계별 영상 장면으로: unity/video/video_park/s단계_번호.jpg (1280x720, 초당 plan.video 장).
        /// 차는 운전해서가 아니라 회전 반경으로 계산한 자리에 장면마다 놓는다(뒤축 가운데가 도는 반지름 r, 핸들 끝까지 = 원호).
        ///   1 접근(직진) → 2 왼쪽 끝까지 돌리고 45° 전진 → 3 R·오른쪽 끝까지(제자리) → 4 후진해 칸과 나란히 → 5 핸들 풀기 → 6 똑바로 뒤로 → 7 P
        /// 좌표: 목표 칸 x 5~7.5, y 3~8(통로 y -3~3), 차 방향 0° = +x, 오른쪽 = +y.
        /// </summary>
        static void ParkVideo(Plan plan, World world, SceneRig scene, Camera cam, RenderTexture rt, string outDir)
        {
            string vdir = Path.GetFullPath(Path.Combine(outDir, "..", "video", plan.name));
            if (Directory.Exists(vdir)) foreach (var f in Directory.GetFiles(vdir, "*.jpg")) File.Delete(f);
            Directory.CreateDirectory(vdir);
            var small = new RenderTexture(1280, 720, 0); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var car = world.car;
            float back = (car.wheelbase + car.frontOh - car.rearOh) / 2f;       // 차 가운데 → 뒤축
            float R = Mathf.Sqrt(Mathf.Max(1f, car.spec.minTurningRadiusM * car.spec.minTurningRadiusM - car.wheelbase * car.wheelbase)) - car.width / 2f;   // 뒤축 가운데의 회전 반지름
            float full = car.maxSteerDeg, y0 = 1.07f, xSlot = 6.25f;
            float x0 = xSlot + R - 2f * R * Mathf.Sin(45f * Mathf.Deg2Rad);     // 45° 전진 뒤 끝까지 후진하면 칸 가운데로 들어가는 정지 자리
            float yEnd = 5.5f + back;                                          // 칸 가운데에 섰을 때 뒤축
            int fps = Mathf.Max(5, plan.video);
            Func<float, V2> right = th => new V2(-Mathf.Sin(th), Mathf.Cos(th));
            Func<float, float> ease = u => u * u * (3f - 2f * u);
            var c2 = new V2(x0, y0 - R);                                       // 2단계 원호 중심(차 왼쪽)
            float a45 = -45f * Mathf.Deg2Rad, a90 = -90f * Mathf.Deg2Rad;
            var p45 = c2 + right(a45) * R;
            var c4 = p45 + right(a45) * R;                                     // 4단계 원호 중심(차 오른쪽)
            var pIn = c4 - right(a90) * R;
            Action<int, float, Func<float, (V2 p, float th, float steer, Gear g)>> seg = (step, sec, fn) =>
            {
                int n = Mathf.RoundToInt(sec * fps);
                for (int i = 0; i < n; i++)
                {
                    var q = fn(n > 1 ? i / (float)(n - 1) : 1f);
                    car.heading = q.th; car.pos = q.p; car.steerDeg = q.steer; car.gear = q.g; car.speed = 0f;
                    scene.Tick(world, 1f / fps, false);
                    cam.transform.position = Space3D.W(new V2(plan.eyeAbs[0], plan.eyeAbs[1]), plan.eyeAbs[2]);
                    cam.transform.LookAt(Space3D.W(new V2(plan.aim[0], plan.aim[1]), 0f));
                    cam.fieldOfView = 50f;
                    DcRender.Now(cam, rt);
                    VideoFrame(rt, small, tex, Path.Combine(vdir, "s" + step + "_" + i.ToString("00000") + ".jpg"));
                }
            };
            bool fog = RenderSettings.fog; float fov = cam.fieldOfView; RenderSettings.fog = false;
            seg(1, 4.0f, u => (new V2(Mathf.Lerp(x0 - 7.5f, x0, 1f - (1f - u) * (1f - u)), y0), 0f, 0f, Gear.D));
            seg(2, 4.5f, u => { float k = Mathf.Clamp01(u / 0.22f), m = ease(Mathf.Clamp01((u - 0.22f) / 0.78f)); float th = a45 * m; return (c2 + right(th) * R, th, -full * k, Gear.D); });
            seg(3, 3.0f, u => (p45, a45, Mathf.Lerp(-full, full, Mathf.Clamp01(u / 0.8f)), u < 0.15f ? Gear.D : Gear.R));
            seg(4, 5.0f, u => { float th = Mathf.Lerp(a45, a90, ease(u)); return (c4 - right(th) * R, th, full, Gear.R); });
            seg(5, 2.5f, u => (pIn, a90, Mathf.Lerp(full, 0f, Mathf.Clamp01(u / 0.8f)), Gear.R));
            seg(6, 4.0f, u => (new V2(xSlot, Mathf.Lerp(pIn.y, yEnd, ease(u))), a90, 0f, Gear.R));
            seg(7, 2.0f, u => (new V2(xSlot, yEnd), a90, 0f, u < 0.3f ? Gear.R : Gear.P));
            RenderSettings.fog = fog; cam.fieldOfView = fov;
            small.Release(); UnityEngine.Object.DestroyImmediate(small); UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllText(Path.Combine(vdir, "summary.txt"), string.Format(CultureInfo.InvariantCulture, "r={0:0.00} x0={1:0.00} p45=({2:0.00},{3:0.00}) in=({4:0.00},{5:0.00}) end y={6:0.00} fps={7}", R, x0, p45.x, p45.y, pIn.x, pIn.y, yEnd, fps));
            Debug.Log("DCVIDEO " + plan.name + " done");
        }

        /// <summary>
        /// 실제 차 평행 주차 7단계 영상: unity/video/video_ppark/s단계_번호.jpg.
        /// 앱 core ParallelParkingProcedure 와 같은 순서. 차는 회전 반경으로 계산한 자리에 장면마다 놓는다(뒤축 가운데가 반지름 r 원호).
        ///   1 앞차 옆 0.8 m 띄워 나란히, 뒤 범퍼를 맞춰 정지 → 2 R, 똑바로 1.5 m 후진(앞차 뒤 끝이 내 뒷좌석 창) → 3 핸들 오른쪽 끝 →
        ///   4 후진해 45° → 5 핸들 가운데, 똑바로 후진(내 오른쪽 앞 끝이 앞차 뒤 끝을 지날 때까지) → 6 핸들 왼쪽 끝, 후진해 길과 나란히 → 7 핸들 가운데, D 로 앞뒤 가운데, P
        /// 좌표(ParallelStage 14): 칸 x 6~12, 주차 차로 가운데 y 2.2, 연석 y 3.2, 앞차 뒤 끝 x 12.65, 뒤차 앞 끝 x 5.35. 차 방향 0° = +x, 오른쪽 = +y(연석).
        /// 연석에서 0.3 m 에 서도록 45° 직진 후진 길이를 정한다(아반떼 CN8: 원호 동안 뒤차 0.35 m·앞차 0.69 m·연석 0.16 m 이상 떨어짐을 계산으로 확인).
        /// </summary>
        static void ParVideo(Plan plan, World world, SceneRig scene, Camera cam, RenderTexture rt, string outDir)
        {
            string vdir = Path.GetFullPath(Path.Combine(outDir, "..", "video", plan.name));
            if (Directory.Exists(vdir)) foreach (var f in Directory.GetFiles(vdir, "*.jpg")) File.Delete(f);
            Directory.CreateDirectory(vdir);
            var small = new RenderTexture(1280, 720, 0); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var car = world.car;
            float back = (car.wheelbase + car.frontOh - car.rearOh) / 2f;       // 차 가운데 → 뒤축
            float R = Mathf.Sqrt(Mathf.Max(1f, car.spec.minTurningRadiusM * car.spec.minTurningRadiusM - car.wheelbase * car.wheelbase)) - car.width / 2f;
            float full = car.maxSteerDeg;
            const float frontRear = 12.65f, laneY = 2.2f, curbY = 3.2f, slotMid = 9.0f, frontCarHalfW = 0.94f;
            float y0 = laneY - frontCarHalfW - 0.8f - car.width / 2f;          // 앞차 왼쪽 옆에서 0.8 m
            float yf = curbY - 0.3f - car.width / 2f;                          // 끝: 연석에서 0.3 m
            float xA = frontRear + car.rearOh, xB = xA - 1.1f, xC = slotMid - back;   // 1.5 m 후진이면 끝에서 뒤차와 0.35 m·뒤 범퍼가 칸 밖 → 1.1 m(앞차 뒤 끝 = 내 뒷바퀴)
            int fps = Mathf.Max(5, plan.video);
            Func<float, V2> right = th => new V2(-Mathf.Sin(th), Mathf.Cos(th));
            Func<float, V2> fwd = th => new V2(Mathf.Cos(th), Mathf.Sin(th));
            Func<float, float> ease = u => u * u * (3f - 2f * u);
            float a45 = -45f * Mathf.Deg2Rad;
            var c4 = new V2(xB, y0 + R);                                      // 4단계 원호 중심(차 오른쪽)
            var p4e = c4 - right(a45) * R;
            float s = Mathf.Max(0f, ((yf - R) + right(a45).y * R - p4e.y) / Mathf.Sin(-a45));
            var p8s = p4e - fwd(a45) * s;
            var c8 = p8s - right(a45) * R;                                    // 6단계 원호 중심(차 왼쪽)
            var pf = c8 + right(0f) * R;
            Action<int, float, Func<float, (V2 p, float th, float steer, Gear g)>> seg = (step, sec, fn) =>
            {
                int n = Mathf.RoundToInt(sec * fps);
                for (int i = 0; i < n; i++)
                {
                    var q = fn(n > 1 ? i / (float)(n - 1) : 1f);
                    car.heading = q.th; car.pos = q.p; car.steerDeg = q.steer; car.gear = q.g; car.speed = 0f;
                    scene.Tick(world, 1f / fps, false);
                    cam.transform.position = Space3D.W(new V2(plan.eyeAbs[0], plan.eyeAbs[1]), plan.eyeAbs[2]);
                    cam.transform.LookAt(Space3D.W(new V2(plan.aim[0], plan.aim[1]), 0f));
                    cam.fieldOfView = 50f;
                    DcRender.Now(cam, rt);
                    VideoFrame(rt, small, tex, Path.Combine(vdir, "s" + step + "_" + i.ToString("00000") + ".jpg"));
                }
            };
            bool fog = RenderSettings.fog; float fov = cam.fieldOfView; RenderSettings.fog = false;
            seg(1, 4.0f, u => (new V2(Mathf.Lerp(xA - 7.5f, xA, 1f - (1f - u) * (1f - u)), y0), 0f, 0f, Gear.D));
            seg(2, 3.5f, u => (new V2(Mathf.Lerp(xA, xB, ease(Mathf.Clamp01((u - 0.2f) / 0.8f))), y0), 0f, 0f, u < 0.15f ? Gear.D : Gear.R));
            seg(3, 2.5f, u => (new V2(xB, y0), 0f, Mathf.Lerp(0f, full, Mathf.Clamp01(u / 0.8f)), Gear.R));
            seg(4, 4.5f, u => { float th = Mathf.Lerp(0f, a45, ease(u)); return (c4 - right(th) * R, th, full, Gear.R); });
            seg(5, 4.0f, u => (p4e - fwd(a45) * (s * ease(Mathf.Clamp01((u - 0.4f) / 0.6f))), a45, Mathf.Lerp(full, 0f, Mathf.Clamp01(u / 0.4f)), Gear.R));
            seg(6, 5.5f, u => { float th = Mathf.Lerp(a45, 0f, ease(Mathf.Clamp01((u - 0.25f) / 0.75f))); return (c8 + right(th) * R, th, Mathf.Lerp(0f, -full, Mathf.Clamp01(u / 0.25f)), Gear.R); });
            seg(7, 4.0f, u => (new V2(Mathf.Lerp(pf.x, xC, ease(Mathf.Clamp01((u - 0.3f) / 0.5f))), pf.y), 0f, Mathf.Lerp(-full, 0f, Mathf.Clamp01(u / 0.25f)), u < 0.3f ? Gear.R : (u < 0.85f ? Gear.D : Gear.P)));
            RenderSettings.fog = fog; cam.fieldOfView = fov;
            small.Release(); UnityEngine.Object.DestroyImmediate(small); UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllText(Path.Combine(vdir, "summary.txt"), string.Format(CultureInfo.InvariantCulture, "r={0:0.00} y0={1:0.00} xA={2:0.00} xB={3:0.00} p45=({4:0.00},{5:0.00}) s={6:0.00} pf=({7:0.00},{8:0.00}) xC={9:0.00} fps={10}", R, y0, xA, xB, p4e.x, p4e.y, s, pf.x, pf.y, xC, fps));
            Debug.Log("DCVIDEO " + plan.name + " done");
        }

        /// <summary>영상 한 장: 지금 카메라 그림(rt)을 1280x720 으로 줄여 JPG 로</summary>
        static void VideoFrame(RenderTexture rt, RenderTexture small, Texture2D tex, string path)
        {
            Graphics.Blit(rt, small);
            var prev = RenderTexture.active; RenderTexture.active = small;
            tex.ReadPixels(new UnityEngine.Rect(0, 0, small.width, small.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToJPG(80));
        }

        static void DriveShots(Plan plan, World world, SceneRig scene, Camera cam, RenderTexture rt, string outDir)
        {
            // 시연 영상: 초당 plan.video 장을 운전석(d_)·따라가기(c_) 두 시점으로 쓰고, 장마다 위치·속도·위반 수를 표(frames.csv)로 남긴다
            int vfrom = 0, vcount = 500; bool vmore = false;
            string vdir = null; RenderTexture small = null; Texture2D vtex = null; float vnext = 0f; int vn = 0;
            var csv = new System.Text.StringBuilder("n,t,x,y,z,head,kmh,limit,lane,routeS,laneDepart,bump,redLight,stopLine,speeding,noSignal,why\n");
            if (plan.video > 0)
            {
                vdir = Path.GetFullPath(Path.Combine(outDir, "..", "video", plan.name));
                // 한 번에 수천 장을 그리면 그래픽 장치 오류(d3d12)로 편집기가 죽는다(1400장째) → 실행 한 번에 -dcVideoCount 장씩.
                // 다음 실행은 처음부터 다시 달리되(같은 결과) -dcVideoFrom 장까지는 그리지 않고 건너뛴다. 다 달리면 summary.txt 를 쓴다.
                int.TryParse(Arg("-dcVideoFrom", "0"), out vfrom); if (!int.TryParse(Arg("-dcVideoCount", "500"), out vcount)) vcount = 500;
                if (vfrom == 0 && Directory.Exists(vdir)) foreach (var f in Directory.GetFiles(vdir, "*.*")) File.Delete(f);
                Directory.CreateDirectory(vdir);
                small = new RenderTexture(1280, 720, 0); vtex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            }
            string dir = Path.Combine(outDir, "drive");
            Directory.CreateDirectory(dir);
            AutoDrive auto = null;
            if (!string.IsNullOrEmpty(plan.routeFile)) { var ta = Resources.Load<TextAsset>("DC/Routes/" + plan.routeFile); if (ta != null) auto = AutoDrive.FromRouteJson(ta.text); }
            if (auto == null) auto = new AutoDrive(1, plan.route);
            var car = world.car;
            car.gear = Gear.D;
            const float dt = 1f / 30f;
            float next = 0f, dist = 0f, steer = 0f, minZ = 0f, maxZ = 0f; int n = 0;
            var log = new System.Text.StringBuilder();
            for (float t = 0f; t <= plan.drive; t += dt)
            {
                car.gear = auto.Reverse ? Gear.R : Gear.D;
                float want = auto.Drive(world, dt, plan.kmh, out float ac, out bool br, out float ba);
                steer = Mathf.MoveTowards(steer, want, 900f * dt);
                car.turnSignal = auto.Blinker; car.signalKnown = true;
                var before = car.pos;
                world.Update(dt, steer, br, ac, ba);
                dist += car.pos.DistanceTo(before);
                minZ = Mathf.Min(minZ, car.z); maxZ = Mathf.Max(maxZ, car.z);
                if (auto.RouteDone && Mathf.Abs(car.speed) < 0.1f) { log.AppendLine(string.Format(CultureInfo.InvariantCulture, "t={0:0.0} 경로 끝 도착", t)); break; }
                if (world.state != PlayState.Play) { log.AppendLine(string.Format(CultureInfo.InvariantCulture, "t={0:0.0} 판 끝남: {1}", t, world.state)); break; }
                if (vdir != null && t >= vnext)
                {
                    float vdt = 1f / plan.video; vnext += vdt;
                    var rr0 = world.roadRun;
                    scene.info = rr0 != null && rr0.limitKmh > 0f ? "제한 " + rr0.limitKmh.ToString("0") + "\n자동" : "자동";
                    scene.Tick(world, vdt, false);
                    if (vn >= vfrom + vcount) { vmore = true; break; }
                    if (vn >= vfrom && vidHud != null && vidModel != null)
                    {
                        int k = Mathf.RoundToInt(Mathf.Abs(car.speed) * 3.6f);
                        vidModel.info = rr0 != null && rr0.limitKmh > 0f ? "자동 보통, " + k + " / 제한 " + rr0.limitKmh.ToString("0") + " km/h" : "자동 보통, " + k + " km/h";
                        vidModel.steerDeg = steer; vidModel.maxSteerDeg = car.maxSteerDeg; vidModel.brakeOn = br; vidModel.realGear = "D";
                        vidModel.blink = scene.carView.BlinkNow(car);
                    }
                    if (vn >= vfrom) foreach (var cm in new[] { CamMode.Driver, CamMode.Chase })
                    {
                        scene.cameraRig.Snap(cm, world);
                        if (vidHud != null) { vidModel.cam = cm; vidModel.driverView = cm == CamMode.Driver; vidModel.mirrors = cm != CamMode.Driver; vidHud.Tick(vidModel); Canvas.ForceUpdateCanvases(); }
                        if (cm == CamMode.Driver) scene.RenderAuxNow(world);
                        DcRender.Now(cam, rt);
                        VideoFrame(rt, small, vtex, Path.Combine(vdir, (cm == CamMode.Driver ? "d_" : "c_") + vn.ToString("00000") + ".jpg"));
                    }
                    csv.Append(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.0},{6:0.0},{7:0},{8},{9:0.0},{10},{11},{12},{13},{14},{15},{16}\n", vn, t, car.Center.x, car.Center.y, car.z, car.heading * Mathf.Rad2Deg, car.speed * 3.6f,
                        rr0 != null ? rr0.limitKmh : 0f, auto.Lane, auto.HasRoute ? auto.RouteS : 0f, rr0 != null ? rr0.laneDepartCount : 0, rr0 != null ? rr0.bumpCount : 0, rr0 != null ? rr0.redLightCount : 0, rr0 != null ? rr0.stopLineCount : 0, rr0 != null ? rr0.speedingCount : 0, rr0 != null ? rr0.noSignalCount : 0, (auto.Why ?? "").Replace(',', ' ')));
                    vn++;
                    if (vn % 300 == 0) { File.WriteAllText(Path.Combine(vdir, "frames.csv"), csv.ToString()); Debug.Log("DCVIDEO " + plan.name + " frame " + vn); }
                }
                if (vdir == null && t >= next)
                {
                    next += plan.every;
                    scene.Tick(world, dt, false);
                    scene.cameraRig.Snap(plan.cam, world);
                    DcRender.Now(cam, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, plan.name + "_" + n.ToString("000") + ".jpg"), tex.EncodeToJPG(72));
                    UnityEngine.Object.DestroyImmediate(tex);
                    log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:000} t={1:0.0} x={2:0.0} y={3:0.0} z={4:0.0} head={5:0} v={6:0.0}km/h lane={7} | {8}", n, t, car.Center.x, car.Center.y, car.z, car.heading * Mathf.Rad2Deg, car.speed * 3.6f, auto.Lane, auto.Why));
                    n++;
                }
            }
            var rr = world.roadRun;
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "끝: 거리 {0:0} m, 높이 {1:0.0}~{2:0.0} m, 지나온 차로 {3}, 다시 잡음 {4}, 막다른 곳 {5}", dist, minZ, maxZ, auto.lanesDone, auto.reacquired, auto.deadEnds));
            if (rr != null) log.AppendLine("기록: 차로 벗어남 " + rr.laneDepartCount + ", 부딪힘 " + rr.bumpCount + ", 신호 위반 " + rr.redLightCount + ", 정지선 " + rr.stopLineCount + ", 과속 " + rr.speedingCount);
            log.AppendLine("자동 운전: 신호에 섬 " + auto.signalStops + ", 물러남 " + auto.backedUp + (auto.HasRoute ? string.Format(CultureInfo.InvariantCulture, ", 경로 {0:0}/{1:0} m", auto.RouteS, auto.RouteLen) : "") + (rr != null ? ", 깜빡이 안 켬 " + rr.noSignalCount + ", 빨간불 우회전 일시정지 안 함 " + rr.rightOnRedCount + ", 보행자 " + rr.pedCount : ""));
            File.WriteAllText(Path.Combine(dir, plan.name + ".txt"), log.ToString());
            if (vdir != null)
            {
                File.WriteAllText(Path.Combine(vdir, "frames.csv"), csv.ToString());
                if (!vmore) File.WriteAllText(Path.Combine(vdir, "summary.txt"), log.ToString());
                small.Release(); UnityEngine.Object.DestroyImmediate(small); UnityEngine.Object.DestroyImmediate(vtex);
                vidHud = null; vidModel = null;
            }
            Debug.Log("DCDRIVE " + plan.name + " frames " + n + " dist " + dist.ToString("0"));
        }

        static void GridShots(Plan plan, World world, Camera cam, string outDir)
        {
            var rg = world.st.road?.region;
            if (rg == null || rg.boundary.Length < 3) return;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in rg.boundary) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            // 칸 = 300 m 정사각형. 열 = A,B,…(서→동), 줄 = 1,2,…(y 작은 쪽부터). 길이 없는 칸은 건너뛴다
            const float Cell = 300f; const int Px = 1000;
            // grid_s* = 반 칸(150 m)씩 옮긴 격자: 먼젓번 격자의 칸 경계에 걸려 있던 곳이 칸 한가운데에 온다
            if (plan.name.StartsWith("grid_s")) { x0 -= Cell / 2f; y0 -= Cell / 2f; }
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / Cell)), ny = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / Cell));
            var used = new bool[nx, ny];
            foreach (var s in world.st.road.strips)
                for (int i = 0; i + 1 < s.pts.Length; i++)
                {
                    V2 pa = s.pts[i], pb = s.pts[i + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(pa.DistanceTo(pb) / 40f));
                    for (int k = 0; k <= steps; k++)
                    {
                        var q = pa + (pb - pa) * (k / (float)steps);
                        int ix = Mathf.FloorToInt((q.x - x0) / Cell), iy = Mathf.FloorToInt((q.y - y0) / Cell);
                        if (ix >= 0 && iy >= 0 && ix < nx && iy < ny) used[ix, iy] = true;
                    }
                }
            var rt = new RenderTexture(Px, Px, 24);
            var keepT = cam.targetTexture; bool keepO = cam.orthographic; float keepFar = cam.farClipPlane, keepSize = cam.orthographicSize; bool keepFog = RenderSettings.fog;
            cam.targetTexture = rt; cam.orthographic = true; cam.farClipPlane = 1500f; RenderSettings.fog = false;
            // oblique = 남쪽 하늘에서 북쪽으로 45° 내려다봄(화면 위 = 북, 같은 칸)
            void Snap(float cx, float cy, float half, string name, bool oblique = false)
            {
                var c = Space3D.W(new V2(cx, cy), 0f);
                var rot = Quaternion.Euler(oblique ? 45f : 90f, 0f, 0f);
                cam.transform.position = oblique ? c - (rot * Vector3.forward) * 600f : c + Vector3.up * 500f;
                cam.transform.rotation = rot;       // 화면 위 = 북
                cam.orthographicSize = half;
                DcRender.Now(cam, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(Px, Px, TextureFormat.RGB24, false);
                tex.ReadPixels(new UnityEngine.Rect(0, 0, Px, Px), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(outDir, "grid", plan.name.Substring(5) + "_" + name + ".jpg"), tex.EncodeToJPG(82));
                UnityEngine.Object.DestroyImmediate(tex);
            }
            Directory.CreateDirectory(Path.Combine(outDir, "grid"));
            float bw = nx * Cell, bh = ny * Cell, big = Mathf.Max(bw, bh) / 2f;
            Snap(x0 + bw / 2f, y0 + bh / 2f, big, "all");
            int shot = 0;
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                    if (used[ix, iy])
                    {
                        string cn = ((char)('A' + ix)).ToString() + (iy + 1);
                        Snap(x0 + (ix + 0.5f) * Cell, y0 + (iy + 0.5f) * Cell, Cell / 2f, cn);
                        Snap(x0 + (ix + 0.5f) * Cell, y0 + (iy + 0.5f) * Cell, Cell / 2f, cn + "_o", true);
                        shot++;
                    }
            File.WriteAllText(Path.Combine(outDir, "grid", plan.name.Substring(5) + ".txt"),
                string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5} {6} {7}", x0, y0, nx, ny, Cell, big, shot, Px));
            Debug.Log("DCGRID " + plan.name + " " + nx + "x" + ny + " shots " + shot);
            cam.targetTexture = keepT; cam.orthographic = keepO; cam.farClipPlane = keepFar; cam.orthographicSize = keepSize; RenderSettings.fog = keepFog;
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }

        static void MemReport(string tag)
        {
            Debug.Log($"DCMEM {tag} total native {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20} MB, mono used {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20} MB, mono heap {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} MB, gfx {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20} MB");
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            var byType = new System.Collections.Generic.Dictionary<string, (long bytes, int n)>();
            var big = new System.Collections.Generic.List<(long bytes, string what)>();
            long verts = 0; int meshes = 0, gos = 0;
            foreach (var o in all)
            {
                if (o == null) continue;
                if (EditorUtility.IsPersistent(o) && !(o is Texture) && !(o is Mesh)) continue;
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(o);
                string t = o.GetType().Name;
                byType[t] = byType.TryGetValue(t, out var cur) ? (cur.bytes + b, cur.n + 1) : (b, 1);
                if (o is Mesh m) { verts += m.vertexCount; meshes++; }
                if (o is GameObject) gos++;
                if (b > (1 << 20)) big.Add((b, t + " " + o.name + (o is Mesh mm ? $" v={mm.vertexCount} readable={mm.isReadable}" : (o is Texture tx ? $" {tx.width}x{tx.height}" : ""))));
            }
            var types = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, (long bytes, int n)>>(byType);
            types.Sort((a, b) => b.Value.bytes.CompareTo(a.Value.bytes));
            for (int i = 0; i < Math.Min(14, types.Count); i++) Debug.Log($"DCMEM {tag} type {types[i].Key}: {types[i].Value.bytes >> 20} MB x{types[i].Value.n}");
            Debug.Log($"DCMEM {tag} meshes {meshes}, vertices {verts}, gameobjects {gos}");
            // 메시는 이름(숫자·좌표를 뗀 앞머리)별 합계도: 무엇이 여러 벌 복사되는지
            var byName = new System.Collections.Generic.Dictionary<string, (long bytes, int n, long v)>();
            foreach (var o in all)
                if (o is Mesh m2)
                {
                    string k = System.Text.RegularExpressions.Regex.Replace(m2.name ?? "", @"[-@_ ]?[-0-9,._]+$", "");
                    long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m2);
                    byName[k] = byName.TryGetValue(k, out var c2) ? (c2.bytes + b, c2.n + 1, c2.v + m2.vertexCount) : (b, 1, (long)m2.vertexCount);
                }
            var names = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, (long bytes, int n, long v)>>(byName);
            names.Sort((a, b) => b.Value.bytes.CompareTo(a.Value.bytes));
            for (int i = 0; i < Math.Min(22, names.Count); i++) Debug.Log($"DCMEM {tag} mesh '{names[i].Key}': {names[i].Value.bytes >> 20} MB x{names[i].Value.n} v={names[i].Value.v}");
            big.Sort((a, b) => b.bytes.CompareTo(a.bytes));
            for (int i = 0; i < Math.Min(12, big.Count); i++) Debug.Log($"DCMEM {tag} big {big[i].bytes >> 20} MB {big[i].what}");
        }

        static void MenuShots(string outDir)
        {
            var go = new GameObject("ShotCam");
            go.transform.position = new Vector3(0f, -500f, 0f);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            var rt = new RenderTexture(ShotW, ShotH, 24);
            cam.targetTexture = rt;
            var menu = new MenuScreen(null, cam);
            UiKit.FixScale(menu.canvas, ShotH / 1080f);
            Canvas.ForceUpdateCanvases();
            foreach (var (tab, name) in new[] { (0, "menu_steering"), (1, "menu_parking") })
            {
                menu.ShowTab(tab);
                Canvas.ForceUpdateCanvases();
                Save(cam, rt, Path.Combine(outDir, "shot_" + name + ".png"));
                Debug.Log("DCSETUP shot " + name);
            }
            var menuPaths = new System.Collections.Generic.List<(string[], string)> { (new string[0], "menu_road"), (new[] { "유럽" }, "menu_road_europe"), (new[] { "아시아 태평양" }, "menu_road_asia"), (new[] { "아시아 태평양", "한국", "서울" }, "menu_road_city"), (new[] { "아시아 태평양", "한국" }, "menu_road_korea"), (new[] { "아시아 태평양", "한국", "서울", "서초구" }, "menu_road_dong"), (new[] { "아시아 태평양", "한국", "서울", "강남구" }, "menu_road_dong2"), (new[] { "아시아 태평양", "한국", "서울", "서초구", "양재2동" }, "menu_road_start") };
            // 독일 지역이 있으면 유럽 › 독일 › 도시 › 구 › 지역(Beta) 화면도
            int dei = DeIndex();
            if (dei >= 0)
            {
                var de = RegionCatalog.Entries[dei];
                menuPaths.Add((new[] { "유럽", de.country }, "menu_road_de"));
                menuPaths.Add((new[] { "유럽", de.country, de.city }, "menu_road_de_city"));
                menuPaths.Add((new[] { "유럽", de.country, de.city, de.district }, "menu_road_de_dong"));
                menuPaths.Add((new[] { "유럽", de.country, de.city, de.district, de.name + (de.beta ? " Beta" : "") }, "menu_road_de_start"));
            }
            foreach (var (path, name) in menuPaths)
            {
                menu.RoadGo(path);
                Canvas.ForceUpdateCanvases();
                Save(cam, rt, Path.Combine(outDir, "shot_" + name + ".png"));
                Debug.Log("DCSETUP shot " + name);
            }
            menu.SetMode(false);
            menu.OpenSettings();
            Canvas.ForceUpdateCanvases();
            Save(cam, rt, Path.Combine(outDir, "shot_menu_settings.png"));
            Debug.Log("DCSETUP shot menu_settings");
            Dc.Kill(menu.canvas.gameObject);
            cam.targetTexture = null;
            rt.Release();
            Dc.Kill(go);
        }

        static void Save(Camera cam, RenderTexture rt, string path)
        {
            DcRender.Now(cam, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = prev;
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
