// 순수 계산(UnityEngine 없음): 위에서 본 2D 평면, 단위 m. x 오른쪽, y 아래(Godot 화면 좌표와 같음), 각도 0 = +x, 시계 방향 +.
// 3D 로 그릴 때는 Unity (X, Z) = (x, -y), 요(yaw, 도) = 90 + heading(도) 로 바꾼다(WorldView3D).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    /// <summary>2D 벡터(float). Godot Vector2 와 같은 규칙(orthogonal = (y, -x)).</summary>
    [Serializable]
    public struct V2 : IEquatable<V2>
    {
        public float x, y;
        public V2(float x, float y) { this.x = x; this.y = y; }

        public static readonly V2 Zero = new V2(0f, 0f);
        public static readonly V2 Inf = new V2(float.PositiveInfinity, float.PositiveInfinity);

        public static V2 operator +(V2 a, V2 b) => new V2(a.x + b.x, a.y + b.y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.x - b.x, a.y - b.y);
        public static V2 operator -(V2 a) => new V2(-a.x, -a.y);
        public static V2 operator *(V2 a, float s) => new V2(a.x * s, a.y * s);
        public static V2 operator *(float s, V2 a) => new V2(a.x * s, a.y * s);
        public static V2 operator /(V2 a, float s) => new V2(a.x / s, a.y / s);
        public static bool operator ==(V2 a, V2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(V2 a, V2 b) => !(a == b);
        public bool Equals(V2 o) => this == o;
        public override bool Equals(object o) => o is V2 v && this == v;
        public override int GetHashCode() => x.GetHashCode() * 31 + y.GetHashCode();
        public override string ToString() => $"({x:0.###}, {y:0.###})";

        public float Dot(V2 o) => x * o.x + y * o.y;
        public float Length => (float)Math.Sqrt(x * x + y * y);
        public float DistanceTo(V2 o) => (this - o).Length;
        public V2 Normalized { get { var l = Length; return l > 0f ? this / l : Zero; } }
        /// <summary>atan2(y, x) 라디안</summary>
        public float Angle => (float)Math.Atan2(y, x);
        /// <summary>Godot Vector2.orthogonal(): (y, -x)</summary>
        public V2 Orthogonal => new V2(y, -x);
        public V2 Lerp(V2 to, float t) => new V2(x + (to.x - x) * t, y + (to.y - y) * t);
        public static V2 FromAngle(float rad) => new V2((float)Math.Cos(rad), (float)Math.Sin(rad));
        public bool IsInf => float.IsInfinity(x) || float.IsInfinity(y);
    }

    /// <summary>축 정렬 사각형(Godot Rect2: position + size).</summary>
    [Serializable]
    public struct Rect
    {
        public float x, y, w, h;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; this.w = w; this.h = h; }
        public V2 Position => new V2(x, y);
        public V2 End => new V2(x + w, y + h);
        public V2 Center => new V2(x + w / 2f, y + h / 2f);
        /// <summary>Godot Rect2.has_point: 끝 변은 포함하지 않는다.</summary>
        public bool HasPoint(V2 p) => p.x >= x && p.y >= y && p.x < x + w && p.y < y + h;
        public Rect Merge(Rect o)
        {
            float x0 = Math.Min(x, o.x), y0 = Math.Min(y, o.y);
            float x1 = Math.Max(x + w, o.x + o.w), y1 = Math.Max(y + h, o.y + o.h);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }
        public Rect Expand(V2 p)
        {
            float x0 = Math.Min(x, p.x), y0 = Math.Min(y, p.y);
            float x1 = Math.Max(x + w, p.x), y1 = Math.Max(y + h, p.y);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }
        public Rect Grow(float m) => new Rect(x - m, y - m, w + 2 * m, h + 2 * m);
    }

    public static class Geom
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public const float Tau = (float)(Math.PI * 2.0);
        public const float Pi = (float)Math.PI;

        /// <summary>가운데·크기·각도(도)로 회전 사각형 네 꼭짓점</summary>
        public static V2[] RectPoly(V2 c, float w, float h, float angleDeg = 0f)
        {
            float a = angleDeg * Deg2Rad;
            var ux = new V2((float)Math.Cos(a), (float)Math.Sin(a)) * (w / 2f);
            var uy = new V2(-(float)Math.Sin(a), (float)Math.Cos(a)) * (h / 2f);
            return new[] { c - ux - uy, c + ux - uy, c + ux + uy, c - ux + uy };
        }

        /// <summary>두 볼록 다각형이 겹치는가(분리축 정리)</summary>
        public static bool ConvexOverlap(IList<V2> a, IList<V2> b)
        {
            foreach (var poly in new[] { a, b })
            {
                int n = poly.Count;
                for (int i = 0; i < n; i++)
                {
                    V2 p1 = poly[i], p2 = poly[(i + 1) % n];
                    var axis = new V2(p1.y - p2.y, p2.x - p1.x);
                    float amin = float.PositiveInfinity, amax = float.NegativeInfinity;
                    foreach (var p in a) { float d = axis.Dot(p); amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
                    float bmin = float.PositiveInfinity, bmax = float.NegativeInfinity;
                    foreach (var p in b) { float d = axis.Dot(p); bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
                    if (amax < bmin || bmax < amin) return false;
                }
            }
            return true;
        }

        static bool SegHit(V2 a, V2 b, V2 c, V2 d)
        {
            float d1 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x), d2 = (b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x);
            float d3 = (d.x - c.x) * (a.y - c.y) - (d.y - c.y) * (a.x - c.x), d4 = (d.x - c.x) * (b.y - c.y) - (d.y - c.y) * (b.x - c.x);
            return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
        }

        /// <summary>
        /// 볼록 다각형 a(차)와 아무 다각형 b(오목해도 됨: ㄱ·ㄷ 모양 건물)가 겹치는가.
        /// ConvexOverlap 은 오목한 b 를 볼록 껍질처럼 봐서, ㄱ 모양 건물의 안쪽 빈 자리(길·마당)에서도 "건물에 부딪혔어요"가 났다.
        /// </summary>
        public static bool PolyOverlap(IList<V2> a, IList<V2> b)
        {
            if (!ConvexOverlap(a, b)) return false;        // 껍질끼리도 안 겹치면 끝
            int na = a.Count, nb = b.Count;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < nb; j++)
                    if (SegHit(a[i], a[(i + 1) % na], b[j], b[(j + 1) % nb])) return true;
            if (PointInPolygon(a[0], b)) return true;
            return PointInPolygon(b[0], a);
        }

        /// <summary>점이 다각형 안에 있는가(짝홀 규칙)</summary>
        public static bool PointInPolygon(V2 p, IList<V2> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                V2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y))
                {
                    float xCross = (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x;
                    if (p.x < xCross) inside = !inside;
                }
            }
            return inside;
        }

        public static V2 ClosestPointOnSegment(V2 p, V2 a, V2 b)
        {
            var ab = b - a;
            float l2 = ab.Dot(ab);
            if (l2 <= 0f) return a;
            float t = Math.Max(0f, Math.Min(1f, (p - a).Dot(ab) / l2));
            return a + ab * t;
        }

        /// <summary>원(가운데 c, 반지름 r)과 볼록 다각형이 겹치는가</summary>
        public static bool CircleOverlap(V2 c, float r, IList<V2> poly)
        {
            if (PointInPolygon(c, poly)) return true;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
                if (ClosestPointOnSegment(c, poly[i], poly[(i + 1) % n]).DistanceTo(c) <= r) return true;
            return false;
        }

        public static V2 PolyCenter(IList<V2> p)
        {
            var s = V2.Zero;
            foreach (var v in p) s += v;
            return s / Math.Max(1f, p.Count);
        }

        /// <summary>Godot wrapf</summary>
        public static float Wrap(float value, float min, float max)
        {
            float range = max - min;
            if (Math.Abs(range) < 1e-6f) return min;
            float result = value - range * (float)Math.Floor((value - min) / range);
            if (Math.Abs(result - max) < 1e-5f) return min;
            return result;
        }

        /// <summary>각도 차(도, -180..180)</summary>
        public static float AngleDiffDeg(float a, float b) => Wrap(a - b, -180f, 180f);

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>점에서 볼록 다각형까지 거리(안이면 0)와 가장 가까운 점</summary>
        public static float DistPointPoly(V2 p, IList<V2> poly, out V2 closest)
        {
            if (PointInPolygon(p, poly)) { closest = p; return 0f; }
            float best = float.PositiveInfinity;
            closest = p;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var q = ClosestPointOnSegment(p, poly[i], poly[(i + 1) % n]);
                float d = q.DistanceTo(p);
                if (d < best) { best = d; closest = q; }
            }
            return best;
        }
    }
}
