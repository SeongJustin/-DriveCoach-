// 다른 차 모델을 읽어
// 종류마다 본(꺼 둔 GameObject) 하나를 만들고, 차마다 복사해 차체색만 바꾼다. 상표·글자 없음.
//   본: 재질별 차체 조각("fleet-<재질>"), 브레이크등 조각은 이름 "brake", 방향지시등은 "amber"(TrafficView 가 켜고 끈다),
//       바퀴 4개("wheel-front-left" … : TrafficView 가 굴리고 앞바퀴를 꺾는다, 시내·마을버스 뒤는 복륜처럼 두 겹).
// 세단·택시도 같은 방식(택시는 지붕 표시등 — 글자 없음). 내 차는 따로(더 자세한 sedan.bytes, CarBody).
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class FleetModels
    {
        static readonly Dictionary<string, GameObject> protos = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, float[]> specs = new Dictionary<string, float[]>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static Transform shelf;

        /// <summary>종류 이름(suv·hatch·van·truck·bus·townbus)의 본. 없으면 null</summary>
        public static GameObject Proto(string name)
        {
            if (protos.TryGetValue(name, out var p)) return p;
            if (missing.Contains(name)) return null;
            var ta = Resources.Load<TextAsset>("DC/Car/fleet_" + name);
            if (ta == null) { missing.Add(name); Debug.LogWarning("FleetModels: fleet_" + name + " 없음"); return null; }
            var raw = ta.bytes;
            Resources.UnloadAsset(ta);   // 본은 protos 에 남으니 파일(TextAsset)은 바로 내린다
            try
            {
                p = Build(name, raw);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("FleetModels: 읽기 실패 " + name + " " + e.Message);
                missing.Add(name);
                return null;
            }
            protos[name] = p;
            return p;
        }

        static GameObject Build(string name, byte[] bytes)
        {
            if (shelf == null)
            {
                var sh = new GameObject("FleetProtos");
                sh.SetActive(false);
                if (Application.isPlaying) Object.DontDestroyOnLoad(sh);   // 에디터 사진 찍기(플레이 아님)에서는 부르면 예외 → 모든 차 모델이 빠졌다
                shelf = sh.transform;
            }
            var root = new GameObject("fleet-" + name);
            root.transform.SetParent(shelf, false);
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                var magic = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
                int ver = br.ReadInt32();
                if (magic != "DCCR" || ver != 2) throw new System.Exception("형식 다름 " + magic + ver);
                var spec = new float[10];
                for (int i = 0; i < 10; i++) spec[i] = br.ReadSingle();
                specs[name] = spec;
                int np = br.ReadInt32();
                var wheelParts = new List<(int mat, Mesh mesh)>();
                for (int p = 0; p < np; p++)
                {
                    int nl = br.ReadByte();
                    string pn = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nl));
                    int grp = br.ReadByte(), mat = br.ReadByte();
                    int nv = br.ReadInt32(), ni = br.ReadInt32();
                    var v = new Vector3[nv];
                    var n = new Vector3[nv];
                    var t = new int[ni];
                    for (int i = 0; i < nv; i++) v[i] = new Vector3(br.ReadInt16() * 1e-3f, br.ReadInt16() * 1e-3f, br.ReadInt16() * 1e-3f);
                    for (int i = 0; i < nv; i++) n[i] = new Vector3(br.ReadSByte() / 127f, br.ReadSByte() / 127f, br.ReadSByte() / 127f);
                    bool small = nv <= 65535;
                    for (int i = 0; i < ni; i++) t[i] = small ? br.ReadUInt16() : br.ReadInt32();
                    var mesh = new Mesh { name = pn };
                    if (nv > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = v;
                    mesh.normals = n;
                    mesh.triangles = t;
                    mesh.RecalculateBounds();
                    if (grp == 1) { wheelParts.Add((mat, mesh)); continue; }
                    if (mat == 16)
                    {
                        // 방향지시등은 왼쪽·오른쪽을 따로 켜야 해서 x 부호로 나눈다
                        foreach (var sgn in new[] { -1, 1 })
                        {
                            var tl = new List<int>();
                            for (int i = 0; i + 2 < t.Length; i += 3)
                                if ((v[t[i]].x + v[t[i + 1]].x + v[t[i + 2]].x) * sgn > 0f) { tl.Add(t[i]); tl.Add(t[i + 1]); tl.Add(t[i + 2]); }
                            if (tl.Count == 0) continue;
                            var am = new Mesh { name = pn + (sgn < 0 ? "-L" : "-R"), vertices = v, normals = n, triangles = tl.ToArray() };
                            var ag = new GameObject(sgn < 0 ? "amber-left" : "amber-right");
                            ag.transform.SetParent(root.transform, false);
                            ag.AddComponent<MeshFilter>().sharedMesh = am;
                            var ar = ag.AddComponent<MeshRenderer>();
                            ar.sharedMaterial = Mat(16);
                            ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                        continue;
                    }
                    var go = new GameObject(mat == 8 ? "brake" : "fleet-" + mat);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = Mat(mat);
                    mr.shadowCastingMode = mat == 0 || mat == 2 || mat == 14 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                // 바퀴 4개(축·윤거 자리). 왼쪽 바퀴는 거울로 뒤집은 것이 아니라 돌려서 놓는다(휠 무늬 바깥)
                float L = spec[0], WB = spec[3], FOH = spec[4], ROH = spec[5], TF = spec[6], TRk = spec[7], R = spec[8];
                float zf = L / 2f - FOH, zr = -L / 2f + ROH;
                bool dual = name == "bus" || name == "townbus" || name == "truck";
                foreach (var (front, z, tr) in new[] { (true, zf, TF), (false, zr, TRk) })
                    foreach (var side in new[] { -1, 1 })
                    {
                        var wgo = new GameObject($"wheel-{(front ? "front" : "rear")}-{(side < 0 ? "left" : "right")}");
                        wgo.transform.SetParent(root.transform, false);
                        wgo.transform.localPosition = new Vector3(side * tr / 2f, R, z);
                        wgo.transform.localRotation = side < 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                        foreach (var (mat, mesh) in wheelParts)
                        {
                            var pg = new GameObject("w" + mat);
                            pg.transform.SetParent(wgo.transform, false);
                            pg.AddComponent<MeshFilter>().sharedMesh = mesh;
                            pg.AddComponent<MeshRenderer>().sharedMaterial = Mat(mat);
                            if (dual && !front)
                            {
                                var p2 = Object.Instantiate(pg, wgo.transform);
                                p2.transform.localPosition = new Vector3(-0.30f, 0f, 0f);
                            }
                        }
                    }
            }
            return root;
        }

        static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();

        /// <summary>차 모델 파일의 재질 번호 → Unity 재질(차체색 0 은 차마다 바꾼다)</summary>
        public static Material Mat(int id)
        {
            if (mats.TryGetValue(id, out var m)) return m;
            switch (id)
            {
                case 0: m = DcAssets.Lit(DcAssets.Hex("#8A8F98"), 0.7f, 0.35f); break;
                case 1: m = DcAssets.Lit(DcAssets.Hex("#11161C"), 0.95f, 0.3f); break;
                case 2: m = DcAssets.Lit(DcAssets.Hex("#1C1D20"), 0.3f); break;
                case 3: m = DcAssets.Lit(DcAssets.Hex("#0A0B0C"), 0.9f, 0.1f); break;
                case 4: m = DcAssets.Lit(DcAssets.Hex("#D2D7DD"), 0.9f, 1f); break;
                case 5: m = DcAssets.Lit(DcAssets.Hex("#70767E"), 0.85f, 0.7f); break;      // 등 안쪽(어두운 크롬)
                case 6: m = DcAssets.Unlit(DcAssets.Hex("#EEF2F8")); break;          // 앞등·행선지판(빛)
                case 7: m = DcAssets.Lit(DcAssets.Hex("#5C0B0E"), 0.9f, 0.1f); break;
                case 8: m = DcAssets.Unlit(new Color(0.35f, 0.03f, 0.03f)); break;   // 브레이크등(꺼짐)
                case 9: m = DcAssets.Lit(DcAssets.Hex("#D8DADC"), 0.8f, 0.1f); break;       // 후진등(꺼짐)
                case 10: m = DcAssets.Lit(DcAssets.Hex("#1A1B1D"), 0.12f); break;
                case 11: m = DcAssets.Lit(DcAssets.Hex("#B4BAC2"), 0.78f, 0.9f); break;
                case 12: m = DcAssets.Lit(DcAssets.Hex("#5C5F64"), 0.5f, 0.8f); break;
                case 14: m = DcAssets.Lit(DcAssets.Hex("#0E0F11"), 0.05f); break;
                case 15: m = DcAssets.Lit(DcAssets.Hex("#ECECE8"), 0.4f); break;
                case 16: m = DcAssets.Unlit(DcAssets.Hex("#5A3510")); break;
                case 17: m = DcAssets.Lit(DcAssets.Hex("#7A1014"), 0.7f, 0.1f); break;      // 반사판          // 방향지시등(꺼짐)
                default: m = DcAssets.Lit(DcAssets.Hex("#2A2D31"), 0.4f); break;
            }
            mats[id] = m;
            return m;
        }

        static readonly Dictionary<Color, Material> paints = new Dictionary<Color, Material>();

        /// <summary>차 한 대: 본을 복사해 차체색(재질 0)을 바꾼다</summary>
        public static GameObject Make(string name, Transform parent, Color paint)
        {
            var proto = Proto(name);
            if (proto == null) return null;
            var go = Object.Instantiate(proto, parent);
            go.name = "traffic-" + name;
            go.SetActive(true);
            if (!paints.TryGetValue(paint, out var pm)) { pm = DcAssets.Lit(paint, 0.72f, 0.35f); paints[paint] = pm; }
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                if (r.gameObject.name == "fleet-0") r.sharedMaterial = pm;
            return go;
        }
    }
}
