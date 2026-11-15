// 담당: S
// 한 판(단계 또는 튜토리얼)의 상태와 규칙: 차 움직이기, 충돌·보행자, 목표 판정, 튜토리얼 단계 검사. 좌표는 전부 m.
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public enum PlayState { Play, Fail, Done }

    public class PedState
    {
        public PedDef def;
        public V2 pos;
        public int i = 1, dir = 1;
        public float wait, facing;
        public bool walking;
    }

    public class Obstacle { public V2[] poly; public V2 c; public float r; public string kind; }

    /// <summary>판정·결과. 성공이면 stars(1~3).</summary>
    public class JudgeResult
    {
        public bool ok;
        public string why = "";
        public bool precise;
        public float dist, angle, offset;
        public string kind = "";
        public int stars, moves, par;
        public bool efficient, tutorial;
    }

    /// <summary>튜토리얼 안내선. mark = 차에서 맞출 점: shoulder | rear_bumper | rear_axle | ""</summary>
    public class Guide { public V2 a, b; public string label, mark; }

    public class World
    {
        public const float PedR = 0.28f;
        public const float PedWaitEnd = 1.5f;

        public Stage st;
        public Vehicle car;
        public int floorI;
        public float t;
        public PlayState state = PlayState.Play;
        public string failReason = "";
        public V2 failPoint = V2.Inf;
        public JudgeResult result;
        public List<PedState> peds = new List<PedState>();
        public List<Obstacle> obstacles = new List<Obstacle>();
        public int moves;
        int lastSign;
        public List<TutorialStep> steps;
        public int stepI;
        public float turnAcc;
        public float stepOdo;
        bool hinted;
        public string toast = "";
        public float toastT;
        public float sensorFront = float.PositiveInfinity;
        public float sensorRear = float.PositiveInfinity;
        public float grade;
        public bool onRamp;
        public float minClear = float.PositiveInfinity;
        /// <summary>서킷·도로 주행이면 그 진행(주차면 null)</summary>
        public CircuitRun circuitRun;
        public RoadRun roadRun;
        /// <summary>도로 주행의 다른 차들(없으면 null)</summary>
        public Traffic traffic;
        /// <summary>내 동네 횡단보도를 건너는 사람들(11/9, 없으면 null)</summary>
        public CrossPeds crossPeds;
        /// <summary>자동 속도(핸들만 돌리기)로 달리는 중 — 안내 말을 "속도를 줄이세요" 대신 핸들·시선 쪽으로 바꾼다</summary>
        public bool autoSpeed;
        /// <summary>젖은 길(비·눈): 앞차 거리 기준을 늘린다. 전조등이 필요한 때(밤·비·안개·눈, 해질녘 궂은 날) — GameMain 이 설정에서</summary>
        public bool wetRoad, needHeadlights;

        public World(Stage stage, CarSpec spec)
        {
            st = stage;
            car = new Vehicle(spec);
            car.heading = st.startH * Geom.Deg2Rad;
            car.pos = st.startC - car.Dir * ((car.wheelbase + car.frontOh - car.rearOh) / 2f);
            floorI = FloorAt(car.Center, 0);
            steps = st.steps;
            foreach (var f in st.floors)
            {
                foreach (var w in f.walls) AddOb(w, "wall");
                foreach (var p in f.pillars) AddOb(p, "pillar");
                foreach (var oc in f.cars) AddOb(Geom.RectPoly(oc.c, oc.len, oc.wid, oc.h), "car");
                foreach (var cb in f.curbs) AddOb(cb, "curb");
            }
            if (st.road != null) foreach (var b in st.road.buildings) AddOb(b.poly, "building");
            if (st.road?.region != null)
                foreach (var pc in st.road.region.parked) AddOb(Geom.RectPoly(pc.c, pc.L, pc.W, pc.ang * Geom.Rad2Deg), "car");   // 이면도로 세운 차
            // (11/15) 주차장 칸에서 출발: 출발 자리 150 m 안 주차장에 세워 둔 차는 부딪히는 물체(빠져나올 때 옆 차를 뚫고 지나가지 않게).
            //   길(차로) 위로 나온 칸의 차는 그리지 않으므로(FacilityBuilder) 차로 가운데에서 2.2 m 안의 차는 넣지 않는다
            if (st.road?.region != null)
                foreach (var fc in st.road.region.facilities)
                {
                    if (fc.kind != "parking" || fc.poly == null || fc.poly.Length < 3 || fc.cars.Count == 0) continue;
                    if (Geom.PolyCenter(fc.poly).DistanceTo(st.startC) > 150f) continue;
                    foreach (var (p, ang) in fc.cars)
                        if (!NearLane(st.road, p, 2.2f)) AddOb(Geom.RectPoly(p, 4.6f, 1.85f, ang * Geom.Rad2Deg), "car");
                }
            foreach (var pd in st.peds)
                peds.Add(new PedState { def = pd, pos = pd.path[0], i = 1, dir = 1, wait = pd.delay });
            if (st.mode == StageMode.Circuit && st.circuit != null)
            {
                car.model = DriveModel.Road();
                circuitRun = new CircuitRun(st.circuit, car.Center);
            }
            else if (st.mode == StageMode.Road && st.road != null)
            {
                car.model = DriveModel.Road();
                roadRun = new RoadRun(st.road, car);
                if (st.road.trafficCount > 0 && st.road.lanes.Count > 0) traffic = new Traffic(st.road, st.road.trafficCount, car.Center, car.heading);
                if (st.road.region != null && (st.road.region.crossings.Count > 0 || st.road.region.crosswalks.Count > 0)) crossPeds = new CrossPeds(st.road, 1f);
            }
        }

        static bool NearLane(RoadDef road, V2 p, float r)
        {
            foreach (var ln in road.lanes)
            {
                var c = ln.center;
                if (c == null) continue;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    if (Math.Abs(c[i].x - p.x) > 80f && Math.Abs(c[i + 1].x - p.x) > 80f) continue;
                    if (Geom.ClosestPointOnSegment(p, c[i], c[i + 1]).DistanceTo(p) < r) return true;
                }
            }
            return false;
        }

        void AddOb(V2[] poly, string kind)
        {
            var c = Geom.PolyCenter(poly);
            float r = 0f;
            foreach (var p in poly) r = Math.Max(r, p.DistanceTo(c));
            obstacles.Add(new Obstacle { poly = poly, c = c, r = r, kind = kind });
        }

        public bool IsTutorial => steps.Count > 0;
        public Target Target => st.target;
        public void Say(string text, float sec = 2.6f) { toast = text; toastT = sec; }
        public bool Stopped => car.speed == 0f;
        public float HeadingDeg => Geom.Wrap(car.heading, -Geom.Pi, Geom.Pi) * Geom.Rad2Deg;

        // ───────────────────────── 한 프레임 ─────────────────────────

        /// <summary>steerDeg: 실제 핸들 각(+ 오른쪽), brake: 실제 페달 또는 화면 버튼, accel: 0..1, brakeAmount: 브레이크 세기 0..1</summary>
        public void Update(float dt, float steerDeg, bool brake, float accel, float brakeAmount = 1f)
        {
            if (toastT > 0f) toastT -= dt;
            car.steerDeg = steerDeg;
            if (state != PlayState.Play) return;
            t += dt;
            UpdateGrade();
            float prevH = car.heading;
            car.Step(dt, brake, accel, grade, brakeAmount);
            turnAcc += Math.Abs(car.heading - prevH);
            if (Math.Abs(car.speed) > 0.2f)
            {
                int sg = car.speed > 0f ? 1 : -1;
                if (sg != lastSign) { moves++; lastSign = sg; }
            }
            floorI = FloorAt(car.Center, floorI);
            if (st.road != null && st.road.HasGrade) car.z = st.road.HeightAt(car.Center, car.z);
            UpdatePeds(dt);
            traffic?.Update(this, dt);
            crossPeds?.Update(this, dt);
            CheckCollisions();
            lastPos = car.pos; lastHeading = car.heading;
            if (state != PlayState.Play) return;
            UpdateSensors();
            if (steps.Count > 0) CheckStep();
            if (circuitRun != null)
            {
                circuitRun.Update(this, dt);
                if (circuitRun.Finished) FinishCircuit();
            }
            roadRun?.Update(this, dt);
        }

        void FinishCircuit()
        {
            var cr = circuitRun;
            state = PlayState.Done;
            int off = cr.offTrackCount;
            float best = float.PositiveInfinity;
            foreach (var lt in cr.lapTimes) best = Math.Min(best, lt);
            result = new JudgeResult
            {
                ok = true, kind = "circuit", stars = off == 0 ? 3 : (off <= 2 ? 2 : 1),
                precise = off == 0, efficient = true, moves = off, offset = best,
            };
        }

        void UpdateGrade()
        {
            grade = 0f;
            onRamp = false;
            var c = car.Center;
            foreach (var r in st.ramps)
            {
                if (Geom.PointInPolygon(c, r.poly))
                {
                    var ax = (r.b - r.a).Normalized;
                    grade = r.grade * car.Dir.Dot(ax);
                    onRamp = true;
                    return;
                }
            }
        }

        public int FloorAt(V2 c, int cur)
        {
            for (int i = 0; i < st.floors.Count; i++)
                if (st.floors[i].ground.HasPoint(c)) return i;
            return cur;
        }

        bool InBounds(V2 c)
        {
            foreach (var f in st.floors) if (f.ground.HasPoint(c)) return true;
            foreach (var r in st.ramps) if (Geom.PointInPolygon(c, r.poly)) return true;
            foreach (var p in st.pads) if (Geom.PointInPolygon(c, p)) return true;
            return false;
        }

        // ───────────────────────── 보행자 ─────────────────────────

        void UpdatePeds(float dt)
        {
            var body = car.Corners(0.5f);
            foreach (var p in peds)
            {
                p.walking = false;
                if (p.wait > 0f) { p.wait -= dt; continue; }
                var path = p.def.path;
                var tgt = path[p.i];
                var to = tgt - p.pos;
                float stride = p.def.speed * dt;
                var nxt = p.pos + to.Normalized * Math.Min(stride, to.Length);
                // 차가 바로 앞에 서 있으면 사람이 기다린다
                if (Geom.CircleOverlap(nxt, PedR, body)) continue;
                p.pos = nxt;
                p.walking = true;
                if (to.Length > 0.001f) p.facing = to.Angle;
                if (p.pos.DistanceTo(tgt) < 0.01f)
                {
                    int n = path.Length;
                    if (p.def.loop) p.i = (p.i + 1) % n;
                    else
                    {
                        if (p.i >= n - 1) { p.dir = -1; p.wait = PedWaitEnd; }
                        else if (p.i <= 0) { p.dir = 1; p.wait = PedWaitEnd; }
                        p.i += p.dir;
                    }
                }
            }
        }

        // ───────────────────────── 충돌·센서 ─────────────────────────

        static readonly Dictionary<string, string> KindName = new Dictionary<string, string> { { "wall", "벽" }, { "pillar", "기둥" }, { "car", "주차된 차" }, { "building", "건물" }, { "tree", "나무" }, { "lamp", "가로등" }, { "prop", "보도 시설물" } };

        void CheckCollisions()
        {
            var cs = car.Corners();
            var cc = car.Center;
            float reach = car.length / 2f + 0.5f;
            List<V2[]> tires = null;
            bool curbNow = false;
            // (11/11 대표: "충돌은 그냥 부딪힌 거지 게임 오버로 넘어가지 말아줘") 도로 주행에서는 부딪혀도 판을 끝내지 않는다:
            //   닿기 직전 자리로 되돌려 세우고 한마디만 한다(횟수는 결과 리포트에). 주차·서킷 연습은 그대로 실패.
            // (11/15 밤 대표: "어디 박아도 실패라는 말이 안 떠야 … 그 자리에 정지하고 후진해서 빠져나오는 게 맞지") 주차·서킷 연습도 판을 끝내지 않고 그 자리에 세운다
            bool soft = true;
            foreach (var ob in obstacles)
            {
                if (cc.DistanceTo(ob.c) > ob.r + reach) continue;
                if (Math.Abs(car.z) > 3f && ob.kind != "curb") continue;     // 고가 위·지하차도 안: 땅 위 건물·물체와 닿지 않는다
                if (Math.Abs(car.z) > 0.6f && ob.kind == "curb") continue;
                if (ob.kind == "curb")
                {
                    // 연석은 낮아서 범퍼는 넘어가고 바퀴만 닿는다
                    if (tires == null) tires = car.Tires();
                    foreach (var tp in tires)
                    {
                        if (!Geom.ConvexOverlap(tp, ob.poly)) continue;
                        if (roadRun == null) { Bump("연석에 바퀴가 닿았습니다"); return; }   // 주차 연습: 연석은 넘지 못하고 그 자리에 선다
                        // (11/11 대표) 도로 주행: 연석은 밟고 올라설 수 있다 → 멈추지 않고 횟수만 남긴다(한 번 밟는 동안 한 번)
                        curbNow = true;
                        break;
                    }
                    continue;
                }
                if (ob.kind == "building" ? Geom.PolyOverlap(cs, ob.poly) : Geom.ConvexOverlap(cs, ob.poly))
                {
                    string what = KindName.TryGetValue(ob.kind, out var w) ? w : "물체";
                    if (soft) Bump(what + "에 부딪혔습니다"); else Fail(what + "에 부딪혔습니다", Contact(cs, ob.poly));
                    return;
                }
            }
            foreach (var p in peds)
            {
                if (Math.Abs(car.speed) > 0.01f && Geom.CircleOverlap(p.pos, PedR, cs)) { if (soft) Bump("사람과 부딪혔습니다"); else Fail("사람과 부딪혔습니다", p.pos); return; }
            }
            if (crossPeds != null && Math.Abs(car.speed) > 0.01f)
            {
                var hp = crossPeds.Hit(cs);
                if (hp != null) { if (soft) Bump("사람과 부딪혔습니다"); else Fail("사람과 부딪혔습니다", hp.pos); return; }
            }
            var hit = traffic?.Hit(car);
            if (hit != null) { if (soft) Bump("다른 차와 부딪혔습니다"); else Fail("다른 차와 부딪혔습니다", hit.pos); return; }
            if (!InBounds(cc)) { if (soft) Bump("여기는 길 밖입니다"); else Fail("길 밖으로 나갔습니다", cc); return; }
            touching = false;
            if (roadRun != null)
            {
                if (curbNow && !onCurb && t - lastCurbT > 1.5f) { roadRun.curbCount++; Say("연석을 밟았습니다.", 2.2f); }
                if (curbNow) lastCurbT = t;
                onCurb = curbNow;
            }
        }

        bool onCurb; float lastCurbT = -10f;

        /// <summary>지금 무언가에 닿아 서 있다(자동 운전이 뒤로 물러날 때 본다)</summary>
        public bool Touching => touching;
        /// <summary>닿았을 때 가던 쪽(1 = 앞으로 가다, -1 = 뒤로 가다)</summary>
        public int BumpDir => bumpDir;
        /// <summary>(11/14 베타: 차가 왜 서 있는지 모르겠다) 자동 속도가 차를 세운 까닭(없으면 "")</summary>
        public string autoWhy = "";
        bool touching; int bumpDir; float lastBumpT = -10f; V2 lastPos; float lastHeading;

        /// <summary>
        /// 도로 주행에서 부딪힘(11/11 대표: "원래 위치로 되돌릴 필요도 없어. 그냥 부딪히면 그 자리에 멈추는 거지"): 그 자리에 세운다. 판은 계속된다.
        /// 닿은 채로는 부딪힌 쪽(앞으로 가다 닿았으면 앞)으로는 더 못 가고, 반대쪽(후진)으로만 빠져나올 수 있다.
        /// </summary>
        void Bump(string what)
        {
            if (!touching)
            {
                touching = true;
                bumpDir = car.speed < 0f ? -1 : 1;
                car.speed = 0f;
                if (t - lastBumpT > 1.5f)
                {
                    if (roadRun != null) roadRun.bumpCount++;
                    if (roadRun != null) Say(what + (bumpDir > 0 ? ". R로 조금 물러난 뒤 다시 가십시오." : ". D로 조금 나온 뒤 다시 가십시오."), 3.6f);   // 주차 연습에서는 말 없이 세우기만 한다
                }
                lastBumpT = t;
            }
            else if (car.speed * bumpDir > 0f) { car.speed = 0f; car.pos = lastPos; car.heading = lastHeading; }   // 닿은 쪽으로는 조금도 더 못 간다
        }

        static V2 Contact(V2[] a, V2[] b)
        {
            foreach (var p in a) if (Geom.PointInPolygon(p, b)) return p;
            foreach (var p in b) if (Geom.PointInPolygon(p, a)) return p;
            return (Geom.PolyCenter(a) + Geom.PolyCenter(b)) / 2f;
        }


        void Fail(string reason, V2 at)
        {
            state = PlayState.Fail;
            // (11/15 베타 5A) 결과 창 문장 끝 마침표
            failReason = string.IsNullOrEmpty(reason) || reason.EndsWith(".") ? reason : reason + ".";
            failPoint = at;
            car.speed = 0f;
            result = new JudgeResult { ok = false, why = reason };
        }

        /// <summary>앞·뒤 주차 센서(범퍼 앞뒤 부채꼴만 감지). 결과는 sensorFront / sensorRear (m)</summary>
        void UpdateSensors()
        {
            sensorFront = float.PositiveInfinity;
            sensorRear = float.PositiveInfinity;
            var cs = car.Corners();
            var fwd = car.Dir;
            var fpts = new[] { cs[0], cs[0].Lerp(cs[1], 0.5f), cs[1], cs[0].Lerp(cs[1], 0.2f), cs[0].Lerp(cs[1], 0.8f) };
            var rpts = new[] { cs[3], cs[3].Lerp(cs[2], 0.5f), cs[2], cs[3].Lerp(cs[2], 0.2f), cs[3].Lerp(cs[2], 0.8f) };
            var cc = car.Center;
            float reach = car.length / 2f + 2f;
            foreach (var ob in obstacles)
            {
                if (ob.kind == "curb" || cc.DistanceTo(ob.c) > ob.r + reach) continue;
                sensorFront = Math.Min(sensorFront, Sense(fpts, ob.poly, fwd));
                sensorRear = Math.Min(sensorRear, Sense(rpts, ob.poly, -fwd));
            }
            if (traffic != null)
            {
                foreach (var tc in traffic.cars)
                {
                    if (tc.pos.DistanceTo(cc) > reach + 3f) continue;
                    var poly = tc.Poly;
                    sensorFront = Math.Min(sensorFront, Sense(fpts, poly, fwd));
                    sensorRear = Math.Min(sensorRear, Sense(rpts, poly, -fwd));
                }
            }
            foreach (var p in peds)
            {
                var circle = Geom.RectPoly(p.pos, PedR * 2f, PedR * 2f);
                sensorFront = Math.Min(sensorFront, Sense(fpts, circle, fwd));
                sensorRear = Math.Min(sensorRear, Sense(rpts, circle, -fwd));
            }
            minClear = Math.Min(minClear, Math.Min(sensorFront, sensorRear));
        }

        static float Sense(V2[] pts, V2[] poly, V2 look)
        {
            float best = float.PositiveInfinity;
            foreach (var p in pts)
            {
                float d = Geom.DistPointPoly(p, poly, out var q);
                if (d > 2f) continue;
                var v = q - p;
                if (d > 0.05f && v.Dot(look) < 0.35f * d) continue; // 옆에 있는 것은 앞뒤 센서에 잡히지 않는다
                best = Math.Min(best, d);
            }
            return best;
        }

        // ───────────────────────── 기어 ─────────────────────────

        /// <summary>화면 기어 버튼. 바꿀 수 없으면 이유를 돌려준다.</summary>
        public string Shift(Gear g, bool brake)
        {
            if (state != PlayState.Play || g == car.gear) return "";
            if (Math.Abs(car.speed) > 0.15f) return "차가 완전히 선 뒤에 바꾸십시오";
            if (!brake) return "브레이크를 밟은 채로 기어를 바꾸십시오.";
            car.GearChange(g);
            if (g == Gear.P) Finish();
            return "";
        }

        void Finish()
        {
            if (roadRun != null)
            {
                // 도로 주행 미리보기: 서서 P 를 누르면 끝. 위반이 없을수록 별이 많다.
                var rr = roadRun;
                int bad = rr.redLightCount * 2 + rr.laneDepartCount + rr.speedingCount + rr.rightOnRedCount + rr.tailgateCount + rr.lightCount + rr.beltCount + rr.humpCount + rr.schoolStopCount * 2 + rr.pedCount * 2
                    + rr.bumpCount * 3 + rr.curbCount + rr.noSignalCount + rr.highBeamCount;   // (11/15 오류 검수) 부딪힘, 연석, 방향지시등, 상향등도 별점에 넣는다(부딪혀도 별 셋이 나오던 것)
                state = PlayState.Done;
                // (11/14 베타: 골목을 10초 달리고 P 를 눌렀는데 ★★★ "모두 잘 지켰어요") 300 m 안쪽이면 평가하지 않고 별 하나
                bool tooShort = rr.distance < 300f;
                result = new JudgeResult { ok = true, kind = "road", stars = tooShort ? 1 : (bad == 0 ? 3 : (bad <= 3 ? 2 : 1)), moves = bad, precise = bad == 0 && !tooShort, efficient = true };
                return;
            }
            var tg = Target;
            if (tg.IsEmpty) return; // 핸들 감각 튜토리얼은 단계 검사(gear_p)로 끝난다
            var j = Judge();
            if (j.ok)
            {
                state = PlayState.Done;
                bool efficient = moves <= st.parMoves;
                j.stars = 1 + (j.precise ? 1 : 0) + (efficient ? 1 : 0);
                j.moves = moves;
                j.par = st.parMoves;
                j.efficient = efficient;
                result = j;
                if (IsTutorial) stepI = steps.Count;
            }
            else Say(j.why, 3.5f);
        }

        // ───────────────────────── 판정 ─────────────────────────

        public JudgeResult Judge()
        {
            var tg = Target;
            if (tg.kind == TargetKind.Line)
            {
                var bp = tg.frontBumper ? car.FrontBumperCenter : car.RearBumperCenter;
                float d = (bp - tg.p).Dot(tg.n);
                float tolL = tg.tol;
                float ang = Math.Abs(Geom.AngleDiffDeg(HeadingDeg, tg.n.Angle * Geom.Rad2Deg + (tg.frontBumper ? 0f : 180f)));
                string whyL = "";
                if (d < -tolL) whyL = "선까지 조금 남았습니다. 조금 더 가십시오";
                else if (d > tolL) whyL = "선을 지나쳤습니다. 조금 되돌아오십시오";
                return new JudgeResult { ok = Math.Abs(d) <= tolL, why = whyL, precise = Math.Abs(d) <= tolL * 0.5f, dist = d, angle = ang, offset = Math.Abs(d), kind = "line" };
            }
            // 주차칸
            var poly = tg.poly;
            var c = tg.hasCenter ? tg.center : Geom.PolyCenter(poly);
            var u = V2.FromAngle(tg.heading * Geom.Deg2Rad);
            var v = u.Orthogonal;
            float vmin = float.PositiveInfinity, vmax = float.NegativeInfinity, umin = float.PositiveInfinity, umax = float.NegativeInfinity;
            foreach (var p in poly)
            {
                vmin = Math.Min(vmin, (p - c).Dot(v)); vmax = Math.Max(vmax, (p - c).Dot(v));
                umin = Math.Min(umin, (p - c).Dot(u)); umax = Math.Max(umax, (p - c).Dot(u));
            }
            float innerLen = tg.len > 0f ? tg.len : umax - umin;
            float e = 0.30f + Math.Max(0f, car.length - innerLen) / 2f; // 앞뒤로 30 cm 까지는 삐져나와도 된다
            var cs = car.Corners();
            bool latOk = true, longOk = true;
            foreach (var p in cs)
            {
                float pv = (p - c).Dot(v);
                if (pv < vmin - 0.05f || pv > vmax + 0.05f) latOk = false;
                if (Geom.DistPointPoly(p, poly, out _) > e) longOk = false;
            }
            float hd = HeadingDeg;
            float err = Math.Abs(Geom.AngleDiffDeg(hd, tg.heading));
            if (tg.either) err = Math.Min(err, Math.Abs(Geom.AngleDiffDeg(hd, tg.heading + 180f)));
            float tol = tg.tol;
            float offset = Math.Abs((car.Center - c).Dot(v));
            string why = "";
            bool far = car.Center.DistanceTo(c) > 4f;
            if (far) why = "아직 목표 칸이 아닙니다. 연두색 칸에 넣고 P를 누르십시오.";
            else if (err > tol) why = "차가 비스듬합니다. 칸과 나란히 맞춘 뒤 P를 누르십시오.";
            else if (!latOk) why = "바퀴가 칸 선을 밟았습니다. 칸 가운데로 맞추십시오.";
            else if (!longOk) why = "차 앞이나 뒤가 칸 밖으로 나와 있습니다.";
            bool ok = !far && err <= tol && latOk && longOk;
            return new JudgeResult { ok = ok, why = why, precise = err <= 2.5f && offset <= 0.2f, angle = err, offset = offset, kind = "slot" };
        }

        // ───────────────────────── 튜토리얼 ─────────────────────────

        public string StepText => stepI < steps.Count ? steps[stepI].text : "";

        void CheckStep()
        {
            if (stepI >= steps.Count) return;
            string name = steps[stepI].check;
            if (!Check(name))
            {
                // 이 단계에서 움직였다가 멈췄는데 아직 아니면 한 번 알려 준다
                if (Stopped && car.odometer - stepOdo > 0.3f && !hinted) { hinted = true; Hint(name); }
                else if (!Stopped) hinted = false;
                return;
            }
            stepI++;
            stepOdo = car.odometer;
            hinted = false;
            turnAcc = 0f;
            if (stepI >= steps.Count)
            {
                state = PlayState.Done;
                result = new JudgeResult { ok = true, stars = 3, tutorial = true, moves = moves, par = moves, efficient = true, precise = true, kind = "tutorial" };
            }
            else Say("좋습니다! 다음: " + steps[stepI].text, 7f);   // (11/14 밤) 할 일 카드를 없애 다음 할 일을 알림으로
        }

        public bool Check(string name)
        {
            float mx = car.maxSteerDeg;
            switch (name)
            {
                case "steer_left_full": return car.steerDeg <= -0.9f * mx;
                case "steer_right_full": return car.steerDeg >= 0.9f * mx;
                case "steer_center": return Math.Abs(car.steerDeg) < 20f;
                case "full_circle": return turnAcc >= Geom.Tau * 0.97f;
                case "gear_p": return car.gear == Gear.P;
                case "gear_r_right_full": return car.gear == Gear.R && car.steerDeg >= 0.9f * mx;
                case "shoulder_line": return Stopped && Math.Abs(HeadingDeg) < 6f && Math.Abs(car.Shoulder.x - PerpShoulderX()) <= 0.35f;
                case "heading_30": return Stopped && HeadingDeg <= -24f && HeadingDeg >= -40f;
                case "heading_vertical": return Stopped && Math.Abs(HeadingDeg + 90f) <= 5f;
                case "rear_bumper_line":
                    {
                        float gap = ParGap();
                        return Stopped && Math.Abs(HeadingDeg) < 6f && Math.Abs(car.RearBumperCenter.x - ParPlan().rearBumperX) <= 0.35f && gap >= 0.3f && gap <= 1.3f;
                    }
                case "heading_minus45": return Stopped && HeadingDeg <= -38f && HeadingDeg >= -52f;
                case "straight_back": return Stopped && Math.Abs(car.pos.y - ParYNeeded()) <= 0.2f;
                case "parked": return false;
            }
            return false;
        }

        /// <summary>직각 후진 주차(30° 방법): 뒷축이 칸 가운데 x 에 오도록 선다 → 어깨는 그보다 (축거 − 1.25 m) 앞.</summary>
        public float PerpShoulderX() => Geom.PolyCenter(Target.poly).x + (car.wheelbase - 1.25f);

        public float MinRearRadius() => car.wheelbase / (float)Math.Tan(car.maxWheelDeg * Geom.Deg2Rad);

        public float OuterFrontCornerRadius()
        {
            float r = MinRearRadius();
            return (float)Math.Sqrt(Math.Pow(r + car.width / 2f, 2) + Math.Pow(car.wheelbase + car.frontOh, 2));
        }

        public float ParGap() => st.parRef.sideY - (car.pos.y + car.width / 2f);

        public struct ParPlanResult { public float xEnd, xA, d, rearBumperX, slack; public bool feasible; }

        /// <summary>평행 주차(45° 방법) 계획. 지금 차의 옆 위치(y)로 멈출 곳을 계산한다.</summary>
        public ParPlanResult ParPlan()
        {
            var rf = st.parRef;
            float r = MinRearRadius();
            float beta = 45f * Geom.Deg2Rad;
            float y0 = car.pos.y;
            float yEnd = ParYEnd();
            float d = Math.Max(0f, (yEnd - y0 - 2f * r * (1f - (float)Math.Cos(beta))) / (float)Math.Sin(beta));
            float rFr = OuterFrontCornerRadius();
            var k = new V2(rf.rearX, rf.sideY);
            float dy = k.y - (yEnd - r);
            float xMax = k.x - (float)Math.Sqrt(Math.Max(0f, rFr * rFr - dy * dy)) - 0.15f;
            float xMin = rf.backX + car.rearOh + 0.2f;
            float xEnd = xMax >= xMin ? (xMin + xMax) / 2f : xMin;
            float xA = xEnd + 2f * r * (float)Math.Sin(beta) + d * (float)Math.Cos(beta);
            return new ParPlanResult { xEnd = xEnd, xA = xA, d = d, rearBumperX = xA - car.rearOh, feasible = xMax >= xMin, slack = xMax - xMin };
        }

        /// <summary>다 넣었을 때 뒷축 y: 칸 가운데, 단 연석 쪽 바퀴가 연석에서 20 cm 는 떨어지게</summary>
        public float ParYEnd()
        {
            var rf = st.parRef;
            float y = rf.laneY;
            if (rf.hasCurb) y = Math.Min(y, rf.curbY - 0.2f - 0.12f - car.trackR / 2f);
            return y;
        }

        /// <summary>2단계(똑바로 후진) 뒤 3단계(왼쪽 끝까지)를 시작해야 하는 뒷축 y</summary>
        public float ParYNeeded()
        {
            float h = Math.Abs(car.heading);
            return ParYEnd() - MinRearRadius() * (1f - (float)Math.Cos(h));
        }

        /// <summary>튜토리얼 안내 표시(초록 선)</summary>
        public List<Guide> Guides()
        {
            var outList = new List<Guide>();
            if (steps.Count == 0 || stepI >= steps.Count || state != PlayState.Play) return outList;
            string check = steps[stepI].check;
            switch (check)
            {
                case "shoulder_line":
                    {
                        float x = PerpShoulderX(), y = car.pos.y;
                        outList.Add(new Guide { a = new V2(x, y - 2.2f), b = new V2(x, y + 2.2f), label = "어깨를 여기에", mark = "shoulder" });
                        break;
                    }
                case "rear_bumper_line":
                    {
                        float x = ParPlan().rearBumperX, y = car.pos.y;
                        outList.Add(new Guide { a = new V2(x, y - 1.8f), b = new V2(x, y + 1.8f), label = "뒤 범퍼를 여기에", mark = "rear_bumper" });
                        break;
                    }
                case "straight_back":
                    {
                        float yn = ParYNeeded();
                        float dy = car.Dir.y;
                        if (Math.Abs(dy) < 0.05f) break;
                        float s = (yn - car.pos.y) / dy;
                        var p = car.pos + car.Dir * s;
                        var n = car.Left;
                        outList.Add(new Guide { a = p - n * 1.3f, b = p + n * 1.3f, label = "뒷바퀴 축을 여기까지", mark = "rear_axle" });
                        break;
                    }
                case "heading_30":
                case "heading_vertical":
                case "heading_minus45":
                    {
                        float deg = check == "heading_30" ? -30f : (check == "heading_vertical" ? -90f : -45f);
                        var d = V2.FromAngle(deg * Geom.Deg2Rad);
                        var c = car.Center;
                        outList.Add(new Guide { a = c - d * 3.6f, b = c + d * 3.6f, label = "이 선과 나란히", mark = "" });
                        break;
                    }
            }
            return outList;
        }

        /// <summary>단계를 못 넘긴 채 멈췄을 때 감각적인 힌트(숫자 없이)</summary>
        void Hint(string name)
        {
            float hd = HeadingDeg;
            switch (name)
            {
                case "heading_30": Say(hd > -24f ? "조금 더 앞으로" : "너무 돌았습니다. 조금 후진해서 맞춰 보십시오"); break;
                case "heading_vertical": Say(hd > -85f ? "조금 더 후진" : "너무 돌았습니다. 스티어링 휠을 풀고 조금 앞으로"); break;
                case "heading_minus45": Say(hd > -38f ? "조금 더 후진" : "너무 돌았습니다. 조금 앞으로 가서 맞춰 보십시오"); break;
                case "rear_bumper_line":
                    {
                        float gap = ParGap();
                        if (gap < 0.3f) Say("앞차와 너무 붙었습니다");
                        else if (gap > 1.3f) Say("앞차와 너무 멉니다");
                        else Say("뒤 범퍼를 초록 선에 맞춰 보십시오");
                        break;
                    }
                case "shoulder_line": Say("어깨(흰 점)를 초록 선에 맞춰 보십시오"); break;
                case "straight_back": Say("흰 점을 초록 선에 맞춰 보십시오"); break;
            }
        }
    }
}
