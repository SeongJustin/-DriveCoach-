// 담당: S (진단 — Pleos 쪽 문제를 앱 안에서 바로 시험하는 실험 패널, 11/7)
// 설정 > 진단 보기 > "Pleos 실험". 결과는 화면 목록과 logcat(태그 PleosLab)에 남는다.
package kr.drivecoach.app.diag

import ai.pleos.playground.tts.TextToSpeech
import ai.pleos.playground.tts.constant.ErrorType
import ai.pleos.playground.tts.constant.Mode
import ai.pleos.playground.tts.listener.EventListener
import ai.pleos.playground.tts.listener.OnInitializeListener
import ai.pleos.playground.stt.SpeechToText
import ai.pleos.playground.stt.listener.ResultListener
import ai.pleos.playground.stt.constant.ErrorType as SttErrorType
import ai.pleos.playground.stt.constant.Mode as SttMode
import android.car.Car
import android.car.content.pm.CarPackageManager
import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.Log
import java.util.Locale
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kr.drivecoach.app.MainActivity
import kr.drivecoach.app.speech.VoiceCommandListener

/**
 * 1) 주행 중 화면(DO): 설치 출처, CarPackageManager 가 MainActivity 를 DO 로 보는지, 차량 서비스가 허용하는 설치 출처 목록, 메타데이터가 실제로 읽히는지.
 *    AAOS user 빌드는 시스템 앱이 아니면 "허용된 설치 출처(앱 마켓)"에서 설치된 앱의 DO 선언만 인정한다.
 * 2) Pleos TTS(ON_DEVICE)로 시험 문장 한 번. 3) Pleos STT(ON_DEVICE)로 8초 동안 한 번 듣기.
 * 11/7 에뮬레이터 결과: DO = false(user 빌드 + 허용 설치 출처 목록 비어 있음 → 사이드로드 앱은 인정 안 됨).
 *    TTS·STT 의 ON_DEVICE_ENGINE_ERROR 는 로캘을 "ko"로 넘긴 탓이었다(ko-KR 로 고치고 Cold Boot 후 둘 다 동작).
 *    (서버·HYBRID 모드, 오디오 존·용도를 바꾸는 시험은 원인을 찾은 뒤 뺐다.)
 */
class PleosLab(
    context: Context,
    /** TTS·STT 실험이 끝난 뒤 부른다. 같은 앱에서 엔진을 하나 더 만들었다 닫으면 Pleos 서비스 쪽 콜백이 지워지므로 앱의 엔진을 다시 연결한다. */
    private val afterTrial: () -> Unit = {},
) {
    private val app = context.applicationContext
    private val main = Handler(Looper.getMainLooper())

    private val _lines = MutableStateFlow<List<String>>(emptyList())
    val lines: StateFlow<List<String>> = _lines.asStateFlow()

    private fun add(s: String) {
        Log.i(TAG, s)
        main.post { _lines.value = (_lines.value + s).takeLast(40) }
    }

    fun clear() { _lines.value = emptyList() }

    /** 주행 중 화면(DO) 점검. */
    fun checkDistractionOptimized() {
        val pkg = app.packageName
        val pm = app.packageManager
        val installer = try {
            if (Build.VERSION.SDK_INT >= 30) {
                val info = pm.getInstallSourceInfo(pkg)
                "installing=${info.installingPackageName} initiating=${info.initiatingPackageName}"
            } else {
                @Suppress("DEPRECATION") pm.getInstallerPackageName(pkg).toString()
            }
        } catch (t: Throwable) { "읽기 실패 ${t.javaClass.simpleName}" }
        add("DO 설치 출처: $installer")

        try {
            val ai = pm.getActivityInfo(ComponentName(app, MainActivity::class.java), PackageManager.GET_META_DATA)
            add("DO 메타데이터 distractionOptimized = ${ai.metaData?.get("distractionOptimized")}")
        } catch (t: Throwable) { add("DO 메타데이터 읽기 실패 ${t.javaClass.simpleName}") }

        try {
            val car = Car.createCar(app)
            val cpm = car?.getCarManager(Car.PACKAGE_SERVICE) as? CarPackageManager
            val result = cpm?.isActivityDistractionOptimized(pkg, MainActivity::class.java.name)
            add("DO CarPackageManager.isActivityDistractionOptimized(MainActivity) = $result")
            car?.disconnect()
        } catch (t: Throwable) { add("DO CarPackageManager 오류 ${t.javaClass.simpleName} ${t.message}") }

        for (carPkg in CAR_SERVICE_PACKAGES) {
            try {
                val res = pm.getResourcesForApplication(carPkg)
                val id = res.getIdentifier("allowedAppInstallSources", "array", carPkg)
                val list = if (id == 0) "(항목 없음)" else res.getStringArray(id).joinToString().ifEmpty { "(빈 목록)" }
                add("DO $carPkg 허용 설치 출처 = $list")
            } catch (t: Throwable) { add("DO $carPkg 읽기 실패 ${t.javaClass.simpleName}") }
        }
    }

    /** Pleos TTS(ON_DEVICE)로 시험 문장을 한 번 말한다. 10초 안에 onDone/onError 가 없으면 "응답 없음". */
    fun tryTts() {
        val tts = try { TextToSpeech(app, Mode.ON_DEVICE) } catch (t: Throwable) { add("TTS: 생성 실패 ${t.javaClass.simpleName}"); return }
        var finished = false
        val finish = { result: String ->
            main.post {
                if (!finished) {
                    finished = true
                    add("TTS: $result")
                    main.postDelayed({ runCatching { tts.release() }; main.postDelayed({ afterTrial() }, 500) }, 300)
                }
            }
            Unit
        }
        main.postDelayed({ finish("응답 없음(10초)") }, 10_000)
        add("TTS: 시작")
        try {
            tts.initialize(object : OnInitializeListener {
                override fun onSuccess() {
                    main.post {
                        runCatching {
                            tts.addEventListener(object : EventListener {
                                override fun onStart() { add("TTS: onStart") }
                                override fun onReady() {}
                                override fun onDone() = finish("onDone — 소리가 났는지 확인")
                                override fun onError(errorType: ErrorType) = finish("onError $errorType")
                                override fun onStop() = finish("onStop")
                                override fun onUpdatedRms(rms: Double) {}
                            })
                        }.onFailure { add("TTS: addEventListener 실패 ${it.message}") }
                        runCatching { tts.setLocale(Locale.KOREA) } // ko-KR 만 받음(Locale.KOREAN 은 엔진 오류)
                        runCatching { tts.speak("음성 시험입니다. 이 소리가 들리면 성공입니다.", true) }
                            .onFailure { finish("speak 예외 ${it.javaClass.simpleName} ${it.message}") }
                    }
                }
                override fun onFailure(errorType: ErrorType) = finish("initialize 실패 $errorType")
            })
        } catch (t: Throwable) { finish("initialize 예외 ${t.javaClass.simpleName} ${t.message}") }
    }

    /** Pleos STT(ON_DEVICE)로 한 번 들어 본다. 8초 동안 콜백을 적고 끝낸다. */
    fun trySst() {
        val stt = try { SpeechToText(app, SttMode.ON_DEVICE) } catch (t: Throwable) { add("STT: 생성 실패 ${t.javaClass.simpleName}"); return }
        var done = false
        val end = { why: String ->
            main.post {
                if (!done) {
                    done = true
                    add("STT: 끝 — $why")
                    runCatching { stt.stop() }
                    main.postDelayed({ runCatching { stt.release() }; main.postDelayed({ afterTrial() }, 500) }, 300)
                }
            }
            Unit
        }
        main.postDelayed({ end("8초") }, 8_000)
        add("STT: 시작 — 지금 \"다시\" 라고 말해 보세요(에뮬레이터는 PC 마이크 연결을 켜야 들림)")
        try {
            stt.initialize(object : ai.pleos.playground.stt.listener.OnInitializeListener {
                override fun onSuccess() {
                    main.post {
                        runCatching {
                            stt.addListener(object : ResultListener {
                                override fun onUpdated(text: String, isFinal: Boolean) { add("STT: onUpdated '$text' final=$isFinal"); if (isFinal) end("결과 받음") }
                                override fun onUpdatedEpdData(start: Long, endMs: Long) {}
                                override fun onStartedRecognition() { add("STT: onStartedRecognition") }
                                override fun onEndedRecognition() { add("STT: onEndedRecognition") }
                                override fun onError(errorType: SttErrorType) = end("onError $errorType")
                                override fun onReady() { add("STT: onReady") }
                                override fun onUpdatedRms(rms: Double) {}
                            })
                            stt.setLocale(Locale.KOREA) // ko-KR 만 받음
                            stt.request()
                            add("STT: request 보냄")
                        }.onFailure { end("request 예외 ${it.javaClass.simpleName} ${it.message}") }
                    }
                }
                override fun onFailure(errorType: SttErrorType) = end("initialize 실패 $errorType")
            })
        } catch (t: Throwable) { end("initialize 예외 ${t.javaClass.simpleName} ${t.message}") }
    }

    /** debug 빌드의 시험 녹음 목록(assets 의 voice_test 폴더 .pcm 파일, 빌드 도구 으로 만든다). release 에는 없어 빈 목록. */
    fun commandRecordings(): List<String> =
        try { app.assets.list(VOICE_TEST_DIR)?.filter { it.endsWith(".pcm") }?.sorted() ?: emptyList() } catch (_: Throwable) { emptyList() }

    /** 녹음 파일 → 말(글자). "명령 음성 만들기" 가 함께 만든 voice_test/index.txt("파일<TAB>말")를 읽고, 없으면 이름으로 짐작. */
    private val labels: Map<String, String> by lazy {
        try {
            app.assets.open("$VOICE_TEST_DIR/index.txt").use { it.readBytes().toString(Charsets.UTF_8) }
                .lines().mapNotNull { l -> l.split('\t').takeIf { it.size == 2 }?.let { it[0].trim() to it[1].trim() } }.toMap()
        } catch (_: Throwable) { emptyMap() }
    }
    fun label(file: String): String = labels[file] ?: fallbackLabel(file)

    /** 시험 녹음을 마이크 대신 앱의 음성 명령 엔진에 넣는다 → 인식·명령 처리까지 사람 없이 시험(결과는 logcat VoiceCommand·CoachController). */
    fun injectCommand(voice: VoiceCommandListener, file: String) {
        val pcm = try { app.assets.open("$VOICE_TEST_DIR/$file").use { it.readBytes() } } catch (t: Throwable) { add("말 넣기: 읽기 실패 $file ${t.javaClass.simpleName}"); return }
        val ok = voice.inject(pcm)
        add("말 넣기: ${label(file)} → ${if (ok) "넣음(인식 결과는 화면 위 마이크 옆)" else "엔진 준비 안 됨(${voice.state.value})"}")
    }

    companion object {
        private const val TAG = "PleosLab"
        private const val VOICE_TEST_DIR = "voice_test"
        /** index.txt 가 없을 때: "01_next.pcm" → "다음" */
        private fun fallbackLabel(file: String): String = when (kindOf(file)) {
            "next" -> "다음"; "again" -> "다시"; "confirm" -> "확인"; "prev" -> "이전"
            "quiet" -> "조용히"; "pullover" -> "세울래요"; "chat" -> "잡담"
            else -> file.substringBefore('.')
        }

        /** 녹음이 뜻하는 명령 종류: "12_again4.pcm" → "again". 이름 앞의 번호와 뒤의 숫자를 뗀다. */
        fun kindOf(file: String): String = file.substringAfter('_').substringBefore('.').trimEnd { it.isDigit() }
        private val CAR_SERVICE_PACKAGES = listOf("com.android.car", "com.android.car.updatable", "com.google.android.car.updatable")
    }
}
