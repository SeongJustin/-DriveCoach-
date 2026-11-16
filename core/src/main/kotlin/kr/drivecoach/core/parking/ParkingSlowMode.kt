// 직각 주차 슬로우 모드 상태 머신 — 후진 경고, 실행/확인
package kr.drivecoach.core.parking

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId

/** 슬로우 모드가 바깥(TTS·화면)으로 내보내는 것. 문장은 전부 [PhraseId] 로만 나간다. */
sealed class SlowModeOutput {
    /** 지금 단계의 한 문장 안내. [illustration] 은 정차(P) 상태의 화면에서만 보여 준다. */
    data class Say(val phrase: PhraseId, val illustration: String? = null) : SlowModeOutput()

    /** 위험 피드백(짧게, 1회). */
    data class Warn(val phrase: PhraseId) : SlowModeOutput()

    /** "꼬였어요/도움" → 3가지 상황 중 고르게 한다. */
    data class AskRecoveryChoice(val prompt: PhraseId, val options: List<Pair<Tangle, PhraseId>>) : SlowModeOutput()

    /** 공식 완주. 단계별 소요 시간 기록을 함께 준다(리포트용). 이어서 자기평가를 받는다. */
    data class Completed(val records: List<StepRecord>) : SlowModeOutput()
}

/** 출력 콜백. app 모듈이 TTS/화면으로 연결한다. */
fun interface SlowModeOutputListener {
    fun onOutput(output: SlowModeOutput)
}

/** 한 단계를 어떻게 통과했는지. */
enum class PassedBy { SENSOR, CONFIRM, RESTART, HELP }

/** 단계별 소요 시간 기록. [tangle] 이 null 이면 공식 단계, 아니면 그 복구 절차의 단계. */
data class StepRecord(
    val attempt: Int,
    val tangle: Tangle?,
    val stepIndex: Int,
    val startedMs: Long,
    val endedMs: Long,
    val passedBy: PassedBy,
) {
    val durationMs: Long get() = endedMs - startedMs
}

/** 상태 머신의 큰 단계. */
enum class SlowModePhase { IDLE, STEP, RECOVERY_CHOICE, RECOVERY_STEP, SELF_EVAL, DONE }

/**
 * 직각 주차 슬로우 모드. 공식 한 단계에 한 문장(기준점 포함)을 내보내고,
 * - 운전자 "확인/했어요" 또는 조향각·기어·속도가 기대치를 만족하면 다음 단계로,
 * - 후진(R) 중 속도 초과면 경고 1회(액셀/RPM 경고는 DangerRules),
 * - "처음부터" 로 1단계 복귀, "꼬였어요/도움" 으로 탈출법 3가지 선택,
 * - 완주 후 자기평가 3버튼 결과로 다음 시도 힌트를 돌려준다.
 *
 * 주차선 안에 들어갔는지·옆 차와의 거리는 **절대 판정하지 않는다**. 미러에 무엇이 보이는지는 운전자만 안다.
 *
 * @param idleRpm 공회전 RPM 공급자. app 에서는 safety.IdleRpmEstimator 의 값을 넘긴다.
 * @param rewarnIntervalMs 같은 경고를 다시 낼 수 있는 최소 간격. 기본값이면 한 시도 안에서 사실상 1회.
 */
class ParkingSlowMode(
    private val output: SlowModeOutputListener,
    /** 직각([ParkingProcedure.FORMULA], 기본) 또는 평행([ParallelParkingProcedure.FORMULA]) 공식. */
    val formula: ParkingFormula = ParkingProcedure.FORMULA,
    private val idleRpm: () -> Double = { DEFAULT_IDLE_RPM },
    private val rewarnIntervalMs: Long = DEFAULT_REWARN_INTERVAL_MS,
) : StateListener {

    companion object {
        const val DEFAULT_IDLE_RPM = 800.0
        /** 공회전 대비 이만큼 오르면 엑셀을 밟은 것으로 본다. */
        const val RPM_RISE_DELTA = 300.0
        /** 이 속도 이하를 "정차"로 본다. */
        const val STOPPED_SPEED_KMH = 0.5
        /** 들어갈 때 이미 맞아 있던 단계는 이만큼 지나야 센서로 넘긴다(안내를 볼 시간) */
        const val MIN_SENSOR_DWELL_MS = 2500L
        /** 같은 경고 재발화 금지 간격(ms). 한 시도는 보통 이보다 짧으므로 "1회"가 된다. */
        const val DEFAULT_REWARN_INTERVAL_MS = 120_000L
    }

    /** 현재 큰 단계. */
    var phase: SlowModePhase = SlowModePhase.IDLE
        private set

    /** 몇 번째 시도인지(처음부터 → +1). */
    var attempt: Int = 1
        private set

    /** 현재 진행 중인 단계(공식 또는 복구). IDLE/선택/평가 중이면 null. */
    val currentStep: ParkingStep? get() = stepsNow?.getOrNull(cursor)

    /** 현재 복구 절차(복구 중일 때만). */
    var currentRecovery: RecoveryProcedure? = null
        private set

    /** 지금까지의 단계별 기록. */
    val records: List<StepRecord> get() = recordsMut.toList()

    private val recordsMut = mutableListOf<StepRecord>()
    private var cursor = 0
    /** 탈출법 고르기 전에 하던 자리(큰 단계, 단계 번호, 탈출 절차). "하던 단계로" 가 쓴다. */
    private var helpFrom: Triple<SlowModePhase, Int, RecoveryProcedure?>? = null
    private var stepStartedMs = 0L
    private var satisfiedAtEntry = false
    private var lastState: VehicleState? = null
    private var lastAccelWarnMs: Long? = null
    private var lastSpeedWarnMs: Long? = null

    private val stepsNow: List<ParkingStep>?
        get() = when (phase) {
            SlowModePhase.STEP -> formula.steps
            SlowModePhase.RECOVERY_STEP -> currentRecovery?.steps
            else -> null
        }

    // ---------- 운전자 입력 ----------

    /** 슬로우 모드 시작. 1단계 문장을 내보낸다. */
    fun start(nowMs: Long) {
        attempt = 1
        recordsMut.clear()
        resetWarnings()
        enterFormulaStep(0, nowMs)
    }

    /** "확인/했어요": 현재 단계를 통과시킨다. 정지 후 확인 단계에서 아직 움직이는 중이면 먼저 멈추라고 한다. */
    fun confirm(nowMs: Long) {
        val step = currentStep ?: return
        if (step.expected is StepExpectation.StoppedThenConfirm && !isStopped()) {
            output.onOutput(SlowModeOutput.Say(ParkingProcedure.STOP_FIRST_PHRASE))
            return
        }
        // 센서 단계(핸들 끝까지·가운데·기어)는 '확인' 으로 통과시키지 않는다 — 신호가 아직 안 맞으면 미감지 안내만. 건너뛰려면 [skip].
        val sensorStep = step.expected is StepExpectation.SteeringAtLeast || step.expected is StepExpectation.SteeringCentered ||
            step.expected is StepExpectation.GearIs || step.expected is StepExpectation.ParkingBrakeOn
        if (sensorStep && lastState?.let { sensorSatisfied(step.expected, it) } != true) {
            output.onOutput(SlowModeOutput.Say(ParkingProcedure.NOT_DETECTED_PHRASE))
            return
        }
        advance(nowMs, PassedBy.CONFIRM)
    }

    /** "건너뛰기": 센서가 없는 차(에뮬레이터)를 위해 어떤 단계든 넘긴다. */
    fun skip(nowMs: Long) {
        currentStep ?: return
        advance(nowMs, PassedBy.CONFIRM)
    }

    /** "다시": 현재 단계 문장을 한 번 더 들려준다. 기준점이 있으면 기준점 문장을 쓴다. */
    fun again() {
        val step = currentStep ?: return
        output.onOutput(SlowModeOutput.Say(step.referencePointPhrase ?: step.instructionPhrase, step.illustration))
    }

    /** "처음부터": 기록을 닫고 1단계로 돌아간다. 경고 1회 카운트도 새로 센다. */
    fun restart(nowMs: Long) {
        if (phase == SlowModePhase.IDLE) { start(nowMs); return }
        closeCurrentStep(nowMs, PassedBy.RESTART)
        attempt += 1
        currentRecovery = null
        resetWarnings()
        output.onOutput(SlowModeOutput.Say(formula.restartPhrase))
        enterFormulaStep(0, nowMs)
    }

    /** "꼬였어요/도움": 현재 단계를 닫고 탈출법 3가지 중 고르게 한다. */
    fun help(nowMs: Long) {
        if (phase != SlowModePhase.STEP && phase != SlowModePhase.RECOVERY_STEP) return
        closeCurrentStep(nowMs, PassedBy.HELP)
        helpFrom = Triple(phase, cursor, currentRecovery)
        currentRecovery = null
        phase = SlowModePhase.RECOVERY_CHOICE
        output.onOutput(
            SlowModeOutput.AskRecoveryChoice(
                formula.recoveryChoicePrompt,
                Tangle.values().map { it to formula.recoveries.getValue(it).titlePhrase },
            ),
        )
    }

    /** 탈출법 고르기에서 "하던 단계로": 고르기 전에 하던 단계(공식 또는 탈출)로 돌아간다. */
    fun cancelHelp(nowMs: Long) {
        if (phase != SlowModePhase.RECOVERY_CHOICE) return
        val (ph, idx, rec) = helpFrom ?: Triple(SlowModePhase.STEP, 0, null)
        helpFrom = null
        currentRecovery = rec
        phase = ph
        enterStep(idx, nowMs)
    }

    /** 탈출법 선택. 해당 복구 절차의 1단계로 들어간다. */
    fun chooseRecovery(tangle: Tangle, nowMs: Long) {
        if (phase != SlowModePhase.RECOVERY_CHOICE) return
        val recovery = formula.recoveries.getValue(tangle)
        currentRecovery = recovery
        phase = SlowModePhase.RECOVERY_STEP
        output.onOutput(SlowModeOutput.Say(recovery.titlePhrase))
        enterStep(0, nowMs)
    }

    /**
     * 완주 후 자기평가. 운전자가 "안 맞았다"고 누른 항목을 받아 다음 시도 힌트 문장 ID 를 돌려준다.
     * 완주 전에 부르면 힌트 없이 [SelfEvalHints.ALL_GOOD] 이 아니라 null 을 준다.
     */
    fun selfEvaluate(issues: Set<SelfEvalItem>): PhraseId? {
        if (phase != SlowModePhase.SELF_EVAL) return null
        phase = SlowModePhase.DONE
        return formula.hintFor(issues)
    }

    // ---------- 차량 신호 ----------

    override fun onState(state: VehicleState) {
        lastState = state
        if (phase != SlowModePhase.STEP && phase != SlowModePhase.RECOVERY_STEP) return
        checkReverseWarnings(state)
        val step = currentStep ?: return
        if (satisfiedAtEntry && state.timestampMs - stepStartedMs < MIN_SENSOR_DWELL_MS) return
        if (sensorSatisfied(step.expected, state)) advance(state.timestampMs, PassedBy.SENSOR)
    }

    /** 센서로 알 수 있는 기대치만 평가한다. 확인형 기대치는 항상 false. */
    private fun sensorSatisfied(expected: StepExpectation, s: VehicleState): Boolean = when (expected) {
        is StepExpectation.SteeringAtLeast -> {
            val a = s.steeringAngleDeg
            a != null && when (expected.direction) {
                SteeringDirection.RIGHT -> a >= expected.deg
                SteeringDirection.LEFT -> a <= -expected.deg
            }
        }
        is StepExpectation.GearIs -> s.gear == expected.gear
        is StepExpectation.SteeringCentered -> s.steeringAngleDeg?.let { kotlin.math.abs(it) <= expected.tolDeg } ?: false
        StepExpectation.ParkingBrakeOn -> s.parkingBrakeOn == true
        is StepExpectation.SpeedBelow -> s.speedKmh?.let { it < expected.kmh } ?: false
        StepExpectation.StoppedThenConfirm, StepExpectation.ManualConfirm -> false
    }

    /** 후진(R) 중 속도 초과를 1회 경고한다. 액셀(RPM 상승) 경고는 DangerRules 가 담당(같은 멘트가 두 곳에서 나가던 것 정리). */
    private fun checkReverseWarnings(s: VehicleState) {
        if (s.gear != Gear.R) return
        val v = s.speedKmh
        if (v != null && v > ParkingProcedure.REVERSE_SPEED_LIMIT_KMH && canWarn(lastSpeedWarnMs, s.timestampMs)) {
            lastSpeedWarnMs = s.timestampMs
            output.onOutput(SlowModeOutput.Warn(ParkingProcedure.REVERSE_SPEED_WARNING))
        }
    }

    private fun canWarn(last: Long?, nowMs: Long) = last == null || nowMs - last >= rewarnIntervalMs

    private fun resetWarnings() { lastAccelWarnMs = null; lastSpeedWarnMs = null }

    /** 속도를 모르면(null) 멈춘 것으로 본다 — 운전자 확인을 막을 근거가 없기 때문. */
    private fun isStopped(): Boolean = (lastState?.speedKmh ?: 0.0) <= STOPPED_SPEED_KMH

    // ---------- 단계 이동 ----------

    private fun enterFormulaStep(index: Int, nowMs: Long) {
        phase = SlowModePhase.STEP
        enterStep(index, nowMs)
    }

    /** 단계에 들어가며 문장을 내보낸다. 이미 센서 기대치를 만족하고 있으면 설명 없이 바로 통과. */
    private fun enterStep(index: Int, nowMs: Long) {
        cursor = index
        stepStartedMs = nowMs
        val step = currentStep ?: return
        output.onOutput(SlowModeOutput.Say(step.instructionPhrase, step.illustration))
        // (이미 맞는 단계(핸들 가운데·기어 P)는 들어가자마자 넘어가 "단계가 사라졌다") 들어간 직후에는 넘기지 않고,
        //   들어갈 때 이미 맞아 있던 단계는 안내를 볼 시간(MIN_SENSOR_DWELL_MS)이 지난 뒤의 차량 신호로 넘긴다
        val s = lastState
        satisfiedAtEntry = s != null && sensorSatisfied(step.expected, s)
    }

    private fun closeCurrentStep(nowMs: Long, by: PassedBy) {
        val step = currentStep ?: return
        recordsMut += StepRecord(attempt, currentRecovery?.tangle, step.index, stepStartedMs, nowMs, by)
    }

    private fun advance(nowMs: Long, by: PassedBy) {
        val steps = stepsNow ?: return
        closeCurrentStep(nowMs, by)
        val next = cursor + 1
        when (phase) {
            SlowModePhase.RECOVERY_STEP -> {
                if (next < steps.size) enterStep(next, nowMs)
                else {
                    val resume = currentRecovery?.resumeAtStepIndex ?: 0
                    currentRecovery = null
                    enterFormulaStep(resume, nowMs)
                }
            }
            SlowModePhase.STEP -> {
                if (next < steps.size) enterStep(next, nowMs)
                else {
                    phase = SlowModePhase.SELF_EVAL
                    output.onOutput(SlowModeOutput.Completed(records))
                    output.onOutput(SlowModeOutput.Say(formula.donePhrase))
                    output.onOutput(SlowModeOutput.Say(formula.selfEvalPrompt))
                }
            }
            else -> Unit
        }
    }
}
