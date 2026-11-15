// 담당: I (11/15 평행 주차 공식 테스트)
package kr.drivecoach.core.parking

import kr.drivecoach.core.signal.FakeSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId
import kr.drivecoach.core.tts.Phrases
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

private class ParRecorder : SlowModeOutputListener {
    val outputs = mutableListOf<SlowModeOutput>()
    override fun onOutput(output: SlowModeOutput) { outputs += output }
    fun lastSay(): PhraseId? = outputs.filterIsInstance<SlowModeOutput.Say>().lastOrNull()?.phrase
}

/** 공식 데이터: 번호·그림 키·멘트가 모두 있고, 탈출 복귀 자리가 범위 안. */
private fun testData() {
    val f = ParallelParkingProcedure.FORMULA
    assertEquals(ParkingKind.PARALLEL, f.kind)
    val steps = f.steps
    assertEquals((0 until steps.size).toList(), steps.map { it.index })
    assertTrue(steps.all { Regex("^par\\d\\d_").containsMatchIn(it.illustration) }, "그림 키는 parNN_")
    assertEquals(StepExpectation.GearIs(Gear.D), steps.first().expected)
    assertEquals(StepExpectation.GearIs(Gear.P), steps[steps.size - 2].expected)
    assertEquals(StepExpectation.ParkingBrakeOn, steps.last().expected)
    // 순서: R → 오른쪽 끝 → (45°) → 가운데 → 왼쪽 끝 → (나란히)
    val order = steps.map { it.expected }
    val iR = order.indexOf(StepExpectation.GearIs(Gear.R))
    val iRight = order.indexOf(StepExpectation.SteeringAtLeast(ParkingProcedure.FULL_LOCK_DEG, SteeringDirection.RIGHT))
    val iLeft = order.indexOf(StepExpectation.SteeringAtLeast(ParkingProcedure.FULL_LOCK_DEG, SteeringDirection.LEFT))
    assertTrue(iR in 0 until iRight && iRight < iLeft, "R → 오른쪽 끝 → 왼쪽 끝 순서")
    assertEquals(PhraseId("parking_par_step_reverse_in"), steps[ParallelParkingProcedure.REVERSE_IN_STEP_INDEX].instructionPhrase)
    val ids = mutableListOf(f.recoveryChoicePrompt, f.restartPhrase, f.donePhrase, f.selfEvalPrompt, f.frontBackHint, Phrases.parkingParIntro.id)
    steps.forEach { ids += it.instructionPhrase; it.referencePointPhrase?.let { r -> ids += r } }
    f.recoveries.values.forEach { rp ->
        assertTrue(rp.resumeAtStepIndex in steps.indices, "복귀 단계 범위")
        assertEquals((0 until rp.steps.size).toList(), rp.steps.map { it.index })
        ids += rp.titlePhrase
        rp.steps.forEach { ids += it.instructionPhrase; it.referencePointPhrase?.let { r -> ids += r } }
    }
    ids.forEach { assertNotNull(Phrases.find(it), "멘트 없음: ${it.id}") }
    assertEquals(Tangle.values().toSet(), f.recoveries.keys)
    println("  평행 주차 데이터 OK (${steps.size}단계, 멘트 ${ids.toSet().size}개)")
}

/** 처음부터 끝까지: 센서 단계는 신호로, 확인 단계는 정지 후 확인으로 넘어가 자기평가까지. */
private fun testHappyPath() {
    val rec = ParRecorder()
    val mode = ParkingSlowMode(rec, formula = ParallelParkingProcedure.FORMULA)
    val src = FakeSignalSource(VehicleState(timestampMs = 0, speedKmh = 0.0, rpm = 800.0, steeringAngleDeg = 0.0, gear = Gear.P, parkingBrakeOn = false))
    src.addListener(mode)
    mode.start(1_000)
    var t = 1_000L
    var guard = 0
    while (mode.phase == SlowModePhase.STEP && guard++ < 50) {
        val step = mode.currentStep!!
        t += 3_000
        when (val e = step.expected) {
            is StepExpectation.GearIs -> src.update(t) { it.copy(gear = e.gear) }
            is StepExpectation.SteeringAtLeast -> src.update(t) { it.copy(steeringAngleDeg = if (e.direction == SteeringDirection.RIGHT) 470.0 else -470.0) }
            is StepExpectation.SteeringCentered -> src.update(t) { it.copy(steeringAngleDeg = 3.0) }
            StepExpectation.ParkingBrakeOn -> src.update(t) { it.copy(parkingBrakeOn = true) }
            else -> { src.update(t) { it.copy(speedKmh = 0.0) }; mode.confirm(t) }
        }
    }
    assertEquals(SlowModePhase.SELF_EVAL, mode.phase)
    assertEquals(ParallelParkingProcedure.FORMULA.selfEvalPrompt, rec.lastSay())
    assertEquals(PhraseId("parking_par_hint_front_back"), mode.selfEvaluate(setOf(SelfEvalItem.FRONT_BACK)))
    println("  평행 주차 완주 OK")
}

/** 탈출 '연석에 붙음' → 3단계 → 공식 '핸들 가운데'로 복귀. */
private fun testRecovery() {
    val rec = ParRecorder()
    val mode = ParkingSlowMode(rec, formula = ParallelParkingProcedure.FORMULA)
    mode.start(0)
    mode.help(10)
    val ask = rec.outputs.filterIsInstance<SlowModeOutput.AskRecoveryChoice>().last()
    assertEquals(PhraseId("parking_par_recover_choose"), ask.prompt)
    mode.chooseRecovery(Tangle.TOO_CLOSE, 20)
    repeat(3) { mode.skip(30L + it) }
    assertEquals(SlowModePhase.STEP, mode.phase)
    assertEquals(10, mode.currentStep!!.index)
    println("  평행 주차 탈출 복귀 OK")
}

fun main() {
    println("ParallelParkingTest")
    testData()
    testHappyPath()
    testRecovery()
    println("ParallelParkingTest: ALL OK")
}
