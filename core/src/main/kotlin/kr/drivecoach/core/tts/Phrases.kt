// 멘트 카탈로그
package kr.drivecoach.core.tts

/** 멘트를 가리키는 ID. 영문 snake_case ("danger_speeding", "guide_turn_right_300m" 등). */
@JvmInline
value class PhraseId(val id: String)

/**
 * 멘트 카테고리. 발화 정책(SpeechPolicy)의 우선순위와 허용 규칙은 이 값으로 결정한다.
 * - BRIEFING: 출발 전 브리핑(5-2), 정차 중에만
 * - GUIDE: 주행 중 다음 동작 안내(5-3, 4-7)
 * - DANGER: 위험 피드백(1장). 즉시, 한 번만, 짧게
 * - PANIC: 공황·긴장 대응(5-3)
 * - WARNING_LAMP: 경고등 3색 대응(2-8, 7장)
 * - PRACTICE: 기능 연습(2장)·감각 익히기(4장), 정차 또는 저속 연습 중
 * - PARKING: 주차 슬로우 모드·탈출법(4-5)
 * - REPORT: 주행 후 리포트(5-4), 정차 후
 * - SYSTEM: 명령 응답, 무응답 재안내 등
 */
enum class PhraseCategory(
    /** 이 카테고리 멘트의 권장 최대 글자 수(공백 포함, 변수 자리는 3글자로 셈). 더 긴 문장은 쪼갠다. */
    val defaultMaxLength: Int,
) {
    BRIEFING(60), GUIDE(40), DANGER(12), PANIC(40), WARNING_LAMP(30), PRACTICE(200), PARKING(28), REPORT(45), SYSTEM(35)
}

/**
 * 멘트 한 개.
 * @param text 사용자에게 들리는 문장. `{이름}` 자리는 [Phrases.render] 로 치환한다.
 * @param maxLengthHint 이 멘트가 넘지 말아야 할 글자 수([nominalLength] 기준). TTS 길이 제어 힌트.
 * @param variant 같은 상황의 변형 표시("tall", "short", "left_handed", "highway", "option_fog" 등). 없으면 기본 멘트.
 */
data class Phrase(
    val id: PhraseId,
    val category: PhraseCategory,
    val text: String,
    val maxLengthHint: Int,
    val variant: String? = null,
) {
    /** 문장 안의 `{이름}` 자리 목록. */
    val placeholders: List<String>
        get() = PLACEHOLDER.findAll(text).map { it.groupValues[1] }.toList()

    /** 치환 후 예상 글자 수. `{이름}` 자리는 보통 값("300", "80")이 들어가므로 3글자로 센다. */
    val nominalLength: Int
        get() = PLACEHOLDER.replace(text, "000").length

    companion object {
        // 닫는 중괄호도 이스케이프: 안드로이드(ICU) 정규식은 홀로 있는 '}' 를 문법 오류로 본다 (JVM 은 허용) — 에뮬레이터 크래시로 확인
        internal val PLACEHOLDER = Regex("\\{([a-z_]+)\\}")
    }
}

/** 2장 연습 항목 하나의 멘트 묶음(설명 → 해보기 → 확인 → 통과). practice 모듈(D)의 ID 규칙과 1:1. */
data class PracticeItemPhrases(val explain: Phrase, val tryIt: Phrase, val pass: Phrase, val confirm: Phrase?)

/**
 * 모든 멘트 카탈로그. 다른 모듈은 사용자에게 들리는 문장을 직접 쓰지 않고 여기 ID로만 참조한다.
 * 원칙: 한 단어 지적 금지, 위험 피드백 10자 안팎 한 문장, 실수 뒤 "괜찮아요" 한마디,
 * 감지 불가 항목의 판단은 운전자에게("안전하면 이동하십시오, 위험하면 이동하지 마십시오").
 *
 * ID 규칙: 영문 snake_case.
 */
object Phrases {

    private val registry = LinkedHashMap<String, Phrase>()
    private val ID_PATTERN = Regex("[a-z0-9]+([_.][a-z0-9]+)*")

    /** 멘트 등록. 같은 ID 중복 등록은 개발 실수이므로 즉시 실패시킨다. */
    private fun p(id: String, category: PhraseCategory, text: String, maxLength: Int = category.defaultMaxLength, variant: String? = null): Phrase {
        require(ID_PATTERN.matches(id)) { "멘트 ID는 영문 snake_case 여야 함: $id" }
        require(!registry.containsKey(id)) { "멘트 ID 중복: $id" }
        val phrase = Phrase(PhraseId(id), category, text, maxLength, variant)
        registry[id] = phrase
        return phrase
    }

    /**
     * 2장 항목 멘트 묶음 등록. ID 는 practice 모듈 규칙 `practice_<항목(하이픈→밑줄)>_explain|try|pass|confirm`.
     * confirm 은 감지 불가(수동 확인) 항목에만 있다.
     */
    private fun item2(itemId: String, explain: String, tryIt: String, pass: String, confirm: String? = null): PracticeItemPhrases {
        val base = "practice_" + itemId.replace('-', '_')
        val c = PhraseCategory.PRACTICE
        return PracticeItemPhrases(
            p("${base}_explain", c, explain), p("${base}_try", c, tryIt), p("${base}_pass", c, pass),
            confirm?.let { p("${base}_confirm", c, it) },
        )
    }

    // ───────────────────────────────── 출발 전 브리핑 (5-2) ─────────────────────────────────
    private val B = PhraseCategory.BRIEFING
    val briefingStart = p("briefing_start", B, "출발 전 30초 점검을 시작합니다.")
    val briefingCheckBelt = p("briefing_check_belt", B, "안전벨트를 확인하십시오. 어깨 부분은 어깨 중앙, 골반 부분은 낮게 매십시오.")
    val briefingCheckBeltMissing = p("briefing_check_belt_missing", B, "안전벨트 미착용 경고등이 켜져 있습니다. 안전벨트를 먼저 매십시오.")
    val briefingCheckMirror = p("briefing_check_mirror", B, "실외 미러와 실내 미러를 확인하십시오. 뒤차가 충분히 보이도록 조절하십시오.")
    val briefingCheckSignalLever = p("briefing_check_signal_lever", B, "방향지시등 레버 위치를 손으로 확인하십시오. 올리면 우회전, 내리면 좌회전입니다.")
    val briefingCheckWiper = p("briefing_check_wiper", B, "와이퍼 레버 위치도 손으로 확인하십시오. 비가 오면 바로 작동할 수 있어야 합니다.")
    val briefingCheckDoneAsk = p("briefing_check_done_ask", B, "점검을 마쳤으면 '다음'을 누르십시오.")
    val briefingWeatherClear = p("briefing_weather_clear", B, "오늘은 맑습니다.")
    val briefingWeatherRain = p("briefing_weather_rain", B, "비 예보가 있습니다. 와이퍼를 작동하고 평소보다 속도를 줄이십시오.")
    val briefingWeatherSnow = p("briefing_weather_snow", B, "눈 예보가 있습니다. 브레이크 페달은 평소보다 일찍, 부드럽게 밟으십시오.")
    val briefingWeatherFog = p("briefing_weather_fog", B, "안개가 있습니다. 전조등을 켜고 앞차와의 거리를 넓히십시오.")
    val briefingSunsetBefore = p("briefing_sunset_before", B, "일몰 시각은 {sunset_time}입니다. 어두워지기 전에 도착합니다.")
    val briefingSunsetAfterDark = p("briefing_sunset_after_dark", B, "도착할 무렵 어두워집니다. 전조등 스위치가 오토 위치에 있는지 확인하십시오.")
    val briefingCourseSummary = p("briefing_course_summary", B, "오늘 코스는 {distance}km입니다. 우회전 {right_turns}번, 좌회전 {left_turns}번, 차로 변경 {lane_changes}번입니다.")
    val briefingCourseNoteQuickLaneChange = p("briefing_course_note_quick_lane_change", B, "우회전 바로 뒤에 좌회전({location})이 있습니다. 우회전을 마치면 바로 왼쪽 방향지시등을 켜십시오.")
    val briefingCourseNoteUnprotectedLeft = p("briefing_course_note_unprotected_left", B, "비보호 좌회전({location})이 있습니다. 마주 오는 차를 확인하고, 차가 있으면 기다리십시오.")
    val briefingCourseNoteMerge = p("briefing_course_note_merge", B, "{location}에 합류 구간이 있습니다. 가속 차로 {accel_length}m에서 시속 {target_speed}km까지 올리고 방향지시등을 켜십시오.", 70)
    val briefingCourseNoteMergeFallback = p("briefing_course_note_merge_fallback", B, "합류하지 못하면 다음 나들목으로 나가면 됩니다.")
    val briefingCourseNoteSchoolZone = p("briefing_course_note_school_zone", B, "{location}은 어린이 보호구역입니다. 시속 30km 이하로 주행하십시오.")
    val briefingStopSpots = p("briefing_stop_spots", B, "중간에 정차할 수 있는 곳은 {spot}입니다.")
    val briefingWeakPointLast = p("briefing_weak_point_last", B, "지난 주행에서 {item}이 부족했습니다. 오늘은 그 부분에 주의하십시오.")
    val briefingQuizSuddenAccel = p("briefing_quiz_sudden_accel", B, "질문입니다. 차가 갑자기 튀어 나가면 무엇부터 해야 합니까?")
    val briefingQuizSuddenAccelAnswer = p("briefing_quiz_sudden_accel_answer", B, "정답입니다. 가속 페달에서 발을 떼고 브레이크를 끝까지, N, 시동 버튼을 길게 누릅니다.")
    val briefingQuizSuddenAccelRemind = p("briefing_quiz_sudden_accel_remind", B, "정답은 가속 페달에서 발 떼기, 브레이크 끝까지, N, 시동 버튼 길게입니다. 꼭 기억하십시오.")
    val briefingQuizWarningLamp = p("briefing_quiz_warning_lamp", B, "빨간색 경고등이 켜지면 어떻게 해야 합니까?")
    val briefingQuizWarningLampAnswer = p("briefing_quiz_warning_lamp_answer", B, "정답입니다. 즉시 안전한 곳에 정차합니다.")
    val briefingQuizWarningLampRemind = p("briefing_quiz_warning_lamp_remind", B, "빨간색은 즉시, 노란색은 가까운 안전한 곳에 정차합니다. 꼭 기억하십시오.")
    val briefingRelax = p("briefing_relax", B, "서두르지 말고 천천히 주행하십시오.")
    val briefingVoiceCommands = p("briefing_voice_commands", B, "주행 중 음성 명령은 '다시', '조용히', '확인', '다음'입니다.")
    val briefingEnd = p("briefing_end", B, "준비되었으면 출발하십시오.")

    // ───────────────────────────────── 주행 중 안내 (5-3, 4-7) ─────────────────────────────────
    private val G = PhraseCategory.GUIDE
    val guideTurnRight300m = p("guide_turn_right_300m", G, "{distance}m 앞 우회전입니다. 방향지시등을 켜고 오른쪽 차로로 가십시오.")
    val guideTurnRightNow = p("guide_turn_right_now", G, "여기서 우회전입니다. 횡단보도의 보행자를 확인하십시오.")
    val guideTurnRightStopFirst = p("guide_turn_right_stop_first", G, "우회전 전에 일시 정지하십시오. 보행자가 없으면 천천히 진행하십시오.")
    val guideTurnLeft300m = p("guide_turn_left_300m", G, "{distance}m 앞 좌회전입니다. 방향지시등을 켜고 왼쪽 차로로 가십시오.")
    val guideTurnLeft100mCheck = p("guide_turn_left_100m_check", G, "방향지시등, 맞은편 차, 보행자를 확인하십시오. 확인 후 '확인'.")
    val guideTurnLeftWait = p("guide_turn_left_wait", G, "확인하지 못했으면 기다리십시오.")
    val guideTurnLeftUnprotected = p("guide_turn_left_unprotected", G, "비보호 좌회전입니다. 마주 오는 차가 없을 때만 진행하십시오.")
    val guideTurnLeftNow = p("guide_turn_left_now", G, "지금 좌회전하십시오.")
    val guideStraight = p("guide_straight", G, "직진입니다.")
    val guideUturn = p("guide_uturn", G, "{distance}m 앞 유턴입니다. 방향지시등을 켜고 1차로로 이동하십시오.")
    val guideRoundabout = p("guide_roundabout", G, "회전교차로입니다. 회전 중인 차가 우선입니다.")
    val guideSchoolZone = p("guide_school_zone", G, "어린이 보호구역입니다. 시속 30km 이하로 주행하십시오.")
    val guideLaneChangeMirrorLeft = p("guide_lane_change_mirror_left", G, "왼쪽 실외 미러를 확인하십시오.")
    val guideLaneChangeMirrorRight = p("guide_lane_change_mirror_right", G, "오른쪽 실외 미러를 확인하십시오.")
    val guideLaneChangeSignal = p("guide_lane_change_signal", G, "방향지시등을 켜십시오.")
    val guideLaneChangeMove = p("guide_lane_change_move", G, "안전하면 이동하십시오. 위험하면 이동하지 마십시오.")
    val guideLaneChangeSkip = p("guide_lane_change_skip", G, "이번 차로 변경은 생략합니다. 다음 기회에 하십시오.")
    val guideLaneChangeDone = p("guide_lane_change_done", G, "차로 변경을 마쳤습니다.")
    val guideMergeSignal = p("guide_merge_signal", G, "방향지시등을 켜십시오.", 12)
    val guideMergeSpeed = p("guide_merge_speed", G, "{target_speed}까지 올리십시오.", 14)
    val guideMergeGo = p("guide_merge_go", G, "뒤 확인 후 진입하십시오.", 14)   // 페르소나 재검증: '보고' 뒤 동작 명시
    val guideMergeDone = p("guide_merge_done", G, "합류를 마쳤습니다.")
    val guideMergeAbort = p("guide_merge_abort", G, "합류하지 못하면 다음 나들목으로 나가면 됩니다.")
    val guideSignalRemind = p("guide_signal_remind", G, "방향지시등에 주의하십시오.")
    val guideSignalRemindReason = p("guide_signal_remind_reason", G, "방향지시등을 먼저 켜십시오. 옆 차가 알 수 있어야 합니다.")   // 페르소나 재검증: '못 봐요' 질책 어조 제거
    val guideSignalPreventive = p("guide_signal_preventive", G, "곧 지난번에 방향지시등을 놓친 지점입니다. 미리 켜 두십시오.")
    val guidePressureCalm = p("guide_pressure_calm", G, "지금 속도를 유지하십시오.")
    val guidePressureYield = p("guide_pressure_yield", G, "{distance}m 앞에서 비켜 줄 수 있습니다. 방향지시등을 켜고 이동하십시오.")
    val guideDepartureDelay = p("guide_departure_delay", G, "준비되면 출발하십시오. 서두르지 않아도 됩니다.")
    val guideArrive = p("guide_arrive", G, "도착했습니다. 정차 후 오늘의 리포트를 안내합니다.")
    val guideCoachingReduced = p("guide_coaching_reduced", G, "주행이 안정적이므로 안내를 줄입니다.")

    // ───────────────────────────────── 위험 피드백 (1장, 10자 안팎 한 문장) ─────────────────────────────────
    private val D = PhraseCategory.DANGER
    val dangerSpeeding = p("danger_speeding", D, "속도를 줄이십시오.")
    val dangerSpeedingHigh = p("danger_speeding_high", D, "크게 감속하십시오.")              // F 요청 · 페르소나 재검증: 평가('빨라요') 대신 동작만
    val dangerHardBrake = p("danger_hard_brake", D, "급제동에 주의하십시오.")
    val dangerHardLaneChange = p("danger_hard_lane_change", D, "차로 변경이 급합니다.")
    val dangerHardLaneChangeNoSignal = p("danger_hard_lane_change_no_signal", D, "방향지시등을 켜십시오.")   // F 요청 · 페르소나 재검증: '천천히요' 어미 정리
    val dangerReverseRpm = p("danger_reverse_rpm", D, "가속 페달을 떼십시오.")
    val dangerReverseSpeed = p("danger_reverse_speed", D, "천천히 후진하십시오.")                  // I 요청
    val dangerAccelAfterShift = p("danger_accel_after_shift", D, "브레이크를 밟으십시오.")
    val dangerReassure = p("danger_reassure", D, "정상 범위입니다.")

    // ───────────────────────────────── 공황 대응 (5-3) ─────────────────────────────────
    private val PN = PhraseCategory.PANIC
    val systemPullOverAck = p("system_pull_over_ack", PN, "안내를 멈춥니다. 비상 경고등을 켜고 오른쪽에 천천히 정차하십시오.")        // C/G 요청
    val systemPullOverHighway = p("system_pull_over_highway", PN, "여기서는 정차할 수 없습니다. 직진하십시오. 가까운 휴게소로 안내합니다.", variant = "highway") // C/G 요청
    val panicPullOver = p("panic_pull_over", PN, "{distance}m 앞 {spot}에 정차 가능합니다. 방향지시등을 켜고 서행하십시오.")
    val panicPullOverNow = p("panic_pull_over_now", PN, "여기입니다. 비상 경고등을 켜고 천천히 정차하십시오.")
    val panicHighwayKeepStraight = p("panic_highway_keep_straight", PN, "그대로 직진을 유지하십시오.", variant = "highway")
    val panicHighwayRestArea = p("panic_highway_rest_area", PN, "{distance}km 앞에 {spot}이 있습니다. 그곳까지 주행하십시오.", variant = "highway")
    val panicMergeKeepGoing = p("panic_merge_keep_going", PN, "지금은 정차할 수 없습니다. 속도를 유지하고 직진하십시오.", variant = "merge")
    val panicStoppedBreath = p("panic_stopped_breath", PN, "정차했습니다. 4초 들이쉬고, 6초 내쉬십시오.")
    val panicStoppedRest = p("panic_stopped_rest", PN, "오늘은 여기서 마쳐도 됩니다. 계속하려면 '다음'을 누르십시오.")
    val panicResume = p("panic_resume", PN, "다시 시작합니다. 천천히 출발하십시오.")
    val panicDetectedAsk = p("panic_detected_ask", PN, "상태를 확인합니다. 쉬려면 버튼을 길게 누르십시오.")   // 페르소나 재검증: '급하게 섰네요' 지적처럼 들림

    // ───────────────────────────────── 경고등 3색 (2-8, 7장) ─────────────────────────────────
    private val W = PhraseCategory.WARNING_LAMP
    val warningLampGreen = p("warning_lamp_green", W, "녹색 표시등입니다. 계속 주행해도 됩니다.")
    val warningLampYellow = p("warning_lamp_yellow", W, "노란색 경고등입니다. 가까운 안전한 곳에 정차하십시오.")
    val warningLampRed = p("warning_lamp_red", W, "빨간색 경고등입니다. 즉시 안전한 곳에 정차하십시오.")
    val warningLampCoachingPaused = p("warning_lamp_coaching_paused", W, "안내를 멈추고 정차 안내만 합니다.")
    val warningLampStoppedNext = p("warning_lamp_stopped_next", W, "정차했습니다. 시동을 끄고 경고등을 확인하십시오.")
    /** 주행 중 실제 점등: "{lamp}이 켜졌어요." 뒤에 아래 warning_action_* 을 잇는다 (warning 모듈 J). */
    val warningLampOn = p("warning_lamp_on", W, "{lamp}이 켜졌습니다.")
    // 경고등별 행동 한 문장. ID 규칙 warning_action_<WarningLamp.id 소문자>, 문장은 WarningLamp.action 과 같아야 한다(테스트로 검사).
    val warningActionSeatBelt = p("warning_action_seat_belt", W, "안전벨트를 먼저 매십시오.", variant = "red")
    val warningActionBrake = p("warning_action_brake", W, "급제동하지 말고 천천히 정차하십시오.", variant = "red")
    val warningActionOilPressure = p("warning_action_oil_pressure", W, "즉시 정차하고 시동을 끄십시오.", variant = "red")
    val warningActionCharging = p("warning_action_charging", W, "안전하게 정차 후 시동을 끄십시오.", variant = "red")
    val warningActionCoolantTempHigh = p("warning_action_coolant_temp_high", W, "과열, 정차 후 시동을 끄십시오.", variant = "red")
    val warningActionFcaActive = p("warning_action_fca_active", W, "즉시 브레이크를 밟으십시오.", variant = "red")
    val warningActionAirbag = p("warning_action_airbag", W, "가까운 정비소에서 점검을 받으십시오.", variant = "yellow")
    val warningActionDoorOpen = p("warning_action_door_open", W, "정차하고 도어를 닫으십시오.", variant = "yellow")
    val warningActionEpbFault = p("warning_action_epb_fault", W, "P로 변속하고 점검을 받으십시오.", variant = "yellow")
    val warningActionAbs = p("warning_action_abs", W, "급제동을 피하고 점검을 받으십시오.", variant = "yellow")
    val warningActionEbd = p("warning_action_ebd", W, "감속하고 즉시 점검을 받으십시오.", variant = "yellow")
    val warningActionMdps = p("warning_action_mdps", W, "조향이 무겁습니다. 점검받으십시오.", variant = "yellow")
    val warningActionMil = p("warning_action_mil", W, "주유구 캡 확인 후 점검받으십시오.", variant = "yellow")
    val warningActionOilLevel = p("warning_action_oil_level", W, "엔진 오일을 보충하십시오.", variant = "yellow")
    val warningActionLowFuel = p("warning_action_low_fuel", W, "가까운 주유소에서 주유하십시오.", variant = "yellow")
    val warningActionTpms = p("warning_action_tpms", W, "정차하고 타이어 상태를 확인하십시오.", variant = "yellow")
    val warningActionMasterWarning = p("warning_action_master_warning", W, "클러스터의 메시지를 확인하십시오.", variant = "yellow")
    val warningActionEscFault = p("warning_action_esc_fault", W, "미끄럼에 주의하고 점검을 받으십시오.", variant = "yellow")
    val warningActionAutoHoldFault = p("warning_action_auto_hold_fault", W, "직접 제동하고 점검을 받으십시오.", variant = "yellow")
    val warningActionIsgFault = p("warning_action_isg_fault", W, "정비소에서 점검을 받으십시오.", variant = "yellow")
    val warningActionExtLampFault = p("warning_action_ext_lamp_fault", W, "전구를 점검하거나 교체하십시오.", variant = "yellow")
    val warningActionLedHeadlampFault = p("warning_action_led_headlamp_fault", W, "전조등 점검을 받으십시오.", variant = "yellow")
    val warningActionFcaFault = p("warning_action_fca_fault", W, "센서를 닦고 점검을 받으십시오.", variant = "yellow")
    val warningActionLkaFault = p("warning_action_lka_fault", W, "카메라를 닦고 점검을 받으십시오.", variant = "yellow")
    val warningActionDaw = p("warning_action_daw", W, "가까운 곳에서 휴식하십시오.", variant = "yellow")
    val warningActionEscActive = p("warning_action_esc_active", W, "미끄럽습니다. 속도를 줄이십시오.", variant = "green")
    val warningActionEscOff = p("warning_action_esc_off", W, "ESC 버튼을 눌러 다시 켜십시오.", variant = "green")
    val warningActionAutoHold = p("warning_action_auto_hold", W, "오토 홀드가 작동 중입니다.", variant = "green")
    val warningActionIsg = p("warning_action_isg", W, "엔진 일시 정지, 정상입니다.", variant = "green")
    val warningActionKey = p("warning_action_key", W, "스마트 키를 차 안에 두십시오.", variant = "green")
    val warningActionTurnSignal = p("warning_action_turn_signal", W, "회전 후 꺼졌는지 확인하십시오.", variant = "green")
    val warningActionHighBeam = p("warning_action_high_beam", W, "마주 오는 차가 있으면 하향등.", variant = "green")
    val warningActionLightOn = p("warning_action_light_on", W, "전조등이 켜져 있습니다.", variant = "green")
    val warningActionHba = p("warning_action_hba", W, "상향등 자동 전환이 작동 중입니다.", variant = "green")
    val warningActionLka = p("warning_action_lka", W, "차로 유지 보조가 작동 중입니다.", variant = "green")
    val warningActionCruise = p("warning_action_cruise", W, "크루즈 작동 중, 차간 거리 확인.", variant = "green")
    val warningActionDriveMode = p("warning_action_drive_mode", W, "주행 모드 표시등입니다.", variant = "green")
    val warningActionIcyRoad = p("warning_action_icy_road", W, "결빙 주의, 천천히 주행하십시오.", variant = "green")
    val warningActionOverSpeed = p("warning_action_over_speed", W, "속도를 줄이십시오.", variant = "green")
    // 경고등 퀴즈 (정차 중, 2-8)
    val warningQuizIntro = p("warning_quiz_intro", PhraseCategory.PRACTICE, "경고등 퀴즈 세 문제입니다.")
    val warningQuizChoiceGreen = p("warning_quiz_choice_green", PhraseCategory.PRACTICE, "계속 주행해도 됩니다.")
    val warningQuizChoiceYellow = p("warning_quiz_choice_yellow", PhraseCategory.PRACTICE, "가까운 안전한 곳에 정차합니다.")
    val warningQuizChoiceRed = p("warning_quiz_choice_red", PhraseCategory.PRACTICE, "즉시 정차합니다.")
    val warningQuizResult = p("warning_quiz_result", PhraseCategory.PRACTICE, "{total}문제 중 {correct}문제를 맞혔습니다.")
    val warningQuizRetryIntro = p("warning_quiz_retry_intro", PhraseCategory.PRACTICE, "틀린 문제만 다시 풉니다.")
    val warningQuizNotParked = p("warning_quiz_not_parked", PhraseCategory.PRACTICE, "퀴즈는 정차 후 P로 변속한 뒤에 진행합니다.")

    // ───────────────────────────────── 기능 연습 공통·엔진 (2장, practice 모듈 D) ─────────────────────────────────
    private val PR = PhraseCategory.PRACTICE
    val practiceNeedP = p("practice_need_p", PR, "이 연습은 정차 후 P로 변속한 상태에서 진행합니다.")
    val practiceNotDetected = p("practice_not_detected", PR, "아직 감지되지 않았습니다. 다시 하거나 '건너뛰기'를 누르십시오.")
    val practiceSkipped = p("practice_skipped", PR, "건너뜁니다.")
    val practiceAllDoneSkipped = p("practice_all_done_skipped", PR, "이 장의 연습을 마쳤습니다. 건너뛴 항목은 목록에서 골라 다시 할 수 있습니다.")
    val practiceAllDone = p("practice_all_done", PR, "이 장의 연습을 모두 마쳤습니다.")
    /** 목록 끝까지 간 뒤 앞쪽의 안 한 항목으로 돌아갈 때. */
    val practiceWrapAround = p("practice_wrap_around", PR, "마지막 항목까지 했습니다. 아직 하지 않은 항목으로 돌아갑니다.")
    val practiceNoSuchItem = p("practice_no_such_item", PR, "해당 항목이 없습니다. 다음 항목으로 넘어갑니다.")
    val practiceAutoPass = p("practice_auto_pass", PR, "이미 마친 항목입니다. 통과합니다.")
    val practiceNext = p("practice_next", PR, "다음 항목으로 넘어갑니다.")

    // ───────────────────────────────── 도로 법규 설명 (law 모듈 S) — 화면 글 = 음성, 길이 제한 170자 ─────────────────────────────────
    // (추천안) 규칙은 도로교통법(시행 2026. 10. 13.) 조문에 근거한 것만 쉬운 말로 옮긴다. 과실 비율·캠페인 요령·해석은 넣지 않는다.
    //   우회전·신호: 시행규칙 별표 2, 법 제25조 제5항, 제27조 제2항 / 비보호 좌회전: 시행규칙 별표 2, 제25조 제2항 / 유턴: 제5조, 제18조 제1항, 제38조 제1항
    //   차로: 제13조 제3항, 제14조 제5항, 제19조 제3항 / 횡단보도: 제27조 제1항 / 어린이 보호구역: 제12조 제1항, 제27조 제7항, 제32조 제8호
    //   회전교차로: 제25조의2, 제38조 제1항 / 긴급자동차: 제29조 제4항·제5항 / 고속도로: 제21조 제1항, 제60조 / 주정차: 제32조, 제33조
    //   기본: 제17조 제3항, 제38조 제1항, 제49조 제1항 제10호, 제50조 제1항
    // 글 = 도로교통법 조문을 줄여 옮긴 것(근거 조문은 TrafficRules.basis)
    private const val LAW_MAX = 170
    val lawRightTurn = p("law_right_turn", PR, "적색 등화에서는 정지선, 횡단보도 및 교차로의 직전에서 정지한 후, 다른 차의 교통을 방해하지 않고 우회전할 수 있다. 횡단하는 보행자의 통행을 방해하면 안 된다.", LAW_MAX)
    val lawUnprotectedLeft = p("law_unprotected_left", PR, "비보호 좌회전 표지가 있는 곳에서는 녹색의 등화에 좌회전할 수 있다. 교차로의 중심 안쪽을 이용하여 서행하면서 좌회전해야 한다. 맞은편에서 직진하는 차(그림의 빨간 화살표)의 진행을 방해하면 안 된다.", LAW_MAX)
    val lawUTurn = p("law_u_turn", PR, "보행자나 다른 차의 정상적인 통행을 방해할 우려가 있는 경우에는 유턴하면 안 된다. 유턴하려면 그 행위가 끝날 때까지 신호를 해야 한다.", LAW_MAX)
    val lawSignalStopline = p("law_signal_stopline", PR, "적색 등화에서는 정지선, 횡단보도 및 교차로의 직전에서 정지해야 한다. 교차로에 정지하게 되어 다른 차의 통행에 방해가 될 우려가 있는 경우에는 그 교차로에 들어가면 안 된다.", LAW_MAX)
    val lawLanes = p("law_lanes", PR, "도로의 중앙(중앙선) 우측 부분을 통행해야 한다. 진로 변경이 금지된 곳에서는 진로를 변경하면 안 된다. 오고 있는 다른 차의 정상적인 통행에 장애를 줄 우려가 있을 때에는 진로를 변경하면 안 된다.", LAW_MAX)
    val lawCrosswalk = p("law_crosswalk", PR, "보행자가 횡단보도를 통행하고 있거나 통행하려고 하는 때에는 횡단보도 앞(정지선이 있으면 그 정지선)에서 일시정지해야 한다. 보행자의 횡단을 방해하거나 위험을 주면 안 된다.", LAW_MAX)
    val lawSchoolZone = p("law_school_zone", PR, "어린이 보호구역의 통행속도는 시속 30킬로미터 이내로 제한된다. 신호기가 없는 횡단보도 앞에서는 보행자의 횡단 여부와 관계없이 일시정지해야 한다. 어린이 보호구역에서는 정차와 주차가 금지된다.", LAW_MAX)
    val lawRoundabout = p("law_roundabout", PR, "회전교차로에서는 반시계방향으로 통행해야 한다. 진입하려는 경우에는 서행하거나 일시정지해야 하며, 이미 진행하고 있는 다른 차에 진로를 양보해야 한다. 진입하거나 진출하는 경우에는 방향지시기로 신호를 해야 한다.", LAW_MAX)
    val lawEmergency = p("law_emergency", PR, "교차로나 그 부근에서 긴급자동차가 접근하는 경우에는 교차로를 피하여 일시정지해야 한다. 그 밖의 곳에서는 긴급자동차가 우선통행할 수 있도록 진로를 양보해야 한다.", LAW_MAX)
    val lawHighway = p("law_highway", PR, "고속도로에서 다른 차를 앞지르려면 앞차의 좌측으로 통행해야 하고, 방향지시기, 등화 또는 경음기를 사용하여 안전하게 앞지르기해야 한다. 갓길로 통행하면 안 된다. 최저속도는 매시 50킬로미터이다.", LAW_MAX)
    val lawParkingBan = p("law_parking_ban", PR, "교차로, 횡단보도, 건널목, 보도와 차도가 구분된 도로의 보도에서는 정차와 주차를 하면 안 된다. 교차로의 가장자리나 도로의 모퉁이로부터 5미터 이내, 버스 정류지 표시로부터 10미터 이내, 건널목의 가장자리 또는 횡단보도로부터 10미터 이내, 소방용수시설 또는 비상소화장치로부터 5미터 이내도 같다.", LAW_MAX)
    val lawBasics = p("law_basics", PR, "최고속도보다 빠르게 운전하면 안 된다. 좌회전, 우회전, 진로 변경은 30미터(고속도로 100미터) 전부터 그 행위가 끝날 때까지 신호를 해야 한다. 운전 중에는 휴대용 전화를 사용하면 안 된다. 좌석안전띠를 매고, 모든 좌석의 동승자에게도 매도록 해야 한다.", LAW_MAX)
    val practiceRetry = p("practice_retry", PR, "다시 한 번 해 보십시오.")
    val practiceConfirmPrompt = p("practice_confirm_prompt", PR, "마쳤으면 '다음'을 누르십시오.")

    // 2-1 운전석 체형 맞추기
    val item21SeatForeAft = item2("2-1-seat-fore-aft",
        "좌석의 앞뒤 위치는 브레이크 페달을 끝까지 밟았을 때 무릎이 약간 굽는 위치로 맞춥니다. 무릎이 완전히 펴지면 급제동할 때 힘이 제대로 실리지 않고, 너무 가까우면 스티어링 휠 조작이 불편해집니다. 조절은 좌석 쿠션 앞쪽 레버로 합니다. 앞을 잘 보려고 좌석을 너무 당기기 쉬우니, 시야가 아니라 페달과의 거리를 기준으로 맞추십시오.",
        "좌석 쿠션 앞쪽 레버를 당긴 채 좌석을 앞뒤로 움직이십시오. 브레이크 페달을 끝까지 밟았을 때 무릎이 약간 굽는 위치에서 레버를 놓으십시오.", "좌석 앞뒤 위치를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21SeatBackrest = item2("2-1-seat-backrest",
        "등받이 각도는 어깨를 등받이에 붙이고 팔을 뻗었을 때 손목이 스티어링 휠 윗부분에 닿는 각도로 맞춥니다. 그래야 스티어링 휠을 크게 돌릴 때도 어깨가 등받이에서 떨어지지 않습니다. 조절은 좌석 바깥쪽의 등받이 각도 조절 레버로 합니다. 등받이를 많이 눕히면 시야가 낮아지고 조작이 늦어지니, 거의 세운 상태에서 시작하십시오.",
        "등받이 각도 조절 레버를 당겨 등받이를 세우십시오. 어깨를 붙이고 팔을 뻗었을 때 손목이 스티어링 휠 윗부분에 닿으면 레버를 놓으십시오.", "등받이 각도를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21SeatHeight = item2("2-1-seat-height",
        "좌석 높이는 후드 앞쪽과 클러스터이 잘 보이고 머리가 천장에 닿지 않는 위치로 맞춥니다. 너무 낮으면 차 앞쪽의 거리감이 떨어지고, 너무 높으면 급제동할 때 머리를 부딪칠 수 있습니다. 조절은 좌석 바깥쪽의 높낮이 조절 레버로 합니다. 높이를 바꾸면 페달과의 거리도 달라지니, 높이를 맞춘 뒤 앞뒤 위치를 다시 확인하십시오.",
        "높낮이 조절 레버를 위로 여러 번 당겨 좌석을 올리십시오. 앞이 잘 보이고 머리가 천장에 닿지 않으면 멈추십시오.", "좌석 높이를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21SteeringTilt = item2("2-1-steering-tilt",
        "스티어링 휠 높이는 클러스터 전체가 스티어링 휠에 가려지지 않는 위치로 맞춥니다. 스티어링 컬럼 아래쪽 레버를 내리면 잠금이 풀려 위아래로 움직이고, 위치를 맞춘 뒤 레버를 올리면 고정됩니다. 레버를 올려 고정하지 않으면 주행 중 스티어링 휠이 움직일 수 있으니 반드시 고정하십시오.",
        "스티어링 휠 아래 레버를 내리고, 클러스터이 가려지지 않는 높이로 맞추십시오. 맞춘 뒤 레버를 올려 고정하십시오.", "스티어링 휠 높이를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21SteeringTelescopic = item2("2-1-steering-telescopic",
        "스티어링 휠 거리는 어깨를 등받이에 붙이고 팔을 뻗었을 때 손목이 스티어링 휠 윗부분에 닿는 위치로 맞춥니다. 높이 조절과 같은 레버를 내린 상태에서 스티어링 휠을 앞뒤로 움직이고, 맞춘 뒤 레버를 올려 고정합니다. 스티어링 휠이 가슴에 너무 가까우면 에어백이 펼쳐질 때 위험하니 가깝게 당기지 마십시오.",
        "레버를 내린 상태에서 스티어링 휠을 몸 쪽으로 당기십시오. 팔을 뻗었을 때 손목이 윗부분에 닿는 위치에서 레버를 올려 고정하십시오.", "스티어링 휠 거리를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21Posture = item2("2-1-posture",
        "올바른 자세의 기준은 세 가지입니다. 브레이크 페달을 끝까지 밟았을 때 무릎이 약간 굽고, 팔을 뻗으면 손목이 스티어링 휠 위에 닿고, 머리가 헤드레스트 중앙에 옵니다. 그림은 피해야 할 자세입니다. 엉덩이를 앞으로 빼고 비스듬히 앉으면 안전벨트와 에어백이 몸을 제대로 보호하지 못합니다. 엉덩이를 좌석 깊숙이 넣고 등을 등받이에 붙이십시오.",
        "브레이크 페달을 끝까지 밟아 무릎이 약간 굽는지, 팔을 뻗어 손목이 스티어링 휠에 닿는지, 머리가 헤드레스트에 닿는지 확인하십시오.", "운전 자세를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val item21Headrest = item2("2-1-headrest",
        "헤드레스트는 중앙이 귀 높이에 오도록 맞춥니다. 뒤에서 추돌당하면 머리가 뒤로 꺾이는데, 헤드레스트가 머리를 받쳐 목 부상을 줄여 줍니다. 조절은 헤드레스트 옆 버튼을 누른 채 위아래로 움직여서 합니다. 헤드레스트를 목 높이까지 내리면 추돌할 때 오히려 머리가 더 꺾이니, 편하다고 낮추지 마십시오.",
        "헤드레스트 옆 버튼을 누른 채 위아래로 움직여 중앙이 귀 높이에 오도록 맞추십시오. 버튼을 놓으면 고정됩니다.", "헤드레스트 높이를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21SideMirror = item2("2-1-side-mirror",
        "실외 미러는 차의 옆과 뒤쪽을 보는 거울입니다. 미러 안쪽 가장자리에 내 차체가 약간 보이도록 맞춥니다. 조절은 운전석 도어의 미러 조절 스위치에서 좌우를 선택한 뒤 방향 버튼으로 합니다. 차체가 미러의 절반 가까이 보이면 옆 차로가 덜 보여 사각지대가 커지니, 차체는 약간만 보이게 하십시오.",
        "운전석 도어의 미러 조절 스위치에서 L 또는 R을 선택하고, 방향 버튼으로 좌우 미러를 맞추십시오. 안쪽 가장자리에 차체가 약간 보이면 됩니다.", "실외 미러를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21RoomMirror = item2("2-1-room-mirror",
        "실내 미러는 차 바로 뒤를 보는 거울입니다. 올바른 자세로 앉아 고개를 돌리지 않고 눈만 움직여 뒷유리 전체가 보이도록 맞춥니다. 조절은 미러 테두리를 손으로 잡고 돌려서 합니다. 좌석이나 등받이를 바꾸면 눈 위치가 달라지니, 자세를 먼저 맞춘 뒤 실내 미러를 조절하십시오.",
        "올바른 자세로 앉은 채 실내 미러 테두리를 잡고 돌려, 뒷유리 전체가 보이도록 맞추십시오.", "실내 미러를 맞췄습니다.", "맞췄으면 '다음'을 누르십시오.")
    val item21BlindSpot = item2("2-1-blind-spot",
        "사각지대는 실외 미러와 실내 미러 어디에도 비치지 않는 구역입니다. 특히 옆 차로에서 내 차 바로 옆에 있는 차는 미러에 보이지 않습니다. 그래서 차로를 바꾸기 전에는 미러를 본 뒤 반드시 고개를 돌려 어깨 너머로 직접 확인해야 합니다. 미러만 보고 차로를 바꾸는 것이 흔한 접촉 사고의 원인이니, 두 가지 확인을 항상 함께 하십시오.",
        "고개를 돌려 왼쪽 어깨 너머, 이어서 오른쪽 어깨 너머를 직접 보십시오. 미러에 보이지 않던 옆 공간이 보이는지 확인하십시오.", "사각지대를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val practiceSeatTall = p("practice_seat_tall", PR, "키가 크면 좌석을 뒤로 빼고 높이를 낮추십시오. 미러는 평소보다 약간 아래로 맞추십시오.", variant = "tall")
    val practiceSeatShort = p("practice_seat_short", PR, "키가 작으면 좌석 높이를 올리십시오. 후드 끝이 보이면 됩니다.", variant = "short")

    // 2-2 안전벨트
    val item22Seatbelt = item2("2-2-seatbelt",
        "안전벨트는 어깨 부분이 어깨 중앙을 지나 가슴을 가로지르고, 골반 부분은 배가 아니라 골반 위를 낮게 지나도록 매야 합니다. 버클은 찰칵 소리가 날 때까지 밀어 넣어야 잠긴 것입니다. 마지막으로 벨트가 꼬이거나 느슨한 곳이 없는지 당겨서 확인하십시오. 골반 부분이 배 위로 올라가면 충돌할 때 배를 크게 다칠 수 있습니다.",
        "어깨 부분은 어깨 중앙, 골반 부분은 골반 위로 지나게 한 뒤, 버클을 찰칵 소리가 날 때까지 밀어 넣으십시오.", "안전벨트를 착용했습니다.", "제대로 맸는지 확인한 뒤 '다음'을 누르십시오.")
    val item22SeatbeltWrong = item2("2-2-seatbelt-wrong-examples",
        "잘못된 착용은 세 가지입니다. 어깨 부분이 목에 걸리는 경우, 골반 부분이 배 위로 올라오는 경우, 어깨 부분을 팔 아래로 빼서 매는 경우입니다. 이렇게 매면 충돌할 때 벨트가 몸을 잡아 주지 못하고 오히려 목이나 배를 다치게 합니다. 목에 걸린다고 팔 아래로 빼지 말고, 좌석 높이나 벨트 높이 조절 장치로 맞추십시오.",
        "지금 맨 벨트의 어깨 부분이 어깨 중앙을 지나는지, 골반 부분이 골반 위에 있는지 확인하십시오.", "확인했습니다.", "확인했으면 '다음'을 누르십시오.")

    // 2-3 시동과 주차브레이크
    val item23Ignition = item2("2-3-ignition",
        "시동은 브레이크 페달을 밟은 상태에서 시동 버튼을 눌러 겁니다. 브레이크 페달을 밟지 않고 누르면 시동은 걸리지 않고 전원만 켜집니다. 시동이 걸리면 클러스터의 경고등이 대부분 꺼지고, 엔진 소리나 READY 표시로 확인할 수 있습니다. 전원만 켜진 상태를 시동으로 착각하기 쉬우니 버튼을 누른 뒤 클러스터을 확인하십시오.",
        "이미 시동이 걸려 있으면 버튼을 누르지 말고 클러스터의 경고등이 꺼져 있는지만 확인하십시오. 꺼져 있다면 브레이크 페달을 끝까지 밟은 채 시동 버튼을 한 번 누르십시오.", "시동이 걸렸습니다.", "시동이 걸렸으면 '다음'을 누르십시오.")
    val item23EpbRelease = item2("2-3-epb-release",
        "전자식 주차 브레이크는 스티어링 휠 왼쪽 아래, 무릎 앞 패널에 있는 P 표시 스위치로 조작합니다. 브레이크 페달을 밟은 상태에서 스위치를 누르면 해제되고, 클러스터의 주차 브레이크 경고등이 꺼지면 해제된 것입니다. 브레이크 페달을 밟지 않으면 해제되지 않습니다. 변속 레버의 P와 주차 브레이크 스위치의 P는 서로 다르니 혼동하지 마십시오.",
        "브레이크 페달을 밟은 채 스티어링 휠 왼쪽 아래의 주차 브레이크 스위치를 누르십시오. 이미 해제되어 있으면 먼저 당겨서 건 뒤 다시 누르십시오.", "주차 브레이크를 해제했습니다.", "경고등이 꺼졌으면 '다음'을 누르십시오.")
    val item23EpbApply = item2("2-3-epb-apply",
        "전자식 주차 브레이크는 같은 스위치를 당기면 걸립니다. 걸리면 클러스터에 주차 브레이크 경고등이 켜집니다. 주차할 때는 P로 변속하고 주차 브레이크를 거는 것이 기본 순서입니다. 누르면 해제, 당기면 걸기이니 두 동작을 혼동하지 마십시오.", "브레이크 페달을 밟은 채 주차 브레이크 스위치를 당기십시오. 클러스터에 주차 브레이크 경고등이 켜지는지 확인하십시오.", "주차 브레이크를 걸기했습니다.", "경고등이 켜졌으면 '다음'을 누르십시오.")
    val item23AutoholdOn = item2("2-3-autohold-on",
        "오토 홀드는 정차한 뒤 브레이크 페달에서 발을 떼도 정차 상태를 유지하는 자동 정차 기능입니다. 켜 두면 신호 대기 중 브레이크 페달을 계속 밟지 않아도 되고, 가속 페달을 밟으면 자동으로 해제됩니다. 오토 홀드 스위치를 누르면 클러스터에 표시등이 켜집니다. 시동을 다시 걸면 꺼질 수 있으니 출발 전에 표시등을 확인하십시오.",
        "브레이크 페달을 밟은 채 화면의 차량 설정, 빠른 설정에서 AUTO HOLD 버튼을 누르십시오. 주행 정보 화면에 오토 홀드 표시등이 켜지는지 확인한 뒤 이 앱으로 돌아오십시오.", "오토 홀드를 켰습니다.", "켜졌으면 '다음'을 누르십시오.")
    val item23AutoholdOff = item2("2-3-autohold-off",
        "주차장처럼 좁은 곳에서는 오토 홀드가 불편할 수 있습니다. 브레이크 페달에서 발을 뗄 때마다 차가 정차 상태를 유지해 조금씩 움직이기 어렵기 때문입니다. 이때는 화면의 AUTO HOLD 버튼을 한 번 더 눌러 끄십시오. 끄면 브레이크 페달에서 발을 떼는 순간 차가 움직이니 주의하십시오.",
        "브레이크 페달을 밟은 채 빠른 설정의 AUTO HOLD 버튼을 한 번 더 눌러 끄십시오. 오토 홀드 표시등이 꺼지는지 확인한 뒤 이 앱으로 돌아오십시오.", "오토 홀드를 껐습니다.", "껐으면 '다음'을 누르십시오.")
    val practiceStartOffIntro = p("practice_start_off_intro", PR, "시동을 끌 때는 P로 변속하고 시동 버튼을 한 번 누르십시오.")
    val practiceStartOffPass = p("practice_start_off_pass", PR, "시동이 꺼졌습니다.")

    // 2-4 기어와 페달
    val item24GearSequence = item2("2-4-gear-sequence",
        "변속은 브레이크 페달을 밟은 상태에서만 됩니다. 브레이크 페달을 밟고 P에서 R, N, D 순서로 옮겨 보는 연습입니다. 차가 완전히 정지한 뒤에 변속해야 하며, 움직이는 중에 R이나 P로 변속하면 변속기가 손상될 수 있습니다.",
        "브레이크 페달을 끝까지 밟고 스티어링 휠 오른쪽 변속 레버로 R, N, D 순서로 변속하십시오. 변속할 때마다 화면의 변속 위치 표시를 확인하십시오.", "변속을 마쳤습니다.")
    val item24GearMeaning = item2("2-4-gear-meaning",
        "P는 주차, R은 후진, N은 중립, D는 주행입니다. 정차해 둘 때는 P, 뒤로 갈 때는 R, 앞으로 갈 때는 D를 씁니다. N은 동력이 끊긴 상태여서 경사로에서는 차가 밀립니다. 주차할 때는 N이 아니라 P로 변속하십시오.",
        "스티어링 휠 오른쪽 변속 레버의 R, N, D 표시와 레버 끝의 P 버튼을 한 번씩 짚어 보십시오.", "확인했습니다.", "짚었으면 '다음'을 누르십시오.")
    val item24NeutralUse = item2("2-4-neutral-use",
        "N은 중립으로, 평소 주행이나 주차에는 쓰지 않습니다. 자동 세차기에서 컨베이어가 차를 끌 때와 견인할 때만 사용합니다. 신호 대기 중 N에 둘 필요는 없으며, 경사로에서는 차가 밀릴 수 있습니다.",
        "N을 쓰는 경우는 자동 세차와 견인, 두 가지입니다. 기억해 두십시오.", "확인했습니다.", "기억했으면 '다음'을 누르십시오.")
    val item24PedalPosition = item2("2-4-pedal-position",
        "페달은 오른발로만 조작합니다. 오른쪽이 가속 페달, 왼쪽이 브레이크 페달입니다. 왼발은 왼쪽 발판에 두고 쓰지 않습니다. 두 발로 따로 밟으면 급할 때 두 페달을 동시에 밟거나 잘못 밟기 쉽습니다. 발뒤꿈치를 바닥에 붙이고 발끝만 옮기면 두 페달을 혼동하지 않습니다.",
        "오른발로 브레이크 페달과 가속 페달의 위치를 번갈아 확인하십시오. 밟지는 마십시오.", "페달 위치를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val practiceGearBrakeFirst = p("practice_gear_brake_first", PR, "브레이크 페달을 먼저 밟아야 변속됩니다.")
    val practicePedalHabit = p("practice_pedal_habit", PR, "출발할 때마다 발로 페달 위치를 확인하십시오. 페달을 잘못 밟는 것을 막는 습관입니다.")
    // 2-4 급발진·헛밟음 대처 훈련 (SuddenAccelDrill 의 ID 규칙)
    val item24SuddenAccel = item2("2-4-sudden-accel",
        "차가 갑자기 튀어 나가면 세 가지만 하십시오. 첫째, 가속 페달에서 발을 떼고 브레이크 페달을 끝까지 밟습니다. 둘째, N으로 변속합니다. 셋째, 시동 버튼을 길게 눌러 시동을 끕니다. 브레이크 페달을 나눠 밟으면 제동력이 약해지니 한 번에 끝까지 밟아야 합니다.",
        "지금 해 보십시오. 브레이크 페달을 끝까지 밟고 N으로 변속하십시오.", "급발진 대처를 마쳤습니다.")
    val practiceSuddenAccelStep1 = p("practice_sudden_accel_step1", PR, "하나, 가속 페달에서 발을 떼고 브레이크 페달을 끝까지 밟습니다.")
    val practiceSuddenAccelStep2 = p("practice_sudden_accel_step2", PR, "둘, N으로 변속합니다.")
    val practiceSuddenAccelStep3 = p("practice_sudden_accel_step3", PR, "셋, 시동 버튼을 길게 누릅니다.")
    val practiceSuddenAccelWhy = p("practice_sudden_accel_why", PR, "브레이크 페달은 끝까지 밟아야 멈춥니다. 약하게 밟으면 멈추지 않습니다.")
    val practiceSuddenAccelRetry = p("practice_sudden_accel_retry", PR, "다시 한 번. 브레이크 끝까지, N.")
    val practiceSuddenAccelQuizQ1 = p("practice_2_4_sudden_accel_quiz_q1", PR, "첫 번째. 차가 갑자기 튀어 나갑니다. 순서를 고르십시오.")
    val practiceSuddenAccelQuizQ2 = p("practice_2_4_sudden_accel_quiz_q2", PR, "두 번째. 브레이크 페달을 밟았는데 멈추지 않습니다. 순서를 고르십시오.")
    val practiceSuddenAccelQuizQ3 = p("practice_2_4_sudden_accel_quiz_q3", PR, "세 번째. 주차장에서 페달을 잘못 밟았습니다. 순서를 고르십시오.")
    val practiceSuddenAccelQuizCorrect = p("practice_2_4_sudden_accel_quiz_correct", PR, "정답입니다.")
    val practiceSuddenAccelQuizWrong = p("practice_2_4_sudden_accel_quiz_wrong", PR, "오답입니다. 발 떼고 브레이크 끝까지, N, 시동 버튼 길게입니다.")   // 페르소나 재검증: 오답에도 '괜찮아요'
    val practiceSuddenAccelDone = p("practice_2_4_sudden_accel_done", PR, "급발진 대처를 마쳤습니다. 출발 전에 가끔 다시 확인합니다.")
    val practiceSuddenAccelReviewQuestion = p("practice_2_4_sudden_accel_review_question", PR, "차가 갑자기 튀어 나가면 무엇부터 합니까?")
    // 퀴즈 보기 음성용 라벨 (SuddenAccelDrill.Action 이름 소문자)
    val practiceSuddenAccelActionFootOffPedals = p("practice_2_4_sudden_accel_action_foot_off_pedals", PR, "가속 페달에서 발을 뗍니다.")
    val practiceSuddenAccelActionBrakeFull = p("practice_2_4_sudden_accel_action_brake_full", PR, "브레이크 페달을 끝까지 밟습니다.")
    val practiceSuddenAccelActionShiftN = p("practice_2_4_sudden_accel_action_shift_n", PR, "N으로 변속합니다.")
    val practiceSuddenAccelActionEngineButtonLong = p("practice_2_4_sudden_accel_action_engine_button_long", PR, "시동 버튼을 길게 누릅니다.")
    val practiceSuddenAccelActionPumpBrake = p("practice_2_4_sudden_accel_action_pump_brake", PR, "브레이크 페달을 나눠 밟습니다.")
    val practiceSuddenAccelActionShiftP = p("practice_2_4_sudden_accel_action_shift_p", PR, "P로 변속합니다.")
    val practiceSuddenAccelActionTurnWheel = p("practice_2_4_sudden_accel_action_turn_wheel", PR, "스티어링 휠을 꺾습니다.")

    // 2-5 등화
    val item25HeadlightAuto = item2("2-5-headlight-auto",
        "실외 조명은 스티어링 휠 왼쪽 아래 패널의 조명 버튼으로 바꿉니다. 누를 때마다 오토, 전조등, 꺼짐 순으로 바뀌고, 주행 중에는 꺼짐으로 바뀌지 않습니다. 평소에는 오토에 두십시오. 오토는 주변이 어두워지면 자동으로 전조등을 켭니다. 단, 비나 안개로 시야가 나쁜 낮에는 켜지지 않을 수 있으니 직접 전조등 위치로 돌리십시오.",
        "스티어링 휠 왼쪽 아래의 실외 조명 버튼을 눌러 오토에 맞추십시오.", "전조등을 오토로 맞췄습니다.", "오토에 맞췄으면 '다음'을 누르십시오.")
    val item25HeadlightLow = item2("2-5-headlight-low",
        "하향등은 밤에 평소 사용하는 전조등입니다. 빛이 아래쪽을 비추어 앞차와 마주 오는 차의 운전자를 눈부시게 하지 않습니다. 실외 조명 버튼을 오토에서 한 번 더 누르면 전조등으로 바뀌어 하향등이 켜집니다. 주간 주행등만 켜진 상태에서는 뒤쪽 미등이 꺼져 있으니, 밤에는 반드시 하향등을 켜십시오.", "실외 조명 버튼을 한 번 더 눌러 전조등으로 바꾸십시오.", "하향등을 켰습니다.")
    val item25HeadlightHigh = item2("2-5-headlight-high",
        "상향등은 빛을 멀리 비추는 전조등으로, 가로등이 없는 어두운 길에서 사용합니다. 앞차나 마주 오는 차가 있으면 상대 운전자가 눈부시니 사용하지 마십시오. 전조등이 켜진 상태에서 스티어링 휠 왼쪽 레버를 앞으로 밀었다 놓으면 켜지고 몸 쪽으로 당겼다 놓으면 꺼집니다. 클러스터에 파란색 상향등 표시등이 켜지면 상향등 상태입니다.",
        "스티어링 휠 왼쪽 레버를 앞으로 밀었다 놓으십시오. 클러스터에 파란색 상향등 표시등이 켜집니다.", "상향등을 켰습니다. 레버를 몸 쪽으로 당겼다 놓아 끄십시오.")
    val item25Passing = item2("2-5-passing",
        "상향등 점멸은 상향등을 잠깐 켰다 끄는 동작으로, 흔히 패싱이라고 합니다. 레버를 몸 쪽으로 당겼다 놓으면 상향등이 한 번 켜졌다 꺼집니다. 양보할 때 신호로 사용합니다. 상향등과 달리 레버를 놓으면 바로 꺼지므로 따로 끌 필요가 없습니다.",
        "스티어링 휠 왼쪽 레버를 몸 쪽으로 한 번 당겼다 놓으십시오.", "상향등 점멸을 확인했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item25FogLight = item2("2-5-fog-light",
        "안개등은 안개가 끼었을 때만 사용합니다. 낮고 넓게 비추어 안개 속에서 내 차의 위치를 알립니다. 전조등 스위치 레버 가운데의 링을 돌리면 켜집니다. 맑은 날 켜 두면 다른 운전자가 눈부시니, 안개가 걷히면 바로 끄십시오.",
        "전조등 스위치 레버 가운데의 링을 돌려 안개등을 켜십시오.", "안개등을 켰습니다.", "켜 보았으면 '다음'을 누르십시오.")
    val item25Hazard = item2("2-5-hazard",
        "비상 경고등 스위치는 가운데의 빨간 삼각형 버튼입니다. 누르면 좌우 방향지시등이 동시에 깜박입니다. 갓길에 정차할 때와 양보를 받아 감사를 표할 때 누릅니다. 켜져 있는 동안에는 방향지시등이 구분되지 않으니, 차로를 변경하기 전에 끄십시오.",
        "가운데의 빨간 삼각형 비상 경고등 스위치를 누르십시오.", "비상 경고등을 켰습니다. 다시 눌러 끄십시오.", "깜박이는 것을 확인했으면 '다음'을 누르십시오.")
    // 아반떼(CN8) 취급설명서의 경음기·안전 삼각대
    val item25Horn = item2("2-5-horn",
        "경음기는 스티어링 휠 가운데의 경음기 표시가 있는 부분을 누르면 울립니다. 위험을 알려야 할 때만 짧게 사용하십시오. 정당한 이유 없이 계속 울리거나 반복해서 울리면 안 됩니다. 주택가와 밤에는 특히 삼가십시오.",
        "스티어링 휠 가운데의 경음기 표시 위치를 확인하십시오. 주변에 사람이 없고 울려도 되는 곳이라면 짧게 한 번 눌러 보십시오.", "경음기 위치를 확인했습니다.", "위치를 확인했으면 '다음'을 누르십시오.")
    val item25WarningTriangle = item2("2-5-warning-triangle",
        "안전 삼각대는 트렁크 안에 들어 있습니다. 고속도로나 자동차 전용도로에서 고장이나 사고로 차를 움직일 수 없으면 비상 경고등을 켜고, 뒤에서 오는 차가 확인할 수 있는 위치에 안전 삼각대를 세워야 합니다. 삼각대를 세울 때는 뒤에서 오는 차를 살피고, 세운 뒤에는 도로 밖 안전한 곳으로 피하십시오.",
        "트렁크를 열어 안전 삼각대가 어디에 있는지 확인하십시오. 케이스에서 꺼내 다리를 펴고 삼각형으로 세우는 방법도 한 번 보아 두십시오.", "안전 삼각대 위치를 확인했습니다.", "위치를 확인했으면 '다음'을 누르십시오.")
    val item25InteriorLight = item2("2-5-interior-light",
        "실내등은 천장의 스위치로 켭니다. 밤에 켠 채로 주행하면 앞유리에 실내가 비쳐 바깥이 잘 보이지 않습니다. 물건을 찾을 때만 잠깐 켜고 바로 끄십시오. 출발 전에 실내등이 꺼져 있는지 확인하십시오.",
        "천장의 실내등 스위치를 누르십시오.", "실내등을 켰습니다.", "켜 보았으면 '다음'을 누르십시오.")

    // 2-6 와이퍼·방향지시등
    val item26Wiper = item2("2-6-wiper",
        "와이퍼는 스티어링 휠 왼쪽 레버 끝의 노브를 돌려 작동합니다. 방향지시등과 같은 레버입니다. 노브를 돌리면 꺼짐, 간헐, 저속, 고속 순으로 빨라집니다. 비가 약하면 간헐에 두고, 비가 세질수록 한 칸씩 더 돌리십시오. 마른 유리에서 와이퍼를 작동하면 유리가 긁힐 수 있으니 비가 올 때만 사용하십시오.",
        "스티어링 휠 왼쪽 레버 끝의 와이퍼 노브를 한 칸 돌리십시오.", "와이퍼를 작동했습니다. 노브를 꺼짐으로 되돌리십시오.")
    val item26Washer = item2("2-6-washer",
        "와셔액은 앞유리를 닦는 세척액입니다. 스티어링 휠 왼쪽 레버 끝의 버튼을 깊게 누르면 와셔액이 분사되고 와이퍼가 함께 작동합니다. 살짝만 누르면 와이퍼가 한 번만 움직입니다. 유리에 먼지가 쌓였을 때 와이퍼만 작동하면 유리가 긁히니, 먼저 와셔액을 분사하십시오. 와셔액은 떨어지기 전에 미리 보충하십시오.", "왼쪽 레버 끝의 버튼을 깊게 한 번 눌렀다 놓으십시오.", "와셔액을 분사했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item26RearWiper = item2("2-6-rear-wiper",
        "리어 와이퍼는 와이퍼 레버 끝을 돌려 작동합니다. 비 오는 날 후진하거나 실내 미러로 뒤를 볼 때 사용합니다. 앞 와이퍼와 따로 작동하므로, 앞 와이퍼를 켰다고 뒷유리가 닦이지는 않습니다. 뒷유리 열선과는 다른 기능입니다.", "와이퍼 레버 끝을 돌려 리어 와이퍼를 켜십시오.", "리어 와이퍼를 작동했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item26TurnSignalRight = item2("2-6-turn-signal-right",
        "방향지시등 레버는 스티어링 휠 왼쪽에 있습니다. 레버를 위로 올리면 오른쪽 방향지시등이 켜집니다. 스티어링 휠을 돌리는 방향으로 레버를 움직인다고 기억하십시오. 회전을 마치고 스티어링 휠이 돌아오면 자동으로 꺼지지만, 완만하게 돌 때는 꺼지지 않을 수 있으니 클러스터을 확인하십시오.", "스티어링 휠 왼쪽 방향지시등 레버를 위로 올리십시오.", "오른쪽 방향지시등을 켰습니다.")
    val item26TurnSignalLeft = item2("2-6-turn-signal-left",
        "레버를 아래로 내리면 왼쪽 방향지시등이 켜집니다. 오른쪽과 마찬가지로 스티어링 휠을 돌리는 방향과 같습니다. 좌회전하거나 왼쪽 차로로 변경하기 전에 미리 켜야 뒤차가 알 수 있습니다. 켜지 않으면 뒤차가 예측하지 못해 위험합니다.", "방향지시등 레버를 아래로 내리십시오.", "왼쪽 방향지시등을 켰습니다.")
    val item26LaneChangeTap = item2("2-6-lane-change-tap",
        "방향지시등 레버를 끝까지 올리지 않고 살짝 올렸다 놓으면 세 번 깜박이고 저절로 꺼집니다. 원터치 방향지시등 기능으로, 차로를 바꿀 때 사용합니다. 끝까지 올리면 스티어링 휠이 돌아올 때까지 계속 켜져 있어 직접 꺼야 합니다. 세 번 깜박이는 동안 차로를 다 옮기지 못하면 한 번 더 조작하십시오.",
        "방향지시등 레버를 걸리지 않을 만큼만 살짝 올렸다 놓으십시오.", "원터치 방향지시등 기능을 확인했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val practiceSignalIntroLeftHanded = p("practice_signal_intro_left_handed", PR, "방향지시등 레버는 스티어링 휠 왼쪽에 있습니다. 위로 올리면 오른쪽, 아래로 내리면 왼쪽입니다. 왼손으로 조작하십시오.", variant = "left_handed")

    // 2-7 창문·도어락·공조
    val item27Window = item2("2-7-window",
        "파워 윈도우 스위치는 운전석 도어 팔걸이에 있습니다. 스위치를 누르면 창문이 내려가고, 당기면 올라갑니다. 운전석에서 모든 창문을 조작할 수 있습니다. 창문을 올릴 때는 손이나 머리가 끼지 않도록 뒷좌석을 확인하십시오.",
        "팔걸이의 스위치를 눌러 운전석 창문을 내리고, 당겨서 다시 올리십시오.", "창문을 조작했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item27WindowLock = item2("2-7-window-lock",
        "파워 윈도우 잠금 스위치는 창문 스위치 옆에 있습니다. 누르면 뒷좌석에서 창문을 열 수 없습니다. 어린이를 태울 때 사용하십시오. 뒷좌석 창문이 열리지 않으면 고장이 아니라 이 스위치가 눌려 있는 경우가 많으니, 먼저 잠금 스위치를 확인하십시오.",
        "파워 윈도우 잠금 스위치를 눌러 잠근 뒤, 다시 눌러 해제하십시오.", "창문 잠금을 확인했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item27DoorLock = item2("2-7-door-lock",
        "도어 잠금 스위치는 운전석 도어에 있습니다. 누르면 모든 도어가 잠기고, 다시 누르면 해제됩니다. 출발하면 자동으로 잠기므로 직접 누르지 않아도 됩니다. 주행 중 도어가 저절로 잠기는 것은 정상입니다.",
        "도어 잠금 스위치를 눌러 잠근 뒤, 다시 눌러 해제하십시오.", "도어 잠금을 확인했습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item27Hvac = item2("2-7-hvac",
        "공조 장치는 가운데 화면 아래쪽 바와 공조 화면에서 조작합니다. 온도, 바람 세기, 바람 방향을 조절합니다. 에어컨은 A/C 버튼으로 켜고, 난방은 온도를 올리면 됩니다. 온도만 낮춘다고 찬바람이 나오지는 않으며, A/C가 켜져 있어야 찬바람이 나옵니다.",
        "공조 화면의 A/C 버튼을 눌러 에어컨을 켜십시오.", "에어컨을 켰습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item27Defog = item2("2-7-defog",
        "앞유리에 김이 서리면 앞유리 모양의 서리 제거 버튼을 누르십시오. 바람이 앞유리로 나오고 에어컨이 함께 켜져 습기를 없앱니다. 겨울에도 에어컨이 켜지는 것은 정상이며, 김을 없애려면 끄지 말고 그대로 두십시오. 김이 걷히면 원래 바람 방향으로 되돌리십시오.",
        "화면 아래쪽 바의 앞유리 모양 버튼을 누르십시오.", "앞유리 서리 제거를 켰습니다.", "해 보았으면 '다음'을 누르십시오.")
    val item27AirRecirculation = item2("2-7-air-recirculation",
        "내기 순환은 바깥 공기를 막고 실내 공기만 순환시킵니다. 터널이나 매연이 심한 곳에서 사용하고, 평소에는 외기 유입에 두십시오. 내기 순환을 오래 켜 두면 실내 공기가 탁해져 졸음이 오고 유리에 김이 서리기 쉬우니, 터널을 지나면 외기 유입으로 바꾸십시오.",
        "내기 순환 버튼을 눌러 내기로 바꾼 뒤, 다시 눌러 외기로 되돌리십시오.", "내기와 외기 전환을 확인했습니다.", "해 보았으면 '다음'을 누르십시오.")

    // 2-8 계기판과 경고등
    val item28Speedometer = item2("2-8-speedometer",
        "속도계는 현재 주행 속도를 나타내며, 단위는 시속 킬로미터입니다. 주행 중에는 시선을 오래 두지 말고 짧게 확인한 뒤 앞을 보십시오. 속도계의 숫자는 현재 속도일 뿐 제한 속도가 아니니, 제한 속도는 도로 표지판으로 확인하십시오.", "주행 정보 화면의 속도가 지금 0인지 확인하십시오.", "속도계를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val item28Rpm = item2("2-8-rpm",
        "엔진 회전계는 엔진이 1분에 몇 번 회전하는지를 나타냅니다. 시동만 건 공회전 상태에서는 1,000 아래에 머뭅니다. 가속하면 올라가고 속도가 일정해지면 내려옵니다. 회전수가 높다고 반드시 빠른 것은 아니며, 회전수가 지나치게 높아지면 가속 페달을 늦추십시오.",
        "주행 정보 화면 왼쪽 위의 엔진 회전수가 지금 얼마인지 확인하십시오.", "엔진 회전계를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val item28Fuel = item2("2-8-fuel",
        "연료계는 남은 연료량을 나타냅니다. 연료계 옆 주유기 표시의 작은 화살표는 주유구가 있는 쪽을 가리킵니다. 주유소에 들어갈 때 이 화살표를 보고 주유기 방향을 정하십시오. 연료 잔량 표시가 거의 비면 연료 부족 경고등이 켜지기 전에 미리 주유하십시오.", "연료계 옆 화살표가 왼쪽인지 오른쪽인지 확인하십시오.", "연료계를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val item28Trip = item2("2-8-trip",
        "트립 컴퓨터는 이번 주행의 거리 등을 보여 줍니다. 주행 정보 화면 아래쪽의 유틸리티 위젯을 열고 좌우로 넘기면 현 주행 정보가 나옵니다. 트립 거리는 초기화할 수 있으며, 차의 누적 주행 거리와는 다른 값입니다. 누적 주행 거리는 초기화되지 않습니다.",
        "주행 정보 화면 아래쪽의 유틸리티 위젯에서 현 주행 정보를 찾으십시오.", "트립 화면을 확인했습니다.", "찾았으면 '다음'을 누르십시오.")
    val item28WarningLampColors = item2("2-8-warning-lamp-colors",
        "경고등과 표시등은 색으로 구분합니다. 녹색은 기능이 작동 중이라는 표시로, 계속 주행해도 됩니다. 노란색은 점검이 필요하다는 뜻으로, 가까운 안전한 곳에서 확인하십시오. 빨간색은 위험하다는 뜻으로, 즉시 안전한 곳에 정차하십시오. 시동 직후 여러 경고등이 잠깐 켜졌다 꺼지는 것은 정상입니다.",
        "녹색, 노란색, 빨간색. 각 색의 뜻을 한 번씩 소리 내어 말해 보십시오.", "경고등 색을 확인했습니다.", "기억했으면 '다음'을 누르십시오.")
    val practiceDashQuizAsk = p("practice_dash_quiz_ask", PR, "이 경고등이 켜지면 어떻게 해야 합니까?")
    val practiceDashQuizCorrect = p("practice_dash_quiz_correct", PR, "정답입니다.")
    val practiceDashQuizWrong = p("practice_dash_quiz_wrong", PR, "오답입니다. 이것은 {color} 등이므로 {action}.")   // 페르소나 재검증: 오답에도 '괜찮아요'
    val practiceDashRehearsalIntro = p("practice_dash_rehearsal_intro", PR, "주행 중에 이 경고등이 켜지면 다음과 같이 안내합니다.")
    val practiceDashRehearsalOutro = p("practice_dash_rehearsal_outro", PR, "그때는 갓길이나 가까운 주차장에 정차하십시오.")

    // 2-9 운전자 보조 기능 (옵션 있을 때만)
    val item29Lka = item2("2-9-lka",
        "차로 이탈방지 보조는 차가 차로를 벗어나려 하면 경고하고, 스티어링 휠을 살짝 돌려 차로 안으로 되돌립니다. 차로 가운데를 유지하도록 돕기도 합니다. 차선이 흐리거나 급한 커브에서는 작동하지 않을 수 있고 자율 주행 기능이 아니니, 손은 항상 스티어링 휠에 두십시오. 클러스터에 표시등이 켜져 있는지 확인하십시오.",
        "스티어링 휠 왼쪽의 차로 모양 버튼을 길게 눌러 기능을 켜십시오. 클러스터에 표시등이 켜집니다.", "차로 이탈방지 보조를 켰습니다.", "켜 보았으면 '다음'을 누르십시오.")
    val item29Fca = item2("2-9-fca",
        "전방 충돌방지 보조는 앞차와 가까워지면 경고음을 울리고, 그래도 감속하지 않으면 제동을 돕습니다. 항상 켜 두십시오. 충돌을 완전히 막아 주는 기능이 아니며 날씨나 속도에 따라 작동하지 않을 수 있으니, 앞차와의 거리는 운전자가 직접 유지하십시오.",
        "차량 설정의 운전자 보조 메뉴에서 전방 충돌방지 보조가 켜져 있는지 확인하십시오. 확인했으면 이 앱으로 돌아와 '다음'을 누르십시오.", "전방 충돌방지 보조를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val item29Bcw = item2("2-9-bcw",
        "후측방 충돌 경고는 옆 차로 뒤쪽 사각지대에 차가 있으면 실외 미러에 경고 표시를 켭니다. 그 상태에서 방향지시등을 켜면 경고음이 울립니다. 경고 표시가 꺼져 있어도 빠르게 다가오는 차는 늦게 감지될 수 있으니, 차로를 변경하기 전에는 어깨 너머로 직접 확인하십시오.",
        "양쪽 실외 미러에서 경고 표시가 켜지는 위치를 확인하십시오.", "후측방 충돌 경고를 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val item29Cruise = item2("2-9-cruise",
        "크루즈 컨트롤은 가속 페달을 밟지 않아도 설정한 속도를 유지합니다. 차가 적은 고속도로에서 사용하고, 신호와 차가 많은 시내에서는 끄십시오. 브레이크 페달을 밟으면 바로 해제됩니다. 작동 중에도 앞차와의 거리와 전방 상황은 운전자가 직접 살피고, 비나 눈이 올 때는 사용하지 마십시오.",
        "스티어링 휠 왼쪽 주행 보조 버튼의 위치만 확인하십시오. 지금은 켜지 마십시오.", "크루즈 버튼을 확인했습니다.", "확인했으면 '다음'을 누르십시오.")
    val practiceAdasIntro = p("practice_adas_intro", PR, "이 차에는 {feature}이 있습니다. 하나씩 안내합니다.")

    // ───────────────────────────────── 감각 익히기 (4-0 ~ 4-7, 빈 주차장 저속) ─────────────────────────────────
    val sensePlaceIntro = p("sense_place_intro", PR, "혼자 연습할 때는 빈 주차장에서, 새벽이나 일요일 오전에 하십시오.")
    val sensePlaceSuggest = p("sense_place_suggest", PR, "{place}는 그 시간에 한적합니다.")
    val sensePlaceStopSpots = p("sense_place_stop_spots", PR, "코스 중간에 정차할 수 있는 곳은 {spot}입니다.")
    // 4-1 브레이크
    val senseBrakeIntro = p("sense_brake_intro", PR, "D에서 브레이크 페달을 살짝 놓으면 차가 천천히 움직입니다. 이 상태에서 세 가지 세기로 제동을 연습합니다.")
    val senseBrakeTrySoft = p("sense_brake_try_soft", PR, "브레이크 페달을 부드럽게 밟아 서서히 정지하십시오.")
    val senseBrakeTryMid = p("sense_brake_try_mid", PR, "이번에는 중간 세기로 밟으십시오.")
    val senseBrakeTryHard = p("sense_brake_try_hard", PR, "마지막은 강하게 밟아 급정지하십시오. 느린 속도이므로 위험하지 않습니다.")   // 페르소나 재검증: 겁 많은 운전자 안심
    val senseBrakePass = p("sense_brake_pass", PR, "세 가지 세기의 제동을 마쳤습니다.")
    // 4-2 액셀
    val senseAccelIntro = p("sense_accel_intro", PR, "N에서는 가속 페달을 밟아도 차가 움직이지 않습니다. 그래서 N에서 먼저 연습합니다.")
    val senseAccelBrakeCheck = p("sense_accel_brake_check", PR, "N인지 확인하십시오. 왼발은 사용하지 말고, 오른발은 바로 브레이크 페달로 옮길 수 있게 준비하십시오.")
    val senseAccelTry = p("sense_accel_try", PR, "가속 페달을 부드럽게 밟아 회전수를 1,000에서 2,000 사이로 맞추십시오.")
    val senseAccelTooHigh = p("sense_accel_too_high", PR, "조금 셉니다. 가속 페달을 살짝 놓으십시오.")
    val senseAccelHold = p("sense_accel_hold", PR, "그대로 3초간 유지하십시오.")
    val senseAccelPass = p("sense_accel_pass", PR, "출발할 때 밟는 세기입니다. 연습을 마쳤습니다.")
    val senseSteerCircleIntro = p("sense_steer_circle_intro", PR, "저속으로 원을 그리며 주행하십시오. 뒷바퀴는 앞바퀴보다 안쪽으로 지나갑니다.")
    val senseSteerCircleReturn = p("sense_steer_circle_return", PR, "회전한 뒤에는 스티어링 휠을 풀어야 바퀴가 똑바로 돌아옵니다. 손에 힘을 살짝 빼면 저절로 돌아옵니다.")
    val senseSteerLargeCar = p("sense_steer_large_car", PR, "이 차는 크기가 크므로 차폭과 길이를 더 넉넉하게 보십시오.", variant = "large_car")
    // 4-4 후진
    val senseReverseIntro = p("sense_reverse_intro", PR, "R로 변속하면 후방 카메라 화면이 표시됩니다. 카메라와 미러를 번갈아 보십시오.")
    val senseReverseRule = p("sense_reverse_rule", PR, "후진 중에는 가속 페달을 밟지 마십시오. 브레이크 페달만 살짝 놓아서 이동합니다.")
    val senseReverseTryStraight = p("sense_reverse_try_straight", PR, "R로 변속하고 브레이크 페달을 살짝 놓아 똑바로 후진하십시오.")
    val senseReverseTryLeft = p("sense_reverse_try_left", PR, "스티어링 휠을 반시계 방향으로 돌리십시오. 차의 뒤쪽이 왼쪽으로 갑니다.")
    val senseReverseTryRight = p("sense_reverse_try_right", PR, "스티어링 휠을 시계 방향으로 돌리십시오. 차의 뒤쪽이 오른쪽으로 갑니다.")
    val senseReversePass = p("sense_reverse_pass", PR, "후진 연습을 마쳤습니다.")
    // 4-6 경사로
    val senseSlopeUpIntro = p("sense_slope_up_intro", PR, "오르막에서 출발할 때는 오토 홀드를 켜십시오. 가속 페달을 살짝 밟으면 오토 홀드가 해제되면서 출발합니다.")
    val senseSlopeUpTry = p("sense_slope_up_try", PR, "오토 홀드를 켜고 출발하십시오.")
    val senseSlopeDownIntro = p("sense_slope_down_intro", PR, "내리막에서는 브레이크 페달을 나누어 밟으십시오. 계속 밟고 있으면 브레이크가 과열됩니다.")
    val senseSlopeEngineBrake = p("sense_slope_engine_brake", PR, "긴 내리막에서는 낮은 단으로 변속하십시오. 엔진 브레이크가 속도를 줄여 줍니다.")
    val senseSlopePass = p("sense_slope_pass", PR, "경사로 연습을 마쳤습니다.")
    // 4-7 차선 변경 절차 (정차 상태)
    val senseLaneIntro = p("sense_lane_intro", PR, "차로 변경 순서입니다. 미러, 어깨 너머 확인, 방향지시등 3초, 이동 순입니다.")
    val senseLaneStepMirror = p("sense_lane_step_mirror", PR, "왼쪽 실외 미러를 보십시오.")
    val senseLaneStepShoulder = p("sense_lane_step_shoulder", PR, "어깨 너머로 한 번 확인하십시오.")
    val senseLaneStepSignal = p("sense_lane_step_signal", PR, "방향지시등을 켜고 3초 기다리십시오.")
    val senseLaneStepMove = p("sense_lane_step_move", PR, "안전하면 이동하십시오. 위험하면 이동하지 마십시오.")
    val senseLanePretend = p("sense_lane_pretend", PR, "처음에는 이동하지 말고 미러, 어깨 너머, 방향지시등까지만 하십시오.")
    val senseLaneRoadSuggest = p("sense_lane_road_suggest", PR, "{road}는 새벽에 한적합니다. 3차로 도로라 연습하기에 적합합니다.")
    val senseLaneCountDone = p("sense_lane_count_done", PR, "오늘 차로 변경을 {count}번 했습니다. 오늘 연습은 여기까지입니다.")
    val senseLaneMergeGap = p("sense_lane_merge_gap", PR, "들어가도 되는 때는 뒤차가 미러에 작게 보이고, 내 속도가 옆 차로와 비슷할 때입니다.")
    val senseLanePass = p("sense_lane_pass", PR, "차로 변경 순서를 마쳤습니다.")

    // ───────────────────────────────── 주차 (4-5, parking 모듈 I 의 ID 규칙) ─────────────────────────────────
    private val PK = PhraseCategory.PARKING
    // 사전 학습 (정차 중, 일러스트와 함께)
    val parkingPrelearnIntro = p("parking_prelearn_intro", PK, "주차는 순서대로 합니다. 기준점은 네 개입니다.", 40)
    val parkingPrelearnPoint1 = p("parking_prelearn_point1", PK, "하나. 어깨가 목표 칸 가운데에 오면 정지.")
    val parkingPrelearnPoint2 = p("parking_prelearn_point2", PK, "둘. 왼쪽 끝까지, 미러에 칸 모서리.")
    val parkingPrelearnPoint3 = p("parking_prelearn_point3", PK, "셋. R, 오른쪽 끝까지, 선과 나란하면 풂.")
    val parkingPrelearnPoint4 = p("parking_prelearn_point4", PK, "넷. 똑바로 후진, 뒤 선이 보이면 정지.")
    val parkingPrelearnTall = p("parking_prelearn_tall", PK, "키가 크면 선이 일찍 보이니 반 발 더 가십시오.", variant = "tall")
    val parkingPrelearnShort = p("parking_prelearn_short", PK, "키가 작으면 선이 늦게 보이니 바로 정지하십시오.", variant = "short")
    val parkingPrelearnNextPrompt = p("parking_prelearn_next_prompt", PK, "'다음'을 누르면 넘어갑니다.")
    // 슬로우 모드 (한 동작 한 문장, 기준점 포함)
    /** 직각 주차 시작 전 설명 화면(무엇을 하는 연습인지 모른 채 첫 지시가 'D' 였음). 정차 중이라 길게. */
    /** 도로 법규 마지막 주제에서 '끝'. */
    val lawAllDone = p("law_all_done", PR, "도로 법규를 모두 보았습니다. 본 주제는 버튼이 연한 연두색으로 표시되고, 누르면 다시 볼 수 있습니다.")
    val lawEndSome = p("law_end_some", PR, "마지막 주제까지 보았습니다. 버튼이 흰색인 주제는 아직 보지 않은 것입니다.")
    val parkingIntro = p("parking_intro", PR, "직각 주차 연습입니다. 빈 주차장에서 실제로 차를 아주 천천히 움직이며 따라 합니다. 브레이크 페달을 밟은 채 시작하고, 한 동작씩 안내하면 그대로 한 뒤 정지하십시오. 차가 움직이는 동안에는 화면이 가려지고 음성으로만 안내합니다. 준비되면 '시작'을 누르십시오.")
    val parkingSlowStart = p("parking_slow_start", PK, "천천히 한 가지씩 안내합니다.")
    val parkingStepGearD = p("parking_step_gear_d", PK, "칸을 오른쪽에 두고 D로 변속하십시오.")
    val parkingStepApproach = p("parking_step_approach", PK, "어깨가 목표 칸 가운데에 오면 정지하십시오.")
    val parkingRefApproach = p("parking_ref_approach", PK, "어깨를 목표 칸 가운데에 맞추십시오.")
    val parkingStepSteerLeftFull = p("parking_step_steer_left_full", PK, "스티어링 휠을 반시계 방향으로 끝까지 돌리십시오.")
    val parkingStepForward45 = p("parking_step_forward45", PK, "오른쪽 미러에 칸 모서리가 보이면 정지하십시오.")
    val parkingRefForward45 = p("parking_ref_forward45", PK, "오른쪽 미러에 칸 앞 모서리를 맞추십시오.")
    val parkingStepGearR = p("parking_step_gear_r", PK, "R로 변속하십시오.")
    val parkingStepSteerRightFull = p("parking_step_steer_right_full", PK, "스티어링 휠을 시계 방향으로 끝까지 돌리십시오.")
    val parkingStepReverseIn = p("parking_step_reverse_in", PK, "천천히 후진, 칸 선과 나란하면 정지하십시오.")
    val parkingRefReverseIn = p("parking_ref_reverse_in", PK, "양쪽 미러에 주차선을 나란히 맞추십시오.")
    val parkingStepCenterWheel = p("parking_step_center_wheel", PK, "스티어링 휠을 가운데로 되돌리십시오.")
    val parkingStepStraightBack = p("parking_step_straight_back", PK, "똑바로 후진, 뒤 선이 보이면 정지하십시오.")
    val parkingRefStraightBack = p("parking_ref_straight_back", PK, "미러에 뒤 선이 보이면 범퍼가 가깝습니다.")
    val parkingSlowStepP = p("parking_slow_step_p", PK, "P로 변속하십시오.")
    val parkingStopFirst = p("parking_stop_first", PK, "먼저 정지한 뒤 확인하십시오.")
    val parkingSlowWaiting = p("parking_slow_waiting", PK, "마쳤으면 '다음'을 누르십시오.")
    val parkingSlowRepeatAsk = p("parking_slow_repeat_ask", PK, "같은 기준점을 다시 안내할까요?")
    // 탈출법 (꼬였을 때)
    val parkingRecoverChoose = p("parking_recover_choose", PK, "너무 붙음, 비스듬함, 너무 멂 중 고르십시오.")   // 페르소나 재검증: 보기 형태 통일
    val parkingEscapeTooClose = p("parking_escape_too_close", PK, "너무 붙었습니다. D로 빠져나와 다시 하십시오.")
    val parkingEscapeAngle = p("parking_escape_angle", PK, "비스듬합니다. 앞으로 빼며 칸과 맞추십시오.")
    val parkingEscapeTooFar = p("parking_escape_too_far", PK, "너무 멉니다. 앞으로 빼십시오.")
    val parkingEscapeRestart = p("parking_escape_restart", PK, "처음부터 다시 합니다. 칸 옆에 나란히 서십시오.")
    val parkingRecoverGearD = p("parking_recover_gear_d", PK, "D로 변속하십시오.")
    val parkingRecoverTooCloseForward = p("parking_recover_too_close_forward", PK, "전진하다 왼쪽 미러에 틈이 보이면 정지하십시오.")
    val parkingRecoverTooCloseRef = p("parking_recover_too_close_ref", PK, "기준점은 왼쪽 미러의 옆 선과 틈입니다.")
    val parkingRecoverAngledForwardAlign = p("parking_recover_angled_forward_align", PK, "앞으로 빼다 선과 나란하면 정지하십시오.")
    val parkingRecoverTooFarForward = p("parking_recover_too_far_forward", PK, "조금 전진, 미러에 칸이 안 보이면 정지하십시오.")
    val parkingRecoverTooFarRef = p("parking_recover_too_far_ref", PK, "기준점은 오른쪽 미러의 칸 안쪽입니다.")
    val parkingRetryOk = p("parking_retry_ok", PK, "다시 해 보십시오.")
    // 확인·자기평가·마무리
    val parkingDoneSelfcheck = p("parking_done_selfcheck", PK, "주차를 마쳤습니다. 내려서 뒤 간격과 앞뒤 위치를 확인하십시오.", 40)
    val parkingSelfevalAsk = p("parking_selfeval_ask", PK, "뒤 간격, 각도, 앞뒤 위치를 평가하십시오.")
    val parkingSelfevalGood = p("parking_selfeval_good", PK, "다음에도 같은 순서로 하십시오.")
    val parkingSelfevalAngle = p("parking_selfeval_angle", PK, "다음에는 스티어링 휠을 조금 일찍 푸십시오.")
    val parkingHintCurbGap = p("parking_hint_curb_gap", PK, "다음에는 옆 간격부터 맞추십시오.")
    val parkingHintFrontBack = p("parking_hint_front_back", PK, "다음에는 미러로 뒤 선을 보며 정지하십시오.")
    val parkingLoopStars = p("parking_loop_stars", PK, "별 {stars}개입니다. 순서를 잘 지켰습니다.")
    val parkingLoopRestAsk = p("parking_loop_rest_ask", PK, "휴식과 기준점 재확인 중 고르십시오.")
    val parkingFinishP = p("parking_finish_p", PK, "P로 변속하십시오.")
    val parkingFinishEpb = p("parking_finish_epb", PK, "주차 브레이크를 거십시오.")
    val parkingFinishEngineOff = p("parking_finish_engine_off", PK, "시동을 끄십시오.")
    val parkingFinishWindows = p("parking_finish_windows", PK, "창문을 닫으십시오.")
    val parkingFinishLights = p("parking_finish_lights", PK, "전조등이 꺼졌는지 확인하십시오.")
    // (실제 차 평행 주차) 평행 주차 공식(ParallelParkingProcedure) — D·R·핸들·P·주차브레이크 멘트는 직각 주차와 같은 것을 쓴다
    val parkingParIntro = p("parking_par_intro", PR, "평행 주차 연습입니다. 길가 빈 칸의 앞차 옆에서 시작해 실제로 차를 아주 천천히 움직이며 따라 합니다. 브레이크 페달을 밟은 채 시작하고, 한 동작씩 안내하면 그대로 한 뒤 정지하십시오. 차가 움직이는 동안에는 화면이 가려지고 음성으로만 안내합니다. 준비되면 '시작'을 누르십시오.")
    val parkingParStepApproach = p("parking_par_step_approach", PK, "앞차 옆에 나란히 서면 정지하십시오.")
    val parkingParRefApproach = p("parking_par_ref_approach", PK, "앞차와 1미터쯤 띄우고 뒤 범퍼를 맞추십시오.")
    val parkingParStepBack1 = p("parking_par_step_back1", PK, "똑바로 1미터쯤 후진하다 정지하십시오.")
    val parkingParRefBack1 = p("parking_par_ref_back1", PK, "앞차 뒤 끝이 내 뒷바퀴쯤 오면 됩니다.")
    val parkingParStepReverse45 = p("parking_par_step_reverse45", PK, "천천히 후진, 45도쯤 되면 정지하십시오.")
    val parkingParRefReverse45 = p("parking_par_ref_reverse45", PK, "왼쪽 미러에 뒤차 앞면이 다 보이면 45도입니다.")
    val parkingParStepBack2 = p("parking_par_step_back2", PK, "똑바로 후진, 앞 끝이 앞차를 지나면 정지하십시오.")
    val parkingParRefBack2 = p("parking_par_ref_back2", PK, "오른쪽 앞 모서리가 앞차 뒤 끝을 지나면 됩니다.")
    val parkingParStepReverseIn = p("parking_par_step_reverse_in", PK, "천천히 후진, 길과 나란하면 정지하십시오.")
    val parkingParRefReverseIn = p("parking_par_ref_reverse_in", PK, "오른쪽 미러에 연석이 나란히 보이면 됩니다.")
    val parkingParStepAdjust = p("parking_par_step_adjust", PK, "조금 움직여 앞뒤 간격을 맞추고 정지하십시오.")
    val parkingParRefAdjust = p("parking_par_ref_adjust", PK, "앞차와 뒤차 사이 가운데에 서십시오.")
    val parkingParRecoverChoose = p("parking_par_recover_choose", PK, "연석에 붙음, 비스듬함, 연석과 멂 중 고르십시오.")
    val parkingParEscapeCurb = p("parking_par_escape_curb", PK, "연석에 붙었습니다. 앞으로 빼며 맞추십시오.")
    val parkingParEscapeAngle = p("parking_par_escape_angle", PK, "비스듬합니다. 앞뒤로 나눠 맞추십시오.")
    val parkingParEscapeFar = p("parking_par_escape_far", PK, "연석과 멉니다. 빠져나와 다시 하십시오.")
    val parkingParRecoverCurbForward = p("parking_par_recover_curb_forward", PK, "천천히 전진, 길과 나란하면 정지하십시오.")
    val parkingParRecoverAngleForward = p("parking_par_recover_angle_forward", PK, "앞차에 닿지 않게 조금 전진 후 정지하십시오.")
    val parkingParRecoverFarOut = p("parking_par_recover_far_out", PK, "앞차 옆으로 빠져나와 나란히 서십시오.")
    val parkingParRestart = p("parking_par_restart", PK, "처음부터 다시 합니다. 앞차 옆에 나란히 서십시오.")
    val parkingParDoneSelfcheck = p("parking_par_done_selfcheck", PK, "주차를 마쳤습니다. 내려서 연석 간격과 앞뒤 위치를 확인하십시오.", 40)
    val parkingParSelfevalAsk = p("parking_par_selfeval_ask", PK, "연석 간격, 각도, 앞뒤 위치를 평가하십시오.")
    val parkingParHintFrontBack = p("parking_par_hint_front_back", PK, "다음에는 앞뒤 차 간격을 보며 정지하십시오.")
    val parkingFinishDone = p("parking_finish_done", PK, "주차 마무리를 마쳤습니다.")

    // ───────────────────────────────── 리포트 (5-4, 5-5, report 모듈 M) ─────────────────────────────────
    private val R = PhraseCategory.REPORT
    val reportIntro = p("report_intro", R, "오늘 주행 리포트입니다.")
    val reportGood = p("report_good", R, "잘한 항목은 {item}입니다.")
    val reportImproved = p("report_improved", R, "지난번보다 나아진 항목은 {item}입니다.")
    val reportNext = p("report_next", R, "다음에 고칠 항목은 {item}입니다.")
    // {item} 자리에 들어가는 지표 이름 (M 요청, ReportComposer.phraseId 규칙 report_item_<key>)
    val reportItemSignalUsage = p("report_item_signal_usage", R, "방향지시등 켜기")
    val reportItemSignalLead = p("report_item_signal_lead", R, "방향지시등 켜고 기다리기")
    val reportItemHardBrake = p("report_item_hard_brake", R, "부드럽게 제동하기")
    val reportItemOverspeed = p("report_item_overspeed", R, "제한 속도 지키기")
    val reportItemStopLine = p("report_item_stop_line", R, "정지선 앞에서 정지하기")
    val reportCoachCount = p("report_coach_count", R, "오늘 코치가 {count}번 안내했습니다.")
    val reportStatSignal = p("report_stat_signal", R, "방향지시등 사용률은 {percent}%입니다.")
    val reportStatSignalDelay = p("report_stat_signal_delay", R, "방향지시등을 켜고 이동하기까지 평균 {seconds}초입니다.")
    val reportStatHardBrake = p("report_stat_hard_brake", R, "급제동은 {count}번입니다.")
    val reportStatSpeeding = p("report_stat_speeding", R, "과속 구간은 {count}곳입니다.")
    val reportStatStopLine = p("report_stat_stop_line", R, "정지선 앞 정지를 {count}번 연속 지켰습니다.")
    val reportStatMerge = p("report_stat_merge", R, "합류할 때의 속도는 {speed}입니다.")
    val reportLaneChangeCount = p("report_lane_change_count", R, "차로 변경은 {count}번입니다.")
    val reportPanicNotFailure = p("report_panic_not_failure", R, "중간에 쉰 것은 실수로 세지 않습니다.")
    val reportExamCandidates = p("report_exam_candidates", R, "감점 후보는 {count}개입니다. 합격선까지 {points}점입니다.")
    val reportGraduation = p("report_graduation", R, "10번째 주행을 마쳤습니다. 초보 졸업입니다.")
    val reportEnd = p("report_end", R, "주행 리포트를 마칩니다.")

    // ───────────────────────────────── 시스템 (명령 응답, 무응답, C/G 모듈) ─────────────────────────────────
    private val S = PhraseCategory.SYSTEM
    val systemWelcomeCar = p("system_welcome_car", S, "현재 차량은 {car_model}입니다.")
    val systemWelcomeDefaultCar = p("system_welcome_default_car", S, "차종을 확인하지 못해 {car_model} 기준으로 안내합니다.")   // 차가 차종을 알려 주지 않을 때
    val systemCmdAgain = p("system_cmd_again", S, "다시 안내합니다.")
    val systemQuietAck = p("system_quiet_ack", S, "안내를 멈춥니다. 위험할 때만 안내합니다.")       // C/G 요청
    val systemCmdQuietOff = p("system_cmd_quiet_off", S, "안내를 다시 시작합니다.")
    val systemCmdConfirmAck = p("system_cmd_confirm_ack", S, "확인.", 6)
    val systemCmdNextAck = p("system_cmd_next_ack", S, "다음.", 6)
    val systemNoInputReprompt = p("system_no_input_reprompt", S, "마쳤으면 '다음'을 누르십시오.")   // C/G 요청, 1회만
    val systemNoInputRepromptConfirm = p("system_no_input_reprompt_confirm", S, "마쳤으면 '확인'을 누르십시오.")   // 주차 단계 화면의 버튼은 '확인'
    val systemModeParkedHint = p("system_mode_parked_hint", S, "정차했습니다. 이제 화면을 볼 수 있습니다.")     // C/G 요청
    val systemWheelModeHint = p("system_wheel_mode_hint", S, "짧게 누르면 확인, 길게 누르면 정차할 곳을 찾습니다.")   // C/G 요청
    val systemDetectFail = p("system_detect_fail", S, "지금은 감지되지 않습니다. 마쳤으면 '다음'을 누르십시오.")
    val systemDrivingVoiceOnly = p("system_driving_voice_only", S, "주행 중에는 음성으로만 안내합니다.")
    val systemCommandsDriving = p("system_commands_driving", S, "주행 중에는 휠 버튼 짧게 '다음', 길게 '세울래요'입니다.")
    // S 요청 (앱 연결, CoachController): 주행 모드 진입/복귀 한마디, 미구현 메뉴 안내
    val systemDriveStart = p("system_drive_start", S, "출발합니다. 주행 중에는 음성으로만 안내합니다.")
    val systemDriveEnd = p("system_drive_end", S, "정차했습니다. 이제 화면을 볼 수 있습니다.")
    val systemFeatureNotReady = p("system_feature_not_ready", S, "이 기능은 아직 준비 중입니다.")
    // S 요청 (코스 주행 내비 연동, 5-1): 코스를 차량 내비에 넣었을 때 / 내비가 경로를 못 찾았을 때 한마디
    val systemCourseSent = p("system_course_sent", S, "내비게이션에 코스를 보냈습니다. 경로가 표시되면 출발하십시오.")
    val systemCourseRouteFailed = p("system_course_route_failed", S, "경로를 찾지 못했습니다. 다른 코스를 선택하십시오.")

    // ───────────────────────────────── 조회 ─────────────────────────────────

    /**
     * 다른 나라 도로 법규 글([kr.drivecoach.core.law.ForeignLaws])을 멘트로 등록한다. ID 는 `law_<나라>_<주제>`.
     * 같은 ID·같은 글을 다시 부르면 처음 등록한 것을 돌려주고, 글이 다르면 충돌로 보고 멈춘다. 한국 법규와 같은 길이 제한(170자).
     */
    @Synchronized
    fun registerLaw(id: String, text: String): Phrase {
        require(id.startsWith("law_")) { "법규 멘트 ID 는 law_ 로 시작해야 함: $id" }
        registry[id]?.let { require(it.text == text) { "법규 멘트 ID 충돌: $id" }; return it }
        require(text.length <= LAW_MAX) { "법규 글이 ${LAW_MAX}자를 넘음: $id (${text.length}자)" }
        return p(id, PhraseCategory.PRACTICE, text, LAW_MAX)
    }

    /** 등록된 모든 멘트(등록 순서). */
    val all: List<Phrase> get() = registry.values.toList()

    /** ID로 멘트를 찾는다. 없으면 예외(멘트 누락은 개발 단계에서 잡아야 한다). */
    fun get(id: PhraseId): Phrase = registry[id.id] ?: throw NoSuchElementException("멘트 없음: ${id.id}")

    /** ID로 멘트를 찾되 없으면 null. */
    fun find(id: PhraseId): Phrase? = registry[id.id]

    /** 카테고리별 멘트 목록. */
    fun byCategory(category: PhraseCategory): List<Phrase> = registry.values.filter { it.category == category }

    /**
     * `{이름}` 자리를 채운 문장을 만든다. 값이 없는 자리는 그대로 남긴다(테스트에서 잡히도록).
     * 예: render(guide_turn_right_300m, mapOf("distance" to "300")) → "300m 앞 우회전. ..."
     */
    fun render(id: PhraseId, vars: Map<String, String> = emptyMap()): String {
        val phrase = get(id)
        return Phrase.PLACEHOLDER.replace(phrase.text) { m -> vars[m.groupValues[1]] ?: m.value }
    }
}
