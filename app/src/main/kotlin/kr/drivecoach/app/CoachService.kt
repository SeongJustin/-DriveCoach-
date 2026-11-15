// 담당: S (앱 연결 — 코칭 두뇌·신호 소스·TTS 를 Activity 밖 포그라운드 서비스로. 11/3 에뮬레이터: 기어 D 에서 Pleos 런처가 앱 화면을 덮어도 음성 코칭은 계속돼야 함)
package kr.drivecoach.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.car.VehicleIgnitionState
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.os.Binder
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kr.drivecoach.app.game.GameLink
import kr.drivecoach.app.navi.NaverDirectionsProvider
import kr.drivecoach.app.navi.PleosNaviAdapter
import kr.drivecoach.app.diag.PleosLab
import kr.drivecoach.app.speech.FallbackSpeaker
import kr.drivecoach.app.speech.SpeakerFactory
import kr.drivecoach.app.speech.VoiceCommandListener
import kr.drivecoach.app.vehicle.CarSpecLoader
import kr.drivecoach.app.vehicle.PleosSignalSource
import kr.drivecoach.app.vehicle.UxRestrictionsMonitor
import kr.drivecoach.core.practice.Phase
import kr.drivecoach.core.course.CourseSource
import kr.drivecoach.core.course.DifficultyCourseGenerator
import kr.drivecoach.core.course.OffsetCourseProvider
import kr.drivecoach.core.mode.DriveMode
import kr.drivecoach.core.signal.CarSpec

/**
 * 코칭의 "몸통" — 차량 신호 소스, TTS, [CoachController], 주행 규제 감시, 차종 인식을 **Activity 와 무관하게** 소유하는 포그라운드 서비스.
 *
 * 왜 서비스인가:
 *  기어를 D 로 두면 Pleos 런처가 앱 창 전체를 "주행 중에는 사용할 수 없습니다" 로 덮고 MainActivity 는 onPause 된다
 *  (AAOS CarPackageManager 는 우리 액티비티를 허용했고 차단 로그도 비어 있다 — 런처 자체 가림이라 매니페스트로 풀 수 없다).
 *  주행 중에는 화면 학습 없이 **음성만** 안내하므로, 신호 구독·판정·발화는 화면이 가려지거나 닫혀도 돌아야 한다.
 *  그래서 이 셋을 서비스로 옮기고, Activity 는 바인딩해서 같은 StateFlow 를 보여 주기만 한다.
 *
 * 생명주기:
 *  - MainActivity.onCreate 가 `ContextCompat.startForegroundService` + `bindService(BIND_AUTO_CREATE)` 로 띄운다.
 *  - onCreate: 신호 소스 start → TTS 엔진 선택 → 내비 어댑터 start → CoachController.start → 주행 규제·차종 로드. 즉시 `startForeground` (5초 규칙).
 *  - onStartCommand: START_STICKY (시스템이 죽여도 다시 띄움). 매번 startForeground 를 다시 불러도 무해.
 *  - Activity 가 unbind 해도 서비스는 계속 돈다(startService 로 띄웠으므로). "코칭 끄기" 버튼 → `stopService` → onDestroy 에서 전부 해제.
 *  - 알림: 낮은 중요도 채널 하나("운전 코칭 실행 중"), 내용은 마지막 안내 문장(없으면 모드). 알림을 누르면 MainActivity 가 열린다.
 *
 * 스레드: 모든 콜백을 메인으로 모은다(CoachController 규약). 코루틴 스코프는 SupervisorJob + Dispatchers.Main, onDestroy 에서 cancel.
 */
class CoachService : Service() {

    /** 같은 프로세스의 Activity 가 서비스 인스턴스를 바로 얻기 위한 바인더. */
    inner class LocalBinder : Binder() {
        /** 바인딩한 쪽이 StateFlow 를 관찰할 서비스 인스턴스. */
        val service: CoachService get() = this@CoachService
    }

    private val binder = LocalBinder()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    /** 차량 신호 소스(Vehicle SDK + CarPropertyManager + FusedLocation). 바인딩한 Activity 가 stateFlow·diag 를 본다. */
    lateinit var signals: PleosSignalSource
        private set
    /** TTS 엔진(Pleos → Android → 없음). */
    lateinit var speaker: FallbackSpeaker
        private set
    /** 차량 내비(NaviHelper) 어댑터. 코스 주행(5장)의 경로 전달·TBT·제한속도·도착 이벤트. 진단 화면이 diag 를 본다. */
    lateinit var navi: PleosNaviAdapter
        private set
    /** 코칭 두뇌. */
    lateinit var coach: CoachController
        private set
    /** 음성 명령(Pleos STT, 누르고 말하기). 화면 마이크 버튼·휠 음성 버튼이 [listenForCommand] 를 부른다. */
    lateinit var voice: VoiceCommandListener
        private set
    /** (담당 T) 네이버 Directions 어댑터. local.properties 에 NCP 키가 없으면 null(오프셋 코스로 대체). 진단 화면이 diag 를 본다. */
    var directions: NaverDirectionsProvider? = null
        private set
    private var uxMonitor: UxRestrictionsMonitor? = null

    private val _restricted = MutableStateFlow(false)
    /** AAOS CarUxRestrictions 가 DO(주행 중 제한)를 요구하는지. 화면 모드 판정에 쓴다. */
    val restricted: StateFlow<Boolean> = _restricted.asStateFlow()

    private val _carSpec = MutableStateFlow<CarSpec?>(null)
    /** 인식된 차종(없으면 아직 인식 중). 헤더 표시용. */
    val carSpec: StateFlow<CarSpec?> = _carSpec.asStateFlow()

    private var foregroundStarted = false

    override fun onCreate() {
        super.onCreate()
        Log.i(TAG, "onCreate")
        createChannel()
        ensureForeground(initialNotificationText())

        signals = PleosSignalSource(this)
        signals.start()
        speaker = SpeakerFactory.create(this)
        navi = PleosNaviAdapter(this)
        navi.start()
        // (담당 T) 코스 소스: 키가 있으면 실제 길찾기, 생성당 최대 COURSE_MAX_API_CALLS 회(하루 상한은 어댑터가 센다).
        // (11/15 강명수) 키가 있을 때는 난이도별 순환 코스 생성기(DifficultyCourseGenerator: 난이도 1~5, 연습 시간, 출발지 200 m 안 복귀).
        // 키가 없으면(제출 release APK) 지금처럼 오프셋 기본 코스. CoachController 는 CourseSource 만 안다.
        directions = NaverDirectionsProvider.fromBuildConfig(this)
        val courseSource: CourseSource = directions?.let { DifficultyCourseGenerator(it, maxApiCalls = COURSE_MAX_API_CALLS) }
            ?: OffsetCourseProvider()
        Log.i(TAG, "코스 소스: ${if (directions != null) "Naver Directions (키 있음)" else "OffsetCourseProvider (키 없음)"}")
        // 제한속도 기본값은 시연 빌드에서만(local.properties DEMO_SPEED_LIMIT_KMH=50). 0 이면 끔.
        val demoLimit = BuildConfig.DEMO_SPEED_LIMIT_KMH.takeIf { it > 0 }?.toDouble()
        coach = CoachController(signals, speaker, scope, speaker.activeName, navi = navi, courseSource = courseSource, routeDiag = directions?.diag,
            defaultSpeedLimitKmh = demoLimit)
        // 음성 명령: 들은 문장 → CoachController.onVoice(core 의 CommandRecognizer 로 해석)
        voice = VoiceCommandListener(this, isCoachSpeaking = { speaker.isSpeaking }) { text -> coach.onVoice(text) }
        voice.start()
        coach.onWillSpeak = { voice.onCoachWillSpeak() }
        // 설정(음성 속도)은 앱을 껐다 켜도 유지되게 SharedPreferences 에 둔다.
        val prefs = getSharedPreferences(PREFS, MODE_PRIVATE)
        coach.setSpeechRate(prefs.getFloat(PREF_SPEECH_RATE, 1f))
        coach.onSpeechRateChanged = { rate -> prefs.edit().putFloat(PREF_SPEECH_RATE, rate).apply() }
        // 코칭 음성 켜짐/꺼짐·연습 결과(통과/건너뜀)도 유지한다(베타 A-05/D-12/D-40). 결과는 "id=PASSED" 문자열 집합.
        coach.setQuiet(prefs.getBoolean(PREF_QUIET, false))
        coach.onQuietChanged = { q -> prefs.edit().putBoolean(PREF_QUIET, q).apply() }
        coach.onBringToFront = {
            // 내비가 경로 취소 화면을 띄운 뒤에 앞으로 오도록 조금 기다린다
            android.os.Handler(android.os.Looper.getMainLooper()).postDelayed({
                runCatching {
                    startActivity(android.content.Intent(this, MainActivity::class.java)
                        .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK or android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
                }
            }, 700L)
        }
        coach.restorePracticeResults(
            prefs.getStringSet(PREF_PRACTICE_RESULTS, emptySet()).orEmpty().mapNotNull { entry ->
                val (id, ph) = entry.split('=', limit = 2).let { if (it.size == 2) it[0] to it[1] else return@mapNotNull null }
                runCatching { id to Phase.valueOf(ph) }.getOrNull()
            }.toMap()
        )
        coach.restoreLessonsDone(prefs.getStringSet(PREF_LESSONS_DONE, emptySet()).orEmpty())
        coach.onLessonsDoneChanged = { done -> prefs.edit().putStringSet(PREF_LESSONS_DONE, done).apply() }
        coach.onPracticeResultsChanged = { results ->
            prefs.edit().putStringSet(PREF_PRACTICE_RESULTS, results.map { (id, ph) -> "$id=${ph.name}" }.toSet()).apply()
        }
        coach.start()

        // 차종: 콜백 스레드가 보장되지 않으므로 메인 코루틴으로 넘긴다.
        CarSpecLoader(this, vehicle = signals.vehicle).load { spec ->
            scope.launch { _carSpec.value = spec; coach.onCarSpec(spec) }
        }
        // 주행 규제: 콜백은 메인(Car 서비스가 메인 Looper 로 부름)이지만 같은 경로로 통일한다.
        uxMonitor = UxRestrictionsMonitor(this) { requiresDo, _ ->
            scope.launch { _restricted.value = requiresDo; coach.setRestricted(requiresDo) }
        }.also { it.start() }

        // Pleos "Suspend 모드 - 오디오 포커스" 가이드(11/9): 시동이 꺼져(OFF·LOCK) Suspend 로 들어가면 음성을 멈추고 포커스를 반납,
        // 그동안 새로 재생하지 않는다. 시동이 다시 켜지면(ACC·ON·START) 허용만 하고 멈췄던 문장은 다시 틀지 않는다.
        // 앱이 뜰 때의 첫 값으로는 판단하지 않는다(바뀌는 순간만 본다 — 에뮬레이터 기본값 등으로 처음부터 말을 막지 않게).
        signals.onIgnitionState = { s -> scope.launch { onIgnition(s) } }
        // 시스템 종료 직전 방송도 같은 처리(Suspend 대신 꺼지는 경우).
        runCatching {
            ContextCompat.registerReceiver(this, shutdownReceiver, IntentFilter(Intent.ACTION_SHUTDOWN), ContextCompat.RECEIVER_NOT_EXPORTED)
        }.onFailure { Log.w(TAG, "종료 방송 등록 실패: ${it.message}") }

        // 마지막 안내 문장이 바뀔 때마다 알림 내용을 갱신한다(화면이 가려져 있어도 무슨 말을 했는지 알 수 있게).
        scope.launch {
            coach.lastSpoken.collect { text -> updateNotification(text) }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            Log.i(TAG, "ACTION_STOP")
            stopSelf()
            return START_NOT_STICKY
        }
        // startForegroundService 로 띄워졌으면 5초 안에 startForeground 를 불러야 한다. onCreate 에서 이미 했지만 재호출은 무해.
        ensureForeground(coach.lastSpoken.value.ifBlank { initialNotificationText() })
        return START_STICKY
    }

    private var lastIgnition: Int? = null

    private fun onIgnition(state: Int) {
        val prev = lastIgnition
        lastIgnition = state
        if (prev == null || prev == state) return
        val off = isIgnitionOff(state)
        if (off != isIgnitionOff(prev)) {
            Log.i(TAG, "시동 $prev → $state: ${if (off) "Suspend 준비(음성 멈춤·포커스 반납)" else "Resume(음성 허용)"}")
            speaker.setSuspended(off)
        }
    }

    private fun isIgnitionOff(s: Int) = s == VehicleIgnitionState.OFF || s == VehicleIgnitionState.LOCK

    private val shutdownReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            Log.i(TAG, "시스템 종료 방송 → 음성 멈춤·포커스 반납")
            runCatching { speaker.setSuspended(true) }
        }
    }

    /** 게임 프로세스(:game)용 송신기. 게임을 열 때 처음 만든다. */
    private val gameLink by lazy { GameLink(scope, { signals.stateFlow }, carSpec).also { it.onSpeak = { t -> runCatching { coach.speakGameHint(t) } } } }

    /** 설정 > 진단 > "Pleos 실험"(DO 점검·TTS 조건별 시험). 처음 열 때 만든다. */
    val lab by lazy { PleosLab(this) { runCatching { speaker.restart() }; runCatching { voice.restart() } } }

    /** 화면 마이크 버튼: 항상 듣기를 켜고 끈다(11/7). */
    fun toggleVoice() {
        voice.setAlwaysOn(!voice.alwaysOn.value)
    }

    /** 휠 음성 버튼 등 "지금 말할게": 코치가 말하고 있으면 끊는다(자기 목소리를 명령으로 듣지 않게).
     *  항상 듣기면 말이 끊긴 뒤 곧 마이크가 다시 열리고, 꺼져 있으면 한 문장 듣는다. */
    fun listenForCommand() {
        if (voice.state.value != VoiceCommandListener.State.LISTENING) runCatching { speaker.stop() }
        if (!voice.alwaysOn.value) voice.listen()
    }

    /** 같은 프로세스의 Activity 는 LocalBinder, 게임(GameLink.ACTION)은 Messenger 바인더. */
    override fun onBind(intent: Intent?): IBinder =
        if (intent?.action == GameLink.ACTION) gameLink.messenger.binder else binder

    override fun onDestroy() {
        Log.i(TAG, "onDestroy")
        scope.cancel()
        runCatching { coach.stop() }.onFailure { Log.w(TAG, "coach.stop 실패: ${it.message}") }
        runCatching { voice.release() }.onFailure { Log.w(TAG, "voice.release 실패: ${it.message}") }
        runCatching { speaker.release() }.onFailure { Log.w(TAG, "speaker.release 실패: ${it.message}") }
        runCatching { navi.stop() }.onFailure { Log.w(TAG, "navi.stop 실패: ${it.message}") }
        runCatching { unregisterReceiver(shutdownReceiver) }
        signals.onIgnitionState = null
        runCatching { uxMonitor?.stop() }.onFailure { Log.w(TAG, "uxMonitor.stop 실패: ${it.message}") }
        uxMonitor = null
        runCatching { signals.stop() }.onFailure { Log.w(TAG, "signals.stop 실패: ${it.message}") }
        super.onDestroy()
    }

    /**
     * 런타임 권한(android.car.permission.CAR_SPEED 등)이 나중에 허용됐을 때 신호 소스를 다시 시작해 속도 구독을 건다.
     * MainActivity 의 권한 결과 콜백이 부른다. CoachController 의 collect 는 같은 stateFlow 객체를 보고 있으므로 그대로 유지된다.
     */
    fun restartSignals() {
        Log.i(TAG, "restartSignals (권한 변경)")
        signals.stop()
        signals.start()
    }

    // ═══════════════════════════ 알림 ═══════════════════════════

    /** minSdk 28 ≥ O 이므로 채널은 항상 만든다. */
    private fun createChannel() {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val channel = NotificationChannel(CHANNEL_ID, getString(R.string.notif_channel_name), NotificationManager.IMPORTANCE_LOW)
        channel.description = getString(R.string.notif_channel_desc)
        channel.setShowBadge(false)
        nm.createNotificationChannel(channel)
    }

    private fun buildNotification(content: String): Notification {
        val openIntent = Intent(this, MainActivity::class.java)
        // minSdk 28: FLAG_IMMUTABLE(API 23+)·채널 생성자(API 26+) 모두 쓸 수 있다. API 31+ 는 IMMUTABLE/MUTABLE 중 하나가 필수.
        val pending = PendingIntent.getActivity(this, 0, openIntent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_stat_coach)
            .setContentTitle(getString(R.string.notif_title))
            .setContentText(content)
            .setContentIntent(pending)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()
    }

    /** 처음 한 번 startForeground. targetSdk 34 는 서비스 타입이 필수라 API 34+ 에서는 SPECIAL_USE 를 넘긴다. */
    private fun ensureForeground(content: String) {
        if (foregroundStarted) return
        try {
            val n = buildNotification(content)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                startForeground(NOTIFICATION_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
            } else {
                startForeground(NOTIFICATION_ID, n)
            }
            foregroundStarted = true
        } catch (t: Throwable) {
            // 백그라운드 시작 제한(ForegroundServiceStartNotAllowedException 등). 서비스는 일반 서비스로라도 계속 돈다.
            Log.w(TAG, "startForeground 실패: ${t.message}")
        }
    }

    private fun updateNotification(lastSpoken: String) {
        if (!foregroundStarted) return
        val content = lastSpoken.ifBlank { initialNotificationText() }
        runCatching {
            val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            nm.notify(NOTIFICATION_ID, buildNotification(content))
        }.onFailure { Log.w(TAG, "알림 갱신 실패: ${it.message}") }
    }

    /** 아직 말한 것이 없을 때 알림 내용: 현재 모드. */
    private fun initialNotificationText(): String {
        val mode = if (this::coach.isInitialized) coach.mode.value else DriveMode.UNKNOWN
        return getString(
            when (mode) {
                DriveMode.PARKED_LEARN -> R.string.notif_mode_parked
                DriveMode.UNKNOWN -> R.string.notif_mode_unknown
                else -> R.string.notif_mode_driving
            },
        )
    }

    companion object {
        const val PREFS = "coach_settings"
        const val PREF_SPEECH_RATE = "speech_rate"
        const val PREF_QUIET = "quiet"
        const val PREF_PRACTICE_RESULTS = "practice_results"
        const val PREF_LESSONS_DONE = "lessons_done"
        private const val TAG = "CoachService"
        const val CHANNEL_ID = "coach_running"
        const val NOTIFICATION_ID = 1001
        /**
         * (담당 T) 코스 1회 생성에 허용하는 길찾기 호출 수. 하루 상한이 NaverDirectionsProvider.DAILY_CALL_CAP(100)이므로
         * core 기본 12 대신 8 — 하루 최소 12번은 코스를 만들 수 있게. 조건 충족 후보가 나오면 그 전에 멈춘다(보통 1~5회).
         */
        const val COURSE_MAX_API_CALLS = 8
        /** 외부(알림 액션 등)에서 서비스를 끌 때 쓰는 액션. */
        const val ACTION_STOP = "kr.drivecoach.app.action.STOP"

        /** 포그라운드 서비스로 띄우는 인텐트. */
        fun startIntent(context: Context): Intent = Intent(context, CoachService::class.java)
    }
}
