// 공통 타입, 모든 모듈이 사용
package kr.drivecoach.core.signal

/** 변속 기어 상태. */
enum class Gear { P, R, N, D, UNKNOWN }

/** 방향지시등 상태. */
enum class TurnSignal { OFF, LEFT, RIGHT, UNKNOWN }

/** 전조등 상태. */
enum class Headlight { OFF, AUTO, LOW, HIGH, UNKNOWN }

/** 와이퍼 상태. */
enum class Wiper { OFF, INT, LOW, HIGH, UNKNOWN }

/**
 * 한 시점의 차량 상태 스냅샷. 앱이 감지할 수 있는 신호만 담는다.
 * 감지 불가 항목(미러 각도, 옆 차 유무, 주차선)은 의도적으로 없다.
 * 액셀 페달은 차가 AAOS 표준 속성(ACCELERATOR_PEDAL_COMPRESSION_PERCENTAGE)을 줄 때만 값이 있다(3D 게임의 "실제 액셀" 모드용).
 * null 은 "이 차량/시점에서 값을 모름"을 뜻한다.
 */
data class VehicleState(
    val timestampMs: Long,
    val speedKmh: Double? = null,          // AAOS PERF_VEHICLE_SPEED
    val rpm: Double? = null,               // AAOS ENGINE_RPM
    val steeringAngleDeg: Double? = null,  // AAOS PERF_STEERING_ANGLE, 양수 = 시계방향(우회전)
    val gear: Gear = Gear.UNKNOWN,         // AAOS GEAR_SELECTION
    val brakePressed: Boolean? = null,     // Vehicle SDK Brake 도메인
    val parkingBrakeOn: Boolean? = null,   // Vehicle SDK Brake 도메인
    val seatBeltWarning: Boolean? = null,  // 경고등 켜짐 = 운전석 미착용
    val turnSignal: TurnSignal = TurnSignal.OFF,
    val hazardOn: Boolean? = null,
    val headlight: Headlight = Headlight.UNKNOWN,
    val wiper: Wiper = Wiper.UNKNOWN,
    val windowsClosed: Boolean? = null,
    val doorsLocked: Boolean? = null,
    val hvacOn: Boolean? = null,
    val seatPositionChanged: Boolean = false, // 직전 스냅샷 대비 시트 위치 변화 이벤트
    val ignitionOn: Boolean? = null,
    val odometerKm: Double? = null,
    val fuelPercent: Double? = null,
    val warningLamps: Set<String> = emptySet(), // 켜진 경고등 ID 집합 (warning 모듈의 ID 체계)
    val lat: Double? = null,
    val lon: Double? = null,
    val accelPedalPct: Double? = null,     // AAOS ACCELERATOR_PEDAL_COMPRESSION_PERCENTAGE 0~100 (READ_CAR_PEDALS, 차가 줄 때만)
)

/** 차량 제원. CarInfo 도메인에서 읽거나, 없으면 차종별 기본값을 쓴다. */
data class CarSpec(
    val name: String,
    val lengthM: Double,
    val widthM: Double,
    val wheelbaseM: Double,
    val maxSteeringWheelDeg: Double, // 핸들 한쪽 끝까지 돌렸을 때 각도 (예: 480 = 1.33바퀴)
    val steeringRatio: Double,       // 핸들 각 / 바퀴 각
    /** 차량에서 실제로 읽어 맞춘 값인지(true) 기본 차종인지(false). */
    val detected: Boolean = false,
    /** 앞·뒤 오버행(m). 없으면 (전장 - 축거)/2 로 나눈다. */
    val frontOverhangM: Double? = null,
    val rearOverhangM: Double? = null,
    /** 윤거(m). 카탈로그 값. */
    val trackFrontM: Double? = null,
    val trackRearM: Double? = null,
    /** 최소회전반경(m, 바깥 앞바퀴 중심). 카탈로그에 없어 차급 일반값으로 추정 → [turningRadiusEstimated]. */
    val minTurningRadiusM: Double? = null,
    val turningRadiusEstimated: Boolean = true,
) {
    companion object {
        /**
         * 기준 차량 디 올 뉴 아반떼(CN8). 현대닷컴 디지털 카탈로그(2026-11) 제원.
         * 오버행은 측면도 비율로 추정, 최소회전반경 5.3 m 는 차급 일반값(추정), 조향비는 그 둘에서 역산.
         */
        val AVANTE_CN8 = CarSpec(
            "아반떼 CN8", lengthM = 4.765, widthM = 1.855, wheelbaseM = 2.75, maxSteeringWheelDeg = 480.0, steeringRatio = 13.15,
            frontOverhangM = 0.915, rearOverhangM = 1.10, trackFrontM = 1.629, trackRearM = 1.639, minTurningRadiusM = 5.3,
        )
    }
}

/** 차량 상태를 받는 콜백. core 에서는 코루틴 대신 이 인터페이스를 쓴다. */
fun interface StateListener {
    fun onState(state: VehicleState)
}

/** 차량 신호 공급원. app 모듈의 Pleos 어댑터가 구현한다. */
interface SignalSource {
    /** 가장 최근 상태. */
    val current: VehicleState
    fun addListener(listener: StateListener)
    fun removeListener(listener: StateListener)
}

/** 테스트·시뮬레이션용 신호 공급원. push() 로 상태를 밀어 넣는다. */
class FakeSignalSource(initial: VehicleState = VehicleState(timestampMs = 0)) : SignalSource {
    private val listeners = mutableListOf<StateListener>()
    override var current: VehicleState = initial
        private set
    override fun addListener(listener: StateListener) { listeners += listener }
    override fun removeListener(listener: StateListener) { listeners -= listener }
    /** 새 상태를 반영하고 리스너에 전달한다. */
    fun push(state: VehicleState) { current = state; listeners.toList().forEach { it.onState(state) } }
    /** 현재 상태를 일부만 바꿔 push 한다. */
    fun update(timestampMs: Long, change: (VehicleState) -> VehicleState) = push(change(current.copy(timestampMs = timestampMs)))
}
