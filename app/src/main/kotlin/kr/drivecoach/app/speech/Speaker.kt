// 앱 연결 — TTS 어댑터 공통 인터페이스
package kr.drivecoach.app.speech

/**
 * 실제로 소리를 내는 쪽(Pleos TTS, Android TTS, 아무것도 안 함)을 하나의 모양으로 감싼다.
 * CoachController 는 이 인터페이스만 알고, 어떤 엔진이 쓰이는지는 [SpeakerFactory] 가 정한다.
 *
 * 규칙:
 *  - 한 번에 한 문장만 말한다고 가정한다(발화 큐·우선순위는 core/tts SpeechPolicy 가 담당).
 *  - 모든 콜백은 **메인 스레드**에서 온다(구현체가 보장).
 *  - 말하기가 끝나든(onFinished completed=true), 끊기든, 실패하든 [SpeakerListener.onFinished] 가 반드시 한 번 불린다.
 */
interface Speaker {
    /** 진단 화면 표시용 이름 (예: "Pleos TTS(ON_DEVICE)", "Android TTS", "없음"). */
    val name: String

    /** 지금 말하는 중인지. */
    val isSpeaking: Boolean

    /**
     * 문장을 말한다.
     * @param utteranceId 호출자가 붙이는 식별자. 콜백에서 그대로 돌려준다.
     * @param interrupt true 면 지금 말하는 문장을 끊고 바로 말한다(위험 피드백).
     */
    fun speak(text: String, utteranceId: String, interrupt: Boolean = false)

    /** 지금 말하는 문장을 끊는다. 끊긴 문장은 onFinished(completed=false) 로 알린다. */
    fun stop()

    /** 종료(Activity onDestroy). 이후 speak 는 무시된다. */
    fun release()

    /** 말하기 시작/끝 콜백 등록. null 이면 해제. */
    fun setListener(listener: SpeakerListener?)

    /** 말 빠르기(1.0 = 보통). 지원하지 않는 엔진은 무시한다(설정 화면 "음성 속도"). */
    fun setRate(rate: Float) {}
}

/** 말하기 진행 콜백. CoachController 가 SpeechPolicy.onSpoken 호출과 다음 발화 예약에 쓴다. */
interface SpeakerListener {
    /** 실제로 소리가 나기 시작했다(엔진이 알려주지 않으면 speak 직후). */
    fun onStarted(utteranceId: String)

    /** 문장이 끝났다. [completed] 가 false 면 끊겼거나 실패한 것. */
    fun onFinished(utteranceId: String, completed: Boolean)
}

/**
 * 아무것도 말하지 않고 로그만 남기는 구현. TTS 를 하나도 못 쓰는 환경(권한 없음, 엔진 없음)에서의 마지막 대체.
 * onFinished 를 바로 불러 주므로 CoachController 의 흐름(연습 자동 진행 등)은 그대로 돈다.
 */
class NoopSpeaker(private val log: (String) -> Unit = {}) : Speaker {
    private var listener: SpeakerListener? = null
    override val name: String = "없음(로그만)"
    override val isSpeaking: Boolean = false

    override fun speak(text: String, utteranceId: String, interrupt: Boolean) {
        log("[TTS 없음] $text")
        listener?.onStarted(utteranceId)
        listener?.onFinished(utteranceId, true)
    }

    override fun stop() {}
    override fun release() { listener = null }
    override fun setListener(listener: SpeakerListener?) { this.listener = listener }
}
