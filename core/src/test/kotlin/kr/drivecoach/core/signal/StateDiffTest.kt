// StateDiff 테스트
package kr.drivecoach.core.signal

import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.assertFalse

private fun base(t: Long = 0) = VehicleState(
    timestampMs = t, speedKmh = 0.0, rpm = 700.0, gear = Gear.P,
    brakePressed = false, parkingBrakeOn = true, seatBeltWarning = true,
    turnSignal = TurnSignal.OFF, hazardOn = false, headlight = Headlight.OFF, wiper = Wiper.OFF,
    windowsClosed = true, doorsLocked = false, hvacOn = false, ignitionOn = false,
)

private fun testNoChange() {
    val s = base()
    assertEquals(emptyList(), StateDiff.diff(s, s.copy(timestampMs = 1)))
}

private fun testGearChange() {
    val a = base()
    val b = a.copy(gear = Gear.R)
    val ev = StateDiff.diff(a, b)
    assertEquals(listOf(StateEvent.GEAR_CHANGED, StateEvent.GEAR_TO_R), ev)
    assertEquals(listOf(StateEvent.GEAR_CHANGED, StateEvent.GEAR_TO_D), StateDiff.diff(b, b.copy(gear = Gear.D)))
    assertEquals(listOf(StateEvent.GEAR_CHANGED, StateEvent.GEAR_TO_N), StateDiff.diff(b, b.copy(gear = Gear.N)))
    assertEquals(listOf(StateEvent.GEAR_CHANGED, StateEvent.GEAR_TO_P), StateDiff.diff(b, b.copy(gear = Gear.P)))
}

private fun testUnknownProducesNoEvent() {
    val a = base().copy(gear = Gear.UNKNOWN, brakePressed = null, turnSignal = TurnSignal.UNKNOWN, headlight = Headlight.UNKNOWN)
    val b = a.copy(gear = Gear.D, brakePressed = true, turnSignal = TurnSignal.LEFT, headlight = Headlight.LOW)
    assertEquals(emptyList(), StateDiff.diff(a, b), "모름 → 값 은 이벤트가 아니어야 한다")
    assertEquals(emptyList(), StateDiff.diff(b, a), "값 → 모름 도 이벤트가 아니어야 한다")
}

private fun testBrakeAndParkingBrake() {
    val a = base()
    assertEquals(listOf(StateEvent.BRAKE_PRESSED), StateDiff.diff(a, a.copy(brakePressed = true)))
    assertEquals(listOf(StateEvent.BRAKE_RELEASED), StateDiff.diff(a.copy(brakePressed = true), a))
    assertEquals(listOf(StateEvent.PARKING_BRAKE_OFF), StateDiff.diff(a, a.copy(parkingBrakeOn = false)))
    assertEquals(listOf(StateEvent.PARKING_BRAKE_ON), StateDiff.diff(a.copy(parkingBrakeOn = false), a))
}

private fun testTurnSignalAndHazard() {
    val a = base()
    assertEquals(listOf(StateEvent.TURN_SIGNAL_LEFT_ON), StateDiff.diff(a, a.copy(turnSignal = TurnSignal.LEFT)))
    assertEquals(listOf(StateEvent.TURN_SIGNAL_RIGHT_ON), StateDiff.diff(a, a.copy(turnSignal = TurnSignal.RIGHT)))
    assertEquals(listOf(StateEvent.TURN_SIGNAL_OFF), StateDiff.diff(a.copy(turnSignal = TurnSignal.RIGHT), a))
    // 좌 → 우 직접 전환도 "오른쪽 켜짐" 하나만
    assertEquals(listOf(StateEvent.TURN_SIGNAL_RIGHT_ON), StateDiff.diff(a.copy(turnSignal = TurnSignal.LEFT), a.copy(turnSignal = TurnSignal.RIGHT)))
    assertEquals(listOf(StateEvent.HAZARD_ON), StateDiff.diff(a, a.copy(hazardOn = true)))
    assertEquals(listOf(StateEvent.HAZARD_OFF), StateDiff.diff(a.copy(hazardOn = true), a))
}

private fun testHeadlightAndWiper() {
    val a = base()
    assertEquals(listOf(StateEvent.HEADLIGHT_CHANGED, StateEvent.HEADLIGHT_AUTO), StateDiff.diff(a, a.copy(headlight = Headlight.AUTO)))
    assertEquals(listOf(StateEvent.HEADLIGHT_CHANGED, StateEvent.HEADLIGHT_HIGH), StateDiff.diff(a.copy(headlight = Headlight.LOW), a.copy(headlight = Headlight.HIGH)))
    assertEquals(listOf(StateEvent.HEADLIGHT_CHANGED, StateEvent.HEADLIGHT_OFF), StateDiff.diff(a.copy(headlight = Headlight.LOW), a))
    assertEquals(listOf(StateEvent.WIPER_CHANGED, StateEvent.WIPER_INT), StateDiff.diff(a, a.copy(wiper = Wiper.INT)))
    assertEquals(listOf(StateEvent.WIPER_CHANGED, StateEvent.WIPER_LOW), StateDiff.diff(a.copy(wiper = Wiper.INT), a.copy(wiper = Wiper.LOW)))
    assertEquals(listOf(StateEvent.WIPER_CHANGED, StateEvent.WIPER_HIGH), StateDiff.diff(a, a.copy(wiper = Wiper.HIGH)))
    assertEquals(listOf(StateEvent.WIPER_CHANGED, StateEvent.WIPER_OFF), StateDiff.diff(a.copy(wiper = Wiper.HIGH), a))
}

private fun testCabinFields() {
    val a = base()
    assertEquals(listOf(StateEvent.WINDOWS_OPENED), StateDiff.diff(a, a.copy(windowsClosed = false)))
    assertEquals(listOf(StateEvent.WINDOWS_CLOSED), StateDiff.diff(a.copy(windowsClosed = false), a))
    assertEquals(listOf(StateEvent.DOORS_LOCKED), StateDiff.diff(a, a.copy(doorsLocked = true)))
    assertEquals(listOf(StateEvent.DOORS_UNLOCKED), StateDiff.diff(a.copy(doorsLocked = true), a))
    assertEquals(listOf(StateEvent.HVAC_ON), StateDiff.diff(a, a.copy(hvacOn = true)))
    assertEquals(listOf(StateEvent.HVAC_OFF), StateDiff.diff(a.copy(hvacOn = true), a))
    assertEquals(listOf(StateEvent.IGNITION_ON), StateDiff.diff(a, a.copy(ignitionOn = true)))
    assertEquals(listOf(StateEvent.IGNITION_OFF), StateDiff.diff(a.copy(ignitionOn = true), a))
}

private fun testSeat() {
    val a = base()
    // 시트 변화 플래그는 next 에 있으면 그대로 이벤트
    assertEquals(listOf(StateEvent.SEAT_POSITION_CHANGED), StateDiff.diff(a, a.copy(seatPositionChanged = true)))
    assertEquals(emptyList(), StateDiff.diff(a.copy(seatPositionChanged = true), a))
    // 경고등 꺼짐(true→false) = 착용
    assertEquals(listOf(StateEvent.SEAT_BELT_FASTENED), StateDiff.diff(a, a.copy(seatBeltWarning = false)))
    assertEquals(listOf(StateEvent.SEAT_BELT_UNFASTENED), StateDiff.diff(a.copy(seatBeltWarning = false), a))
}

private fun testWarningLamps() {
    val a = base().copy(warningLamps = setOf("ENGINE_CHECK"))
    val b = a.copy(warningLamps = setOf("ENGINE_CHECK", "LOW_FUEL"))
    assertEquals(listOf(StateEvent.WARNING_LAMP_ON), StateDiff.diff(a, b))
    assertEquals(setOf("LOW_FUEL"), StateDiff.warningLampsTurnedOn(a, b))
    assertEquals(emptySet(), StateDiff.warningLampsTurnedOff(a, b))
    val c = a.copy(warningLamps = setOf("LOW_FUEL"))
    // ENGINE_CHECK 꺼지고 LOW_FUEL 켜짐 → 둘 다
    val ev = StateDiff.diff(a, c)
    assertTrue(StateEvent.WARNING_LAMP_ON in ev)
    assertTrue(StateEvent.WARNING_LAMP_OFF in ev)
    assertEquals(setOf("ENGINE_CHECK"), StateDiff.warningLampsTurnedOff(a, c))
}

private fun testMoving() {
    val a = base()
    assertEquals(listOf(StateEvent.VEHICLE_STARTED_MOVING), StateDiff.diff(a, a.copy(speedKmh = 5.0)))
    assertEquals(listOf(StateEvent.VEHICLE_STOPPED), StateDiff.diff(a.copy(speedKmh = 5.0), a.copy(speedKmh = 0.4)))
    // 임계값 이하의 미세 변화는 이벤트 없음
    assertEquals(emptyList(), StateDiff.diff(a.copy(speedKmh = 0.2), a.copy(speedKmh = 0.9)))
    // 속도만 바뀌고 계속 움직이는 중이면 이벤트 없음
    assertEquals(emptyList(), StateDiff.diff(a.copy(speedKmh = 10.0), a.copy(speedKmh = 30.0)))
    // 속도 모름 → 이벤트 없음
    assertEquals(emptyList(), StateDiff.diff(a.copy(speedKmh = null), a.copy(speedKmh = 30.0)))
}

private fun testLocation() {
    val a = base()
    assertEquals(listOf(StateEvent.LOCATION_UPDATED), StateDiff.diff(a, a.copy(lat = 37.5, lon = 127.0)))
    val b = a.copy(lat = 37.5, lon = 127.0)
    assertEquals(emptyList(), StateDiff.diff(b, b.copy(timestampMs = 5)))
    assertEquals(listOf(StateEvent.LOCATION_UPDATED), StateDiff.diff(b, b.copy(lon = 127.1)))
    // 위치가 사라져도(null) 이벤트 없음
    assertFalse(StateEvent.LOCATION_UPDATED in StateDiff.diff(b, a))
}

private fun testMultipleChangesAtOnce() {
    // 시동 → D단 → 출발: 한 스냅샷에 여러 변화가 동시에 있으면 모두 나와야 한다
    val a = base()
    val b = a.copy(ignitionOn = true, gear = Gear.D, parkingBrakeOn = false, brakePressed = true, speedKmh = 3.0, seatBeltWarning = false)
    val ev = StateDiff.diff(a, b)
    val expected = setOf(
        StateEvent.GEAR_CHANGED, StateEvent.GEAR_TO_D, StateEvent.BRAKE_PRESSED, StateEvent.PARKING_BRAKE_OFF,
        StateEvent.SEAT_BELT_FASTENED, StateEvent.IGNITION_ON, StateEvent.VEHICLE_STARTED_MOVING,
    )
    assertEquals(expected, ev.toSet())
    assertEquals(expected.size, ev.size, "중복 이벤트가 없어야 한다")
}

private fun testWorksWithFakeSignalSource() {
    val src = FakeSignalSource(base())
    val collected = ArrayList<StateEvent>()
    var prev = src.current
    src.addListener { s -> collected += StateDiff.diff(prev, s); prev = s }
    src.update(1) { it.copy(brakePressed = true) }
    src.update(2) { it.copy(gear = Gear.D) }
    src.update(3) { it.copy(turnSignal = TurnSignal.LEFT) }
    assertEquals(listOf(StateEvent.BRAKE_PRESSED, StateEvent.GEAR_CHANGED, StateEvent.GEAR_TO_D, StateEvent.TURN_SIGNAL_LEFT_ON), collected)
}

fun main() {
    val tests = listOf(
        ::testNoChange, ::testGearChange, ::testUnknownProducesNoEvent, ::testBrakeAndParkingBrake,
        ::testTurnSignalAndHazard, ::testHeadlightAndWiper, ::testCabinFields, ::testSeat,
        ::testWarningLamps, ::testMoving, ::testLocation, ::testMultipleChangesAtOnce, ::testWorksWithFakeSignalSource,
    )
    var failed = 0
    for (t in tests) {
        try { t(); println("  ok   ${t.name}") } catch (e: Throwable) { failed++; println("  FAIL ${t.name}: $e") }
    }
    if (failed > 0) { println("StateDiffTest: $failed failed"); kotlin.system.exitProcess(1) }
    println("StateDiffTest: all ${tests.size} passed")
}
