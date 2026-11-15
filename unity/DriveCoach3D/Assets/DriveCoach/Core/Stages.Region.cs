// Stages 의 동네 도로 주행 판 만들기(11/15 코드 정리로 Region.cs 에서 나눈 파일).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public static partial class Stages
    {
        /// <summary>신호 시계 기본(하루 중 분) — 게임 설정의 시간(아침 08:00·낮 14:00·해질녘 18:30·밤 23:00)</summary>
        public static readonly int[] SignalClocks = { 8 * 60, 14 * 60, 18 * 60 + 30, 23 * 60 };
        public static int SignalClockDefault = 8 * 60;
        /// <summary>차로 하나 폭(m): 큰길 3.3, 동네길 3.0, 골목·서비스 2.75</summary>
        public static float LaneWidth(string hw)
        {
            if (hw.StartsWith("trunk") || hw.StartsWith("primary") || hw.StartsWith("secondary")) return 3.3f;
            if (hw.StartsWith("tertiary")) return 3.1f;
            if (hw == "service" || hw == "living_street") return 2.75f;
            return 3.0f;
        }

        public const float SignalRadius = 16f;   // 신호 교차로 반지름(이만큼 앞에 정지선)

        /// <summary>
        /// 지역 데이터 → 도로 주행 판. id 는 400번대(지역).
        /// 시작: 건물이 가장 많이 모인 곳 근처의 동네길 차로.
        /// </summary>
        /// <summary>start: 시작 자리(가장 가까운 차로 위에 놓는다), headingDeg: 바라볼 방향 힌트(동 사이 이동 — 달리던 방향을 잇는다). 없으면 동네 한가운데</summary>
        public static Stage Region(RegionData d, int id, V2 start, float headingDeg = float.NaN)
        {
            var s = Region(d, id);
            PlaceAt(s, s.road, start, headingDeg);
            return s;
        }

        /// <summary>(11/15) 주차장 칸처럼 정확한 자리·방향에서 시작(차로에 맞추지 않는다)</summary>
        public static Stage RegionExact(RegionData d, int id, V2 c, float headingDeg)
        {
            var s = Region(d, id);
            s.startC = c;
            s.startH = headingDeg;
            return s;
        }

        /// <summary>p 에서 가장 가까운 차로 위(고가·지하가 아닌 곳)에 차를 놓는다. headingDeg 가 있으면 그 방향과 맞는 차로를 고른다</summary>
        static void PlaceAt(Stage s, RoadDef road, V2 p, float headingDeg)
        {
            float bestScore = float.MaxValue; V2 bp = V2.Zero, bd = V2.Zero; bool found = false;
            bool hint = !float.IsNaN(headingDeg);
            var want = hint ? new V2((float)Math.Cos(headingDeg * Math.PI / 180.0), (float)Math.Sin(headingDeg * Math.PI / 180.0)) : V2.Zero;
            foreach (var ln in road.lanes)
            {
                var c = ln.center;
                if (c == null || c.Length < 2) continue;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    float segLen = c[i].DistanceTo(c[i + 1]);
                    if (segLen < 6f) continue;
                    if (ln.z != null && (Math.Abs(ln.z[i]) > 0.05f || Math.Abs(ln.z[Math.Min(i + 1, ln.z.Length - 1)]) > 0.05f)) continue;
                    var q = Geom.ClosestPointOnSegment(p, c[i], c[i + 1]);
                    var dir = (c[i + 1] - c[i]).Normalized;
                    float score = q.DistanceTo(p) + (hint ? (1f - dir.Dot(want)) * 25f : 0f);
                    if (score < bestScore) { bestScore = score; bp = q; bd = dir; found = true; }
                }
            }
            if (!found) return;
            s.startC = bp;
            s.startH = (float)(Math.Atan2(bd.y, bd.x) * 180.0 / Math.PI);
        }

        public static Stage Region(RegionData d, int id = 400)
        {
            var s = MakeStage(id, "내 동네(" + d.name + ")", d.name + " 도로를 달려 보십시오(베타). 신호, 차로, 제한 속도를 지키고, 끝내려면 서서 P");
            s.mode = StageMode.Road;
            var road = new RoadDef { speedLimitKmh = 50f, region = d, trafficCount = 30 };
            // 1) 길 → 띠·차로
            foreach (var r in d.roads)
            {
                // (11/13) 지하차도 안(tunnel 이고 높이가 있는 길)도 차로·높이는 넣는다 — 안 넣으면 입구에서 차로가 끊기고 차가 땅 위로 튀어 오른다.
                //   땅 위 띠(strips: 인도·바닥 꾸밈)만 뺀다. 높이 없는 터널(산 밑 터널·굴다리)은 전처럼 뺀다
                bool under = false;
                if (r.tunnel)
                {
                    if (r.z != null) foreach (var zz in r.z) if (zz < -0.6f) { under = true; break; }
                    if (!under) continue;
                }
                float lw = LaneWidth(r.hw);
                int n = Math.Max(1, r.lanes);
                int fwd, bwd;
                if (r.oneway) { fwd = n; bwd = 0; }
                else if (n == 1) { fwd = 1; bwd = 1; lw = r.width > 0f ? r.width / 2f : Math.Min(lw, 2.75f); }   // 좁은 양방향 골목
                else { fwd = (n + 1) / 2; bwd = n / 2; }
                float median = !r.oneway && n >= 4 ? 0.5f : 0f;
                float width = (fwd + bwd) * lw + median;
                if (!under) road.strips.Add(new RoadStrip
                {
                    pts = r.pts, width = width, hw = r.hw, twoWay = bwd > 0, fwd = fwd, bwd = bwd, laneWidth = lw,
                    sidewalk = r.hw != "service" && !r.hw.EndsWith("_link") && !r.bridge,
                    visL = r.wl > 0f ? r.wl : width / 2f, visR = r.wr > 0f ? r.wr : width / 2f,
                });
                int lane0 = road.lanes.Count;
                if (r.z != null) AddGrade(road, r, Math.Max(r.wl > 0f ? r.wl : width / 2f, r.wr > 0f ? r.wr : width / 2f));
                if (r.laneR != null && r.laneR.Length > 0)
                {
                    // 11/8 밤: 실제 칠한 차로 가운데로(다른 차·자동 경로가 차선 위가 아니라 차로 가운데로 다니게)
                    AddMeasuredLanes(road, r, r.laneR, false, lw);
                    if (!r.oneway && r.laneL != null) AddMeasuredLanes(road, r, r.laneL, true, lw);
                    SetLaneZ(road, r, lane0);
                    // (11/13) 좌우 따로 준 도랑(tl·tr): 그 바닥 밖의 차로는 내려가지 않고 땅 높이에 남는다(가운데 두 차로만 지하차도로 들어가는 넓은 길)
                    if (r.twl > 0f || r.twr > 0f)
                        for (int i = 0; i < r.laneR.Length && lane0 + i < road.lanes.Count; i++)
                            if (r.laneR[i] < -r.twl - 0.2f || r.laneR[i] > r.twr + 0.2f) road.lanes[lane0 + i].z = null;
                    continue;
                }
                if (!r.oneway && n == 1)
                {
                    // 중앙선 없는 골목: 폭 4.6 m 넘으면 두 방향을 가운데에서 1.3 m 씩(마주 오는 두 차가 겹치지 않게, 11/9 대표 "나란히 가도 충돌이 없어야"),
                    //   좁으면 가운데로 같이 쓴다(shared) — 다른 차는 마주치면 서로 오른쪽으로 붙여 비켜 간다(11/9 대표 "골목에서 마주치면 서로 양쪽으로 쭉 붙여 주는")
                    bool split = width >= 4.6f;
                    float off = split ? Math.Min(1.35f, width / 4f) : 0f;
                    int iF = road.lanes.Count;
                    road.lanes.Add(new Lane { center = split ? Offset(r.pts, off) : (V2[])r.pts.Clone(), width = split ? width / 2f : width, name = r.name, speedLimitKmh = Math.Min(r.maxspeed, 30), shared = !split, pair = iF + 1, room = width });
                    var back = split ? Offset(r.pts, -off) : (V2[])r.pts.Clone();
                    Array.Reverse(back);
                    road.lanes.Add(new Lane { center = back, width = split ? width / 2f : width, name = r.name, speedLimitKmh = Math.Min(r.maxspeed, 30), shared = !split, pair = iF, room = width });
                    SetLaneZ(road, r, lane0);
                    continue;
                }
                // (11/13) 일방통행(잰 차로 없음): 길 가운데선이 곧 차도 한가운데 — 차로를 가운데선 양쪽으로 나눈다(전에는 모두 오른쪽으로 밀려 차로 절반이 길 밖이었다)
                float one = bwd == 0 ? -fwd * lw / 2f : median / 2f;
                for (int i = 0; i < fwd; i++)
                    road.lanes.Add(new Lane { center = Offset(r.pts, one + (i + 0.5f) * lw), width = lw, name = r.name, speedLimitKmh = r.maxspeed });
                for (int i = 0; i < bwd; i++)
                {
                    var c = Offset(r.pts, -(median / 2f + (i + 0.5f) * lw));
                    Array.Reverse(c);
                    road.lanes.Add(new Lane { center = c, width = lw, name = r.name, speedLimitKmh = r.maxspeed });
                }
                SetLaneZ(road, r, lane0);
            }
            LinkLanes(road.lanes);
            // 2) 신호: 15 m 안의 신호 점을 한 교차로로 묶고, 들어오는 차로마다 정지선
            var clusters = new List<List<V2>>();
            foreach (var p in d.signals)
            {
                List<V2> hit = null;
                foreach (var c in clusters) if (Mean(c).DistanceTo(p) < 25f) { hit = c; break; }
                if (hit == null) clusters.Add(new List<V2> { p }); else hit.Add(p);
            }
            foreach (var cl in clusters) AddSignalJunction(road, Mean(cl), d);
            road.SetSignalClock(SignalClockDefault);
            // 3) 건물
            foreach (var b in d.buildings) road.buildings.Add(b);
            // 4) 바닥: 데이터 전체를 덮는 사각형(밖으로 나가면 실패)
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var r in d.roads) foreach (var p in r.pts) { x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); }
            var f = MakeFloor(d.name, new Rect(x0 - 30f, y0 - 30f, (x1 - x0) + 60f, (y1 - y0) + 60f));
            f.surface = Surface.Block;      // 동네 땅: 무광 회색(StageBuilder.SurfaceMat)
            s.floors.Add(f);
            s.road = road;
            s.target = new Target();
            PickStart(s, road, d);
            return s;
        }

        /// <summary>
        /// 입체 교차 길의 차로 높이: 차로 가운데 점은 길 꼭짓점과 같은 수(Offset) — 거꾸로 만든 차로(역방향)는 높이도 거꾸로.
        /// 첫 점이 길 첫 점 쪽인지로 정한다.
        /// </summary>
        static void SetLaneZ(RoadDef road, RegionRoad r, int from)
        {
            for (int i = from; i < road.lanes.Count; i++) road.lanes[i].postedKmh = r.posted;      // 확인된 제한속도(없으면 0)
            if (r.z == null) return;
            for (int i = from; i < road.lanes.Count; i++)
            {
                var ln = road.lanes[i];
                if (ln.center.Length != r.z.Length) continue;
                var z = (float[])r.z.Clone();
                if (ln.center[0].DistanceTo(r.pts[0]) > ln.center[0].DistanceTo(r.pts[r.pts.Length - 1])) Array.Reverse(z);
                ln.z = z;
            }
        }

        /// <summary>입체 교차 길 조각을 높이 찾기 목록에(RoadDef.HeightAt)</summary>
        static void AddGrade(RoadDef road, RegionRoad r, float half)
        {
            for (int i = 0; i + 1 < r.pts.Length; i++)
            {
                if (Math.Abs(r.z[i]) < 0.05f && Math.Abs(r.z[i + 1]) < 0.05f) continue;
                // (11/12 밤) 좌우 따로 준 도랑(twl·twr): 그 바닥 위에서만 내려간다 — 가운데선을 바닥 한가운데로 옮겨 넣는다(옆 차로는 땅 높이 그대로)
                if ((r.twl > 0f || r.twr > 0f) && (r.z[i] < 0f || r.z[i + 1] < 0f))
                {
                    var dd = (r.pts[i + 1] - r.pts[i]).Normalized; var nr = new V2(-dd.y, dd.x);      // 오른쪽
                    float sh = (r.twr - r.twl) / 2f, hf = (r.twr + r.twl) / 2f;
                    road.AddGradeSeg(r.pts[i] + nr * sh, r.pts[i + 1] + nr * sh, r.z[i], r.z[i + 1], hf + 0.4f);
                    continue;
                }
                road.AddGradeSeg(r.pts[i], r.pts[i + 1], r.z[i], r.z[i + 1], half + 0.4f);
            }
        }

        static void AddMeasuredLanes(RoadDef road, RegionRoad r, float[] offs, bool back, float lw)
        {
            for (int i = 0; i < offs.Length; i++)
            {
                float w = lw;
                if (offs.Length > 1)
                {
                    float g = float.MaxValue;
                    if (i > 0) g = Math.Min(g, Math.Abs(offs[i] - offs[i - 1]));
                    if (i + 1 < offs.Length) g = Math.Min(g, Math.Abs(offs[i + 1] - offs[i]));
                    w = Math.Max(2.4f, Math.Min(4.2f, g));
                }
                var c = Offset(r.pts, offs[i], r.laneShift);
                if (back) Array.Reverse(c);
                road.lanes.Add(new Lane { center = c, width = w, name = r.name, speedLimitKmh = r.maxspeed });
            }
        }

        static V2 Mean(List<V2> ps)
        {
            var m = V2.Zero;
            foreach (var p in ps) m += p;
            return m / Math.Max(1, ps.Count);
        }

        /// <summary>선을 오른쪽(+)·왼쪽(−)으로 off 만큼 옮긴다(2D 화면 좌표: y 아래, 진행 방향 오른쪽 = (-dy, dx)).</summary>
        public static V2[] Offset(V2[] pts, float off, float[] shift = null)
        {
            var o = new V2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var dPrev = i > 0 ? (pts[i] - pts[i - 1]).Normalized : V2.Zero;
                var dNext = i + 1 < pts.Length ? (pts[i + 1] - pts[i]).Normalized : V2.Zero;
                var d = (dPrev + dNext).Normalized;
                if (d.Length < 0.5f) d = dNext.Length > 0.5f ? dNext : dPrev;
                var right = new V2(-d.y, d.x);
                // 꺾인 곳은 안쪽이 겹치지 않게 길이 보정(최대 2배)
                float cosHalf = dPrev.Length > 0.5f && dNext.Length > 0.5f ? Math.Max(0.5f, (dPrev.Dot(dNext) + 1f) / 2f) : 1f;
                float k = 1f / (float)Math.Sqrt(cosHalf);
                o[i] = pts[i] + right * ((off + (shift != null && i < shift.Length ? shift[i] : 0f)) * k);
            }
            return o;
        }

        /// <summary>차로 끝 근처(4 m)에서 같은 쪽으로 이어지는 차로(30도 안)를 "다음"으로 잇는다(자동 속도가 길이 끝난 줄 알고 줄이지 않게).</summary>
        static void LinkLanes(List<Lane> lanes)
        {
            var starts = new Dictionary<long, List<int>>();
            long Key(V2 p) => ((long)Math.Floor(p.x / 8f) << 32) ^ (uint)(int)Math.Floor(p.y / 8f);
            for (int i = 0; i < lanes.Count; i++)
            {
                var k = Key(lanes[i].center[0]);
                if (!starts.TryGetValue(k, out var l)) starts[k] = l = new List<int>();
                l.Add(i);
            }
            for (int i = 0; i < lanes.Count; i++)
            {
                var c = lanes[i].center;
                var end = c[c.Length - 1];
                var dir = (end - c[c.Length - 2]).Normalized;
                int best = -1;
                float bestScore = float.MaxValue;
                for (int gx = -1; gx <= 1; gx++)
                    for (int gy = -1; gy <= 1; gy++)
                    {
                        long k = ((long)(Math.Floor(end.x / 8f) + gx) << 32) ^ (uint)(int)(Math.Floor(end.y / 8f) + gy);
                        if (!starts.TryGetValue(k, out var cand)) continue;
                        foreach (int j in cand)
                        {
                            if (j == i) continue;
                            var o = lanes[j].center;
                            float dist = o[0].DistanceTo(end);
                            if (dist > 4f) continue;
                            float al = (o[1] - o[0]).Normalized.Dot(dir);
                            if (al < 0.87f) continue;
                            float score = dist + (1f - al) * 10f;
                            if (score < bestScore) { bestScore = score; best = j; }
                        }
                    }
                lanes[i].next = best;
            }
        }

        /// <summary>신호 교차로 하나: 가로·세로 두 갈래 신호(반 주기 엇갈림), 반지름 안으로 들어오는 차로마다 정지선</summary>
        static void AddSignalJunction(RoadDef road, V2 c, RegionData d = null)
        {
            V2 axisA;
            var seen = new List<(V2 p, V2 dir, Lane lane)>();
            foreach (var ln in road.lanes)
            {
                var pts = ln.center;
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    float da = pts[i].DistanceTo(c), db = pts[i + 1].DistanceTo(c);
                    if (!(da > SignalRadius && db <= SignalRadius)) continue;
                    if (Math.Abs(ln.ZAt(i, 0.5f)) > 1.5f) continue;    // 교차로 밑(지하차도)·위(고가)로 지나는 차로는 이 신호와 상관없다
                    // 반지름 원과 만나는 곳
                    var p = CircleHit(pts[i], pts[i + 1], c, SignalRadius);
                    var dir = (pts[i + 1] - pts[i]).Normalized;
                    seen.Add((p, dir, ln));
                    break;
                }
            }
            if (seen.Count == 0) return;
            axisA = seen[0].dir;
            // 신호 시간(11/9 아침, 대표: "신호등도 리얼하게 실제랑 똑같이 작동해야겠지(빨간불 1분 30초면 똑같이)"):
            //   이 교차로의 서울 교차로 번호에 경찰청 운영계획("sp")이 있으면 그 시간대 주기·현시, 없으면 교차로 크기로 정한 값(SignalPlan).
            //   A 갈래 = 들어오는 차로가 많은 갈래(운영계획 1 현시가 보통 큰 길)
            int nA = 0, nB = 0;
            foreach (var e in seen) { if (Math.Abs(e.dir.Dot(axisA)) > 0.7f) nA++; else nB++; }
            if (nB > nA)
            {
                foreach (var e in seen) if (Math.Abs(e.dir.Dot(axisA)) <= 0.7f) { axisA = e.dir; break; }
                int t = nA; nA = nB; nB = t;
            }
            var plan = new SignalPlan { nA = nA, nB = nB };
            float cyc0 = nA + nB <= 4 ? 90f : (nA + nB <= 10 ? 120f : 160f);
            plan.defOffset = (float)(((c.x * 0.37 + c.y * 0.53) % cyc0 + cyc0) % cyc0);   // 교차로마다 신호가 같은 때 바뀌지 않게
            if (d != null)
            {
                int bj = -1;
                float bd = 35f;
                for (int j = 0; j < d.junctions.Count; j++)
                {
                    var jj = d.junctions[j];
                    if (!jj.sig || !d.junctionIds.ContainsKey(j)) continue;
                    float dd = jj.p.DistanceTo(c);
                    if (dd < bd) { bd = dd; bj = j; }
                }
                if (bj >= 0)
                {
                    var id = d.junctionIds[bj];
                    plan.no = id.no; plan.name = id.name;
                    if (d.signalPlans.TryGetValue(id.no, out var sl)) plan.slots = sl;
                }
            }
            // 같은 쪽에서 들어오는 차로들(방향 20도 안)을 한 신호로: 기둥은 가장 오른쪽 차로의 오른쪽 길가
            var groups = new List<List<(V2 p, V2 dir, Lane lane)>>();
            foreach (var e in seen)
            {
                List<(V2 p, V2 dir, Lane lane)> g = null;
                foreach (var gg in groups) if (gg[0].dir.Dot(e.dir) > 0.94f && gg[0].p.DistanceTo(e.p) < 15f) { g = gg; break; }
                if (g == null) groups.Add(new List<(V2 p, V2 dir, Lane lane)> { e }); else g.Add(e);
            }
            foreach (var g in groups)
            {
                var dir = g[0].dir;
                var right = new V2(-dir.y, dir.x);
                bool groupA = Math.Abs(dir.Dot(axisA)) > 0.7f;   // 가로·세로 두 갈래가 반 주기씩 번갈아
                (V2 p, V2 dir, Lane lane) edge = g[0];
                foreach (var e in g) if ((e.p - c).Dot(right) > (edge.p - c).Dot(right)) edge = e;
                var pole = edge.p + right * (edge.lane.width / 2f + 1.2f) + dir * 1.0f;
                int si = road.signals.Count;
                // 한 주기: A 직진 → 노랑 → (A 좌회전 화살표 → 노랑) → 전적색 → B 직진 → 노랑 → (B 화살표 → 노랑) → 전적색.
                //   B 신호의 주기 처음은 A 의 직진·화살표·전적색이 끝난 뒤, 이 진입로 앞 횡단보도는 다른 갈래 직진 초록 동안 보행 초록(SignalPlan.Apply)
                road.signals.Add(new TrafficSignal
                {
                    pos = pole, facingDeg = (float)(Math.Atan2(-dir.y, -dir.x) * 180.0 / Math.PI),
                    junction = c, plan = plan, groupA = groupA,
                });
                foreach (var (p0, d0, ln) in g)
                {
                    // (11/13) 정지선 자리: 교차로 가운데에서 16 m 원이 아니라, 그 앞 횡단보도의 2.5 m 앞(실제 정지선 자리).
                    //   큰 교차로(폭 40 m)에서는 16 m 원이 교차로 한가운데라, 차들이 횡단보도를 지나 교차로 안에 들어가 섰다
                    var p = p0; var d2 = d0;
                    // (11/13 저녁) 칠해진 정지선이 이 차로 위에 있으면 그 자리가 먼저다(빨간불에 선 차가 칠해진 정지선을 밟고 있었다)
                    if (d != null && StopAtPaint(ln, d, ref p, ref d2)) { }
                    else if (d != null && d.crosswalks.Count > 0) StopBeforeCrosswalk(ln, d, ref p, ref d2);
                    var r2 = new V2(-d2.y, d2.x);
                    road.stopLines.Add(new StopLine { a = p - r2 * (ln.width / 2f), b = p + r2 * (ln.width / 2f), approach = d2, signal = si });
                }
            }
            road.signalPoles.Add(c);
        }

        /// <summary>이 차로 위(가운데선에서 0.9 m 안)에 칠해진 정지선 가운데, p 에서 길 따라 뒤로 45 m ~ 앞으로 12 m 사이에 있고 교차로에 가장 가까운 것으로 정지선 자리를 옮긴다</summary>
        static bool StopAtPaint(Lane ln, RegionData d, ref V2 p, ref V2 dir)
        {
            if (d.stopPaint.Count == 0) return false;
            var c = ln.center; if (c == null || c.Length < 2) return false;
            // p 의 길 따라 거리
            float sP = 0f, acc = 0f, bestD = float.MaxValue;
            for (int i = 0; i + 1 < c.Length; i++)
            {
                var q = Geom.ClosestPointOnSegment(p, c[i], c[i + 1]); float dd = q.DistanceTo(p);
                if (dd < bestD) { bestD = dd; sP = acc + q.DistanceTo(c[i]); }
                acc += c[i].DistanceTo(c[i + 1]);
            }
            float bestS = float.NegativeInfinity; V2 bp = p, bdir = dir;
            foreach (var m in d.stopPaint)
            {
                if (Math.Abs(m.x - p.x) > 60f || Math.Abs(m.y - p.y) > 60f) continue;
                float a2 = 0f;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    float L = c[i].DistanceTo(c[i + 1]);
                    var q = Geom.ClosestPointOnSegment(m, c[i], c[i + 1]);
                    if (q.DistanceTo(m) < 0.9f)
                    {
                        float sM = a2 + q.DistanceTo(c[i]);
                        if (sM - sP >= -45f && sM - sP <= 12f && sM > bestS) { bestS = sM; bp = q; bdir = (c[i + 1] - c[i]).Normalized; }
                    }
                    a2 += L;
                }
            }
            if (float.IsNegativeInfinity(bestS)) return false;
            p = bp - bdir * 0.3f; dir = bdir;       // 띠의 앞 가장자리
            return true;
        }

        /// <summary>차로를 p 에서 거꾸로 45 m 까지 되짚어, 이 차로가 가로지르는 횡단보도가 있으면 그 2.5 m 앞으로 정지선 자리를 옮긴다</summary>
        static void StopBeforeCrosswalk(Lane ln, RegionData d, ref V2 p, ref V2 dir)
        {
            var c = ln.center;
            // p 가 놓인 조각
            int seg = 0; float bestD = float.MaxValue;
            for (int i = 0; i + 1 < c.Length; i++) { float dd = Geom.ClosestPointOnSegment(p, c[i], c[i + 1]).DistanceTo(p); if (dd < bestD) { bestD = dd; seg = i; } }
            float back = 0f, lastIn = -1f; V2 q = p; int k = seg; V2 inPt = p, inDir = dir;
            while (back < 45f && k >= 0)
            {
                var a = c[k]; var segDir = (c[k + 1] - a).Normalized;
                float room = q.DistanceTo(a);
                float step = Math.Min(1f, room);
                if (room < 0.05f) { k--; if (k >= 0) q = c[k + 1]; continue; }
                q = q - segDir * step; back += step;
                foreach (var (xc, xn, xl, xw) in d.crosswalks)
                {
                    if (Math.Abs(xn.Dot(segDir)) > 0.5f) continue;       // 내 길을 가로지르는 횡단보도만
                    var rel = q - xc;
                    if (Math.Abs(rel.Dot(xn)) <= xl / 2f + 1f && Math.Abs(rel.x * -xn.y + rel.y * xn.x) <= xw / 2f + 0.3f) { lastIn = back; inPt = q; inDir = segDir; break; }
                }
                if (lastIn >= 0f && back - lastIn > 6f) break;       // 횡단보도를 다 지나 6 m 더 봤다
            }
            if (lastIn < 0f) return;
            p = inPt - inDir * 2.5f; dir = inDir;
        }

        static V2 CircleHit(V2 a, V2 b, V2 c, float r)
        {
            // a 는 밖, b 는 안: 이분법
            V2 lo = a, hi = b;
            for (int k = 0; k < 20; k++)
            {
                var m = (lo + hi) / 2f;
                if (m.DistanceTo(c) > r) lo = m; else hi = m;
            }
            return (lo + hi) / 2f;
        }

        /// <summary>시작 자리: 동 안, 건물이 가장 많이 모인 곳(반지름 120 m) 근처의 동네길·2차로 차로 위, 차로 방향</summary>
        static void PickStart(Stage s, RoadDef road, RegionData d)
        {
            Lane best = null;
            int bestI = 0;
            float bestScore = float.MinValue;
            foreach (var ln in road.lanes)
            {
                var c = ln.center;
                if (ln.speedLimitKmh > 50 || c.Length < 2) continue;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    float segLen = c[i].DistanceTo(c[i + 1]);
                    if (segLen < 30f) continue;
                    var p = c[i] + (c[i + 1] - c[i]).Normalized * 8f;
                    if (d.boundary.Length >= 3 && !Geom.PointInPolygon(p, d.boundary)) continue;
                    int near = 0;
                    foreach (var b in d.buildings) if (b.center.DistanceTo(p) < 120f) near++;
                    float score = near + segLen * 0.05f;
                    if (score > bestScore) { bestScore = score; best = ln; bestI = i; }
                }
            }
            if (best == null && road.lanes.Count > 0) { best = road.lanes[0]; bestI = 0; }
            if (best == null) return;
            var a = best.center[bestI];
            var dir = (best.center[bestI + 1] - a).Normalized;
            s.startC = a + dir * 8f;
            s.startH = (float)(Math.Atan2(dir.y, dir.x) * 180.0 / Math.PI);
        }
    }
}
