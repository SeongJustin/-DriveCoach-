// 보행자: 신호 막판에 뛰어드는 사람, 횡단보도 보행자 보호(2022.7.12 개정: 건너려 할 때도 일시정지).
// 내 동네 지도의 횡단보도(OSM crossing 점 → 그 자리 길에 수직인 선)를 사람이 건넌다.
//   신호 있는 횡단보도(30 m 안에 신호): 그 길의 차 신호가 빨간불일 때만 건너기 시작(보행 신호), 막판(빨간불 끝 4초)에는 뛰어 건넌다.
//   신호 없는 횡단보도: 가끔(밤엔 드물게) 한 사람씩. 내 차가 가까이(12 m) 빠르게 오고 있으면 길가에서 기다렸다가 건넌다.
//   건너는 중 다음 걸음이 차에 닿으면 멈춰 기다린다. 다 건너면 사라진다. 내 차에서 멀어진 사람도 지운다.
// 운전 규칙(RoadRun), 자동 속도(AutoSpeed), 다른 차(Traffic)가 Ahead(…) 로 앞의 사람을 본다.
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public class CrossPed
    {
        public V2 a, b;           // 건너는 선(시작 → 끝)
        public V2 pos;
        public float speed, facing, wait;
        public bool walking, onRoad, done;
        public int cross;         // 횡단보도 번호
        public int look;          // 모양 번호(Unity 가 옷 색을 고른다)
        public float t;           // 0..1 (시작 → 끝)
    }

    public class CrossPeds
    {
        public class Crossing
        {
            public V2 c, n;           // 가운데, 건너는 방향(길에 수직, 단위)
            public float half;        // 길 반폭 + 0.6 m(보도 가장자리에서 시작)
            public int signal = -1;   // 그 길 차 신호(-1 = 신호 없음)
            public float nextSpawn;
        }

        public readonly List<Crossing> crossings = new List<Crossing>();
        public readonly List<CrossPed> peds = new List<CrossPed>();
        readonly RoadDef road;
        readonly Random rnd = new Random(31);
        readonly int want;
        float spawnT;
        readonly List<Crossing> near = new List<Crossing>();
        public const float R = 0.3f;
        /// <summary>횡단보도 끝(차도 가장자리)에서 보도 안쪽으로 물러서 기다리는 거리(m)</summary>
        public const float Edge = 1.3f;

        /// <summary>density: 1 = 낮 보통, 밤 0.35, 비 0.6 …(GameMain 이 시간·날씨로)</summary>
        public CrossPeds(RoadDef r, float density)
        {
            road = r;
            want = Math.Max(0, (int)Math.Round(10 * density));
            var rg = r.region;
            if (rg == null) return;
            // 서울 T-GIS 횡단보도(칠과 같은 자리)가 있으면 그것으로
            if (rg.crosswalks.Count > 0)
            {
                foreach (var (c, n, L, W) in rg.crosswalks)
                {
                    if (L > 30f) continue;                      // 아주 긴 것(중앙 섬 두 번 건넘)은 빼고
                    var cr = new Crossing { c = c, n = n, half = L / 2f + 0.4f };
                    var dir = new V2(n.y, -n.x);                // 길 방향
                    float bs = 30f;
                    foreach (var sl in r.stopLines)
                    {
                        if (sl.signal < 0) continue;
                        float d = ((sl.a + sl.b) / 2f).DistanceTo(c);
                        if (d < bs && Math.Abs(sl.approach.Dot(dir)) > 0.8f) { bs = d; cr.signal = sl.signal; }
                    }
                    // 신호 교차로 옆인데 어느 신호인지 모르면(-2) 사람을 내보내지 않는다(초록불에 건너는 사람이 되지 않게)
                    if (cr.signal < 0) foreach (var sp in rg.signals) if (sp.DistanceTo(c) < 30f) { cr.signal = -2; break; }
                    crossings.Add(cr);
                }
                return;
            }
            foreach (var c in rg.crossings)
            {
                // 가장 가까운 차도 조각(3 m 안)의 방향·반폭
                float best = 3.5f; V2 dir = V2.Zero; float half = 0f;
                foreach (var rd in rg.roads)
                {
                    if (rd.pts.Length < 2 || rd.hw == "footway" || rd.hw == "service") continue;
                    if (rd.z != null) { bool up = false; foreach (var z in rd.z) if (Math.Abs(z) > 0.6f) { up = true; break; } if (up) continue; }
                    for (int i = 0; i + 1 < rd.pts.Length; i++)
                    {
                        var a = rd.pts[i]; var b = rd.pts[i + 1];
                        if (Math.Abs(c.x - a.x) > 40f && Math.Abs(c.x - b.x) > 40f) continue;
                        float d = Geom.ClosestPointOnSegment(c, a, b).DistanceTo(c);
                        if (d < best)
                        {
                            best = d; dir = (b - a).Normalized;
                            half = rd.wl > 0f || rd.wr > 0f ? Math.Max(rd.wl, rd.wr) : Math.Max(2.8f, rd.lanes * 1.6f);
                        }
                    }
                }
                if (dir == V2.Zero || half > 14f) continue;          // 아주 넓은 길(중앙 섬 두 번 건넘)은 빼고
                var cr = new Crossing { c = c, n = new V2(-dir.y, dir.x), half = half + 0.6f };
                // 신호: 25 m 안 정지선 중 길과 나란히 들어오는 것
                float bs = 25f;
                foreach (var sl in r.stopLines)
                {
                    if (sl.signal < 0) continue;
                    float d = ((sl.a + sl.b) / 2f).DistanceTo(c);
                    if (d < bs && Math.Abs(sl.approach.Dot(dir)) > 0.8f) { bs = d; cr.signal = sl.signal; }
                }
                crossings.Add(cr);
            }
        }

        public void Update(World w, float dt)
        {
            var car = w.car;
            var me = car.Center;
            spawnT -= dt;
            // 생기기: 1초마다, 내 차 120 m 안 횡단보도 하나를 골라
            if (spawnT <= 0f && peds.Count < want && crossings.Count > 0)
            {
                spawnT = 0.5f;
                // 가까운 횡단보도만 골라(내 차 10~120 m)
                near.Clear();
                foreach (var c0 in crossings) { float dd = c0.c.DistanceTo(me); if (dd > 10f && dd < 120f && w.t >= c0.nextSpawn) near.Add(c0); }
                for (int tries = 0; tries < 4 && near.Count > 0; tries++)
                {
                    var cr = near[rnd.Next(near.Count)];
                    if (cr.signal == -2) continue;
                    bool sig = cr.signal >= 0 && cr.signal < road.signals.Count;
                    if (sig && !road.signals[cr.signal].PedOn(w.t)) continue;
                    int side = rnd.Next(2) == 0 ? 1 : -1;
                    // 기다리는 자리·다 건넌 자리는 차도 가장자리가 아니라 보도 위(가장자리에서 Edge m 더 밖)
                    var a = cr.c - cr.n * ((cr.half + Edge) * side);
                    var b = cr.c + cr.n * ((cr.half + Edge) * side);
                    float sp = 1.1f + (float)rnd.NextDouble() * 0.45f;
                    peds.Add(new CrossPed { a = a, b = b, pos = a, speed = sp, facing = (b - a).Angle, cross = crossings.IndexOf(cr), look = rnd.Next(8),
                        wait = (float)rnd.NextDouble() * 1.5f });
                    cr.nextSpawn = w.t + (sig ? 3f + (float)rnd.NextDouble() * 4f : 8f + (float)rnd.NextDouble() * 18f);
                    break;
                }
            }
            var body = car.Corners(0.4f);
            for (int i = peds.Count - 1; i >= 0; i--)
            {
                var p = peds[i];
                var cr = crossings[p.cross];
                p.walking = false;
                if (p.done || p.pos.DistanceTo(me) > 220f) { peds.RemoveAt(i); continue; }
                if (p.wait > 0f) { p.wait -= dt; continue; }
                // 길에 들어서기 전: 내 차가 12 m 안에서 4 m/s 넘게 다가오면 기다린다(갑자기 뛰어들지 않는다)
                if (!p.onRoad && p.t < 0.5f)
                {
                    var rel = cr.c - car.FrontBumperCenter;
                    float ahead = rel.Dot(car.Dir);
                    if (ahead > -2f && ahead < 12f && car.speed > 4f && Math.Abs(rel.x * car.Dir.y - rel.y * car.Dir.x) < cr.half + 2f) continue;
                    // 차가 이미 코앞(8 m 안)까지 와서 움직이고 있으면 느리더라도 발을 내딛지 않는다. 꺾으려고 천천히 오던 차가 정지선을 넘은 자리에서 급히 서게 됐다
                    if (ahead > -2f && ahead < 8f && car.speed > 0.6f && Math.Abs(rel.x * car.Dir.y - rel.y * car.Dir.x) < cr.half + 2f) continue;
                }
                // 신호 막판(빨간불이 4초 안에 끝남 = 보행 신호 깜빡임)이면 뛴다
                float sp = p.speed;
                if (cr.signal >= 0 && cr.signal < road.signals.Count && !road.signals[cr.signal].PedOn(w.t + 4f)) sp = Math.Max(sp, 2.6f);
                var to = p.b - p.pos;
                float L = to.Length;
                var nxt = p.pos + to.Normalized * Math.Min(sp * dt, L);
                if (Geom.CircleOverlap(nxt, R, body)) continue;     // 차가 앞을 막고 있으면 기다린다
                p.pos = nxt;
                p.walking = true;
                p.facing = to.Angle;
                float total = p.a.DistanceTo(p.b);
                p.t = total > 0.01f ? p.a.DistanceTo(p.pos) / total : 1f;
                { float Lp = p.a.DistanceTo(p.b), along = p.t * Lp; p.onRoad = along > Edge - 0.3f && along < Lp - Edge + 0.3f; }
                if (L < 0.02f) p.done = true;
            }
        }

        /// <summary>
        /// from 에서 dir 쪽 앞 maxAhead m 안, 옆 lateral m 안에 길 위(건너는 중)이거나 막 들어서려는 사람이 있으면 그 앞 거리(없으면 ∞).
        /// </summary>
        public float Ahead(V2 from, V2 dir, float maxAhead, float lateral)
        {
            float best = float.PositiveInfinity;
            foreach (var p in peds)
            {
                if (!p.onRoad && !(p.walking && p.t < 0.1f)) continue;
                var rel = p.pos - from;
                float ahead = rel.Dot(dir);
                if (ahead < -0.5f || ahead > maxAhead) continue;
                if (Math.Abs(rel.x * dir.y - rel.y * dir.x) > lateral) continue;
                best = Math.Min(best, ahead);
            }
            return best;
        }

        /// <summary>차(네모)와 닿은 사람(없으면 null)</summary>
        public CrossPed Hit(V2[] carPoly)
        {
            foreach (var p in peds) if (Geom.CircleOverlap(p.pos, R, carPoly)) return p;
            return null;
        }
    }
}
