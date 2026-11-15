// 담당: S (3D 주차 연습 — 화면 글자·버튼을 코드로 만드는 도구. 앱(CoachTheme)과 같은 흰 바탕·검정 글씨·현대 블루, 둥근 카드)
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    public static class UiKit
    {
        static Sprite round;

        /// <summary>둥근 모서리 9조각 스프라이트(코드로 만든다)</summary>
        public static Sprite Round
        {
            get
            {
                if (round != null) return round;
                const int s = 64, r = 20;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (s - r)));
                        float dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (s - r)));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255);
                        px[y * s + x] = new Color32(255, 255, 255, a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                round = Sprite.Create(tex, new UnityEngine.Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return round;
            }
        }

        /// <summary>
        /// 화면 캔버스(기준 1920×1080, 높이 맞춤). cam 이 있으면 그 카메라 앞에 그린다(bat 캡처에 글자까지 찍히게),
        /// 없으면 화면 맨 위(게임 중).
        /// </summary>
        public static Canvas ScreenCanvas(string name, Transform parent, Camera cam, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            if (cam != null)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = Mathf.Max(0.15f, cam.nearClipPlane + 0.05f);   // 카메라 앞면보다 가까우면 글자가 잘려 안 보인다
            }
            else c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 1f;
            if (cam == null) go.AddComponent<AdaptiveScaler>();   // 게임 화면: 세로 디스플레이면 세로 기준으로
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>bat 캡처용: 화면 크기와 관계없이 정해진 배율(그림 높이/1080)로 그린다</summary>
        public static void FixScale(Canvas c, float scale)
        {
            var sc = c.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            sc.scaleFactor = scale;
            c.scaleFactor = scale;
        }

        /// <summary>속이 빈(투명) 이미지: 누르기·끌기만 받는다</summary>
        public static Image Blank(Transform parent, string name, Color? tint = null)
        {
            var rt = Node(name, parent);
            var im = rt.gameObject.AddComponent<Image>();
            im.color = tint ?? new Color(0f, 0f, 0f, 0f);
            return im;
        }

        /// <summary>앵커 비율 상자(부모 안에서 x0..x1, y0..y1)</summary>
        public static RectTransform Frac(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static string Stars(int n, int size = 0)
        {
            var on = new string('★', Mathf.Clamp(n, 0, 3));
            var off = new string('★', 3 - Mathf.Clamp(n, 0, 3));
            string s = "<color=#0B0B0C>" + on + "</color><color=#C9C9CF>" + off + "</color>";
            return size > 0 ? "<size=" + size + ">" + s + "</size>" : s;
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>앵커(0..1)와 픽셀 위치·크기로 배치. pivot 은 앵커와 같게.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static Image Card(Transform parent, string name, Color fill, Color? border = null)
        {
            var rt = Node(name, parent);
            Image outline = null;
            if (border.HasValue)
            {
                outline = rt.gameObject.AddComponent<Image>();
                outline.sprite = Round; outline.type = Image.Type.Sliced; outline.color = border.Value;
                var inner = Node("fill", rt);
                Stretch(inner, 2, 2, 2, 2);
                var img = inner.gameObject.AddComponent<Image>();
                img.sprite = Round; img.type = Image.Type.Sliced; img.color = fill;
                img.raycastTarget = false;
                return outline;
            }
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = Round; im.type = Image.Type.Sliced; im.color = fill;
            return im;
        }

        public static Text Label(Transform parent, string text, int size, Color color, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rt = Node("text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DcAssets.Font(bold);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public enum Kind { Plain, Primary, Danger }

        /// <summary>버튼: 둥근 카드 + 가운데 글자. kind 로 파랑 채움·빨강 채움·흰 바탕.</summary>
        public static Button Btn(Transform parent, string label, int size, Kind kind, UnityAction onClick, out Text text)
        {
            var bg = Card(parent, "btn " + label, DcAssets.Card, DcAssets.UiLine);
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            text = Label(bg.transform, label, size, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            SetKind(b, text, kind);
            return b;
        }

        public static void SetKind(Button b, Text text, Kind kind)
        {
            var outline = b.GetComponent<Image>();
            var fill = b.transform.Find("fill")?.GetComponent<Image>();
            Color f = DcAssets.Card, border = DcAssets.UiLine, tc = DcAssets.UiText;
            if (kind == Kind.Primary) { f = DcAssets.Lime; border = DcAssets.Lime; tc = DcAssets.OnLime; }
            else if (kind == Kind.Danger) { f = DcAssets.OnLime; border = DcAssets.Lime; tc = Color.white; }   // (11/14 베타: 어두운 대시보드에 묻힌다) 테두리는 라임   // 브레이크 등: 빨강 대신 흰 바탕·검은 글씨
            outline.color = border;
            if (fill != null) fill.color = f;
            text.color = tc;
        }
    }
}
