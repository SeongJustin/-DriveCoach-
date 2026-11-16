// 난이도별 코스 생성 — 현재 위치에서 출발해 200 m 안으로 돌아오는 순환 코스를 네이버 Directions 15 로 찾고 난이도(1~5)·연습 시간·집중 연습에 맞는 후보를 고른다.
// 난이도 점수는 초기 휴리스틱(실제 운전자 평가로 보정 전). 키가 있을 때만 쓰고, 키가 없는 제출 APK 는 OffsetCourseProvider(기본 코스)를 쓴다(CoachService).
package kr.drivecoach.core.course

import java.util.concurrent.CancellationException
import kotlin.math.abs
import kotlin.math.max

/** Heuristic v1, not a calibrated safety score. Components are 0..100. */
data class DifficultyAssessment(
    val score: Double,
    val components: Map<String, Double>,
    val trafficCoverage: Double,
    val left: Int, val right: Int, val uturn: Int,
    val unprotected: Int, val roundabouts: Int,
    val merges: Int, val branches: Int, val expresswayEvents: Int,
    val laneChangeBurdenEstimate: Int, val closePairs: Int,
    val repeatedRoadShareEstimate: Double,
    val rejectionReasons: List<String>,
) {
    val eligible: Boolean get() = rejectionReasons.isEmpty()
}

object DifficultyEvaluator {
    // Distance has only 5%; weights total 1.0. Constants require field calibration.
    val weights = linkedMapOf("traffic" to .20, "lane" to .20, "turns" to .20,
        "merge" to .15, "sequence" to .15, "complex" to .05, "distance" to .05)
    private val entry = setOf(50, 52, 57, 59, 66, 68, 75, 76, 77)
    private val exit = setOf(51, 53, 58, 60, 67, 69, 78, 79, 80)
    private val branch = setOf(4, 5, 11, 12, 13, 14, 15, 16, 41, 42, 54, 62, 63, 64, 65, 71, 72, 73, 74)
    private fun isRound(c: Int) = c in 21..34 || c in 91..104
    private fun isAction(c: Int) = c in setOf(2, 3, 6, 8, 81, 82) || c in entry || c in exit || c in branch || isRound(c)
    private fun normalized(value: Double, ceiling: Double) = (value / ceiling * 100).coerceIn(0.0, 100.0)

    fun evaluate(route: Route, spec: CourseSpec, start: LatLon): DifficultyAssessment {
        val rejects = mutableListOf<String>()
        val points = route.points
        val validPoints = points.size >= 2 && points.all { valid(it) }
        val cum = if (validPoints) Geo.cumulativeM(points) else doubleArrayOf()
        val length = cum.lastOrNull() ?: 0.0
        if (!validPoints || length <= 0 || route.totalDistanceM <= 0 || route.totalDurationMs <= 0)
            rejects += "경로 좌표, 거리, 시간이 유효하지 않음"
        if (validPoints && Geo.distanceM(start, points.first()) > 200) rejects += "출발점이 현재 위치에서 200m 이상 떨어짐"
        if (spec.loop && validPoints && Geo.distanceM(start, points.last()) > 200) rejects += "출발지 200m 이내로 복귀하지 않음"
        if (route.totalDistanceM > spec.maxDistanceKm * 1000) rejects += "최대 거리 초과"
        val minutes = route.totalDurationMs / 60000.0
        if (minutes !in spec.targetMinutes * .5..spec.targetMinutes * 1.5) rejects += "연습 시간 허용 범위(±50%) 벗어남"
        if (route.guides.isEmpty()) rejects += "회전 안내 정보 없음"

        // Keep original codes: 'left direction' is NOT a confirmed left turn.
        val guides = route.guides.distinctBy { it.pointIndex to it.rawCode }
        val codes = guides.map { it.rawCode }
        val left = codes.count { it == 2 || it == 8 }
        val right = codes.count { it == 3 }
        val uturn = codes.count { it in setOf(6, 22, 92) }
        val unprotected = codes.count { it == 8 }
        val rounds = codes.count { isRound(it) }
        val merges = codes.count { it == 81 || it == 82 }
        val branches = codes.count { it in branch }
        val express = codes.count { it in entry || it in exit }
        val recognized = setOf(1, 2, 3, 6, 8, 47, 48, 49, 55, 56, 81, 82, 87, 88, 121, 122, 123) + entry + exit + branch
        if (codes.any { it !in recognized && !isRound(it) }) rejects += "해석하지 못한 안내 코드 존재"
        if (codes.any { it == 48 || it == 49 }) rejects += "페리 구간 제외"
        if (guides.any { it.pointIndex !in points.indices }) rejects += "안내 좌표 인덱스 유효하지 않음"

        val actions = guides.filter { isAction(it.rawCode) && it.pointIndex in cum.indices }.sortedBy { it.pointIndex }
        var close = 0
        var opposite = 0
        for ((a, b) in actions.zipWithNext()) {
            val gap = cum[b.pointIndex] - cum[a.pointIndex]
            if (gap in 0.0..200.0) close++
            // Not a count of actual lane changes; opposite turns are only a workload proxy.
            val reverse = (a.rawCode in setOf(2, 8) && b.rawCode == 3) || (a.rawCode == 3 && b.rawCode in setOf(2, 8))
            if (gap in 0.0..500.0 && reverse) opposite++
        }
        val lane = opposite + merges + express

        // Use section geometry intervals; unknown/absent sections are not 'free flowing'.
        // Overlaps count once and the most congested known value wins.
        val edgeCongestion = IntArray(max(0, points.size - 1))
        route.sections.forEach { section ->
            if (section.congestion in 1..3 && section.pointIndex >= 0 && section.pointCount >= 2 &&
                section.pointIndex.toLong() + section.pointCount <= points.size) {
                for (i in section.pointIndex until section.pointIndex + section.pointCount - 1)
                    edgeCongestion[i] = max(edgeCongestion[i], section.congestion)
            }
        }
        var known = 0.0; var trafficWeighted = 0.0; var congested = 0.0
        if (validPoints) for (i in edgeCongestion.indices) {
            val d = cum[i + 1] - cum[i]
            val c = edgeCongestion[i]
            if (c > 0) {
                known += d
                trafficWeighted += d * when(c) { 1 -> 0.0; 2 -> 60.0; else -> 100.0 }
                if (c == 3) congested += d
            }
        }
        val coverage = if (length > 0) (known / length).coerceIn(0.0, 1.0) else 0.0
        val traffic = if (length > 0) (trafficWeighted + (length - known) * 50) / length else 50.0

        // Approximate repeated directed/undirected geometry, no road IDs available.
        val seenEdges = HashSet<String>(); var repeated = 0.0
        if (validPoints) for (i in 0 until points.lastIndex) {
            fun key(p: LatLon) = "${(p.lat * 100000).toLong()},${(p.lon * 100000).toLong()}"
            val edge = listOf(key(points[i]), key(points[i + 1])).sorted().joinToString("|")
            if (!seenEdges.add(edge)) repeated += cum[i + 1] - cum[i]
        }
        val repeatShare = if (length > 0) repeated / length else 0.0
        val components = linkedMapOf(
            "traffic" to traffic,
            "lane" to normalized(lane.toDouble(), 6.0),
            "turns" to normalized(left * 1.5 + right + uturn * 3.0, 24.0),
            "merge" to normalized(merges * 2.0 + express * 2.0 + branches, 8.0),
            "sequence" to normalized(close.toDouble(), 5.0),
            // Base left-turn cost is already in turns, only extra burden here.
            "complex" to normalized(unprotected * 2.0 + rounds, 6.0),
            "distance" to normalized(route.totalDistanceM / 1000.0, 20.0),
        )
        val score = components.entries.sumOf { (k, v) -> weights.getValue(k) * v }
        val level = spec.effectiveDifficulty
        val caps = listOf(25.0, 40.0, 55.0, 70.0, 85.0)
        if (score > caps[level - 1]) rejects += "선택 난이도의 점수 상한 초과"
        if (coverage < if (level <= 2) .5 else .25) rejects += "교통정보 확인 비율 부족"
        if (level <= 2) {
            if (uturn > 0 || unprotected > 0 || rounds > 0 || express > 0 || merges > 0)
                rejects += "초급 제외 동작 포함(유턴, 비보호, 회전교차로, 합류, 전용도로 진출입)"
            if (close > if (level == 1) 0 else 1) rejects += "초급 연속 조작 상한 초과"
            if (length > 0 && congested / length > .15) rejects += "초급 혼잡 구간 비율 초과"
        }
        if (left !in spec.leftTurns) rejects += "좌회전 목표 횟수 불일치"
        if (lane !in spec.laneChanges) rejects += "차로 변경 부담 추정 목표 불일치"
        if (spec.includeMerge && merges + codes.count { it in entry } == 0) rejects += "목표 합류 구간 없음"
        // 직진 위주(페달 헷갈림)는 회전 2번 이하(CourseScorer 와 같은 기준)
        if (spec.fearFocus == Fear.PEDAL_CONFUSION && left + right + uturn > 2) rejects += "직진 위주 목적인데 회전이 2번 넘음"
        if (repeatShare > .5) rejects += "동일 구간 반복 비율 과다"
        return DifficultyAssessment(score, components, coverage, left, right, uturn, unprotected, rounds,
            merges, branches, express, lane, close, repeatShare, rejects)
    }

    fun valid(p: LatLon) = p.lat.isFinite() && p.lon.isFinite() && p.lat in -85.0..85.0 && p.lon in -180.0..180.0
}

/** Uses the existing Naver RouteProvider, so API keys/timeouts/daily budget stay in app/. */
class DifficultyCourseGenerator(
    private val provider: RouteProvider,
    private val maxApiCalls: Int = 8,
    private val isCancelled: () -> Boolean = { Thread.currentThread().isInterrupted },
) : CourseSource {
    init { require(maxApiCalls in 1..30) }

    override fun generate(start: LatLon, headingDeg: Double?, spec: CourseSpec): CourseResult {
        require(DifficultyEvaluator.valid(start)) { "현재 위치 좌표가 유효하지 않습니다" }
        if (spec.effectiveDifficulty <= 2 && spec.includeMerge)
            return CourseResult(null, emptyList(), 0, 0, "합류 연습은 난이도 3 이상을 선택하세요")
        val heading = headingDeg?.takeIf { it.isFinite() } ?: 0.0
        val candidates = candidates(start, heading, spec)
        val scored = mutableListOf<ScoredCourse>()
        val seen = HashSet<List<LatLon>>()
        var calls = 0; var lastFailure: String? = null; var aborted = false
        for (candidate in candidates.take(maxApiCalls)) {
            if (isCancelled()) throw CancellationException("코스 생성 취소")
            calls++
            val route = try {
                provider.route(start, candidate.waypoints, candidate.goal,
                    if (spec.effectiveDifficulty <= 2) "traavoidcaronly" else "tracomfort")
            } catch (e: RouteProviderException) { lastFailure = e.reason; aborted = true; break }
            if (isCancelled()) throw CancellationException("코스 생성 취소")
            if (route == null) { lastFailure = provider.lastFailure ?: "경로 조회 실패"; continue }
            if (!seen.add(route.points)) continue
            val a = DifficultyEvaluator.evaluate(route, spec, start)
            val old = CourseSummarizer.summarize(route, spec.nightOrRain)
            val summary = old.copy(leftTurns = a.left, rightTurns = a.right, uTurns = a.uturn,
                unprotectedLefts = a.unprotected, roundabouts = a.roundabouts,
                laneChangeEstimate = a.laneChangeBurdenEstimate, difficultyScore = 1 + a.score / 25)
            val course = Course(route, start, candidate.waypoints, candidate.goal, summary)
            val minutes = route.totalDurationMs / 60000.0
            // Prefer target STRUCTURAL challenge; never reward congestion for 'advanced'.
            val structural = a.score - a.components.getValue("traffic") * .20
            val target = listOf(5.0, 12.0, 22.0, 32.0, 42.0)[spec.effectiveDifficulty - 1]
            val cost = abs(structural - target) + abs(minutes - spec.targetMinutes) / spec.targetMinutes * 20 +
                a.components.getValue("traffic") * .20 + (1 - a.trafficCoverage) * 15 +
                a.repeatedRoadShareEstimate * 15 + (if (spec.preferRightTurns) a.left * 2.0 else 0.0)
            val info = listOf("난이도 점수 ${"%.1f".format(java.util.Locale.US, a.score)}/100 (추정)",
                "교통정보 확인 ${(a.trafficCoverage * 100).toInt()}%", "차로 변경 부담 추정 ${a.laneChangeBurdenEstimate}",
                "예상 ${minutes.toInt()}분, 출발지 근처 복귀", "내비 재탐색 시 코스가 달라질 수 있음")
            scored += ScoredCourse(course, -cost, a.eligible, info + a.rejectionReasons, a)
        }
        val best = scored.filter { it.meetsSpec }.maxByOrNull { it.score }
        val failure = if (best == null) lastFailure ?: scored.flatMap { it.assessment?.rejectionReasons.orEmpty() }
            .distinct().take(3).joinToString(" / ").ifEmpty { "호출 예산 내 조건에 맞는 코스 없음" } else null
        return CourseResult(best, scored, calls, calls, failure, aborted)
    }

    /** 2 waypoints fit the current Pleos handoff limit. Finish near, not exactly at, start. */
    fun candidates(start: LatLon, heading: Double, spec: CourseSpec): List<Candidate> {
        val targetKm = minOf(spec.maxDistanceKm * .8, spec.targetMinutes / 60.0 * 20.0)
        val base = (targetKm * 1000 / 5.0).coerceIn(300.0, 4000.0)
        val preferred = if (spec.preferRightTurns) 1 else -1
        // 직진 위주는 옆으로 꺾는 경유지 없이 곧장 가는 편도 후보만 만든다(예전 CourseGenerator 와 같음)
        if (spec.fearFocus == Fear.PEDAL_CONFUSION) return (0 until maxApiCalls).map { i ->
            val bearing = heading + listOf(0.0, 90.0, -90.0, 180.0)[(i / 2) % 4]
            val distance = (targetKm * 1000 * listOf(1.0, .65)[i % 2]).coerceIn(500.0, 15000.0)
            Candidate(emptyList(), Geo.offset(start, bearing, distance), 0, distance)
        }
        return (0 until maxApiCalls).map { i ->
            // Cycle direction AND scale early, so small budgets don't exhaust one radius.
            val bearing = heading + (i / 2) * 90.0
            val direction = if (i % 2 == 0) preferred else -preferred
            val factor = listOf(1.0, .65, 1.35)[i % 3]
            val radius = base * factor
            val waypoints = listOf(Geo.offset(start, bearing, radius), Geo.offset(start, bearing + direction * 100, radius))
            val goal = Geo.offset(start, bearing + direction * 160, 100.0)
            Candidate(waypoints, goal, direction, radius)
        }
    }
}

/** Fail explicitly rather than manufacturing road characteristics from coordinate offsets. */
class ApiRequiredCourseSource : CourseSource {
    override fun generate(start: LatLon, headingDeg: Double?, spec: CourseSpec) =
        CourseResult(null, emptyList(), 0, 0, "네이버 Directions API 키가 필요합니다. 키를 설정한 debug 빌드를 사용하세요.")
}
