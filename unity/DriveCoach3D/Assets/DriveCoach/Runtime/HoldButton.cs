// 3D 주차 연습: 누르고 있는 동안만 참인 버튼(가속 페달). Unity 는 MonoBehaviour 를 파일 이름과 같은 클래스로 찾으므로 따로 둔다
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool held;
        public Action<bool> changed;
        public void OnPointerDown(PointerEventData e) { held = true; changed?.Invoke(true); }
        public void OnPointerUp(PointerEventData e) { held = false; changed?.Invoke(false); }
        public void OnPointerExit(PointerEventData e) { if (held) { held = false; changed?.Invoke(false); } }
        void OnDisable() { if (held) { held = false; changed?.Invoke(false); } }
    }
}
