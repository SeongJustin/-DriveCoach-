// 담당: S (3D 주차 연습 — 내 차: 제원으로 직접 만든 세단(CarBody, 없으면 Kenney 모델), 앞바퀴 꺾임·바퀴 굴림·브레이크등·후진등, 경사로 기울기)
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class CarView
    {
        /// <summary>
        /// 내 차 겉모습(차체·바퀴·고리·등)이 놓이는 층. 운전석 시점의 화면 카메라는 이 층을 빼고 그린다
        /// (차 안에서 차체 안쪽 면이 앞을 가리지 않게. 보닛은 Cockpit 이 따로 만든다). 거울·후방 카메라는 그대로 본다.
        /// </summary>
        public const int BodyLayer = 30;
        public readonly GameObject root;
        readonly Transform body;
        readonly List<Transform> frontWheels = new List<Transform>();
        readonly List<Transform> allWheels = new List<Transform>();
        readonly Dictionary<Transform, Quaternion> baseRot = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<Transform, Transform> calipers = new Dictionary<Transform, Transform>();
        /// <summary>내 차 색(짙은 메탈릭 파랑). 운전석 보닛(Cockpit)도 같은 색</summary>
        public static readonly Color Paint = new Color(0.12f, 0.27f, 0.58f);
        /// <summary>직접 만든 차(CarBody)를 쓰는지: 그러면 Cockpit 은 따로 보닛을 만들지 않는다</summary>
        public static bool HasOwnBody { get; private set; }
        readonly Material brakeMat, reverseMat;
        readonly Transform ring;
        float roll;
        readonly float halfLen;
        float wheelRadius = 0.33f;

        /// <summary>내 차는 "sedan" 모델. 바닥에 파란 고리를 둘러 내 차를 바로 알아보게 한다.</summary>
        public CarView(Vehicle v, Transform parent)
        {
            root = new GameObject("MyCar");
            root.transform.SetParent(parent, false);
            halfLen = v.length / 2f;
            brakeMat = DcAssets.UnlitInstance(new Color(0.35f, 0.02f, 0.02f));
            reverseMat = DcAssets.UnlitInstance(new Color(0.25f, 0.25f, 0.25f));
            // 11/8 밤: 차 제원으로 직접 만든 세단. 없으면 예전 Kenney 모델
            Transform driverView = null;
            var holderGo = new GameObject("car");
            holderGo.transform.SetParent(root.transform, false);
            bool own = CarBody.Build(v, holderGo.transform, Paint, brakeMat, reverseMat, out driverView);
            GameObject holder = holderGo;
            if (!own)
            {
                Dc.Kill(holderGo);
                // 길이·폭은 차 제원 그대로, 높이도 실제 승용차 높이(아반떼 CN8 1.42 m)로 — 예전엔 모델 비율대로 키워 좁고 높아 보였다(11/8)
                holder = StageBuilder.ModelCar("Kenney/CarKit/sedan", v.length, v.width, root.transform, DcAssets.Blue, 1.42f);
            }
            body = holder.transform;
            foreach (var t in holder.GetComponentsInChildren<Transform>())
            {
                var n = t.name.ToLowerInvariant();
                if (!n.Contains("wheel")) continue;
                allWheels.Add(t);
                baseRot[t] = t.localRotation;
                if (n.Contains("front")) frontWheels.Add(t);
                var cal = t.Find("caliper");
                if (cal != null) calipers[t] = cal;
            }
            if (own) wheelRadius = CarBody.WheelRadius;
            else if (allWheels.Count > 0)
            {
                var b = StageBuilder.LocalBounds(allWheels[0].gameObject);
                wheelRadius = Mathf.Max(0.2f, b.size.y * allWheels[0].lossyScale.y / 2f);
            }
            if (!own)
            {
                // 브레이크등·후진등(뒤 범퍼 양쪽 작은 판) — 직접 만든 차는 뒷등 안에 있다
                float hz = -v.length / 2f - 0.01f;
                foreach (var sx in new[] { -1f, 1f })
                {
                    Lamp("brake", new Vector3(sx * (v.width / 2f - 0.25f), 0.85f, hz), new Vector3(0.38f, 0.12f, 0.02f), brakeMat);
                    Lamp("reverse", new Vector3(sx * (v.width / 2f - 0.6f), 0.75f, hz), new Vector3(0.16f, 0.08f, 0.02f), reverseMat);
                }
            }
            HasOwnBody = own;
            if (own) wipers = Wipers.Make(holder.transform);   // 11/8 밤: 와이퍼가 실제로 움직인다
            // 내 차 표시 고리(바닥에 옅은 파랑)
            var ringMk = new MeshKit();
            var pts = new List<V2>();
            for (int i = 0; i < 48; i++) pts.Add(V2.FromAngle(i / 48f * Geom.Tau) * (v.length * 0.62f));
            ringMk.Ribbon(pts, 0.08f, 0.015f, true);
            ring = ringMk.ToObject("ring", DcAssets.Unlit(new Color(DcAssets.Lime.r, DcAssets.Lime.g, DcAssets.Lime.b, 0.55f)), root.transform).transform;
            SetLayer(root.transform, BodyLayer);
            // 보닛 위·카울·와이퍼·사이드미러는 운전석 화면에서도 보이게(기본 층). 차체 안쪽 면은 없어서(한쪽 면만) 앞을 가리지 않는다
            if (driverView != null) SetLayer(driverView, 0);
        }

        readonly List<Light> headlights = new List<Light>();
        public Wipers wipers;
        bool envHeadlights;
        readonly GameObject[] blink = new GameObject[2];   // 0 왼쪽, 1 오른쪽(앞·뒤 모서리 주황 등)
        float blinkT;

        /// <summary>방향지시등 등(앞뒤 모서리, 켜졌을 때만 밝은 주황)을 만든다</summary>
        void MakeBlinkers(Vehicle v)
        {
            var mat = DcAssets.Unlit(new Color(1f, 0.62f, 0.10f));
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1f : 1f;
                var mk = new MeshKit();
                mk.Box(new Vector3(sx * (v.width / 2f - 0.10f), 0.70f, v.length / 2f - 0.10f), new Vector3(0.16f, 0.07f, 0.10f), 0f);
                mk.Box(new Vector3(sx * (v.width / 2f - 0.10f), 0.86f, -v.length / 2f + 0.06f), new Vector3(0.18f, 0.06f, 0.06f), 0f);
                mk.Box(new Vector3(sx * (v.width / 2f + 0.06f), 1.02f, v.length / 2f - 1.42f), new Vector3(0.05f, 0.025f, 0.12f), 0f);   // 거울 옆
                blink[s] = mk.ToObject(s == 0 ? "blink-left" : "blink-right", mat, root.transform);
                blink[s].layer = BodyLayer;     // (11/13) 차체와 같은 층: 운전석 화면에서 거울 옆 주황 등이 공중에 뜬 판자처럼 보였다(나중에 만들어져 기본 층에 있었음)
                blink[s].SetActive(false);
            }
        }

        /// <summary>
        /// 방향지시등·비상등 깜빡임(1분 85번 — 법정 60~120회), 전조등은 실제 스위치(켜짐·상향·끔)를 따르고 자동·모름이면 날씨·시간대로.
        /// </summary>
        public void Signals(Vehicle v, float dt)
        {
            if (blink[0] == null) MakeBlinkers(v);
            blinkT = (blinkT + dt) % (60f / 85f);
            bool on = blinkT < 30f / 85f;
            bool l = v.hazard || v.turnSignal < 0, r = v.hazard || v.turnSignal > 0;
            blink[0].SetActive(l && on);
            blink[1].SetActive(r && on);
            bool head = v.headlight == 1 || v.headlight == 2 || ((v.headlight < 0 || v.headlight == 3) && envHeadlights);
            ApplyHeadlights(head, v.headlight == 2);
        }

        /// <summary>깜빡이가 지금 켜진 쪽(HUD 화살표용): -1 왼쪽, 1 오른쪽, 2 둘 다, 0 없음</summary>
        public int BlinkNow(Vehicle v)
        {
            bool on = blinkT < 30f / 85f;
            if (!on) return 0;
            if (v.hazard) return 2;
            return v.turnSignal;
        }

        /// <summary>전조등(11/8 밤: 밤·궂은 날씨). 처음 켤 때 만든다(스폿 두 개, 그림자 없음)</summary>
        public void SetHeadlights(bool on) { envHeadlights = on; ApplyHeadlights(on, false); }

        GameObject beam;

        void ApplyHeadlights(bool on, bool high)
        {
            if (on && headlights.Count == 0)
            {
                foreach (var sx in new[] { -1f, 1f })
                {
                    var g = new GameObject("headlight");
                    g.transform.SetParent(root.transform, false);
                    g.transform.localPosition = new Vector3(sx * 0.62f, 0.72f, halfLen - 0.05f);
                    g.transform.localRotation = Quaternion.Euler(7f, sx * 4f, 0f);
                    var l = g.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.range = 60f;
                    l.spotAngle = 62f;
                    l.innerSpotAngle = 30f;
                    l.intensity = 120f;
                    l.color = new Color(1f, 0.97f, 0.9f);
                    l.shadows = LightShadows.None;
                    l.renderMode = LightRenderMode.ForcePixel;     // 조명 개수 제한에서 먼저(내 차 전조등)
                    headlights.Add(l);
                }
            }
            // URP 조명은 거리 제곱으로 약해진다(10 m 앞 = 1/100): 예전 세기 6 은 차 바로 앞만 겨우 비췄다(11/9 밤 사진 검수)
            foreach (var l in headlights)
            {
                l.enabled = on;
                l.range = high ? 110f : 70f;
                l.spotAngle = high ? 50f : 64f;
                l.intensity = high ? 200f : 120f;     // 11/9 2차: 40 도 어두운 아스팔트(반사 0.2)에서 10 m 앞이 거의 안 보였다
            }
            // 노면에 번지는 전조등 빛(조명 개수가 적은 기기·그래픽 낮음에서도 앞길이 밝게 보이게): 차 앞 2~16 m 부채꼴 빛 판
            if (on && beam == null && root != null)
            {
                var mk = new MeshKit();
                // 둥근 빛(가운데 차 앞 9 m, 앞뒤 11 m): 뒤쪽 끝은 차 밑에서 사라져 경계가 보이지 않는다(11/9 밤 사진: 반쪽 판은 뒤 끝이 칼처럼 잘려 보였다)
                NightLights.Pool(mk, new Vector3(0f, 0.05f, halfLen + 11f), 4.6f, 14f, 0f);
                NightLights.Pool(mk, new Vector3(0f, 0.055f, halfLen + 6f), 2.8f, 6.5f, 0f);       // 가까운 곳은 더 밝게(두 판이 겹침)
                beam = mk.ToObject("headlight-beam", NightLights.Glow(new Color(1f, 0.95f, 0.84f, 0.42f)), root.transform);
                // 먼 빛(15~45 m, 은은하게): 운전석에서는 보닛에 가려 가까운 빛이 안 보인다 — 앞길이 보이게
                var far = new MeshKit();
                NightLights.Pool(far, new Vector3(0f, 0.06f, halfLen + 28f), 5.5f, 18f, 0f);
                far.ToObject("headlight-far", NightLights.Glow(new Color(1f, 0.95f, 0.86f, 0.24f)), beam.transform);
            }
            if (beam != null)
            {
                beam.SetActive(on && NightLights.On);      // 노면 빛 판은 어두울 때만(낮 눈·비에는 거의 안 보인다)
                beam.transform.localScale = new Vector3(1f, 1f, high ? 1.5f : 1f);
            }
        }

        public static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        void Lamp(string name, Vector3 pos, Vector3 size, Material m)
        {
            var mk = new MeshKit();
            mk.Box(pos, size, 0f);
            mk.ToObject(name, m, root.transform);
        }

        /// <summary>차 위치·방향·높이(경사로)와 바퀴를 맞춘다.</summary>
        public void Sync(Vehicle v, float groundY, float pitchDeg, bool brake, float dt)
        {
            var center = v.Center;
            root.transform.position = Space3D.W(center, groundY);
            root.transform.rotation = Space3D.Rot(v.heading) * Quaternion.Euler(-pitchDeg, 0f, 0f);
            float steerDeg = v.WheelAngle() * Mathf.Rad2Deg;
            roll += v.speed * dt / wheelRadius * Mathf.Rad2Deg;
            foreach (var w in allWheels)
            {
                var q = baseRot[w];
                bool front = frontWheels.Contains(w);
                // 바퀴 노드의 위쪽 축 기준 꺾기, 옆 축 기준 굴림(모델 축이 달라도 부모 기준으로 돌린다)
                var steer = front ? Quaternion.AngleAxis(steerDeg, w.parent != null ? w.parent.InverseTransformDirection(body.up) : Vector3.up) : Quaternion.identity;
                var spin = Quaternion.AngleAxis(roll, w.parent != null ? w.parent.InverseTransformDirection(body.right) : Vector3.right);
                w.localRotation = steer * spin * q;
                // 캘리퍼는 바퀴와 함께 꺾이되 돌지는 않는다
                if (calipers.TryGetValue(w, out var cal)) cal.localRotation = Quaternion.Inverse(spin);
            }
            DcAssets.Tint(brakeMat, brake && v.gear != Gear.P ? new Color(1f, 0.1f, 0.08f) : new Color(0.35f, 0.02f, 0.02f));
            DcAssets.Tint(reverseMat, v.gear == Gear.R ? new Color(1f, 1f, 0.95f) : new Color(0.25f, 0.25f, 0.25f));
            ring.gameObject.SetActive(v.speed == 0f);
        }

        public void Dispose() { wipers?.Dispose(); Dc.Kill(root); }
    }
}
