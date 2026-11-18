// 3D 주차 연습 설정: 화면 거울, 처음 시점, 센서음, 도로 주행 미리보기 보이기, 그래픽 품질, 운전석 시야. 기기 안 PlayerPrefs.
using UnityEngine;

namespace DriveCoach.Unity
{
    /// <summary>화면에 거울 띄우기(유로트럭의 HUD 거울처럼): 고개를 돌리지 않아도 양쪽 사이드미러·실내 백미러가 화면 가장자리에 보인다.</summary>
    public enum HudMirrorMode { Off = 0, DriverOnly = 1, Always = 2 }

    public static class Prefs
    {
        static int Get(string k, int def) { try { return PlayerPrefs.GetInt("dc3d_" + k, def); } catch { return def; } }
        static void Set(string k, int v) { try { PlayerPrefs.SetInt("dc3d_" + k, v); PlayerPrefs.Save(); } catch { } }

        /// <summary>화면 거울: 기본은 "운전석 시점에서" (운전석에서는 오른쪽 사이드미러가 고개를 돌려야만 보이므로)</summary>
        public static HudMirrorMode HudMirrors
        {
            get => (HudMirrorMode)Mathf.Clamp(Get("hudMirrors2", 0), 0, 2);   // 기본은 끔
            set => Set("hudMirrors2", (int)value);
        }

        /// <summary>처음 시점(마지막에 고른 시점을 기억한다)</summary>
        public static CamMode StartCam
        {
            get => (CamMode)Mathf.Clamp(Get("startCam2", (int)CamMode.Driver), 0, 3);   // 기본은 운전석(1인칭)
            set => Set("startCam2", (int)value);
        }

        /// <summary>화면 조작: 가상 운전대·가상 액셀·가상 브레이크(각각 켜고 끔)</summary>
        // 새 키 vWheel2: 기본 켬. 실제 핸들이 없는 에뮬레이터·화면만으로도 끝까지 해 볼 수 있게
        public static bool VWheel { get => Get("vWheel2", 1) == 1; set => Set("vWheel2", value ? 1 : 0); }
        public static bool VAccel { get => Get("vAccel2", 1) == 1; set => Set("vAccel2", value ? 1 : 0); }
        public static bool VBrake { get => Get("vBrake", 0) == 1; set => Set("vBrake", value ? 1 : 0); }

        public static bool SensorSound
        {
            get => Get("sensorSound", 1) == 1;
            set => Set("sensorSound", value ? 1 : 0);
        }

        /// <summary>그래픽: 0 = 가볍게(그림자·후처리 끔, 거울 해상도 낮춤), 1 = 보통(후처리·그림자), 2 = 높게(+ 그늘·블룸·거울 그림자)</summary>
        public static int Quality
        {
            get => QualityOverride >= 0 ? QualityOverride : Mathf.Clamp(Get("quality2", 1), 0, 2);
            set => Set("quality2", value);
        }

        /// <summary>화면 사진(DcBuild.Shots)용: 설정과 상관없이 이 품질로(-1 = 설정대로)</summary>
        public static int QualityOverride = -1;

        /// <summary>
        /// 서킷·도로 속도: 0 느긋하게 · 1 보통 · 2 빠르게(자동 속도, 핸들만 돌리기) · 3 실제 액셀(차가 페달 값을 줄 때만, 없으면 자동 보통).
        /// 화면 '가속' 버튼 대신.
        /// </summary>
        public static int SpeedChoice
        {
            get => Mathf.Clamp(Get("speedChoice", 1), 0, 3);
            set => Set("speedChoice", value);
        }

        /// <summary>날씨(설정에서 날씨·시간): 0 맑음 · 1 흐림 · 2 비 · 3 안개 · 4 눈</summary>
        public static int Weather
        {
            get => Mathf.Clamp(Get("weather", 0), 0, 4);
            set => Set("weather", value);
        }

        /// <summary>시간: 0 아침 · 1 낮 · 2 해질녘 · 3 밤</summary>
        public static int TimeOfDay
        {
            get => Mathf.Clamp(Get("timeOfDay", 1), 0, 3);
            set => Set("timeOfDay", value);
        }

        /// <summary>교통량: 0 적게(0.5배) · 1 보통 · 2 많게(1.6배): 시간·날씨 배율에 곱한다</summary>
        public static int TrafficLevel
        {
            get => Mathf.Clamp(Get("trafficLevel", 1), 0, 2);
            set => Set("trafficLevel", value);
        }

        /// <summary>보행자: 0 없음 · 1 적게(0.5배) · 2 보통 · 3 많게(1.8배)</summary>
        public static int PedLevel
        {
            get => Mathf.Clamp(Get("pedLevel", 2), 0, 3);
            set => Set("pedLevel", value);
        }

        // ── 운전석 시야(눈 위치) 옮김, cm. 각자 맞추고 싶은 시야가 있으니 앞뒤·위아래·좌우로 ──
        public const int SeatMaxZ = 20, SeatMaxY = 15, SeatMaxX = 12, SeatStep = 2;
        static Vector3? seatCache;

        /// <summary>앞뒤(+앞으로) cm</summary>
        public static int SeatZ { get => Mathf.Clamp(Get("seatZ", 0), -SeatMaxZ, SeatMaxZ); set { Set("seatZ", Mathf.Clamp(value, -SeatMaxZ, SeatMaxZ)); seatCache = null; } }
        /// <summary>높이(+위로) cm</summary>
        public static int SeatY { get => Mathf.Clamp(Get("seatY", 0), -SeatMaxY, SeatMaxY); set { Set("seatY", Mathf.Clamp(value, -SeatMaxY, SeatMaxY)); seatCache = null; } }
        /// <summary>좌우(+오른쪽) cm</summary>
        public static int SeatX { get => Mathf.Clamp(Get("seatX", 0), -SeatMaxX, SeatMaxX); set { Set("seatX", Mathf.Clamp(value, -SeatMaxX, SeatMaxX)); seatCache = null; } }

        /// <summary>차 기준(+X 오른쪽, +Y 위, +Z 앞) 눈 위치 옮김(m). 매 프레임 읽어도 가볍게(바뀔 때만 다시 읽음)</summary>
        public static Vector3 SeatOffset
        {
            get
            {
                if (seatCache == null) seatCache = new Vector3(SeatX, SeatY, SeatZ) * 0.01f;
                return seatCache.Value;
            }
        }

        public static void ResetSeat() { SeatX = 0; SeatY = 0; SeatZ = 0; }

        public static int MenuTab
        {
            get => Get("menuTab", 1);
            set => Set("menuTab", value);
        }
    }
}
