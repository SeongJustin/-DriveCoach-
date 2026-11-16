// 3D 주차 연습 — 화면을 끌어 시점 돌리기(운전석에서 고개 돌리기). 끌어 움직인 양(픽셀)을 넘긴다
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    public class DragArea : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Action<Vector2> dragged;
        public Action released;
        public void OnPointerDown(PointerEventData e) { }
        public void OnDrag(PointerEventData e) { dragged?.Invoke(e.delta); }
        public void OnPointerUp(PointerEventData e) { released?.Invoke(); }
        void OnDisable() { released?.Invoke(); }
    }
}
