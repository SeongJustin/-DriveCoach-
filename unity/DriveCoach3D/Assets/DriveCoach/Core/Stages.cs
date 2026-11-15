// 담당: S
// 25단계와 튜토리얼 3개의 지도(전부 m 단위, 실제 규격).
// 주차장법 시행규칙: 일반형 직각 주차칸 2.5 x 5.0 m, 평행 주차칸 2.0 x 6.0 m, 직각 주차 차로 6.0 m.
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public struct Seg { public V2 a, b; public Seg(V2 a, V2 b) { this.a = a; this.b = b; } }

    /// <summary>주차된 차. c = 가운데, h = 방향(도), col = 색 번호(Stages.CarColors)</summary>
    public class ParkedCar { public V2 c; public float h; public int col; public float len, wid; }

    public class FloorArrow { public V2 p; public float h; }

    /// <summary>바닥 재질(3D 화면). 주차장 아스팔트·지하 콘크리트·잔디 등.</summary>
    public enum Surface { Asphalt, Concrete, Grass, /** 실제 동네의 건물 사이 땅(무광 포장) */ Block }

    public class Floor
    {
        public string name;
        public Rect ground;
        public bool dark;
        public Surface surface = Surface.Asphalt;
        public List<Seg> lines = new List<Seg>();
        public List<Seg> yellow = new List<Seg>();
        public List<V2[]> walls = new List<V2[]>();
        public List<V2[]> pillars = new List<V2[]>();
        public List<ParkedCar> cars = new List<ParkedCar>();
        public List<V2[]> crosswalks = new List<V2[]>();
        public List<FloorArrow> arrows = new List<FloorArrow>();
        public List<V2[]> curbs = new List<V2[]>();
        public List<Rect> walks = new List<Rect>();
    }

    public enum TargetKind { None, Slot, Line }

    public class Target
    {
        public TargetKind kind = TargetKind.None;
        public int floor;
        // 칸
        public V2[] poly;
        public float heading;
        public float tol = 6f;
        public bool either;
        public bool hasCenter;
        public V2 center;
        public float len = -1f;   // 칸 안쪽 길이(없으면 -1 → 다각형에서 잰다)
        // 선
        public V2 p, n;
        public bool frontBumper = true;

        public bool IsEmpty => kind == TargetKind.None;
        public V2 Center => kind == TargetKind.Line ? p : (hasCenter ? center : Geom.PolyCenter(poly));
    }

    public class PedDef { public int floor; public V2[] path; public float speed = 1.2f, delay; public bool loop; }

    public class Ramp
    {
        public V2 a, b;
        public float w, grade;
        public int lower, upper;
        public V2[] poly;
    }

    public class TutorialStep { public string text, check; public TutorialStep(string t, string c) { text = t; check = c; } }

    /// <summary>평행 주차 계산에 쓰는 기준(앞차 뒤 모서리 등)</summary>
    public class ParRef { public float rearX, sideY, laneY, backX; public bool hasCurb; public float curbY; }

    /// <summary>판의 종류. 주차(단계·튜토리얼), 서킷(핸들 감각), 도로 주행(미리보기).</summary>
    public enum StageMode { Parking, Circuit, Road }

    public class Stage
    {
        public StageMode mode = StageMode.Parking;
        /// <summary>서킷이면 트랙(Circuit.cs), 도로 주행이면 도로망(Road.cs). 주차면 null.</summary>
        public CircuitDef circuit;
        public RoadDef road;
        public int id;
        public string title, goal;
        public List<Floor> floors = new List<Floor>();
        public V2 startC;
        public float startH;
        public Target target = new Target();
        public List<PedDef> peds = new List<PedDef>();
        public List<Ramp> ramps = new List<Ramp>();
        public List<V2[]> pads = new List<V2[]>();
        public int parMoves = 3;
        public List<TutorialStep> steps = new List<TutorialStep>();
        public ParRef parRef;
    }

    public static partial class Stages
    {
        public const float SW = 2.5f, SL = 5.0f, AISLE = 6.0f, PW = 2.0f, PL = 6.0f;
        public const int COUNT = 25;

        /// <summary>주차된 차 색(RGB 0..1)</summary>
        public static readonly float[][] CarColors =
        {
            new[] { 0.20f, 0.22f, 0.25f }, new[] { 0.62f, 0.64f, 0.68f }, new[] { 0.55f, 0.10f, 0.12f }, new[] { 0.86f, 0.87f, 0.89f },
            new[] { 0.15f, 0.25f, 0.42f }, new[] { 0.35f, 0.37f, 0.40f }, new[] { 0.92f, 0.92f, 0.94f }, new[] { 0.28f, 0.33f, 0.30f },
        };

        public static readonly string[] TutorialNames = { "스티어링 휠 감각", "직각 주차(후진)", "평행 주차" };

        static V2 P(float x, float y) => new V2(x, y);

        public static Stage Get(int i)
        {
            switch (i)
            {
                case 1: return S01();
                case 2: return S02();
                case 3: return S03();
                case 4: return PerpStage(4, "직각 전진 주차(빈 칸)", "앞으로 들어가 연두색 칸에 넣으십시오", Occ(), false, AISLE, false);
                case 5: return PerpStage(5, "직각 전진 주차(한쪽 차)", "옆 차에 닿지 않게 앞으로 넣으십시오", Occ(5, "out"), false, AISLE, false);
                case 6: return PerpStage(6, "직각 전진 주차(양쪽 차)", "양쪽 차 사이로 앞으로 넣으십시오", Occ(5, "out", 7, "in", 2, "out", 9, "in"), false, AISLE, false);
                case 7: return PerpStage(7, "직각 후진 주차(빈 주차장)", "후진으로 연두색 칸에 넣으십시오(앞이 통로를 보게)", Occ(), true, AISLE, false);
                case 8: return PerpStage(8, "직각 후진 주차(양옆 차)", "양옆 차 사이로 후진 주차하십시오", Occ(5, "out", 7, "out", 3, "in", 9, "out"), true, AISLE, true);
                case 9: return PerpStage(9, "직각 후진 주차(좁은 통로)", "통로가 좁습니다. 맞은편 차에 닿지 않게 후진 주차하십시오", Occ(5, "out", 7, "in", 4, "out", 8, "out"), true, 5.0f, true);
                case 10: return S10();
                case 11: return DiagStage(11, "사선 주차(빈 칸)", "비스듬한 칸에 앞으로 넣으십시오", new HashSet<int>());
                case 12: return DiagStage(12, "사선 주차(양쪽 차)", "양쪽 차 사이 비스듬한 칸에 앞으로 넣으십시오", new HashSet<int> { 3, 5, 1, 7 });
                case 13: return ParallelStage(13, "평행 주차(넓은 칸)", "넉넉한 길가 칸에 평행 주차하십시오", 8.0f, 0.65f, false);
                case 14: return ParallelStage(14, "평행 주차(규격 칸)", "앞뒤 차 사이 칸에 평행 주차하십시오", PL, 0.65f, true);
                case 15: return ParallelStage(15, "평행 주차(빠듯한 칸)", "앞뒤 차가 칸 선에 바짝 붙어 있습니다. 여러 번 나눠 넣어도 됩니다", PL, 0.2f, true);
                case 16: return S16();
                case 17: return S17();
                case 18: return S18();
                case 19: return S19();
                case 20: return S20();
                case 21: return S21();
                case 22: return S22();
                case 23: return S23();
                case 24: return S24();
                case 25: return S25();
            }
            return S01();
        }

        public static Stage Tutorial(int i)
        {
            switch (i)
            {
                case 2: return TPerp();
                case 3: return TParallel();
                default: return TSteering();
            }
        }

        // ───────────────────────── 만들기 도구 ─────────────────────────

        /// <summary>칸 번호 → "in"(앞부터 들어감) | "out"(후진 주차, 앞이 통로)</summary>
        static Dictionary<int, string> Occ(params object[] kv)
        {
            var d = new Dictionary<int, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(int)kv[i]] = (string)kv[i + 1];
            return d;
        }

        static Floor MakeFloor(string name, Rect ground, bool dark = false)
            => new Floor { name = name, ground = ground, dark = dark, surface = dark ? Surface.Concrete : Surface.Asphalt };

        static Stage MakeStage(int id, string title, string goal) => new Stage { id = id, title = title, goal = goal };

        static void Wall(Floor f, V2 a, V2 b, float thick = 0.3f)
        {
            var c = (a + b) / 2f;
            float ang = (b - a).Angle * Geom.Rad2Deg;
            f.walls.Add(Geom.RectPoly(c, a.DistanceTo(b) + thick, thick, ang));
        }

        /// <summary>둘레 벽(지하주차장·막힌 곳)</summary>
        static void Border(Floor f, Rect r)
        {
            Wall(f, r.Position, P(r.End.x, r.y));
            Wall(f, P(r.End.x, r.y), r.End);
            Wall(f, r.End, P(r.x, r.End.y));
            Wall(f, P(r.x, r.End.y), r.Position);
        }

        static void Pillar(Floor f, V2 c, float size = 0.6f) => f.pillars.Add(Geom.RectPoly(c, size, size));

        static void Car(Floor f, V2 c, float h, int idx, float len = 4.7f, float wid = 1.86f)
            => f.cars.Add(new ParkedCar { c = c, h = h, col = ((idx % CarColors.Length) + CarColors.Length) % CarColors.Length, len = len, wid = wid });

        /// <summary>
        /// 직각 주차칸 한 줄. x0 부터 n 칸, 입구가 yEdge, side=+1 이면 아래로(+y) SL, -1 이면 위로.
        /// occ 칸에 차를 세운다. 칸 다각형 목록을 돌려준다. pgap &gt; 0 이면 세 칸마다 pgap 만큼 띄우고 기둥.
        /// </summary>
        static List<V2[]> PerpRow(Floor f, float x0, float yEdge, int n, int side, Dictionary<int, string> occ, int colorSeed = 0, float pgap = 0f)
        {
            var slots = new List<V2[]>();
            float yBack = yEdge + side * SL;
            float xEnd = x0;
            for (int i = 0; i < n; i++)
            {
                float xa = x0 + i * SW + (i / 3) * pgap;
                float xb = xa + SW;
                xEnd = xb;
                f.lines.Add(new Seg(P(xa, yEdge), P(xa, yBack)));
                bool groupEnd = pgap > 0f && (i % 3 == 2);
                if (i == n - 1 || groupEnd) f.lines.Add(new Seg(P(xb, yEdge), P(xb, yBack)));
                if (pgap > 0f && groupEnd && i < n - 1) Pillar(f, P(xb + pgap / 2f, yEdge + side * 0.45f), pgap - 0.1f);
                float ylo = Math.Min(yEdge, yBack), yhi = Math.Max(yEdge, yBack);
                slots.Add(new[] { P(xa, ylo), P(xb, ylo), P(xb, yhi), P(xa, yhi) });
                if (occ.TryGetValue(i, out var how))
                {
                    float into = 90f * side;
                    float h = how == "in" ? into : into + 180f;
                    var c = P(xa + SW / 2f, yEdge + side * (SL / 2f + 0.05f));
                    bool big = (i + colorSeed) % 3 == 0;
                    Car(f, c, h, i + colorSeed, big ? 4.8f : 4.65f, big ? 1.9f : 1.84f);
                }
            }
            f.lines.Add(new Seg(P(x0, yBack), P(xEnd, yBack)));
            return slots;
        }

        static Target SlotTarget(int floorI, V2[] poly, float heading, float tol = 6f, bool either = false)
            => new Target { kind = TargetKind.Slot, floor = floorI, poly = poly, heading = heading, tol = tol, either = either };

        /// <summary>(11/15) 차 한 대가 들어가는 직선 칸(길이 BoxL·폭 SW): 1~3단계 "칸에 맞춰 들어가기"</summary>
        const float BoxL = 5.4f;
        static Target BoxTarget(V2 c, float heading, float len = BoxL)
            => SlotTarget(0, Geom.RectPoly(c, len, SW, heading), heading);

        static PedDef Ped(int floorI, V2[] path, float speed = 1.2f, float delay = 0f, bool loop = false)
            => new PedDef { floor = floorI, path = path, speed = speed, delay = delay, loop = loop };

        static Ramp MakeRamp(V2 a, V2 b, float w, float grade, int lower, int upper)
        {
            var c = (a + b) / 2f;
            float ang = (b - a).Angle * Geom.Rad2Deg;
            return new Ramp { a = a, b = b, w = w, grade = grade, lower = lower, upper = upper, poly = Geom.RectPoly(c, a.DistanceTo(b), w, ang) };
        }

        // ───────────────────────── 1~3 기본 감각 ─────────────────────────

        static Floor LaneFloor(float len = 40f)
        {
            var f = MakeFloor("연습장", new Rect(-14, -7, len + 18, 14));
            f.yellow.Add(new Seg(P(-14, -3.6f), P(len + 4, -3.6f)));
            f.yellow.Add(new Seg(P(-14, 3.6f), P(len + 4, 3.6f)));
            Wall(f, P(-14, -4.2f), P(len + 4, -4.2f), 0.4f);
            Wall(f, P(-14, 4.2f), P(len + 4, 4.2f), 0.4f);
            return f;
        }

        static Stage S01()
        {
            // (11/15 대표: "선에 맞춰 서기는 아닌 것 같다, 칸에 맞춰 들어가기로") 1~3단계 목표를 선 대신 칸으로
            var s = MakeStage(1, "브레이크로 서기", "기어 D, 브레이크를 떼면 천천히 갑니다. 앞의 연두색 칸에 차가 다 들어가면 브레이크로 세우십시오");
            var f = LaneFloor();
            Wall(f, P(34, -4.2f), P(34, 4.2f), 0.4f);
            f.arrows.Add(new FloorArrow { p = P(6, 0), h = 0f });
            s.floors.Add(f);
            s.startC = P(-6, 0); s.startH = 0f;
            s.target = BoxTarget(P(18f - BoxL / 2f, 0), 0f);
            s.parMoves = 1;
            return s;
        }

        static Stage S02()
        {
            var s = MakeStage(2, "후진으로 서기", "기어 R로 바꾸고, 뒤쪽 연두색 칸에 차가 다 들어가게 후진해 세우십시오");
            var f = LaneFloor();
            Wall(f, P(-13, -4.2f), P(-13, 4.2f), 0.4f);
            s.floors.Add(f);
            s.startC = P(14, 0); s.startH = 0f;
            s.target = BoxTarget(P(-2f + BoxL / 2f, 0), 0f);
            s.parMoves = 1;
            return s;
        }

        static Stage S03()
        {
            var s = MakeStage(3, "벽 앞에 바짝", "벽에 닿지 않게, 벽 바로 앞 연두색 칸에 차를 넣어 세우십시오. 센서 소리를 들어 보십시오");
            var f = LaneFloor();
            Wall(f, P(24, -4.2f), P(24, 4.2f), 0.4f);
            s.floors.Add(f);
            s.startC = P(-4, 0); s.startH = 0f;
            s.target = BoxTarget(P(23.7f - 5.2f / 2f, 0), 0f, 5.2f);     // 벽 면(23.8)에서 0.1 m 앞까지, 짧은 칸이라 바짝 붙여야 들어간다
            s.parMoves = 1;
            return s;
        }

        // ───────────────────────── 4~10 직각 주차 ─────────────────────────

        /// <summary>통로(가로)와 아래·위 주차칸 줄. 목표는 아래 줄 6번 칸(x 5.0~7.5).</summary>
        /// <summary>(11/14 대표: "직각 주차도 실제 게임 안에서 주차하는 모습으로") 실제 차 직각 주차 안내 그림용 주차장: 8단계와 같되 맞은편 줄은 비운다(45° 로 앞머리를 내밀 자리)</summary>
        public static Stage ParkingGuide() => PerpStage(8, "직각 후진 주차", "", Occ(5, "out", 7, "out", 3, "in", 9, "out"), true, AISLE, false);
        /// <summary>(11/15) 앱의 실제 차 평행 주차 안내 영상용: 규격 칸(6 m), 앞뒤 차(14단계와 같은 자리)</summary>
        public static Stage ParallelGuide() => ParallelStage(14, "평행 주차", "", PL, 0.65f, true);

        static Stage PerpStage(int id, string title, string goal, Dictionary<int, string> occ, bool backIn, float aisle, bool upperCars, bool dark = false, float pgap = 0f)
        {
            var s = MakeStage(id, title, goal);
            float half = aisle / 2f;
            var f = MakeFloor(dark ? "B1" : "주차장", new Rect(-16, -half - SL - 2, 50, aisle + 2 * SL + 4), dark);
            var below = PerpRow(f, -10f, half, 12, 1, occ, id, pgap);
            var upOcc = upperCars ? Occ(1, "out", 2, "in", 4, "out", 5, "out", 6, "in", 7, "out", 9, "out", 10, "in") : Occ();
            PerpRow(f, -10f, -half, 12, -1, upOcc, id + 3, pgap);
            Border(f, new Rect(-15, -half - SL - 0.3f, 47, aisle + 2 * SL + 0.6f));
            s.floors.Add(f);
            s.startC = P(-11, 0); s.startH = 0f;
            s.target = SlotTarget(0, below[6], backIn ? -90f : 90f);
            s.parMoves = backIn ? 3 : 2;
            return s;
        }

        static Stage S10() => PerpStage(10, "직각 후진 주차(기둥 옆)", "지하주차장. 세 칸마다 기둥이 있습니다. 기둥 옆 연두색 칸에 후진 주차하십시오",
            Occ(7, "out", 3, "out", 9, "in"), true, AISLE, true, true, 0.8f);

        // ───────────────────────── 11~12 사선, 13~15 평행 주차 ─────────────────────────

        /// <summary>45° 사선 칸. 칸(2.5 x 5.0)을 감싸는 평행사변형의 긴 변 = 7.5 m</summary>
        static Stage DiagStage(int id, string title, string goal, HashSet<int> occ)
        {
            var s = MakeStage(id, title, goal);
            var f = MakeFloor("주차장", new Rect(-16, -8, 50, 20));
            float yEdge = 2.5f;
            float a45 = 45f * Geom.Deg2Rad;
            float stepX = SW / (float)Math.Sin(a45);
            var dv = new V2((float)Math.Cos(a45), (float)Math.Sin(a45)) * (SL + SW);
            var slots = new List<V2[]>();
            for (int i = 0; i < 10; i++)
            {
                var a = P(-8 + i * stepX, yEdge);
                f.lines.Add(new Seg(a, a + dv));
            }
            for (int i = 0; i < 9; i++)
            {
                var a = P(-8 + i * stepX, yEdge);
                var b = P(-8 + (i + 1) * stepX, yEdge);
                slots.Add(new[] { a, b, b + dv, a + dv });
                if (occ.Contains(i)) Car(f, (a + b) / 2f + dv / 2f, 45f, i + id, 4.7f, 1.85f);
            }
            f.lines.Add(new Seg(P(-8, yEdge) + dv, P(-8 + 9 * stepX, yEdge) + dv));
            f.arrows.Add(new FloorArrow { p = P(-6, -0.5f), h = 0f });
            Border(f, new Rect(-15, -4.5f, 47, 13.3f));
            s.floors.Add(f);
            s.startC = P(-11, -0.5f); s.startH = 0f;
            var tg = SlotTarget(0, slots[4], 45f);
            tg.len = SL;
            s.target = tg;
            s.parMoves = 1;
            return s;
        }

        /// <summary>아래쪽이 보도 턱, 그 위 평행 주차 차로(폭 2 m), 그 위 주행 차로.</summary>
        static Stage ParallelStage(int id, string title, string goal, float slotLen, float gap, bool cars)
        {
            var s = MakeStage(id, title, goal);
            var f = MakeFloor("길가", new Rect(-26, -7, 72, 12));
            float yTop = 1.2f;
            float yCurb = yTop + PW;
            f.lines.Add(new Seg(P(-26, yTop), P(46, yTop)));
            f.yellow.Add(new Seg(P(-26, -4.6f), P(46, -4.6f)));
            f.curbs.Add(Geom.RectPoly(P(10, yCurb + 0.15f), 72, 0.3f));
            f.walks.Add(new Rect(-26, yCurb + 0.3f, 72, 1.5f));
            Wall(f, P(-26, -5.2f), P(46, -5.2f), 0.4f);
            float x0 = 6f, x1 = x0 + slotLen;
            foreach (var x in new[] { x0 - 2 * PL, x0 - PL, x0, x1, x1 + PL, x1 + 2 * PL })
                f.lines.Add(new Seg(P(x, yTop), P(x, yCurb)));
            float laneY = yTop + PW / 2f;
            if (cars)
            {
                Car(f, P(x0 - gap - 2.35f, laneY), 0f, id, 4.7f, 1.85f);        // 뒤차
                Car(f, P(x1 + gap + 2.4f, laneY), 0f, id + 1, 4.8f, 1.88f);     // 앞차
                Car(f, P(x0 - PL - 3.0f, laneY), 0f, id + 2, 4.5f, 1.8f);
                Car(f, P(x1 + PL + 3.1f, laneY), 0f, id + 3, 4.9f, 1.9f);
            }
            var poly = new[] { P(x0, yTop - 0.3f), P(x1, yTop - 0.3f), P(x1, yCurb), P(x0, yCurb) };
            s.floors.Add(f);
            s.startC = P(-10, -1.6f); s.startH = 0f;
            var tg = SlotTarget(0, poly, 0f, 6f, true);
            tg.hasCenter = true; tg.center = P((x0 + x1) / 2f, laneY);
            s.target = tg;
            s.parRef = new ParRef { rearX = x1 + gap, sideY = laneY - 1.88f / 2f, laneY = laneY, hasCurb = true, curbY = yCurb, backX = cars ? x0 - gap : x0 - 0.2f };
            s.parMoves = gap < 0.5f ? 4 : 3;
            return s;
        }

        // ───────────────────────── 16~18 주차장 돌아다니기 ─────────────────────────

        class LoopLot { public Floor floor; public List<V2[]> top, bottom; }

        /// <summary>가운데 섬(양쪽 줄)과 바깥 줄이 있는 고리형 주차장. 시계 방향 일방통행.</summary>
        static LoopLot MakeLoopLot(int id, bool dark = false)
        {
            var f = MakeFloor("주차장", new Rect(-6, -18, 52, 36), dark);
            Border(f, new Rect(-4, -16.3f, 48.5f, 32.6f));
            float x0 = 4f;
            int n = 13;
            PerpRow(f, x0, -5f, n, 1, Occ(0, "out", 2, "in", 3, "out", 5, "out", 6, "out", 8, "in", 9, "out", 11, "out", 12, "in"), id);
            PerpRow(f, x0, 5f, n, -1, Occ(1, "out", 2, "out", 4, "in", 5, "out", 7, "out", 8, "out", 10, "in", 12, "out"), id + 1);
            var top = PerpRow(f, x0, -11f, n, -1, Occ(0, "out", 1, "in", 3, "out", 4, "out", 6, "in", 7, "out", 9, "out", 10, "out", 12, "in"), id + 2);
            var bottom = PerpRow(f, x0, 11f, n, 1, Occ(0, "out", 1, "out", 3, "in", 4, "out", 6, "out", 8, "out", 9, "in", 11, "out", 12, "out"), id + 3);
            f.arrows.Add(new FloorArrow { p = P(14, -8), h = 0f });
            f.arrows.Add(new FloorArrow { p = P(40.5f, -2), h = 90f });
            f.arrows.Add(new FloorArrow { p = P(30, 8), h = 180f });
            f.arrows.Add(new FloorArrow { p = P(0, 2), h = -90f });
            return new LoopLot { floor = f, top = top, bottom = bottom };
        }

        static Stage S16()
        {
            var s = MakeStage(16, "주차장 돌아보기", "화살표를 따라 한 바퀴 돌아 아래쪽 연두색 칸에 앞으로 넣으십시오");
            var lot = MakeLoopLot(16);
            s.floors.Add(lot.floor);
            s.startC = P(0, -8); s.startH = 0f;
            s.target = SlotTarget(0, lot.bottom[5], 90f);
            s.parMoves = 2;
            return s;
        }

        static Stage S17()
        {
            var s = MakeStage(17, "보행자 주의", "사람이 통로를 건넙니다. 기다렸다가 아래쪽 연두색 칸에 후진 주차하십시오");
            var lot = MakeLoopLot(17);
            var f = lot.floor;
            f.crosswalks.Add(Geom.RectPoly(P(20, -8), 3.0f, 6.0f));
            f.crosswalks.Add(Geom.RectPoly(P(40.5f, 4), 8.0f, 3.0f));
            s.floors.Add(f);
            s.peds.Add(Ped(0, new[] { P(20, -11.4f), P(20, -4.6f) }, 1.1f, 2.0f));
            s.peds.Add(Ped(0, new[] { P(37, 4), P(44, 4) }, 1.0f, 6.0f));
            s.peds.Add(Ped(0, new[] { P(30, 9.8f), P(8, 9.8f) }, 1.2f, 0.0f));
            s.startC = P(0, -8); s.startH = 0f;
            s.target = SlotTarget(0, lot.bottom[7], -90f);
            s.parMoves = 3;
            return s;
        }

        static Stage S18()
        {
            var s = MakeStage(18, "막다른 통로", "통로 끝이 막혀 돌아 나갈 수 없습니다. 끝 쪽 연두색 칸에 후진 주차하십시오");
            var f = MakeFloor("주차장", new Rect(-16, -10, 46, 20));
            var below = PerpRow(f, -10f, 3f, 10, 1, Occ(0, "out", 1, "in", 3, "out", 4, "out", 6, "out", 9, "in"), 18);
            PerpRow(f, -10f, -3f, 10, -1, Occ(0, "out", 2, "out", 3, "in", 5, "out", 6, "out", 7, "in", 8, "out", 9, "out"), 21);
            Border(f, new Rect(-15, -8.3f, 33, 16.6f));
            s.floors.Add(f);
            s.startC = P(-11, 0); s.startH = 0f;
            s.target = SlotTarget(0, below[8], -90f);
            s.parMoves = 4;
            return s;
        }

        // ───────────────────────── 19~21 지하·진입로 ─────────────────────────

        static Stage S19()
        {
            var s = PerpStage(19, "지하주차장 기둥 사이", "좁은 통로, 기둥 사이 연두색 칸에 후진 주차하십시오",
                Occ(1, "out", 2, "out", 4, "in", 5, "out", 7, "out", 8, "in", 10, "out"), true, 5.5f, true, true, 0.8f);
            s.floors[0].name = "B2";
            return s;
        }

        /// <summary>폭 w 의 꺾인 통로 벽(가운데선 pts 를 따라)</summary>
        static void Corridor(Floor f, V2[] pts, float w)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var offs = new List<V2>();
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = pts[i];
                    var n = V2.Zero;
                    if (i > 0) { var d1 = (p - pts[i - 1]).Normalized; n += new V2(-d1.y, d1.x); }
                    if (i < pts.Length - 1) { var d2 = (pts[i + 1] - p).Normalized; n += new V2(-d2.y, d2.x); }
                    n = n.Normalized;
                    float scale = 1f;
                    if (i > 0 && i < pts.Length - 1)
                    {
                        var d1b = (p - pts[i - 1]).Normalized;
                        scale = 1f / Math.Max(0.5f, n.Dot(new V2(-d1b.y, d1b.x)));
                    }
                    offs.Add(p + n * side * (w / 2f + 0.15f) * scale);
                }
                for (int i = 0; i < offs.Count - 1; i++) Wall(f, offs[i], offs[i + 1], 0.3f);
            }
        }

        /// <summary>통로 시작 쪽을 막는 벽</summary>
        static void Cap(Floor f, V2 p, V2 dir, float w)
        {
            var n = new V2(-dir.y, dir.x).Normalized * (w / 2f + 0.3f);
            Wall(f, p - n, p + n, 0.3f);
        }

        static Stage S20()
        {
            var s = MakeStage(20, "직각 진입로", "좁은 진입로가 직각으로 꺾입니다. 내려가서 연두색 칸에 후진 주차하십시오");
            var f = MakeFloor("B1", new Rect(-28, -22, 64, 32), true);
            Corridor(f, new[] { P(-26, -18), P(-2, -18), P(-2, -3) }, 3.6f);
            Cap(f, P(-26.15f, -18), P(1, 0), 3.6f);
            var below = PerpRow(f, -10f, 3f, 12, 1, Occ(1, "out", 2, "in", 4, "out", 5, "out", 7, "out", 8, "in", 10, "out"), 20);
            PerpRow(f, 2f, -3f, 7, -1, Occ(0, "out", 2, "out", 3, "in", 5, "out", 6, "out"), 23);
            Wall(f, P(-16, -3.15f), P(-3.95f, -3.15f));
            Wall(f, P(-16, -3.15f), P(-16, 8.3f));
            Wall(f, P(-16, 8.3f), P(34, 8.3f));
            Wall(f, P(34, 8.3f), P(34, -8.3f));
            Wall(f, P(34, -8.3f), P(-0.05f, -8.3f));
            f.arrows.Add(new FloorArrow { p = P(-14, -18), h = 0f });
            f.arrows.Add(new FloorArrow { p = P(-2, -10), h = 90f });
            s.floors.Add(f);
            s.startC = P(-21, -18); s.startH = 0f;
            s.target = SlotTarget(0, below[6], -90f);
            s.parMoves = 4;
            return s;
        }

        static Stage S21()
        {
            var s = MakeStage(21, "좁은 S자 통로", "좁은 S자 통로를 지나, 사람을 조심하며 연두색 칸에 앞으로 넣으십시오");
            var f = MakeFloor("B1", new Rect(-36, -14, 74, 24), true);
            Corridor(f, new[] { P(-34, -9), P(-22, -9), P(-17, 0), P(-6, 0) }, 3.6f);
            Cap(f, P(-34.15f, -9), P(1, 0), 3.6f);
            var below = PerpRow(f, -4f, 3f, 14, 1, Occ(0, "out", 2, "out", 3, "in", 5, "out", 6, "out", 7, "in", 9, "out", 11, "out", 12, "out"), 21);
            PerpRow(f, -4f, -3f, 14, -1, Occ(0, "out", 1, "in", 3, "out", 4, "out", 6, "in", 7, "out", 8, "out", 10, "in", 11, "out", 13, "out"), 25);
            Wall(f, P(-6, -8.3f), P(36, -8.3f));
            Wall(f, P(36, -8.3f), P(36, 8.3f));
            Wall(f, P(36, 8.3f), P(-6, 8.3f));
            Wall(f, P(-6, 8.3f), P(-6, 1.95f));
            Wall(f, P(-6, -1.95f), P(-6, -8.3f));
            f.crosswalks.Add(Geom.RectPoly(P(11, 0), 3.0f, 6.0f));
            s.floors.Add(f);
            s.peds.Add(Ped(0, new[] { P(11, -3.3f), P(11, 3.3f) }, 1.0f, 3.0f));
            s.startC = P(-30, -9); s.startH = 0f;
            s.target = SlotTarget(0, below[8], 90f);
            s.parMoves = 3;
            return s;
        }

        // ───────────────────────── 22~25 층 이동·종합 ─────────────────────────

        static Stage S22()
        {
            var s = MakeStage(22, "경사로로 위층", "B1에서 경사로를 올라가 1층 연두색 칸에 후진 주차하십시오. 오르막은 차가 느려집니다.");
            var b1 = MakeFloor("B1", new Rect(-22, -10, 24, 20), true);
            Wall(b1, P(-20, -8.3f), P(2, -8.3f));
            Wall(b1, P(2, -8.3f), P(2, -2.25f));
            Wall(b1, P(2, 2.25f), P(2, 8.3f));
            Wall(b1, P(2, 8.3f), P(-20, 8.3f));
            Wall(b1, P(-20, 8.3f), P(-20, -8.3f));
            Wall(b1, P(2, -2.25f), P(20, -2.25f));
            Wall(b1, P(2, 2.25f), P(20, 2.25f));
            PerpRow(b1, -17.5f, 3f, 6, 1, Occ(0, "out", 1, "out", 3, "in", 4, "out", 5, "out"), 22);
            PerpRow(b1, -17.5f, -3f, 6, -1, Occ(0, "in", 2, "out", 3, "out", 5, "in"), 25);
            var f1 = MakeFloor("1F", new Rect(20, -10, 34, 20));
            var below = PerpRow(f1, 22f, 3f, 10, 1, Occ(0, "out", 1, "in", 3, "out", 5, "out", 6, "out", 8, "in", 9, "out"), 23);
            PerpRow(f1, 22f, -3f, 10, -1, Occ(0, "out", 2, "out", 3, "out", 5, "in", 7, "out", 8, "out"), 24);
            Wall(f1, P(20, -8.3f), P(50, -8.3f));
            Wall(f1, P(50, -8.3f), P(50, 8.3f));
            Wall(f1, P(50, 8.3f), P(20, 8.3f));
            Wall(f1, P(20, 8.3f), P(20, 2.25f));
            Wall(f1, P(20, -2.25f), P(20, -8.3f));
            b1.arrows.Add(new FloorArrow { p = P(-8, 0), h = 0f });
            s.floors.Add(b1);
            s.floors.Add(f1);
            s.ramps.Add(MakeRamp(P(2, 0), P(20, 0), 4.2f, 0.12f, 0, 1));
            s.startC = P(-14, 0); s.startH = 0f;
            s.target = SlotTarget(1, below[4], -90f);
            s.parMoves = 3;
            return s;
        }

        /// <summary>1층에서 동쪽으로 오르다 직각으로 꺾여 북쪽 2층으로. 폭 3.6 m, 경사 14%.</summary>
        static Stage S23()
        {
            var s = MakeStage(23, "좁고 꺾인 경사로", "좁은 경사로가 직각으로 꺾입니다. 2층 기둥 옆 연두색 칸에 후진 주차하십시오");
            var f1 = MakeFloor("1F", new Rect(-22, -6, 28, 16), true);
            Wall(f1, P(-20, -4.3f), P(4, -4.3f));
            Wall(f1, P(4, -4.3f), P(4, -1.95f));
            Wall(f1, P(4, 1.95f), P(4, 8.3f));
            Wall(f1, P(4, 8.3f), P(-20, 8.3f));
            Wall(f1, P(-20, 8.3f), P(-20, -4.3f));
            PerpRow(f1, -17.5f, 3f, 8, 1, Occ(0, "out", 2, "in", 3, "out", 5, "out", 6, "out"), 23);
            Corridor(f1, new[] { P(4, 0), P(16, 0), P(16, -20) }, 3.6f);
            f1.arrows.Add(new FloorArrow { p = P(-8, -0.5f), h = 0f });
            var f2 = MakeFloor("2F", new Rect(-4, -33, 44, 19), true);
            Wall(f2, P(-3, -31.3f), P(38.5f, -31.3f));
            Wall(f2, P(38.5f, -31.3f), P(38.5f, -14.7f));
            Wall(f2, P(38.5f, -14.7f), P(17.95f, -14.7f));
            Wall(f2, P(14.05f, -14.7f), P(-3, -14.7f));
            Wall(f2, P(-3, -14.7f), P(-3, -31.3f));
            var north = PerpRow(f2, -1f, -26f, 14, -1, Occ(0, "out", 1, "in", 3, "out", 4, "out", 6, "in", 7, "out", 8, "out", 10, "out", 11, "in", 13, "out"), 26, 0.8f);
            PerpRow(f2, -1f, -20f, 6, 1, Occ(0, "out", 2, "in", 3, "out", 5, "out"), 27);
            PerpRow(f2, 18.5f, -20f, 7, 1, Occ(0, "out", 1, "out", 3, "in", 4, "out", 6, "out"), 28);
            s.floors.Add(f1);
            s.floors.Add(f2);
            s.ramps.Add(MakeRamp(P(4, 0), P(14.2f, 0), 3.6f, 0.14f, 0, 1));
            s.ramps.Add(MakeRamp(P(16, -1.8f), P(16, -20), 3.6f, 0.14f, 0, 1));
            s.pads.Add(Geom.RectPoly(P(16, 0), 3.6f, 3.6f));
            s.startC = P(-14, 0); s.startH = 0f;
            s.target = SlotTarget(1, north[9], 90f);
            s.parMoves = 4;
            return s;
        }

        static Stage S24()
        {
            var s = MakeStage(24, "종합(진입로와 평행 주차)", "꺾인 진입로를 지나, 사람을 피해, 벽 옆 칸에 평행 주차하십시오");
            var f = MakeFloor("B1", new Rect(-28, -22, 70, 30), true);
            Corridor(f, new[] { P(-26, -18), P(-3, -18), P(-3, -3) }, 3.6f);
            Cap(f, P(-26.15f, -18), P(1, 0), 3.6f);
            Wall(f, P(-16, -3.15f), P(-4.95f, -3.15f));
            Wall(f, P(-1.05f, -3.15f), P(40, -3.15f));
            Wall(f, P(-16, -3.15f), P(-16, 6.2f));
            Wall(f, P(40, -3.15f), P(40, 6.2f));
            Wall(f, P(-16, 6.2f), P(40, 6.2f), 0.4f);
            f.lines.Add(new Seg(P(-16, 4.0f), P(40, 4.0f)));
            float x0 = 14f, x1 = x0 + PL;
            foreach (var x in new[] { x0 - PL, x0, x1, x1 + PL, x1 + 2 * PL }) f.lines.Add(new Seg(P(x, 4.0f), P(x, 6.0f)));
            Car(f, P(x0 - 3.0f, 5.0f), 0f, 3, 4.7f, 1.85f);
            Car(f, P(x1 + 3.0f, 5.0f), 0f, 4, 4.85f, 1.9f);
            Car(f, P(x1 + PL + 3.0f, 5.0f), 0f, 5, 4.6f, 1.82f);
            f.crosswalks.Add(Geom.RectPoly(P(4, 0.5f), 3.0f, 6.6f));
            f.arrows.Add(new FloorArrow { p = P(-14, -18), h = 0f });
            s.floors.Add(f);
            s.peds.Add(Ped(0, new[] { P(4, -2.7f), P(4, 3.6f) }, 1.0f, 1.0f));
            s.peds.Add(Ped(0, new[] { P(31, 3.6f), P(31, -2.6f) }, 0.9f, 5.0f));
            var poly = new[] { P(x0, 3.7f), P(x1, 3.7f), P(x1, 6.0f), P(x0, 6.0f) };
            s.startC = P(-21, -18); s.startH = 0f;
            var tg = SlotTarget(0, poly, 0f, 6f, true);
            tg.hasCenter = true; tg.center = P((x0 + x1) / 2f, 5.0f);
            s.target = tg;
            s.parMoves = 5;
            return s;
        }

        static Stage S25()
        {
            var s = S23();
            s.id = 25;
            s.title = "종합(경사로와 보행자)";
            s.goal = "꺾인 경사로를 올라, 2층 통로의 사람을 조심하며 기둥 옆 연두색 칸에 후진 주차하십시오";
            var f2 = s.floors[1];
            f2.crosswalks.Add(Geom.RectPoly(P(27.5f, -23), 3.0f, 6.0f));
            s.peds.Clear();
            s.peds.Add(Ped(1, new[] { P(27.5f, -19.6f), P(27.5f, -26.4f) }, 1.0f, 0.0f));
            s.peds.Add(Ped(1, new[] { P(35, -20.7f), P(1, -20.7f) }, 1.1f, 4.0f));
            s.target.tol = 5.0f;
            return s;
        }

        // ───────────────────────── 튜토리얼 ─────────────────────────

        static Stage TSteering()
        {
            var s = MakeStage(101, "스티어링 휠 감각", "스티어링 휠을 돌려 보며 바퀴와 지나갈 자리를 익힙니다");
            var f = MakeFloor("연습장", new Rect(-20, -16, 44, 32));
            Border(f, new Rect(-18, -14, 40, 28));
            Pillar(f, P(6, 5), 0.8f);
            s.floors.Add(f);
            s.startC = P(-6, 0); s.startH = 0f;
            s.target = new Target();
            s.steps.Add(new TutorialStep("스티어링 휠을 왼쪽 끝까지 돌려 보십시오. 앞바퀴가 꺾이고, 초록 띠(차가 지나갈 자리)가 왼쪽으로 휩니다.", "steer_left_full"));
            s.steps.Add(new TutorialStep("이번엔 오른쪽 끝까지. 안쪽 주황 띠가 '내륜차'입니다. 뒷바퀴는 앞바퀴보다 안쪽으로 돕니다.", "steer_right_full"));
            s.steps.Add(new TutorialStep("스티어링 휠을 가운데로 돌려 바퀴를 똑바로 하십시오.", "steer_center"));
            s.steps.Add(new TutorialStep("스티어링 휠을 오른쪽 끝까지 돌린 채, 브레이크를 밟고 화면 기어 D를 누른 뒤 브레이크를 떼 보십시오. 차가 한 바퀴 돕니다. 이 원이 이 차가 가장 작게 돌 수 있는 크기입니다. 몸으로 기억해 두십시오.", "full_circle"));
            s.steps.Add(new TutorialStep("꺾을 때는 뒷바퀴 쪽이 더 안쪽으로 들어오니 기둥과 넉넉히 떨어져 도십시오. 브레이크를 밟고 기어 P를 누르면 끝납니다.", "gear_p"));
            return s;
        }

        static Stage TPerp()
        {
            var s = PerpStage(102, "직각 주차(후진) 따라 하기", "주차 공식을 한 단계씩 따라 합니다", Occ(5, "out", 7, "out"), true, AISLE, false);
            s.startC = P(-11, 0.3f); s.startH = 0f;
            s.steps.Add(new TutorialStep("목표 칸이 오른쪽에 있습니다. 칸 쪽으로 붙어 천천히 가다가, 내 어깨(흰 점)가 초록 선에 오면 브레이크.", "shoulder_line"));
            s.steps.Add(new TutorialStep("스티어링 휠을 왼쪽 끝까지 돌리십시오.", "steer_left_full"));
            s.steps.Add(new TutorialStep("브레이크를 떼고 앞으로. 차가 초록 선과 나란해지면 브레이크. (실제 차에서는 오른쪽 미러에 칸 입구 모서리가 보일 때쯤)", "heading_30"));
            s.steps.Add(new TutorialStep("기어 R, 스티어링 휠을 오른쪽 끝까지 돌리십시오.", "gear_r_right_full"));
            s.steps.Add(new TutorialStep("브레이크를 떼고 천천히 후진. 차가 초록 선(칸 선)과 나란해지면 브레이크.", "heading_vertical"));
            s.steps.Add(new TutorialStep("스티어링 휠을 가운데로 돌리십시오.", "steer_center"));
            s.steps.Add(new TutorialStep("똑바로 후진. 센서 소리가 빨라지면 브레이크, 그리고 기어 P.", "parked"));
            return s;
        }

        static Stage TParallel()
        {
            var s = ParallelStage(103, "평행 주차 따라 하기", "평행 주차 공식을 한 단계씩 따라 합니다", PL, 0.65f, true);
            s.startC = P(-2, -0.4f); s.startH = 0f;
            s.steps.Add(new TutorialStep("앞차(목표 칸 앞의 차) 옆으로 가서, 너무 붙지도 멀지도 않게 나란히. 내 뒤 범퍼(흰 점)가 초록 선에 오면 브레이크.", "rear_bumper_line"));
            s.steps.Add(new TutorialStep("기어 R, 스티어링 휠을 오른쪽 끝까지 돌리십시오.", "gear_r_right_full"));
            s.steps.Add(new TutorialStep("천천히 후진. 차가 초록 선과 나란해지면 브레이크.", "heading_minus45"));
            s.steps.Add(new TutorialStep("스티어링 휠을 가운데로.", "steer_center"));
            s.steps.Add(new TutorialStep("똑바로 후진. 뒷바퀴 축(흰 점)이 초록 선에 오면 브레이크.", "straight_back"));
            s.steps.Add(new TutorialStep("스티어링 휠을 왼쪽 끝까지 돌리십시오.", "steer_left_full"));
            s.steps.Add(new TutorialStep("천천히 후진. 차가 길과 나란해지면 브레이크. 필요하면 D로 조금 앞으로 맞추고 기어 P.", "parked"));
            return s;
        }

        /// <summary>단계 전체가 들어오는 사각형(지도 보기용)</summary>
        public static Rect Bounds(Stage st)
        {
            var r = st.floors[0].ground;
            for (int i = 1; i < st.floors.Count; i++) r = r.Merge(st.floors[i].ground);
            foreach (var rp in st.ramps) foreach (var p in rp.poly) r = r.Expand(p);
            return r;
        }
    }
}
