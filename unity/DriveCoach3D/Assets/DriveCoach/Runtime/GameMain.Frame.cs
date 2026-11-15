// GameMain 의 매 프레임 처리 부분(11/15 코드 정리로 GameMain.cs 에서 나눈 파일). 전체 설명은 GameMain.cs 맨 위.
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
            // 11/10 Pleos 성능 가이드(정차 중 CPU 25% 이하 — 3D 연습은 주차 중에만 돈다): 메뉴·결과·멈춤 화면은 초당 30장,
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
            // (11/13) 가상 운전대: 켜 두면 화면의 원으로 돌린다. 손을 떼면 달리는 동안 가운데로 돌아온다(실제 차의 복원처럼, 서 있으면 그대로)
            // (11/15) 실제 스티어링 휠을 돌린 뒤에는 화면 원을 잡고 있을 때만 화면 원을 따르고, 아니면 실제 각도를 따른다(원도 같이 돈다)
            if (Prefs.VWheel && hud != null && realSteerSeen && !hud.DialHeld)
            {
                hud.DialDeg = realSteer;
                target = realSteer;
            }
            else if (Prefs.VWheel && hud != null)
            {
                // (11/14 베타: 손을 떼면 가운데로 돌아와 후진 주차 중에 끝까지 돌린 채로 둘 수 없다)
                //   주차장과 후진에서는 놓은 자리에 그대로 둔다. 도로·서킷에서 앞으로 달릴 때만 실제 차처럼 천천히 풀린다
                bool selfCenter = world != null && world.st.mode != StageMode.Parking && world.car.speed > 2f;
                if (!hud.DialHeld && selfCenter) hud.DialDeg = Mathf.MoveTowards(hud.DialDeg, 0f, Mathf.Min(240f, world.car.speed * 40f) * dt);
                target = hud.DialDeg;
            }
            autoStop = false;
            if (AutoOn && world.state == PlayState.Play)
            {
                if (auto == null) auto = new AutoDrive(1, autoPlan);
                if (world.car.gear == Gear.P || world.car.gear == Gear.N) world.Shift(Gear.D, true);
                // (11/13) 핸들 + 속도를 함께(신호·굽은 길·빨간불 우회전 일시정지·방향지시등). 순항 50 km/h, 제한속도·앞차는 AutoSpeed 가 본다
                target = auto.Drive(world, dt, 50f, out autoAccel, out autoBrake, out autoBrakeAmount);
                // 부딪혀 섰으면 잠깐 R 로 물러났다가 다시 D
                if (auto.Reverse && world.car.gear != Gear.R) world.Shift(Gear.R, true);
                else if (!auto.Reverse && world.car.gear == Gear.R) world.Shift(Gear.D, true);
            }
            steer = Mathf.MoveTowards(steer, Mathf.Clamp(target, -maxDeg, maxDeg), 900f * dt);
            brakeOn = realBrake || (Prefs.VBrake ? (hud != null && hud.BrakeHeld) : screenBrake) || Input.GetKey(KeyCode.Space) || autoStop;
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
            // (11/11) 옆 동(앱에 든 동)이 가까워지면 그 동 지도를 뒤에서 미리 읽어 둔다 — 1초에 한 번, 지금 자리·80 m 앞·200 m 앞이 옆 동 안인지 본다
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
            // (11/11 대표) 동 사이 이동: 옆 동(앱에 들어 있는 동) 안으로 들어서면 그 동 지도로 바꿔 이어 달린다
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
            model.vWheel = Prefs.VWheel; model.vAccel = Prefs.VAccel; model.vBrake = Prefs.VBrake; model.autoDrive = AutoOn;
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

        /// <summary>5초마다 초당 화면 수를 logcat 에 남긴다(자동 검수가 보고서에 옮김)</summary>
        void LogFps()
        {
            fpsFrames++;
            fpsTime += Time.unscaledDeltaTime;
            Governor(Time.unscaledDeltaTime);
            if (fpsTime < 5f) return;
            string where = world == null ? "menu" : $"{curKind}:{curId}";
            Debug.Log($"DC3D fps={fpsFrames / fpsTime:0.0} screen={where} cam={camMode} mirrors={(scene != null && scene.hudMirrors)} quality={Prefs.Quality} perf={(scene != null ? scene.perfStep : 0)} gpu={SystemInfo.graphicsDeviceName}");
            // (11/11) 기기에서의 메모리 속: 유니티가 쓰는 양 / 잡아 둔 양 / C# 쓰는 양 / C# 잡아 둔 양(MB) — 어디를 줄일지 보려고
            Debug.Log($"DC3D mem unityUsed={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} unityReserved={UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20} monoUsed={UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20} monoHeap={UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} gfx={UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20} screen={where}");
            fpsFrames = 0;
            fpsTime = 0f;
        }

        // 11/8: 에뮬레이터에서 초당 5장(검수). 장면이 시작되고 2초 뒤부터 2초씩 재서, 22장 아래면 한 단계 가볍게(최대 3),
        // 20초 넘게 55장 넘으면 한 단계 되돌린다. 단계는 다음 판에도 이어 쓴다(같은 기기).
        static int perfCarry;
        float govT, govFrames, govAge, govGood;
        SceneRig govScene;

        void Governor(float dt)
        {
            if (scene == null) { govScene = null; return; }
            if (govScene != scene) { govScene = scene; govT = govFrames = govAge = govGood = 0f; scene.Degrade(perfCarry); }
            govAge += dt;
            if (govAge < 2f) return;
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
        /// 이번 프레임의 액셀·브레이크(11/8). 서킷·도로: 실제 액셀이 있으면 그것, 없으면 자동 속도(핸들만 돌리기).
        /// 주차: 크리프(실제 액셀이 있으면 조금 더). 사람 브레이크(실제 페달·화면 버튼·스페이스)가 언제나 이긴다. PC: ↑(W) 는 액셀 끝까지.
        /// </summary>
        void Drive(out float accel, out bool brake, out float brakeAmount)
        {
            accel = 0f;
            brake = brakeOn;
            brakeAmount = 1f;
            bool roadLike = world.st.mode != StageMode.Parking;
            world.autoSpeed = roadLike && !UsePedal;
            bool autoOn = AutoOn;
            if (!autoOn && Prefs.VAccel)      // (11/13) 가상 액셀: 누르는 동안만 가속(자동 속도는 끈다). 브레이크가 이긴다
            {
                world.autoSpeed = false;
                accel = hud != null && hud.AccelHeld && !brakeOn ? 1f : 0f;
                return;
            }
            if (autoOn) world.autoSpeed = true;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) { accel = 1f; return; }
            if (autoOn && auto != null && !brakeOn)
            {
                accel = autoAccel; brake = autoBrake; brakeAmount = autoBrake ? autoBrakeAmount : 1f;
                return;
            }
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
            // (11/14 베타: 가상 액셀을 켜면 누를 때만 가는데 "자동 보통"이라고 떠 있다)
            if (Prefs.VAccel) return "화면 가속 페달";
            int lv = speedChoice == 3 ? 1 : Mathf.Clamp(speedChoice, 0, 2);
            return "속도 자동(" + AutoSpeed.LevelNames[lv] + ")";   // (11/14 베타 4차: "자동 보통"이 무슨 뜻인지 모르겠다)
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
        /// (11/14 베타: 차가 왜 서 있는지 화면에 안 나온다) 서 있는 까닭: 신호·보행자·앞차(11/16: 닿아 선 것은 말하지 않음). 정보 칸 끝에 " · 까닭" 으로 붙인다.
        /// 사람이 브레이크를 밟고 있거나 P·N 이면 붙이지 않는다(까닭이 뻔하다).
        /// </summary>
        string StopWhy()
        {
            var car = world.car;
            if (world.state != PlayState.Play) return "";
            // (11/16) 닿아 서 있을 때는 까닭을 붙이지 않는다.
            if (world.Touching) return "";
            if (Mathf.Abs(car.speed) > 0.3f) { lastMoveT = world.t; return ""; }
            if (world.t - lastMoveT < 1.2f || car.gear == Gear.P || car.gear == Gear.N) return "";
            if (AutoOn && auto != null)
            {
                string why = auto.Why ?? "";
                if (why.StartsWith("red-wait") || why.Contains("red=1")) return ", 신호 대기";
                if (why.StartsWith("ped-wait") || why.Contains("ped=1")) return ", 보행자 대기";
                return string.IsNullOrEmpty(world.autoWhy) ? "" : ", " + world.autoWhy;
            }
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
                // (11/15 베타 5A: "1/2 바퀴"가 반 바퀴로도 읽힌다)
                return $"{kmh} km/h, {cr.def.laps}바퀴 중 {lap}번째, {Hud.Clock(cr.LapTime(world.t))}" + StopWhy();
            }
            if (world.roadRun != null)
            {
                float lim = world.roadRun.limitKmh;      // 0 = 확인된 제한속도 없음 → 표시하지 않는다(11/13 대표)
                if (lim <= 0f) return $"{kmh} km/h" + StopWhy();
                string col = kmh > lim + 0.5f ? "#D93025" : "#111111";
                return $"<color={col}>{kmh}</color> / 제한 {lim:0} km/h" + StopWhy();
            }
            // (11/14 베타: 주차 화면에 속도가 안 나온다)
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
            if (AutoOn && auto != null) { car.turnSignal = auto.Blinker; car.signalKnown = true; }      // 자동 운전: 꺾기 30 m 앞에서 방향지시등
            car.headlight = connected ? realLight : -1;
            car.wiper = connected ? realWiper : -1;
            car.belt = connected ? realBelt : -1;
        }
    }
}
