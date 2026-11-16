// 3D 주차 연습 — 코드로 만드는 도형: 바닥 다각형, 선(띠), 벽(기둥형), 리본(곡선 띠)
// 같은 재질은 한 메시에 모아(드로우 콜을 줄여) 에뮬레이터에서도 가볍게 그린다.
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DriveCoach.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    /// <summary>삼각형을 모아 한 메시로 만든다. uvScale(m) 간격으로 바닥 좌표를 그대로 UV 로 쓴다(질감 반복).</summary>
    public class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();
        public float uvScale = 4f;
        public bool IsEmpty => t.Count == 0;
        /// <summary>
        /// 위를 보는 삼각형(TriUp)의 같은 자리 점을 하나로 합친다. 실제 노면(아스팔트·보도·칠)은 삼각형마다 점 3개를 따로 두어
        /// 동네 하나에 60만 점이었다. 법선(위)·UV(바닥 좌표)가 자리에서 정해지므로 합쳐도 모양·질감은 같다.
        /// </summary>
        public bool weld;
        Dictionary<Vector3, int> welded;

        int Add(Vector3 p, Vector3 normal, Vector2 tex) { v.Add(p); n.Add(normal); uv.Add(tex); return v.Count - 1; }
        int AddUp(Vector3 p)
        {
            if (!weld) return Add(p, Vector3.up, Planar(p));
            if (welded == null) welded = new Dictionary<Vector3, int>();
            if (welded.TryGetValue(p, out int i)) return i;
            i = Add(p, Vector3.up, Planar(p));
            welded[p] = i;
            return i;
        }
        Vector2 Planar(Vector3 p) => new Vector2(p.x / uvScale, p.z / uvScale);

        /// <summary>볼록(또는 별 모양) 다각형을 높이 y 에 위로 보게(부채꼴)</summary>
        public void Polygon(IList<V2> poly, float y)
        {
            if (poly.Count < 3) return;
            int b = v.Count;
            foreach (var p in poly) { var w = Space3D.W(p, y); Add(w, Vector3.up, Planar(w)); }
            // 계산 좌표는 y 아래 → Unity Z = -y 라 감는 방향이 바뀐다. 위에서 보이도록 두 방향 모두 맞춘다.
            bool flip = SignedArea(poly) > 0f;
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                if (flip) { t.Add(b); t.Add(b + i); t.Add(b + i + 1); }
                else { t.Add(b); t.Add(b + i + 1); t.Add(b + i); }
            }
        }

        public void Rect(Core.Rect r, float y) => Polygon(new[] { new V2(r.x, r.y), new V2(r.x + r.w, r.y), new V2(r.x + r.w, r.y + r.h), new V2(r.x, r.y + r.h) }, y);

        static float SignedArea(IList<V2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.x * r.y - r.x * q.y; }
            return a * 0.5f;
        }

        /// <summary>선분 a→b 를 폭 w 의 납작한 띠로(바닥 칠)</summary>
        public void Line(V2 a, V2 b, float w, float y)
        {
            var d = (b - a).Normalized;
            var o = new V2(-d.y, d.x) * (w / 2f);
            Polygon(new[] { a + o, b + o, b - o, a - o }, y);
        }

        /// <summary>바닥 다각형을 y0 에서 y1 까지 세운 기둥(벽·기둥·연석). 옆면 + 윗면.</summary>
        public void Prism(IList<V2> poly, float y0, float y1)
        {
            Polygon(poly, y1);
            int cnt = poly.Count;
            bool ccw = SignedArea(poly) > 0f;
            for (int i = 0; i < cnt; i++)
            {
                var a = poly[i]; var bb = poly[(i + 1) % cnt];
                var wa0 = Space3D.W(a, y0); var wb0 = Space3D.W(bb, y0); var wa1 = Space3D.W(a, y1); var wb1 = Space3D.W(bb, y1);
                var edge = (wb0 - wa0);
                // 넓이가 + 인 다각형(계산 좌표)은 Unity 위에서 보면 시계 방향 → 바깥 법선 = -(위 × 변)
                var normal = Vector3.Cross(Vector3.up, edge).normalized * (ccw ? -1f : 1f);
                float len = edge.magnitude;
                int s = v.Count;
                Add(wa0, normal, new Vector2(0, y0 / uvScale));
                Add(wb0, normal, new Vector2(len / uvScale, y0 / uvScale));
                Add(wb1, normal, new Vector2(len / uvScale, y1 / uvScale));
                Add(wa1, normal, new Vector2(0, y1 / uvScale));
                if (ccw) { t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3); }
                else { t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2); }
            }
        }

        /// <summary>점들을 따라 폭 w 의 띠(서킷 노면·가장자리 선). closed 면 고리. off = 가운데선에서 옆으로 민 거리(+ 왼쪽)</summary>
        public void Ribbon(IList<V2> pts, float w, float y, bool closed, float off = 0f, int from = 0, int to = -1)
        {
            int n0 = pts.Count;
            if (to < 0) to = closed ? n0 : n0 - 1;
            V2 Normal(int i)
            {
                var prev = pts[closed ? (i - 1 + n0) % n0 : Mathf.Max(0, i - 1)];
                var next = pts[closed ? (i + 1) % n0 : Mathf.Min(n0 - 1, i + 1)];
                var d = (next - prev).Normalized;
                return new V2(d.y, -d.x);   // 진행 방향 왼쪽(계산 좌표)
            }
            float along = 0f;
            int prevL = -1, prevR = -1;
            for (int k = from; k <= to; k++)
            {
                int i = closed ? k % n0 : k;
                var p = pts[i];
                var nn = Normal(i);
                var c = p + nn * off;
                var l = Space3D.W(c + nn * (w / 2f), y);
                var r = Space3D.W(c - nn * (w / 2f), y);
                if (k > from) along += pts[i].DistanceTo(pts[closed ? (k - 1) % n0 : k - 1]);
                int il = Add(l, Vector3.up, new Vector2(0f, along / uvScale));
                int ir = Add(r, Vector3.up, new Vector2(w / uvScale, along / uvScale));
                if (prevL >= 0)
                {
                    t.Add(prevL); t.Add(il); t.Add(ir);
                    t.Add(prevL); t.Add(ir); t.Add(prevR);
                }
                prevL = il; prevR = ir;
            }
        }

        /// <summary>바닥에서 h 높이로 세운 판(띠 두께 없음). 방호벽·간판 등.</summary>
        public void Box(Vector3 center, Vector3 size, float yawDeg)
        {
            var q = Quaternion.Euler(0f, yawDeg, 0f);
            var hx = size.x / 2f; var hy = size.y / 2f; var hz = size.z / 2f;
            Vector3[] c =
            {
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz),
            };
            int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
            Vector3[] normals = { Vector3.back, Vector3.forward, Vector3.down, Vector3.up, Vector3.left, Vector3.right };
            for (int f = 0; f < 6; f++)
            {
                int s = v.Count;
                var nrm = q * normals[f];
                for (int k = 0; k < 4; k++)
                {
                    var p = center + q * c[faces[f][k]];
                    Add(p, nrm, new Vector2((k == 1 || k == 2) ? 1f : 0f, k >= 2 ? 1f : 0f));
                }
                t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
        }

        /// <summary>
        /// 네 점 판(건물 벽 등). outward 쪽에서 보이도록 감는 방향을 맞춘다(Unity: 앞면 = cross(p1-p0, p2-p0) 쪽).
        /// uv 는 p0..p3 순서.
        /// </summary>
        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            int s = v.Count;
            Add(p0, outward, uv0); Add(p1, outward, uv1); Add(p2, outward, uv2); Add(p3, outward, uv3);
            bool front = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) >= 0f;
            if (front) { t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3); }
            else { t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2); }
        }

        /// <summary>삼각형 하나(UV 지정). normal 쪽에서 보이게 감는 방향을 맞춘다(표지판·신호 불빛).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 normal)
        {
            int s = v.Count;
            Add(a, normal, ua); Add(b, normal, ub); Add(c, normal, uc);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0f) { t.Add(s); t.Add(s + 1); t.Add(s + 2); }
            else { t.Add(s); t.Add(s + 2); t.Add(s + 1); }
        }

        /// <summary>위를 보는 삼각형 하나(바닥 좌표 그대로 UV). 감는 방향은 위에서 보이게 맞춘다.</summary>
        public void TriUp(Vector3 a, Vector3 b, Vector3 c)
        {
            int ia = AddUp(a), ib = AddUp(b), ic = AddUp(c);
            if (Vector3.Cross(b - a, c - a).y >= 0f) { t.Add(ia); t.Add(ib); t.Add(ic); }
            else { t.Add(ia); t.Add(ic); t.Add(ib); }
        }

        /// <summary>둥근 고리(핸들 테): XZ 평면, 가운데 center, 고리 반지름 R, 굵기 반지름 r. UV u = 둘레, v = 단면</summary>
        public void Torus(Vector3 center, float R, float r, int seg = 48, int sides = 12)
        {
            int b = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k <= sides; k++)
                {
                    float c = k / (float)sides * Mathf.PI * 2f;
                    var nrm = dir * Mathf.Cos(c) + Vector3.up * Mathf.Sin(c);
                    Add(center + dir * R + nrm * r, nrm, new Vector2(i / (float)seg * 8f, k / (float)sides));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < seg; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a0 = b + i * row + k, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                    t.Add(a0); t.Add(a1); t.Add(b1); t.Add(a0); t.Add(b1); t.Add(b0);
                }
        }

        /// <summary>오목한 다각형도 되는 위를 보는 면(귀 자르기 삼각분할). 건물 지붕.</summary>
        public void PolygonEar(IList<V2> poly, float y)
        {
            int cnt = poly.Count;
            if (cnt < 3) return;
            int b = v.Count;
            foreach (var p in poly) { var w = Space3D.W(p, y); Add(w, Vector3.up, Planar(w)); }
            float area = SignedArea(poly);
            var idx = new List<int>();
            for (int i = 0; i < cnt; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4 * cnt)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k - 1 + idx.Count) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    var a = poly[i0]; var c = poly[i1]; var d = poly[i2];
                    float cr = (c.x - a.x) * (d.y - a.y) - (c.y - a.y) * (d.x - a.x);
                    if (cr * area <= 0f) continue;   // 볼록한 꼭짓점만
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(poly[j], a, c, d)) { inside = true; break; }
                    }
                    if (inside) continue;
                    Tri(b, i0, i1, i2, area > 0f);
                    idx.RemoveAt(k);
                    cut = true;
                    break;
                }
                if (!cut) break;   // 꼬인 다각형: 남은 것은 부채꼴로
            }
            for (int k = 1; k + 1 < idx.Count; k++) Tri(b, idx[0], idx[k], idx[k + 1], area > 0f);
        }

        void Tri(int b, int i0, int i1, int i2, bool flip)
        {
            if (flip) { t.Add(b + i0); t.Add(b + i1); t.Add(b + i2); }
            else { t.Add(b + i0); t.Add(b + i2); t.Add(b + i1); }
        }

        static bool InTri(V2 p, V2 a, V2 b, V2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>켜 두면(null 아님) Build 가 만든 메시를 모은다. 다 지은 뒤 한꺼번에 그래픽 장치로만 넘기고 일반 메모리 사본을 내리기 위해(CityBuilder).</summary>
        public static List<Mesh> Built;

        /// <summary>
        /// 납작한 면의 큰 삼각형을 가장 긴 변의 가운데에서 잘라 변이 maxEdge 를 넘지 않게 한다.
        /// 안개는 꼭짓점에서 계산되므로, 수백 m 짜리 삼각형(큰 공원·숲·땅)은 가까운 곳까지 안개색으로 뿌옇게 칠해진다.
        /// </summary>
        public void SplitLong(float maxEdge)
        {
            float lim = maxEdge * maxEdge;
            var mid = new Dictionary<long, int>();
            for (int i = 0; i + 2 < t.Count && t.Count < 900000; )
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                float ab = (v[a] - v[b]).sqrMagnitude, bc = (v[b] - v[c]).sqrMagnitude, ca = (v[c] - v[a]).sqrMagnitude;
                float mx = Mathf.Max(ab, Mathf.Max(bc, ca));
                if (mx <= lim) { i += 3; continue; }
                // 가장 긴 변이 a-b 가 되게 돌린다(감는 방향은 그대로)
                if (mx == bc) { int x = a; a = b; b = c; c = x; }
                else if (mx == ca) { int x = c; c = b; b = a; a = x; }
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!mid.TryGetValue(key, out int m))
                {
                    var pm = (v[a] + v[b]) * 0.5f;
                    m = Add(pm, n[a], (uv[a] + uv[b]) * 0.5f);
                    mid[key] = m;
                }
                t[i] = a; t[i + 1] = m; t[i + 2] = c;          // 앞 절반은 제자리에서 다시 검사
                t.Add(m); t.Add(b); t.Add(c);                  // 뒤 절반은 끝에 붙여 나중에 검사
            }
        }

        public Mesh Build(string name = "dc", bool tangents = true) => Build(name, tangents, false);

        Mesh Build(string name, bool tangents, bool compact)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            // 노멀맵(사진 질감의 요철)은 접선이 있어야 맞게 보인다
            bool hasTan = tangents && uv.Count == v.Count && v.Count > 0;
            if (hasTan) m.RecalculateTangents();
            if (compact) Compact(m, hasTan);
            Built?.Add(m);
            return m;
        }

        /// <summary>
        /// 정점 압축: 위치·UV 는 float32 그대로 두고 법선·접선만 8비트(SNorm8 ×4)로. 점 하나 48 → 28 바이트(접선 있음), 32 → 24 바이트.
        /// 그래픽 장치(에뮬레이터는 GL 드라이버가 앱 메모리에 사본을 둔다)의 정점 버퍼가 그만큼 준다. 평면(길·벽)의 법선은 축 방향이라 그대로이고,
        /// 둥근 면도 각도 오차가 0.5° 아래라 그늘 차이가 보이지 않는다. 기기가 이 형식을 못 쓰면 예전 그대로(float32).
        /// </summary>
        public static bool CompactVertices = true;
        static int snorm8 = -1;
        static bool Snorm8Ok
        {
            get
            {
                if (snorm8 < 0) snorm8 = SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.SNorm8, 4) ? 1 : 0;
                return snorm8 == 1;
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct VtxN { public Vector3 p; public uint n; public Vector2 uv; }
        [StructLayout(LayoutKind.Sequential)] struct VtxNT { public Vector3 p; public uint n; public uint t; public Vector2 uv; }

        static readonly VertexAttributeDescriptor[] LayoutN =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
        static readonly VertexAttributeDescriptor[] LayoutNT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
        const MeshUpdateFlags QuietUpdate = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

        static uint S8(float f) => (uint)(byte)(sbyte)Mathf.Clamp(Mathf.RoundToInt(f * 127f), -127, 127);
        static uint Pack(float x, float y, float z, float w) => S8(x) | (S8(y) << 8) | (S8(z) << 16) | (S8(w) << 24);

        void Compact(Mesh m, bool hasTan)
        {
            int cnt = v.Count;
            if (!CompactVertices || cnt == 0 || n.Count != cnt || uv.Count != cnt || !Snorm8Ok) return;
            try
            {
                if (hasTan)
                {
                    var tan = new NativeArray<Vector4>(cnt, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    try
                    {
                        using (var md = Mesh.AcquireReadOnlyMeshData(m)) md[0].GetTangents(tan);
                        var a = new NativeArray<VtxNT>(cnt, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                        try
                        {
                            for (int i = 0; i < cnt; i++)
                            {
                                var nn = n[i]; var tt = tan[i];
                                a[i] = new VtxNT { p = v[i], n = Pack(nn.x, nn.y, nn.z, 0f), t = Pack(tt.x, tt.y, tt.z, tt.w < 0f ? -1f : 1f), uv = uv[i] };
                            }
                            m.SetVertexBufferParams(cnt, LayoutNT);
                            m.SetVertexBufferData(a, 0, 0, cnt, 0, QuietUpdate);
                        }
                        finally { a.Dispose(); }
                    }
                    finally { tan.Dispose(); }
                }
                else
                {
                    var a = new NativeArray<VtxN>(cnt, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    try
                    {
                        for (int i = 0; i < cnt; i++)
                        {
                            var nn = n[i];
                            a[i] = new VtxN { p = v[i], n = Pack(nn.x, nn.y, nn.z, 0f), uv = uv[i] };
                        }
                        m.SetVertexBufferParams(cnt, LayoutN);
                        m.SetVertexBufferData(a, 0, 0, cnt, 0, QuietUpdate);
                    }
                    finally { a.Dispose(); }
                }
                m.RecalculateBounds();   // 배치를 바꾼 뒤 경계 상자를 다시(화면 밖 판정이 틀리지 않게)
            }
            catch (System.Exception e)
            {
                // 어떤 이유로든 압축이 안 되면 다시 float32 로(모양이 깨지지 않게) 하고 이후에는 압축하지 않는다
                Debug.LogWarning("MeshKit compact off: " + e.Message);
                CompactVertices = false;
                m.Clear(false);   // 8비트 배치를 지우고 기본(float32) 배치로
                if (cnt > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                if (hasTan) m.RecalculateTangents();
            }
        }

        /// <summary>다 쓴 점 목록을 놓는다(동네의 칸·재질별 메시 수천 개를 짓는 동안 목록이 끝까지 남지 않게)</summary>
        public void Release()
        {
            v.Clear(); v.Capacity = 0;
            n.Clear(); n.Capacity = 0;
            uv.Clear(); uv.Capacity = 0;
            t.Clear(); t.Capacity = 0;
            welded = null;
        }

        /// <summary>
        /// Built 가 켜져 있으면(동네 짓는 중) ToObject 가 메시를 만들자마자 그래픽 장치로 넘기고 일반 메모리의 사본을 내린다.
        /// 예전에는 동네 메시 사본을 끝까지 모두 들고 있다가 한꺼번에 내려서(사용 최고 235 MB) Unity 가 잡아 둔 메모리가 그만큼 남았다.
        /// 곧 다시 읽을 메시(밤 창 불빛 사본을 뜨는 외벽)는 KeepReadable 을 켜 두면 예전처럼 끝에 내린다.
        /// </summary>
        public static bool KeepReadable;

        /// <summary>메시를 가진 GameObject 를 만든다(그림자 받기·드리우기 선택)</summary>
        /// <summary>
        /// 단면들(점 개수가 같음)을 차례로 이어 매끈한 곡면을 만든다(실내: 대시보드·필러·문턱). sections[i][k] = i 번째 단면의 k 번째 점.
        /// 법선은 이웃 점들로 계산해 부드럽게 보이고, flip 이면 반대쪽을 앞면으로. UV 는 두 방향 길이(m) / uvM.
        /// </summary>
        public void Loft(IList<Vector3[]> sections, bool flip = false, float uvM = 0.5f)
        {
            int ns = sections.Count;
            if (ns < 2) return;
            int m = sections[0].Length;
            int b = v.Count;
            var ua = new float[ns, m];
            for (int k = 0; k < m; k++)
                for (int i = 1; i < ns; i++) ua[i, k] = ua[i - 1, k] + (sections[i][k] - sections[i - 1][k]).magnitude;
            for (int i = 0; i < ns; i++)
            {
                float va = 0f;
                for (int k = 0; k < m; k++)
                {
                    if (k > 0) va += (sections[i][k] - sections[i][k - 1]).magnitude;
                    var du = sections[Mathf.Min(ns - 1, i + 1)][k] - sections[Mathf.Max(0, i - 1)][k];
                    var dv = sections[i][Mathf.Min(m - 1, k + 1)] - sections[i][Mathf.Max(0, k - 1)];
                    var nrm = Vector3.Cross(du, dv);
                    nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.up;
                    Add(sections[i][k], flip ? -nrm : nrm, new Vector2(ua[i, k] / uvM, va / uvM));
                }
            }
            for (int i = 0; i + 1 < ns; i++)
                for (int k = 0; k + 1 < m; k++)
                {
                    int a = b + i * m + k, c = b + (i + 1) * m + k, d = b + (i + 1) * m + k + 1, e = b + i * m + k + 1;
                    if (!flip) { t.Add(a); t.Add(c); t.Add(d); t.Add(a); t.Add(d); t.Add(e); }
                    else { t.Add(a); t.Add(d); t.Add(c); t.Add(a); t.Add(e); t.Add(d); }
                }
        }

        public GameObject ToObject(string name, Material mat, Transform parent, bool castShadows = false, bool receiveShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // 접선은 요철 지도(노멀맵)가 있는 재질에만 쓰인다 → 없는 재질(차선 칠·볼라드·표지 등)은 만들지 않는다(점마다 16바이트, 모양은 같음)
            bool needTan = mat == null || (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null);
            var mesh = Build(name, needTan, true);
            if (Built != null && !KeepReadable) mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = receiveShadows;
            return go;
        }
    }
}
