// 직각 주차 슬로우 모드(4-5) 동작
// CoachController 를 기능별 파일로 나눴다. 상태는 CoachController 클래스에, 동작은 이 파일의 확장 함수에 있다.
package kr.drivecoach.app

import android.util.Log
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.parking.ParallelParkingProcedure
import kr.drivecoach.core.parking.ParkingKind
import kr.drivecoach.core.parking.ParkingProcedure
import kr.drivecoach.core.parking.ParkingSlowMode
import kr.drivecoach.core.parking.SelfEvalItem
import kr.drivecoach.core.parking.SlowModeOutput
import kr.drivecoach.core.parking.SlowModePhase
import kr.drivecoach.core.parking.StepExpectation
import kr.drivecoach.core.parking.SteeringDirection
import kr.drivecoach.core.parking.Tangle
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.Phrases
import kotlin.math.abs

/**
 * "직각 주차" 열기. core ParkingSlowMode(공식 11단계 + 탈출 3종 + 자기평가)를 만들고, 먼저 시작 전 설명 화면을 보여 준다
 * (무엇을 하는 연습인지 모른 채 첫 지시가 'D' 였음). '시작'([parkingBegin]) 을 누르면 1단계부터.
 * 기어가 R/D 로 바뀌어 주행 모드가 돼도 닫지 않는다(그게 공식의 흐름). 그동안 발화 모드는 PRACTICE 라 PARKING 멘트가 나간다.
 */
fun CoachController.startParking(kind: ParkingKind = ParkingKind.PERPENDICULAR) {
    coachingPaused = false   // 운전자가 새 활동을 열면 "세울래요"로 멈춘 코칭을 푼다
    if (slowMode != null) return
    endActivity()
    // (실제 차 주차는 직각·평행 둘 다) 공식 한 벌을 골라 같은 슬로우 모드로 돌린다
    val formula = if (kind == ParkingKind.PARALLEL) ParallelParkingProcedure.FORMULA else ParkingProcedure.FORMULA
    val intro = if (kind == ParkingKind.PARALLEL) Phrases.parkingParIntro else Phrases.parkingIntro
    val m = ParkingSlowMode(output = { out -> onParkingOutput(out) }, formula = formula, idleRpm = { idleRpm.idleRpm })
    slowMode = m
    parkingIllustration = formula.steps.first().illustration
    parkingText = intro.text
    parkingNotice = null
    parkingDone = false
    refreshSubMode()
    m.onState(currentState()) // 현재 신호를 먼저 알려 줘야 "이미 만족한 단계는 설명 없이 통과" 가 동작
    publishParkingUi(currentState())
    requestQueued(intro.id)
}

/** 시작 전 설명 화면의 '시작'(또는 확인/다음): 슬로우 모드 1단계부터. */
fun CoachController.parkingBegin() {
    val m = slowMode ?: return
    if (m.phase != SlowModePhase.IDLE) return
    clearSubModeQueue()
    interruptCoaching()
    // ("브레이크를 밟은 채 시작"인데 안 밟아도 시작된다) 막지는 않고(신호가 없는 차도 있다) 밟지 않았으면 먼저 알린다
    val noBrake = currentState().brakePressed == false
    showParkingNotice(if (noBrake) "브레이크 페달이 밟혀 있지 않습니다. 밟은 채로 따라 하십시오. " + Phrases.parkingSlowStart.text else Phrases.parkingSlowStart.text)
    requestQueued(Phrases.parkingSlowStart.id)
    m.start(clock())
    publishParkingUi(currentState())
}

internal fun CoachController.showParkingNotice(text: String) {
    parkingNotice = text
    parkingNoticeAt = clock()
}

/** 직각 주차를 닫는다. */
fun CoachController.endParking() {
    if (slowMode == null) return
    slowMode = null
    _parkingUi.value = null
    awaiting.cancel()
    clearSubModeQueue()
    refreshSubMode()
}

/** "꼬였어요": 탈출법 3가지 중 고르게 한다(화면 버튼). */
fun CoachController.parkingHelp() {
    val m = slowMode ?: return
    clearSubModeQueue()
    interruptCoaching()
    m.help(clock())
    publishParkingUi(currentState())
}

/** 탈출법 고르기에서 "하던 단계로". */
fun CoachController.parkingCancelHelp() {
    val m = slowMode ?: return
    clearSubModeQueue()
    interruptCoaching()
    m.cancelHelp(clock())
    publishParkingUi(currentState())
}

/** 탈출법 선택(화면 버튼 3개 중 하나). */
fun CoachController.parkingChooseRecovery(tangle: Tangle) {
    val m = slowMode ?: return
    clearSubModeQueue()
    interruptCoaching()
    m.chooseRecovery(tangle, clock())
    publishParkingUi(currentState())
}

/** "처음부터": 공식 0단계로. */
fun CoachController.parkingRestart() {
    val m = slowMode ?: return
    clearSubModeQueue()
    interruptCoaching()
    m.restart(clock())
    publishParkingUi(currentState())
}

/** 완주 후 자기평가. 운전자가 "안 맞았다"고 누른 항목을 받아 힌트 한마디를 말하고 화면에도 적는다. */
fun CoachController.parkingSelfEvaluate(issues: Set<SelfEvalItem>) {
    val m = slowMode ?: return
    val hint = m.selfEvaluate(issues) ?: return
    parkingDone = true
    markLessonDone(if (m.formula.kind == ParkingKind.PARALLEL) "parking_parallel" else "parking")
    parkingText = Phrases.find(hint)?.text ?: parkingText
    clearSubModeQueue()
    interruptCoaching()
    requestQueued(hint)
    publishParkingUi(currentState())
}

internal fun CoachController.onParkingInput(m: ParkingSlowMode, command: Command, now: Long) {
    clearSubModeQueue()
    interruptCoaching()
    when (command) {
        // 확인/다음: 이 단계를 통과(센서 단계는 신호가 맞아야 통과). 건너뛰기: 센서가 없는 차용으로 무조건 넘긴다.
        Command.CONFIRM, Command.NEXT -> {
            if (m.phase == SlowModePhase.IDLE) { parkingBegin(); return }
            if (m.phase == SlowModePhase.DONE) { endParking(); return }
            m.confirm(now)
        }
        Command.SKIP -> {
            if (m.phase == SlowModePhase.IDLE) { parkingBegin(); return }
            if (m.phase == SlowModePhase.DONE) { endParking(); return }
            m.skip(now)
        }
        Command.AGAIN -> m.again()
        else -> {}
    }
    publishParkingUi(currentState())
}

/** 슬로우 모드 출력 → 멘트는 request(), 일러스트·문장은 화면 상태로. */
internal fun CoachController.onParkingOutput(out: SlowModeOutput) {
    val now = clock()
    when (out) {
        is SlowModeOutput.Say -> {
            out.illustration?.let { parkingIllustration = it }
            val m = slowMode
            val cur = m?.currentStep
            if (m != null && (m.phase == SlowModePhase.STEP || m.phase == SlowModePhase.RECOVERY_STEP)) {
                // 단계 중: 큰 문장은 publishParkingUi 가 지금 단계 지시를 바로 쓴다(멘트가 늦게 나와도 체크 항목과 어긋나지 않게).
                // 지시·기준점이 아닌 멘트(아직 감지 안 됨·먼저 정지·처음부터·탈출 제목)는 그 아래 보조 안내로 잠깐 보여 준다.
                if (out.phrase != cur?.instructionPhrase && out.phrase != cur?.referencePointPhrase) {
                    Phrases.find(out.phrase)?.let { showParkingNotice(it.text) }
                }
            } else {
                // 선택·평가·완료 화면: 그 멘트가 실제로 나갈 때 큰 문장을 바꾼다(여러 문장이 한꺼번에 나오면 마지막 것만 보이던 문제).
                Phrases.find(out.phrase)?.let { ph -> screenTextWhenSpoken[out.phrase] = { parkingText = ph.text; publishParkingUi(currentState()) } }
            }
            // 확인으로만 넘어가는 단계면 입력을 기다린다(무응답 시 정차 중 1회 재안내, 휠 버튼 짧게 = 확인).
            val step = slowMode?.currentStep
            if (step != null && (step.expected is StepExpectation.StoppedThenConfirm || step.expected is StepExpectation.ManualConfirm)) {
                awaiting.open("parking_${step.index}", now, accepts = setOf(Command.CONFIRM, Command.NEXT))
            } else {
                awaiting.cancel()
            }
            requestQueued(out.phrase)
        }
        is SlowModeOutput.Warn -> request(out.phrase)
        is SlowModeOutput.AskRecoveryChoice -> {
            awaiting.cancel()
            Phrases.find(out.prompt)?.let { parkingText = it.text }
            requestQueued(out.prompt)
        }
        is SlowModeOutput.Completed -> {
            awaiting.cancel()
            Log.i(COACH_TAG, "주차 완주: ${out.records.size}단계 기록")
        }
    }
}

internal fun CoachController.publishParkingUi(state: VehicleState) {
    val m = slowMode ?: return
    val step = m.currentStep
    val recovery = m.currentRecovery
    val total = when (m.phase) {
        SlowModePhase.RECOVERY_STEP -> recovery?.steps?.size ?: m.formula.steps.size
        else -> m.formula.steps.size
    }
    val expected = step?.expected
    val angle = state.steeringAngleDeg
    val speed = state.speedKmh ?: 0.0
    val checks = mutableListOf<ParkingCheck>()
    when (expected) {
        is StepExpectation.GearIs -> checks += ParkingCheck("기어 ${expected.gear.name}", state.gear == expected.gear)
        is StepExpectation.SteeringAtLeast -> checks += ParkingCheck(
            if (expected.direction == SteeringDirection.LEFT) "스티어링 휠 왼쪽 끝까지" else "스티어링 휠 오른쪽 끝까지",
            angle != null && (if (expected.direction == SteeringDirection.LEFT) angle <= -expected.deg else angle >= expected.deg),
        )
        is StepExpectation.SteeringCentered -> checks += ParkingCheck("스티어링 휠 가운데", angle != null && abs(angle) <= expected.tolDeg)
        is StepExpectation.StoppedThenConfirm -> checks += ParkingCheck("차가 멈춤", speed <= STOPPED_SPEED_KMH)
        StepExpectation.ParkingBrakeOn -> checks += ParkingCheck("주차 브레이크", state.parkingBrakeOn == true)
        else -> {}
    }
    if (state.gear == Gear.R || state.gear == Gear.D) checks += ParkingCheck("속도 5 이하", speed <= PARKING_SPEED_OK_KMH)
    // (상태 고르기 화면에 직전 단계 영상이 남아 고르는 내용과 맞지 않았다) 고르기 화면은 그림 없이
    val illustration = if (m.phase == SlowModePhase.RECOVERY_CHOICE) "" else step?.illustration ?: parkingIllustration
    val inStep = step != null && (m.phase == SlowModePhase.STEP || m.phase == SlowModePhase.RECOVERY_STEP)
    _parkingUi.value = ParkingUi(
        phase = m.phase,
        stepIndex = step?.index ?: (if (m.phase == SlowModePhase.SELF_EVAL || m.phase == SlowModePhase.DONE) m.formula.steps.lastIndex else 0),
        stepTotal = total,
        text = (if (inStep) Phrases.find(step!!.instructionPhrase)?.text else null) ?: parkingText,
        detail = if (inStep) step!!.referencePointPhrase?.let { Phrases.find(it)?.text } else null,
        notice = parkingNotice?.takeIf { clock() - parkingNoticeAt <= PARKING_NOTICE_MS },
        illustration = illustration,
        recovery = recovery?.tangle,
        recoveryOptions = if (m.phase == SlowModePhase.RECOVERY_CHOICE) Tangle.values().toList() else emptyList(),
        checks = checks,
        needsConfirm = expected is StepExpectation.StoppedThenConfirm || expected is StepExpectation.ManualConfirm,
        attempt = m.attempt,
        done = parkingDone || m.phase == SlowModePhase.DONE,
        kind = m.formula.kind,
    )
}


// ── 주차 배우기: 따라 하기(센서 확인, 자기평가) 없이 단계 영상과 설명을 한 장씩 본다. 자리 바로잡기 세 가지는 맨 뒤에 이어서 보여 준다.

/** 배우기 한 장. [tangle] 이 있으면 자리 바로잡기 장이다. */
data class ParkLearnPage(val step: kr.drivecoach.core.parking.ParkingStep, val tangle: Tangle?, val no: Int, val of: Int)

data class ParkLearnUi(val kind: ParkingKind, val index: Int, val total: Int, val tangle: Tangle?, val no: Int, val of: Int, val text: String, val detail: String?, val illustration: String)

fun CoachController.startParkLearn(kind: ParkingKind) {
    coachingPaused = false
    endActivity()
    val formula = if (kind == ParkingKind.PARALLEL) ParallelParkingProcedure.FORMULA else ParkingProcedure.FORMULA
    val pages = mutableListOf<ParkLearnPage>()
    formula.steps.forEachIndexed { i, s -> pages += ParkLearnPage(s, null, i + 1, formula.steps.size) }
    for (t in Tangle.values()) formula.recoveries[t]?.let { r -> r.steps.forEachIndexed { i, s -> pages += ParkLearnPage(s, t, i + 1, r.steps.size) } }
    parkLearnPages = pages
    showParkLearn(kind, 0)
    refreshSubMode()
}

/** 다음(+1), 이전(-1), 다시 듣기(0). 마지막 장에서 다음이면 닫는다. */
fun CoachController.parkLearnGo(delta: Int) {
    val cur = _parkLearnUi.value ?: return
    val next = cur.index + delta
    if (next >= parkLearnPages.size) {
        markLessonDone(if (cur.kind == ParkingKind.PARALLEL) "parking_parallel" else "parking")
        endParkLearn(); return
    }
    showParkLearn(cur.kind, maxOf(0, next))
}

fun CoachController.endParkLearn() {
    if (_parkLearnUi.value == null) return
    _parkLearnUi.value = null
    clearSubModeQueue()
    interruptCoaching()
    refreshSubMode()
}

internal fun CoachController.showParkLearn(kind: ParkingKind, index: Int) {
    val pg = parkLearnPages.getOrNull(index) ?: return
    _parkLearnUi.value = ParkLearnUi(
        kind, index, parkLearnPages.size, pg.tangle, pg.no, pg.of,
        text = Phrases.find(pg.step.instructionPhrase)?.text.orEmpty(),
        detail = pg.step.referencePointPhrase?.let { Phrases.find(it)?.text },
        illustration = pg.step.illustration,
    )
    clearSubModeQueue()
    interruptCoaching()
    request(pg.step.instructionPhrase, force = true)
}
