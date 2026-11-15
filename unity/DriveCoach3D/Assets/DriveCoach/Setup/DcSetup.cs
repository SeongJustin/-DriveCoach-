// 담당: S (3D 주차 연습 — 편집기 없이 bat 으로 프로젝트 준비 1단계: Unity 공식 패키지 받기)
// 이 어셈블리는 다른 우리 코드에 기대지 않는다: 다른 스크립트에 컴파일 오류가 있어도 이 단계는 돌아간다.
// 실행: Unity.exe -batchmode -quit -projectPath <프로젝트> -executeMethod DriveCoach.Setup.DcSetup.AddPackages
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace DriveCoach.Setup
{
    public static class DcSetup
    {
        /// <summary>URP(조명·그림자)를 편집기 버전에 맞는 판으로 받는다. 이미 있으면 그대로.</summary>
        public static void AddPackages()
        {
            int code = 0;
            TrimManifest();
            foreach (var pkg in new[] { "com.unity.render-pipelines.universal" })
            {
                if (!Add(pkg)) code = 1;
            }
            Debug.Log("DCSETUP packages done code=" + code);
            EditorApplication.Exit(code);
        }

        /// <summary>
        /// 처음 열 때 Unity 가 새 프로젝트 기본 패키지(결제·분석·협업 등)를 넣는다(11/8 첫 실행). 차 안 게임에 필요 없고,
        /// 결제(purchasing)는 결제 권한을, 분석(analytics·services)은 기기 정보를 밖으로 보내는 코드를 APK 에 넣으므로 뺀다.
        /// 필요한 모듈은 다른 패키지가 기대면 Package Manager 가 다시 넣는다.
        /// </summary>
        static readonly string[] Unwanted =
        {
            "com.unity.purchasing", "com.unity.analytics", "com.unity.services.core", "com.unity.services.analytics",
            "com.unity.collab-proxy", "com.unity.xr.legacyinputhelpers", "com.unity.multiplayer.center", "com.unity.ai.navigation",
            "com.unity.test-framework", "com.unity.ide.rider", "com.unity.timeline", "com.unity.2d.sprite", "com.unity.2d.tilemap",
            "com.unity.nuget.newtonsoft-json", "com.unity.modules.unityanalytics", "com.unity.modules.xr", "com.unity.modules.vehicles",
            "com.unity.modules.cloth", "com.unity.modules.wind", "com.unity.modules.video", "com.unity.modules.tilemap",
            "com.unity.modules.physics2d", "com.unity.modules.physicscore2d", "com.unity.modules.terrainphysics", "com.unity.modules.ai",
            "com.unity.modules.tetgen", "com.unity.modules.timelinefoundation", "com.unity.modules.director",
        };

        static void TrimManifest()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
            if (!File.Exists(path)) return;
            var lines = new List<string>(File.ReadAllLines(path));
            int removed = lines.RemoveAll(l =>
            {
                var t = l.Trim();
                foreach (var u in Unwanted) if (t.StartsWith("\"" + u + "\"")) return true;
                return false;
            });
            if (removed == 0) return;
            // dependencies 의 마지막 항목 뒤 쉼표 정리
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith("}") && i > 0 && lines[i - 1].TrimEnd().EndsWith(","))
                    lines[i - 1] = lines[i - 1].TrimEnd().TrimEnd(',');
            }
            File.WriteAllLines(path, lines);
            Debug.Log("DCSETUP manifest trimmed " + removed);
            Client.Resolve();
        }

        static bool Add(string id)
        {
            var list = Client.List(true, true);
            if (!Wait(list, 120)) { Debug.LogError("DCSETUP list timeout"); return false; }
            if (list.Status == StatusCode.Success)
            {
                foreach (var p in list.Result)
                {
                    if (p.name == id) { Debug.Log("DCSETUP already " + id + " " + p.version); return true; }
                }
            }
            var req = Client.Add(id);
            if (!Wait(req, 600)) { Debug.LogError("DCSETUP add timeout " + id); return false; }
            if (req.Status != StatusCode.Success) { Debug.LogError("DCSETUP add failed " + id + ": " + req.Error?.message); return false; }
            Debug.Log("DCSETUP added " + req.Result.name + " " + req.Result.version);
            return true;
        }

        static bool Wait(Request r, int seconds)
        {
            var until = DateTime.Now.AddSeconds(seconds);
            while (!r.IsCompleted && DateTime.Now < until) Thread.Sleep(200);
            return r.IsCompleted;
        }
    }
}
