// (3D 도로 주행 — 다른 차 모습: Kenney 차 모델을 차 크기·실제 차 높이에 맞춰 놓고 매 프레임 자리·방향을 맞춘다.
// 서면 브레이크등, 바퀴는 달린 거리만큼 굴리고 앞바퀴는 도는 정도만큼 꺾는다)
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class TrafficView
    {
        static readonly string[] Models = { "sedan", "suv", "hatchback-sports", "taxi", "sedan-sports", "suv-luxury", "van", "sedan" };
        // 실제 차 높이(m): 세단 1.45, SUV 1.70, 해치백 1.40, 택시(세단) 1.47, 스포츠 1.35, 큰 SUV 1.78, 승합 1.95
        static readonly float[] Heights = { 1.45f, 1.70f, 1.40f, 1.47f, 1.35f, 1.78f, 1.95f, 1.45f };

        class Wheels
        {
            public readonly List<Transform> all = new List<Transform>(), front = new List<Transform>();
            public readonly Dictionary<Transform, Quaternion> baseRot = new Dictionary<Transform, Quaternion>();
            public float roll, radius = 0.32f, lastHeading;
            public bool started;
        }
        readonly Dictionary<TrafficCar, Wheels> wheels = new Dictionary<TrafficCar, Wheels>();
        // 차체 움직임: 경사로에서는 길 기울기만큼 앞뒤로 기울고,
        //   가속·감속 때 살짝 들리고 숙이며(±2°), 돌 때 바깥으로 살짝 기운다(±2.5°). 예전에는 고가 경사로에서도 차가 수평으로 떠 있었다
        class Motion { public float speed, z, heading, slope, dive, lean; public bool started; }
        readonly Dictionary<TrafficCar, Motion> motion = new Dictionary<TrafficCar, Motion>();
        readonly Transform root;
        readonly Elevation el;
        readonly Dictionary<TrafficCar, (GameObject go, Renderer[] lamps)> views = new Dictionary<TrafficCar, (GameObject, Renderer[])>();
        // 방향지시등(버스가 정류장에 설 때 오른쪽, 떠날 때 왼쪽) — 차마다 왼쪽·오른쪽 등
        readonly Dictionary<TrafficCar, (Renderer[] left, Renderer[] right)> ambers = new Dictionary<TrafficCar, (Renderer[], Renderer[])>();
        Material amberOff, amberOn;
        // 차 종류별 모델: 0 세단, 1 SUV, 2 해치백, 3 택시, 4 승합, 5 1톤 화물, 6 시내버스, 7 마을버스(없으면 Kenney)
        static readonly string[] FleetName = { "sedan", "suv", "hatch", "taxi", "van", "truck", "bus", "townbus" };
        // 차체색: 종류별로 우리나라 길에 흔한 비율을 대충 따라 —
        //   승용은 흰·검정·회색·은색이 대부분, 남색·파랑·빨강·갈색·베이지·초록·하늘·주황·노랑이 가끔. 상표·로고 색은 쓰지 않는다.
        //   (색, 비율) — 차마다 TrafficCar.paint(0~9999)로 고른다
        static readonly (Color c, float w)[] CarPaint =
        {
            (DcAssets.Hex("#F3F3F0"), 0.20f), (DcAssets.Hex("#E7E3DA"), 0.07f),   // 흰, 진주 흰
            (DcAssets.Hex("#1B1D21"), 0.17f),                                       // 검정
            (DcAssets.Hex("#C8CBD0"), 0.10f), (DcAssets.Hex("#8B9096"), 0.10f), (DcAssets.Hex("#4A4F56"), 0.08f),   // 은·회·진회
            (DcAssets.Hex("#253B63"), 0.05f), (DcAssets.Hex("#3C6DB4"), 0.03f), (DcAssets.Hex("#9DB6CB"), 0.02f),   // 남·파랑·하늘
            (DcAssets.Hex("#8D1E24"), 0.04f), (DcAssets.Hex("#C3342B"), 0.02f),   // 짙은 빨강·빨강
            (DcAssets.Hex("#6A5541"), 0.03f), (DcAssets.Hex("#CDBD9B"), 0.03f),   // 갈색·베이지
            (DcAssets.Hex("#3E5A47"), 0.02f), (DcAssets.Hex("#D9792D"), 0.01f), (DcAssets.Hex("#E4C24A"), 0.01f), (DcAssets.Hex("#7FC4BA"), 0.01f),   // 초록·주황·노랑·민트
        };
        // 해치백(작은 차): 밝은 색이 더 많이
        static readonly (Color c, float w)[] HatchPaint =
        {
            (DcAssets.Hex("#F3F3F0"), 0.22f), (DcAssets.Hex("#C8CBD0"), 0.12f), (DcAssets.Hex("#1B1D21"), 0.10f), (DcAssets.Hex("#C3342B"), 0.10f),
            (DcAssets.Hex("#3C6DB4"), 0.08f), (DcAssets.Hex("#E4C24A"), 0.08f), (DcAssets.Hex("#7FC4BA"), 0.08f), (DcAssets.Hex("#9DB6CB"), 0.07f),
            (DcAssets.Hex("#D9792D"), 0.05f), (DcAssets.Hex("#8B9096"), 0.05f), (DcAssets.Hex("#E8A3B0"), 0.05f),
        };
        // 승합: 흰·검정·회색이 대부분
        static readonly (Color c, float w)[] VanPaint =
        {
            (DcAssets.Hex("#F3F3F0"), 0.35f), (DcAssets.Hex("#1B1D21"), 0.25f), (DcAssets.Hex("#8B9096"), 0.15f), (DcAssets.Hex("#C8CBD0"), 0.12f),
            (DcAssets.Hex("#253B63"), 0.07f), (DcAssets.Hex("#6A5541"), 0.06f),
        };
        // 택시: 흰(가장 많음)·꽃담황토색(서울 택시)·은색·검정(모범)
        static readonly (Color c, float w)[] TaxiPaint =
        {
            (DcAssets.Hex("#F3F3F0"), 0.50f), (DcAssets.Hex("#E8833A"), 0.25f), (DcAssets.Hex("#C8CBD0"), 0.15f), (DcAssets.Hex("#1B1D21"), 0.10f),
        };
        // 1톤 화물: 흰·파랑·은색
        static readonly (Color c, float w)[] TruckPaint =
        {
            (DcAssets.Hex("#F3F3F0"), 0.45f), (DcAssets.Hex("#2F5FB8"), 0.30f), (DcAssets.Hex("#C8CBD0"), 0.15f), (DcAssets.Hex("#8B9096"), 0.10f),
        };
        // 시내버스: 서울 버스 색 체계 — 간선 파랑, 지선 초록, 광역 빨강, 순환 노랑
        static readonly (Color c, float w)[] BusPaint =
        {
            (DcAssets.Hex("#2F5FB8"), 0.50f), (DcAssets.Hex("#3E9E3A"), 0.35f), (DcAssets.Hex("#C8352B"), 0.10f), (DcAssets.Hex("#E2B12A"), 0.05f),
        };
        // 마을버스: 초록이 대부분, 가끔 노랑
        static readonly (Color c, float w)[] TownBusPaint = { (DcAssets.Hex("#3E9E3A"), 0.80f), (DcAssets.Hex("#E2B12A"), 0.20f) };
        readonly Material lampOff, lampOn, tailNight, headLamp, beam;
        // 밤 불빛: 전조등(앞 두 개)·노면을 비추는 빛 판, 미등은 밤이면 은은하게 켠다
        readonly Dictionary<TrafficCar, GameObject> nightFx = new Dictionary<TrafficCar, GameObject>();
        bool night;

        public TrafficView(Transform parent, Elevation elev)
        {
            root = new GameObject("Traffic").transform;
            root.SetParent(parent, false);
            el = elev;
            lampOff = DcAssets.Unlit(new Color(0.35f, 0.03f, 0.03f));
            lampOn = DcAssets.Unlit(new Color(1f, 0.12f, 0.08f));
            tailNight = DcAssets.Unlit(new Color(0.75f, 0.06f, 0.05f));
            headLamp = DcAssets.Unlit(new Color(1f, 0.97f, 0.88f));
            beam = NightLights.Glow(new Color(1f, 0.95f, 0.82f, 0.38f));
            amberOff = DcAssets.Unlit(DcAssets.Hex("#5A3510"));
            amberOn = DcAssets.Unlit(DcAssets.Hex("#FF9A1F"));
            night = NightLights.On;
        }

        public static Color Pick((Color c, float w)[] pal, int paint) => pal[PickIndex(pal, paint)].c;

        public static int PickIndex((Color c, float w)[] pal, int paint)
        {
            float sum = 0f;
            foreach (var e in pal) sum += e.w;
            float x = (paint % 10000) / 10000f * sum;
            for (int i = 0; i < pal.Length; i++) { x -= pal[i].w; if (x < 0f) return i; }
            return pal.Length - 1;
        }

        public static (Color c, float w)[] PaletteOf(int kind)
        {
            // 버스·택시 색은 나라 표에 있으면 그것(베를린: 노란 BVG 버스, 상아색 택시). 한국은 아래 서울 색 그대로
            var cs = CountryStyle.Current;
            if (kind == 6 && cs.busPaint != null) return cs.busPaint;
            if (kind == 7 && cs.townBusPaint != null) return cs.townBusPaint;
            if (kind == 3 && cs.taxiPaint != null) return cs.taxiPaint;
            return kind == 6 ? BusPaint : kind == 7 ? TownBusPaint : kind == 5 ? TruckPaint : kind == 4 ? VanPaint : kind == 3 ? TaxiPaint : kind == 2 ? HatchPaint : CarPaint;
        }

        (GameObject, Renderer[]) Make(TrafficCar c)
        {
            int mi = c.model % Models.Length;
            GameObject go = null;
            string fleet = c.kind >= 0 && c.kind < FleetName.Length ? FleetName[c.kind] : null;
            if (fleet != null)
            {
                var fm = FleetModels.Make(fleet, root, Pick(PaletteOf(c.kind), c.paint));
                if (fm != null)
                {
                    go = new GameObject("traffic");
                    go.transform.SetParent(root, false);
                    fm.transform.SetParent(go.transform, false);
                    fm.transform.localPosition = Vector3.zero;
                    fm.transform.localRotation = Quaternion.identity;
                }
            }
            if (go == null)
            {
                if (c.kind == 3) mi = 3;                       // 택시
                else if (c.kind == 0 && (mi == 3 || mi == 6)) mi = 0;   // 세단 자리에 택시·승합 모델이 나오지 않게
                go = StageBuilder.ModelCar("Kenney/CarKit/" + Models[mi], c.length, c.width, root, Pick(PaletteOf(c.kind), c.paint), Heights[mi]);
            }
            go.name = "traffic";
            var wh = new Wheels();
            foreach (var t in go.GetComponentsInChildren<Transform>())
            {
                var n = t.name.ToLowerInvariant();
                if (!n.Contains("wheel")) continue;
                wh.all.Add(t);
                wh.baseRot[t] = t.localRotation;
                if (n.Contains("front")) wh.front.Add(t);
            }
            if (wh.all.Count > 0)
            {
                var b = StageBuilder.LocalBounds(wh.all[0].gameObject);
                wh.radius = Mathf.Max(0.2f, b.size.y * wh.all[0].lossyScale.y / 2f);
            }
            wheels[c] = wh;
            Renderer[] lamps;
            var fleetBrake = new List<Renderer>();
            var al = new List<Renderer>();
            var ar = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name == "brake") fleetBrake.Add(r);
                else if (r.gameObject.name == "amber-left") al.Add(r);
                else if (r.gameObject.name == "amber-right") ar.Add(r);
            }
            if (fleetBrake.Count > 0) lamps = fleetBrake.ToArray();
            else
            {
                lamps = new Renderer[2];
                for (int i = 0; i < 2; i++)
                {
                    var mk = new MeshKit();
                    mk.Box(new Vector3((i == 0 ? -1f : 1f) * (c.width / 2f - 0.25f), 0.8f, -c.length / 2f - 0.01f), new Vector3(0.36f, 0.12f, 0.02f), 0f);
                    var lg = mk.ToObject("brake", lampOff, go.transform);
                    lamps[i] = lg.GetComponent<Renderer>();
                }
            }
            if (al.Count == 0 && ar.Count == 0)
            {
                // Kenney 차: 앞뒤 모서리에 작은 주황 등
                foreach (var sx in new[] { -1f, 1f })
                {
                    var mk = new MeshKit();
                    mk.Box(new Vector3(sx * (c.width / 2f - 0.08f), 0.72f, c.length / 2f + 0.01f), new Vector3(0.12f, 0.06f, 0.02f), 0f);
                    mk.Box(new Vector3(sx * (c.width / 2f - 0.08f), 0.78f, -c.length / 2f - 0.01f), new Vector3(0.12f, 0.06f, 0.02f), 0f);
                    var ag = mk.ToObject(sx < 0 ? "amber-left" : "amber-right", amberOff, go.transform);
                    (sx < 0 ? al : ar).Add(ag.GetComponent<Renderer>());
                }
            }
            ambers[c] = (al.ToArray(), ar.ToArray());
            // 밤 불빛: 앞 전조등 두 개 + 앞 노면 빛(길이 14 m, 폭 5 m, 범퍼에서 멀어질수록 흐려짐)
            var fx = new GameObject("night");
            fx.transform.SetParent(go.transform, false);
            var hm = new MeshKit();
            for (int i = 0; i < 2; i++)
                hm.Box(new Vector3((i == 0 ? -1f : 1f) * (c.width / 2f - 0.28f), 0.72f, c.length / 2f + 0.01f), new Vector3(0.30f, 0.11f, 0.02f), 0f);
            hm.ToObject("head", headLamp, fx.transform);
            var bm = new MeshKit();
            NightLights.Pool(bm, new Vector3(0f, 0.04f, c.length / 2f + 7f), 2.6f, 7f, 0f, 0.5f, 1f);
            bm.ToObject("beam", beam, fx.transform, false, false);
            fx.SetActive(night);
            nightFx[c] = fx;
            return (go, lamps);
        }

        public void Tick(Traffic t)
        {
            var rgn = t.Road?.region;
            // 사라진 차의 모습 지우기
            if (views.Count > t.cars.Count)
            {
                var gone = new List<TrafficCar>();
                foreach (var k in views.Keys) if (!t.cars.Contains(k)) gone.Add(k);
                foreach (var k in gone) { Dc.Kill(views[k].go); views.Remove(k); wheels.Remove(k); motion.Remove(k); nightFx.Remove(k); ambers.Remove(k); }
            }
            if (night != NightLights.On)
            {
                night = NightLights.On;
                foreach (var fx in nightFx.Values) if (fx != null) fx.SetActive(night);
            }
            foreach (var c in t.cars)
            {
                if (!views.TryGetValue(c, out var v)) { v = Make(c); views[c] = v; }
                float bump = rgn != null && Mathf.Abs(c.z) < 0.5f ? rgn.HumpHeight(c.pos) : 0f;     // 과속방지턱 위면 들린다
                v.go.transform.position = Space3D.W(c.pos, el.At(c.pos, 0) + c.z + bump);
                {
                    float dtm = Mathf.Max(1e-4f, Time.deltaTime);
                    if (!motion.TryGetValue(c, out var mo)) motion[c] = mo = new Motion();
                    if (mo.started && dtm < 0.2f)
                    {
                        float ds = c.speed * dtm;
                        float slopeT = ds > 0.01f ? Mathf.Clamp(Mathf.Atan2(c.z - mo.z, ds) * Mathf.Rad2Deg, -8f, 8f) : mo.slope;
                        float acc = (c.speed - mo.speed) / dtm;
                        float yawRate = Mathf.DeltaAngle(mo.heading * Mathf.Rad2Deg, c.heading * Mathf.Rad2Deg) * Mathf.Deg2Rad / dtm;
                        float big = c.kind >= 4 ? 0.6f : 1f;          // 승합·화물·버스는 덜 흔들린다
                        mo.slope = Mathf.Lerp(mo.slope, slopeT, 1f - Mathf.Exp(-6f * dtm));
                        mo.dive = Mathf.Lerp(mo.dive, Mathf.Clamp(-acc * 0.45f, -2f, 2f) * big, 1f - Mathf.Exp(-4f * dtm));
                        mo.lean = Mathf.Lerp(mo.lean, Mathf.Clamp(c.speed * yawRate * 0.45f, -2.5f, 2.5f) * big, 1f - Mathf.Exp(-4f * dtm));
                    }
                    mo.speed = c.speed; mo.z = c.z; mo.heading = c.heading; mo.started = true;
                    // 앞이 높으면(오르막·가속) 코가 들린다: X 회전 −, 오른쪽으로 돌면 왼쪽으로 기운다: Z 회전 +
                    v.go.transform.rotation = Space3D.Rot(c.heading) * Quaternion.Euler(-(mo.slope - mo.dive), 0f, mo.lean);
                }
                if (wheels.TryGetValue(c, out var wh) && wh.all.Count > 0)
                {
                    float dt = Mathf.Max(1e-4f, Time.deltaTime);
                    wh.roll = (wh.roll + c.speed * dt / wh.radius * Mathf.Rad2Deg) % 360f;
                    // 앞바퀴 꺾임: 도는 빠르기(라디안/초)와 속도로 자전거 모델 각(축거 약 2.7 m), ±35°
                    float dh = wh.started ? Mathf.DeltaAngle(wh.lastHeading * Mathf.Rad2Deg, c.heading * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt : 0f;
                    wh.lastHeading = c.heading;
                    wh.started = true;
                    float steer = c.speed > 0.3f ? Mathf.Clamp(Mathf.Atan(2.7f * dh / c.speed) * Mathf.Rad2Deg, -35f, 35f) : 0f;
                    var body = v.go.transform;
                    foreach (var w in wh.all)
                    {
                        var par = w.parent != null ? w.parent : body;
                        var sp = Quaternion.AngleAxis(wh.roll, par.InverseTransformDirection(body.right));
                        var st = wh.front.Contains(w) ? Quaternion.AngleAxis(steer, par.InverseTransformDirection(body.up)) : Quaternion.identity;
                        w.localRotation = st * sp * wh.baseRot[w];
                    }
                }
                var m = c.braking || c.speed < 0.1f ? lampOn : (night ? tailNight : lampOff);
                if (v.lamps.Length > 0 && v.lamps[0].sharedMaterial != m) foreach (var lr in v.lamps) lr.sharedMaterial = m;
                if (ambers.TryGetValue(c, out var am))
                {
                    bool phase = (Time.time * 1.5f) % 1f < 0.5f;     // 1분에 90번(법정 60~120)
                    var ml = c.blink == -1 && phase ? amberOn : amberOff;
                    var mr = c.blink == 1 && phase ? amberOn : amberOff;
                    foreach (var r in am.left) if (r.sharedMaterial != ml) r.sharedMaterial = ml;
                    foreach (var r in am.right) if (r.sharedMaterial != mr) r.sharedMaterial = mr;
                }
            }
        }

        public void Dispose() { Dc.Kill(root.gameObject); }
    }
}
