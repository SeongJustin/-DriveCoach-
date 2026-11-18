// GameMain: 판 진행
using System;
using System.Globalization;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveCoach.Unity
{
    public partial class GameMain
    {
        // ───────────────────────── 진행 ─────────────────────────

        /// <summary>지금 판의 할 일(시작 알림·"할 일" 버튼)</summary>
        /// <summary>한글로 끝나는 문장 끝에 마침표(이미 있으면 그대로)</summary>
        static string Dot(string s) => string.IsNullOrEmpty(s) || s.EndsWith(".") || s.EndsWith("?") || s.EndsWith("!") || s.EndsWith("…") ? (s ?? "") : s + ".";

        string GoalHint()
        {
            if (world == null) return "";
            string steerHow = Prefs.VWheel && !realSteerSeen ? "화면 왼쪽 아래 스티어링 휠을 돌려 방향을 바꿉니다." : "스티어링 휠로 방향을 바꿉니다.";
            if (curKind == "tutorial") return world.StepText;
            if (curKind == "stage") return Dot(world.st.goal) + "\n맞췄으면 선 채로 P를 누르면 판정합니다.";
            if (curKind == "circuit" || curKind == "road" || curKind == "region")
                return (UsePedal ? "브레이크를 밟은 채로 D → 브레이크를 떼고 실제 가속 페달을 밟는 만큼 나아갑니다."
                        : Prefs.VAccel ? "브레이크를 밟은 채로 D → 브레이크를 떼고 화면의 가속 버튼을 누르는 동안 나아갑니다."
                        : "브레이크를 밟은 채로 D → 브레이크를 떼면 출발합니다. 속도는 자동입니다.")
                       + "\n" + steerHow
                       + (curKind == "region" ? " 신호, 차로, 제한 속도를 지키고, 끝내려면 서서 P를 누르십시오." : "")
                       // 주차장에서 출발하면 먼저 꺾어 나가라고(곧장 나가 통로 건너편 차에 부딪히지 않게)
                       + (curKind == "region" && world.st.road?.region != null && world.st.road.region.InParkingLot(world.car.Center) ? "\n주차장 칸에서 출발합니다. 통로에 들어서면 스티어링 휠을 돌려 길 쪽으로 나가십시오." : "");
            return world.st.goal ?? "";
        }

        void OnButton(string id)
        {
            if (id == "goal") { if (world != null) world.Say(GoalHint(), 9f); return; }
            if (id == "settings") { if (world != null) { settingsPaused = true; menu.OpenSettingsInGame(); } return; }
            if (id.StartsWith("stage:")) StartSession("stage", ParseId(id));
            else if (id.StartsWith("tut:")) StartSession("tutorial", ParseId(id));
            else if (id.StartsWith("circuit:")) StartSession("circuit", ParseId(id));
            else if (id.StartsWith("road:")) StartSession("road", ParseId(id));
            else if (id.StartsWith("region:"))
            {
                // "region:동네번호" 또는 "region:동네번호:시작자리번호"(0 = 동네 한가운데, 1.. = 길 이름으로 고른 자리)
                var parts = id.Split(':');
                int ri = parts.Length > 1 && int.TryParse(parts[1], out var a1) ? a1 : 0;
                // "region:동네번호:@x,y[,방향]" = 동네 지도에서 고른 자리(동네 좌표 m). 방향까지 있으면 그 자리·방향 그대로(주차장 칸)
                if (parts.Length > 2 && parts[2].StartsWith("@"))
                {
                    var xy = parts[2].Substring(1).Split(',');
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    var ns = System.Globalization.NumberStyles.Float;
                    if (xy.Length >= 2 && float.TryParse(xy[0], ns, ci, out var mx) && float.TryParse(xy[1], ns, ci, out var my))
                    {
                        float mh = xy.Length >= 3 && float.TryParse(xy[2], ns, ci, out var hh) ? hh : float.NaN;
                        StartCoroutine(LoadRegion(ri, 0, new V2(mx, my), mh));
                        return;
                    }
                }
                int spot = parts.Length > 2 && int.TryParse(parts[2], out var a2) ? a2 : 0;
                StartCoroutine(LoadRegion(ri, spot));
            }
            else if (id == "close") CloseGame();
            else if (id == "menu:road" || id == "menu:practice") { menu.SetMode(id == "menu:road"); ShowMenu(); }
            else if (id == "turn:L" || id == "turn:R")
            {
                int want = id == "turn:L" ? -1 : 1;
                keyTurn = keyTurn == want ? 0 : want;
                keySignalUsed = true;
                if (world != null) keyTurnHeading = world.car.heading;
            }
            else if (id == "hazard") keyHazard = !keyHazard;
            else if (id == "menu") ShowMenu();
            else if (id == "retry" && world != null)
            {
                // 동네는 크다: 옛 장면을 먼저 내린 뒤 다시 짓는다(두 벌이 함께 메모리에 올라가지 않게)
                if (curKind == "region") StartCoroutine(SwapRegion(curId, "다시 여는 중…", () => { if (pickedRegion == curId && pickedStart.HasValue) { regionStart = pickedStart; regionHeading = pickedHeading; regionExact = pickedExact; } }, null));
                else StartSession(curKind, curId);
            }
            else if (id == "next" && world != null)
            {
                var n = NextOf(curKind, curId);
                if (n.kind == null) ShowMenu(); else StartSession(n.kind, n.id);
            }
            else if (id.StartsWith("gear:") && world != null)
            {
                if (!Enum.TryParse<Gear>(id.Substring(5), out var g)) return;
                var msg = world.Shift(g, brakeOn);
                if (!string.IsNullOrEmpty(msg)) world.Say(msg);
            }
            else if (id == "brake") { if (!Prefs.VBrake) screenBrake = !screenBrake; }
            else if (id == "view" && world != null) CycleView();
        }

        static int ParseId(string id) => int.TryParse(id.Substring(id.IndexOf(':') + 1), out var n) ? n : 1;

        /// <summary>다음 판: 핸들 감각 → 서킷 1 → 서킷 2 → 직각 → 평행 → 13단계, 단계는 차례로. 없으면 kind=null</summary>
        static (string kind, int id) NextOf(string kind, int id)
        {
            switch (kind)
            {
                case "stage": return id < Stages.COUNT ? ("stage", id + 1) : (null, 0);
                case "tutorial":
                    if (id == 1) return ("circuit", 1);
                    return id < 3 ? ("tutorial", id + 1) : ("stage", 13);
                case "circuit": return id < Stages.CIRCUIT_COUNT ? ("circuit", id + 1) : ("tutorial", 2);
                default: return (null, 0);
            }
        }

        void CycleView()
        {
            camMode = camMode == CamMode.Chase ? CamMode.Driver
                    : camMode == CamMode.Driver ? CamMode.Top
                    : camMode == CamMode.Top ? CamMode.Overview : CamMode.Chase;
            // 시점 버튼은 판 안에서만 바꾼다(설정의 "처음 시점"은 그대로). 다음 판은 설정의 처음 시점으로
            lookYaw = lookPitch = 0f;
            scene.cameraRig.Snap(camMode, world);
        }

        /// <summary>
        /// 달리던 그대로 옆 동 지도로 넘어간다: 같은 실제 위치(위도·경도)·방향·속도·기어·기록을 잇는다. 지도를 바꾸는 동안 "넘어가는 중" 표시.
        /// </summary>
        System.Collections.IEnumerator CrossTo(int ni)
        {
            if (loadingRegion || world == null) yield break;
            var from = RegionCatalog.Load(curId);
            var to = RegionCatalog.Load(ni);                  // 가까워질 때 뒤에서 미리 읽어 둔 것(없으면 여기서 읽는다)
            if (from == null || to == null) yield break;
            var car0 = world.car;
            var p0 = car0.Center; float h0 = car0.heading * Mathf.Rad2Deg, v0 = car0.speed; var g0 = car0.gear;
            var rr0 = world.roadRun;
            string toName = RegionCatalog.Entries[ni].name;
            // 지역 좌표(m, 동쪽 +x · 남쪽 +y) → 위도·경도 → 옆 동 좌표
            double lat = from.originLat - p0.y / 110574.0;
            double lon = from.originLon + p0.x / (System.Math.Cos(from.originLat * System.Math.PI / 180.0) * 111320.0);
            float nx = (float)((lon - to.originLon) * System.Math.Cos(to.originLat * System.Math.PI / 180.0) * 111320.0);
            float ny = (float)((to.originLat - lat) * 110574.0);
            crossing = true;
            yield return SwapRegion(ni, toName + "으로 넘어가는 중…", () => { regionStart = new V2(nx, ny); regionHeading = h0; }, () =>
            {
                if (world == null) return;
                world.car.gear = g0;
                world.car.speed = v0;
                world.roadRun?.CarryFrom(rr0);
                world.Say(toName + "에 들어왔습니다", 2.5f);
                Debug.Log($"DCCROSS done {toName} at {nx:0},{ny:0}");
            });
            crossing = false;
        }

        /// <summary>
        /// 달리던 동네를 내리고 다른(또는 같은) 동네를 연다. 두 동네가 함께 메모리에 오르지 않게(함께 오르면 최대 1,147 MB) 이 순서를 지킨다:
        /// ① 달리던 화면을 사진 한 장으로 깔아 두고(화면이 비지 않게) ② 옛 동네를 먼저 내리고 한 장 기다려 실제로 지워지게 한 뒤
        /// 안 쓰는 것을 정리하고 ③ 새 동네를 짓는다. 화면에는 작은 알림만 뜬다.
        /// </summary>
        System.Collections.IEnumerator SwapRegion(int id, string note, System.Action before, System.Action after)
        {
            if (loadingRegion) yield break;
            loadingRegion = true;
            UnityEngine.UI.Image pill = null; GameObject freeze = null; RenderTexture frt = null;
            if (hud != null)
            {
                pill = UiKit.Card(hud.canvas.transform, "swap", new Color(1f, 1f, 1f, 0.95f), DcAssets.UiLine);
                var t = UiKit.Label(pill.transform, note, 30, DcAssets.UiText, true, TextAnchor.MiddleCenter);
                UiKit.Stretch(t.rectTransform);
                UiKit.Place(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(Mathf.Max(360f, t.preferredWidth + 72f), 68f));
            }
            yield return null;
            try
            {
                if (scene != null && hud != null)
                {
                    frt = new RenderTexture(Mathf.Max(16, Screen.width), Mathf.Max(16, Screen.height), 24);
                    DcRender.Now(scene.cameraRig.cam, frt);
                    freeze = new GameObject("freeze");
                    freeze.transform.SetParent(hud.canvas.transform, false);
                    var ri = freeze.AddComponent<UnityEngine.UI.RawImage>();
                    ri.texture = frt;
                    UiKit.Stretch(ri.rectTransform);
                    freeze.transform.SetAsFirstSibling();      // 단추·알림 아래에 깔린다
                    hud.HideSceneBits();                       // 거울·후방 화면·알림은 감춘다(장면이 내려가면 검게 보인다)
                }
            }
            catch (Exception e) { Debug.LogWarning("freeze: " + e.Message); }
            EndSession();
            // 떠나온 동네 자료(지도·노면)는 새 동네를 짓기 전에 잊는다(짓는 동안 두 동네 자료가 함께 있으면 스크립트 힙 175 MB).
            //   새 동네 것(미리 읽어 둔 것 포함)은 그대로 둔다.
            if (id >= 0 && id < RegionCatalog.Entries.Count)
            {
                RegionCatalog.KeepOnly(RegionCatalog.Entries[id].file);
                // 지은 뒤 노면 자료를 잊으므로("다시"로 같은 동네를 열 때) 노면 파일 해석을 지금 뒤에서 시작한다
                //   아래 정리(UnloadUnusedAssets·GC)와 겹쳐 floors 단계의 1.5~2.5초를 줄인다. 옆 동처럼 이미 읽는 중이면 아무것도 안 한다
                RoadSurface.Preload(RegionCatalog.Entries[id].file + "_surface");
            }
            uiCam.enabled = true;                              // 장면 카메라가 없는 동안 화면을 그릴 카메라
            yield return null;
            var unload = Resources.UnloadUnusedAssets();
            while (unload != null && !unload.isDone) yield return null;
            GC.Collect();
            try
            {
                before?.Invoke();
                StartSession("region", id);
                if (id >= 0 && id < RegionCatalog.Entries.Count) RegionCatalog.KeepOnly(RegionCatalog.Entries[id].file);   // 떠나온 동네 자료는 잊는다
                after?.Invoke();
            }
            finally
            {
                loadingRegion = false;
                if (pill != null) Dc.Kill(pill.gameObject);
                if (freeze != null) Dc.Kill(freeze);
                if (frt != null) { frt.Release(); Dc.Kill(frt); }
            }
        }

        /// <summary>자가 검사용: 앱에 들어 있는 옆 동의 안쪽(경계에서 60 m 남짓) 길 위로 차를 옮긴다 → 평소와 같은 길로 동 사이 이동이 일어난다</summary>
        void CrossTestJump()
        {
            var rd = RegionCatalog.Load(curId);
            if (rd == null) { Debug.Log("DCCROSS no region"); return; }
            foreach (var nb in rd.neighbors)
            {
                if (string.IsNullOrEmpty(nb.file) || nb.poly.Length < 3) continue;
                if (RegionCatalog.Entries.FindIndex(en => en.file == nb.file) < 0) continue;
                foreach (var r in rd.roads)
                    foreach (var p in r.pts)
                    {
                        bool deep = Geom.PointInPolygon(p, nb.poly);
                        for (int k = 0; deep && k < 4; k++)
                            deep = Geom.PointInPolygon(p + new V2(k == 0 ? 60f : (k == 1 ? -60f : 0f), k == 2 ? 60f : (k == 3 ? -60f : 0f)), nb.poly);
                        if (!deep) continue;
                        if (crossTestPos == null)
                        {
                            // 먼저 미리 읽기를 걸고(실제 주행에서 경계에 다가갈 때처럼) 5초 뒤에 옮긴다
                            RegionCatalog.Preload(RegionCatalog.Entries.FindIndex(en => en.file == nb.file));
                            crossTestPos = p;
                            crossTestAt = Time.realtimeSinceStartup + 5f;
                            return;
                        }
                        world.car.pos = crossTestPos.Value; world.car.speed = 0f;
                        crossTestPos = null;
                        Debug.Log($"DCCROSS jump to {nb.name} at {p.x:0},{p.y:0}");
                        return;
                    }
            }
            Debug.Log("DCCROSS no neighbour road found");
        }

        bool loadingRegion;
        /// <summary>실제 동네는 지도가 커서 여는 데 몇 초~수십 초가 걸린다. 누르자마자 "불러오는 중"을 띄우고 다음 장면에서 연다.</summary>
        /// <summary>실제 동네를 열 때의 시작 자리(없으면 동네 한가운데)와 바라볼 방향 힌트. StartSession("region") 이 한 번 쓰고 비운다</summary>
        V2? regionStart; float regionHeading = float.NaN;
        bool regionExact;          // 차로에 맞추지 않고 그 자리·방향 그대로(주차장 칸 출발)
        /// <summary>맵 고르기에서 고른 시작 위치. "다시"를 누르면 같은 자리에서 다시 시작한다</summary>
        V2? pickedStart; int pickedRegion = -1; float pickedHeading = float.NaN; bool pickedExact;

        System.Collections.IEnumerator LoadRegion(int id, int spot = 0, V2? at = null, float atHeading = float.NaN)
        {
            if (loadingRegion) yield break;
            loadingRegion = true;
            var veil = UiKit.Card(menu.canvas.transform, "loading", new Color(DcAssets.Bg.r, DcAssets.Bg.g, DcAssets.Bg.b, 0.92f));
            UiKit.Stretch(veil.rectTransform);
            var t = UiKit.Label(veil.transform, "불러오는 중...", 40, DcAssets.UiText, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(t.rectTransform);
            yield return null;
            yield return null;
            regionStart = null; regionHeading = float.NaN; regionExact = false;
            if (at.HasValue)
            {
                regionStart = at.Value;                        // 지도에서 고른 자리. 방향이 있으면(주차장 칸) 그대로, 없으면 가장 가까운 차로에
                if (!float.IsNaN(atHeading)) { regionHeading = atHeading; regionExact = true; }
            }
            else if (spot == 0)
            {
                // 자리를 고르지 않았으면 동네 기본 자리에서 가장 가까운 주차장 칸
                var rd0 = RegionCatalog.Load(id);
                var ps = rd0?.ParkingStarts();
                if (rd0 != null && ps != null && ps.Count > 0)
                {
                    var c0 = rd0.boundary.Length >= 3 ? Geom.PolyCenter(rd0.boundary) : V2.Zero;     // 동 한가운데
                    RegionData.ParkStart best = null; float bd = float.MaxValue;
                    foreach (var q in ps) { float d = q.c.DistanceTo(c0); if (d < bd) { bd = d; best = q; } }
                    regionStart = best.c; regionHeading = best.headingDeg; regionExact = true;
                }
            }
            else if (spot > 0)
            {
                var rd0 = RegionCatalog.Load(id);
                var spots = rd0?.StartSpots();
                if (spots != null && spot - 1 < spots.Count) regionStart = spots[spot - 1].p;
            }
            pickedStart = regionStart; pickedRegion = id; pickedHeading = regionHeading; pickedExact = regionExact;
            // 목록에서 동네를 고르면 그 동네 자료만 남기고 앞서 읽은 다른 동네 자료는 비운다
            if (id >= 0 && id < RegionCatalog.Entries.Count) RegionCatalog.KeepOnly(RegionCatalog.Entries[id].file);
            bool opened = false;
            try { StartSession("region", id); opened = true; }
            finally { loadingRegion = false; if (!opened && veil != null) Dc.Kill(veil.gameObject); }
            // 연 직후 몇 장은 느리고(에뮬레이터에서는 한 장에 몇 초) 차·HUD 가 제자리를 잡기 전 모습(차가 없고 가운데 흰 네모)이 보이지 않게
            //   가림막을 주행 화면 쪽으로 옮겨 네 장 더 둔다.
            if (veil != null && hud != null)
            {
                veil.transform.SetParent(hud.canvas.transform, false);
                veil.transform.SetAsLastSibling();
                UiKit.Stretch(veil.rectTransform);
            }
            for (int i = 0; i < 4; i++) yield return null;
            if (veil != null) Dc.Kill(veil.gameObject);
            GovQuiet();      // 가림막을 걷은 뒤부터 잠깐 기다렸다가 화면 수를 잰다(가림막 아래 첫 그리기 장은 재지 않는다)
        }

        void StartSession(string kind, int id)
        {
            Dc.Lap(null);
            EndSession(); Dc.Lap("end-session");
            censusTick = 0;
            // 주차·서킷 연습에서는 동네 지도·노면 자료가 필요 없다(동네로 돌아가면 다시 읽는다)
            if (kind != "region") RegionCatalog.KeepOnly("");
            Stage st;
            switch (kind)
            {
                case "stage": st = Stages.Get(Mathf.Clamp(id, 1, Stages.COUNT)); break;
                case "tutorial": st = Stages.Tutorial(Mathf.Clamp(id, 1, 3)); break;
                case "circuit": st = Stages.Circuit(Mathf.Clamp(id, 1, Stages.CIRCUIT_COUNT)); break;
                case "region":
                    {
                        var rd = RegionCatalog.Load(id);
                        if (rd == null) { ShowMenu(); return; }
                        // 출발 자리가 길가 버스전용차로에 서지 않게 노면의 파란 칠을 지역 자료에 넘긴다(읽은 노면은 길을 지을 때 그대로 다시 쓴다)
                        if (rd.busPaint == null) rd.busPaint = RoadSurface.Load(rd.surfaceFile)?.BusPaint();
                        st = !regionStart.HasValue ? Stages.Region(rd, 400 + id)
                           : regionExact ? Stages.RegionExact(rd, 400 + id, regionStart.Value, regionHeading)
                           : Stages.Region(rd, 400 + id, regionStart.Value, regionHeading);
                        regionStart = null; regionHeading = float.NaN; regionExact = false;
                        // 교통량: 설정의 시간·날씨로
                        //   출근(아침) 1.4배, 낮 1, 퇴근(해질녘) 1.3배, 밤 0.45배, 눈 0.7배, 기준 30 대(내 차 둘레 150~400 m)
                        //   다른 차 없이 만든 판(beta 동네, 0 대)은 그대로 둔다. 건너는 사람도 World 가 만들지 않아 아래 사람 수 조정을 건너뛴다
                        if (st.road != null && st.road.trafficCount > 0)
                        {
                            float k = new[] { 1.4f, 1f, 1.3f, 0.45f }[Prefs.TimeOfDay] * (Prefs.Weather == 4 ? 0.7f : 1f) * new[] { 0.5f, 1f, 1.6f }[Prefs.TrafficLevel];
                            // 지역에 맞는 통행량: 동네마다 배율(index.json traffic), 상업지는 많게, 주거지는 보통
                            var ent = id >= 0 && id < RegionCatalog.Entries.Count ? RegionCatalog.Entries[id] : null;
                            if (ent != null) k *= ent.trafficK;
                            st.road.trafficCount = Mathf.Clamp(Mathf.RoundToInt(30 * k), 4, 80);
                        }
                        break;
                    }
                default: st = Stages.RoadPreview(); kind = "road"; id = 1; break;
            }
            curKind = kind;
            curId = id;
            Dc.Lap("stage-data");
            world = new World(st, spec); Dc.Lap("world");
            // 다른 차의 붙는 구간(가로지름·모임)을 불러오는 화면 뒤에서 모두 찾아 둔다. 첫 주행 장에 몰리지 않게
            world.traffic?.PrepareCrossings(); Dc.Lap("traffic-cross");
            // 앞 연습에서 돌려 둔 화면 휠·키보드 핸들이 새 연습·"다시"로 넘어오지 않게:
            //   실제 핸들이 연결된 때와 옆 동으로 이어 달릴 때만 그대로 잇고, 그 밖에는 가운데에서 시작한다
            // 앱 안에서는 차 신호가 늘 와서 connected 지만 핸들은 화면 휠(Prefs.VWheel)이 정하므로, 화면 휠을 쓰면 늘 가운데로
            if (!crossing && (!connected || Prefs.VWheel)) { steer = keySteer = 0f; if (hud != null) hud.DialDeg = 0f; }
            world.car.steerDeg = steer;
            {
                int wx = Prefs.Weather, tm = Prefs.TimeOfDay;
                world.wetRoad = wx == 2 || wx == 4;
                world.needHeadlights = tm == 3 || wx == 2 || wx == 3 || wx == 4 || (tm == 2 && wx != 0);
                // 횡단보도 사람 수: 아침·저녁 많고 밤 적게, 비·눈 줄임
                if (world.crossPeds != null)
                {
                    float pd = new[] { 1.3f, 1f, 1.2f, 0.35f }[Mathf.Clamp(tm, 0, 3)] * (wx == 2 || wx == 4 ? 0.6f : 1f) * new[] { 0f, 0.5f, 1f, 1.8f }[Prefs.PedLevel];
                    if (curKind == "region" && curId >= 0 && curId < RegionCatalog.Entries.Count) pd *= RegionCatalog.Entries[curId].pedK;   // 지역 유동 인구 배율
                    world.crossPeds = pd > 0f ? new CrossPeds(world.st.road, pd) : null;
                }
            }
            scene = SceneRig.Build(world, worldRoot);
            scene.Tick(world, 0.016f, true);                   // 첫 장부터 차가 제자리에 보이게
            camMode = Prefs.StartCam;
            scene.cameraRig.Snap(camMode, world); Dc.Lap("scene-tail");
            savedResult = false;
            screenBrake = false;          // 게임 속 차는 P 로 시작하니 저절로 움직이지 않는다
            lookYaw = lookPitch = 0f;
            hud.Bind(scene);
            // 왼쪽 위 할 일 카드를 없앤 대신 시작할 때 할 일을 잠깐 알린다(튜토리얼은 단계마다 World 가 알린다). 오른쪽 위 "할 일" 로 다시 볼 수 있다
            // 할 일은 화면에 띄우지 않고 음성으로 한 번만(기기 밖, 편집기에서는 예전처럼 알림)
            if (AndroidLink.OnDevice) AndroidLink.Call("speakHint", (GoalHint() ?? "").Replace("\n", " ").Replace("→", ",")); else world.Say(GoalHint(), 9f);
            menu.SetVisible(false);
            hud.SetVisible(true);
            uiCam.enabled = false;
        }

        void EndSession()
        {
            if (scene != null) scene.Dispose();
            scene = null;
            world = null;
            CityBuilder.Forget();   // 지을 때만 쓰던 노면·건물 격자 참조를 놓는다
        }

        void ShowMenu()
        {
            EndSession();
            hud.SetVisible(false);
            menu.SetVisible(true);
            uiCam.enabled = true;
        }

        void SaveResult()
        {
            savedResult = true;
            if (world.state != PlayState.Done) return;
            string key = curKind == "tutorial" ? "tut:" + curId : curKind + ":" + curId;
            int stars = curKind == "tutorial" ? 1 : Mathf.Max(1, world.result != null ? world.result.stars : 1);
            ProgressStore.Record(key, stars);
        }

        void Back()
        {
            if (menu.SettingsOpen) { menu.CloseSettings(); return; }
            if (world != null) ShowMenu();
            else if (menu.SettingsOpen) menu.CloseSettings();
            else CloseGame();
        }

        void CloseGame()
        {
            if (AndroidLink.OnDevice) AndroidLink.Call("closeGame");
#if !UNITY_EDITOR
            else Application.Quit();
#endif
        }
    }
}
