// 담당: S
// 차 한 대의 움직임(위에서 본 2D). 기준점 = 뒷축 가운데. 좌표 m, x 오른쪽 / y 아래, heading 0 = +x, 시계 방향 +.
//
// 조향: 자전거 모델. 바퀴 각 δ = 핸들 각 / 조향비(제원에서 역산). θ' = v·tan(δ)/축거.
// 속도: 1차 지연 시스템(대표 결정 11/5).
//   브레이크를 밟고 있으면  dv/dt = −v/τ_brake  (가장 약하게 밟았다고 가정)
//   떼면(D/R) 크리프 속도로  dv/dt = (v_creep − v)/τ_creep
//   N 은 굴러가며 서서히 줄고, 경사에서는 아래로 흐른다.
//   엔진 회전수를 아는 차(엔진차)는 회전수가 공회전보다 오른 만큼 목표 속도를 조금 올린다(액셀 간접 인식).
using System;
using System.Collections.Generic;

namespace DriveCoach.Core
{
    /// <summary>차종 제원(m, 도). 앱이 보내는 JSON(GameLink.specJson)과 같은 이름.</summary>
    [Serializable]
    public class CarSpec
    {
        public string name = "아반떼 CN8";
        public float lengthM = 4.765f, widthM = 1.855f, wheelbaseM = 2.75f;
        public float frontOverhangM = 0.915f, rearOverhangM = 1.10f;
        public float trackFrontM = 1.629f, trackRearM = 1.639f;
        public float minTurningRadiusM = 5.3f;
        public float maxSteeringWheelDeg = 480f, steeringRatio = 13.15f;
        // JSON 에 없던 값(NaN = 없음 → 비율로 추정)
        public bool hasFrontOverhang = true, hasRearOverhang = true, hasTrackFront = true, hasTrackRear = true, hasMinRadius = true;

        /// <summary>앱 연결 전·PC 시험용 기본 제원: 디 올 뉴 아반떼(CN8) 카탈로그 값</summary>
        public static CarSpec Default() => new CarSpec();

        /// <summary>GameLink.specJson 을 읽는다. 없는 값은 기본값 대신 "없음"으로 두어 Vehicle 이 비율로 추정하게 한다.</summary>
        public static CarSpec FromJson(string json)
        {
            var s = Default();
            var o = MiniJson.ParseObject(json);
            if (o == null || !o.ContainsKey("lengthM")) return s;
            s.name = MiniJson.Str(o, "name", s.name);
            s.lengthM = MiniJson.Num(o, "lengthM", s.lengthM);
            s.widthM = MiniJson.Num(o, "widthM", s.widthM);
            s.wheelbaseM = MiniJson.Num(o, "wheelbaseM", s.wheelbaseM);
            s.maxSteeringWheelDeg = MiniJson.Num(o, "maxSteeringWheelDeg", s.maxSteeringWheelDeg);
            s.steeringRatio = MiniJson.Num(o, "steeringRatio", s.steeringRatio);
            s.hasFrontOverhang = o.ContainsKey("frontOverhangM");
            s.hasRearOverhang = o.ContainsKey("rearOverhangM");
            s.hasTrackFront = o.ContainsKey("trackFrontM");
            s.hasTrackRear = o.ContainsKey("trackRearM");
            s.hasMinRadius = o.ContainsKey("minTurningRadiusM");
            if (s.hasFrontOverhang) s.frontOverhangM = MiniJson.Num(o, "frontOverhangM", s.frontOverhangM);
            if (s.hasRearOverhang) s.rearOverhangM = MiniJson.Num(o, "rearOverhangM", s.rearOverhangM);
            if (s.hasTrackFront) s.trackFrontM = MiniJson.Num(o, "trackFrontM", s.trackFrontM);
            if (s.hasTrackRear) s.trackRearM = MiniJson.Num(o, "trackRearM", s.trackRearM);
            if (s.hasMinRadius) s.minTurningRadiusM = MiniJson.Num(o, "minTurningRadiusM", s.minTurningRadiusM);
            return s;
        }
    }

    public enum Gear { P, R, N, D }

    /// <summary>
    /// 속도 성질. 주차는 크리프(4~5 km/h)만, 서킷·도로 주행은 액셀로 더 빨리 간다.
    /// 실제 차는 P 에 둔 채로 하므로 액셀은 "밟은 정도 0..1" 로만 들어온다: 자동 속도(AutoSpeed, 핸들만 돌리기)가 정하거나,
    /// 차가 알려 주면 실제 액셀 페달(0~100%)을 쓴다.
    /// </summary>
    public class DriveModel
    {
        public float creepD = 1.4f, creepR = 1.1f;   // m/s
        public float accelExtra = 1.4f;              // 액셀을 끝까지 밟았을 때 더해지는 목표 속도(m/s, 전진)
        public float accelExtraR = 1.4f;             // 후진(빨리 후진하는 연습은 하지 않는다)
        public float tauCreep = 0.9f;                // 목표 속도에 다가가는 시간상수(초)
        public float tauBrake = 0.7f;                // 브레이크(가장 약하게) 시간상수(초)
        public float tauDown = 0f;                   // 액셀을 떼어 느려질 때 시간상수(0 = tauCreep 과 같음, 주차)
        public float maxBrake = 0f;                  // 브레이크 최대 감속(m/s², 0 = 제한 없음: 주차 판과 같음)

        /// <summary>주차 연습(Godot 판과 같은 값)</summary>
        public static DriveModel Parking() => new DriveModel();

        /// <summary>
        /// 서킷·도로 주행: 액셀 끝까지 약 50 km/h. 액셀을 떼면 천천히(엔진 브레이크 정도) 줄고,
        /// 브레이크는 속도에 비례하되 5 m/s²(단단히 밟은 정도)를 넘지 않는다 — 빠를 때 갑자기 서지 않게.
        /// </summary>
        public static DriveModel Road() => new DriveModel { accelExtra = 12.5f, accelExtraR = 1.4f, tauCreep = 2.6f, tauDown = 5f, tauBrake = 0.9f, maxBrake = 5f };
    }

    public class Vehicle
    {
        public const float TauBrake = 0.7f;     // 브레이크(가장 약하게) 시간상수, 초
        public const float TauCreep = 0.9f;     // 크리프 도달 시간상수, 초
        public const float TauCoast = 4.0f;     // N 에서 저절로 줄어드는 시간상수
        public const float CreepD = 1.4f;       // m/s (약 5 km/h)
        public const float CreepR = 1.1f;       // m/s (약 4 km/h)
        public const float AccelExtra = 1.4f;   // 회전수 가속으로 더할 수 있는 최대 속도(m/s)
        public const float StopEps = 0.05f;     // 이보다 느리면 완전히 선다
        public const float G = 9.81f;

        public CarSpec spec;
        public float wheelbase, length, width, frontOh, rearOh, trackF, trackR;
        public float maxWheelDeg;   // 바퀴(자전거 모델 가운데) 최대각
        public float maxSteerDeg;   // 핸들 한쪽 끝
        public float ratio;         // 조향비

        public DriveModel model = DriveModel.Parking();

        public V2 pos = V2.Zero;    // 뒷축 가운데
        public float heading;       // 라디안
        public float speed;         // m/s, 후진은 음수
        public Gear gear = Gear.P;
        public float steerDeg;      // 핸들 각(양수 = 오른쪽)
        public float odometer;

        // 11/8 밤(조사 P1 "실차 스위치 연동"): 실제 차의 방향지시등·비상등·전조등·와이퍼·안전벨트(앱이 Pleos 신호로 보냄).
        // 차가 안 주면 화면·키보드(Z 왼쪽, C 오른쪽, X 비상등)로 켠다. signalKnown = 방향지시등 값을 받을 수 있는가(규칙 안내를 할지)
        public int turnSignal;      // -1 왼쪽, 0 끔, +1 오른쪽
        public bool hazard;
        public bool signalKnown;
        public int headlight = -1;  // -1 모름, 0 끔, 1 하향(켜짐), 2 상향, 3 자동
        public int wiper = -1;      // -1 모름, 0 끔, 1 간헐, 2 느리게, 3 빠르게
        public int belt = -1;       // -1 모름, 0 안 맴, 1 맴
        /// <summary>땅에서 높이(m): 고가 위 +, 지하차도 −(11/8 밤 입체 교차). 물리·충돌은 2D 그대로, 층이 다르면 서로 닿지 않는다</summary>
        public float z;

        public Vehicle(CarSpec s) { SetSpec(s); }

        public void SetSpec(CarSpec s)
        {
            spec = s;
            length = s.lengthM;
            width = s.widthM;
            wheelbase = s.wheelbaseM;
            float ohTotal = length - wheelbase;
            frontOh = s.hasFrontOverhang ? s.frontOverhangM : ohTotal * 0.454f;
            rearOh = s.hasRearOverhang ? s.rearOverhangM : ohTotal - frontOh;
            trackF = s.hasTrackFront ? s.trackFrontM : width - 0.22f;
            trackR = s.hasTrackRear ? s.trackRearM : trackF;
            maxSteerDeg = s.maxSteeringWheelDeg;
            float rMin = s.hasMinRadius ? s.minTurningRadiusM : 0f;
            if (rMin > wheelbase)
            {
                maxWheelDeg = WheelAngleForMinRadius(rMin) * Geom.Rad2Deg;
                ratio = maxSteerDeg / maxWheelDeg;
            }
            else
            {
                ratio = s.steeringRatio;
                maxWheelDeg = maxSteerDeg / ratio;
            }
        }

        /// <summary>최소회전반경(바깥 앞바퀴 중심 원)에서 자전거 모델 가운데 바퀴 각(라디안)</summary>
        public float WheelAngleForMinRadius(float rMin)
        {
            float rRear = (float)Math.Sqrt(rMin * rMin - wheelbase * wheelbase) - trackF / 2f;
            return (float)Math.Atan(wheelbase / rRear);
        }

        /// <summary>지금 바퀴 각(라디안, 양수 = 오른쪽)</summary>
        public float WheelAngle()
        {
            float d = steerDeg / ratio;
            return Geom.Clamp(d, -maxWheelDeg, maxWheelDeg) * Geom.Deg2Rad;
        }

        public float MinTurningRadius()
        {
            float rRear = wheelbase / (float)Math.Tan(maxWheelDeg * Geom.Deg2Rad);
            return (float)Math.Sqrt(Math.Pow(rRear + trackF / 2f, 2) + wheelbase * wheelbase);
        }

        public V2 Dir => new V2((float)Math.Cos(heading), (float)Math.Sin(heading));
        public V2 Left => new V2((float)Math.Sin(heading), -(float)Math.Cos(heading));
        public V2 Center => pos + Dir * ((wheelbase + frontOh - rearOh) / 2f);
        public V2 FrontAxle => pos + Dir * wheelbase;

        /// <summary>차체 네 모서리(앞왼, 앞오, 뒤오, 뒤왼)</summary>
        public V2[] Corners(float margin = 0f)
        {
            var f = Dir * (wheelbase + frontOh + margin);
            var b = Dir * (-rearOh - margin);
            var l = Left * (width / 2f + margin);
            return new[] { pos + f + l, pos + f - l, pos + b - l, pos + b + l };
        }

        /// <summary>네 바퀴가 연석 높이(약 15 cm)에서 차지하는 자리(앞바퀴는 바퀴 각만큼 돌아 있다). 0.5 x 0.22 m</summary>
        public List<V2[]> Tires()
        {
            var outList = new List<V2[]>(4);
            float d = WheelAngle();
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                var c = pos + Dir * (front ? wheelbase : 0f) - Left * side * ((front ? trackF : trackR) / 2f);
                outList.Add(Geom.RectPoly(c, 0.5f, 0.22f, (heading + (front ? d : 0f)) * Geom.Rad2Deg));
            }
            return outList;
        }

        public V2 FrontBumperCenter => pos + Dir * (wheelbase + frontOh);
        public V2 RearBumperCenter => pos - Dir * rearOh;

        /// <summary>운전석(왼쪽) 어깨 — 주차 공식 기준점. 앞축에서 약 1.25 m 뒤, 차 왼쪽 안으로 0.45 m.</summary>
        public V2 Shoulder => pos + Dir * (wheelbase - 1.25f) + Left * (width / 2f - 0.45f);

        /// <summary>사이드미러 위치(side: -1 왼쪽, +1 오른쪽)</summary>
        public V2 Mirror(float side) => pos + Dir * (wheelbase - 0.55f) - Left * side * (width / 2f + 0.12f);

        /// <summary>한 번 진행. gradeAlong: 차 앞 방향 기준 오르막 +, 내리막 − (0.12 = 12%)</summary>
        /// <param name="brakeAmount">브레이크 세기 0..1(사람 발 = 1, 자동 속도가 살짝 줄일 때는 작게). 최대 감속(model.maxBrake)에 곱한다.</param>
        public void Step(float dt, bool brake, float accel, float gradeAlong, float brakeAmount = 1f)
        {
            const int sub = 4;
            float h = dt / sub;
            for (int i = 0; i < sub; i++)
            {
                IntegrateSpeed(h, brake, accel, gradeAlong, brakeAmount);
                float d = WheelAngle();
                float ds = speed * h;
                heading += ds * (float)Math.Tan(d) / wheelbase;
                pos += Dir * ds;
                odometer += Math.Abs(ds);
            }
        }

        void IntegrateSpeed(float h, bool brake, float accel, float gradeAlong, float brakeAmount)
        {
            float slopeAcc = -G * gradeAlong * 0.6f;
            if (gear == Gear.P) { speed = 0f; return; }
            if (brake)
            {
                float dec = Math.Abs(speed) / model.tauBrake;
                if (model.maxBrake > 0f) dec = Math.Min(dec, model.maxBrake * Geom.Clamp(brakeAmount, 0.05f, 1f));
                float ns = speed - Math.Sign(speed) * dec * h;
                if (Math.Sign(ns) != Math.Sign(speed) || Math.Abs(ns) < StopEps) ns = 0f;
                speed = ns;
                return;
            }
            if (gear == Gear.N)
            {
                speed += (-speed / TauCoast + slopeAcc) * h;
                if (Math.Abs(speed) < StopEps && Math.Abs(gradeAlong) < 0.02f) speed = 0f;
                return;
            }
            float sign = gear == Gear.D ? 1f : -1f;
            float extra = gear == Gear.D ? model.accelExtra : model.accelExtraR;
            float creep = (gear == Gear.D ? model.creepD : model.creepR) + extra * Geom.Clamp(accel, 0f, 1f);
            float along = gradeAlong * sign;
            creep *= Geom.Clamp(1f - 3f * along, 0.25f, 1.8f);
            float target = sign * creep;
            float tau = model.tauDown > 0f && Math.Abs(target) < Math.Abs(speed) && Math.Sign(target) == Math.Sign(speed) ? model.tauDown : model.tauCreep;
            speed += ((target - speed) / tau + slopeAcc * 0.15f) * h;
        }

        public bool GearChange(Gear g)
        {
            if (Math.Abs(speed) > 0.15f && g != gear) return false;
            gear = g;
            if (g == Gear.P) speed = 0f;
            return true;
        }

        /// <summary>뒷축 가운데에서 지금 바퀴 각으로 s(m, 음수 = 후진) 갔을 때 뒷축 위치와 방향(라디안). view.gd pose_after.</summary>
        public void PoseAfter(float s, out V2 p, out float h)
        {
            float d = WheelAngle();
            float h0 = heading;
            if (Math.Abs(d) < 0.0005f) { p = pos + V2.FromAngle(h0) * s; h = h0; return; }
            float r = wheelbase / (float)Math.Tan(d);
            var right0 = new V2(-(float)Math.Sin(h0), (float)Math.Cos(h0));
            var c = pos + right0 * r;
            h = h0 + s / r;
            p = c - new V2(-(float)Math.Sin(h), (float)Math.Cos(h)) * r;
        }
    }
}
