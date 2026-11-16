// 위험 피드백
package kr.drivecoach.core.safety

import kr.drivecoach.core.signal.FakeSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kotlin.math.abs
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** FakeSignalSource 에 시계열을 밀어 넣는 작은 시뮬레이터. 100ms 간격 샘플. */
private class Sim(idle: () -> Double = { 800.0 }) {
    val src = FakeSignalSource()
    val rules = DangerRules(idleRpmProvider = idle)
    val fired = mutableListOf<DangerEvent>()
    var t = 0L
    init {
        src.addListener(rules)
        rules.addListener { fired += it }
    }
    /** dtMs 뒤 상태를 하나 푸시. */
    fun step(dtMs: Long = 100, change: (VehicleState) -> VehicleState) { t += dtMs; src.update(t) { change(it) } }
    /** 일정 상태를 durationMs 동안 100ms 간격으로 유지. */
    fun hold(durationMs: Long, change: (VehicleState) -> VehicleState) { repeat((durationMs / 100).toInt()) { step(100, change) } }
    /** from→to 로 durationMs 에 걸쳐 선형 변화 (속도·조향 공통). */
    fun ramp(durationMs: Long, from: Double, to: Double, apply: (VehicleState, Double) -> VehicleState) {
        val n = (durationMs / 100).toInt()
        for (i in 1..n) step(100) { apply(it, from + (to - from) * i / n) }
    }
    fun count(type: DangerType) = fired.count { it.type == type }
}

private fun check(name: String, body: () -> Unit) {
    try { body(); println("  ok  $name") } catch (e: Throwable) { println("  FAIL $name: ${e.message}"); throw e }
}

// ───────────────────────── SPEEDING ─────────────────────────

private fun testSpeeding() = check("SPEEDING: 제한+여유 초과 3초 지속 시 1회, 에피소드 중 재발화 없음, 쿨다운 후 재발화") {
    val sim = Sim()
    sim.rules.speedLimitKmh = 50.0
    sim.hold(2_900) { it.copy(speedKmh = 60.0) }
    assertEquals(0, sim.count(DangerType.SPEEDING), "3초 전에는 발화하지 않음")
    sim.hold(200) { it.copy(speedKmh = 60.0) }
    assertEquals(1, sim.count(DangerType.SPEEDING), "3초 지속 후 발화")
    val ev = sim.fired.first()
    assertEquals(Severity.MEDIUM, ev.severity)
    assertEquals(DangerPhrases.SPEEDING, ev.phraseId)
    assertEquals(60.0, ev.evidence["speedKmh"])
    assertEquals(50.0, ev.evidence["limitKmh"])

    sim.hold(10_000) { it.copy(speedKmh = 62.0) }
    assertEquals(1, sim.count(DangerType.SPEEDING), "계속 과속해도 같은 에피소드면 한 번만")

    // 제한속도 아래로 내려왔다가 쿨다운(20초) 안에 다시 과속 → 발화 안 함
    sim.hold(1_000) { it.copy(speedKmh = 45.0) }
    sim.hold(4_000) { it.copy(speedKmh = 60.0) }
    assertEquals(1, sim.count(DangerType.SPEEDING), "쿨다운 안에서는 재발화 없음")

    // 쿨다운 지난 뒤 새 에피소드 → 발화
    sim.hold(1_000) { it.copy(speedKmh = 45.0) }
    sim.hold(20_000) { it.copy(speedKmh = 40.0) }
    sim.hold(4_000) { it.copy(speedKmh = 75.0) }
    assertEquals(2, sim.count(DangerType.SPEEDING), "쿨다운 후 새 에피소드는 발화")
    assertEquals(Severity.HIGH, sim.fired.last().severity, "20km/h 이상 초과는 HIGH")
    assertEquals(DangerPhrases.SPEEDING_HIGH, sim.fired.last().phraseId)
}

private fun testSpeedingNotFired() = check("SPEEDING 비발화: 여유 구간 / 짧은 초과 / 제한속도 모름") {
    val sim = Sim()
    sim.rules.speedLimitKmh = 50.0
    sim.hold(10_000) { it.copy(speedKmh = 54.0) }          // 여유 5km/h 안
    assertEquals(0, sim.count(DangerType.SPEEDING), "여유 구간은 발화 안 함")
    sim.hold(2_000) { it.copy(speedKmh = 70.0) }           // 2초만 초과
    sim.hold(5_000) { it.copy(speedKmh = 48.0) }
    assertEquals(0, sim.count(DangerType.SPEEDING), "3초 미만 초과는 발화 안 함")

    val sim2 = Sim() // 제한속도 null
    sim2.hold(10_000) { it.copy(speedKmh = 120.0) }
    assertEquals(0, sim2.count(DangerType.SPEEDING), "제한속도를 모르면 규칙 비활성")
}

// ───────────────────────── HARD_BRAKE ─────────────────────────

private fun testHardBrake() = check("HARD_BRAKE: 브레이크+0.35g 이상 감속 시 1회, 등급, 쿨다운") {
    val sim = Sim()
    sim.hold(2_000) { it.copy(speedKmh = 50.0, brakePressed = false) }
    // 50→0 km/h 를 2초에: 13.9/2 = 6.9 m/s² → HIGH
    sim.ramp(2_000, 50.0, 0.0) { s, v -> s.copy(speedKmh = v, brakePressed = true) }
    assertEquals(1, sim.count(DangerType.HARD_BRAKE), "급제동 1회 발화")
    val ev = sim.fired.first { it.type == DangerType.HARD_BRAKE }
    assertEquals(Severity.HIGH, ev.severity)
    assertEquals(DangerPhrases.HARD_BRAKE, ev.phraseId)
    assertTrue(abs(ev.evidence["decelMps2"]!! - 6.94) < 0.3, "감속도 ≈6.9 m/s², 실제 ${ev.evidence["decelMps2"]}")
    assertEquals(1.0, ev.evidence["brakePressed"])

    // 쿨다운 안에 또 급제동 → 발화 없음
    sim.hold(3_000) { it.copy(speedKmh = 50.0, brakePressed = false) }
    sim.ramp(2_000, 50.0, 0.0) { s, v -> s.copy(speedKmh = v, brakePressed = true) }
    assertEquals(1, sim.count(DangerType.HARD_BRAKE), "쿨다운 안 재발화 없음")

    // 쿨다운 후 MEDIUM 급제동: 50→36 을 1초에 = 3.9 m/s²
    sim.hold(20_000) { it.copy(speedKmh = 50.0, brakePressed = false) }
    sim.ramp(1_000, 50.0, 36.0) { s, v -> s.copy(speedKmh = v, brakePressed = true) }
    assertEquals(2, sim.count(DangerType.HARD_BRAKE), "쿨다운 후 재발화")
    assertEquals(Severity.MEDIUM, sim.fired.last().severity)
    assertEquals(2, sim.rules.eventCount(DangerType.HARD_BRAKE), "리포트용 횟수")
}

private fun testHardBrakeNotFired() = check("HARD_BRAKE 비발화: 완만한 제동 / 브레이크 안 밟음 / 저속") {
    val sim = Sim()
    sim.hold(1_000) { it.copy(speedKmh = 50.0) }
    sim.ramp(4_000, 50.0, 30.0) { s, v -> s.copy(speedKmh = v, brakePressed = true) } // 1.4 m/s²
    assertEquals(0, sim.count(DangerType.HARD_BRAKE), "완만한 제동은 발화 안 함")

    sim.hold(1_000) { it.copy(speedKmh = 50.0, brakePressed = false) }
    sim.ramp(2_000, 50.0, 0.0) { s, v -> s.copy(speedKmh = v, brakePressed = false) } // 신호상 안 밟음
    assertEquals(0, sim.count(DangerType.HARD_BRAKE), "브레이크 신호가 '안 밟음'이면 발화 안 함")

    sim.hold(1_000) { it.copy(speedKmh = 8.0, brakePressed = false) }
    sim.ramp(300, 8.0, 0.0) { s, v -> s.copy(speedKmh = v, brakePressed = true) } // 저속 정지
    assertEquals(0, sim.count(DangerType.HARD_BRAKE), "10km/h 미만에서의 정지는 발화 안 함")
}

// ───────────────────────── ABRUPT_LANE_CHANGE ─────────────────────────

private fun testLaneChange() = check("ABRUPT_LANE_CHANGE: 60km/h 급조향, 깜빡이 유무에 따른 등급, 쿨다운") {
    val sim = Sim()
    sim.hold(2_000) { it.copy(speedKmh = 60.0, steeringAngleDeg = 0.0, turnSignal = TurnSignal.OFF) }
    // 핸들 0→40° 를 200ms 에 = 200 deg/s, 깜빡이 없음 → HIGH
    sim.ramp(200, 0.0, 40.0) { s, a -> s.copy(steeringAngleDeg = a) }
    assertEquals(1, sim.count(DangerType.ABRUPT_LANE_CHANGE))
    val ev = sim.fired.last()
    assertEquals(Severity.HIGH, ev.severity, "깜빡이 없으면 HIGH")
    assertEquals(DangerPhrases.LANE_CHANGE_NO_SIGNAL, ev.phraseId)
    assertEquals(0.0, ev.evidence["signaled"])
    assertTrue(ev.evidence["steerRateDegPerS"]!! >= 120.0)

    // 되돌리는 조향(40→0)도 빠르지만 쿨다운 안이라 재발화 없음
    sim.ramp(200, 40.0, 0.0) { s, a -> s.copy(steeringAngleDeg = a) }
    sim.hold(2_000) { it.copy(steeringAngleDeg = 0.0) }
    assertEquals(1, sim.count(DangerType.ABRUPT_LANE_CHANGE), "쿨다운 안 재발화 없음")

    // 쿨다운 후: 오른쪽 깜빡이를 1초 전에 켰다 끄고 급조향 → MEDIUM
    sim.hold(20_000) { it.copy(steeringAngleDeg = 0.0) }
    sim.hold(500) { it.copy(turnSignal = TurnSignal.RIGHT) }
    sim.hold(1_000) { it.copy(turnSignal = TurnSignal.OFF) }
    sim.ramp(200, 0.0, 40.0) { s, a -> s.copy(steeringAngleDeg = a) }
    assertEquals(2, sim.count(DangerType.ABRUPT_LANE_CHANGE))
    assertEquals(Severity.MEDIUM, sim.fired.last().severity, "3초 안에 깜빡이 있었으면 MEDIUM")
    assertEquals(DangerPhrases.LANE_CHANGE, sim.fired.last().phraseId)
    assertEquals(1.0, sim.fired.last().evidence["signaled"])
}

private fun testLaneChangeNotFired() = check("ABRUPT_LANE_CHANGE 비발화: 저속 / 느린 조향 / 작은 각") {
    val sim = Sim()
    sim.hold(1_000) { it.copy(speedKmh = 20.0, steeringAngleDeg = 0.0) }
    sim.ramp(200, 0.0, 90.0) { s, a -> s.copy(steeringAngleDeg = a) } // 주차장 급조향
    assertEquals(0, sim.count(DangerType.ABRUPT_LANE_CHANGE), "30km/h 미만은 판정 안 함")

    sim.ramp(2_000, 90.0, 0.0) { s, a -> s.copy(steeringAngleDeg = a) } // 핸들 천천히 되돌림
    sim.hold(1_000) { it.copy(speedKmh = 60.0, steeringAngleDeg = 0.0) }
    sim.ramp(2_000, 0.0, 40.0) { s, a -> s.copy(steeringAngleDeg = a) } // 20 deg/s 보통 차로변경
    assertEquals(0, sim.count(DangerType.ABRUPT_LANE_CHANGE), "보통 속도의 조향은 발화 안 함")

    sim.ramp(2_000, 40.0, 0.0) { s, a -> s.copy(steeringAngleDeg = a) } // 천천히 되돌림
    sim.hold(1_000) { it.copy(steeringAngleDeg = 0.0) }
    sim.step(100) { it.copy(steeringAngleDeg = 8.0) } // 8° 가 100ms 에 튐 = 80deg/s 지만 작은 각
    sim.step(100) { it.copy(steeringAngleDeg = 0.0) }
    assertEquals(0, sim.count(DangerType.ABRUPT_LANE_CHANGE), "작은 각 잡음은 발화 안 함")
}

// ───────────────────────── REVERSE_THROTTLE ─────────────────────────

private fun testReverseThrottle() = check("REVERSE_THROTTLE: R 기어 RPM +300 이상 시 1회, 쿨다운, 속도 무관") {
    val sim = Sim(idle = { 800.0 })
    sim.hold(1_000) { it.copy(gear = Gear.R, rpm = 820.0, speedKmh = 3.0) } // 크리핑
    assertEquals(0, sim.count(DangerType.REVERSE_THROTTLE), "크리핑(공회전)은 발화 안 함")
    sim.hold(500) { it.copy(rpm = 1_200.0, speedKmh = 6.0) }
    assertEquals(1, sim.count(DangerType.REVERSE_THROTTLE), "RPM +400 → 발화")
    val ev = sim.fired.last()
    assertEquals(Severity.MEDIUM, ev.severity)
    assertEquals(DangerPhrases.REVERSE_THROTTLE, ev.phraseId)
    assertEquals(400.0, ev.evidence["riseRpm"])
    assertEquals(800.0, ev.evidence["idleRpm"])

    sim.hold(3_000) { it.copy(rpm = 1_500.0) }
    assertEquals(1, sim.count(DangerType.REVERSE_THROTTLE), "계속 밟고 있어도 한 번만")
    sim.hold(1_000) { it.copy(rpm = 800.0) }
    sim.hold(1_000) { it.copy(rpm = 1_300.0) }
    assertEquals(1, sim.count(DangerType.REVERSE_THROTTLE), "쿨다운 안 재발화 없음")

    sim.hold(20_000) { it.copy(rpm = 800.0) }
    sim.hold(500) { it.copy(rpm = 1_500.0, speedKmh = 0.0) } // 속도 0 이어도 RPM 만으로 판정
    assertEquals(2, sim.count(DangerType.REVERSE_THROTTLE), "쿨다운 후 재발화")
    assertEquals(Severity.HIGH, sim.fired.last().severity, "+600 이상은 HIGH")
}

private fun testReverseThrottleNotFired() = check("REVERSE_THROTTLE 비발화: D 기어 / 학습된 높은 공회전 / RPM 모름") {
    val sim = Sim()
    sim.hold(2_000) { it.copy(gear = Gear.D, rpm = 2_000.0, speedKmh = 20.0) }
    assertEquals(0, sim.count(DangerType.REVERSE_THROTTLE), "D 기어는 무관")
    sim.hold(1_000) { it.copy(gear = Gear.R, rpm = null) }
    assertEquals(0, sim.count(DangerType.REVERSE_THROTTLE), "RPM 모르면 판정 안 함")

    val estimator = IdleRpmEstimator()
    val sim2 = Sim(idle = { estimator.idleRpm })
    sim2.src.addListener(estimator)
    // 냉간 시동: P, 정차, 1,050 RPM 공회전을 5초 학습
    sim2.hold(5_000) { it.copy(gear = Gear.P, speedKmh = 0.0, rpm = 1_050.0, ignitionOn = true) }
    assertTrue(estimator.isLearned, "공회전 학습됨")
    assertEquals(1_050.0, estimator.idleRpm)
    sim2.hold(1_000) { it.copy(gear = Gear.R, rpm = 1_250.0, speedKmh = 2.0) } // 학습값 대비 +200
    assertEquals(0, sim2.count(DangerType.REVERSE_THROTTLE), "학습된 공회전 기준으로는 +300 미만 → 발화 안 함")
    sim2.hold(500) { it.copy(rpm = 1_400.0) } // +350
    assertEquals(1, sim2.count(DangerType.REVERSE_THROTTLE), "학습값 기준 +300 이상 → 발화")
}

// ───────────────────────── IdleRpmEstimator ─────────────────────────

private fun testIdleRpmEstimator() = check("IdleRpmEstimator: 정차 P/N 구간 중앙값 학습, 주행·D 구간 무시, 워밍업 추종") {
    val est = IdleRpmEstimator()
    val src = FakeSignalSource()
    src.addListener(est)
    var t = 0L
    fun push(gear: Gear, speed: Double, rpm: Double?) { t += 100; src.update(t) { it.copy(gear = gear, speedKmh = speed, rpm = rpm, ignitionOn = true) } }

    assertEquals(800.0, est.idleRpm, "학습 전 기본값")
    // 시동 직후 2초(settle)는 버림: 크랭킹 2,000 RPM
    repeat(20) { push(Gear.P, 0.0, 2_000.0) }
    assertEquals(0, est.sampleCount, "settle 구간은 표본 안 받음")
    // 잡음 섞인 냉간 공회전 ~1,100
    val noisy = listOf(1_080.0, 1_120.0, 1_100.0, 1_090.0, 1_110.0, 1_400.0 /*튐*/, 1_100.0, 1_095.0, 1_105.0, 1_100.0, 1_100.0, 1_098.0)
    noisy.forEach { push(Gear.P, 0.0, it) }
    assertTrue(est.isLearned)
    assertEquals(1_100.0, est.idleRpm, "중앙값이라 튐(1,400)에 흔들리지 않음")
    // D 기어 주행 중 높은 RPM 은 무시
    repeat(50) { push(Gear.D, 40.0, 2_500.0) }
    assertEquals(1_100.0, est.idleRpm, "주행 구간은 표본 아님")
    // 신호 대기(N, 정차)에서 워밍업 후 750 → 윈도(60) 가 채워지며 값이 따라 내려감
    repeat(70) { push(Gear.N, 0.0, 750.0) }
    assertEquals(750.0, est.idleRpm, "워밍업 후 공회전 추종")
    // 시동 꺼짐 → 다시 settle
    push(Gear.P, 0.0, 0.0)
    repeat(10) { push(Gear.P, 0.0, 700.0) }
    assertEquals(750.0, est.idleRpm, "재시동 직후 settle 동안은 갱신 없음")
    est.reset()
    assertEquals(800.0, est.idleRpm)
}

fun main() {
    println("DangerRulesTest")
    testSpeeding()
    testSpeedingNotFired()
    testHardBrake()
    testHardBrakeNotFired()
    testLaneChange()
    testLaneChangeNotFired()
    testReverseThrottle()
    testReverseThrottleNotFired()
    testIdleRpmEstimator()
    println("DangerRulesTest: all passed")
}
