// 운전자 시점을 앞뒤·위아래·좌우로 옮기기 — 각자 맞추고 싶은 시야가 있으니까
// 설정 화면과 게임 중(운전석 시점의 '시야 조절' 버튼) 둘 다 이 조절판을 쓴다. 누를 때마다 2 cm, 값은 Prefs 에 바로 저장되고
// 운전석 카메라는 매 프레임 Prefs.SeatOffset 을 읽으므로 게임 중이면 곧바로 보인다.
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    public class SeatPanel
    {
        static readonly string[] Names = { "앞뒤", "높이", "좌우" };
        static readonly string[] Minus = { "뒤로", "아래로", "왼쪽으로" };
        static readonly string[] Plus = { "앞으로", "위로", "오른쪽으로" };
        readonly Text[] values = new Text[3];
        public Action onChanged;

        /// <summary>parent 의 왼쪽 위 (x, y)부터 폭 w, 줄 높이 rowH 로 세 줄(이름 · [−] · 값 · [+])</summary>
        public SeatPanel(RectTransform parent, float x, float y, float w, float rowH, int font)
        {
            float nameW = w * 0.16f, bw = w * 0.3f, valW = w - nameW - 2f * bw - 20f;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                float ry = y - i * rowH;
                float bh = rowH - 14f;
                var l = UiKit.Label(parent, Names[i], font, DcAssets.UiText, true, TextAnchor.MiddleLeft);
                UiKit.Place(l.rectTransform, new Vector2(0, 1), new Vector2(x, ry), new Vector2(nameW, bh));
                var bm = UiKit.Btn(parent, Minus[i], font - 2, UiKit.Kind.Plain, () => Bump(k, -Prefs.SeatStep), out _);
                UiKit.Place((RectTransform)bm.transform, new Vector2(0, 1), new Vector2(x + nameW, ry), new Vector2(bw, bh));
                values[i] = UiKit.Label(parent, "", font, DcAssets.UiText, true, TextAnchor.MiddleCenter);
                UiKit.Place(values[i].rectTransform, new Vector2(0, 1), new Vector2(x + nameW + bw + 10f, ry), new Vector2(valW, bh));
                var bp = UiKit.Btn(parent, Plus[i], font - 2, UiKit.Kind.Plain, () => Bump(k, Prefs.SeatStep), out _);
                UiKit.Place((RectTransform)bp.transform, new Vector2(0, 1), new Vector2(x + w - bw, ry), new Vector2(bw, bh));
            }
            Refresh();
        }

        static int Value(int axis) => axis == 0 ? Prefs.SeatZ : (axis == 1 ? Prefs.SeatY : Prefs.SeatX);

        void Bump(int axis, int d)
        {
            if (axis == 0) Prefs.SeatZ += d;
            else if (axis == 1) Prefs.SeatY += d;
            else Prefs.SeatX += d;
            Refresh();
            onChanged?.Invoke();
        }

        public void Reset()
        {
            Prefs.ResetSeat();
            Refresh();
            onChanged?.Invoke();
        }

        public void Refresh()
        {
            for (int i = 0; i < 3; i++)
            {
                int v = Value(i);
                values[i].text = v == 0 ? "기본" : (v > 0 ? "+" : "−") + Mathf.Abs(v) + " cm";
            }
        }

        /// <summary>한 줄 요약(설정 화면 줄 설명용)</summary>
        public static string Summary()
        {
            int z = Prefs.SeatZ, y = Prefs.SeatY, x = Prefs.SeatX;
            if (z == 0 && y == 0 && x == 0) return "기본 자리";
            string Part(int v, string pos, string neg) => v == 0 ? null : (v > 0 ? pos : neg) + " " + Mathf.Abs(v) + "cm";
            return string.Join(", ", new[] { Part(z, "앞", "뒤"), Part(y, "위", "아래"), Part(x, "오른쪽", "왼쪽") }.Where(s => s != null));
        }
    }
}
