// 3D 주차 연습: 2D 계산 좌표 ↔ Unity 3D 좌표, 층 높이
// 계산(Core)은 위에서 본 평면: x 오른쪽, y 아래, 각도 0 = +x, 시계 방향 +.
// Unity: X = x, Z = -y(위에서 보면 화면 위쪽이 +Z), Y = 높이. 요(yaw, 도) = 90 + 계산 각도(도).
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class Space3D
    {
        public static Vector3 W(V2 p, float y = 0f) => new Vector3(p.x, y, -p.y);
        public static V2 P(Vector3 w) => new V2(w.x, -w.z);
        public static float Yaw(float headingRad) => 90f + headingRad * Mathf.Rad2Deg;
        public static Quaternion Rot(float headingRad) => Quaternion.Euler(0f, Yaw(headingRad), 0f);
    }

    /// <summary>
    /// 층·경사로 높이. 첫 층 = 0 m. 경사로는 아래 끝 높이에서 경사만큼 오른다.
    /// 이어진 경사로(앞 경사로 끝이나 중간 발판 근처에서 시작)는 앞 경사로가 끝난 높이에서 이어서 오른다.
    /// </summary>
    public class Elevation
    {
        public readonly float[] floor;
        public readonly float[] rampStart;
        public readonly float[] rampEnd;
        public readonly float[] padHeight;
        readonly Stage st;

        public Elevation(Stage s)
        {
            st = s;
            floor = new float[s.floors.Count];
            rampStart = new float[s.ramps.Count];
            rampEnd = new float[s.ramps.Count];
            padHeight = new float[s.pads.Count];
            for (int i = 0; i < s.ramps.Count; i++)
            {
                var r = s.ramps[i];
                float start = floor[r.lower];
                if (i > 0 && r.a.DistanceTo(s.ramps[i - 1].b) < 4f) start = rampEnd[i - 1];
                rampStart[i] = start;
                rampEnd[i] = start + r.grade * r.a.DistanceTo(r.b);
                floor[r.upper] = Math.Max(floor[r.upper], rampEnd[i]);
            }
            for (int p = 0; p < s.pads.Count; p++)
            {
                var c = Geom.PolyCenter(s.pads[p]);
                float best = float.PositiveInfinity;
                for (int i = 0; i < s.ramps.Count; i++)
                {
                    float d = s.ramps[i].b.DistanceTo(c);
                    if (d < best) { best = d; padHeight[p] = rampEnd[i]; }
                }
            }
        }

        /// <summary>점 p 의 바닥 높이. 경사로·발판 위면 그 높이, 아니면 floorHint 층(또는 p 가 들어 있는 층).</summary>
        public float At(V2 p, int floorHint)
        {
            for (int i = 0; i < st.ramps.Count; i++)
            {
                var r = st.ramps[i];
                if (!Geom.PointInPolygon(p, r.poly)) continue;
                var ax = r.b - r.a;
                float t = Mathf.Clamp01((p - r.a).Dot(ax) / ax.Dot(ax));
                return Mathf.Lerp(rampStart[i], rampEnd[i], t);
            }
            for (int i = 0; i < st.pads.Count; i++) if (Geom.PointInPolygon(p, st.pads[i])) return padHeight[i];
            for (int i = 0; i < st.floors.Count; i++) if (st.floors[i].ground.HasPoint(p)) return floor[i];
            return floorHint >= 0 && floorHint < floor.Length ? floor[floorHint] : 0f;
        }
    }
}
