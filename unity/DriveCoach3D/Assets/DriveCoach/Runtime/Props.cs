// 거리 소품: 한전 변압기함·통신함, 쓰레기통, 맨홀, 화분, 벤치. Poly Haven CC0 모델
// 같은 소품은 160 m 칸마다 한 메시로 합친다(드로우 콜이 늘지 않게). 질감은 DcAssets.Pbr(사진 질감 세트).
// 모델이 없으면(Resources 에 없으면) 아무것도 놓지 않는다.
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    public class PropKit
    {
        const float Tile = 160f;

        class Proto
        {
            public readonly List<(Mesh mesh, Matrix4x4 local, Material[] mats)> parts = new List<(Mesh, Matrix4x4, Material[])>();
            public Bounds bounds;
        }

        static readonly Dictionary<string, Proto> protos = new Dictionary<string, Proto>();
        // 모델 메시는 하나만 두고, 놓는 자리마다 그 메시를 가리키는 물체를 만든다. 칸마다 소품을 한 메시로 합치면(CombineMeshes)
        //   점이 전부 복사돼 동네 하나에 메시만 6 GB(점 1.3억 개)가 되어 에뮬레이터(3 GB)에서 메모리가 모자란다.
        readonly Dictionary<(int tx, int ty), List<(string id, Mesh mesh, Matrix4x4 m, Material[] mats)>> items = new Dictionary<(int, int), List<(string, Mesh, Matrix4x4, Material[])>>();

        /// <summary>id 에 '/' 가 있으면 Resources/DC 아래 경로 그대로(예: Quaternius 가로등), 없으면 Poly Haven 소품</summary>
        static bool IsPoly(string id) => !id.Contains("/");

        static Proto Get(string id)
        {
            // 합친 메시는 실행 중에 만든 것이라, 혹시 지워졌으면(Unity null) 다시 만든다
            if (protos.TryGetValue(id, out var p) && (p == null || p.parts.Count == 0 || p.parts[0].mesh != null)) return p;
            var go = DcAssets.Model(IsPoly(id) ? "PolyHaven/Models/" + id : id);
            if (go == null) { protos[id] = null; return null; }
            p = new Proto();
            // 본(프리팹) 뿌리의 회전·크기까지 넣는다(위치만 뺀다). 뿌리에 축 변환(-90° X)이 있는 FBX(Quaternius 가로등)를
            //   뿌리 기준으로만 합치면 기둥이 눕고, 높이 맞춤(크기 = 원하는 높이 / 누운 두께)이 15 배쯤 커져 길 위에 거대한 초록 막대가 떴다
            var root = Matrix4x4.Translate(-go.transform.position);
            bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var local = root * mf.transform.localToWorldMatrix;
                var mr = mf.GetComponent<MeshRenderer>();
                p.parts.Add((mf.sharedMesh, local, mr != null ? mr.sharedMaterials : new Material[0]));
                var b = mf.sharedMesh.bounds;
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sy in new[] { -1f, 1f })
                        foreach (var sz in new[] { -1f, 1f })
                        {
                            var c = local.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(sx, sy, sz)));
                            if (first) { p.bounds = new Bounds(c, Vector3.zero); first = false; } else p.bounds.Encapsulate(c);
                        }
            }
            if (p.parts.Count == 0) p = null;
            else if (IsPoly(id) && p.parts.Count > 1) MergeParts(p, id);
            protos[id] = p;
            return p;
        }

        /// <summary>
        /// Poly Haven 소품은 조각(쓰레기통은 손잡이·뚜껑 등 8개)이 모두 같은 재질(사진 질감 한 벌)로 그려진다 →
        /// 조각을 제자리 그대로 한 메시로 합쳐 소품 하나가 물체 하나가 되게 한다(동네 하나에 쓰레기통 849개 × 8 = 6,792 물체 → 849). 모양·재질 같음.
        /// 조각마다 서브메시가 하나일 때만(아니면 예전처럼 조각마다).
        /// </summary>
        static void MergeParts(Proto p, string id)
        {
            var cis = new List<CombineInstance>();
            long verts = 0;
            foreach (var (mesh, local, _) in p.parts)
            {
                if (mesh.subMeshCount != 1 || !mesh.isReadable) return;
                cis.Add(new CombineInstance { mesh = mesh, transform = local });
                verts += mesh.vertexCount;
            }
            var merged = new Mesh { name = id + "-merged" };
            if (verts > 65535) merged.indexFormat = IndexFormat.UInt32;
            merged.CombineMeshes(cis.ToArray(), true, true);
            merged.RecalculateBounds();
            p.parts.Clear();
            p.parts.Add((merged, Matrix4x4.identity, new Material[0]));
        }

        public static bool Has(string id) => Get(id) != null;

        /// <summary>소품 하나: pos = 바닥 가운데, size = 높이(byWidth 면 가장 긴 가로 길이)</summary>
        public void Add(string id, Vector3 pos, float yawDeg, float size, bool byWidth = false)
        {
            var p = Get(id);
            if (p == null) return;
            var b = p.bounds;
            float cur = byWidth ? Mathf.Max(b.size.x, b.size.z) : b.size.y;
            float s = size / Mathf.Max(1e-4f, cur);
            // 바닥 가운데를 원점으로 옮긴 뒤 크기·방향·위치
            var m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one * s) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z));
            int tx = Mathf.FloorToInt(pos.x / Tile), ty = Mathf.FloorToInt(pos.z / Tile);
            if (!items.TryGetValue((tx, ty), out var l)) items[(tx, ty)] = l = new List<(string, Mesh, Matrix4x4, Material[])>();
            bool poly = IsPoly(id);
            foreach (var (mesh, local, mats) in p.parts)
            {
                var use = new Material[mesh.subMeshCount];
                for (int sm = 0; sm < use.Length; sm++)
                    use[sm] = poly ? null : (sm < mats.Length ? mats[sm] : (mats.Length > 0 ? mats[mats.Length - 1] : null));
                l.Add((id, mesh, m * local, use));
            }
        }

        public void Emit(Transform parent, bool castShadows = true)
        {
            var node = new GameObject("props").transform;
            node.SetParent(parent, false);
            Material fallback = null;
            foreach (var kv in items)
            {
                var tile = new GameObject("props@" + kv.Key.tx + "," + kv.Key.ty);
                tile.transform.SetParent(node, false);
                var rs = new List<Renderer>(kv.Value.Count);
                foreach (var (id, mesh, m, mats) in kv.Value)
                {
                    var go = new GameObject(id);
                    var t = go.transform;
                    t.SetParent(tile.transform, false);
                    Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
                    t.localPosition = m.GetColumn(3);
                    if (c1.sqrMagnitude > 1e-12f && c2.sqrMagnitude > 1e-12f) t.localRotation = Quaternion.LookRotation(c2, c1);
                    t.localScale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == null)
                            mats[i] = IsPoly(id) ? DcAssets.Pbr(id, DcAssets.Hex("#8A8F98"), Color.white)
                                                 : (fallback != null ? fallback : (fallback = DcAssets.Lit(DcAssets.Hex("#5E636B"), 0.4f, 0.6f)));
                    foreach (var mt in mats) if (mt != null && !mt.enableInstancing) mt.enableInstancing = true;
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = castShadows && id != "water_manhole_cover" ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    rs.Add(mr);
                }
                // 멀리 있는 칸은 그리지 않는다(약 230 m 밖, 화질 가볍게면 약 140 m)
                var lg = tile.AddComponent<LODGroup>();
                int q = Prefs.Quality;
                lg.SetLODs(new[] { new LOD(q == 0 ? 0.062f : (q == 2 ? 0.028f : 0.038f), rs.ToArray()) });
                lg.size = 10f;
                lg.localReferencePoint = new Vector3((kv.Key.tx + 0.5f) * Tile, 0f, (kv.Key.ty + 0.5f) * Tile);
            }
        }
    }
}
