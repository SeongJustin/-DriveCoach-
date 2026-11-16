// PracticeEngine / Chapter2Items / SuddenAccelDrill 테스트, fun main 러너
package kr.drivecoach.core.practice

import kr.drivecoach.core.signal.FakeSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.Headlight
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.signal.Wiper
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

private val P0 = VehicleState(timestampMs = 0, gear = Gear.P, brakePressed = false, parkingBrakeOn = true, wiper = Wiper.OFF, headlight = Headlight.OFF)

private fun engineInP(items: List<PracticeItem> = Chapter2Items.all): Pair<PracticeEngine, FakeSignalSource> {
    val src = FakeSignalSource(P0)
    val engine = PracticeEngine(items)
    src.addListener(engine)
    engine.onState(src.current)
    return engine to src
}

/** 자동 통과: 와이퍼 항목을 EXPLAIN 중에 실제로 조작하면 설명 없이 즉시 PASSED. */
fun testAutoPassDuringExplain() {
    val (engine, src) = engineInP()
    val step = engine.start("2-6-wiper")
    assertEquals(Phase.EXPLAIN, step.phase)
    assertEquals("practice_2_6_wiper_explain", step.phrase?.id)
    src.update(100) { it.copy(wiper = Wiper.LOW) }
    assertEquals(Phase.PASSED, engine.phase)
    assertTrue(engine.lastStep.autoPassed)
    assertEquals("practice_2_6_wiper_pass", engine.lastStep.phrase?.id)
    assertEquals(Phase.PASSED, engine.results["2-6-wiper"])
}

/** 와이퍼를 OFF 로 돌리는 것은 통과가 아니다. 상향등은 HIGH 로만 통과. */
fun testAutoPredicatesNegative() {
    val (engine, src) = engineInP()
    engine.start("2-6-wiper")
    src.update(100) { it.copy(wiper = Wiper.OFF) }
    assertEquals(Phase.EXPLAIN, engine.phase)

    engine.start("2-5-headlight-high")
    src.update(200) { it.copy(headlight = Headlight.LOW) }
    assertEquals(Phase.EXPLAIN, engine.phase)
    src.update(300) { it.copy(headlight = Headlight.HIGH) }
    assertEquals(Phase.PASSED, engine.phase)
}

/** 수동 확인: 감지 불가 항목(사이드미러)은 센서로는 절대 통과하지 않고 CONFIRM 으로만 통과. */
fun testManualConfirm() {
    val (engine, src) = engineInP()
    engine.start("2-1-side-mirror")
    src.update(100) { it.copy(wiper = Wiper.HIGH, headlight = Headlight.HIGH, seatPositionChanged = true) }
    assertEquals(Phase.EXPLAIN, engine.phase)
    assertEquals(Phase.TRY, engine.onInput(UserInput.NEXT).phase)
    val ask = engine.onInput(UserInput.NEXT)
    assertEquals(Phase.CONFIRM, ask.phase)
    assertEquals("practice_2_1_side_mirror_confirm", ask.phrase?.id)
    val done = engine.onInput(UserInput.CONFIRM)
    assertEquals(Phase.PASSED, done.phase)
    assertFalse(done.autoPassed)
    assertEquals(Phase.PASSED, engine.results["2-1-side-mirror"])
}

/** TRY 중 바로 "확인"해도 수동 항목은 통과. 자동 전용 항목은 "아직 감지 안 됨" 안내. */
fun testConfirmInTry() {
    val (engine, _) = engineInP()
    engine.start("2-8-speedometer")
    engine.onInput(UserInput.NEXT)
    assertEquals(Phase.PASSED, engine.onInput(UserInput.CONFIRM).phase)

    engine.start("2-5-headlight-low") // 자동 전용 항목(비상등은 수동 확인 허용으로 바뀜)
    engine.onInput(UserInput.NEXT)
    val s = engine.onInput(UserInput.CONFIRM)
    assertEquals(Phase.TRY, s.phase)
    assertEquals(PracticeEngine.PHRASE_NOT_DETECTED, s.phrase)
}

/** 건너뛰기: 어느 단계에서든 SKIP → SKIPPED, NEXT 로 다음 항목 시작. skipAll 은 장 전체. */
fun testSkip() {
    val (engine, _) = engineInP()
    engine.start("2-1-seat-fore-aft")
    engine.onInput(UserInput.NEXT)
    val s = engine.onInput(UserInput.SKIP)
    assertEquals(Phase.SKIPPED, s.phase)
    assertEquals(PracticeEngine.PHRASE_SKIPPED, s.phrase)
    val next = engine.onInput(UserInput.NEXT)
    assertEquals("2-1-seat-backrest", next.itemId)
    assertEquals(Phase.EXPLAIN, next.phase)

    engine.skipAll("2-1")
    assertEquals(Phase.SKIPPED, engine.phase)
    assertTrue(Chapter2Items.chapter("2-1").all { engine.results[it.id] == Phase.SKIPPED })
    assertEquals(0 to Chapter2Items.chapter("2-1").size, engine.progressOf("2-1"))
    // 2-1 을 다 건너뛰었으니 다음은 2-2 첫 항목
    assertEquals("2-2-seatbelt", engine.onInput(UserInput.NEXT).itemId)
}

/** P 가 아니면 시작 거부(상태 미수신 포함). */
fun testRejectWhenNotInP() {
    val engine = PracticeEngine(Chapter2Items.all)
    val s0 = engine.start("2-6-wiper")
    assertEquals(PracticeEngine.PHRASE_NEED_P, s0.phrase)
    assertNull(engine.current)

    engine.onState(P0.copy(gear = Gear.D, timestampMs = 1))
    assertFalse(engine.canStart())
    val s1 = engine.start("2-6-wiper")
    assertEquals(PracticeEngine.PHRASE_NEED_P, s1.phrase)
    assertNull(engine.phase)

    engine.onState(P0.copy(timestampMs = 2))
    assertEquals(Phase.EXPLAIN, engine.start("2-6-wiper").phase)
}

/** 기어 순서 추적: P→R→N→D 를 모두 거쳐야 통과. 중간에 P 로 돌아가면 처음부터. */
fun testGearSequence() {
    val (engine, src) = engineInP()
    engine.start("2-4-gear-sequence")
    engine.onInput(UserInput.NEXT)
    src.update(100) { it.copy(brakePressed = true) }
    src.update(200) { it.copy(gear = Gear.R) }
    assertEquals(Phase.TRY, engine.phase)
    assertEquals("P→R 다음 N", engine.lastStep.progress)
    src.update(300) { it.copy(gear = Gear.N) }
    src.update(400) { it.copy(gear = Gear.P) }     // 순서 이탈 → 리셋
    assertEquals(Phase.TRY, engine.phase)
    src.update(500) { it.copy(gear = Gear.D) }     // P→D 는 인정 안 됨
    assertEquals(Phase.TRY, engine.phase)
    src.update(600) { it.copy(gear = Gear.P) }
    src.update(700) { it.copy(gear = Gear.R) }
    src.update(800) { it.copy(gear = Gear.N) }
    src.update(900) { it.copy(gear = Gear.D) }
    assertEquals(Phase.PASSED, engine.phase)
    assertTrue(engine.lastStep.autoPassed)
}

/** 주차브레이크 풀기/걸기 두 항목이 각각 올바른 방향 전이로만 통과. */
fun testParkingBrakeItems() {
    val (engine, src) = engineInP()
    engine.start("2-3-epb-release")
    src.update(100) { it.copy(parkingBrakeOn = true) }   // 변화 없음
    assertEquals(Phase.EXPLAIN, engine.phase)
    src.update(200) { it.copy(parkingBrakeOn = false) }
    assertEquals(Phase.PASSED, engine.phase)
    engine.start("2-3-epb-apply")
    src.update(300) { it.copy(parkingBrakeOn = true) }
    assertEquals(Phase.PASSED, engine.phase)
}

/** 옵션 필터: 안개등·뒷유리 와이퍼가 없는 차에는 그 항목이 없다. 2-9 는 옵션 있을 때만. */
fun testItemsForCapabilities() {
    val ids = Chapter2Items.itemsFor(Chapter2Items.AVANTE_CN8_MODERN).map { it.id }
    assertFalse("2-5-fog-light" in ids)
    assertFalse("2-6-rear-wiper" in ids)
    assertFalse("2-9-bcw" in ids)
    assertTrue("2-9-lka" in ids)
    assertTrue("2-3-epb-release" in ids)
    assertTrue("2-6-wiper" in ids)
    assertTrue(Chapter2Items.itemsFor(emptySet()).none { it.requiredCapability != null })
    // 감지 불가 항목은 반드시 Manual (규약)
    val undetectable = setOf("2-4-pedal-position", "2-1-side-mirror", "2-1-room-mirror")
    assertTrue(Chapter2Items.all.filter { it.id in undetectable }.all { !it.detectable && it.check is CheckKind.Manual })
    assertEquals(9, Chapter2Items.all.map { it.chapter }.distinct().size)
    assertEquals(Chapter2Items.all.size, Chapter2Items.all.map { it.id }.distinct().size)
}

/** 급발진 훈련: P 에서 시작 → 30초 뒤 ACTION → 브레이크 유지 + N 변속 → 퀴즈 3문항 → DONE, 재확인 간격 1/3/7일. */
fun testSuddenAccelDrill() {
    val events = mutableListOf<SuddenAccelDrill.Event>()
    val drill = SuddenAccelDrill { events += it }
    assertFalse(drill.start(P0.copy(gear = Gear.D), nowMs = 0))
    assertEquals(SuddenAccelDrill.Stage.IDLE, drill.stage)

    assertTrue(drill.start(P0, nowMs = 1_000))
    assertEquals(SuddenAccelDrill.Stage.EXPLAIN, drill.stage)
    drill.tick(20_000)
    assertEquals(SuddenAccelDrill.Stage.EXPLAIN, drill.stage)
    drill.tick(31_000)
    assertEquals(SuddenAccelDrill.Stage.ACTION, drill.stage)

    // 브레이크 없이 N → 통과 아님
    drill.onState(P0.copy(timestampMs = 32_000, gear = Gear.N, brakePressed = false))
    assertEquals(SuddenAccelDrill.Stage.ACTION, drill.stage)
    drill.onState(P0.copy(timestampMs = 33_000, gear = Gear.P, brakePressed = false))
    // 브레이크 밟고 유지한 채 N → 통과
    drill.onState(P0.copy(timestampMs = 34_000, gear = Gear.P, brakePressed = true))
    drill.onState(P0.copy(timestampMs = 35_000, gear = Gear.N, brakePressed = true))
    assertEquals(SuddenAccelDrill.Stage.QUIZ, drill.stage)
    assertEquals(35_000L, drill.passedAtMs)
    assertEquals(0, drill.currentQuestion()?.let { SuddenAccelDrill.QUIZ.indexOf(it) })

    assertTrue(drill.answer(0, 40_000))
    assertFalse(drill.answer(0, 41_000))   // q2 정답은 1
    assertTrue(drill.answer(2, 42_000))
    assertEquals(SuddenAccelDrill.Stage.DONE, drill.stage)
    assertEquals(2, drill.correctCount)
    val day = 24L * 3600 * 1000
    assertEquals(42_000 + day, drill.nextReviewDueMs)
    assertFalse(drill.isReviewDue(42_000 + day - 1))
    assertTrue(drill.isReviewDue(42_000 + day))

    val t1 = 42_000 + day
    assertEquals(t1 + 3 * day, drill.onReviewResult(true, t1))
    val t2 = t1 + 3 * day
    assertEquals(t2 + 7 * day, drill.onReviewResult(true, t2))
    val t3 = t2 + 7 * day
    assertEquals(t3 + 7 * day, drill.onReviewResult(true, t3))   // 7일 유지
    assertEquals(t3 + day, drill.onReviewResult(false, t3))      // 틀리면 1일로
    assertTrue(events.any { it.stage == SuddenAccelDrill.Stage.DONE })
}

/** 설명 중에 바로 동작해도 통과(자동 통과 규칙). PracticeEngine 의 2-4-sudden-accel 항목도 같은 판정. */
fun testSuddenAccelAutoPassDuringExplain() {
    val drill = SuddenAccelDrill()
    drill.start(P0.copy(brakePressed = true), nowMs = 0)
    drill.onState(P0.copy(timestampMs = 500, gear = Gear.N, brakePressed = true))
    assertEquals(SuddenAccelDrill.Stage.QUIZ, drill.stage)

    val (engine, src) = engineInP()
    engine.start("2-4-sudden-accel")
    src.update(100) { it.copy(brakePressed = true) }
    src.update(200) { it.copy(gear = Gear.N) }
    assertEquals(Phase.PASSED, engine.phase)
}

/** 이전(PREV): 목록의 바로 앞 항목을 처음부터. 첫 항목이면 현재 항목을 처음부터. 현재 항목 결과는 유지. */
fun testPrev() {
    val (engine, _) = engineInP()
    val items = Chapter2Items.all
    engine.start(items[2].id)
    engine.onInput(UserInput.NEXT) // TRY
    val step = engine.onInput(UserInput.PREV)
    assertEquals(items[1].id, step.itemId)
    assertEquals(Phase.EXPLAIN, step.phase)
    engine.onInput(UserInput.SKIP) // items[1] SKIPPED
    assertEquals(Phase.SKIPPED, engine.results[items[1].id])
    engine.onInput(UserInput.PREV) // items[0]
    assertEquals(items[0].id, engine.current?.id)
    engine.onInput(UserInput.PREV) // 첫 항목 → 그대로 처음부터
    assertEquals(items[0].id, engine.current?.id)
    assertEquals(Phase.EXPLAIN, engine.phase)
    assertEquals(Phase.SKIPPED, engine.results[items[1].id]) // 결과는 남는다
}

/** 이전 세션 결과를 넘기면 ✓ 가 이어지고 "다음 미완료" 가 그 뒤부터 시작된다. */
fun testInitialResults() {
    val items = Chapter2Items.all
    val src = FakeSignalSource(P0)
    val engine = PracticeEngine(items, initialResults = mapOf(items[0].id to Phase.PASSED, items[1].id to Phase.SKIPPED, "no-such" to Phase.PASSED))
    src.addListener(engine)
    engine.onState(src.current)
    assertEquals(Phase.PASSED, engine.results[items[0].id])
    assertNull(engine.results["no-such"])
    val step = engine.onInput(UserInput.NEXT)
    assertEquals(items[2].id, step.itemId)
}

fun main() {
    val tests = listOf(
        ::testAutoPassDuringExplain, ::testAutoPredicatesNegative, ::testManualConfirm, ::testConfirmInTry,
        ::testSkip, ::testRejectWhenNotInP, ::testGearSequence, ::testParkingBrakeItems,
        ::testItemsForCapabilities, ::testSuddenAccelDrill, ::testSuddenAccelAutoPassDuringExplain,
        ::testPrev, ::testInitialResults,
    )
    var failed = 0
    for (t in tests) {
        try { t(); println("  PASS ${t.name}") } catch (e: Throwable) { failed++; println("  FAIL ${t.name}: $e") }
    }
    if (failed > 0) { println("PracticeEngineTest: $failed failed"); kotlin.system.exitProcess(1) }
    println("PracticeEngineTest: all ${tests.size} passed")
}
