// 3D 주차 연습 — 가로·세로 화면 모두 꽉 채우기. Pleos 설계 가이드: 세로형 디스플레이도 레터박스 없이
// 가로: 기준 1920×1080·높이 맞춤, 세로: 기준 1080×1920·폭 맞춤. 화면이 돌거나 크기가 바뀌면 바로 바꾼다.
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    [RequireComponent(typeof(CanvasScaler))]
    public class AdaptiveScaler : MonoBehaviour
    {
        CanvasScaler sc;
        int lastW, lastH;

        public static bool Portrait => Screen.height > Screen.width;

        void Awake() { sc = GetComponent<CanvasScaler>(); Apply(); }

        void Update()
        {
            if (Screen.width != lastW || Screen.height != lastH) Apply();
        }

        void Apply()
        {
            lastW = Screen.width; lastH = Screen.height;
            if (sc == null || sc.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            if (Portrait) { sc.referenceResolution = new Vector2(1080, 1920); sc.matchWidthOrHeight = 0f; }
            else { sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f; }
        }
    }
}
