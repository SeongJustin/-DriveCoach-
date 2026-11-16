// 멘트 카탈로그·발화 정책 테스트
package kr.drivecoach.core.tts

import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.assertNotNull
import kotlin.test.assertFailsWith

private fun req(phrase: Phrase, force: Boolean = false) = SpeechRequest.of(phrase, force)
private val driving = SpeechContext(mode = DriveMode.DRIVING)
private val parked = SpeechContext(mode = DriveMode.PARKED)

// ───────────── Phrases ─────────────

fun testCatalogIdsUniqueAndSnakeCase() {
    val ids = Phrases.all.map { it.id.id }
    assertEquals(ids.size, ids.toSet().size, "멘트 ID 중복")
    val re = Regex("[a-z0-9]+(_[a-z0-9]+)*")
    ids.forEach { assertTrue(re.matches(it), "snake_case 아님: $it") }
    assertTrue(Phrases.all.size >= 300, "멘트 수가 너무 적음: ${Phrases.all.size}")
}

/** 다른 모듈이 코드에 적어 둔 PhraseId 리터럴과 practice 항목이 만드는 ID 가 전부 카탈로그에 있어야 한다. */
fun testOtherModulesPhraseIdsExist() {
    // practice 모듈(D)의 항목별 explain/try/pass/confirm
    kr.drivecoach.core.practice.Chapter2Items.all.forEach { item ->
        listOf(item.explainPhrase, item.tryPhrase, item.passPhrase).forEach { assertNotNull(Phrases.find(it), "practice 멘트 없음: ${it.id}") }
        val manual = when (val c = item.check) {
            is kr.drivecoach.core.practice.CheckKind.Manual -> c
            is kr.drivecoach.core.practice.CheckKind.Either -> c.manual
            else -> null
        }
        manual?.let { assertNotNull(Phrases.find(it.confirmPhrase), "practice confirm 멘트 없음: ${it.confirmPhrase.id}") }
    }
    // warning 모듈(J)의 경고등별 행동 멘트: ID 가 있고 문장이 WarningLamp.action 과 같아야 한다
    kr.drivecoach.core.warning.WarningLamps.all.forEach { lamp ->
        val ph = Phrases.find(lamp.actionPhraseId)
        assertNotNull(ph, "경고등 행동 멘트 없음: ${lamp.actionPhraseId.id}")
        assertEquals(lamp.action, ph.text, "경고등 행동 멘트가 WarningLamp.action 과 다름: ${lamp.id}")
    }
    // 소스 안의 PhraseId("...") 리터럴 (프로젝트 루트에서 실행할 때만 검사하고, 폴더가 없으면 건너뛴다)
    val root = java.io.File("core/src/main/kotlin/kr/drivecoach/core")
    if (!root.isDirectory) return
    val literal = Regex("PhraseId\\(\"([^\"$]+)\"\\)")
    val missing = root.walkTopDown().filter { it.isFile && it.extension == "kt" && !it.path.contains("/tts/") }
        .flatMap { f -> literal.findAll(f.readText()).map { it.groupValues[1] } }
        .distinct().filter { Phrases.find(PhraseId(it)) == null }.toList()
    assertTrue(missing.isEmpty(), "다른 모듈이 쓰는 멘트가 카탈로그에 없음: $missing")
}

fun testEveryCategoryHasPhrases() {
    PhraseCategory.values().forEach { c -> assertTrue(Phrases.byCategory(c).isNotEmpty(), "카테고리 비어 있음: $c") }
}

fun testLengthHints() {
    Phrases.all.forEach { ph ->
        assertTrue(ph.nominalLength <= ph.maxLengthHint, "길이 초과(${ph.nominalLength}>${ph.maxLengthHint}): ${ph.id.id} '${ph.text}'")
    }
    // 위험 피드백은 10자 안팎 한 문장, 변수 없음
    Phrases.byCategory(PhraseCategory.DANGER).forEach { ph ->
        assertTrue(ph.text.length <= 12, "위험 멘트가 너무 김: ${ph.id.id}")
        assertTrue(ph.placeholders.isEmpty(), "위험 멘트에 변수 금지: ${ph.id.id}")
        assertEquals(1, ph.text.count { it == '.' }, "위험 멘트는 한 문장: ${ph.id.id}")
    }
}

fun testNoSingleWordScolding() {
    // "깜빡이" 처럼 한 단어로 끝나는 지적 금지 (합류 세 마디 "깜빡이."는 안내이므로 예외)
    val allowedSingleWord = setOf("guide_merge_signal", "guide_merge_speed", "guide_merge_go", "parking_finish_p", "parking_finish_epb",
        "system_cmd_confirm_ack", "system_cmd_next_ack", "danger_reassure")
    Phrases.all.filter { it.id.id.startsWith("danger_") || it.id.id.contains("remind") }.forEach { ph ->
        if (ph.id.id !in allowedSingleWord) assertTrue(ph.text.trim().split(" ").size >= 2, "한 단어 지적: ${ph.id.id}")
    }
}

fun testRequiredIdsExist() {
    listOf(
        "danger_speeding", "danger_hard_brake", "danger_hard_lane_change", "danger_reverse_rpm",
        "guide_turn_right_300m", "guide_lane_change_mirror_left", "guide_lane_change_signal", "guide_lane_change_move",
        "guide_merge_signal", "guide_merge_speed", "guide_merge_go", "guide_turn_left_unprotected",
        "panic_pull_over", "panic_highway_keep_straight",
        "warning_lamp_green", "warning_lamp_yellow", "warning_lamp_red",
        "practice_2_6_wiper_explain", "practice_2_4_sudden_accel_try", "practice_2_4_sudden_accel_quiz_q1",
        "parking_step_gear_r", "parking_escape_too_close",
        "report_good", "report_improved", "report_next", "report_item_signal_usage",
        "system_no_input_reprompt", "system_cmd_again", "system_quiet_ack",
    ).forEach { assertNotNull(Phrases.find(PhraseId(it)), "필수 멘트 없음: $it") }
    assertEquals("마쳤으면 '다음'을 누르십시오.", Phrases.get(PhraseId("system_no_input_reprompt")).text)
    // 2장 항목 묶음은 practice 모듈의 ID 규칙과 1:1
    assertEquals("practice_2_1_seat_fore_aft_confirm", Phrases.item21SeatForeAft.confirm?.id?.id)
    assertEquals(null, Phrases.item25HeadlightLow.confirm, "자동 감지 항목에는 confirm 없음")
    assertNotNull(Phrases.item25Hazard.confirm, "비상등은 신호가 없어 수동 확인 허용(베타 A-06)")
    assertFailsWith<NoSuchElementException> { Phrases.get(PhraseId("no_such_phrase")) }
}

fun testRenderPlaceholders() {
    assertEquals(listOf("distance"), Phrases.guideTurnRight300m.placeholders)
    val out = Phrases.render(Phrases.guideTurnRight300m.id, mapOf("distance" to "300"))
    assertEquals("300m 앞 우회전입니다. 방향지시등을 켜고 오른쪽 차로로 가십시오.", out)
    // 값이 없으면 자리 그대로 남는다
    assertTrue(Phrases.render(Phrases.panicPullOver.id, mapOf("distance" to "200")).contains("{spot}"))
    // 변수 표기는 전부 소문자 snake_case
    Phrases.all.forEach { ph -> ph.placeholders.forEach { assertTrue(Regex("[a-z_]+").matches(it), "변수명: ${ph.id.id}") } }
}

// ───────────── SpeechPolicy ─────────────

fun testPriorityOrder() {
    val p = SpeechPolicy.Companion
    assertTrue(p.priorityOf(PhraseCategory.DANGER) < p.priorityOf(PhraseCategory.PANIC))
    assertTrue(p.priorityOf(PhraseCategory.PANIC) < p.priorityOf(PhraseCategory.WARNING_LAMP))
    assertTrue(p.priorityOf(PhraseCategory.WARNING_LAMP) < p.priorityOf(PhraseCategory.GUIDE))
    assertTrue(p.priorityOf(PhraseCategory.GUIDE) < p.priorityOf(PhraseCategory.BRIEFING))
    assertEquals(p.priorityOf(PhraseCategory.BRIEFING), p.priorityOf(PhraseCategory.PRACTICE))
}

fun testDangerPreemptsGuide() {
    val policy = SpeechPolicy()
    val ctx = driving.copy(currentlySpeaking = PhraseCategory.GUIDE)
    val out = policy.decide(req(Phrases.dangerSpeeding), 1_000, ctx)
    assertEquals(Decision.SPEAK, out.decision)
    assertTrue(out.interrupt, "위험 멘트는 안내를 끊어야 함")
    // 반대로 안내는 위험 멘트 재생 중엔 기다린다
    val out2 = policy.decide(req(Phrases.guideStraight), 1_000, driving.copy(currentlySpeaking = PhraseCategory.DANGER))
    assertEquals(Decision.DEFER, out2.decision)
    assertNotNull(out2.retryAfterMs)
}

fun testRepeatSuppression() {
    val policy = SpeechPolicy()
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerSpeeding), 0, driving).decision)
    policy.onSpoken(Phrases.dangerSpeeding.id, 0)
    assertEquals("repeat_suppressed", policy.decide(req(Phrases.dangerSpeeding), 10_000, driving).reason)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.dangerSpeeding), 29_999, driving).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerSpeeding), 30_000, driving).decision)
    // 다른 위험 멘트는 바로 가능 (DANGER 는 최소 간격 예외)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerHardBrake), 100, driving).decision)
    // "다시" 는 재발화 금지를 무시
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerSpeeding, force = true), 10_000, driving).decision)
}

fun testMinGapBetweenUtterances() {
    val policy = SpeechPolicy()
    policy.onSpoken(Phrases.guideStraight.id, 0)
    val out = policy.decide(req(Phrases.guideTurnRight300m), 500, driving)
    assertEquals(Decision.DEFER, out.decision)
    assertEquals("min_gap", out.reason)
    assertEquals(1_000L, out.retryAfterMs)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideTurnRight300m), 1_500, driving).decision)
}

fun testNaviOverlapDefersExceptDanger() {
    val policy = SpeechPolicy()
    val ctx = driving.copy(naviSpeaking = true, naviSpeakingUntilMs = 3_000)
    val g = policy.decide(req(Phrases.guideTurnRight300m), 1_000, ctx)
    assertEquals(Decision.DEFER, g.decision)
    assertEquals("navi_overlap", g.reason)
    assertEquals(2_000L + 700L, g.retryAfterMs)
    // 종료 시각 모르면 기본 지연
    val g2 = policy.decide(req(Phrases.guideTurnRight300m), 1_000, driving.copy(naviSpeaking = true))
    assertEquals(2_000L + 700L, g2.retryAfterMs)
    // 위험은 내비 위로도 말한다
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerHardBrake), 1_000, ctx).decision)
    // 내비 끝난 직후 한 박자
    policy.onNaviFinished(5_000)
    assertEquals("after_navi_gap", policy.decide(req(Phrases.guideStraight), 5_200, driving).reason)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideStraight), 5_700, driving).decision)
}

fun testQuietMode() {
    val policy = SpeechPolicy()
    val ctx = driving.copy(quietMode = true)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.guideTurnRight300m), 0, ctx).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerSpeeding), 0, ctx).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.systemPullOverAck), 0, ctx).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.warningLampRed), 0, ctx).decision)
    // 시스템 멘트: 운전자 명령에 대한 직접 응답(force)만, 자동 멘트(출발/정차)는 막는다
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.systemCmdQuietOff, force = true), 0, ctx).decision)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.systemDriveStart), 0, ctx).decision)
}

fun testCoachingPausedAndWarningLamp() {
    val policy = SpeechPolicy()
    assertEquals("coaching_paused", policy.decide(req(Phrases.guideStraight), 0, driving.copy(coachingPaused = true)).reason)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.panicPullOver), 0, driving.copy(coachingPaused = true)).decision)
    assertEquals("warning_lamp_active", policy.decide(req(Phrases.guideStraight), 0, driving.copy(warningLampActive = true)).reason)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.warningLampYellow), 0, driving.copy(warningLampActive = true)).decision)
}

fun testFocusZoneMinimal() {
    val policy = SpeechPolicy()
    val merge = driving.copy(focusZone = FocusZone.MERGE)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideMergeSignal), 0, merge).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideMergeSpeed), 0, merge).decision)
    val nonEssential = policy.decide(req(Phrases.guideSignalRemind), 0, merge)
    assertEquals(Decision.DEFER, nonEssential.decision)
    assertEquals("focus_zone_nonessential", nonEssential.reason)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerSpeeding), 0, merge).decision)
    // 좌회전 구간에선 좌회전 멘트만
    val left = driving.copy(focusZone = FocusZone.LEFT_TURN)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideTurnLeft100mCheck), 0, left).decision)
    assertEquals(Decision.DEFER, policy.decide(req(Phrases.guideMergeSignal), 0, left).decision)
    // 집중 구간에서 내비 뒤 간격은 두 배
    policy.onNaviFinished(10_000)
    val out = policy.decide(req(Phrases.guideTurnLeft100mCheck), 10_000, left)
    assertEquals(Decision.DEFER, out.decision)
    assertEquals(1_400L, out.retryAfterMs)
}

fun testModeRestrictions() {
    val policy = SpeechPolicy()
    // 주행 중 브리핑·연습·리포트·주차 멘트는 버린다
    assertEquals("mode_driving", policy.decide(req(Phrases.briefingStart), 0, driving).reason)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.item21SeatForeAft.explain), 0, driving).decision)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.reportIntro), 0, driving).decision)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.parkingStepGearR), 0, driving).decision)
    // P 상태에선 주행 안내를 버리고 브리핑·리포트는 허용
    assertEquals(Decision.DROP, policy.decide(req(Phrases.guideTurnRight300m), 0, parked).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.briefingStart), 0, parked).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.reportGood), 0, parked).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.panicStoppedBreath), 0, parked).decision)
    // 저속 연습 모드에선 감각·주차 허용, 브리핑 불가
    val practice = SpeechContext(mode = DriveMode.PRACTICE)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.parkingStepGearR), 0, practice).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.senseSteerCircleIntro), 0, practice).decision)
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.dangerReverseRpm), 0, practice).decision)
    assertEquals(Decision.DROP, policy.decide(req(Phrases.briefingStart), 0, practice).decision)
}

fun testAdaptiveReduction() {
    val policy = SpeechPolicy()
    val ctx = driving.copy(followStreak = mapOf(Phrases.guideLaneChangeSignal.id to 3))
    assertEquals("adaptive_reduced", policy.decide(req(Phrases.guideLaneChangeSignal), 0, ctx).reason)
    // 놓치면(streak 리셋) 다시 늘린다
    val ctx2 = driving.copy(followStreak = mapOf(Phrases.guideLaneChangeSignal.id to 0))
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideLaneChangeSignal), 0, ctx2).decision)
    // 다른 멘트에는 영향 없음
    assertEquals(Decision.SPEAK, policy.decide(req(Phrases.guideLaneChangeMove), 0, ctx).decision)
}

fun testNoInputReminderSpokenOnceOnly() {
    val policy = SpeechPolicy()
    val p = Phrases.systemNoInputReprompt
    assertEquals(Decision.SPEAK, policy.decide(req(p), 0, parked).decision)
    policy.onSpoken(p.id, 0)
    assertEquals(Decision.DROP, policy.decide(req(p), 500, parked).decision)
    policy.reset()
    assertEquals(Decision.SPEAK, policy.decide(req(p), 500, parked).decision)
}

/** tts_clips/phrases.json(멘트 목록)이 있으면 모든 멘트 ID 가 그 목록에 있는지 본다. 이 저장소에는 그 파일이 없어 건너뛴다. */
fun testEveryPhraseHasClipEntry() {
    val f = java.io.File("tts_clips/phrases.json")
    if (!f.isFile) return
    val ids = Regex("\"id\":\\s*\"([a-z0-9_.]+)\"").findAll(f.readText()).map { it.groupValues[1] }.toSet()
    val missing = Phrases.all.map { it.id.id }.filter { it !in ids }
    assertTrue(missing.isEmpty(), "클립 목록에 없는 멘트 ${missing.size}개: ${missing.take(5)}")
}

fun main() {
    val tests = listOf(
        ::testCatalogIdsUniqueAndSnakeCase, ::testEveryCategoryHasPhrases, ::testLengthHints, ::testNoSingleWordScolding,
        ::testRequiredIdsExist, ::testRenderPlaceholders, ::testOtherModulesPhraseIdsExist,
        ::testPriorityOrder, ::testDangerPreemptsGuide, ::testRepeatSuppression, ::testMinGapBetweenUtterances,
        ::testNaviOverlapDefersExceptDanger, ::testQuietMode, ::testCoachingPausedAndWarningLamp, ::testFocusZoneMinimal,
        ::testModeRestrictions, ::testAdaptiveReduction, ::testNoInputReminderSpokenOnceOnly, ::testEveryPhraseHasClipEntry,
    )
    var failed = 0
    for (t in tests) {
        try { t(); println("  ok   ${t.name}") } catch (e: Throwable) { failed++; println("  FAIL ${t.name}: ${e.message}") }
    }
    println("SpeechPolicyTest: ${tests.size - failed}/${tests.size} passed")
    if (failed > 0) kotlin.system.exitProcess(1)
}
