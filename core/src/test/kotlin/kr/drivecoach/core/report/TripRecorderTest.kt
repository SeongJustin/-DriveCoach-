// 담당: M (리포트 · 모의 시험 · 성장 기록) — fun main 러너
package kr.drivecoach.core.report

import kr.drivecoach.core.signal.FakeSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private const val HARD_BRAKE = 3.4 // safety DangerConfig.hardBrakeDecelMps2 와 같은 값(주입)
private val STOP_A = StopLine("A", 37.5000, 127.0000)

private fun config(limit: Double = 50.0) = TripRecorderConfig(
    hardBrakeDecelMps2 = HARD_BRAKE, speedLimitKmh = limit, stopLines = listOf(STOP_A),
)

/** 간단한 주행 시뮬레이터: 100ms 간격으로 상태를 밀어 넣는다. */
private class Sim(cfg: TripRecorderConfig) {
    val src = FakeSignalSource(VehicleState(timestampMs = 0, speedKmh = 0.0, steeringAngleDeg = 0.0, gear = Gear.D, parkingBrakeOn = false, lat = 37.49, lon = 127.0))
    val rec = TripRecorder(cfg).also { src.addListener(it); src.push(src.current) } // t=0 상태부터 기록
    var t = 0L
    fun step(ms: Long = 100, change: (VehicleState) -> VehicleState = { it }) { t += ms; src.update(t, change) }
    fun hold(ms: Long, change: (VehicleState) -> VehicleState = { it }) { var left = ms; while (left > 0) { step(100, change); left -= 100 } }
}

/** (11/15 오류 검수) 속도 사이에 다른 신호가 몇 ms 간격으로 끼어도 부드러운 감속을 급제동으로 세지 않는다. */
fun testInterleavedSignalsDoNotInflateDecel() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 40.0, brakePressed = true) }
    var v = 40.0
    repeat(30) {
        s.step(80) { st -> st.copy(steeringAngleDeg = (st.steeringAngleDeg ?: 0.0) + 1.0) }   // 다른 신호만 바뀜
        v -= 0.54                                                                              // 1.5 m/s² 감속
        s.step(20) { st -> st.copy(speedKmh = v) }
    }
    assertEquals(0, s.rec.finish().hardBrakeCount)
}

/** (11/15 오류 검수) 주행 중 제한속도가 정해지면(내비) 리포트 과속도 그 값으로 센다. */
fun testSpeedLimitChangeCountsOverspeed() {
    val s = Sim(TripRecorderConfig(hardBrakeDecelMps2 = HARD_BRAKE, speedLimitKmh = null))
    s.hold(2_000) { it.copy(speedKmh = 70.0) }
    s.rec.speedLimitKmh = 50.0
    s.hold(4_000) { it.copy(speedKmh = 70.0) }
    s.hold(1_000) { it.copy(speedKmh = 40.0) }
    assertEquals(1, s.rec.finish().overspeedCount)
}

fun testTurnWithSignalCounted() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 30.0) }
    s.step { it.copy(turnSignal = TurnSignal.RIGHT) }
    s.hold(2_000)                                         // 깜빡이 2초 유지
    s.step { it.copy(steeringAngleDeg = 90.0) }           // 우회전 조향
    s.hold(1_000)
    s.step { it.copy(steeringAngleDeg = 0.0, turnSignal = TurnSignal.OFF) }
    val m = s.rec.finish()
    assertEquals(1, m.turnCount)
    assertEquals(1, m.turnWithSignalCount)
    assertEquals(1.0, m.signalUsageRate)
    assertNotNull(m.avgSignalToMoveMs)
    assertTrue(m.avgSignalToMoveMs!! in 2_000L..2_300L, "lead=${m.avgSignalToMoveMs}")
    assertEquals(1, s.rec.events.count { it.type == TripEventType.TURN_SIGNAL_OK })
}

fun testTurnWithoutSignalMissed() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 30.0) }
    s.step { it.copy(steeringAngleDeg = -80.0) }          // 깜빡이 없이 좌회전
    s.hold(500)
    s.step { it.copy(steeringAngleDeg = 0.0) }
    val m = s.rec.finish()
    assertEquals(1, m.turnCount)
    assertEquals(0, m.turnWithSignalCount)
    val ev = s.rec.events.single { it.type == TripEventType.TURN_SIGNAL_MISSED }
    assertEquals("좌", ev.detail)
    assertNotNull(ev.lat)
}

fun testWrongDirectionSignalIsMissed() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 30.0) }
    s.step { it.copy(turnSignal = TurnSignal.LEFT) }
    s.hold(1_000)
    s.step { it.copy(steeringAngleDeg = 90.0) }           // 왼쪽 깜빡이인데 우회전
    val m = s.rec.finish()
    assertEquals(1, m.turnCount)
    assertEquals(0, m.turnWithSignalCount)
}

fun testSteeringWhileStoppedIsNotTurn() {
    val s = Sim(config())
    s.hold(500)
    s.step { it.copy(steeringAngleDeg = 200.0) }          // 정차 중 핸들 돌림
    s.hold(500)
    assertEquals(0, s.rec.finish().turnCount)
}

fun testHardBrakeCountedOnceWithCooldown() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 50.0) }
    // 50 → 30 km/h 를 1초 안에: 5.6 m/s² ≥ 3.4
    s.step(500) { it.copy(speedKmh = 40.0) }
    s.step(500) { it.copy(speedKmh = 30.0) }
    s.step(500) { it.copy(speedKmh = 20.0) }              // 쿨다운(2초) 안이므로 추가 카운트 없음
    s.hold(3_000) { it.copy(speedKmh = 20.0) }
    s.step(500) { it.copy(speedKmh = 10.0) }              // 두 번째 급제동
    val m = s.rec.finish()
    assertEquals(2, m.hardBrakeCount)
    val ev = s.rec.events.first { it.type == TripEventType.HARD_BRAKE }
    assertTrue(ev.value!! >= HARD_BRAKE)
}

fun testGentleBrakeNotCounted() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 50.0) }
    s.hold(5_000) { it.copy(speedKmh = (it.speedKmh ?: 0.0) - 1.0) } // 10 km/h per s ≈ 2.8 m/s²
    assertEquals(0, s.rec.finish().hardBrakeCount)
}

fun testOverspeedSegment() {
    val s = Sim(config(limit = 50.0))
    s.hold(1_000) { it.copy(speedKmh = 48.0) }
    s.hold(3_000) { it.copy(speedKmh = 60.0) }            // +10, 3초
    s.hold(1_000) { it.copy(speedKmh = 45.0) }
    s.hold(2_000) { it.copy(speedKmh = 65.0) }            // +15, 2초
    val m = s.rec.finish()                                // 열린 구간은 finish 가 닫는다
    assertEquals(2, m.overspeedCount)
    assertTrue(m.overspeedDurationMs in 4_800L..5_200L, "dur=${m.overspeedDurationMs}")
    assertEquals(15.0, m.maxOverspeedKmh)
    val evs = s.rec.events.filter { it.type == TripEventType.OVERSPEED }
    assertEquals(listOf(10.0, 15.0), evs.map { it.value })
    assertEquals(1_100L, evs[0].timestampMs)              // 구간 시작 시각
}

fun testStopLineComplianceAndViolation() {
    val s = Sim(config())
    // 정지선 A 반경(15m) 안으로 들어가 멈춤
    s.hold(500) { it.copy(speedKmh = 20.0) }
    s.step { it.copy(lat = STOP_A.lat + 0.00005, lon = STOP_A.lon, speedKmh = 10.0) } // 약 5.5m
    s.hold(1_000) { it.copy(speedKmh = 0.0) }
    s.step { it.copy(lat = STOP_A.lat + 0.001, speedKmh = 20.0) }                    // 반경 밖(약 110m)
    // 다시 들어와서 멈추지 않고 통과
    s.step { it.copy(lat = STOP_A.lat - 0.00005, speedKmh = 25.0) }
    s.hold(500)
    s.step { it.copy(lat = STOP_A.lat - 0.001) }
    val m = s.rec.finish()
    assertEquals(2, m.stopLineCount)
    assertEquals(1, m.stopLineCompliedCount)
    assertEquals(0.5, m.stopLineComplianceRate)
    val v = s.rec.events.single { it.type == TripEventType.STOP_LINE_VIOLATION }
    assertEquals("A", v.detail)
    assertEquals(25.0, v.value)
}

fun testDistanceDurationAverageAndCoachCount() {
    val s = Sim(config())
    s.hold(10_000) { it.copy(speedKmh = 36.0) }           // 36 km/h = 10 m/s × 10초 = 100m
    s.rec.onCoachSpoke("coach_turn_right_300m")
    s.rec.onCoachSpoke()
    val m = s.rec.finish()
    assertEquals(10_000L, m.durationMs)
    assertTrue(m.distanceKm in 0.095..0.101, "dist=${m.distanceKm}")
    assertTrue(m.avgSpeedKmh in 34.0..37.0, "avg=${m.avgSpeedKmh}")
    assertEquals(36.0, m.maxSpeedKmh)
    assertEquals(2, m.coachSpeakCount)
    assertEquals("coach_turn_right_300m", s.rec.events.first { it.type == TripEventType.COACH_SPEAK }.detail)
}

fun testOdometerPreferredForDistance() {
    val s = Sim(config())
    s.step { it.copy(odometerKm = 1000.0, speedKmh = 30.0) }
    s.hold(5_000)
    s.step { it.copy(odometerKm = 1000.5) }
    assertEquals(0.5, s.rec.finish().distanceKm)
}

fun testEndStateAndMovingSafetyEvents() {
    val s = Sim(config())
    s.hold(1_000) { it.copy(speedKmh = 20.0, parkingBrakeOn = true, seatBeltWarning = true) }
    s.hold(1_000) { it.copy(speedKmh = 0.0, parkingBrakeOn = false, seatBeltWarning = false, gear = Gear.D) }
    s.rec.finish()
    val types = s.rec.events.map { it.type }
    assertEquals(1, types.count { it == TripEventType.PARKING_BRAKE_MOVING })
    assertEquals(1, types.count { it == TripEventType.SEATBELT_WARNING_MOVING })
    assertTrue(TripEventType.END_GEAR_NOT_P in types)
    assertTrue(TripEventType.END_PARKING_BRAKE_OFF in types)
    // finish 를 두 번 불러도 이벤트가 늘지 않는다
    val n = s.rec.events.size
    s.rec.finish()
    assertEquals(n, s.rec.events.size)
}

fun testFinishWithoutStatesIsEmpty() {
    val rec = TripRecorder(config())
    val m = rec.finish()
    assertEquals(0L, m.durationMs)
    assertNull(m.signalUsageRate)
    assertNull(m.stopLineComplianceRate)
    assertTrue(rec.events.isEmpty())
}

// ───────────── ReportComposer ─────────────

private fun metrics(
    signal: Int = 10, turns: Int = 10, lead: Long? = 3_000, hardBrake: Int = 0,
    overspeedMs: Long = 0, stop: Int = 5, stopOk: Int = 5,
) = Metrics(
    distanceKm = 10.0, durationMs = 1_800_000, avgSpeedKmh = 20.0, maxSpeedKmh = 60.0,
    turnCount = turns, turnWithSignalCount = signal, avgSignalToMoveMs = lead,
    hardBrakeCount = hardBrake, overspeedDurationMs = overspeedMs, overspeedCount = if (overspeedMs > 0) 1 else 0,
    stopLineCount = stop, stopLineCompliedCount = stopOk,
)

fun testComposerSlots() {
    // 급제동 3회/10km → 0.4, 깜빡이 100%, 정지선 100%
    val r = ReportComposer.compose(metrics(hardBrake = 3), emptyList(), previous = null, courseId = "c1")
    assertEquals("report_item_signal_usage", r.bestOne)       // 동점(1.0)이면 선언 순서
    assertEquals("report_item_hard_brake", r.fixOne)
    assertNull(r.improvedOne)                                 // 이전 리포트 없음
    assertEquals("c1", r.courseId)
}

fun testComposerImprovedAgainstPrevious() {
    val prev = ReportComposer.compose(metrics(signal = 4, hardBrake = 3), emptyList())
    val now = ReportComposer.compose(metrics(signal = 9, hardBrake = 2), emptyList(), previous = prev)
    assertEquals("report_item_signal_usage", now.improvedOne)  // 0.4 → 0.9 가 가장 큰 개선
    assertEquals("report_item_hard_brake", now.fixOne)         // 0.6 < 0.7
}

fun testComposerNothingToFixAndNoData() {
    val perfect = ReportComposer.compose(metrics(), emptyList())
    assertNull(perfect.fixOne)
    assertNotNull(perfect.bestOne)
    val empty = ReportComposer.compose(Metrics(), emptyList())
    assertNull(empty.bestOne); assertNull(empty.fixOne); assertNull(empty.improvedOne)
}

fun testMetricScores() {
    assertEquals(0.5, MetricKey.SIGNAL_LEAD.score(metrics(lead = 1_500)))
    assertNull(MetricKey.SIGNAL_LEAD.score(metrics(lead = null)))
    assertNull(MetricKey.SIGNAL_USAGE.score(metrics(turns = 0, signal = 0)))
    assertEquals(0.0, MetricKey.HARD_BRAKE.score(metrics(hardBrake = 7)))
    assertEquals(0.5, MetricKey.OVERSPEED.score(metrics(overspeedMs = 90_000)))   // 5% / 10%
}

// ───────────── MockExamScorer ─────────────

private fun ev(type: TripEventType, value: Double? = null) = TripEvent(type, 0, null, null, "", value)

fun testDeductionTableShape() {
    assertEquals(46, DeductionTable.ITEMS.size)
    assertEquals((1..46).toList(), DeductionTable.ITEMS.map { it.no })
    assertEquals(7, DeductionTable.byNo(30).points)
    assertEquals(10, DeductionTable.byNo(43).points)
    assertEquals(7, DeductionTable.byNo(21).points)
    assertEquals(39, DeductionTable.NOT_DETECTABLE.size)
    assertTrue(DeductionTable.NOT_DETECTABLE.all { !it.detectable })
}

fun testScorerPassAndFail() {
    val scorer = MockExamScorer(avgSpeedKmh = 30.0)
    val pass = scorer.score(listOf(ev(TripEventType.TURN_SIGNAL_MISSED), ev(TripEventType.HARD_BRAKE), ev(TripEventType.STOP_LINE_OK)))
    assertEquals(14, pass.totalDeduction)
    assertEquals(86, pass.score)
    assertTrue(pass.passed)
    assertEquals(39, pass.notDetectable.size)

    val fail = scorer.score(List(3) { ev(TripEventType.STOP_LINE_VIOLATION) } + ev(TripEventType.TURN_SIGNAL_MISSED))
    assertEquals(37, fail.totalDeduction)
    assertEquals(63, fail.score)
    assertFalse(fail.passed)
    assertEquals(30, fail.byItem()[DeductionTable.byNo(43)])
}

fun testScorerSignalLeadDistance() {
    val scorer = MockExamScorer(avgSpeedKmh = 36.0) // 10 m/s
    val short = scorer.score(listOf(ev(TripEventType.TURN_SIGNAL_OK, 2_000.0)))  // 20m < 30m
    assertEquals(31, short.deductions.single().item.no)
    val ok = scorer.score(listOf(ev(TripEventType.TURN_SIGNAL_OK, 3_500.0)))     // 35m
    assertTrue(ok.deductions.isEmpty())
}

fun testScorerDisqualification() {
    val scorer = MockExamScorer()
    val minor = scorer.score(listOf(ev(TripEventType.OVERSPEED, 8.0)))
    assertFalse(minor.disqualified); assertEquals(100, minor.score)          // 10km/h 이내: 감점 항목 없음
    val dq = scorer.score(listOf(ev(TripEventType.OVERSPEED, 12.0)))
    assertTrue(dq.disqualified); assertFalse(dq.passed)
    val belt = scorer.score(listOf(ev(TripEventType.SEATBELT_WARNING_MOVING)))
    assertTrue(belt.disqualified)
    val end = scorer.score(listOf(ev(TripEventType.END_GEAR_NOT_P), ev(TripEventType.END_PARKING_BRAKE_OFF), ev(TripEventType.PARKING_BRAKE_MOVING)))
    assertEquals(20, end.totalDeduction)
}

// ───────────── Progress ─────────────

fun testProgressStagesAndCourses() {
    val p = Progress()
    p.completeStage("2-1"); p.completeStage("4-7")
    assertTrue(p.isStageCompleted("2-1")); assertFalse(p.isStageCompleted("5-1"))
    p.addReport(ReportComposer.compose(metrics(), emptyList(), courseId = "town"))
    p.addReport(ReportComposer.compose(metrics(), emptyList(), courseId = "exam"))
    p.addReport(ReportComposer.compose(metrics(), emptyList(), courseId = "town"))
    assertEquals(2, p.reportsForCourse("town").size)
    assertEquals(3, p.tripCount)
    val restored = Progress(p.state())
    assertEquals(setOf("2-1", "4-7"), restored.completedStages)
    assertEquals(3, restored.tripCount)
}

fun testProgressWeakestOverRecentFive() {
    val p = Progress()
    assertNull(p.weakestMetric())
    // 오래된 3회: 급제동 최악
    repeat(3) { p.addReport(ReportComposer.compose(metrics(hardBrake = 10), emptyList())) }
    // 최근 5회: 정지선 0/5 가 최악, 급제동은 깨끗
    repeat(5) { p.addReport(ReportComposer.compose(metrics(stopOk = 0), emptyList())) }
    val weak = p.weakestMetric()!!
    assertEquals(MetricKey.STOP_LINE, weak.key)
    assertEquals(0.0, weak.avgScore)
    assertEquals("5-3", weak.practiceStage)
}

fun testProgressGraduation() {
    val p = Progress()
    repeat(9) { p.addReport(ReportComposer.compose(metrics(), emptyList())) }
    assertFalse(p.graduated); assertEquals(1, p.tripsToGraduate)
    p.addReport(ReportComposer.compose(metrics(), emptyList(), previous = p.lastReport()))
    assertTrue(p.graduated); assertEquals(0, p.tripsToGraduate)
}

fun main() {
    val tests = listOf(
        ::testInterleavedSignalsDoNotInflateDecel, ::testSpeedLimitChangeCountsOverspeed,
        ::testTurnWithSignalCounted, ::testTurnWithoutSignalMissed, ::testWrongDirectionSignalIsMissed,
        ::testSteeringWhileStoppedIsNotTurn, ::testHardBrakeCountedOnceWithCooldown, ::testGentleBrakeNotCounted,
        ::testOverspeedSegment, ::testStopLineComplianceAndViolation, ::testDistanceDurationAverageAndCoachCount,
        ::testOdometerPreferredForDistance, ::testEndStateAndMovingSafetyEvents, ::testFinishWithoutStatesIsEmpty,
        ::testComposerSlots, ::testComposerImprovedAgainstPrevious, ::testComposerNothingToFixAndNoData, ::testMetricScores,
        ::testDeductionTableShape, ::testScorerPassAndFail, ::testScorerSignalLeadDistance, ::testScorerDisqualification,
        ::testProgressStagesAndCourses, ::testProgressWeakestOverRecentFive, ::testProgressGraduation,
    )
    var failed = 0
    for (t in tests) {
        try { t(); println("  ok   ${t.name}") } catch (e: Throwable) { failed++; println("  FAIL ${t.name}: $e") }
    }
    println("report: ${tests.size - failed}/${tests.size} passed")
    if (failed > 0) { System.exit(1) }
}
