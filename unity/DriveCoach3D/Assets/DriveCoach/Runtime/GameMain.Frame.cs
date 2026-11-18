// GameMain: 매 프레임 처리
using System;
using System.Globalization;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    public partial class GameMain
    {
        // ───────────────────────── 매 프레임 ─────────────────────────

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            LogFps();
            // Pleos 성능 가이드(정차 중 CPU 25% 이하, 3D 연습은 주차 중에만 돈다): 메뉴·결과·멈춤 화면은 초당 30장,
            // 달리는 중에는 그래픽 "높게"일 때만 60장, 그 밖에는 30장(자가 검사에서 40장 안팎일 때 CPU 26.7% 였다).
            int wantFps = (world != null && scene != null && world.state == PlayState.Play && !paused && Prefs.Quality >= 2) ? 60 : 30;
            if (Application.targetFrameRate != wantFps) Application.targetFrameRate = wantFps;
            if (autoExitAt > 0f && Time.realtimeSinceStartup >= autoExitAt) { autoExitAt = -1f; CloseGame(); }
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
            Keys();
            float maxDeg = world != null ? world.car.maxSteerDeg : spec.maxSteeringWheelDeg;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) keySteer = Mathf.Max(-maxDeg, keySteer - 420f * dt);
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) keySteer = Mathf.Min(maxDeg, keySteer + 420f * dt);
            float target = connected ? realSteer : keySteer;
            if (connected && !realSteerSeen)
            {
                if (float.IsNaN(realSteerBase)) realSteerBase = realSteer;
                else if (Mathf.Abs(realSteer - realSteerBase) > 5f) realSteerSeen = true;
            }
            // 가상 운전대: 켜 두면 화면의 원으로 돌린다. 손을 떼면 달리는 동안 가운데로 돌아온다(실제 차의 복원처럼, 서 있으면 그대로)
            // 실제 스티어링 휠을 돌린 뒤에는 화면 원을 잡고 있을 때만 화면 원을 따르고, 아니면 실제 각도를 따른다(원도 같이 돈다)
            if (Prefs.VWheel && hud != null && realSteerSeen && !hud.DialHeld)
            {
                hud.DialDeg = realSteer;
                target = realSteer;
            }
            else if (Prefs.VWheel && hud != null)
            {
                // 후진 주차 중에 끝까지 돌린 채로 둘 수 있게:
                //   주차장과 후진에서는 놓은 자리에 그대로 둔다. 도로·서킷에서 앞으로 달릴 때만 실제 차처럼 천천히 풀린다
                bool selfCenter = world != null && world.st.mode != StageMode.Parking && world.car.speed > 2f;
                if (!hud.DialHeld && selfCenter) hud.DialDeg = Mathf.MoveTowards(hud.DialDeg, 0f, Mathf.Min(240f, world.car.speed * 40f) * dt);
                target = hud.DialDeg;
            }
            steer = Mathf.MoveTowards(steer, Mathf.Clamp(target, -maxDeg, maxDeg), 900f * dt);
            brakeOn = realBrake || (Prefs.VBrake ? (hud != null && hud.BrakeHeld) : screenBrake) || Input.GetKey(KeyCode.Space);
            paused = settingsPaused || (connected && (realGear == "R" || realGear == "N" || realGear == "D" || realSpeed > 1f));
            if (world == null || scene == null) return;

            Switches();
            if (!paused)
            {
                Drive(out float accel, out bool brake, out float brakeAmount);
                world.Update(dt, steer, brake, accel, brakeAmount);
            }
            if (world.state != PlayState.Play && !savedResult) SaveResult();
            if (crossTestAt > 0f && Time.realtimeSinceStartup >= crossTestAt && curKind == "region" && !loadingRegion && world.roadRun != null)
            {
                crossTestAt = -1f;
                CrossTestJump();
            }
            // 옆 동(앱에 든 동)이 가까워지면 그 동 지도를 뒤에서 미리 읽어 둔다. 1초에 한 번, 지금 자리·80 m 앞·200 m 앞이 옆 동 안인지 본다
            if (curKind == "region" && !loadingRegion && (preloadT -= dt) <= 0f)
            {
                preloadT = 1f;
                var rdNow = RegionCatalog.Load(curId);
                if (rdNow != null)
                    foreach (float ahead in PreloadAhead)
                    {
                        var nbA = rdNow.NeighborAt(world.car.Center + world.car.Dir * ahead);
                        if (nbA == null || string.IsNullOrEmpty(nbA.file)) continue;
                        int pi = RegionCatalog.Entries.FindIndex(en => en.file == nbA.file);
                        if (pi >= 0 && pi != curId) { RegionCatalog.Preload(pi); break; }
                    }
            }
            // 동 사이 이동: 옆 동(앱에 들어 있는 동) 안으로 들어서면 그 동 지도로 바꿔 이어 달린다
            if (curKind == "region" && !loadingRegion && world.roadRun != null && !string.IsNullOrEmpty(world.roadRun.wantRegion))
            {
                string wantFile = world.roadRun.wantRegion;
                world.roadRun.wantRegion = null;
                int ni = RegionCatalog.Entries.FindIndex(en => en.file == wantFile);
                if (ni >= 0 && ni != curId) { StartCoroutine(CrossTo(ni)); return; }
            }

            // 고개: 끄는 동안 그대로, 놓으면 앞으로 천천히 돌아온다. PC 는 Q/E.
            bool q = Input.GetKey(KeyCode.Q), e = Input.GetKey(KeyCode.E);
            if (q || e) { lookYaw = Mathf.MoveTowards(lookYaw, q ? -80f : 80f, 240f * dt); }
            else if (!lookHeld)
            {
                float k = 1f - Mathf.Exp(-4f * dt);
                lookYaw = Mathf.Lerp(lookYaw, 0f, k);
                lookPitch = Mathf.Lerp(lookPitch, 0f, k);
            }
            scene.cameraRig.lookYaw = lookYaw;
            scene.cameraRig.lookPitch = lookPitch;

            bool mirrorsOn = MirrorsVisible();
            scene.hudMirrors = mirrorsOn;
            scene.info = CockpitInfo();
            scene.Tick(world, dt, brakeOn);
            beeper.Tick(world, sensorSound && !paused, dt);

            model.w = world;
            model.tag = curKind == "stage" ? curId.ToString() : "";
            model.brakeOn = brakeOn;
            model.vWheel = Prefs.VWheel; model.vAccel = Prefs.VAccel; model.vBrake = Prefs.VBrake;
            model.steerDeg = steer; model.maxSteerDeg = world.car.maxSteerDeg;
            model.paused = paused && !settingsPaused;     // 설정 창 때문에 멈춘 것은 "실제 차를 P에" 안내를 띄우지 않는다

            model.canNext = NextOf(curKind, curId).kind != null;
            model.kind = curKind;
            model.realGear = realGear;
            model.cam = camMode;
            model.mirrors = mirrorsOn;
            model.driverView = camMode == CamMode.Driver;
            model.info = HudInfo();
            model.blink = scene.carView.BlinkNow(world.car);
            hud.Tick(model);
        }

        /// <summary>판마다 census 를 한 번만 찍으려고 센다(StartSession 에서 0)</summary>
        internal static int censusTick;

        /// <summary>5초마다 초당 화면 수를 logcat 에 남긴다</summary>
        void LogFps()
        {
            fpsFrames++;
            fpsTime += Time.unscaledDeltaTime;
            Governor(Time.unscaledDeltaTime);
            if (fpsTime < 5f) return;
            string where = world == null ? "menu" : $"{curKind}:{curId}";
            Debug.Log($"DC3D fps={fpsFrames / fpsTime:0.0} screen={where} cam={camMode} mirrors={(scene != null && scene.hudMirrors)} quality={Prefs.Quality} perf={(scene != null ? scene.perfStep : 0)} gpu={SystemInfo.graphicsDeviceName}");
            // 기기에서의 메모리 속: 유니티가 쓰는 양 / 잡아 둔 양 / C# 쓰는 양 / C# 잡아 둔 양(MB). 어디를 줄일지 보려고
            Debug.Log($"DC3D mem unityUsed={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} unityReserved={UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20} monoUsed={UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20} monoHeap={UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} gfx={UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20} gfxUsed={Dc.GfxMB()} sysUsed={Dc.SysMB()} screen={where}");
            // 판을 연 뒤 두 번째 줄(약 10초 뒤, 짓기·첫 장면 끝)에 한 번: 물체·메시·smaps 요약
            //   주행 중에 찍으면 한 번 90 ms 남짓 멈춘다(census + /proc/self/smaps 읽기) → 진단할 때만(Dc.CensusInPlay). 동네를 지은 직후 한 번은 그대로 찍는다(StageBuilder)
            if (Dc.CensusInPlay && world != null && ++censusTick == 2) Dc.Census(where);
            fpsFrames = 0;
            fpsTime = 0f;
        }

        // 에뮬레이터에서 초당 5장. 장면이 시작되고 2초 뒤부터 2초씩 재서, 22장 아래면 한 단계 가볍게(최대 3),
        // 20초 넘게 55장 넘으면 한 단계 되돌린다. 단계는 다음 판에도 이어 쓴다(같은 기기).
        // 판을 짓는 동안과 그 뒤 잠깐은 재지 않는다(판을 지은 장은 5~10초라, 재면 열 때마다 단계가 내려가고 다음 판까지 이어진다).
        //   판이 바뀐 장은 버리고, 동네를 여는 중(loadingRegion)이거나 판을 연 뒤 GovWarm 초 안의 긴 장(GovLongFrame 초 넘게)이면
        //   그때부터 GovSettle 초를 더 기다린 뒤에 잰다. 판을 연 뒤 GovWarm 초가 지나면 긴 장도 그대로 잰다(정말 느린 기기는 단계가 내려간다).
        const float GovSettle = 2f, GovLongFrame = 0.5f, GovWarm = 15f;
        static int perfCarry;
        float govT, govFrames, govGood, govQuietUntil, govSceneAt;
        SceneRig govScene;

        /// <summary>지금부터 GovSettle 초 동안 화면 수를 재지 않는다(재던 창도 비운다)</summary>
        void GovQuiet()
        {
            govQuietUntil = Time.realtimeSinceStartup + GovSettle;
            govT = govFrames = 0f;
        }

        void Governor(float dt)
        {
            if (scene == null) { govScene = null; return; }
            float now = Time.realtimeSinceStartup;
            if (govScene != scene) { govScene = scene; govT = govFrames = govGood = 0f; govSceneAt = now; scene.Degrade(perfCarry); GovQuiet(); return; }
            if (loadingRegion || (dt > GovLongFrame && now - govSceneAt < GovWarm)) GovQuiet();
            if (now < govQuietUntil) return;
            govT += dt; govFrames++;
            if (govT < 2f) return;
            float fps = govFrames / govT;
            govT = govFrames = 0f;
            if (fps < 22f && scene.perfStep < 3) { scene.Degrade(scene.perfStep + 1); perfCarry = scene.perfStep; govGood = 0f; Debug.Log($"DC3D perf -> {scene.perfStep} (fps {fps:0.0})"); }
            else if (fps > 55f && scene.perfStep > 0) { govGood += 2f; if (govGood >= 20f) { scene.Degrade(scene.perfStep - 1); perfCarry = scene.perfStep; govGood = 0f; } }
            else govGood = 0f;
        }

        /// <summary>실제 액셀을 쓰나(설정이 "실제 액셀"이고 차가 페달 값을 준 적이 있을 때)</summary>
        bool UsePedal => speedChoice == 3 && pedalSeen && realPedal >= 0f;

        /// <summary>
        /// 이번 프레임의 액셀·브레이크. 서킷·도로: 실제 액셀이 있으면 그것, 없으면 자동 속도(핸들만 돌리기).
        /// 주차: 크리프(실제 액셀이 있으면 조금 더). 사람 브레이크(실제 페달·화면 버튼·스페이스)가 언제나 이긴다. PC: ↑(W) 는 액셀 끝까지.
        /// </summary>
        void Drive(out float accel, out bool brake, out float brakeAmount)
        {
            accel = 0f;
            brake = brakeOn;
            brakeAmount = 1f;
            bool roadLike = world.st.mode != StageMode.Parking;
            world.autoSpeed = roadLike && !UsePedal;
            if (Prefs.VAccel)      // 가상 액셀: 누르는 동안만 가속(자동 속도는 끈다). 브레이크가 이긴다
            {
                world.autoSpeed = false;
                accel = hud != null && hud.AccelHeld && !brakeOn ? 1f : 0f;
                return;
            }
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) { accel = 1f; return; }
            if (UsePedal) { accel = realPedal; return; }
            if (!roadLike || brakeOn) return;
            int lv = Mathf.Clamp(speedChoice, 0, AutoSpeed.LevelKmh.Length - 1);
            if (speedChoice == 3) lv = 1;   // 실제 액셀을 골랐지만 차가 값을 주지 않으면 "보통"
            var o = AutoSpeed.Control(world, AutoSpeed.LevelKmh[lv]);
            accel = o.accel;
            if (o.brake) { brake = true; brakeAmount = o.brakeAmount; }
        }

        string SpeedTag()
        {
            if (UsePedal) return "실제 가속 페달";
            // 화면 가속 페달을 쓰면 자동 속도가 아니다
            if (Prefs.VAccel) return "화면 가속 페달";
            int lv = speedChoice == 3 ? 1 : Mathf.Clamp(speedChoice, 0, 2);
            return "속도 자동(" + AutoSpeed.LevelNames[lv] + ")";   // 무엇이 자동인지 함께: "속도 자동(보통)"
        }

        bool MirrorsVisible()
        {
            if (world == null) return false;
            switch (hudMirrorMode)
            {
                case HudMirrorMode.DriverOnly: return camMode == CamMode.Driver;
                case HudMirrorMode.Always: return camMode != CamMode.Overview;
                default: return false;
            }
        }

        float lastMoveT;
        /// <summary>
        /// 서 있는 까닭: 신호·보행자·앞차(닿아 선 것은 말하지 않음). 정보 칸 끝에 " · 까닭" 으로 붙인다.
        /// 사람이 브레이크를 밟고 있거나 P·N 이면 붙이지 않는다(까닭이 뻔하다).
        /// </summary>
        string StopWhy()
        {
            var car = world.car;
            if (world.state != PlayState.Play) return "";
            // 닿아 서 있을 때는 까닭을 붙이지 않는다.
            if (world.Touching) return "";
            if (Mathf.Abs(car.speed) > 0.3f) { lastMoveT = world.t; return ""; }
            if (world.t - lastMoveT < 1.2f || car.gear == Gear.P || car.gear == Gear.N) return "";
            if (brakeOn) return "";
            if (!string.IsNullOrEmpty(world.autoWhy) && world.autoSpeed) return ", " + world.autoWhy;
            if (Prefs.VAccel && world.st.mode != StageMode.Parking) return ", 가속 버튼을 누르면 갑니다";
            return "";
        }

        string HudInfo()
        {
            var car = world.car;
            int kmh = Mathf.RoundToInt(Mathf.Abs(car.speed) * 3.6f);
            if (world.circuitRun != null)
            {
                var cr = world.circuitRun;
                int lap = Mathf.Min(cr.lap + 1, cr.def.laps);
                // "2바퀴 중 1번째" 꼴(1/2 은 반 바퀴로도 읽힌다)
                return $"{kmh} km/h, {cr.def.laps}바퀴 중 {lap}번째, {Hud.Clock(cr.LapTime(world.t))}" + StopWhy();
            }
            if (world.roadRun != null)
            {
                float lim = world.roadRun.limitKmh;      // 0 = 확인된 제한속도 없음 → 표시하지 않는다
                if (lim <= 0f) return $"{kmh} km/h" + StopWhy();
                string col = kmh > lim + 0.5f ? "#D93025" : "#111111";
                return $"<color={col}>{kmh}</color> / 제한 {lim:0} km/h" + StopWhy();
            }
            // 주차 화면에도 속도
            return $"{kmh} km/h" + StopWhy();
        }

        string CockpitInfo()
        {
            if (world.circuitRun != null)
            {
                var cr = world.circuitRun;
                return $"{Mathf.Min(cr.lap + 1, cr.def.laps)}번째 바퀴\n{Hud.Clock(cr.LapTime(world.t))}\n{(UsePedal ? "가속 페달" : "자동")}";
            }
            if (world.roadRun != null) return (world.roadRun.limitKmh > 0f ? $"제한 {world.roadRun.limitKmh:0}\n" : "") + (UsePedal ? "가속 페달" : "자동");
            var fl = world.st.floors;
            return fl.Count > 1 ? fl[Mathf.Clamp(world.floorI, 0, fl.Count - 1)].name : "";
        }

        void Keys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) OnButton("gear:P");
            if (Input.GetKeyDown(KeyCode.Alpha2)) OnButton("gear:R");
            if (Input.GetKeyDown(KeyCode.Alpha3)) OnButton("gear:N");
            if (Input.GetKeyDown(KeyCode.Alpha4)) OnButton("gear:D");
            if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.M)) OnButton("view");
            if (Input.GetKeyDown(KeyCode.Return)) OnButton("retry");
            if (Input.GetKeyDown(KeyCode.Z)) OnButton("turn:L");
            if (Input.GetKeyDown(KeyCode.C)) OnButton("turn:R");
            if (Input.GetKeyDown(KeyCode.X)) OnButton("hazard");
        }

        /// <summary>
        /// 방향지시등·비상등·전조등·와이퍼·벨트를 차(Vehicle)에 넣는다. 실제 차 값이 있으면 그것, 없으면 키보드·화면 값.
        /// 키보드·화면 방향지시등은 켠 뒤 60° 넘게 돌았다가 핸들이 거의 풀리면 저절로 꺼진다.
        /// </summary>
        void Switches()
        {
            if (world == null) return;
            var car = world.car;
            if (keyTurn != 0)
            {
                float turned = Mathf.Abs(Mathf.DeltaAngle(keyTurnHeading * Mathf.Rad2Deg, car.heading * Mathf.Rad2Deg));
                if (turned > 60f && Mathf.Abs(car.steerDeg) < 60f) keyTurn = 0;
            }
            if (connected && realTurnKnown) { car.turnSignal = realTurn; car.hazard = realHazard; car.signalKnown = true; }
            else { car.turnSignal = keyTurn; car.hazard = keyHazard; car.signalKnown = keySignalUsed; }
            car.headlight = connected ? realLight : -1;
            car.wiper = connected ? realWiper : -1;
            car.belt = connected ? realBelt : -1;
        }
    }
}
