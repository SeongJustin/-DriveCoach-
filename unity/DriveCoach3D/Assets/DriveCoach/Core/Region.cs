// 담당: S (3D 내 동네 도로 연수(베타), 11/8 대표 요청: "한국 지도 데이터를 받아와서 자기 동네에서 정도는 운전해볼 수 있게")
// 오픈스트리트맵(OSM, © OpenStreetMap contributors, ODbL)에서 행정동 하나를 뽑은 JSON을
// 도로 주행 판(Stage, StageMode.Road)으로 바꾼다:
//  - 길(OSM way) → 차로 가운데선(우측통행: 진행 방향 오른쪽이 정방향 차로), 다음 차로 잇기(곧장 이어지는 길), 노면(띠)·교차로(원)
//  - 신호(traffic_signals) 묶음 → 교차로마다 신호 2갈래(가로·세로 번갈아), 들어오는 차로마다 정지선
//  - 건물 윤곽·높이 → 장애물(부딪히면 실패)과 3D 건물(Unity StageBuilder)
//  - 동 경계 → 동 밖 MARGIN 까지는 달릴 수 있고, 그 밖은 "지도 끝"
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{

    public class RegionData
    {
        public string name = "", attribution = "© OpenStreetMap contributors";
        public double originLat, originLon;
        public float margin = 300f;
        /// <summary>실제 노면 파일(Resources/DC/Regions/&lt;이름&gt;.bytes: 아스팔트·보도·칠·연석·소품). 없으면 빈 문자열</summary>
        public string surfaceFile = "";
        public V2[] boundary = new V2[0];
        /// <summary>데이터가 없는 옆 동 쪽으로 경계선에서 이만큼(m)까지는 그냥 다닌다 — 경계선이 큰길 한가운데를 지나서, 그 길의 반대쪽 차로까지는 다닐 수 있어야 한다</summary>
        public const float BorderSlack = 22f;
        public List<RegionRoad> roads = new List<RegionRoad>();
        public List<RegionJunction> junctions = new List<RegionJunction>();
        public List<V2> signals = new List<V2>(), crossings = new List<V2>();
        public List<RegionBuilding> buildings = new List<RegionBuilding>();
        public List<RegionArea> areas = new List<RegionArea>();

        /// <summary>
        /// (11/11 대표: "맵 선택에서 시작 위치 선택이 있어야") 시작할 수 있는 자리: 동 안에서 가장 긴 큰길 3곳·동네길 2곳의 실제 길 이름.
        /// 자리는 그 길의 가장 긴 곧은 조각 위(동 경계 안). 0 번은 따로 "동네 한가운데"(PickStart)라 여기에는 없다.
        /// </summary>
        public List<(string name, V2 p)> StartSpots()
        {
            var len = new Dictionary<string, float>();
            var best = new Dictionary<string, (float seg, V2 p)>();
            var major = new Dictionary<string, bool>();
            foreach (var r in roads)
            {
                if (string.IsNullOrEmpty(r.name) || r.tunnel || r.bridge || r.pts == null || r.pts.Length < 2) continue;
                if (r.hw == "service" || r.hw.EndsWith("_link") || r.hw == "motorway" || r.hw == "trunk") continue;
                bool mj = r.hw == "primary" || r.hw == "secondary" || r.hw == "tertiary";
                for (int i = 0; i + 1 < r.pts.Length; i++)
                {
                    var a0 = r.pts[i]; var b0 = r.pts[i + 1];
                    float L = a0.DistanceTo(b0);
                    var mid = (a0 + b0) / 2f;
                    if (boundary.Length >= 3 && !Geom.PointInPolygon(mid, boundary)) continue;
                    if (r.z != null && (Math.Abs(r.z[i]) > 0.05f || Math.Abs(r.z[i + 1]) > 0.05f)) continue;   // 고가·지하 구간은 시작 자리로 쓰지 않는다
                    len[r.name] = (len.TryGetValue(r.name, out var cur) ? cur : 0f) + L;
                    if (!major.ContainsKey(r.name) || mj) major[r.name] = mj || (major.TryGetValue(r.name, out var m0) && m0);
                    if (L >= 25f && (!best.TryGetValue(r.name, out var b) || L > b.seg)) best[r.name] = (L, mid);
                }
            }
            var names = new List<string>(best.Keys);
            names.Sort((x, y) => len[y].CompareTo(len[x]));
            var res = new List<(string, V2)>();
            int nMaj = 0, nMin = 0;
            foreach (var n in names)
            {
                bool mj = major.TryGetValue(n, out var m1) && m1;
                if (mj && nMaj >= 3) continue;
                if (!mj && nMin >= 2) continue;
                if (mj) nMaj++; else nMin++;
                res.Add((n, best[n].p));
                if (res.Count >= 5) break;
            }
            return res;
        }
        /// <summary>(11/15 대표: "모든 출발은 주차장 칸에서") 출발할 수 있는 주차장 칸 하나: 칸 가운데, 통로 쪽을 보는 방향(도), 가까운 길 이름, 주차장 번호</summary>
        public class ParkStart { public V2 c; public float headingDeg; public string name = ""; public int lot; }
        List<ParkStart> parkStarts;

        /// <summary>
        /// 동 안 주차장마다 길로 바로 나갈 수 있는 빈 칸 하나. 칸은 주차칸 선 세 개(왼쪽·오른쪽·안쪽 끝)씩 묶여 있다(add_facilities.py).
        /// 차는 칸 가운데에 통로(열린 쪽)를 보고 선다. 열린 쪽에서 가장 가까운 땅 위 길이 40 m 안이고 그 사이(폭 2.4 m)에 건물이 없는 칸만,
        /// 그중 길에 가장 가까운 칸. 세워 둔 차가 있는 칸은 뺀다.
        /// </summary>
        public List<ParkStart> ParkingStarts()
        {
            if (parkStarts != null) return parkStarts;
            var res = new List<ParkStart>();
            bool hasB = boundary.Length >= 3;
            for (int li = 0; li < facilities.Count; li++)
            {
                var f = facilities[li];
                if (f.kind != "parking" || f.stalls == null || f.stalls.Length < 6 || f.poly == null || f.poly.Length < 3) continue;
                if (hasB && !Geom.PointInPolygon(Geom.PolyCenter(f.poly), boundary)) continue;
                var cands = new List<(float d, V2 c, V2 dir, V2 open, V2 q, string nm)>();
                for (int k = 0; k + 5 < f.stalls.Length; k += 6)
                {
                    V2 l0 = f.stalls[k], l1 = f.stalls[k + 1], r0 = f.stalls[k + 2], r1 = f.stalls[k + 3], b0 = f.stalls[k + 4], b1 = f.stalls[k + 5];
                    var c = (l0 + l1 + r0 + r1) / 4f;
                    bool taken = false;
                    foreach (var pc in f.cars) if (pc.p.DistanceTo(c) < 1.2f) { taken = true; break; }
                    if (taken) continue;
                    var dir = (c - (b0 + b1) / 2f).Normalized;
                    var open = c + dir * 2.5f;
                    if (!NearestGroundRoad(open, out var q, out float d, out string nm) || d > 40f) continue;
                    cands.Add((d, c, dir, open, q, nm));
                }
                cands.Sort((x, y) => x.d.CompareTo(y.d));
                foreach (var cd in cands)
                {
                    if (BuildingBetween(cd.open, cd.q, 1.2f)) continue;
                    if (ExitBlocked(cd.c, cd.dir)) continue;
                    string nm = cd.nm;
                    if (string.IsNullOrEmpty(nm)) nm = NamedRoadNear(cd.open, 80f);
                    res.Add(new ParkStart { c = cd.c, headingDeg = (float)(Math.Atan2(cd.dir.y, cd.dir.x) * 180.0 / Math.PI), name = string.IsNullOrEmpty(nm) ? "" : nm + " 옆 주차장", lot = li });
                    break;
                }
            }
            for (int i = 0; i < res.Count; i++) if (res[i].name == "") res[i].name = "주차장 " + (i + 1);
            parkStarts = res;
            return res;
        }

        /// <summary>땅 위(고가·지하·터널 아님) 차도 중 p 에서 가장 가까운 점</summary>
        bool NearestGroundRoad(V2 p, out V2 q, out float dist, out string name)
        {
            q = V2.Zero; dist = float.MaxValue; name = "";
            foreach (var r in roads)
            {
                if (r.pts == null || r.pts.Length < 2 || r.tunnel || r.bridge) continue;
                if (r.hw == "motorway" || r.hw == "trunk" || r.hw.EndsWith("_link") || r.hw == "footway" || r.hw == "pedestrian") continue;
                for (int i = 0; i + 1 < r.pts.Length; i++)
                {
                    if (Math.Abs(r.pts[i].x - p.x) > 120f && Math.Abs(r.pts[i + 1].x - p.x) > 120f) continue;
                    if (r.z != null && (Math.Abs(r.z[i]) > 0.05f || Math.Abs(r.z[i + 1]) > 0.05f)) continue;
                    var c = Geom.ClosestPointOnSegment(p, r.pts[i], r.pts[i + 1]);
                    float d = c.DistanceTo(p);
                    if (d < dist) { dist = d; q = c; name = r.name ?? ""; }
                }
            }
            return dist < float.MaxValue;
        }

        string NamedRoadNear(V2 p, float maxD)
        {
            float best = maxD; string nm = "";
            foreach (var r in roads)
            {
                if (string.IsNullOrEmpty(r.name) || r.pts == null) continue;
                for (int i = 0; i + 1 < r.pts.Length; i++)
                {
                    float d = Geom.ClosestPointOnSegment(p, r.pts[i], r.pts[i + 1]).DistanceTo(p);
                    if (d < best) { best = d; nm = r.name; }
                }
            }
            return nm;
        }

        /// <summary>서 있는 자리부터 앞으로 곧게 칸 끝 5 m 앞까지(폭 2 m)에 건물·길가에 세운 차·주차장에 세운 차가 있는지</summary>
        bool ExitBlocked(V2 c, V2 dir)
        {
            var a = c - dir * 2.6f; var b = c + dir * 7.5f;        // 서 있는 차 몸통(뒤 끝)부터 칸 끝 5 m 앞까지
            if (BuildingBetween(a, b, 1.0f)) return true;
            var mid = (a + b) / 2f;
            var corridor = Geom.RectPoly(mid, a.DistanceTo(b), 2.0f, (float)(Math.Atan2(dir.y, dir.x) * 180.0 / Math.PI));
            foreach (var pc in parked)
                if (pc.c.DistanceTo(mid) < 10f && Geom.PolyOverlap(corridor, Geom.RectPoly(pc.c, pc.L, pc.W, pc.ang * Geom.Rad2Deg))) return true;
            foreach (var f in facilities)
                foreach (var (p, ang) in f.cars)
                    if (p.DistanceTo(mid) < 10f && Geom.PolyOverlap(corridor, Geom.RectPoly(p, 4.6f, 1.85f, ang * Geom.Rad2Deg))) return true;
            return false;
        }

        /// <summary>a→b 사이(폭 2 × halfW)에 건물이 있는지</summary>
        bool BuildingBetween(V2 a, V2 b, float halfW)
        {
            float len = a.DistanceTo(b);
            if (len < 0.3f) return false;
            var mid = (a + b) / 2f;
            var corridor = Geom.RectPoly(mid, len, halfW * 2f, (float)(Math.Atan2(b.y - a.y, b.x - a.x) * 180.0 / Math.PI));
            foreach (var bd in buildings)
            {
                if (bd.poly == null || bd.poly.Length < 3) continue;
                if (bd.center.DistanceTo(mid) > len / 2f + 60f) continue;
                if (Geom.PolyOverlap(corridor, bd.poly)) return true;
            }
            return false;
        }

        /// <summary>p 가 주차장(시설 parking) 안인지 — 도로 주행 판정에서 주차장 안은 차로 규칙을 보지 않는다</summary>
        public bool InParkingLot(V2 p)
        {
            foreach (var f in facilities)
                if (f.kind == "parking" && f.poly != null && f.poly.Length >= 3 && Geom.PointInPolygon(p, f.poly)) return true;
            return false;
        }

        public List<RegionNeighbor> neighbors = new List<RegionNeighbor>();
        public List<RegionSign> signs = new List<RegionSign>();
        public List<RegionFacility> facilities = new List<RegionFacility>();
        public List<RegionSignalHead> signalHeads = new List<RegionSignalHead>();
        /// <summary>어린이보호구역(11/9 새벽, OSM 초등학교·유치원 둘레 근사): 이름·다각형, 입구 빨간 노면 삼각형(x,y × 3 씩)</summary>
        public List<(string name, V2[] poly, V2 lo, V2 hi)> schoolZones = new List<(string, V2[], V2, V2)>();
        public float[] schoolRed = new float[0];
        /// <summary>횡단보도 잔여시간 표시기(11/9, 서울 T-GIS): 자리, 바라보는 방향(라디안)</summary>
        public List<(V2 p, float ang)> countdowns = new List<(V2, float)>();
        /// <summary>이면도로 가장자리에 세워 둔 차: 가운데, 방향(라디안), 길이, 폭</summary>
        public List<(V2 c, float ang, float L, float W)> parked = new List<(V2, float, float, float)>();
        /// <summary>
        /// 버스정류장: 표지 자리, 버스 진행 방향(라디안), 0 길가·1 중앙(섬 위)·2 중앙(섬 없음, 세우지 않음),
        /// 쉘터 가운데(없으면 hasShelter=false), 중앙 쉘터 길이·폭(길가는 3.6 × 1.4 m), 이름·정류소 번호
        /// </summary>
        public List<BusStop> busStops = new List<BusStop>();
        /// <summary>
        /// 신호 교차로 → 서울 교차로 번호와
        /// 번호별 실제 운영계획(평일 시간대, "sp": [[번호, [[시작 분, 주기, 옵셋, [A 링 현시…]], …]], …] — 경찰청 교차로계획정보)
        /// </summary>
        public Dictionary<int, (int no, string name)> junctionIds = new Dictionary<int, (int, string)>();
        public Dictionary<int, List<SignalSlot>> signalPlans = new Dictionary<int, List<SignalSlot>>();

        public struct BusStop
        {
            public V2 sign, shelter;
            public float heading, shelterLen, shelterWid;
            public int kind;
            public bool hasShelter;
            public string name, number;
        }

        /// <summary>횡단보도(11/9, 서울 T-GIS 넓이): 가운데, 건너는 방향(단위), 건너는 길이, 폭</summary>
        public List<(V2 c, V2 n, float L, float W)> crosswalks = new List<(V2, V2, float, float)>();
        /// <summary>(11/13) 노면에 칠해진 정지선(차로마다 그 차로를 가로막는 자리의 가운데). JSON "stl". 신호 정지선을 이 자리에 맞춘다</summary>
        public List<V2> stopPaint = new List<V2>();
        /// <summary>중앙선 무단횡단 방지 울타리(11/9 낮, add_medians.py "medf"): 황색 실선 겹선 위 선(점 목록)</summary>
        public List<V2[]> medianFences = new List<V2[]>();
        /// <summary>하천 깊이(11/9 낮, add_water_cut.py "water_cut"): 0 이면 물은 땅 높이 평면(예전)</summary>
        public float waterCut;

        /// <summary>p 가 들어 있는 어린이보호구역 번호(없으면 -1)</summary>
        public int SchoolZoneAt(V2 p)
        {
            for (int i = 0; i < schoolZones.Count; i++)
            {
                var z = schoolZones[i];
                if (p.x < z.lo.x || p.x > z.hi.x || p.y < z.lo.y || p.y > z.hi.y) continue;
                if (Geom.PointInPolygon(p, z.poly)) return i;
            }
            return -1;
        }

        /// <summary>과속방지턱(11/9 새벽, 서울 T-GIS 험프): 가운데, 진행 방향(라디안), 길이(진행 방향), 폭</summary>
        public List<Hump> humps = new List<Hump>();
        readonly Dictionary<long, List<int>> humpGrid = new Dictionary<long, List<int>>();
        const float HumpCell = 16f;
        static long HKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        public struct Hump
        {
            public V2 c, dir; public float L, W;
            /// <summary>원호형 방지턱 높이(m): 길이 3 m 넘으면 10 cm, 그 밑은 7.5 cm(소형)</summary>
            public float H => L >= 3f ? 0.10f : 0.075f;
        }

        public void AddHump(V2 c, float ang, float L, float W)
        {
            int id = humps.Count;
            humps.Add(new Hump { c = c, dir = new V2((float)Math.Cos(ang), (float)Math.Sin(ang)), L = L, W = W });
            float r = Math.Max(L, W) / 2f + 1f;
            for (int gx = (int)Math.Floor((c.x - r) / HumpCell); gx <= (int)Math.Floor((c.x + r) / HumpCell); gx++)
                for (int gy = (int)Math.Floor((c.y - r) / HumpCell); gy <= (int)Math.Floor((c.y + r) / HumpCell); gy++)
                {
                    var k = HKey(gx, gy);
                    if (!humpGrid.TryGetValue(k, out var l)) humpGrid[k] = l = new List<int>();
                    l.Add(id);
                }
        }

        /// <summary>점 p 의 방지턱 높이(m, 없으면 0). 단면 = 포물선(가운데 H, 앞뒤 끝 0)</summary>
        public float HumpHeight(V2 p)
        {
            if (humps.Count == 0) return 0f;
            if (!humpGrid.TryGetValue(HKey((int)Math.Floor(p.x / HumpCell), (int)Math.Floor(p.y / HumpCell)), out var l)) return 0f;
            float best = 0f;
            foreach (int i in l)
            {
                var h = humps[i];
                var d = p - h.c;
                float a = d.Dot(h.dir), b = Math.Abs(d.x * h.dir.y - d.y * h.dir.x);
                if (b > h.W / 2f || Math.Abs(a) > h.L / 2f) continue;
                float u = 2f * a / h.L;
                best = Math.Max(best, h.H * (1f - u * u));
            }
            return best;
        }

        /// <summary>p 에서 dir 쪽으로 [from, to] m 안에 방지턱이 있는가(차 앞 감속용)</summary>
        /// <summary>곧은 앞(옆 lateral m 안) lo~hi m 사이에 칠해진 정지선 가운데 가장 먼 것까지의 거리(없으면 -∞)</summary>
        public float StopPaintAhead(V2 from, V2 dir, float lo, float hi, float lateral)
        {
            float best = float.NegativeInfinity;
            foreach (var m in stopPaint)
            {
                var rel = m - from;
                if (Math.Abs(rel.x) > hi + 2f || Math.Abs(rel.y) > hi + 2f) continue;
                float ahead = rel.Dot(dir);
                if (ahead < lo || ahead > hi || Math.Abs(rel.x * dir.y - rel.y * dir.x) > lateral) continue;
                if (ahead > best) best = ahead;
            }
            return best;
        }

        public bool HumpAhead(V2 p, V2 dir, float from, float to)
        {
            if (humps.Count == 0) return false;
            for (float s = from; s <= to; s += 2f)
            {
                var q = p + dir * s;
                if (!humpGrid.TryGetValue(HKey((int)Math.Floor(q.x / HumpCell), (int)Math.Floor(q.y / HumpCell)), out var l)) continue;
                foreach (int i in l)
                {
                    var h = humps[i];
                    var d = q - h.c;
                    if (Math.Abs(d.Dot(h.dir)) <= h.L / 2f + 1f && Math.Abs(d.x * h.dir.y - d.y * h.dir.x) <= h.W / 2f) return true;
                }
            }
            return false;
        }

        /// <summary>p 가 들어 있는 옆 동(없으면 null)</summary>
        public RegionNeighbor NeighborAt(V2 p)
        {
            foreach (var n in neighbors) if (n.poly.Length >= 3 && Geom.PointInPolygon(p, n.poly)) return n;
            return null;
        }

        static float[] Dm(Dictionary<string, object> r, string key)
        {
            if (!r.TryGetValue(key, out var v) || !(v is List<object> l) || l.Count == 0) return null;
            var a = new float[l.Count];
            for (int i = 0; i < l.Count; i++) a[i] = Convert.ToSingle(l[i]) / 10f;
            return a;
        }

        static V2[] Pts(object o)
        {
            var a = o as List<object>;
            if (a == null) return new V2[0];
            var r = new V2[a.Count / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new V2(Convert.ToSingle(a[2 * i]) / 10f, Convert.ToSingle(a[2 * i + 1]) / 10f);
            return r;
        }

        public static RegionData Parse(string json)
        {
            var o = MiniJson.ParseObject(json);
            if (o == null) throw new FormatException("지역 JSON 을 읽지 못함");
            var d = new RegionData
            {
                name = MiniJson.Str(o, "name", ""),
                attribution = MiniJson.Str(o, "attribution", "© OpenStreetMap contributors"),
                margin = MiniJson.Num(o, "margin", 300f),
                surfaceFile = MiniJson.Str(o, "surface_file", ""),
                boundary = Pts(o.TryGetValue("boundary", out var bd) ? bd : null),
            };
            if (o.TryGetValue("origin", out var org) && org is List<object> ol && ol.Count == 2)
            {
                d.originLat = Convert.ToDouble(ol[0]);
                d.originLon = Convert.ToDouble(ol[1]);
            }
            if (o.TryGetValue("signs", out var sgo) && sgo is List<object> sgl)
                foreach (var it in sgl)
                    if (it is List<object> a && a.Count >= 4)
                        d.signs.Add(new RegionSign { p = new V2(Convert.ToSingle(a[0]) / 10f, Convert.ToSingle(a[1]) / 10f), faceDeg = Convert.ToSingle(a[2]), codes = Convert.ToString(a[3]).Split('|') });
            if (o.TryGetValue("sigs", out var sho) && sho is List<object> shl)
                foreach (var it in shl)
                    if (it is List<object> a && a.Count >= 7)
                        d.signalHeads.Add(new RegionSignalHead
                        {
                            pole = new V2(Convert.ToSingle(a[0]) / 10f, Convert.ToSingle(a[1]) / 10f), head = new V2(Convert.ToSingle(a[2]) / 10f, Convert.ToSingle(a[3]) / 10f),
                            faceDeg = Convert.ToSingle(a[4]), kind = Convert.ToInt32(a[5]), lamp = Convert.ToInt32(a[6]),
                        });
            if (o.TryGetValue("fac", out var fco) && fco is List<object> fcl)
                foreach (var it in fcl)
                {
                    if (!(it is Dictionary<string, object> fd)) continue;
                    var f = new RegionFacility { kind = MiniJson.Str(fd, "k", "parking"), poly = Pts(fd.TryGetValue("pts", out var fp) ? fp : null) };
                    if (fd.TryGetValue("st", out var st) && st is List<object>) f.stalls = Pts(st);
                    if (fd.TryGetValue("cars", out var cs) && cs is List<object> cl)
                        for (int i = 0; i + 2 < cl.Count; i += 3)
                            f.cars.Add((new V2(Convert.ToSingle(cl[i]) / 10f, Convert.ToSingle(cl[i + 1]) / 10f), Convert.ToSingle(cl[i + 2]) / 10f * Geom.Deg2Rad));
                    if (fd.TryGetValue("can", out var cn) && cn is List<object> canl && canl.Count >= 5)
                        f.canopy = new[] { Convert.ToSingle(canl[0]) / 10f, Convert.ToSingle(canl[1]) / 10f, Convert.ToSingle(canl[2]) / 10f * Geom.Deg2Rad, Convert.ToSingle(canl[3]) / 10f, Convert.ToSingle(canl[4]) / 10f };
                    if (f.poly != null && f.poly.Length >= 3) d.facilities.Add(f);
                }
            if (o.TryGetValue("sz", out var szo) && szo is List<object> szl)
                foreach (var it in szl)
                {
                    if (!(it is Dictionary<string, object> zd)) continue;
                    var poly = Pts(zd.TryGetValue("pts", out var zp) ? zp : null);
                    if (poly == null || poly.Length < 3) continue;
                    var lo = new V2(float.MaxValue, float.MaxValue); var hi = new V2(float.MinValue, float.MinValue);
                    foreach (var q in poly) { lo = new V2(Math.Min(lo.x, q.x), Math.Min(lo.y, q.y)); hi = new V2(Math.Max(hi.x, q.x), Math.Max(hi.y, q.y)); }
                    d.schoolZones.Add((MiniJson.Str(zd, "n", ""), poly, lo, hi));
                }
            if (o.TryGetValue("szr", out var szr) && szr is List<object> szrl)
            {
                d.schoolRed = new float[szrl.Count];
                for (int i = 0; i < szrl.Count; i++) d.schoolRed[i] = Convert.ToSingle(szrl[i]) / 10f;
            }
            if (o.TryGetValue("cd", out var cdo) && cdo is List<object> cdl)
                for (int i = 0; i + 2 < cdl.Count; i += 3)
                    d.countdowns.Add((new V2(Convert.ToSingle(cdl[i]) / 10f, Convert.ToSingle(cdl[i + 1]) / 10f), Convert.ToSingle(cdl[i + 2]) / 10f * Geom.Deg2Rad));
            if (o.TryGetValue("pk", out var pko) && pko is List<object> pkl)
                for (int i = 0; i + 4 < pkl.Count; i += 5)
                    d.parked.Add((new V2(Convert.ToSingle(pkl[i]) / 10f, Convert.ToSingle(pkl[i + 1]) / 10f), Convert.ToSingle(pkl[i + 2]) / 10f * Geom.Deg2Rad,
                        Convert.ToSingle(pkl[i + 3]) / 10f, Convert.ToSingle(pkl[i + 4]) / 10f));
            if (o.TryGetValue("bs", out var bso) && bso is List<object> bsl)
                foreach (var it in bsl)
                {
                    if (!(it is Dictionary<string, object> bsd)) continue;
                    var st = new BusStop
                    {
                        sign = new V2(MiniJson.Num(bsd, "x", 0f) / 10f, MiniJson.Num(bsd, "y", 0f) / 10f),
                        heading = MiniJson.Num(bsd, "a", 0f) / 10f * Geom.Deg2Rad,
                        kind = (int)MiniJson.Num(bsd, "m", 0),
                        hasShelter = bsd.ContainsKey("sx"),
                        shelter = new V2(MiniJson.Num(bsd, "sx", 0f) / 10f, MiniJson.Num(bsd, "sy", 0f) / 10f),
                        shelterLen = bsd.ContainsKey("sl") ? MiniJson.Num(bsd, "sl", 36f) / 10f : 3.6f,
                        shelterWid = bsd.ContainsKey("sw") ? MiniJson.Num(bsd, "sw", 14f) / 10f : 1.4f,
                        name = MiniJson.Str(bsd, "n", ""),
                        number = MiniJson.Str(bsd, "r", ""),
                    };
                    d.busStops.Add(st);
                }
            if (o.TryGetValue("stl", out var stlo) && stlo is List<object> stl)
                for (int i = 0; i + 3 < stl.Count; i += 4)
                    d.stopPaint.Add(new V2((Convert.ToSingle(stl[i]) + Convert.ToSingle(stl[i + 2])) / 20f, (Convert.ToSingle(stl[i + 1]) + Convert.ToSingle(stl[i + 3])) / 20f));
            if (o.TryGetValue("xw", out var xwo) && xwo is List<object> xwl)
                for (int i = 0; i + 4 < xwl.Count; i += 5)
                {
                    float ang = Convert.ToSingle(xwl[i + 2]) / 10f * Geom.Deg2Rad;
                    d.crosswalks.Add((new V2(Convert.ToSingle(xwl[i]) / 10f, Convert.ToSingle(xwl[i + 1]) / 10f), new V2((float)Math.Cos(ang), (float)Math.Sin(ang)),
                        Convert.ToSingle(xwl[i + 3]) / 10f, Convert.ToSingle(xwl[i + 4]) / 10f));
                }
            if (o.TryGetValue("water_cut", out var wco)) d.waterCut = Convert.ToSingle(wco);
            if (o.TryGetValue("medf", out var mfo) && mfo is List<object> mfl)
                foreach (var it in mfl)
                {
                    var pl = Pts(it);
                    if (pl != null && pl.Length >= 2) d.medianFences.Add(pl);
                }
            if (o.TryGetValue("hp", out var hpo) && hpo is List<object> hpl)
                for (int i = 0; i + 4 < hpl.Count; i += 5)
                    d.AddHump(new V2(Convert.ToSingle(hpl[i]) / 10f, Convert.ToSingle(hpl[i + 1]) / 10f), Convert.ToSingle(hpl[i + 2]) / 10f * Geom.Deg2Rad,
                        Convert.ToSingle(hpl[i + 3]) / 10f, Convert.ToSingle(hpl[i + 4]) / 10f);
            if (o.TryGetValue("nb", out var nbo) && nbo is List<object> nbl)
                foreach (var it in nbl)
                    if (it is Dictionary<string, object> nd)
                        d.neighbors.Add(new RegionNeighbor { name = MiniJson.Str(nd, "n", ""), file = MiniJson.Str(nd, "f", ""), poly = Pts(nd.TryGetValue("b", out var nb) ? nb : null) });
            if (o.TryGetValue("roads", out var rs) && rs is List<object> rl)
            {
                foreach (var it in rl)
                {
                    if (!(it is Dictionary<string, object> r)) continue;
                    var road = new RegionRoad
                    {
                        hw = MiniJson.Str(r, "hw", "residential"),
                        name = MiniJson.Str(r, "name", ""),
                        lanes = Math.Max(1, (int)MiniJson.Num(r, "lanes", 2)),
                        maxspeed = Math.Max(10, (int)MiniJson.Num(r, "maxspeed", 30)),
                        posted = Math.Max(0, (int)MiniJson.Num(r, "ms", 0)),
                        oneway = MiniJson.Num(r, "oneway", 0) > 0,
                        bridge = MiniJson.Num(r, "bridge", 0) > 0,
                        tunnel = MiniJson.Num(r, "tunnel", 0) > 0,
                        layer = (int)MiniJson.Num(r, "layer", 0),
                        width = MiniJson.Num(r, "w", 0f),
                        wl = MiniJson.Num(r, "wl", 0f) / 10f,
                        wr = MiniJson.Num(r, "wr", 0f) / 10f,
                        twl = MiniJson.Num(r, "tl", 0f) / 10f,
                        twr = MiniJson.Num(r, "tr", 0f) / 10f,
                        pts = Pts(r.TryGetValue("pts", out var p) ? p : null),
                    };
                    road.laneR = Dm(r, "lr");
                    road.laneL = Dm(r, "ll");
                    road.laneShift = Dm(r, "lsh");
                    if (road.laneShift != null && road.laneShift.Length != road.pts.Length) road.laneShift = null;
                    road.z = Dm(r, "z");
                    if (road.z != null && road.z.Length != road.pts.Length) road.z = null;
                    var jl = r.TryGetValue("jn", out var j) ? j as List<object> : null;
                    road.jn = new int[road.pts.Length];
                    for (int i = 0; i < road.jn.Length; i++) road.jn[i] = jl != null && i < jl.Count ? Convert.ToInt32(jl[i]) : -1;
                    if (road.pts.Length >= 2) d.roads.Add(road);
                }
            }
            if (o.TryGetValue("junctions", out var js) && js is List<object> jl2)
            {
                foreach (var it in jl2)
                {
                    var r = it as Dictionary<string, object>;
                    d.junctions.Add(r == null ? new RegionJunction() : new RegionJunction { p = new V2(MiniJson.Num(r, "x", 0), MiniJson.Num(r, "y", 0)), sig = MiniJson.Num(r, "sig", 0) > 0 });
                }
            }
            if (o.TryGetValue("jid", out var jio) && jio is List<object> jil)
                foreach (var it in jil)
                    if (it is List<object> e && e.Count >= 3)
                        d.junctionIds[Convert.ToInt32(e[0])] = (Convert.ToInt32(e[1]), Convert.ToString(e[2]));
            if (o.TryGetValue("sp", out var spo) && spo is List<object> spl)
                foreach (var it in spl)
                {
                    if (!(it is List<object> e) || e.Count < 2 || !(e[1] is List<object> sl)) continue;
                    var list = new List<SignalSlot>();
                    foreach (var so in sl)
                    {
                        if (!(so is List<object> q) || q.Count < 4 || !(q[3] is List<object> ph)) continue;
                        var arr = new float[ph.Count];
                        for (int k = 0; k < ph.Count; k++) arr[k] = Convert.ToSingle(ph[k]);
                        list.Add(new SignalSlot { startMin = Convert.ToInt32(q[0]), cycle = Convert.ToSingle(q[1]), offset = Convert.ToSingle(q[2]), phases = arr });
                    }
                    if (list.Count > 0) d.signalPlans[Convert.ToInt32(e[0])] = list;
                }
            d.signals.AddRange(Pts(o.TryGetValue("signals", out var sg) ? sg : null));
            d.crossings.AddRange(Pts(o.TryGetValue("crossings", out var cr) ? cr : null));
            if (o.TryGetValue("buildings", out var bs) && bs is List<object> bl)
            {
                foreach (var it in bl)
                {
                    if (!(it is Dictionary<string, object> b)) continue;
                    var poly = Pts(b.TryGetValue("pts", out var p) ? p : null);
                    if (poly.Length < 3) continue;
                    d.buildings.Add(new RegionBuilding
                    {
                        kind = MiniJson.Str(b, "k", "other"),
                        name = MiniJson.Str(b, "name", ""),
                        levels = Math.Max(1, (int)MiniJson.Num(b, "lv", 2)),
                        height = Math.Max(2.5f, MiniJson.Num(b, "h", 6f)),
                        generated = MiniJson.Num(b, "gen", 0) > 0,
                        poly = poly,
                        center = Geom.PolyCenter(poly),
                    });
                }
            }
            if (o.TryGetValue("areas", out var ar) && ar is List<object> al)
            {
                foreach (var it in al)
                {
                    if (!(it is Dictionary<string, object> a)) continue;
                    var poly = Pts(a.TryGetValue("pts", out var p) ? p : null);
                    if (poly.Length >= 3) d.areas.Add(new RegionArea { kind = MiniJson.Str(a, "k", "park"), poly = poly });
                }
            }
            return d;
        }
    }

    /// <summary>도로 주행 판의 길 띠(노면 그리기용)</summary>
    public class RoadStrip
    {
        public V2[] pts;
        public float width;
        public string hw = "";
        public bool twoWay, sidewalk;
        public int fwd, bwd;
        public float laneWidth;
        /// <summary>보이는 노면 반폭(왼쪽·오른쪽). 실제 값이 없으면 width/2</summary>
        public float visL, visR;
    }
}
