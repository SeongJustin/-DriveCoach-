// 버스정류장: 상표·글자 없는 일반 모양.
//   길가: 연석 0.6 m 뒤 표지 기둥(2.8 m, 위에 판) + 연석 2 m 뒤 쉘터(3.6 × 1.4 m: 지붕·뒷벽 유리·옆 유리·의자·노선 안내판 자리).
//   중앙(서울 중앙버스전용차로, 노면에 이미 있는 중앙 섬 위만): 섬 길이만큼 긴 지붕(기둥 3 m 마다)·양쪽 낮은 난간·의자, 섬 끝에 표지 기둥.
//   중앙 섬이 없는 곳(kind 2)은 세우지 않는다(차로를 막는 섬을 새로 만들지 않는다).
// 정류장 10 m 안 정차 위반 규칙은 넣지 않는다.
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class BusStopBuilder
    {
        public static void Build(RoadDef r, float h, Transform node)
        {
            var d = r.region;
            if (d == null || d.busStops.Count == 0) return;
            var metal = new MeshKit();     // 기둥·지붕 틀·의자 다리(진회색 금속)
            var roof = new MeshKit();      // 지붕 판(밝은 회색)
            var glass = new MeshKit();     // 유리(옅은 청회색, 반투명)
            var plate = new MeshKit();     // 표지판(남색)
            var mark = new MeshKit();      // 표지판 안 흰 그림 칸(버스 모양을 단순한 네모로)
            var board = new MeshKit();     // 노선 안내판(어두운 판 + 밝은 칸)
            var bench = new MeshKit();     // 의자(나무색)
            var surf = CityBuilder.Current;
            int n = 0;
            foreach (var s in d.busStops)
            {
                if (s.kind == 2) continue;
                n++;
                float y0 = h + (surf != null && surf.Sidewalk(s.sign) ? CityBuilder.WalkH : 0.15f);
                Pole(s.sign, s.heading, y0, metal, plate, mark);
                if (!s.hasShelter) continue;
                float ys = h + (surf != null && surf.Sidewalk(s.shelter) ? CityBuilder.WalkH : 0.15f);
                if (s.kind == 0) Curb(s, ys, metal, roof, glass, board, bench);
                else Median(s, ys, metal, roof, glass, bench);
            }
            if (n == 0) return;
            metal.ToObject("busstop-metal", DcAssets.Lit(DcAssets.Hex("#3A3F45"), 0.45f, 0.6f), node, true, true);
            if (!roof.IsEmpty) roof.ToObject("busstop-roof", DcAssets.Lit(DcAssets.Hex("#C9CDD2"), 0.3f), node, true, true);
            if (!glass.IsEmpty) glass.ToObject("busstop-glass", DcAssets.Unlit(new Color(0.62f, 0.74f, 0.80f, 0.28f)), node);
            plate.ToObject("busstop-plate", DcAssets.Lit(CountryStyle.Current.busStopPlate, 0.35f), node, true, true);     // 나라 표: 한국 남색 판 + 흰 칸, 독일 노란 판 + 초록
            mark.ToObject("busstop-mark", DcAssets.Unlit(CountryStyle.Current.busStopMark), node);
            if (!board.IsEmpty) board.ToObject("busstop-board", DcAssets.Lit(DcAssets.Hex("#23272D"), 0.5f), node, false, true);
            if (!bench.IsEmpty) bench.ToObject("busstop-bench", DcAssets.Lit(DcAssets.Hex("#8A6A4A"), 0.25f), node, true, true);
            Debug.Log($"BusStopBuilder: 버스정류장 {n}곳");
        }

        static Vector3 W(V2 p, float y) => Space3D.W(p, y);

        /// <summary>길 방향(fwd)·바깥(right, 길에서 먼 쪽) 단위 벡터</summary>
        static void Axes(float heading, out Vector3 fwd, out Vector3 side)
        {
            fwd = Space3D.Rot(heading) * Vector3.forward;
            side = Vector3.Cross(Vector3.up, fwd).normalized;
        }

        static void Pole(V2 p, float heading, float y0, MeshKit metal, MeshKit plate, MeshKit mark)
        {
            float yaw = Space3D.Yaw(heading);
            var c = W(p, y0);
            metal.Box(c + Vector3.up * 1.4f, new Vector3(0.07f, 2.8f, 0.07f), yaw);
            // 판: 길 방향으로 넓은 면이 오게(지나가는 운전자가 옆에서 보는 판 — 실제 정류장 표지처럼 길과 나란한 면 두 개)
            plate.Box(c + Vector3.up * 2.45f, new Vector3(0.06f, 0.70f, 0.52f), yaw);
            Axes(heading, out var fwd, out var side);
            foreach (var sg in new[] { -1f, 1f })
            {
                var face = c + Vector3.up * 2.52f + side * (sg * 0.035f);
                mark.Box(face, new Vector3(0.005f, 0.26f, 0.30f), yaw);                 // 버스 몸통 칸
                mark.Box(face + Vector3.down * 0.2f, new Vector3(0.005f, 0.06f, 0.36f), yaw);   // 아랫줄
            }
        }

        static void Curb(RegionData.BusStop s, float y0, MeshKit metal, MeshKit roof, MeshKit glass, MeshKit board, MeshKit bench)
        {
            Axes(s.heading, out var fwd, out var side);
            // 뒤(길에서 먼 쪽): 쉘터 가운데가 표지보다 길에서 먼 쪽
            var rel = s.shelter - s.sign;
            var relW = new Vector3(rel.x, 0f, -rel.y);
            var back = Vector3.Dot(relW, side) >= 0f ? side : -side;
            float L = s.shelterLen, Wd = s.shelterWid, H = 2.5f;
            float yaw = Space3D.Yaw(s.heading);
            var c = W(s.shelter, y0);
            // 기둥 4개(앞 둘은 가늘게)
            foreach (var a in new[] { -1f, 1f })
                foreach (var b in new[] { -1f, 1f })
                    metal.Box(c + fwd * (a * (L / 2f - 0.1f)) + back * (b * (Wd / 2f - 0.1f)) + Vector3.up * (H / 2f), new Vector3(0.07f, H, 0.07f), yaw);
            // 지붕: 뒤가 조금 높게(빗물) — 판 + 앞 테두리
            roof.Box(c + Vector3.up * (H + 0.05f) + back * 0.1f, new Vector3(Wd + 0.4f, 0.08f, L + 0.3f), yaw);
            metal.Box(c + Vector3.up * (H - 0.04f) - back * (Wd / 2f + 0.08f), new Vector3(0.06f, 0.14f, L + 0.3f), yaw);
            // 뒷벽 유리·옆 유리(한쪽)
            glass.Box(c + back * (Wd / 2f - 0.1f) + Vector3.up * 1.25f, new Vector3(0.02f, 1.9f, L - 0.3f), yaw);
            glass.Box(c + fwd * (L / 2f - 0.1f) + Vector3.up * 1.25f, new Vector3(Wd - 0.3f, 1.9f, 0.02f), yaw);
            // 노선 안내판(다른 쪽 끝): 어두운 판
            board.Box(c - fwd * (L / 2f - 0.12f) + back * 0.25f + Vector3.up * 1.35f, new Vector3(0.9f, 1.4f, 0.05f), yaw);
            // 의자: 뒷벽 앞 길이 2 m
            bench.Box(c + back * (Wd / 2f - 0.42f) + Vector3.up * 0.45f, new Vector3(0.4f, 0.05f, 2.0f), yaw);
            foreach (var a in new[] { -0.85f, 0.85f })
                metal.Box(c + back * (Wd / 2f - 0.42f) + fwd * a + Vector3.up * 0.22f, new Vector3(0.05f, 0.44f, 0.05f), yaw);
        }

        static void Median(RegionData.BusStop s, float y0, MeshKit metal, MeshKit roof, MeshKit glass, MeshKit bench)
        {
            Axes(s.heading, out var fwd, out var side);
            float L = Mathf.Max(4f, s.shelterLen), Wd = Mathf.Max(1.2f, s.shelterWid), H = 2.7f;
            float yaw = Space3D.Yaw(s.heading);
            var c = W(s.shelter, y0);
            int k = Mathf.Max(2, Mathf.RoundToInt(L / 3f) + 1);
            for (int i = 0; i < k; i++)
            {
                float t = -L / 2f + 0.15f + (L - 0.3f) * i / (k - 1);
                metal.Box(c + fwd * t + Vector3.up * (H / 2f), new Vector3(0.09f, H, 0.09f), yaw);
            }
            roof.Box(c + Vector3.up * (H + 0.05f), new Vector3(Wd + 0.3f, 0.09f, L), yaw);
            // 양쪽 낮은 난간(유리 1.0 m) — 버스가 서는 쪽 가운데는 비운다(타고 내리는 자리 6 m)
            foreach (var sg in new[] { -1f, 1f })
            {
                float gap = Mathf.Min(6f, L - 2f);
                float seg = (L - gap) / 2f;
                if (seg < 0.5f) continue;
                foreach (var e in new[] { -1f, 1f })
                    glass.Box(c + side * (sg * (Wd / 2f)) + fwd * (e * (gap / 2f + seg / 2f)) + Vector3.up * 0.55f, new Vector3(0.02f, 1.0f, seg), yaw);
            }
            bench.Box(c + Vector3.up * 0.45f, new Vector3(0.4f, 0.05f, Mathf.Min(3f, L * 0.4f)), yaw);
        }
    }
}
