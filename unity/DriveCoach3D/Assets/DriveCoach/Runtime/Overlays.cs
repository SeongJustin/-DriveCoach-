// (3D 주차 연습에서 매 프레임 바뀌는 안내: 지나갈 자리(초록 띠)·내륜차(주황), 목표 칸, 튜토리얼 안내선·맞출 점,
// 주차 센서 호, 실패 표시, 보행자, 도로 신호등 불빛, 서킷 다음 체크포인트)
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class Overlays
    {
        const float PathLen = 6f;
        readonly Transform root;
        readonly Mesh pathMesh, innerMesh, targetFill, guideMesh, sensorMesh;
        readonly Material pathMat, innerMat, targetFillMat, targetLineMat, guideMat, sensorMat, failMat;
        readonly GameObject markA, markB, failRing;
        readonly List<GameObject> peds = new List<GameObject>();
        readonly List<Renderer[]> signalLamps = new List<Renderer[]>();
        readonly Elevation el;
        public bool showInner;
        /// <summary>운전석 시점: 차 위에 띄우는 맞출 점(어깨 높이)은 눈앞을 가리므로 숨긴다(바닥 안내선은 창밖으로 보인다)</summary>
        public bool driverView;
        float time;

        public Overlays(World w, Elevation elev, Transform parent)
        {
            el = elev;
            root = new GameObject("Overlays").transform;
            root.SetParent(parent, false);
            pathMat = DcAssets.UnlitInstance(new Color(DcAssets.Green.r, DcAssets.Green.g, DcAssets.Green.b, 0.28f));
            innerMat = DcAssets.UnlitInstance(new Color(DcAssets.Orange.r, DcAssets.Orange.g, DcAssets.Orange.b, 0.35f));
            targetFillMat = DcAssets.UnlitInstance(new Color(DcAssets.Lime.r, DcAssets.Lime.g, DcAssets.Lime.b, 0.22f));
            targetLineMat = DcAssets.UnlitInstance(DcAssets.Lime);
            guideMat = DcAssets.UnlitInstance(DcAssets.Green);
            sensorMat = DcAssets.UnlitInstance(new Color(1f, 0.76f, 0f, 0.85f));
            failMat = DcAssets.UnlitInstance(DcAssets.Red);
            pathMesh = MeshObj("path", pathMat);
            innerMesh = MeshObj("inner", innerMat);
            targetFill = MeshObj("target", targetFillMat);
            guideMesh = MeshObj("guides", guideMat);
            sensorMesh = MeshObj("sensors", sensorMat);
            BuildTargetOutline(w);
            markA = Marker("mark", Color.white, 0.4f);
            markB = Marker("mark-dot", DcAssets.Text, 0.2f);
            failRing = Marker("fail", DcAssets.Red, 1.2f);
            failRing.GetComponent<Renderer>().sharedMaterial = failMat;
            foreach (var p in w.peds) peds.Add(Pedestrian());
            // 신호등 불빛(빨강·노랑·초록 공): 도로 주행. 실제 자리 신호등(SignalHeads)이 있으면 그쪽이 불을 켠다
            if (w.st.road != null && SignalHeads.Current == null)
            {
                foreach (var sg in w.st.road.signals)
                {
                    var lamps = new Renderer[3];
                    var cols = new[] { DcAssets.Red, new Color(1f, 0.75f, 0f), DcAssets.Green };
                    for (int k = 0; k < 3; k++)
                    {
                        var g = Marker("lamp" + k, cols[k], 0.32f);
                        g.transform.position = Space3D.W(sg.pos, el.At(sg.pos, 0) + 4.6f + (2 - k) * 0.38f);
                        g.GetComponent<Renderer>().sharedMaterial = DcAssets.UnlitInstance(cols[k]);
                        lamps[k] = g.GetComponent<Renderer>();
                    }
                    signalLamps.Add(lamps);
                }
            }
        }

        Mesh MeshObj(string name, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mesh;
        }

        GameObject Marker(string name, Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Dc.KillCollider(go);
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<Renderer>().sharedMaterial = DcAssets.Unlit(c);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        GameObject Pedestrian()
        {
            var go = new GameObject("ped");
            go.transform.SetParent(root, false);
            var bodyG = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Dc.KillCollider(bodyG);
            bodyG.transform.SetParent(go.transform, false);
            bodyG.transform.localScale = new Vector3(0.48f, 0.62f, 0.32f);
            bodyG.transform.localPosition = new Vector3(0, 0.95f, 0);
            bodyG.GetComponent<Renderer>().sharedMaterial = DcAssets.Lit(DcAssets.Orange, 0.3f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Dc.KillCollider(head);
            head.transform.SetParent(go.transform, false);
            head.transform.localScale = Vector3.one * 0.26f;
            head.transform.localPosition = new Vector3(0, 1.72f, 0);
            head.GetComponent<Renderer>().sharedMaterial = DcAssets.Lit(DcAssets.Hex("#3A2A20"), 0.2f);
            return go;
        }

        void BuildTargetOutline(World w)
        {
            var tg = w.Target;
            if (tg.IsEmpty) return;
            float h = el.floor[Mathf.Clamp(tg.floor, 0, el.floor.Length - 1)] + 0.03f;
            var mk = new MeshKit();
            if (tg.kind == TargetKind.Line)
            {
                var o = tg.n.Orthogonal;
                mk.Line(tg.p - o * 2.2f, tg.p + o * 2.2f, 0.16f, h);
            }
            else
            {
                for (int i = 0; i < tg.poly.Length; i++) mk.Line(tg.poly[i], tg.poly[(i + 1) % tg.poly.Length], 0.12f, h);
                if (!tg.either)
                {
                    var c = tg.Center;
                    var d = V2.FromAngle(tg.heading * Geom.Deg2Rad);
                    var tip = c + d * 1.4f;
                    mk.Polygon(new[] { tip, tip - d * 0.6f + d.Orthogonal * 0.45f, tip - d * 0.6f - d.Orthogonal * 0.45f }, h);
                }
            }
            mk.ToObject("target-outline", targetLineMat, root);
        }

        /// <summary>매 프레임. 모든 안내를 지금 상태에 맞춘다.</summary>
        public void Tick(World w, float dt)
        {
            time += dt;
            var car = w.car;
            float gy = el.At(car.Center, w.floorI);
            bool play = w.state == PlayState.Play;
            // 지나갈 자리(주차·튜토리얼만: 서킷·도로에서는 앞을 가린다)
            bool showPath = play && w.st.mode == StageMode.Parking;
            UpdatePath(car, gy + 0.025f, showPath);
            // 목표(맥박처럼 밝기)
            var tg = w.Target;
            targetFill.Clear();
            if (!tg.IsEmpty)
            {
                float h = el.floor[Mathf.Clamp(tg.floor, 0, el.floor.Length - 1)] + 0.02f;
                var mk = new MeshKit();
                if (tg.kind == TargetKind.Line)
                {
                    var o = tg.n.Orthogonal;
                    mk.Polygon(new[] { tg.p - o * 2.2f - tg.n * tg.tol, tg.p + o * 2.2f - tg.n * tg.tol, tg.p + o * 2.2f + tg.n * tg.tol, tg.p - o * 2.2f + tg.n * tg.tol }, h);
                }
                else mk.Polygon(tg.poly, h);
                Fill(targetFill, mk);
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3f);
                DcAssets.Tint(targetFillMat, new Color(DcAssets.Lime.r, DcAssets.Lime.g, DcAssets.Lime.b, 0.14f + 0.12f * pulse));
            }
            // 튜토리얼 안내선·맞출 점
            var guides = w.Guides();
            var gmk = new MeshKit();
            markA.SetActive(false); markB.SetActive(false);
            foreach (var g in guides)
            {
                gmk.Line(g.a, g.b, 0.12f, gy + 0.035f);
                V2 mp = V2.Inf;
                if (g.mark == "shoulder") mp = car.Shoulder;
                else if (g.mark == "rear_bumper") mp = car.RearBumperCenter + car.Left * (car.width / 2f - 0.15f);
                else if (g.mark == "rear_axle") mp = car.pos;
                if (!mp.IsInf && !driverView)
                {
                    float y = gy + 1.25f;   // 차 위로 띄워 보이게(어깨 높이)
                    markA.SetActive(true); markB.SetActive(true);
                    markA.transform.position = Space3D.W(mp, y);
                    markB.transform.position = Space3D.W(mp, y);
                    markA.transform.localScale = Vector3.one * (0.42f + 0.06f * Mathf.Sin(time * 6f));
                }
            }
            Fill(guideMesh, gmk);
            // 센서 호
            var smk = new MeshKit();
            float worst = Mathf.Min(w.sensorFront, w.sensorRear);
            if (play)
            {
                SensorArc(smk, car, true, w.sensorFront, gy);
                SensorArc(smk, car, false, w.sensorRear, gy);
            }
            Fill(sensorMesh, smk);
            DcAssets.Tint(sensorMat, worst < 0.3f ? DcAssets.Red : (worst < 0.6f ? DcAssets.Orange : new Color(0.95f, 0.76f, 0f)));
            // 실패 자리
            bool fail = w.state == PlayState.Fail && !w.failPoint.IsInf;
            failRing.SetActive(fail);
            if (fail)
            {
                failRing.transform.position = Space3D.W(w.failPoint, el.At(w.failPoint, w.floorI) + 0.6f);
                failRing.transform.localScale = Vector3.one * (1.0f + 0.3f * Mathf.Sin(time * 8f));
            }
            // 보행자
            for (int i = 0; i < peds.Count && i < w.peds.Count; i++)
            {
                var p = w.peds[i];
                var go = peds[i];
                float bob = p.walking ? Mathf.Abs(Mathf.Sin(time * 9f + i)) * 0.05f : 0f;
                go.transform.position = Space3D.W(p.pos, el.At(p.pos, p.def.floor) + bob);
                go.transform.rotation = Space3D.Rot(p.facing);
            }
            // 신호등
            if (SignalHeads.Current != null) SignalHeads.Current.Tick(w);
            if (w.st.road != null)
            {
                for (int s = 0; s < signalLamps.Count; s++)
                {
                    var col = w.st.road.signals[s].At(w.t);
                    // 독일: 초록 직전 빨강+노랑(나라 표 redAmberSec, 한국 0 = 예전 그대로)
                    bool ra = CountryStyle.Current.redAmberSec > 0f && col == SignalColor.Red && w.st.road.signals[s].RedLeft(w.t) <= CountryStyle.Current.redAmberSec;
                    var on = new[] { col == SignalColor.Red, col == SignalColor.Yellow || ra, col == SignalColor.Green };
                    var cols = new[] { DcAssets.Red, new Color(1f, 0.75f, 0f), DcAssets.Green };
                    for (int k = 0; k < 3; k++)
                    {
                        signalLamps[s][k].gameObject.SetActive(true);
                        DcAssets.Tint(signalLamps[s][k].sharedMaterial, on[k] ? cols[k] * 1.4f : cols[k] * 0.18f);
                    }
                }
            }
        }

        static void Fill(Mesh target, MeshKit mk)
        {
            target.Clear();
            if (mk.IsEmpty) return;
            var built = mk.Build();
            target.SetVertices(built.vertices);
            target.SetUVs(0, built.uv);
            target.SetNormals(built.normals);
            target.SetTriangles(built.triangles, 0);
            target.RecalculateBounds();
            Dc.Kill(built);
        }

        /// <summary>view.gd _draw_path 와 같은 계산: 앞으로(D·N·P) 또는 뒤로(R) 6 m 동안 차가 쓸고 갈 자리</summary>
        void UpdatePath(Vehicle car, float y, bool show)
        {
            pathMesh.Clear(); innerMesh.Clear();
            if (!show) return;
            bool back = car.gear == Gear.R;
            float sgn = back ? -1f : 1f;
            float d = car.WheelAngle();
            bool turning = Mathf.Abs(d) > 0.004f;
            bool rightTurn = d > 0f;
            const int n = 32;
            var ea = new List<V2>(); var eb = new List<V2>(); var innerFront = new List<V2>();
            for (int k = 0; k <= n; k++)
            {
                car.PoseAfter(sgn * PathLen * k / n, out var p, out var h);
                var fwd = V2.FromAngle(h);
                var right = new V2(-Mathf.Sin(h), Mathf.Cos(h));
                var front = fwd * (car.wheelbase + car.frontOh);
                var rear = fwd * -car.rearOh;
                float hw = car.width / 2f;
                if (back) { ea.Add(p + rear - right * hw); eb.Add(p + rear + right * hw); }
                else if (turning)
                {
                    var outer = rightTurn ? -right : right;
                    ea.Add(p + front + outer * hw);
                    eb.Add(p - outer * hw);
                    innerFront.Add(p + fwd * car.wheelbase - outer * (car.trackF / 2f));
                }
                else { ea.Add(p + front - right * hw); eb.Add(p + front + right * hw); }
            }
            Band(pathMesh, ea, eb, y);
            if (showInner && innerFront.Count > 1) Band(innerMesh, innerFront, eb, y + 0.004f);
        }

        static void Band(Mesh m, List<V2> a, List<V2> b, float y)
        {
            int n = Mathf.Min(a.Count, b.Count);
            var v = new Vector3[n * 2];
            var tri = new List<int>();
            for (int i = 0; i < n; i++) { v[i * 2] = Space3D.W(a[i], y); v[i * 2 + 1] = Space3D.W(b[i], y); }
            for (int i = 0; i + 1 < n; i++)
            {
                int a0 = i * 2, b0 = i * 2 + 1, a1 = i * 2 + 2, b1 = i * 2 + 3;
                // 양면(위에서 보면 어느 쪽으로 감겼든 보이게)
                tri.AddRange(new[] { a0, a1, b1, a0, b1, b0, a0, b1, a1, a0, b0, b1 });
            }
            m.vertices = v;
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
        }

        static void SensorArc(MeshKit mk, Vehicle car, bool front, float dist, float gy)
        {
            if (dist > 1.2f) return;
            int rings = dist < 0.3f ? 3 : (dist < 0.6f ? 2 : 1);
            var fwd = front ? car.Dir : -car.Dir;
            var b = front ? car.FrontBumperCenter : car.RearBumperCenter;
            var c = b - fwd * 0.6f;
            float a0 = fwd.Angle;
            for (int k = 0; k < rings; k++)
            {
                float r = 0.85f + k * 0.22f;
                var pts = new List<V2>();
                for (int s = 0; s <= 12; s++) pts.Add(c + V2.FromAngle(a0 - 0.75f + 1.5f * s / 12f) * r);
                mk.Ribbon(pts, 0.08f, gy + 0.45f, false);
            }
        }

        public void Dispose() { Dc.Kill(root.gameObject); }
    }

    /// <summary>재생 중이든 편집기(캡처)든 안전하게 지우기</summary>
    public static class Dc
    {
        static System.Diagnostics.Stopwatch lapSw;
        /// <summary>동네를 여는 데 걸리는 시간을 단계별로 적는다("DC3D lap 이름 ms"). 어디가 느린지 보려고. 처음 부르면 시계를 켠다.</summary>
        public static void Lap(string label)
        {
            if (lapSw == null) { lapSw = System.Diagnostics.Stopwatch.StartNew(); return; }
            // 단계마다 메모리도 함께(스크립트 힙 사용/크기, Unity 사용/예약 MB), 어느 단계에서 부푸는지 보려고
            if (label != null) Debug.Log($"DC3D lap {label} {lapSw.ElapsedMilliseconds} | mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20}/{UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} unity {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20}/{UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20}");
            lapSw.Restart();
        }

        /// <summary>동네를 짓는 단계 사이에 다 쓴 임시 자료를 치운다(스크립트 힙이 최고치까지 부풀지 않게). 편집기 사진에서는 하지 않는다.</summary>
        public static void Trim()
        {
            if (Application.isPlaying) System.GC.Collect();
        }

        /// <summary>true 면 판을 연 뒤 약 10초에 주행 중 census·smaps 를 한 번 더 찍는다(그 프레임이 90 ms 남짓 멈춤). 평소에는 끔</summary>
        public static bool CensusInPlay = false;

        static global::Unity.Profiling.ProfilerRecorder gfxRec, sysRec;
        static bool recOn;
        static long Rec(ref global::Unity.Profiling.ProfilerRecorder r, string name)
        {
            if (!recOn)
            {
                recOn = true;
                try
                {
                    gfxRec = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Memory, "Gfx Used Memory");
                    sysRec = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Memory, "System Used Memory");
                }
                catch (System.Exception e) { Debug.LogWarning("recorder: " + e.Message); }
            }
            return r.Valid ? r.LastValue >> 20 : -1;
        }
        /// <summary>그래픽 메모리(텍스처·메시·그림판, Unity 추정 MB). release 빌드에서도 읽힌다. 못 읽으면 -1</summary>
        public static long GfxMB() => Rec(ref gfxRec, "gfx");
        /// <summary>운영체제가 보는 이 프로세스의 메모리(MB). 못 읽으면 -1</summary>
        public static long SysMB() => Rec(ref sysRec, "sys");

        /// <summary>
        /// 장면 속 물체 수와 메시 크기(정점·인덱스 버퍼 MB)를 한 줄로("DC3D census"). 무엇이 큰지, 줄인 효과가 기기에서 얼마인지 보려고.
        /// </summary>
        public static void Census(string tag)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int tfs = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                int rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                long vb = 0, ib = 0, verts = 0, readBytes = 0;
                int meshes = 0, readable = 0;
                foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                {
                    if (m == null) continue;
                    meshes++;
                    long vc = m.vertexCount, stride = 0;
                    for (int s = 0; s < m.vertexBufferCount; s++) stride += m.GetVertexBufferStride(s);
                    long ic = 0;
                    for (int sm = 0; sm < m.subMeshCount; sm++) ic += m.GetIndexCount(sm);
                    long b = vc * stride, bi = ic * (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
                    verts += vc; vb += b; ib += bi;
                    if (m.isReadable) { readable++; readBytes += b + bi; }
                }
                Debug.Log($"DC3D census {tag} transforms={tfs} meshRenderers={rends} meshes={meshes} vertices={verts} vbMB={vb >> 20} ibMB={ib >> 20} readable={readable} readableMB={readBytes >> 20} texMB={(long)(Texture.currentTextureMemory >> 20)} gfxUsedMB={GfxMB()} sysUsedMB={SysMB()} ms={sw.ElapsedMilliseconds}");
                Smaps(tag);
            }
            catch (System.Exception e) { Debug.LogWarning("census: " + e.Message); }
        }

        /// <summary>
        /// 이 프로세스의 /proc/self/smaps 를 이름별 PSS 합으로 줄여 한 줄("DC3D smaps"). adb 로는 읽을 수 없어(SELinux) 게임이 스스로 읽는다.
        /// 이름 없는 익명 영역은 크기(<1M, 1-8M, 8-64M, 64M+)로 묶는다(Unity 할당기 블록·스크립트 힙이 여기). 안드로이드(리눅스)에서만.
        /// </summary>
        static void Smaps(string tag)
        {
            const string path = "/proc/self/smaps";
            if (!System.IO.File.Exists(path)) return;
            var agg = new System.Collections.Generic.Dictionary<string, long>();
            var cnt = new System.Collections.Generic.Dictionary<string, int>();
            string cur = null; long total = 0;
            foreach (var line in System.IO.File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                char c0 = line[0];
                bool header = ((c0 >= '0' && c0 <= '9') || (c0 >= 'a' && c0 <= 'f')) && line.IndexOf('-') > 0 && line.IndexOf('-') < 17 && line.IndexOf(' ') > line.IndexOf('-');
                if (header)
                {
                    // 주소-주소 권한 오프셋 장치 inode [이름]
                    var parts = line.Split(new[] { ' ' }, 6, System.StringSplitOptions.RemoveEmptyEntries);
                    string name = parts.Length >= 6 ? parts[5].Trim() : "";
                    if (name.Length == 0)
                    {
                        var ab = parts[0].Split('-');
                        long size = (System.Convert.ToInt64(ab[1], 16) - System.Convert.ToInt64(ab[0], 16)) >> 20;
                        name = size < 1 ? "(anon <1M)" : size < 8 ? "(anon 1-8M)" : size < 64 ? "(anon 8-64M)" : "(anon 64M+)";
                    }
                    else if (name[0] == '/') { int k = name.LastIndexOf('/'); name = name.Substring(k + 1); }
                    else if (name.StartsWith("[anon:scudo")) name = "[anon:scudo]";
                    else if (name.StartsWith("[anon:dalvik")) name = "[anon:dalvik]";
                    else if (name.StartsWith("[anon:stack_and_tls")) name = "[anon:stack]";
                    cur = name;
                    cnt[cur] = cnt.TryGetValue(cur, out var n0) ? n0 + 1 : 1;
                    continue;
                }
                if (cur != null && line.StartsWith("Pss:"))
                {
                    var t = line.Substring(4).Trim();
                    int sp = t.IndexOf(' ');
                    if (sp > 0 && long.TryParse(t.Substring(0, sp), out long kb)) { agg[cur] = (agg.TryGetValue(cur, out var a0) ? a0 : 0) + kb; total += kb; }
                }
            }
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, long>>(agg);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new System.Text.StringBuilder();
            sb.Append("DC3D smaps ").Append(tag).Append(" pssMB=").Append(total >> 10);
            for (int i = 0; i < list.Count && i < 14; i++) sb.Append(" | ").Append(list[i].Key).Append('=').Append(list[i].Value >> 10).Append("MB x").Append(cnt[list[i].Key]);
            Debug.Log(sb.ToString());
        }

        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        public static void KillCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Kill(c);
        }
    }
}
