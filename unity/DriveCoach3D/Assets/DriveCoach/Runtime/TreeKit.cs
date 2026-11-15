// 담당: S (11/9 낮, 대표: "저 나무는 누가 만든 거야? 저런 나무도 외부 리소스로 할 수 있지 않나..? 최대한 외부 리소스 많이 쓸 것")
// 가로수·공원 나무: EZ-Tree(MIT, eztree.dev)로 만든 나무.
//   - 80 m 칸마다 가까이(LOD1, 약 6~8천 삼각형) / 멀리(LOD2, 약 4천) 두 메시로 합치고 LODGroup 으로 바꾼다(멀면 안 그림)
//   - 껍질: 사진 질감(tree_bark), 잎: 투명 자르기(DC_LitCutout, 양면) — 가로수는 아래 잎을 친 모양(street)
//   - 높이는 나무마다(9 m × 크기), 방향·종류는 자리마다 고정된 난수
// 질감·모양이 없으면 false(예전 코드 나무로).
using System.Collections.Generic;
using System.IO;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    public class TreeKit
    {
        const float Tile = 80f;
        public const float BaseHeight = 9f;
        // 서울 가로수 흉내: 물푸레(ash) — 느티나무 자리, 참나무(oak) — 플라타너스 자리, 사시나무(aspen, 좁고 곧은 모양) — 은행나무 자리
        // 공원은 여기에 바늘잎나무(pine — 잣나무·스트로브잣나무 자리)를 더한다. 파일이 없는 종류는 건너뛴다.
        static readonly string[] Kinds = { "ash_medium", "oak_medium" };
        static readonly string[] StreetKinds = { "ash_medium", "oak_medium", "aspen_medium" };
        static readonly string[] ParkKinds = { "ash_medium", "oak_medium", "aspen_medium", "pine_medium" };

        class Shape { public Mesh bark, leaves; }
        static readonly Dictionary<string, Shape> shapes = new Dictionary<string, Shape>();
        static readonly HashSet<string> missing = new HashSet<string>();

        readonly Dictionary<(int tx, int ty), Dictionary<(int lod, Material m), List<CombineInstance>>> tiles =
            new Dictionary<(int, int), Dictionary<(int, Material), List<CombineInstance>>>();
        public int count;

        public static bool Available => Load(Kinds[0] + "_street_lod1") != null;

        static Shape Load(string name)
        {
            if (shapes.TryGetValue(name, out var s)) return s;
            if (missing.Contains(name)) return null;
            var ta = Resources.Load<TextAsset>("DC/Trees/" + name);
            if (ta == null) { missing.Add(name); return null; }
            try
            {
                s = Read(ta.bytes, name);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("TreeKit: 읽기 실패 " + name + " " + e.Message);
                missing.Add(name);
                return null;
            }
            shapes[name] = s;
            return s;
        }

        static Shape Read(byte[] bytes, string name)
        {
            var s = new Shape();
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                var magic = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
                int ver = br.ReadInt32();
                br.ReadSingle();   // 원래 높이(참고)
                if (magic != "DCTR" || ver != 1) throw new System.Exception("형식 다름 " + magic + ver);
                int np = br.ReadInt32();
                for (int p = 0; p < np; p++)
                {
                    int nl = br.ReadByte();
                    string pn = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nl));
                    int nv = br.ReadInt32(), ni = br.ReadInt32();
                    var v = new Vector3[nv];
                    var n = new Vector3[nv];
                    var uv = new Vector2[nv];
                    var t = new int[ni];
                    for (int i = 0; i < nv; i++) v[i] = new Vector3(br.ReadInt16() * 1e-4f, br.ReadInt16() * 1e-4f, br.ReadInt16() * 1e-4f);
                    for (int i = 0; i < nv; i++) n[i] = new Vector3(br.ReadSByte() / 127f, br.ReadSByte() / 127f, br.ReadSByte() / 127f);
                    for (int i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                    bool small = nv <= 65535;
                    for (int i = 0; i < ni; i++) t[i] = small ? br.ReadUInt16() : br.ReadInt32();
                    var mesh = new Mesh { name = name + "-" + pn };
                    if (nv > 65535) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = v;
                    mesh.normals = n;
                    mesh.uv = uv;
                    mesh.triangles = t;
                    mesh.RecalculateBounds();
                    if (pn == "leaves") s.leaves = mesh; else s.bark = mesh;
                }
            }
            return s;
        }

        static Material BarkMat() => DcAssets.Pbr("tree_bark", DcAssets.Hex("#6B4F3A"), Color.white, 0.6f);

        static Material LeafMat(string kind)
        {
            // 종류마다 조금 다른 초록(사진 잎 질감에 곱한다)
            var tint = kind.StartsWith("oak") ? new Color(0.92f, 0.98f, 0.88f)
                : kind.StartsWith("aspen") ? new Color(0.95f, 1f, 0.92f)
                : new Color(1f, 1f, 1f);
            return DcAssets.Cutout("DC/Trees/" + kind + "_leaves", tint, DcAssets.Hex("#4E7A3A"));
        }

        /// <summary>나무 하나. street = 가로수(아래 잎 친 모양), height = 높이(m), yawDeg = 돌림, seed = 종류 고르기</summary>
        public bool Add(V2 p, float y, float height, float yawDeg, int seed, bool street)
        {
            var list = street ? StreetKinds : ParkKinds;
            string v = street ? "street" : "park";
            string kind = null;
            Shape near = null, far = null;
            int k0 = (seed & 0x7fffffff) % list.Length;
            for (int i = 0; i < list.Length && kind == null; i++)
            {
                var kk = list[(k0 + i) % list.Length];
                near = Load(kk + "_" + v + "_lod1");
                far = Load(kk + "_" + v + "_lod2");
                if (near != null && far != null) kind = kk;
            }
            if (kind == null) return false;
            int tx = Mathf.FloorToInt(p.x / Tile), ty = Mathf.FloorToInt(p.y / Tile);
            if (!tiles.TryGetValue((tx, ty), out var parts)) { parts = new Dictionary<(int, Material), List<CombineInstance>>(); tiles[(tx, ty)] = parts; }
            // 크기: 높이 그대로, 가로는 ±8 % (같은 나무가 줄지어 똑같아 보이지 않게)
            float wx = 1f + ((seed >> 4) % 17 - 8) * 0.01f;
            var M = Matrix4x4.TRS(Space3D.W(p, y), Quaternion.Euler(0f, yawDeg, 0f), new Vector3(height * wx, height, height * wx));
            var bark = BarkMat();
            var leaf = LeafMat(kind);
            foreach (var (lod, s) in new[] { (0, near), (1, far) })
            {
                if (s.bark != null) AddPart(parts, lod, bark, s.bark, M);
                if (s.leaves != null) AddPart(parts, lod, leaf, s.leaves, M);
            }
            count++;
            return true;
        }

        /// <summary>관목 하나(EZ-Tree bush_1, 공원 모양). 파일이 없으면 false</summary>
        public bool AddBush(V2 p, float y, float height, float yawDeg, int seed)
        {
            var near = Load("bush_1_park_lod1");
            var far = Load("bush_1_park_lod2");
            if (near == null || far == null) return false;
            int tx = Mathf.FloorToInt(p.x / Tile), ty = Mathf.FloorToInt(p.y / Tile);
            if (!tiles.TryGetValue((tx, ty), out var parts)) { parts = new Dictionary<(int, Material), List<CombineInstance>>(); tiles[(tx, ty)] = parts; }
            float wx = 1f + ((seed >> 9) % 21 - 10) * 0.015f;
            var M = Matrix4x4.TRS(Space3D.W(p, y), Quaternion.Euler(0f, yawDeg, 0f), new Vector3(height * wx * 1.15f, height, height * wx * 1.15f));
            var bark = BarkMat();
            var leaf = LeafMat("bush_1");
            foreach (var (lod, s) in new[] { (0, near), (1, far) })
            {
                if (s.bark != null) AddPart(parts, lod, bark, s.bark, M);
                if (s.leaves != null) AddPart(parts, lod, leaf, s.leaves, M);
            }
            count++;
            return true;
        }

        static void AddPart(Dictionary<(int, Material), List<CombineInstance>> parts, int lod, Material m, Mesh mesh, Matrix4x4 M)
        {
            if (!parts.TryGetValue((lod, m), out var l)) { l = new List<CombineInstance>(); parts[(lod, m)] = l; }
            l.Add(new CombineInstance { mesh = mesh, transform = M });
        }

        /// <summary>칸마다 LODGroup(가까이 = LOD1 모양, 그림자 있음 / 멀리 = LOD2, 그림자 없음 / 아주 멀면 안 그림)</summary>
        public void Emit(Transform node, string name = "trees")
        {
            if (count == 0) return;
            var root = new GameObject(name).transform;
            root.SetParent(node, false);
            foreach (var kv in tiles)
            {
                var tile = new GameObject($"{name}-{kv.Key.tx}_{kv.Key.ty}");
                tile.transform.SetParent(root, false);
                var lodR = new List<Renderer>[] { new List<Renderer>(), new List<Renderer>() };
                foreach (var pk in kv.Value)
                {
                    // (11/11) 나무마다 점을 복사해 한 메시로 합치던 것을 그만둔다(동네 하나에 메시 수 GB → 에뮬레이터 메모리 부족).
                    //   나무 모양 메시는 하나만 두고, 나무마다 그 메시를 가리키는 물체를 만든다.
                    if (!pk.Key.Item2.enableInstancing) pk.Key.Item2.enableInstancing = true;
                    foreach (var ci in pk.Value)
                    {
                        var go = new GameObject(pk.Key.Item1 == 0 ? "near" : "far");
                        var t = go.transform;
                        t.SetParent(tile.transform, false);
                        var m = ci.transform;
                        Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
                        t.localPosition = m.GetColumn(3);
                        if (c1.sqrMagnitude > 1e-12f && c2.sqrMagnitude > 1e-12f) t.localRotation = Quaternion.LookRotation(c2, c1);
                        t.localScale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
                        go.AddComponent<MeshFilter>().sharedMesh = ci.mesh;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterial = pk.Key.Item2;
                        r.shadowCastingMode = pk.Key.Item1 == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        lodR[pk.Key.Item1].Add(r);
                    }
                }
                var lg = tile.AddComponent<LODGroup>();
                // 화질 설정(가볍게 0·보통 1·좋게 2)에 따라: 가볍게면 가까운 모양을 덜 쓰고 일찍 안 그림, 좋게면 멀리까지 가까운 모양
                int q = Prefs.Quality;
                float nearH = q == 0 ? 0.75f : (q == 2 ? 0.25f : 0.40f), cullH = q == 0 ? 0.12f : (q == 2 ? 0.025f : 0.05f);
                lg.SetLODs(new[] { new LOD(nearH, lodR[0].ToArray()), new LOD(cullH, lodR[1].ToArray()) });
                // 칸(80 m) 크기로 재면 너무 멀리서 바뀐다 → 나무 몇 그루 크기(30 m)로: 약 65 m 까지 가까운 모양, 약 500 m 밖은 안 그림
                lg.size = 30f;
                lg.localReferencePoint = Space3D.W(new V2((kv.Key.tx + 0.5f) * Tile, (kv.Key.ty + 0.5f) * Tile), 0f);
            }
        }
    }
}
