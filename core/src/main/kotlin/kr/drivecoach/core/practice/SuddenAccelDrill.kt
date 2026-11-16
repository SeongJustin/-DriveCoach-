// 2-4 급발진·헛밟음 대처 훈련 + 5-2 브리핑 재확인용 간격 반복
package kr.drivecoach.core.practice

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId

/**
 * 급발진·헛밟음 대처 훈련(2-4).
 *
 * 흐름: P 상태에서 시작 → EXPLAIN(화면+음성 30초: "발 떼고 → 브레이크 끝까지 → N → 시동 버튼 길게")
 *   → ACTION(실제로 "브레이크 밟은 채 유지 + N 변속" 이 감지되면 통과; 설명 중에 해도 통과)
 *   → QUIZ(행동 순서 고르기 3문항) → DONE.
 * 이후 출발 전 브리핑(5-2)에서 [nextReviewDueMs] 가 지났으면 한 문장 재확인 질문을 하고 [onReviewResult] 로 결과를 돌려준다.
 *
 * 주의: 브레이크 신호는 on/off 뿐이라 "끝까지" 밟았는지는 알 수 없다. brakePressed=true 를 N 변속 시점까지 끊김 없이 유지한 것을
 * "끝까지"로 간주한다. 시동 버튼 길게 누르기는 P 정차 중 실제로 시키지 않는다(시동이 꺼지므로 설명·퀴즈로만 다룬다).
 */
class SuddenAccelDrill(
    private val listener: ((Event) -> Unit)? = null,
) : StateListener {

    /** 훈련 단계. */
    enum class Stage { IDLE, EXPLAIN, ACTION, QUIZ, DONE }

    /** 퀴즈 보기에 쓰는 행동 단위. */
    enum class Action { FOOT_OFF_PEDALS, BRAKE_FULL, SHIFT_N, ENGINE_BUTTON_LONG, PUMP_BRAKE, SHIFT_P, TURN_WHEEL }

    /** 퀴즈 문항. 보기는 행동 순서 목록, 정답은 [correct] 번째 보기. 문항·보기 설명 멘트는 PhraseId. */
    data class Question(val id: String, val phrase: PhraseId, val options: List<List<Action>>, val correct: Int)

    /** 엔진이 내놓는 이벤트: 단계와 멘트. */
    data class Event(val stage: Stage, val phrase: PhraseId?, val questionIndex: Int? = null, val correct: Boolean? = null)

    /**
     * "브레이크를 밟은 채 유지하면서 N 으로 변속" 누적 판정. PracticeEngine 의 2-4-sudden-accel 항목과 이 드릴이 공유한다.
     * 브레이크가 떨어졌다 다시 밟히면 처음부터 다시 센다.
     */
    class ActionCheck : StatefulCheck {
        private var brakeHeld = false
        override fun reset() { brakeHeld = false }
        override fun onState(prev: VehicleState, now: VehicleState): Boolean {
            if (now.brakePressed != true) { brakeHeld = false; return false }
            // 브레이크를 먼저 밟고(직전 스냅샷에서도 밟힌 채) N 으로 바꿔야 한다.
            val heldBefore = brakeHeld || prev.brakePressed == true
            brakeHeld = true
            val shiftedToN = now.gear == Gear.N && prev.gear != Gear.N
            return heldBefore && shiftedToN
        }
        override fun progress(): String = if (brakeHeld) "브레이크 유지 중 → N으로" else "브레이크 끝까지"
    }

    var stage: Stage = Stage.IDLE
        private set
    private var explainStartedMs: Long = 0
    private val check = ActionCheck()
    private var prev: VehicleState? = null

    /** 퀴즈 진행 인덱스와 정답 수. */
    var questionIndex: Int = 0
        private set
    var correctCount: Int = 0
        private set

    /** 훈련 통과 시각(ms). 아직이면 null. */
    var passedAtMs: Long? = null
        private set

    /** 지금까지 브리핑 재확인에 연속으로 맞힌 횟수(간격 반복 단계). */
    var reviewStreak: Int = 0
        private set

    /** 다음 재확인 질문 예정 시각(ms). 훈련 완료 전이면 null. */
    var nextReviewDueMs: Long? = null
        private set

    /** 시작. P 가 아니면 거부(false). */
    fun start(state: VehicleState, nowMs: Long): Boolean {
        if (state.gear != Gear.P) { emit(Event(stage, PracticeEngine.PHRASE_NEED_P)); return false }
        stage = Stage.EXPLAIN
        explainStartedMs = nowMs
        prev = state
        check.reset()
        questionIndex = 0; correctCount = 0
        emit(Event(Stage.EXPLAIN, PHRASE_EXPLAIN))
        return true
    }

    /** 시간 경과 통지. 설명 30초가 지나면 ACTION 으로 넘어가 "직접 해보세요" 멘트. */
    fun tick(nowMs: Long) {
        if (stage == Stage.EXPLAIN && nowMs - explainStartedMs >= EXPLAIN_MS) {
            stage = Stage.ACTION
            emit(Event(Stage.ACTION, PHRASE_TRY))
        }
    }

    /** 차량 상태 수신. EXPLAIN/ACTION 중 동작이 감지되면 통과 → 퀴즈 시작. */
    override fun onState(state: VehicleState) {
        val p = prev
        prev = state
        if (stage != Stage.EXPLAIN && stage != Stage.ACTION) return
        if (p == null) return
        if (check.onState(p, state)) {
            passedAtMs = state.timestampMs
            stage = Stage.QUIZ
            emit(Event(Stage.QUIZ, PHRASE_PASS))
            emit(Event(Stage.QUIZ, QUIZ[0].phrase, questionIndex = 0))
        }
    }

    /** 퀴즈 답. 정답 여부를 돌려주고, 3문항 끝나면 DONE 으로 가며 첫 재확인 시각을 잡는다. */
    fun answer(optionIndex: Int, nowMs: Long): Boolean {
        check(stage == Stage.QUIZ) { "퀴즈 단계가 아님" }
        val q = QUIZ[questionIndex]
        val ok = optionIndex == q.correct
        if (ok) correctCount++
        emit(Event(Stage.QUIZ, if (ok) PHRASE_QUIZ_CORRECT else PHRASE_QUIZ_WRONG, questionIndex, ok))
        questionIndex++
        if (questionIndex >= QUIZ.size) {
            stage = Stage.DONE
            reviewStreak = 0
            nextReviewDueMs = nowMs + REVIEW_INTERVALS_MS[0]
            emit(Event(Stage.DONE, PHRASE_DONE))
        } else {
            emit(Event(Stage.QUIZ, QUIZ[questionIndex].phrase, questionIndex))
        }
        return ok
    }

    /** 현재 퀴즈 문항. */
    fun currentQuestion(): Question? = if (stage == Stage.QUIZ) QUIZ[questionIndex] else null

    /** 브리핑(5-2)에서 재확인 질문을 할 때인가. */
    fun isReviewDue(nowMs: Long): Boolean = nextReviewDueMs?.let { nowMs >= it } ?: false

    /**
     * 재확인 결과 반영. 맞히면 간격이 1일 → 3일 → 7일(이후 7일 유지)로 늘고, 틀리면 1일로 되돌아간다.
     * 돌려주는 값은 새 [nextReviewDueMs].
     */
    fun onReviewResult(correct: Boolean, nowMs: Long): Long {
        reviewStreak = if (correct) reviewStreak + 1 else 0
        val due = nowMs + intervalFor(reviewStreak)
        nextReviewDueMs = due
        return due
    }

    private fun emit(e: Event) { listener?.invoke(e) }

    companion object {
        /** 설명 시간(30초). */
        const val EXPLAIN_MS = 30_000L
        private const val DAY_MS = 24L * 60 * 60 * 1000
        /** 간격 반복: 1일, 3일, 7일. */
        val REVIEW_INTERVALS_MS = listOf(1 * DAY_MS, 3 * DAY_MS, 7 * DAY_MS)

        /** 연속 정답 수에 따른 다음 간격. 범위를 넘으면 마지막(7일) 유지. */
        fun intervalFor(streak: Int): Long = REVIEW_INTERVALS_MS[streak.coerceIn(0, REVIEW_INTERVALS_MS.size - 1)]

        /** 올바른 대처 순서. */
        val CORRECT_SEQUENCE = listOf(Action.FOOT_OFF_PEDALS, Action.BRAKE_FULL, Action.SHIFT_N, Action.ENGINE_BUTTON_LONG)
        /** 브리핑 재확인 질문의 정답(같은 순서). */
        val REVIEW_ANSWER = CORRECT_SEQUENCE

        val PHRASE_EXPLAIN = PhraseId("practice_2_4_sudden_accel_explain")
        val PHRASE_TRY = PhraseId("practice_2_4_sudden_accel_try")
        val PHRASE_PASS = PhraseId("practice_2_4_sudden_accel_pass")
        val PHRASE_QUIZ_CORRECT = PhraseId("practice_2_4_sudden_accel_quiz_correct")
        val PHRASE_QUIZ_WRONG = PhraseId("practice_2_4_sudden_accel_quiz_wrong")
        val PHRASE_DONE = PhraseId("practice_2_4_sudden_accel_done")
        val PHRASE_REVIEW_QUESTION = PhraseId("practice_2_4_sudden_accel_review_question")

        /** 3문항: 모두 "행동 순서 고르기". 정답은 항상 CORRECT_SEQUENCE 이고 오답은 흔한 실수. */
        val QUIZ: List<Question> = listOf(
            Question(
                "q1", PhraseId("practice_2_4_sudden_accel_quiz_q1"),
                options = listOf(
                    CORRECT_SEQUENCE,
                    listOf(Action.PUMP_BRAKE, Action.SHIFT_P, Action.ENGINE_BUTTON_LONG),
                    listOf(Action.TURN_WHEEL, Action.BRAKE_FULL, Action.SHIFT_N),
                ), correct = 0,
            ),
            Question(
                "q2", PhraseId("practice_2_4_sudden_accel_quiz_q2"),
                options = listOf(
                    listOf(Action.SHIFT_P, Action.BRAKE_FULL, Action.ENGINE_BUTTON_LONG),
                    CORRECT_SEQUENCE,
                    listOf(Action.ENGINE_BUTTON_LONG, Action.BRAKE_FULL, Action.SHIFT_N),
                ), correct = 1,
            ),
            Question(
                "q3", PhraseId("practice_2_4_sudden_accel_quiz_q3"),
                options = listOf(
                    listOf(Action.BRAKE_FULL, Action.PUMP_BRAKE, Action.SHIFT_P),
                    listOf(Action.FOOT_OFF_PEDALS, Action.SHIFT_N, Action.TURN_WHEEL),
                    CORRECT_SEQUENCE,
                ), correct = 2,
            ),
        )
    }
}
