// 담당: C+G (명령 입력: 음성/휠 버튼 → Command)
package kr.drivecoach.core.input

/**
 * 운전자가 쓸 수 있는 명령 전부(음성·휠 버튼·화면 버튼).
 * [PULL_OVER] 는 5-3 공황·긴장 대응("세울래요"/"힘들어" 또는 휠 버튼 길게).
 */
enum class Command {
    /** 방금 안내를 다시 들려 달라. */
    AGAIN,
    /** 코칭 음성을 잠시 멈춰 달라(위험 피드백은 계속). */
    QUIET,
    /** 감지 불가 항목을 눈으로 확인했다 / 했다. */
    CONFIRM,
    /** 다음 단계로(설명 끝·확인 대기 중 "다음"). */
    NEXT,
    /** 이 항목을 건너뛰고 다음 항목으로(화면 "건너뛰기" 버튼). */
    SKIP,
    /** 이전 항목으로 돌아가기(화면 "이전" 버튼). */
    PREV,
    /** "세울래요": 코칭을 멈추고 가까운 안전한 정차 장소 안내. 휠 버튼 길게 누름과 같다. */
    PULL_OVER,
}

/** 휠 버튼 누름 길이. */
enum class PressType { SHORT, LONG }

/** 명령이 들어온 경로. */
sealed class InputSource {
    /** STT 전사 텍스트. [confidence] 는 0~1, 엔진이 안 주면 null. */
    data class Voice(val transcript: String, val confidence: Double? = null) : InputSource()

    /** 스티어링 휠 버튼. 짧게/길게. */
    data class WheelButton(val press: PressType) : InputSource()
}

/** 입력 방식 설정값. */
enum class InputMode {
    /** 기본값: 음성 명령. */
    VOICE,
    /** 설정에서 켜면: 휠 버튼(짧게 = 문맥에 따라 확인/다음, 길게 = 세울래요). */
    WHEEL_BUTTON,
}

/**
 * 설정 화면에서 바꾸는 입력 설정. 기본은 음성, 설정하면 휠 버튼.
 * 어느 모드든 "세울래요"(음성 동의어·휠 버튼 길게)는 항상 받는다 — 공황 상황에서 설정을 따질 수 없기 때문.
 */
data class InputSettings(
    val mode: InputMode = InputMode.VOICE,
    /** 이 값 미만의 STT 신뢰도는 무시(null 반환). */
    val minVoiceConfidence: Double = DEFAULT_MIN_CONFIDENCE,
) {
    companion object {
        const val DEFAULT_MIN_CONFIDENCE = 0.5
        val DEFAULT = InputSettings()
    }
}

/**
 * 휠 버튼 "짧게"의 뜻은 지금 앱이 무엇을 기다리는지에 달려 있다.
 * 확인을 기다리면 CONFIRM, 그 외(설명 끝·대기 없음)는 NEXT.
 */
enum class ShortPressContext { AWAITING_CONFIRM, IDLE_OR_AWAITING_NEXT }

/**
 * [InputSource] 를 [Command] 로 바꾼다. 못 알아들으면 null (앱은 아무 반응도 하지 않는다 — 재촉 금지).
 *
 * 음성 매핑 규칙:
 *  - 전사 텍스트를 정규화(공백·문장부호 제거)한 뒤 동의어 사전과 **포함** 비교. 가장 긴 키워드 우선
 *    (더 긴 키워드와 글자가 겹치는 짧은 키워드는 따로 세지 않는다).
 *  - 서로 다른 명령의 키워드가 함께 잡히면 애매하므로 null. 단 PULL_OVER 키워드가 있으면 PULL_OVER 우선(안전).
 *  - "네/예/응" 처럼 짧은 긍정어는 다른 말에 섞이기 쉬우므로 **전체 일치**일 때만 CONFIRM.
 *  - 신뢰도가 [InputSettings.minVoiceConfidence] 미만이면 null.
 */
class CommandRecognizer(var settings: InputSettings = InputSettings.DEFAULT) {

    /** 입력 경로와 설정에 맞춰 명령을 알아낸다. */
    fun recognize(source: InputSource, context: ShortPressContext = ShortPressContext.IDLE_OR_AWAITING_NEXT): Command? =
        when (source) {
            is InputSource.Voice -> {
                val cmd = recognizeVoice(source.transcript, source.confidence)
                // 음성 모드가 아니면 "세울래요"만 통과
                if (settings.mode == InputMode.VOICE || cmd == Command.PULL_OVER) cmd else null
            }
            is InputSource.WheelButton -> when (source.press) {
                PressType.LONG -> Command.PULL_OVER
                PressType.SHORT ->
                    if (settings.mode != InputMode.WHEEL_BUTTON) null
                    else when (context) {
                        ShortPressContext.AWAITING_CONFIRM -> Command.CONFIRM
                        ShortPressContext.IDLE_OR_AWAITING_NEXT -> Command.NEXT
                    }
            }
        }

    /** STT 전사 텍스트만으로 명령을 알아낸다(설정의 모드와 무관). */
    fun recognizeVoice(transcript: String, confidence: Double? = null): Command? {
        if (confidence != null && confidence < settings.minVoiceConfidence) return null
        val text = normalize(transcript)
        if (text.isEmpty()) return null

        if (text in EXACT_CONFIRM) return Command.CONFIRM

        // 긴 키워드부터 찾고, 이미 찾은 더 긴 키워드와 글자가 겹치는 짧은 키워드는 세지 않는다
        // (예: "뭐라 했어" 의 "했어" 는 "뭐라했"(다시)과 "했" 이 겹치므로 확인으로 보지 않는다).
        val covered = BooleanArray(text.length)
        val commands = mutableSetOf<Command>()
        for ((keyword, command) in SYNONYMS) {   // SYNONYMS 는 긴 순서
            var from = 0
            while (true) {
                val at = text.indexOf(keyword, from)
                if (at < 0) break
                val range = at until at + keyword.length
                if (range.none { covered[it] }) {
                    commands += command
                    range.forEach { covered[it] = true }
                }
                from = at + 1
            }
        }
        if (commands.isEmpty()) return null
        if (Command.PULL_OVER in commands) return Command.PULL_OVER
        if (commands.size > 1) return null
        return commands.single()
    }

    companion object {
        /** 동의어 사전. (정규화된 키워드, 명령). 긴 키워드가 앞에 오도록 정렬돼 있다. */
        val SYNONYMS: List<Pair<String, Command>> = listOf(
            "다시" to Command.AGAIN,
            "한번더" to Command.AGAIN,
            "다시한번" to Command.AGAIN,
            "뭐라고" to Command.AGAIN,
            // (11/8) 실제로 할 법한 말투: "한 번만 더", "못 들었어요", "못 알아들었어", "뭐라 했어?", "반복해 줘"
            "한번만더" to Command.AGAIN,
            "못들었" to Command.AGAIN,
            "못알아들" to Command.AGAIN,
            "뭐라했" to Command.AGAIN,
            "반복" to Command.AGAIN,
            "조용히" to Command.QUIET,
            "그만" to Command.QUIET,
            "조용" to Command.QUIET,
            "시끄러" to Command.QUIET,
            "말하지마" to Command.QUIET,
            "확인" to Command.CONFIRM,
            "했어요" to Command.CONFIRM,
            "했어" to Command.CONFIRM,
            "보여요" to Command.CONFIRM,
            "보여" to Command.CONFIRM,
            "봤어요" to Command.CONFIRM,
            "봤어" to Command.CONFIRM,
            // (11/8) 연습 단계에서 한 일을 말하는 대답: "안전벨트 맸어요", "켰어요", "껐어요", "맞췄어요", "다 됐어요", "완료"
            "맸어" to Command.CONFIRM,
            "켰어" to Command.CONFIRM,
            "껐어" to Command.CONFIRM,
            "맞췄어" to Command.CONFIRM,
            "다됐어" to Command.CONFIRM,
            "완료" to Command.CONFIRM,
            "다음" to Command.NEXT,
            "넘어가" to Command.NEXT,
            "넘겨" to Command.NEXT,
            "건너뛰" to Command.SKIP,
            // "패스" 는 넣지 않는다 — 차 안에서 "하이패스" 를 자주 말한다.
            "스킵" to Command.SKIP,
            "이전" to Command.PREV,
            "전으로" to Command.PREV,
            "세울래요" to Command.PULL_OVER,
            "세울래" to Command.PULL_OVER,
            "세워" to Command.PULL_OVER,
            "힘들어" to Command.PULL_OVER,
            "잠깐" to Command.PULL_OVER,
            "멈춰" to Command.PULL_OVER,
        ).sortedByDescending { it.first.length }

        /** 전체 일치일 때만 CONFIRM 으로 보는 짧은 긍정어. */
        val EXACT_CONFIRM: Set<String> = setOf("네", "예", "응", "넵", "네네", "예예", "오케이", "ok", "okay")

        /** 공백·문장부호 제거, 소문자화. */
        fun normalize(s: String): String =
            s.lowercase().filter { !it.isWhitespace() && (it.isLetterOrDigit()) }
    }
}
