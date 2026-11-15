// 담당: J (코스 자동 생성)
package kr.drivecoach.core.course

import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * 코스 한 개의 요약. UI 와 출발 전 브리핑(5-2)이 읽는다.
 * @param laneChangeEstimate 길찾기 API 는 차선 변경을 주지 않으므로 CourseSummarizer 의 규칙으로 추정한 값.
 * @param maxCongestion 구간 중 최악 혼잡도(0~3).
 * @param congestedShare 서행·혼잡(2 이상) 구간이 전체 구간 거리에서 차지하는 비율 0..1.
 * @param difficultyScore 1.0~5.0. CourseSpec.difficulty 와 같은 척도.
 */
data class CourseSummary(
    val leftTurns: Int,
    val rightTurns: Int,
    val unprotectedLefts: Int,
    val uTurns: Int,
    val roundabouts: Int,
    val merges: Int,
    val highwayEnters: Int,
    val highwayExits: Int,
    val tollgates: Int,
    val laneChangeEstimate: Int,
    val maxCongestion: Int,
    val congestedShare: Double,
    val distanceKm: Double,
    val durationMin: Double,
    val returnsToStart: Boolean,
    val difficultyScore: Double,
)

/** 생성된 코스. route 는 공급자가 준 경로 원본, waypoints 는 그 경로를 얻은 요청 경유지. */
data class Course(
    val route: Route,
    val start: LatLon,
    val waypoints: List<LatLon>,
    val goal: LatLon,
    val summary: CourseSummary,
)

/** Route → CourseSummary. 차선 변경 추정 규칙이 여기 있다. */
object CourseSummarizer {
    /** "회전 직전 이 거리 안에서 도로가 바뀌었으면 차선 변경 1회"로 보는 창(m). */
    const val LANE_CHANGE_WINDOW_M = 500.0
    /** 도착점이 출발점에서 이 거리 안이면 순환 코스로 본다(m). */
    const val LOOP_CLOSE_M = 300.0

    /**
     * 차선 변경 추정 규칙 (API 가 직접 주지 않으므로 보수적으로 센다):
     *  1. MERGE / HIGHWAY_ENTER / HIGHWAY_EXIT 는 램프 진입·진출 자체가 차선 변경이므로 각 +1.
     *  2. 회전(LEFT/RIGHT/UTURN/UNPROTECTED_LEFT) 직전 500 m 안에서 도로명 구간(section)이 새로 시작했으면 +1.
     *     (새 도로에 들어와 곧바로 회전 차로로 옮겨야 하는 상황)
     *  3. 직전 500 m 안에 반대 방향 회전이 있었으면(우→좌, 좌→우) +1. 규칙 2 와 중복되면 한 번만 센다.
     * 좌표(points)가 없는 경로는 guide.distanceM 누적으로 거리를 잰다.
     */
    fun laneChangeEstimate(route: Route): Int {
        val pos = guidePositionsM(route)
        val roadStarts = sectionStartsM(route)
        var count = 0
        var lastTurnPos = Double.NEGATIVE_INFINITY
        var lastTurnLeft: Boolean? = null
        route.guides.forEachIndexed { i, g ->
            when {
                g.type == ManeuverType.MERGE || g.type == ManeuverType.HIGHWAY_ENTER || g.type == ManeuverType.HIGHWAY_EXIT -> count++
                g.type.isTurn -> {
                    val p = pos[i]
                    val roadChanged = roadStarts.any { it > p - LANE_CHANGE_WINDOW_M && it < p && it > 0.0 }
                    val isLeft = g.type.isLeftish
                    val opposite = lastTurnLeft != null && lastTurnLeft != isLeft && p - lastTurnPos <= LANE_CHANGE_WINDOW_M
                    if (roadChanged || opposite) count++
                    if (g.type != ManeuverType.UTURN) { lastTurnPos = p; lastTurnLeft = isLeft } else { lastTurnPos = p; lastTurnLeft = null }
                }
            }
        }
        return count
    }

    /** 각 guide 의 출발점 기준 경로상 위치(m). */
    fun guidePositionsM(route: Route): DoubleArray {
        val out = DoubleArray(route.guides.size)
        val cum = if (route.points.size >= 2) Geo.cumulativeM(route.points) else null
        var acc = 0.0
        route.guides.forEachIndexed { i, g ->
            acc += g.distanceM
            out[i] = if (cum != null && g.pointIndex in cum.indices) cum[g.pointIndex] else acc
        }
        return out
    }

    /** 각 section 이 시작하는 경로상 위치(m). pointIndex 가 없으면 distanceM 누적으로 대체. */
    fun sectionStartsM(route: Route): List<Double> {
        val cum = if (route.points.size >= 2) Geo.cumulativeM(route.points) else null
        val out = ArrayList<Double>()
        var acc = 0.0
        for (s in route.sections) {
            out += if (cum != null && s.pointIndex in cum.indices) cum[s.pointIndex] else acc
            acc += s.distanceM
        }
        return out
    }

    /** Route 를 요약한다. nightOrRain 은 난이도 점수에만 +1 반영. */
    fun summarize(route: Route, nightOrRain: Boolean = false): CourseSummary {
        val g = route.guides
        val left = g.count { it.type == ManeuverType.LEFT }
        val unprotected = g.count { it.type == ManeuverType.UNPROTECTED_LEFT }
        val right = g.count { it.type == ManeuverType.RIGHT }
        val uturn = g.count { it.type == ManeuverType.UTURN }
        val round = g.count { it.type == ManeuverType.ROUNDABOUT }
        val merges = g.count { it.type == ManeuverType.MERGE }
        val hwIn = g.count { it.type == ManeuverType.HIGHWAY_ENTER }
        val hwOut = g.count { it.type == ManeuverType.HIGHWAY_EXIT }
        val toll = g.count { it.type == ManeuverType.TOLLGATE }
        val lane = laneChangeEstimate(route)
        val maxCong = route.sections.maxOfOrNull { it.congestion } ?: 0
        val secTotal = route.sections.sumOf { it.distanceM }.toDouble()
        val congShare = if (secTotal <= 0) 0.0 else route.sections.filter { it.congestion >= 2 }.sumOf { it.distanceM } / secTotal
        val distKm = route.totalDistanceM / 1000.0
        val s = route.start; val e = route.end
        val loop = s != null && e != null && Geo.distanceM(s, e) <= LOOP_CLOSE_M
        val difficulty = difficultyScore(
            leftTurns = left + unprotected, unprotectedLefts = unprotected, merges = merges + hwIn,
            laneChanges = lane, maxCongestion = maxCong, roundabouts = round, distanceKm = distKm, nightOrRain = nightOrRain,
        )
        return CourseSummary(
            leftTurns = left + unprotected, rightTurns = right, unprotectedLefts = unprotected, uTurns = uturn,
            roundabouts = round, merges = merges, highwayEnters = hwIn, highwayExits = hwOut, tollgates = toll,
            laneChangeEstimate = lane, maxCongestion = maxCong, congestedShare = congShare,
            distanceKm = distKm, durationMin = route.totalDurationMs / 60000.0,
            returnsToStart = loop, difficultyScore = difficulty,
        )
    }

    /**
     * 코스 난이도 1.0~5.0.
     * 1 + 좌회전 0.3 + 비보호 0.5 + 합류·고속진입 1.0 + 차선변경 0.4 + 회전교차로 0.4 + (최악혼잡-1) 0.5 + 거리 5km 당 0.2 + 야간우천 1.
     */
    fun difficultyScore(
        leftTurns: Int, unprotectedLefts: Int, merges: Int, laneChanges: Int,
        maxCongestion: Int, roundabouts: Int, distanceKm: Double, nightOrRain: Boolean,
    ): Double {
        val raw = 1.0 + 0.3 * leftTurns + 0.5 * unprotectedLefts + 1.0 * merges + 0.4 * laneChanges +
            0.4 * roundabouts + 0.5 * (maxCongestion - 1).coerceAtLeast(0) + 0.2 * (distanceKm / 5.0) +
            (if (nightOrRain) 1.0 else 0.0)
        return ((raw.coerceIn(1.0, 5.0)) * 10).roundToInt() / 10.0
    }
}

/** 점수화 결과. score 가 클수록 조건에 잘 맞는다. meetsSpec 은 필수 조건을 전부 만족했는지. */
data class ScoredCourse(val course: Course, val score: Double, val meetsSpec: Boolean, val reasons: List<String>, val assessment: DifficultyAssessment? = null)

/**
 * CourseSpec 에 대한 코스 점수. 벌점·가점의 합(0 이 "조건 완전 일치").
 * 가중치는 상수로 노출해 튜닝할 수 있게 둔다.
 */
object CourseScorer {
    const val W_LEFT_MISS = 10.0        // 좌회전 수가 범위에서 1 벗어날 때마다
    const val W_LANE_MISS = 5.0         // 차선 변경 추정치가 범위에서 1 벗어날 때마다
    const val W_MERGE_MISSING = 30.0    // 합류 원했는데 없음
    const val W_MERGE_UNWANTED = 10.0   // 합류 원치 않았는데 있음(개당)
    const val W_CONGESTION = 25.0       // 허용 혼잡도 초과 단계당
    const val W_CONG_SHARE = 20.0       // 서행·혼잡 구간 비율(0..1) 곱
    const val W_DIST_OVER = 15.0        // 최대 거리 초과 km 당
    const val W_TOO_SHORT = 10.0        // 1 km 미만
    const val W_LOOP_MISS = 50.0        // 순환 원했는데 출발지로 안 돌아옴
    const val W_UNPROTECTED_LOW = 8.0   // 난이도 1~2 에서 비보호 좌회전 개당
    const val W_ROUNDABOUT_LOW = 5.0    // 난이도 1~2 에서 회전교차로 개당
    const val W_HIGHWAY_LOW = 15.0      // 난이도 1~3 에서 고속도로·톨게이트 개당(합류 미요청 시)
    const val W_DIFF_GAP = 6.0          // 코스 난이도 점수와 요청 난이도 차이당
    const val W_RIGHT_BONUS = 3.0       // 우회전 위주일 때 우회전 개당 가점
    const val W_RIGHT_LEFT_PENALTY = 5.0 // 우회전 위주일 때 좌회전 개당 감점
    const val W_FEAR_BONUS = 4.0        // 무서운 것에 해당하는 연습 요소 개당 가점(최대 5개)

    /** 코스를 spec 에 대해 채점한다. */
    fun score(course: Course, spec: CourseSpec): ScoredCourse {
        val s = course.summary
        var score = 0.0
        val reasons = ArrayList<String>()
        var meets = true
        fun penalty(v: Double, why: String) { if (v > 0) { score -= v; reasons += "-${fmt(v)} $why" } }
        fun bonus(v: Double, why: String) { if (v > 0) { score += v; reasons += "+${fmt(v)} $why" } }

        val leftMiss = rangeMiss(s.leftTurns, spec.leftTurns)
        if (leftMiss > 0) { meets = false; penalty(W_LEFT_MISS * leftMiss, "좌회전 ${s.leftTurns}회 (원함 ${spec.leftTurns})") }
        val laneMiss = rangeMiss(s.laneChangeEstimate, spec.laneChanges)
        if (laneMiss > 0) { meets = false; penalty(W_LANE_MISS * laneMiss, "차선변경 추정 ${s.laneChangeEstimate}회 (원함 ${spec.laneChanges})") }

        val mergeLike = s.merges + s.highwayEnters
        if (spec.includeMerge) {
            if (mergeLike == 0) { meets = false; penalty(W_MERGE_MISSING, "합류 구간 없음") } else bonus(W_FEAR_BONUS, "합류 구간 포함")
        } else if (mergeLike > 0 && spec.effectiveDifficulty <= 3) {
            penalty(W_MERGE_UNWANTED * mergeLike, "원치 않는 합류 ${mergeLike}회")
        }

        val congOver = s.maxCongestion - spec.allowedCongestion
        if (congOver > 0) { meets = false; penalty(W_CONGESTION * congOver, "혼잡도 ${s.maxCongestion} (허용 ${spec.allowedCongestion})") }
        penalty(W_CONG_SHARE * s.congestedShare, "서행·혼잡 구간 비율 ${(s.congestedShare * 100).roundToInt()}%")

        val over = s.distanceKm - spec.maxDistanceKm
        if (over > 0) { meets = false; penalty(W_DIST_OVER * over, "거리 ${fmt(s.distanceKm)}km > ${fmt(spec.maxDistanceKm)}km") }
        if (s.distanceKm < 1.0) penalty(W_TOO_SHORT, "너무 짧음")

        if (spec.loop && !s.returnsToStart) { meets = false; penalty(W_LOOP_MISS, "출발지로 복귀하지 않음") }

        val d = spec.effectiveDifficulty
        if (d <= 2) {
            penalty(W_UNPROTECTED_LOW * s.unprotectedLefts, "비보호 좌회전 ${s.unprotectedLefts}회(저난이도)")
            penalty(W_ROUNDABOUT_LOW * s.roundabouts, "회전교차로 ${s.roundabouts}회(저난이도)")
        }
        if (d <= 3 && !spec.includeMerge) {
            val hw = s.highwayEnters + s.highwayExits + s.tollgates
            penalty(W_HIGHWAY_LOW * hw, "고속도로·톨게이트 ${hw}회(난이도 ${d})")
        }
        penalty(W_DIFF_GAP * abs(s.difficultyScore - spec.difficulty), "코스 난이도 ${s.difficultyScore} vs 요청 ${spec.difficulty}")

        if (spec.preferRightTurns) {
            bonus(W_RIGHT_BONUS * s.rightTurns.coerceAtMost(6), "우회전 ${s.rightTurns}회")
            penalty(W_RIGHT_LEFT_PENALTY * s.leftTurns, "우회전 위주인데 좌회전 ${s.leftTurns}회")
        }
        if (spec.fearFocus == Fear.PEDAL_CONFUSION) {
            val turns = s.leftTurns + s.rightTurns
            if (turns > 2) meets = false
            penalty(W_RIGHT_LEFT_PENALTY * turns, "직진 위주인데 회전 ${turns}회")
        }
        when (spec.fearFocus) {
            Fear.LEFT_TURN -> bonus(W_FEAR_BONUS * s.leftTurns.coerceAtMost(5), "좌회전 연습 요소")
            Fear.LANE_CHANGE -> bonus(W_FEAR_BONUS * s.laneChangeEstimate.coerceAtMost(5), "차선변경 연습 요소")
            Fear.MERGE -> if (!spec.includeMerge) bonus(W_FEAR_BONUS * mergeLike.coerceAtMost(5), "합류 연습 요소")
            else -> {}
        }
        return ScoredCourse(course, score, meets, reasons)
    }

    private fun rangeMiss(v: Int, r: IntRange): Int = when { v < r.first -> r.first - v; v > r.last -> v - r.last; else -> 0 }
    private fun fmt(d: Double): String = if (d == d.roundToInt().toDouble()) d.roundToInt().toString() else ((d * 10).roundToInt() / 10.0).toString()
}

/**
 * 후보 생성 설정.
 * @param maxApiCalls 한 번의 generate 에서 RouteProvider 를 부르는 최대 횟수(비용 예산).
 * @param bearings 출발지 기준 첫 경유지 방위각 후보. 앞에 있을수록 먼저 시도.
 * @param radiusFactors 기본 반경에 곱할 배율 후보.
 * @param loopGoalOffsetM 순환 코스에서 도착지를 출발지에서 떨어뜨리는 거리(네이버는 출발=도착이면 오류 1).
 * @param stopAtFirstMatch 필수 조건을 전부 만족하는 후보가 나오면 더 호출하지 않고 끝낸다(예산 절약).
 */
data class GeneratorConfig(
    val maxApiCalls: Int = 12,
    val bearings: List<Double> = listOf(0.0, 90.0, 180.0, 270.0, 45.0, 135.0, 225.0, 315.0),
    val radiusFactors: List<Double> = listOf(1.0, 0.6, 1.4),
    val loopGoalOffsetM: Double = 120.0,
    val stopAtFirstMatch: Boolean = true,
    val option: String = "traoptimal",
)

/** 후보 경유지 조합. orientation: +1 시계방향(우회전 위주), -1 반시계방향(좌회전 위주), 0 편도. */
data class Candidate(val waypoints: List<LatLon>, val goal: LatLon, val orientation: Int, val radiusM: Double)

/**
 * generate 결과. best 가 null 이면 예산 안에서 경로를 하나도 못 받은 것.
 * @param failure best 가 null 일 때의 이유. 공급자가 [RouteProviderException] 으로 중단시켰으면 그 reason(일일 호출 상한·인증 실패),
 *                아니면 마지막 호출의 [RouteProvider.lastFailure](HTTP 코드·응답 code·네트워크). 화면 ERROR 의 "이유" 로 쓴다.
 * @param aborted 공급자가 중단시켜 남은 후보를 시도하지 못했는지.
 */
data class CourseResult(
    val best: ScoredCourse?,
    val scored: List<ScoredCourse>,
    val apiCalls: Int,
    val candidatesTried: Int,
    val failure: String? = null,
    val aborted: Boolean = false,
)

/**
 * 조건에 맞는 연습 코스를 자동 생성한다.
 * 흐름: 출발지 주변 방위각·반경 샘플링으로 경유지 조합 생성 → RouteProvider 호출(예산 내) → CourseScorer 채점 → 최고 점수 1개.
 *
 * 순환 코스는 경유지 3개를 정삼각형 꼭짓점에 둔다. 우측통행에서 반시계 방향으로 돌면 모서리가 좌회전, 시계 방향이면 우회전이 되므로
 * 좌회전을 많이 원하면 반시계 후보를, 우회전 위주면 시계 후보를 먼저 시도한다.
 */
class CourseGenerator(private val provider: RouteProvider, private val config: GeneratorConfig = GeneratorConfig()) {

    /** 출발지 start 와 조건 spec 으로 코스를 만든다. */
    fun generate(start: LatLon, spec: CourseSpec, bearingOffsetDeg: Double = 0.0): CourseResult {
        val candidates = candidates(start, spec, bearingOffsetDeg)
        val scored = ArrayList<ScoredCourse>()
        var calls = 0
        var tried = 0
        var lastFailure: String? = null
        var aborted = false
        for (c in candidates) {
            if (calls >= config.maxApiCalls) break
            tried++
            calls++
            val route = try {
                provider.route(start, c.waypoints, c.goal, config.option)
            } catch (e: RouteProviderException) {
                // 일일 상한·인증 실패: 더 불러도 소용없다. 지금까지 받은 후보 중에서만 고른다.
                lastFailure = e.reason
                aborted = true
                break
            }
            if (route == null) { lastFailure = provider.lastFailure ?: lastFailure; continue }
            val summary = CourseSummarizer.summarize(route, spec.nightOrRain)
            val sc = CourseScorer.score(Course(route, start, c.waypoints, c.goal, summary), spec)
            scored += sc
            if (config.stopAtFirstMatch && sc.meetsSpec) break
        }
        val best = scored.maxWithOrNull(compareBy<ScoredCourse> { it.meetsSpec }.thenBy { it.score })
        val failure = if (best == null) (lastFailure ?: if (calls == 0) "후보 없음" else "경로 없음") else null
        return CourseResult(best, scored, calls, tried, failure, aborted)
    }

    /** 기본 반경(m). 순환: 삼각형 둘레 ≈ 5.5r, 도로 우회 1.3배 → 목표 길이(최대의 70%)에 맞춤. 편도: 직선 2r × 1.3. */
    fun baseRadiusM(spec: CourseSpec): Double {
        val targetM = spec.maxDistanceKm * 1000 * 0.7
        return if (spec.loop) targetM / 7.0 else targetM / 2.6
    }

    /** 시도 순서대로 정렬된 후보 목록(예산보다 많을 수 있다). */
    fun candidates(start: LatLon, spec: CourseSpec, bearingOffsetDeg: Double = 0.0): List<Candidate> {
        val r0 = baseRadiusM(spec)
        val out = ArrayList<Candidate>()
        val wantsLeft = !spec.preferRightTurns && (spec.leftTurns.first >= 2 || spec.fearFocus == Fear.LEFT_TURN)
        val orientations = when {
            spec.preferRightTurns -> listOf(1, -1)
            wantsLeft -> listOf(-1, 1)
            else -> listOf(-1, 1)
        }
        for (f in config.radiusFactors) {
            val r = r0 * f
            for (b0 in config.bearings) {
                val b = b0 + bearingOffsetDeg
                if (spec.loop) {
                    for (o in orientations) {
                        val wps = (0 until 3).map { k -> Geo.offset(start, b + o * 120.0 * k, r) }
                        val goal = Geo.offset(start, Geo.bearingDeg(start, wps.last()), config.loopGoalOffsetM)
                        out += Candidate(wps, goal, o, r)
                    }
                } else {
                    val goal = Geo.offset(start, b, 2 * r)
                    // (11/14 베타: "직진만"인데 회전 6번) 직진 위주는 옆으로 꺾는 경유지를 두지 않고 곧장 목적지로
                    if (spec.fearFocus == Fear.PEDAL_CONFUSION) { out += Candidate(emptyList(), goal, 0, r); continue }
                    val side = Geo.offset(start, b + (if (wantsLeft) -35.0 else 35.0), r)
                    out += Candidate(listOf(side), goal, 0, r)
                }
            }
        }
        // 순환이면서 방향 선호가 있을 때: 선호 방향 후보를 전부 앞으로(안정 정렬).
        return if (spec.loop && (spec.preferRightTurns || wantsLeft)) out.sortedBy { if (it.orientation == orientations.first()) 0 else 1 } else out
    }
}
