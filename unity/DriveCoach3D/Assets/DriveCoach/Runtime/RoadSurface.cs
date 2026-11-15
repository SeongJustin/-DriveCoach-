// 담당: S (11/8 내 동네 도로 연수 — 실제 노면): 미리 만들어 둔 <지역>_surface.bytes 읽기.
// 아스팔트(큰길·동네길)·보도·실제 칠(흰·노랑·파랑) 삼각형, 연석 선, 소품 자리. 좌표는 지역 좌표(dm, int16).
// 대표 요청(11/8): 칠이 아스팔트 밖으로 나가면 안 되고, 아스팔트 위에 나무 같은 물체가 들어가면 안 된다.
//  → 노면은 미리 한 몸으로 만들어 두고, 실행 중에 무언가 놓을 때는 Asphalt(p) 로 아스팔트 위인지 확인한다.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class RoadSurface
    {
        public readonly Dictionary<string, Vector2[]> tris = new Dictionary<string, Vector2[]>();
        public readonly List<V2[]> curbs = new List<V2[]>();
        /// <summary>보도 바깥 가장자리(보도가 선의 (−dy, dx) 쪽)</summary>
        public readonly List<V2[]> walkEdges = new List<V2[]>();
        /// <summary>소품: 종류, 자리(지역 좌표 m), 방향(지역 시계 방향 도), 크기</summary>
        public readonly List<(string kind, V2 p, float ang, float size)> props = new List<(string, V2, float, float)>();

        // 삼각형 격자(8 m 칸): 점이 아스팔트(또는 보도) 위인지 빠르게
        const float Cell = 8f;
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        readonly Dictionary<long, List<int>> wgrid = new Dictionary<long, List<int>>();
        Vector2[] asph, walk;

        static readonly Dictionary<string, RoadSurface> cache = new Dictionary<string, RoadSurface>();
        static readonly string[] PropKinds = { "tree", "light", "utility_box_01", "utility_box_02", "metal_trash_can", "planter_box_01", "painted_wooden_bench", "water_manhole_cover", "park_tree" };

        static readonly Dictionary<string, System.Threading.Tasks.Task<RoadSurface>> pending = new Dictionary<string, System.Threading.Tasks.Task<RoadSurface>>();

        /// <summary>(11/11) 옆 동 노면 파일을 뒤에서 미리 읽어 둔다(RegionCatalog.Preload 가 부른다). 내용 꺼내기는 주 스레드, 해석은 작업 스레드.</summary>
        public static void Preload(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            lock (cache) { if (cache.ContainsKey(file) || pending.ContainsKey(file)) return; }
            byte[] raw = null;
            try { raw = RegionCatalog.ReadBytes(file); }
            catch (Exception e) { Debug.LogWarning("Surface " + file + ": " + e.Message); }
            if (raw == null) return;
            var task = System.Threading.Tasks.Task.Run(() => Parse(raw));
            lock (cache) pending[file] = task;
        }

        public static void KeepOnly(string file)
        {
            lock (cache)
            {
                var drop = new List<string>();
                foreach (var k in cache.Keys) if (k != file) drop.Add(k);
                foreach (var k in drop) cache.Remove(k);
            }
        }

        public static RoadSurface Load(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            System.Threading.Tasks.Task<RoadSurface> t;
            lock (cache)
            {
                if (cache.TryGetValue(file, out var have)) return have;
                pending.TryGetValue(file, out t);
            }
            RoadSurface s = null;
            bool done = false;
            if (t != null)
            {
                try { s = t.Result; done = true; }       // 아직 읽는 중이면 끝날 때까지 기다린다
                catch (Exception e) { Debug.LogWarning("Surface preload " + file + ": " + e.Message); }
                lock (cache) pending.Remove(file);
            }
            if (!done)
            {
                try
                {
                    var raw = RegionCatalog.ReadBytes(file);
                    if (raw != null) s = Parse(raw);
                }
                catch (Exception e) { Debug.LogWarning("Surface " + file + ": " + e.Message); s = null; }
            }
            lock (cache) cache[file] = s;
            return s;
        }

        static RoadSurface Parse(byte[] b)
        {
            if (b.Length < 4 || b[0] != 'D' || b[1] != 'C' || b[2] != 'S' || b[3] != '1') return null;
            var s = new RoadSurface();
            int o = 4;
            while (o + 8 <= b.Length)
            {
                string tag = System.Text.Encoding.ASCII.GetString(b, o, 4);
                o += 4;
                int n = BitConverter.ToInt32(b, o);
                o += 4;
                if (tag == "CURB" || tag == "SEDG")
                {
                    for (int k = 0; k < n; k++)
                    {
                        int m = BitConverter.ToInt32(b, o); o += 4;
                        var line = new V2[m];
                        for (int i = 0; i < m; i++) { line[i] = new V2(BitConverter.ToInt16(b, o) / 10f, BitConverter.ToInt16(b, o + 2) / 10f); o += 4; }
                        (tag == "CURB" ? s.curbs : s.walkEdges).Add(line);
                    }
                }
                else if (tag == "PROP")
                {
                    // 한 개 = 종류(int16) x y(int16 dm) 방향(int16, 0.1도) 크기(int16, cm)
                    for (int k = 0; k < n; k++)
                    {
                        int kind = BitConverter.ToInt16(b, o);
                        var p = new V2(BitConverter.ToInt16(b, o + 2) / 10f, BitConverter.ToInt16(b, o + 4) / 10f);
                        float ang = BitConverter.ToInt16(b, o + 6) / 10f;
                        float size = BitConverter.ToInt16(b, o + 8) / 100f;
                        o += 10;
                        if (kind >= 0 && kind < PropKinds.Length) s.props.Add((PropKinds[kind], p, ang, size));
                    }
                }
                else
                {
                    // 삼각형: int16 값 n 개(x, y 번갈아)
                    var v = new Vector2[n / 2];
                    for (int i = 0; i < v.Length; i++) { v[i] = new Vector2(BitConverter.ToInt16(b, o) / 10f, BitConverter.ToInt16(b, o + 2) / 10f); o += 4; }
                    s.tris[tag] = v;
                }
            }
            s.Index();
            return s;
        }

        void Index()
        {
            var a = new List<Vector2>();
            foreach (var tag in new[] { "AMAJ", "AMIN" })
                if (tris.TryGetValue(tag, out var v)) a.AddRange(v);
            asph = a.ToArray();
            walk = tris.TryGetValue("SIDE", out var w) ? w : new Vector2[0];
            Fill(grid, asph);
            Fill(wgrid, walk);
        }

        static void Fill(Dictionary<long, List<int>> g, Vector2[] v)
        {
            for (int t = 0; t + 2 < v.Length; t += 3)
            {
                float x0 = Mathf.Min(v[t].x, Mathf.Min(v[t + 1].x, v[t + 2].x)), x1 = Mathf.Max(v[t].x, Mathf.Max(v[t + 1].x, v[t + 2].x));
                float y0 = Mathf.Min(v[t].y, Mathf.Min(v[t + 1].y, v[t + 2].y)), y1 = Mathf.Max(v[t].y, Mathf.Max(v[t + 1].y, v[t + 2].y));
                for (int gx = Mathf.FloorToInt(x0 / Cell); gx <= Mathf.FloorToInt(x1 / Cell); gx++)
                    for (int gy = Mathf.FloorToInt(y0 / Cell); gy <= Mathf.FloorToInt(y1 / Cell); gy++)
                    {
                        long k = Key(gx, gy);
                        if (!g.TryGetValue(k, out var l)) g[k] = l = new List<int>();
                        l.Add(t);
                    }
            }
        }

        static long Key(int gx, int gy) => ((long)gx << 32) ^ (uint)gy;

        static bool Hit(Dictionary<long, List<int>> g, Vector2[] v, V2 p)
        {
            if (!g.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell)), out var l)) return false;
            foreach (int t in l)
                if (InTri(p.x, p.y, v[t], v[t + 1], v[t + 2])) return true;
            return false;
        }

        /// <summary>점이 아스팔트 위인가</summary>
        public bool Asphalt(V2 p) => Hit(grid, asph, p);

        /// <summary>점이 보도(연석 높이로 올린 곳) 위인가</summary>
        public bool Sidewalk(V2 p) => Hit(wgrid, walk, p);

        /// <summary>반지름 r 원이 아스팔트에 닿나(가운데 + 둘레 8점)</summary>
        public bool TouchesAsphalt(V2 p, float r)
        {
            if (Asphalt(p)) return true;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                if (Asphalt(new V2(p.x + Mathf.Cos(a) * r, p.y + Mathf.Sin(a) * r))) return true;
            }
            return false;
        }

        /// <summary>
        /// 아스팔트 위(또는 r 안)면 가장 가까운 바깥 자리로(최대 14 m). 같은 거리면 보도 위를 먼저 고른다
        /// (연석에서 r + 0.15 m 떨어진 보도). 못 찾으면 그대로.
        /// </summary>
        public V2 PushOff(V2 p, float r)
        {
            if (!TouchesAsphalt(p, r)) return p;
            V2? any = null;
            for (float d = 0.5f; d <= 14f; d += 0.25f)
            {
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI / 16f;
                    var q = new V2(p.x + Mathf.Cos(a) * d, p.y + Mathf.Sin(a) * d);
                    if (TouchesAsphalt(q, r + 0.15f)) continue;
                    if (Sidewalk(q)) return q;
                    if (any == null) any = q;
                }
                // 보도가 없는 곳: 아스팔트 밖 첫 자리에서 2 m 더 찾아봐도 보도가 없으면 그 자리
                if (any != null && d > any.Value.DistanceTo(p) + 2f) return any.Value;
            }
            return any ?? p;
        }

        static bool InTri(float px, float py, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (px - b.x) * (a.y - b.y) - (a.x - b.x) * (py - b.y);
            float d2 = (px - c.x) * (b.y - c.y) - (b.x - c.x) * (py - c.y);
            float d3 = (px - a.x) * (c.y - a.y) - (c.x - a.x) * (py - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
