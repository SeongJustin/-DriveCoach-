// (3D 주차 연습: 게임 전체 흐름. 규칙:
// 앱(Kotlin Game3DActivity)이 실제 차의 핸들 각·기어·브레이크·속도·회전수를 보낸다. 실제 차는 P 에 둔 채로 하고,
// 게임 속 차의 기어는 화면 버튼으로 바꾼다. 실제 차가 P 가 아니거나 움직이면 멈춘다.
// PC·편집기 시험: ←→(A D) 핸들, 스페이스 브레이크, ↑(W) 가속, 1~4 기어(P R N D), V 시점, Q/E 고개, Esc 목록, Enter 다시.
// 명령줄: -dcScreen stage:7 | tut:2 | circuit:1 | road:1   -dcCam chase|driver|top)
using System;
using System.Globalization;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    /// <summary>
    /// 장면의 "DriveCoachBridge" 오브젝트에 붙는다. 앱이 UnitySendMessage("DriveCoachBridge", "OnVehicleState", "steer;gear;brake;speed;rpm")
    /// 와 UnitySendMessage("DriveCoachBridge", "OnCarSpec", json) 으로 부른다.
    /// </summary>
    public partial class GameMain : MonoBehaviour
    {
        CarSpec spec = CarSpec.Default();
        bool connected;
        float realSteer, realSpeed, realRpm = -1f;
        // 화면 스티어링 휠이 켜져 있어도 실제 스티어링 휠을 5° 넘게 돌리면 그때부터 실제 각도를 따른다
        float realSteerBase = float.NaN;
        bool realSteerSeen;
        float realPedal = -1f;          // 실제 액셀 페달 0..1(차가 줄 때만, 모르면 -1)
        bool pedalSeen;                 // 이번 실행에서 페달 값을 한 번이라도 받았나
        int speedChoice = 1;
        string realGear = "P";
        bool realBrake;
        // 실차 스위치(앱이 보내면): 방향지시등 -1/0/1, 비상등, 전조등·와이퍼·벨트(Vehicle 의 값 그대로)
        int realTurn, realLight = -1, realWiper = -1, realBelt = -1;
        bool realHazard, realTurnKnown;
        // 키보드·화면 방향지시등(차가 값을 안 줄 때): Z 왼쪽, C 오른쪽, X 비상등. 돌고 나면 저절로 꺼진다(실제 차처럼)
        int keyTurn;
        bool keyHazard, keySignalUsed;
        float keyTurnHeading;
        float keySteer, steer;
        bool crossing;            // 옆 동으로 이어 달리는 중(핸들 각을 그대로 잇는다)
        bool screenBrake, brakeOn, paused;
        bool settingsPaused;      // 주행 중 설정 창을 연 동안 멈춤

        World world;
        SceneRig scene;
        string curKind = "";
        int curId;
        bool savedResult;
        CamMode camMode;
        HudMirrorMode hudMirrorMode;
        bool sensorSound;

        Hud hud;
        MenuScreen menu;
        Beeper beeper;
        Transform worldRoot;
        Camera uiCam;
        float lookYaw, lookPitch;
        bool lookHeld;
        float preloadT;
        static readonly float[] PreloadAhead = { 0f, 80f, 200f };
        V2? crossTestPos;               // 자가 검사: 미리 읽기를 건 뒤 5초 있다가 옮길 자리
        float crossTestAt = -1f;        // 자가 검사: 몇 초 뒤 옆 동 안쪽 길 위로 차를 옮겨 동 사이 이동을 확인(-dcCrossTest)
        float autoExitAt = -1f;         // 자가 검사: 몇 초 뒤 스스로 닫기(-dcAutoExit)
        int fpsFrames;
        float fpsTime;
        readonly HudModel model = new HudModel();

        void Awake()
        {
            Application.targetFrameRate = 30;
            QualitySettings.vSyncCount = 0;      // 목표 장수를 그대로 따르게
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = true;
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            // 메뉴 뒤 바탕(3D 장면이 없을 때 화면을 지운다)
            var uc = new GameObject("UiCamera");
            uc.transform.SetParent(transform, false);
            uiCam = uc.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.backgroundColor = DcAssets.Bg;
            uiCam.cullingMask = 0;
            uiCam.depth = -100f;
            worldRoot = new GameObject("World").transform;
            beeper = new Beeper(gameObject);

            hud = new Hud(transform, null);
            hud.onButton = OnButton;
            hud.onLook = d =>
            {
                // 끈 만큼 고개를 돌린다(오른쪽으로 끌면 오른쪽을 본다). 화면 크기와 관계없게 높이로 나눈다.
                float k = 1080f / Mathf.Max(1, Screen.height);
                lookHeld = true;
                lookYaw = Mathf.Clamp(lookYaw + d.x * 0.2f * k, -125f, 125f);
                lookPitch = Mathf.Clamp(lookPitch - d.y * 0.14f * k, -30f, 25f);
            };
            hud.onLookEnd = () => lookHeld = false;
            menu = new MenuScreen(transform, null);
            menu.onPick = OnButton;
            menu.onSettingsChanged = LoadPrefs;
            menu.onSettingsClosed = () => { settingsPaused = false; LoadPrefs(); };
            LoadPrefs();
            camMode = Prefs.StartCam;
            ShowMenu();
            ParseArgs();
        }

        void Start() => AndroidLink.Call("onUnityReady");

        void LoadPrefs()
        {
            hudMirrorMode = Prefs.HudMirrors;
            sensorSound = Prefs.SensorSound;
            speedChoice = Prefs.SpeedChoice;
            // 시점 버튼을 빼고 설정에서 바꾼다. 달리는 중에 바꾸면 바로 바뀐다
            if (world != null && scene != null && camMode != Prefs.StartCam) { camMode = Prefs.StartCam; lookYaw = lookPitch = 0f; scene.cameraRig.Snap(camMode, world); }
            // 달리는 중에 날씨, 시간을 바꾸면 화면만 바뀌고 판정(젖은 길 간격, 전조등)은 시작할 때 값 그대로였다
            if (world != null)
            {
                int wx = Prefs.Weather, tm = Prefs.TimeOfDay;
                world.wetRoad = wx == 2 || wx == 4;
                world.needHeadlights = tm == 3 || wx == 2 || wx == 3 || wx == 4 || (tm == 2 && wx != 0);
            }
        }

        void ParseArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-dcAutoExit" && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                    autoExitAt = Time.realtimeSinceStartup + sec;
                if (args[i] == "-dcCrossTest" && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cs))
                    crossTestAt = Time.realtimeSinceStartup + cs;
                if (args[i] == "-dcCam")
                {
                    var v = args[i + 1].ToLowerInvariant();
                    camMode = v == "driver" ? CamMode.Driver : (v == "top" ? CamMode.Top : CamMode.Chase);
                }
            }
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-dcScreen") OnButton(args[i + 1]);
        }
    }
}
