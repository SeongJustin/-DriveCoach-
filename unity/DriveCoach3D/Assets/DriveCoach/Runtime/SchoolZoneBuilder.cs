// 담당: S (11/9 새벽, 조사 P1 "스쿨존 세트: 빨간 노면 …")
// 어린이보호구역 입구의 빨간 노면(미끄럼 방지 포장): 지역 파일에 아스팔트 안만 잘라 둔 삼각형("szr")을
// 아스팔트(h + 0.03) 와 칠(h + 0.043) 사이 높이에 깐다 — 차선·글자 칠은 그 위로 보인다.
// 구역 안 제한 속도 30·신호 없는 횡단보도 일시정지는 Core(RoadRun.SchoolZoneRules).
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class SchoolZoneBuilder
    {
        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null || d.schoolRed.Length < 6) return;
            var mk = new MeshKit { uvScale = 1.2f };     // 알갱이 질감이 1.2 m 마다 되풀이
            var a = d.schoolRed;
            for (int i = 0; i + 5 < a.Length; i += 6)
                mk.TriUp(Space3D.W(new V2(a[i], a[i + 1]), h + 0.036f), Space3D.W(new V2(a[i + 2], a[i + 3]), h + 0.036f), Space3D.W(new V2(a[i + 4], a[i + 5]), h + 0.036f));
            mk.ToObject("school-zone-red", DcAssets.LitTex("school-red", CityTextures.RedPave(), 0.12f), node, false, true);
            Debug.Log($"SchoolZoneBuilder: 어린이보호구역 {d.schoolZones.Count}, 빨간 노면 삼각형 {a.Length / 6}");
        }
    }
}
