// 담당: L (경고등 데이터 + 퀴즈 테스트, fun main 러너)
package kr.drivecoach.core.warning

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId
import kr.drivecoach.core.tts.Phrases
import kotlin.random.Random
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private var passed = 0
private fun test(name: String, body: () -> Unit) {
    try { body(); passed++; println("  ok  $name") }
    catch (t: Throwable) { println("  FAIL $name: $t"); throw t }
}

/** 데이터 무결성: ID 유일, 3색 모두 존재, 멘트 길이, 필수 경고등 포함. */
private fun testLampData() {
    test("ID 가 유일하다") {
        assertEquals(WarningLamps.all.size, WarningLamps.all.map { it.id }.toSet().size)
    }
    test("세 심각도가 모두 있고 각각 3개 이상") {
        Severity.values().forEach { assertTrue(WarningLamps.bySeverity(it).size >= 3, "$it") }
    }
    test("action 은 음성용 20자 내외(최대 20자)") {
        WarningLamps.all.forEach { assertTrue(it.action.length <= 20, "${it.id}: '${it.action}' ${it.action.length}자") }
    }
    test("nameKo, description 비어 있지 않음") {
        WarningLamps.all.forEach { assertTrue(it.nameKo.isNotBlank() && it.description.isNotBlank(), it.id) }
    }
    test("필수 경고등(기획 7장) 포함") {
        listOf("BRAKE", "OIL_PRESSURE", "CHARGING", "COOLANT_TEMP_HIGH", "MIL", "LOW_FUEL", "TPMS", "SEAT_BELT", "AIRBAG").forEach {
            assertNotNull(WarningLamps.byId(it), it)
        }
    }
    test("byId 모르는 ID 는 null") { assertNull(WarningLamps.byId("NOPE")) }
    test("RED 경고등 분류 확인") {
        assertEquals(Severity.RED, WarningLamps.byId("OIL_PRESSURE")!!.severity)
        assertEquals(Severity.RED, WarningLamps.byId("BRAKE")!!.severity)
        assertEquals(Severity.YELLOW, WarningLamps.byId("LOW_FUEL")!!.severity)
        assertEquals(Severity.GREEN, WarningLamps.byId("TURN_SIGNAL")!!.severity)
    }
    test("forVehicleState 는 RED 먼저, 모르는 ID 무시") {
        val lamps = WarningLamps.forVehicleState(setOf("TURN_SIGNAL", "LOW_FUEL", "OIL_PRESSURE", "ABS", "BRAKE", "UNKNOWN_X"))
        assertEquals(5, lamps.size)
        assertEquals(listOf(Severity.RED, Severity.RED, Severity.YELLOW, Severity.YELLOW, Severity.GREEN), lamps.map { it.severity })
        assertEquals(listOf("BRAKE", "OIL_PRESSURE"), lamps.take(2).map { it.id }) // 같은 심각도 안에선 all 순서
        assertTrue(WarningLamps.forVehicleState(emptySet()).isEmpty())
    }
    test("Phrase ID 규칙(snake_case) 과 K 카탈로그 재사용 ID 존재") {
        assertEquals(PhraseId("warning_action_brake"), WarningLamps.byId("BRAKE")!!.actionPhraseId)
        assertEquals(WarningPhraseIds.CHOICE_RED, WarningPhraseIds.choice(Severity.RED))
        WarningLamps.all.forEach { assertTrue(it.actionPhraseId.id.matches(Regex("[a-z0-9]+(_[a-z0-9]+)*")), it.id) }
        // 이미 있다고 표시한 ID 는 실제로 카탈로그에 있어야 한다
        listOf(WarningPhraseIds.QUIZ_QUESTION, WarningPhraseIds.QUIZ_CORRECT, WarningPhraseIds.QUIZ_WRONG,
            WarningPhraseIds.SEVERITY_GREEN_MEANING, WarningPhraseIds.SEVERITY_YELLOW_MEANING, WarningPhraseIds.SEVERITY_RED_MEANING)
            .forEach { assertNotNull(Phrases.find(it), it.id) }
        assertEquals(setOf("color", "action"), Phrases.get(WarningPhraseIds.QUIZ_WRONG).placeholders.toSet())
    }
}

/** 퀴즈: P 가드, 3문항, 보기 3개, 채점, 재출제, 결정성. */
private fun testQuiz() {
    test("P 가 아니면 start 는 null") {
        val quiz = WarningQuiz(Random(1))
        listOf(Gear.D, Gear.R, Gear.N, Gear.UNKNOWN).forEach { assertNull(quiz.start(it), "$it") }
        assertNull(quiz.start(VehicleState(timestampMs = 0, gear = Gear.D)))
        assertNotNull(quiz.start(VehicleState(timestampMs = 0, gear = Gear.P)))
    }
    test("3문항, 서로 다른 경고등, 보기 3개에 정답 포함, 세 심각도 고루") {
        val s = WarningQuiz(Random(7)).start(Gear.P)!!
        assertEquals(3, s.questions.size)
        assertEquals(3, s.questions.map { it.lamp.id }.toSet().size)
        assertEquals(Severity.values().toSet(), s.questions.map { it.correct }.toSet())
        s.questions.forEach { q ->
            assertEquals(3, q.choices.size)
            assertEquals(3, q.choices.toSet().size)
            assertTrue(q.correct in q.choices)
            assertEquals(3, q.choicePhraseIds.size)
        }
        assertFalse(s.isRetry)
    }
    test("같은 시드면 같은 출제(결정적)") {
        val a = WarningQuiz(Random(123)).start(Gear.P)!!
        val b = WarningQuiz(Random(123)).start(Gear.P)!!
        assertEquals(a.questions, b.questions)
    }
    test("채점과 점수, 틀린 항목은 재출제 큐로") {
        val quiz = WarningQuiz(Random(42))
        val s = quiz.start(Gear.P)!!
        val q1 = s.current()!!
        assertEquals(1, s.number)
        val r1 = s.answer(Gear.P, q1.correct)!!
        assertTrue(r1.isCorrect)
        assertEquals(WarningPhraseIds.QUIZ_CORRECT, r1.feedbackPhraseId)

        val q2 = s.current()!!
        val wrong = Severity.values().first { it != q2.correct }
        val r2 = s.answer(Gear.P, wrong)!!
        assertFalse(r2.isCorrect)
        assertEquals(WarningPhraseIds.QUIZ_WRONG, r2.feedbackPhraseId)
        assertEquals(q2.lamp.actionPhraseId, r2.actionPhraseId)
        assertEquals(mapOf("color" to WarningPhraseIds.colorWord(q2.correct)), r2.feedbackVars)
        assertTrue(r1.feedbackVars.isEmpty())

        val q3 = s.current()!!
        s.answer(Gear.P, q3.correct)
        assertTrue(s.isFinished)
        assertNull(s.current())
        assertNull(s.answer(Gear.P, Severity.RED)) // 끝난 뒤엔 null
        assertEquals(WarningQuiz.Score(2, 3), s.score())
        assertFalse(s.score().isPerfect)
        assertEquals(listOf(q2.lamp), s.wrongLamps())
        assertEquals(listOf(q2.lamp), quiz.retryQueue)
    }
    test("주행 중(P 이탈)에는 answer 가 null 이고 상태가 안 바뀐다") {
        val quiz = WarningQuiz(Random(5))
        val s = quiz.start(Gear.P)!!
        val q = s.current()!!
        assertNull(s.answer(Gear.D, q.correct))
        assertEquals(q, s.current())
        assertEquals(WarningQuiz.Score(0, 0), s.score())
    }
    test("재출제: 큐의 경고등만 다시 나오고, 맞히면 큐에서 빠진다") {
        val quiz = WarningQuiz(Random(99))
        assertNull(quiz.startRetry(Gear.P)) // 큐 비어 있음
        val s = quiz.start(Gear.P)!!
        // 세 문항 모두 틀린다
        repeat(3) { val q = s.current()!!; s.answer(Gear.P, Severity.values().first { it != q.correct }) }
        assertEquals(3, quiz.retryQueue.size)
        assertNull(quiz.startRetry(Gear.D))

        val r = quiz.startRetry(Gear.P)!!
        assertTrue(r.isRetry)
        assertEquals(quiz.retryQueue.map { it.id }.toSet(), r.questions.map { it.lamp.id }.toSet())
        val first = r.current()!!
        r.answer(Gear.P, first.correct)                         // 맞힘 → 큐에서 제거
        assertFalse(quiz.retryQueue.any { it.id == first.lamp.id })
        val second = r.current()!!
        r.answer(Gear.P, Severity.values().first { it != second.correct }) // 또 틀림 → 그대로(중복 없음)
        assertEquals(1, quiz.retryQueue.count { it.id == second.lamp.id })
        assertEquals(2, quiz.retryQueue.size)
        quiz.clearRetryQueue()
        assertTrue(quiz.retryQueue.isEmpty())
    }
    test("ParkedOnlyGuard 는 P→주행 전환 때 한 번만 호출") {
        var calls = 0
        val g = ParkedOnlyGuard { calls++ }
        g.onState(VehicleState(0, gear = Gear.P))
        g.onState(VehicleState(1, gear = Gear.P))
        g.onState(VehicleState(2, gear = Gear.D))
        g.onState(VehicleState(3, gear = Gear.D))
        assertEquals(1, calls)
        g.onState(VehicleState(4, gear = Gear.P))
        g.onState(VehicleState(5, gear = Gear.R))
        assertEquals(2, calls)
    }
}

fun main() {
    println("WarningQuizTest")
    testLampData()
    testQuiz()
    println("WarningQuizTest: $passed tests passed")
}
