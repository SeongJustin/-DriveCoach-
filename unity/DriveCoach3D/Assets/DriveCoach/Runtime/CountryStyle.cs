// 나라마다 다른 "모양" 을 한 곳에 모은 표.
// 지역 파일·목록의 country 값으로 고른다(지역 파일 이름으로 고르지 않는다). 모르는 나라·빈 값은 한국 값 = 예전 그대로.
//   칠(지하차도·고가 위·노면 파일 없는 길): 중앙선 색·겹선 여부·자리, 차로 점선 길이/간격
//   점형블록 색, 과속방지턱 도색, 안전표지 그림 모음(번호 앞머리), 주유소 가격판 그림, 신호(빨강+노랑 뒤 초록, 보행 초록 깜빡임),
//   버스·택시 색, 버스정류장 표지 색, 초록 옥상 비율.
// 노면 파일(<지역>_surface.bytes)의 실제 칠은 데이터가 색을 정한다(베를린 칠은 모두 흰색 PNTW). 여기 값은 Unity 가 직접 긋는 선에만 쓴다.
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public sealed class CountryStyle
    {
        public string code = "KR";

        // ── 칠 ──
        /// <summary>양방향 길 중앙선 색(지하차도·고가 위·노면 파일 없는 길)</summary>
        public Color centre = DcAssets.Hex("#E2B33C");
        /// <summary>중앙선 겹선(한국 황색 복선) / 한 줄(독일 흰 실선, Z 295)</summary>
        public bool centreDouble = true;
        /// <summary>
        /// 중앙선 자리: true = 지역 자료의 차로 자리로 정한 방향 경계(오른쪽 차로 중 가장 안쪽과 왼쪽 차로 중 가장 안쪽의 가운데, 자료 없으면 0) / false = 길 가운데선(0).
        /// 베를린 브란덴부르크 거리: 차로 +2.9 | 0.0 -2.8 → 중앙선 +1.45(원자료 +1.3), 길 가운데 0 은 차로 한가운데.
        /// 한국도 켬: 앱에 든 한국 동네의 양방향 지하·고가 길은 모두 차로 자료가 없거나 대칭이라 이 값이 0, 화면 그대로.
        /// </summary>
        public bool centreFromLanes = true;
        /// <summary>동네길(큰길 아님) 중앙선을 점선으로(독일: 흰 Leitlinie). 0 = 실선</summary>
        public float minorCentreDash;
        /// <summary>차로 경계 점선: 칠 길이·빈 길이(m). 한국 3/5(지금 코드 값), 독일 시내 Leitlinie 3/6</summary>
        public float laneDash = 3f, laneGap = 5f;

        // ── 보도·노면 시설 ──
        /// <summary>점형블록 색: 한국 노랑, 독일 흰·밝은 회색(Bodenindikatoren)</summary>
        public Color tactile = DcAssets.Hex("#E9B92C");
        /// <summary>과속방지턱 윗면: 바탕·빗금(한국: 노랑 + 흰 빗금)</summary>
        public Color32 humpBase = new Color32(226, 179, 60, 255), humpStripe = new Color32(236, 236, 230, 255);

        // ── 표지·판 ──
        /// <summary>안전표지 그림 모음(Resources 경로, signs.json 같은 이름)</summary>
        public string signAtlas = "DC/Signs/signs";
        /// <summary>그 나라 표지 번호 앞머리(지역 파일 signs 의 "de274-30" 처럼). 빈 값이면 앞머리 없는 번호(한국)만 쓴다</summary>
        public string signPrefix = "";
        /// <summary>주유소·충전소 가격판 그림(Resources 경로). null 이면 가격판을 세우지 않는다</summary>
        public string stationBoard = "DC/Signs/station";

        // ── 신호 ──
        /// <summary>초록 직전 빨강+노랑을 함께 켜는 초(독일 StVO §37 Rot-Gelb). 0 = 없음(한국). 화면 표시만: 차는 빨강으로 다룬다</summary>
        public float redAmberSec;
        /// <summary>보행 초록 끝 무렵 깜빡임(한국 보행 녹색 점멸). 독일은 깜빡이지 않는다</summary>
        public bool pedGreenBlink = true;

        // ── 차·정류장·건물 ──
        /// <summary>시내버스·마을버스·택시 색(null = TrafficView 의 한국 색)</summary>
        public (Color c, float w)[] busPaint, townBusPaint, taxiPaint;
        /// <summary>버스정류장 표지 판·그림 색(한국: 남색 판 + 흰 칸)</summary>
        public Color busStopPlate = DcAssets.Hex("#1F3F7A"), busStopMark = DcAssets.Hex("#F2F4F7");
        /// <summary>내 차 모델(Resources 경로). 한국 DC/Car/sedan(번호판 "12가 3456"), 독일 DC/Car/sedan_de(같은 모양, 번호판 "B DC 2026")</summary>
        public string carModel = "DC/Car/sedan";
        /// <summary>상가 1층 간판 사진 질감 이름 앞머리(DC/CC0/&lt;앞머리&gt;n_albedo). 한국 "shop_"(한글 업종 낱말), 독일 "shopde_"(독일어 업종 낱말)</summary>
        public string shopTex = "shop_";
        /// <summary>낮은 건물 옥상 초록 방수 페인트 비율(한국 동네 0.6). double: 예전 식 `rnd.NextDouble() &lt; 0.6` 과 비교 값이 비트까지 같게(0.6f 는 0.6000000238)</summary>
        public double greenRoof = 0.6;

        public static readonly CountryStyle KR = new CountryStyle();

        public static readonly CountryStyle DE = new CountryStyle
        {
            code = "DE",
            centre = DcAssets.Paint,
            centreDouble = false,
            minorCentreDash = 3f,
            laneDash = 3f, laneGap = 6f,
            tactile = DcAssets.Hex("#D8D8D2"),
            humpBase = new Color32(78, 81, 86, 255), humpStripe = new Color32(236, 236, 230, 255),   // 아스팔트 + 흰 빗금(노랑 도색 없음)
            signAtlas = "DC/Signs/signs_de",
            signPrefix = "de",
            stationBoard = "DC/Signs/station_de",
            redAmberSec = 1f,
            pedGreenBlink = false,
            // 베를린 시내버스(BVG)는 노랑, 택시는 밝은 상아색(RAL 1015)이 대부분
            busPaint = new[] { (DcAssets.Hex("#F2C500"), 0.95f), (DcAssets.Hex("#F3F3F0"), 0.05f) },
            townBusPaint = new[] { (DcAssets.Hex("#F2C500"), 1f) },
            taxiPaint = new[] { (DcAssets.Hex("#E9E1C6"), 0.80f), (DcAssets.Hex("#1B1D21"), 0.10f), (DcAssets.Hex("#C8CBD0"), 0.10f) },
            busStopPlate = DcAssets.Hex("#F2C500"), busStopMark = DcAssets.Hex("#1E7B3A"),        // 정류장 표지(Z 224): 노란 판 + 초록 원
            greenRoof = 0f,
            shopTex = "shopde_",
            carModel = "DC/Car/sedan_de",
        };

        /// <summary>지금 짓고 있는(또는 달리는) 단계의 나라 모양. 지역이 아닌 단계(주차·서킷)는 한국</summary>
        public static CountryStyle Current = KR;

        /// <summary>country 값(지역 파일 "DE", 목록 "독일" 등) → 모양. 모르는 값은 한국(예전 그대로)</summary>
        public static CountryStyle For(string country)
        {
            switch ((country ?? "").Trim().ToLowerInvariant())
            {
                case "de": case "deu": case "독일": case "germany": case "deutschland": return DE;
                default: return KR;
            }
        }

        public static CountryStyle For(RegionData d) => d == null ? KR : For(d.country);

        public bool IsKorea => code == "KR";

        /// <summary>양방향 길의 중앙선 옆 자리(m, 길 가운데선 기준 +쪽). centreFromLanes 가 아니거나 차로 자료가 없으면 0</summary>
        public float CentreOffset(RegionRoad rd)
        {
            if (!centreFromLanes || rd == null || rd.laneR == null || rd.laneL == null || rd.laneR.Length == 0 || rd.laneL.Length == 0) return 0f;
            float r = float.MaxValue, l = float.MinValue;
            foreach (var v in rd.laneR) r = Mathf.Min(r, v);
            foreach (var v in rd.laneL) l = Mathf.Max(l, v);
            if (r <= l) return 0f;                 // 차로 순서가 엇갈린 자료: 예전처럼 가운데
            return (r + l) / 2f;
        }
    }
}
