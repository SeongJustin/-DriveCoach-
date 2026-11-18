// Road: 정지선, 차로, 길 정의
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    /// <summary>정지선: a→b 선분, 차가 approach 방향으로 지나가면 "넘음". signal 번호(-1 = 일시정지 표지)</summary>
    public class StopLine { public V2 a, b, approach; public int signal = -1; }

    /// <summary>
    /// 차로 하나(가운데선, 진행 방향은 점 순서). 반대 방향 차로는 따로 둔다.
    /// speedLimitKmh: 이 차로 제한 속도(0 = 도로 전체 값), next: 끝에서 곧장 이어지는 차로 번호(-1 = 없음, 꺾어야 함).
    /// </summary>
    public class Lane
    {
        public V2[] center; public float width = 3.25f; public string name = ""; public float speedLimitKmh; public int next = -1;
        /// <summary>자료로 확인된 제한속도(km/h, 0 = 모름·없음). speedLimitKmh 는 다른 차·자동 속도가 쓰는 '흐름 속도'(길 등급 기본값일 수 있다)</summary>
        public float postedKmh;
        /// <summary>
        /// 양방향 좁은 골목(폭 4.6 m 밑, 중앙선 없음): 두 방향이 한 가운데를 같이 쓴다. pair = 맞은편 차로 번호, room = 길 폭(m).
        /// 다른 차는 마주치면 서로 오른쪽으로 붙여 천천히 비켜 가고, 둘이 못 지나갈 만큼 좁으면 들어가기 전에 기다린다(Traffic)
        /// </summary>
        public bool shared;
        public int pair = -1;
        public float room;
        /// <summary>가운데 점마다 높이(m, 입체 교차 길). null = 땅</summary>
        public float[] z;
        public float ZAt(int seg, float t) => z == null ? 0f : z[seg] + (z[Math.Min(seg + 1, z.Length - 1)] - z[seg]) * t;
    }

    public class RoadDef
    {
        public List<Lane> lanes = new List<Lane>();
        public List<StopLine> stopLines = new List<StopLine>();
        public List<TrafficSignal> signals = new List<TrafficSignal>();
        /// <summary>도로 바닥(아스팔트) 사각형들. 나머지 바닥은 보도·잔디.</summary>
        public List<Rect> pavement = new List<Rect>();
        /// <summary>가운데 노란 선(중앙선)</summary>
        public List<Seg> centerLines = new List<Seg>();
        /// <summary>흰 점선(차로 경계)</summary>
        public List<Seg> laneLines = new List<Seg>();
        public float speedLimitKmh = 30f;
        /// <summary>내 동네(지역 지도): 노면 띠, 건물, 신호 교차로 가운데, 원본 지역 데이터(이름·출처·동 경계). 미리보기 판은 비어 있다.</summary>
        public List<RoadStrip> strips = new List<RoadStrip>();
        public List<RegionBuilding> buildings = new List<RegionBuilding>();
        public List<V2> signalPoles = new List<V2>();
        public RegionData region;
        /// <summary>
        /// 신호 시계(하루 중 분): 실제 운영계획이 있는 교차로는 그 시각 시간대의 주기·현시로 바꾼다(설정의 시간: 아침 08:00·낮 14:00·해질녘 18:30·밤 23:00).
        /// </summary>
        public int SignalClockMin { get; private set; } = -1;
        public void SetSignalClock(int minute)
        {
            if (minute == SignalClockMin) return;
            SignalClockMin = minute;
            var done = new HashSet<SignalPlan>();
            foreach (var s in signals)
            {
                if (s.plan == null) continue;
                if (done.Add(s.plan)) s.plan.Select(minute);
                s.plan.Apply(s);
            }
        }
        public float LimitOf(Lane ln) => ln != null && ln.speedLimitKmh > 0f ? ln.speedLimitKmh : speedLimitKmh;
        /// <summary>다른 차 수(0 = 없음). 연습 동네 4, 내 동네 30, beta 동네 0.</summary>
        public int trafficCount;
        /// <summary>횡단보도를 건너는 사람(CrossPeds)을 두지 않는다. 신호·횡단보도 칠·정류장은 그대로</summary>
        public bool noPeds;

        // ── 입체 교차: 고가·지하 길 조각과 높이. 차가 어느 층에 있는지는 앞 높이에서 이어지는 쪽으로 정한다 ──
        readonly List<(V2 a, V2 b, float za, float zb, float half)> gradeSegs = new List<(V2, V2, float, float, float)>();
        readonly Dictionary<long, List<int>> gradeGrid = new Dictionary<long, List<int>>();
        const float GradeCell = 20f;
        public bool HasGrade => gradeSegs.Count > 0;

        static long GKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        public void AddGradeSeg(V2 a, V2 b, float za, float zb, float half)
        {
            int id = gradeSegs.Count;
            gradeSegs.Add((a, b, za, zb, half));
            float x0 = Math.Min(a.x, b.x) - half, x1 = Math.Max(a.x, b.x) + half, y0 = Math.Min(a.y, b.y) - half, y1 = Math.Max(a.y, b.y) + half;
            for (int gx = (int)Math.Floor(x0 / GradeCell); gx <= (int)Math.Floor(x1 / GradeCell); gx++)
                for (int gy = (int)Math.Floor(y0 / GradeCell); gy <= (int)Math.Floor(y1 / GradeCell); gy++)
                {
                    var k = GKey(gx, gy);
                    if (!gradeGrid.TryGetValue(k, out var l)) gradeGrid[k] = l = new List<int>();
                    l.Add(id);
                }
        }

        /// <summary>
        /// 점 p 의 길 높이. 고가·지하 길 위(반폭 안)이고 그 높이가 지금 높이(prev)에서 1.5 m 안이면 그 높이, 아니면 0(땅).
        /// → 경사로로 올라가면 따라 올라가고, 다리 밑을 지나는 차는 땅에 남는다.
        /// </summary>
        public float HeightAt(V2 p, float prev)
        {
            if (gradeSegs.Count == 0) return 0f;
            if (!gradeGrid.TryGetValue(GKey((int)Math.Floor(p.x / GradeCell), (int)Math.Floor(p.y / GradeCell)), out var l)) return 0f;
            float best = float.NaN, bestD = float.MaxValue;
            foreach (int i in l)
            {
                var (a, b, za, zb, half) = gradeSegs[i];
                var ab = b - a;
                float L2 = ab.Dot(ab);
                float t = L2 > 1e-6f ? Geom.Clamp((p - a).Dot(ab) / L2, 0f, 1f) : 0f;
                if ((a + ab * t).DistanceTo(p) > half) continue;
                float z = za + (zb - za) * t;
                float dz = Math.Abs(z - prev);
                if (dz > 1.5f) continue;
                if (dz < bestD) { bestD = dz; best = z; }
            }
            if (!float.IsNaN(best)) return best;
            return Math.Abs(prev) < 1.5f ? 0f : 0f;
        }
    }
}
