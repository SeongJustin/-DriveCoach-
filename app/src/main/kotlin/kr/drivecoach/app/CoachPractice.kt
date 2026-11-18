// 차 기능 익히기(2장) + 도로 법규 동작
package kr.drivecoach.app

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.law.RuleTopic
import kr.drivecoach.core.law.LawSets
import kr.drivecoach.core.practice.Chapter2Items
import kr.drivecoach.core.practice.CheckKind
import kr.drivecoach.core.practice.Phase
import kr.drivecoach.core.practice.PracticeEngine
import kr.drivecoach.core.practice.PracticeItem
import kr.drivecoach.core.practice.PracticeStep
import kr.drivecoach.core.practice.UserInput
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.Phrases

/** 서비스가 저장해 둔 이전 결과를 넣는다(앱을 껐다 켜도 ✓ 유지). */
fun CoachController.restorePracticeResults(results: Map<String, Phase>) {
    practiceResults.putAll(results)
    _practiceDone.value = practiceResults.filterValues { it == Phase.PASSED }.keys
}

/** 연습 선택 화면용 항목 목록(차량 옵션 반영). */
fun CoachController.practiceItemList(): List<PracticeItem> = Chapter2Items.itemsFor(Chapter2Items.AVANTE_CN8_MODERN)

/**
 * "차 기능 익히기" 시작. [fromItemId] 가 있으면 그 항목부터(선택 연습), 없으면 첫 미완료 항목부터(순서대로).
 * 이미 진행 중이면 그 항목으로 건너뛴다. P 가 아니면 엔진이 practice_need_p 를 말한다. 이전 세션의 통과/건너뜀 결과는 [practiceResults] 로 이어진다.
 */
fun CoachController.startPractice(fromItemId: String? = null) {
    coachingPaused = false   // 운전자가 새 활동을 열면 "세울래요"로 멈춘 코칭을 푼다
    val running = practice
    if (running?.current != null) {
        if (fromItemId != null && fromItemId != running.current?.id) { interruptCoaching(); running.start(fromItemId) }
        return
    }
    endActivity() // 하위 활동은 한 번에 하나
    practiceItems = practiceItemList()
    val engine = PracticeEngine(practiceItems, { step -> onPracticeStep(step) }, initialResults = practiceResults)
    engine.autoDetect = false   // 설명을 보고 '다음'으로 넘어간다
    engine.onState(currentState())
    practice = engine
    lastStepWasNotDetected = false
    if (fromItemId != null) engine.start(fromItemId) else engine.onInput(UserInput.NEXT) // P 가 아니면 need_p
    if (engine.current == null) practice = null
    refreshSubMode()
}

/** 연습 종료(주행 시작, 메뉴 닫기). 결과는 남겨 두어 다시 열면 이어진다. */
fun CoachController.endPractice() {
    practice?.let { practiceResults.putAll(it.results) }
    _practiceDone.value = practiceResults.filterValues { it == Phase.PASSED }.keys
    onPracticeResultsChanged?.invoke(practiceResults.toMap())
    practice = null
    _practiceUi.value = null
    awaiting.cancel()
    refreshSubMode()
}


/**
 * "도로 법규" 열기. [topicId] 가 없으면 첫 주제. 주제 글을 화면에 띄우고 같은 문장을 읽어 준다.
 * [country] 를 주면 그 나라 법규로 바꾼다. 첫 화면에서 새로 열면 한국부터.
 */
fun CoachController.startLaw(topicId: String? = null, country: String? = null) {
    coachingPaused = false   // 운전자가 새 활동을 열면 "세울래요"로 멈춘 코칭을 푼다
    val fresh = _lawUi.value == null
    if (fresh) endActivity()
    if (country != null) lawSet = LawSets.of(country) else if (fresh) lawSet = LawSets.korea
    val topic = topicId?.let { lawSet.byId(it) } ?: lawSet.topics.first()
    showLaw(topic)
    refreshSubMode()
}

/** 다음/이전 주제. 끝에서 "다음" 은 닫는다. */
fun CoachController.lawNext() {
    val cur = _lawUi.value ?: return
    val next = lawSet.topics.getOrNull(cur.index + 1)
    if (next == null) {
        // 마지막 주제: 다 봤다는 한마디 후 닫기
        val allSeen = lawSet.topics.all { lawSet.seenKey(it.id) in lessonsDone.value }
        endLaw()
        request(if (allSeen) Phrases.lawAllDone.id else Phrases.lawEndSome.id, force = true)
    } else showLaw(next)
}

fun CoachController.lawPrev() {
    val cur = _lawUi.value ?: return
    showLaw(lawSet.topics[maxOf(0, cur.index - 1)])
}

fun CoachController.endLaw() {
    if (_lawUi.value == null) return
    _lawUi.value = null
    clearSubModeQueue()
    interruptCoaching()
    refreshSubMode()
}

internal fun CoachController.showLaw(topic: RuleTopic) {
    val text = Phrases.find(topic.phraseId)?.text.orEmpty()
    _lawUi.value = LawUi(topic.id, lawSet.indexOf(topic.id), lawSet.topics.size, topic.title, text, topic.heading, topic.basis, lawSet.country)
    markLessonDone(lawSet.seenKey(topic.id))   // 본 주제 표시(주제 버튼 ✓), 앱을 껐다 켜도 유지. 한국은 예전 키 그대로
    clearSubModeQueue()
    interruptCoaching()
    request(topic.phraseId, force = true)
}

internal fun CoachController.onPracticeInput(engine: PracticeEngine, command: Command) {
    // 자동으로 다음 항목에 막 들어간 순간의 '다음/확인' 은 무시한다. 넘어가는 순간 누른 것이 새 항목에 먹히면 설명을 통째로 건너뛴다.
    if ((command == Command.NEXT || command == Command.CONFIRM) && clock() - practiceAutoAdvancedAt < AUTO_ADVANCE_GUARD_MS) return
    // 통과/건너뜀 멘트가 아직 나가는 중에 누른 '다음/확인' = "빨리 다음으로". 멘트를 끊으면 onDone 이 자동 진행을 하므로 여기서 끝.
    if ((engine.phase == Phase.PASSED || engine.phase == Phase.SKIPPED) && command != Command.AGAIN && speaking != null) {
        interruptCoaching()
        return
    }
    // 운전자가 입력했으면 지금 나가던 설명은 끊고 새 단계 멘트를 바로 들려준다(위험 피드백은 끊지 않음).
    interruptCoaching()
    if ((command == Command.NEXT || command == Command.CONFIRM) && engine.phase == Phase.EXPLAIN) {
        // '다음' = 바로 다음 항목. 본 항목은 ✓ 로 적는다
        engine.advance()
        practiceResults.putAll(engine.results)
        _practiceDone.value = practiceResults.filterValues { it == Phase.PASSED }.keys
        onPracticeResultsChanged?.invoke(practiceResults.toMap())
        return
    }
    val input = when (command) {
        // 화면 버튼은 건너뛰기/다음/이전. "다음" 은 확인과 같다: 설명→해보기, 해보기(수동)→통과, 확인→통과, 통과→다음 항목.
        // 자동 항목의 해보기 단계에서 "다음" 을 두 번 연속 누르면(앞서 '아직 감지 안 됨' 안내를 들었으면) 건너뛰기로 본다.
        Command.CONFIRM, Command.NEXT -> UserInput.CONFIRM   // 건너뛰기는 따로 있는 버튼으로
        Command.AGAIN -> UserInput.AGAIN
        Command.SKIP -> UserInput.SKIP
        Command.PREV -> UserInput.PREV
        else -> return
    }
    engine.onInput(input)
}

internal fun CoachController.onPracticeStep(step: PracticeStep) {
    val engine = practice ?: return
    val item = step.itemId?.let { id -> practiceItems.firstOrNull { it.id == id } }
    lastStepWasNotDetected = step.phrase == PracticeEngine.PHRASE_NOT_DETECTED
    if (step.phase == Phase.PASSED || step.phase == Phase.SKIPPED) {
        practiceResults.putAll(engine.results)
        _practiceDone.value = practiceResults.filterValues { it == Phase.PASSED }.keys
        onPracticeResultsChanged?.invoke(practiceResults.toMap())
    }
    val text = step.phrase?.let { Phrases.find(it)?.text }.orEmpty()
    val stepPhase = step.phase  // 다른 모듈의 public 프로퍼티라 스마트 캐스트가 안 되므로 지역 변수로 받는다
    if (item != null && stepPhase != null) {
        // '아직 감지 안 됨' 안내는 지시문을 덮지 않고 진행 줄에 보여 준다.
        val notDetected = step.phrase == PracticeEngine.PHRASE_NOT_DETECTED
        val state = currentState()
        _practiceUi.value = PracticeUi(
            itemId = item.id, chapter = item.chapter, title = item.title, phase = stepPhase,
            text = if (notDetected) _practiceUi.value?.text.orEmpty().ifEmpty { text } else text.ifEmpty { _practiceUi.value?.text.orEmpty() },
            // 설명 단계에 진행 줄이 맥락 없이 붙지 않고, 미감지 안내가 무엇을 기다리는지 말하게:
            //   센서 진행 줄은 해보기 단계에서만 "지금 할 것: …" 으로, 미감지 안내에는 그 줄을 덧붙인다
            progress = run {
                val now = step.progress?.takeIf { it.isNotBlank() }?.let { "지금 할 것: $it" }
                when {
                    notDetected -> listOfNotNull(text, now).joinToString(" ")
                    stepPhase == Phase.TRY -> now
                    else -> null
                }
            },
            detectable = item.detectable && signalAvailableFor(item, state),
            index = practiceItems.indexOf(item) + 1, total = practiceItems.size,
            allDone = step.phrase == PracticeEngine.PHRASE_ALL_DONE,
            passedBefore = practiceResults[item.id] == Phase.PASSED && stepPhase != Phase.PASSED,
        )
    } else if (step.phrase == PracticeEngine.PHRASE_ALL_DONE) {
        _practiceUi.value = _practiceUi.value?.copy(text = text, allDone = true)
    }
    val phrase = step.phrase ?: return
    val now = clock()
    if (phrase == PracticeEngine.PHRASE_ALL_DONE) {
        // 이 장을 다 했다: 멘트가 끝나면 연습을 닫는다(자동 진행하면 같은 멘트가 무한 반복된다).
        awaiting.cancel()
        val skipped = engine.results.count { it.value == Phase.SKIPPED }
        val doneId = if (skipped > 0) Phrases.practiceAllDoneSkipped.id else phrase
        request(doneId, force = true, onDone = { if (practice === engine) endPractice() })
        return
    }
    when (step.phase) {
        // 통과/건너뜀 멘트가 끝나면 다음 항목으로 자동 진행. 통과 화면은 최소 [PASS_HOLD_MS] 보여 준다.
        Phase.PASSED, Phase.SKIPPED -> {
            awaiting.cancel()
            // 변속 연습은 D 나 N 에서 통과한다. P 가 아니면 다음 항목이 열리지 않아
            //   "P로 변속한 상태에서 진행합니다"는 몇 초마다 되풀이하지 않고 한 번만 말하며, P 가 되면 넘어간다.
            if (phrase == PracticeEngine.PHRASE_NEED_P) {
                request(phrase, force = true)
                scope.launch(Dispatchers.Main) {
                    while (practice === engine && engine.phase == step.phase && currentState().gear != Gear.P) delay(300)
                    if (practice === engine && engine.phase == step.phase) { practiceAutoAdvancedAt = clock(); engine.onInput(UserInput.NEXT) }
                }
                return
            }
            val startedAt = now
            request(phrase, force = true, onDone = {
                val remain = PASS_HOLD_MS - (clock() - startedAt)
                val go = {
                    if (practice === engine && engine.phase == step.phase) {
                        // 목록 끝까지 간 뒤 앞쪽의 안 한 항목으로 돌아가면 한마디 하고 넘어간다(진행 막대가 줄어드는 까닭).
                        val cur = engine.current
                        val nxt = engine.nextPending(cur)
                        val wrapped = cur != null && nxt != null && practiceItems.indexOf(nxt) <= practiceItems.indexOf(cur)
                        val proceed = {
                            if (practice === engine && engine.phase == step.phase) {
                                practiceAutoAdvancedAt = clock()
                                engine.onInput(UserInput.NEXT)
                                if (wrapped) _practiceUi.value = _practiceUi.value?.copy(progress = Phrases.practiceWrapAround.text)
                            }
                        }
                        if (wrapped) request(Phrases.practiceWrapAround.id, force = true, onDone = proceed) else proceed()
                    }
                }
                if (remain > 0) scope.launch(Dispatchers.Main) { delay(remain); go() } else go()
            })
        }
        // 확인 요청: 운전자 입력을 기다린다(무응답이면 정차 중 1회 재안내). 대기 시계는 안내가 끝난 뒤부터.
        Phase.CONFIRM -> {
            request(phrase, force = true, onDone = { if (practice === engine) awaiting.open(item?.id ?: "practice", clock(), accepts = setOf(Command.CONFIRM, Command.NEXT)) })
        }
        Phase.TRY -> {
            // 감지 불가(수동) 항목은 해보기 단계부터 '다음' 을 기다린다. 자동 항목은 센서가 통과시킨다.
            val manual = item != null && !item.detectable
            request(phrase, force = true, onDone = { if (manual && practice === engine) awaiting.open(item!!.id, clock(), accepts = setOf(Command.CONFIRM, Command.NEXT)) })
        }
        else -> request(phrase, force = true)
    }
}

/**
 * Either(자동+수동) 항목인데 이 차에서 해당 신호가 안 오면(null) 화면에 '자동 확인' 을 붙이지 않는다.
 * 신호가 있는지는 VehicleState 의 해당 필드가 null 이 아닌지로 본다.
 */
internal fun CoachController.signalAvailableFor(item: PracticeItem, state: VehicleState): Boolean {
    val id = item.id
    return when {
        // 좌석은 "위치가 바뀌었다" 이벤트만 있어(값 자체는 null 이 아님) 신호 유무를 여기서 알 수 없다 → 자동 확인으로 둔다
        id.startsWith("2-1-seat") -> true
        id == "2-2-seatbelt" -> state.seatBeltWarning != null
        id == "2-3-ignition" -> state.ignitionOn != null
        id == "2-3-epb-release" || id == "2-3-epb-apply" -> state.parkingBrakeOn != null
        id == "2-5-hazard" -> state.hazardOn != null
        id == "2-7-window" -> state.windowsClosed != null
        id == "2-7-door-lock" -> state.doorsLocked != null
        id == "2-7-hvac" -> state.hvacOn != null
        else -> true
    }
}

/** 기어를 P 밖으로 옮겨야 통과하는 항목(기어 순서·급발진 드릴 = Stateful 판정). */
internal fun CoachController.isGearDrill(item: PracticeItem?): Boolean {
    val c = item?.check ?: return false
    return c is CheckKind.Stateful || (c is CheckKind.Either && c.auto is CheckKind.Stateful)
}
