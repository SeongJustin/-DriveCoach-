// 3D 도로 주행
// 지금은 미리보기: 신호 있는 사거리와 2차로 도로가 있는 작은 동네를 자유롭게 달리며
// 신호 위반·정지선 넘김·차로 이탈·제한속도를 짧은 말로 알려 준다. 나중에 코스(목적지)·점수·다른 차를 붙일 자리를 남겼다.
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{

    /// <summary>도로 주행 한 판의 진행. 실패는 충돌·밖으로 나감뿐이고, 위반은 횟수와 안내로.</summary>
    public class RoadRun
    {
        public readonly RoadDef def;
        public int redLightCount, stopLineCount, laneDepartCount, speedingCount, noSignalCount;
        /// <summary>부딪힌 횟수(도로 주행에서는 부딪혀도 판이 끝나지 않는다)</summary>
        public int bumpCount;
        /// <summary>옆 동 지도로 바꿔 달라는 요청(그 동의 파일 이름). 없으면 null</summary>
        public string wantRegion;

        /// <summary>동을 넘어갈 때 지금까지의 기록(횟수·거리)을 이어받는다</summary>
        public void CarryFrom(RoadRun o)
        {
            if (o == null) return;
            redLightCount = o.redLightCount; stopLineCount = o.stopLineCount; laneDepartCount = o.laneDepartCount; speedingCount = o.speedingCount; noSignalCount = o.noSignalCount;
            bumpCount = o.bumpCount; curbCount = o.curbCount;
            rightOnRedCount = o.rightOnRedCount; tailgateCount = o.tailgateCount; lightCount = o.lightCount; highBeamCount = o.highBeamCount; beltCount = o.beltCount;
            humpCount = o.humpCount; schoolStopCount = o.schoolStopCount; pedCount = o.pedCount;
            distance = o.distance;
        }
        /// <summary>연석을 밟은 횟수(멈추지 않고 기록만)</summary>
        public int curbCount;
        /// <summary>추가 규칙: 빨간불 우회전 때 일시정지 안 함, 앞차와 너무 가까움, 밤·비에 전조등 끔, 마주 오는 차 앞 상향등, 안전벨트</summary>
        public int rightOnRedCount, tailgateCount, lightCount, highBeamCount, beltCount, humpCount, schoolStopCount, pedCount;
        readonly Dictionary<int, float> pedWarned = new Dictionary<int, float>();
        bool onHump;
        // 어린이보호구역: 지금 구역(-1 = 밖), 신호 없는 구역 안 횡단보도(정해 두기), 앞에 멈춰야 할 횡단보도까지 거리(자동 속도가 본다)
        public int schoolZone = -1;
        public float zoneStopDist = float.PositiveInfinity;
        List<V2> zoneCrossings;
        readonly Dictionary<int, float> crossCleared = new Dictionary<int, float>();
        public float distance;
        /// <summary>지금 차로(가장 가까운 차로)와 그 제한 속도</summary>
        public Lane lane;
        /// <summary>지금 길의 제한속도(km/h). 0 = 확인된 제한속도 없음(과속 판정·표시 안 함)</summary>
        public float limitKmh;
        /// <summary>지금 차로의 흐름 속도(자동 속도가 제한속도를 모를 때 쓰는 값)</summary>
        public float flowKmh;
        bool outside;
        V2 lastFront;
        bool laneOut, speeding;
        bool joined = true;       // 차로에 들어섰는지(주차장에서 출발하면 차로 안에서 방향이 맞을 때까지 false)
        /// <summary>아직 차로에 들어서지 않음(주차장 안·주차장에서 나오는 중). 자동 속도가 이때는 천천히 간다</summary>
        public bool NotJoined => !joined;
        float sayCooldown;
        // 방향지시등 규칙: 회전·차로 변경 전에 그쪽 방향지시등을 켰는가(도로교통법 제38조: 시내 30 m 전부터)
        readonly List<(float t, float h)> headHist = new List<(float, float)>();
        float lastLeftT = -99f, lastRightT = -99f, turnCooldown, laneChangeCooldown;
        Lane prevLane;
        // 빨간불 우회전(2023.1.22 시행: 앞 신호가 빨간불이면 정지선에서 일시정지 후 우회전): 정지선을 넘은 뒤 오른쪽으로 도는지 지켜본다
        bool redPending, redStopped, redArrow, leftPending;
        float redT, redH, redDist, stoppedT = -99f, distAtStop, leftT, leftH, leftDist;
        // 안전거리·전조등·상향등·안전벨트
        float tailT, tailCool, lightT, lightCool, highCool, beltT, beltCool;

        public RoadRun(RoadDef d, Vehicle car) { def = d; lastFront = car.FrontBumperCenter; limitKmh = d.region == null ? d.speedLimitKmh : 0f; flowKmh = d.speedLimitKmh; }

        static float AngDiff(float a, float b)
        {
            float d = a - b;
            while (d > Math.PI) d -= 2f * (float)Math.PI;
            while (d < -Math.PI) d += 2f * (float)Math.PI;
            return d;
        }

        /// <summary>
        /// 방향지시등 안내: 회전(6초 안에 50° 넘게 돎)이나 차로 변경(나란한 옆 차로로 옮김) 때 그쪽 방향지시등이 5초 안에 켜져 있었는가.
        /// 실제 차에서 방향지시등 값을 받거나(signalKnown) 화면·키보드로 한 번이라도 켰을 때만 따진다(신호를 모르면 잔소리하지 않는다).
        /// heading 이 커지면 오른쪽으로 도는 것(지역 좌표 y 남쪽).
        /// </summary>
        void SignalRules(World w, float dt, Lane aligned, float alignedDist)
        {
            var car = w.car;
            if (car.turnSignal < 0) lastLeftT = w.t;
            if (car.turnSignal > 0) lastRightT = w.t;
            turnCooldown -= dt;
            laneChangeCooldown -= dt;
            if (car.speed < 1.5f) { headHist.Clear(); prevLane = aligned; return; }
            headHist.Add((w.t, car.heading));
            while (headHist.Count > 0 && w.t - headHist[0].t > 6f) headHist.RemoveAt(0);
            if (!car.signalKnown) { prevLane = aligned; return; }
            if (turnCooldown <= 0f && headHist.Count > 1)
            {
                float dh = AngDiff(car.heading, headHist[0].h);
                if (Math.Abs(dh) > 50f * Geom.Deg2Rad)
                {
                    bool right = dh > 0f;
                    float last = right ? lastRightT : lastLeftT;
                    if (w.t - last > 5f)
                    {
                        noSignalCount++;
                        w.Say(right ? "우회전할 때는 30 m 앞에서 오른쪽 방향지시등을 켜십시오" : "좌회전할 때는 30 m 앞에서 왼쪽 방향지시등을 켜십시오", 3f);
                    }
                    turnCooldown = 8f;
                    headHist.Clear();
                }
            }
            // 차로 변경: 나란한(같은 방향) 다른 차로로 옮겨 갔고, 앞 차로 가운데에서 1.2 m 넘게 벗어남
            if (aligned != null && prevLane != null && aligned != prevLane && laneChangeCooldown <= 0f && turnCooldown <= 0f)
            {
                float best = float.PositiveInfinity; V2 cp = car.Center, seg = car.Dir;
                for (int i = 0; i + 1 < prevLane.center.Length; i++)
                {
                    var p = Geom.ClosestPointOnSegment(car.Center, prevLane.center[i], prevLane.center[i + 1]);
                    float d = p.DistanceTo(car.Center);
                    if (d < best) { best = d; cp = p; seg = (prevLane.center[i + 1] - prevLane.center[i]).Normalized; }
                }
                if (best > 1.2f && best < 6f && seg.Dot(car.Dir) > 0.9f && alignedDist < aligned.width / 2f)
                {
                    var off = car.Center - cp;
                    bool right = seg.x * off.y - seg.y * off.x > 0f;     // 진행 방향 오른쪽(y 남 좌표)
                    float last = right ? lastRightT : lastLeftT;
                    if (w.t - last > 5f)
                    {
                        noSignalCount++;
                        w.Say(right ? "차로를 바꿀 때는 먼저 오른쪽 방향지시등을 켜고 사각지대를 보십시오" : "차로를 바꿀 때는 먼저 왼쪽 방향지시등을 켜고 사각지대를 보십시오", 3f);
                    }
                    laneChangeCooldown = 6f;
                }
            }
            prevLane = aligned;
        }

        /// <summary>
        /// 앞차 거리(2초, 비·눈은 3초 — 1.5초·2.5초보다 가까운 채 3초 넘게 달리면), 밤·비·안개에 전조등을 끈 채 달림,
        /// 마주 오는 차(150 m 안) 앞에서 상향등, 안전벨트를 안 맨 채 달림. 실제 차가 값을 줄 때만 따진다(모름 = -1 이면 말하지 않는다).
        /// </summary>
        void CarRules(World w, float dt)
        {
            var car = w.car;
            tailCool -= dt; lightCool -= dt; highCool -= dt; beltCool -= dt;
            float v = car.speed;
            // 앞차 거리
            float gapT = float.PositiveInfinity;
            TrafficCar oncoming = null;
            if (w.traffic != null && v > 1f)
            {
                var dir = car.Dir;
                var me = car.Center;
                foreach (var c in w.traffic.cars)
                {
                    if (Math.Abs(c.z - car.z) > 2.5f) continue;
                    var d = c.pos - me;
                    float along = d.Dot(dir);
                    if (along <= 0f || along > 160f) continue;
                    float lat = Math.Abs(d.x * dir.y - d.y * dir.x);
                    float cosH = (float)Math.Cos(c.heading - car.heading);
                    if (cosH > 0.8f && lat < 1.6f && along < 90f)
                    {
                        float gap = along - car.length / 2f - c.length / 2f;
                        if (gap / Math.Max(1f, v) < gapT) gapT = gap / Math.Max(1f, v);
                    }
                    else if (cosH < -0.8f && lat < 12f && oncoming == null) oncoming = c;
                }
            }
            float minGap = w.wetRoad ? 2.5f : 1.5f;
            if (v > 8.3f && gapT < minGap) tailT += dt; else tailT = 0f;
            if (tailT > 3f && tailCool <= 0f)
            {
                tailgateCount++;
                w.Say(w.wetRoad ? "비나 눈이 오는 날은 앞차와 평소보다 멀리(3초 넘게) 떨어져 가십시오" : "앞차와 2초 넘게 떨어져 가십시오(앞차가 지난 곳을 2초 뒤에 지나게)", 3.2f);
                tailCool = 20f; tailT = 0f;
            }
            // 전조등
            if (w.needHeadlights && car.headlight == 0 && v > 2f) lightT += dt; else lightT = 0f;
            if (lightT > 4f && lightCool <= 0f)
            {
                lightCount++;
                w.Say("어둡거나 비나 안개가 있으면 전조등을 켜십시오", 2.8f);
                lightCool = 60f; lightT = 0f;
            }
            // 상향등: 마주 오는 차가 있으면 하향으로
            if (car.headlight == 2 && oncoming != null && highCool <= 0f)
            {
                highBeamCount++;
                w.Say("마주 오는 차가 있으면 상향등을 끄십시오(눈부심)", 2.6f);
                highCool = 25f;
            }
            SchoolZoneRules(w, dt);
            HazardQuiz(w, dt);
            // 보행자 보호(도로교통법 제27조, 2022.7.12: 건너고 있거나 건너려 할 때 일시정지): 횡단보도 위를 지나는데 그 횡단보도에서
            // 8 m 안에 건너는 사람이 있으면
            if (w.crossPeds != null && v > 1.5f)
            {
                var cp = w.crossPeds;
                for (int ci = 0; ci < cp.crossings.Count; ci++)
                {
                    var cr = cp.crossings[ci];
                    if (cr.c.DistanceTo(car.Center) > cr.half + 6f) continue;
                    var a = cr.c - cr.n * cr.half; var b = cr.c + cr.n * cr.half;
                    if (Geom.ClosestPointOnSegment(car.FrontBumperCenter, a, b).DistanceTo(car.FrontBumperCenter) > 1.5f) continue;
                    if (pedWarned.TryGetValue(ci, out var tw) && w.t - tw < 10f) continue;
                    foreach (var p in cp.peds)
                    {
                        if (p.cross != ci || !p.onRoad || p.pos.DistanceTo(car.Center) > 8f) continue;
                        pedCount++;
                        pedWarned[ci] = w.t;
                        w.Say("횡단보도에 사람이 있으면 멈춰서 다 건널 때까지 기다리십시오", 3f);
                        break;
                    }
                }
            }
            // 과속방지턱(앞바퀴가 올라선 순간의 속도): 30 km/h 넘으면
            var rg = def.region;
            if (rg != null && rg.humps.Count > 0)
            {
                bool on = rg.HumpHeight(car.FrontAxle) > 0.01f;
                if (on && !onHump && v > 30f / 3.6f)
                {
                    humpCount++;
                    w.Say("과속방지턱 앞에서는 속도를 줄이십시오(30 km/h 아래로)", 2.6f);
                }
                onHump = on;
            }
            // 안전벨트
            if (car.belt == 0 && v > 2f) beltT += dt; else beltT = 0f;
            if (beltT > 2f && beltCool <= 0f)
            {
                beltCount++;
                w.Say("안전벨트를 매십시오", 2.4f);
                beltCool = 60f; beltT = 0f;
            }
        }

        /// <summary>
        /// 어린이보호구역: 들어서면 알림(30 km/h, 신호 없는 횡단보도 일시정지 — 2022.7.12 시행), 제한 속도 30.
        /// 구역 안 신호 없는 횡단보도(30 m 안에 신호가 없는 것): 앞 25 m 안이면 zoneStopDist 로 알려(자동 속도가 그 앞에서 선다),
        /// 13 m 안 또는 그 횡단보도 정지선(가운데에서 13.5 m 안, 8~13 m 앞에 있는 곳이 있다) 바로 앞 4 m 안에서 멈추면(0.3 m/s 밑) 통과, 멈추지 않고 지나가면 안내·셈.
        /// </summary>
        void SchoolZoneRules(World w, float dt)
        {
            var rg = def.region;
            zoneStopDist = float.PositiveInfinity;
            if (rg == null || rg.schoolZones.Count == 0) { schoolZone = -1; return; }
            var car = w.car;
            if (zoneCrossings == null)
            {
                zoneCrossings = new List<V2>();
                var pts = new List<V2>();
                if (rg.crosswalks.Count > 0) foreach (var x in rg.crosswalks) pts.Add(x.c); else pts.AddRange(rg.crossings);
                foreach (var c in pts)
                {
                    if (rg.SchoolZoneAt(c) < 0) continue;
                    bool sig = false;
                    foreach (var sp in rg.signals) if (sp.DistanceTo(c) < 30f) { sig = true; break; }
                    if (!sig) zoneCrossings.Add(c);
                }
            }
            int z = Math.Abs(car.z) < 2.5f ? rg.SchoolZoneAt(car.Center) : -1;
            if (z >= 0 && schoolZone < 0)
                w.Say("어린이 보호구역입니다. 30 km/h 아래로, 신호 없는 횡단보도 앞에서는 꼭 멈췄다 가십시오", 3.4f);
            schoolZone = z;
            if (z < 0) return;
            var front = car.FrontBumperCenter;
            var dir = car.Dir;
            for (int i = 0; i < zoneCrossings.Count; i++)
            {
                var c = zoneCrossings[i];
                if (crossCleared.TryGetValue(i, out var tc) && w.t - tc < 15f) continue;
                var rel = c - front;
                float ahead = rel.Dot(dir), side = Math.Abs(rel.x * dir.y - rel.y * dir.x);
                if (side > 7f || ahead < -3f || ahead > 25f) continue;
                if (ahead > 0.5f) zoneStopDist = Math.Min(zoneStopDist, ahead);
                // 13 m 안, 또는 이 횡단보도 정지선(가운데에서 13.5 m 안에 칠해진 것) 바로 앞(4 m 안)에서 멈추면 통과 —
                //   정지선이 횡단보도 가운데에서 11~13 m 앞인 곳(양재1동 바우뫼로 (348,-174) 12.4 m)에서는 정지선에 바르게 서도 13 m 를 넘어
                //   '안 멈춤'으로 셌고, 자동 운전(정지선 2.4 m 앞에 섬)은 영영 섰다. 일괄 16 m 로 넓히지 않고 정지선이 있는 곳만 넓힌다.
                if (Math.Abs(car.speed) < 0.3f && (ahead < 13f || StopLineJustAhead(rg, front, dir, ahead))) { crossCleared[i] = w.t; continue; }   // 멈췄다 → 통과
                if (ahead < 0.5f && car.speed > 1f)
                {
                    schoolStopCount++;
                    w.Say("어린이 보호구역의 신호 없는 횡단보도 앞에서는 사람이 없어도 꼭 멈췄다 가십시오", 3.2f);
                    crossCleared[i] = w.t;
                }
            }
        }

        /// <summary>앞 4 m 안에 칠해진 정지선이 있고, 그 정지선이 횡단보도 가운데(ahead m 앞)에서 13.5 m 안인가</summary>
        static bool StopLineJustAhead(RegionData rg, V2 front, V2 dir, float ahead)
        {
            if (rg.stopPaint.Count == 0 || ahead > 18f) return false;
            float pd = rg.StopPaintAhead(front, dir, -0.5f, 4f, 1.6f);
            return !float.IsNegativeInfinity(pd) && ahead - pd > 0.5f && ahead - pd < 13.5f;
        }

        // 위험 예측 한마디: 초보 운전자 훈련 연구에서 효과가 확인된 "위험이 어떻게 커질지 미리 보게 하는" 방식.
        // 신호 없는 횡단보도·버스 정류장 35 m 앞에서 "어디를 봐야 할까요?" 를 묻고, 지나간 뒤 답을 짚어 준다.
        // 잔소리가 되지 않게 한 곳은 한 번만, 묻는 간격은 45초 넘게.
        List<(V2 p, int type)> hazards;
        readonly HashSet<int> hazardAsked = new HashSet<int>();
        int hazardOpen = -1;
        float hazardCool = 20f;
        public int hazardQuizCount;

        /// <summary>
        /// 위험 예측 한마디를 물을 자리: 신호 없는 횡단보도(0)·버스 정류장(1).
        /// 세우지 않는 중앙 정류장(kind 2 = JSON "m":2, 섬 없음 — BusStopBuilder 도 짓지 않음)은 뺀다. 없는 정류장을 "앞에 버스 정류장" 이라고 물었다.
        /// </summary>
        public static List<(V2 p, int type)> HazardPoints(RegionData rg)
        {
            var res = new List<(V2, int)>();
            var pts = new List<V2>();
            if (rg.crosswalks.Count > 0) foreach (var x in rg.crosswalks) pts.Add(x.c); else pts.AddRange(rg.crossings);
            foreach (var c in pts)
            {
                bool sig = false;
                foreach (var sp in rg.signals) if (sp.DistanceTo(c) < 30f) { sig = true; break; }
                if (!sig) res.Add((c, 0));
            }
            foreach (var b in rg.busStops) if (b.kind != 2) res.Add((b.sign, 1));
            return res;
        }

        void HazardQuiz(World w, float dt)
        {
            var rg = def.region;
            if (rg == null) return;
            if (hazards == null) hazards = HazardPoints(rg);
            var car = w.car;
            var front = car.FrontBumperCenter;
            var dir = car.Dir;
            hazardCool -= dt;
            if (hazardOpen >= 0)
            {
                var rel = hazards[hazardOpen].p - front;
                float ahead = rel.Dot(dir);
                if (ahead < -2f || rel.Length > 80f)
                {
                    if (ahead < -2f && rel.Length < 30f)
                        w.Say(hazards[hazardOpen].type == 0
                            ? "답: 횡단보도 양쪽 끝과 세워 둔 차 사이. 사람이 갑자기 나올 수 있는 곳입니다"
                            : "답: 서 있는 버스의 앞쪽. 내린 사람이 버스 앞으로 건널 수 있습니다", 3.6f);
                    hazardOpen = -1;
                }
                return;
            }
            if (hazardCool > 0f || car.speed < 3f || w.toastT > 0f) return;
            for (int i = 0; i < hazards.Count; i++)
            {
                if (hazardAsked.Contains(i)) continue;
                var rel = hazards[i].p - front;
                float ahead = rel.Dot(dir), side = Math.Abs(rel.x * dir.y - rel.y * dir.x);
                if (ahead < 28f || ahead > 40f || side > (hazards[i].type == 0 ? 5f : 9f)) continue;
                hazardAsked.Add(i);
                hazardOpen = i;
                hazardCool = 45f;
                hazardQuizCount++;
                w.Say(hazards[i].type == 0 ? "앞에 신호 없는 횡단보도. 어디를 봐야 합니까?" : "앞에 버스 정류장. 어디를 봐야 합니까?", 3.2f);
                break;
            }
        }

        public void Update(World w, float dt)
        {
            var car = w.car;
            var front = car.FrontBumperCenter;
            distance += Math.Abs(car.speed) * dt;
            sayCooldown -= dt;

            if (Math.Abs(car.speed) < 0.3f) { stoppedT = w.t; distAtStop = distance; }
            // 정지선을 앞 범퍼가 approach 방향으로 지나갔는가(고가 위·지하차도 안에서는 땅의 정지선을 보지 않는다)
            foreach (var sl in def.stopLines)
            {
                if (Math.Abs(car.z) > 2.5f) break;
                if (!SegCross(lastFront, front, sl.a, sl.b)) continue;
                if ((front - lastFront).Dot(sl.approach) <= 0f) continue;
                if (sl.signal >= 0 && sl.signal < def.signals.Count)
                {
                    var sg = def.signals[sl.signal];
                    var col = sg.At(w.t);
                    var lcol = sg.LeftAt(w.t);
                    if (col == SignalColor.Red && !redPending)
                    {
                        // 우회전이면 일시정지했는지만 본다(빨간불 우회전은 멈춘 뒤 허용) — 곧게·왼쪽이면 신호 위반. 18 m 또는 10초 안에 판단
                        // 좌회전 화살표가 켜져 있으면 왼쪽으로 꺾는 것은 괜찮다(곧게 가면 위반)
                        redPending = true; redT = w.t; redH = car.heading; redDist = distance;
                        redStopped = w.t - stoppedT < 8f && distance - distAtStop < 12f;
                        redArrow = lcol != SignalColor.Red;
                    }
                    else if (col == SignalColor.Green && sg.leftGreen > 0f && !leftPending)
                    {
                        // 좌회전 화살표가 따로 있는 교차로에서 직진 초록에 왼쪽으로 꺾으면 신호 위반(비보호 좌회전 표지 없음)
                        leftPending = true; leftT = w.t; leftH = car.heading; leftDist = distance;
                    }
                    else if (col == SignalColor.Yellow) w.Say("노란불입니다. 정지선 전이면 서는 것이 원칙입니다", 2.6f);
                }
            }
            lastFront = front;
            if (redPending)
            {
                float dh = AngDiff(car.heading, redH);
                if (dh > 40f * Geom.Deg2Rad)
                {
                    redPending = false;
                    if (!redStopped)
                    {
                        rightOnRedCount++;
                        w.Say("앞 신호가 빨간불이면 우회전 전에 정지선에서 완전히 멈추십시오. 그다음 보행자를 보고 천천히", 3.4f);
                    }
                }
                else if (dh < -30f * Geom.Deg2Rad && redArrow)
                    redPending = false;            // 좌회전 화살표에 왼쪽으로 꺾음
                else if (dh < -30f * Geom.Deg2Rad || distance - redDist > (redArrow ? 50f : 18f) || w.t - redT > (redArrow ? 18f : 10f))      // 좌회전 화살표 때는 큰 교차로에서 왼쪽으로 꺾기 시작할 때까지(50 m) 본다
                {
                    redPending = false;
                    redLightCount++;
                    w.Say(redArrow ? "좌회전 화살표일 때는 왼쪽으로만 갈 수 있습니다. 직진 신호를 기다리십시오" : "빨간불에 정지선을 넘었습니다. 정지선 앞에서 서십시오", 3f);
                }
            }
            if (leftPending)
            {
                float dh = AngDiff(car.heading, leftH);
                if (dh < -30f * Geom.Deg2Rad)
                {
                    leftPending = false;
                    redLightCount++;
                    w.Say("이 교차로는 좌회전 화살표 신호가 따로 있습니다. 직진 초록불에는 왼쪽으로 꺾지 마십시오", 3.2f);
                }
                else if (dh > 30f * Geom.Deg2Rad || distance - leftDist > 55f || w.t - leftT > 20f) leftPending = false;
            }

            // 차로 이탈: 가장 가까운 차로 가운데에서 차로 폭의 절반 넘게 벗어나면
            float best = float.PositiveInfinity; Lane bestLane = null;
            float bestA = float.PositiveInfinity; Lane alignedLane = null;
            foreach (var ln in def.lanes)
            {
                for (int i = 0; i + 1 < ln.center.Length; i++)
                {
                    if (ln.z != null || Math.Abs(car.z) > 0.5f)
                    {
                        // 입체 교차: 다른 층 차로는 빼고 고른다
                        float lz = ln.z == null ? 0f : (ln.z[i] + ln.z[i + 1]) / 2f;
                        if (Math.Abs(lz - car.z) > 2.5f) continue;
                    }
                    float d = Geom.ClosestPointOnSegment(car.Center, ln.center[i], ln.center[i + 1]).DistanceTo(car.Center);
                    if (d < best) { best = d; bestLane = ln; }
                    if (d < bestA && (ln.center[i + 1] - ln.center[i]).Normalized.Dot(car.Dir) > 0.7f) { bestA = d; alignedLane = ln; }
                }
            }
            // 제한 속도는 진행 방향이 맞는 차로에서(교차로에서 가로지르는 길의 제한을 쓰지 않게)
            if (alignedLane != null && bestA < alignedLane.width)
            {
                lane = alignedLane; flowKmh = def.LimitOf(alignedLane);
                limitKmh = def.region == null ? flowKmh : alignedLane.postedKmh;      // 동네 지도: 자료로 확인된 값만(없으면 0)
            }
            if (schoolZone >= 0) limitKmh = limitKmh > 0f ? Math.Min(limitKmh, 30f) : 30f;      // 어린이보호구역
            // 교차로에서 꺾는 동안(핸들을 120° 넘게 돌린 상태)은 차로 가운데 판정을 하지 않는다 — 바르게 원호로 돌아도 "차로 가운데로 가세요"가 났다
            // 주차장 안, 그리고 주차장에서 나와 차로에 들어서기(차로 안에서 방향이 맞기) 전까지는
            //   차로 가운데 판정을 하지 않는다 — 길로 들어가는 동안은 차로를 가로지르는 것이 맞다
            bool inLot = def.region != null && def.region.InParkingLot(car.Center);
            if (!joined && bestLane != null && best < bestLane.width / 2f && alignedLane == bestLane) joined = true;
            if (inLot) joined = false;
            bool outNow = joined && bestLane != null && best > bestLane.width / 2f + 0.2f && Math.Abs(car.speed) > 1f && Math.Abs(car.steerDeg) < 120f;
            if (outNow && !laneOut) { laneDepartCount++; if (sayCooldown <= 0f) { w.Say("차로 가운데로 가십시오", 2f); sayCooldown = 3f; } }
            laneOut = outNow;

            SignalRules(w, dt, alignedLane, bestA);
            CarRules(w, dt);

            // 제한속도(차로마다 다를 수 있다: 큰길 50, 동네길 30 — 안전속도 5030)
            bool over = limitKmh > 0f && car.speed * 3.6f > limitKmh + 2f;
            if (over && !speeding) { speedingCount++; w.Say($"제한속도 {limitKmh:0} km/h 입니다", 2.2f); }
            speeding = over;

            // 내 동네: 동 경계(막다른 길이 아니라 옆 동으로 이어진다).
            //   옆 동 데이터가 앱에 있으면 그대로 넘어간다(알림). 없으면 경계 밖 4 m 안에서 속도를 0 까지 줄여 세우고
            //   "OO동 데이터를 아직 다운받지 않으셨습니다" — 동 안쪽으로 돌아가는 움직임은 막지 않는다.
            var rg = def.region;
            if (rg != null && rg.boundary.Length >= 3)
            {
                var c = car.Center;
                bool inside = Geom.PointInPolygon(c, rg.boundary);
                var edge = c;
                float dist = inside ? 0f : Geom.DistPointPoly(c, rg.boundary, out edge);
                if (!inside)
                {
                    var nb = rg.NeighborAt(c);
                    bool have = nb != null && nb.file != "";
                    if (!have && dist <= RegionData.BorderSlack) inside = true;      // 경계를 따라 난 큰길(경계선이 길 한가운데를 지난다)은 길 폭만큼 그냥 다닌다
                    else if (!have)
                    {
                        var outDir = (c - edge).Normalized;
                        float vOut = car.speed * car.Dir.Dot(outDir);
                        float past = dist - RegionData.BorderSlack;
                        if (vOut > 0f)
                        {
                            float dec = (5f + 30f * Math.Min(1f, past / 4f)) * dt;
                            car.speed = car.speed > 0f ? Math.Max(0f, car.speed - dec) : Math.Min(0f, car.speed + dec);
                            if (past > 4f) car.speed = 0f;
                        }
                        if (!outside || (sayCooldown <= 0f && Math.Abs(car.speed) < 0.1f && vOut >= 0f))
                        {
                            w.Say($"{(nb != null ? nb.name : "옆 동")}은 아직 준비 중입니다. 지도 안쪽으로 돌아가십시오.", 3f);
                            sayCooldown = 4f;
                        }
                    }
                    else
                    {
                        if (!outside) w.Say($"{nb.name}으로 들어갑니다", 2f);
                        // (동 사이 이동) 옆 동 안으로 40 m 넘게 들어오면 그 동 지도로 바꿔 달라고 알린다(GameMain 이 받아서 이어 달리게 한다).
                        //   40 m 여유: 경계를 따라 난 길에서 두 동을 왔다 갔다 하며 계속 바뀌지 않게.
                        if (dist > 40f) wantRegion = nb.file;
                    }
                }
                outside = !inside;
                if (dist > rg.margin - 40f)
                {
                    // 판을 끝내지 않는다: 지도 끝에서는 더 못 가게 세우고 알려 준다
                    var outDir2 = (c - edge).Normalized;
                    if (car.speed * car.Dir.Dot(outDir2) > 0f) car.speed = 0f;
                    if (sayCooldown <= 0f) { w.Say("여기가 지도 끝입니다. 돌아가 주십시오.", 3f); sayCooldown = 4f; }
                }
            }
        }

        static bool SegCross(V2 p1, V2 p2, V2 q1, V2 q2)
        {
            float d1 = Cross(q2 - q1, p1 - q1), d2 = Cross(q2 - q1, p2 - q1);
            float d3 = Cross(p2 - p1, q1 - p1), d4 = Cross(p2 - p1, q2 - p1);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }
        static float Cross(V2 a, V2 b) => a.x * b.y - a.y * b.x;
    }
}
