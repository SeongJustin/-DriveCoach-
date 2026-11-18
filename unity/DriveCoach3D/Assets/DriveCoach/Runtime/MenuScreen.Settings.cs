// MenuScreen: 설정
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    public partial class MenuScreen
    {
        // ───────────────────────── 설정 ─────────────────────────

        class SegRow { public Func<int> get; public Action<int> set; public Button[] btns; public Text[] texts; }
        readonly List<SegRow> rows = new List<SegRow>();

        public void OpenSettings()
        {
            settingsGo.SetActive(true);
            // 주행 중에는 뒤를 더 어둡게(뒤 버튼·글이 창 가장자리에 반쯤 잘려 보이지 않게)
            var dim = settingsGo.GetComponent<Image>(); if (dim != null) dim.color = new Color(0f, 0f, 0f, inGame ? 0.62f : 0.35f);
            ClearSettings();
            var t = UiKit.Label(settingsCard, "설정", 40, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(t.rectTransform, new Vector2(0, 1), new Vector2(48, -28), new Vector2(400, 64));
            if (false)
            {
                // 주행 중에 바꾸면 바로 바뀌는 것(화면 조작·속도·거울·센서음)과 판을 다시 지어야 바뀌는 것을 알린다
                // 하늘·비·눈은 바로 바뀌고, 판을 다시 지어야 하는 것만 '다시'
                var note = UiKit.Label(settingsCard, "날씨와 시간은 바로 바뀝니다.\n그래픽, 교통, 보행자는 '리셋'을 누르면 바뀝니다.", 20, DcAssets.UiText, false, TextAnchor.MiddleRight);
                UiKit.Place(note.rectTransform, new Vector2(1, 1), new Vector2(-48, -30), new Vector2(560, 60));
            }
            float y = -100f;
            Seg(ref y, "보조 거울", "옆 거울과 뒤 거울을 화면 가장자리에",
                new[] { "끔", "운전석에서", "항상" }, () => (int)Prefs.HudMirrors, v => Prefs.HudMirrors = (HudMirrorMode)v);
            Seg(ref y, "시점", "",
                new[] { "따라가기", "운전석", "위에서" },
                () => Prefs.StartCam == CamMode.Driver ? 1 : (Prefs.StartCam == CamMode.Top ? 2 : 0),
                v => Prefs.StartCam = v == 1 ? CamMode.Driver : (v == 2 ? CamMode.Top : CamMode.Chase));
            // 속도: 먼저 자동/수동, 그 아래에서 자동 속도 단계(수동 = 실제 가속 페달)
            if (Prefs.SpeedChoice != 3) autoLevel = Mathf.Clamp(Prefs.SpeedChoice, 0, 2);
            Seg(ref y, "속도", "", new[] { "자동", "수동" }, () => Prefs.SpeedChoice == 3 ? 1 : 0, v => Prefs.SpeedChoice = v == 1 ? 3 : autoLevel);
            y += 14f;
            Seg(ref y, "", "", new[] { "느림", "보통", "빠름" }, () => autoLevel, v => { autoLevel = v; if (Prefs.SpeedChoice != 3) Prefs.SpeedChoice = v; });
            Seg(ref y, "주차 센서음", "", new[] { "켬", "끔" }, () => Prefs.SensorSound ? 0 : 1, v => Prefs.SensorSound = v == 0);
            Seg(ref y, "그래픽", "", new[] { "낮음", "중간", "높음" }, () => Prefs.Quality, v => Prefs.Quality = v);
            // 운전석 시야: 앞뒤·위아래·좌우로 눈 위치 옮기기 → 따로 한 장
            ActionRow(ref y, "운전석 시야", SeatPanel.Summary(),
                new[] { ("맞추기", (Action)OpenSeatPage), ("초기화", (Action)(() => { Prefs.ResetSeat(); OpenSettings(); })) });
            // 날씨·시간(바깥 판: 내 동네·서킷·도로. 지하 주차장은 늘 실내등)
            ActionRow(ref y, "날씨, 시간, 교통", WeatherNames[Prefs.Weather] + ", " + TimeNames[Prefs.TimeOfDay] + ", 교통 " + new[] { "적게", "보통", "많게" }[Prefs.TrafficLevel] + ", 보행자 " + new[] { "없음", "적게", "보통", "많게" }[Prefs.PedLevel],
                new[] { ("바꾸기", (Action)OpenWeatherPage) });
            // 화면 조작(가상 운전대·액셀·브레이크)
            ActionRow(ref y, "화면 조작", "스티어링 휠 " + OnOff(Prefs.VWheel) + ", 가속 페달 " + OnOff(Prefs.VAccel) + ", 브레이크 " + (Prefs.VBrake ? "누르는 동안" : "켜고 끄기"),
                new[] { ("바꾸기", (Action)OpenControlPage) });
            BottomButton("닫기", CloseSettings);
        }

        static string OnOff(bool b) => b ? "켬" : "끔";

        /// <summary>설정 안 한 장 '화면을 눌러 운전하기': 가상 운전대·액셀·브레이크를 따로 켜고 끈다</summary>
        void OpenControlPage()
        {
            ClearSettings();
            var t = UiKit.Label(settingsCard, "화면 조작", 40, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(t.rectTransform, new Vector2(0, 1), new Vector2(48, -28), new Vector2(700, 64));
            var h = UiKit.Label(settingsCard,
                "",
                22, DcAssets.UiSub, false, TextAnchor.UpperLeft);
            UiKit.Place(h.rectTransform, new Vector2(0, 1), new Vector2(48, -104), new Vector2(944, 110));
            float y = -100f;   // 설명 줄이 비어 생긴 빈칸 없이 제목 바로 아래부터(첫 화면과 같은 자리)
            // 설정 이름에 무엇이 바뀌는지 그대로 쓴다
            Seg(ref y, "화면 스티어링 휠", "", new[] { "끔", "켬" }, () => Prefs.VWheel ? 1 : 0, v => Prefs.VWheel = v == 1);
            Seg(ref y, "화면 가속 페달", "누르는 동안 가속", new[] { "끔", "켬" }, () => Prefs.VAccel ? 1 : 0, v => Prefs.VAccel = v == 1);
            Seg(ref y, "브레이크 버튼", "한 번 누르면 밟은 채로 / 누르는 동안만 밟음", new[] { "켜고 끄기", "누르는 동안" }, () => Prefs.VBrake ? 1 : 0, v => Prefs.VBrake = v == 1);
            BottomButton("설정으로", OpenSettings);
        }

        void BottomButton(string label, Action onClick)
        {
            var close = UiKit.Btn(settingsCard, label, 30, UiKit.Kind.Primary, () => onClick(), out _);
            var rt = (RectTransform)close.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 32f);
            rt.sizeDelta = new Vector2(240f, 88f);
        }

        void ClearSettings()
        {
            for (int i = settingsCard.childCount - 1; i >= 0; i--)
                if (settingsCard.GetChild(i).name != "fill") Dc.Kill(settingsCard.GetChild(i).gameObject);
            rows.Clear();
        }

        static readonly string[] WeatherNames = { "맑음", "흐림", "비", "안개", "눈" };
        static readonly string[] TimeNames = { "아침", "낮", "해질녘", "밤" };

        /// <summary>설정 안 한 장: 날씨·시간</summary>
        void OpenWeatherPage()
        {
            ClearSettings();
            var t = UiKit.Label(settingsCard, "날씨, 시간, 교통", 40, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(t.rectTransform, new Vector2(0, 1), new Vector2(48, -28), new Vector2(600, 64));
            var h = UiKit.Label(settingsCard,
                "",
                22, DcAssets.UiSub, false, TextAnchor.UpperLeft);
            UiKit.Place(h.rectTransform, new Vector2(0, 1), new Vector2(48, -104), new Vector2(944, 110));
            float y = -100f;   // 제목 바로 아래부터
            Seg(ref y, "날씨", "", WeatherNames, () => Prefs.Weather, v => Prefs.Weather = v);
            Seg(ref y, "시간", "", TimeNames, () => Prefs.TimeOfDay, v => Prefs.TimeOfDay = v);
            Seg(ref y, "교통량", "다른 차", new[] { "적게", "보통", "많게" }, () => Prefs.TrafficLevel, v => Prefs.TrafficLevel = v);
            Seg(ref y, "보행자", "횡단보도", new[] { "없음", "적게", "보통", "많게" }, () => Prefs.PedLevel, v => Prefs.PedLevel = v);
            BottomButton("설정으로", OpenSettings);
        }

        /// <summary>설정 안 한 장: 운전석 시야(눈 위치) 앞뒤·높이·좌우</summary>
        void OpenSeatPage()
        {
            ClearSettings();
            var t = UiKit.Label(settingsCard, "운전석 시야", 40, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(t.rectTransform, new Vector2(0, 1), new Vector2(48, -28), new Vector2(600, 64));
            var h = UiKit.Label(settingsCard,
                "",
                22, DcAssets.UiSub, false, TextAnchor.UpperLeft);
            UiKit.Place(h.rectTransform, new Vector2(0, 1), new Vector2(48, -104), new Vector2(944, 130));
            var panel = new SeatPanel(settingsCard, 48f, -268f, 944f, 124f, 30);
            var reset = UiKit.Btn(settingsCard, "초기화", 28, UiKit.Kind.Plain, () => panel.Reset(), out _);
            UiKit.Place((RectTransform)reset.transform, new Vector2(1, 1), new Vector2(-48, -650), new Vector2(220, 76));
            BottomButton("설정으로", OpenSettings);
        }

        /// <summary>설정 한 줄: 이름·설명 + 오른쪽 버튼들(누르면 바로 동작, 고르는 줄이 아님)</summary>
        void ActionRow(ref float y, string label, string help, (string name, Action act)[] buttons)
        {
            var l = UiKit.Label(settingsCard, label, 30, DcAssets.UiText, true, TextAnchor.MiddleLeft);   // 버튼 가운데 높이에
            UiKit.Place(l.rectTransform, new Vector2(0, 1), new Vector2(48, y + 4f), new Vector2(360, 68));
            const float bw = 170f, gap = 10f;
            float buttonsW = buttons.Length * bw + (buttons.Length - 1) * gap;
            help = "";
            var hl = UiKit.Label(settingsCard, help, 20, DcAssets.UiSub, false, TextAnchor.UpperLeft);
            UiKit.Place(hl.rectTransform, new Vector2(0, 1), new Vector2(48, y - 46), new Vector2(Mathf.Min(560f, 1040f - 96f - buttonsW - 24f), 54));
            float x = -48f;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                var act = buttons[i].act;
                // 고르는 칸(Seg)과 헷갈리지 않게 모두 흰 버튼
                var b = UiKit.Btn(settingsCard, buttons[i].name, 26, UiKit.Kind.Plain, () => act(), out _);
                UiKit.Place((RectTransform)b.transform, new Vector2(1, 1), new Vector2(x, y + 4f), new Vector2(bw, 68));
                x -= bw + gap;
            }
            y -= RowStep;
        }

        static int autoLevel = 1;
        const float RowStep = 88f;   // 날씨·시간 줄을 더하며 108 → 96

        void Seg(ref float y, string label, string help, string[] options, Func<int> get, Action<int> set)
        {
            // 이름을 버튼(높이 68)의 가운데 높이에 맞춘다
            var l = UiKit.Label(settingsCard, label, 30, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(l.rectTransform, new Vector2(0, 1), new Vector2(48, y + 4f), new Vector2(300, 68));
            float bw = options.Length > 4 ? 136f : (options.Length > 3 ? 150f : 170f);
            const float gap = 10f;
            float buttonsW = options.Length * bw + (options.Length - 1) * gap;
            help = "";
            var h = UiKit.Label(settingsCard, help, 20, DcAssets.UiSub, false, TextAnchor.UpperLeft);
            UiKit.Place(h.rectTransform, new Vector2(0, 1), new Vector2(48, y - 46), new Vector2(Mathf.Min(420f, 1040f - 96f - buttonsW - 24f), 54));
            var row = new SegRow { get = get, set = set, btns = new Button[options.Length], texts = new Text[options.Length] };
            float x = -48f;
            for (int i = options.Length - 1; i >= 0; i--)
            {
                int k = i;
                row.btns[i] = UiKit.Btn(settingsCard, options[i], 26, UiKit.Kind.Plain, () => { set(k); RefreshRows(); onSettingsChanged?.Invoke(); }, out row.texts[i]);
                UiKit.Place((RectTransform)row.btns[i].transform, new Vector2(1, 1), new Vector2(x, y + 4f), new Vector2(bw, 68));
                x -= bw + gap;
            }
            rows.Add(row);
            RefreshRow(row);
            y -= RowStep;
        }

        void RefreshRows() { foreach (var r in rows) RefreshRow(r); }

        static void RefreshRow(SegRow r)
        {
            int v = r.get();
            for (int i = 0; i < r.btns.Length; i++) UiKit.SetKind(r.btns[i], r.texts[i], i == v ? UiKit.Kind.Primary : UiKit.Kind.Plain);
        }
    }
}
