// Road: 신호등과 신호 계획
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    public enum SignalColor { Green, Yellow, Red }

    public class TrafficSignal
    {
        public V2 pos;             // 신호등 기둥 자리
        public float facingDeg;    // 신호등이 바라보는 방향(다가오는 차 쪽)
        public float green = 12f, yellow = 3f, red = 15f, offset;
        /// <summary>
        /// 직진 뒤 좌회전 화살표: 0 이면 좌회전 화살표가 초록과 함께(동시 신호).
        /// 0 보다 크면 한 주기 = [직진 초록 green][노랑][좌회전 화살표 leftGreen][노랑][빨강 red]. 화살표 동안 직진은 빨강.
        /// </summary>
        public float leftGreen;
        /// <summary>이 진입로 앞 횡단보도(이 길을 가로지르는 것)의 보행 초록: 주기 처음에서 pedStart 초부터 pedLen 초(0 = 빨간불 내내)</summary>
        public float pedStart, pedLen;
        /// <summary>이 신호가 속한 교차로 가운데(지역 지도: 실제 운영계획을 붙일 때 씀)</summary>
        public V2 junction;
        /// <summary>이 신호가 속한 교차로의 시간 계획(null = 고정 값). groupA: 교차로에서 먼저 초록인 갈래(1 현시)</summary>
        public SignalPlan plan;
        public bool groupA;
        float LeftPhase => leftGreen > 0f ? leftGreen + yellow : 0f;
        public float Cycle => green + yellow + LeftPhase + red;
        float Phase(float t) => ((t + offset) % Cycle + Cycle) % Cycle;

        /// <summary>직진(과 우회전) 신호</summary>
        public SignalColor At(float t)
        {
            float x = Phase(t);
            return x < green ? SignalColor.Green : (x < green + yellow ? SignalColor.Yellow : SignalColor.Red);
        }

        /// <summary>좌회전 신호: 화살표 단계가 없으면 직진과 같다</summary>
        public SignalColor LeftAt(float t)
        {
            if (leftGreen <= 0f) return At(t);
            float x = Phase(t) - green - yellow;
            return x >= 0f && x < leftGreen ? SignalColor.Green : (x >= leftGreen && x < leftGreen + yellow ? SignalColor.Yellow : SignalColor.Red);
        }

        /// <summary>직진 초록이 끝나기까지 남은 초(초록이 아니면 0): 보행등 깜빡임(초록 끝 무렵)</summary>
        public float GreenLeft(float t)
        {
            float x = Phase(t);
            return x < green ? green - x : 0f;
        }

        /// <summary>직진 빨강이 끝나(초록이 되기)까지 남은 초(빨강이 아니면 0): 독일 빨강+노랑(Rot-Gelb) 표시용</summary>
        public float RedLeft(float t) => At(t) == SignalColor.Red ? Cycle - Phase(t) : 0f;

        /// <summary>앞 횡단보도 보행 초록인가(pedLen 0 이면 직진·좌회전이 모두 빨강인 동안)</summary>
        public bool PedOn(float t) => PedLeft(t) > 0f;

        /// <summary>앞 횡단보도 보행 초록 남은 초(잔여시간 표시기), 아니면 0</summary>
        public float PedLeft(float t)
        {
            float x = Phase(t);
            if (pedLen <= 0f)
            {
                float st = green + yellow + LeftPhase;
                return x < st ? 0f : Cycle - x;
            }
            float r = ((x - pedStart) % Cycle + Cycle) % Cycle;
            return r < pedLen ? pedLen - r : 0f;
        }
    }

    /// <summary>
    /// 실제 신호 운영계획 한 시간대.
    /// 경찰청 교차로계획정보(교차로운영계획: OPER_PLAN_HH·MI, INT_OPER_CYCLE_VAL, INT_OPER_OFFSET_VAL, A_RING_n_PHASE_VAL)의 평일 계획.
    /// phases: A 링 현시 길이(초, 0 아닌 것만, 노랑·전적색 포함).
    /// </summary>
    public class SignalSlot { public int startMin; public float cycle, offset; public float[] phases = new float[0]; }

    /// <summary>
    /// 신호 교차로 하나의 시간 계획(같은 교차로 신호들이 함께 쓴다). slots 가 없으면 교차로 크기로 정한 값(서울 신호 크기:
    /// 들어오는 차로 수로 주기 90·120·160 s, 두 갈래가 차로 수 비율로 초록을 나눔, 큰 길은 직진 뒤 좌회전 화살표).
    /// 현시 → 움직임(운영계획에는 현시가 어느 방향인지 없다, 서울 흔한 순서로 맞춤):
    ///   2 현시 = [큰 갈래 직진(좌회전 함께)][작은 갈래], 3 = [큰 직진][큰 좌회전][작은 갈래], 4 이상 = [큰 직진][큰 좌회전][작은 직진][작은 좌회전(나머지 합)].
    ///   현시 값은 노랑 3 s 를 포함하고, 갈래의 마지막 현시는 전적색 2 s 도 포함한다고 본다.
    /// </summary>
    public class SignalPlan
    {
        public const float Y = 3f, AR = 2f;
        public int no;              // 서울 교차로 번호(0 = 모름)
        public string name = "";
        public int nA, nB;          // 갈래별(양방향) 들어오는 차로 수: A 가 큰 갈래(1 현시)
        public float defOffset;     // 고정 값일 때 교차로마다 다른 옵셋(같은 때 바뀌지 않게)
        public List<SignalSlot> slots = new List<SignalSlot>();
        /// <summary>지금 쓰는 시간대(null = 고정 값)</summary>
        public SignalSlot active;
        public float cycle, gA, lA, gB, lB, offA;

        public float LA => lA > 0f ? lA + Y : 0f;
        public float LB => lB > 0f ? lB + Y : 0f;

        /// <summary>하루 중 minute(0~1439) 에 맞는 시간대를 골라 주기·초록·화살표·옵셋을 정한다</summary>
        public void Select(int minute)
        {
            active = null;
            foreach (var s in slots) if (s.startMin <= minute && (active == null || s.startMin > active.startMin)) active = s;
            if (active == null) foreach (var s in slots) if (active == null || s.startMin > active.startMin) active = s;   // 자정 앞 계획이 이어짐
            if (active != null && Fit(active, minute)) return;
            active = null;
            Default();
        }

        void Default()
        {
            int nAll = nA + nB;
            cycle = nAll <= 4 ? 90f : (nAll <= 10 ? 120f : 160f);
            lA = nB > 0 && nA >= 4 ? (cycle >= 160f ? 16f : (cycle >= 120f ? 12f : 0f)) : 0f;
            lB = nB >= 4 ? (cycle >= 160f ? 16f : (cycle >= 120f ? 12f : 0f)) : 0f;
            float usable = cycle - 2f * (Y + AR) - LA - LB;
            gA = nB == 0 ? cycle - Y - AR : Math.Max(15f, Math.Min(usable - 15f, usable * nA / Math.Max(1, nAll)));
            gB = nB == 0 ? 0f : usable - gA;
            offA = defOffset;
        }

        bool Fit(SignalSlot s, int minute)
        {
            var p = new List<float>();
            foreach (var v in s.phases) if (v > 0f) p.Add(v);
            if (p.Count < 2 || s.cycle < 30f) return false;
            float sum = 0f;
            foreach (var v in p) sum += v;
            if (Math.Abs(sum - s.cycle) > 1f) for (int i = 0; i < p.Count; i++) p[i] *= s.cycle / sum;   // A 링 합을 주기에 맞춤
            cycle = s.cycle;
            float ta, la = 0f, tb, lb = 0f;
            if (nB == 0) { ta = s.cycle; tb = 0f; }
            else if (p.Count == 2) { ta = p[0]; tb = p[1]; }
            else if (p.Count == 3) { ta = p[0]; la = p[1]; tb = p[2]; }
            else { ta = p[0]; la = p[1]; tb = p[2]; for (int i = 3; i < p.Count; i++) lb += p[i]; }
            // 현시 → 초록: 직진 현시 = 초록 + 노랑, 좌회전 현시 = 화살표 + 노랑 (+ 갈래 끝 전적색)
            if (la > 0f) { gA = ta - Y; lA = la - Y - AR; } else { gA = ta - Y - AR; lA = 0f; }
            if (nB == 0) { gB = 0f; lB = 0f; }
            else if (lb > 0f) { gB = tb - Y; lB = lb - Y - AR; } else { gB = tb - Y - AR; lB = 0f; }
            if (gA < 5f || (nB > 0 && gB < 5f) || (la > 0f && lA < 3f) || (lb > 0f && lB < 3f)) return false;
            // 옵셋: 하루 시계(자정부터 초)에서 이 교차로 1 현시가 시작하는 때. 게임 시간 0 = 이 시간대의 그 시각(minute)
            offA = minute * 60f - s.offset;
            return true;
        }

        /// <summary>신호 하나에 지금 시간대 값을 넣는다(A 갈래는 주기 처음, B 는 A 의 직진·화살표·전적색 뒤)</summary>
        public void Apply(TrafficSignal t)
        {
            float g0 = t.groupA ? gA : gB, l0 = t.groupA ? lA : lB, L0 = t.groupA ? LA : LB, gOther = t.groupA ? gB : gA;
            t.yellow = Y;
            t.green = g0;
            t.leftGreen = l0;
            t.red = cycle - g0 - Y - L0;
            t.offset = t.groupA ? offA : offA - (gA + Y + LA + AR);
            t.pedStart = g0 + Y + L0 + AR;
            t.pedLen = nB == 0 ? 0f : gOther;
        }
    }
}
