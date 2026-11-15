// 담당: S (3D 주차 연습 — 운전석 시점(유로트럭·플라이트 시뮬레이터처럼): 실내(대시보드·A필러·핸들·계기판·가운데 화면)와
// 거울 3개(실내 백미러·양쪽 사이드미러)를 차 안에 만들고, 실제 차의 핸들 각을 가상 핸들에 그대로 돌린다.
// 11/8 대표 요청: "3인칭 내 운전자 시점이 유로트럭같이 … CN8 차량 내부의 모습이 그대로 재현돼서 연동"
//               "사이드나 백미러는 설정을 하면, 고개를 돌리지 않아도 화면 내에서 보이도록" → MirrorRig 의 그림을 Hud 가 화면 가장자리에도 띄운다.
// 실내 치수(눈높이·핸들 위치·대시보드 높이)는 준중형 세단(아반떼 CN8 급) 기준 근사. 현대 로고·고유 디자인은 쓰지 않는다(상표·디자인권).
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{

    public class Cockpit
    {
        public readonly GameObject root;
        public readonly Vector3 eyeLocal;            // 차 기준(가운데·바닥 0, +Z 앞, +X 오른쪽) 눈 위치
        readonly Transform wheel;
        readonly Text speedText, gearText, infoText;
        readonly RawImage centerScreen;
        readonly Image speedArc;
        string lastGear;
        const float DialMax = 160f;
        Texture screenTex;

        /// <summary>운전자 눈 위치(차 가운데·바닥 기준). 어깨(주차 공식 기준점)와 같은 앞뒤 위치, 왼쪽 운전석.</summary>
        public static Vector3 Eye(Vehicle v)
        {
            float centerOff = (v.wheelbase + v.frontOh - v.rearOh) / 2f;   // 뒷축 → 차 가운데
            float zEye = (v.wheelbase - 1.25f) - centerOff;
            float xEye = -(v.width / 2f - 0.45f);
            return new Vector3(xEye, 1.17f, zEye + 0.05f);
        }

        public Cockpit(Vehicle v, Transform carRoot, MirrorRig mirrors)
        {
            root = new GameObject("Cockpit");
            root.transform.SetParent(carRoot, false);
            eyeLocal = Eye(v);
            float xEye = eyeLocal.x, zEye = eyeLocal.z - 0.05f;
            float inner = v.width - 0.24f;

            var dark = DcAssets.Lit(DcAssets.Hex("#2E3238"), 0.3f);
            var trim = DcAssets.Lit(DcAssets.Hex("#3A3E45"), 0.4f);
            var soft = DcAssets.Lit(DcAssets.Hex("#2C3036"), 0.15f);
            // 11/8 "유로트럭 느낌": 가죽·소프트 촉감 질감(Poly Haven CC0). 없으면 단색
            var wheelMat = DcAssets.Pbr("brown_leather", DcAssets.Hex("#1E2023"), new Color(0.13f, 0.13f, 0.135f), 0.85f);
            var mk = new MeshKit();
            // 대시보드(11/8 밤 다시 만듦, 대표: "저 정도 퀄리티로 직접"): 단면 13점을 폭 방향 25곳에서 이어 붙인 곡면(로프트).
            // 위 판(소프트 촉감) · 앞면(투톤 가죽) · 아래(무광 검정) 세 띠. 가운데 아래는 콘솔 쪽으로 불룩, 양 끝은 문 쪽으로 둥글게 감긴다.
            DashLoft(root.transform, xEye, zEye, inner);
            // 계기판 덮개: 가운데가 평평하고 끝이 둥근 아치(넓은 화면 위 눈부심 가리개)
            HoodLoft(root.transform, xEye, zEye);
            // 11/8 실내 다듬기(특정 차 디자인을 본뜨지 않은 일반적인 요즘 실내): 장식 띠·은은한 조명·송풍구·콘솔·문 안쪽·천장·햇빛 가리개
            Trim(root.transform, xEye, zEye, inner);
            // (11/14 대표: "내부 시점에서 시점 돌아봤을 때 내부 모델링이 덜됐어") 옆·뒤를 돌아보면 보이는 것: 앞뒤 좌석, B·C 필러, 뒷문, 지붕, 뒷유리 틀,
            //   뒤 선반, 바닥, 페달, 칼럼 레버(아반떼 CN8 취급설명서의 실내 그림 배치를 따름. 로고·고유 무늬는 넣지 않음)
            Cabin(root.transform, v, xEye, zEye, inner);
            // 지붕 앞 가로대(앞 유리 위): 눈보다 0.5 m 앞·0.25 m 위 — 11/8 첫 사진에서 머리 바로 위라 화면 위를 크게 가려 옮겼다
            float zTop = zEye + 0.62f;
            mk.Box(new Vector3(0f, 1.42f, zTop), new Vector3(inner, 0.05f, 0.12f), 0f);
            mk.ToObject("interior", dark, root.transform, false, true);
            // A필러(앞 유리 양옆 기둥): 대시보드 끝에서 지붕 앞까지 기울어진 막대 하나(계단 모양 상자 대신)
            var pillars = new MeshKit();
            foreach (var sx in new[] { -1f, 1f })
                TubeLoft(pillars, new Vector3(sx * inner / 2f, 0.93f, zEye + 1.15f), new Vector3(sx * (inner / 2f - 0.06f), 1.43f, zTop), 0.11f, 0.075f, 0.12f, 0.08f);
            pillars.ToObject("a-pillars", DcAssets.Lit(DcAssets.Hex("#77746F"), 0.06f), root.transform, false, true);
            // 보닛(차체는 운전석에서 그리지 않으므로 앞 유리 너머로 보이는 보닛만 따로): 앞으로 갈수록 조금 낮아진다
            var hood = new MeshKit();
            float zHood0 = zEye + 1.22f, zHood1 = v.length / 2f - 0.12f;
            const int steps = 4;
            for (int i = 0; i < steps; i++)
            {
                float a = zHood0 + (zHood1 - zHood0) * i / steps, b = zHood0 + (zHood1 - zHood0) * (i + 1) / steps;
                float y = 0.95f - 0.12f * (i + 0.5f) / steps;
                float wd = (v.width - 0.16f) * (1f - 0.08f * i / steps);
                hood.Box(new Vector3(0f, y, (a + b) / 2f), new Vector3(wd, 0.06f, b - a + 0.01f), 0f);
            }
            // 직접 만든 차(CarBody)는 차체 보닛을 운전석에서도 그대로 보여 주므로 상자 보닛은 만들지 않는다(11/8)
            if (!CarView.HasOwnBody) hood.ToObject("hood", DcAssets.Lit(CarView.Paint, 0.78f, 0.15f), root.transform, false, true);
            // 실내 거울 받침 + 거울 몸통(유광 검정, 거울 판 뒤)
            var stem = new MeshKit();
            stem.Box(new Vector3(0.05f, 1.42f, zEye + 0.66f), new Vector3(0.03f, 0.06f, 0.03f), 0f);
            stem.ToObject("mirror-stem", soft, root.transform);
            var mbody = new MeshKit();
            mbody.Box(new Vector3(0.05f, 1.36f, zEye + 0.657f), new Vector3(0.285f, 0.098f, 0.026f), 8f);
            mbody.ToObject("mirror-body", DcAssets.Lit(DcAssets.Hex("#0A0B0D"), 0.92f), root.transform);

            // 핸들(고리 + 바큇살 3개). 실제 핸들 각을 그대로 돌린다.
            var wheelGo = new GameObject("SteeringWheel");
            wheel = wheelGo.transform;
            wheel.SetParent(root.transform, false);
            // 눈에서 앞 0.6 m·아래 0.26 m(실제 운전 자세 근처). 11/8 첫 사진: 0.4 m 앞이라 화면의 절반을 가렸다
            wheel.localPosition = new Vector3(xEye, WheelY, zEye + 0.62f);
            wheel.localRotation = Quaternion.Euler(WheelTilt, 0f, 0f);   // 운전자 쪽으로 기운 원판(법선이 눈 쪽)
            // 테: 둥근 가죽 고리(11/8 "유로트럭 느낌" — 예전 납작한 띠 세 겹)
            var rim = new MeshKit();
            rim.Torus(Vector3.zero, 0.18f, 0.017f, 48, 10);
            rim.ToObject("rim", wheelMat, wheel, false, true);
            // (11/14) 아반떼(CN8) 취급설명서 그림(cn8_005)의 핸들 배치: 큰 둥근 네모 가운데 패드, 3·9시 넓은 바큇살 위 버튼 묶음(둥근 4방향 단추),
            //   아래는 두 갈래 살을 가로대로 이은 U 모양. 가운데 무늬(로고)는 넣지 않는다
            var ring = new MeshKit();
            foreach (var sx in new[] { -1f, 1f })
                ring.Box(new Vector3(sx * 0.118f, -0.006f, -0.006f), new Vector3(0.10f, 0.018f, 0.052f), 0f);
            foreach (var deg in new[] { 235f, 305f })
            {
                float a = deg * Geom.Deg2Rad;   // 로컬 +Z 가 12시, +X 가 3시
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ring.Box(d * 0.112f + Vector3.down * 0.006f, new Vector3(0.105f, 0.016f, 0.026f), -a * Mathf.Rad2Deg);
            }
            ring.Box(new Vector3(0f, -0.006f, -0.128f), new Vector3(0.18f, 0.016f, 0.022f), 0f);   // U 아래 가로대(두 갈래 살 끝을 잇는다)
            Capsule(ring, new Vector3(-0.042f, -0.012f, 0.004f), new Vector3(0.042f, -0.012f, 0.004f), 0.112f, 0.112f, 0.05f, 0.05f);   // 가운데 에어백 패드
            ring.ToObject("spokes", trim, wheel, false, true);
            // 바큇살 위 버튼판(유광 검정) + 둥근 4방향 단추·작은 버튼(은색)
            var pads = new MeshKit();
            var btns = new MeshKit();
            foreach (var sx in new[] { -1f, 1f })
            {
                pads.Box(new Vector3(sx * 0.118f, 0.004f, -0.006f), new Vector3(0.078f, 0.006f, 0.044f), 0f);
                btns.Torus(new Vector3(sx * 0.105f, 0.0085f, -0.004f), 0.0125f, 0.0025f, 24, 6);
                btns.Box(new Vector3(sx * 0.105f, 0.0085f, -0.004f), new Vector3(0.008f, 0.003f, 0.008f), 0f);
                foreach (var bz in new[] { -0.014f, 0.006f })
                    btns.Box(new Vector3(sx * 0.140f, 0.0085f, bz), new Vector3(0.012f, 0.004f, 0.010f), 0f);
            }
            pads.ToObject("wheel-pads", DcAssets.Lit(DcAssets.Hex("#0A0B0D"), 0.92f), wheel);
            btns.ToObject("wheel-buttons", DcAssets.Lit(DcAssets.Hex("#AEB4BC"), 0.7f, 1f), wheel);
            // 핸들 기둥 덮개(핸들 뒤에서 대시보드로)
            Pillar(root.transform, soft, new Vector3(xEye, 0.875f, zEye + 0.655f), new Vector3(xEye, 0.79f, zEye + 0.77f), 0.075f, 0.09f);
            // 12시 표시(핸들이 얼마나 돌았는지 눈으로 보이게)
            var mark = new MeshKit();
            // (11/11) 가는 줄로: 예전 3.5 cm 네모는 운전석 눈에서 계기판 속도 숫자를 통째로 가렸다
            mark.Line(V2.FromAngle(-90f * Geom.Deg2Rad) * 0.164f, V2.FromAngle(-90f * Geom.Deg2Rad) * 0.196f, 0.012f, 0.02f);
            mark.ToObject("top-mark", DcAssets.Unlit(DcAssets.Orange), wheel);

            // 계기판 + 가운데 화면을 이은 넓은 화면(요즘 준중형 세단에 흔한 배치). 운전자 쪽으로 조금 돌려 둔다.
            var panel = WorldCanvas("Displays", new Vector3(xEye + 0.15f, 1.06f, zEye + 0.80f), new Vector2(0.58f, 0.13f), 0.001f);
            panel.transform.localRotation = Quaternion.Euler(14f, 8f, 0f);
            var bezel = new MeshKit();
            bezel.Box(new Vector3(0f, 0f, 0.009f), new Vector3(0.605f, 0.152f, 0.014f), 0f);
            bezel.Box(new Vector3(0.05f, -0.09f, 0.03f), new Vector3(0.16f, 0.04f, 0.03f), 0f);   // 화면 받침(대시보드 위로)
            var bezelGo = bezel.ToObject("display-bezel", DcAssets.Lit(DcAssets.Hex("#08090B"), 0.9f), root.transform);
            bezelGo.transform.localPosition = panel.transform.localPosition;
            bezelGo.transform.localRotation = panel.transform.localRotation;
            var bg = UiKit.Card(panel.transform, "bezel", DcAssets.Hex("#07080A"));
            UiKit.Stretch(bg.rectTransform, -8, -8, -8, -8);
            // 왼쪽: 계기판(속도·기어·안내)
            var clusterRt = UiKit.Node("cluster", panel.transform);
            clusterRt.anchorMin = new Vector2(0f, 0f); clusterRt.anchorMax = new Vector2(0.47f, 1f);
            clusterRt.offsetMin = clusterRt.offsetMax = Vector2.zero;
            var cbg = clusterRt.gameObject.AddComponent<Image>();
            cbg.color = DcAssets.Hex("#0D1014");
            // 둥근 속도 눈금(아래 90도가 빈 270도 고리): 어두운 눈금판 + 속도만큼 차오르는 파란 고리
            foreach (var fg in new[] { false, true })
            {
                var ri = UiKit.Node(fg ? "speed-arc" : "speed-dial", clusterRt).gameObject.AddComponent<Image>();
                ri.sprite = RingSprite(fg);
                ri.color = fg ? DcAssets.Hex("#4FB2FF") : DcAssets.Hex("#8E99A6");
                ri.raycastTarget = false;
                var rrt = ri.rectTransform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
                rrt.sizeDelta = new Vector2(124f, 124f);
                rrt.anchoredPosition = Vector2.zero;
                if (fg)
                {
                    ri.type = Image.Type.Filled;
                    ri.fillMethod = Image.FillMethod.Radial360;
                    ri.fillOrigin = (int)Image.Origin360.Bottom;
                    ri.fillClockwise = true;
                    ri.fillAmount = 0.125f;
                    speedArc = ri;
                }
            }
            speedText = UiKit.Label(clusterRt, "0", 44, Color.white, true, TextAnchor.MiddleCenter);
            Box(speedText.rectTransform, 0.25f, 0.5f, 0.75f, 1.0f);      // (11/11) 핸들 테 위로 보이는 윗부분에(예전 자리는 테에 가렸다)
            var unit = UiKit.Label(clusterRt, "km/h", 14, DcAssets.Hex("#9AA3AE"), false, TextAnchor.UpperCenter);
            Box(unit.rectTransform, 0.25f, 0.1f, 0.75f, 0.32f);
            gearText = UiKit.Label(clusterRt, "P", 17, DcAssets.Hex("#7FB3FF"), true, TextAnchor.MiddleCenter);
            gearText.supportRichText = true;
            gearText.lineSpacing = 0.85f;
            Box(gearText.rectTransform, 0.8f, 0.08f, 0.98f, 0.92f);
            infoText = UiKit.Label(clusterRt, "", 15, DcAssets.Hex("#C9D1DA"), false, TextAnchor.MiddleCenter);
            infoText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Box(infoText.rectTransform, 0.0f, 0.2f, 0.26f, 0.95f);
            // 오른쪽: 가운데 화면(지도, 후진이면 후방 카메라)
            var scr = UiKit.Node("center", panel.transform);
            scr.anchorMin = new Vector2(0.5f, 0f); scr.anchorMax = new Vector2(1f, 1f);
            scr.offsetMin = scr.offsetMax = Vector2.zero;
            centerScreen = scr.gameObject.AddComponent<RawImage>();

            // 거울 판: 거울 카메라 그림을 좌우 뒤집어 붙인다. 판은 운전자 눈 쪽을 보게(월드 캔버스는 -Z 쪽에서 읽힌다)
            if (CarView.HasOwnBody)
            {
                // 직접 만든 차: 차체 사이드미러 몸통(운전석에서도 보임)의 거울 면 바로 앞(운전자 쪽 4 cm)에 맞춘다(11/8)
                float mx = 0.948f * v.width / 1.855f, mzq = 0.966f + (v.length / 2f - v.frontOh) - 1.4675f;   // 앞축과 함께 움직인다
                // (11/13 대표: "차에 붙어 있는 거울 모양이 다르다") 거울 판을 눈 쪽으로 돌려 4 cm 띄워 두어, 몸통 밖으로 비어져 나온 검은 테 네모로 보였다
                //   → 몸통의 거울 면(뒤를 보는 평평한 면)에 그대로 붙이고(5 mm 앞), 테 없이 거울 면 안에 들어가는 크기로
                mzq += 0.034f;
                MirrorQuad("MirrorL", new Vector3(-mx, 1.052f, mzq), new Vector2(0.150f, 0.088f), 0f, mirrors.texL, false);
                MirrorQuad("MirrorR", new Vector3(mx, 1.052f, mzq), new Vector2(0.150f, 0.088f), 0f, mirrors.texR, false);
            }
            else
            {
                MirrorQuad("MirrorL", new Vector3(-(v.width / 2f + 0.1f), 1.05f, zEye + 0.72f), new Vector2(0.20f, 0.117f), -22f, mirrors.texL);
                MirrorQuad("MirrorR", new Vector3(v.width / 2f + 0.1f, 1.05f, zEye + 0.72f), new Vector2(0.20f, 0.117f), 30f, mirrors.texR);
            }
            MirrorQuad("MirrorC", new Vector3(0.05f, 1.36f, zEye + 0.64f), new Vector2(0.26f, 0.082f), 8f, mirrors.texC);
            // 실내 채움 빛(11/8 첫 사진: 지붕 그늘에 실내가 너무 어두움). 그림자 없이 실내만 닿는 거리(1.7 m)로 은은하게
            var fill = new GameObject("cabin-fill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(0f, 1.32f, zEye + 0.35f);
            fill.type = LightType.Point;
            fill.range = 1.7f;
            fill.intensity = 0.9f;
            fill.color = new Color(1f, 0.96f, 0.9f);
            fill.shadows = LightShadows.None;
            root.SetActive(false);
        }

        /// <summary>
        /// 실내 장식(일반적인 요즘 준중형 실내 — 특정 차를 본뜨지 않음, 상표 없음).
        /// 대시보드 앞면: 은색 띠 + 은은한 파란 조명 줄 + 송풍구(가운데 둘·운전석 끝·조수석 가로로 긴 것) + 공조 버튼판.
        /// 가운데 콘솔: 소프트 촉감 몸통, 유광 검정 윗판, 수납함, 무선 충전판, 컵홀더, 팔걸이, 옆 조명 줄.
        /// 문 안쪽: 윗부분·장식 띠·조명 줄·가죽 덧판·팔걸이·창 스위치판·손잡이·사이드 거울 안쪽 판.
        /// 천장·햇빛 가리개.
        /// </summary>
        static void Trim(Transform root, float xEye, float zEye, float inner)
        {
            var piano = new MeshKit();
            var chrome = new MeshKit();
            var satin = new MeshKit();
            var softTouch = new MeshKit();
            var insert = new MeshKit();
            var fabric = new MeshKit();
            var glow = new MeshKit();
            var hole = new MeshKit();
            float face = zEye + 0.712f;   // 대시보드 앞면(세로 부분, 높이 0.80~0.90)
            // 대시보드 띠·조명
            satin.Box(new Vector3(0f, 0.893f, face - 0.004f), new Vector3(inner - 0.06f, 0.014f, 0.018f), 0f);
            glow.Box(new Vector3(0f, 0.881f, face - 0.003f), new Vector3(inner - 0.32f, 0.004f, 0.012f), 0f);
            // 송풍구: 안쪽 어두운 판 + 유광 검정 테 + 가로 날개
            void Vent(float cx, float cy, float w, float h)
            {
                hole.Box(new Vector3(cx, cy, face + 0.002f), new Vector3(w, h, 0.012f), 0f);
                piano.Box(new Vector3(cx, cy + h / 2f, face - 0.006f), new Vector3(w + 0.012f, 0.006f, 0.02f), 0f);
                piano.Box(new Vector3(cx, cy - h / 2f, face - 0.006f), new Vector3(w + 0.012f, 0.006f, 0.02f), 0f);
                piano.Box(new Vector3(cx - w / 2f, cy, face - 0.006f), new Vector3(0.006f, h, 0.02f), 0f);
                piano.Box(new Vector3(cx + w / 2f, cy, face - 0.006f), new Vector3(0.006f, h, 0.02f), 0f);
                int n = Mathf.Max(2, Mathf.RoundToInt(h / 0.012f));
                for (int i = 1; i < n; i++)
                    satin.Box(new Vector3(cx, cy - h / 2f + h * i / n, face - 0.004f), new Vector3(w - 0.004f, 0.0025f, 0.016f), 0f);
            }
            Vent(-0.085f, 0.842f, 0.12f, 0.05f);
            Vent(0.085f, 0.842f, 0.12f, 0.05f);
            Vent(-inner / 2f + 0.11f, 0.842f, 0.11f, 0.05f);
            float pv0 = 0.21f, pv1 = inner / 2f - 0.07f;
            Vent((pv0 + pv1) / 2f, 0.85f, pv1 - pv0, 0.028f);
            // 공조 버튼판(가운데 송풍구 아래)
            piano.Box(new Vector3(0f, 0.765f, face + 0.002f), new Vector3(0.30f, 0.06f, 0.012f), 0f);
            for (int i = 0; i < 6; i++)
                satin.Box(new Vector3(-0.1f + i * 0.04f, 0.765f, face - 0.005f), new Vector3(0.026f, 0.01f, 0.004f), 0f);
            // 가운데 콘솔
            softTouch.Box(new Vector3(0f, 0.48f, zEye + 0.30f), new Vector3(0.26f, 0.26f, 0.9f), 0f);
            piano.Box(new Vector3(0f, 0.615f, zEye + 0.33f), new Vector3(0.24f, 0.012f, 0.62f), 0f);
            // (11/14) 아반떼(CN8)는 변속 레버가 스티어링 휠 오른쪽 칼럼에 있다(취급설명서) → 콘솔의 다이얼 대신 작은 수납함(Cabin 에 칼럼 레버)
            hole.Box(new Vector3(0f, 0.6215f, zEye + 0.24f), new Vector3(0.15f, 0.002f, 0.11f), 0f);
            hole.Box(new Vector3(0f, 0.623f, zEye + 0.50f), new Vector3(0.16f, 0.005f, 0.12f), 0f);   // 무선 충전판
            foreach (var cx in new[] { -0.055f, 0.055f })
            {
                chrome.Torus(new Vector3(cx, 0.622f, zEye + 0.04f), 0.034f, 0.0035f, 28, 6);   // 컵홀더
                hole.Box(new Vector3(cx, 0.6215f, zEye + 0.04f), new Vector3(0.058f, 0.002f, 0.058f), 0f);
            }
            softTouch.Box(new Vector3(0f, 0.67f, zEye - 0.32f), new Vector3(0.22f, 0.06f, 0.4f), 0f);   // 팔걸이
            foreach (var sx in new[] { -1f, 1f })
                glow.Box(new Vector3(sx * 0.1315f, 0.585f, zEye + 0.3f), new Vector3(0.003f, 0.004f, 0.8f), 0f);
            // 문 안쪽(양쪽). (11/14) 앞문은 B 필러(눈 뒤 0.36 m)에서 끝난다 — 그 뒤는 뒷문(Cabin)
            float zB = zEye - 0.36f;
            foreach (var sx in new[] { -1f, 1f })
            {
                float x(float inset) => sx * (inner / 2f - inset);
                // 문 윗부분(창틀 아래 둥근 턱): 로프트
                var sill = new List<Vector3[]>();
                for (int i = 0; i <= 12; i++)
                {
                    float z = zB + (zEye + 0.97f - zB) * i / 12f;
                    float drop = Mathf.SmoothStep(0f, 1f, (z - (zEye + 0.70f)) / 0.25f) * 0.03f;   // 앞쪽은 대시보드로 내려간다
                    float[] px = { 0.02f, -0.02f, -0.055f, -0.08f, -0.09f, -0.085f };
                    float[] py = { 0.99f, 1.0f, 0.988f, 0.962f, 0.925f, 0.885f };
                    var sec = new Vector3[px.Length];
                    for (int k = 0; k < px.Length; k++) sec[k] = new Vector3(sx * (inner / 2f + px[k]), py[k] - drop, z);
                    sill.Add(sec);
                }
                softTouch.Loft(sill, sx > 0f, 0.4f);
                float zf = zEye + 0.85f, zi = zEye + 0.75f, zl = zEye + 0.95f;
                chrome.Box(new Vector3(x(0.0805f), 0.889f, (zB + 0.04f + zf) / 2f), new Vector3(0.004f, 0.01f, zf - zB - 0.04f), 0f);
                glow.Box(new Vector3(x(0.079f), 0.879f, (zB + 0.1f + zf) / 2f), new Vector3(0.003f, 0.004f, zf - zB - 0.1f), 0f);
                insert.Box(new Vector3(x(0.045f), 0.79f, (zB + 0.06f + zi) / 2f), new Vector3(0.07f, 0.17f, zi - zB - 0.06f), 0f);
                softTouch.Box(new Vector3(x(0.035f), 0.5f, (zB + 0.02f + zl) / 2f), new Vector3(0.07f, 0.4f, zl - zB - 0.02f), 0f);
                softTouch.Box(new Vector3(x(0.11f), 0.69f, zEye - 0.03f), new Vector3(0.08f, 0.05f, 0.5f), 0f);
                piano.Box(new Vector3(x(0.11f), 0.717f, zEye + 0.12f), new Vector3(0.07f, 0.008f, 0.16f), 0f);
                foreach (var bz in new[] { 0.08f, 0.15f })
                    satin.Box(new Vector3(x(0.11f), 0.723f, zEye + bz), new Vector3(0.022f, 0.006f, 0.03f), 0f);
                chrome.Box(new Vector3(x(0.088f), 0.845f, zEye + 0.33f), new Vector3(0.014f, 0.022f, 0.11f), 0f);
                piano.Box(new Vector3(x(0.03f), 0.99f, zEye + 0.86f), new Vector3(0.05f, 0.08f, 0.2f), 0f);
                satin.Box(new Vector3(x(0.071f), 0.42f, zEye + 0.3f), new Vector3(0.004f, 0.006f, 0.8f), 0f);
            }
            // 천장·햇빛 가리개(접어 올린 것)
            fabric.Box(new Vector3(0f, 1.462f, zEye - 0.25f), new Vector3(inner, 0.02f, 1.7f), 0f);
            fabric.Box(new Vector3(xEye + 0.02f, 1.432f, zEye + 0.47f), new Vector3(0.40f, 0.022f, 0.16f), 0f);
            fabric.Box(new Vector3(-xEye - 0.02f, 1.432f, zEye + 0.47f), new Vector3(0.40f, 0.022f, 0.16f), 0f);
            piano.ToObject("trim-piano", DcAssets.Lit(DcAssets.Hex("#0A0B0D"), 0.92f), root);
            chrome.ToObject("trim-chrome", DcAssets.Lit(DcAssets.Hex("#C8CDD4"), 0.82f, 1f), root);
            satin.ToObject("trim-satin", DcAssets.Lit(DcAssets.Hex("#7D838B"), 0.5f, 1f), root);
            softTouch.ToObject("trim-soft", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#34373C"), new Color(0.27f, 0.28f, 0.3f), 0.45f), root);
            insert.ToObject("trim-insert", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#5A5650"), new Color(0.5f, 0.48f, 0.45f), 0.35f), root);
            fabric.ToObject("headliner", DcAssets.Lit(DcAssets.Hex("#7E7B76"), 0.05f), root);
            glow.ToObject("ambient-light", DcAssets.Unlit(DcAssets.Hex("#6CC4FF")), root);
            hole.ToObject("trim-dark", DcAssets.Lit(DcAssets.Hex("#050506"), 0.1f), root);
        }

        /// <summary>
        /// (11/14) 실내 나머지: 앞뒤 좌석(운전석은 머리받침·등받이 윗부분을 뺀다 — 눈 바로 뒤라 돌아보면 화면을 덮는다), B·C 필러, 지붕 옆·뒤 가로대,
        /// 뒷문 안쪽, 뒤 선반, 바닥·발판, 페달, 콘솔 뒤쪽, 안전띠, 칼럼 레버(오른쪽 = 변속, 왼쪽 = 등·방향지시등)와 시동 버튼.
        /// 크기는 준중형 세단(아반떼 CN8: 전장 4.765 m·축거 2.75 m) 실내 근사. 모든 자리는 눈(운전석) 기준.
        /// </summary>
        /// <summary>뒷좌석 층(실내 백미러 카메라가 그리지 않는다)</summary>
        public const int RearCabinLayer = 28;

        static void Cabin(Transform root, Vehicle v, float xEye, float zEye, float inner)
        {
            var seat = new MeshKit();
            var seatIn = new MeshKit();
            var dark = new MeshKit();
            var trimS = new MeshKit();
            var trimI = new MeshKit();
            var pillar = new MeshKit();
            var carpet = new MeshKit();
            var metal = new MeshKit();
            var piano = new MeshKit();
            var belt = new MeshKit();
            var seatR = new MeshKit();
            var seatInR = new MeshKit();
            var darkR = new MeshKit();
            float half = inner / 2f;
            float zB = zEye - 0.36f;              // B 필러 가운데(앞문 끝)
            float zC = zEye - 1.17f;              // 뒷문 끝(C 필러 아래 앞)
            float zW = zEye - 1.64f;              // 뒷유리 아래(뒤 선반 뒤 끝)
            float zR = zEye - 1.07f;              // 지붕 뒤 끝(뒷유리 위)
            const float yBelt = 0.99f, yRoof = 1.42f, yFloor = 0.27f;

            // ── 좌석 ──
            float zH = zEye + 0.17f, yH = 0.50f;  // 앞좌석 엉덩이 자리
            foreach (var sx in new[] { -1f, 1f })
            {
                bool driver = sx < 0f;
                float xs = sx * Mathf.Abs(xEye);
                Seat(seat, seatIn, dark, xs, zH, yH, 22f, 0.52f, driver ? 0.86f : 1.16f, !driver);
            }
            // 뒷좌석(3인): 엉덩이 자리 앞좌석보다 0.9 m 뒤
            float zH2 = zH - 0.92f;
            foreach (var (xs, wd, head) in new[] { (-0.44f, 0.44f, true), (0f, 0.38f, true), (0.44f, 0.44f, true) })
                Seat(seatR, seatInR, darkR, xs, zH2, yH + 0.02f, 25f, wd, 1.12f, head, xs == 0f ? 0.9f : 1f);
            // 뒷좌석 등받이 뒤 칸막이(좌석 사이·아래로 트렁크가 비쳐 보이지 않게)
            {
                float tan2 = Mathf.Tan(25f * Mathf.Deg2Rad), yb0 = yH + 0.06f, yb1 = 1.05f, zb0 = zH2 - 0.16f - 0.085f;
                var wall = new List<Vector3[]>();
                foreach (var t in new[] { 0f, 1f })
                {
                    float y = Mathf.Lerp(yb0, yb1, t), z = zb0 - (y - (yH + 0.06f)) * tan2;
                    wall.Add(new[] { new Vector3(-0.68f, y, z), new Vector3(0.68f, y, z) });
                }
                darkR.Loft(wall, false, 0.5f);
                darkR.Loft(wall, true, 0.5f);
            }
            // ── 바닥·발판·가운데 턱 ──
            carpet.Box(new Vector3(0f, yFloor, (zW + 0.3f + zEye + 0.85f) / 2f), new Vector3(inner, 0.02f, zEye + 0.85f - zW - 0.3f), 0f);
            var toe = new List<Vector3[]>();
            foreach (var t in new[] { 0f, 1f })
                toe.Add(new[] { new Vector3(-half, Mathf.Lerp(yFloor, 0.62f, t), zEye + 0.84f + 0.28f * t), new Vector3(half, Mathf.Lerp(yFloor, 0.62f, t), zEye + 0.84f + 0.28f * t) });
            carpet.Loft(toe, true, 0.5f);
            carpet.Box(new Vector3(0f, yFloor + 0.05f, (zH2 + zB) / 2f), new Vector3(0.26f, 0.08f, zB - zH2 + 0.2f), 0f);   // 뒷자리 가운데 턱
            // 페달(운전석 발밑): 브레이크(넓은 판)·가속(세운 판)
            belt.Box(new Vector3(xEye + 0.05f, 0.40f, zEye + 0.86f), new Vector3(0.11f, 0.07f, 0.025f), 0f);
            belt.Box(new Vector3(xEye + 0.24f, 0.37f, zEye + 0.92f), new Vector3(0.06f, 0.17f, 0.02f), 0f);
            dark.Box(new Vector3(xEye + 0.05f, 0.47f, zEye + 0.92f), new Vector3(0.02f, 0.14f, 0.02f), 0f);
            // ── 콘솔 뒤쪽(팔걸이 밑)·뒷자리 송풍구 ──
            trimS.Box(new Vector3(0f, 0.46f, zEye - 0.335f), new Vector3(0.24f, 0.36f, 0.37f), 0f);
            foreach (var vx in new[] { -0.045f, 0.045f })
                piano.Box(new Vector3(vx, 0.55f, zEye - 0.522f), new Vector3(0.07f, 0.035f, 0.008f), 0f);
            metal.Box(new Vector3(0f, 0.47f, zEye - 0.522f), new Vector3(0.05f, 0.012f, 0.006f), 0f);
            // ── 기둥·지붕 ──
            foreach (var sx in new[] { -1f, 1f })
            {
                // B 필러: 창 아래(문)에서 지붕까지, 위로 갈수록 안쪽으로(차 옆면이 위로 좁아짐)
                TubeLoft(pillar, new Vector3(sx * (half - 0.02f), yBelt - 0.02f, zB), new Vector3(sx * (half - 0.09f), yRoof - 0.01f, zB - 0.02f), 0.13f, 0.11f, 0.08f, 0.07f);
                // 안전띠(B 필러 안쪽에 걸린 띠)
                belt.Box(new Vector3(sx * (half - 0.075f), 0.98f, zB + 0.065f), new Vector3(0.008f, 0.62f, 0.042f), 0f);
                metal.Box(new Vector3(sx * (half - 0.08f), 1.30f, zB + 0.06f), new Vector3(0.02f, 0.05f, 0.04f), 0f);
                // 지붕 옆 가로대(앞 유리 위 ~ 뒷유리 위)
                pillar.Box(new Vector3(sx * (half - 0.07f), yRoof + 0.005f, (zR + zEye + 0.6f) / 2f), new Vector3(0.12f, 0.05f, zEye + 0.6f - zR), 0f);
                // 손잡이(조수석·뒷문 위)
                if (sx > 0f) dark.Box(new Vector3(sx * (half - 0.1f), yRoof - 0.04f, zEye + 0.1f), new Vector3(0.025f, 0.03f, 0.22f), 0f);
                dark.Box(new Vector3(sx * (half - 0.1f), yRoof - 0.04f, zB - 0.45f), new Vector3(0.025f, 0.03f, 0.22f), 0f);
                // C 필러: 뒷문 창 뒤 끝(옆, 문 위)에서 뒷유리 옆 끝(안쪽)까지 이어진 넓은 판(높이 따라 단면 3점)
                var cp = new List<Vector3[]>();
                for (int i = 0; i <= 6; i++)
                {
                    float t = i / 6f;
                    float y = Mathf.Lerp(yBelt + 0.02f, yRoof - 0.01f, t);
                    var a = new Vector3(sx * Mathf.Lerp(half - 0.02f, half - 0.09f, t), y, Mathf.Lerp(zC, zR + 0.04f, t));          // 뒷문 창 뒤 끝
                    var b = new Vector3(sx * Mathf.Lerp(half - 0.27f, half - 0.22f, t), y, Mathf.Lerp(zW + 0.02f, zR, t));         // 뒷유리 옆 끝(뒷유리 폭 약 1.1 m)
                    var m = Vector3.Lerp(a, b, 0.5f) + new Vector3(-sx * 0.025f, 0f, 0f);
                    cp.Add(new[] { a, m, b });
                }
                pillar.Loft(cp, sx > 0f, 0.5f);
                pillar.Loft(cp, sx < 0f, 0.5f);
                // 뒷문 안쪽: 창 아래 둥근 턱 + 덧판 + 팔걸이 + 손잡이 + 창 스위치
                var sill = new List<Vector3[]>();
                for (int i = 0; i <= 8; i++)
                {
                    float z = Mathf.Lerp(zB - 0.06f, zC, i / 8f);
                    float[] px = { 0.02f, -0.02f, -0.055f, -0.08f, -0.09f, -0.085f };
                    float[] py = { 0.99f, 1.0f, 0.988f, 0.962f, 0.925f, 0.885f };
                    var sec = new Vector3[px.Length];
                    for (int k = 0; k < px.Length; k++) sec[k] = new Vector3(sx * (half + px[k]), py[k], z);
                    sill.Add(sec);
                }
                trimS.Loft(sill, sx > 0f, 0.4f);
                float zd = (zB - 0.06f + zC) / 2f, ld = zB - 0.06f - zC;
                trimI.Box(new Vector3(sx * (half - 0.045f), 0.79f, zd), new Vector3(0.07f, 0.17f, ld - 0.08f), 0f);
                trimS.Box(new Vector3(sx * (half - 0.035f), 0.5f, zd), new Vector3(0.07f, 0.4f, ld), 0f);
                trimS.Box(new Vector3(sx * (half - 0.10f), 0.68f, zd - 0.05f), new Vector3(0.07f, 0.05f, 0.42f), 0f);
                piano.Box(new Vector3(sx * (half - 0.10f), 0.706f, zd + 0.08f), new Vector3(0.06f, 0.008f, 0.08f), 0f);
                metal.Box(new Vector3(sx * (half - 0.088f), 0.845f, zB - 0.20f), new Vector3(0.014f, 0.022f, 0.10f), 0f);
                // 뒷자리 옆(바퀴집 위) 덧판: 뒷문 뒤 ~ 뒷유리 아래
                trimS.Box(new Vector3(sx * (half - 0.06f), 0.72f, (zC + zW) / 2f), new Vector3(0.12f, 0.55f, zC - zW), 0f);
            }
            // 지붕 뒤 가로대(뒷유리 위)와 뒤쪽 천장
            pillar.Box(new Vector3(0f, yRoof + 0.005f, zR + 0.03f), new Vector3(inner - 0.16f, 0.06f, 0.1f), 0f);
            pillar.Box(new Vector3(0f, 1.455f, (zR + zEye - 1.1f) / 2f), new Vector3(inner - 0.1f, 0.02f, zEye - 1.1f - zR + 0.02f), 0f);
            // 뒤 선반(뒷좌석 등받이 뒤 ~ 뒷유리 아래)과 스피커 그릴
            darkR.Box(new Vector3(0f, 1.04f, (zW + zH2 - 0.55f) / 2f), new Vector3(inner - 0.3f, 0.025f, zH2 - 0.55f - zW), 0f);
            foreach (var gx in new[] { -0.45f, 0.45f })
                piano.Box(new Vector3(gx, 1.055f, (zW + zH2 - 0.55f) / 2f), new Vector3(0.18f, 0.006f, 0.12f), 0f);
            // ── 칼럼 레버·시동 버튼(아반떼 CN8: 오른쪽 레버 끝을 돌려 R·N·D, 끝 단추 P / 왼쪽 레버 등·방향지시등) ──
            foreach (var sx in new[] { -1f, 1f })
            {
                var a = new Vector3(xEye + sx * 0.05f, 0.865f, zEye + 0.705f);
                var b = new Vector3(xEye + sx * 0.20f, 0.875f, zEye + 0.675f);
                TubeLoft(dark, a, b, 0.03f, 0.036f, 0.026f, 0.032f);
                if (sx > 0f)
                {
                    TubeLoft(metal, b, b + new Vector3(0.045f, 0.003f, -0.008f), 0.044f, 0.044f, 0.044f, 0.044f);   // 돌리는 손잡이
                    piano.Box(b + new Vector3(0.048f, 0.003f, -0.009f), new Vector3(0.006f, 0.03f, 0.03f), 0f);      // P 단추
                    piano.Box(Vector3.Lerp(a, b, 0.55f) + new Vector3(0f, 0.018f, -0.004f), new Vector3(0.07f, 0.004f, 0.024f), 0f);   // R·N·D 표시 창
                }
            }
            metal.Box(new Vector3(xEye + 0.27f, 0.795f, zEye + 0.718f), new Vector3(0.045f, 0.045f, 0.012f), 0f);    // 시동 버튼
            piano.Box(new Vector3(xEye + 0.27f, 0.795f, zEye + 0.711f), new Vector3(0.034f, 0.034f, 0.004f), 0f);

            var seatMat = DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#2C2F34"), new Color(0.25f, 0.26f, 0.28f), 0.4f);
            seat.ToObject("seats", seatMat, root, false, true);
            seatIn.ToObject("seat-insert", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#4B4E54"), new Color(0.42f, 0.43f, 0.45f), 0.35f), root, false, true);
            dark.ToObject("cabin-dark", DcAssets.Lit(DcAssets.Hex("#1B1D20"), 0.3f), root);
            belt.ToObject("seat-belts", DcAssets.Lit(DcAssets.Hex("#3A3D42"), 0.2f), root);
            // 뒷좌석·칸막이·뒤 선반은 실내 백미러 카메라에 찍히지 않는 층에 둔다(머리받침이 거울 아래 절반을 가렸다 — 연습용 거울은 뒤가 잘 보이게)
            foreach (var go in new[]
            {
                seatR.ToObject("rear-seats", seatMat, root, false, true),
                seatInR.ToObject("rear-seat-insert", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#4B4E54"), new Color(0.42f, 0.43f, 0.45f), 0.35f), root, false, true),
                darkR.ToObject("rear-dark", DcAssets.Lit(DcAssets.Hex("#1B1D20"), 0.3f), root),
            })
                go.layer = RearCabinLayer;
            trimS.ToObject("rear-trim-soft", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#34373C"), new Color(0.27f, 0.28f, 0.3f), 0.45f), root);
            trimI.ToObject("rear-trim-insert", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#5A5650"), new Color(0.5f, 0.48f, 0.45f), 0.35f), root);
            pillar.ToObject("pillars-roof", DcAssets.Lit(DcAssets.Hex("#7E7B76"), 0.05f), root);
            carpet.ToObject("carpet", DcAssets.Lit(DcAssets.Hex("#202226"), 0.02f), root);
            metal.ToObject("cabin-metal", DcAssets.Lit(DcAssets.Hex("#9EA4AC"), 0.6f, 0.9f), root);
            piano.ToObject("cabin-piano", DcAssets.Lit(DcAssets.Hex("#0A0B0D"), 0.92f), root);
            // 뒷자리 채움 빛(앞쪽 빛은 1.7 m 까지만 닿아 뒤가 너무 어두움)
            var fill = new GameObject("cabin-fill-rear").AddComponent<Light>();
            fill.transform.SetParent(root, false);
            fill.transform.localPosition = new Vector3(0f, 1.3f, zH2 + 0.2f);
            fill.type = LightType.Point;
            fill.range = 1.6f;
            fill.intensity = 0.6f;
            fill.color = new Color(1f, 0.96f, 0.9f);
            fill.shadows = LightShadows.None;
        }

        /// <summary>
        /// 좌석 하나: 방석·등받이(둥근 사각 관)·옆 받침·가운데 덧판·머리받침·바닥 받침.
        /// x, zH, yH: 엉덩이 자리. recline: 등받이 기울기(도). backTop: 등받이 꼭대기 높이(운전석은 낮게 — 눈 바로 뒤라 돌아보면 화면을 덮는다).
        /// </summary>
        static void Seat(MeshKit body, MeshKit insert, MeshKit dark, float x, float zH, float yH, float recline, float w, float backTop, bool head, float headK = 1f)
        {
            float tan = Mathf.Tan(recline * Mathf.Deg2Rad);
            // 방석(앞으로 조금 들림)
            Capsule(body, new Vector3(x, yH, zH - 0.12f), new Vector3(x, yH + 0.03f, zH + 0.34f), w, w - 0.02f, 0.13f, 0.12f);
            Capsule(insert, new Vector3(x, yH + 0.062f, zH - 0.06f), new Vector3(x, yH + 0.088f, zH + 0.28f), w * 0.55f, w * 0.55f, 0.012f, 0.012f);
            foreach (var bs in new[] { -1f, 1f })
                Capsule(body, new Vector3(x + bs * (w / 2f - 0.04f), yH + 0.04f, zH - 0.08f), new Vector3(x + bs * (w / 2f - 0.04f), yH + 0.06f, zH + 0.26f), 0.08f, 0.07f, 0.09f, 0.08f);
            // 등받이
            float y0 = yH + 0.04f, z0 = zH - 0.16f;
            float y1 = backTop, z1 = z0 - (y1 - y0) * tan;
            Capsule(body, new Vector3(x, y0, z0), new Vector3(x, y1, z1), w, w - 0.05f, 0.15f, 0.12f);
            var nf = new Vector3(0f, tan, 1f).normalized * 0.065f;       // 등받이 앞면 쪽
            Capsule(insert, new Vector3(x, y0 + 0.08f, z0 - 0.08f * tan) + nf, new Vector3(x, y1 - 0.08f, z1 + 0.08f * tan) + nf, w * 0.5f, w * 0.48f, 0.012f, 0.012f);
            foreach (var bs in new[] { -1f, 1f })
                Capsule(body, new Vector3(x + bs * (w / 2f - 0.035f), y0 + 0.06f, z0 + 0.03f), new Vector3(x + bs * (w / 2f - 0.045f), Mathf.Min(y1, y0 + 0.5f), z0 + 0.03f - (Mathf.Min(y1, y0 + 0.5f) - y0) * tan),
                    0.07f, 0.06f, 0.17f, 0.13f);
            // 머리받침(기둥 2개)
            if (head)
            {
                float hy0 = y1 + 0.05f, hy1 = y1 + 0.05f + 0.16f * headK;
                Capsule(body, new Vector3(x, hy0, z1 - 0.01f), new Vector3(x, hy1, z1 - 0.01f - 0.16f * headK * tan * 0.5f), 0.26f, 0.25f, 0.10f, 0.09f);
                foreach (var px in new[] { -0.06f, 0.06f })
                    dark.Box(new Vector3(x + px, y1 + 0.02f, z1 - 0.01f), new Vector3(0.012f, 0.08f, 0.012f), 0f);
            }
            // 바닥 받침(레일)
            dark.Box(new Vector3(x, (0.28f + yH - 0.06f) / 2f, zH + 0.08f), new Vector3(w - 0.12f, yH - 0.06f - 0.28f, 0.42f), 0f);
        }

        /// <summary>a → b 둥근 사각 단면 관에 양 끝을 둥글게 막은 것(좌석 덩어리). 단면 폭(옆, x 쪽)·두께가 w0→w1·d0→d1 로 바뀐다.</summary>
        static void Capsule(MeshKit mk, Vector3 a, Vector3 b, float w0, float w1, float d0, float d1)
        {
            var axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            var t = axis / len;
            var n1 = Vector3.right - t * Vector3.Dot(Vector3.right, t);
            if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.forward - t * Vector3.Dot(Vector3.forward, t);
            n1.Normalize();
            var n2 = Vector3.Cross(t, n1).normalized;
            float cap = Mathf.Min(Mathf.Min(w0, w1), Mathf.Min(d0, d1)) * 0.5f;
            const int M = 14;
            var us = new List<float>();
            for (int i = 0; i <= 3; i++) us.Add(-cap + cap * Mathf.Sin(i / 3f * Mathf.PI / 2f));
            for (int i = 1; i <= 3; i++) us.Add(len * i / 4f);
            for (int i = 1; i <= 3; i++) us.Add(len + cap * Mathf.Sin(i / 3f * Mathf.PI / 2f));
            var secs = new List<Vector3[]>();
            foreach (var u in us)
            {
                float f = Mathf.Clamp01(u / len);
                float k = u < 0f ? Mathf.Sqrt(Mathf.Max(0f, 1f - (u / cap) * (u / cap))) : (u > len ? Mathf.Sqrt(Mathf.Max(0f, 1f - ((u - len) / cap) * ((u - len) / cap))) : 1f);
                k = Mathf.Max(k, 0.02f);
                var c = a + t * u;
                float hw = Mathf.Lerp(w0, w1, f) / 2f * k, hd = Mathf.Lerp(d0, d1, f) / 2f * k;
                var sec = new Vector3[M + 1];
                for (int j = 0; j <= M; j++)
                {
                    float th = j / (float)M * Mathf.PI * 2f;
                    float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                    float ex = Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.45f), ey = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.45f);
                    sec[j] = c + n1 * (ex * hw) + n2 * (ey * hd);
                }
                secs.Add(sec);
            }
            var du = secs[secs.Count / 2 + 1][0] - secs[secs.Count / 2 - 1][0];
            var dv = secs[secs.Count / 2][1] - secs[secs.Count / 2][M - 1];
            bool flip = Vector3.Dot(Vector3.Cross(du, dv), secs[secs.Count / 2][0] - (a + t * us[secs.Count / 2])) < 0f;
            mk.Loft(secs, flip, 0.3f);
        }

        /// <summary>대시보드 본체: 폭 방향 25 단면 로프트, 세 띠(위 판·앞면·아래)로 재질을 나눈다</summary>
        static void DashLoft(Transform root, float xEye, float zEye, float inner)
        {
            // (앞뒤 z: 눈 기준, 높이 y) — 앞 유리 밑 → 위 판 → 둥근 턱 → 앞면 → 아래
            float[] pz = { 1.30f, 1.14f, 1.00f, 0.90f, 0.83f, 0.785f, 0.750f, 0.728f, 0.718f, 0.718f, 0.735f, 0.770f, 0.820f };
            float[] py = { 0.932f, 0.952f, 0.970f, 0.980f, 0.975f, 0.960f, 0.938f, 0.905f, 0.860f, 0.800f, 0.730f, 0.650f, 0.560f };
            int[][] bands = { new[] { 0, 5 }, new[] { 5, 10 }, new[] { 10, 12 } };
            float half = inner / 2f;
            const int N = 24;
            var secs = new List<Vector3[]>();
            for (int i = 0; i <= N; i++)
            {
                float x = -half + inner * i / N;
                float center = 1f - Mathf.SmoothStep(0.12f, 0.24f, Mathf.Abs(x));            // 가운데(콘솔 쪽)
                float end = Mathf.SmoothStep(half - 0.14f, half, Mathf.Abs(x));             // 양 끝(문 쪽)
                float driver = 1f - Mathf.SmoothStep(0.18f, 0.32f, Mathf.Abs(x - xEye));    // 운전석 앞(핸들 기둥 자리)
                var sec = new Vector3[pz.Length];
                for (int k = 0; k < pz.Length; k++)
                {
                    float z = pz[k], y = py[k];
                    if (k >= 11) z -= center * (k == 11 ? 0.06f : 0.10f);   // 가운데 아래는 콘솔 쪽으로 불룩
                    if (k >= 6) z += end * 0.05f;                           // 끝은 앞으로 둥글게 감긴다
                    if (k >= 7 && k <= 10) z += driver * 0.012f;            // 운전석 앞 앞면은 조금 들어간다
                    if (k <= 4) y -= end * 0.012f;
                    sec[k] = new Vector3(x, y, zEye + z);
                }
                secs.Add(sec);
            }
            var mats = new[]
            {
                DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#2A2D31"), new Color(0.23f, 0.24f, 0.26f), 0.4f),   // 위 판: 소프트 촉감
                DcAssets.Pbr("brown_leather", DcAssets.Hex("#56585C"), new Color(0.50f, 0.49f, 0.48f), 0.5f),      // 앞면: 투톤 가죽(밝은 회색)
                DcAssets.Lit(DcAssets.Hex("#1D2023"), 0.25f),                                                     // 아래: 무광 검정
            };
            string[] names = { "dash-top", "dash-face", "dash-lower" };
            for (int bi = 0; bi < bands.Length; bi++)
            {
                var part = new List<Vector3[]>();
                foreach (var sec in secs)
                {
                    var p = new Vector3[bands[bi][1] - bands[bi][0] + 1];
                    for (int k = 0; k < p.Length; k++) p[k] = sec[bands[bi][0] + k];
                    part.Add(p);
                }
                var mk = new MeshKit();
                mk.Loft(part, false, 0.45f);
                mk.ToObject(names[bi], mats[bi], root, false, true);
            }
            // 앞면 가죽 박음질선(가는 밝은 선) — 턱 아래
            var stitch = new MeshKit();
            var line = new List<Vector3[]>();
            for (int i = 0; i <= N; i++)
            {
                var a = secs[i][7]; var b = secs[i][8];
                var c = Vector3.Lerp(a, b, 0.35f) + new Vector3(0f, 0f, -0.002f);
                line.Add(new[] { c + new Vector3(0f, 0.0015f, 0f), c - new Vector3(0f, 0.0015f, 0f) });
            }
            stitch.Loft(line, false, 0.05f);
            stitch.ToObject("dash-stitch", DcAssets.Lit(DcAssets.Hex("#9A958C"), 0.2f), root);
        }

        /// <summary>계기판 덮개: 가운데 평평·끝 둥근 아치 로프트</summary>
        static void HoodLoft(Transform root, float xEye, float zEye)
        {
            float[] pz = { 1.00f, 0.97f, 0.93f, 0.87f, 0.81f, 0.78f, 0.768f };
            // (11/13 대표: "계기판과 차량 모델링이 겹쳐 보인다") 덮개가 화면 윗부분과 양 끝을 가렸다(눈에서 보면 뒤쪽 턱이 화면 위 1/4 을 덮고,
            //   끝으로 갈수록 낮아져 지도 오른쪽은 절반이 가려짐) → 턱을 1.2 cm 올리고, 화면보다 넉넉히 넓혀 화면 위에서는 거의 평평하게
            float[] py = { 0.975f, 1.07f, 1.127f, 1.157f, 1.160f, 1.147f, 1.124f };
            float x0 = xEye - 0.32f, x1 = xEye + 0.62f, xc = (x0 + x1) / 2f, hw = (x1 - x0) / 2f;
            var secs = new List<Vector3[]>();
            const int N = 16;
            for (int i = 0; i <= N; i++)
            {
                float x = x0 + (x1 - x0) * i / N;
                float u = (x - xc) / hw;
                float arch = Mathf.Clamp01(1f - u * u * u * u);   // 가운데 1, 끝 0
                float h = 0.18f + 0.82f * Mathf.Sqrt(arch);
                var sec = new Vector3[pz.Length];
                for (int k = 0; k < pz.Length; k++) sec[k] = new Vector3(x, 0.975f + (py[k] - 0.975f) * h, zEye + pz[k] + (1f - h) * 0.02f);
                secs.Add(sec);
            }
            var mk = new MeshKit();
            mk.Loft(secs, false, 0.4f);
            mk.ToObject("cluster-hood", DcAssets.Pbr("fabric_leather_02", DcAssets.Hex("#2A2D31"), new Color(0.23f, 0.24f, 0.26f), 0.4f), root, false, true);
        }

        /// <summary>a → b 로 가는 둥근(모서리 둥근 사각) 단면 관. 굵기는 w0·d0 에서 w1·d1 로 줄어든다. 바깥이 앞면.</summary>
        static void TubeLoft(MeshKit mk, Vector3 a, Vector3 b, float w0, float w1, float d0, float d1)
        {
            var t = (b - a).normalized;
            var n1 = Vector3.Cross(t, Vector3.up);
            if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.right;
            n1.Normalize();
            var n2 = Vector3.Cross(n1, t).normalized;
            const int S = 10, M = 16;
            var secs = new List<Vector3[]>();
            for (int i = 0; i <= S; i++)
            {
                float f = i / (float)S;
                var c = Vector3.Lerp(a, b, f);
                float w = Mathf.Lerp(w0, w1, f) / 2f, d = Mathf.Lerp(d0, d1, f) / 2f;
                var sec = new Vector3[M + 1];
                for (int k = 0; k <= M; k++)
                {
                    float th = k / (float)M * Mathf.PI * 2f;
                    float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                    // 모서리 둥근 사각(초타원, 지수 0.5)
                    float ex = Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.5f), ey = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.5f);
                    sec[k] = c + n1 * (ex * w) + n2 * (ey * d);
                }
                secs.Add(sec);
            }
            // 바깥이 앞면이 되게 감는 방향 확인
            var du = secs[1][0] - secs[0][0];
            var dv = secs[0][1] - secs[0][M - 1];
            bool flip = Vector3.Dot(Vector3.Cross(du, dv), secs[0][0] - a) < 0f;
            mk.Loft(secs, flip, 0.3f);
        }

        static readonly Sprite[] ringSprites = new Sprite[2];

        /// <summary>270도 고리 그림(아래 90도 비움). fg=false 면 흐린 고리 + 20 km/h 눈금, fg=true 면 꽉 찬 고리</summary>
        static Sprite RingSprite(bool fg)
        {
            int idx = fg ? 1 : 0;
            if (ringSprites[idx] != null) return ringSprites[idx];
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = fg ? "dial-arc" : "dial" };
            var px = new Color32[N * N];
            float c = (N - 1) / 2f, r0 = N * (fg ? 0.405f : 0.43f), r1 = N * 0.47f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(-dx, -dy) * Mathf.Rad2Deg;   // 0 = 아래, 보는 쪽에서 시계 방향 +
                    if (ang < 0f) ang += 360f;
                    float inArc = Mathf.Clamp01(Mathf.Min(ang - 45f, 315f - ang) * 2f + 0.5f);
                    float a = Mathf.Clamp01(Mathf.Min(r - r0, r1 - r) + 0.5f) * inArc;
                    if (!fg)
                    {
                        a *= 0.35f;
                        // 눈금: 0..DialMax 를 20 km/h 마다(고리 안쪽으로 조금 더 길게)
                        float step = 270f / (DialMax / 20f);
                        float k = (ang - 45f) / step;
                        float off = Mathf.Abs(k - Mathf.Round(k)) * step * Mathf.Deg2Rad * r;   // 눈금선까지 픽셀 거리
                        if (ang >= 44f && ang <= 316f && r > N * 0.36f && r < N * 0.47f)
                            a = Mathf.Max(a, Mathf.Clamp01(1.6f - off) * 0.95f);
                    }
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            ringSprites[idx] = Sprite.Create(tex, new UnityEngine.Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            return ringSprites[idx];
        }

        static void Box(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        const float WheelY = 0.91f, WheelTilt = -65f;

        /// <summary>a→b 로 기울어진 막대(상자 하나를 돌려 세운다)</summary>
        static void Pillar(Transform parent, Material mat, Vector3 a, Vector3 b, float w, float d)
        {
            var mk = new MeshKit();
            mk.Box(Vector3.zero, new Vector3(w, (b - a).magnitude, d), 0f);
            var go = mk.ToObject("a-pillar", mat, parent, false, true);
            go.transform.localPosition = (a + b) / 2f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        }

        void MirrorQuad(string name, Vector3 local, Vector2 size, float yawToEye, RenderTexture tex, bool framed = true)
        {
            var canvas = WorldCanvas(name, local, size, 0.001f);
            canvas.transform.localRotation = Quaternion.Euler(0f, yawToEye, 0f);
            if (framed)
            {
                var frame = UiKit.Card(canvas.transform, "frame", DcAssets.Hex("#15171A"));
                UiKit.Stretch(frame.rectTransform, -6, -6, -6, -6);
            }
            var img = UiKit.Node("glass", canvas.transform).gameObject.AddComponent<RawImage>();
            UiKit.Stretch(img.rectTransform);
            img.texture = tex;
            img.uvRect = new UnityEngine.Rect(1f, 0f, -1f, 1f);   // 거울이므로 좌우 뒤집기
        }

        Canvas WorldCanvas(string name, Vector3 local, Vector2 sizeM, float pxToM)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;   // 가까이서 봐도 글자가 흐리지 않게
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeM / pxToM;
            rt.localScale = Vector3.one * pxToM;
            rt.localPosition = local;
            rt.localRotation = Quaternion.identity;
            return c;
        }

        public void SetActive(bool on) { if (root.activeSelf != on) root.SetActive(on); }

        /// <summary>가운데 화면에 띄울 그림(지도 또는 후방 카메라). 화면 비율에 맞춰 위아래를 잘라 보인다.</summary>
        /// <param name="mirror">후방 카메라는 실제 차처럼 좌우를 뒤집어 보인다(거울과 같은 방향)</param>
        public void SetScreen(Texture tex, bool mirror)
        {
            if (tex == screenTex) return;
            screenTex = tex;
            centerScreen.texture = tex;
            if (tex == null) return;
            float screenAspect = 0.58f * 0.5f / 0.13f;
            float texAspect = (float)tex.width / Mathf.Max(1, tex.height);
            float h = Mathf.Clamp01(texAspect / screenAspect);
            centerScreen.uvRect = mirror ? new UnityEngine.Rect(1f, (1f - h) / 2f, -1f, h) : new UnityEngine.Rect(0f, (1f - h) / 2f, 1f, h);
        }

        /// <summary>핸들·계기판. info: 계기판 왼쪽 작은 글(서킷 바퀴·제한 속도 등)</summary>
        public void Tick(Vehicle v, bool brake, string info)
        {
            if (!root.activeSelf) return;
            // 실제 핸들 각 그대로(시계 방향 +): 원판 법선 축으로 돌린다
            wheel.localRotation = Quaternion.Euler(WheelTilt, 0f, 0f) * Quaternion.AngleAxis(v.steerDeg, Vector3.up);
            float kmh = Mathf.Abs(v.speed) * 3.6f;
            speedText.text = Mathf.RoundToInt(kmh).ToString();
            if (speedArc != null) speedArc.fillAmount = 0.125f + 0.75f * Mathf.Clamp01(kmh / DialMax);
            string g = v.gear.ToString();
            if (g != lastGear)
            {
                lastGear = g;
                var sb = new System.Text.StringBuilder();
                foreach (var c in new[] { "P", "R", "N", "D" })
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(c == g ? "<color=#7FB3FF>" + c + "</color>" : "<color=#3E4650>" + c + "</color>");
                }
                gearText.text = sb.ToString();
            }
            infoText.text = (brake ? "<color=#FF6B5E>● 제동</color>\n" : "") + (info ?? "");
        }
    }
}
