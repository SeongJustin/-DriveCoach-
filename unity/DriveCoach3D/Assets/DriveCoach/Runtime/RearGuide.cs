// 담당: S (3D 주차 연습 — 후방 카메라 안내선(실제 차 후방 카메라처럼). 11/8 제안 "후방 카메라에 거리선·진행선" 수락)
//  - 거리선: 뒤 범퍼에서 0.5 m(빨강)·1 m(노랑)·2 m(초록) 가로 막대와 양옆 세로선(차 폭)
//  - 진행선: 지금 핸들 각으로 뒤로 갈 때 차 양옆이 지나갈 곡선(주황, 3 m)
// 후방 카메라에만 보이는 층(29)에 그린다(화면 카메라·거울에는 안 보임).
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class RearGuide
    {
        public const int Layer = 29;
        readonly GameObject root;
        readonly Mesh path;
        readonly float halfW, rearZ, axleZ, wheelbase;
        float lastWheel = float.NaN;

        public RearGuide(Vehicle v, Transform carRoot)
        {
            root = new GameObject("RearGuide");
            root.transform.SetParent(carRoot, false);
            root.layer = Layer;
            halfW = v.width / 2f;
            float centerOff = (v.wheelbase + v.frontOh - v.rearOh) / 2f;
            axleZ = -centerOff;
            rearZ = -v.length / 2f;
            wheelbase = v.wheelbase;
            // 거리선(고정)
            Bar(0.5f, DcAssets.Red, "d05");
            Bar(1.0f, DcAssets.Yellow, "d10");
            Bar(2.0f, DcAssets.Green, "d20");
            var sides = new MeshKit();
            foreach (var sx in new[] { -1f, 1f })
                Seg(sides, new Vector3(sx * halfW, 0.03f, rearZ - 0.3f), new Vector3(sx * halfW, 0.03f, rearZ - 2.0f), 0.05f);
            Obj("sides", sides.Build("rear-sides"), DcAssets.Unlit(new Color(1f, 1f, 1f, 1f)));
            path = new Mesh { name = "rear-path" };
            path.MarkDynamic();
            Obj("path", path, DcAssets.Unlit(DcAssets.Orange));
            root.SetActive(false);
        }

        void Bar(float d, Color c, string name)
        {
            var mk = new MeshKit();
            float z = rearZ - d;
            // 가운데가 빈 막대(양옆 0.45 m 씩) — 실제 후방 카메라 모양
            foreach (var sx in new[] { -1f, 1f })
                Seg(mk, new Vector3(sx * halfW, 0.03f, z), new Vector3(sx * (halfW - 0.45f), 0.03f, z), 0.06f);
            Obj(name, mk.Build("rear-" + name), DcAssets.Unlit(c));
        }

        static void Seg(MeshKit mk, Vector3 a, Vector3 b, float w)
        {
            var d = (b - a).normalized;
            var side = Vector3.Cross(Vector3.up, d) * (w / 2f);
            mk.Quad(a - side, b - side, b + side, a + side, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }

        void Obj(string name, Mesh m, Material mat)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>R 일 때만 보이고, 핸들 각이 바뀌면 진행선을 다시 그린다</summary>
        public void Tick(Vehicle v)
        {
            bool on = v.gear == Gear.R;
            if (root.activeSelf != on) root.SetActive(on);
            if (!on) return;
            float wheel = v.WheelAngle();
            if (Mathf.Abs(wheel - lastWheel) < 0.002f) return;
            lastWheel = wheel;
            // 뒷축 가운데에서 뒤로 0.1 m 씩: 방향(오른쪽으로 돌수록 +) += ds·tan(바퀴각)/축거 (ds 는 뒤로 가므로 음수)
            var mk = new MeshKit();
            float yaw = 0f;
            var pos = new Vector3(0f, 0.03f, axleZ);
            var prevL = Vector3.zero; var prevR = Vector3.zero;
            bool started = false;
            for (float s = 0f; s <= 3.0f + (axleZ - rearZ); s += 0.1f)
            {
                var dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var right = new Vector3(dir.z, 0f, -dir.x);
                var l = pos - right * halfW; var r = pos + right * halfW;
                if (pos.z <= rearZ - 0.1f || started)
                {
                    if (started)
                    {
                        Seg(mk, prevL, l, 0.07f);
                        Seg(mk, prevR, r, 0.07f);
                    }
                    started = true;
                }
                prevL = l; prevR = r;
                float ds = -0.1f;
                yaw += ds * Mathf.Tan(wheel) / wheelbase;
                pos += dir * ds;
            }
            var built = mk.Build("rear-path");
            path.Clear();
            path.SetVertices(built.vertices);
            path.SetNormals(built.normals);
            path.SetUVs(0, built.uv);
            path.SetTriangles(built.triangles, 0);
            path.RecalculateBounds();
            Dc.Kill(built);
        }

        public void Dispose() { Dc.Kill(root); }
    }
}
