// 비·눈 알갱이, 젖은 노면.
// 알갱이는 입자 시스템 대신 메시 한 덩이(카메라 둘레 36 × 18 × 36 m 상자에 빗줄기·눈송이를 고르게 뿌린 것)를
// 위아래 두 장 겹쳐 카메라를 따라가며 아래로 흘려보낸다(정점을 매 프레임 고치지 않아 가볍다). 빗줄기는 십자로 세운 가는 판.
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public class WeatherFx
    {
        const float BoxW = 36f, BoxH = 18f;
        readonly GameObject root;
        readonly Transform a, b;
        readonly float speed, drift;
        float fall;

        WeatherFx(Transform parent, bool snow)
        {
            root = new GameObject(snow ? "Snow" : "Rain");
            root.transform.SetParent(parent, false);
            var mk = new MeshKit();
            var rnd = new System.Random(snow ? 11 : 7);
            int n = snow ? 1500 : 2200;
            for (int i = 0; i < n; i++)
            {
                var c = new Vector3((float)(rnd.NextDouble() - 0.5) * BoxW, (float)rnd.NextDouble() * BoxH, (float)(rnd.NextDouble() - 0.5) * BoxW);
                if (snow)
                {
                    float s = 0.025f + (float)rnd.NextDouble() * 0.025f;
                    Cross(mk, c, s, s, 0f);
                }
                else
                {
                    float len = 0.45f + (float)rnd.NextDouble() * 0.3f;
                    Cross(mk, c, 0.008f, len, 0.06f);
                }
            }
            var mat = DcAssets.Unlit(snow ? new Color(0.96f, 0.97f, 1f, 0.85f) : new Color(0.78f, 0.82f, 0.9f, 0.32f));
            a = mk.ToObject("fx-a", mat, root.transform).transform;
            b = Object.Instantiate(a.gameObject, root.transform).transform;
            speed = snow ? 1.3f : 9f;
            drift = snow ? 0.35f : 0f;
        }

        /// <summary>가는 판 두 장을 십자로(어느 쪽에서 봐도 보이게). tilt = 아래로 갈수록 옆으로(바람)</summary>
        static void Cross(MeshKit mk, Vector3 c, float w, float h, float tilt)
        {
            var up = new Vector3(tilt, 1f, 0f).normalized * (h / 2f);
            foreach (var side in new[] { Vector3.right, Vector3.forward })
            {
                var r = side * (w / 2f);
                var n = Vector3.Cross(side, Vector3.up);
                mk.Quad(c - r - up, c + r - up, c + r + up, c - r + up, n, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                mk.Quad(c + r - up, c - r - up, c - r + up, c + r + up, -n, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
        }

        /// <summary>weather: 2 비 · 4 눈 이면 만든다(그 밖은 null)</summary>
        public static WeatherFx Make(Transform parent, int weather)
        {
            if (weather == 2) return new WeatherFx(parent, false);
            if (weather == 4) return new WeatherFx(parent, true);
            return null;
        }

        public void Tick(Camera cam, float dt)
        {
            if (cam == null) return;
            fall = (fall + speed * dt) % BoxH;
            var p = cam.transform.position;
            // 칸 단위로 옮겨(빗줄기가 카메라와 함께 미끄러져 보이지 않게) 상자 가운데에 카메라
            float sx = Mathf.Floor(p.x / 4f) * 4f, sz = Mathf.Floor(p.z / 4f) * 4f;
            float baseY = p.y - BoxH * 0.5f - fall;
            float dx = Mathf.Sin(Time.time * 0.7f) * drift;
            a.position = new Vector3(sx + dx, baseY, sz);
            b.position = new Vector3(sx + dx, baseY + BoxH, sz);
        }

        public void Dispose() { Dc.Kill(root); }
    }
}
