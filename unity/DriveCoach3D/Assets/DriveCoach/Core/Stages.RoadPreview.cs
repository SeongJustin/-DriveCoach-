// Stages 의 도로 주행 미리보기 판(Road.cs 에서 나눈 파일).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public static partial class Stages
    {
        /// <summary>
        /// 도로 주행 미리보기: 가로 120 m · 세로 70 m 고리 도로(양방향 2차로)와 가운데 남북 도로가 만나는 신호 사거리 2곳.
        /// 우리나라는 우측통행이라 화면에서 동쪽으로 갈 때 아래쪽(+y) 차로를 쓴다.
        /// </summary>
        public static Stage RoadPreview()
        {
            var s = MakeStage(300, "도로 주행(미리보기)", "신호를 지키며 동네 한 바퀴를 자유롭게 달려 보십시오. 끝내려면 서서 P");
            s.mode = StageMode.Road;
            var road = new RoadDef { speedLimitKmh = 30f, trafficCount = 4 };
            const float L = 3.25f;            // 차로 폭
            float x0 = -60, x1 = 60, y0 = -35, y1 = 35;
            // 도로 바닥(고리 4변 + 가운데 남북 도로)
            road.pavement.Add(new Rect(x0 - L, y0 - L, (x1 - x0) + 2 * L, 2 * L));
            road.pavement.Add(new Rect(x0 - L, y1 - L, (x1 - x0) + 2 * L, 2 * L));
            road.pavement.Add(new Rect(x0 - L, y0 - L, 2 * L, (y1 - y0) + 2 * L));
            road.pavement.Add(new Rect(x1 - L, y0 - L, 2 * L, (y1 - y0) + 2 * L));
            road.pavement.Add(new Rect(-L, y0 - L, 2 * L, (y1 - y0) + 2 * L));
            // 중앙선(노랑)
            road.centerLines.Add(new Seg(P(x0, y0), P(x1, y0)));
            road.centerLines.Add(new Seg(P(x0, y1), P(x1, y1)));
            road.centerLines.Add(new Seg(P(x0, y0), P(x0, y1)));
            road.centerLines.Add(new Seg(P(x1, y0), P(x1, y1)));
            road.centerLines.Add(new Seg(P(0, y0 + L + 0.5f), P(0, y1 - L - 0.5f)));
            // 차로(우측통행): 시계 방향 고리 = 위쪽 변은 동쪽으로, 위쪽 변의 남쪽(+y) 차로
            road.lanes.Add(new Lane { name = "북쪽 동행", center = new[] { P(x0, y0 + L / 2), P(x1, y0 + L / 2) } });
            road.lanes.Add(new Lane { name = "북쪽 서행", center = new[] { P(x1, y0 - L / 2), P(x0, y0 - L / 2) } });
            road.lanes.Add(new Lane { name = "남쪽 서행", center = new[] { P(x1, y1 - L / 2), P(x0, y1 - L / 2) } });
            road.lanes.Add(new Lane { name = "남쪽 동행", center = new[] { P(x0, y1 + L / 2), P(x1, y1 + L / 2) } });
            road.lanes.Add(new Lane { name = "동쪽 남행", center = new[] { P(x1 - L / 2, y0), P(x1 - L / 2, y1) } });
            road.lanes.Add(new Lane { name = "동쪽 북행", center = new[] { P(x1 + L / 2, y1), P(x1 + L / 2, y0) } });
            road.lanes.Add(new Lane { name = "서쪽 북행", center = new[] { P(x0 + L / 2, y1), P(x0 + L / 2, y0) } });
            road.lanes.Add(new Lane { name = "서쪽 남행", center = new[] { P(x0 - L / 2, y0), P(x0 - L / 2, y1) } });
            road.lanes.Add(new Lane { name = "가운데 남행", center = new[] { P(-L / 2, y0), P(-L / 2, y1) } });
            road.lanes.Add(new Lane { name = "가운데 북행", center = new[] { P(L / 2, y1), P(L / 2, y0) } });
            // 사거리 두 곳(위·아래 변과 가운데 도로가 만나는 곳) — 신호와 정지선
            AddSignalT(road, P(0, y0), L, 0f);
            AddSignalT(road, P(0, y1), L, 14f);
            var f = MakeFloor("동네", new Rect(x0 - 25, y0 - 25, (x1 - x0) + 50, (y1 - y0) + 50));
            f.surface = Surface.Grass;
            s.floors.Add(f);
            s.road = road;
            s.startC = P(x0 + 18, y0 + L / 2);
            s.startH = 0f;
            s.target = new Target();
            return s;
        }

        /// <summary>T 자 사거리(고리 변 + 가운데 도로)에 신호 하나와 세 방향 정지선</summary>
        static void AddSignalT(RoadDef road, V2 c, float L, float offset)
        {
            int si = road.signals.Count;
            road.signals.Add(new TrafficSignal { pos = c + P(L + 1.5f, -L - 1.5f), facingDeg = 180f, offset = offset });
            float g = 2.5f;   // 교차로 가장자리에서 정지선까지
            // 서쪽에서 오는 동행 차로(+y 쪽 절반), 동쪽에서 오는 서행 차로(-y 쪽 절반), 가운데 도로에서 오는 차로
            road.stopLines.Add(new StopLine { a = c + P(-L - g, 0), b = c + P(-L - g, L), approach = P(1, 0), signal = si });
            road.stopLines.Add(new StopLine { a = c + P(L + g, -L), b = c + P(L + g, 0), approach = P(-1, 0), signal = si });
            road.stopLines.Add(new StopLine { a = c + P(0, (c.y < 0 ? 1 : -1) * (L + g)), b = c + P((c.y < 0 ? 1 : -1) * L, (c.y < 0 ? 1 : -1) * (L + g)), approach = P(0, c.y < 0 ? -1 : 1), signal = si });
        }
    }
}
