// 내 동네 횡단보도를 건너는 사람(Core.CrossPeds)을 그린다: 몸통·머리·두 다리·두 팔(낮은 다각형), 옷 색 8 가지,
// 걸을 때 다리·팔이 앞뒤로 흔들리고 몸이 살짝 위아래로. 사람 수가 적어(10 명 안팎) 하나씩 둔다.
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class PedView
    {
        static readonly Color[] Shirts =
        {
            DcAssets.Hex("#2E4A7A"), DcAssets.Hex("#B8463A"), DcAssets.Hex("#E8E6E0"), DcAssets.Hex("#3C3F44"),
            DcAssets.Hex("#6B8E5A"), DcAssets.Hex("#C9A15A"), DcAssets.Hex("#7A5A8E"), DcAssets.Hex("#D9D2C2"),
        };
        static readonly Color[] Pants = { DcAssets.Hex("#23262B"), DcAssets.Hex("#3B4D6B"), DcAssets.Hex("#5A5248"), DcAssets.Hex("#2B2B2B") };

        class View { public GameObject go; public Transform legL, legR, armL, armR; public float phase; }

        readonly Transform root;
        readonly Elevation el;
        readonly Dictionary<CrossPed, View> views = new Dictionary<CrossPed, View>();

        public PedView(Transform parent, Elevation elev)
        {
            root = new GameObject("CrossPeds").transform;
            root.SetParent(parent, false);
            el = elev;
        }

        public void Tick(CrossPeds cp, float dt)
        {
            if (views.Count > 0)
            {
                List<CrossPed> gone = null;
                foreach (var k in views.Keys) if (!cp.peds.Contains(k)) (gone ??= new List<CrossPed>()).Add(k);
                if (gone != null) foreach (var k in gone) { Dc.Kill(views[k].go); views.Remove(k); }
            }
            foreach (var p in cp.peds)
            {
                if (!views.TryGetValue(p, out var v)) { v = Make(p); views[p] = v; }
                if (p.walking) v.phase += dt * p.speed * 4.2f;
                float sw = p.walking ? Mathf.Sin(v.phase) : 0f;
                float bob = p.walking ? Mathf.Abs(Mathf.Cos(v.phase)) * 0.035f : 0f;
                v.go.transform.position = Space3D.W(p.pos, el.At(p.pos, 0) + 0.14f * (p.onRoad ? 0f : 1f) + bob);
                v.go.transform.rotation = Space3D.Rot(p.facing);
                v.legL.localRotation = Quaternion.Euler(sw * 28f, 0f, 0f);
                v.legR.localRotation = Quaternion.Euler(-sw * 28f, 0f, 0f);
                v.armL.localRotation = Quaternion.Euler(-sw * 22f, 0f, 0f);
                v.armR.localRotation = Quaternion.Euler(sw * 22f, 0f, 0f);
            }
        }

        View Make(CrossPed p)
        {
            var go = new GameObject("ped");
            go.transform.SetParent(root, false);
            var shirt = DcAssets.Lit(Shirts[p.look % Shirts.Length], 0.25f);
            var pants = DcAssets.Lit(Pants[p.look % Pants.Length], 0.2f);
            var skin = DcAssets.Lit(DcAssets.Hex("#C9A184"), 0.3f);
            var hair = DcAssets.Lit(DcAssets.Hex("#1E1A17"), 0.2f);
            float s = 0.92f + (p.look % 3) * 0.05f;      // 키 조금씩 다르게
            // 둥근 몸: 몸통·머리·팔다리를 회전체(단면 고리를 이어 붙임)로
            var shoe = DcAssets.Lit(DcAssets.Hex("#1B1B1D"), 0.25f);
            Round(go.transform, "torso", new Vector3(0f, 0.90f * s, 0f), shirt,
                new[] { 0f, 0.06f * s, 0.30f * s, 0.50f * s, 0.56f * s, 0.60f * s }, new[] { 0.150f, 0.172f, 0.180f, 0.205f, 0.170f, 0.060f }, new[] { 0.095f, 0.112f, 0.118f, 0.122f, 0.100f, 0.050f });
            Round(go.transform, "neck", new Vector3(0f, 1.47f * s, 0f), skin, new[] { 0f, 0.07f * s }, new[] { 0.048f, 0.045f }, new[] { 0.050f, 0.047f });
            Round(go.transform, "head", new Vector3(0f, 1.50f * s, 0.005f), skin,
                new[] { 0f, 0.025f, 0.07f, 0.13f, 0.185f, 0.22f, 0.235f }, new[] { 0.045f, 0.078f, 0.094f, 0.098f, 0.085f, 0.052f, 0.010f }, new[] { 0.050f, 0.086f, 0.104f, 0.108f, 0.094f, 0.058f, 0.010f });
            Round(go.transform, "hair", new Vector3(0f, 1.50f * s + 0.105f, -0.012f), hair,
                new[] { 0f, 0.04f, 0.09f, 0.125f, 0.140f }, new[] { 0.103f, 0.104f, 0.090f, 0.056f, 0.010f }, new[] { 0.112f, 0.113f, 0.098f, 0.062f, 0.010f });
            var v = new View { go = go, phase = p.look };
            v.legL = RoundLimb(go.transform, "leg-l", new Vector3(-0.085f, 0.92f * s, 0f), 0.86f * s, 0.082f, 0.058f, pants, shoe);
            v.legR = RoundLimb(go.transform, "leg-r", new Vector3(0.085f, 0.92f * s, 0f), 0.86f * s, 0.082f, 0.058f, pants, shoe);
            v.armL = RoundLimb(go.transform, "arm-l", new Vector3(-0.235f, 1.42f * s, 0f), 0.60f * s, 0.052f, 0.040f, shirt, null, skin);
            v.armR = RoundLimb(go.transform, "arm-r", new Vector3(0.235f, 1.42f * s, 0f), 0.60f * s, 0.052f, 0.040f, shirt, null, skin);
            return v;
        }

        static void Box(Transform parent, string name, Vector3 c, Vector3 size, Material m)
        {
            var mk = new MeshKit();
            mk.Box(c, size, 0f);
            mk.ToObject(name, m, parent, true, true);
        }

        /// <summary>회전체: 바닥(baseC)에서 위로 ys[i] 높이마다 반지름(옆 rx, 앞뒤 rz)의 고리를 두고 이어 붙인다(12각)</summary>
        static void Round(Transform parent, string name, Vector3 baseC, Material m, float[] ys, float[] rx, float[] rz)
        {
            const int N = 12;
            var secs = new List<Vector3[]>();
            for (int i = 0; i < ys.Length; i++)
            {
                var ring = new Vector3[N + 1];
                for (int k = 0; k <= N; k++)
                {
                    float a = k % N * Mathf.PI * 2f / N;
                    ring[k] = baseC + new Vector3(Mathf.Cos(a) * rx[i], ys[i], Mathf.Sin(a) * rz[i]);
                }
                secs.Add(ring);
            }
            var mk = new MeshKit();
            mk.Loft(secs, false, 0.5f);
            mk.ToObject(name, m, parent, true, true);
        }

        /// <summary>어깨·엉덩이(pivot)에 매단 둥근 팔·다리: pivot 아래로 len 만큼, 위 반지름 r0 → 아래 r1. 다리는 끝에 신발, 팔은 끝에 손</summary>
        static Transform RoundLimb(Transform parent, string name, Vector3 pivot, float len, float r0, float r1, Material m, Material shoe, Material hand = null)
        {
            var piv = new GameObject(name).transform;
            piv.SetParent(parent, false);
            piv.localPosition = pivot;
            float end = shoe != null ? 0.05f : (hand != null ? 0.09f : 0f);
            Round(piv, name + "-mesh", new Vector3(0f, -len + end, 0f), m,
                new[] { 0f, 0.02f, (len - end) * 0.5f, len - end - 0.03f, len - end }, new[] { r1 * 0.8f, r1, (r0 + r1) / 2f, r0, r0 * 0.75f }, new[] { r1 * 0.8f, r1, (r0 + r1) / 2f, r0, r0 * 0.75f });
            if (shoe != null)
                Round(piv, name + "-shoe", new Vector3(0f, -len, 0.035f), shoe, new[] { 0f, 0.015f, 0.05f, 0.065f }, new[] { 0.040f, 0.052f, 0.050f, 0.030f }, new[] { 0.095f, 0.115f, 0.105f, 0.060f });
            if (hand != null)
                Round(piv, name + "-hand", new Vector3(0f, -len, 0f), hand, new[] { 0f, 0.02f, 0.06f, 0.09f }, new[] { 0.018f, 0.034f, 0.038f, 0.030f }, new[] { 0.020f, 0.040f, 0.044f, 0.034f });
            return piv;
        }

        public void Dispose() { Dc.Kill(root.gameObject); }
    }
}
