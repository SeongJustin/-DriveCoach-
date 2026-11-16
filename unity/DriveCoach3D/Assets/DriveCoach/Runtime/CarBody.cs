// 내 차 겉모습.
// 가 만든 세단(Resources/DC/Car/sedan.bytes: 차체·유리·등·그릴·바퀴…)을 읽어
// 지금 차의 제원(전장·전폭·축거·앞뒤 오버행·윤거)에 맞게 늘이고 줄인다. 모양은 직접 정한 일반 세단(특정 차 디자인을 본뜨지 않음).
//   길이: 앞·뒤 축 둘레 ±0.45 m 는 그대로 옮기기만(바퀴 아치가 찌그러지지 않게), 그 사이·앞뒤 끝만 늘이고 줄인다.
//   폭: 전폭 비율. 바퀴: 윤거·축 자리에 4개(이름에 "wheel"·"front" → CarView 가 꺾고 굴림). 캘리퍼는 바퀴 안에서 돌지 않는다.
// 무리 3(보닛 위·카울·와이퍼·사이드미러)은 운전석 화면에서도 보이게 따로 모아 둔다(CarView 가 층을 정한다).
using System.Collections.Generic;
using System.IO;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    public static class CarBody
    {
        const string ResPath = "DC/Car/sedan";
        public const float Height = 1.425f;   // 아반떼 CN8 전고(만든 모양의 높이)

        class Part
        {
            public string name;
            public int group, mat;
            public Vector3[] v, n;
            public int[] t;
        }

        static List<Part> parts;
        static float[] refSpec;   // 전장, 전폭, 전고, 축거, 앞 오버행, 뒤 오버행, 앞 윤거, 뒤 윤거, 타이어 반지름, 타이어 폭
        static bool tried;

        /// <summary>만든 차 모양이 들어 있는지(없으면 예전 Kenney 모델로)</summary>
        public static bool Available => Load();

        public static float WheelRadius => Load() ? refSpec[8] : 0.33f;

        /// <summary>지금 읽어 둔 모델 경로. 나라 표의 carModel 이 바뀌면(독일: 번호판 글자만 다른 sedan_de) 다시 읽는다</summary>
        static string loaded;

        static bool Load()
        {
            var cm = CountryStyle.Current.carModel;
            string want = string.IsNullOrEmpty(cm) ? ResPath : cm;
            if (loaded != want) { parts = null; tried = false; loaded = want; }
            if (parts != null) return true;
            if (tried) return false;
            tried = true;
            var ta = Resources.Load<TextAsset>(want);
            if (ta == null && want != ResPath) { Debug.LogWarning("CarBody: " + want + " 없음 → " + ResPath); ta = Resources.Load<TextAsset>(ResPath); }
            if (ta == null) { Debug.LogWarning("CarBody: " + ResPath + " 없음 → Kenney 모델"); return false; }
            try
            {
                using (var br = new BinaryReader(new MemoryStream(ta.bytes)))
                {
                    var magic = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
                    int ver = br.ReadInt32();
                    if (magic != "DCCR" || ver != 1) { Debug.LogWarning("CarBody: 형식 다름 " + magic + ver); return false; }
                    refSpec = new float[10];
                    for (int i = 0; i < 10; i++) refSpec[i] = br.ReadSingle();
                    int np = br.ReadInt32();
                    var list = new List<Part>(np);
                    for (int p = 0; p < np; p++)
                    {
                        var part = new Part();
                        int nl = br.ReadByte();
                        part.name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nl));
                        part.group = br.ReadByte();
                        part.mat = br.ReadByte();
                        int nv = br.ReadInt32(), ni = br.ReadInt32();
                        part.v = new Vector3[nv];
                        part.n = new Vector3[nv];
                        part.t = new int[ni];
                        for (int i = 0; i < nv; i++) part.v[i] = new Vector3(br.ReadInt16() * 1e-4f, br.ReadInt16() * 1e-4f, br.ReadInt16() * 1e-4f);
                        for (int i = 0; i < nv; i++) part.n[i] = new Vector3(br.ReadSByte() / 127f, br.ReadSByte() / 127f, br.ReadSByte() / 127f);
                        bool small = nv <= 65535;
                        for (int i = 0; i < ni; i++) part.t[i] = small ? br.ReadUInt16() : br.ReadInt32();
                        list.Add(part);
                    }
                    parts = list;
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("CarBody: 읽기 실패 " + e.Message);
                parts = null;
                return false;
            }
        }

        /// <summary>재질 번호(차 모델 파일의 재질 순서)별 Unity 재질</summary>
        static Material Mat(int id, Color paint, Material brake, Material reverse)
        {
            switch (id)
            {
                case 0: return DcAssets.Lit(paint, 0.84f, 0.45f);                          // 차체색(메탈릭)
                case 1: return DcAssets.Lit(DcAssets.Hex("#0D1217"), 0.95f, 0.35f);       // 유리(짙게)
                case 2: return DcAssets.Lit(DcAssets.Hex("#17181A"), 0.30f);              // 무광 검정 플라스틱
                case 3: return DcAssets.Lit(DcAssets.Hex("#0A0B0C"), 0.92f, 0.1f);        // 유광 검정
                case 4: return DcAssets.Lit(DcAssets.Hex("#D2D7DD"), 0.92f, 1f);          // 크롬
                case 5: return DcAssets.Lit(DcAssets.Hex("#22262B"), 0.88f, 0.6f);        // 앞등 안쪽
                case 6: return DcAssets.Unlit(DcAssets.Hex("#EEF4FF"));                   // 주간등(빛)
                case 7: return DcAssets.Lit(DcAssets.Hex("#5C0B0E"), 0.92f, 0.1f);        // 뒷등 렌즈
                case 8: return brake;                                                      // 브레이크등(밟으면 밝게)
                case 9: return reverse;                                                    // 후진등
                case 10: return DcAssets.Lit(DcAssets.Hex("#1A1B1D"), 0.12f);             // 타이어
                case 11: return DcAssets.Lit(DcAssets.Hex("#B4BAC2"), 0.78f, 0.9f);       // 휠
                case 12: return DcAssets.Lit(DcAssets.Hex("#5C5F64"), 0.5f, 0.8f);        // 디스크·휠 안쪽
                case 13: return DcAssets.Lit(DcAssets.Hex("#2B2E33"), 0.45f, 0.3f);       // 캘리퍼
                case 14: return DcAssets.Lit(DcAssets.Hex("#0E0F11"), 0.05f);             // 바닥·틈·바퀴집
                case 15: return DcAssets.Lit(DcAssets.Hex("#ECECE8"), 0.4f);              // 번호판
                case 16: return DcAssets.Lit(DcAssets.Hex("#E08A1E"), 0.85f);             // 방향지시등(주황)
                default: return DcAssets.Lit(DcAssets.Hex("#6E0F12"), 0.8f);              // 반사판
            }
        }

        /// <summary>제원 맞춤: 만든 차의 z → 지금 차의 z (축 둘레 ±h 는 그대로 옮김). k = 그 구간 늘임 비율</summary>
        class ZMap
        {
            public float zf0, zr0, zf1, zr1, h, kFront, kMid, kRear;
            public float Map(float z, out float k)
            {
                if (z >= zf0 + h) { k = kFront; return zf1 + h + (z - zf0 - h) * kFront; }
                if (z >= zf0 - h) { k = 1f; return z - zf0 + zf1; }
                if (z >= zr0 + h) { k = kMid; return zr1 + h + (z - zr0 - h) * kMid; }
                if (z >= zr0 - h) { k = 1f; return z - zr0 + zr1; }
                k = kRear; return zr1 - h + (z - zr0 + h) * kRear;
            }
        }

        /// <summary>
        /// holder 아래에 차를 만든다(원점 = 차 가운데 바닥, +Z 앞). 운전석에서도 보일 부품(보닛 등)은 driverView 아래에 모은다.
        /// 바퀴 4개: wheel-front-left … (가운데가 바퀴 중심), 그 아래 caliper(돌지 않게 CarView 가 되돌림).
        /// </summary>
        public static bool Build(Vehicle v, Transform holder, Color paint, Material brake, Material reverse, out Transform driverView)
        {
            driverView = null;
            if (!Load()) return false;
            float L0 = refSpec[0], W0 = refSpec[1], WB0 = refSpec[3], FOH0 = refSpec[4], ROH0 = refSpec[5];
            float sx = v.width / W0;
            var zm = new ZMap { zf0 = L0 / 2f - FOH0, zr0 = -L0 / 2f + ROH0, zf1 = v.length / 2f - v.frontOh, zr1 = -v.length / 2f + v.rearOh };
            zm.h = Mathf.Min(0.45f, Mathf.Min(Mathf.Min(FOH0, ROH0), Mathf.Min(v.frontOh, v.rearOh)) - 0.15f);
            zm.h = Mathf.Max(0.1f, Mathf.Min(zm.h, Mathf.Min(WB0, v.wheelbase) / 2f - 0.2f));
            zm.kFront = Mathf.Max(0.3f, (v.frontOh - zm.h) / (FOH0 - zm.h));
            zm.kRear = Mathf.Max(0.3f, (v.rearOh - zm.h) / (ROH0 - zm.h));
            zm.kMid = Mathf.Max(0.3f, (v.wheelbase - 2f * zm.h) / (WB0 - 2f * zm.h));

            var dvGo = new GameObject("driver-view");
            dvGo.transform.SetParent(holder, false);
            driverView = dvGo.transform;
            // 차체: 무리·재질별로 한 메시로 합친다(그리기 횟수 줄이기)
            var groups = new Dictionary<int, List<Part>>();
            var wipers = new List<Part>();
            foreach (var p in parts)
            {
                if (p.group != 0 && p.group != 3) continue;
                // 와이퍼는 따로(움직여야 한다: Wipers 가 이름 "…wipers-l/r" 로 찾아 축을 감싼다). 합쳐져서 와이퍼·빗방울이 안 돌던 것
                if (p.name != null && p.name.StartsWith("wipers")) { wipers.Add(p); continue; }
                int key = p.group * 100 + p.mat;
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<Part>();
                l.Add(p);
            }
            foreach (var kv in groups)
            {
                int grp = kv.Key / 100, mat = kv.Key % 100;
                var mesh = Merge(kv.Value, false,
                    p => new Vector3(p.x * sx, p.y, zm.Map(p.z, out _)),
                    (p, n) => { zm.Map(p.z, out float k); return new Vector3(n.x / sx, n.y, n.z / k).normalized; });
                bool glow = mat == 6 || mat == 8 || mat == 9;
                Spawn((grp == 3 ? "dv-" : "body-") + mat, mesh, Mat(mat, paint, brake, reverse), grp == 3 ? driverView : holder, !glow && mat != 1);
            }
            foreach (var p in wipers)
            {
                var mesh = Merge(new List<Part> { p }, false,
                    q => new Vector3(q.x * sx, q.y, zm.Map(q.z, out _)),
                    (q, n) => { zm.Map(q.z, out float k); return new Vector3(n.x / sx, n.y, n.z / k).normalized; });
                Spawn((p.group == 3 ? "dv-" : "body-") + p.name, mesh, Mat(p.mat, paint, brake, reverse), p.group == 3 ? driverView : holder, true);
            }
            // 바퀴 4개(왼쪽은 거울)
            float r = refSpec[8];
            var spin = ByMat(parts.FindAll(p => p.group == 1));
            var still = ByMat(parts.FindAll(p => p.group == 2));
            foreach (var front in new[] { true, false })
                foreach (var side in new[] { -1f, 1f })
                {
                    float track = front ? v.trackF : v.trackR;
                    var w = new GameObject((front ? "wheel-front-" : "wheel-rear-") + (side < 0 ? "left" : "right"));
                    w.transform.SetParent(holder, false);
                    w.transform.localPosition = new Vector3(side * track / 2f, r, front ? zm.zf1 : zm.zr1);
                    bool mirror = side < 0;
                    foreach (var byMat in spin)
                        Spawn("w" + byMat.Key, Merge(byMat.Value, mirror, p => Mirror(p, mirror), (p, n) => Mirror(n, mirror)),
                            Mat(byMat.Key, paint, brake, reverse), w.transform, true);
                    var cal = new GameObject("caliper");
                    cal.transform.SetParent(w.transform, false);
                    foreach (var byMat in still)
                        Spawn("c" + byMat.Key, Merge(byMat.Value, mirror, p => Mirror(p, mirror), (p, n) => Mirror(n, mirror)),
                            Mat(byMat.Key, paint, brake, reverse), cal.transform, false);
                }
            return true;
        }

        static Vector3 Mirror(Vector3 p, bool m) => m ? new Vector3(-p.x, p.y, p.z) : p;

        static Dictionary<int, List<Part>> ByMat(List<Part> ps)
        {
            var d = new Dictionary<int, List<Part>>();
            foreach (var p in ps)
            {
                if (!d.TryGetValue(p.mat, out var l)) d[p.mat] = l = new List<Part>();
                l.Add(p);
            }
            return d;
        }

        static Mesh Merge(List<Part> ps, bool flip, System.Func<Vector3, Vector3> pos, System.Func<Vector3, Vector3, Vector3> nrm)
        {
            int nv = 0, nt = 0;
            foreach (var p in ps) { nv += p.v.Length; nt += p.t.Length; }
            var V = new Vector3[nv];
            var N = new Vector3[nv];
            var T = new int[nt];
            int ov = 0, ot = 0;
            foreach (var p in ps)
            {
                for (int i = 0; i < p.v.Length; i++)
                {
                    V[ov + i] = pos(p.v[i]);
                    N[ov + i] = nrm(p.v[i], p.n[i]);
                }
                for (int i = 0; i < p.t.Length; i += 3)
                {
                    T[ot + i] = p.t[i] + ov;
                    T[ot + i + 1] = (flip ? p.t[i + 2] : p.t[i + 1]) + ov;
                    T[ot + i + 2] = (flip ? p.t[i + 1] : p.t[i + 2]) + ov;
                }
                ov += p.v.Length;
                ot += p.t.Length;
            }
            var m = new Mesh();
            if (nv > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = V;
            m.normals = N;
            m.triangles = T;
            m.RecalculateBounds();
            return m;
        }

        static void Spawn(string name, Mesh mesh, Material mat, Transform parent, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }
    }
}
