// 평행 주차 공식: 직각 주차(ParkingProcedure)와 같은 슬로우 모드로 돌린다
package kr.drivecoach.core.parking

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.tts.PhraseId

/** 실제 차 주차 안내의 종류. */
enum class ParkingKind { PERPENDICULAR, PARALLEL }

/**
 * 슬로우 모드 한 벌이 쓰는 공식: 단계표·탈출 절차·고정 멘트.
 * 직각 주차는 [ParkingProcedure.FORMULA], 평행 주차는 [ParallelParkingProcedure.FORMULA].
 */
data class ParkingFormula(
    val kind: ParkingKind,
    val steps: List<ParkingStep>,
    val recoveries: Map<Tangle, RecoveryProcedure>,
    val recoveryChoicePrompt: PhraseId,
    val restartPhrase: PhraseId,
    val donePhrase: PhraseId,
    val selfEvalPrompt: PhraseId,
    /** 자기평가 '앞뒤 위치' 힌트(직각: 뒤 선, 평행: 앞뒤 차 간격). 나머지 힌트는 [SelfEvalHints] 공통. */
    val frontBackHint: PhraseId,
) {
    /** 누른 항목 집합에서 힌트 하나(각도 > 간격 > 앞뒤). */
    fun hintFor(issues: Set<SelfEvalItem>): PhraseId = when {
        SelfEvalItem.ANGLE in issues -> SelfEvalHints.ANGLE
        SelfEvalItem.CURB_GAP in issues -> SelfEvalHints.CURB_GAP
        SelfEvalItem.FRONT_BACK in issues -> frontBackHint
        else -> SelfEvalHints.ALL_GOOD
    }
}

/**
 * 평행 주차(길가, 운전석 기준 오른쪽 연석) 공식. 여러 출처를 하나로 정리한 것.
 *
 * 흐름(14단계): D → 빈 칸 앞차 옆에 0.5~1 m 띄워 나란히, 뒤 범퍼를 맞춰 정지 → R → 똑바로 1 m쯤 후진(앞차 뒤 끝이 내 뒷바퀴쯤) →
 *       핸들 오른쪽 끝 → 후진해 약 45°(왼쪽 미러에 뒤차 앞면이 다 보임) → 핸들 가운데 → 똑바로 후진(오른쪽 미러가 앞차 뒤 범퍼를 지남) →
 *       핸들 왼쪽 끝 → 후진해 길과 나란히 → 핸들 가운데 → 앞뒤 간격 맞추기 → P → 주차브레이크.
 * 기준점은 운전자가 미러·창으로 보는 것이며 앱은 판정하지 않는다(직각 주차와 같다). 영상(3D)은 아반떼 CN8 회전 반경으로 같은 순서를 계산해 그렸다.
 */
object ParallelParkingProcedure {
    private val FULL = ParkingProcedure.FULL_LOCK_DEG

    val STEPS: List<ParkingStep> = listOf(
        ParkingStep(0, PhraseId("parking_step_gear_d"), null, "par01_approach", StepExpectation.GearIs(Gear.D)),
        ParkingStep(1, PhraseId("parking_par_step_approach"), PhraseId("parking_par_ref_approach"), "par01_approach", StepExpectation.StoppedThenConfirm),
        ParkingStep(2, PhraseId("parking_step_gear_r"), null, "par02_gear_r_back", StepExpectation.GearIs(Gear.R)),
        ParkingStep(3, PhraseId("parking_par_step_back1"), PhraseId("parking_par_ref_back1"), "par02_gear_r_back", StepExpectation.StoppedThenConfirm),
        ParkingStep(4, PhraseId("parking_step_steer_right_full"), null, "par03_steer_right", StepExpectation.SteeringAtLeast(FULL, SteeringDirection.RIGHT)),
        ParkingStep(5, PhraseId("parking_par_step_reverse45"), PhraseId("parking_par_ref_reverse45"), "par04_reverse45", StepExpectation.StoppedThenConfirm),
        ParkingStep(6, PhraseId("parking_step_center_wheel"), null, "par05_center_back", StepExpectation.SteeringCentered()),
        ParkingStep(7, PhraseId("parking_par_step_back2"), PhraseId("parking_par_ref_back2"), "par05_center_back", StepExpectation.StoppedThenConfirm),
        ParkingStep(8, PhraseId("parking_step_steer_left_full"), null, "par06_steer_left_in", StepExpectation.SteeringAtLeast(FULL, SteeringDirection.LEFT)),
        ParkingStep(9, PhraseId("parking_par_step_reverse_in"), PhraseId("parking_par_ref_reverse_in"), "par06_steer_left_in", StepExpectation.StoppedThenConfirm),
        ParkingStep(10, PhraseId("parking_step_center_wheel"), null, "par07_adjust_p", StepExpectation.SteeringCentered()),
        ParkingStep(11, PhraseId("parking_par_step_adjust"), PhraseId("parking_par_ref_adjust"), "par07_adjust_p", StepExpectation.StoppedThenConfirm),
        ParkingStep(12, PhraseId("parking_slow_step_p"), null, "par07_adjust_p", StepExpectation.GearIs(Gear.P)),
        ParkingStep(13, PhraseId("parking_finish_epb"), null, "par07_adjust_p", StepExpectation.ParkingBrakeOn),
    )

    /** 핸들 왼쪽 끝으로 넣는 단계(탈출 '비스듬함' 뒤에 돌아오는 곳). */
    const val REVERSE_IN_STEP_INDEX = 9

    /** 상황별 탈출. 연석에 붙음 = 앞으로 빼며 나란히, 비스듬함 = 앞뒤로 나눠 넣기, 연석과 멂 = 빠져나와 다시. */
    val RECOVERIES: Map<Tangle, RecoveryProcedure> = mapOf(
        Tangle.TOO_CLOSE to RecoveryProcedure(
            Tangle.TOO_CLOSE, PhraseId("parking_par_escape_curb"),
            listOf(
                ParkingStep(0, PhraseId("parking_recover_gear_d"), null, "rec_par_close", StepExpectation.GearIs(Gear.D)),
                ParkingStep(1, PhraseId("parking_step_steer_right_full"), null, "rec_par_close", StepExpectation.SteeringAtLeast(FULL, SteeringDirection.RIGHT)),
                ParkingStep(2, PhraseId("parking_par_recover_curb_forward"), PhraseId("parking_par_ref_reverse_in"), "rec_par_close", StepExpectation.StoppedThenConfirm),
            ),
            resumeAtStepIndex = 10, // → 핸들 가운데 → 앞뒤 맞추기 → P
        ),
        Tangle.ANGLED to RecoveryProcedure(
            Tangle.ANGLED, PhraseId("parking_par_escape_angle"),
            listOf(
                ParkingStep(0, PhraseId("parking_recover_gear_d"), null, "rec_par_angled", StepExpectation.GearIs(Gear.D)),
                ParkingStep(1, PhraseId("parking_step_steer_right_full"), null, "rec_par_angled", StepExpectation.SteeringAtLeast(FULL, SteeringDirection.RIGHT)),
                ParkingStep(2, PhraseId("parking_par_recover_angle_forward"), null, "rec_par_angled", StepExpectation.StoppedThenConfirm),
                ParkingStep(3, PhraseId("parking_step_gear_r"), null, "rec_par_angled", StepExpectation.GearIs(Gear.R)),
                ParkingStep(4, PhraseId("parking_step_steer_left_full"), null, "rec_par_angled", StepExpectation.SteeringAtLeast(FULL, SteeringDirection.LEFT)),
            ),
            resumeAtStepIndex = REVERSE_IN_STEP_INDEX, // → 후진해 길과 나란히
        ),
        Tangle.TOO_FAR to RecoveryProcedure(
            Tangle.TOO_FAR, PhraseId("parking_par_escape_far"),
            listOf(
                ParkingStep(0, PhraseId("parking_recover_gear_d"), null, "rec_par_far", StepExpectation.GearIs(Gear.D)),
                ParkingStep(1, PhraseId("parking_par_recover_far_out"), PhraseId("parking_par_ref_approach"), "rec_par_far", StepExpectation.StoppedThenConfirm),
            ),
            resumeAtStepIndex = 2, // → R → 똑바로 조금 후진 …
        ),
    )

    val FORMULA = ParkingFormula(
        kind = ParkingKind.PARALLEL,
        steps = STEPS,
        recoveries = RECOVERIES,
        recoveryChoicePrompt = PhraseId("parking_par_recover_choose"),
        restartPhrase = PhraseId("parking_par_restart"),
        donePhrase = PhraseId("parking_par_done_selfcheck"),
        selfEvalPrompt = PhraseId("parking_par_selfeval_ask"),
        frontBackHint = PhraseId("parking_par_hint_front_back"),
    )
}
