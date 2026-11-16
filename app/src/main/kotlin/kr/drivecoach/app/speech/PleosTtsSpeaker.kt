// 앱 연결 — Pleos TextToSpeech SDK 어댑터
// 검증: TextToSpeech-3.3.1.jar 를 javap 로 읽어 시그니처를 맞췄다. 소리가 실제로 나는지는 에뮬레이터에서 확인할 것.
package kr.drivecoach.app.speech

import ai.pleos.playground.tts.TextToSpeech
import ai.pleos.playground.tts.constant.ErrorType
import ai.pleos.playground.tts.constant.Mode
import ai.pleos.playground.tts.listener.EventListener
import ai.pleos.playground.tts.listener.OnInitializeListener
import android.content.ComponentName
import android.content.Context
import android.media.AudioAttributes
import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.util.Log
import java.util.Locale

/**
 * Pleos TextToSpeech SDK 로 말하는 [Speaker].
 *
 * javap 로 확인한 실제 API (TextToSpeech-3.3.1):
 *  - `TextToSpeech(context, mode: Mode)` — `Mode { SERVER, ON_DEVICE, HYBRID }`, 기본값 HYBRID(우리는 ON_DEVICE)
 *  - `initialize(OnInitializeListener { onSuccess(); onFailure(ErrorType) })`
 *  - `addEventListener(EventListener { onStart(); onReady(); onDone(); onError(ErrorType); onStop(); onUpdatedRms(Double) })`
 *  - `speak(text: String, isFinal: Boolean = true)`, `stop()`, `setLocale(Locale)`, `release()`
 *  - **ON_DEVICE 모드만** 쓴다(앱 등록·네트워크 불필요). SERVER/HYBRID 는 registerApp 이 필요하고 에뮬레이터에서 우리 프로젝트 자격 증명으로 UNAUTHENTICATED 라 뺐다.
 *  - 서비스 바인딩 대상(ServiceConstants): 패키지 `ai.pleos.playground.caas`, 컴포넌트 `…caas.service.speech.SpeechService`,
 *    ON_DEVICE 액션 `ai.pleos.playground.caas.intent.action.TTS_STREAM_ON_DEVICE_START`. 이 컴포넌트가 없으면 bindService 가 조용히 실패하므로
 *    [start] 에서 먼저 resolveService 로 존재 여부를 보고 없으면 바로 [onUnavailable] 을 불러 대체 엔진으로 넘어간다.
 *
 * 콜백에는 문장 식별자가 없다(onDone/onStop 만 옴). 한 번에 한 문장만 보낸다는 전제로 "현재 문장" 하나만 추적한다.
 * 모든 SDK 호출은 try/catch(Throwable) — 권한 거부·서비스 없음·초기화 전 호출(lateinit 예외)에서 앱이 죽지 않게 한다.
 */
class PleosTtsSpeaker(
    context: Context,
    /** 이 엔진을 쓸 수 없다고 판단했을 때 한 번 호출(이유 문자열). SpeakerFactory 가 대체 엔진으로 넘어간다. */
    private val onUnavailable: (reason: String) -> Unit,
) : Speaker {
    private val appContext: Context = context.applicationContext
    private val main = Handler(Looper.getMainLooper())
    private var tts: TextToSpeech? = null
    private var listener: SpeakerListener? = null

    private var ready = false
    private var released = false
    private var unavailableReported = false
    private var consecutiveErrors = 0

    /** 지금 말하는 문장 ID. 없으면 null. */
    private var current: String? = null
    /** 우리가 stop() 을 불러서 올 onStop 을 기다리는 중(그 onStop 은 '끊김' 통지로 쓰지 않는다). */
    private var expectingStop = false
    /** 초기화 전에 들어온 마지막 문장. 준비되면 말한다(그 전 것은 버림). */
    private var pending: Pair<String, String>? = null

    override val name: String get() = "Pleos TTS(ON_DEVICE)"
    override val isSpeaking: Boolean get() = current != null

    private val initTimeout = Runnable { if (!ready) fail("초기화 응답 없음(${INIT_TIMEOUT_MS}ms)") }

    /** speak 뒤 [START_TIMEOUT_MS] 안에 onStart 가 없으면 음성 서비스가 멈춘 것으로 보고 실패로 처리한다(SpeakerFactory 가 화면 글씨 안내로 바꾸고 나중에 다시 시도). */
    private var started = false
    private val startWatch = Runnable { if (current != null && !started) fail("onStart 응답 없음(${START_TIMEOUT_MS}ms)") }
    /** onStart 뒤 문장 길이에 비해 너무 오래 onDone 이 없으면 끝난 것으로 본다(서비스가 끝 알림을 빠뜨린 경우 코칭이 멈추지 않게). */
    private val doneWatch = Runnable { if (current != null) { Log.w(TAG, "onDone 응답 없음 → 끝난 것으로 처리"); finishCurrent(completed = true) } }
    private var doneTimeoutMs = 0L

    private val events = object : EventListener {
        override fun onStart() = post {
            started = true
            main.removeCallbacks(startWatch)
            main.removeCallbacks(doneWatch)
            if (current != null) main.postDelayed(doneWatch, doneTimeoutMs)
            current?.let { listener?.onStarted(it) }
        }
        override fun onReady() {}
        override fun onDone() = post { consecutiveErrors = 0; finishCurrent(completed = true) }
        override fun onStop() = post {
            if (expectingStop) expectingStop = false else finishCurrent(completed = false)
        }
        override fun onError(errorType: ErrorType) = post {
            Log.w(TAG, "TTS onError $errorType")
            finishCurrent(completed = false)
            consecutiveErrors++
            if (errorType in FATAL_ERRORS || consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) fail("onError $errorType")
        }
        override fun onUpdatedRms(rms: Double) {}
    }

    /** 서비스 존재 확인 → initialize. 실패하면 [onUnavailable]. */
    fun start() {
        if (released) return
        try {
            val probe = Intent()
                .setComponent(ComponentName(SERVICE_PACKAGE, SERVICE_CLASS))
                .setAction(ACTION_STREAM_ON_DEVICE)
            val resolved = appContext.packageManager.resolveService(probe, 0)
            if (resolved == null) {
                fail("TTS 서비스 없음: $SERVICE_PACKAGE/$SERVICE_CLASS")
                return
            }
        } catch (t: Throwable) {
            // 조회 자체가 실패해도 바인딩은 시도해 본다(패키지 가시성 문제일 수 있음).
            Log.w(TAG, "resolveService 실패: ${t.message}")
        }
        try {
            val engine = TextToSpeech(appContext, Mode.ON_DEVICE)
            tts = engine
            // 주의: addEventListener 는 SDK 내부의 lateinit `tts`(initialize 가 만드는 엔진)를 바로 건드린다.
            // initialize 전에 부르면 UninitializedPropertyAccessException → onSuccess 안에서 등록한다.
            main.postDelayed(initTimeout, INIT_TIMEOUT_MS)
            engine.initialize(object : OnInitializeListener {
                override fun onSuccess() = post {
                    main.removeCallbacks(initTimeout)
                    if (released) return@post
                    try { engine.addEventListener(events) } catch (t: Throwable) { Log.w(TAG, "addEventListener 실패: ${t.message}") }
                    // Pleos 엔진은 "ko-KR"(Locale.KOREA)만 받는다. Locale.KOREAN("ko")을 주면 엔진 오류가 나고 caas 를 다시 시작할 때까지 계속 ON_DEVICE_ENGINE_ERROR(다른 참가 팀 실측과 같음).
                    try { engine.setLocale(Locale.KOREA) } catch (t: Throwable) { Log.w(TAG, "setLocale 실패: ${t.message}") }
                    // 출력 용도: 내비 안내 음성(GuidanceAudioFocus.ATTRIBUTES 와 같은 값).
                    try {
                        engine.setAudioAttributes(
                            AudioAttributes.Builder()
                                .setUsage(AudioAttributes.USAGE_ASSISTANCE_NAVIGATION_GUIDANCE)
                                .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                                .build()
                        )
                    } catch (t: Throwable) { Log.w(TAG, "setAudioAttributes 실패: ${t.message}") }
                    becomeReady()
                }
                override fun onFailure(errorType: ErrorType) = post {
                    main.removeCallbacks(initTimeout)
                    fail("initialize onFailure $errorType")
                }
            })
        } catch (t: Throwable) {
            main.removeCallbacks(initTimeout)
            fail("initialize 예외: ${t.javaClass.simpleName} ${t.message}")
        }
    }

    private fun becomeReady() {
        if (released || ready) return
        ready = true
        Log.i(TAG, "Pleos TTS 준비됨 (ON_DEVICE)")
        pending?.let { (text, id) -> pending = null; speak(text, id, interrupt = false) }
    }

    override fun speak(text: String, utteranceId: String, interrupt: Boolean) {
        if (released || unavailableReported) return
        if (!ready) { pending = text to utteranceId; return }
        val engine = tts ?: return
        if (current != null) {
            if (interrupt) stop() else finishCurrent(completed = false) // 큐 동작이 미확인이라 이전 문장은 끝난 것으로 본다
        }
        current = utteranceId
        started = false
        doneTimeoutMs = (DONE_TIMEOUT_BASE_MS + text.length * DONE_TIMEOUT_PER_CHAR_MS).coerceAtLeast(DONE_TIMEOUT_BASE_MS)
        main.removeCallbacks(doneWatch)
        main.removeCallbacks(startWatch)
        main.postDelayed(startWatch, START_TIMEOUT_MS)
        try {
            engine.speak(text, true)
        } catch (t: Throwable) {
            Log.w(TAG, "speak 예외: ${t.message}")
            finishCurrent(completed = false)
            fail("speak 예외: ${t.javaClass.simpleName}")
        }
    }

    override fun stop() {
        if (current == null) return
        expectingStop = true
        try { tts?.stop() } catch (t: Throwable) { Log.w(TAG, "stop 예외: ${t.message}"); expectingStop = false }
        finishCurrent(completed = false)
    }

    override fun release() {
        if (released) return
        released = true
        main.removeCallbacks(initTimeout)
        main.removeCallbacks(startWatch)
        main.removeCallbacks(doneWatch)
        current = null
        pending = null
        val engine = tts
        tts = null
        try { engine?.removeEventListener(events) } catch (_: Throwable) {}
        try { engine?.release() } catch (_: Throwable) {}
        listener = null
    }

    override fun setListener(listener: SpeakerListener?) { this.listener = listener }

    private fun finishCurrent(completed: Boolean) {
        main.removeCallbacks(startWatch)
        main.removeCallbacks(doneWatch)
        val id = current ?: return
        current = null
        listener?.onFinished(id, completed)
    }

    private fun fail(reason: String) {
        if (unavailableReported || released) return
        unavailableReported = true
        Log.w(TAG, "Pleos TTS 사용 불가: $reason")
        // 먼저 공장에 알린다 → 공장이 리스너를 떼고 다음 엔진에 진행 중 문장을 다시 넣는다.
        // 그 뒤에 남은 문장을 정리하므로(리스너가 이미 떼였으면 통지 없음) 같은 문장이 두 번 '끝남'으로 가지 않는다.
        onUnavailable(reason)
        pending = null
        finishCurrent(completed = false)
    }

    /** SDK 콜백은 임의 스레드에서 올 수 있어 전부 메인으로 보낸다. */
    private fun post(block: () -> Unit) { main.post(Runnable { if (!released) block() }) }

    companion object {
        private const val TAG = "PleosTtsSpeaker"
        /** initialize 응답(onSuccess/onFailure)이 이 시간 안에 없으면 포기. bindService 실패는 SDK 가 로그만 남기고 콜백을 주지 않는다. */
        const val INIT_TIMEOUT_MS = 12_000L
        private const val MAX_CONSECUTIVE_ERRORS = 3
        /** speak → onStart 최대 대기. 에뮬레이터 실측: 첫 문장 약 0.5초, 이후 0.1초 안쪽(다른 참가 팀 실측도 같음). */
        private const val START_TIMEOUT_MS = 5_000L
        /** onStart → onDone 최대 대기 = 기본 + 글자당(한국어 TTS 는 초당 5~7자). */
        private const val DONE_TIMEOUT_BASE_MS = 6_000L
        private const val DONE_TIMEOUT_PER_CHAR_MS = 300L
        /** 이 오류는 재시도해도 소용없는 것으로 보고 바로 대체 엔진으로 간다. */
        private val FATAL_ERRORS = setOf(
            ErrorType.PERMISSION_DENIED, ErrorType.NOT_SUPPORTED_API, ErrorType.UNAUTHENTICATED,
            ErrorType.ON_DEVICE_ENGINE_ERROR, ErrorType.UNAVAILABLE_LANGUAGE,
        )
        // javap: ai.pleos.playground.tts.constant.ServiceConstants (jar 안의 문자열 상수)
        const val SERVICE_PACKAGE = "ai.pleos.playground.caas"
        const val SERVICE_CLASS = "ai.pleos.playground.caas.service.speech.SpeechService"
        const val ACTION_STREAM_ON_DEVICE = "ai.pleos.playground.caas.intent.action.TTS_STREAM_ON_DEVICE_START"
    }
}
