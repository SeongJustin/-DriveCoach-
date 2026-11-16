// 3D 서킷·도로 주행 — 자동 속도
// 실제 차는 P 에 둔 채로 하므로 액셀 페달 값을 모르는 경우가 많다. 그래서 기본은 "핸들만 돌리기":
// 차가 정한 속도(느긋하게 25 · 보통 35 · 빠르게 45 km/h)로 달리고, 코너·모퉁이 앞에서는 스스로 미리 줄인다.
// 사람의 브레이크(실제 페달·화면 버튼)는 언제나 이긴다 — 밟으면 서고, 떼면 다시 간다. 도로에서는 제한 속도를 넘지 않는다.
using System;

namespace DriveCoach.Core
{
    public static class AutoSpeed
    {
        public static readonly string[] LevelNames = { "느림", "보통", "빠름" };
        public static readonly float[] LevelKmh = { 25f, 35f, 45f };
        /// <summary>코너에서 옆으로 받는 가속도 한계(m/s²) — 탄 사람이 편안한 정도</summary>
        public const float LatAcc = 2.2f;
        /// <summary>코너 앞에서 미리 줄이는 정도(m/s²) — 액셀을 떼고 살짝 브레이크</summary>
        public const float PlanDecel = 1.5f;
        public const float MinKmh = 10f;
        /// <summary>도로 모퉁이·T자 끝에서 꺾는 속도</summary>
        public const float TurnKmh = 15f;
        /// <summary>주차장 안(차로에 들어서기 전) 속도</summary>
        public const float LotKmh = 8f;

        public struct Output { public float accel; public bool brake; public float brakeAmount; public float target; }

        /// <summary>이번 프레임의 액셀·자동 브레이크. D 가 아니면(R·N·P) 아무것도 하지 않는다(후진은 사람 몫, 크리프만).</summary>
        public static Output Control(World w, float cruiseKmh)
        {
            var o = new Output();
            var car = w.car;
            if (car.gear != Gear.D) return o;
            float target = Target(w, cruiseKmh / 3.6f);
            o.target = target;
            var m = car.model;
            o.accel = Geom.Clamp((target - m.creepD) / Math.Max(0.1f, m.accelExtra), 0f, 1f);
            float over = car.speed - target;
            if (target < 0.2f)
            {
                // 서야 할 때(앞차·스쿨존 횡단보도): 브레이크를 밟고 있는다 — 안 그러면 D 의 크리프(약 5 km/h)로 밀려 나간다
                o.accel = 0f; o.brake = true;
                o.brakeAmount = Geom.Clamp((PlanDecel + 1.5f * Math.Max(0f, over)) / (m.maxBrake > 0f ? m.maxBrake : 5f), 0.3f, 0.8f);
                return o;
            }
            if (over > 0.3f)
            {
                // 목표보다 빠르면 브레이크: 미리 줄이는 정도(PlanDecel)에 넘친 만큼 더한다(최대 단단히 밟은 정도의 80%)
                o.accel = 0f;
                o.brake = true;
                float maxB = m.maxBrake > 0f ? m.maxBrake : 5f;
                o.brakeAmount = Geom.Clamp((PlanDecel + 1.5f * over) / maxB, 0.1f, 0.8f);
            }
            return o;
        }

        /// <summary>지금 낼 속도(m/s): 정한 속도, 서킷 코너, 도로 제한 속도·모퉁이 중 가장 느린 것</summary>
        public static float Target(World w, float cruise)
        {
            float t = cruise;
            float v = Math.Max(0f, w.car.speed);
            w.autoWhy = "";
            if (w.circuitRun != null && w.st.circuit != null) t = Math.Min(t, CircuitLimit(w.st.circuit, w.circuitRun.nearest, v));
            if (w.roadRun != null)
            {
                t = Math.Min(t, ((w.roadRun.limitKmh > 0f ? w.roadRun.limitKmh : w.roadRun.flowKmh) - 3f) / 3.6f);      // 제한속도를 모르는 길은 흐름 속도로
                t = Math.Min(t, RoadLimit(w));
                // (주차장 칸에서 D 로 브레이크를 떼면 '보통' 35 km/h 로 내달아 2~3초 만에 통로 건너편 차에 부딪혔다)
                //   차로에 들어서기 전(주차장 안·나오는 중)에는 주차장 속도로만 — 스티어링 휠을 돌릴 틈을 준다
                if (w.roadRun.NotJoined && t > LotKmh / 3.6f) { t = LotKmh / 3.6f; w.autoWhy = "주차장 안: 천천히"; }
            }
            float follow = w.traffic != null ? FollowLimit(w) : float.PositiveInfinity;
            t = Math.Min(t, follow);
            if (follow < 0.5f) w.autoWhy = "앞차나 옆 차를 기다리는 중";
            // 횡단보도를 건너는 사람: 앞 30 m·옆 6 m 안이면 그 3.5 m 앞에 선다
            float pedStop = float.PositiveInfinity;
            if (w.crossPeds != null)
            {
                float d = w.crossPeds.Ahead(w.car.FrontBumperCenter, w.car.Dir, 30f, 6f);
                if (!float.IsInfinity(d))
                {
                    float stopD = d - 3.5f;
                    // (법 제27조①) 그 사람 앞 16 m 안에 정지선이 칠해져 있으면 그 앞에 선다(2.4 m 앞을 겨냥 — 멈추며 1 m 남짓 더 나간다. 이미 지났으면 그대로)
                    var rgs = w.st.road?.region;
                    if (rgs != null && rgs.stopPaint.Count > 0)
                    {
                        float pd = rgs.StopPaintAhead(w.car.FrontBumperCenter, w.car.Dir, Math.Max(0.9f, d - 16f), d - 0.5f, 1.6f);
                        if (!float.IsNegativeInfinity(pd)) stopD = Math.Min(stopD, pd - 2.4f);
                    }
                    pedStop = (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, stopD)); t = Math.Min(t, pedStop);
                    if (pedStop < 0.5f) w.autoWhy = "건너는 사람을 기다리는 중";
                }
            }
            // 어린이보호구역 신호 없는 횡단보도: 그 4 m 앞에서 한 번 선다(RoadRun 이 멈춘 걸 보면 다시 간다)
            if (w.roadRun != null && w.roadRun.zoneStopDist < 25f)
            {
                float zd = w.roadRun.zoneStopDist, stopZ = zd - 4f;
                // 정지선이 칠해져 있으면 그 앞(2.4 m 앞을 겨냥, 사진에서 정지선 위에 걸쳐 섰다)
                var rgz = w.st.road?.region;
                if (rgz != null && rgz.stopPaint.Count > 0)
                {
                    float pd = rgz.StopPaintAhead(w.car.FrontBumperCenter, w.car.Dir, Math.Max(0.9f, zd - 13f), zd - 0.5f, 1.6f);
                    if (!float.IsNegativeInfinity(pd)) stopZ = Math.Min(stopZ, pd - 2.4f);
                }
                t = Math.Min(t, (float)Math.Sqrt(2f * 2f * Math.Max(0f, stopZ)));
                if (stopZ < 1f) w.autoWhy = "어린이 보호구역 횡단보도 앞 일시정지";
            }
            // 과속방지턱: 앞 25 m 안에 있으면 20 km/h 로(넘는 동안도)
            var rg = w.st.road?.region;
            if (rg != null && rg.humps.Count > 0 && rg.HumpAhead(w.car.FrontBumperCenter, w.car.Dir, -3f, Math.Max(8f, v * 2.5f))) t = Math.Min(t, 20f / 3.6f);
            if (w.roadRun != null && w.roadRun.zoneStopDist < 25f) return t;     // 서야 하는 곳: 최소 속도를 두지 않는다
            if (follow < MinKmh / 3.6f || pedStop < MinKmh / 3.6f) return t;                                // 앞차(서 있거나 마주 오는 차) 앞에서는 선다(최소 속도로 밀고 들어가던 것)
            float floor = w.roadRun != null && w.roadRun.NotJoined ? Math.Min(MinKmh, LotKmh) / 3.6f : MinKmh / 3.6f;   // 주차장 안은 주차장 속도가 바닥
            return Math.Max(t, floor);
        }

        /// <summary>앞차 따라가기: 내 앞 50 m·옆 1.8 m 안의 같은 쪽으로 가는 차와 5 m 넘게 떨어지게(편안한 감속 3 m/s²)</summary>
        public static float FollowLimit(World w)
        {
            var me = w.car;
            var dir = me.Dir;
            var right = new V2(-dir.y, dir.x);
            var front = me.FrontBumperCenter;
            float gap = float.PositiveInfinity;
            foreach (var c in w.traffic.cars)
            {
                var rel = c.pos - front;
                float ahead = rel.Dot(dir);
                var cd = new V2((float)Math.Cos(c.heading), (float)Math.Sin(c.heading));
                if (cd.Dot(dir) < 0.3f && cd.Dot(dir) >= -0.7f && ahead > -1f && ahead < 30f && Math.Abs(rel.Dot(right)) < 25f && Math.Abs(me.z - c.z) < 2.5f)
                {
                    // 가로지르는 차(신호 없는 교차로에서 옆에서 들어오는 차를 들이받음): 2.4초 안에 3.2 m 안으로 만날 것 같으면 그 앞에 선다
                    float v = Math.Max(me.speed, 2f);
                    // 서 있는 차(내 차를 기다리는 중일 수 있다 — 둘이 서로 기다려 멈춤)는 내가 갈 길(차 반폭 + 0.5 m)에 그 차 네모가 걸릴 때만
                    bool moving = c.speed > 0.5f;
                    bool conflict = moving && rel.Length < 9f;            // 바로 앞을 지나는 중이면 다 지날 때까지
                    if (!moving)
                    {
                        // 서 있는 차가 내 앞 범퍼 바로 앞(차 반폭 + 0.8 m)이면 — 꺾어 들어가며 옆구리로 다가갈 때(곧은 앞길로는 못 봄)
                        var fd = front - c.pos;
                        float fx = Math.Max(0f, Math.Abs(fd.Dot(cd)) - c.length / 2f), fy = Math.Max(0f, Math.Abs(fd.Dot(new V2(-cd.y, cd.x))) - c.width / 2f);
                        if (Math.Sqrt(fx * fx + fy * fy) < me.width / 2f + 0.8f) conflict = true;
                    }
                    for (float tt = moving ? 0.4f : 0f; tt <= 3.2f && !conflict; tt += 0.4f)
                    {
                        var mine = me.Center + dir * (v * tt);
                        if (moving)
                        {
                            var theirs = c.pos + cd * (c.speed * tt);
                            if (mine.DistanceTo(theirs) < 4.2f) conflict = true;
                        }
                        else
                        {
                            var dd = mine - c.pos;
                            float lx = Math.Max(0f, Math.Abs(dd.Dot(cd)) - c.length / 2f), ly = Math.Max(0f, Math.Abs(dd.Dot(new V2(-cd.y, cd.x))) - c.width / 2f);
                            if (Math.Sqrt(lx * lx + ly * ly) < me.width / 2f + 0.5f) conflict = true;
                        }
                    }
                    if (conflict) gap = Math.Min(gap, Math.Max(0f, ahead - 2f));
                    continue;
                }
                // 서 있는 맞은편 차가 10 m 안에서 옆 2.4 m 안이면(꺾어 들어가며 그 차 옆구리로 다가갈 때) 그 앞에 선다
                bool stoppedNear = c.speed < 0.5f && ahead < 10f && Math.Abs(rel.Dot(right)) < 2.4f && cd.Dot(dir) < -0.7f;
                if (ahead < -1f || ahead > 50f || (Math.Abs(rel.Dot(right)) > 1.8f && !stoppedNear)) continue;
                if (cd.Dot(dir) < -0.7f)
                {
                    // 좁은 길에서 정면으로 마주 오는 차(옆 2.4 m 안 — 비켜 갈 수 없음): 그 차가 오는 만큼 더 일찍 선다(골목 정면 충돌)
                    if (Math.Abs(rel.Dot(right)) < 2.4f) gap = Math.Min(gap, ahead - c.length / 2f - c.speed * 1.5f);
                    continue;
                }
                if (cd.Dot(dir) < 0.3f) continue;
                gap = Math.Min(gap, ahead - c.length / 2f);
            }
            if (float.IsInfinity(gap)) return float.PositiveInfinity;
            return (float)Math.Sqrt(2f * 3f * Math.Max(0f, gap - 5f));
        }

        /// <summary>코너 속도 vc 로 dist 앞에서 맞추려면 지금 낼 수 있는 속도</summary>
        static float Allowed(float vc, float dist) => (float)Math.Sqrt(vc * vc + 2f * PlanDecel * Math.Max(0f, dist));

        public static float CornerSpeed(float curvature) => curvature < 1e-4f ? float.PositiveInfinity : (float)Math.Sqrt(LatAcc / curvature);

        /// <summary>서킷: 앞쪽(멈출 수 있는 거리 + 15 m) 코너들의 곡률을 보고</summary>
        public static float CircuitLimit(CircuitDef d, int i0, float v)
        {
            int n = d.center.Length;
            float look = v * v / (2f * PlanDecel) + 15f;
            float best = float.PositiveInfinity, s = 0f;
            for (int k = 0; k < n && s <= look; k++)
            {
                int a = (i0 + k) % n;
                if (k > 0) s += d.center[a].DistanceTo(d.center[(a - 1 + n) % n]);
                float vc = CornerSpeed(d.curvature[a]);
                if (float.IsInfinity(vc)) continue;
                best = Math.Min(best, Allowed(vc, s - 2f - v * 0.5f));   // 0.5초 늦게 반응해도 맞게
            }
            return best;
        }

        /// <summary>
        /// 도로: 진행 방향이 맞는 가장 가까운 차로의 끝(고리 모퉁이·T자 끝 = 반드시 꺾는 곳)에서 TurnKmh 로 꺾을 수 있게.
        /// 꺾는 중(맞는 차로가 없음)에는 TurnKmh 를 지킨다. 사거리를 곧장 지나는 곳은 줄이지 않는다(꺾고 싶으면 사람이 브레이크).
        /// </summary>
        public static float RoadLimit(World w)
        {
            var car = w.car;
            var c = car.Center;
            float bestD = float.PositiveInfinity;
            Lane lane = null;
            int seg = 0;
            foreach (var ln in w.roadRun.def.lanes)
            {
                for (int i = 0; i + 1 < ln.center.Length; i++)
                {
                    var a = ln.center[i];
                    var b = ln.center[i + 1];
                    if ((b - a).Normalized.Dot(car.Dir) < 0.85f) continue;
                    float d = Geom.ClosestPointOnSegment(c, a, b).DistanceTo(c);
                    if (d < bestD) { bestD = d; lane = ln; seg = i; }
                }
            }
            if (lane == null || bestD > lane.width * 1.5f) return TurnKmh / 3.6f;
            var p = Geom.ClosestPointOnSegment(c, lane.center[seg], lane.center[seg + 1]);
            float rest = p.DistanceTo(lane.center[seg + 1]);
            for (int i = seg + 1; i + 1 < lane.center.Length; i++) rest += lane.center[i].DistanceTo(lane.center[i + 1]);
            // 곧장 이어지는 차로(지역 지도: 길이 교차로마다 나뉜다)는 끝으로 치지 않는다. 앞쪽 제한 속도가 낮아지면 그 앞에서 맞춘다.
            var lanes = w.roadRun.def.lanes;
            float limit = float.PositiveInfinity;
            int nx = lane.next, guard = 0;
            float look = Math.Max(0f, car.speed) * Math.Max(0f, car.speed) / (2f * PlanDecel) + 30f;
            while (nx >= 0 && nx < lanes.Count && rest < look && guard++ < 50)
            {
                var ln = lanes[nx];
                float lim = w.roadRun.def.LimitOf(ln) / 3.6f;
                if (lim < 200f) limit = Math.Min(limit, Allowed(lim - 2f / 3.6f, rest));
                for (int i = 0; i + 1 < ln.center.Length; i++) rest += ln.center[i].DistanceTo(ln.center[i + 1]);
                nx = ln.next;
            }
            if (nx >= 0) return limit;   // 앞이 계속 이어진다(볼 만큼 봤다)
            rest -= car.length / 2f;   // 차 가운데 → 앞 범퍼
            return Math.Min(limit, Allowed(TurnKmh / 3.6f, rest - 3f - Math.Max(0f, car.speed) * 0.5f));
        }
    }
}
