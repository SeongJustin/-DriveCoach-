// 앱 연결 — 차량 신호 → core 모듈 → 발화 정책 → TTS, 그리고 화면용 상태. 코스 주행(5장)은 NaviPort 이벤트도 같은 길로 보낸다
package kr.drivecoach.app

import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kr.drivecoach.app.navi.NaviArrival
import kr.drivecoach.app.navi.NaviLocation
import kr.drivecoach.app.navi.NaviPort
import kr.drivecoach.app.navi.NaviPortListener
import kr.drivecoach.app.navi.NaviRouteStart
import kr.drivecoach.app.navi.NaviTbt
import kr.drivecoach.app.speech.Speaker
import kr.drivecoach.app.speech.SpeakerListener
import kr.drivecoach.app.vehicle.PleosSignalSource
import kr.drivecoach.core.course.Course
import kr.drivecoach.core.course.CourseOptionId
import kr.drivecoach.core.course.CourseResult
import kr.drivecoach.core.course.CourseSource
import kr.drivecoach.core.course.LatLon
import kr.drivecoach.core.course.NaviRequest
import kr.drivecoach.core.course.OffsetCourseProvider
import kr.drivecoach.core.course.TbtGuide
import kr.drivecoach.core.input.AwaitingInputManager
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.input.CommandRecognizer
import kr.drivecoach.core.input.InputMode
import kr.drivecoach.core.input.InputSettings
import kr.drivecoach.core.input.InputSource
import kr.drivecoach.core.input.ShortPressContext
import kr.drivecoach.core.mode.DriveMode
import kr.drivecoach.core.mode.DriveModeDetector
import kr.drivecoach.core.parking.ParkingSlowMode
import kr.drivecoach.core.practice.Phase
import kr.drivecoach.core.practice.PracticeEngine
import kr.drivecoach.core.practice.PracticeItem
import kr.drivecoach.core.report.Metrics
import kr.drivecoach.core.report.TripRecorder
import kr.drivecoach.core.report.TripRecorderConfig
import kr.drivecoach.core.safety.DangerConfig
import kr.drivecoach.core.safety.DangerRules
import kr.drivecoach.core.safety.DangerType
import kr.drivecoach.core.safety.IdleRpmEstimator
import kr.drivecoach.core.signal.CarSpec
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseCategory
import kr.drivecoach.core.tts.PhraseId
import kr.drivecoach.core.tts.Phrases
import kr.drivecoach.core.tts.SpeechPolicy

/**
 * 앱의 "두뇌". MainActivity 가 하나만 만들어 들고 있다.
 *
 * 데이터 길:
 *  PleosSignalSource.stateFlow ──(메인 디스패처 코루틴)──► [onState]
 *     ├─ DriveModeDetector  → 주차/주행 모드 (화면 정책·발화 모드)
 *     ├─ IdleRpmEstimator   → 공회전 RPM 학습 (R 기어 엑셀 판정의 기준)
 *     ├─ DangerRules        → 위험 이벤트 → DANGER 멘트
 *     ├─ TripRecorder       → 주행 지표 누적 (P 복귀 시 마무리 한마디)
 *     ├─ PracticeEngine     → 연습 자동 통과·단계 멘트 (연습 중일 때만)
 *     ├─ ParkingSlowMode    → 4-5 직각 주차: 조향각·기어로 자동 통과, R 중 경고 (열려 있을 때만, R/D 로 가도 유지)
 *     └─ AwaitingInputManager.tick → 무응답 재안내
 *  NaviPort(PleosNaviAdapter) ──► [naviListener] 5-1 코스 전달 결과 / 5-3 TBT·제한속도 / 5-4 도착 (코스 주행이 열려 있을 때만)
 *  각 모듈이 내놓는 PhraseId → [request] → SpeechPolicy.decide → [Speaker]
 *
 * 스레드: 모든 메서드는 메인 스레드에서 부른다(신호 소스도 메인에서 발행, Speaker 콜백도 메인).
 * 사용자 문장은 core/tts/Phrases 의 ID 로만 다룬다.
 */
class CoachController(
    /** 차량 상태 스트림(PleosSignalSource.stateFlow). */
    internal val stateFlow: StateFlow<VehicleState>,
    /** 가장 최근 상태(틱에서 쓴다). */
    internal val currentState: () -> VehicleState,
    internal val speaker: Speaker,
    internal val scope: CoroutineScope,
    internal val speakerNameFlow: StateFlow<String>? = null,
    /** 벽시계(ms). 발화 정책의 쿨다운·최소 간격 기준. 테스트에서는 가짜 시계를 넣는다. */
    internal val clock: () -> Long = { System.currentTimeMillis() },
    /** 차량 내비 접점(CoachService 가 소유). null 이면 코스 주행 메뉴가 ERROR 로 안내한다. */
    internal val navi: NaviPort? = null,
    /**
     * 코스 소스. 키가 있으면 CoachService 가 DifficultyCourseGenerator(NaverDirectionsProvider) 를 넣고(실제 길찾기, 백그라운드 호출),
     * 없으면 기본값인 오프셋 코스(OffsetCourseProvider).
     */
    internal val courseSource: CourseSource = OffsetCourseProvider(),
    /** 길찾기 실패 때 "임시 코스로 진행" 이 쓰는 마지막 수단. courseSource 가 이미 오프셋이면 쓰이지 않는다. */
    internal val fallbackCourseSource: CourseSource = OffsetCourseProvider(),
    /** 길찾기 어댑터 진단 줄(NaverDirectionsProvider.diag). 화면 "내비 진단" 에 같이 보인다. */
    internal val routeDiag: StateFlow<List<String>>? = null,
    /**
     * 제한속도를 모를 때 쓸 기본값(km/h). **시연 빌드에서만** 50 을 넣고(local.properties DEMO_SPEED_LIMIT_KMH=50), 평소·제출 빌드는 null —
     * 모르면 과속 피드백을 하지 않는다.
     */
    internal val defaultSpeedLimitKmh: Double? = null,
) {
    /** 앱에서 쓰는 생성자. 테스트·시뮬레이션은 위 기본 생성자에 가짜 Flow 를 넣는다. */
    constructor(
        signals: PleosSignalSource, speaker: Speaker, scope: CoroutineScope, speakerNameFlow: StateFlow<String>? = null, navi: NaviPort? = null,
        courseSource: CourseSource = OffsetCourseProvider(), routeDiag: StateFlow<List<String>>? = null, defaultSpeedLimitKmh: Double? = null,
    ) : this(signals.stateFlow, { signals.current }, speaker, scope, speakerNameFlow, navi = navi, courseSource = courseSource, routeDiag = routeDiag,
        defaultSpeedLimitKmh = defaultSpeedLimitKmh)

    // ───────── core 모듈 ─────────
    internal val modeDetector = DriveModeDetector()
    internal val idleRpm = IdleRpmEstimator()
    internal val danger = DangerRules(idleRpmProvider = { idleRpm.idleRpm })
    internal var recorder = newRecorder()
    internal val awaiting = AwaitingInputManager()
    internal val policy = SpeechPolicy()
    /** 휠 버튼은 설정 없이도 바로 쓰게 WHEEL_BUTTON 모드로 둔다(음성 STT 는 아직 연결 전). */
    internal val recognizer = CommandRecognizer(InputSettings(mode = InputMode.WHEEL_BUTTON))
    internal var practice: PracticeEngine? = null
    internal var practiceItems: List<PracticeItem> = emptyList()
    /** 4-5 직각 주차 슬로우 모드(열려 있을 때만). */
    internal var slowMode: ParkingSlowMode? = null

    // ───────── 화면용 상태 ─────────
    internal val _mode = MutableStateFlow(DriveMode.UNKNOWN)
    val mode: StateFlow<DriveMode> = _mode.asStateFlow()
    internal val _practiceUi = MutableStateFlow<PracticeUi?>(null)
    val practiceUi: StateFlow<PracticeUi?> = _practiceUi.asStateFlow()
    /** 세션 동안 누적된 항목 결과(통과/건너뜀). 엔진을 새로 만들 때 넘겨 이어 간다. */
    internal val practiceResults = LinkedHashMap<String, Phase>()
    internal val _practiceDone = MutableStateFlow<Set<String>>(emptySet())
    /** 통과한 항목 ID (연습 선택 화면의 ✓ 표시). */
    val practiceDone: StateFlow<Set<String>> = _practiceDone.asStateFlow()
    /** 결과가 바뀔 때마다 서비스가 저장(SharedPreferences). */
    var onPracticeResultsChanged: ((Map<String, Phase>) -> Unit)? = null
    var onQuietChanged: ((Boolean) -> Unit)? = null
    /** (내비로 보낸 뒤 "닫기"를 눌러도 내비 화면에 남는다) 경로를 취소한 뒤 앱 화면을 앞으로 */
    var onBringToFront: (() -> Unit)? = null

    internal val _subMode = MutableStateFlow(SubMode.NONE)
    /** 지금 열려 있는 하위 활동(연습/핸들/주차). 화면이 어떤 카드를 그릴지 정한다. */
    val subMode: StateFlow<SubMode> = _subMode.asStateFlow()
    internal val _lawUi = MutableStateFlow<LawUi?>(null)
    /** 지금 보는 나라의 법규 묶음(기본 한국). */
    internal var lawSet: kr.drivecoach.core.law.LawSet = kr.drivecoach.core.law.LawSets.korea.also {
        kr.drivecoach.core.law.LawSets.all   // 다른 나라 법규 멘트 등록을 앱 시작 때(메인 스레드) 끝내 둔다
    }
    /** 도로 법규 화면. null 이면 닫힘. */
    val lawUi: StateFlow<LawUi?> = _lawUi.asStateFlow()
    internal val _parkingUi = MutableStateFlow<ParkingUi?>(null)
    /** 직각 주차 화면 상태. 슬로우 모드가 열려 있지 않으면 null. */
    val parkingUi: StateFlow<ParkingUi?> = _parkingUi.asStateFlow()
    // 주차 배우기: 따라 하지 않고 단계 영상과 설명만 한 장씩 본다
    internal val _parkLearnUi = MutableStateFlow<ParkLearnUi?>(null)
    val parkLearnUi: StateFlow<ParkLearnUi?> = _parkLearnUi.asStateFlow()
    internal var parkLearnPages: List<ParkLearnPage> = emptyList()
    internal val _courseUi = MutableStateFlow<CourseUi?>(null)
    /** 코스 주행 화면 상태. 코스 메뉴가 열려 있지 않으면 null. */
    val courseUi: StateFlow<CourseUi?> = _courseUi.asStateFlow()
    internal val _lastSpoken = MutableStateFlow("")
    val lastSpoken: StateFlow<String> = _lastSpoken.asStateFlow()
    internal val _dangerCount = MutableStateFlow(0)
    val dangerCount: StateFlow<Int> = _dangerCount.asStateFlow()
    internal val _quiet = MutableStateFlow(false)
    val quiet: StateFlow<Boolean> = _quiet.asStateFlow()

    internal val _speechRate = MutableStateFlow(1f)
    /** 말 빠르기(설정 화면). 0.85 느리게 / 1.0 보통 / 1.2 빠르게. */
    val speechRate: StateFlow<Float> = _speechRate.asStateFlow()

    /** 설정 "음성 속도". 엔진에 바로 적용(다음 문장부터). 저장은 서비스가 한다(onSpeechRateChanged). */
    fun setSpeechRate(rate: Float) {
        _speechRate.value = rate
        speaker.setRate(rate)
        onSpeechRateChanged?.invoke(rate)
    }
    var onSpeechRateChanged: ((Float) -> Unit)? = null
    /** 코치가 말하기 직전(speaker.speak 앞)에 불린다. 서비스가 항상 듣기 마이크를 바로 닫는 데 쓴다. */
    var onWillSpeak: (() -> Unit)? = null

    /** 설정 "코칭 음성" 켜기/끄기(= 조용히). 멘트 없이 조용히 바꾼다. */
    fun setQuiet(quiet: Boolean) {
        if (_quiet.value == quiet) return
        _quiet.value = quiet
        if (quiet) interruptCoaching()
        onQuietChanged?.invoke(quiet)
    }
    internal val _speakerName = MutableStateFlow(speaker.name)
    /** 진단 화면용 현재 TTS 엔진 이름. */
    val speakerName: StateFlow<String> = speakerNameFlow ?: _speakerName.asStateFlow()
    internal val _log = MutableStateFlow<List<String>>(emptyList())
    /** 진단 화면용: 최근 발화 판정 기록("ok danger_hard_brake", "DROP quiet_mode practice_…"). */
    val speechLog: StateFlow<List<String>> = _log.asStateFlow()
    /** 마지막 주행의 지표(P 복귀 후). 없으면 null. */
    var lastTripMetrics: Metrics? = null
        internal set

    // ───────── 발화 상태 ─────────
    internal data class Speaking(
        val id: PhraseId, val category: PhraseCategory, val utteranceId: String, val startedMs: Long,
        /** 이 문장이 끝나기까지 걸릴 것으로 보는 시간(ms). 긴 설명(최대 200자)을 감시견이 끊지 않게 글자 수로 계산한다. */
        val expectedMs: Long = SPEAK_WATCHDOG_MS,
        val vars: Map<String, String> = emptyMap(),
    )
    internal var speaking: Speaking? = null
    internal var utteranceSeq = 0
    internal val afterUtterance = HashMap<String, () -> Unit>()
    internal var lastPhraseSpoken: Pair<PhraseId, Map<String, String>>? = null
    internal var restricted = false
    internal var coachingPaused = false
    /** "세울래요" 뒤 아직 서지 않음. 속도 0 이 되면 호흡 안내 한 번. */
    internal var pullOverPending = false
    internal var carName: String? = null
    /** 차가 차종을 알려 줬는지. 못 알아내면 기본 제원(아반떼)으로 동작하고 환영 문장도 그렇게 말한다. */
    internal var carDetected = false
    internal var lastStepWasNotDetected = false
    /** 통과/건너뜀 뒤 자동으로 다음 항목으로 넘어간 시각. 직후의 '다음' 은 무시한다(설명을 못 보고 넘어가던 문제). */
    internal var practiceAutoAdvancedAt = 0L
    internal var collectJob: Job? = null
    internal var tickJob: Job? = null
    // 직각 주차: 마지막으로 보여 준 일러스트·문장(선택/평가 단계에는 단계가 없어 직전 것을 유지)
    internal var parkingIllustration: String? = null
    internal var parkingText = ""
    /** 직각 주차: 단계 문장 아래에 잠깐 보여 주는 안내(아직 감지 안 됨·먼저 정지·처음부터 등). */
    internal var parkingNotice: String? = null
    internal var parkingNoticeAt = 0L
    internal var parkingDone = false
    internal val _lessonsDone = MutableStateFlow<Set<String>>(emptySet())
    /** 끝낸 레슨("parking", "law:…") — 선택 화면 버튼에 ✓. 앱을 껐다 켜도 유지. */
    val lessonsDone: StateFlow<Set<String>> = _lessonsDone.asStateFlow()
    var onLessonsDoneChanged: ((Set<String>) -> Unit)? = null
    fun restoreLessonsDone(done: Set<String>) { _lessonsDone.value = done }
    internal fun markLessonDone(key: String) {
        if (key in _lessonsDone.value) return
        _lessonsDone.value = _lessonsDone.value + key
        onLessonsDoneChanged?.invoke(_lessonsDone.value)
    }
    // 코스 주행: 열려 있으면 phase 가 null 이 아니다
    internal var coursePhase: CoursePhase? = null
    internal var courseChosen: CourseOptionId? = null
    internal var course: Course? = null
    internal var naviRequest: NaviRequest? = null
    internal var courseSummaryText = ""
    internal var courseMismatch: String? = null
    internal var courseError: String? = null
    internal var courseRouteStart: NaviRouteStart? = null
    internal var courseArrival: NaviArrival? = null
    internal var courseResult: CourseResultUi? = null
    internal var courseSpeedLimit: Int? = null
    internal var courseTbt: NaviTbt? = null
    internal val tbtGuide = TbtGuide()
    /** 5-2 브리핑을 이 경로에서 이미 했는지(onRouteStarted 와 onRoutingCompleted 가 둘 다 올 수 있다). */
    internal var courseBriefed = false
    /** 코스 생성(백그라운드) 작업. 닫거나 다시 고르면 취소한다. */
    internal var courseGenJob: Job? = null
    /** 코스 생성 요청 번호. 늦게 끝난 이전 요청의 결과를 버리기 위해. */
    internal var courseGenSeq = 0
    /** 마지막 코스 생성 결과(호출 수·실패 이유). */
    internal var courseGenResult: CourseResult? = null
    /** 마지막 코스가 실제 길찾기(true)인지 오프셋 임시 코스(false)인지. */
    internal var courseGenerated = false
    /** (같은 목적을 다시 고르면 다른 코스) 목적별 마지막 결과. 같은 자리(300 m 안)·같은 방식이면 다시 쓴다. */
    internal val courseCache = HashMap<CourseOptionId, Triple<LatLon, CourseResult, Boolean>>()
    /** 마지막 코스 생성이 길찾기 쪽에서 실패해 "임시 코스로 진행" 을 제안할 수 있는지. */
    internal var courseFallbackAvailable = false
    /** 코스 난이도 1~5 와 연습 시간(분). 키가 있을 때 난이도별 생성기가 쓴다. */
    internal var courseDifficulty = 2
    internal var courseTargetMinutes = 20

    // ═══════════════════════════ 생명주기 ═══════════════════════════

    /** 신호 구독과 1초 틱을 시작한다. onCreate 에서 한 번. */
    fun start() {
        speaker.setListener(speakerListener)
        modeDetector.addListener { prev, next, state -> onModeChanged(prev, next, state) }
        danger.addListener { event ->
            _dangerCount.value = _dangerCount.value + 1
            // 급제동은 그 자리에서 말하지 않고 P 복귀 뒤 리포트("급제동 N번")에서만 짚는다 — 즉시 지적은 질책처럼 들림
            if (event.type == DangerType.HARD_BRAKE) log("HARD_BRAKE (리포트로)") else request(event.phraseId)
        }
        navi?.setListener(naviListener)
        // Main.immediate: 신호 소스가 메인 스레드에서 value 를 바꾸면 그 자리에서 collect 가 이어져 중간 값이 합쳐지지 않는다
        // (StateFlow 는 느린 수집자에게 중간 값을 건너뛰므로, 깜빡이 LEFT→OFF 같은 짧은 변화를 놓치지 않으려면 이게 필요).
        collectJob = scope.launch(Dispatchers.Main.immediate) {
            stateFlow.collect { state -> onState(state) }
        }
        tickJob = scope.launch(Dispatchers.Main) {
            while (isActive) {
                delay(TICK_MS)
                tick()
            }
        }
    }

    /** onDestroy. 구독을 끊고 TTS 를 멈춘다(Speaker.release 는 소유자인 Activity 가 한다). */
    fun stop() {
        collectJob?.cancel(); collectJob = null
        tickJob?.cancel(); tickJob = null
        navi?.setListener(null)
        speaker.stop()
        speaker.setListener(null)
    }

    /** 차종 인식 결과. 환영 한마디는 인트로가 끝나고 메뉴가 보일 때 한다 — [onIntroFinished]. */
    fun onCarSpec(spec: CarSpec) {
        carName = spec.name
        carDetected = spec.detected
        welcomePending = true
        if (introFinished) speakWelcome()
    }

    internal var introFinished = false
    internal var welcomePending = false

    /** 화면이 인트로를 끝내고 메뉴를 보여 줄 때 호출. 차종을 이미 알면 그때 환영 멘트를 한다. */
    fun onIntroFinished() {
        introFinished = true
        if (welcomePending) speakWelcome()
    }

    /** AAOS CarUxRestrictions 의 DO 요구 여부. 화면 모드와 발화 모드에 "더 엄격한 쪽" 으로 합쳐진다. */
    fun setRestricted(requiresDo: Boolean) { restricted = requiresDo }


    // ═══════════════════════════ 신호 ═══════════════════════════

    internal fun onState(state: VehicleState) {
        modeDetector.onState(state)
        idleRpm.onState(state)
        danger.onState(state)
        recorder.onState(state)
        practice?.onState(state)
        // 기어 드릴 예외로 열어 둔 연습도 차가 실제로 움직이면 접는다.
        if (_mode.value != DriveMode.PARKED_LEARN && practice?.current != null && (state.speedKmh ?: 0.0) > STOPPED_SPEED_KMH) {
            endActivity(); awaiting.cancel()
            request(Phrases.systemDriveStart.id)
        }
        speakWarningLamps(state)
        if (pullOverPending && (state.speedKmh ?: 1.0) <= STOPPED_SPEED_KMH) {
            pullOverPending = false
            request(Phrases.panicStoppedBreath.id, force = true)
        }
        slowMode?.let { m ->
            m.onState(state)
            publishParkingUi(state)
        }
        handleNoInput(awaiting.tick(state.timestampMs, state))
    }

    internal fun tick() {
        val now = clock()
        // 신호 소스는 값이 바뀔 때만 상태를 발행한다. 차가 가만히 서 있으면 새 상태가 안 와서
        // DriveModeDetector 의 "2초 유지" 판정이 영영 끝나지 않으므로(에뮬레이터에서 UNKNOWN 에 머묾),
        // 틱마다 현재 상태를 지금 시각으로 다시 넣어 준다. 값이 같으면 다른 모듈에는 영향이 없다.
        modeDetector.onState(currentState().copy(timestampMs = now))
        // 주차 단계도 같다: 들어갈 때 이미 맞던 단계는 2.5초 뒤의 신호로 넘어가는데, 차가 서 있으면 새 신호가 없어 멈춰 있었다
        slowMode?.let { m -> val s = currentState().copy(timestampMs = now); m.onState(s); publishParkingUi(s) }
        handleNoInput(awaiting.tick(now, currentState()))
        if (slowMode != null && parkingNotice != null && now - parkingNoticeAt > PARKING_NOTICE_MS) {
            parkingNotice = null
            publishParkingUi(currentState())
        }
        // TTS 가 끝났다는 콜백이 영영 안 오는 경우(엔진 이상) 를 대비한 감시견
        speaking?.let { s ->
            if (now - s.startedMs > s.expectedMs) {
                Log.w(COACH_TAG, "발화 종료 콜백 없음 → 강제 종료: ${s.id.id}")
                onUtteranceFinished(s.utteranceId, completed = false)
            }
        }
    }

    internal fun onModeChanged(prev: DriveMode, next: DriveMode, state: VehicleState) {
        _mode.value = next
        when {
            // 주차 → 주행: 연습·핸들 감각(P 전용)·대기 입력은 접고, 주행 한마디.
            // 직각 주차 슬로우 모드는 R/D 로 움직이는 것이 본래 흐름이므로 접지 않는다(음성으로 계속, 발화 모드는 PRACTICE).
            // 코스 주행(5장)은 주행이 본체이므로 역시 접지 않는다 — 내비 TBT·제한속도·도착 이벤트를 주행 중에 받아 GUIDE 로 말한다.
            prev == DriveMode.PARKED_LEARN && next != DriveMode.PARKED_LEARN -> {
                val parking = _subMode.value == SubMode.PARKING
                val courseOpen = _subMode.value == SubMode.COURSE
                // 기어 순서(P→R→N→D)·급발진 드릴은 서 있는 채로 기어를 옮기는 것이 과제라, 속도 0 이면 연습을 접지 않는다.
                // 실제로 움직이면(속도 > 0.5) onState 가 접는다.
                val gearDrill = _subMode.value == SubMode.PRACTICE && isGearDrill(practice?.current) && (state.speedKmh ?: 0.0) <= STOPPED_SPEED_KMH
                if (!parking && !courseOpen && !gearDrill) { endActivity(); awaiting.cancel() }
                // 코스를 이미 내비에 보냈으면(ROUTING/DRIVING) 도착 리포트(5-4)를 위해 sendCourseToNavi 에서 만든 기록기를 이어 간다.
                val courseTrip = coursePhase == CoursePhase.ROUTING || coursePhase == CoursePhase.DRIVING
                if (!courseTrip) recorder = newRecorder()
                danger.reset()
                if (!parking && !gearDrill) request(Phrases.systemDriveStart.id)
            }
            // 주행 → 주차: 주행 마무리 한마디 (+ 급제동이 있었으면 횟수). 주차 슬로우 모드 중이면 "P 넣어요" 통과가 곧 마무리라 생략.
            // 코스 주행에서 도착한 뒤 세우면 5-4 리포트 멘트(report_*)가 이어진다.
            next == DriveMode.PARKED_LEARN && prev != DriveMode.PARKED_LEARN -> {
                coachingPaused = false
                val metrics = recorder.finish()
                lastTripMetrics = metrics
                val arrived = coursePhase == CoursePhase.ARRIVED
                // 변속 연습 중에 P 로 되돌린 것은 주행을 마친 것이 아니다(차는 내내 서 있었다) → 정차 멘트 생략
                val gearDrillBack = _subMode.value == SubMode.PRACTICE && metrics.distanceKm <= 0.0
                if (_subMode.value != SubMode.PARKING && !gearDrillBack && (prev != DriveMode.UNKNOWN || metrics.distanceKm > 0.0)) {
                    if (arrived) {
                        // 코스 도착 후 정차: "세웠어요" 가 끝난 뒤 5-4 리포트 줄을 시작한다(같은 순위끼리 미루다 포기하지 않도록 onDone 으로 잇는다).
                        request(Phrases.systemDriveEnd.id, onDone = { speakCourseReport(metrics) })
                    } else {
                        request(Phrases.systemDriveEnd.id)
                        if (metrics.hardBrakeCount > 0) {
                            request(Phrases.reportStatHardBrake.id, mapOf("count" to metrics.hardBrakeCount.toString()))
                        }
                    }
                } else if (arrived) {
                    speakCourseReport(metrics)
                }
                recorder = newRecorder()
            }
        }
    }

    /** 급제동 임계는 safety 모듈과 같은 상수를 쓴다(리포트와 위험 피드백이 같은 사건을 세도록). */
    internal fun newRecorder() = TripRecorder(
        TripRecorderConfig(hardBrakeDecelMps2 = DangerConfig.DEFAULT.hardBrakeDecelMps2, speedLimitKmh = danger.speedLimitKmh ?: defaultSpeedLimitKmh),
    )

    init {
        danger.speedLimitKmh = defaultSpeedLimitKmh
    }

    // ═══════════════════════════ 명령 ═══════════════════════════

    /** 휠 버튼(짧게/길게) → Command. 짧게의 뜻은 지금 확인을 기다리는지에 따라 다르다. */
    fun onWheelButton(source: InputSource.WheelButton) {
        val waitingConfirm = awaiting.current?.accepts?.contains(Command.CONFIRM) == true
        val ctx = if (waitingConfirm) ShortPressContext.AWAITING_CONFIRM else ShortPressContext.IDLE_OR_AWAITING_NEXT
        recognizer.recognize(source, ctx)?.let { onCommand(it) }
    }

    /**
     * 음성 명령(Pleos STT 최종 문장). core 의 동의어 사전으로 해석하고, 못 알아들으면 아무 반응도 하지 않는다(재촉 금지).
     * 입력 설정(휠 버튼 모드)과 관계없이 받는다 — 누르고 말하기라 오인식 위험이 낮다.
     */
    /**
     * (말하지 않았는데 단계가 넘어가고, 주변 말이 화면에 뜬다) 들은 문장 → 명령.
     * 명령은 짧게 말한다("다음", "다시 한 번", "확인했어요"). 항상 듣기는 차 안 대화·라디오도 받아 적는데, 그 긴 문장 속에
     * "다음"·"확인" 같은 낱말이 들어 있으면 명령으로 알아듣던 것을 막는다: 띄어쓰기를 뺀 글자 수가 [VOICE_COMMAND_MAX_CHARS] 를 넘으면 명령이 아니다.
     * "세울래요"(갓길 정차)는 안전 명령이라 길이와 상관없이 받는다. 단, 길이 예외는 "세울래", "세워" 처럼 분명한 말에만 준다
     * ("잠깐만 저기 편의점 들렀다 가자" 같은 대화 속 "잠깐", "힘들어", "멈춰"로 정차 안내가 나가던 것).
     */
    fun voiceCommand(transcript: String): Command? {
        val cmd = recognizer.recognizeVoice(transcript) ?: return null
        if (transcript.count { !it.isWhitespace() } > VOICE_COMMAND_MAX_CHARS) {
            val clearPullOver = cmd == Command.PULL_OVER && PULL_OVER_CLEAR.containsMatchIn(transcript.replace(" ", ""))
            if (!clearPullOver) return null
        }
        return cmd
    }

    fun onVoice(transcript: String) {
        val cmd = voiceCommand(transcript)
        Log.i(COACH_TAG, if (kr.drivecoach.app.BuildConfig.DEBUG) "voice '$transcript' -> $cmd" else "voice ${transcript.length}자 -> $cmd")   // 대화 원문은 debug 에서만
        cmd?.let { onCommand(it) }
    }

    /** 화면 버튼·휠 버튼·음성에서 온 명령 하나를 처리한다. */
    fun onCommand(command: Command) {
        val now = clock()
        Log.i(COACH_TAG, "command $command")
        when (command) {
            Command.QUIET -> {
                val q = !_quiet.value
                _quiet.value = q
                onQuietChanged?.invoke(q)
                if (q) interruptCoaching() // "조용히" 는 지금 말하던 안내도 바로 끊는다
                request(if (q) Phrases.systemQuietAck.id else Phrases.systemCmdQuietOff.id, force = true)
            }
            Command.PULL_OVER -> {
                awaiting.cancel()
                speaker.stop()
                // 이미 P 에 서 있으면 정차 안내도, 코칭 멈춤도 필요 없다. 말하던 것만 멈춘다.
                //   (전에는 여기서 멈춘 코칭을 풀 길이 없어, 차를 몰았다 다시 세울 때까지 연습 음성이 나오지 않았다)
                val parkedNow = _mode.value == DriveMode.PARKED_LEARN && (currentState().speedKmh ?: 0.0) <= STOPPED_SPEED_KMH
                if (parkedNow) {
                    log("PULL_OVER (정차 중이라 말만 멈춤)")
                } else {
                    coachingPaused = true
                    pullOverPending = true
                    request(Phrases.systemPullOverAck.id, force = true)
                }
            }
            Command.CONFIRM, Command.NEXT, Command.AGAIN, Command.SKIP, Command.PREV -> {
                awaiting.onCommand(command, now)
                val engine = practice
                val slow = slowMode
                if (engine != null && engine.current != null) {
                    onPracticeInput(engine, command)
                } else if (slow != null) {
                    onParkingInput(slow, command, now)
                } else if (_parkLearnUi.value != null) {
                    when (command) {
                        Command.NEXT, Command.CONFIRM, Command.SKIP -> parkLearnGo(+1)
                        Command.PREV -> parkLearnGo(-1)
                        else -> parkLearnGo(0)
                    }
                } else if (_lawUi.value != null) {
                    // 도로 법규: 휠 버튼·음성 "다음/확인/건너뛰기" = 다음 주제, "이전" = 이전 주제, "다시" = 이 주제 다시 읽기
                    when (command) {
                        Command.NEXT, Command.CONFIRM, Command.SKIP -> lawNext()
                        Command.PREV -> lawPrev()
                        else -> _lawUi.value?.let { u -> lawSet.byId(u.topicId)?.let { showLaw(it) } }
                    }
                } else if (command == Command.AGAIN) {
                    lastPhraseSpoken?.let { (id, vars) -> request(id, vars, force = true) }
                }
            }
        }
    }

    // ═══════════════════════════ 하위 활동 공통 ═══════════════════════════

    /** 열려 있는 하위 활동(연습/주차/코스/법규)을 닫는다. "닫기" 버튼과 주행 전환에서 쓴다. */
    fun endActivity() {
        when (_subMode.value) {
            SubMode.PRACTICE -> endPractice()
            SubMode.PARKING -> { endParking(); endParkLearn() }
            SubMode.COURSE -> endCourse()
            SubMode.LAW -> endLaw()
            SubMode.NONE -> {}
        }
        // 어느 쪽이든 남은 것이 있으면 정리(방어적).
        if (practice != null) endPractice()
        if (slowMode != null) endParking()
        if (_parkLearnUi.value != null) endParkLearn()
        if (coursePhase != null) endCourse()
        if (_lawUi.value != null) endLaw()
    }

    /** 화면에 어떤 카드가 열려 있는지 갱신한다. 셋 중 하나만 열려 있다는 전제. */
    internal fun refreshSubMode() {
        _subMode.value = when {
            practice?.current != null -> SubMode.PRACTICE
            slowMode != null || _parkLearnUi.value != null -> SubMode.PARKING
            coursePhase != null -> SubMode.COURSE
            _lawUi.value != null -> SubMode.LAW
            else -> SubMode.NONE
        }
    }

    /**
     * 직각 주차 멘트 줄. 주차 모듈은 한 번에 멘트를 여러 개 내놓는다(예: "딱 맞아요" + "이번엔 끝까지").
     * 전부 request() 로 던지면 정책이 각각 1초씩 최대 8번 미루다 포기할 수 있어, 한 문장이 끝나면(onDone) 다음을 넣는 식으로 직렬화한다.
     * 위험 피드백(DANGER)은 이 줄을 거치지 않고 바로 request() 로 간다.
     */
    internal val subModeQueue = ArrayDeque<Pair<PhraseId, Map<String, String>>>()
    /** 멘트가 실제로 나가는 순간 화면 문장을 바꾸는 콜백(직각 주차). */
    internal val screenTextWhenSpoken = HashMap<PhraseId, () -> Unit>()
    internal var subModeQueueBusy = false

    // ═══════════════════════════ 기능별 동작은 다른 파일에 있다 ═══════════════════════════
    //  CoachPractice.kt  차량 조작법(2장) + 도로 법규
    //  CoachParking.kt   직각 주차(4-5)                      CoachCourse.kt    코스 주행(5장)
    //  CoachSpeech.kt    발화 파이프라인(request → SpeechPolicy → Speaker)
    //  CoachUiState.kt   화면 상태 데이터 클래스 + 상수
    // 모두 CoachController 의 확장 함수라서 이 클래스의 상태를 그대로 쓴다. 상태(필드)는 이 파일에만 둔다.

    // ═══════════════════════════ 코스 주행: 내비 리스너 ═══════════════════════════

    /** 내비 이벤트(메인 스레드). 코스 주행이 열려 있을 때만 반응한다. 제한속도만은 항상 과속 규칙에 반영한다. */
    internal val naviListener = object : NaviPortListener {
        override fun onRouteStarted(info: NaviRouteStart) {
            if (coursePhase == null) return
            courseRouteStart = info
            onCourseRouteBegan()
        }

        override fun onRoutingCompleted() {
            if (coursePhase == null) return
            onCourseRouteBegan()
        }

        override fun onRouteCancelled() {
            if (coursePhase == CoursePhase.ROUTING || coursePhase == CoursePhase.DRIVING) {
                coursePhase = CoursePhase.READY
                courseTbt = null
                publishCourseUi()
            }
        }

        override fun onTbt(tbt: NaviTbt) {
            if (coursePhase != CoursePhase.DRIVING) return
            courseTbt = tbt
            // 회전 안내 음성은 차량 내비 한 곳에서만 한다(두 곳에서 말하면 겹침 + Pleos 단일 경로 정책: 경로 안내 음성은
            // Active Route 를 가진 내비 앱만). 우리 앱은 내비를 대신할 수 없으므로 "N m 앞 우회전" 같은 회전 안내를 말하지 않는다.
            // TBT 는 화면 표시(courseTbt)와 기록에만 쓴다. 되살리려면 SPEAK_ROUTE_TURNS 를 true 로.
            val a = tbtGuide.onTbt(tbt.maneuver, tbt.distanceM, tbt.key)
            if (SPEAK_ROUTE_TURNS && a != null) request(a.phraseId, a.params)
            publishCourseUi()
        }

        override fun onRoadSpeedLimit(kmh: Int) {
            val v = if (kmh > 0) kmh else null
            courseSpeedLimit = v
            danger.speedLimitKmh = v?.toDouble() ?: defaultSpeedLimitKmh
            recorder.speedLimitKmh = danger.speedLimitKmh   // 리포트의 과속 집계도 같은 제한속도로
            if (coursePhase != null) publishCourseUi()
        }

        override fun onDestinationArrived(info: NaviArrival) {
            if (coursePhase != CoursePhase.DRIVING && coursePhase != CoursePhase.ROUTING) return
            courseArrival = info
            coursePhase = CoursePhase.ARRIVED
            courseTbt = null
            request(Phrases.guideArrive.id, force = true)
            // 아직 달리는 중이면 P 복귀 때 리포트(onModeChanged). 이미 서 있으면(에뮬레이터 등) 지금 바로.
            if (_mode.value == DriveMode.PARKED_LEARN) {
                val metrics = recorder.finish()
                lastTripMetrics = metrics
                recorder = newRecorder()
                speakCourseReport(metrics)
            }
            publishCourseUi()
        }

        override fun onNaviError(code: String) {
            if (coursePhase == CoursePhase.ROUTING || coursePhase == CoursePhase.DRIVING) {
                setCourseError(code)
                request(Phrases.systemCourseRouteFailed.id, force = true)
            }
        }

        override fun onLocation(loc: NaviLocation) {}
    }

    /** 운전자 입력으로 문맥이 바뀔 때마다 1 증가. DEFER 재시도는 자기 세대가 아니면 버린다. 위험·경고등(GUIDE 보다 우선)은 세대와 무관. */
    internal var requestGen = 0

    // ═══════════════════════════ 발화 상태(동작은 CoachSpeech.kt) ═══════════════════════════

    /** 경고등이 새로 켜지면 "○○ 경고등이 켜졌습니다" + 행동 한마디. 재발화 억제는 SpeechPolicy 가 한다. */
    internal var lastLampState: VehicleState? = null

    internal val speakerListener = object : SpeakerListener {
        override fun onStarted(utteranceId: String) {}
        override fun onFinished(utteranceId: String, completed: Boolean) = onUtteranceFinished(utteranceId, completed)
    }
}

/** 차량 내비가 주는 회전(TBT)을 우리 코치도 말할지. 끔 — 회전 안내는 차량 내비 한 곳에서만. */
internal const val SPEAK_ROUTE_TURNS = false

/** 긴 문장 속에서도 정차 명령으로 받는 분명한 말. */
internal val PULL_OVER_CLEAR = Regex("세울래|세워|세울게|세우고싶")
