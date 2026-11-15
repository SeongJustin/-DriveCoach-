// GameMain 의 앱에서 오는 신호 받기 부분(11/15 코드 정리로 GameMain.cs 에서 나눈 파일). 전체 설명은 GameMain.cs 맨 위.
using System;
using System.Globalization;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    public partial class GameMain
    {
        // ───────────────────────── 앱에서 오는 것 ─────────────────────────

        /// <summary>"steer;gear;brake;speed;rpm" — 핸들 각(도, + 오른쪽), P/R/N/D, 1/0, km/h, 회전수(모르면 -1)</summary>
        public void OnVehicleState(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return;
            var p = csv.Split(';');
            if (p.Length < 4) return;
            var inv = CultureInfo.InvariantCulture;
            if (float.TryParse(p[0], NumberStyles.Float, inv, out var s)) realSteer = s;
            realGear = p[1].Trim().ToUpperInvariant();
            realBrake = p[2].Trim() == "1" || p[2].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (float.TryParse(p[3], NumberStyles.Float, inv, out var v)) realSpeed = v;
            realRpm = p.Length > 4 && float.TryParse(p[4], NumberStyles.Float, inv, out var r) ? r : -1f;
            realPedal = p.Length > 5 && float.TryParse(p[5], NumberStyles.Float, inv, out var a) && a >= 0f ? Mathf.Clamp01(a / 100f) : -1f;
            if (realPedal >= 0f) pedalSeen = true;
            // 실차 스위치(7번째부터): "방향(L/R/O/U);비상(1/0/-1);전조등;와이퍼;벨트(1/0/-1)"
            if (p.Length > 10)
            {
                string t = p[6].Trim();
                realTurnKnown = t == "L" || t == "R" || t == "O";
                realTurn = t == "L" ? -1 : (t == "R" ? 1 : 0);
                realHazard = p[7].Trim() == "1";
                switch (p[8].Trim()) { case "OFF": realLight = 0; break; case "LOW": realLight = 1; break; case "HIGH": realLight = 2; break; case "AUTO": realLight = 3; break; default: realLight = -1; break; }
                switch (p[9].Trim()) { case "OFF": realWiper = 0; break; case "INT": realWiper = 1; break; case "LOW": realWiper = 2; break; case "HIGH": realWiper = 3; break; default: realWiper = -1; break; }
                realBelt = p[10].Trim() == "1" ? 1 : (p[10].Trim() == "0" ? 0 : -1);
            }
            connected = true;
        }

        /// <summary>차 제원(JSON). 다음 판부터 쓴다(지금 판의 차 크기를 바꾸지 않는다).</summary>
        public void OnCarSpec(string json)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("lengthM")) return;
            try { spec = CarSpec.FromJson(json); }
            catch (Exception e) { Debug.LogWarning("OnCarSpec: " + e.Message); }
        }

        /// <summary>앱 첫 화면에서 고른 메뉴(앱이 보냄): "road" = 도로 주행(나라·도시·구·동 고르기), 그 밖 = 주차·핸들 연습</summary>
        public void OnMenuMode(string v)
        {
            if (menu == null) return;
            bool road = v != null && v.Trim() == "road";
            menu.SetMode(road);
            if (world == null) ShowMenu();
        }

        /// <summary>오디오 포커스(앱이 보냄): "0" 이면 다른 소리(통화·음성 비서·음악 앱)에 자리를 내줘 효과음을 멈춘다, "1" 이면 다시 낸다.
        /// Pleos 오디오 포커스 가이드: 포커스 변화는 반드시 처리.</summary>
        public void OnAudioFocus(string v)
        {
            AudioListener.pause = v != null && v.Trim() == "0";
        }
    }
}
