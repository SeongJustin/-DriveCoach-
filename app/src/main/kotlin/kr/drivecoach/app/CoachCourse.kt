// 코스 주행(5장) 동작 — 팀원 담당 영역. 내비 리스너(naviListener)는 CoachController 클래스 안에 있다.
// (11/5 정리) CoachController 를 기능별 파일로 나눴다. 상태는 CoachController 클래스에, 동작은 이 파일의 확장 함수에 있다.
package kr.drivecoach.app

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.runInterruptible
import kr.drivecoach.app.navi.NaviPort
import kr.drivecoach.core.course.CourseBriefing
import kr.drivecoach.core.course.CourseOptionId
import kr.drivecoach.core.course.CourseOptions
import kr.drivecoach.core.course.CourseResult
import kr.drivecoach.core.course.CourseSource
import kr.drivecoach.core.course.CourseSpec
import kr.drivecoach.core.course.CourseSummary
import kr.drivecoach.core.course.Fear
import kr.drivecoach.core.course.LatLon
import kr.drivecoach.core.course.NaviHandoff
import kr.drivecoach.core.course.OffsetCourseProvider
import kr.drivecoach.core.report.Metrics
import kr.drivecoach.core.tts.PhraseId
import kr.drivecoach.core.tts.Phrases

/**
 * "코스 주행" 메뉴. 5-1 선택지(가장 무서운 것 / 목적)를 보여 준다(CHOOSE).
 * 흐름: chooseCourseOption → (코스 소스가 코스 생성, READY) → sendCourseToNavi → (NaviHandoff → NaviPort.requestCourse, ROUTING)
 *      → onRouteStarted(DRIVING, 5-2 브리핑) → onTbt(5-3 안내) / onRoadSpeedLimit(과속 기준) → onDestinationArrived(ARRIVED) → P 복귀 시 5-4 리포트.
 */
fun CoachController.startCourse() {
    if (coursePhase != null) return
    coachingPaused = false   // (11/15 오류 검수) 운전자가 새 활동을 열면 "세울래요"로 멈춘 코칭을 푼다
    endActivity()
    coursePhase = CoursePhase.CHOOSE
    courseChosen = null; course = null; naviRequest = null
    courseSummaryText = ""; courseError = null; courseMismatch = null
    courseRouteStart = null; courseArrival = null; courseResult = null
    courseTbt = null; courseSpeedLimit = navi?.roadSpeedLimit?.value
    courseBriefed = false
    courseGenResult = null; courseGenerated = false; courseFallbackAvailable = false
    tbtGuide.reset()
    refreshSubMode()
    navi?.refresh()
    publishCourseUi()
}

/** 코스 주행을 닫는다. 안내 중이면 내비 경로도 취소한다. */
fun CoachController.endCourse() {
    if (coursePhase == null) return
    val wasRouting = coursePhase == CoursePhase.ROUTING || coursePhase == CoursePhase.DRIVING
    coursePhase = null
    courseGenJob?.cancel(); courseGenJob = null; courseGenSeq++
    if (wasRouting) { navi?.cancelRoute(); onBringToFront?.invoke() }
    // 내비가 준 제한속도가 있으면 유지(onRoadSpeedLimit 은 코스와 무관하게 과속 기준을 갱신), 없으면 기본값.
    danger.speedLimitKmh = courseSpeedLimit?.toDouble() ?: defaultSpeedLimitKmh
    _courseUi.value = null
    clearSubModeQueue()
    refreshSubMode()
}

/**
 * 5-1 선택지를 고르면 현재 위치(차량 신호 lat/lon → 없으면 내비 현재 위치)에서 코스를 만든다.
 * 위치를 모르면 ERROR. 코스 소스(실제 길찾기면 네트워크)는 **Dispatchers.IO 에서** 돌고, 그동안 GENERATING("코스 계산 중…").
 * 코스가 나오면 READY 로 가고 5-2 브리핑 문장·숫자를 화면에 미리 보여 준다(발화는 경로가 시작될 때).
 * 실패하면 ERROR(이유 = HTTP 코드 / 일일 호출 상한 / 네트워크 / 응답 code) + "다시 시도" / "임시 코스로 진행"(오프셋 코스).
 */
fun CoachController.chooseCourseOption(id: CourseOptionId, difficulty: Int = courseDifficulty, targetMinutes: Int = courseTargetMinutes) {
    if (coursePhase == null) return
    // (11/15 강명수) 난이도 1~5·연습 시간. 키가 있을 때(난이도별 생성기)만 코스에 반영된다. 바꾸면 지난 결과(courseCache)는 버린다.
    if (difficulty !in 1..5 || targetMinutes !in 5..90) return
    if (difficulty != courseDifficulty || targetMinutes != courseTargetMinutes) courseCache.clear()
    courseDifficulty = difficulty
    courseTargetMinutes = targetMinutes
    courseChosen = id
    generateCourse(courseSource, isGenerated = courseSource !== fallbackCourseSource && courseSource !is OffsetCourseProvider)
}

/**
 * ERROR → "임시 코스로 진행": 길찾기 없이 오프셋 코스(OffsetCourseProvider)로 같은 선택지의 코스를 만든다(마지막 수단).
 * 경유지가 도로 밖일 수 있어 내비가 거부할 수도 있다 — 그래서 기본이 아니라 버튼이다.
 */
fun CoachController.useFallbackCourse() {
    if (coursePhase != CoursePhase.ERROR && coursePhase != CoursePhase.CHOOSE) return
    if (courseChosen == null) { coursePhase = CoursePhase.CHOOSE; publishCourseUi(); return }
    generateCourse(fallbackCourseSource, isGenerated = false)
}

internal fun CoachController.generateCourse(source: CourseSource, isGenerated: Boolean) {
    val id = courseChosen ?: return
    val state = currentState()
    val naviLoc = navi?.currentLocation?.value
    val lat = state.lat   // 다른 모듈의 data class 프로퍼티라 스마트 캐스트가 안 되므로 지역 변수로
    val lon = state.lon
    val start = when {
        lat != null && lon != null -> LatLon(lat, lon)
        naviLoc != null -> LatLon(naviLoc.lat, naviLoc.lon)
        else -> null
    }
    if (start == null) {
        courseFallbackAvailable = false
        setCourseError("no_location")
        return
    }
    // (11/15 강명수) 길찾기 코스는 난이도·연습 시간·출발지 복귀(순환)를 조건에 넣는다. 오프셋 기본 코스는 예전 조건 그대로.
    // (11/15 오류 검수) 합류 연습은 생성기가 난이도 3 이상에서만 만든다 → 낮게 골랐으면 3으로 올린다.
    //   직진 위주(페달 헷갈림)는 꺾지 않는 편도 코스라 순환으로 바꾸지 않는다(11/14 베타 수정 유지).
    val spec = CourseOptions.spec(id).let { base ->
        if (isGenerated) base.copy(
            difficulty = if (base.includeMerge) maxOf(courseDifficulty, 3) else courseDifficulty,
            targetMinutes = courseTargetMinutes,
            maxDistanceKm = (courseTargetMinutes * 0.75).coerceIn(3.0, 30.0),
            loop = base.fearFocus != Fear.PEDAL_CONFUSION,
        ) else base
    }
    val heading = naviLoc?.headingDeg
    // 이전 생성이 돌고 있으면 버린다(늦게 끝나도 seq 가 달라 무시).
    courseGenJob?.cancel()
    // (11/15 베타 7h: 같은 목적을 다시 고르면 길찾기 응답에 따라 다른 코스가 나와 "가장 가까운 코스"가 무엇인지 알 수 없었다)
    //   같은 자리(300 m 안)에서 같은 방식으로 고른 목적은 지난 결과를 그대로 쓴다
    courseCache[id]?.let { (at, cached, gen) ->
        if (gen == isGenerated && kr.drivecoach.core.course.Geo.distanceM(at, start) < 300.0) {
            ++courseGenSeq
            courseGenerated = isGenerated
            applyCourseResult(id, cached, isGenerated)
            return
        }
    }
    val seq = ++courseGenSeq
    course = null; naviRequest = null; courseSummaryText = ""; courseError = null; courseMismatch = null
    courseGenResult = null; courseGenerated = isGenerated; courseFallbackAvailable = false
    coursePhase = CoursePhase.GENERATING
    publishCourseUi()
    courseGenJob = scope.launch(Dispatchers.Main) {
        val outcome: Result<CourseResult> = try {
            // (11/15 강명수) runInterruptible: 닫기·재선택으로 취소하면 길찾기 스레드도 멈춘다(생성기가 interrupt 를 확인)
            Result.success(runInterruptible(Dispatchers.IO) { source.generate(start, heading, spec) })
        } catch (e: java.util.concurrent.CancellationException) {
            throw e   // 닫기·재선택으로 취소된 것 — 코루틴 규약대로 전파
        } catch (t: Throwable) {
            Result.failure(t)
        }
        if (seq != courseGenSeq || coursePhase != CoursePhase.GENERATING) return@launch
        val result = outcome.getOrNull()
        if (result == null) {
            val t = outcome.exceptionOrNull()
            Log.w(COACH_TAG, "코스 생성 실패: ${t?.message}")
            courseFallbackAvailable = isGenerated
            setCourseError("course_generation: ${t?.javaClass?.simpleName} ${t?.message ?: ""}".trim())
            return@launch
        }
        if (result.best != null) courseCache[id] = Triple(start, result, isGenerated)
        applyCourseResult(id, result, isGenerated)
    }
}

/** (11/14 베타: 고른 목적과 나온 코스가 다르다) 조건과 다른 점을 사람 말로. 다 맞으면 null. */
internal fun mismatchText(spec: CourseSpec, s: CourseSummary): String? {
    // (11/15 베타 7h: "좌회전 4번(고른 조건 3번)" — 숫자를 고른 적이 없는데 '고른 조건', 연습 목적이면 많아도 괜찮은데 안 맞음으로 표시)
    //   괄호는 "이 목적은 …", 좌회전·차로 변경 연습은 모자랄 때만 적는다
    fun range(r: IntRange) = if (r.first == r.last) "${r.first}번" else "${r.first}~${r.last}번"
    val out = ArrayList<String>()
    val leftDrill = spec.fearFocus == Fear.LEFT_TURN
    val laneDrill = spec.fearFocus == Fear.LANE_CHANGE
    if (spec.fearFocus == Fear.PEDAL_CONFUSION && s.leftTurns + s.rightTurns > 2) out += "회전 ${s.leftTurns + s.rightTurns}번(이 목적은 2번 이하)"
    else if (if (leftDrill) s.leftTurns < spec.leftTurns.first else s.leftTurns !in spec.leftTurns) out += "좌회전 ${s.leftTurns}번(이 목적은 ${range(spec.leftTurns)})"
    if (if (laneDrill) s.laneChangeEstimate < spec.laneChanges.first else s.laneChangeEstimate !in spec.laneChanges) out += "차로 변경 ${s.laneChangeEstimate}번(이 목적은 ${range(spec.laneChanges)})"
    if (spec.includeMerge && s.merges + s.highwayEnters == 0) out += "합류 구간 없음"
    if (s.distanceKm > spec.maxDistanceKm) out += "거리 ${"%.1f".format(s.distanceKm)}km(이 목적은 ${"%.0f".format(spec.maxDistanceKm)}km 이하)"   // 같은 화면 안내("…km입니다")와 같이 붙여 쓴다(베타 6f)
    if (spec.loop && !s.returnsToStart) out += "출발지로 돌아오지 않음"
    return out.takeIf { it.isNotEmpty() }?.joinToString(", ")
}

/** 생성 결과 → READY(코스·내비 요청·브리핑 문장·숫자) 또는 ERROR(이유). 메인 스레드. */
internal fun CoachController.applyCourseResult(id: CourseOptionId, result: CourseResult, isGenerated: Boolean) {
    courseGenResult = result
    val best = result.best
    if (best == null) {
        courseFallbackAvailable = isGenerated
        setCourseError(result.failure ?: "no_course")
        return
    }
    course = best.course
    courseMismatch = if (best.meetsSpec) null else mismatchText(CourseOptions.spec(id), best.course.summary)
    // 넘기는 점은 전부 길찾기 경로 path 위의 점(경유지 ≤ 2 + 목적지). 오프셋 코스는 그 직선 경로의 점.
    naviRequest = NaviHandoff.toNaviRequest(best.course, destinationLabel = COURSE_DESTINATION_LABEL)
    courseSummaryText = CourseBriefing.notes(best.course).joinToString(" ") { n -> Phrases.render(PhraseId(n.phraseId), n.params) }
    // (11/15 강명수) 난이도별 생성기의 평가가 있으면 그 숫자를 앞에 붙인다(추정치임을 밝힘).
    //   (11/15 오류 검수) 코스 요약(거리, 좌회전·우회전·차로 변경 수)은 지우지 않고 뒤에 둔다
    best.assessment?.let { a ->
        val loopNote = if (CourseOptions.spec(id).fearFocus == Fear.PEDAL_CONFUSION) "" else " 출발지 근처로 돌아옵니다."
        courseSummaryText = "난이도 추정 ${a.score.toInt()}/100, 예상 ${best.course.route.totalDurationMs / 60000}분.$loopNote " + courseSummaryText
    }
    courseError = null
    courseFallbackAvailable = false
    coursePhase = CoursePhase.READY
    val s = best.course.summary
    Log.i(COACH_TAG, "코스 ${id.name} (${if (isGenerated) "길찾기" else "오프셋"}, 호출 ${result.apiCalls}회, 조건충족 ${best.meetsSpec}): ${best.reasons} / " +
        "${s.distanceKm}km 좌 ${s.leftTurns} 우 ${s.rightTurns} 차선 ${s.laneChangeEstimate} 비보호 ${s.unprotectedLefts} / " +
        "경유지 ${naviRequest?.waypoints?.size ?: 0}개")   // (11/15 보안 점검) 좌표는 logcat 에 남기지 않는다
    publishCourseUi()
}

/** READY → 내비로 보내기. NaviHandoff 가 만든 요청(경유지 ≤ 2 + 목적지)을 NaviPort 에 넘긴다. 결과는 내비 리스너로 온다(ROUTING). */
fun CoachController.sendCourseToNavi() {
    if (coursePhase != CoursePhase.READY && coursePhase != CoursePhase.ERROR) return
    val req = naviRequest ?: run { setCourseError("no_course"); return }
    val port = navi ?: run { setCourseError("navi_unavailable"); return }
    if (!port.naviReady.value) { setCourseError("navi_not_ready"); return }
    tbtGuide.reset()
    courseBriefed = false
    courseRouteStart = null; courseArrival = null; courseResult = null; courseTbt = null
    // 도착 리포트(5-4)는 코스를 보낸 시점부터 센다.
    recorder = newRecorder()
    val ok = port.requestCourse(req.destination, req.destinationLabel, req.waypoints)
    if (!ok) { setCourseError("request_failed"); return }
    coursePhase = CoursePhase.ROUTING
    courseError = null
    publishCourseUi()
    request(Phrases.systemCourseSent.id, force = true)
}

/** ERROR → 다시 시도: 코스가 있으면 READY(내비로 다시 보내기), 선택지는 골랐는데 코스가 없으면 같은 선택지로 다시 생성, 아니면 선택지부터. */
fun CoachController.retryCourse() {
    if (coursePhase != CoursePhase.ERROR) return
    courseError = null
    val chosen = courseChosen
    when {
        naviRequest != null -> { coursePhase = CoursePhase.READY; publishCourseUi() }
        chosen != null -> chooseCourseOption(chosen)
        else -> { coursePhase = CoursePhase.CHOOSE; publishCourseUi() }
    }
}

internal fun CoachController.setCourseError(reason: String) {
    courseError = reason
    coursePhase = CoursePhase.ERROR
    Log.w(COACH_TAG, "코스 오류: $reason")
    publishCourseUi()
}

/** 경로가 시작됐다(onRouteStarted 또는 onRoutingCompleted). DRIVING 으로 가고 5-2 코스 브리핑을 한 번 말한다(정차 중에만 허용되는 BRIEFING). */
internal fun CoachController.onCourseRouteBegan() {
    if (coursePhase != CoursePhase.ROUTING && coursePhase != CoursePhase.READY) { publishCourseUi(); return }
    coursePhase = CoursePhase.DRIVING
    tbtGuide.reset()
    if (!courseBriefed) {
        courseBriefed = true
        course?.let { c ->
            for (n in CourseBriefing.notes(c)) requestQueuedVars(PhraseId(n.phraseId), n.params)
        }
    }
    publishCourseUi()
}

/** 5-4 도착 리포트: 리포트 멘트 ID 들을 직렬 줄로. 화면용 숫자도 같이 만든다. */
internal fun CoachController.speakCourseReport(metrics: Metrics) {
    courseResult = CourseResultUi(
        distanceKm = metrics.distanceKm, durationMin = metrics.durationMs / 60000.0,
        hardBrakeCount = metrics.hardBrakeCount, overspeedCount = metrics.overspeedCount,
        coachSpeakCount = metrics.coachSpeakCount, arrival = courseArrival,
    )
    requestQueued(Phrases.reportIntro.id)
    if (metrics.overspeedCount > 0) requestQueuedVars(Phrases.reportStatSpeeding.id, mapOf("count" to metrics.overspeedCount.toString()))
    if (metrics.hardBrakeCount > 0) requestQueuedVars(Phrases.reportStatHardBrake.id, mapOf("count" to metrics.hardBrakeCount.toString()))
    requestQueuedVars(Phrases.reportCoachCount.id, mapOf("count" to metrics.coachSpeakCount.toString()))
    requestQueued(Phrases.reportEnd.id)
    publishCourseUi()
}

internal fun CoachController.publishCourseUi() {
    val phase = coursePhase ?: run { _courseUi.value = null; return }
    val tbt = courseTbt
    _courseUi.value = CourseUi(
        phase = phase,
        options = CourseOptions.ALL,
        chosen = courseChosen,
        summary = courseSummaryText,
        start = course?.start,
        destination = naviRequest?.destination,
        waypoints = naviRequest?.waypoints.orEmpty(),
        routeDistanceM = courseRouteStart?.distanceM,
        routeDurationS = courseRouteStart?.durationS,
        tbtManeuver = tbt?.maneuver,
        tbtDistanceM = tbt?.distanceM?.toInt(),
        tbtDescription = tbt?.description,
        speedLimit = courseSpeedLimit,
        naviReady = navi?.naviReady?.value ?: false,
        naviDiag = navi?.diag?.value.orEmpty(),
        error = courseError,
        result = courseResult,
        distanceKm = course?.summary?.distanceKm,
        leftTurns = course?.summary?.leftTurns,
        laneChanges = course?.summary?.laneChangeEstimate,
        unprotectedLefts = course?.summary?.unprotectedLefts,
        rightTurns = course?.summary?.rightTurns,
        apiCalls = courseGenResult?.apiCalls ?: 0,
        generated = course != null && courseGenerated,
        fallbackAvailable = phase == CoursePhase.ERROR && courseFallbackAvailable,
        routeDiag = routeDiag?.value.orEmpty(),
        mismatch = courseMismatch,
        routeShape = course?.route?.points?.let { pts -> if (pts.size <= 400) pts else pts.filterIndexed { i, _ -> i % ((pts.size + 399) / 400) == 0 } + pts.last() }.orEmpty(),
        difficultyAvailable = courseSource is kr.drivecoach.core.course.DifficultyCourseGenerator,
        difficulty = courseDifficulty,
        targetMinutes = courseTargetMinutes,
    )
}
