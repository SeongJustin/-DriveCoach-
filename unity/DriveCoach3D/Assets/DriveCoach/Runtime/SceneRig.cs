// 3D 주차 연습 — 한 판의 3D 장면 전체: 지형·내 차·안내·카메라·조명·후방 카메라
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    public enum CamMode { Chase, Top, Driver, Overview }

    /// <summary>카메라: 따라가기(뒤 위) / 위에서(2D 와 같은 북쪽 위) / 운전석 / 전체(지도). 부드럽게 옮긴다.</summary>
    public class CameraRig
    {
        public readonly Camera cam;
        public CamMode mode = CamMode.Chase;
        Vector3 pos, look;
        bool snapped;
        /// <summary>운전석 시점: 차 몸체와 눈 위치(차 기준), 고개 돌림(도)</summary>
        public Transform carRoot;
        public Vector3 eyeLocal = new Vector3(-0.48f, 1.17f, 0.2f);
        public float lookYaw, lookPitch;
        float turnLook;
        // 몸으로 느끼는 차 — 제동하면 앞으로 숙고, 돌면 바깥으로 기울고, 빠를수록 노면 떨림
        float bodyPitch, bodyRoll, lastSpeed, lastHeading, shakeT;
        bool bodyInit;

        public CameraRig(Transform parent)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            go.transform.SetParent(parent, false);
            cam = go.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.allowMSAA = true;
        }

        /// <summary>가로 화면은 세로 시야각 그대로, 세로 화면은 가로 시야각(hFov)이 나오도록(좁게 보이지 않게)</summary>
        float Fov(float landscapeVFov, float portraitHFov)
        {
            float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
            if (aspect >= 1f) return landscapeVFov;
            return Mathf.Min(110f, Camera.HorizontalToVerticalFieldOfView(portraitHFov, aspect));
        }

        public void Snap(CamMode m, World w) { mode = m; snapped = false; Want(w, null, out pos, out look); Apply(); snapped = true; }

        public void Tick(World w, Elevation el, float dt)
        {
            if (mode == CamMode.Driver && carRoot != null)
            {
                // 유로트럭처럼: 눈 위치는 차에 붙어 있고, 고개는 끌어서 돌린다. 핸들을 돌리면 그쪽으로 조금 고개가 따라간다.
                float want = Mathf.Clamp(w.car.steerDeg / Mathf.Max(1f, w.car.maxSteerDeg), -1f, 1f) * 14f;
                turnLook = Mathf.Lerp(turnLook, want, 1f - Mathf.Exp(-3f * dt));
                var car = w.car;
                float heading = Mathf.Atan2(car.Dir.y, car.Dir.x);
                if (!bodyInit || dt <= 0f) { lastSpeed = car.speed; lastHeading = heading; bodyInit = true; }
                float acc = dt > 0f ? (car.speed - lastSpeed) / dt : 0f;
                float yawRate = dt > 0f ? Mathf.DeltaAngle(lastHeading * Mathf.Rad2Deg, heading * Mathf.Rad2Deg) / dt : 0f;
                lastSpeed = car.speed; lastHeading = heading;
                float latAcc = Mathf.Abs(car.speed) * yawRate * Mathf.Deg2Rad;
                bodyPitch = Mathf.Lerp(bodyPitch, Mathf.Clamp(-acc * 0.32f, -2.2f, 2.2f), 1f - Mathf.Exp(-5f * dt));
                bodyRoll = Mathf.Lerp(bodyRoll, Mathf.Clamp(latAcc * 0.28f, -1.8f, 1.8f), 1f - Mathf.Exp(-4f * dt));
                shakeT += dt * (6f + Mathf.Abs(car.speed) * 0.8f);
                float shake = Mathf.Min(1f, Mathf.Abs(car.speed) / 14f) * 0.12f;
                float sp = (Mathf.PerlinNoise(shakeT, 0.3f) - 0.5f) * shake, sr = (Mathf.PerlinNoise(0.7f, shakeT) - 0.5f) * shake;
                cam.transform.position = carRoot.TransformPoint(eyeLocal + new Vector3(0f, sp * 0.02f, 0f));
                cam.transform.rotation = carRoot.rotation * Quaternion.Euler(4f + lookPitch + bodyPitch + sp, lookYaw + turnLook, bodyRoll + sr);
                cam.fieldOfView = Fov(68f, 100f);
                cam.cullingMask = ~((1 << CarView.BodyLayer) | (1 << RearGuide.Layer));   // 차체·후방 안내선은 빼고(실내·보닛은 Cockpit)
                snapped = false;
                return;
            }
            cam.fieldOfView = Fov(55f, 85f);
            cam.cullingMask = ~(1 << RearGuide.Layer);   // 후방 안내선은 후방 카메라에만
            Want(w, el, out var p, out var l);
            if (!snapped) { pos = p; look = l; snapped = true; }
            float k = mode == CamMode.Driver ? 1f : 1f - Mathf.Exp(-4f * dt);
            pos = Vector3.Lerp(pos, p, k);
            look = Vector3.Lerp(look, l, mode == CamMode.Driver ? 1f : 1f - Mathf.Exp(-6f * dt));
            Apply();
        }

        void Apply()
        {
            cam.transform.position = pos;
            var dir = look - pos;
            if (dir.sqrMagnitude < 1e-6f) return;
            // 위에서 볼 때는 북쪽(+Z)이 화면 위가 되게
            var up = mode == CamMode.Top || mode == CamMode.Overview ? Vector3.forward : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(dir.normalized, up)) > 0.99f) up = Vector3.forward;
            cam.transform.rotation = Quaternion.LookRotation(dir, up);
        }

        void Want(World w, Elevation el, out Vector3 p, out Vector3 l)
        {
            var car = w.car;
            float gy = el != null ? el.At(car.Center, w.floorI) : 0f;
            gy += car.z;      // 지하차도·고가에서는 차 높이를 따라간다 — 전에는 땅 높이에 남아 터널 안에서는 차가 안 보이고(덮개 위), 고가에서는 다리 밑을 비췄다
            var c = Space3D.W(car.Center, gy);
            var fwd = Space3D.W(car.Dir) - Space3D.W(V2.Zero);
            bool reverse = car.gear == Gear.R;
            switch (mode)
            {
                case CamMode.Top:
                    {
                        // 2D 판과 비슷하게: 차 바로 위 18 m, 가는 방향으로 조금 앞을 보여 준다
                        var ahead = fwd * (car.speed * 0.9f);
                        p = c + ahead + Vector3.up * 18f + Vector3.back * 0.01f;
                        l = c + ahead;
                        break;
                    }
                case CamMode.Driver:
                    {
                        // 운전석 눈높이(어깨 위 0.75 m, 앞 유리 쪽). 후진이면 고개를 돌려 뒤를 본다.
                        var eye = Space3D.W(car.Shoulder + car.Dir * 0.1f, gy + 1.25f);
                        p = eye;
                        l = reverse ? eye - fwd * 10f + Vector3.down * 1.8f : eye + fwd * 10f + Vector3.down * 1.0f;
                        break;
                    }
                case CamMode.Overview:
                    {
                        var b = Stages.Bounds(w.st);
                        var center = Space3D.W(b.Center, 0f);
                        float span = Mathf.Max(b.w, b.h * 1.78f);
                        p = center + Vector3.up * span * 0.95f + Vector3.back * span * 0.25f;
                        l = center;
                        break;
                    }
                default:
                    {
                        // 뒤 위에서 따라가기. 후진이면 차 앞쪽 위로 옮겨 차 뒤 공간이 보이게.
                        float back = 7.5f, height = 6.2f;
                        // (동네 주차장 칸에서 출발하면 낮은 도로용 카메라를 뒤에 선 차들이 가려 화면 아래 1/3 이 차 지붕) 차로에 들어서기 전에는 주차 연습처럼 높이
                        bool inLot = w.roadRun != null && w.roadRun.NotJoined;
                        if (w.st.mode != StageMode.Parking && !inLot) { back = 9f; height = 4.2f; }
                        // (후진하면 차를 앞에서 보는 화면으로 뒤집혀 좌우가 헷갈린다) 후진도 차 뒤 위에서 — 화면 위쪽이 늘 차 앞.
                        //   뒤 공간이 보이게 더 높이 올라가 차 뒤쪽을 내려다본다(뒤는 후방 카메라 창도 같이 뜬다).
                        p = reverse ? c - fwd * 3.5f + Vector3.up * (height + 4.5f) : c - fwd * back + Vector3.up * height;
                        l = reverse ? c - fwd * 3.0f : c + fwd * 2.0f;
                        break;
                    }
            }
        }
    }

    public class SceneRig
    {
        public readonly GameObject root;
        public readonly Elevation el;
        public readonly CarView carView;
        public readonly Overlays overlays;
        public readonly CameraRig cameraRig;
        public readonly Camera rearCam;
        public readonly RenderTexture rearTex;
        public readonly Camera mapCam;
        public readonly RenderTexture mapTex;
        public readonly MirrorRig mirrors;
        public readonly Cockpit cockpit;
        public readonly RearGuide rearGuide;
        public readonly TrafficView trafficView;
        public readonly PedView pedView;
        public readonly CountdownView countdownView;
        /// <summary>화면 거울(Hud)이 지금 보이는지 — GameMain 이 설정·시점을 보고 정한다</summary>
        public bool hudMirrors;
        int frame;
        readonly Light sun;
        GameObject postVolume;
        bool darkNow;
        bool envSet;
        int envWeather = -1, envTime = -1;
        WeatherFx weatherFx;

        SceneRig(World w, Transform parent)
        {
            root = new GameObject("Scene");
            if (parent != null) root.transform.SetParent(parent, false);
            Dc.Lap(null);
            el = new Elevation(w.st); Dc.Lap("elevation");
            StageBuilder.Build(w.st, el, root.transform); Dc.Lap("stage-rest");
            // 동네: 가로수·가로등·보도 시설물도 부딪히는 물체로(예전에는 차가 나무를 뚫고 지나갔다)
            if (w.st.road?.region != null)
                foreach (var (sp, half, kind) in CityBuilder.Solids)
                    w.obstacles.Add(new Obstacle { poly = Geom.RectPoly(sp, half * 2f, half * 2f, 0f), c = sp, r = half * 1.5f, kind = kind });
            Dc.Lap("solids");
            carView = new CarView(w.car, root.transform);
            overlays = new Overlays(w, el, root.transform);
            overlays.showInner = w.st.id == 101;
            cameraRig = new CameraRig(root.transform);
            // 해(그림자)
            var sg = new GameObject("Sun");
            sg.transform.SetParent(root.transform, false);
            sun = sg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = Prefs.Quality == 0 ? LightShadows.None : LightShadows.Soft;   // 가볍게: 그림자 끔
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            Look.MainCamera(cameraRig.cam);
            // 후방 카메라(R 일 때 화면 구석에 보인다): 뒤 범퍼 위에서 아래쪽 뒤를 본다
            rearTex = new RenderTexture(480, 270, 16);
            var rc = new GameObject("RearCamera");
            rc.transform.SetParent(carView.root.transform, false);
            rc.transform.localPosition = new Vector3(0f, 1.05f, -w.car.length / 2f + 0.05f);
            rc.transform.localRotation = Quaternion.Euler(28f, 180f, 0f);
            rearCam = rc.AddComponent<Camera>();
            rearCam.fieldOfView = 100f;
            rearCam.nearClipPlane = 0.05f;
            rearCam.farClipPlane = 60f;
            rearCam.targetTexture = rearTex;
            rearCam.depth = -10f;
            rearCam.enabled = false;
            // 위에서 본 지도(운전석 가운데 화면): 북쪽 위, 차를 따라간다
            mapTex = new RenderTexture(560, 252, 16);
            var mc = new GameObject("MapCamera");
            mc.transform.SetParent(root.transform, false);
            mapCam = mc.AddComponent<Camera>();
            mapCam.orthographic = true;
            mapCam.orthographicSize = w.st.mode == StageMode.Parking ? 16f : 40f;
            mapCam.nearClipPlane = 0.5f;
            mapCam.farClipPlane = 200f;
            mapCam.targetTexture = mapTex;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = DcAssets.Hex("#E8EBF0");
            mapCam.depth = -10f;
            mapCam.enabled = false;
            // 거울 카메라 3대 + 운전석 실내(거울 판·핸들·계기판·가운데 화면)
            Dc.Lap("car-cameras");
            mirrors = new MirrorRig(w.car, carView.root.transform, Prefs.Quality == 0);
            cockpit = new Cockpit(w.car, carView.root.transform, mirrors);
            cameraRig.carRoot = carView.root.transform;
            rearGuide = new RearGuide(w.car, carView.root.transform);
            if (w.traffic != null) { trafficView = new TrafficView(root.transform, el); trafficView.Tick(w.traffic); }
            if (w.crossPeds != null) { pedView = new PedView(root.transform, el); pedView.Tick(w.crossPeds, 0f); }
            if (w.st.road?.region != null && w.st.road.region.countdowns.Count > 0) { countdownView = new CountdownView(root.transform, w.st.road, el.At(V2.Zero, 0)); countdownView.Tick(w.t, 1f); }
            Dc.Lap("cockpit-traffic-peds");
            mapCam.cullingMask = ~(1 << RearGuide.Layer);
            foreach (var mc2 in new[] { mirrors.left, mirrors.right, mirrors.center }) mc2.cullingMask = ~((1 << RearGuide.Layer) | (1 << Cockpit.RearCabinLayer));   // 뒷좌석은 거울에 안 찍히게
            cameraRig.eyeLocal = cockpit.eyeLocal + Prefs.SeatOffset;
            foreach (var aux in new[] { rearCam, mapCam, mirrors.left, mirrors.right, mirrors.center }) Look.AuxCamera(aux);
        }

        public static SceneRig Build(World w, Transform parent) => new SceneRig(w, parent);

        /// <summary>매 프레임: 차·안내·카메라·조명(지하/지상)을 지금 상태에 맞춘다.</summary>
        /// <summary>계기판 왼쪽 작은 글(서킷 바퀴·제한 속도 등). GameMain 이 채운다.</summary>
        public string info = "";

        public void Tick(World w, float dt, bool brake)
        {
            var car = w.car;
            float gy = el.At(car.Center, w.floorI);
            // 경사로 기울기(앞뒤 높이 차)
            float yf = el.At(car.FrontBumperCenter, w.floorI), yr = el.At(car.RearBumperCenter, w.floorI);
            // 입체 교차: 고가·지하 길 높이를 더한다(앞뒤 범퍼 높이로 기울기도)
            var rd = w.st.road;
            if (rd != null && rd.HasGrade)
            {
                gy += car.z;
                yf += rd.HeightAt(car.FrontBumperCenter, car.z);
                yr += rd.HeightAt(car.RearBumperCenter, car.z);
            }
            float pitch = Mathf.Atan2(yf - yr, car.length) * Mathf.Rad2Deg;
            // 과속방지턱: 앞·뒤 바퀴가 올라선 높이만큼 차가 들리고 기운다(빨리 넘으면 운전석 시점도 크게 출렁)
            var rgn = rd?.region;
            if (rgn != null && rgn.humps.Count > 0 && Mathf.Abs(car.z) < 0.5f)
            {
                float hf = rgn.HumpHeight(car.FrontAxle), hr = rgn.HumpHeight(car.pos);
                if (hf > 0f || hr > 0f)
                {
                    gy += (hf + hr) / 2f;
                    pitch += Mathf.Atan2(hf - hr, car.wheelbase) * Mathf.Rad2Deg;
                }
            }
            carView.Sync(car, gy, pitch, brake, dt);
            carView.Signals(car, dt);
            // 와이퍼·앞유리 빗방울(비·눈, 지하 주차장 빼고). 빗방울 판은 운전석 시점에서만
            carView.wipers?.Tick(dt, car.wiper, !darkNow && (Prefs.Weather == 2 || Prefs.Weather == 4), cameraRig.mode == CamMode.Driver ? cameraRig.cam : null);
            bool driver = cameraRig.mode == CamMode.Driver;
            overlays.driverView = driver;
            overlays.Tick(w, dt);
            cockpit.SetActive(driver);
            if (car.gear == Gear.R) cockpit.SetScreen(rearTex, true); else cockpit.SetScreen(mapTex, false);
            cockpit.Tick(car, brake, info);
            rearGuide.Tick(car);
            if (trafficView != null && w.traffic != null) trafficView.Tick(w.traffic);
            if (pedView != null && w.crossPeds != null) pedView.Tick(w.crossPeds, dt);
            countdownView?.Tick(w.t, dt);
            cameraRig.eyeLocal = cockpit.eyeLocal + Prefs.SeatOffset;   // 운전석 시야(설정·게임 중 '시야 조절'에서 바로 바뀐다)
            cameraRig.Tick(w, el, dt);
            bool reverse = car.gear == Gear.R;
            rearCam.enabled = reverse && cameraRig.mode != CamMode.Top;
            mirrors.Tick(driver || hudMirrors);
            // 지도(운전석 가운데 화면, 후진이 아닐 때)는 두 프레임에 한 번
            frame++;
            mapCam.transform.position = Space3D.W(car.Center, gy + 60f);
            mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            mapCam.enabled = driver && !reverse && frame % (perfStep >= 3 ? 4 : 2) == 0;
            bool dark = w.st.floors[Mathf.Clamp(w.floorI, 0, w.st.floors.Count - 1)].dark;
            if (!envSet || dark != darkNow || Prefs.Weather != envWeather || Prefs.TimeOfDay != envTime)
            {
                SetEnvironment(dark);
                // 신호 시계: 설정의 시간에 맞는 실제 운영계획 시간대(아침 08:00·낮 14:00·해질녘 18:30·밤 23:00)
                w.st.road?.SetSignalClock(Stages.SignalClocks[Mathf.Clamp(Prefs.TimeOfDay, 0, Stages.SignalClocks.Length - 1)]);
            }
            weatherFx?.Tick(cameraRig.cam, dt);
        }

        void SetEnvironment(bool dark)
        {
            envSet = true;
            darkNow = dark;
            var cam = cameraRig.cam;
            // 하늘(CC0 HDRI)·안개·따뜻한 낮은 해·후처리(Look)
            // 설정의 날씨·시간(바깥만. 지하 주차장은 늘 실내등)
            int wx = Prefs.Weather, tm = Prefs.TimeOfDay;
            envWeather = wx; envTime = tm;
            float expo = 0f;
            if (dark) Look.Underground(cam, sun); else expo = Look.Outdoor(cam, sun, tm, wx);
            if (postVolume != null) Dc.Kill(postVolume);
            postVolume = Look.PostVolume(root.transform, dark, expo, dark ? 0f : (wx == 1 || wx == 2 ? -18f : (wx == 3 ? -25f : 0f)));
            // 비·눈 알갱이, 젖은 노면(비·눈), 전조등(밤·해질녘 궂은 날·비·안개·눈)
            weatherFx?.Dispose();
            weatherFx = dark ? null : WeatherFx.Make(root.transform, wx);
            DcAssets.SetWet(!dark && (wx == 2 || wx == 4) ? (wx == 2 ? 1f : 0.5f) : 0f);
            // 가로등 빛·다른 차 전조등: 밤, 해질녘(궂은 날), 안개 — 내 차 전조등 노면 빛이 이것을 보므로 먼저
            NightLights.Set(!dark && (tm == 3 || (tm == 2 && wx != 0) || wx == 3));
            carView.SetHeadlights(!dark && (tm == 3 || wx == 2 || wx == 3 || wx == 4 || (tm == 2 && wx != 0)));
            rearCam.clearFlags = cam.clearFlags;
            rearCam.backgroundColor = cam.backgroundColor;
        }

        /// <summary>
        /// 느린 기기에서 자동으로 가볍게(GameMain 의 초당 화면 수 감시). 0 = 설정대로, 1 = 해상도 85%·거울 2프레임에 하나,
        /// 2 = 해상도 70%·그림자·후처리 끔, 3 = 해상도 55%·거울 3프레임에 하나·지도 4프레임에 한 번
        /// </summary>
        public int perfStep;

        public void Degrade(int step)
        {
            perfStep = Mathf.Clamp(step, 0, 3);
            Look.RenderScale(new[] { 1f, 0.85f, 0.7f, 0.55f }[perfStep]);
            mirrors.every = perfStep >= 3 ? 3 : (perfStep >= 1 ? 2 : 1);
            bool heavy = perfStep < 2;
            sun.shadows = heavy && Prefs.Quality > 0 ? LightShadows.Soft : LightShadows.None;
            Look.Post(cameraRig.cam, heavy);
            if (postVolume != null) postVolume.SetActive(heavy);
        }

        /// <summary>화면 사진(DcBuild.Shots)용: 보조 카메라(거울·후방·지도)를 지금 바로 그린다(게임 루프가 없으므로)</summary>
        public void RenderAuxNow(World w)
        {
            mirrors.RenderNow();
            if (w.car.gear == Gear.R) DcRender.Now(rearCam, rearTex);
            else DcRender.Now(mapCam, mapTex);
        }

        public void Dispose()
        {
            if (rearTex != null) rearTex.Release();
            if (mapTex != null) mapTex.Release();
            mirrors.Dispose();
            overlays.Dispose();
            weatherFx?.Dispose();
            Dc.Kill(root);
            // 코드로 만든 메시·재질·텍스처는 물체를 지워도 메모리에 남는다(맵 하나에 수 GB) → 안 쓰는 것을 내린다.
            // (PC 사진 단계가 맵을 22번 지으며 92 GB 까지 불어나 꺼진 원인. 차에서도 스테이지를 바꿀 때마다 쌓였다.)
            Resources.UnloadUnusedAssets();
        }
    }
}
