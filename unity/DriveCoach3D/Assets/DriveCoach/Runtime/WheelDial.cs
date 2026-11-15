// 담당: S (11/13 대표: "화면을 터치하면서 운전하고 싶은 분도" — 가상 운전대. 손가락으로 원을 돌리면 그만큼 핸들 각이 쌓인다)
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    public class WheelDial : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>핸들 각(도, + 오른쪽)</summary>
        public float deg;
        public float maxDeg = 540f;
        public bool held;
        float lastAng;

        float Ang(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp);
            lp -= rt.rect.center;
            return Mathf.Atan2(lp.y, lp.x) * Mathf.Rad2Deg;
        }

        public void OnPointerDown(PointerEventData e) { held = true; lastAng = Ang(e); }
        public void OnDrag(PointerEventData e)
        {
            float a = Ang(e);
            float d = Mathf.DeltaAngle(lastAng, a);     // 화면에서 반시계 +
            lastAng = a;
            deg = Mathf.Clamp(deg - d, -maxDeg, maxDeg);  // 시계 방향 = 오른쪽
        }
        public void OnPointerUp(PointerEventData e) { held = false; }
        void OnDisable() { held = false; }
    }
}
