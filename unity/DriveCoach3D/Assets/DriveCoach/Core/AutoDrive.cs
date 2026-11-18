// 자동 운전(핸들).
// 차로 가운데선을 따라간다: 지금 차로에서 앞쪽 한 점(속도에 따라 5~12 m)을 겨냥해 핸들을 돌린다(순수 추종). 속도는 AutoSpeed 가 낸다.
// 차로가 끝나면 다음 차로를 고른다: 경로 글자(S 곧장·L 왼쪽·R 오른쪽, 차례로 한 글자씩)가 있으면 그대로, 없거나 다 쓰면 씨앗 난수로(대개 곧장).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public class AutoDrive
    {
        readonly Random rnd;
        readonly string plan;
        int planI;
        int li = -1, nextLi = -2;
        float s, nextS;
        float[] cum;
        float stuckT;
        /// <summary>시험 기록: 지나온 차로 수·다시 잡은 횟수·막다른 곳 횟수</summary>
        public int lanesDone, reacquired, deadEnds, backedUp;
        /// <summary>지도 끝(데이터 없는 옆 동)에서 돌아 나온 횟수</summary>
        public int uTurns;
        /// <summary>돌아 나오는 중: 돌아 들어갈 맞은편 차로(-1 이면 아님)</summary>
        int uLane = -1;
        float uT, uTry;
        /// <summary>부딪혀 서면 잠깐 뒤로 물러난다. 이 동안 부르는 쪽이 기어를 R 로 두고 살짝 밟는다. 끝나면 D 로</summary>
        public bool Reverse => reverseT > 0f;
        float reverseT, biasM, biasLeft, lastBumpAge = 99f;
        /// <summary>다음 차로로 넘어갈 때 꺾는 각(도, + 오른쪽). 방향지시등·신호(좌회전 화살표)·굽은 길 속도에 쓴다</summary>
        float nextTurn;
        float blinkHold; int blink; float redStopT; bool redStopped;
        /// <summary>방향지시등(-1 왼쪽, 0 끔, +1 오른쪽): 꺾기 30 m 앞에서 켜고, 다 꺾고 2초 뒤 끈다</summary>
        public int Blinker => blink;
        /// <summary>시험 기록: 빨간불·노란불에 선 횟수</summary>
        public int signalStops;
        public int Lane => li;
        /// <summary>주행 점검용: 갈림길에서 고른 것을 적는다(없으면 안 적음)</summary>
        public Action<string> Log;

        public AutoDrive(int seed = 1, string plan = "") { rnd = new Random(seed); this.plan = (plan ?? "").ToUpperInvariant(); }

        // ── 정해 둔 경로 따라가기(시연 코스) ─────────────────────────────────────────────
        // 경로 = 차로 가운데선을 이어 만든 점들: 꺾는 곳은 원호, 차로 변경은 S 자. 깜빡이 구간도 경로가 준다.
        V2[] route; float[] routeCum; (float s0, float s1, int dir, bool turn, float at)[] routeBlink;
        public bool HasRoute => route != null;
        /// <summary>경로를 따라 온 거리·전체 길이(m)</summary>
        public float RouteS => s;
        public float RouteLen => routeCum != null ? routeCum[routeCum.Length - 1] : 0f;
        public bool RouteDone { get; private set; }

        public AutoDrive(V2[] routePts, (float, float, int, bool, float)[] blinkZones) : this(1, "")
        {
            route = routePts; routeCum = Cum(routePts); routeBlink = blinkZones ?? new (float, float, int, bool, float)[0];
        }

        /// <summary>경로 JSON({"pts":[dm…],"blink":[[s0,s1,방향]…]})에서</summary>
        public static AutoDrive FromRouteJson(string json)
        {
            var o = MiniJson.ParseObject(json);
            if (o == null || !o.TryGetValue("pts", out var pv) || !(pv is List<object> pl) || pl.Count < 4) return null;
            var pts = new V2[pl.Count / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new V2(Convert.ToSingle(pl[2 * i]) / 10f, Convert.ToSingle(pl[2 * i + 1]) / 10f);
            var bz = new List<(float, float, int, bool, float)>();
            if (o.TryGetValue("blink", out var bv) && bv is List<object> bl)
                foreach (var e in bl) if (e is List<object> q && q.Count >= 3) bz.Add((Convert.ToSingle(q[0]), Convert.ToSingle(q[1]), Convert.ToInt32(q[2]), q.Count < 4 || Convert.ToInt32(q[3]) != 0, q.Count >= 5 ? Convert.ToSingle(q[4]) : Convert.ToSingle(q[0]) + 32f));
            return new AutoDrive(pts, bz.ToArray());
        }

        float RouteSteer(World w, float dt, out bool stop)
        {
            stop = false; var car = w.car;
            s = Project(route, routeCum, car.Center, s - 3f, s + 30f, out float off);
            float len = routeCum[routeCum.Length - 1];
            // 깜빡이·꺾는 방향(좌회전 화살표 신호를 보려면 알아야 한다)
            blink = 0; nextTurn = 0f; nextLi = -2;
            foreach (var z in routeBlink)
            {
                if (s >= z.s0 && s <= z.s1) blink = z.dir;

            }
            if (len - s < 2.5f) { stop = true; RouteDone = true; }
            float ld = Math.Max(4.5f, Math.Min(11f, 3.2f + Math.Abs(car.speed) * 0.9f));
            var target = At(route, routeCum, Math.Min(len, s + ld), out _);
            var v = target - car.pos;
            float L = Math.Max(0.5f, v.Length);
            var d = car.Dir;
            float sinA = (d.x * v.y - d.y * v.x) / L;
            float wheel = (float)Math.Atan2(2f * car.wheelbase * sinA, L) * Geom.Rad2Deg;
            return Geom.Clamp(wheel * car.ratio, -car.maxSteerDeg, car.maxSteerDeg);
        }

        static float[] Cum(V2[] c)
        {
            var a = new float[c.Length];
            for (int i = 1; i < c.Length; i++) a[i] = a[i - 1] + c[i].DistanceTo(c[i - 1]);
            return a;
        }

        static V2 At(V2[] c, float[] cu, float d, out V2 dir)
        {
            d = Math.Max(0f, Math.Min(cu[cu.Length - 1], d));
            int i = 0;
            while (i + 2 < c.Length && cu[i + 1] < d) i++;
            float L = Math.Max(1e-4f, cu[i + 1] - cu[i]);
            dir = (c[i + 1] - c[i]).Normalized;
            return c[i] + (c[i + 1] - c[i]) * ((d - cu[i]) / L);
        }

        /// <summary>점에서 차로까지: 가장 가까운 자리의 길 따라 거리와 떨어진 거리(sMin 뒤만 본다)</summary>
        static float Project(V2[] c, float[] cu, V2 p, float sMin, float sMax, out float dist)
        {
            float best = float.MaxValue, bs = sMin;
            for (int i = 0; i + 1 < c.Length; i++)
            {
                if (cu[i + 1] < sMin || cu[i] > sMax) continue;
                var q = Geom.ClosestPointOnSegment(p, c[i], c[i + 1]);
                float dd = q.DistanceTo(p);
                if (dd < best) { best = dd; bs = cu[i] + q.DistanceTo(c[i]); }
            }
            dist = best;
            return bs;
        }

        void Acquire(RoadDef road, Vehicle car)
        {
            float best = float.MaxValue; int bi = -1; float bs = 0f;
            var p = car.Center; var d = car.Dir;
            for (int k = 0; k < road.lanes.Count; k++)
            {
                var c = road.lanes[k].center;
                if (c == null || c.Length < 2) continue;
                // 빠른 거르기: 차로 첫 점·끝 점이 모두 400 m 밖이면 넘어간다(긴 차로는 아래에서 다시 본다)
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    if (Math.Abs(c[i].x - p.x) > 60f && Math.Abs(c[i + 1].x - p.x) > 60f && (c[i].x - p.x) * (c[i + 1].x - p.x) > 0f) continue;
                    if (Math.Abs(c[i].y - p.y) > 60f && Math.Abs(c[i + 1].y - p.y) > 60f && (c[i].y - p.y) * (c[i + 1].y - p.y) > 0f) continue;
                    var q = Geom.ClosestPointOnSegment(p, c[i], c[i + 1]);
                    float dd = q.DistanceTo(p);
                    var ld = (c[i + 1] - c[i]).Normalized;
                    float al = ld.Dot(d);
                    if (al < 0.2f) continue;
                    float score = dd + (1f - al) * 6f + Math.Abs(road.lanes[k].ZAt(i, 0.5f) - car.z) * 3f;
                    if (score < best)
                    {
                        best = score; bi = k;
                        float acc = 0f; for (int j = 0; j < i; j++) acc += c[j + 1].DistanceTo(c[j]);
                        bs = acc + q.DistanceTo(c[i]);
                    }
                }
            }
            li = bi; s = bs; nextLi = -2;
            cum = bi >= 0 ? Cum(road.lanes[bi].center) : null;
        }

        /// <summary>그 차로 끝에 이어 갈 차로가 있는가(next 또는 끝 9 m 안을 지나는, 15 m 넘게 남은 다른 차로)</summary>
        static bool HasOnward(RoadDef road, int k)
        {
            var ln = road.lanes[k]; if (ln.next >= 0) return true;
            var c = ln.center; var end = c[c.Length - 1]; var dir = (end - c[c.Length - 2]).Normalized;
            for (int m = 0; m < road.lanes.Count; m++)
            {
                if (m == k) continue;
                var oc = road.lanes[m].center; if (oc == null || oc.Length < 2) continue;
                float acc = 0f, hit = -1f; 
                for (int i = 0; i + 1 < oc.Length; i++)
                {
                    float segL = oc[i + 1].DistanceTo(oc[i]);
                    if (hit < 0f && Math.Abs(oc[i].x - end.x) < 40f + segL && Math.Abs(oc[i].y - end.y) < 40f + segL)
                    {
                        var q = Geom.ClosestPointOnSegment(end, oc[i], oc[i + 1]);
                        if (q.DistanceTo(end) <= 9f && (oc[i + 1] - oc[i]).Normalized.Dot(dir) > -0.3f) hit = acc + q.DistanceTo(oc[i]);
                    }
                    acc += segL;
                }
                if (hit >= 0f && acc - hit > 15f) return true;
            }
            return false;
        }

        /// <summary>그 차로를 s 부터 ahead m 가는 동안, 데이터가 없는 옆 동으로 나가는가</summary>
        static bool LeavesRegion(RoadDef road, RegionData rg, int lane, float s, float ahead)
        {
            var oc = road.lanes[lane].center; float acc = 0f;
            for (int i = 0; i + 1 < oc.Length; i++)
            {
                float segL = oc[i + 1].DistanceTo(oc[i]);
                if (acc + segL >= s)
                {
                    var p = oc[i + 1];
                    if (!Geom.PointInPolygon(p, rg.boundary))
                    {
                        var nb = rg.NeighborAt(p);
                        if ((nb == null || nb.file == "") && Geom.DistPointPoly(p, rg.boundary, out _) > RegionData.BorderSlack - 2f) return true;
                    }
                }
                acc += segL;
                if (acc > s + ahead) break;
            }
            return false;
        }

        /// <summary>차로 끝에서 갈 차로: 곧장 이어지는 차로와, 끝 근처(9 m)를 지나는 다른 차로들 가운데 경로 글자(또는 난수)에 맞는 것</summary>
        void Choose(RoadDef road)
        {
            var ln = road.lanes[li]; var c = ln.center;
            var end = c[c.Length - 1]; var dir = (end - c[c.Length - 2]).Normalized;
            float endZ = ln.ZAt(c.Length - 2, 1f);
            var opts = new List<(int lane, float s, float turn)>();      // turn: + 오른쪽(도)
            if (ln.next >= 0 && ln.next < road.lanes.Count)
            {
                var nc = road.lanes[ln.next].center;
                var nd = (nc[1] - nc[0]).Normalized;
                opts.Add((ln.next, 0f, (float)(Math.Atan2(dir.x * nd.y - dir.y * nd.x, dir.Dot(nd)) * Geom.Rad2Deg)));
            }
            for (int k = 0; k < road.lanes.Count; k++)
            {
                if (k == li || k == ln.next) continue;
                var oc = road.lanes[k].center;
                if (oc == null || oc.Length < 2) continue;
                float acc = 0f;
                for (int i = 0; i + 1 < oc.Length; i++)
                {
                    float segL = oc[i + 1].DistanceTo(oc[i]);
                    bool far = (Math.Abs(oc[i].x - end.x) > 12f && Math.Abs(oc[i + 1].x - end.x) > 12f && (oc[i].x - end.x) * (oc[i + 1].x - end.x) > 0f)
                            || (Math.Abs(oc[i].y - end.y) > 12f && Math.Abs(oc[i + 1].y - end.y) > 12f && (oc[i].y - end.y) * (oc[i + 1].y - end.y) > 0f);
                    if (!far)
                    {
                        var q = Geom.ClosestPointOnSegment(end, oc[i], oc[i + 1]);
                        if (q.DistanceTo(end) <= 9f && Math.Abs(road.lanes[k].ZAt(i, 0.5f) - endZ) < 2.5f)
                        {
                            var od = (oc[i + 1] - oc[i]).Normalized;
                            float ss = acc + q.DistanceTo(oc[i]);
                            float total = acc; for (int j = i; j + 1 < oc.Length; j++) total += oc[j + 1].DistanceTo(oc[j]);
                            if (od.Dot(dir) > -0.3f && (total - ss > 15f || (total - ss > 5f && HasOnward(road, k))))      // 짧은 이음 차로(교차로 안 13 m 따위)도 그 끝에 갈 길이 있으면 고른다
                            {
                                opts.Add((k, ss, (float)(Math.Atan2(dir.x * od.y - dir.y * od.x, dir.Dot(od)) * Geom.Rad2Deg)));
                                break;
                            }
                        }
                    }
                    acc += segL;
                }
            }
            nextTurn = 0f;
            if (opts.Count == 0) { nextLi = -1; deadEnds++; return; }
            // 고른 차로가 몇 m 뒤 끝나고 곧장 다음 차로로 이어지면(T 자 골목: 건너편 차로의 마지막 3 m 에 닿음) 그 다음 차로를 바로 고른다.
            //   안 그러면 겨냥점이 그 3 m 끝에 묶여 늦게 꺾고, 맞은편 건물에 부딪혔다(양재2동 강남대로2길).
            for (int oi = 0; oi < opts.Count; oi++)
            {
                var o = opts[oi]; var ol = road.lanes[o.lane];
                if (o.lane == ln.next || ol.next < 0 || ol.next >= road.lanes.Count || ol.next == li) continue;
                var occ = ol.center; float tot = 0f; for (int j = 0; j + 1 < occ.Length; j++) tot += occ[j + 1].DistanceTo(occ[j]);
                if (tot - o.s > 12f) continue;
                var nc2 = road.lanes[ol.next].center; var nd2 = (nc2[1] - nc2[0]).Normalized;
                if (nc2[0].DistanceTo(occ[occ.Length - 1]) > 4f) continue;
                opts[oi] = (ol.next, 0f, (float)(Math.Atan2(dir.x * nd2.y - dir.y * nd2.x, dir.Dot(nd2)) * Geom.Rad2Deg));
            }
            for (int oi = opts.Count - 1; oi > 0; oi--)
                for (int oj = 0; oj < oi; oj++) if (opts[oj].lane == opts[oi].lane) { opts.RemoveAt(oi); break; }
            // 데이터가 없는 옆 동으로 나가는 길은 고르지 않는다(동 경계에서 멈춰 서 버린다). 남는 길이 없으면 그대로 둔다.
            var rg = road.region;
            if (opts.Count > 1 && rg != null && rg.boundary.Length >= 3)
            {
                var keep = new List<(int lane, float s, float turn)>();
                foreach (var o in opts) if (!LeavesRegion(road, rg, o.lane, o.s, 250f)) keep.Add(o);
                if (keep.Count > 0 && keep.Count < opts.Count) opts = keep;
            }
            // 끝이 막힌 차로(지도 끝·막다른 골목)는 다른 길이 있으면 고르지 않는다
            if (opts.Count > 1)
            {
                var keep = new List<(int lane, float s, float turn)>();
                foreach (var o in opts) if (HasOnward(road, o.lane)) keep.Add(o);
                if (keep.Count > 0 && keep.Count < opts.Count) opts = keep;
            }
            // 신호 교차로 안의 짧은 차로(정지선을 이미 지난 뒤)에서는 좌회전을 고르지 않는다:
            //   정지선에서는 "직진"으로 보고 직진 초록불에 들어온 뒤 안에서 왼쪽으로 꺾어 '좌회전 화살표 신호' 위반이 됐다.
            if (opts.Count > 1 && cum != null && cum[cum.Length - 1] < 30f)
            {
                bool sigJ = false;
                foreach (var g in road.signals) if (g.junction.DistanceTo(end) < 45f) { sigJ = true; break; }
                if (sigJ)
                {
                    var keep = new List<(int lane, float s, float turn)>();
                    foreach (var o in opts) if (o.turn > -45f) keep.Add(o);
                    if (keep.Count > 0 && keep.Count < opts.Count) opts = keep;
                }
            }
            char want = planI < plan.Length ? plan[planI] : '?';
            if (opts.Count > 1 && planI < plan.Length) planI++;
            (int lane, float s, float turn) pick = opts[0];
            if (want == 'L' || want == 'R')
            {
                float bestT = 20f; bool found = false;
                foreach (var o in opts)
                {
                    float t = want == 'R' ? o.turn : -o.turn;
                    if (t > bestT) { bestT = t; pick = o; found = true; }
                }
                if (!found) want = 'S';
            }
            if (want == 'S' || (want == '?' && rnd.NextDouble() < 0.7))
            {
                float bestA = float.MaxValue;
                foreach (var o in opts) if (Math.Abs(o.turn) < bestA) { bestA = Math.Abs(o.turn); pick = o; }
            }
            else if (want == '?') pick = opts[rnd.Next(opts.Count)];
            nextLi = pick.lane; nextS = pick.s; nextTurn = pick.turn;
            if (Log != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var o in opts) sb.Append(o.lane == pick.lane ? " [" : " ").Append(o.lane).Append(':').Append(road.lanes[o.lane].name).Append(' ').Append(o.turn.ToString("0")).Append(o.lane == pick.lane ? "°]" : "°");
                Log($"갈림 {li}({ln.name}) 끝 ({end.x:0},{end.y:0}) 글자 {want} →{sb}");
            }
        }

        /// <summary>
        /// 핸들 + 속도를 함께 낸다.
        /// 속도는 AutoSpeed(제한속도·앞차·횡단보도 보행자·과속방지턱·어린이보호구역)에 더해:
        ///   · 신호: 앞 60 m 안 내 차로의 정지선이 빨강·노랑이면 그 1.5 m 앞에 선다(왼쪽으로 꺾을 때는 좌회전 화살표를 본다)
        ///   · 빨간불 우회전: 정지선에서 완전히 섰다가(1.2초) 간다
        ///   · 굽은 길·꺾는 곳: 앞 25 m 의 꺾임에 맞춰 미리 줄인다(옆으로 쏠리는 힘 2 m/s² 안)
        /// </summary>
        public float Drive(World w, float dt, float cruiseKmh, out float accel, out bool brake, out float brakeAmount)
        {
            float steer = Steer(w, dt, out bool stop);
            var car = w.car; var road = w.st.road;
            accel = 0f; brake = false; brakeAmount = 0f;
            if (Reverse) { accel = 0.35f; return steer; }
            if (stop || road == null || (li < 0 && route == null)) { brake = true; brakeAmount = RouteDone ? 0.5f : 1f; return steer; }
            float cap = cruiseKmh / 3.6f;
            if (uLane >= 0) cap = Math.Min(cap, 8f / 3.6f);      // 돌아 나오는 중
            var dir = car.Dir; var front = car.FrontBumperCenter;
            // 굽은 길: 지금 방향과 12 m·25 m 앞 길 방향의 차이
            float turnAhead = Math.Max(HeadingChange(road, 12f, dir), HeadingChange(road, 25f, dir) * 0.6f);
            if (turnAhead > 20f) cap = Math.Min(cap, turnAhead > 55f ? 14f / 3.6f : (turnAhead > 35f ? 20f / 3.6f : 28f / 3.6f));
            // 신호
            float stopAt = float.PositiveInfinity; bool redNow = false;
            if (Math.Abs(car.z) < 2.5f)
                foreach (var sl in road.stopLines)
                {
                    if (sl.signal < 0 || sl.approach.Dot(dir) < 0.8f) continue;
                    var mid = (sl.a + sl.b) / 2f; var rel = mid - front;
                    float ahead = rel.Dot(dir);
                    if (ahead < -0.5f || ahead > 60f) continue;
                    if (Math.Abs(rel.Dot(new V2(-dir.y, dir.x))) > 9f) continue;      // 같은 진입로의 옆 차로 정지선도 본다(정지선이 없는 차로가 있다)
                    var sg = road.signals[sl.signal];
                    bool leftTurn = nextLi >= 0 && nextTurn < -25f, rightTurn = nextLi >= 0 && nextTurn > 25f;
                    if (route != null)
                    {
                        // 경로: 이 정지선의 교차로에서 꺾는가 = 꺾기 시작이 정지선 앞 5 m ~ 뒤 45 m 사이
                        leftTurn = rightTurn = false;
                        foreach (var z in routeBlink)
                            if (z.turn && z.at - s > ahead - 8f && z.at - s < ahead + 45f) { if (z.dir < 0) leftTurn = true; else rightTurn = true; break; }
                    }
                    var col = leftTurn ? sg.LeftAt(w.t) : sg.At(w.t);
                    bool must = col == SignalColor.Red || (col == SignalColor.Yellow && ahead > car.speed * car.speed / 6f);
                    if (must && rightTurn && col == SignalColor.Red)
                    {
                        // 빨간불 우회전: 정지선에서 한 번 완전히 선 뒤 간다
                        if (redStopped) must = false;
                        else if (ahead < 4.0f && Math.Abs(car.speed) < 0.3f) { redStopT += dt; if (redStopT > 1.2f) { redStopped = true; redStopT = 0f; must = false; } }
                    }
                    if (must) { stopAt = Math.Min(stopAt, ahead - 2.6f); redNow = true; }      // 1.5 → 2.6 m: 사진에서 앞 범퍼가 정지선 칠 위에 걸쳐 섰다(멈추며 1 m 남짓 더 나감)
                }
            // 경로 주행: 내가 지나갈 길(꺾는 곳 포함) 위의 횡단보도에 건너는 사람·막 들어서는 사람이 있으면 그 앞 3 m 에 서서 다 건널 때까지 기다린다
            //   (AutoSpeed 는 곧은 앞만 보므로 꺾은 직후 횡단보도의 사람은 여기서 본다)
            //   경로 없이 차로를 따라 달릴 때도 같다: 지금 차로 + 고른 다음 차로를 따라 본다.
            if ((route != null || (li >= 0 && cum != null)) && w.crossPeds != null && w.crossPeds.peds.Count > 0)
            {
                var cp = w.crossPeds; float fo = car.Center.DistanceTo(front);
                V2[] pc = route ?? road.lanes[li].center; float[] pcum = route != null ? routeCum : cum;
                float len = pcum[pcum.Length - 1];
                V2[] nc = null; float[] ncum = null;
                if (route == null && nextLi >= 0) { if (pedNextLi != nextLi) { pedNextLi = nextLi; pedNextCum = Cum(road.lanes[nextLi].center); } nc = road.lanes[nextLi].center; ncum = pedNextCum; }
                float pedAt = float.PositiveInfinity;
                foreach (var p in cp.peds)
                {
                    if (p.done || !(p.onRoad || (p.t < 0.1f && p.wait <= 0.6f))) continue;
                    var cr = cp.crossings[p.cross];
                    if (cr.c.DistanceTo(front) > cr.half + 34f) continue;
                    var a = cr.c - cr.n * cr.half; var b = cr.c + cr.n * cr.half;
                    for (float d = 0f; d <= 30f; d += 0.75f)
                    {
                        V2 q;
                        if (s + fo + d <= len) q = At(pc, pcum, s + fo + d, out _);
                        else if (nc != null && nextS + (s + fo + d - len) <= ncum[ncum.Length - 1]) q = At(nc, ncum, nextS + (s + fo + d - len), out _);
                        else break;
                        if (Geom.ClosestPointOnSegment(q, a, b).DistanceTo(q) < 2.0f) { pedAt = Math.Min(pedAt, d - 2.0f); break; }
                    }
                }
                // 이미 횡단보도에 들어섰으면(앞 범퍼가 선을 넘음) 멈추지 않고 빠져나간다
                if (pedAt > -1.0f && !float.IsInfinity(pedAt))
                {
                    float stopP = pedAt - 3f;
                    // (법 제27조① "횡단보도 앞(정지선이 있는 곳에서는 그 정지선)") 횡단보도 앞 14 m 안에 칠해진 정지선이 내 길 위에 있으면 그 1.5 m 앞에 선다.
                    //   사진에서 횡단보도 3 m 앞에 서느라 정지선을 반쯤 넘어 서 있었다. 이미 정지선을 지났으면 그대로(횡단보도 3 m 앞).
                    var rgp = road.region;
                    if (rgp != null && rgp.stopPaint.Count > 0)
                    {
                        float paintD = float.NegativeInfinity, paintBest = 1.6f;
                        for (float d = Math.Max(0f, pedAt - 14f); d <= pedAt - 0.5f; d += 0.5f)
                        {
                            V2 q;
                            if (s + fo + d <= len) q = At(pc, pcum, s + fo + d, out _);
                            else if (nc != null && nextS + (s + fo + d - len) <= ncum[ncum.Length - 1]) q = At(nc, ncum, nextS + (s + fo + d - len), out _);
                            else break;
                            foreach (var m in rgp.stopPaint)
                                if (Math.Abs(m.x - q.x) < 1.6f && Math.Abs(m.y - q.y) < 1.6f && m.DistanceTo(q) < paintBest) { paintBest = m.DistanceTo(q); paintD = d; }
                        }
                        if (paintD - 1.5f > -0.3f) stopP = Math.Min(stopP, paintD - 1.5f);
                    }
                    cap = Math.Min(cap, (float)Math.Sqrt(2f * 2.2f * Math.Max(0f, stopP)));
                    if (stopP < 0.6f) { Why = $"ped-wait pedAt={pedAt:0.0}"; pedStops += wasPed ? 0 : 1; wasPed = true; brake = true; brakeAmount = Math.Abs(car.speed) > 1.5f ? 0.7f : 0.45f; return steer; }
                }
                else wasPed = false;
            }
            else wasPed = false;
            if (redNow)
            {
                float vStop = (float)Math.Sqrt(2f * 2.2f * Math.Max(0f, stopAt));
                cap = Math.Min(cap, vStop);
                if (stopAt < 0.6f)
                {
                    if (!wasRed) signalStops++;
                    Why = $"red-wait stopAt={stopAt:0.0}"; wasRed = true; brake = true; brakeAmount = Math.Abs(car.speed) > 1.5f ? 0.7f : 0.45f; return steer;
                }
            }
            else wasRed = false;
            // 시연 경로 안전장치: 신호·보행자 때문이 아닌데 앞에 선 차에 막혀 15초 넘게 못 가면 앞에 서 있는 다른 차를 치운다.
            //   큰 교차로에서 다른 차들이 서로 기다리다 멈추면(역삼역, 드물게) 시연이 그 뒤에서 오래 선다.
            Why = $"red={(redNow ? 1 : 0)} stopAt={(float.IsInfinity(stopAt) ? 99f : stopAt):0} ped={(wasPed ? 1 : 0)} follow={(w.traffic != null ? Math.Min(99f, AutoSpeed.FollowLimit(w) * 3.6f) : -1f):0} cap={cap * 3.6f:0} blk={blockedT:0} cleared={clearedAhead}";
            // 걸리는 조건: 앞 45 m 안의 빨간불·보행자 대기가 아닌데 15초 넘게 서 있음. 치우는 범위: 앞 18 m·옆 8 m(가로로 선 버스).
            // 경로 없는 자유 자동 운전에도 건다: 내 차를 기다리는 다른 차와 서로 마주 서서(내 차는 앞차를, 그 차는 내 차를 기다림) 영영 설 수 있다
            if (!NoClear && w.traffic != null && !(redNow && stopAt < 45f) && !wasPed && Math.Abs(car.speed) < 0.2f && !RouteDone && uLane < 0)
            {
                blockedT += dt; blockedAll += dt;
                if (blockedT > 15f)
                {
                    int n = 0;
                    for (int i = w.traffic.cars.Count - 1; i >= 0; i--)
                    {
                        var tc = w.traffic.cars[i]; var rel = tc.pos - front; float ah = rel.Dot(dir);
                        if (tc.speed < 0.3f && ah > -2f && ah < 18f && Math.Abs(rel.x * dir.y - rel.y * dir.x) < 8f && Math.Abs(tc.z - car.z) < 2.5f
                            && (route != null || WaitsForMe(tc, blockedAll))) { w.traffic.cars.RemoveAt(i); n++; }
                    }
                    clearedAhead += n; blockedT = 8f;
                    Log?.Invoke($"막힘 풀기: 앞에 선 차 {n}대 치움 ({front.x:0},{front.y:0})");
                }
            }
            else { blockedT = 0f; blockedAll = 0f; }
            w.autoSpeed = true;
            var o = AutoSpeed.Control(w, Math.Max(1f, cap * 3.6f));
            accel = o.accel; brake = o.brake; brakeAmount = o.brakeAmount;
            // AutoSpeed 는 가장 느려도 10 km/h 로 간다 → 신호 앞에서 그보다 느려야 하면 직접 줄인다
            if (cap < AutoSpeed.MinKmh / 3.6f && car.speed > cap + 0.2f)
            {
                accel = 0f; brake = true;
                brakeAmount = Geom.Clamp((2.2f + 1.5f * (car.speed - cap)) / (car.model.maxBrake > 0f ? car.model.maxBrake : 5f), 0.25f, 0.8f);
            }
            else if (cap < AutoSpeed.MinKmh / 3.6f) accel = Math.Min(accel, 0.05f);
            return steer;
        }
        bool wasRed, wasPed;
        int pedNextLi = -9; float[] pedNextCum;
        /// <summary>횡단보도의 사람을 기다리느라 선 횟수</summary>
        public int pedStops;
        /// <summary>시연 경로에서 막힘을 풀려고 치운 다른 차 수</summary>
        public int clearedAhead;
        /// <summary>주행 점검용: 지금 속도를 정한 까닭(빨간불·보행자·앞차·막힘)</summary>
        public string Why = "";
        /// <summary>주행 점검용: 막힘 풀기를 끈다(다른 차끼리 막히는지 그대로 보려고)</summary>
        public static bool NoClear;
        float blockedT, blockedAll;

        /// <summary>
        /// 경로 없는 자유 자동 운전에서 치워도 되는 차: 기다림 사슬(waitBy)이 내 차에서 끝나는 차(서로 마주 서 기다림)만.
        /// 빨간불·보행자·버스 정류장 줄(사슬 끝이 신호·보행자·버스)은 치우지 않는다. 정지선이 45 m 넘게 먼 신호 줄 뒤에서 15초마다 앞차가 사라졌다.
        /// 다른 차끼리 얽힌 고리(사슬이 16대를 넘거나 돈다)는 Traffic 의 고리 풀기가 45~90초 안에 푸므로 120초 넘게 막혔을 때만.
        /// </summary>
        static bool WaitsForMe(TrafficCar tc, float blocked)
        {
            var x = tc;
            for (int n = 0; n < 16 && x.waitBy != null; n++) { x = x.waitBy; if (x == tc) break; }
            if (x.waitBy != null) return blocked > 120f;      // 고리·긴 사슬
            return x.waitWhy == "me" || (x.waitWhy == "box" && x.speed < 0.3f);      // 내 차 때문에 섬(막은 차 없는 box = 앞이 내 차라 붙는 구간에 못 들어감)
        }

        /// <summary>내 길을 따라 ahead m 앞의 길 방향이 지금 차 방향과 이루는 각(도)</summary>
        float HeadingChange(RoadDef road, float ahead, V2 dir)
        {
            if (route != null) { At(route, routeCum, Math.Min(routeCum[routeCum.Length - 1], s + ahead), out var rd); return (float)(Math.Acos(Geom.Clamp(rd.Dot(dir), -1f, 1f)) * Geom.Rad2Deg); }
            if (li < 0 || cum == null) return 0f;
            var c = road.lanes[li].center; float len = cum[cum.Length - 1];
            V2 d;
            if (s + ahead <= len) At(c, cum, s + ahead, out d);
            else if (nextLi >= 0) { var nc = road.lanes[nextLi].center; At(nc, Cum(nc), nextS + (s + ahead - len), out d); }
            else At(c, cum, len, out d);
            return (float)(Math.Acos(Geom.Clamp(d.Dot(dir), -1f, 1f)) * Geom.Rad2Deg);
        }

        /// <summary>
        /// 데이터가 없는 옆 동 쪽으로 경계 밖 14 m 넘게 나와 바깥으로 가고 있는가. 22 m(+4 m)를 넘으면 World 가 차를 세워
        /// 자동 운전은 그 전에 맞은편 차로로 돌아 나온다(지도 끝에서 영영 서지 않게).
        /// </summary>
        static bool NearEdgeOut(RoadDef road, Vehicle car)
        {
            var rg = road.region;
            if (rg == null || rg.boundary.Length < 3) return false;
            var c = car.Center;
            if (Geom.PointInPolygon(c, rg.boundary)) return false;
            float dist = Geom.DistPointPoly(c, rg.boundary, out var edge);
            if (dist < RegionData.BorderSlack - 8f) return false;
            var nb = rg.NeighborAt(c);
            // 옆 동 데이터가 있으면 넘어가 그 지도로 바뀐다(GameMain). 바뀌지 않고 지도 그림 끝(여유 폭 - 60 m)까지 오면 그때는 돌아 나온다
            if (nb != null && nb.file != "" && dist < rg.margin - 60f) return false;
            return car.Dir.Dot((c - edge).Normalized) > 0.2f;
        }

        /// <summary>
        /// 돌아 나올 차로: 차 둘레 25 m 안의 거꾸로 가는(차 방향과 120° 넘게) 차로 가운데 가깝고, 그 길로 30 m 가면 지도 안쪽으로 다가가는 것
        /// (막다른 길이면 안쪽 조건 없이, 좁은 골목의 맞은편 차로도)
        /// </summary>
        void StartUTurn(RoadDef road, Vehicle car, bool deadEnd = false)
        {
            var p = car.Center; var d = car.Dir; var rg = road.region;
            bool hasB = rg != null && rg.boundary.Length >= 3;      // 동네 지도가 아니면(연습 코스) 막다른 길에서만 쓴다
            if (!hasB && !deadEnd) return;
            float dNow = !hasB || Geom.PointInPolygon(p, rg.boundary) ? 0f : Geom.DistPointPoly(p, rg.boundary, out _);
            int best = -1; float bestScore = float.MaxValue, bestS = 0f;
            for (int k = 0; k < road.lanes.Count; k++)
            {
                var c = road.lanes[k].center;
                if (c == null || c.Length < 2 || (road.lanes[k].shared && !deadEnd)) continue;
                float acc = 0f;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    float segL = c[i + 1].DistanceTo(c[i]);
                    if (Math.Abs(c[i].x - p.x) < 25f + segL && Math.Abs(c[i].y - p.y) < 25f + segL)
                    {
                        var q = Geom.ClosestPointOnSegment(p, c[i], c[i + 1]);
                        float dd = q.DistanceTo(p);
                        var ld = (c[i + 1] - c[i]).Normalized;
                        if (dd < 25f && ld.Dot(d) < -0.5f && Math.Abs(road.lanes[k].ZAt(i, 0.5f) - car.z) < 2.5f)
                        {
                            float sq = acc + q.DistanceTo(c[i]);
                            var cu = Cum(c);
                            if (cu[cu.Length - 1] - sq > 30f)
                            {
                                var ahead = At(c, cu, sq + 30f, out _);
                                float dA = !hasB || Geom.PointInPolygon(ahead, rg.boundary) ? 0f : Geom.DistPointPoly(ahead, rg.boundary, out _);
                                if ((deadEnd || dA < dNow - 5f) && dd < bestScore) { bestScore = dd; best = k; bestS = sq; }
                            }
                        }
                    }
                    acc += segL;
                }
            }
            uTry = 3f;      // 못 찾으면 3초 뒤 다시(차로를 다 훑으므로 매 장 하지 않는다)
            if (best < 0) return;
            uLane = best; uT = 0f; uTurns++;
            li = best; cum = Cum(road.lanes[best].center); s = bestS; nextLi = -2; biasLeft = 0f;
            Log?.Invoke($"{(deadEnd ? "막다른 길" : "지도 끝")}: 맞은편 차로 {best}({road.lanes[best].name})로 돌아 나옴 ({p.x:0},{p.y:0})");
        }

        /// <summary>돌아 나오기: 왼쪽 끝까지 감고 천천히 돈다(닿으면 오른쪽으로 감고 물러났다 다시). 차로 방향과 25° 안으로 맞으면 보통 주행으로(60초 넘으면 그만)</summary>
        float UTurnSteer(RoadDef road, Vehicle car, float dt, out bool stop)
        {
            stop = false;
            uT += dt;
            var c = road.lanes[uLane].center;
            s = Project(c, cum, car.Center, Math.Max(0f, s - 10f), s + 30f, out float off);
            var target = At(c, cum, Math.Min(cum[cum.Length - 1], s + 8f), out var ld);
            if ((ld.Dot(car.Dir) > 0.9f && off < 2.5f) || uT > 60f) { uLane = -1; if (uT > 60f) li = -1; return 0f; }
            if (w0Touching && car.gear == Gear.D) { reverseT = 1.5f; backedUp++; return car.maxSteerDeg; }
            // 차 방향이 차로 방향과 60° 안으로 들어오면 겨냥점을 따라간다(그 전에는 왼쪽 끝까지)
            if (ld.Dot(car.Dir) > 0.5f)
            {
                var v = target - car.pos; float L = Math.Max(0.5f, v.Length); var dd = car.Dir;
                float sinA = (dd.x * v.y - dd.y * v.x) / L;
                float wheel = (float)Math.Atan2(2f * car.wheelbase * sinA, L) * Geom.Rad2Deg;
                return Geom.Clamp(wheel * car.ratio, -car.maxSteerDeg, car.maxSteerDeg);
            }
            return -car.maxSteerDeg;
        }
        bool w0Touching;

        /// <summary>이번 프레임의 핸들 각(도, + 오른쪽). stop = 갈 곳이 없어 서야 함</summary>
        public float Steer(World w, float dt, out bool stop)
        {
            stop = false;
            var road = w.st.road; var car = w.car;
            if (road == null || road.lanes.Count == 0) { stop = true; return 0f; }
            if (route != null) return RouteSteer(w, dt, out stop);
            lastBumpAge += dt;
            w0Touching = w.Touching && w.BumpDir > 0;
            if (reverseT > 0f)
            {
                reverseT -= dt;
                if (w.Touching && w.BumpDir < 0) reverseT = 0f;      // 물러나다 뒤가 닿았다: 더 물러나지 않고 바로 앞으로 간다
                if (reverseT <= 0f && uLane < 0) { li = -1; biasLeft = 22f; }      // 다시 갈 때 22 m 동안 옆으로 비켜 간다
                return uLane >= 0 ? car.maxSteerDeg : 0f;      // 돌아 나오는 중이면 오른쪽 끝까지 감고 물러난다(세 번 꺾어 돌기)
            }
            if (w.Touching && car.gear == Gear.D && w.BumpDir > 0)      // 뒤가 닿은 상태(BumpDir < 0)에서는 다시 물러나지 않는다
            {
                // 닿았다: 1.8초 물러났다가 옆으로 비켜 다시 간다. 처음에는 왼쪽(세운 차는 대개 오른쪽 가), 12초 안에 또 닿으면 반대쪽으로
                biasM = lastBumpAge < 12f && biasM < 0f ? 1.3f : -1.3f;
                lastBumpAge = 0f; reverseT = 1.8f; backedUp++;
                return 0f;
            }
            if (uTry > 0f) uTry -= dt;
            if (uLane < 0 && li >= 0 && uTry <= 0f && NearEdgeOut(road, car)) StartUTurn(road, car);
            if (uLane >= 0) return UTurnSteer(road, car, dt, out stop);
            if (li < 0 || cum == null) { Acquire(road, car); if (li < 0) { stop = true; return 0f; } }
            var c = road.lanes[li].center;
            s = Project(c, cum, car.Center, s - 3f, s + 30f, out float off);
            if (off > 14f) { reacquired++; Acquire(road, car); if (li < 0) { stop = true; return 0f; } c = road.lanes[li].center; }
            float len = cum[cum.Length - 1];
            if (nextLi == -2 && len - s < 45f) Choose(road);      // 미리 골라 둔다(방향지시등 30 m 앞)
            if (nextLi >= 0 && Math.Abs(nextTurn) > 25f && len - s < 32f) { blink = nextTurn > 0f ? 1 : -1; blinkHold = 2f; }
            else if (blinkHold > 0f) { blinkHold -= dt; if (blinkHold <= 0f) blink = 0; }
            float ld = Math.Max(5f, Math.Min(12f, 3.5f + Math.Abs(car.speed) * 1.0f));
            V2 target;
            float want = s + ld;
            if (want <= len) target = At(c, cum, want, out _);
            else
            {
                if (nextLi == -2) Choose(road);
                if (nextLi < 0)
                {
                    target = c[c.Length - 1];
                    // 막다른 길: 끝 12 m 앞에서 맞은편 차로로 돌아 나온다(없으면 선다)
                    if (len - s < 12f && Math.Abs(car.speed) < 3f && uTry <= 0f) { StartUTurn(road, car, true); if (uLane >= 0) return UTurnSteer(road, car, dt, out stop); }
                    if (len - s < 3f) stop = true;
                }
                else
                {
                    var nc = road.lanes[nextLi].center; var ncu = Cum(nc);
                    target = At(nc, ncu, nextS + (want - len), out _);
                    // 넘어가기: 이 차로 끝을 지났거나, 다음 차로에 충분히 가까워졌으면
                    float ns = Project(nc, ncu, car.Center, nextS - 2f, nextS + 25f, out float noff);
                    if (len - s < 0.6f || (noff < 1.2f && ns > nextS + 1f))
                    {
                        li = nextLi; cum = ncu; s = Math.Max(nextS, ns); nextLi = -2; lanesDone++; redStopped = false;
                    }
                }
            }
            if (biasLeft > 0f)
            {
                biasLeft -= Math.Abs(car.speed) * dt;
                var fd = car.Dir; target += new V2(-fd.y, fd.x) * (biasM * Math.Min(1f, biasLeft / 6f));
            }
            var v = target - car.pos;                          // 뒤 차축에서 겨냥점까지
            float L = Math.Max(0.5f, v.Length);
            var d = car.Dir;
            float sinA = (d.x * v.y - d.y * v.x) / L;         // + 오른쪽
            float wheel = (float)Math.Atan2(2f * car.wheelbase * sinA, L) * Geom.Rad2Deg;
            float steer = wheel * car.ratio;
            // 막힘 살피기: 가야 하는데 8초 넘게 서 있으면 차로를 다시 잡는다
            if (Math.Abs(car.speed) < 0.2f && !stop) { stuckT += dt; if (stuckT > 8f) { stuckT = 0f; reacquired++; li = -1; } } else stuckT = 0f;
            return Geom.Clamp(steer, -car.maxSteerDeg, car.maxSteerDeg);
        }
    }
}
