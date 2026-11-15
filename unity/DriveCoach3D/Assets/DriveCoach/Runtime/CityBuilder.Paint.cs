// CityBuilder 의 노면 표시(칠) 부분(11/15 코드 정리로 CityBuilder.cs 에서 나눈 파일). 전체 설명은 CityBuilder.cs 맨 위.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static partial class CityBuilder
    {
        // ───────────────────────── 칠 ─────────────────────────

        static bool Major(string hw) => hw.StartsWith("trunk") || hw.StartsWith("primary") || hw.StartsWith("secondary") || hw.StartsWith("tertiary");

        static void BuildMarkings(RoadDef r, float h, Transform node, Dictionary<int, (V2 p, float r)> jr, Func<V2, bool> InJunction)
        {
            var yellow = new MeshKit();
            var white = new MeshKit();
            float y = h + 0.045f;
            foreach (var s in r.strips)
            {
                float median = s.twoWay && s.fwd + s.bwd >= 4 ? 0.5f : 0f;
                if (s.twoWay && s.fwd + s.bwd >= 2 && s.hw != "service" && s.hw != "living_street")
                {
                    if (Major(s.hw))
                    {
                        Paint(yellow, Stages.Offset(s.pts, median / 2f + 0.12f), 0.13f, y, 0f, 0f, InJunction);
                        Paint(yellow, Stages.Offset(s.pts, -(median / 2f + 0.12f)), 0.13f, y, 0f, 0f, InJunction);
                    }
                    else Paint(yellow, s.pts, 0.13f, y, 0f, 0f, InJunction);
                }
                for (int k = 1; k < s.fwd; k++) Paint(white, Stages.Offset(s.pts, median / 2f + k * s.laneWidth), 0.12f, y, 3f, 5f, InJunction);
                for (int k = 1; k < s.bwd; k++) Paint(white, Stages.Offset(s.pts, -(median / 2f + k * s.laneWidth)), 0.12f, y, 3f, 5f, InJunction);
                // 큰길 가장자리 실선
                if (Major(s.hw))
                {
                    Paint(white, Stages.Offset(s.pts, s.width / 2f - 0.25f), 0.12f, y, 0f, 0f, InJunction);
                    Paint(white, Stages.Offset(s.pts, -(s.width / 2f - 0.25f)), 0.12f, y, 0f, 0f, InJunction);
                }
            }
            // 정지선(신호 교차로) + 그 바로 뒤 횡단보도
            foreach (var sl in r.stopLines)
            {
                white.Line(sl.a, sl.b, 0.45f, y + 0.002f);
                Zebra(white, (sl.a + sl.b) / 2f + sl.approach * 3.0f, sl.approach, sl.a.DistanceTo(sl.b), y + 0.002f);
            }
            // OSM 횡단보도 점: 가장 가까운 길 띠의 방향·폭으로
            if (r.region != null)
            {
                foreach (var c in r.region.crossings)
                {
                    RoadStrip best = null; V2 dir = V2.Zero; float bd = 6f;
                    foreach (var s in r.strips)
                    {
                        for (int i = 0; i + 1 < s.pts.Length; i++)
                        {
                            float d = Geom.ClosestPointOnSegment(c, s.pts[i], s.pts[i + 1]).DistanceTo(c);
                            if (d < bd) { bd = d; best = s; dir = (s.pts[i + 1] - s.pts[i]).Normalized; }
                        }
                    }
                    if (best != null) Zebra(white, c, dir, best.width, y + 0.002f);
                }
            }
            if (!yellow.IsEmpty) yellow.ToObject("yellow-lines", DcAssets.Lit(DcAssets.Hex("#E0AE2E"), 0.45f), node);
            if (!white.IsEmpty) white.ToObject("white-lines", DcAssets.Lit(DcAssets.Hex("#E6E6E1"), 0.45f), node);
        }

        /// <summary>선을 칠한다(dash > 0 이면 점선: dash 칠·gap 띄움). 교차로 안은 칠하지 않는다. 실선은 4 m 조각으로.</summary>
        static void Paint(MeshKit mk, V2[] pts, float w, float y, float dash, float gap, Func<V2, bool> skip)
        {
            float cycle = dash > 0f ? dash + gap : 4f;
            float phase = 0f;   // 지금 무늬 안 위치
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                float len = a.DistanceTo(b);
                if (len < 0.05f) continue;
                var d = (b - a) / len;
                float x = 0f;
                while (x < len - 1e-3f)
                {
                    bool on = dash <= 0f || phase < dash;
                    float until = dash <= 0f ? cycle - phase : (on ? dash - phase : cycle - phase);
                    float seg = Math.Min(until, len - x);
                    if (on && seg > 0.05f)
                    {
                        var p0 = a + d * x; var p1 = a + d * (x + seg);
                        if (!skip((p0 + p1) / 2f)) mk.Line(p0, p1, w, y);
                    }
                    x += seg;
                    phase += seg;
                    if (phase >= cycle - 1e-4f) phase = 0f;
                }
            }
        }

        /// <summary>얼룩말 횡단보도: 길 방향으로 긴 흰 막대(폭 0.5 m, 길이 3 m)를 길 폭에 걸쳐 1 m 마다</summary>
        static void Zebra(MeshKit mk, V2 c, V2 dir, float width, float y)
        {
            var right = new V2(-dir.y, dir.x);
            for (float x = -width / 2f + 0.5f; x <= width / 2f - 0.4f; x += 1f)
            {
                var m = c + right * x;
                mk.Line(m - dir * 1.5f, m + dir * 1.5f, 0.5f, y);
            }
        }
    }
}
