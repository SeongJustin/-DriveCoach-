// 담당: S (앱 연결 — Pleos SpeechToText SDK 로 음성 명령 받기)
// 11/7: Pleos 서포트 답변(TTS·STT 는 공모전용으로 에뮬레이터에서 제공)에 따라 연결. 명령 해석은 core 의 CommandRecognizer.recognizeVoice.
package kr.drivecoach.app.speech

import ai.pleos.playground.stt.SpeechToText
import ai.pleos.playground.stt.constant.ErrorType
import ai.pleos.playground.stt.constant.Mode
import ai.pleos.playground.stt.listener.OnInitializeListener
import ai.pleos.playground.stt.listener.ResultListener
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import java.util.Locale
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * "다음", "다시", "확인", "조용히", "세울래요" 같은 짧은 음성 명령을 Pleos SpeechToText(ON_DEVICE)로 받는다.
 *
 * javap 로 확인한 API (SpeechToText-3.3.1):
 *  - `SpeechToText(context, Mode)` — `Mode { HYBRID, SERVER, ON_DEVICE }`
 *  - `initialize(OnInitializeListener { onSuccess(); onFailure(ErrorType) })`, `addListener(ResultListener)`, `request()`, `stop()`, `setLocale(Locale)`, `release()`
 *  - `ResultListener { onUpdated(text, isFinal); onUpdatedEpdData(Long, Long); onStartedRecognition(); onEndedRecognition(); onError(ErrorType); onReady(); onUpdatedRms(Double) }`
 *  - 마이크는 Pleos 서비스가 차량 마이크로 연다(RECORD_AUDIO 불필요). 권한은 매니페스트 `pleos.car.permission.STT_SERVICE`.
 *
 * 듣는 방법(11/7 대표 요청으로 바꿈): **항상 듣기**가 기본. 코치가 말하지 않는 동안에는 마이크를 계속 열어 두고(한 번 듣기가 끝나면 바로 다시 연다),
 * 운전자가 언제든 "다음"·"다시"라고 하면 받는다. 화면의 마이크 버튼으로 켜고 끈다(앱을 다시 켜도 유지, [alwaysOn]).
 *  - 코치가 말하는 동안에는 듣지 않는다: 차 스피커 소리를 자기 명령으로 알아듣지 않게(에뮬레이터에는 에코 제거가 없다). 말이 끝나고 [RESUME_AFTER_SPEECH_MS] 뒤 다시 연다.
 *    코치 말을 끊고 싶으면 휠 버튼·화면 버튼을 쓴다.
 *  - 끄면 [listen](한 번 듣기)만 쓴다.
 * 모든 SDK 호출은 try/catch — 서비스가 없거나 오류가 나도 앱은 휠·화면 버튼으로 계속 동작한다.
 */
class VoiceCommandListener(
    context: Context,
    /** 지금 코치가 말하고 있는지. 말하는 동안은 항상 듣기를 멈춘다. */
    private val isCoachSpeaking: () -> Boolean = { false },
    /** 최종 인식 문장(한 번). 명령 해석은 받는 쪽(CoachController.onVoice)이 한다. */
    private val onTranscript: (String) -> Unit,
) {
    enum class State { STARTING, READY, LISTENING, UNAVAILABLE }

    private val appContext = context.applicationContext
    private val main = Handler(Looper.getMainLooper())
    private var stt: SpeechToText? = null
    private var released = false

    private val _state = MutableStateFlow(State.STARTING)
    /** 화면·설정용 상태. */
    val state: StateFlow<State> = _state.asStateFlow()

    private val _lastText = MutableStateFlow("")
    /** 마지막으로 들은 말(듣는 중엔 중간 결과). 화면에 작게 보여 준다. */
    val lastText: StateFlow<String> = _lastText.asStateFlow()

    private val _lastError = MutableStateFlow("")
    /** 마지막 오류(진단용). */
    val lastError: StateFlow<String> = _lastError.asStateFlow()

    private val prefs = appContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val _alwaysOn = MutableStateFlow(prefs.getBoolean(KEY_ALWAYS_ON, true))
    /** 항상 듣기 켜짐. 화면 마이크 버튼으로 바꾼다. */
    val alwaysOn: StateFlow<Boolean> = _alwaysOn.asStateFlow()

    /** 지금 듣는 것이 항상 듣기에서 연 것인지(코치가 말하면 멈춘다). */
    private var sessionAlways = false
    /** 녹음을 넣는 중(진단 자동 시험). 그동안 항상 듣기가 마이크를 열지 않는다. */
    private var injecting = false
    /** 자가 검사용: 녹음 인식 결과를 명령으로 실행하지 않고 여기로만 돌려준다. */
    private var injectResult: ((String) -> Unit)? = null
    private var lastSpeakingAt = 0L
    private var nextAllowedAt = 0L
    private val clearText = Runnable { if (_state.value != State.LISTENING || sessionAlways) _lastText.value = "" }

    /** 항상 듣기: 코치가 말하면 멈추고, 말이 끝나면 다시 연다. */
    private val tick = object : Runnable {
        override fun run() {
            if (released) return
            if (injecting && _state.value != State.LISTENING) injecting = false
            if (!injecting && _alwaysOn.value && (_state.value == State.READY || _state.value == State.LISTENING)) {
                val now = SystemClock.elapsedRealtime()
                val speaking = try { isCoachSpeaking() } catch (_: Throwable) { false }
                if (speaking) {
                    lastSpeakingAt = now
                    if (_state.value == State.LISTENING && sessionAlways) stopListening()
                } else if (_state.value == State.READY && now - lastSpeakingAt >= RESUME_AFTER_SPEECH_MS && now >= nextAllowedAt) {
                    startSession(always = true)
                }
            }
            main.postDelayed(this, TICK_MS)
        }
    }

    /**
     * 코치가 막 말하려 할 때(CoachController.speakNow 직전) 부른다. 항상 듣기로 연 마이크를 **바로** 닫는다 —
     * [tick] 은 0.4초마다라 그 사이 코치 첫 소리("다음 주제…" 등)를 명령으로 알아들을 수 있다.
     */
    fun onCoachWillSpeak() {
        if (released) return
        lastSpeakingAt = SystemClock.elapsedRealtime()
        if (!injecting && sessionAlways && _state.value == State.LISTENING) {
            Log.i(TAG, "코치 말 시작 → 듣기 멈춤")
            stopListening()
        }
    }

    /** 항상 듣기 켜기/끄기(저장됨). 끄면 지금 듣던 것도 멈춘다. */
    fun setAlwaysOn(on: Boolean) {
        if (released) return
        _alwaysOn.value = on
        prefs.edit().putBoolean(KEY_ALWAYS_ON, on).apply()
        Log.i(TAG, "항상 듣기 ${if (on) "켬" else "끔"}")
        if (!on && _state.value == State.LISTENING) stopListening()
        _lastText.value = ""
    }

    private val timeout = Runnable { if (_state.value == State.LISTENING) { Log.i(TAG, "듣기 시간 초과"); stopListening() } }
    /** request 뒤 [READY_TIMEOUT_MS] 안에 onReady 가 없으면 Pleos 서비스와의 콜백 연결이 끊긴 것으로 보고 엔진을 다시 연결한다.
     *  듣기 시간([LISTEN_TIMEOUT_MS])은 마이크가 열린 뒤(onReady)부터 센다 — 에뮬레이터에서 마이크가 열리기까지 0.3~4초 걸렸다. */
    private var gotReady = false
    private fun onMicOpen() {
        if (gotReady) return
        gotReady = true
        main.removeCallbacks(readyWatch)
        if (_state.value == State.LISTENING) {
            main.removeCallbacks(timeout)
            main.postDelayed(timeout, LISTEN_TIMEOUT_MS)
        }
    }
    private val readyWatch = Runnable {
        if (_state.value == State.LISTENING && !gotReady) {
            // 11/7 에뮬레이터: 같은 앱에서 SpeechToText 를 하나 더 만들었다 닫으면(진단 실험) 서비스 쪽 콜백이 지워져 결과가 안 온다.
            Log.w(TAG, "request 응답 없음 → 엔진 다시 연결")
            restart()
        }
    }
    private val initTimeout = Runnable { if (_state.value == State.STARTING) unavailable("초기화 응답 없음") }

    private val listener = object : ResultListener {
        override fun onUpdated(text: String, isFinal: Boolean) = post {
            onMicOpen()
            // 항상 듣기에서는 차 안 대화가 화면에 뜨지 않게 중간 결과를 보여 주지 않는다(받아들인 명령만 잠깐 보인다).
            if (!sessionAlways || isFinal) _lastText.value = text
            if (isFinal) {
                // (11/15 보안 점검) 항상 듣기는 차 안 대화도 받아 적는다 → 원문은 debug 빌드에서만 logcat 에 남긴다
                Log.i(TAG, if (kr.drivecoach.app.BuildConfig.DEBUG) "인식: $text" else "인식: ${text.length}자")
                main.removeCallbacks(timeout)
                _state.value = State.READY
                val tap = injectResult
                injectResult = null
                if (tap != null) tap(text) else if (text.isNotBlank()) onTranscript(text)
                main.removeCallbacks(clearText)
                main.postDelayed(clearText, SHOW_HEARD_MS)
            }
        }
        override fun onUpdatedEpdData(start: Long, end: Long) {}
        override fun onStartedRecognition() = post { onMicOpen(); Log.i(TAG, "인식 시작") }
        override fun onEndedRecognition() = post {
            // 진단: 이전 세션의 늦은 끝 알림이 새 세션(아직 마이크 안 열림) 중에 오는지 본다(gotReady=false 면 의심).
            Log.i(TAG, "인식 끝 (state=${_state.value}, 마이크 열림=$gotReady)")
            main.removeCallbacks(timeout)
            // 11/8 로그: "인식 끝" 이 최종 결과보다 수십 ms 먼저 온다. 그 사이 항상 듣기가 새 request 를 보내지 않게 잠깐 기다린다.
            nextAllowedAt = maxOf(nextAllowedAt, SystemClock.elapsedRealtime() + END_GRACE_MS)
            if (_state.value == State.LISTENING) _state.value = State.READY
        }
        override fun onError(errorType: ErrorType) = post {
            Log.w(TAG, "STT onError $errorType")
            injectResult?.let { tap -> injectResult = null; tap("") }
            main.removeCallbacks(timeout)
            main.removeCallbacks(readyWatch)
            nextAllowedAt = SystemClock.elapsedRealtime() + RETRY_AFTER_ERROR_MS
            if (errorType in ENGINE_ERRORS) {
                // 엔진 자체를 못 쓰는 오류(11/7 에뮬레이터: request 직후 ON_DEVICE_ENGINE_ERROR) → 마이크 버튼을 흐리게 하고 다시 누르지 않게 한다.
                unavailable(errorType.name)
            } else {
                _lastError.value = errorType.name
                if (_state.value != State.UNAVAILABLE) _state.value = State.READY
            }
        }
        override fun onReady() = post { onMicOpen() }
        override fun onUpdatedRms(rms: Double) {}
    }

    /** 엔진 초기화. 실패하면 [State.UNAVAILABLE](마이크 버튼은 "사용 불가"로 보인다). */
    fun start() {
        if (released) return
        try {
            val engine = SpeechToText(appContext, Mode.ON_DEVICE)
            stt = engine
            main.postDelayed(initTimeout, INIT_TIMEOUT_MS)
            engine.initialize(object : OnInitializeListener {
                override fun onSuccess() = post {
                    main.removeCallbacks(initTimeout)
                    if (released) return@post
                    try { engine.addListener(listener) } catch (t: Throwable) { Log.w(TAG, "addListener 실패: ${t.message}") }
                    // Pleos 엔진은 "ko-KR"(Locale.KOREA)만 받는다. Locale.KOREAN("ko")을 주면 엔진 오류가 나고 caas 를 다시 시작할 때까지 계속 ON_DEVICE_ENGINE_ERROR(11/7 확인, 다른 참가 팀 실측과 같음).
                    try { engine.setLocale(Locale.KOREA) } catch (t: Throwable) { Log.w(TAG, "setLocale 실패: ${t.message}") }
                    Log.i(TAG, "Pleos STT 준비됨 (ON_DEVICE)")
                    _state.value = State.READY
                    main.removeCallbacks(tick)
                    main.post(tick)
                }
                override fun onFailure(errorType: ErrorType) = post {
                    main.removeCallbacks(initTimeout)
                    unavailable("initialize onFailure $errorType")
                }
            })
        } catch (t: Throwable) {
            main.removeCallbacks(initTimeout)
            unavailable("initialize 예외: ${t.javaClass.simpleName} ${t.message}")
        }
    }

    /** 한 문장 듣기(항상 듣기를 끈 경우). 이미 듣는 중이면 멈춘다(한 번 더 누르기 = 취소). */
    fun listen() {
        if (released) return
        when (_state.value) {
            State.LISTENING -> { stopListening(); return }
            State.READY -> {}
            else -> return
        }
        startSession(always = false)
    }

    /**
     * 진단·자동 시험용(11/7): 마이크 대신 녹음([pcm], 16 kHz·16bit·mono)을 같은 Pleos STT 엔진에 넣는다.
     * 인식 결과는 평소처럼 [onTranscript] → 명령 처리로 간다. 듣는 중이면 마이크를 닫고 넣는다. 넣지 못하면 false.
     * (다른 참가 팀도 같은 방법으로 에뮬레이터에서 시험: sendAudio → completeAudioSend, request 없이)
     */
    fun inject(pcm: ByteArray, onResult: ((String) -> Unit)? = null): Boolean {
        if (released) return false
        val engine = stt ?: return false
        if (_state.value != State.READY && _state.value != State.LISTENING) return false
        val wasListening = _state.value == State.LISTENING
        if (wasListening) stopListening()
        injecting = true
        injectResult = onResult
        sessionAlways = false
        _lastText.value = ""
        _state.value = State.LISTENING
        gotReady = true // 마이크를 열지 않으므로 onReady 를 기다리지 않는다
        main.removeCallbacks(readyWatch)
        main.removeCallbacks(timeout)
        main.postDelayed(timeout, pcm.size / 32L + INJECT_EXTRA_MS) // 16 kHz·16bit = 32 바이트/ms
        Log.i(TAG, "녹음 넣기 ${pcm.size} bytes (${pcm.size / 32} ms)")
        main.postDelayed({
            if (released) return@postDelayed
            try {
                engine.sendAudio(pcm)
                engine.completeAudioSend()
            } catch (t: Throwable) {
                Log.w(TAG, "녹음 넣기 실패: ${t.message}")
                injectResult?.let { tap -> injectResult = null; tap("") }
                _lastError.value = "inject ${t.javaClass.simpleName}"
                main.removeCallbacks(timeout)
                injecting = false
                _state.value = State.READY
            }
        }, if (wasListening) 300L else 0L)
        return true
    }

    private fun startSession(always: Boolean) {
        val engine = stt ?: return
        sessionAlways = always
        try {
            if (!always) _lastText.value = ""
            _state.value = State.LISTENING
            gotReady = false
            Log.i(TAG, if (always) "듣기 시작(항상)" else "듣기 시작(한 번)")
            engine.request()
            main.removeCallbacks(timeout)
            main.removeCallbacks(readyWatch)
            main.postDelayed(readyWatch, READY_TIMEOUT_MS)
        } catch (t: Throwable) {
            Log.w(TAG, "request 실패: ${t.message}")
            _lastError.value = "request ${t.javaClass.simpleName}"
            _state.value = State.READY
        }
    }

    fun stopListening() {
        injectResult?.let { tap -> injectResult = null; tap("") } // 녹음 시험에서 결과 없이 끝남
        main.removeCallbacks(timeout)
        main.removeCallbacks(readyWatch)
        try { stt?.stop() } catch (t: Throwable) { Log.w(TAG, "stop 실패: ${t.message}") }
        if (_state.value == State.LISTENING) _state.value = State.READY
    }

    fun release() {
        if (released) return
        released = true
        releaseEngine()
    }

    /** 엔진을 닫고 처음부터 다시 연결한다(진단 실험 뒤, 또는 request 에 응답이 없을 때). 엔진 오류로 꺼진 상태도 다시 시도한다. */
    fun restart() {
        if (released) return
        Log.i(TAG, "Pleos STT 다시 연결")
        releaseEngine()
        _state.value = State.STARTING
        _lastError.value = ""
        start()
    }

    private fun releaseEngine() {
        main.removeCallbacks(tick)
        main.removeCallbacks(clearText)
        main.removeCallbacks(timeout)
        main.removeCallbacks(readyWatch)
        main.removeCallbacks(initTimeout)
        try { stt?.removeListener(listener) } catch (_: Throwable) {}
        try { stt?.release() } catch (_: Throwable) {}
        stt = null
    }

    private fun unavailable(reason: String) {
        Log.w(TAG, "Pleos STT 사용 불가: $reason")
        _lastError.value = reason
        _state.value = State.UNAVAILABLE
    }

    private fun post(block: () -> Unit) { main.post { if (!released) block() } }

    companion object {
        private const val TAG = "VoiceCommand"
        private const val PREFS = "voice"
        private const val KEY_ALWAYS_ON = "always_on"
        /** 항상 듣기 점검 주기. */
        private const val TICK_MS = 400L
        /** 코치 말이 끝나고 다시 듣기까지(스피커 잔향을 듣지 않게). */
        const val RESUME_AFTER_SPEECH_MS = 700L
        /** 인식 끝 알림 뒤 최종 결과를 기다리는 시간(그동안 새로 듣기 시작하지 않음). */
        private const val END_GRACE_MS = 500L
        /** 오류(말 없음 등) 뒤 다시 열기까지. */
        private const val RETRY_AFTER_ERROR_MS = 1_000L
        /** 녹음 넣기: 녹음 길이에 더해 결과를 기다리는 시간. */
        private const val INJECT_EXTRA_MS = 6_000L
        /** 받아들인 명령을 화면에 보여 주는 시간. */
        private const val SHOW_HEARD_MS = 4_000L
        /** 한 번 나면 이번 실행 동안은 음성 명령을 끄는 오류(말이 없거나 시간 초과 같은 일시적 오류는 제외). */
        private val ENGINE_ERRORS = setOf(
            ErrorType.ON_DEVICE_ENGINE_ERROR, ErrorType.UNAUTHENTICATED, ErrorType.PERMISSION_DENIED,
            ErrorType.NOT_SUPPORTED_API, ErrorType.UNAVAILABLE_LANGUAGE,
        )
        private const val INIT_TIMEOUT_MS = 12_000L
        /** 마이크가 열린 뒤(onReady) 이 시간 안에 말이 끝나지 않으면 멈춘다. */
        const val LISTEN_TIMEOUT_MS = 7_000L
        /** request 뒤 마이크가 열리기(onReady)까지 기다리는 시간. 에뮬레이터 실측 0.3~4초(처음이 느림) → 넉넉히. */
        private const val READY_TIMEOUT_MS = 8_000L
    }
}
