// 신호 변화 이벤트 추출
package kr.drivecoach.core.signal

/**
 * 두 [VehicleState] 스냅샷 사이에서 일어난 "변화 이벤트" 종류.
 * practice(기능 연습 자동 통과), safety(위험 피드백), report(리포트) 모듈이 이 enum 을 보고 반응한다.
 */
enum class StateEvent {
    /** 기어가 바뀜(아래 GEAR_TO_* 와 함께 발생). */
    GEAR_CHANGED,
    GEAR_TO_P, GEAR_TO_R, GEAR_TO_N, GEAR_TO_D,

    /** 브레이크 페달 밟음 / 뗌. */
    BRAKE_PRESSED, BRAKE_RELEASED,
    /** 전자식 주차브레이크 걸림 / 풀림. */
    PARKING_BRAKE_ON, PARKING_BRAKE_OFF,

    /** 방향지시등 왼쪽/오른쪽 켜짐, 꺼짐. */
    TURN_SIGNAL_LEFT_ON, TURN_SIGNAL_RIGHT_ON, TURN_SIGNAL_OFF,
    /** 비상등 켜짐 / 꺼짐. */
    HAZARD_ON, HAZARD_OFF,

    /** 전조등 상태 바뀜(아래 HEADLIGHT_* 와 함께 발생). */
    HEADLIGHT_CHANGED,
    HEADLIGHT_OFF, HEADLIGHT_AUTO, HEADLIGHT_LOW, HEADLIGHT_HIGH,

    /** 와이퍼 단계 바뀜(아래 WIPER_* 와 함께 발생). */
    WIPER_CHANGED,
    WIPER_OFF, WIPER_INT, WIPER_LOW, WIPER_HIGH,

    /** 창문 전부 닫힘 / 하나라도 열림. */
    WINDOWS_CLOSED, WINDOWS_OPENED,
    /** 도어 잠김 / 풀림. */
    DOORS_LOCKED, DOORS_UNLOCKED,
    /** 공조 켜짐 / 꺼짐. */
    HVAC_ON, HVAC_OFF,
    /** 시트 위치(앞뒤·등받이·높이 등)가 움직임. */
    SEAT_POSITION_CHANGED,
    /** 운전석 안전벨트 착용 / 해제(경고등 기준). */
    SEAT_BELT_FASTENED, SEAT_BELT_UNFASTENED,
    /** 시동 켜짐 / 꺼짐. */
    IGNITION_ON, IGNITION_OFF,

    /** 경고등이 새로 켜짐 / 꺼짐. 어떤 등인지는 [StateDiff.warningLampsTurnedOn] 로 확인. */
    WARNING_LAMP_ON, WARNING_LAMP_OFF,

    /** 정지 → 움직임 / 움직임 → 정지 (속도 기준 [StateDiff.MOVING_THRESHOLD_KMH]). */
    VEHICLE_STARTED_MOVING, VEHICLE_STOPPED,
    /** 위치(lat/lon)가 갱신됨. */
    LOCATION_UPDATED,
}

/**
 * 순수 함수로 두 상태를 비교해 [StateEvent] 목록을 만든다.
 *
 * 규칙:
 * - 값이 null / UNKNOWN 인 쪽이 있으면 "모름"이므로 이벤트를 내지 않는다(오탐 방지).
 *   예외: [VehicleState.seatPositionChanged] 는 이미 이벤트 플래그이므로 next 가 true 이면 그대로 낸다.
 * - 같은 값이면 이벤트 없음. 결과 순서는 enum 선언 순서와 무관하게 필드 비교 순서를 따른다.
 */
object StateDiff {
    /** 이 속도(km/h)를 넘으면 "움직임"으로 본다. */
    const val MOVING_THRESHOLD_KMH = 1.0

    /** prev → next 사이의 변화 이벤트. 변화가 없으면 빈 리스트. */
    fun diff(prev: VehicleState, next: VehicleState): List<StateEvent> {
        val out = ArrayList<StateEvent>()

        // 기어
        if (prev.gear != Gear.UNKNOWN && next.gear != Gear.UNKNOWN && prev.gear != next.gear) {
            out += StateEvent.GEAR_CHANGED
            out += when (next.gear) {
                Gear.P -> StateEvent.GEAR_TO_P
                Gear.R -> StateEvent.GEAR_TO_R
                Gear.N -> StateEvent.GEAR_TO_N
                Gear.D -> StateEvent.GEAR_TO_D
                Gear.UNKNOWN -> error("unreachable")
            }
        }

        bool(prev.brakePressed, next.brakePressed, StateEvent.BRAKE_PRESSED, StateEvent.BRAKE_RELEASED, out)
        bool(prev.parkingBrakeOn, next.parkingBrakeOn, StateEvent.PARKING_BRAKE_ON, StateEvent.PARKING_BRAKE_OFF, out)

        // 방향지시등
        if (prev.turnSignal != TurnSignal.UNKNOWN && next.turnSignal != TurnSignal.UNKNOWN && prev.turnSignal != next.turnSignal) {
            out += when (next.turnSignal) {
                TurnSignal.LEFT -> StateEvent.TURN_SIGNAL_LEFT_ON
                TurnSignal.RIGHT -> StateEvent.TURN_SIGNAL_RIGHT_ON
                TurnSignal.OFF -> StateEvent.TURN_SIGNAL_OFF
                TurnSignal.UNKNOWN -> error("unreachable")
            }
        }
        bool(prev.hazardOn, next.hazardOn, StateEvent.HAZARD_ON, StateEvent.HAZARD_OFF, out)

        // 전조등
        if (prev.headlight != Headlight.UNKNOWN && next.headlight != Headlight.UNKNOWN && prev.headlight != next.headlight) {
            out += StateEvent.HEADLIGHT_CHANGED
            out += when (next.headlight) {
                Headlight.OFF -> StateEvent.HEADLIGHT_OFF
                Headlight.AUTO -> StateEvent.HEADLIGHT_AUTO
                Headlight.LOW -> StateEvent.HEADLIGHT_LOW
                Headlight.HIGH -> StateEvent.HEADLIGHT_HIGH
                Headlight.UNKNOWN -> error("unreachable")
            }
        }

        // 와이퍼
        if (prev.wiper != Wiper.UNKNOWN && next.wiper != Wiper.UNKNOWN && prev.wiper != next.wiper) {
            out += StateEvent.WIPER_CHANGED
            out += when (next.wiper) {
                Wiper.OFF -> StateEvent.WIPER_OFF
                Wiper.INT -> StateEvent.WIPER_INT
                Wiper.LOW -> StateEvent.WIPER_LOW
                Wiper.HIGH -> StateEvent.WIPER_HIGH
                Wiper.UNKNOWN -> error("unreachable")
            }
        }

        bool(prev.windowsClosed, next.windowsClosed, StateEvent.WINDOWS_CLOSED, StateEvent.WINDOWS_OPENED, out)
        bool(prev.doorsLocked, next.doorsLocked, StateEvent.DOORS_LOCKED, StateEvent.DOORS_UNLOCKED, out)
        bool(prev.hvacOn, next.hvacOn, StateEvent.HVAC_ON, StateEvent.HVAC_OFF, out)
        if (next.seatPositionChanged) out += StateEvent.SEAT_POSITION_CHANGED
        // seatBeltWarning: 경고등 켜짐(true) = 미착용 → 꺼짐(false) 이 "착용"
        bool(prev.seatBeltWarning, next.seatBeltWarning, StateEvent.SEAT_BELT_UNFASTENED, StateEvent.SEAT_BELT_FASTENED, out)
        bool(prev.ignitionOn, next.ignitionOn, StateEvent.IGNITION_ON, StateEvent.IGNITION_OFF, out)

        // 경고등
        if (warningLampsTurnedOn(prev, next).isNotEmpty()) out += StateEvent.WARNING_LAMP_ON
        if (warningLampsTurnedOff(prev, next).isNotEmpty()) out += StateEvent.WARNING_LAMP_OFF

        // 움직임
        val prevMoving = prev.speedKmh?.let { it > MOVING_THRESHOLD_KMH }
        val nextMoving = next.speedKmh?.let { it > MOVING_THRESHOLD_KMH }
        bool(prevMoving, nextMoving, StateEvent.VEHICLE_STARTED_MOVING, StateEvent.VEHICLE_STOPPED, out)

        // 위치
        if (next.lat != null && next.lon != null && (prev.lat != next.lat || prev.lon != next.lon)) {
            out += StateEvent.LOCATION_UPDATED
        }
        return out
    }

    /** prev 에는 없고 next 에 새로 켜진 경고등 ID. */
    fun warningLampsTurnedOn(prev: VehicleState, next: VehicleState): Set<String> = next.warningLamps - prev.warningLamps

    /** prev 에는 있었고 next 에서 꺼진 경고등 ID. */
    fun warningLampsTurnedOff(prev: VehicleState, next: VehicleState): Set<String> = prev.warningLamps - next.warningLamps

    /** Boolean 필드 공통 처리: 둘 다 알려져 있고 값이 다를 때만 on/off 이벤트. */
    private fun bool(prev: Boolean?, next: Boolean?, onTrue: StateEvent, onFalse: StateEvent, out: MutableList<StateEvent>) {
        if (prev == null || next == null || prev == next) return
        out += if (next) onTrue else onFalse
    }
}
