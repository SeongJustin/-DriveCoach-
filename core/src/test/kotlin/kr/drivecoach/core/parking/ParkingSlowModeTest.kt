// 직각 주차 슬로우 모드 테스트
package kr.drivecoach.core.parking

import kr.drivecoach.core.signal.FakeSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** 출력을 모아 두는 리스너. */
private class Recorder : SlowModeOutputListener {
    val outputs = mutableListOf<SlowModeOutput>()
    override fun onOutput(output: SlowModeOutput) { outputs += output }
    val says get() = outputs.filterIsInstance<SlowModeOutput.Say>().map { it.phrase }
    val warns get() = outputs.filterIsInstance<SlowModeOutput.Warn>().map { it.phrase }
    fun lastSay(): PhraseId? = outputs.filterIsInstance<SlowModeOutput.Say>().lastOrNull()?.phrase
}

/** 정상 완주 시나리오를 돌려 (모드, 기록기, 신호원) 을 돌려준다. */
private data class Rig(val mode: ParkingSlowMode, val rec: Recorder, val src: FakeSignalSource)

private fun rig(idleRpm: Double = 800.0): Rig {
    val rec = Recorder()
    val mode = ParkingSlowMode(rec, idleRpm = { idleRpm })
    val src = FakeSignalSource(VehicleState(timestampMs = 0, speedKmh = 0.0, rpm = idleRpm, steeringAngleDeg = 0.0, gear = Gear.D))
    src.addListener(mode)
    return Rig(mode, rec, src)
}

private fun Rig.stopped(t: Long) = src.update(t) { it.copy(speedKmh = 0.0) }
private fun Rig.creep(t: Long, kmh: Double = 3.0, rpm: Double = 800.0) = src.update(t) { it.copy(speedKmh = kmh, rpm = rpm) }

/** D(0) → 접근 정지(1) → 핸들 왼쪽 끝(2) → 전진 45° 정지(3) → R(4) → 핸들 오른쪽 끝(5) 까지 진행. */
private fun Rig.runUntilReverse(): Long {
    mode.start(1_000)
    // 0: D 넣기. 시작 시점엔 아직 신호가 없어 대기 → 첫 신호(기어 D)로 자동 통과
    assertEquals(PhraseId("parking_step_gear_d"), rec.lastSay())
    assertEquals(0, mode.currentStep!!.index)
    src.update(1_500) { it }
    assertEquals(1, mode.currentStep!!.index)
    assertEquals(PhraseId("parking_step_approach"), rec.lastSay())
    // 1: 움직이는 중 확인 → 먼저 멈추라고만 하고 넘어가지 않는다
    creep(2_000)
    mode.confirm(2_500)
    assertEquals(ParkingProcedure.STOP_FIRST_PHRASE, rec.lastSay())
    assertEquals(1, mode.currentStep!!.index)
    stopped(3_000)
    mode.confirm(3_500)
    assertEquals(2, mode.currentStep!!.index)
    assertEquals(PhraseId("parking_step_steer_left_full"), rec.lastSay())
    // 2: 조향각 -420 → 센서 자동 통과
    src.update(5_000) { it.copy(steeringAngleDeg = -420.0) }
    assertEquals(3, mode.currentStep!!.index)
    // 3: 전진 후 정지·확인
    creep(6_000); stopped(8_000)
    mode.confirm(8_500)
    assertEquals(4, mode.currentStep!!.index)
    // 4: R 기어 → 자동 통과
    src.update(9_000) { it.copy(gear = Gear.R) }
    assertEquals(5, mode.currentStep!!.index)
    // 5: 핸들 오른쪽 끝 → 자동 통과
    src.update(10_000) { it.copy(steeringAngleDeg = 450.0) }
    assertEquals(6, mode.currentStep!!.index)
    assertEquals(PhraseId("parking_step_reverse_in"), rec.lastSay())
    return 10_000
}

private fun testHappyPath() {
    val r = rig()
    r.runUntilReverse()
    // 6: 천천히 후진(크리핑, RPM 공회전) → 경고 없음 → 정지·확인
    r.creep(11_000, kmh = 2.0); r.creep(12_000, kmh = 2.0); r.stopped(14_000)
    assertTrue(r.rec.warns.isEmpty(), "크리핑 후진에는 경고가 없어야 한다")
    r.mode.confirm(14_500)
    assertEquals(7, r.mode.currentStep!!.index)
    // 7: 핸들 풀기. 조향각이 가운데(±20°)로 오면 센서로 통과. '확인' 으로는 못 넘긴다.
    r.mode.confirm(14_800)
    assertEquals(7, r.mode.currentStep!!.index, "센서 단계는 확인으로 넘어가지 않는다")
    assertEquals(ParkingProcedure.NOT_DETECTED_PHRASE, r.rec.lastSay())
    r.src.update(15_000) { it.copy(steeringAngleDeg = 0.0) }
    assertEquals(8, r.mode.currentStep!!.index)
    // 8: 직진 후진 → 정지·확인
    r.creep(16_000, kmh = 2.0); r.stopped(18_000)
    r.mode.confirm(18_500)
    assertEquals(9, r.mode.currentStep!!.index)
    // 9: P → 10: 주차브레이크. 확인으로는 못 넘기고 신호로 통과
    r.src.update(19_000) { it.copy(gear = Gear.P, speedKmh = 0.0) }
    assertEquals(10, r.mode.currentStep!!.index)
    assertEquals(PhraseId("parking_finish_epb"), r.rec.lastSay())
    r.mode.confirm(19_200)
    assertEquals(10, r.mode.currentStep!!.index)
    r.src.update(19_500) { it.copy(parkingBrakeOn = true) }
    assertEquals(SlowModePhase.SELF_EVAL, r.mode.phase)
    assertNull(r.mode.currentStep)
    assertTrue(r.rec.outputs.any { it is SlowModeOutput.Completed })
    assertEquals(ParkingProcedure.SELF_EVAL_PROMPT, r.rec.lastSay())
    // 기록: 11단계 모두, 시도 1, 시간은 음수가 아님
    val recs = r.mode.records
    assertEquals(11, recs.size)
    assertEquals((0..10).toList(), recs.map { it.stepIndex })
    assertTrue(recs.all { it.attempt == 1 && it.tangle == null && it.durationMs >= 0 })
    assertEquals(PassedBy.SENSOR, recs[0].passedBy)
    assertEquals(PassedBy.CONFIRM, recs[1].passedBy)
    assertEquals(PassedBy.SENSOR, recs[2].passedBy)
    assertEquals(2_000L, recs[1].durationMs) // 1_500 → 3_500
    // 자기평가: 각도 + 간격 → 각도 힌트 우선
    assertEquals(SelfEvalHints.ANGLE, r.mode.selfEvaluate(setOf(SelfEvalItem.ANGLE, SelfEvalItem.CURB_GAP)))
    assertEquals(SlowModePhase.DONE, r.mode.phase)
    assertEquals(SelfEvalHints.ALL_GOOD, SelfEvalHints.hintFor(emptySet()))
    assertEquals(SelfEvalHints.FRONT_BACK, SelfEvalHints.hintFor(setOf(SelfEvalItem.FRONT_BACK)))
    // 완주 전 자기평가는 null
    assertNull(rig().mode.selfEvaluate(emptySet()))
    println("  정상 완주 OK")
}

private fun testSkip() {
    val r = rig()
    r.runUntilReverse()
    // 건너뛰기: 센서·확인 어떤 단계든 넘긴다(센서가 없는 차용)
    r.mode.skip(11_000)
    assertEquals(7, r.mode.currentStep!!.index)
    r.mode.skip(11_500)
    assertEquals(8, r.mode.currentStep!!.index)
    println("  건너뛰기 OK")
}

private fun testRestart() {
    val r = rig()
    r.runUntilReverse()
    r.mode.restart(12_000)
    assertEquals(SlowModePhase.STEP, r.mode.phase)
    assertEquals(0, r.mode.currentStep!!.index)
    assertEquals(2, r.mode.attempt)
    // 안내 순서: "처음부터" → 1단계(D 넣기) 문장. 지금 기어가 R 이라 바로 통과하지 않는다
    val says = r.rec.says
    assertEquals(ParkingProcedure.RESTART_PHRASE, says[says.size - 2])
    assertEquals(PhraseId("parking_step_gear_d"), says.last())
    // 중단된 6단계(후진)도 RESTART 로 기록됨
    val last = r.mode.records.last()
    assertEquals(6, last.stepIndex); assertEquals(PassedBy.RESTART, last.passedBy); assertEquals(1, last.attempt)
    println("  처음부터 OK")
}

private fun testRecoveryBranch() {
    val r = rig()
    r.runUntilReverse()
    r.creep(11_000, 2.0); r.stopped(12_000)
    // 꼬였어요 → 선택지 3개
    r.mode.help(12_500)
    assertEquals(SlowModePhase.RECOVERY_CHOICE, r.mode.phase)
    val ask = r.rec.outputs.last() as SlowModeOutput.AskRecoveryChoice
    assertEquals(3, ask.options.size)
    assertEquals(Tangle.values().toList(), ask.options.map { it.first })
    // 선택 전에는 확인이 아무것도 하지 않는다
    r.mode.confirm(12_600)
    assertEquals(SlowModePhase.RECOVERY_CHOICE, r.mode.phase)
    // 너무 붙음 선택 → 핸들 왼쪽 끝(0) → D(1) → 앞으로 빼서 정지·확인(2) → 공식 4단계(R)로 복귀
    r.mode.chooseRecovery(Tangle.TOO_CLOSE, 13_000)
    assertEquals(SlowModePhase.RECOVERY_STEP, r.mode.phase)
    assertEquals(Tangle.TOO_CLOSE, r.mode.currentRecovery!!.tangle)
    assertEquals(PhraseId("parking_step_steer_left_full"), r.rec.lastSay())
    r.src.update(14_000) { it.copy(steeringAngleDeg = -460.0) }
    assertEquals(1, r.mode.currentStep!!.index)
    r.src.update(15_000) { it.copy(gear = Gear.D) }
    assertEquals(2, r.mode.currentStep!!.index)
    r.creep(16_000, 3.0); r.stopped(18_000)
    r.mode.confirm(18_500)
    assertEquals(SlowModePhase.STEP, r.mode.phase)
    assertNull(r.mode.currentRecovery)
    assertEquals(4, r.mode.currentStep!!.index)
    assertEquals(PhraseId("parking_step_gear_r"), r.rec.lastSay())
    // 기록에 복구 단계가 tangle 과 함께 남는다
    val recRecs = r.mode.records.filter { it.tangle == Tangle.TOO_CLOSE }
    assertEquals(listOf(0, 1, 2), recRecs.map { it.stepIndex })
    assertEquals(PassedBy.HELP, r.mode.records.first { it.stepIndex == 6 && it.tangle == null }.passedBy)
    // 각도 틀어짐은 직진 후진(8)으로 복귀
    assertEquals(8, ParkingProcedure.RECOVERIES.getValue(Tangle.ANGLED).resumeAtStepIndex)
    println("  탈출 분기 OK")
}

private fun testReverseAccelWarningOnce() {
    val r = rig(idleRpm = 750.0)
    r.runUntilReverse()
    // R 에서 RPM 상승(액셀) 경고는 DangerRules 가 담당한다 → 슬로우 모드는 속도 초과만 1회 경고
    r.creep(11_000, kmh = 2.0, rpm = 1_300.0)
    r.creep(11_500, kmh = 2.5, rpm = 1_400.0)
    assertEquals(emptyList<PhraseId>(), r.rec.warns)
    assertEquals(6, r.mode.currentStep!!.index)
    // 속도 초과(8 km/h)는 1회, 반복돼도 추가 경고 없음
    r.creep(13_000, kmh = 8.0, rpm = 800.0)
    r.creep(13_500, kmh = 9.0, rpm = 800.0)
    assertEquals(listOf(ParkingProcedure.REVERSE_SPEED_WARNING), r.rec.warns)
    // D 에서의 속도는 후진 경고 대상이 아니다
    r.src.update(14_000) { it.copy(gear = Gear.D, rpm = 2_000.0, speedKmh = 10.0) }
    assertEquals(1, r.rec.warns.size)
    // 처음부터 → 경고 카운트 초기화 → R 에서 다시 1회
    r.mode.restart(15_000)
    r.src.update(16_000) { it.copy(gear = Gear.R, rpm = 800.0, speedKmh = 9.0) }
    assertEquals(2, r.rec.warns.size)
    println("  후진 중 속도 초과 경고 1회 OK")
}

private fun testProcedureData() {
    val steps = ParkingProcedure.STEPS
    assertEquals((0 until steps.size).toList(), steps.map { it.index })
    assertTrue(steps.all { it.illustration.endsWith(".svg") })
    assertNotNull(steps.first { it.expected == StepExpectation.GearIs(Gear.R) })
    assertEquals(StepExpectation.GearIs(Gear.D), steps.first().expected)
    assertEquals(StepExpectation.GearIs(Gear.P), steps[steps.size - 2].expected)
    assertEquals(StepExpectation.ParkingBrakeOn, steps.last().expected)
    assertEquals(PhraseId("parking_step_reverse_in"), steps[ParkingProcedure.REVERSE_IN_STEP_INDEX].instructionPhrase)
    ParkingProcedure.RECOVERIES.values.forEach { rp ->
        assertTrue(rp.resumeAtStepIndex in steps.indices, "복귀 단계 범위")
        assertEquals((0 until rp.steps.size).toList(), rp.steps.map { it.index })
    }
    println("  단계 데이터 OK")
}

/** 탈출법 고르기에서 "하던 단계로": 공식 단계·탈출 단계 모두 고르기 전 자리로 돌아간다. */
private fun testCancelHelp() {
    val r = rig()
    r.runUntilReverse()
    val before = r.mode.currentStep!!.index
    r.mode.help(12_000)
    assertEquals(SlowModePhase.RECOVERY_CHOICE, r.mode.phase)
    r.mode.cancelHelp(12_500)
    assertEquals(SlowModePhase.STEP, r.mode.phase)
    assertEquals(before, r.mode.currentStep!!.index)
    assertNull(r.mode.currentRecovery)
    // 탈출 2단계 중에 다시 고르기 → 하던 단계로 = 같은 탈출의 2단계
    r.mode.help(13_000); r.mode.chooseRecovery(Tangle.TOO_CLOSE, 13_100)
    r.src.update(14_000) { it.copy(steeringAngleDeg = -460.0) }
    assertEquals(1, r.mode.currentStep!!.index)
    r.mode.help(14_500); r.mode.cancelHelp(14_600)
    assertEquals(SlowModePhase.RECOVERY_STEP, r.mode.phase)
    assertEquals(Tangle.TOO_CLOSE, r.mode.currentRecovery!!.tangle)
    assertEquals(1, r.mode.currentStep!!.index)
    // 고르기 화면이 아니면 아무것도 하지 않는다
    r.mode.cancelHelp(15_000)
    assertEquals(SlowModePhase.RECOVERY_STEP, r.mode.phase)
    println("  하던 단계로 OK")
}

fun main() {
    println("ParkingSlowModeTest")
    testCancelHelp()
    testProcedureData()
    testHappyPath()
    testRestart()
    testSkip()
    testRecoveryBranch()
    testReverseAccelWarningOnce()
    println("ParkingSlowModeTest: ALL OK")
}
