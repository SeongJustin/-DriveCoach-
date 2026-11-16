// 3D 도로 주행 — 다른 차들
// 차로(Lane)를 따라 달리는 차들: 차로 제한 속도 근처로, 앞차·내 차와 거리를 두고(앞에 있으면 선다),
// 빨간·노란불이면 정지선 앞에 서고, 차로가 끝나면 이어지는 차로나 꺾을 차로로 간다. 내 차에서 너무 멀어지면 다른 곳에 다시 나타난다.
// 내 차가 부딪히면 실패(World). 다른 차끼리는 부딪힘을 따지지 않는다(교차로는 신호로 나뉜다).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{

    public class Traffic
    {
        public readonly List<TrafficCar> cars = new List<TrafficCar>();
        readonly RoadDef road;
        public RoadDef Road => road;
        readonly float[][] cum;           // 차로마다 누적 길이
        readonly Dictionary<long, List<(int lane, int seg)>> grid = new Dictionary<long, List<(int, int)>>();
        /// <summary>시험용 씨앗(게임은 17 그대로)</summary>
        public static int Seed = 17;
        readonly Random rnd = new Random(Seed);
        int nextId;
        readonly int want;
        const float Cell = 12f;
        // 차로 고르기 무게(교통량): 차로 길이 × 길 종류(제한 50 km/h 넘는 큰길 4, 그 밑 1) — 큰길에 차가 많다
        float[] laneW;
        float laneWSum;

        public Traffic(RoadDef r, int count, V2 player, float playerHeading)
        {
            road = r;
            want = count;
            cum = new float[r.lanes.Count][];
            for (int i = 0; i < r.lanes.Count; i++)
            {
                var c = r.lanes[i].center;
                var a = new float[c.Length];
                for (int k = 1; k < c.Length; k++) a[k] = a[k - 1] + c[k].DistanceTo(c[k - 1]);
                cum[i] = a;
                for (int k = 0; k + 1 < c.Length; k++)
                {
                    var key = Key(c[k]);
                    if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<(int, int)>();
                    l.Add((i, k));
                    var key2 = Key(c[k + 1]);
                    if (key2 != key)
                    {
                        if (!grid.TryGetValue(key2, out var l2)) grid[key2] = l2 = new List<(int, int)>();
                        l2.Add((i, k));
                    }
                }
            }
            laneW = new float[r.lanes.Count];
            for (int i = 0; i < r.lanes.Count; i++)
            {
                var ln = r.lanes[i];
                float L = LaneLength(i);
                float lim = r.LimitOf(ln);
                float w = (L < 40f || lim < 25f) ? 0f : L * (lim >= 50f ? 4f : 1f) * (ln.shared ? 0.5f : 1f);
                laneWSum += w;
                laneW[i] = laneWSum;
            }
            laneStops = BuildLaneStops();
            stopBack = BuildStopBack();
            for (int i = 0; i < count; i++) Spawn(player, 30f, 300f);
        }

        static long Key(V2 p) => ((long)(int)Math.Floor(p.x / Cell) << 32) ^ (uint)(int)Math.Floor(p.y / Cell);

        public float LaneLength(int i) => cum[i][cum[i].Length - 1];

        /// <summary>차로 i 위 거리 s 의 점과 방향</summary>
        public void Sample(int i, float s, out V2 p, out V2 dir) => Sample(i, s, out p, out dir, out _);

        public void Sample(int i, float s, out V2 p, out V2 dir, out float z)
        {
            var c = road.lanes[i].center;
            var a = cum[i];
            s = Geom.Clamp(s, 0f, a[a.Length - 1]);
            int lo = 0, hi = a.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (a[m] <= s) lo = m; else hi = m; }
            float seg = a[hi] - a[lo];
            float t = seg > 1e-4f ? (s - a[lo]) / seg : 0f;
            p = c[lo] + (c[hi] - c[lo]) * t;
            z = road.lanes[i].z == null ? 0f : road.lanes[i].z[lo] + (road.lanes[i].z[hi] - road.lanes[i].z[lo]) * t;
            dir = (c[hi] - c[lo]).Normalized;
            if (dir.Length < 0.5f) dir = new V2(1f, 0f);
        }

        /// <summary>플레이어 근처(min..max m) 아무 차로(제한 30 km/h 이상, 60 m 넘는 것)에 한 대</summary>
        bool Spawn(V2 player, float min, float max)
        {
            for (int tries = 0; tries < 60; tries++)
            {
                int li;
                if (laneWSum > 0f)
                {
                    float x = (float)rnd.NextDouble() * laneWSum;
                    int lo = 0, hi = laneW.Length - 1;
                    while (lo < hi) { int m = (lo + hi) / 2; if (laneW[m] < x) lo = m + 1; else hi = m; }
                    li = lo;
                }
                else li = rnd.Next(road.lanes.Count);
                var ln = road.lanes[li];
                float L = LaneLength(li);
                if (L < 40f || road.LimitOf(ln) < 25f) continue;
                float s = 5f + (float)rnd.NextDouble() * (L - 10f);
                Sample(li, s, out var p, out var d);
                float dist = p.DistanceTo(player);
                if (dist < min || dist > max) continue;
                bool clash = false;
                foreach (var o in cars) if (o.pos.DistanceTo(p) < 12f || (ln.shared && o.lane == ln.pair)) { clash = true; break; }
                if (clash) continue;
                double r0 = rnd.NextDouble();
                int kind = PickKind(road.LimitOf(ln), ln, li);
                var car = new TrafficCar { id = ++nextId, lane = li, s = s, model = rnd.Next(8), kind = kind, speed = road.LimitOf(ln) / 3.6f * 0.6f, style = r0 < 0.2 ? 1 : (r0 < 0.35 ? 2 : 0) };
                car.pos = p;
                car.z = ln.z == null ? 0f : ln.ZAt(0, 0f);
                car.heading = (float)Math.Atan2(d.y, d.x);
                car.length = TrafficCar.Kinds[kind].L;
                car.width = TrafficCar.Kinds[kind].W;
                car.paint = PaintSeed(li, s, car.model);
                if (car.IsBus) car.style = 1;
                cars.Add(car);
                return true;
            }
            return false;
        }

        static int PaintSeed(int li, float s, int model)
        {
            uint h = (uint)li * 2654435761u ^ (uint)(int)(s * 131f) * 2246822519u ^ (uint)model * 3266489917u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (int)(h % 10000u);
        }

        /// <summary>
        /// 차 종류 고르기: 시내버스는 제한 50 km/h 넘는 큰길·폭 3 m 넘는 차로에만, 마을버스는 동네 큰길(30~50)에만, 1톤 화물·승합은 어디나.
        /// 버스는 노선(정류장이 붙은 차로)에 많이(4 배), 정류장 없는 차로에는 적게(0.6 배)
        /// </summary>
        int PickKind(float limitKmh, Lane ln, int li = -1)
        {
            bool onRoute = HasStops(li);
            var K = TrafficCar.Kinds;
            double sum = 0;
            var w = new double[K.Length];
            for (int i = 0; i < K.Length; i++)
            {
                double x = K[i].weight;
                if (i == 6 && (limitKmh < 50f || ln.width < 3.0f)) x = 0;
                if (i == 7 && (limitKmh < 30f || limitKmh > 50f || ln.width < 2.8f)) x = 0;
                if (i >= 6 && laneStops != null) x *= onRoute ? 4.0 : 0.6;
                w[i] = x; sum += x;
            }
            double r = rnd.NextDouble() * sum;
            for (int i = 0; i < K.Length; i++) { r -= w[i]; if (r <= 0) return i; }
            return 0;
        }

        /// <summary>
        /// 버스정류장(지역 "bs", 길가만): 차로마다 정류장 자리(차로 위 거리). 정류장 표지가 그 차로 오른쪽 1.5~6.5 m 에 있고 방향이 같으면.
        /// 같은 방향 차로가 여럿이면 가장 오른쪽(표지에 가장 가까운) 차로만.
        /// </summary>
        List<float>[] BuildLaneStops()
        {
            var res = new List<float>[road.lanes.Count];
            var rg = road.region;
            if (rg == null || rg.busStops.Count == 0) return res;
            foreach (var st in rg.busStops)
            {
                if (st.kind != 0) continue;
                var hd = new V2((float)Math.Cos(st.heading), (float)Math.Sin(st.heading));
                int best = -1; float bestLat = float.PositiveInfinity, bestS = 0f;
                for (int gx = -1; gx <= 1; gx++)
                    for (int gy = -1; gy <= 1; gy++)
                    {
                        long k = ((long)((int)Math.Floor(st.sign.x / Cell) + gx) << 32) ^ (uint)((int)Math.Floor(st.sign.y / Cell) + gy);
                        if (!grid.TryGetValue(k, out var l)) continue;
                        foreach (var (li, seg) in l)
                        {
                            var c = road.lanes[li].center;
                            var dir = (c[seg + 1] - c[seg]).Normalized;
                            if (dir.Dot(hd) < 0.8f) continue;
                            var q = Geom.ClosestPointOnSegment(st.sign, c[seg], c[seg + 1]);
                            var rel = st.sign - q;
                            float lat = rel.Dot(new V2(-dir.y, dir.x));     // 오른쪽 + (2D: y 아래, 시계 방향)
                            if (lat < 1.5f || lat > 6.5f) continue;
                            if (Math.Abs(rel.Dot(dir)) > 1.0f) continue;
                            if (lat < bestLat) { bestLat = lat; best = li; bestS = cum[li][seg] + q.DistanceTo(c[seg]); }
                        }
                    }
                if (best < 0) continue;
                if (res[best] == null) res[best] = new List<float>();
                res[best].Add(bestS);
            }
            foreach (var l in res) l?.Sort();
            return res;
        }

        List<float>[] laneStops;
        float[] stopBack;
        /// <summary>골목(같이 쓰는 길)마다 옆길 입구 자리(길 따라 거리, 쪽 ±1) — 처음 물을 때 한 번 찾는다</summary>
        readonly Dictionary<int, List<(float s, float side)>> mouths = new Dictionary<int, List<(float s, float side)>>();

        /// <summary>시험용: 좁은 골목에서 옆길 입구로 비켜 선 횟수</summary>
        public int AlleyAside;

        public List<(float s, float side)> Mouths(int li)
        {
            if (mouths.TryGetValue(li, out var m)) return m;
            m = new List<(float s, float side)>();
            var ln = road.lanes[li];
            var c = ln.center;
            float reach = ln.room / 2f + 4f;
            for (int k = 0; k < road.lanes.Count; k++)
            {
                if (k == li || k == ln.pair) continue;
                var oc = road.lanes[k].center;
                if (oc.Length < 2) continue;
                for (int e = 0; e < 2; e++)
                {
                    var q = e == 0 ? oc[0] : oc[oc.Length - 1];
                    var qd = e == 0 ? (oc[1] - oc[0]).Normalized : (oc[oc.Length - 1] - oc[oc.Length - 2]).Normalized;
                    for (int seg = 0; seg + 1 < c.Length; seg++)
                    {
                        var p = Geom.ClosestPointOnSegment(q, c[seg], c[seg + 1]);
                        if (p.DistanceTo(q) > reach) continue;
                        var d = (c[seg + 1] - c[seg]).Normalized;
                        if (Math.Abs(qd.Dot(d)) > 0.7f) continue;          // 나란한 길(같은 골목 이어짐)은 입구가 아니다
                        // 옆길 쪽: 나가는 차로(e=0)는 그 방향, 들어오는 차로(e=1)는 반대쪽에 옆길이 있다
                        float sd = qd.Dot(new V2(-d.y, d.x)) * (e == 0 ? 1f : -1f);
                        float s = cum[li][seg] + p.DistanceTo(c[seg]);
                        if (s > LaneLength(li) - 8f) continue;               // 골목 끝 교차로는 빼고(물러나는 쪽은 처음 쪽)
                        if (s < 8f)
                        {
                            // 골목 처음 교차로: 옆길도 골목(같이 쓰는 길)일 때만 — 큰길로 비켜 서면 큰길 차와 겹친다
                            if (!road.lanes[k].shared) continue;
                            s = Math.Max(s, 6.5f);    // 교차로 한가운데가 아니라 골목 쪽(맞은편 차가 교차로를 지나며 스치지 않게)
                        }
                        m.Add((s, Math.Sign(sd)));
                        break;
                    }
                }
            }
            m.Sort((a, b) => a.s.CompareTo(b.s));
            mouths[li] = m;
            return m;
        }

        /// <summary>시험용: 앞지르기로 차로를 옮긴 횟수</summary>
        public int LaneChanges;

        /// <summary>앞지르기: 옆 차로를 찾아 옮긴다(왼쪽 먼저). 옮기면 true — 차 자리는 그대로 두고 옆 거리(lat)로 천천히 미끄러진다</summary>
        bool TrySideLane(TrafficCar c, V2 dir, V2 mePos)
        {
            var right = new V2(-dir.y, dir.x);
            int best = -1; float bestS = 0f, bestSide = 0f;
            for (int gx = -1; gx <= 1; gx++)
                for (int gy = -1; gy <= 1; gy++)
                {
                    long k = ((long)((int)Math.Floor(c.pos.x / Cell) + gx) << 32) ^ (uint)((int)Math.Floor(c.pos.y / Cell) + gy);
                    if (!grid.TryGetValue(k, out var l)) continue;
                    foreach (var (oi, seg) in l)
                    {
                        if (oi == c.lane || road.lanes[oi].shared) continue;
                        var oc = road.lanes[oi].center;
                        var q = Geom.ClosestPointOnSegment(c.pos, oc[seg], oc[seg + 1]);
                        float side = (q - c.pos).Dot(right);
                        // 옆 차로 위 바로 옆 점만(선분 끝으로 밀린 점이면 앞뒤로 어긋나 옮길 때 차가 1~3 m 튀었다)
                        if (Math.Abs((q - c.pos).Dot(dir)) > 0.6f) continue;
                        if (Math.Abs(side) < 2.2f || Math.Abs(side) > 4.6f || q.DistanceTo(c.pos) > 4.8f) continue;
                        if ((oc[seg + 1] - oc[seg]).Normalized.Dot(dir) < 0.95f) continue;
                        float s2 = cum[oi][seg] + q.DistanceTo(oc[seg]);
                        if (LaneLength(oi) - s2 < 60f || Math.Abs(road.lanes[oi].z == null ? 0f : road.lanes[oi].ZAt(seg, 0f) - c.z) > 1f) continue;
                        bool free = true;
                        foreach (var o in cars)
                        {
                            if (o == c) continue;
                            if ((o.lane == oi && o.s > s2 - 15f && o.s < s2 + 20f) || o.pos.DistanceTo(q) < 7f) { free = false; break; }
                        }
                        // 내 차(플레이어) 둘레 12 m 도 비어야
                        if (mePos.DistanceTo(q) < 12f) free = false;
                        if (!free) continue;
                        // 왼쪽(−) 먼저(도로교통법 제21조: 앞지르기는 왼쪽으로)
                        if (best < 0 || (side < 0f && bestSide > 0f)) { best = oi; bestS = s2; bestSide = side; }
                    }
                }
            if (best < 0) return false;
            LaneChanges++;
            c.lane = best;
            c.s = bestS;
            c.lat = -bestSide;      // 지금 자리 그대로(새 차로에서 옆으로 −side) → 0 으로 미끄러짐
            c.planNext = -2;
            c.waitJ = 0f;
            if (!c.IsBus) { c.blink = bestSide < 0f ? -1 : 1; c.leaveT = 4f; }
            return true;
        }

        /// <summary>
        /// 차로마다 교차로에서 양보할 때 설 자리(차로 끝에서 앞 범퍼까지 뒤로 m): 끝 14 m 안을 엇갈려(30~150°) 지나는 다른 차로 가운데선에서
        /// 앞 범퍼가 1.8 m(큰 차 반폭 + 여유) 떨어지게. 차로 끝이 교차로 한가운데(길 선이 만나는 점)라 끝에 서면 엇갈리는 차와 겹쳤다
        /// </summary>
        float[] BuildStopBack()
        {
            var res = new float[road.lanes.Count];
            for (int li = 0; li < road.lanes.Count; li++)
            {
                var c = road.lanes[li].center;
                res[li] = 2f;
                if (c.Length < 2) continue;
                var end = c[c.Length - 1];
                var dir = (end - c[c.Length - 2]).Normalized;
                var nrm = new V2(-dir.y, dir.x);
                var seen = new HashSet<long>();
                // 끝에서 가장 가까운 점만 보면 비스듬히(70~80°) 지나는 차로가 내 길을 실제로 가로지르는 자리보다 앞으로 잡혀
                //   서 있는 차 코가 큰길 차로에 걸렸다 → 내 가운데선(끝에서 뒤 14 m ~ 앞 4 m)과 그 차로 가운데선이 만나는 자리로,
                //   그 차로 차 반폭 + 0.6 m 를 비스듬한 만큼(1/sin) 늘리고 내 차 반폭이 비스듬히 먼저 닿는 만큼(cot) 더 뒤에 선다
                foreach (var probe in new[] { end, end - dir * 10f })
                    for (int gx = -1; gx <= 1; gx++)
                        for (int gy = -1; gy <= 1; gy++)
                        {
                            long k = ((long)((int)Math.Floor(probe.x / Cell) + gx) << 32) ^ (uint)((int)Math.Floor(probe.y / Cell) + gy);
                            if (!seen.Add(k) || !grid.TryGetValue(k, out var l)) continue;
                            foreach (var (oi, seg) in l)
                            {
                                if (oi == li) continue;
                                var oc = road.lanes[oi].center;
                                var a = oc[seg]; var b = oc[seg + 1];
                                var od = (b - a).Normalized;
                                float cos = Math.Abs(od.Dot(dir));
                                if (cos > 0.87f) continue;     // 30° 안(나란한 것)
                                float sa = (a - end).Dot(nrm), sb = (b - end).Dot(nrm);
                                if (sa * sb > 0f || Math.Abs(sa - sb) < 1e-4f) continue;   // 내 가운데선을 건너지 않는 토막
                                var x = a + (b - a) * (sa / (sa - sb));
                                float along = (x - end).Dot(dir);
                                if (along < -14f || along > 4f) continue;
                                float sin = (float)Math.Sqrt(Math.Max(0.05f, 1f - cos * cos));
                                float need = (0.95f + 0.6f) / sin + 0.95f * cos / sin;
                                res[li] = Math.Max(res[li], Math.Min(14f, need - along));
                            }
                        }
            }
            return res;
        }
        /// <summary>시험용: 정류장이 붙은 차로 수</summary>
        /// <summary>시험용: 차로의 정류장 자리(길 따라 m)</summary>
        public List<float> StopsOn(int lane) => HasStops(lane) ? laneStops[lane] : null;
        public bool HasStops(int lane) => laneStops != null && lane >= 0 && lane < laneStops.Length && laneStops[lane] != null;
        public int LanesWithStops { get { int n = 0; if (laneStops != null) foreach (var l in laneStops) if (l != null) n++; return n; } }

        /// <summary>차로 끝에서 갈 곳: 곧장 이어지는 차로, 없으면 끝 근처(8 m)를 지나는 다른 차로 중 하나(유턴 빼고)</summary>
        int NextLane(int li, out float startS)
        {
            startS = 0f;
            var ln = road.lanes[li];
            if (ln.next >= 0 && rnd.NextDouble() < 0.75) return ln.next;   // 대개 곧장, 가끔 꺾는다
            var c = ln.center;
            var end = c[c.Length - 1];
            var dir = (end - c[c.Length - 2]).Normalized;
            var opts = new List<(int lane, float s)>();
            for (int gx = -1; gx <= 1; gx++)
                for (int gy = -1; gy <= 1; gy++)
                {
                    long k = ((long)((int)Math.Floor(end.x / Cell) + gx) << 32) ^ (uint)((int)Math.Floor(end.y / Cell) + gy);
                    if (!grid.TryGetValue(k, out var l)) continue;
                    foreach (var (oi, seg) in l)
                    {
                        if (oi == li) continue;
                        var oc = road.lanes[oi].center;
                        var q = Geom.ClosestPointOnSegment(end, oc[seg], oc[seg + 1]);
                        if (q.DistanceTo(end) > 8f) continue;
                        var od = (oc[seg + 1] - oc[seg]).Normalized;
                        if (od.Dot(dir) < -0.3f) continue;
                        float s = cum[oi][seg] + q.DistanceTo(oc[seg]);
                        if (LaneLength(oi) - s < 15f) continue;
                        // 도로교통법 제25조: 우회전은 도로 오른쪽 가장자리에서, 좌회전은 중앙선 쪽에서 — 오른쪽(왼쪽)에 같은 방향 차로가
                        //   나란히 끝나면 그 쪽으로는 꺾지 않는다(전에는 2차로에서도 우회전해 교차로 안에서 옆 차로 직진 차와 서로 막혀 섰다 — 역삼역 시연 경로)
                        Sample(oi, Math.Min(s + 6f, LaneLength(oi)), out _, out var dn);
                        if (dir.Dot(dn) < 0.8f)
                        {
                            int side = dir.x * dn.y - dir.y * dn.x < 0f ? -1 : 1;
                            if (HasSideLane(li, side)) continue;
                        }
                        opts.Add((oi, s));
                    }
                }
            if (opts.Count == 0) return ln.next;
            var pick = opts[rnd.Next(opts.Count)];
            startS = pick.s;
            return pick.lane;
        }

        /// <summary>차로 끝 옆(side: +1 오른쪽, −1 왼쪽, 1.5~4.6 m)에 같은 방향 차로가 나란히 끝나는지 — 꺾는 차로 고르기(제25조)</summary>
        bool HasSideLane(int li, int side)
        {
            var c = road.lanes[li].center;
            var end = c[c.Length - 1];
            var dir = (end - c[c.Length - 2]).Normalized;
            var rt = new V2(-dir.y, dir.x);      // y 아래 좌표: 진행 방향 오른쪽
            for (int gx = -1; gx <= 1; gx++)
                for (int gy = -1; gy <= 1; gy++)
                {
                    long k = ((long)((int)Math.Floor(end.x / Cell) + gx) << 32) ^ (uint)((int)Math.Floor(end.y / Cell) + gy);
                    if (!grid.TryGetValue(k, out var l)) continue;
                    foreach (var (oi, seg) in l)
                    {
                        if (oi == li) continue;
                        var oc = road.lanes[oi].center;
                        if (seg != oc.Length - 2) continue;
                        var oe = oc[oc.Length - 1];
                        if ((oe - oc[oc.Length - 2]).Normalized.Dot(dir) < 0.95f) continue;
                        var rel = oe - end;
                        float lat = rel.Dot(rt) * side;
                        if (lat > 1.5f && lat < 4.6f && Math.Abs(rel.Dot(dir)) < 8f) return true;
                    }
                }
            return false;
        }

        /// <summary>한 프레임: 속도 정하기(제한·앞차·내 차·신호) → 움직이기 → 멀어진 차 다시 놓기</summary>
        public void Update(World w, float dt)
        {
            var me = w.car;
            var myPoly = me.Corners();
            foreach (var o in cars) o.guardPoly = o.Poly;
            xDec.Clear();
            for (int ci = cars.Count - 1; ci >= 0; ci--)
            {
                var c = cars[ci];
                if (c.id == 0) c.id = ++nextId;      // 밖에서 넣은 차(시험·사진)에도 번호
                var ln = road.lanes[c.lane];
                float limit = road.LimitOf(ln) / 3.6f;
                float target = limit * c.SpeedK;
                // 나를 세우는 다른 차(wBy)와 까닭, 차가 아닌 것(신호·보행자·내 차·버스·골목)으로 서는지(hold)
                TrafficCar wBy = null; string wWhy = null, hold = null; float wV = 0.5f;
                if (c.passT > 0f) { c.passT -= dt; if (c.passT <= 0f) { c.passBy = null; if (c.speed < 0.3f) c.passCool = 20f; } }
                if (c.passCool > 0f) c.passCool -= dt;
                Sample(c.lane, c.s, out var p, out var dir);
                var front = p + dir * (c.length / 2f);
                // 옆으로 미끄러지는 중(앞지르기·골목 비킴)에는 실제 자리(옆 거리 lat 만큼)에서 앞을 본다 — 차로 가운데선에서만 보면
                //   옛 차로에 선 버스를 3 m 옆으로 보고 그대로 밀고 들어가 겹쳤다(양재2동 논현로 시험)
                var frontAct = Math.Abs(c.lat) > 0.05f ? front + new V2(-dir.y, dir.x) * c.lat : front;
                // 앞에 있는 것(다른 차·내 차): 차 앞 40 m, 옆으로 1.8 m 안
                float gap = float.PositiveInfinity;
                TrafficCar lead = null;
                float restL = LaneLength(c.lane) - c.s;
                foreach (var o in cars)
                {
                    if (o == c) continue;
                    // 같은 차로(또는 다음에 들어갈 차로)의 앞차는 차로를 따라 잰 거리로(급하게 굽은 회차로에서
                    //   앞차가 옆 1.8 m 밖으로 보여 못 보고 서 있는 차를 뚫고 지나감)
                    if (o.lane == c.lane)
                    {
                        if (o.s <= c.s) continue;
                        float g2 = o.s - c.s - (o.length + c.length) / 2f;
                        if (o.s - c.s < 45f && g2 < gap) { gap = g2; lead = o; }
                        continue;
                    }
                    if (c.planNext >= 0 && o.lane == c.planNext && o.s >= c.planS && restL < 40f)
                    {
                        // 세게 풀 때: 들어갈 자리 바로 옆(한 차 길이 안)에 나란히 선 그 차는 앞차로 보지 않는다(서로 코가 걸려 둘 다 섰다 — 양재2동 논현로20길)
                        if (c.passHard && o == c.passBy && o.s - c.planS < o.length + 1f) continue;
                        float g2 = restL + (o.s - c.planS) - (o.length + c.length) / 2f;
                        if (g2 < 40f && g2 < gap) { gap = g2; lead = o; }
                        continue;
                    }
                    if (Math.Abs(o.z - c.z) > 2.5f || o == c.passBy || (c.passHard && InPass(c, o))) continue;     // 다른 층, 서로 기다림을 풀려고 잠시 무시하는 차
                    var rel = o.pos - frontAct;
                    float ahead = rel.Dot(dir);
                    if (ahead < 0f || ahead > 40f) continue;
                    // 차로 끝에서 꺾을 차는 끝 너머를 곧게 보지 않는다(꺾어 갈 길은 몸통 겹침 막기·붙는 구간 양보가 본다) —
                    //   곧게 보면 교차로 안에서 꺾어 비켜 갈 차를 앞차로 보고 서로 섰다(양재1동 중앙로 (-1130,1503))
                    if (c.planNext >= 0 && c.planTurn != 0 && ahead > restL - c.length / 2f + 1f) continue;
                    float side = Math.Abs(rel.Dot(new V2(-dir.y, dir.x)));
                    if (side > 1.8f) continue;
                    if (o.speed > 0.5f && new V2((float)Math.Cos(o.heading), (float)Math.Sin(o.heading)).Dot(dir) < 0f) continue;   // 마주 오는 차
                    if (ahead - o.length / 2f < gap) { gap = ahead - o.length / 2f; lead = o; }
                }
                // 몸통 겹침 막기: 앞으로 갈 길(지금 차로 → 다음 차로, 옆 거리 포함)에 내 몸통 네모(옆 0.1 m 여유)를
                //   1 m 마다 놓아 보고, 다른 차 몸통과 겹치는 첫 자리 앞에서 선다. 위의 "앞차"(가운데가 옆 1.8 m 안)만 보면
                //   교차로 정지선에서 코가 큰길로 나온 차, 옆 차로에서 비스듬히 선 버스 꼬리 같은 것을 못 보고 뚫고 지나갔다.
                //   내 뒤에 있는 차(그 차가 나를 본다)와 맞은편에서 오는 차(골목 비켜 가기·맞은편 차로 규칙이 따로)는 빼고.
                c.leadBy = lead != null && gap < 6f ? lead : null;      // 나를 세운(세울) 앞차 — 서로 기다림 풀기에 쓴다
                var gapBy = lead;
                c.guardBy = null;
                if (c.speed > -0.1f && c.guardPoly != null)
                {
                    float look = 2.5f + Math.Max(0f, c.speed) * 1.3f;
                    List<TrafficCar> cand = null;
                    foreach (var o in cars)
                    {
                        if (o == c || o.guardPoly == null || Math.Abs(o.z - c.z) > 2.5f || (c.passHard && InPass(c, o))) continue;
                        var rel = o.pos - c.pos;
                        if (rel.Length > (c.length + o.length) / 2f + look + 1f || rel.Dot(dir) <= 0f) continue;
                        var odir = new V2((float)Math.Cos(o.heading), (float)Math.Sin(o.heading));
                        if (odir.Dot(dir) < -0.5f && (ln.shared || o.speed > 0.3f)) continue;
                        // 서로가 서로를 막고 있으면(이미 맞닿아 엇갈린 두 차) 상대가 덜 앞에 있는 쪽이 먼저 빠져나간다(둘 다 서면 영영 선다)
                        if (o.guardBy == c)
                        {
                            float aC = rel.Dot(dir), aO = (c.pos - o.pos).Dot(odir);
                            if (aC < aO - 0.1f || (Math.Abs(aC - aO) <= 0.1f && c.model * 31 + c.paint < o.model * 31 + o.paint)) continue;
                        }
                        (cand ??= new List<TrafficCar>()).Add(o);
                    }
                    if (cand != null)
                    {
                        float laneLen = LaneLength(c.lane);
                        for (float sd = 0.5f; sd <= look; sd += 1f)
                        {
                            float sa = c.s + sd;
                            V2 gp, gd;
                            if (sa <= laneLen) Sample(c.lane, sa, out gp, out gd);
                            else if (c.planNext >= 0) Sample(c.planNext, Math.Min(c.planS + (sa - laneLen), LaneLength(c.planNext)), out gp, out gd);
                            else { gp = p + dir * sd; gd = dir; }
                            if (Math.Abs(c.lat) > 0.05f) gp += new V2(-gd.y, gd.x) * c.lat;
                            var gpoly = Geom.RectPoly(gp, c.length, c.width + 0.2f, (float)Math.Atan2(gd.y, gd.x) * Geom.Rad2Deg);
                            bool hit = false;
                            foreach (var o in cand) if (Geom.ConvexOverlap(gpoly, o.guardPoly)) { hit = true; c.guardBy = o; break; }
                            if (hit) { if (sd - 1f < gap) gapBy = c.guardBy; gap = Math.Min(gap, sd - 1f); break; }
                        }
                    }
                }
                // 사람처럼 앞지르기: 앞차가 많이 느리면(내 제한 속도의 45 % 밑, 20 m 안) 성향에 따라 1.5~5초 뒤
                //   옆 차로(같은 방향, 나란히 2.2~4.6 m — 왼쪽 먼저)로 옮긴다. 옆 차로 앞 20 m·뒤 15 m 가 비어 있고 그 차로가 60 m 넘게 남아야.
                //   깜빡이를 켜고 옆으로 미끄러지듯(옆 거리를 0.8 m/s 로 줄인다)
                if (lead != null && !c.IsBus && !ln.shared && gap < 20f && lead.speed < limit * 0.45f && LaneLength(c.lane) - c.s > 50f && Math.Abs(c.lat) < 0.3f)
                    c.slowT += dt;
                else c.slowT = 0f;
                if (c.slowT > (c.style == 2 ? 1.5f : (c.style == 1 ? 5f : 3f)))
                {
                    c.slowT = 0f;
                    if (TrySideLane(c, dir, me.Center)) { ln = road.lanes[c.lane]; Sample(c.lane, c.s, out p, out dir); front = p + dir * (c.length / 2f); frontAct = front + new V2(-dir.y, dir.x) * c.lat; }   // 옛 앞차까지 틈(gap)은 그대로 — 아직 그 뒤에 있다
                }
                bool meetMe = false;
                if (Math.Abs(me.z - c.z) < 2.5f)
                {
                    var rel = me.Center - frontAct;
                    float ahead = rel.Dot(dir);
                    float side = Math.Abs(rel.Dot(new V2(-dir.y, dir.x)));
                    // 마주 오는 내 차가 가까이(옆 3.2 m 안, 앞 18 m ~ 옆을 지나는 중) 오면 오른쪽으로 0.5 m 비켜 준다(좁은 길 사람 운전 — 골목 입구에서 스침)
                    if (me.Dir.Dot(dir) < -0.5f && side < 3.2f && ahead > -me.length - 2f && ahead < 18f) meetMe = true;
                    // 옆 2.8 m 까지(좁은 골목에서 마주 오는 내 차 앞에 선다 — 2.2 m 로는 비스듬히 스쳐 부딪힘)
                    // 내 차가 마주 오면 그 속도만큼(1.5초) 더 일찍
                    float closing = me.Dir.Dot(dir) < -0.5f ? Math.Max(0f, me.speed) * 1.5f : 0f;
                    // 서 있다가 출발할 때는 바로 옆을 지나가는 내 차가 다 지나갈 때까지(뒤로 내 차 길이 + 1 m) 기다린다
                    //   (골목 입구에 서 있던 차가 옆을 스치며 꺾어 들어오는 내 차 옆구리로 출발)
                    float behind = c.speed < 1f && Math.Abs(me.speed) > 0.5f ? -(me.length + 1f) : -1f;
                    if (ahead > behind && ahead < 40f && side < 2.8f && ahead - me.length / 2f - closing < gap) { gap = ahead - me.length / 2f - closing; gapBy = null; }
                }
                if (!float.IsInfinity(gap))
                {
                    // 교차로 안의 짧은 차로에서는 앞차에 바짝(1.8 m) 붙는다 — 4~6 m 띄우고 서면 꼬리가 가로지르는 차로를 덮어 교차로가 막혔다
                    float safe = Math.Max(0f, gap - (LaneLength(c.lane) < 30f ? Math.Min(c.GapM, 1.8f) : c.GapM));
                    float vg = (float)Math.Sqrt(2f * 3f * safe);
                    target = Math.Min(target, vg);
                    if (vg < wV) { if (gapBy != null) { wV = vg; wBy = gapBy; wWhy = "gap"; } else hold = "me"; }
                }
                // 좁은 길에서 내 차와 정면으로 마주 서 버리면(둘 다 거의 섬, 12 m 안) 3초 뒤 천천히 물러난다(비켜 줄 수 없는 골목)
                bool headOn = Math.Abs(me.z - c.z) < 2.5f && me.Dir.Dot(dir) < -0.6f && Math.Abs(me.speed) < 1.2f && c.speed < 0.5f
                              && me.Center.DistanceTo(front) < 15f && Math.Abs((me.Center - front).Dot(new V2(-dir.y, dir.x))) < 2.4f;
                c.waitMe = headOn ? c.waitMe + dt : 0f;
                // 바로 앞(2~5 m)에 내 차가 있으면(교차로에서 꺾어 들자마자 마주친 경우 등) 급제동(골목 들머리 충돌)
                bool emergency = false;
                if (Math.Abs(me.z - c.z) < 2.5f && me.Center.DistanceTo(front) < 14f)
                {
                    // 내 차 네모까지 거리(가운데만 보면 비스듬히 선 내 차 모서리를 옆에서 들이받았다)
                    float look = 2f + c.speed * 1.2f;
                    float laneLen = LaneLength(c.lane);
                    for (float sd = 0f; sd <= look; sd += 1.5f)
                    {
                        // 차로를 따라 앞(꺾는 중이면 굽은 길 그대로), 차로 끝을 넘으면 곧게
                        float sAt = c.s + c.length / 2f + sd;
                        V2 probe;
                        if (sAt <= laneLen) Sample(c.lane, sAt, out probe, out _);
                        else probe = front + dir * sd;
                        if (Geom.PointInPolygon(probe, myPoly) || Geom.DistPointPoly(probe, myPoly, out _) < c.width / 2f + 0.4f) { target = 0f; emergency = true; hold = "me"; break; }
                    }
                }
                // 다음 차로를 차로 끝 70 m 앞에서 미리 정한다(꺾는 쪽에 따라 직진 신호·좌회전 화살표를 본다)
                if (c.planNext == -2 && LaneLength(c.lane) - c.s < 70f)
                {
                    c.planNext = NextLane(c.lane, out c.planS);
                    c.planTurn = 0;
                    if (c.planNext >= 0)
                    {
                        var lc = road.lanes[c.lane].center;
                        var dEnd = (lc[lc.Length - 1] - lc[lc.Length - 2]).Normalized;
                        Sample(c.planNext, Math.Min(c.planS + 6f, LaneLength(c.planNext)), out var pn, out var dn);
                        float cr = dEnd.x * dn.y - dEnd.y * dn.x;      // y 아래 좌표: + 오른쪽(시계 방향)
                        if (dEnd.Dot(dn) < 0.8f) c.planTurn = cr < 0f ? -1 : 1;
                    }
                }
                // 신호: 이 차로를 따라 앞 50 m 안의 정지선이 빨강·노랑이면 그 앞 1.5 m 에 선다(왼쪽으로 꺾을 차는 좌회전 화살표를 본다)
                float slAhead = float.PositiveInfinity;      // 내 차로 앞 정지선까지(색과 관계없이) — 꼬리물기 금지에 쓴다
                // 이미 넘은 정지선(뒤 40 m 안)과 같은 교차로의 안쪽 정지선은 보지 않는다 — 교차로 안에 서면 가로지르는 차들과
                //   서로 막는다(역삼1동 강남대로×도곡로 (-326,1246) 교차로 안 정지선). 넘은 교차로는 지난 장 것을 쓴다
                bool inJ = c.inJct; var jIn = c.jct; c.inJct = false;
                foreach (var sl in road.stopLines)
                {
                    if (sl.signal < 0 || sl.approach.Dot(dir) < 0.8f) continue;
                    if (Math.Abs(c.z) > 2.5f) break;     // 고가·지하차도 위에서는 땅 신호를 보지 않는다
                    var mid = (sl.a + sl.b) / 2f;
                    var rel = mid - front;
                    float ahead = rel.Dot(dir);
                    if (ahead < -40f || ahead > 50f) continue;
                    if (Math.Abs(rel.Dot(new V2(-dir.y, dir.x))) > 2.5f) continue;
                    var sg = road.signals[sl.signal];
                    if (ahead < -0.5f) { c.inJct = true; c.jct = sg.junction; continue; }
                    if (inJ && sg.junction.DistanceTo(jIn) < 3f) continue;
                    if (ahead > 0.5f) slAhead = Math.Min(slAhead, ahead);
                    var col = c.planTurn < 0 ? sg.LeftAt(w.t) : sg.At(w.t);
                    // 노란불: 서기 편한 거리면 선다(급한 차는 더 가까워도 지나간다)
                    bool stop = col == SignalColor.Red || (col == SignalColor.Yellow && ahead > c.speed * c.speed / (c.style == 2 ? 3f : 6f));
                    if (stop) { float vs = (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, ahead - 1.5f)); target = Math.Min(target, vs); if (vs < 0.5f) hold = "signal"; }
                }
                // (법 제25조⑤ 꼬리물기 금지) 신호 교차로: 정지선을 넘기 전인데, 내가 들어갈 교차로 안 차로(30 m 안 되는 짧은 차로)에 서 있는 차가 있으면
                //   초록불이라도 정지선에서 기다린다. 앞차를 따라 들어가 교차로 안에 서는 바람에 가로지르는 차들과 고리처럼 막혀 역삼역 교차로가 80초 넘게 멈췄다.
                if (!float.IsInfinity(slAhead) && slAhead < 40f)
                {
                    int boxLane = c.planNext >= 0 ? c.planNext : road.lanes[c.lane].next;
                    if (boxLane >= 0 && LaneLength(boxLane) < 30f)
                        foreach (var o in cars)
                            if (o != c && !Ign(c, o) && o.lane == boxLane && o.speed < 0.5f && Math.Abs(o.z - c.z) < 2.5f)
                            {
                                float vb = (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, slAhead - 1.5f));
                                target = Math.Min(target, vb);
                                if (vb < wV) { wV = vb; wBy = o; wWhy = "box"; }
                                break;
                            }
                }
                // 차로끼리 가로지르는 자리(교차로 안): 먼저 갈 차를 정해 다른 차는 그 앞에 선다, 앞이 막혀 그 자리 안에 서게 되면 들어가지 않는다
                c.xGap = gap; c.xLead = gapBy; c.xLeadV = gapBy != null ? gapBy.speed : (float.IsInfinity(gap) ? 99f : 0f);
                bool crossYield = CrossYield(c, ref target, ref wV, ref wBy, ref wWhy);
                c.crossWait = crossYield && c.speed < 0.5f ? c.crossWait + dt : (crossYield ? c.crossWait : 0f);
                // 횡단보도를 건너는 사람: 앞 18 m·옆 3 m 안이면 그 3 m 앞에 선다
                if (w.crossPeds != null && Math.Abs(c.z) < 0.5f)
                {
                    float pd = w.crossPeds.Ahead(front, dir, 18f, 3f);
                    if (!float.IsInfinity(pd)) { float vp = (float)Math.Sqrt(2f * 3f * Math.Max(0f, pd - 3f)); target = Math.Min(target, vp); if (vp < 0.5f) hold = "ped"; }
                }
                // 꺾이는 곳에서는 줄인다: 다른 차들이 시속 30 km 로 직각을 돌았다(자동 점검 "방향 휙 돎" 수백 번).
                //   내 차로(끝을 넘으면 다음으로 고른 차로)의 8 m·16 m 앞 방향이 지금 방향과 30° 넘게 다르면 시속 14 km(60° 넘으면 10 km)까지
                {
                    float lenNow = LaneLength(c.lane);
                    for (int look = 1; look <= 2; look++)
                    {
                        float sa = c.s + 8f * look; V2 da;
                        if (sa <= lenNow) Sample(c.lane, sa, out _, out da);
                        else if (c.planNext >= 0) Sample(c.planNext, Math.Min(c.planS + (sa - lenNow), LaneLength(c.planNext)), out _, out da);
                        else break;
                        float cosA = da.Dot(dir);
                        if (cosA > 0.866f) continue;
                        float vt = (cosA < 0.5f ? 10f : 14f) / 3.6f;
                        target = Math.Min(target, look == 1 ? vt : (float)Math.Sqrt(vt * vt + 2f * 2.5f * 8f));
                    }
                }
                // 과속방지턱: 앞 20 m 안·넘는 동안 20 km/h
                var rgn = road.region;
                if (rgn != null && rgn.humps.Count > 0 && Math.Abs(c.z) < 0.5f && rgn.HumpAhead(front, dir, -c.length, 6f + c.speed * 2f))
                    target = Math.Min(target, 20f / 3.6f);
                // 버스: 이 차로의 다음 정류장(앞 60 m 안)에 선다 — 앞 범퍼가 표지 1 m 앞, 오른쪽 깜빡이, 10~20초 뒤 왼쪽 깜빡이 켜고 떠남
                if (c.IsBus && laneStops != null && c.lane >= 0 && laneStops[c.lane] != null && Math.Abs(c.z) < 0.5f)
                {
                    float frontS = c.s + c.length / 2f;
                    foreach (var ss in laneStops[c.lane])
                    {
                        if (c.doneStopLane == c.lane && Math.Abs(ss - c.doneStopS) < 1f) continue;
                        float ahead = ss + 1f - frontS;
                        if (ahead < -3f || ahead > 60f) continue;
                        if (ahead < 30f) c.blink = 1;
                        if (c.dwell > 0f)
                        {
                            target = 0f; hold = "bus";
                            c.dwell -= dt;
                            if (c.dwell <= 0f) { c.doneStopLane = c.lane; c.doneStopS = ss; c.blink = -1; c.leaveT = 4f; }
                        }
                        else
                        {
                            // 서는 까닭은 그 규칙 값으로만 적는다 — 다른 차 때문에 선 것을 '버스 정류장'으로 적어 고리를 못 찾았다(역삼1동 39대)
                            float vbs = (float)Math.Sqrt(2f * 1.6f * Math.Max(0f, ahead));
                            target = Math.Min(target, vbs);
                            if (vbs < 0.5f) hold = "bus";
                            // 표지 4 m 앞까지 와서 섰으면(교차로 앞 양보로 조금 일찍 선 경우 등) 거기서 태운다
                            if (ahead < 4f && c.speed < 0.4f) c.dwell = 10f + (float)rnd.NextDouble() * 10f;
                        }
                        break;
                    }
                }
                if (c.leaveT > 0f) { c.leaveT -= dt; if (c.leaveT <= 0f) c.blink = 0; }
                // 좁은 양방향 골목(shared)에서 맞은편 차가 40 m 안에서 오면
                //   둘 다 오른쪽으로 붙이고(길 폭/2 − 차 반폭 − 0.15 m, 최대 1.4 m) 25 m 안에서는 11 km/h 로 비켜 간다.
                //   둘이 못 지나갈 만큼 좁으면(폭 < 두 차 폭 + 0.35 m) 골목에 덜 들어온 쪽이 오른쪽에 붙어 서서 기다린다
                c.latT = meetMe && !ln.shared ? 0.5f : 0f;
                bool facing = false;
                if (ln.shared && ln.pair >= 0 && Math.Abs(c.z) < 0.5f)
                {
                    float pull = Math.Max(0f, Math.Min(1.4f, ln.room / 2f - c.width / 2f - 0.15f));
                    // 짝 차로가 같은 선을 거꾸로 간 것(길이가 같다)이면 맞은편 차까지 거리를 길 따라 잰다 — 곧은 거리·옆 거리(3.5 m)로 재면
                    //   굽은 짧은 골목에서 판정이 매 장 바뀌어(마주 섬 ↔ 아님) 덜 들어온 차가 물러나지 못하고 둘 다 섰다(양재1동 (1226,-1884))
                    float pairL = LaneLength(ln.pair);
                    bool sameLine = Math.Abs(pairL - LaneLength(c.lane)) < 1f;
                    foreach (var o in cars)
                    {
                        if (o.lane != ln.pair) continue;
                        var rel = o.pos - c.pos;
                        float along = sameLine ? pairL - o.s - c.s : rel.Dot(dir);
                        if (along < -1f || along > 40f || (!sameLine && Math.Abs(rel.Dot(new V2(-dir.y, dir.x))) > 3.5f)) continue;
                        c.latT = pull;
                        if (along < 25f) target = Math.Min(target, 11f / 3.6f);
                        bool fits = ln.room >= c.width + o.width + 0.35f;
                        if (!fits)
                        {
                            float oIn = o.s, myIn = c.s;     // 골목에 들어온 거리
                            if (c.aside != 0f)
                            {
                                facing = true;      // 옆길 입구에 비켜 서 있는 중
                            }
                            else if (myIn < oIn || (Math.Abs(myIn - oIn) < 0.5f && c.paint < o.paint))
                            {
                                // 덜 들어온 쪽이 선다 — 3초 넘게 마주 서 있으면 골목 입구 쪽으로 천천히 물러난다(사람이 하듯)
                                facing = true;
                                target = Math.Min(target, (float)Math.Sqrt(2f * 3f * Math.Max(0f, along - (c.length + o.length) / 2f - 4f)));
                                if (c.speed < 0.3f && along < 15f) c.alleyWait += dt;
                                if (c.alleyWait > 3f) c.alleyBack = true;
                                // 물러나다 뒤 60 m 안 옆길 입구에 닿으면 그쪽으로 비켜 서서 보낸다
                                if (c.alleyBack)
                                {
                                    var ms = Mouths(c.lane);
                                    // 맞은편 차가 꺾어 들어갈 옆길 입구는 피한다(그 입구에 비켜 서면 그 차가 들이받는다)
                                    bool oTurns = o.planNext >= 0;
                                    for (int mi = ms.Count - 1; mi >= 0; mi--)
                                    {
                                        if (ms[mi].s > c.s + 0.5f || c.s - ms[mi].s > 60f) continue;
                                        if (oTurns)
                                        {
                                            Sample(c.lane, ms[mi].s, out var mp0, out var md0);
                                            // 맞은편 차가 들어갈 차로의 처음 15 m 가 그 입구 쪽(골목 가운데에서 그쪽으로 1.2 m 넘게, 앞뒤 8 m 안)으로 들어가면 = 그 옆길로 꺾는다
                                            bool hit = false;
                                            var sideN = new V2(-md0.y, md0.x) * ms[mi].side;
                                            for (float q = 0f; q <= 15f && !hit; q += 1.5f)
                                            {
                                                Sample(o.planNext, Math.Min(o.planS + q, LaneLength(o.planNext)), out var op, out _);
                                                var rq = op - mp0;
                                                if (rq.Dot(sideN) > 1.2f && Math.Abs(rq.Dot(md0)) < 8f) hit = true;
                                            }
                                            if (hit) continue;
                                        }
                                        if (c.s <= ms[mi].s + 0.5f) { c.aside = ms[mi].side; c.alleyBack = false; AlleyAside++; }
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
                if (c.aside != 0f)
                {
                    // 비켜 선 동안: 옆길 입구 쪽으로(골목 반폭 + 1.6 m) 서 있다가, 맞은편 차가 옆(앞뒤 8 m)을 다 지나가면 돌아온다
                    bool near = false;
                    foreach (var o in cars)
                    {
                        if (o.lane != ln.pair) continue;
                        float along = (o.pos - c.pos).Dot(dir);
                        if (along > -8f && along < 40f && Math.Abs((o.pos - c.pos).Dot(new V2(-dir.y, dir.x))) < 6f) { near = true; break; }
                    }
                    // 골목 처음 교차로(8 m 안)에서는 옆길 안으로 더 깊이(맞은편 차가 교차로에서 꺾으며 스치지 않게)
                    if (near) { c.latT = c.aside * (ln.room / 2f + (c.s < 8f ? 3.2f : 1.6f)); target = 0f; facing = true; }
                    else if (Math.Abs(c.lat) > 0.1f) { target = 0f; facing = true; }     // 골목 가운데로 돌아오는 중
                    else c.aside = 0f;
                }
                if (!facing) { c.alleyWait = 0f; c.alleyBack = false; }
                else if (target < 0.5f) hold = "alley";     // 골목 마주침은 물러나기·비켜 서기가 따로 푼다
                float latRate = c.aside != 0f ? 1.2f : 0.8f;
                c.lat += Math.Max(-latRate * dt, Math.Min(latRate * dt, c.latT - c.lat));
                // 꺾기 전 30 m 부터 방향지시등(버스 정류장 깜빡이는 그대로)
                if (c.planNext >= 0 && c.planTurn != 0 && c.dwell <= 0f && c.leaveT <= 0f && LaneLength(c.lane) - c.s < 30f) c.blink = c.planTurn;
                // 차로 끝(꺾을 곳)은 천천히
                float rest = LaneLength(c.lane) - c.s;
                // 다른 차끼리 겹치지 않게 차로 끝(교차로)에서 양보
                //   ① 들어갈 차로의 들어가는 자리(앞뒤 차 길이 + 3 m)에 다른 차가 있으면 차로 끝 2 m 앞에 선다
                //   ② 내 차로 끝 10 m 안에 엇갈려(30~150°) 오는 차가 나보다 그 자리에 가까우면 선다
                //   서로 기다리다 멈추지 않게 4초 넘게 기다렸으면 간다
                if (rest < 25f && Math.Abs(c.z) < 0.5f)
                {
                    bool yield = false, occ = false, brk = false; c.occBy = null; TrafficCar occO = null;
                    var lcn = ln.center;
                    var endP = lcn[lcn.Length - 1];
                    float myD = c.pos.DistanceTo(endP);
                    if (c.planNext >= 0)
                    {
                        Sample(c.planNext, Math.Min(c.planS + 2f, LaneLength(c.planNext)), out var entry, out var entryDir);
                        // 들어갈 자리: 그 차로 방향으로 놓은 내 차 네모(앞뒤 1 m·옆 0.4 m 여유) — 옆 차로(3 m 남짓 옆)에 선 차는 막지 않는다
                        var entryPoly = Geom.RectPoly(entry, c.length + 2f, c.width + 0.8f, (float)Math.Atan2(entryDir.y, entryDir.x) * Geom.Rad2Deg);
                        // 들어갈 자리에 내 차(플레이어)가 움직이며 있으면 기다린다(서 있는 내 차는 이 차를 기다리는 중일 수 있어 — 둘이 서로 기다려 멈추지 않게)
                        if (Math.Abs(me.z - c.z) < 2.5f && Math.Abs(me.speed) > 1f && (me.Center - c.pos).Dot(dir) > 0f && me.Center.DistanceTo(entry) < (me.length + c.length) / 2f + 3f + c.speed * c.speed / 8f) occ = true;
                        foreach (var o in cars)
                        {
                            if (o == c || Math.Abs(o.z - c.z) > 2.5f || Ign(c, o)) continue;
                            // 들어갈 자리 앞쪽은 내 속도로 설 수 있는 거리(v²/8)만큼 더 비어 있어야
                            // 들어갈 자리를 막은 차가 나 때문에(내 코가 그 차 앞에 걸쳐) 서 있으면 둘 다 영영 선다 →
                            //   5초 넘게 기다렸으면 내가 먼저 빠져나간다(길이 정말 막혔으면 앞차 보기, 몸통 겹침 막기로 여전히 선다)
                            bool mutual = (o.guardBy == c || o.leadBy == c) && o.speed < 0.3f && c.waitJ > 5f;
                            if (mutual && o.lane == c.planNext && o.s > c.planS - o.length && o.s < c.planS + c.length + 3f) { brk = true; continue; }
                            if (o.lane == c.planNext && o.s > c.planS - o.length && o.s < c.planS + c.length + 3f + c.speed * c.speed / 8f) { occ = true; c.occBy = occO = o; break; }
                            // 들어갈 차로 뒤쪽 20 m 안에서 다가오는 차(차로 중간으로 끼어들 때)
                            //   (50 km/h 차는 20 m 를 1.4초에 온다 — 4초 안에 올 거리까지 본다)
                            if (o.lane == c.planNext && c.planS > 3f && o.s <= c.planS - o.length && o.s > c.planS - Math.Max(20f, o.speed * 4f) && o.speed > 1f) { occ = true; occO = o; break; }
                            // 옆길 입구로 비켜 서 있는 차(골목 마주침)는 길을 비켜 준 것 — 그 차 때문에 서면 서로 기다려 멈춘다
                            if (o.lane != c.lane && o.aside == 0f && o.pos.DistanceTo(entry) < (o.length + c.length) / 2f + 1f && Geom.ConvexOverlap(o.Poly, entryPoly))
                            {
                                // (역삼역 시연 경로) 그 차도 나 때문에(내가 그 차 들어갈 자리에 있어) 서 있으면 둘 다 영영 선다 →
                                //   5초 넘게 서로 기다렸으면 차로 끝에 더 가까운 쪽(같으면 번호 작은 쪽)이 먼저 간다(바로 앞을 막으면 앞차 보기로 여전히 선다)
                                if (o.occBy == c && c.waitJ > 5f && o.lane >= 0)
                                {
                                    float oRest = LaneLength(o.lane) - o.s;
                                    if (rest < oRest - 0.5f || (Math.Abs(rest - oRest) <= 0.5f && c.model * 31 + c.paint < o.model * 31 + o.paint)) { brk = true; continue; }
                                }
                                occ = true; c.occBy = occO = o; break;
                            }
                            // 좁은 골목(둘이 못 비켜 가는 폭) 입구: 맞은편에서 골목 안을 오는 차가 있으면 들어가지 않고 기다린다(사람 운전 관습)
                            var pn = road.lanes[c.planNext];
                            if (pn.shared && pn.pair >= 0 && o.lane == pn.pair && pn.room < c.width + o.width + 0.35f)
                            {
                                if ((o.guardBy == c || o.leadBy == c) && o.speed < 0.3f && c.waitJ > 5f) { brk = true; continue; }      // 그 차가 나 때문에 못 나오고 있다
                                occ = true; occO = o; break;
                            }
                            // 다른 차로에서 같은 차로로 들어가려는 차가 나보다 그 끝에 가까우면(합류) 먼저 보낸다
                            //   들어가는 자리가 15 m 넘게 떨어진 차(그 차로 중간으로 들어오는 먼 차)는 빼고, 한쪽이 6초 넘게 기다렸으면 더 오래 기다린 차가
                            //   먼저(상대가 아직 편히 설 수 있을 때만) — 큰길 끝에서 물러나 선 차가 옆길에서 잇달아 오는 차에 5분 넘게 밀렸다(양재1동 남부순환로 (1697,-1798))
                            if (o.planNext == c.planNext && o.lane != c.lane && o.lane >= 0 && Math.Abs(o.planS - c.planS) < 15f)
                            {
                                float oRest = LaneLength(o.lane) - o.s;
                                bool oFirst = oRest < rest - 0.5f || (Math.Abs(oRest - rest) <= 0.5f && o.model * 31 + o.paint < c.model * 31 + c.paint);
                                if (c.waitJ > 6f && c.waitJ > o.waitJ + 1f && CanStop(o, oRest)) oFirst = false;
                                else if (o.waitJ > 6f && o.waitJ > c.waitJ + 1f && CanStop(c, rest)) oFirst = true;
                                if (oRest < 30f && oFirst) { occ = true; occO = o; break; }
                            }
                        }
                    }
                    if (!yield)
                        foreach (var o in cars)
                        {
                            if (o == c || o.lane == c.lane || Math.Abs(o.z - c.z) > 2.5f || o.speed < 0.3f || Ign(c, o)) continue;
                            float oD = o.pos.DistanceTo(endP);
                            // 14 m 안 + 그 자리로 오는 중이고 4초 안에 닿을 차(사람은 빠르게 오는 차를 멀리서 보고 기다린다)
                            var odv = new V2((float)Math.Cos(o.heading), (float)Math.Sin(o.heading));
                            bool coming = (endP - o.pos).Dot(odv) > 0f;
                            float tO = oD / Math.Max(o.speed, 0.5f);
                            if (oD > 14f && !(coming && tO < 4f && oD < 60f)) continue;
                            float dh = Math.Abs((float)Math.IEEERemainder(o.heading - c.heading, 2 * Math.PI)) * Geom.Rad2Deg;
                            if (dh <= 30f || dh >= 150f) continue;
                            // 도로교통법 제26조(신호 없는 교차로): ① 이미 교차로에 들어간 차(그 자리 4 m 안) 먼저 ② 넓은(제한 속도 높은) 길 차 먼저
                            //   ③ 같으면 오른쪽에서 오는 차 먼저 — 그 밖에는 가까운 차 먼저
                            var olane = o.lane >= 0 ? road.lanes[o.lane] : null;
                            float myLim = road.LimitOf(ln), oLim = olane != null ? road.LimitOf(olane) : myLim;
                            bool fromRight = (o.pos - c.pos).Dot(new V2(-dir.y, dir.x)) > 0f;
                            float tMe = (myD + c.length) / Math.Max(c.speed, 2.5f);
                            bool give = oD < 4f && myD > 4f
                                || (myD > 4f && oLim > myLim + 5f)
                                || (myD > 4f && Math.Abs(oLim - myLim) <= 5f && fromRight && (oD < myD + 6f || tO < tMe + 2f))
                                || oD < myD - 1f
                                || (coming && myD > 4f && tO < tMe);
                            if (give) { yield = true; occO = o; break; }
                        }
                    // 들어갈 자리가 막힌 것(occ)은 기다림 시간과 관계없이 선다 — 4초 넘게 기다린 뒤 둘이 함께 들어가 겹쳤다
                    if (occ || (yield && c.waitJ < 4f + (c.model % 5)))
                    {
                        float back = stopBack != null && c.lane < stopBack.Length ? stopBack[c.lane] : 2f;
                        float vj = (float)Math.Sqrt(2f * 3f * Math.Max(0f, rest - c.length / 2f - back));
                        target = Math.Min(target, vj);
                        if (vj < wV) { if (occO != null) { wV = vj; wBy = occO; wWhy = occ ? "occ" : "yield"; } else hold = "me"; }
                        if (c.speed < 0.5f) c.waitJ += dt;
                    }
                    else if (!yield && !occ && !brk) c.waitJ = 0f;     // 서로 기다림을 깨고 가는 중(brk)에는 기다린 시간을 지우지 않는다(지우면 5초마다 한 장씩만 움직였다)
                }
                if (ln.next < 0) target = Math.Min(target, (float)Math.Sqrt(16f / 3.6f * (16f / 3.6f) + 2f * 2f * Math.Max(0f, rest - 4f)));
                c.dbgGap = gap; c.dbgTarget = target;
                c.waitBy = hold == null ? wBy : null;
                c.waitWhy = hold ?? wWhy;
                c.xHeld = target < 0.5f && c.waitWhy != "cross";
                c.stopT = c.speed < 0.3f && target < 0.3f ? c.stopT + dt : 0f;
                if (c.waitBy != null && c.stopT > 5f && c.passBy == null) BreakLoop(c);
                // 다른 차 때문에 90초 넘게 서 있는데 고리로 안 잡히면(고리가 신호 단계에 걸쳐 끊기거나 길게 얽힌 무리) 그 차를 세운 차와
                //   그 기다림 사슬의 서 있는 차들을 무시하고 천천히 빠져나간다(같은 차로·들어갈 차로의 앞차 때문이면 줄 선 것이라 빼고, 빨간불·보행자로 선 차는 waitBy 가 없어 빠진다)
                if (c.waitBy != null && c.stopT > 90f && c.passBy == null && c.passCool <= 0f && LoopRank(c) >= 0 && !ChainEndsAtMe(c)) StartPass(c, true);
                // 가속 2 m/s², 감속 최대 5 m/s²
                float dv = target - c.speed;
                c.speed += dv > 0f ? Math.Min(dv, (c.style == 2 ? 2.8f : (c.style == 1 ? 1.5f : 2f)) * dt) : Math.Max(dv, (emergency ? -9f : -5f) * dt);
                if (c.speed < 0.05f && target < 0.1f) c.speed = 0f;
                if (c.waitMe > 3f) c.backing = true;
                if (c.backing)
                {
                    // 한 번 물러나기 시작하면 길 처음(교차로)까지 물러난다 — 내 차가 지나가거나 멀어지면 그만
                    bool stillFacing = me.Dir.Dot(dir) < -0.6f && me.Center.DistanceTo(front) < 25f && (me.Center - front).Dot(dir) > -1f;
                    if (stillFacing) c.speed = -1.8f; else { c.backing = false; c.waitMe = 0f; }
                }
                if (c.alleyBack) c.speed = -2f;
                if (c.aside != 0f && c.speed < 0f) c.speed = 0f;
                if (c.speed < 0f && c.s < 1f) { cars.RemoveAt(ci); continue; }
                c.braking = dv < -0.3f;
                c.s += c.speed * dt;
                // 차로를 넘어가면 다음 차로
                int guard = 0; bool handed = false; var posBefore = c.pos; float headBefore = c.heading;
                while (c.s > LaneLength(c.lane) && guard++ < 4)
                {
                    float over = c.s - LaneLength(c.lane);
                    int nx; float startS;
                    if (c.planNext != -2) { nx = c.planNext; startS = c.planS; }
                    else nx = NextLane(c.lane, out startS);
                    c.planNext = -2;
                    if (nx < 0) { c.lane = -1; break; }
                    c.lane = nx; handed = true;
                    c.s = startS + over;
                    c.waitJ = 0f;
                    if (c.dwell <= 0f && c.leaveT <= 0f) c.blink = 0;     // 꺾고 나면 방향지시등 끔
                }
                if (c.lane < 0) { cars.RemoveAt(ci); continue; }
                Sample(c.lane, c.s, out c.pos, out var d2, out c.z);
                if (Math.Abs(c.lat) > 0.01f) c.pos += new V2(-d2.y, d2.x) * c.lat;
                float want2 = (float)Math.Atan2(d2.y, d2.x);
                // 옆으로 붙이는 동안은 그쪽으로 살짝 고개를 튼다
                if (c.speed > 0.5f) want2 += Math.Max(-0.25f, Math.Min(0.25f, (c.latT - c.lat) * 0.6f));
                // 이음에서 튀지 않게: 넘어간 순간 "있어야 할 자리(달리던 방향으로 이번 장만큼)"와 새 차로 위 자리의 차이를 담아 두고 천천히 푼다
                if (handed && c.posValid)
                {
                    var hd = new V2((float)Math.Cos(headBefore), (float)Math.Sin(headBefore));
                    var jump = posBefore + hd * (c.speed * dt) - c.pos;     // posBefore 에는 풀리던 몫이 이미 들어 있다
                    c.seam = jump.Length < 8f ? jump : V2.Zero;
                }
                if (c.seam.x != 0f || c.seam.y != 0f)
                {
                    float k = (float)Math.Exp(-dt * 2.2f * Math.Max(0.4f, Math.Min(1.5f, Math.Abs(c.speed) / 6f)));   // 서 있으면 거의 그대로(선 차가 미끄러지지 않게)
                    if (Math.Abs(c.speed) < 0.05f) k = 1f;
                    c.seam *= k;
                    if (c.seam.Length < 0.02f) c.seam = V2.Zero;
                    c.pos += c.seam;
                    // 풀리는 동안은 그쪽으로 살짝 고개를 튼다(게걸음으로 보이지 않게)
                    if (c.speed > 0.5f) { float side = c.seam.Dot(new V2(-d2.y, d2.x)); want2 += Math.Max(-0.09f, Math.Min(0.09f, side * 2.2f / Math.Max(3f, c.speed))); }
                }
                // 방향은 한 장에 휙 돌지 않는다
                if (c.posValid)
                {
                    float dh = (want2 - headBefore + 3f * (float)Math.PI) % (2f * (float)Math.PI) - (float)Math.PI;
                    float cap = Math.Max(40f * Geom.Deg2Rad, Math.Min(150f * Geom.Deg2Rad, Math.Abs(c.speed) / 4f)) * dt;   // 달리는 속도로 반지름 4 m 를 도는 만큼(느려도 초당 40°)
                    want2 = headBefore + Math.Max(-cap, Math.Min(cap, dh));
                }
                c.posValid = true;
                c.heading = want2;
                // 내 차에서 600 m 넘게 멀어지면 다른 곳에 다시
                if (c.pos.DistanceTo(me.Center) > 600f) cars.RemoveAt(ci);
            }
            while (cars.Count < want && Spawn(me.Center, 150f, 400f)) { }
        }

        /// <summary>두 차로가 붙는 구간(가운데선끼리 2.1 m 안): 내 차로 위 s0~s1, 상대 차로 other 위 os0~os1</summary>
        struct Cross { public float s0, s1, os0, os1; public int other; }
        List<Cross>[] crosses;
        Dictionary<long, List<(int lane, int seg)>> segGrid;
        const float XCell = 20f, XNear = 2.1f, XPad = 0.4f;

        /// <summary>
        /// 차로 li 가 같은 높이의 다른 차로와 가운데선끼리 2.1 m 안으로 붙는 구간들(동네를 열 때 PrepareCrossings 가 모두 찾아 둔다, s0 순): 가로지름, 교차로 한가운데로
        /// 모이는 맞은편 차로 끝, 좁아지며 합쳐지는 차로. 이 지도의 교차로는 긴 차로가 그대로 지나가고 꺾는 차는 지나가는 차로로 옮겨 타서,
        /// 차로 끝 양보만으로는 교차로 한가운데를 시속 40~50 km 로 서로 뚫고 지나갔다(역삼1동 테헤란로×언주로: 25분에 30번).
        /// 골목 짝 차로(같은 길 반대쪽)는 골목 규칙이 따로 본다.
        /// </summary>
        List<Cross> Crosses(int li)
        {
            if (crosses == null) crosses = new List<Cross>[road.lanes.Count];
            var res = crosses[li];
            if (res != null) return res;
            if (segGrid == null) BuildSegGrid();
            res = new List<Cross>();
            var ln = road.lanes[li];
            var c = ln.center;
            xTmp.Clear();
            for (int k = 0; k + 1 < c.Length; k++)
            {
                var a = c[k]; var b = c[k + 1];
                float dl = a.DistanceTo(b);
                if (dl < 1e-3f) continue;
                xSeen.Clear();
                int x0 = (int)Math.Floor((Math.Min(a.x, b.x) - XNear) / XCell), x1 = (int)Math.Floor((Math.Max(a.x, b.x) + XNear) / XCell);
                int y0 = (int)Math.Floor((Math.Min(a.y, b.y) - XNear) / XCell), y1 = (int)Math.Floor((Math.Max(a.y, b.y) + XNear) / XCell);
                for (int gx = x0; gx <= x1; gx++)
                    for (int gy = y0; gy <= y1; gy++)
                    {
                        if (!segGrid.TryGetValue(((long)gx << 32) ^ (uint)gy, out var l)) continue;
                        foreach (var (oi, seg) in l)
                        {
                            if (oi == li || oi == ln.pair || !xSeen.Add(((long)oi << 20) | (uint)seg)) continue;
                            var ol = road.lanes[oi];
                            var p0 = ol.center[seg]; var p1 = ol.center[seg + 1];
                            // 토막끼리 가장 가까운 거리가 2.1 m 넘으면 건너뛴다(나란한 옆 차로 따위)
                            if (SegDist(a, b, p0, p1) >= XNear) continue;
                            float el = p0.DistanceTo(p1);
                            // 내 토막에서 상대 토막 가까이(상대 끝점을 내 토막에 비춘 자리 ± 2.1 m, 나란하면 상대 선에서 2.1 m 안)만 0.5 m 마다 본다
                            var ad = (b - a) / dl;
                            float ta = (p0 - a).Dot(ad), tb = (p1 - a).Dot(ad);
                            float lo = Math.Max(0f, Math.Min(ta, tb) - XNear), hi = Math.Min(dl, Math.Max(ta, tb) + XNear);
                            if (el > 1e-4f)
                            {
                                var en = new V2(-(p1.y - p0.y) / el, (p1.x - p0.x) / el);
                                float f0 = (a - p0).Dot(en), f1 = ad.Dot(en);      // 상대 선까지 옆 거리 = f0 + f1·t
                                if (Math.Abs(f1) > 1e-4f)
                                {
                                    float t1 = (-XNear - f0) / f1, t2 = (XNear - f0) / f1;
                                    lo = Math.Max(lo, Math.Min(t1, t2)); hi = Math.Min(hi, Math.Max(t1, t2));
                                }
                                else if (Math.Abs(f0) >= XNear) continue;
                            }
                            if (hi < lo) continue;
                            int n = Math.Max(1, (int)Math.Ceiling((hi - lo) / 0.5f));
                            for (int q = 0; q <= n; q++)
                            {
                                float t = (lo + (hi - lo) * q / n) / dl;
                                var pt = a + (b - a) * t;
                                var cp = Geom.ClosestPointOnSegment(pt, p0, p1);
                                if (cp.DistanceTo(pt) >= XNear) continue;
                                float u = el > 1e-4f ? cp.DistanceTo(p0) / el : 0f;
                                if (Math.Abs(ln.ZAt(k, t) - ol.ZAt(seg, u)) > 2f) continue;
                                xTmp.Add((oi, cum[li][k] + dl * t, cum[oi][seg] + el * u));
                            }
                        }
                    }
            }
            // 상대 차로별로, 1.5 m 넘게 끊기면 다른 구간
            xTmp.Sort((p, q) => p.o != q.o ? p.o.CompareTo(q.o) : p.s.CompareTo(q.s));
            int st = 0;
            for (int q = 1; q <= xTmp.Count; q++)
            {
                if (q < xTmp.Count && xTmp[q].o == xTmp[q - 1].o && xTmp[q].s - xTmp[q - 1].s < 1.5f) continue;
                var cr = new Cross { s0 = xTmp[st].s, s1 = xTmp[st].s, os0 = xTmp[st].u, os1 = xTmp[st].u, other = xTmp[st].o };
                for (int r = st + 1; r < q; r++) { cr.s1 = Math.Max(cr.s1, xTmp[r].s); cr.os0 = Math.Min(cr.os0, xTmp[r].u); cr.os1 = Math.Max(cr.os1, xTmp[r].u); }
                res.Add(cr);
                st = q;
            }
            res.Sort((p, q) => p.s0.CompareTo(q.s0));
            crosses[li] = res;
            return res;
        }
        readonly List<(int o, float s, float u)> xTmp = new List<(int, float, float)>();
        readonly HashSet<long> xSeen = new HashSet<long>();

        /// <summary>붙는 구간 찾기용 격자(토막이 지나는 칸마다, 20 m) — 처음 만들 때 한 번</summary>
        void BuildSegGrid()
        {
            segGrid = new Dictionary<long, List<(int, int)>>();
            for (int i = 0; i < road.lanes.Count; i++)
            {
                var c0 = road.lanes[i].center;
                for (int k = 0; k + 1 < c0.Length; k++)
                {
                    int x0 = (int)Math.Floor(Math.Min(c0[k].x, c0[k + 1].x) / XCell), x1 = (int)Math.Floor(Math.Max(c0[k].x, c0[k + 1].x) / XCell);
                    int y0 = (int)Math.Floor(Math.Min(c0[k].y, c0[k + 1].y) / XCell), y1 = (int)Math.Floor(Math.Max(c0[k].y, c0[k + 1].y) / XCell);
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                        {
                            long key = ((long)gx << 32) ^ (uint)gy;
                            if (!segGrid.TryGetValue(key, out var l)) segGrid[key] = l = new List<(int, int)>();
                            l.Add((i, k));
                        }
                }
            }
        }

        /// <summary>두 토막 사이 가장 가까운 거리(만나면 0)</summary>
        static float SegDist(V2 a, V2 b, V2 c, V2 d)
        {
            var r = b - a; var e = d - c;
            float den = r.x * e.y - r.y * e.x;
            if (Math.Abs(den) > 1e-6f)
            {
                var w0 = c - a;
                float t = (w0.x * e.y - w0.y * e.x) / den, u = (w0.x * r.y - w0.y * r.x) / den;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
            }
            float m = Geom.ClosestPointOnSegment(a, c, d).DistanceTo(a);
            m = Math.Min(m, Geom.ClosestPointOnSegment(b, c, d).DistanceTo(b));
            m = Math.Min(m, Geom.ClosestPointOnSegment(c, a, b).DistanceTo(c));
            return Math.Min(m, Geom.ClosestPointOnSegment(d, a, b).DistanceTo(d));
        }

        /// <summary>
        /// 모든 차로의 붙는 구간을 한꺼번에 찾아 둔다(구간 수를 돌려준다). 동네를 열 때 불러오는 화면 뒤에서 한 번 부른다
        /// (GameMain.StartSession 이 부른다). 처음 쓸 때 찾게 두면 첫 장에 격자 + 차가 있는 차로 거의 전부가
        /// 몰려 그 한 장이 36~77 ms·2.5 MB(.NET 시험 실행) 였다. 차로마다 찾는 값은 지도만 보고 언제 찾든 같으므로 결과는 처음 쓸 때 찾던 것과
        /// 비트까지 같다. 다 찾은 뒤에는 찾기용 격자·작업 목록을 놓는다(더 찾을 차로가 없다). 부르지 않으면 예전처럼 처음 쓸 때 찾는다.
        /// </summary>
        public int PrepareCrossings()
        {
            int n = 0;
            for (int i = 0; i < road.lanes.Count; i++) n += Crosses(i).Count;
            segGrid = null;
            xTmp.Clear(); xTmp.TrimExcess();
            xSeen.Clear(); xSeen.TrimExcess();
            return n;
        }

        /// <summary>이번 장에 정한 두 차의 가로지름 차례(번호 작은 차가 먼저면 true) — 두 차가 같은 판정을 쓰게</summary>
        readonly Dictionary<long, bool> xDec = new Dictionary<long, bool>();

        /// <summary>c 의 갈 길(지금 차로, 끝이 가까우면 다음 차로)과 o 의 갈 길이 붙는 가장 가까운 구간: 두 차 가운데에서 그 구간 처음·끝까지 길 따라 거리</summary>
        bool FindCross(TrafficCar c, TrafficCar o, out float c0, out float c1, out float o0, out float o1)
        {
            c0 = c1 = o0 = o1 = float.PositiveInfinity;
            float restC = LaneLength(c.lane) - c.s, restO = LaneLength(o.lane) - o.s;
            bool found = false;
            for (int pc = 0; pc < 2; pc++)
            {
                int L = pc == 0 ? c.lane : c.planNext;
                if (L < 0 || (pc == 1 && restC > 60f)) break;
                float offC = pc == 0 ? -c.s : restC - c.planS;
                var xs = Crosses(L);
                float from = pc == 0 ? c.s - 40f : c.planS - 40f;
                int lo = 0, hi = xs.Count;
                while (lo < hi) { int m = (lo + hi) / 2; if (xs[m].s0 < from) lo = m + 1; else hi = m; }
                for (int xi = lo; xi < xs.Count; xi++)
                {
                    var x = xs[xi];
                    float d0 = x.s0 + offC, d1 = x.s1 + offC;
                    if (d0 > 70f || d0 >= c0) break;
                    if (d1 < -c.length - 3f || (pc == 1 && x.s1 < c.planS)) continue;
                    float e0, e1;
                    if (x.other == o.lane) { e0 = x.os0 - o.s; e1 = x.os1 - o.s; }
                    else if (x.other == o.planNext && restO < 60f && x.os1 >= o.planS) { e0 = restO + x.os0 - o.planS; e1 = restO + x.os1 - o.planS; }
                    else continue;
                    if (e1 < -o.length - 3f || e0 > 70f) continue;
                    c0 = d0; c1 = d1; o0 = e0; o1 = e1; found = true;
                    break;
                }
            }
            return found;
        }

        /// <summary>
        /// 붙는 구간(가로지름·모임) 양보: 갈 길이 붙는 차마다 먼저 갈 차를 정하고(두 차가 같은 판정을 쓴다), 내가 나중이면
        /// 그 구간 앞 1 m 에 선다. 먼저 갈 차: ① 고리를 풀며 가는 차 ② 이미 그 구간에 걸친 차(둘 다면 더 들어간 차, 빨간불에 선 차는 빼고)
        /// ③ 몸통이 이미 상대 길을 막은 차 ④ 그 구간을 빠져나갈 수 있는 차(앞이 막히지 않은 차) ⑤ 다른 까닭으로 묶이지 않은 차
        /// ⑥ 설 수 없는 차(4 m/s² 로) ⑦ 8초 넘게 더 오래 기다린 차 ⑧ 1.5초 넘게 먼저 닿는 차 ⑨ 넓은(제한 속도 높은) 길 ⑩ 오른쪽에서 오는 차 ⑪ 번호.
        /// 내가 먼저여도 앞이 막혀 그 구간 안에 서게 되면 들어가지 않는다(꼬리물기 금지). 양보했으면 true.
        /// </summary>
        bool CrossYield(TrafficCar c, ref float target, ref float wV, ref TrafficCar wBy, ref string wWhy)
        {
            bool yielded = false;
            foreach (var o in cars)
            {
                if (o == c || Ign(c, o) || o.lane < 0 || Math.Abs(o.z - c.z) > 2.5f) continue;
                if (Math.Abs(o.pos.x - c.pos.x) > 90f || Math.Abs(o.pos.y - c.pos.y) > 90f) continue;
                // 같은 차로·한쪽이 다른 쪽 차로로 들어감·같은 차로로 합류는 앞차·합류 규칙이 본다
                if (o.lane == c.lane || o.lane == c.planNext || c.lane == o.planNext || (c.planNext >= 0 && c.planNext == o.planNext)) continue;
                if (!FindCross(c, o, out float d0, out float d1, out float e0, out float e1)) continue;
                float hc0 = d0 - c.length / 2f - XPad, hc1 = d1 + c.length / 2f + XPad;      // 앞 범퍼가 구간에 닿기까지, 뒤 범퍼가 구간을 벗어나기까지
                float ho0 = e0 - o.length / 2f - XPad, ho1 = e1 + o.length / 2f + XPad;
                if (hc1 <= 0f || ho1 <= 0f) continue;          // 한쪽이 이미 지나감
                if (hc0 > 12f + c.speed * c.speed / 6f || ho0 > 12f + o.speed * o.speed / 6f) continue;   // 아직 멀다(두 차 모두 같은 조건)
                bool inC = hc0 < 0f, inO = ho0 < 0f;
                bool blkC = !inC && c.xLeadV < 2f && c.xGap < hc1 + 1f, blkO = !inO && o.xLeadV < 2f && o.xGap < ho1 + 1f;
                // 다른 까닭(신호·들어갈 자리·앞차 따위)으로 서 있는 차는 이 구간 차례를 내준다(지난 장 기준)
                bool stC = c.speed < 1f && c.waitWhy == "signal", stO = o.speed < 1f && o.waitWhy == "signal";      // 빨간불에 묶인 차
                bool heldC = !inC && c.xHeld && c.speed < 1f, heldO = !inO && o.xHeld && o.speed < 1f;
                long key = c.id < o.id ? ((long)c.id << 32) | (uint)o.id : ((long)o.id << 32) | (uint)c.id;
                bool first, pC = c.passBy != null, pO = o.passBy != null;
                if (pC != pO) first = pC;      // 고리를 풀며 먼저 가는 차에는 비켜 준다
                else if (xDec.TryGetValue(key, out bool smallFirst)) first = smallFirst == (c.id < o.id);
                else
                {
                    // 구간에 걸쳐 있어도 빨간불에 서 있는 차는 지나갈 차가 아니다(몸통이 정말 막으면 몸통 겹침 막기가 세운다)
                    //   — 빨간불에 선 가로 길 차를 기다리다 초록이 끝나 15분 넘게 섰다(역삼1동 강남대로×도곡로)
                    if (inC && inO) first = stC != stO ? stO : (hc0 < ho0 || (hc0 == ho0 && c.id < o.id));
                    else if (inC != inO) first = inC ? !stC : stO;
                    else if ((o.guardBy == c) != (c.guardBy == o)) first = o.guardBy == c;     // 몸통이 이미 상대 길을 막고 있는 차가 먼저(빠져 줘야 풀린다)
                    else if (blkC != blkO) first = blkO;
                    else if (heldC != heldO) first = heldO;
                    else first = CrossFirst(c, hc0, o, ho0);
                    xDec[key] = first == (c.id < o.id);
                }
                if (first && !blkC) continue;
                // 나중이거나 앞이 막혀 그 구간을 빠져나갈 수 없다: 그 구간 앞 1 m 에 선다(이미 걸쳐 있으면 그대로 선다)
                float vx = inC ? 0f : (float)Math.Sqrt(2f * 3f * Math.Max(0f, hc0 - 1f));
                target = Math.Min(target, vx);
                if (vx < wV) { wV = vx; if (first) { wBy = c.xLead; wWhy = "box"; } else { wBy = o; wWhy = "cross"; } }
                if (!first) yielded = true;
            }
            return yielded;
        }

        /// <summary>가로지르는 자리에서 c 가 o 보다 먼저 가는가(eC·eO: 그 자리에 몸이 닿기까지 남은 거리)</summary>
        bool CrossFirst(TrafficCar c, float eC, TrafficCar o, float eO)
        {
            bool canC = eC > c.speed * c.speed / 8f + 0.5f, canO = eO > o.speed * o.speed / 8f + 0.5f;
            float tC = eC / Math.Max(c.speed, 3f), tO = eO / Math.Max(o.speed, 3f);
            if (canC != canO) return !canC;
            if (!canC) return tC < tO || (tC == tO && c.id < o.id);
            if (c.crossWait > 8f && c.crossWait > o.crossWait + 2f) return true;
            if (o.crossWait > 8f && o.crossWait > c.crossWait + 2f) return false;
            if (tC + 1.5f < tO) return true;
            if (tO + 1.5f < tC) return false;
            float vc = road.LimitOf(road.lanes[c.lane]), vo = road.LimitOf(road.lanes[o.lane]);
            if (Math.Abs(vc - vo) > 5f) return vc > vo;
            var hc = new V2((float)Math.Cos(c.heading), (float)Math.Sin(c.heading));
            var ho = new V2((float)Math.Cos(o.heading), (float)Math.Sin(o.heading));
            bool oRight = (o.pos - c.pos).Dot(new V2(-hc.y, hc.x)) > 0f, cRight = (c.pos - o.pos).Dot(new V2(-ho.y, ho.x)) > 0f;
            if (oRight != cRight) return cRight;
            return c.id < o.id;
        }

        /// <summary>
        /// 서로 기다림 고리 풀기: 나를 세운 차(waitBy)를 따라가다 16대 안에 나로 돌아오면 고리다.
        /// 고리 안에서 한 대만 6초 동안 자기를 세운 차와 서 있는 차들의 규칙(들어갈 자리·양보·꼬리물기·붙는 구간)을 무시하고 간다
        /// (모두 같은 순서로 고르므로 한 대만 뽑힌다). 몸통 겹침 막기는 지키되, 45초 넘게 서 있었으면 기다림 사슬의 서 있는 차 몸통도 무시한다.
        /// 먼저 갈 차: 규칙(들어갈 자리·꼬리물기·양보·붙는 구간)으로만 선 차 > 앞차 보기로 선 차 > 몸이 바로 막힌 차, 같으면 차로 끝에 가까운 차(1 m 단위)·번호 작은 차.
        /// 같은 차로(또는 들어갈 차로)의 앞차를 무시하는 일은 없다. 풀어 봤는데도 못 간 차는 20초 동안 뒤로 미룬다.
        /// 신호·보행자·내 차·골목 마주침으로 선 차가 끼면 고리가 아니다(waitBy 가 없다).
        /// </summary>
        void BreakLoop(TrafficCar c)
        {
            TrafficCar best = null;
            var x = c;
            for (int n = 0; n < 16; n++)
            {
                var y = x.waitBy;
                // 이번 장에 이미 빠진 차(차로 끝 lane −1·치운 차)를 가리키는 지난 장 waitBy 를 따라가지 않는다(LaneLength(−1) 예외)
                if (y == null || y.speed > 0.3f || y.lane < 0) return;
                if (LoopRank(x) >= 0 && (best == null || LoopBefore(x, best))) best = x;
                x = y;
                if (x == c) break;
            }
            if (x != c || best != c) return;
            // 처음에는 규칙(들어갈 자리·꼬리물기·양보·붙는 구간)만 무시하고 몸통 겹침 막기는 지킨다. 45초 넘게 풀리지 않으면
            //   고리에 든 차들의 몸통도 무시하고 천천히 빠져나간다(겹쳐 보여도 영영 서 있는 것보다 낫다 — 같은 차로 앞차는 그대로 본다)
            StartPass(c, c.stopT > 45f);
        }

        /// <summary>c 가 6초 동안 waitBy 를 무시하고 간다. 세게(hard)면 기다림 사슬(waitBy 를 따라 16대, 고리면 고리 전체)의 몸통도 무시한다</summary>
        void StartPass(TrafficCar c, bool hard)
        {
            c.passBy = c.waitBy;
            c.passHard = hard;
            c.passT = 6f;
            if (c.passSet == null) c.passSet = new TrafficCar[16];
            c.passN = 0;
            for (var y = c.waitBy; y != null && y != c && c.passN < 16; y = y.waitBy)
            {
                bool seen = false;
                for (int i = 0; i < c.passN; i++) if (c.passSet[i] == y) { seen = true; break; }
                if (seen) break;
                c.passSet[c.passN++] = y;
            }
            LoopBreaks++;
        }

        /// <summary>
        /// 기다림 사슬(waitBy)이 내 차(플레이어) 때문에 선 차에서 끝나는가. 그런 사슬은 90초 규칙으로 뚫지 않는다 —
        /// 내 차 때문에 선 차(그 차는 내 차를 가린다)를 뚫고 지나가면 옆 차로에서 내 차를 못 보는 차(옆 2.8 m 밖, 차로 끝 너머는 곧게만 봄)가
        /// 서 있는 내 차 옆구리로 들어와 '다른 차와 부딪혔습니다'(내 부딪힘 횟수)가 90초마다 났다(양재1동 중앙로 (-1131,1502), 내 차를 세워 둔 점검).
        /// </summary>
        static bool ChainEndsAtMe(TrafficCar c)
        {
            var y = c.waitBy;
            for (int n = 0; n < 16 && y != null && y != c; n++)
            {
                if (y.waitBy == null) return y.waitWhy == "me" || y.waitWhy == "box";      // box 인데 막은 차가 없으면 앞이 내 차다
                y = y.waitBy;
            }
            return false;
        }

        /// <summary>세게 풀 때 몸통을 무시하는 차: 기다림 사슬에 든 서 있는 차</summary>
        static bool InPass(TrafficCar c, TrafficCar o)
        {
            if (c.passBy == null || o.speed > 0.5f) return false;
            for (int i = 0; i < c.passN; i++) if (c.passSet[i] == o) return true;
            return false;
        }

        /// <summary>고리를 풀며 가는 동안 규칙(들어갈 자리·꼬리물기·양보·붙는 구간)으로 보지 않는 차: 무시하기로 한 차와 서 있는 차(막힌 무리)</summary>
        static bool Ign(TrafficCar c, TrafficCar o) => o == c.passBy || (c.passBy != null && o.speed < 0.5f);

        /// <summary>차로 끝 양보 자리(stopBack)에 3 m/s² 로 설 수 있는지(합류 순서를 바꿀 때)</summary>
        bool CanStop(TrafficCar x, float rest)
        {
            float back = stopBack != null && x.lane < stopBack.Length ? stopBack[x.lane] : 2f;
            return rest - x.length / 2f - back > x.speed * x.speed / 6f;
        }

        /// <summary>고리 풀기 순위(작을수록 먼저, −1 이면 풀 수 없음: 같은 차로·들어갈 차로의 앞차에 막힘)</summary>
        int LoopRank(TrafficCar x)
        {
            var y = x.waitBy;
            if (y.lane == x.lane && y.s > x.s) return -1;
            if (x.planNext >= 0 && y.lane == x.planNext && y.s > x.planS + (x.length + y.length) / 2f + 1f) return -1;
            // 몸이 막히지 않고 규칙으로만 선 차 0, 앞차 보기로 선 차 1, 몸통 겹침 막기에 걸린 차(바로 앞에 몸이 있다) 2 — 몸이 막힌 차는 소프트로는 못 빠진다
            int r = x.guardBy != null ? 2 : (x.waitWhy == "gap" ? 1 : 0);
            return x.passCool > 0f ? r + 3 : r;
        }

        bool LoopBefore(TrafficCar a, TrafficCar b)
        {
            int ra = LoopRank(a), rb = LoopRank(b);
            if (ra != rb) return ra < rb;
            // 차로 끝까지 남은 거리는 1 m 단위로 묶어 비교한다(0.5 m 차이로 비교하면 셋 사이에 순서가 돌아 아무도 뽑히지 않았다 — 양재1동 논현로 세 차로 합류)
            int ea = (int)Math.Floor(LaneLength(a.lane) - a.s), eb = (int)Math.Floor(LaneLength(b.lane) - b.s);
            if (ea != eb) return ea < eb;
            return a.id < b.id;
        }

        /// <summary>시험용: 서로 기다림 고리를 푼 횟수</summary>
        public int LoopBreaks;

        /// <summary>
        /// 사진·시험용: p 에서 dir 쪽 ahead m 앞, 같은 방향 차로 중 가장 오른쪽(정류장 쪽) 차로에 kind 차를 한 대 놓는다.
        /// 그 자리 12 m 안에 다른 차가 있으면 뺀다.
        /// </summary>
        public TrafficCar SpawnKindNear(V2 p, V2 dir, float ahead, int kind)
        {
            var goal = p + dir * ahead;
            int best = -1; float bestS = 0f, bestScore = float.PositiveInfinity;
            var right = new V2(-dir.y, dir.x);
            for (int li = 0; li < road.lanes.Count; li++)
            {
                var c = road.lanes[li].center;
                for (int k = 0; k + 1 < c.Length; k++)
                {
                    var d = (c[k + 1] - c[k]).Normalized;
                    if (d.Dot(dir) < 0.85f) continue;
                    var q = Geom.ClosestPointOnSegment(goal, c[k], c[k + 1]);
                    float dist = q.DistanceTo(goal);
                    if (dist > 12f) continue;
                    float score = dist - (q - goal).Dot(right) * 0.5f;    // 오른쪽 차로를 조금 더 좋게
                    if ((kind == 6 || kind == 7) && HasStops(li)) score -= 6f;   // 버스는 정류장이 붙은 차로(바깥 버스 차로)에
                    if (score < bestScore) { bestScore = score; best = li; bestS = cum[li][k] + q.DistanceTo(c[k]); }
                }
            }
            if (best < 0) return null;
            Sample(best, bestS, out var pos, out var d2, out var z);
            cars.RemoveAll(o => o.pos.DistanceTo(pos) < 12f);
            var car = new TrafficCar { id = ++nextId, lane = best, s = bestS, kind = kind, model = 0, speed = 6f, style = 1, pos = pos, z = z, heading = (float)Math.Atan2(d2.y, d2.x), paint = PaintSeed(best, bestS, 0) };
            car.length = TrafficCar.Kinds[kind].L;
            car.width = TrafficCar.Kinds[kind].W;
            cars.Add(car);
            return car;
        }

        /// <summary>내 차와 겹친 다른 차(없으면 null)</summary>
        public TrafficCar Hit(Vehicle me)
        {
            var cs = me.Corners();
            var cc = me.Center;
            foreach (var c in cars)
            {
                if (c.pos.DistanceTo(cc) > 7f) continue;
                if (Math.Abs(c.z - me.z) > 2.5f) continue;      // 다른 층(고가 위·아래)
                if (!Geom.ConvexOverlap(cs, c.Poly)) continue;
                // 뒤에서 다른 차가 와서 닿은 것은 내 잘못이 아니다: 그 차를 세운다
                if ((c.pos - cc).Dot(me.Dir) < -1f) { c.speed = 0f; continue; }
                return c;
            }
            return null;
        }
    }
}
