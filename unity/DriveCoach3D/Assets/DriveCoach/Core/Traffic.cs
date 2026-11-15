// 담당: S (3D 도로 주행 — 다른 차들. 11/8 제안 "도로 주행의 다른 차" 수락)
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
        readonly Random rnd = new Random(17);
        readonly int want;
        const float Cell = 12f;
        // 차로 고르기 무게(11/8 밤 교통량): 차로 길이 × 길 종류(제한 50 km/h 넘는 큰길 4, 그 밑 1) — 큰길에 차가 많다
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
                var car = new TrafficCar { lane = li, s = s, model = rnd.Next(8), kind = kind, speed = road.LimitOf(ln) / 3.6f * 0.6f, style = r0 < 0.2 ? 1 : (r0 < 0.35 ? 2 : 0) };
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
        /// 버스는 노선(정류장이 붙은 차로)에 많이(4 배), 정류장 없는 차로에는 적게(0.6 배) — 대표 11/9: "버스 통행량을 넣기로 했잖아"
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
                        // (11/14) 옆 차로 위 바로 옆 점만(선분 끝으로 밀린 점이면 앞뒤로 어긋나 옮길 때 차가 1~3 m 튀었다)
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
        /// 앞 범퍼가 1.8 m(큰 차 반폭 + 여유) 떨어지게. 차로 끝이 교차로 한가운데(길 선이 만나는 점)라 끝에 서면 엇갈리는 차와 겹쳤다(11/9)
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
                // 11/9 겹침 시험: 끝에서 가장 가까운 점만 보면 비스듬히(70~80°) 지나는 차로가 내 길을 실제로 가로지르는 자리보다 앞으로 잡혀
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
                        // (11/14) 도로교통법 제25조: 우회전은 도로 오른쪽 가장자리에서, 좌회전은 중앙선 쪽에서 — 오른쪽(왼쪽)에 같은 방향 차로가
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
            for (int ci = cars.Count - 1; ci >= 0; ci--)
            {
                var c = cars[ci];
                var ln = road.lanes[c.lane];
                float limit = road.LimitOf(ln) / 3.6f;
                float target = limit * c.SpeedK;
                Sample(c.lane, c.s, out var p, out var dir);
                var front = p + dir * (c.length / 2f);
                // (11/14) 옆으로 미끄러지는 중(앞지르기·골목 비킴)에는 실제 자리(옆 거리 lat 만큼)에서 앞을 본다 — 차로 가운데선에서만 보면
                //   옛 차로에 선 버스를 3 m 옆으로 보고 그대로 밀고 들어가 겹쳤다(양재2동 논현로 시험)
                var frontAct = Math.Abs(c.lat) > 0.05f ? front + new V2(-dir.y, dir.x) * c.lat : front;
                // 앞에 있는 것(다른 차·내 차): 차 앞 40 m, 옆으로 1.8 m 안
                float gap = float.PositiveInfinity;
                TrafficCar lead = null;
                float restL = LaneLength(c.lane) - c.s;
                foreach (var o in cars)
                {
                    if (o == c) continue;
                    // 같은 차로(또는 다음에 들어갈 차로)의 앞차는 차로를 따라 잰 거리로(11/9 신촌기차역 시험: 급하게 굽은 회차로에서
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
                        float g2 = restL + (o.s - c.planS) - (o.length + c.length) / 2f;
                        if (g2 < 40f && g2 < gap) { gap = g2; lead = o; }
                        continue;
                    }
                    if (Math.Abs(o.z - c.z) > 2.5f) continue;     // 다른 층
                    var rel = o.pos - frontAct;
                    float ahead = rel.Dot(dir);
                    if (ahead < 0f || ahead > 40f) continue;
                    float side = Math.Abs(rel.Dot(new V2(-dir.y, dir.x)));
                    if (side > 1.8f) continue;
                    if (o.speed > 0.5f && new V2((float)Math.Cos(o.heading), (float)Math.Sin(o.heading)).Dot(dir) < 0f) continue;   // 마주 오는 차
                    if (ahead - o.length / 2f < gap) { gap = ahead - o.length / 2f; lead = o; }
                }
                // (11/14 대표: "차량들이 겹치는 상황") 몸통 겹침 막기: 앞으로 갈 길(지금 차로 → 다음 차로, 옆 거리 포함)에 내 몸통 네모(옆 0.1 m 여유)를
                //   1 m 마다 놓아 보고, 다른 차 몸통과 겹치는 첫 자리 앞에서 선다. 위의 "앞차"(가운데가 옆 1.8 m 안)만 보면
                //   교차로 정지선에서 코가 큰길로 나온 차, 옆 차로에서 비스듬히 선 버스 꼬리 같은 것을 못 보고 뚫고 지나갔다.
                //   내 뒤에 있는 차(그 차가 나를 본다)와 맞은편에서 오는 차(골목 비켜 가기·맞은편 차로 규칙이 따로)는 빼고.
                c.leadBy = lead != null && gap < 6f ? lead : null;      // 나를 세운(세울) 앞차 — 서로 기다림 풀기에 쓴다
                c.guardBy = null;
                if (c.speed > -0.1f && c.guardPoly != null)
                {
                    float look = 2.5f + Math.Max(0f, c.speed) * 1.3f;
                    List<TrafficCar> cand = null;
                    foreach (var o in cars)
                    {
                        if (o == c || o.guardPoly == null || Math.Abs(o.z - c.z) > 2.5f) continue;
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
                            if (hit) { gap = Math.Min(gap, sd - 1f); break; }
                        }
                    }
                }
                // 사람처럼 앞지르기(11/9 대표 "좀 더 사람처럼"): 앞차가 많이 느리면(내 제한 속도의 45 % 밑, 20 m 안) 성향에 따라 1.5~5초 뒤
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
                    // 마주 오는 내 차가 가까이(옆 3.2 m 안, 앞 18 m ~ 옆을 지나는 중) 오면 오른쪽으로 0.5 m 비켜 준다(좁은 길 사람 운전 — 11/9 시험: 골목 입구에서 스침)
                    if (me.Dir.Dot(dir) < -0.5f && side < 3.2f && ahead > -me.length - 2f && ahead < 18f) meetMe = true;
                    // 옆 2.8 m 까지(좁은 골목에서 마주 오는 내 차 앞에 선다 — 11/9 시험: 2.2 m 로는 비스듬히 스쳐 부딪힘)
                    // 내 차가 마주 오면 그 속도만큼(1.5초) 더 일찍
                    float closing = me.Dir.Dot(dir) < -0.5f ? Math.Max(0f, me.speed) * 1.5f : 0f;
                    // 서 있다가 출발할 때는 바로 옆을 지나가는 내 차가 다 지나갈 때까지(뒤로 내 차 길이 + 1 m) 기다린다
                    //   (11/9 시험: 골목 입구에 서 있던 차가 옆을 스치며 꺾어 들어오는 내 차 옆구리로 출발)
                    float behind = c.speed < 1f && Math.Abs(me.speed) > 0.5f ? -(me.length + 1f) : -1f;
                    if (ahead > behind && ahead < 40f && side < 2.8f) gap = Math.Min(gap, ahead - me.length / 2f - closing);
                }
                if (!float.IsInfinity(gap))
                {
                    // (11/13 밤) 교차로 안의 짧은 차로에서는 앞차에 바짝(1.8 m) 붙는다 — 4~6 m 띄우고 서면 꼬리가 가로지르는 차로를 덮어 교차로가 막혔다
                    float safe = Math.Max(0f, gap - (LaneLength(c.lane) < 30f ? Math.Min(c.GapM, 1.8f) : c.GapM));
                    target = Math.Min(target, (float)Math.Sqrt(2f * 3f * safe));
                }
                // 좁은 길에서 내 차와 정면으로 마주 서 버리면(둘 다 거의 섬, 12 m 안) 3초 뒤 천천히 물러난다(비켜 줄 수 없는 골목)
                bool headOn = Math.Abs(me.z - c.z) < 2.5f && me.Dir.Dot(dir) < -0.6f && Math.Abs(me.speed) < 1.2f && c.speed < 0.5f
                              && me.Center.DistanceTo(front) < 15f && Math.Abs((me.Center - front).Dot(new V2(-dir.y, dir.x))) < 2.4f;
                c.waitMe = headOn ? c.waitMe + dt : 0f;
                // 바로 앞(2~5 m)에 내 차가 있으면(교차로에서 꺾어 들자마자 마주친 경우 등) 급제동(11/9 시험: 골목 들머리 충돌)
                bool emergency = false;
                if (Math.Abs(me.z - c.z) < 2.5f && me.Center.DistanceTo(front) < 14f)
                {
                    // 내 차 네모까지 거리(가운데만 보면 비스듬히 선 내 차 모서리를 옆에서 들이받았다 — 11/9 시험)
                    float look = 2f + c.speed * 1.2f;
                    float laneLen = LaneLength(c.lane);
                    for (float sd = 0f; sd <= look; sd += 1.5f)
                    {
                        // 차로를 따라 앞(꺾는 중이면 굽은 길 그대로), 차로 끝을 넘으면 곧게
                        float sAt = c.s + c.length / 2f + sd;
                        V2 probe;
                        if (sAt <= laneLen) Sample(c.lane, sAt, out probe, out _);
                        else probe = front + dir * sd;
                        if (Geom.PointInPolygon(probe, myPoly) || Geom.DistPointPoly(probe, myPoly, out _) < c.width / 2f + 0.4f) { target = 0f; emergency = true; break; }
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
                foreach (var sl in road.stopLines)
                {
                    if (sl.signal < 0 || sl.approach.Dot(dir) < 0.8f) continue;
                    if (Math.Abs(c.z) > 2.5f) break;     // 고가·지하차도 위에서는 땅 신호를 보지 않는다
                    var mid = (sl.a + sl.b) / 2f;
                    var rel = mid - front;
                    float ahead = rel.Dot(dir);
                    if (ahead < -0.5f || ahead > 50f) continue;
                    if (Math.Abs(rel.Dot(new V2(-dir.y, dir.x))) > 2.5f) continue;
                    if (ahead > 0.5f) slAhead = Math.Min(slAhead, ahead);
                    var sg = road.signals[sl.signal];
                    var col = c.planTurn < 0 ? sg.LeftAt(w.t) : sg.At(w.t);
                    // 노란불: 서기 편한 거리면 선다(급한 차는 더 가까워도 지나간다)
                    bool stop = col == SignalColor.Red || (col == SignalColor.Yellow && ahead > c.speed * c.speed / (c.style == 2 ? 3f : 6f));
                    if (stop) target = Math.Min(target, (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, ahead - 1.5f)));
                }
                // (11/13 밤, 법 제25조⑤ 꼬리물기 금지) 신호 교차로: 정지선을 넘기 전인데, 내가 들어갈 교차로 안 차로(30 m 안 되는 짧은 차로)에 서 있는 차가 있으면
                //   초록불이라도 정지선에서 기다린다. 앞차를 따라 들어가 교차로 안에 서는 바람에 가로지르는 차들과 고리처럼 막혀 역삼역 교차로가 80초 넘게 멈췄다.
                if (!float.IsInfinity(slAhead) && slAhead < 40f)
                {
                    int boxLane = c.planNext >= 0 ? c.planNext : road.lanes[c.lane].next;
                    if (boxLane >= 0 && LaneLength(boxLane) < 30f)
                        foreach (var o in cars)
                            if (o != c && o.lane == boxLane && o.speed < 0.5f && Math.Abs(o.z - c.z) < 2.5f)
                            { target = Math.Min(target, (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, slAhead - 1.5f))); break; }
                }
                // 횡단보도를 건너는 사람(11/9): 앞 18 m·옆 3 m 안이면 그 3 m 앞에 선다
                if (w.crossPeds != null && Math.Abs(c.z) < 0.5f)
                {
                    float pd = w.crossPeds.Ahead(front, dir, 18f, 3f);
                    if (!float.IsInfinity(pd)) target = Math.Min(target, (float)Math.Sqrt(2f * 3f * Math.Max(0f, pd - 3f)));
                }
                // (11/13 저녁) 꺾이는 곳에서는 줄인다: 다른 차들이 시속 30 km 로 직각을 돌았다(자동 점검 "방향 휙 돎" 수백 번).
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
                // 과속방지턱(11/9): 앞 20 m 안·넘는 동안 20 km/h
                var rgn = road.region;
                if (rgn != null && rgn.humps.Count > 0 && Math.Abs(c.z) < 0.5f && rgn.HumpAhead(front, dir, -c.length, 6f + c.speed * 2f))
                    target = Math.Min(target, 20f / 3.6f);
                // 버스(11/9 아침): 이 차로의 다음 정류장(앞 60 m 안)에 선다 — 앞 범퍼가 표지 1 m 앞, 오른쪽 깜빡이, 10~20초 뒤 왼쪽 깜빡이 켜고 떠남
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
                            target = 0f;
                            c.dwell -= dt;
                            if (c.dwell <= 0f) { c.doneStopLane = c.lane; c.doneStopS = ss; c.blink = -1; c.leaveT = 4f; }
                        }
                        else
                        {
                            target = Math.Min(target, (float)Math.Sqrt(2f * 1.6f * Math.Max(0f, ahead)));
                            // 표지 4 m 앞까지 와서 섰으면(교차로 앞 양보로 조금 일찍 선 경우 등 — 11/9 54차 시험) 거기서 태운다
                            if (ahead < 4f && c.speed < 0.4f) c.dwell = 10f + (float)rnd.NextDouble() * 10f;
                        }
                        break;
                    }
                }
                if (c.leaveT > 0f) { c.leaveT -= dt; if (c.leaveT <= 0f) c.blink = 0; }
                // 11/9 대표: "골목에서 마주치면 서로가 양쪽으로 쭉 붙여 주는" — 좁은 양방향 골목(shared)에서 맞은편 차가 40 m 안에서 오면
                //   둘 다 오른쪽으로 붙이고(길 폭/2 − 차 반폭 − 0.15 m, 최대 1.4 m) 25 m 안에서는 11 km/h 로 비켜 간다.
                //   둘이 못 지나갈 만큼 좁으면(폭 < 두 차 폭 + 0.35 m) 골목에 덜 들어온 쪽이 오른쪽에 붙어 서서 기다린다
                c.latT = meetMe && !ln.shared ? 0.5f : 0f;
                bool facing = false;
                if (ln.shared && ln.pair >= 0 && Math.Abs(c.z) < 0.5f)
                {
                    float pull = Math.Max(0f, Math.Min(1.4f, ln.room / 2f - c.width / 2f - 0.15f));
                    foreach (var o in cars)
                    {
                        if (o.lane != ln.pair) continue;
                        var rel = o.pos - c.pos;
                        float along = rel.Dot(dir);
                        if (along < -1f || along > 40f || Math.Abs(rel.Dot(new V2(-dir.y, dir.x))) > 3.5f) continue;
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
                                // 물러나다 뒤 60 m 안 옆길 입구에 닿으면 그쪽으로 비켜 서서 보낸다(11/9 "사람처럼")
                                if (c.alleyBack)
                                {
                                    var ms = Mouths(c.lane);
                                    // 맞은편 차가 꺾어 들어갈 옆길 입구는 피한다(그 입구에 비켜 서면 그 차가 들이받는다 — 11/9 54차 시험)
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
                float latRate = c.aside != 0f ? 1.2f : 0.8f;
                c.lat += Math.Max(-latRate * dt, Math.Min(latRate * dt, c.latT - c.lat));
                // 꺾기 전 30 m 부터 방향지시등(버스 정류장 깜빡이는 그대로)
                if (c.planNext >= 0 && c.planTurn != 0 && c.dwell <= 0f && c.leaveT <= 0f && LaneLength(c.lane) - c.s < 30f) c.blink = c.planTurn;
                // 차로 끝(꺾을 곳)은 천천히
                float rest = LaneLength(c.lane) - c.s;
                // 11/9 대표("나란히 가도 충돌이 없어야"): 다른 차끼리 겹치지 않게 차로 끝(교차로)에서 양보
                //   ① 들어갈 차로의 들어가는 자리(앞뒤 차 길이 + 3 m)에 다른 차가 있으면 차로 끝 2 m 앞에 선다
                //   ② 내 차로 끝 10 m 안에 엇갈려(30~150°) 오는 차가 나보다 그 자리에 가까우면 선다
                //   서로 기다리다 멈추지 않게 4초 넘게 기다렸으면 간다
                if (rest < 25f && Math.Abs(c.z) < 0.5f)
                {
                    bool yield = false, occ = false, brk = false; c.occBy = null;
                    var lcn = ln.center;
                    var endP = lcn[lcn.Length - 1];
                    float myD = c.pos.DistanceTo(endP);
                    if (c.planNext >= 0)
                    {
                        Sample(c.planNext, Math.Min(c.planS + 2f, LaneLength(c.planNext)), out var entry, out var entryDir);
                        // (11/14) 들어갈 자리: 그 차로 방향으로 놓은 내 차 네모(앞뒤 1 m·옆 0.4 m 여유) — 옆 차로(3 m 남짓 옆)에 선 차는 막지 않는다
                        var entryPoly = Geom.RectPoly(entry, c.length + 2f, c.width + 0.8f, (float)Math.Atan2(entryDir.y, entryDir.x) * Geom.Rad2Deg);
                        // 들어갈 자리에 내 차(플레이어)가 움직이며 있으면 기다린다(서 있는 내 차는 이 차를 기다리는 중일 수 있어 — 둘이 서로 기다려 멈추지 않게)
                        if (Math.Abs(me.z - c.z) < 2.5f && Math.Abs(me.speed) > 1f && (me.Center - c.pos).Dot(dir) > 0f && me.Center.DistanceTo(entry) < (me.length + c.length) / 2f + 3f + c.speed * c.speed / 8f) occ = true;
                        foreach (var o in cars)
                        {
                            if (o == c || Math.Abs(o.z - c.z) > 2.5f) continue;
                            // 들어갈 자리 앞쪽은 내 속도로 설 수 있는 거리(v²/8)만큼 더 비어 있어야
                            // (11/15 오류 27) 들어갈 자리를 막은 차가 나 때문에(내 코가 그 차 앞에 걸쳐) 서 있으면 둘 다 영영 선다 →
                            //   5초 넘게 기다렸으면 내가 먼저 빠져나간다(길이 정말 막혔으면 앞차 보기, 몸통 겹침 막기로 여전히 선다)
                            bool mutual = (o.guardBy == c || o.leadBy == c) && o.speed < 0.3f && c.waitJ > 5f;
                            if (mutual && o.lane == c.planNext && o.s > c.planS - o.length && o.s < c.planS + c.length + 3f) { brk = true; continue; }
                            if (o.lane == c.planNext && o.s > c.planS - o.length && o.s < c.planS + c.length + 3f + c.speed * c.speed / 8f) { occ = true; c.occBy = o; break; }
                            // 들어갈 차로 뒤쪽 20 m 안에서 다가오는 차(차로 중간으로 끼어들 때)
                            //   (11/9 겹침 시험: 50 km/h 차는 20 m 를 1.4초에 온다 — 4초 안에 올 거리까지 본다)
                            if (o.lane == c.planNext && c.planS > 3f && o.s <= c.planS - o.length && o.s > c.planS - Math.Max(20f, o.speed * 4f) && o.speed > 1f) { occ = true; break; }
                            // 옆길 입구로 비켜 서 있는 차(골목 마주침)는 길을 비켜 준 것 — 그 차 때문에 서면 서로 기다려 멈춘다(11/9 54차 시험)
                            if (o.lane != c.lane && o.aside == 0f && o.pos.DistanceTo(entry) < (o.length + c.length) / 2f + 1f && Geom.ConvexOverlap(o.Poly, entryPoly))
                            {
                                // (11/14 역삼역 시연 경로) 그 차도 나 때문에(내가 그 차 들어갈 자리에 있어) 서 있으면 둘 다 영영 선다 →
                                //   5초 넘게 서로 기다렸으면 차로 끝에 더 가까운 쪽(같으면 번호 작은 쪽)이 먼저 간다(바로 앞을 막으면 앞차 보기로 여전히 선다)
                                if (o.occBy == c && c.waitJ > 5f && o.lane >= 0)
                                {
                                    float oRest = LaneLength(o.lane) - o.s;
                                    if (rest < oRest - 0.5f || (Math.Abs(rest - oRest) <= 0.5f && c.model * 31 + c.paint < o.model * 31 + o.paint)) { brk = true; continue; }
                                }
                                occ = true; c.occBy = o; break;
                            }
                            // 좁은 골목(둘이 못 비켜 가는 폭) 입구: 맞은편에서 골목 안을 오는 차가 있으면 들어가지 않고 기다린다(사람 운전 관습)
                            var pn = road.lanes[c.planNext];
                            if (pn.shared && pn.pair >= 0 && o.lane == pn.pair && pn.room < c.width + o.width + 0.35f)
                            {
                                if ((o.guardBy == c || o.leadBy == c) && o.speed < 0.3f && c.waitJ > 5f) { brk = true; continue; }      // 그 차가 나 때문에 못 나오고 있다(오류 27)
                                occ = true; break;
                            }
                            // 다른 차로에서 같은 차로로 들어가려는 차가 나보다 그 끝에 가까우면(합류) 먼저 보낸다
                            if (o.planNext == c.planNext && o.lane != c.lane && o.lane >= 0)
                            {
                                float oRest = LaneLength(o.lane) - o.s;
                                if (oRest < 30f && (oRest < rest - 0.5f || (Math.Abs(oRest - rest) <= 0.5f && o.model * 31 + o.paint < c.model * 31 + c.paint))) { occ = true; break; }
                            }
                        }
                    }
                    if (!yield)
                        foreach (var o in cars)
                        {
                            if (o == c || o.lane == c.lane || Math.Abs(o.z - c.z) > 2.5f || o.speed < 0.3f) continue;
                            float oD = o.pos.DistanceTo(endP);
                            // 14 m 안 + 그 자리로 오는 중이고 4초 안에 닿을 차(사람은 빠르게 오는 차를 멀리서 보고 기다린다 — 11/9 겹침 시험)
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
                            if (give) { yield = true; break; }
                        }
                    // 들어갈 자리가 막힌 것(occ)은 기다림 시간과 관계없이 선다 — 4초 넘게 기다린 뒤 둘이 함께 들어가 겹쳤다
                    if (occ || (yield && c.waitJ < 4f + (c.model % 5)))
                    {
                        float back = stopBack != null && c.lane < stopBack.Length ? stopBack[c.lane] : 2f;
                        target = Math.Min(target, (float)Math.Sqrt(2f * 3f * Math.Max(0f, rest - c.length / 2f - back)));
                        if (c.speed < 0.5f) c.waitJ += dt;
                    }
                    else if (!yield && !occ && !brk) c.waitJ = 0f;     // 서로 기다림을 깨고 가는 중(brk)에는 기다린 시간을 지우지 않는다(지우면 5초마다 한 장씩만 움직였다)
                }
                if (ln.next < 0) target = Math.Min(target, (float)Math.Sqrt(16f / 3.6f * (16f / 3.6f) + 2f * 2f * Math.Max(0f, rest - 4f)));
                c.dbgGap = gap; c.dbgTarget = target;
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
            var car = new TrafficCar { lane = best, s = bestS, kind = kind, model = 0, speed = 6f, style = 1, pos = pos, z = z, heading = (float)Math.Atan2(d2.y, d2.x), paint = PaintSeed(best, bestS, 0) };
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
