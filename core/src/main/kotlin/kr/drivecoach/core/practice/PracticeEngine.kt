// 기능 연습 상태 머신
package kr.drivecoach.core.practice

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId

/** 연습 항목 하나의 진행 단계. "설명 → 해보기 → 확인 → (통과 | 건너뜀)". */
enum class Phase { EXPLAIN, TRY, CONFIRM, PASSED, SKIPPED }

/** 사용자가 주행 중에도 할 수 있는 입력(음성 또는 휠 버튼). PREV 는 화면 "이전" 버튼: 목록의 바로 앞 항목으로. */
enum class UserInput { CONFIRM, NEXT, AGAIN, SKIP, PREV }

/**
 * 누적 상태가 필요한 자동 판정(예: 기어를 P→R→N→D 순서로 모두 거쳤는지).
 * 항목이 시작될 때 [reset] 되고, 이후 매 상태 변화마다 [onState] 가 불린다.
 */
interface StatefulCheck {
    /** 항목 시작(또는 다시 하기) 시 누적 상태 초기화. */
    fun reset()
    /** 상태 변화를 반영하고, 이번 변화로 조건이 완성되었으면 true. */
    fun onState(prev: VehicleState, now: VehicleState): Boolean
    /** 화면 표시용 진행 상황(예: "P→R 완료"). 없으면 null. */
    fun progress(): String? = null
}

/** 항목을 어떻게 통과시키는가. 감지 불가 항목은 반드시 [Manual] 이어야 한다(규약). */
sealed class CheckKind {
    /** 감지 가능: 직전/현재 상태 비교만으로 판정. */
    class Auto(val predicate: (prev: VehicleState, now: VehicleState) -> Boolean) : CheckKind()

    /** 감지 불가: 사용자가 눈으로 보고 "확인"해야 통과. [confirmPhrase] 는 확인을 요청하는 멘트. */
    class Manual(val confirmPhrase: PhraseId) : CheckKind()

    /** 감지 가능하지만 센서가 없는 차량도 있어 수동 확인도 허용. [auto] 는 [Auto] 또는 [Stateful]. */
    class Either(val auto: CheckKind, val manual: Manual) : CheckKind() {
        init { require(auto is Auto || auto is Stateful) { "Either.auto 는 Auto 또는 Stateful 이어야 한다" } }
    }

    /** 감지 가능 + 누적 상태 필요(기어 순서 등). 항목마다 새 인스턴스를 만들도록 factory 로 받는다. */
    class Stateful(val factory: () -> StatefulCheck) : CheckKind()

    /** 센서로 판정할 수 있는 종류인가(감지 가능 표기). */
    val detectable: Boolean
        get() = when (this) {
            is Auto, is Stateful, is Either -> true
            is Manual -> false
        }
}

/**
 * 연습 항목 하나.
 * @param chapter "2-1" 처럼 장 번호
 * @param requiredCapability 이 기능이 차량 옵션에 있어야만 보여줄 때 그 capability 이름. null 이면 항상 포함.
 */
data class PracticeItem(
    val id: String,
    val chapter: String,
    val title: String,
    val explainPhrase: PhraseId,
    val tryPhrase: PhraseId,
    val passPhrase: PhraseId,
    val check: CheckKind,
    val requiredCapability: String? = null,
) {
    /** 감지 가능 항목인가. 화면에 "자동 확인" / "직접 확인" 표기용. */
    val detectable: Boolean get() = check.detectable
}

/** 엔진이 내놓는 결과: 어느 항목이 어느 단계에 있고, 지금 무슨 멘트를 해야 하는지. phrase 가 null 이면 말할 것 없음. */
data class PracticeStep(
    val itemId: String?,
    val phase: Phase?,
    val phrase: PhraseId?,
    /** 자동 통과였는지(센서 감지). 리포트·화면 표기용. */
    val autoPassed: Boolean = false,
    /** 누적 판정 진행 상황(기어 순서 등). */
    val progress: String? = null,
)

/** 엔진 상태가 바뀔 때마다 받는 콜백(앱에서 TTS 발화·화면 갱신). */
fun interface PracticeStepListener {
    fun onStep(step: PracticeStep)
}

/**
 * "설명 → 해보기 → 확인 → 다음" 상태 머신.
 *
 * 규칙:
 * - 2장(화면 학습)은 P 상태에서만 시작할 수 있다. gear != P 이면 [start] 거부.
 * - 감지 가능 항목은 EXPLAIN 중이라도 실제로 조작하면 즉시 PASSED(자동 통과).
 * - 어느 단계든 SKIP 으로 건너뛸 수 있다.
 * - 감지 불가 항목은 센서로 판정하지 않고 CONFIRM 입력으로만 통과한다.
 *
 * 입력별 전이:
 * - EXPLAIN: NEXT→TRY, AGAIN→EXPLAIN(재설명), CONFIRM→TRY, SKIP→SKIPPED
 * - TRY: (수동 허용 항목) CONFIRM→PASSED, NEXT→CONFIRM(확인 요청) / (자동 전용) CONFIRM·NEXT→"아직 감지 안 됨" 안내, AGAIN→EXPLAIN, SKIP→SKIPPED
 * - CONFIRM: CONFIRM→PASSED, NEXT→확인 재요청, AGAIN→TRY, SKIP→SKIPPED
 * - PASSED/SKIPPED: NEXT→목록의 다음 항목 시작, AGAIN→같은 항목 다시
 * - 어느 단계든 PREV→목록의 바로 앞 항목을 처음(EXPLAIN)부터. 첫 항목이면 현재 항목을 처음부터. 현재 항목의 결과는 지우지 않는다.
 *
 * @param initialResults 이전 세션의 결과(통과/건너뜀). 연습을 닫았다 다시 열어도 ✓ 표시와 "다음 미완료 항목" 이 이어지게 한다.
 */
class PracticeEngine(
    items: List<PracticeItem>,
    private val listener: PracticeStepListener? = null,
    initialResults: Map<String, Phase> = emptyMap(),
) : StateListener {

    private val items: List<PracticeItem> = items
    private val byId: Map<String, PracticeItem> = items.associateBy { it.id }

    /** 마지막으로 받은 차량 상태. 아직 없으면 null(=P 여부를 몰라 시작 거부). */
    var latestState: VehicleState? = null
        private set

    /** 현재 진행 중(또는 방금 끝난) 항목. */
    var current: PracticeItem? = null
        private set

    /** 현재 항목의 단계. 항목이 없으면 null. */
    var phase: Phase? = null
        private set

    /** 항목별 최종 결과(PASSED / SKIPPED). 진행 중이거나 안 한 항목은 없음. */
    private val resultsById = LinkedHashMap<String, Phase>().apply {
        initialResults.forEach { (id, ph) -> if (id in byId && (ph == Phase.PASSED || ph == Phase.SKIPPED)) put(id, ph) }
    }
    val results: Map<String, Phase> get() = resultsById

    /** 마지막으로 내놓은 스텝. */
    var lastStep: PracticeStep = PracticeStep(null, null, null)
        private set

    private var statefulCheck: StatefulCheck? = null
    private var prevForCheck: VehicleState? = null
    private var lastAutoPassed = false

    /** 차량 상태 수신. 현재 항목이 감지 가능하면 자동 판정한다(EXPLAIN 단계 포함). */
    override fun onState(state: VehicleState) {
        val prev = latestState
        latestState = state
        val item = current ?: return
        val ph = phase ?: return
        if (ph == Phase.PASSED || ph == Phase.SKIPPED) return
        if (!autoDetect) return
        val base = prevForCheck ?: prev ?: return
        prevForCheck = state
        if (autoCheck(item.check, base, state)) {
            finish(item, Phase.PASSED, auto = true)
        } else {
            statefulCheck?.progress()?.let { p ->
                // 진행 상황만 바뀐 경우 화면 갱신용으로 알림(멘트는 없음)
                if (p != lastStep.progress) emit(PracticeStep(item.id, ph, null, progress = p))
            }
        }
    }

    private fun autoCheck(check: CheckKind, prev: VehicleState, now: VehicleState): Boolean = when (check) {
        is CheckKind.Auto -> check.predicate(prev, now)
        is CheckKind.Stateful -> statefulCheck?.onState(prev, now) ?: false
        is CheckKind.Either -> autoCheck(check.auto, prev, now)
        is CheckKind.Manual -> false
    }

    /** 현재 상태에서 2장 항목을 시작할 수 있는가(= 기어 P). */
    fun canStart(): Boolean = latestState?.gear == Gear.P

    /** 항목 시작. P 가 아니면 거부하고 [PHRASE_NEED_P] 를 돌려준다(현재 항목은 바뀌지 않음). */
    fun start(itemId: String): PracticeStep {
        val item = byId[itemId] ?: return emit(PracticeStep(null, null, PHRASE_NO_SUCH_ITEM))
        if (!canStart()) return emit(PracticeStep(current?.id, phase, PHRASE_NEED_P))
        current = item
        prevForCheck = latestState
        lastAutoPassed = false
        statefulCheck = when (val c = item.check) {
            is CheckKind.Stateful -> c.factory().also { it.reset() }
            is CheckKind.Either -> (c.auto as? CheckKind.Stateful)?.factory()?.also { it.reset() }
            else -> null
        }
        phase = Phase.EXPLAIN
        return emit(PracticeStep(item.id, Phase.EXPLAIN, item.explainPhrase, progress = statefulCheck?.progress()))
    }

    /** 사용자 입력 처리. 항목이 없으면 목록의 첫 미완료 항목을 시작한다(NEXT 일 때). */
    fun onInput(input: UserInput): PracticeStep {
        val item = current ?: return if (input == UserInput.NEXT) startNextPending(null) else emit(PracticeStep(null, null, null))
        val ph = phase ?: return emit(PracticeStep(null, null, null))
        if (input == UserInput.PREV) return startPrevious(item)
        val manual = manualOf(item.check)
        return when (ph) {
            Phase.EXPLAIN -> when (input) {
                UserInput.NEXT, UserInput.CONFIRM -> go(item, Phase.TRY, item.tryPhrase)
                UserInput.AGAIN -> go(item, Phase.EXPLAIN, item.explainPhrase)
                UserInput.SKIP -> finish(item, Phase.SKIPPED)
                UserInput.PREV -> startPrevious(item)
            }
            Phase.TRY -> when (input) {
                UserInput.CONFIRM -> if (manual != null) finish(item, Phase.PASSED) else go(item, Phase.TRY, PHRASE_NOT_DETECTED)
                UserInput.NEXT -> if (manual != null) go(item, Phase.CONFIRM, manual.confirmPhrase) else go(item, Phase.TRY, PHRASE_NOT_DETECTED)
                UserInput.AGAIN -> go(item, Phase.EXPLAIN, item.explainPhrase)
                UserInput.SKIP -> finish(item, Phase.SKIPPED)
                UserInput.PREV -> startPrevious(item)
            }
            Phase.CONFIRM -> when (input) {
                UserInput.CONFIRM -> finish(item, Phase.PASSED)
                UserInput.NEXT -> go(item, Phase.CONFIRM, manual?.confirmPhrase ?: PHRASE_NOT_DETECTED)
                UserInput.AGAIN -> go(item, Phase.TRY, item.tryPhrase)
                UserInput.SKIP -> finish(item, Phase.SKIPPED)
                UserInput.PREV -> startPrevious(item)
            }
            Phase.PASSED, Phase.SKIPPED -> when (input) {
                UserInput.NEXT, UserInput.CONFIRM -> startNextPending(item)
                UserInput.AGAIN -> start(item.id)
                UserInput.SKIP -> startNextPending(item)
                UserInput.PREV -> startPrevious(item)
            }
        }
    }

    /** false 면 차량 신호로 자동 통과시키지 않는다. 앱은 설명만 보여 주고 '다음'으로 넘긴다. */
    var autoDetect: Boolean = true

    /** 설명만 보고 넘어가기: 지금 항목을 본 것으로 적고 바로 다음 항목을 연다(해 보기 단계 없음). */
    fun advance(): PracticeStep {
        val item = current ?: return startNextPending(null)
        resultsById[item.id] = Phase.PASSED
        return startNextPending(item)
    }

    /** 레벨 테스트용: 해당 장의 모든 항목을 SKIPPED 처리(이미 PASSED 인 것은 유지). 현재 항목이 그 장이면 종료. */
    private var skippedChapter: String? = null

    fun skipAll(chapter: String) {
        skippedChapter = chapter
        items.filter { it.chapter == chapter }.forEach { if (resultsById[it.id] != Phase.PASSED) resultsById[it.id] = Phase.SKIPPED }
        val cur = current
        if (cur != null && cur.chapter == chapter) {
            phase = Phase.SKIPPED
            emit(PracticeStep(cur.id, Phase.SKIPPED, null))
        }
    }

    /** 어떤 장의 진행률(통과 수 / 전체). 리포트·성장 기록(9장)용. */
    fun progressOf(chapter: String): Pair<Int, Int> {
        val all = items.filter { it.chapter == chapter }
        return all.count { resultsById[it.id] == Phase.PASSED } to all.size
    }

    /** 아직 결과가 없는 다음 항목(after 다음부터, 없으면 처음부터). 결과 없는 항목이 하나도 없으면 건너뛴 항목을 2순위로 돌려준다. */
    fun nextPending(after: PracticeItem?): PracticeItem? {
        val startIdx = if (after == null) 0 else items.indexOf(after) + 1
        // 이미 통과한 항목을 건너뛰지 않고 목록 순서대로 바로 다음 항목을 연다.
        //   마지막 항목 뒤에만 아직 안 한 항목(없으면 건너뛴 항목)으로 돌아가고, 그것도 없으면 끝.
        //   끝낸 항목도 건너뛴 항목도 지나치지 않는다(다음·건너뛰기가 어디로 가는지 알 수 있게).
        //   장을 통째로 건너뛴 직후(skipAll)에만 그 장의 남은 항목을 지나 다음 장 첫 항목으로 간다.
        if (after != null) {
            val ch = skippedChapter; skippedChapter = null
            val next = if (ch != null && after.chapter == ch) items.drop(startIdx).firstOrNull { it.chapter != ch } else items.getOrNull(startIdx)
            if (next != null) return next
        }
        val rotated = items.drop(startIdx) + items.take(startIdx)
        return rotated.firstOrNull { it.id !in resultsById }
            ?: rotated.firstOrNull { resultsById[it.id] == Phase.SKIPPED && it != after }
    }

    /** 건너뛴 항목 수(선택 화면 "건너뛴 N개 다시 하기"). */
    val skippedCount: Int get() = resultsById.count { it.value == Phase.SKIPPED }

    /** 목록의 바로 앞 항목(결과와 무관)을 처음부터. 첫 항목이면 현재 항목을 처음부터. */
    private fun startPrevious(current: PracticeItem): PracticeStep {
        val idx = items.indexOf(current)
        val target = if (idx > 0) items[idx - 1] else current
        return start(target.id)
    }

    private fun startNextPending(after: PracticeItem?): PracticeStep {
        val next = nextPending(after) ?: return emit(PracticeStep(after?.id, phase, PHRASE_ALL_DONE))
        return start(next.id)
    }

    private fun manualOf(check: CheckKind): CheckKind.Manual? = when (check) {
        is CheckKind.Manual -> check
        is CheckKind.Either -> check.manual
        else -> null
    }

    private fun go(item: PracticeItem, to: Phase, phrase: PhraseId): PracticeStep {
        phase = to
        return emit(PracticeStep(item.id, to, phrase, progress = statefulCheck?.progress()))
    }

    private fun finish(item: PracticeItem, result: Phase, auto: Boolean = false): PracticeStep {
        phase = result
        // 이미 통과한 항목을 다시 열어 건너뛰어도 ✓ 는 지우지 않는다(skipAll 과 같은 규칙).
        if (!(result == Phase.SKIPPED && resultsById[item.id] == Phase.PASSED)) resultsById[item.id] = result
        lastAutoPassed = auto
        val phrase = if (result == Phase.PASSED) item.passPhrase else PHRASE_SKIPPED
        return emit(PracticeStep(item.id, result, phrase, autoPassed = auto))
    }

    private fun emit(step: PracticeStep): PracticeStep {
        lastStep = step
        listener?.onStep(step)
        return step
    }

    companion object {
        /** "이 연습은 P(주차) 상태에서만 할 수 있어요. 기어를 P에 두고 다시 시작하세요." */
        val PHRASE_NEED_P = PhraseId("practice_need_p")
        /** "아직 감지되지 않았어요. 다시 해보거나 '건너뛰기'라고 말하세요." */
        val PHRASE_NOT_DETECTED = PhraseId("practice_not_detected")
        /** "이 항목은 건너뛸게요." */
        val PHRASE_SKIPPED = PhraseId("practice_skipped")
        /** "이 장의 연습을 모두 마쳤어요." */
        val PHRASE_ALL_DONE = PhraseId("practice_all_done")
        /** 내부 오류용: 없는 항목 ID. */
        val PHRASE_NO_SUCH_ITEM = PhraseId("practice_no_such_item")
    }
}
