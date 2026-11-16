// (3D 주차 연습 — 게임 중 화면 글자·버튼. 미니멀 배치 + 3D 전용:
// 시점 버튼, 화면 거울(유로트럭 HUD 거울처럼 양쪽 사이드미러·실내 백미러를 화면 가장자리에), 후방 카메라, 서킷·도로 정보, 가속 버튼,
// 운전석에서 화면을 끌어 고개 돌리기)
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    /// <summary>Hud 가 매 프레임 그릴 것(GameMain 이 채운다)</summary>
    public class HudModel
    {
        public World w;
        public string tag = "";          // 왼쪽 위 파란 글(단계 번호·튜토리얼 n/m)
        public bool brakeOn, paused, canNext;
        public bool vWheel, vAccel, vBrake, autoDrive;   // 화면 조작 켬 여부·자동 운전 중
        public float steerDeg, maxSteerDeg = 540f;
        public string kind = "";         // stage | tutorial | circuit | road
        public string realGear = "P";
        public CamMode cam;
        public bool mirrors;             // 화면 거울 보이기
        public bool driverView;          // 운전석 시점(차에 달린 왼쪽 거울이 화면에 보인다 → 화면 왼쪽 거울은 겹치므로 숨긴다)
        public string info = "";         // 오른쪽 위 작은 글(속도·바퀴·제한 속도)
        public int blink;                // 방향지시등 깜빡임(-1 왼쪽, 1 오른쪽, 2 둘 다, 0 꺼짐)
    }

    public class Hud
    {
        public static readonly string[] CamNames = { "따라가기", "위에서", "운전석", "전체" };

        public readonly Canvas canvas;
        readonly RectTransform root;
        public Action<string> onButton;
        public Action<Vector2> onLook;
        public Action onLookEnd;

        readonly GameObject lookArea;
        readonly Image panel;
        readonly Text tagText, bodyText;
        readonly Image floorCard;
        readonly Text floorText;
        readonly Text viewText;
        readonly Image toast;
        readonly Text toastText;
        readonly Text blinkL, blinkR;
        readonly Image infoChip;
        readonly Text infoText;
        readonly Button[] gearBtns = new Button[4];
        readonly Text[] gearTexts = new Text[4];
        readonly Button brakeBtn;
        readonly Text brakeText;
        readonly RectTransform rearBox;
        readonly RawImage rearImg;
        readonly RectTransform mirrorL, mirrorR, mirrorC;
        readonly RawImage mirrorLImg, mirrorRImg, mirrorCImg;
        readonly Text attribution;
        /// <summary>해외 지역 출처 줄의 흰 테두리(한국 지역에서는 꺼 둠 = 예전 그대로)</summary>
        readonly Outline attributionHalo;
        /// <summary>출처 줄 강조 여부는 지역이 바뀔 때만 다시 정한다(CountryStyle.For 는 소문자 문자열을 만들어 매 프레임 부르면 할당이 생긴다)</summary>
        RegionData attrRegion;
        bool attrStrong;
        readonly GameObject pausedGo;
        readonly Text pausedSub;
        readonly GameObject resultGo;
        readonly RectTransform resultCard;
        bool resultBuilt;
        float lastW = -1f;
        bool lastMirrors;

        static readonly Gear[] Gears = { Gear.P, Gear.R, Gear.N, Gear.D };
        // 화면 조작: 가상 운전대(돌리는 원)·가상 액셀(누르는 동안)·가상 브레이크(누르는 동안)
        readonly WheelDial dial;
        readonly RectTransform dialRt, dialSpin;
        readonly Text dialAngle;
        readonly Image[] wheelParts;
        readonly Button accelBtn;
        readonly Text accelText;
        readonly HoldButton accelHold, brakeHold;
        readonly Text autoChip;
        readonly Image autoBg;
        public bool DialHeld => dial.held;
        public float DialDeg { get => dial.deg; set => dial.deg = value; }
        public bool AccelHeld => accelHold.held;
        public bool BrakeHeld => brakeHold.held;
        readonly Button seatBtn;
        readonly Text seatBtnText;
        readonly RectTransform seatCard;
        bool seatOpen;

        public Hud(Transform parent, Camera screenCam)
        {
            canvas = UiKit.ScreenCanvas("Hud", parent, screenCam, 10);
            root = (RectTransform)canvas.transform;

            // 운전석에서 화면을 끌어 고개 돌리기(맨 아래 층: 버튼이 위에서 먼저 받는다)
            var look = UiKit.Blank(root, "look");
            UiKit.Stretch(look.rectTransform);
            var drag = look.gameObject.AddComponent<DragArea>();
            drag.dragged = d => onLook?.Invoke(d);
            drag.released = () => onLookEnd?.Invoke();
            lookArea = look.gameObject;

            // 화면 거울(양쪽 사이드미러·실내 백미러): 좌우를 뒤집은 거울 그림
            mirrorL = MirrorBox("mirrorL", out mirrorLImg);
            mirrorR = MirrorBox("mirrorR", out mirrorRImg);
            mirrorC = MirrorBox("mirrorC", out mirrorCImg);

            // 후방 카메라(R 일 때, 아래 가운데)
            rearBox = UiKit.Card(root, "rear", DcAssets.Hex("#15171A")).rectTransform;
            rearImg = UiKit.Node("img", rearBox).gameObject.AddComponent<RawImage>();
            UiKit.Stretch(rearImg.rectTransform, 6, 6, 6, 6);
            rearImg.uvRect = new UnityEngine.Rect(1f, 0f, -1f, 1f);   // 실제 후방 카메라처럼 거울 방향
            var rearLabel = UiKit.Label(rearBox, "후방", 24, Color.white, true, TextAnchor.UpperLeft);
            UiKit.Stretch(rearLabel.rectTransform, 18, 12, 0, 0);
            rearBox.gameObject.SetActive(false);

            // 왼쪽 위: 할 일
            panel = UiKit.Card(root, "goal", new Color(1f, 1f, 1f, 0.94f), DcAssets.UiLine);
            tagText = UiKit.Label(panel.transform, "", 28, DcAssets.UiText, true, TextAnchor.UpperLeft);
            bodyText = UiKit.Label(panel.transform, "", 26, DcAssets.UiText, false, TextAnchor.UpperLeft);
            bodyText.lineSpacing = 1.05f;
            // 층 표시
            floorCard = UiKit.Card(root, "floor", DcAssets.Card, DcAssets.UiLine);
            floorText = UiKit.Label(floorCard.transform, "", 34, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(floorText.rectTransform);

            // 오른쪽 위: 목록·다시·시점
            var bMenu = UiKit.Btn(root, "메인화면", 28, UiKit.Kind.Plain, () => onButton?.Invoke("menu"), out _);
            UiKit.Place((RectTransform)bMenu.transform, new Vector2(1, 1), new Vector2(-24, -24), new Vector2(170, 76));
            var bRetry = UiKit.Btn(root, "리셋", 28, UiKit.Kind.Plain, () => onButton?.Invoke("retry"), out _);
            UiKit.Place((RectTransform)bRetry.transform, new Vector2(1, 1), new Vector2(-168, -24), new Vector2(120, 76));
            bRetry.gameObject.SetActive(false);   // 리셋 버튼은 뺐다(결과 화면에는 남김)
            var bView = UiKit.Btn(root, "시점", 26, UiKit.Kind.Plain, () => onButton?.Invoke("view"), out viewText);
            UiKit.Place((RectTransform)bView.transform, new Vector2(1, 1), new Vector2(-312, -24), new Vector2(230, 76));
            bView.gameObject.SetActive(false);   // 시점은 설정에서 바꾼다
            // (시작 알림이 사라지면 할 일이 화면에 없다) 누르면 할 일을 다시 알린다
            var bGoal = UiKit.Btn(root, "할 일", 26, UiKit.Kind.Plain, () => onButton?.Invoke("goal"), out _);
            UiKit.Place((RectTransform)bGoal.transform, new Vector2(1, 1), new Vector2(-556, -24), new Vector2(120, 76));
            bGoal.gameObject.SetActive(false);   // 할 일 버튼은 뺐다
            // 누르면 차가 멈춘 채 설정 창
            var bSet = UiKit.Btn(root, "설정", 26, UiKit.Kind.Plain, () => onButton?.Invoke("settings"), out _);
            UiKit.Place((RectTransform)bSet.transform, new Vector2(1, 1), new Vector2(-210, -24), new Vector2(120, 76));

            // 오른쪽 위 아래: 속도·바퀴·제한 속도
            infoChip = UiKit.Card(root, "info", new Color(1f, 1f, 1f, 0.94f), DcAssets.UiLine);
            infoText = UiKit.Label(infoChip.transform, "", 26, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            infoText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Stretch(infoText.rectTransform);

            // 알림
            toast = UiKit.Card(root, "toast", new Color(1f, 1f, 1f, 0.95f), DcAssets.UiLine);
            toastText = UiKit.Label(toast.transform, "", 28, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            toastText.horizontalOverflow = HorizontalWrapMode.Wrap;   // 시작 안내가 길어져 줄을 바꾼다
            UiKit.Stretch(toastText.rectTransform, 30f, 10f, 30f, 10f);   // 줄 끝 글자가 테두리에 붙던 것 → 안쪽 여백
            toast.raycastTarget = false;

            // 위 가운데: 방향지시등 화살표(계기판처럼 초록으로 깜빡)
            var green = DcAssets.Lime;
            blinkL = UiKit.Label(root, "◀", 64, green, true, TextAnchor.MiddleCenter);
            UiKit.Place(blinkL.rectTransform, new Vector2(0.5f, 1f), new Vector2(-70f, -40f), new Vector2(90f, 90f));
            blinkR = UiKit.Label(root, "▶", 64, green, true, TextAnchor.MiddleCenter);
            UiKit.Place(blinkR.rectTransform, new Vector2(0.5f, 1f), new Vector2(70f, -40f), new Vector2(90f, 90f));
            blinkL.raycastTarget = blinkR.raycastTarget = false;
            blinkL.enabled = blinkR.enabled = false;

            // 아래 왼쪽: 기어
            for (int i = 0; i < 4; i++)
            {
                var g = Gears[i];
                gearBtns[i] = UiKit.Btn(root, g.ToString(), 46, UiKit.Kind.Plain, () => onButton?.Invoke("gear:" + g), out gearTexts[i]);
                // (왼쪽 아래 구석에 차량 시스템의 검은 화면 전환 아이콘이 떠서 P 를 가린다) 구석에서 오른쪽으로 비켜 둔다
                UiKit.Place((RectTransform)gearBtns[i].transform, new Vector2(0, 0), new Vector2(110 + i * 128, 24), new Vector2(104, 104));
            }
            // 아래 오른쪽: 브레이크(누를 때마다 밟음/뗌). 서킷·도로의 속도는 자동(핸들만) 또는 실제 액셀 — 화면 가속 버튼은 없앴다
            brakeBtn = UiKit.Btn(root, "브레이크", 34, UiKit.Kind.Plain, () => onButton?.Invoke("brake"), out brakeText);
            UiKit.Place((RectTransform)brakeBtn.transform, new Vector2(1, 0), new Vector2(-24 - 130 - 16, 24), new Vector2(220, 170));   // 브레이크 왼쪽, 가속 오른쪽(높이 같고 가속이 더 좁다)

            brakeHold = brakeBtn.gameObject.AddComponent<HoldButton>();
            // 가상 액셀: 브레이크 위. 누르고 있는 동안 가속
            accelBtn = UiKit.Btn(root, "가속", 34, UiKit.Kind.Plain, () => { }, out accelText);
            UiKit.Place((RectTransform)accelBtn.transform, new Vector2(1, 0), new Vector2(-24, 24), new Vector2(130, 170));
            accelHold = accelBtn.gameObject.AddComponent<HoldButton>();
            accelBtn.gameObject.SetActive(false);
            // 가상 운전대: 기어 위. 원을 손가락으로 돌린다(가운데 막대가 핸들 각을 보여 준다)
            // (이름·각도가 없어 무엇인지 모르겠다, 운전석 시점에서 왼쪽 거울을 가린다) 이름과 지금 각도를 쓰고, 조금 작게·옅게
            // 흰 카드를 없애고 바탕이 비치는 휠 그림(테·바큇살 3개·가운데)으로 그린다.
            //   테와 바큇살은 짙은 몸통 + 흰 가장자리라 밝은 하늘·어두운 길 어디서나 보인다. 맨 위 라임 표시가 돌아간 만큼을 보여 준다
            var dialBg = UiKit.Blank(root, "dial");          // 투명하지만 손가락은 받는다
            dialRt = dialBg.rectTransform;
            UiKit.Place(dialRt, new Vector2(0, 0), new Vector2(24, 148), new Vector2(300, 340));
            dial = dialBg.gameObject.AddComponent<WheelDial>();
            dialSpin = UiKit.Node("spin", dialRt);
            dialSpin.anchorMin = dialSpin.anchorMax = dialSpin.pivot = new Vector2(0.5f, 0.5f);
            dialSpin.anchoredPosition = new Vector2(0f, -4f); dialSpin.sizeDelta = new Vector2(260, 260);
            var parts = new List<Image>();
            Image WheelPart(string name, Sprite sp, Color c, Vector2 pos, Vector2 size)
            {
                var im = UiKit.Blank(dialSpin, name, c);
                im.sprite = sp; im.raycastTarget = false;
                UiKit.Place(im.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
                parts.Add(im);
                return im;
            }
            var spokeC = DcAssets.UiText;
            foreach (var (pos, size) in new[] { (new Vector2(-71f, 0f), new Vector2(66f, 22f)), (new Vector2(71f, 0f), new Vector2(66f, 22f)), (new Vector2(0f, -71f), new Vector2(22f, 66f)) })
            {
                var sp = WheelPart("spoke", null, spokeC, pos, size);
                var ol = sp.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(1f, 1f, 1f, 0.9f); ol.effectDistance = new Vector2(2f, -2f);
            }
            WheelPart("ring", DiscSprite(256, 127f, 101f, 3.5f), Color.white, Vector2.zero, new Vector2(260f, 260f));
            WheelPart("hub", DiscSprite(96, 46f, -1e4f, 3.5f), Color.white, Vector2.zero, new Vector2(86f, 86f));
            var mark = UiKit.Blank(dialSpin, "mark", DcAssets.Lime);
            mark.raycastTarget = false;
            UiKit.Place(mark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 115f), new Vector2(26f, 30f));
            wheelParts = parts.ToArray();
            var dialName = UiKit.Label(dialRt, "스티어링 휠 돌리기", 22, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            dialName.raycastTarget = false;
            Halo(dialName);
            dialName.gameObject.SetActive(false);   // 휠 위아래 글은 뺐다(돌린 각도만 남김)
            UiKit.Place(dialName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(290f, 30f));
            dialAngle = UiKit.Label(dialRt, "", 22, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            dialAngle.raycastTarget = false;
            Halo(dialAngle);
            UiKit.Place(dialAngle.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(290f, 28f));
            dialBg.gameObject.SetActive(false);
            // 자동 운전 중 표시(위 가운데 아래)
            // (글씨에 바탕이 없어 풍경에 묻힌다) 흰 바탕 카드
            autoBg = UiKit.Card(root, "auto", new Color(1f, 1f, 1f, 0.94f), DcAssets.UiLine);
            UiKit.Place(autoBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(240f, 52f));
            autoBg.raycastTarget = false; autoBg.gameObject.SetActive(false);
            autoChip = UiKit.Label(root, "자동 운전 중", 28, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            UiKit.Place(autoChip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(260f, 44f));
            autoChip.raycastTarget = false; autoChip.enabled = false;

            // 운전석 시야 조절: 운전석 시점에서만 브레이크 왼쪽에. 누르면 조절판(앞뒤·높이·좌우)이 열린다
            seatBtn = UiKit.Btn(root, "시야 조절", 26, UiKit.Kind.Plain, () => { seatOpen = !seatOpen; }, out seatBtnText);
            UiKit.Place((RectTransform)seatBtn.transform, new Vector2(1, 0), new Vector2(-268, 24), new Vector2(170, 80));
            seatCard = UiKit.Card(root, "seat", new Color(1f, 1f, 1f, 0.97f), DcAssets.UiLine).rectTransform;
            // (시야 조절을 열면 액셀 버튼이 가려진다) 액셀·브레이크 왼쪽 위로
            UiKit.Place(seatCard, new Vector2(1, 0), new Vector2(-268, 120), new Vector2(640, 400));
            var st = UiKit.Label(seatCard, "운전석 시야", 30, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(st.rectTransform, new Vector2(0, 1), new Vector2(28, -16), new Vector2(360, 52));
            var sh = UiKit.Label(seatCard, "누를 때마다 2 cm, 저장됩니다", 24, DcAssets.UiSub, false, TextAnchor.MiddleLeft);
            UiKit.Place(sh.rectTransform, new Vector2(0, 1), new Vector2(28, -64), new Vector2(400, 32));
            var seatPanel = new SeatPanel(seatCard, 28f, -108f, 584f, 92f, 24);
            var sr = UiKit.Btn(seatCard, "기본으로", 24, UiKit.Kind.Plain, () => seatPanel.Reset(), out _);
            UiKit.Place((RectTransform)sr.transform, new Vector2(1, 1), new Vector2(-160, -16), new Vector2(130, 64));
            var sc = UiKit.Btn(seatCard, "닫기", 24, UiKit.Kind.Primary, () => { seatOpen = false; }, out _);
            UiKit.Place((RectTransform)sc.transform, new Vector2(1, 1), new Vector2(-16, -16), new Vector2(120, 64));
            seatCard.gameObject.SetActive(false);
            seatBtn.gameObject.SetActive(false);

            // 지도 출처(오픈스트리트맵, ODbL): 내 동네 판에서 화면 아래 가운데에 작게
            // (지도 출처 글자가 D 버튼과 겹친다) 조금 작게, 기어 줄을 피해 오른쪽으로
            attribution = UiKit.Label(root, "", 18, DcAssets.UiSub, false, TextAnchor.LowerCenter);
            UiKit.Place(attribution.rectTransform, new Vector2(0.5f, 0f), new Vector2(70f, 6f), new Vector2(560f, 50f));
            Halo(attribution);
            attributionHalo = attribution.GetComponent<Outline>();
            attributionHalo.enabled = false;

            // 멈춤(실제 차가 P 가 아님)
            pausedGo = Dim("paused");
            pausedGo.GetComponent<Image>().raycastTarget = false;   // 멈춰 있어도 목록·다시 버튼은 눌린다
            var pc = OverlayCard(pausedGo.transform, 760, 220);
            var pt = UiKit.Label(pc, "실제 차를 P에 두십시오", 40, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            UiKit.Place(pt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(720, 70));
            pausedSub = UiKit.Label(pc, "", 26, DcAssets.UiSub, false, TextAnchor.MiddleCenter);
            UiKit.Place(pausedSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -130), new Vector2(720, 50));
            pausedGo.SetActive(false);

            // 결과
            resultGo = Dim("result");
            resultCard = OverlayCard(resultGo.transform, 820, 440);
            // (실패 창이 화면 가운데의 차를 가려 무엇에 부딪혔는지 못 본다) 창을 위쪽에 둔다
            // (결과 창이 오른쪽 위 정보 칸을 가린다) 정보 칸(위에서 112~168) 아래에서 시작
            UiKit.Place(resultCard, new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(820f, 440f));
            resultGo.SetActive(false);
        }

        RectTransform MirrorBox(string name, out RawImage img)
        {
            var frame = UiKit.Card(root, name, DcAssets.Hex("#15171A"));
            frame.raycastTarget = false;
            img = UiKit.Node("glass", frame.transform).gameObject.AddComponent<RawImage>();
            UiKit.Stretch(img.rectTransform, 6, 6, 6, 6);
            img.uvRect = new UnityEngine.Rect(1f, 0f, -1f, 1f);
            img.raycastTarget = false;
            frame.gameObject.SetActive(false);
            return frame.rectTransform;
        }

        GameObject Dim(string name)
        {
            var d = UiKit.Blank(root, name, new Color(0f, 0f, 0f, 0.35f));
            UiKit.Stretch(d.rectTransform);
            return d.gameObject;
        }

        static RectTransform OverlayCard(Transform parent, float w, float h)
        {
            var c = UiKit.Card(parent, "card", DcAssets.Card, DcAssets.UiLine);
            UiKit.Place(c.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            return c.rectTransform;
        }

        public void SetVisible(bool on) { if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on); }

        /// <summary>장면이 바뀌면 거울·후방 그림을 새 카메라 것으로</summary>
        public void Bind(SceneRig scene)
        {
            mirrorLImg.texture = scene.mirrors.texL;
            mirrorRImg.texture = scene.mirrors.texR;
            mirrorCImg.texture = scene.mirrors.texC;
            rearImg.texture = scene.rearTex;
            resultBuilt = false;
            lastW = -1f;
        }

        /// <summary>장면을 바꾸는 동안(동 사이 이동·다시): 장면에 딸린 그림(거울·후방 카메라)과 알림을 감춘다 — 장면이 내려가면 검게 보인다. 다음 Tick 이 다시 맞춘다.</summary>
        public void HideSceneBits()
        {
            Show(mirrorL, false); Show(mirrorR, false); Show(mirrorC, false);
            Show(rearBox, false); Show(toast, false);
            lastW = -1f;      // 다음 Tick 에서 거울 자리를 다시 잡게
        }

        static void Show(Component c, bool on) { if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }
        static void Show(GameObject g, bool on) { if (g.activeSelf != on) g.SetActive(on); }

        public void Tick(HudModel m)
        {
            var w = m.w;
            var st = w.st;
            float W = root.rect.width;
            blinkL.enabled = m.blink == -1 || m.blink == 2;
            blinkR.enabled = m.blink == 1 || m.blink == 2;
            bool play = w.state == PlayState.Play;

            // ── 배치(화면 폭·거울 여부가 바뀔 때만) ──
            float xRight = W - 342f;                     // 오른쪽 위 버튼 묶음 왼쪽 끝(설정·할 일·시점·다시·목록)
            float pw = Mathf.Clamp(W * 0.36f, 460f, 760f);
            float mirrorTopBottom = 0f;
            if (W != lastW || m.mirrors != lastMirrors)
            {
                lastW = W; lastMirrors = m.mirrors;
                if (m.mirrors)
                {
                    // 실내 백미러는 왼쪽 위(층 표시 칸 다음)와 오른쪽 버튼 사이 가운데. 자리가 좁으면 그 아래 줄로.
                    //   (왼쪽 위 할 일 칸을 숨긴 뒤에도 그 폭을 비워 둬서 백미러가 오른쪽 아래로 밀려 정보 칸·알림과 겹쳤다)
                    float xLeft = 24f + 110f + 14f;
                    float cw = Mathf.Min(520f, xRight - xLeft - 32f);
                    if (cw >= 300f)
                        UiKit.Place(mirrorC, new Vector2(0f, 1f), new Vector2((xLeft + xRight) / 2f - cw / 2f, -20f), new Vector2(cw, cw * 0.32f));
                    else
                    {
                        cw = 420f;
                        UiKit.Place(mirrorC, new Vector2(1f, 1f), new Vector2(-24f, -116f), new Vector2(cw, cw * 0.32f));
                    }
                    var side = W >= 1700f ? new Vector2(380f, 222f) : new Vector2(300f, 175f);
                    UiKit.Place(mirrorL, new Vector2(0f, 0.5f), new Vector2(24f, 30f), side);
                    UiKit.Place(mirrorR, new Vector2(1f, 0.5f), new Vector2(-24f, 30f), side);
                }
                else pw = Mathf.Min(760f, Mathf.Min(W * 0.5f, xRight - 40f));
                var rs = W >= 1500f ? new Vector2(448f, 252f) : new Vector2(360f, 203f);
                // 세로 화면(폭이 좁음): 아래 줄(기어·브레이크)과 겹치지 않게 그 위로
                // (후방 화면 왼쪽 아래를 D 버튼이, 아래쪽을 지도 출처 글이 덮었다) 출처 글 위로, 기어 줄과 가로로 겹치면 기어 줄 위로
                float rearY = W < 1200f ? 200f : 40f;
                if (W / 2f - rs.x / 2f < 110f + 3f * 128f + 104f + 16f) rearY = Mathf.Max(rearY, 24f + 104f + 16f);
                UiKit.Place(rearBox, new Vector2(0.5f, 0f), new Vector2(0f, rearY), rs);
                panelW = pw;
            }
            if (m.mirrors && mirrorC.anchorMin.x == 0f) mirrorTopBottom = 20f + mirrorC.sizeDelta.y;
            Show(mirrorL, m.mirrors && play && !m.driverView);
            Show(mirrorR, m.mirrors && play);
            Show(mirrorC, m.mirrors && play);

            // ── 할 일 ──
            string body = st.goal ?? "";
            string tag = m.tag;
            if (w.IsTutorial && w.stepI < w.steps.Count)
            {
                body = w.StepText;
                tag = $"{w.stepI + 1}/{w.steps.Count}";
            }
            tagText.text = tag;
            float tagW = string.IsNullOrEmpty(tag) ? 0f : tagText.preferredWidth + 18f;
            UiKit.Place(panel.rectTransform, new Vector2(0, 1), new Vector2(24, -24), new Vector2(panelW, 100));
            UiKit.Place(tagText.rectTransform, new Vector2(0, 1), new Vector2(24, -24), new Vector2(Mathf.Max(1f, tagW), 40));
            bodyText.text = body;
            UiKit.Place(bodyText.rectTransform, new Vector2(0, 1), new Vector2(24 + tagW, -26), new Vector2(panelW - 48 - tagW, 40));
            float bh = Mathf.Min(bodyText.preferredHeight, 26f * 1.45f * 4f);
            bodyText.rectTransform.sizeDelta = new Vector2(panelW - 48 - tagW, bh);
            float ph = bh + 52f;
            panel.rectTransform.sizeDelta = new Vector2(panelW, ph);
            // 할 일 카드는 따라 하는 순서가 있는 튜토리얼에서만 보인다
            //   (주차 1단계에서 무엇을 하라는지 모르겠다) 주차 단계도 할 일이 있으니 남기고, 서킷·도로 주행에서만 숨긴다
            //   튜토리얼·주차 단계에서도 보이지 않는다. 시작할 때 잠깐 뜨는 안내(world.Say)만 남는다.
            bool showGoal = false;
            Show(panel, showGoal);

            // 층(층이 여럿인 단계만)
            bool multi = st.floors.Count > 1;
            Show(floorCard, multi);
            if (multi)
            {
                floorText.text = st.floors[Mathf.Clamp(w.floorI, 0, st.floors.Count - 1)].name;
                UiKit.Place(floorCard.rectTransform, new Vector2(0, 1), new Vector2(showGoal ? 24 + panelW + 14 : 24, -24), new Vector2(110, 76));
            }

            viewText.text = "시점(" + CamNames[(int)m.cam] + ")";
            var rg = st.road != null ? st.road.region : null;
            attribution.text = rg != null ? "지도 " + rg.attribution.Replace(" · ", "\n") : "";   // 가운뎃점 대신 두 줄
            // 해외 지역(출처 표기 조건이 있는 자료 — 베를린: ODbL·dl-de): 운전석에서 핸들 위에 겹쳐도 읽히게 진한 글 + 흰 테두리. 한국은 예전 그대로(회색, 테두리 없음)
            if (!ReferenceEquals(rg, attrRegion)) { attrRegion = rg; attrStrong = rg != null && !CountryStyle.For(rg).IsKorea; }
            bool strongAttr = attrStrong;
            if (attributionHalo.enabled != strongAttr) attributionHalo.enabled = strongAttr;
            attribution.color = strongAttr ? DcAssets.UiText : DcAssets.UiSub;     // Graphic.color 는 같은 값이면 다시 그리지 않는다

            // 오른쪽 위 정보
            bool hasInfo = !string.IsNullOrEmpty(m.info);
            Show(infoChip, hasInfo);
            if (hasInfo)
            {
                infoText.text = m.info;
                UiKit.Place(infoChip.rectTransform, new Vector2(1, 1), new Vector2(-24, -112), new Vector2(infoText.preferredWidth + 48f, 56f));
            }

            // 알림: 할 일 칸(과 실내 백미러) 아래 가운데
            bool showToast = w.toastT > 0f && !string.IsNullOrEmpty(w.toast);
            Show(toast, showToast);
            if (showToast)
            {
                toastText.text = w.toast;
                float maxTw = Mathf.Min(W - 80f, 1100f);
                // 한 줄로 쓸 때의 폭을 재고(가장 긴 줄), 그 폭(최대 maxTw)에서 줄을 바꿔 높이를 잰다
                toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
                float tw = Mathf.Min(toastText.preferredWidth + 64f, maxTw);
                toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
                toast.rectTransform.sizeDelta = new Vector2(tw, 64f);
                float th = Mathf.Max(64f, toastText.preferredHeight + 26f);
                float y = Mathf.Max(showGoal ? 24f + ph : 0f, mirrorTopBottom) + 18f;
                // 좁은 화면에서 긴 알림이 오른쪽 위 정보 칸과 겹치면 그 아래로 내린다
                if (hasInfo && W / 2f + tw / 2f > W - 24f - (infoText.preferredWidth + 48f) - 12f) y = Mathf.Max(y, 112f + 56f + 12f);
                // (알림이 오른쪽 위 시점 버튼에 겹친다) 위 버튼 줄과 가로로 겹치면 그 아래로
                if (W / 2f + tw / 2f > xRight - 12f) y = Mathf.Max(y, 116f);   // (100 이면 위 버튼 줄과 3 px — 붙어 보였다)
                // 자동 운전 중 표시(위에서 96~148 px)와 겹치면 그 아래로(알림 글이 표시에 가려지던 것)
                if (play && m.autoDrive && y + th > 96f - 8f) y = Mathf.Max(y, 96f + 52f + 12f);
                UiKit.Place(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(tw, th));
            }

            // 기어·브레이크·가속
            for (int i = 0; i < 4; i++)
                UiKit.SetKind(gearBtns[i], gearTexts[i], w.car.gear == Gears[i] ? UiKit.Kind.Primary : UiKit.Kind.Plain);
            UiKit.SetKind(brakeBtn, brakeText, m.brakeOn ? UiKit.Kind.Danger : UiKit.Kind.Plain);
            // (한 번 누르면 켜져 있는 방식인지 화면만으로 알 수 없다) 누르면 유지되는 방식(가상 브레이크 끔)에서는 지금 상태를 글로
            //   ("뗌/밟는 중"이 명령인지 상태인지 헷갈린다) 괄호로 지금 상태
            string bt = "브레이크";   // 밟았는지는 글이 아니라 빨간색으로 보여 준다
            if (brakeText.text != bt) brakeText.text = bt;
            // 화면 조작
            bool ctl = play && !m.paused;
            Show(dialRt, ctl && m.vWheel && !m.autoDrive);
            Show(accelBtn, ctl && m.vAccel && !m.autoDrive);
            if (accelBtn.gameObject.activeSelf) UiKit.SetKind(accelBtn, accelText, accelHold.held ? UiKit.Kind.Primary : UiKit.Kind.Plain);
            dial.maxDeg = m.maxSteerDeg;
            dialSpin.localRotation = Quaternion.Euler(0f, 0f, -m.steerDeg);
            if (dialRt.gameObject.activeSelf)
            {
                int a = Mathf.RoundToInt(m.steerDeg);
                string at = Mathf.Abs(a) < 5 ? "" : (a < 0 ? "왼쪽 " : "오른쪽 ") + Mathf.Abs(a) + "°" + (Mathf.Abs(a) >= m.maxSteerDeg - 5f ? " (끝)" : "");
                if (dialAngle.text != at) dialAngle.text = at;
                // 바탕이 비치게: 운전석 시점(거울·창밖을 가리지 않게)에서는 더 옅게
                float wa = m.cam == CamMode.Driver ? 0.55f : 0.75f;
                foreach (var im in wheelParts) { var c = im.color; if (c.a != wa) { c.a = wa; im.color = c; } }
            }
            autoChip.enabled = play && m.autoDrive;
            Show(autoBg, play && m.autoDrive);

            // 후방 카메라: R 이고 위에서·운전석(가운데 화면에 이미 나옴)이 아닐 때
            Show(rearBox, play && w.car.gear == Gear.R && m.cam != CamMode.Top && m.cam != CamMode.Driver);

            // 고개 돌리기: 운전석에서 달리는 중에만
            Show(lookArea, m.cam == CamMode.Driver && play && !m.paused);
            // 시야 조절: 운전석 시점에서만(다른 시점으로 바꾸면 조절판도 닫는다)
            bool driverView = m.cam == CamMode.Driver && play;
            if (!driverView) seatOpen = false;
            Show(seatBtn, false);   // 시야 조절은 설정에서만
            Show(seatCard, driverView && seatOpen);
            UiKit.SetKind(seatBtn, seatBtnText, seatOpen ? UiKit.Kind.Primary : UiKit.Kind.Plain);

            // 멈춤·결과
            Show(pausedGo, play && m.paused);
            if (play && m.paused) pausedSub.text = "지금 기어 " + m.realGear;
            Show(resultGo, !play);
            if (!play && !resultBuilt) BuildResult(m);
            if (play) resultBuilt = false;
        }

        float panelW = 640f;

        void BuildResult(HudModel m)
        {
            resultBuilt = true;
            for (int i = resultCard.childCount - 1; i >= 0; i--)
            {
                var c = resultCard.GetChild(i);
                if (c.name == "fill") continue;
                Dc.Kill(c.gameObject);
            }
            var w = m.w;
            var r = w.result;
            float y = -56f;
            Text Line(string text, int size, Color col, bool bold, float h)
            {
                var t = UiKit.Label(resultCard, text, size, col, bold, TextAnchor.MiddleCenter);
                UiKit.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(780f, h));
                y -= h + 8f;
                return t;
            }
            bool next = false;
            if (w.state == PlayState.Done && r != null)
            {
                // (100 m 달리고 P 를 눌렀는데 "도착") 도로 주행은 "주행 끝", 자동 운전이 달렸으면 그렇게
                // (메뉴 이름은 '핸들 감각'인데 결과는 '튜토리얼 완료', 본문이 크게 비었다) 연습 이름 + 완료, 카드는 버튼까지만
                string tutName = w.st != null && !string.IsNullOrEmpty(w.st.title) ? w.st.title : "연습";
                string title = "완료";   // 결과 화면은 "완료"만. 별점과 설명 글은 뺐다(도로 주행 리포트는 남김)
                Line(title, 48, DcAssets.UiText, true, 64f);
                float cardH = 290f;
                var rep0 = m.kind != "tutorial" && !m.autoDrive && r.kind == "road" && w.roadRun != null ? Report(w.roadRun) : null;
                if (rep0 != null && rep0.Count > 0)
                {
                    // (자동 운전으로 조금 달리고 끝냈는데 별 셋) 자동 운전은 내가 운전한 것이 아니니 별을 매기지 않는다
                    var rep = rep0;
                    {
                        // 연습 결과 리포트(3D 연습 안에서만 — 게임이 만든 세계라 값이 정확하다): 항목별 횟수 + 코치 한마디
                        Line($"달린 거리 {w.roadRun.distance / 1000f:0.0} km", 24, DcAssets.UiSub, false, 34f);
                        int shown = Mathf.Min(rep.Count, 5);
                        for (int i = 0; i < shown; i++)
                        {
                            var a = UiKit.Label(resultCard, rep[i].label, 28, DcAssets.UiText, false, TextAnchor.MiddleLeft);
                            UiKit.Place(a.rectTransform, new Vector2(0.5f, 1f), new Vector2(-70f, y), new Vector2(480f, 40f));
                            var b = UiKit.Label(resultCard, rep[i].count + "회", 28, DcAssets.UiText, true, TextAnchor.MiddleRight);
                            UiKit.Place(b.rectTransform, new Vector2(0.5f, 1f), new Vector2(250f, y), new Vector2(120f, 40f));
                            y -= 46f;
                        }
                        if (rep.Count > shown) Line($"그 밖에 {rep.Count - shown}가지", 24, DcAssets.UiSub, false, 34f);
                        Line(rep[0].advice, 26, DcAssets.UiText, true, 44f);
                        cardH = 64f + 8f + 42f + shown * 46f + (rep.Count > shown ? 42f : 0f) + 52f + 56f + 130f;
                    }
                }
                resultCard.sizeDelta = new Vector2(820f, cardH);
                next = m.canNext;
            }
            else
            {
                // "실패"라는 말은 쓰지 않는다. 무슨 일이 있었는지만 한 줄로 알린다
                resultCard.sizeDelta = new Vector2(820f, 290f);
                Line(w.failReason, 36, DcAssets.UiText, true, 80f);
            }
            var ids = next ? new[] { ("retry", "리셋", UiKit.Kind.Plain), ("next", "다음", UiKit.Kind.Primary), ("menu", "메인화면", UiKit.Kind.Plain) }
                           : new[] { ("retry", "리셋", UiKit.Kind.Plain), ("menu", "메인화면", UiKit.Kind.Plain) };
            float bw = 200f, gap = 16f;
            float total = ids.Length * bw + (ids.Length - 1) * gap;
            float bx = -total / 2f + bw / 2f;
            foreach (var (id, label, kind) in ids)
            {
                string cid = id;
                var b = UiKit.Btn(resultCard, label, 30, kind, () => onButton?.Invoke(cid), out _);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(bx, 32f);
                rt.sizeDelta = new Vector2(bw, 88f);
                bx += bw + gap;
            }
        }

        /// <summary>숫자 대신 느낌으로(2D 판과 같은 말): 반듯했는지, 가운데였는지, 몇 번 만에 넣었는지. 서킷·도로는 기록.</summary>
        /// <summary>도로 연습 결과 리포트: 한 번이라도 있었던 항목을 무거운 순서로(가중치×횟수). 항목마다 코치 한마디.</summary>
        public static List<(string label, int count, string advice)> Report(RoadRun rr)
        {
            var all = new List<(string label, int count, int weight, string advice)>
            {
                ("부딪힘", rr.bumpCount, 6, "좁은 곳에서는 속도를 줄이고, 닿기 전에 멈추십시오."),
                ("연석 밟음", rr.curbCount, 3, "돌 때 안쪽 뒷바퀴가 앞바퀴보다 안쪽으로 지나갑니다. 조금 더 크게 도십시오."),
                ("신호 위반", rr.redLightCount, 5, "노란불이 보이면 미리 발을 브레이크로 옮기십시오."),
                ("보행자 보호", rr.pedCount, 5, "횡단보도에 사람이 보이면 정지선 앞에서 완전히 서십시오."),
                ("어린이 보호구역 횡단보도 일시정지", rr.schoolStopCount, 4, "어린이 보호구역 횡단보도는 사람이 없어도 일단 멈추십시오."),
                ("빨간불 우회전 일시정지", rr.rightOnRedCount, 4, "빨간불 우회전은 정지선에서 완전히 선 다음에 도십시오."),
                ("과속", rr.speedingCount, 3, "제한 속도 표지를 보면 클러스터를 한 번 확인합니다."),
                ("정지선 넘음", rr.stopLineCount, 3, "정지선이 보닛에 가려지기 전에 멈추기 시작합니다."),
                ("앞차와 너무 가까움", rr.tailgateCount, 3, "앞차가 지난 곳을 2초 뒤에 지나도록 간격을 두십시오."),
                ("차로 이탈", rr.laneDepartCount, 2, "멀리 차로 가운데를 보면 차가 따라갑니다."),
                ("방향지시등 안 켬", rr.noSignalCount, 2, "돌거나 차로를 바꾸기 30 m 전에 방향지시등부터 켜십시오."),
                ("방지턱 과속", rr.humpCount, 2, "방지턱 앞에서는 30 km/h 아래로 줄이십시오."),
                ("전조등", rr.lightCount, 1, "어둡거나 비가 오면 출발 전에 전조등을 켜십시오."),
                ("상향등", rr.highBeamCount, 1, "마주 오는 차가 보이면 상향등을 끄십시오."),
                ("안전벨트", rr.beltCount, 1, "출발 전에 안전벨트부터 매십시오."),
            };
            all.RemoveAll(a => a.count <= 0);
            all.Sort((a, b) => (b.weight * b.count).CompareTo(a.weight * a.count));
            var res = new List<(string label, int count, string advice)>();
            foreach (var a in all) res.Add((a.label, a.count, a.advice));
            return res;
        }

        public static string Detail(World w, JudgeResult r)
        {
            if (r.kind == "circuit")
            {
                string best = float.IsInfinity(r.offset) ? "-" : Clock(r.offset);
                return $"가장 빠른 바퀴 {best}, " + (r.moves == 0 ? "길 밖으로 한 번도 안 나갔습니다" : $"길 밖으로 {r.moves}번");
            }
            if (r.kind == "road")
            {
                var rr = w.roadRun;
                if (rr == null) return "";
                if (rr.distance < 300f) return $"{rr.distance:0} m만 달렸습니다. 신호 있는 교차로를 몇 번 지나 본 뒤 P로 끝내면 평가해 드립니다.";
                if (r.moves == 0) return "신호, 차로, 속도 모두 잘 지켰습니다";
                var extra = "";
                if (rr.rightOnRedCount > 0) extra += $", 빨간불 우회전 일시정지 안 함 {rr.rightOnRedCount}";
                if (rr.tailgateCount > 0) extra += $", 앞차 너무 가까움 {rr.tailgateCount}";
                if (rr.noSignalCount > 0) extra += $", 방향지시등 {rr.noSignalCount}";
                if (rr.lightCount > 0) extra += $", 전조등 {rr.lightCount}";
                if (rr.beltCount > 0) extra += $", 안전벨트 {rr.beltCount}";
                if (rr.humpCount > 0) extra += $", 방지턱 과속 {rr.humpCount}";
                if (rr.schoolStopCount > 0) extra += $", 어린이 보호구역 횡단보도 일시정지 안 함 {rr.schoolStopCount}";
                if (rr.pedCount > 0) extra += $", 보행자 보호 {rr.pedCount}";
                return $"신호 위반 {rr.redLightCount}, 정지선 {rr.stopLineCount}, 차로 이탈 {rr.laneDepartCount}, 과속 {rr.speedingCount}" + extra;
            }
            string detail;
            if (r.kind == "line") detail = r.precise ? "선에 딱 맞췄습니다" : "선 근처에 잘 섰습니다";
            else
            {
                bool straight = r.angle <= 2.5f, centered = r.offset <= 0.2f;
                if (straight && centered) detail = "반듯하게, 칸 가운데에";
                else if (straight) detail = "반듯하게, 조금 한쪽으로";
                else if (centered) detail = "칸 가운데에, 조금 비스듬하게";
                else detail = "조금 비스듬하고 한쪽으로";
            }
            return detail + ", " + (r.efficient ? "한 번에 넣었습니다" : "여러 번 고쳐 넣었습니다");
        }

        /// <summary>
        /// 둥근 판·고리 그림(코드로 만든다). 반지름 rIn 안쪽은 비우고(rIn 이 음수면 꽉 찬 원), 가장자리 edge 픽셀은 흰 줄, 몸통은 짙은 색.
        /// </summary>
        static Sprite DiscSprite(int n, float rOut, float rIn, float edge)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            Color dark = DcAssets.UiText;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rOut - d + 0.5f) * Mathf.Clamp01(d - rIn + 0.5f);
                    float e = Mathf.Min(rOut - d, d - rIn);
                    var c = Color.Lerp(dark, Color.white, Mathf.Clamp01(edge - e + 0.5f));
                    c.a = a;
                    px[y * n + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return Sprite.Create(t, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        /// <summary>바탕 없이 화면 위에 쓰는 글: 흰 테두리를 둘러 밝은 곳·어두운 곳 어디서나 읽히게</summary>
        static void Halo(Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(1f, 1f, 1f, 0.95f);
            // (2 px 테두리가 획을 덮어 '가운데'가 '카운데'로 읽힌다) 1 px
            o.effectDistance = new Vector2(1f, -1f);
        }

        public static string Clock(float sec)
        {
            if (sec < 0f || float.IsInfinity(sec) || float.IsNaN(sec)) sec = 0f;
            int m = (int)(sec / 60f);
            return $"{m}:{sec - m * 60f:00.0}";
        }
    }
}
