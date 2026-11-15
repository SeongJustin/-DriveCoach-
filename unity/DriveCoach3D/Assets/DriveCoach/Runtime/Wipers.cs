// 담당: S (11/8 밤, 조사 P1 "앞유리 빗방울 + 와이퍼를 실차 스위치와"): 내 차 와이퍼가 실제로 움직이고, 비 오면 앞유리에 빗방울이 맺혔다가 닦인다.
//   와이퍼 속도: 실제 차 스위치(간헐 4초에 한 번, 느리게 1.1초, 빠르게 0.7초)를 따르고, 차가 값을 안 주면 비·눈일 때 느리게.
//   빗방울: 운전석 시점에서만 카메라 바로 앞 판(코드로 만든 물방울 그림)이 시간이 지나면 짙어지고, 와이퍼가 지나간 쪽(왼·오른 반)부터 지워진다.
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class Wipers
    {
        readonly Transform pivL, pivR;
        float phase;          // 0..1 한 번 왕복
        bool moving;
        float wait;           // 간헐: 다음 왕복까지
        bool rainSeen;
        // 빗방울
        GameObject drops;
        Material dropL, dropR;
        float wetL, wetR;
        static Texture2D dropTex;

        Wipers(Transform l, Transform r) { pivL = l; pivR = r; }

        /// <summary>직접 만든 차(CarBody)의 …wipers-l/r 를 오른쪽 끝을 축으로 돌릴 수 있게 감싼다(없으면 빗방울만)</summary>
        public static Wipers Make(Transform carRoot)
        {
            Transform l = null, r = null;
            foreach (var t in carRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("wipers-l")) l = Wrap(t);
                else if (t.name.EndsWith("wipers-r")) r = Wrap(t);
            }
            // 날을 못 찾아도(예전 차 모델) 앞유리 빗방울은 보이게 만든다
            return new Wipers(l, r);
        }

        static Transform Wrap(Transform blade)
        {
            var mf = blade.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            var b = mf.sharedMesh.bounds;
            // 축: 날의 오른쪽 끝(x 최대) — 날은 축에서 왼쪽으로 누워 있다(왼쪽 운전석 차의 나란한 와이퍼)
            var pivotLocal = new Vector3(b.max.x - 0.03f, b.center.y, b.center.z);
            var piv = new GameObject(blade.name + "-pivot").transform;
            piv.SetParent(blade.parent, false);
            piv.localPosition = blade.localPosition + blade.localRotation * pivotLocal;
            piv.localRotation = Quaternion.identity;
            blade.SetParent(piv, true);
            piv.gameObject.layer = blade.gameObject.layer;
            return piv;
        }

        /// <summary>mode: -1 모름, 0 끔, 1 간헐, 2 느리게, 3 빠르게. rain: 비·눈. driverCam: 운전석 시점의 카메라(아니면 null)</summary>
        public void Tick(float dt, int mode, bool rain, Camera driverCam)
        {
            // 차가 와이퍼 값을 안 주면(모름): 비·눈일 때 간헐(가장 흔히 쓰는 단) — 빗방울이 맺혔다 닦이는 게 보인다
            if (mode < 0) mode = rain ? 1 : 0;
            // 비 오는 중에 출발하면 앞유리는 이미 젖어 있다
            if (rain && !rainSeen) { rainSeen = true; wetL = wetR = 0.7f; wait = 2.5f; }
            if (!rain) rainSeen = false;
            float period = mode == 3 ? 0.7f : 1.1f;
            if (!moving)
            {
                wait -= dt;
                if (mode == 2 || mode == 3 || (mode == 1 && wait <= 0f)) { moving = true; phase = 0f; }
            }
            if (moving)
            {
                float before = phase;
                phase += dt / period;
                // 올라가며 왼쪽 반, 내려오며 오른쪽 반을 닦는다(앞 유리 기준)
                if (before < 0.25f && phase >= 0.25f) wetL = 0f;
                if (before < 0.5f && phase >= 0.5f) wetR = 0f;
                if (phase >= 1f) { phase = 0f; moving = false; wait = mode == 1 ? 4f : 0f; }
            }
            float ang = moving ? Mathf.Sin(phase * Mathf.PI) * 82f : 0f;
            // 앞유리 기울기(약 28°)에 수직인 축으로 돌린다: 위에서 볼 때 반시계(날이 위로 일어선다)
            var axis = new Vector3(0f, 0.88f, 0.47f);
            if (pivL != null) pivL.localRotation = Quaternion.AngleAxis(-ang, axis);
            if (pivR != null) pivR.localRotation = Quaternion.AngleAxis(-ang, axis);

            // 빗방울: 비 오는 동안 4초면 다 맺힌다(빠른 와이퍼면 거의 안 보인다)
            if (rain) { wetL = Mathf.Min(1f, wetL + dt / 4f); wetR = Mathf.Min(1f, wetR + dt / 4f); }
            else { wetL = Mathf.Max(0f, wetL - dt / 3f); wetR = Mathf.Max(0f, wetR - dt / 3f); }
            Drops(driverCam);
        }

        void Drops(Camera cam)
        {
            bool show = cam != null && (wetL > 0.02f || wetR > 0.02f);
            if (!show) { if (drops != null) drops.SetActive(false); return; }
            if (drops == null)
            {
                drops = new GameObject("windshield-drops");
                var tex = DropTex();
                dropL = MakeMat(tex);
                dropR = MakeMat(tex);
                for (int s = 0; s < 2; s++)
                {
                    var mk = new MeshKit();
                    float x0 = s == 0 ? -1f : 0f, x1 = s == 0 ? 0f : 1f;
                    mk.Quad(new Vector3(x0, -1f, 0f), new Vector3(x1, -1f, 0f), new Vector3(x1, 1f, 0f), new Vector3(x0, 1f, 0f), Vector3.back,
                        new Vector2(x0 * 1.6f, 0f), new Vector2(x1 * 1.6f, 0f), new Vector2(x1 * 1.6f, 1.8f), new Vector2(x0 * 1.6f, 1.8f));
                    mk.ToObject(s == 0 ? "drops-l" : "drops-r", s == 0 ? dropL : dropR, drops.transform, false, false);
                }
            }
            if (drops.transform.parent != cam.transform) drops.transform.SetParent(cam.transform, false);
            drops.SetActive(true);
            // 카메라 바로 앞(가까운 면 + 3 cm), 화면을 꽉 채우게
            // (11/15 대표: 비가 대시보드와 스티어링 휠에도 묻어 보인다) 판을 눈앞 3 cm 가 아니라 앞유리쯤(0.8 m)에 둔다.
            //   그보다 가까운 실내(스티어링 휠, 계기판, 기둥)가 판을 가려 유리에만 맺혀 보인다
            float z = Mathf.Max(cam.nearClipPlane + 0.03f, 0.8f);
            float hh = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * z;
            drops.transform.localPosition = new Vector3(0f, 0f, z);
            drops.transform.localRotation = Quaternion.identity;
            drops.transform.localScale = new Vector3(hh * cam.aspect, hh, 1f);
            DcAssets.Tint(dropL, new Color(1f, 1f, 1f, 0.55f * wetL));
            DcAssets.Tint(dropR, new Color(1f, 1f, 1f, 0.55f * wetR));
        }

        static Material MakeMat(Texture2D tex)
        {
            var m = new Material(DcAssets.Unlit(new Color(1f, 1f, 1f, 0.5f)));
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            m.renderQueue = 3100;
            return m;
        }

        /// <summary>물방울 그림(256×256, 반복): 작은 동그라미마다 아래쪽이 밝고 가장자리가 짙은 렌즈 모양</summary>
        static Texture2D DropTex()
        {
            if (dropTex != null) return dropTex;
            const int S = 256;
            var px = new Color32[S * S];
            var rnd = new System.Random(5);
            for (int k = 0; k < 140; k++)
            {
                float cx = (float)rnd.NextDouble() * S, cy = (float)rnd.NextDouble() * S;
                float r = 2f + (float)rnd.NextDouble() * (rnd.NextDouble() < 0.15 ? 7f : 3.5f);
                int r0 = (int)Mathf.Ceil(r) + 1;
                for (int dy = -r0; dy <= r0; dy++)
                    for (int dx = -r0; dx <= r0; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        if (d > 1f) continue;
                        int x = ((int)cx + dx + S) % S, y = ((int)cy + dy + S) % S;
                        float rim = Mathf.SmoothStep(0.55f, 1f, d);
                        float lit = Mathf.Clamp01(-dy / r) * (1f - d);
                        byte a = (byte)Mathf.Clamp(40 + rim * 150 + lit * 60, 0, 230);
                        byte c = (byte)Mathf.Clamp(150 + lit * 105 - rim * 70, 0, 255);
                        var old = px[y * S + x];
                        if (a > old.a) px[y * S + x] = new Color32(c, c, (byte)Mathf.Min(255, c + 10), a);
                    }
            }
            dropTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "dc-drops" };
            dropTex.SetPixels32(px);
            dropTex.Apply(true, true);
            return dropTex;
        }

        public void Dispose() { if (drops != null) Dc.Kill(drops); }
    }
}
