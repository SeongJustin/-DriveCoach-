// 거울(11/15 코드 정리로 Cockpit.cs 에서 나눈 파일).
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    /// <summary>
    /// 거울 카메라 3대(차에 붙어 있음). 실내 거울 판과 화면 거울(Hud)이 같은 그림을 쓴다.
    /// 무겁지 않게 한 프레임에 한 대씩 번갈아 그린다(60 fps 면 거울마다 20 fps).
    /// </summary>
    public class MirrorRig
    {
        public readonly GameObject root;
        public readonly Camera left, right, center;
        public readonly RenderTexture texL, texR, texC;
        int frame;

        public MirrorRig(Vehicle v, Transform carRoot, bool light)
        {
            root = new GameObject("Mirrors");
            root.transform.SetParent(carRoot, false);
            var eye = Cockpit.Eye(v);
            int k = light ? 1 : 2;
            texL = new RenderTexture(192 * k, 112 * k, 24);   // 24비트 깊이(16 비트면 차 옆 이음선 데칼이 거울 속에서 깜빡임 — 차 모델 검토)
            texR = new RenderTexture(192 * k, 112 * k, 24);
            texC = new RenderTexture(240 * k, 76 * k, 24);
            float sideX = v.width / 2f + 0.12f, mz = eye.z + 0.75f;
            // 왼쪽: 평면 거울. 오른쪽(조수석): 볼록 거울이라 더 넓게 보인다("사물이 거울에 보이는 것보다 가까이 있음")
            left = Cam("MirrorCamL", new Vector3(-sideX, 1.05f, mz), 3f, 196f, 30f, texL);
            right = Cam("MirrorCamR", new Vector3(sideX, 1.05f, mz), 3f, 164f, 37f, texR);
            center = Cam("MirrorCamC", new Vector3(0f, 1.32f, eye.z - 0.55f), 4f, 180f, 21f, texC);
        }

        Camera Cam(string name, Vector3 local, float pitch, float yaw, float fov, RenderTexture tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            var c = go.AddComponent<Camera>();
            c.fieldOfView = fov;
            c.nearClipPlane = 0.2f;
            c.farClipPlane = 140f;
            c.targetTexture = tex;
            c.cullingMask = ~(1 << Cockpit.RearCabinLayer);
            c.depth = -10f;
            c.allowMSAA = false;
            c.enabled = false;
            return c;
        }

        /// <summary>active: 지금 거울이 보이는 곳이 있나(운전석 실내 또는 화면 거울). 없으면 아무 거울도 그리지 않는다.</summary>
        /// <summary>느린 기기(Perf 단계): 거울을 몇 프레임에 한 번 그리나(1 = 매 프레임 하나씩 돌아가며)</summary>
        public int every = 1;

        public void Tick(bool active)
        {
            frame++;
            int f = frame / Mathf.Max(1, every);
            bool turn = frame % Mathf.Max(1, every) == 0;
            int k = turn ? f % 3 : -1;
            left.enabled = active && k == 0;
            right.enabled = active && k == 1;
            center.enabled = active && k == 2;
        }

        /// <summary>화면 사진(DcBuild.Shots)용: 셋 다 지금 바로 그린다</summary>
        public void RenderNow()
        {
            DcRender.Now(left, texL);
            DcRender.Now(right, texR);
            DcRender.Now(center, texC);
        }

        public void Dispose() { texL.Release(); texR.Release(); texC.Release(); }
    }
}
