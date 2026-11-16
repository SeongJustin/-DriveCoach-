// 3D 주차 연습 — 카메라 한 장 직접 그리기. URP 에서는 Camera.Render 대신 RenderPipeline.SubmitRenderRequest 를 쓴다
using UnityEngine;
using UnityEngine.Rendering;

namespace DriveCoach.Unity
{
    public static class DcRender
    {
        /// <summary>
        /// cam 을 지금 바로 rt 에 그린다(명령줄 화면 사진·편집기용). 게임 중에는 쓰지 않는다 — 게임 중 보조 카메라는
        /// enabled 를 켜고 꺼서 렌더 루프가 그리게 한다(번갈아 그리기).
        /// </summary>
        public static void Now(Camera cam, RenderTexture rt)
        {
            if (cam == null || rt == null) return;
            var old = cam.targetTexture;
            try
            {
                if (GraphicsSettings.currentRenderPipeline != null)
                {
                    var req = new RenderPipeline.StandardRequest();
                    if (RenderPipeline.SupportsRenderRequest(cam, req))
                    {
                        req.destination = rt;
                        RenderPipeline.SubmitRenderRequest(cam, req);
                        return;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("DcRender request: " + e.Message); }
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
        }
    }
}
