// debug 전용 자가 검사: adb 로 띄워 사람 없이 Pleos 연결·음성·음성 명령을 확인한다.
// 결과는 화면과 logcat(태그 DCTEST)에 "PASS/FAIL/INFO 이름 | 내용" 으로 남고, 마지막 줄은 "DONE pass=N fail=M".
package kr.drivecoach.app.debug

import android.app.Activity
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.graphics.Color
import android.media.AudioManager
import android.os.Bundle
import android.os.IBinder
import android.os.SystemClock
import android.util.Log
import android.view.ViewGroup
import android.widget.ScrollView
import android.widget.TextView
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import kr.drivecoach.app.BuildConfig
import kr.drivecoach.app.CoachService
import kr.drivecoach.app.game.GameLauncher
import kr.drivecoach.app.diag.PleosLab
import kr.drivecoach.app.endLaw
import kr.drivecoach.app.lawNext
import kr.drivecoach.app.speech.VoiceCommandListener
import kr.drivecoach.app.startLaw
import kr.drivecoach.app.vehicle.PleosSignalSource
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.input.CommandRecognizer
import kr.drivecoach.core.tts.PhraseId
import kr.drivecoach.core.tts.Phrases
import kotlin.coroutines.resume

class SelfTestActivity : Activity() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private lateinit var out: TextView
    private var pass = 0
    private var fail = 0
    private var bound = false

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val svc = (binder as? CoachService.LocalBinder)?.service ?: return
            scope.launch { runAll(svc) }
        }
        override fun onServiceDisconnected(name: ComponentName?) {}
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        out = TextView(this).apply { textSize = 20f; setTextColor(Color.WHITE); setPadding(40, 40, 40, 40) }
        setContentView(ScrollView(this).apply {
            setBackgroundColor(Color.rgb(20, 24, 32))
            addView(out, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        })
        line("INFO", "시작", "드라이브코치 자가 검사(debug)")
        ContextCompat.startForegroundService(this, CoachService.startIntent(this))
        bound = bindService(CoachService.startIntent(this), connection, Context.BIND_AUTO_CREATE)
    }

    override fun onDestroy() {
        scope.cancel()
        if (bound) runCatching { unbindService(connection) }
        super.onDestroy()
    }

    private fun line(kind: String, name: String, detail: String) {
        when (kind) { "PASS" -> pass++; "FAIL" -> fail++ }
        Log.i(TAG, "$kind $name | $detail")
        out.append("${mark(kind)} $name — $detail\n")
    }
    private fun mark(kind: String) = when (kind) { "PASS" -> "OK"; "FAIL" -> "NG"; else -> "--" }
    private fun check(name: String, ok: Boolean, detail: String) = line(if (ok) "PASS" else "FAIL", name, detail)

    private suspend fun runAll(svc: CoachService) {
        // 0) 서비스가 SDK 를 붙일 시간을 준다(앱을 막 켰을 때)
        waitUntil(20_000) { svc.voice.state.value != VoiceCommandListener.State.STARTING }
        delay(3_000)

        // 1) Pleos 연결
        val connected = svc.signals.connected.value
        val features = PleosSignalSource.VEHICLE_FEATURES
        val ok = features.count { it in connected }
        check("차량 신호(Vehicle)", ok == features.size, "$ok / ${features.size} 연결")
        check("내비(NaviHelper)", svc.navi.naviReady.value, if (svc.navi.naviReady.value) "연결됨" else "연결 안 됨")
        check("위치(FusedLocation)", "FusedLocation" in connected, if ("FusedLocation" in connected) "연결됨" else "연결 안 됨")
        val sttState = svc.voice.state.value
        check("음성 명령(STT)", sttState == VoiceCommandListener.State.READY || sttState == VoiceCommandListener.State.LISTENING,
            "$sttState ${svc.voice.lastError.value}".trim())
        val engine0 = svc.speaker.activeName.value
        check("음성 엔진", engine0.startsWith("Pleos"), engine0)
        line("INFO", "항상 듣기", if (svc.voice.alwaysOn.value) "켜짐" else "꺼짐")
        // 3D 게임 "실제 액셀" 모드가 가능한지(차가 액셀 페달 값을 주는지). 없으면 게임은 자동 속도(핸들만)
        line("INFO", "액셀 페달 신호", if ("AccelPedal" in connected) "있음(값 ${svc.signals.stateFlow.value.accelPedalPct ?: "아직 없음"}%)" else "없음 — 3D 서킷·도로는 자동 속도")

        // 2) 코치 음성: 도로 법규 첫 주제를 끝까지 말하는지 + 그동안 항상 듣기가 멈추는지
        svc.coach.startLaw()
        val started = waitUntil(8_000) { svc.speaker.isSpeaking }
        check("코치 음성 시작", started, if (started) "말하기 시작" else "8초 안에 시작 안 함")
        if (started) {
            // 말하기 시작을 본 즉시(0.2초 안) 듣기가 멈춰 있어야 한다. 코치 첫 소리를 명령으로 알아듣지 않게(onCoachWillSpeak).
            val listeningAtStart = svc.voice.state.value == VoiceCommandListener.State.LISTENING
            check("말 시작과 함께 듣기 멈춤", !listeningAtStart, "코치 말 시작 직후 STT 상태 ${svc.voice.state.value}")
            delay(1_000)
            val listeningWhileSpeaking = svc.voice.state.value == VoiceCommandListener.State.LISTENING
            check("말하는 동안 듣기 멈춤", !listeningWhileSpeaking, "코치가 말할 때 STT 상태 ${svc.voice.state.value}")
            val finished = waitUntil(60_000) { !svc.speaker.isSpeaking }
            check("코치 음성 끝까지", finished, if (finished) "끝남" else "60초 안에 안 끝남")
        }
        val engine1 = svc.speaker.activeName.value
        check("녹음 클립으로 넘어가지 않음", engine1.startsWith("Pleos"), engine1)
        if (svc.voice.alwaysOn.value) {
            val relistened = waitUntil(5_000) { svc.voice.state.value == VoiceCommandListener.State.LISTENING }
            check("말이 끝난 뒤 다시 듣기", relistened, "STT 상태 ${svc.voice.state.value}")
        }

        // 3) 녹음 인식(명령으로 실행하지 않고 결과만 본다)
        val recordings = svc.lab.commandRecordings()
        if (recordings.isEmpty()) line("INFO", "녹음 인식", "녹음 없음")
        val recognizer = CommandRecognizer()
        // 녹음 인식은 항상 듣기를 잠시 끄고 잰다(듣던 세션을 끊고 바로 넣으면 앞부분이 잘리는지 가려내려고).
        // 항상 듣기와 함께 쓰는 경로는 아래 4) "다음 → 법규 다음 주제" 에서 따로 본다.
        val alwaysWasOn = svc.voice.alwaysOn.value
        if (alwaysWasOn) { svc.voice.setAlwaysOn(false); waitUntil(3_000) { svc.voice.state.value == VoiceCommandListener.State.READY } }
        try {
            for (f in recordings) {
                val kind = PleosLab.kindOf(f)
                val expected = EXPECTED[kind]
                val label = svc.lab.label(f)
                if (kind !in EXPECTED) { line("INFO", "녹음 $label", "기대 명령을 모름($kind) — 건너뜀"); continue }
                val pcm = runCatching { assets.open("voice_test/$f").use { it.readBytes() } }.getOrNull()
                if (pcm == null) { line("FAIL", "녹음 $label", "읽기 실패"); continue }
                waitUntil(5_000) { !svc.speaker.isSpeaking && svc.voice.state.value == VoiceCommandListener.State.READY }
                val heard = injectAndWait(svc.voice, pcm)
                val cmd = heard?.let { recognizer.recognizeVoice(it) }
                when {
                    heard == null -> line("FAIL", "녹음 $label", "넣지 못함(STT ${svc.voice.state.value})")
                    expected == null -> check("녹음 $label", cmd == null, "'$heard' → ${cmd ?: "명령 아님"} (명령이 아니어야 함)")
                    else -> check("녹음 $label", cmd == expected, "'$heard' → ${cmd ?: "명령 아님"} (기대 $expected)")
                }
                delay(800)
            }
        } finally {
            if (alwaysWasOn) svc.voice.setAlwaysOn(true)
        }

        // 4) 음성 명령으로 화면 이동: 도로 법규에서 "다음" → 다음 주제
        val next = recordings.firstOrNull { it.contains("_next") }
        if (next != null) {
            svc.coach.startLaw()
            waitUntil(3_000) { svc.speaker.isSpeaking }
            val before = svc.coach.lawUi.value?.index
            val pcm = assets.open("voice_test/$next").use { it.readBytes() }
            val heard = injectAndWait(svc.voice, pcm)
            heard?.let { svc.coach.onVoice(it) } // 실제 명령 처리 경로
            delay(500)
            val after = svc.coach.lawUi.value?.index
            check("음성 '다음' → 법규 다음 주제", before != null && after == before + 1, "주제 $before → $after (들은 말 '$heard')")
            svc.coach.endLaw()
        }

        // 5) Pleos TTS 문장 길이(회전 안내 시점 보정용): 안내 문장을 직접 말하게 하고
        //    말하라고 한 때 → 소리가 나기 시작한 때 → 소리가 멈춘 때 → 끝 알림(onDone)을 잰다.
        //    끝 알림이 소리보다 먼저 오면(음수) 코치가 "다 말했다" 고 보고 마이크를 다시 여는 시점이 이르다는 뜻이다.
        measureTts(svc)

        // 6) 주행 중 화면(DO): 판정만 기록
        line("INFO", "주행 중 화면(DO)", "adb 설치 앱은 에뮬레이터(user 빌드)에서 인정되지 않는 것이 정상")
        svc.coach.lawUi.value?.let { svc.coach.endLaw() }

        // 7) 3D 주차·핸들 연습: 켜지는지(준비 신호). 8단계를 운전석 시점(거울 3개 + 화면 거울)으로 25초 돌린 뒤 스스로 닫힌다.
        //    초당 화면 수는 게임이 logcat 에 "DC3D fps=…" 로 남긴다.
        if (BuildConfig.HAS_UNITY) {
            // 기기 CPU 종류: Unity 6.5+ 는 x86_64 를 못 만든다 → 에뮬레이터(x86_64)에서 3D 가 안 켜지면 먼저 이것을 본다
            line("INFO", "기기 CPU", android.os.Build.SUPPORTED_ABIS.joinToString(", "))
            val before = GameLauncher.fails(this)
            val t0 = SystemClock.elapsedRealtime()
            GameLauncher.open(this, "-dcScreen stage:8 -dcCam driver -dcAutoExit 25")
            // 준비 신호가 오거나(pending 지움, 실패 0) 3D 화면이 포기하고 2D 로 넘기면(실패 기록) 끝. 첫 실행은 20초 가까이 걸린다.
            val done = waitUntil(45_000) { !GameLauncher.pending(this) }
            val ok = done && GameLauncher.use3d(this) && GameLauncher.fails(this) == 0
            val sec = (SystemClock.elapsedRealtime() - t0) / 1000.0
            check("3D 게임 켜짐", ok, when {
                ok -> String.format("%.1f초 만에 준비(운전석 시점 25초 실행)", sec)
                before >= 2 -> "이 빌드에서 이미 두 번 실패해 2D 로 열림(앱 데이터를 지우거나 3D 를 새로 만들면 다시 시도)"
                done -> String.format("%.1f초 뒤 3D 가 포기하고 2D 로 넘어감 — logcat 의 Unity 오류(libmain.so·그래픽) 확인", sec)
                else -> "45초 안에 준비 신호 없음 — 다음부터 2D 로 열림(실패 ${GameLauncher.fails(this)}번)"
            })
            val ready = ok
            delay(if (ready) 28_000 else 2_000)
            // 스스로 닫히지 않았으면(-dcAutoExit 실패) 프로세스를 끝낸다
            val am = getSystemService(Context.ACTIVITY_SERVICE) as android.app.ActivityManager
            am.runningAppProcesses?.firstOrNull { it.processName.endsWith(":game3d") }?.let {
                line("INFO", "3D 게임 닫기", "스스로 닫히지 않아 프로세스를 끝냄(pid ${it.pid})")
                android.os.Process.killProcess(it.pid)
            }
            startActivity(Intent(this, SelfTestActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            delay(1_500)

            // 8) 실제 동네(도로 주행) 열기: 지도가 커서 메모리가 가장 많이 든다. 에뮬레이터(3 GB)에서 메모리 부족으로
            //    시스템이 재시작된 뒤 넣은 검사. 첫 동네를 운전석 시점으로 열어 40초 뒤에도 살아 있는지와 그때의 메모리(PSS)를 적는다.
            if (ok) {
                line("INFO", "실제 동네 여는 중", "첫 동네(운전석 시점) — 여기서 멈추면 메모리 부족")
                GameLauncher.open(this, "-dcScreen region:0 -dcCam driver -dcCrossTest 44 -dcAutoExit 92")
                delay(40_000)
                val p3 = am.runningAppProcesses?.firstOrNull { it.processName.endsWith(":game3d") }
                val pss = p3?.let { runCatching { am.getProcessMemoryInfo(intArrayOf(it.pid))[0].totalPss / 1024 }.getOrNull() }
                val mi = android.app.ActivityManager.MemoryInfo().also { am.getMemoryInfo(it) }
                check("실제 동네 열림(40초 뒤에도 실행 중)", p3 != null,
                    if (p3 != null) "3D 메모리(PSS) ${pss ?: -1} MB · 기기 남은 메모리 ${mi.availMem / 1048576} / ${mi.totalMem / 1048576} MB" + (if (mi.lowMemory) " · 기기 메모리 부족 상태" else "")
                    else "게임 프로세스가 없음(메모리 부족으로 꺼졌을 수 있음)")
                // 9) 동 사이 이동: 44초에 게임이 차를 옆 동(앱에 든 동) 안쪽 길로 옮긴다 → 평소와 같은 길로 그 동 지도로 바뀐다.
                //    바뀐 뒤에도 살아 있는지와 메모리를 본다(바뀌었는지는 로그의 DCCROSS 줄).
                delay(38_000)
                val p4 = am.runningAppProcesses?.firstOrNull { it.processName.endsWith(":game3d") }
                val pss4 = p4?.let { runCatching { am.getProcessMemoryInfo(intArrayOf(it.pid))[0].totalPss / 1024 }.getOrNull() }
                check("동 사이 이동 뒤에도 실행 중", p4 != null,
                    if (p4 != null) "3D 메모리(PSS) ${pss4 ?: -1} MB" else "게임 프로세스가 없음(지도를 바꾸다 꺼졌을 수 있음)")
                delay(16_000)
                am.runningAppProcesses?.firstOrNull { it.processName.endsWith(":game3d") }?.let { android.os.Process.killProcess(it.pid) }
                startActivity(Intent(this, SelfTestActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
                delay(1_500)
            }
        } else {
            line("INFO", "3D 게임", "이 빌드에는 3D 가 없음(unity/export 없음) — 2D(Godot)만")
        }

        line("INFO", "끝", "통과 $pass · 실패 $fail")
        Log.i(TAG, "DONE pass=$pass fail=$fail")
    }

    private suspend fun measureTts(svc: CoachService) {
        val am = getSystemService(Context.AUDIO_SERVICE) as AudioManager
        val samples = listOf(
            PhraseId("guide_turn_left_now") to emptyMap(),
            PhraseId("guide_turn_right_now") to emptyMap(),
            PhraseId("guide_turn_left_unprotected") to emptyMap(),
            PhraseId("guide_turn_right_300m") to mapOf("distance" to "300"),
        )
        for ((id, vars) in samples) {
            if (Phrases.find(id) == null) continue
            waitUntil(8_000) { !svc.speaker.isSpeaking }
            delay(1_000)
            val text = Phrases.render(id, vars)
            val chars = text.count { it.isLetterOrDigit() }
            val engine = svc.speaker.activeName.value
            svc.voice.onCoachWillSpeak() // 실제 코치처럼 마이크를 먼저 닫는다("확인하십시오" 를 명령으로 듣지 않게)
            val t0 = SystemClock.elapsedRealtime()
            svc.speaker.speak(text, "selftest:${id.id}", true)
            var soundStart = -1L
            var soundEnd = -1L
            var done = -1L
            var usage = -1
            while (SystemClock.elapsedRealtime() - t0 < 20_000) {
                val now = SystemClock.elapsedRealtime()
                val playing = runCatching { am.activePlaybackConfigurations }.getOrNull().orEmpty()
                if (playing.isNotEmpty()) {
                    if (soundStart < 0) { soundStart = now; usage = playing.first().audioAttributes.usage }
                    soundEnd = -1L
                } else if (soundStart >= 0 && soundEnd < 0) soundEnd = now
                if (done < 0 && !svc.speaker.isSpeaking) done = now
                if (done >= 0 && soundEnd >= 0 && now - maxOf(done, soundEnd) > 600) break
                delay(40)
            }
            fun sec(a: Long, b: Long) = if (a < 0 || b < 0) "?" else String.format("%.2f", (b - a) / 1000.0)
            line("INFO", "TTS 길이 ${id.id}",
                "${chars}자 · 시작까지 ${sec(t0, soundStart)}초 · 소리 ${sec(soundStart, soundEnd)}초 · 끝 알림-소리 끝 ${sec(soundEnd, done)}초 · " +
                    "전체 ${sec(t0, done)}초 · usage=$usage · $engine")
            Log.i(TAG, "TTSDUR id=${id.id} chars=$chars request_to_sound=${soundStart - t0} sound=${if (soundEnd > 0 && soundStart > 0) soundEnd - soundStart else -1} " +
                "done_minus_sound_end=${if (soundEnd > 0 && done > 0) done - soundEnd else -9999} total=${if (done > 0) done - t0 else -1} usage=$usage rate=${svc.coach.speechRate.value} engine=$engine")
        }
    }

    /** 녹음을 넣고 인식 결과(없으면 "")를 기다린다. 넣지 못하면 null. */
    private suspend fun injectAndWait(voice: VoiceCommandListener, pcm: ByteArray): String? =
        withTimeoutOrNull(pcm.size / 32L + 10_000L) {
            suspendCancellableCoroutine { cont ->
                val ok = voice.inject(pcm) { text -> if (cont.isActive) cont.resume(text) }
                if (!ok && cont.isActive) cont.resume(null)
            }
        }

    private suspend fun waitUntil(timeoutMs: Long, cond: () -> Boolean): Boolean {
        val end = System.currentTimeMillis() + timeoutMs
        while (System.currentTimeMillis() < end) {
            if (cond()) return true
            delay(200)
        }
        return cond()
    }

    companion object {
        private const val TAG = "DCTEST"
        /** 녹음 이름 앞부분(PleosLab.kindOf) → 기대 명령. chat = 명령이 아니어야 함. */
        private val EXPECTED: Map<String, Command?> = mapOf(
            "next" to Command.NEXT, "again" to Command.AGAIN, "confirm" to Command.CONFIRM, "prev" to Command.PREV,
            "quiet" to Command.QUIET, "pullover" to Command.PULL_OVER, "skip" to Command.SKIP, "chat" to null,
        )
    }
}
