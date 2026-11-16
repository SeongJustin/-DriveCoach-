// 발화 정책
package kr.drivecoach.core.tts

/** 발화 요청에 대한 판정. */
enum class Decision {
    /** 지금 말한다. 현재 재생 중인 것보다 우선순위가 높으면 호출자가 끊고 말한다. */
    SPEAK,
    /** 나중에 다시 요청한다. [SpeechOutcome.retryAfterMs] 뒤에 같은 요청을 다시 decide 에 넣는다. */
    DEFER,
    /** 버린다. 다시 요청하지 않는다. */
    DROP,
}

/** 판정 결과. [reason] 은 로그·리포트("오늘 코치가 몇 번 말했는지")용 짧은 영문 코드. */
data class SpeechOutcome(
    val decision: Decision,
    val reason: String,
    /** DEFER 일 때 최소 이만큼 뒤에 다시 시도. SPEAK/DROP 이면 null. */
    val retryAfterMs: Long? = null,
    /** SPEAK 인데 현재 재생 중인 멘트를 끊어야 하면 true. */
    val interrupt: Boolean = false,
)

/** 집중 구간(합류·좌회전·차선 변경에서는 안내를 최소 문장으로). */
enum class FocusZone { NONE, MERGE, LEFT_TURN, LANE_CHANGE }

/** 앱이 어떤 상태에서 말하려 하는지. P 상태에서만 화면 학습, 주행 중에는 음성만. */
enum class DriveMode {
    /** 주차(P) 상태. 브리핑·기능 연습·리포트가 허용된다. */
    PARKED,
    /** 빈 주차장 등에서의 저속 연습(4장). 감각 연습·주차 슬로우 모드가 허용된다. */
    PRACTICE,
    /** 도로 주행(5장). 안내·위험·공황·경고등·시스템만. */
    DRIVING,
}

/** 발화 요청 한 건. */
data class SpeechRequest(
    val phraseId: PhraseId,
    val category: PhraseCategory,
    /** 사용자가 "다시"라고 해서 다시 읽는 경우처럼 재발화 금지 간격을 무시해야 할 때 true. */
    val force: Boolean = false,
) {
    companion object {
        /** 카탈로그에서 카테고리를 찾아 요청을 만든다. */
        fun of(phrase: Phrase, force: Boolean = false) = SpeechRequest(phrase.id, phrase.category, force)
        fun of(id: PhraseId, force: Boolean = false) = of(Phrases.get(id), force)
    }
}

/** 판정에 필요한 주변 상황. 호출자(app 어댑터·코칭 엔진)가 채운다. */
data class SpeechContext(
    val mode: DriveMode = DriveMode.DRIVING,
    /** "조용히" 모드. 위험·공황·경고등·시스템 응답만 허용. */
    val quietMode: Boolean = false,
    /** 공황 대응으로 코칭이 멈춘 상태. 위험·공황·경고등·시스템만 허용. */
    val coachingPaused: Boolean = false,
    /** 차량 내비가 지금 안내 중인지. */
    val naviSpeaking: Boolean = false,
    /** 내비 안내가 끝날 것으로 예상되는 시각(ms). 모르면 null → 기본 지연을 쓴다. */
    val naviSpeakingUntilMs: Long? = null,
    /** 집중 구간. */
    val focusZone: FocusZone = FocusZone.NONE,
    /** TTS 가 지금 재생 중인 멘트의 카테고리. 없으면 null. */
    val currentlySpeaking: PhraseCategory? = null,
    /** 적응형: 이 안내를 운전자가 연속으로 잘 따른 횟수. 멘트 ID → 횟수. */
    val followStreak: Map<PhraseId, Int> = emptyMap(),
    /** 마지막 경고등(빨강/노랑) 대응 중이면 true. 그 동안 안내는 멈춘다. */
    val warningLampActive: Boolean = false,
)

/**
 * 발화 정책 설정값. 전부 ms. "한 번만, 짧게" 원칙을 숫자로 둔 것.
 */
data class SpeechPolicyConfig(
    /** 같은 ID 재발화 금지 간격. 카테고리별. */
    val minRepeatIntervalMs: Map<PhraseCategory, Long> = mapOf(
        PhraseCategory.DANGER to 30_000L,       // 같은 위험 피드백은 30초에 한 번
        PhraseCategory.PANIC to 15_000L,
        PhraseCategory.WARNING_LAMP to 60_000L, // 같은 경고등 안내는 1분에 한 번
        PhraseCategory.GUIDE to 10_000L,
        PhraseCategory.BRIEFING to 3_000L,
        PhraseCategory.PRACTICE to 3_000L,
        PhraseCategory.PARKING to 3_000L,
        PhraseCategory.REPORT to 3_000L,
        PhraseCategory.SYSTEM to 1_500L,
    ),
    /** 어떤 멘트든 직전 발화와 이만큼은 띄운다(DANGER 제외). 연속 수다 방지. */
    val minGapBetweenUtterancesMs: Long = 1_500L,
    /** 내비 안내 종료 시각을 모를 때 기본 지연. */
    val naviDefaultDeferMs: Long = 2_000L,
    /** 내비 안내가 끝난 뒤 코칭을 "한 박자" 늦추는 간격. 집중 구간에서는 두 배. */
    val afterNaviGapMs: Long = 700L,
    /** 적응형: 같은 안내를 이 횟수 이상 연속으로 잘 따르면 그 안내를 줄인다. */
    val adaptiveReduceAfter: Int = 3,
    /** 같은 상위 우선순위 멘트가 재생 중일 때 대기 간격. */
    val busyDeferMs: Long = 1_000L,
)

/**
 * 발화 정책. 순수 Kotlin, 상태는 "마지막으로 말한 시각" 뿐이다.
 *
 * 우선순위: DANGER > PANIC > WARNING_LAMP > GUIDE > (BRIEFING, PRACTICE, PARKING, REPORT, SYSTEM).
 * 규칙 순서(위에서 먼저 걸리는 것이 판정):
 *  1. 모드 허용: 주행 중 브리핑·연습·리포트 DROP, P 상태에서 주행 안내 DROP.
 *  2. 조용히 모드 / 코칭 중지 / 경고등 대응 중: 위험·공황·경고등·시스템만.
 *  3. 같은 ID 재발화 금지 간격 안이면 DROP (force 는 예외).
 *  4. 적응형: GUIDE 를 연속 N회 잘 따랐으면 DROP.
 *  5. 집중 구간: 구간 전용 최소 멘트만 SPEAK, 다른 GUIDE 는 DEFER, 나머지 하위는 DROP.
 *  6. 내비 안내 겹침: DANGER 만 즉시, 나머지는 내비 끝난 뒤 + 한 박자로 DEFER.
 *  7. 재생 중 멘트와 우선순위 비교: 더 높으면 끊고 SPEAK, 같거나 낮으면 DEFER.
 *  8. 직전 발화와 최소 간격(DANGER 제외) 안이면 DEFER.
 *
 * 사용법: decide() 가 SPEAK 를 주면 TTS 로 보내고 반드시 onSpoken() 을 호출한다.
 */
class SpeechPolicy(private val config: SpeechPolicyConfig = SpeechPolicyConfig()) {

    private val lastSpokenAtMs = HashMap<String, Long>()
    private var lastAnyUtteranceMs: Long? = null
    private var lastNaviEndMs: Long? = null

    /** 판정한다. 부수효과 없음. */
    fun decide(request: SpeechRequest, nowMs: Long, context: SpeechContext): SpeechOutcome {
        val cat = request.category
        val rank = priorityOf(cat)

        // 1. 모드 허용
        if (cat !in allowedIn(context.mode)) return drop("mode_${context.mode.name.lowercase()}")

        // 2. 조용히 / 코칭 중지 / 경고등 대응 중
        // 코칭 음성 꺼짐: 위험·공황·경고등만. 시스템 멘트는 운전자가 누른 것에 대한 직접 응답(force)만 허용 —
        // "출발합니다/정차했습니다/현재 차량은…" 같은 자동 시스템 멘트도 막는다.
        if (context.quietMode && cat !in QUIET_ALLOWED && !(cat == PhraseCategory.SYSTEM && request.force)) return drop("quiet_mode")
        if (context.coachingPaused && cat !in PAUSED_ALLOWED) return drop("coaching_paused")
        if (context.warningLampActive && cat !in PAUSED_ALLOWED) return drop("warning_lamp_active")

        // 3. 같은 ID 재발화 금지
        if (!request.force) {
            val last = lastSpokenAtMs[request.phraseId.id]
            val interval = config.minRepeatIntervalMs[cat] ?: 0L
            if (last != null && nowMs - last < interval) return drop("repeat_suppressed")
        }

        // 4. 적응형 축소 (안내만)
        if (cat == PhraseCategory.GUIDE && !request.force) {
            val streak = context.followStreak[request.phraseId] ?: 0
            if (streak >= config.adaptiveReduceAfter) return drop("adaptive_reduced")
        }

        // 5. 집중 구간
        if (context.focusZone != FocusZone.NONE && rank >= RANK_GUIDE) {
            if (cat == PhraseCategory.GUIDE) {
                if (!isFocusEssential(request.phraseId, context.focusZone)) return defer("focus_zone_nonessential", config.busyDeferMs * 3)
            } else {
                return drop("focus_zone")
            }
        }

        // 6. 내비 안내 겹침: 내비 우선(DANGER 제외)
        if (context.naviSpeaking && cat != PhraseCategory.DANGER) {
            val naviEnd = context.naviSpeakingUntilMs ?: (nowMs + config.naviDefaultDeferMs)
            val gap = if (context.focusZone != FocusZone.NONE) config.afterNaviGapMs * 2 else config.afterNaviGapMs
            return defer("navi_overlap", (naviEnd - nowMs).coerceAtLeast(0L) + gap)
        }
        // 내비가 방금 끝났으면 한 박자 쉬기
        lastNaviEndMs?.let { end ->
            val gap = if (context.focusZone != FocusZone.NONE) config.afterNaviGapMs * 2 else config.afterNaviGapMs
            if (cat != PhraseCategory.DANGER && nowMs - end < gap) return defer("after_navi_gap", gap - (nowMs - end))
        }

        // 7. 재생 중인 멘트와 비교
        context.currentlySpeaking?.let { playing ->
            val playingRank = priorityOf(playing)
            return if (rank < playingRank) SpeechOutcome(Decision.SPEAK, "preempt_${playing.name.lowercase()}", interrupt = true)
            else defer("busy_${playing.name.lowercase()}", config.busyDeferMs)
        }

        // 8. 최소 간격 (DANGER 는 예외)
        if (cat != PhraseCategory.DANGER) {
            lastAnyUtteranceMs?.let { last ->
                val since = nowMs - last
                if (since < config.minGapBetweenUtterancesMs) return defer("min_gap", config.minGapBetweenUtterancesMs - since)
            }
        }

        return SpeechOutcome(Decision.SPEAK, "ok")
    }

    /** TTS 로 실제로 보냈을 때 호출. 재발화 금지와 최소 간격의 기준 시각이 된다. */
    fun onSpoken(phraseId: PhraseId, nowMs: Long) {
        lastSpokenAtMs[phraseId.id] = nowMs
        lastAnyUtteranceMs = nowMs
    }

    /** 내비 안내가 끝났을 때 호출. 이후 한 박자(afterNaviGapMs) 동안 코칭을 늦춘다. */
    fun onNaviFinished(nowMs: Long) { lastNaviEndMs = nowMs }

    /** 세션 시작 등에서 기록을 비운다. */
    fun reset() { lastSpokenAtMs.clear(); lastAnyUtteranceMs = null; lastNaviEndMs = null }

    private fun drop(reason: String) = SpeechOutcome(Decision.DROP, reason)
    private fun defer(reason: String, afterMs: Long) = SpeechOutcome(Decision.DEFER, reason, retryAfterMs = afterMs.coerceAtLeast(0L))

    companion object {
        private const val RANK_GUIDE = 3

        /** 숫자가 작을수록 우선. */
        fun priorityOf(category: PhraseCategory): Int = when (category) {
            PhraseCategory.DANGER -> 0
            PhraseCategory.PANIC -> 1
            PhraseCategory.WARNING_LAMP -> 2
            PhraseCategory.GUIDE -> RANK_GUIDE
            PhraseCategory.BRIEFING, PhraseCategory.PRACTICE, PhraseCategory.PARKING,
            PhraseCategory.REPORT, PhraseCategory.SYSTEM -> 4
        }

        /** "조용히"(코칭 음성 꺼짐) 모드에서 허용되는 카테고리. SYSTEM 은 force 요청만(위 decide 참고). */
        val QUIET_ALLOWED: Set<PhraseCategory> = setOf(PhraseCategory.DANGER, PhraseCategory.PANIC, PhraseCategory.WARNING_LAMP)

        /** 코칭 중지(공황)·경고등 대응 중 허용되는 카테고리. */
        val PAUSED_ALLOWED: Set<PhraseCategory> = QUIET_ALLOWED + PhraseCategory.SYSTEM

        /** 모드별 허용 카테고리. 주행 중에는 화면 학습 계열(브리핑·연습·리포트·주차)을 말하지 않는다. */
        fun allowedIn(mode: DriveMode): Set<PhraseCategory> = when (mode) {
            DriveMode.PARKED -> setOf(
                PhraseCategory.BRIEFING, PhraseCategory.PRACTICE, PhraseCategory.PARKING, PhraseCategory.REPORT,
                PhraseCategory.WARNING_LAMP, PhraseCategory.PANIC, PhraseCategory.SYSTEM, PhraseCategory.DANGER,
            )
            DriveMode.PRACTICE -> setOf(
                PhraseCategory.PRACTICE, PhraseCategory.PARKING, PhraseCategory.DANGER, PhraseCategory.PANIC,
                PhraseCategory.WARNING_LAMP, PhraseCategory.SYSTEM,
            )
            DriveMode.DRIVING -> setOf(
                PhraseCategory.GUIDE, PhraseCategory.DANGER, PhraseCategory.PANIC, PhraseCategory.WARNING_LAMP, PhraseCategory.SYSTEM,
            )
        }

        /** 집중 구간에서 허용되는 최소 안내 멘트의 ID 접두사. */
        private val FOCUS_ESSENTIAL_PREFIX: Map<FocusZone, List<String>> = mapOf(
            FocusZone.MERGE to listOf("guide_merge_"),
            FocusZone.LEFT_TURN to listOf("guide_turn_left_"),
            FocusZone.LANE_CHANGE to listOf("guide_lane_change_"),
        )

        /** 이 안내가 해당 집중 구간의 필수 최소 멘트인지. */
        fun isFocusEssential(id: PhraseId, zone: FocusZone): Boolean =
            FOCUS_ESSENTIAL_PREFIX[zone]?.any { id.id.startsWith(it) } ?: false
    }
}
