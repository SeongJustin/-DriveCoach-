// MenuScreen: 동네 지도 누르기
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    /// <summary>동네 지도 누르기: 누른 자리를 그림 안 0..1 좌표(왼쪽 아래 0,0)로 알린다</summary>
    public class MapClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public Action<Vector2> clicked;
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp)) return;
            var r = rt.rect;
            if (r.width <= 0f || r.height <= 0f) return;
            clicked?.Invoke(new Vector2((lp.x - r.xMin) / r.width, (lp.y - r.yMin) / r.height));
        }
    }
}
