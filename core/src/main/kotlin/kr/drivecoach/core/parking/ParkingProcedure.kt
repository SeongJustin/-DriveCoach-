// 직각 주차 공식 데이터: 사전 학습/슬로우 모드의 단계표
package kr.drivecoach.core.parking

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.tts.PhraseId

/** 핸들을 돌리는 방향. 조향각 부호와 대응: RIGHT = 양수(시계방향), LEFT = 음수. */
enum class SteeringDirection { LEFT, RIGHT }

/**
 * 한 단계를 "통과"로 볼 조건. 센서로 알 수 있는 것(조향각·기어·속도)만 자동으로 보고,
 * 주차선·옆 차·미러에 보이는 것은 절대 센서로 판정하지 않고 운전자 확인으로만 넘긴다.
 */
sealed class StepExpectation {
    /** 핸들이 [direction] 쪽으로 [deg]도 이상 돌아가면 통과(예: 끝까지 = 400도 이상). */
    data class SteeringAtLeast(val deg: Double, val direction: SteeringDirection) : StepExpectation()

    /** 기어가 [gear] 가 되면 통과. */
    data class GearIs(val gear: Gear) : StepExpectation()

    /** 속도가 [kmh] 미만이면 통과(천천히 움직이는 중에도 통과 가능). */
    data class SpeedBelow(val kmh: Double) : StepExpectation()

    /** 핸들이 가운데(±[tolDeg]) 로 돌아오면 통과(조향각으로 판정 가능). */
    data class SteeringCentered(val tolDeg: Double = 20.0) : StepExpectation()

    /** 차가 멈춘 상태에서 운전자가 "확인"해야 통과. 움직이는 중의 확인은 받지 않는다. */
    object StoppedThenConfirm : StepExpectation() { override fun toString() = "StoppedThenConfirm" }

    /** 센서로 알 수 없는 동작. 운전자 "확인/했어요" 로만 통과. */
    object ManualConfirm : StepExpectation() { override fun toString() = "ManualConfirm" }

    /** 주차브레이크가 걸리면 통과. 신호가 없는 차는 '건너뛰기'. */
    object ParkingBrakeOn : StepExpectation() { override fun toString() = "ParkingBrakeOn" }
}

/**
 * 직각 주차 공식의 한 단계.
 * @param index 0부터. 공식 단계와 복구 단계가 각각 독립된 번호를 가진다.
 * @param instructionPhrase 한 동작 한 문장. 기준점이 있으면 이 문장 안에 포함되어 있다.
 * @param referencePointPhrase 기준점만 따로 다시 듣고 싶을 때("다시") 쓰는 문장. 기준점이 없는 단계는 null.
 * @param illustration 아래 SVG 파일명. 정차(P) 상태의 사전 학습 화면에서만 보여 준다.
 * @param expected 통과 조건.
 */
data class ParkingStep(
    val index: Int,
    val instructionPhrase: PhraseId,
    val referencePointPhrase: PhraseId?,
    val illustration: String,
    val expected: StepExpectation,
)

/** 꼬였을 때 운전자가 고르는 상황 3가지. 앱은 꼬임을 감지하지 못하므로 선택지는 항상 운전자에게 보여 준다. */
enum class Tangle {
    /** 옆 차(또는 오른쪽 선)에 너무 붙었다. */
    TOO_CLOSE,
    /** 차체가 주차선과 비스듬하다(한쪽 미러에만 선이 보인다). */
    ANGLED,
    /** 뒤쪽이 칸에 못 들어가고 선 바깥으로 너무 멀다. */
    TOO_FAR,
}

/**
 * 꼬임 상황별 탈출(복구) 절차.
 * @param resumeAtStepIndex 복구 단계를 다 마친 뒤 돌아갈 공식 단계 번호.
 */
data class RecoveryProcedure(
    val tangle: Tangle,
    val titlePhrase: PhraseId,
    val steps: List<ParkingStep>,
    val resumeAtStepIndex: Int,
)

/**
 * 후진 직각 주차(운전석 기준 우측 공간) 공식. 출처 4곳을 하나로 정리한 것.
 *
 * 흐름(11단계, 처음 'D 넣기'·끝 '주차브레이크' 포함):
 *       D 넣기 → 접근·정지 → 핸들 왼쪽 끝 → 전진 45° → R → 핸들 오른쪽 끝 → 후진(미러에 선이 나란히) →
 *       핸들 풀기 → 직진 후진 → P → 주차브레이크.
 * 각 단계의 "기준점"은 운전자가 사이드미러로 보는 것이며, 앱은 기준점 도달을 판정하지 않는다.
 */
object ParkingProcedure {
    /** "핸들 끝까지" 로 인정하는 최소 조향각(도). 아반떼 CN8 최대 480도의 약 85%. */
    const val FULL_LOCK_DEG = 400.0

    /** 슬로우 모드 후진 허용 속도(km/h). 크리핑 속도보다 조금 위. */
    const val REVERSE_SPEED_LIMIT_KMH = 6.0

    /** 공식 단계. 번호는 0부터. */
    val STEPS: List<ParkingStep> = listOf(
        ParkingStep(0, PhraseId("parking_step_gear_d"), null,
            "step01_approach.svg", StepExpectation.GearIs(Gear.D)),
        ParkingStep(1, PhraseId("parking_step_approach"), PhraseId("parking_ref_approach"),
            "step01_approach.svg", StepExpectation.StoppedThenConfirm),
        ParkingStep(2, PhraseId("parking_step_steer_left_full"), null,
            "step02_steer_left_forward45.svg", StepExpectation.SteeringAtLeast(FULL_LOCK_DEG, SteeringDirection.LEFT)),
        ParkingStep(3, PhraseId("parking_step_forward45"), PhraseId("parking_ref_forward45"),
            "step02_steer_left_forward45.svg", StepExpectation.StoppedThenConfirm),
        ParkingStep(4, PhraseId("parking_step_gear_r"), null,
            "step03_gear_r_steer_right.svg", StepExpectation.GearIs(Gear.R)),
        ParkingStep(5, PhraseId("parking_step_steer_right_full"), null,
            "step03_gear_r_steer_right.svg", StepExpectation.SteeringAtLeast(FULL_LOCK_DEG, SteeringDirection.RIGHT)),
        ParkingStep(6, PhraseId("parking_step_reverse_in"), PhraseId("parking_ref_reverse_in"),
            "step04_reverse_in_mirror.svg", StepExpectation.StoppedThenConfirm),
        ParkingStep(7, PhraseId("parking_step_center_wheel"), null,
            "step05_center_wheel.svg", StepExpectation.SteeringCentered()),
        ParkingStep(8, PhraseId("parking_step_straight_back"), PhraseId("parking_ref_straight_back"),
            "step06_straight_back.svg", StepExpectation.StoppedThenConfirm),
        ParkingStep(9, PhraseId("parking_slow_step_p"), null,
            "step07_gear_p_done.svg", StepExpectation.GearIs(Gear.P)),
        ParkingStep(10, PhraseId("parking_finish_epb"), null,
            "step07_gear_p_done.svg", StepExpectation.ParkingBrakeOn),
    )

    /** 후진이 시작되는 공식 단계(복구 후 돌아오는 기본 지점). */
    const val REVERSE_IN_STEP_INDEX = 6

    /** 상황별 탈출 절차. 모두 "앞으로 빼서 다시 잡는다" 가 기본이며, 핸들 방향만 다르다. */
    // 자리 바로잡기마다 위에서 본 설명 영상을 보여 준다
    val RECOVERIES: Map<Tangle, RecoveryProcedure> = mapOf(
        Tangle.TOO_CLOSE to RecoveryProcedure(
            Tangle.TOO_CLOSE, PhraseId("parking_escape_too_close"),
            listOf(
                ParkingStep(0, PhraseId("parking_step_steer_left_full"), null,
                    "rec_perp_close", StepExpectation.SteeringAtLeast(FULL_LOCK_DEG, SteeringDirection.LEFT)),
                ParkingStep(1, PhraseId("parking_recover_gear_d"), null,
                    "rec_perp_close", StepExpectation.GearIs(Gear.D)),
                ParkingStep(2, PhraseId("parking_recover_too_close_forward"), PhraseId("parking_recover_too_close_ref"),
                    "rec_perp_close", StepExpectation.StoppedThenConfirm),
            ),
            resumeAtStepIndex = 4, // → R → 핸들 오른쪽 끝 → 후진
        ),
        Tangle.ANGLED to RecoveryProcedure(
            Tangle.ANGLED, PhraseId("parking_escape_angle"),
            listOf(
                ParkingStep(0, PhraseId("parking_recover_gear_d"), null,
                    "rec_perp_angled", StepExpectation.GearIs(Gear.D)),
                ParkingStep(1, PhraseId("parking_recover_angled_forward_align"), PhraseId("parking_ref_reverse_in"),
                    "rec_perp_angled", StepExpectation.StoppedThenConfirm),
                ParkingStep(2, PhraseId("parking_step_center_wheel"), null,
                    "rec_perp_angled", StepExpectation.SteeringCentered()),
                ParkingStep(3, PhraseId("parking_step_gear_r"), null,
                    "rec_perp_angled", StepExpectation.GearIs(Gear.R)),
            ),
            resumeAtStepIndex = 8, // → 직진 후진 → P → 주차브레이크
        ),
        Tangle.TOO_FAR to RecoveryProcedure(
            Tangle.TOO_FAR, PhraseId("parking_escape_too_far"),
            listOf(
                ParkingStep(0, PhraseId("parking_recover_gear_d"), null,
                    "rec_perp_far", StepExpectation.GearIs(Gear.D)),
                ParkingStep(1, PhraseId("parking_step_steer_left_full"), null,
                    "rec_perp_far", StepExpectation.SteeringAtLeast(FULL_LOCK_DEG, SteeringDirection.LEFT)),
                ParkingStep(2, PhraseId("parking_recover_too_far_forward"), PhraseId("parking_recover_too_far_ref"),
                    "rec_perp_far", StepExpectation.StoppedThenConfirm),
            ),
            resumeAtStepIndex = 4, // → R → 핸들 오른쪽 끝 → 후진
        ),
    )

    /** 꼬임 선택지를 운전자에게 읽어 줄 때 쓰는 문장(3가지 중 고르세요). */
    val RECOVERY_CHOICE_PROMPT = PhraseId("parking_recover_choose")

    /** 공식을 처음부터 다시 시작할 때. */
    val RESTART_PHRASE = PhraseId("parking_escape_restart")

    /** 움직이는 중에 "확인"이 들어왔을 때: 먼저 멈추라는 안내. */
    val STOP_FIRST_PHRASE = PhraseId("parking_stop_first")

    /** 슬로우 모드가 쓰는 직각 주차 공식 한 벌(평행 주차는 [ParallelParkingProcedure.FORMULA]). */
    val FORMULA: ParkingFormula by lazy {
        ParkingFormula(
            kind = ParkingKind.PERPENDICULAR,
            steps = STEPS,
            recoveries = RECOVERIES,
            recoveryChoicePrompt = RECOVERY_CHOICE_PROMPT,
            restartPhrase = RESTART_PHRASE,
            donePhrase = DONE_PHRASE,
            selfEvalPrompt = SELF_EVAL_PROMPT,
            frontBackHint = SelfEvalHints.FRONT_BACK,
        )
    }

    /** 후진 중 RPM 상승(엑셀) 경고. 1회. */
    val REVERSE_ACCEL_WARNING = PhraseId("danger_reverse_rpm")

    /** 후진 중 속도 초과 경고. 1회. */
    val REVERSE_SPEED_WARNING = PhraseId("danger_reverse_speed")

    /** 공식 완료 후 자기평가 요청. */
    val SELF_EVAL_PROMPT = PhraseId("parking_selfeval_ask")
    /** 완주 직후 "P 완료. 내려서 …" 한마디(자기평가 요청 앞에). */
    val DONE_PHRASE = PhraseId("parking_done_selfcheck")
    /** 센서 단계를 '확인' 으로 넘기려 할 때: 아직 감지 안 됨(기능 연습과 같은 문장). */
    val NOT_DETECTED_PHRASE = PhraseId("practice_not_detected")
}

/** 자기평가 3버튼. 운전자가 "안 맞았다"고 느낀 항목을 누른다(복수 가능). */
enum class SelfEvalItem {
    /** 연석(또는 옆 선)과의 간격이 안 맞았다. */
    CURB_GAP,
    /** 차가 비스듬하다. */
    ANGLE,
    /** 앞뒤 위치가 안 맞았다(너무 들어감/덜 들어감). */
    FRONT_BACK,
}

/** 자기평가 결과 → 다음 시도 힌트 문장. 여러 개를 눌렀으면 각도 > 간격 > 앞뒤 순으로 하나만 준다(한 번만, 짧게). */
object SelfEvalHints {
    val ALL_GOOD = PhraseId("parking_selfeval_good")
    val ANGLE = PhraseId("parking_selfeval_angle")
    val CURB_GAP = PhraseId("parking_hint_curb_gap")
    val FRONT_BACK = PhraseId("parking_hint_front_back")

    /** 누른 항목 집합에서 힌트 하나를 고른다. */
    fun hintFor(issues: Set<SelfEvalItem>): PhraseId = when {
        SelfEvalItem.ANGLE in issues -> ANGLE
        SelfEvalItem.CURB_GAP in issues -> CURB_GAP
        SelfEvalItem.FRONT_BACK in issues -> FRONT_BACK
        else -> ALL_GOOD
    }
}
