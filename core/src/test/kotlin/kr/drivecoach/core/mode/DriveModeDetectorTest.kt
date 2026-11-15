// 담당: C+G
package kr.drivecoach.core.mode

import kr.drivecoach.core.signal.FakeSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

private fun st(t: Long, speed: Double? = null, gear: Gear = Gear.UNKNOWN, pb: Boolean? = null) =
    VehicleState(timestampMs = t, speedKmh = speed, gear = gear, parkingBrakeOn = pb)

/** 원시 판정 규칙. */
private fun testClassify() {
    val d = DriveModeDetector()
    assertEquals(DriveMode.PARKED_LEARN, d.classify(st(0, gear = Gear.P)))
    assertEquals(DriveMode.PARKED_LEARN, d.classify(st(0, speed = 0.0, gear = Gear.D, pb = true)))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.classify(st(0, speed = 12.0, gear = Gear.D)))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.classify(st(0, speed = 0.0, gear = Gear.D, pb = false)))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.classify(st(0, speed = 0.0, gear = Gear.R, pb = null)))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.classify(st(0, speed = 0.0, gear = Gear.N, pb = false)))
    // 기어 P 로 보고돼도 속도가 있으면 주행
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.classify(st(0, speed = 5.0, gear = Gear.P)))
    // 속도 노이즈(0.3km/h)는 0 으로
    assertEquals(DriveMode.PARKED_LEARN, d.classify(st(0, speed = 0.3, gear = Gear.D, pb = true)))
    // 아무 신호 없음
    assertEquals(DriveMode.UNKNOWN, d.classify(st(0)))
    assertEquals(DriveMode.UNKNOWN, d.classify(st(0, gear = Gear.D, pb = true)))
    println("  classify OK")
}

/** 히스테리시스: 짧게 튀는 신호로는 모드가 안 바뀐다. */
private fun testHysteresis() {
    val src = FakeSignalSource(st(0, gear = Gear.P))
    val d = DriveModeDetector(toMovingHoldMs = 300, toParkedHoldMs = 2000)
    val changes = mutableListOf<Pair<DriveMode, DriveMode>>()
    d.addListener { p, n, _ -> changes += p to n }
    src.addListener(d)

    // UNKNOWN → PARKED 는 2초 유지 후
    src.push(st(0, gear = Gear.P))
    assertEquals(DriveMode.UNKNOWN, d.mode)
    src.push(st(1000, gear = Gear.P))
    assertEquals(DriveMode.UNKNOWN, d.mode)
    src.push(st(2000, gear = Gear.P))
    assertEquals(DriveMode.PARKED_LEARN, d.mode)

    // 기어 D 로 100ms 튀었다가 복귀 → 그대로 PARKED
    src.push(st(2100, speed = 0.0, gear = Gear.D, pb = false))
    assertEquals(DriveMode.PARKED_LEARN, d.mode)
    src.push(st(2200, gear = Gear.P))
    assertEquals(DriveMode.PARKED_LEARN, d.mode)

    // 진짜 출발: D 300ms 연속 → MOVING
    src.push(st(3000, speed = 0.0, gear = Gear.D, pb = false))
    src.push(st(3200, speed = 0.0, gear = Gear.D, pb = false))
    assertEquals(DriveMode.PARKED_LEARN, d.mode)
    src.push(st(3300, speed = 2.0, gear = Gear.D, pb = false))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.mode)

    // 신호등 정차(속도 0, D, 주차브레이크 없음)는 여전히 MOVING
    src.push(st(10_000, speed = 0.0, gear = Gear.D, pb = false))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.mode)

    // 주차브레이크 걸고 정지 → 2초 뒤 PARKED. 1.5초에 잠깐 신호 끊김(pb=null) → 타이머 리셋
    src.push(st(20_000, speed = 0.0, gear = Gear.D, pb = true))
    src.push(st(21_500, speed = 0.0, gear = Gear.D, pb = null)) // UNKNOWN 으로 튐
    src.push(st(21_600, speed = 0.0, gear = Gear.D, pb = true))
    src.push(st(22_000, speed = 0.0, gear = Gear.D, pb = true))
    assertEquals(DriveMode.MOVING_VOICE_ONLY, d.mode, "타이머가 리셋돼야 함")
    src.push(st(23_600, speed = 0.0, gear = Gear.D, pb = true))
    assertEquals(DriveMode.PARKED_LEARN, d.mode)

    assertEquals(
        listOf(
            DriveMode.UNKNOWN to DriveMode.PARKED_LEARN,
            DriveMode.PARKED_LEARN to DriveMode.MOVING_VOICE_ONLY,
            DriveMode.MOVING_VOICE_ONLY to DriveMode.PARKED_LEARN,
        ),
        changes,
    )
    println("  hysteresis OK")
}

/** UI 정책. */
private fun testUiPolicy() {
    val parked = UiPolicy.forMode(DriveMode.PARKED_LEARN)
    val moving = UiPolicy.forMode(DriveMode.MOVING_VOICE_ONLY)
    assertTrue(parked.allowFullScreenContent && parked.allowTouchMenus)
    assertFalse(moving.allowFullScreenContent || moving.allowTouchMenus)
    assertEquals(1, moving.maxLines)
    assertEquals(moving, UiPolicy.forMode(DriveMode.UNKNOWN))
    // CarUxRestrictions 가 더 엄격한 경우를 흉내: 줄 수 3, 터치 불가
    val ux = UiPolicy(allowFullScreenContent = true, maxLines = 3, allowTouchMenus = false)
    val merged = UiPolicy.stricter(parked, ux)
    assertEquals(UiPolicy(allowFullScreenContent = true, maxLines = 3, allowTouchMenus = false), merged)
    assertEquals(moving, UiPolicy.stricter(moving, ux))
    println("  uiPolicy OK")
}

fun main() {
    testClassify()
    testHysteresis()
    testUiPolicy()
    println("DriveModeDetectorTest PASSED")
}
