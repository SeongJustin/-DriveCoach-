// 지역 지도 자료의 부품 타입(11/15 코드 정리로 Region.cs 에서 나눈 파일).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public class RegionRoad
    {
        public string hw = "", name = "";
        public int lanes = 2, maxspeed = 30;
        /// <summary>(11/13) 자료로 확인된 제한속도(km/h): 노면 숫자·속도제한 표지·OSM maxspeed. 0 = 모름 → 제한속도 없음으로 다룬다(과속 판정·표시 안 함). JSON "ms"</summary>
        public int posted;
        /// <summary>길 폭(m, 골목처럼 따로 잰 것). 0 = 차로 수 × 차로 폭</summary>
        public float width;
        /// <summary>실제 노면 반폭(왼쪽·오른쪽, m). 서울 T-GIS 실제 차선으로 잰 값. 0 = 모름</summary>
        public float wl, wr;
        /// <summary>(11/12 밤) 도랑(지하차도 진입로) 바닥의 반폭을 좌우 따로 줄 때(m). 0 = wl·wr 중 넓은 쪽을 양쪽에.
        /// OSM 가운데선이 방향별 차도 한가운데(옆 차로 포함)에 있어, 가운데 차로만 내려가는 지하차도는 안쪽으로 치우친다.</summary>
        public float twl, twr;
        /// <summary>실제 차선으로 잰 차로 가운데(오프셋 m, 진행 방향 오른쪽 +). laneR = 정방향, laneL = 역방향(음수). null = 모름(차로 수 × 폭)</summary>
        public float[] laneR, laneL;
        /// <summary>점마다 차로 가운데를 옆으로 더 옮길 거리(m, 오른쪽 +, pts 와 같은 길이) — OSM 선이 실제 칠과 나란하지 않은 길(11/9 노면 28차 검토: 차로 경로가 차선 위로 비스듬히). null = 없음</summary>
        public float[] laneShift;
        public bool oneway, bridge, tunnel;
        public int layer;
        public V2[] pts;
        public int[] jn;
        /// <summary>입체 교차: 꼭짓점마다 높이(m, 고가 +·지하 −). null = 땅 높이</summary>
        public float[] z;
    }

    public class RegionJunction { public V2 p; public bool sig; }

    public class RegionBuilding
    {
        public string kind = "other", name = "";
        /// <summary>OSM 에 없어 빈 주택가 블록을 채운 건물</summary>
        public bool generated;
        public int levels = 2;
        public float height = 6f;
        public V2[] poly;
        public V2 center;
    }

    /// <summary>공원·숲·잔디(초록 바닥)와 물(하천·호수)</summary>
    public class RegionArea { public string kind = "park"; public V2[] poly; }

    /// <summary>안전표지 기둥 하나(T-GIS): 자리, 바라보는 각(도, 2D 시계 방향), 위에서부터 표지 번호들</summary>
    public class RegionSign { public V2 p; public float faceDeg; public string[] codes; }

    /// <summary>신호등 하나(T-GIS 신호등부착대): 기둥, 팔 끝(등 자리), 바라보는 각, 부착 종류(2 세로·3 가로 팔 …), 등 종류(2 3색·3 4색·7 보행등 …)</summary>
    public class RegionSignalHead { public V2 pole, head; public float faceDeg; public int kind, lamp; }

    /// <summary>
    /// 시설: kind = parking | fuel | charging | services.
    /// poly 넓이, stalls 주차칸 선(두 점씩), cars 세운 차(x, y, 각 라디안), canopy 주유소 지붕(cx, cy, 각 라디안, 길이, 폭) — 없으면 null.
    /// </summary>
    public class RegionFacility
    {
        public string kind = "parking";
        public V2[] poly;
        public V2[] stalls = new V2[0];
        public List<(V2 p, float ang)> cars = new List<(V2, float)>();
        public float[] canopy;
    }

    /// <summary>옆 동(행정동 경계가 닿는 동). file = 앱에 들어 있는 지역 파일(없으면 "": 아직 받지 않은 동)</summary>
    public class RegionNeighbor { public string name = "", file = ""; public V2[] poly = new V2[0]; }
}
