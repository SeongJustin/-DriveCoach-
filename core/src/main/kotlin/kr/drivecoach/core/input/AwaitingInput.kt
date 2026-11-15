// 담당: C+G (입력이 필요한데 입력이 없는 상황 처리)
package kr.drivecoach.core.input

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId

/**
 * "운전자 입력을 기다리는 중"인 하나의 프롬프트. 원칙: 입력이 필요한데 없는 상황을 반드시 처리한다.
 *
 * @property promptId 어떤 안내(연습 항목·질문)에 대한 대기인지. practice 모듈의 항목 ID 등.
 * @property deadlineMs 이 시각(ms)까지 입력이 없으면 [AwaitingInputManager.tick] 이 조치한다.
 * @property accepts 이 대기를 끝내는 명령들. 기본은 확인/다음.
 * @property repromptCount 지금까지 재안내한 횟수(최대 [AwaitingInputManager.MAX_REPROMPTS]).
 */
data class AwaitingInput(
    val promptId: String,
    val deadlineMs: Long,
    val openedAtMs: Long,
    val accepts: Set<Command> = setOf(Command.CONFIRM, Command.NEXT),
    val repromptCount: Int = 0,
)

/** [AwaitingInputManager.tick] 의 결과. 앱은 이걸 보고 TTS 를 틀거나 다음으로 넘어간다. */
sealed class NoInputDecision {
    /** 기다리는 프롬프트가 없다. */
    object Idle : NoInputDecision()

    /** 아직 마감 전. 아무것도 하지 않는다. */
    data class Waiting(val prompt: AwaitingInput) : NoInputDecision()

    /**
     * 차가 서 있고 재안내 횟수가 남아 있어 한 번 더 부드럽게 안내한다.
     * [phraseId] = "system_no_input_reprompt" ("천천히 해도 돼요. 준비되면 버튼 눌러요").
     * 마감은 [prompt].deadlineMs 로 연장됐다.
     */
    data class Reprompt(val prompt: AwaitingInput, val phraseId: PhraseId) : NoInputDecision()

    /**
     * "다음 기회"로 넘긴다. 주행 중이거나 재안내를 이미 했는데도 입력이 없을 때.
     * 재촉하지 않고, 진행 중인 안내도 끊지 않는다(판단은 운전자). 앱은 이 promptId 를 미해결로 기록하고
     * 다음 정차·다음 기회에 다시 물을 수 있다.
     */
    data class Deferred(val promptId: String, val wasMoving: Boolean) : NoInputDecision()
}

/**
 * 대기 중인 입력을 관리한다. 한 번에 하나의 프롬프트만 기다린다(주행 중 입력 최소화 원칙).
 *
 * 흐름:
 *  1. 안내를 틀고 [open] 으로 대기를 연다.
 *  2. 명령이 오면 [onCommand] — 받아들이는 명령이면 대기가 닫히고 true.
 *  3. 주기적으로(예: 1초) [tick] 을 부른다. 마감이 지나면
 *     (a) 차가 서 있으면 재안내 1회 — [NoInputDecision.Reprompt]
 *     (b) 주행 중이면 재촉 없이 "다음 기회" — [NoInputDecision.Deferred]
 *     (c) 재안내 후에도 없으면 — [NoInputDecision.Deferred]
 */
class AwaitingInputManager(
    /** 기본 대기 시간(ms). */
    private val defaultTimeoutMs: Long = DEFAULT_TIMEOUT_MS,
    /** 재안내 후 다시 기다리는 시간(ms). */
    private val repromptTimeoutMs: Long = DEFAULT_REPROMPT_TIMEOUT_MS,
    /** 이 속도 이하면 "정지"로 본다(km/h). */
    private val speedZeroEpsilonKmh: Double = DEFAULT_SPEED_EPSILON_KMH,
) {
    /** 지금 기다리는 프롬프트. 없으면 null. */
    var current: AwaitingInput? = null
        private set

    /** 새 대기를 연다. 이미 열린 것이 있으면 조용히 교체한다(이전 것은 Deferred 로 처리된 셈). */
    fun open(
        promptId: String,
        nowMs: Long,
        timeoutMs: Long = defaultTimeoutMs,
        accepts: Set<Command> = setOf(Command.CONFIRM, Command.NEXT),
    ): AwaitingInput {
        val prompt = AwaitingInput(promptId = promptId, deadlineMs = nowMs + timeoutMs, openedAtMs = nowMs, accepts = accepts)
        current = prompt
        return prompt
    }

    /** 대기를 취소한다(세울래요, 단계 변경 등). */
    fun cancel() { current = null }

    /**
     * 명령이 들어왔다. 대기를 끝내는 명령이면 닫고 true.
     * AGAIN 은 안내를 다시 들려주는 것이므로 마감을 지금부터 다시 센다(false 반환, 계속 대기).
     * PULL_OVER 는 모든 대기를 취소한다(true 반환 — 소비됨).
     */
    fun onCommand(command: Command, nowMs: Long): Boolean {
        val prompt = current ?: return false
        return when {
            command == Command.PULL_OVER -> { current = null; true }
            command in prompt.accepts -> { current = null; true }
            command == Command.AGAIN -> { current = prompt.copy(deadlineMs = nowMs + defaultTimeoutMs); false }
            else -> false
        }
    }

    /** 주기 호출. 차량 상태로 정지/주행을 판단해 [tick] 한다. */
    fun tick(nowMs: Long, state: VehicleState): NoInputDecision = tick(nowMs, isStopped(state))

    /** 주기 호출. [vehicleStopped] 는 호출자가 이미 판단한 정지 여부. */
    fun tick(nowMs: Long, vehicleStopped: Boolean): NoInputDecision {
        val prompt = current ?: return NoInputDecision.Idle
        if (nowMs < prompt.deadlineMs) return NoInputDecision.Waiting(prompt)

        if (vehicleStopped && prompt.repromptCount < MAX_REPROMPTS) {
            val extended = prompt.copy(deadlineMs = nowMs + repromptTimeoutMs, repromptCount = prompt.repromptCount + 1)
            current = extended
            return NoInputDecision.Reprompt(extended, REPROMPT_PHRASE)
        }
        current = null
        return NoInputDecision.Deferred(prompt.promptId, wasMoving = !vehicleStopped)
    }

    /** 정지 판정: 속도 0(또는 모름이면서 기어 P). 주행 중 재촉 금지 원칙상 애매하면 "주행 중"으로 본다. */
    fun isStopped(state: VehicleState): Boolean {
        val speed = state.speedKmh
        return when {
            speed != null -> speed <= speedZeroEpsilonKmh
            else -> state.gear == Gear.P
        }
    }

    companion object {
        const val DEFAULT_TIMEOUT_MS = 15_000L
        const val DEFAULT_REPROMPT_TIMEOUT_MS = 20_000L
        const val DEFAULT_SPEED_EPSILON_KMH = 0.5
        /** 재안내 최대 횟수. 재촉하지 않는다. */
        const val MAX_REPROMPTS = 1

        /**
         * 재안내 멘트 ID. 문장은 K 담당 core/tts 카탈로그에 등록 요청함.
         * 문장: "천천히 해도 돼요. 준비되면 버튼 눌러요".
         */
        val REPROMPT_PHRASE = PhraseId("system_no_input_reprompt")
    }
}
