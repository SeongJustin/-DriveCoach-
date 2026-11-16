// 가짜 경고등 퀴즈 — 경고등 3색 뜻, 출발 전 브리핑 "경고등 대응 다시 묻기"
package kr.drivecoach.core.warning

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.tts.PhraseId
import kotlin.random.Random

/**
 * 가짜 경고등 퀴즈 생성기.
 * - 안전 원칙: 화면을 보며 배우는 것은 주차(P) 상태에서만. 그래서 모든 진입점은 gear == P 가 아니면 null 을 돌려준다.
 * - 한 라운드는 무작위 [QUESTIONS_PER_ROUND] 문항, 문항마다 보기 [CHOICES_PER_QUESTION] 개(GREEN/YELLOW/RED 행동).
 * - 틀린 문항의 경고등은 [retryQueue] 에 쌓이고 [startRetry] 로 다시 출제한다. 맞히면 큐에서 빠진다.
 * - [random] 에 시드를 주면 출제 순서·보기 순서가 결정적이라 테스트할 수 있다.
 *
 * 사용자에게 들리는 문장은 이 클래스에 없다. [Question.questionPhraseId], [AnswerResult.feedbackPhraseId] 등
 * Phrases ID 만 제공하고, 실제 문장은 K 작업자의 Phrases 카탈로그가 가진다.
 */
class WarningQuiz(
    private val random: Random = Random.Default,
    private val lamps: List<WarningLamp> = WarningLamps.all,
) {
    companion object {
        /** 한 라운드 문항 수. */
        const val QUESTIONS_PER_ROUND = 3
        /** 문항당 보기 수(= Severity 가짓수). */
        const val CHOICES_PER_QUESTION = 3
    }

    /** 퀴즈 한 문항. [choices] 는 섞인 순서의 보기, 정답은 [correct]. */
    data class Question(val lamp: WarningLamp, val choices: List<Severity>) {
        /** 정답 심각도. */
        val correct: Severity get() = lamp.severity
        /** 질문 멘트 ID("이 등이 켜지면 어떻게 할까요?"). 화면에는 [lamp] 의 아이콘과 nameKo 를 함께 보여 준다. */
        val questionPhraseId: PhraseId get() = WarningPhraseIds.QUIZ_QUESTION
        /** 보기 멘트 ID(보기 순서대로). */
        val choicePhraseIds: List<PhraseId> get() = choices.map { WarningPhraseIds.choice(it) }
    }

    /** 한 문항의 채점 결과. */
    data class AnswerResult(
        val question: Question,
        val chosen: Severity,
        val isCorrect: Boolean,
    ) {
        /** 정답/오답 피드백 멘트 ID. 오답 멘트는 {color}, {action} 자리를 [feedbackVars] 로 채운다. */
        val feedbackPhraseId: PhraseId get() = if (isCorrect) WarningPhraseIds.QUIZ_CORRECT else WarningPhraseIds.QUIZ_WRONG
        /** 정답 행동 멘트 ID. 오답일 때 {action} 자리에 이 멘트의 문장을 넣는다. */
        val actionPhraseId: PhraseId get() = question.lamp.actionPhraseId
        /** 정답 보기 멘트 ID. */
        val correctChoicePhraseId: PhraseId get() = WarningPhraseIds.choice(question.correct)
        /**
         * 오답 멘트의 자리 채움 값. {action} 은 Phrases.render(actionPhraseId) 결과를 넣어야 하므로
         * 여기서는 색 이름만 채우고, app 이 `vars + ("action" to 렌더된 문장)` 으로 완성한다.
         */
        val feedbackVars: Map<String, String>
            get() = if (isCorrect) emptyMap() else mapOf("color" to WarningPhraseIds.colorWord(question.correct))
    }

    /** 라운드 점수. */
    data class Score(val correct: Int, val total: Int) {
        /** 전부 맞혔는지. */
        val isPerfect: Boolean get() = total > 0 && correct == total
    }

    /**
     * 진행 중인 퀴즈 라운드. [answer] 로 한 문항씩 채점한다.
     * 차가 P 를 벗어나면 [answer] 는 null 을 돌려주고 상태를 바꾸지 않는다(앱은 이때 퀴즈 화면을 닫아야 한다).
     */
    inner class Session internal constructor(
        val questions: List<Question>,
        /** 재출제 라운드인지. */
        val isRetry: Boolean,
    ) {
        private var index = 0
        private val results = mutableListOf<AnswerResult>()

        /** 지금 풀 문항. 라운드가 끝났으면 null. */
        fun current(): Question? = questions.getOrNull(index)

        /** 현재 문항 번호(1부터). */
        val number: Int get() = (index + 1).coerceAtMost(questions.size)

        /** 라운드가 끝났는지. */
        val isFinished: Boolean get() = index >= questions.size

        /** 지금까지의 채점 결과. */
        val answered: List<AnswerResult> get() = results.toList()

        /**
         * 현재 문항에 답한다. gear != P 이거나 이미 끝났으면 null.
         * 틀리면 경고등을 재출제 큐에 넣고, 재출제 라운드에서 맞히면 큐에서 뺀다.
         */
        fun answer(gear: Gear, chosen: Severity): AnswerResult? {
            if (gear != Gear.P) return null
            val q = current() ?: return null
            val result = AnswerResult(q, chosen, chosen == q.correct)
            results += result
            index++
            if (result.isCorrect) retry.remove(q.lamp.id) else if (!retry.contains(q.lamp.id)) retry += q.lamp.id
            return result
        }

        /** 점수(지금까지 답한 문항 기준). */
        fun score(): Score = Score(results.count { it.isCorrect }, results.size)

        /** 이 라운드에서 틀린 경고등. */
        fun wrongLamps(): List<WarningLamp> = results.filter { !it.isCorrect }.map { it.question.lamp }
    }

    /** 재출제 큐(틀린 순서, 중복 없음). ID 로 보관. */
    private val retry = mutableListOf<String>()

    /** 재출제 대기 중인 경고등 목록. */
    val retryQueue: List<WarningLamp> get() = retry.mapNotNull { id -> lamps.firstOrNull { it.id == id } }

    /** 새 라운드를 만든다. gear != P 면 null. 세 심각도에서 고루 뽑은 뒤 순서를 섞는다. */
    fun start(gear: Gear, count: Int = QUESTIONS_PER_ROUND): Session? {
        if (gear != Gear.P) return null
        val picked = pickBalanced(count)
        if (picked.isEmpty()) return null
        return Session(picked.map { makeQuestion(it) }, isRetry = false)
    }

    /** 재출제 큐 앞에서부터 [count] 개를 다시 출제한다. gear != P 이거나 큐가 비었으면 null. */
    fun startRetry(gear: Gear, count: Int = QUESTIONS_PER_ROUND): Session? {
        if (gear != Gear.P) return null
        val picked = retryQueue.take(count)
        if (picked.isEmpty()) return null
        return Session(picked.map { makeQuestion(it) }, isRetry = true)
    }

    /** 차량 상태로 바로 시작하는 편의 함수. */
    fun start(state: VehicleState): Session? = start(state.gear)

    /** 재출제 큐를 비운다(새 사용자·새 세션). */
    fun clearRetryQueue() = retry.clear()

    /** 심각도별로 돌아가며 뽑아 한 라운드에 GREEN/YELLOW/RED 가 고루 섞이게 한다. */
    private fun pickBalanced(count: Int): List<WarningLamp> {
        val buckets = Severity.values()
            .map { sev -> lamps.filter { it.severity == sev }.shuffled(random).toMutableList() }
            .filter { it.isNotEmpty() }
            .shuffled(random)
        val out = mutableListOf<WarningLamp>()
        var i = 0
        while (out.size < count && buckets.any { it.isNotEmpty() }) {
            val b = buckets[i % buckets.size]
            if (b.isNotEmpty()) out += b.removeAt(0)
            i++
        }
        return out.shuffled(random)
    }

    /** 보기 3개(GREEN/YELLOW/RED)를 섞어 문항을 만든다. 정답은 항상 포함된다. */
    private fun makeQuestion(lamp: WarningLamp): Question =
        Question(lamp, Severity.values().toList().shuffled(random).take(CHOICES_PER_QUESTION))
}

/**
 * 주행 중 퀴즈가 열려 있으면 닫도록 알리는 감시자. 차량 상태를 받아 P 를 벗어나면 [onLeaveParked] 를 한 번 호출한다.
 * 앱 모듈에서 SignalSource.addListener 로 붙인다.
 */
class ParkedOnlyGuard(private val onLeaveParked: () -> Unit) : StateListener {
    private var wasParked = false
    override fun onState(state: VehicleState) {
        val parked = state.gear == Gear.P
        if (wasParked && !parked) onLeaveParked()
        wasParked = parked
    }
}
