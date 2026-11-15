// 담당: S (앱 연결 — 음성 엔진: Pleos TextToSpeech SDK(ON_DEVICE) 하나만. 안 되면 화면 글씨만(소리 없음))
package kr.drivecoach.app.speech

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 쓸 수 있는 TTS 엔진을 순서대로 시도한다.
 *  - 11/7 Pleos 서포트 답변: TTS·STT 는 공모전용으로 에뮬레이터에서도 제공됨. 같은 날 에뮬레이터에서 동작 확인(로캘은 ko-KR 이어야 함)
 *    → **Pleos TextToSpeech SDK(ON_DEVICE)만 쓴다.**
 *  - 11/13 사무국 메일: "음성 읽어 주기 기능은 개발 문서에 안내된 방식으로 구현 … 문서에 안내되지 않은 방식은 사용할 수 없습니다"
 *    → 예비로 두었던 사전 녹음 클립(assets/tts 527개)·Android TTS 를 모두 뺐다. Pleos TTS 가 실패하면 소리 없이 화면 글씨로만 안내하고([NoopSpeaker]),
 *      [RETRY_MS] 부터 간격을 늘려 가며 Pleos TTS 를 다시 시도한다.
 *  - SERVER/HYBRID 모드는 쓰지 않는다(11/7 에뮬레이터에서 우리 프로젝트 자격 증명으로 UNAUTHENTICATED). 앱에 Pleos 키도 넣지 않는다.
 *
 * 엔진 초기화는 비동기라 "지금 어떤 엔진인지"가 나중에 바뀔 수 있다. 그래서 호출자에게는 [FallbackSpeaker] 하나를 돌려주고,
 * 그 안에서 엔진을 갈아끼운다. 진단 화면은 [FallbackSpeaker.activeName] 으로 현재 엔진 이름을 본다.
 */
object SpeakerFactory {
    /** 엔진 선택을 시작하고 갈아끼울 수 있는 Speaker 를 돌려준다. Activity onDestroy 에서 release() 할 것. */
    fun create(context: Context): FallbackSpeaker = FallbackSpeaker(context).also { it.start() }
}

/**
 * 엔진을 순서대로 시도하며 실패하면 다음 엔진으로 넘어가는 [Speaker].
 * 넘어가는 순간 말하던(또는 대기 중이던) 문장이 있으면 새 엔진에 다시 넣어 잃지 않게 한다.
 */
class FallbackSpeaker(context: Context) : Speaker {
    private val appContext: Context = context.applicationContext
    private var active: Speaker = NoopSpeaker { Log.i(TAG, it) }
    /** 코치 음성 동안 오디오 포커스(내비 안내, 음악은 줄이기). 통화·음성 비서로 포커스를 잃으면 말을 멈춘다. */
    private val focus = GuidanceAudioFocus(appContext) { stop() }
    private var listener: SpeakerListener? = null
    private var released = false

    /** 지금 보낸 뒤 아직 끝나지 않은 문장(엔진 교체 시 재전송용). */
    private var inFlight: Triple<String, String, Boolean>? = null

    private val _activeName = MutableStateFlow("초기화 중")
    /** 진단 화면용: 현재 엔진 이름. */
    val activeName: StateFlow<String> = _activeName.asStateFlow()

    /** 엔진 교체 이력(진단용). */
    private val _history = MutableStateFlow<List<String>>(emptyList())
    val history: StateFlow<List<String>> = _history.asStateFlow()

    override val name: String get() = active.name
    /** 선행 묵음(pre-roll)을 기다리는 동안도 말하는 중으로 본다(그 사이 마이크를 열지 않게). */
    override val isSpeaking: Boolean get() = delayed != null || active.isSpeaking

    private val main = Handler(Looper.getMainLooper())
    /** 선행 묵음 뒤에 엔진에 넘길 문장(실행할 일, 문장 id). */
    private var delayed: Pair<Runnable, String>? = null
    /** Suspend(시동 꺼짐) 중: 새 문장을 말하지 않는다. */
    private var suspended = false

    private var retries = 0

    /** Pleos TTS(ON_DEVICE). */
    fun start() {
        if (released) return
        retries = 0
        tryPleos()
    }

    /** 엔진을 처음부터 다시 고른다(진단 실험으로 Pleos 서비스 연결이 바뀐 뒤 등). */
    fun restart() {
        if (released) return
        note("엔진 다시 고르기")
        start()
    }

    /**
     * Pleos TTS 를 못 쓸 때: 소리 없이(화면 글씨만) 두고, 조금 뒤 다시 시도한다(5초 → 10초 → … 최대 30초 간격).
     * 다른 음성 방식(녹음·Android TTS)으로 바꾸지 않는다(11/13 사무국: 문서에 안내된 방식만).
     */
    private fun afterPleos() {
        installNoop()
        val wait = minOf(RETRY_MAX_MS, RETRY_MS shl minOf(retries, 3))
        retries++
        main.postDelayed({ if (!released && active is NoopSpeaker) tryPleos() }, wait)
    }

    private fun tryPleos() {
        // 11/7: ON_DEVICE 고정(서포트: 에뮬레이터에서 ON_DEVICE 정상). 실패하면 소리 없이 두고 다시 시도.
        val pleos = PleosTtsSpeaker(appContext) { reason -> onFailed("Pleos TTS", reason) { afterPleos() } }
        install(pleos)
        pleos.start()
    }

    private fun installNoop() {
        install(NoopSpeaker { Log.i(TAG, it) })
    }

    /** 엔진을 교체하고, 진행 중이던 문장을 새 엔진에 다시 넣는다. */
    private fun install(next: Speaker) {
        val old = active
        if (old !== next) {
            old.setListener(null)
            try { old.release() } catch (t: Throwable) { Log.w(TAG, "release 실패: ${t.message}") }
        }
        active = next
        next.setListener(ListenerFor(next))
        next.setRate(rate)
        _activeName.value = next.name
        note("엔진: ${next.name}")
        inFlight?.let { (text, id, interrupt) -> next.speak(text, id, interrupt) }
    }

    private fun onFailed(engine: String, reason: String, fallback: () -> Unit) {
        if (released) return
        note("$engine 실패 → 다음 엔진. 이유: $reason")
        fallback()
    }

    /**
     * Pleos "TTS / 음성 안내 선행 묵음" 가이드(11/9): 새 음성 세션(포커스를 새로 받은 때)의 첫 문장 앞에 150~200 ms 묵음.
     *  - Pleos TTS 는 엔진이 직접 소리를 내 데이터에 묵음을 넣을 수 없다 → [PREROLL_MS] 만큼 늦게 넘긴다(가이드 3순위, 출력 경로·포커스는 먼저 잡은 상태).
     *  - 포커스를 쥔 채 이어지는 문장(이어 말하기)에는 넣지 않는다.
     */
    override fun speak(text: String, utteranceId: String, interrupt: Boolean) {
        if (released) return
        if (suspended) {
            Log.i(TAG, "Suspend(시동 꺼짐) 중이라 말하지 않음: $text")
            listener?.onFinished(utteranceId, false)
            return
        }
        cancelDelayed(notify = true)
        inFlight = Triple(text, utteranceId, interrupt)
        val fresh = focus.acquire()
        if (fresh) {
            val run = Runnable {
                delayed = null
                if (!released && !suspended) active.speak(text, utteranceId, interrupt)
            }
            delayed = run to utteranceId
            main.postDelayed(run, PREROLL_MS)
        } else {
            active.speak(text, utteranceId, interrupt)
        }
    }

    /** 선행 묵음을 기다리던 문장을 취소한다. [notify] 면 끝남(완료 아님)을 알린다(엔진이 끊긴 문장에 하는 것과 같게). */
    private fun cancelDelayed(notify: Boolean) {
        val (run, id) = delayed ?: return
        delayed = null
        main.removeCallbacks(run)
        if (inFlight?.second == id) inFlight = null
        focus.releaseSoon() // 엔진이 끝냈을 때와 같게: 곧 다음 문장이 없으면 포커스를 놓는다
        if (notify) listener?.onFinished(id, false)
    }

    override fun stop() {
        cancelDelayed(notify = true)
        active.stop()
    }

    /**
     * Pleos "Suspend 모드 - 오디오 포커스" 가이드: 시동이 꺼져 Suspend 에 들어가면 말을 멈추고 포커스를 반납하며, 그동안 새로 재생·요청하지 않는다.
     * 돌아와도(Resume) 멈췄던 문장을 다시 틀지 않는다(다음 안내부터 정상).
     */
    fun setSuspended(on: Boolean) {
        if (released || suspended == on) return
        suspended = on
        focus.suspended = on
        if (on) {
            note("Suspend(시동 꺼짐): 음성 멈춤·포커스 반납")
            inFlight = null
            stop()
            focus.abandon()
        } else {
            note("Resume(시동 켜짐): 음성 다시 허용(멈췄던 문장은 다시 틀지 않음)")
        }
    }

    private var rate = 1f
    override fun setRate(rate: Float) { this.rate = rate; active.setRate(rate) }

    override fun release() {
        if (released) return
        released = true
        cancelDelayed(notify = false)
        inFlight = null
        focus.abandon()
        active.setListener(null)
        try { active.release() } catch (_: Throwable) {}
        listener = null
    }

    override fun setListener(listener: SpeakerListener?) { this.listener = listener }

    private fun note(line: String) {
        Log.i(TAG, line)
        _history.value = (_history.value + line).takeLast(10)
    }

    /** 엔진별 리스너. 이미 교체된 옛 엔진의 늦은 콜백은 무시한다. */
    private inner class ListenerFor(private val owner: Speaker) : SpeakerListener {
        override fun onStarted(utteranceId: String) {
            if (owner !== active) return
            listener?.onStarted(utteranceId)
        }
        override fun onFinished(utteranceId: String, completed: Boolean) {
            if (owner !== active) return
            if (inFlight?.second == utteranceId) inFlight = null
            focus.releaseSoon() // 다음 문장이 곧 오면 계속 잡고 있는다
            listener?.onFinished(utteranceId, completed)
        }
    }

    companion object {
        private const val TAG = "SpeakerFactory"
        /** Pleos TTS 가 실패한 뒤 다시 시도하기까지(ms) */
        private const val RETRY_MS = 5000L
        private const val RETRY_MAX_MS = 30000L
        /** 엔진이 직접 소리를 내는 TTS 의 선행 묵음(가이드 권장 150~200 ms). */
        const val PREROLL_MS = 160L
    }
}
