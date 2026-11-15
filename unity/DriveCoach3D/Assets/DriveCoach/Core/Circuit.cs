// 담당: S (3D 핸들 감각 — 서킷 돌아보기. 11/8 대표 요청: "핸들감각 익히기에 서킷 돌아보기")
// 닫힌 트랙을 몇 바퀴 돈다. 실패는 방호벽·밖으로 나감뿐이고, 트랙 밖으로 바퀴가 나가면 횟수만 센다(별점).
// 코너 앞에서 너무 빠르면 "속도를 줄이세요" 같은 짧은 안내를 낸다(코칭 앱과 같은 말투).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public class CircuitDef
    {
        /// <summary>가운데선(닫힌 고리, 약 1 m 간격). center[0] 이 출발선.</summary>
        public V2[] center;
        /// <summary>각 점까지 누적 길이(m). total = 한 바퀴</summary>
        public float[] dist;
        public float total;
        public float width = 7f;
        /// <summary>방호벽까지 가장자리에서 더 떨어진 거리(m)</summary>
        public float runoff = 2.5f;
        public int laps = 2;
        /// <summary>순서대로 지나야 하는 점 번호(출발선 제외)</summary>
        public int[] checkpoints;
        /// <summary>각 점의 굽은 정도(1/반지름, 1/m). 코너 안내에 쓴다.</summary>
        public float[] curvature;

        public V2 Tangent(int i)
        {
            int n = center.Length;
            return (center[(i + 1) % n] - center[(i - 1 + n) % n]).Normalized;
        }

        /// <summary>점 p 에서 가장 가까운 가운데선 구간(i → i+1)과 거리. hint 근처부터 찾는다(-1 이면 전부).</summary>
        public int Nearest(V2 p, int hint, out float distance)
        {
            int n = center.Length;
            int best = 0;
            float bd = float.PositiveInfinity;
            int from = 0, count = n;
            if (hint >= 0) { from = hint - 25; count = 51; }
            for (int k = 0; k < count; k++)
            {
                int i = ((from + k) % n + n) % n;
                var q = Geom.ClosestPointOnSegment(p, center[i], center[(i + 1) % n]);
                float d = q.DistanceTo(p);
                if (d < bd) { bd = d; best = i; }
            }
            distance = bd;
            return best;
        }
    }

    /// <summary>서킷 한 판의 진행(World 가 매 프레임 부른다).</summary>
    public class CircuitRun
    {
        public readonly CircuitDef def;
        public int nearest;
        public int lap;              // 끝낸 바퀴 수
        public int nextCheckpoint;   // 다음에 지나야 할 checkpoints 번호
        public float lapStart;
        /// <summary>(11/14 베타: 출발 전부터 시간이 흐른다) 차가 처음 움직인 때부터 잰다</summary>
        public bool started;
        public float LapTime(float now) => started ? now - lapStart : 0f;
        public List<float> lapTimes = new List<float>();
        public int offTrackCount;
        public bool offTrack;
        public bool wrongWay;
        float cornerCooldown, wrongCooldown;
        public float maxSpeed;

        public CircuitRun(CircuitDef d, V2 carCenter)
        {
            def = d;
            nearest = d.Nearest(carCenter, -1, out _);
        }

        public bool Finished => lap >= def.laps;

        public void Update(World w, float dt)
        {
            var car = w.car;
            if (!started && Math.Abs(car.speed) > 0.3f) { started = true; lapStart = w.t; }
            int prev = nearest;
            nearest = def.Nearest(car.Center, nearest, out _);
            maxSpeed = Math.Max(maxSpeed, Math.Abs(car.speed));
            int n = def.center.Length;

            // 바퀴가 트랙 밖(가장자리 선 바깥)인가: 네 바퀴 중 하나라도
            bool off = false;
            foreach (var tp in car.Tires())
            {
                var c = Geom.PolyCenter(tp);
                def.Nearest(c, nearest, out float d);
                if (d > def.width / 2f) { off = true; break; }
            }
            if (off && !offTrack) { offTrackCount++; w.Say("바퀴가 트랙 밖으로 나갔습니다. 시선을 멀리 두십시오", 2.2f); }
            offTrack = off;

            // 진행 방향: 트랙 방향과 반대로 가면 알린다
            var tan = def.Tangent(nearest);
            bool backwards = car.speed > 0.5f && car.Dir.Dot(tan) < -0.3f;
            wrongCooldown -= dt;
            if (backwards && !wrongWay && wrongCooldown <= 0f) { w.Say("반대 방향입니다. 돌아서 화살표 쪽으로 가십시오", 2.6f); wrongCooldown = 5f; }
            wrongWay = backwards;

            // 코너 앞 안내: 앞 12~25 m 안에 반지름 15 m 보다 좁은 코너가 있고 시속 20 km 넘으면(자동 속도면 속도 대신 핸들·시선 안내)
            cornerCooldown -= dt;
            if (cornerCooldown <= 0f && (w.autoSpeed ? car.speed > 2f : car.speed > 20f / 3.6f))
            {
                for (int k = 12; k <= 25; k++)
                {
                    if (def.curvature[(nearest + k) % n] > 1f / 15f)
                    {
                        w.Say(w.autoSpeed ? "코너입니다. 시선은 코너 끝에, 스티어링 휠은 미리 천천히" : "코너가 다가옵니다. 들어가기 전에 속도를 줄이십시오", 2.4f);
                        cornerCooldown = 6f;
                        break;
                    }
                }
            }

            // 체크포인트·바퀴 수: 앞으로 조금씩 지나간 경우만 센다(뒤로 갔다 오면 안 셈)
            int step = ((nearest - prev) % n + n) % n;
            if (step > 0 && step < n / 2)
            {
                for (int s = 1; s <= step; s++)
                {
                    int idx = (prev + s) % n;
                    if (nextCheckpoint < def.checkpoints.Length && idx == def.checkpoints[nextCheckpoint]) nextCheckpoint++;
                    if (idx == 0 && nextCheckpoint >= def.checkpoints.Length)
                    {
                        lapTimes.Add(w.t - lapStart);
                        lapStart = w.t;
                        lap++;
                        nextCheckpoint = 0;
                        if (!Finished) w.Say($"{lap}바퀴 완료! 한 바퀴 더", 2.4f);
                    }
                }
            }
        }
    }

    public static partial class Stages
    {
        public const int CIRCUIT_COUNT = 2;
        public static readonly string[] CircuitNames = { "서킷(기본)", "서킷(S자와 헤어핀)" };

        /// <summary>서킷(핸들 감각). 1 = 넓고 완만, 2 = 좁고 굽음.</summary>
        public static Stage Circuit(int i)
        {
            V2[] ctrl;
            float width;
            string title, goal;
            if (i == 2)
            {
                // 가장 급한 곳(헤어핀) 가운데선 반지름 약 6.6 m — 핸들을 거의 끝까지 돌려야 한다(최소회전반경 5.3~5.9 m 차)
                ctrl = new[] { P(0, 0), P(40, 0), P(64, 10), P(70, 32), P(54, 44), P(38, 38), P(24, 46), P(16, 62), P(-4, 68), P(-24, 60), P(-32, 38), P(-22, 12) };
                width = 6f;
                title = CircuitNames[1];
                goal = "좁은 S자와 급한 헤어핀이 있습니다. 코너 앞에서 미리 속도를 줄이고 두 바퀴 도십시오";
            }
            else
            {
                ctrl = new[] { P(0, 0), P(45, 0), P(68, 8), P(74, 28), P(60, 42), P(30, 46), P(0, 46), P(-24, 40), P(-30, 22), P(-20, 6) };
                width = 7.5f;
                title = CircuitNames[0];
                goal = "브레이크를 떼면 출발합니다. 속도는 자동(설정에서 바꿈), 스티어링 휠만 돌려 트랙을 두 바퀴 도십시오. 바퀴가 흰 선 밖으로 나가지 않게";
            }
            var center = CatmullRomClosed(ctrl, 1.0f);
            var def = MakeCircuit(center, width);
            var s = MakeStage(200 + i, title, goal);
            s.mode = StageMode.Circuit;
            s.circuit = def;
            // 바닥(잔디) — 트랙 전체 + 여유
            var r = new Rect(center[0].x, center[0].y, 0, 0);
            foreach (var p in center) r = r.Expand(p);
            var f = MakeFloor("서킷", r.Grow(width / 2f + def.runoff + 12f));
            f.surface = Surface.Grass;
            // 방호벽(타이어 벽): 가장자리 + runoff 바깥, 양쪽
            int n = center.Length;
            foreach (var side in new[] { -1f, 1f })
            {
                float off = width / 2f + def.runoff;
                for (int k = 0; k < n; k += 3)
                {
                    int k2 = (k + 3) % n;
                    var a = center[k] + Normal(def, k) * side * off;
                    var b = center[k2] + Normal(def, k2) * side * off;
                    if (a.DistanceTo(b) > 0.05f) Wall(f, a, b, 0.6f);
                }
            }
            s.floors.Add(f);
            s.startC = center[3];
            s.startH = def.Tangent(3).Angle * Geom.Rad2Deg;
            s.target = new Target();
            s.parMoves = 1;
            return s;
        }

        /// <summary>트랙 왼쪽(진행 방향 기준) 법선</summary>
        public static V2 Normal(CircuitDef d, int i) { var t = d.Tangent(i); return new V2(t.y, -t.x); }

        static CircuitDef MakeCircuit(V2[] center, float width)
        {
            int n = center.Length;
            var d = new CircuitDef { center = center, width = width, dist = new float[n], curvature = new float[n] };
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                d.dist[i] = acc;
                acc += center[i].DistanceTo(center[(i + 1) % n]);
            }
            d.total = acc;
            for (int i = 0; i < n; i++)
            {
                // 세 점 원의 곡률 = 2 sin(각) / 현
                var a = center[(i - 2 + n) % n]; var b = center[i]; var c = center[(i + 2) % n];
                float ab = a.DistanceTo(b), bc = b.DistanceTo(c), ca = c.DistanceTo(a);
                float cross = Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                d.curvature[i] = ab * bc * ca > 1e-6f ? 2f * cross / (ab * bc * ca) : 0f;
            }
            int cps = 4;
            d.checkpoints = new int[cps];
            for (int k = 0; k < cps; k++) d.checkpoints[k] = (int)((k + 1) * n / (float)(cps + 1));
            return d;
        }

        /// <summary>닫힌 Catmull-Rom 곡선을 step(m) 간격으로 나눈 점들</summary>
        public static V2[] CatmullRomClosed(V2[] ctrl, float step)
        {
            var dense = new List<V2>();
            int m = ctrl.Length;
            for (int i = 0; i < m; i++)
            {
                V2 p0 = ctrl[(i - 1 + m) % m], p1 = ctrl[i], p2 = ctrl[(i + 1) % m], p3 = ctrl[(i + 2) % m];
                for (int k = 0; k < 40; k++)
                {
                    float t = k / 40f, t2 = t * t, t3 = t2 * t;
                    dense.Add(0.5f * ((2f * p1) + (-1f * p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-1f * p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            // 같은 간격으로 다시 뽑기
            var outList = new List<V2> { dense[0] };
            float carry = 0f;
            for (int i = 0; i < dense.Count; i++)
            {
                var a = dense[i]; var b = dense[(i + 1) % dense.Count];
                float seg = a.DistanceTo(b);
                float pos = step - carry;
                while (pos <= seg)
                {
                    outList.Add(a.Lerp(b, pos / seg));
                    pos += step;
                }
                carry = seg - (pos - step);
            }
            if (outList.Count > 1 && outList[outList.Count - 1].DistanceTo(outList[0]) < step * 0.5f) outList.RemoveAt(outList.Count - 1);
            return outList.ToArray();
        }
    }
}
