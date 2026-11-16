// 다른 차 한 대(Traffic.cs 에서 나눈 파일).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public class TrafficCar
    {
        public int lane;
        public float s;          // 차로 처음부터 앞 범퍼까지 거리가 아니라 차 가운데까지(m)
        public float speed;      // m/s
        public V2 pos;           // 차 가운데
        public float heading;    // 라디안(2D: x 오른쪽, y 아래, 시계 방향 +)
        public float length = 4.5f, width = 1.8f;
        public int model;        // 모양 번호(Unity 가 차 모델을 고른다)
        /// <summary>차체색 고르는 수(0~9999) — 나오는 자리로 정해 무작위 순서는 그대로</summary>
        public int paint;
        public float z;          // 높이(m, 고가·지하 차로)
        public bool braking;
        /// <summary>좁은 길에서 마주 선 내 차를 기다린 시간(길면 물러난다)</summary>
        public float waitMe;
        public bool backing;
        /// <summary>운전 성향: 0 보통, 1 신중(느리고 멀찍이), 2 급함(빠르고 바짝, 노란불에도 지나감)</summary>
        public int style;
        /// <summary>차 종류: Kinds 표 순번 — 0 세단, 1 SUV, 2 해치백, 3 택시, 4 승합, 5 1톤 화물, 6 시내버스, 7 마을버스</summary>
        public int kind;
        /// <summary>방향지시등: -1 왼쪽, 0 끔, 1 오른쪽(버스가 정류장에 설 때 오른쪽, 떠날 때 왼쪽)</summary>
        public int blink;
        /// <summary>버스: 정류장에 서 있는 남은 초, 이 차로에서 이미 선 정류장(차로 위 거리)</summary>
        public float dwell;
        public float doneStopS = -1f;
        public int doneStopLane = -1;
        public float leaveT;
        /// <summary>차로 끝 70 m 앞에서 미리 정한 다음 차로(-2 = 아직)·그 차로 시작 거리, 꺾는 쪽(-1 왼쪽·0 곧장·1 오른쪽) — 좌회전 화살표 신호를 보려고</summary>
        public int planNext = -2;
        public float planS;
        public int planTurn;
        /// <summary>교차로 앞에서 다른 차에 양보하며 기다린 초(4초 넘으면 그냥 간다 — 서로 기다려 멈추지 않게)</summary>
        public float waitJ;
        /// <summary>이번 장 처음의 몸통 네모(몸통 겹침 막기에 쓴다)</summary>
        internal V2[] guardPoly;
        /// <summary>몸통 겹침 막기로 이 차를 세운 차(시험 기록용)</summary>
        public TrafficCar guardBy;
        public TrafficCar leadBy;      // 바로 앞(6 m 안)에서 나를 세운 앞차
        /// <summary>이번 장에 나를 세운 다른 차(앞차·몸통 겹침·들어갈 자리·꼬리물기·양보)와 까닭(신호·보행자 따위로 서면 차는 null)</summary>
        public TrafficCar waitBy;
        public string waitWhy;
        /// <summary>계속 서 있은 초, 서로 기다림 고리를 풀려고 잠시 무시하는 차와 남은 초, 다시 뽑히기까지 쉬는 초, 번호</summary>
        internal float stopT, passT, passCool;
        /// <summary>가로지르는 자리에서 양보하며 서 있은 초(오래 기다린 차가 먼저)</summary>
        internal float crossWait;
        /// <summary>마지막으로 잰 앞 틈·나를 세운 앞차와 그 속도(가로지름 판정에서 그 자리를 빠져나갈 수 있는지)</summary>
        internal float xGap = float.PositiveInfinity, xLeadV = 99f;
        internal TrafficCar xLead;
        internal bool xHeld;
        /// <summary>뒤 40 m 안에 넘은 정지선이 있는 교차로(그 교차로 안쪽 정지선은 보지 않는다)</summary>
        internal bool inJct;
        internal V2 jct;
        public TrafficCar passBy;
        internal bool passHard;
        /// <summary>세게 풀 때 몸통을 무시할 기다림 사슬(처음 쓸 때 16칸 한 번 만든다)</summary>
        internal TrafficCar[] passSet;
        internal int passN;
        internal int id;
        /// <summary>차로 끝에서 이 차를 세운 다른 차(들어갈 자리 막힘) — 둘이 서로를 기다리면 한쪽이 먼저 간다</summary>
        public TrafficCar occBy;
        /// <summary>좁은 골목에서 오른쪽으로 붙인 거리(m, 진행 방향 오른쪽 +)와 목표</summary>
        public float lat, latT;
        /// <summary>둘이 못 비켜 가는 골목에서 마주 서서 기다린 초, 뒤로 빼는 중(골목 입구까지 물러난다)</summary>
        public float alleyWait;
        public bool alleyBack;
        /// <summary>좁은 골목에서 물러나 옆 골목 입구로 비켜 선 쪽(+오른쪽 −왼쪽, 0 이면 아님)</summary>
        public float aside;
        /// <summary>시험 기록용: 마지막 단계의 앞 틈·목표 속도</summary>
        /// <summary>차로 조각이 바뀌는 이음에서 다음 차로의 시작이 옆·앞으로 어긋나 있으면
        /// 차가 한 장 만에 1.5~5 m 튀었다 → 어긋난 만큼을 여기에 담아 두고 1초쯤에 걸쳐 0 으로 줄인다(보이는 자리만. 차로 위 거리 s 는 그대로)</summary>
        public V2 seam;
        public bool posValid;
        public float dbgGap, dbgTarget;
        /// <summary>느린 앞차 뒤에 붙어 있은 초(길면 옆 차로로 앞지른다)</summary>
        public float slowT;
        public bool IsBus => kind == 6 || kind == 7;
        /// <summary>차 종류별 크기(전장·전폭, m)와 무게(교통량 비율) — 서울 등록 차 비율을 대충 따라(승용 대부분, SUV 많음, 택시·승합·화물 조금, 버스는 큰길만)</summary>
        public static readonly (string name, float L, float W, float weight)[] Kinds =
        {
            ("세단", 4.70f, 1.84f, 0.40f), ("SUV", 4.70f, 1.90f, 0.24f), ("해치백", 4.04f, 1.76f, 0.07f), ("택시", 4.80f, 1.85f, 0.10f),
            ("승합", 5.25f, 2.00f, 0.07f), ("1톤 화물", 5.10f, 1.74f, 0.07f), ("시내버스", 11.0f, 2.49f, 0.04f), ("마을버스", 7.50f, 2.07f, 0.03f),
        };
        public float SpeedK => (IsBus ? 0.85f : 1f) * (style == 1 ? 0.82f : (style == 2 ? 1.04f : 0.92f));
        public float GapM => style == 1 ? 6f : (style == 2 ? 2.5f : 4f);
        public V2[] Poly => Geom.RectPoly(pos, length, width, heading * Geom.Rad2Deg);
    }
}
