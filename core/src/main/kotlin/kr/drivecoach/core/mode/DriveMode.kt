// 담당: C+G (P/주행 모드 판정, UI 정책)
package kr.drivecoach.core.mode

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.VehicleState

/**
 * 앱의 큰 동작 모드. 안전 원칙:
 * "화면을 보며 배우는 것은 주차(P) 상태에서만. 주행 중에는 음성만, 화면은 한 줄 요약까지."
 */
enum class DriveMode {
    /** 주차 상태. 화면 학습(일러스트, 퀴즈, 리포트)과 터치 메뉴 허용. */
    PARKED_LEARN,

    /** 주행(또는 움직일 수 있는) 상태. 음성만, 화면은 한 줄 요약. */
    MOVING_VOICE_ONLY,

    /** 신호가 부족해 판정 불가. 안전을 위해 MOVING 과 같은 제한을 적용한다. */
    UNKNOWN,
}

/**
 * 모드별로 화면에 허용되는 것. 숫자는 "한 줄 요약 이상 띄우지 않는다" 원칙을 그대로 옮긴 것.
 *
 * AAOS `CarUxRestrictions` 와의 결합(앱 담당):
 *  - 앱은 `CarUxRestrictionsManager` 의 현재 제한과 이 정책을 **OR 조건**으로 합친다.
 *    즉 둘 중 더 엄격한 쪽을 따른다 → [stricter] 로 두 정책을 합치면 된다.
 *  - 예) 이 모듈이 PARKED_LEARN 이어도 `UX_RESTRICTIONS_NO_VIDEO`/`LIMIT_STRING_LENGTH` 등이 켜져 있으면
 *    allowFullScreenContent=false, maxLines 는 `getMaxRestrictedStringLength()` 기준으로 더 줄인다.
 *  - 반대로 CarUxRestrictions 가 "제한 없음"이라도 이 모듈이 MOVING 이면 한 줄만 보여준다(앱 자체 원칙이 더 엄격).
 */
data class UiPolicy(
    /** 전체 화면 콘텐츠(일러스트, 퀴즈, 긴 설명, 리포트) 허용 여부. */
    val allowFullScreenContent: Boolean,
    /** 화면에 동시에 띄울 수 있는 최대 줄 수. */
    val maxLines: Int,
    /** 터치로 조작하는 메뉴(목록 스크롤, 코스 선택 등) 허용 여부. */
    val allowTouchMenus: Boolean,
) {
    companion object {
        /** 주차 상태: 모든 화면 학습 허용. 줄 수 제한은 사실상 없음. */
        val PARKED = UiPolicy(allowFullScreenContent = true, maxLines = Int.MAX_VALUE, allowTouchMenus = true)

        /** 주행 상태: 한 줄 요약만, 터치 메뉴 없음. */
        val MOVING = UiPolicy(allowFullScreenContent = false, maxLines = 1, allowTouchMenus = false)

        /** 모드에 대응하는 정책. UNKNOWN 은 안전하게 MOVING 과 동일. */
        fun forMode(mode: DriveMode): UiPolicy = when (mode) {
            DriveMode.PARKED_LEARN -> PARKED
            DriveMode.MOVING_VOICE_ONLY, DriveMode.UNKNOWN -> MOVING
        }

        /** 두 정책 중 더 엄격한 쪽을 항목별로 고른다(앱에서 CarUxRestrictions 와 합칠 때 사용). */
        fun stricter(a: UiPolicy, b: UiPolicy): UiPolicy = UiPolicy(
            allowFullScreenContent = a.allowFullScreenContent && b.allowFullScreenContent,
            maxLines = minOf(a.maxLines, b.maxLines),
            allowTouchMenus = a.allowTouchMenus && b.allowTouchMenus,
        )
    }
}

/** 모드가 바뀌었을 때 호출되는 콜백. */
fun interface DriveModeListener {
    fun onModeChanged(previous: DriveMode, next: DriveMode, state: VehicleState)
}

/**
 * 차량 상태를 받아 [DriveMode] 를 판정한다. [StateListener] 로 SignalSource 에 등록해 쓴다.
 *
 * 판정 규칙(순서대로, 먼저 맞는 것):
 *  1. speed > 0 → MOVING_VOICE_ONLY (기어가 P 라고 보고되더라도 움직이면 주행으로 본다)
 *  2. gear == P → PARKED_LEARN
 *  3. parkingBrakeOn == true && speed == 0 → PARKED_LEARN
 *  4. gear in (D, R, N) 이고 주차브레이크가 켜져 있지 않음(해제 또는 모름) → MOVING_VOICE_ONLY
 *  5. 그 외 → UNKNOWN (MOVING 과 같은 제한)
 *
 * 히스테리시스: 새 모드가 [toMovingHoldMs]/[toParkedHoldMs] 동안 **연속으로** 관측돼야 전환한다.
 * 안전 쪽(MOVING/UNKNOWN)으로 가는 전환은 짧게, 느슨한 쪽(PARKED)으로 가는 전환은 길게 둔다.
 * 신호가 잠깐 튀어 모드가 깜빡이는 것(예: 주차브레이크 신호 순간 끊김)을 막기 위함.
 */
class DriveModeDetector(
    /** PARKED → MOVING/UNKNOWN 전환에 필요한 연속 관측 시간(ms). 안전 쪽이므로 짧게. */
    private val toMovingHoldMs: Long = DEFAULT_TO_MOVING_HOLD_MS,
    /** MOVING/UNKNOWN → PARKED 전환에 필요한 연속 관측 시간(ms). */
    private val toParkedHoldMs: Long = DEFAULT_TO_PARKED_HOLD_MS,
    /** 이 속도 이하는 0 으로 본다(km/h). 센서 노이즈 흡수. */
    private val speedZeroEpsilonKmh: Double = DEFAULT_SPEED_EPSILON_KMH,
    initialMode: DriveMode = DriveMode.UNKNOWN,
) : StateListener {

    /** 현재 확정된 모드. */
    var mode: DriveMode = initialMode
        private set

    /** 현재 모드에 대응하는 UI 정책. */
    val uiPolicy: UiPolicy get() = UiPolicy.forMode(mode)

    private var candidate: DriveMode? = null
    private var candidateSinceMs: Long = 0
    private val listeners = mutableListOf<DriveModeListener>()

    fun addListener(listener: DriveModeListener) { listeners += listener }
    fun removeListener(listener: DriveModeListener) { listeners -= listener }

    /** 히스테리시스 없이 이 스냅샷만 보고 판정한 원시 모드. */
    fun classify(state: VehicleState): DriveMode {
        val speed = state.speedKmh
        val moving = speed != null && speed > speedZeroEpsilonKmh
        val stopped = speed != null && speed <= speedZeroEpsilonKmh
        return when {
            moving -> DriveMode.MOVING_VOICE_ONLY
            state.gear == Gear.P -> DriveMode.PARKED_LEARN
            state.parkingBrakeOn == true && stopped -> DriveMode.PARKED_LEARN
            state.gear in MOVING_GEARS && state.parkingBrakeOn != true -> DriveMode.MOVING_VOICE_ONLY
            else -> DriveMode.UNKNOWN
        }
    }

    override fun onState(state: VehicleState) {
        val raw = classify(state)
        if (raw == mode) {
            candidate = null
            return
        }
        if (raw != candidate) {
            candidate = raw
            candidateSinceMs = state.timestampMs
        }
        val hold = if (raw == DriveMode.PARKED_LEARN) toParkedHoldMs else toMovingHoldMs
        if (state.timestampMs - candidateSinceMs >= hold) {
            val previous = mode
            mode = raw
            candidate = null
            listeners.toList().forEach { it.onModeChanged(previous, raw, state) }
        }
    }

    companion object {
        const val DEFAULT_TO_MOVING_HOLD_MS = 300L
        const val DEFAULT_TO_PARKED_HOLD_MS = 2_000L
        const val DEFAULT_SPEED_EPSILON_KMH = 0.5
        private val MOVING_GEARS = setOf(Gear.D, Gear.R, Gear.N)
    }
}
