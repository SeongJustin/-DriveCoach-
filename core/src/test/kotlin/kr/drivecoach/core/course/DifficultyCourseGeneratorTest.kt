package kr.drivecoach.core.course

import java.util.concurrent.CancellationException
import kotlin.test.*

private val origin = LatLon(37.56, 126.99) // synthetic test origin, not the user's location
private fun fixture(codes: List<Int> = listOf(3, 2), traffic: Int = 1, minutes: Int = 20): Route {
    val pts = listOf(origin, Geo.offset(origin, 0.0, 1000.0), Geo.offset(origin, 45.0, 1400.0),
        Geo.offset(origin, 90.0, 1000.0), Geo.offset(origin, 120.0, 600.0), Geo.offset(origin, 180.0, 100.0))
    val guides = codes.mapIndexed { i, code ->
        val idx = (i + 1).coerceAtMost(pts.lastIndex)
        Guide(NaverDirectionsMapper.maneuver(code), pts[idx].lat, pts[idx].lon, 500, idx, code)
    } + Guide(ManeuverType.DESTINATION, pts.last().lat, pts.last().lon, 500, pts.lastIndex, 88)
    return Route(pts, guides, listOf(Section(traffic, 30.0, 4500, "synthetic", 0, pts.size)), 4500, minutes * 60000L)
}
private val spec = CourseSpec(difficulty = 3, targetMinutes = 20, maxDistanceKm = 15.0)

fun main() {
    var count = 0
    fun test(name: String, block: () -> Unit) { block(); count++; println("PASS $name") }
    test("weights total one, distance only five percent") {
        assertEquals(1.0, DifficultyEvaluator.weights.values.sum(), 1e-9)
        assertEquals(.05, DifficultyEvaluator.weights.getValue("distance"))
    }
    test("left direction is not a confirmed left turn") {
        val a = DifficultyEvaluator.evaluate(fixture(listOf(4, 2, 3)), spec, origin)
        assertEquals(1, a.left); assertEquals(1, a.right); assertEquals(1, a.branches)
    }
    test("unprotected turns and roundabout U-turns are distinct") {
        val a = DifficultyEvaluator.evaluate(fixture(listOf(8, 92)), spec.copy(difficulty = 5), origin)
        assertEquals(1, a.left); assertEquals(1, a.unprotected); assertEquals(1, a.uturn); assertEquals(1, a.roundabouts)
    }
    test("unknown traffic is not free-flowing") {
        val a = DifficultyEvaluator.evaluate(fixture(traffic = 0), spec, origin)
        assertEquals(0.0, a.trafficCoverage); assertEquals(50.0, a.components["traffic"])
        assertFalse(a.eligible)
    }
    test("overlapping traffic sections count once, worst congestion wins") {
        val r = fixture(); val repeated = r.copy(sections = r.sections + r.sections + r.sections.map { it.copy(congestion = 3) })
        val a = DifficultyEvaluator.evaluate(repeated, spec, origin)
        assertEquals(1.0, a.trafficCoverage, 1e-9); assertEquals(100.0, a.components.getValue("traffic"), 1e-9)
    }
    test("partial and invalid traffic sections do not inflate coverage") {
        val r = fixture().copy(sections = listOf(Section(1, 30.0, 999999, pointIndex = 0, pointCount = 2),
            Section(1, 30.0, 999999, pointIndex = -1, pointCount = 1000)))
        val a = DifficultyEvaluator.evaluate(r, spec, origin)
        assertTrue(a.trafficCoverage in .01.. .49)
    }
    test("beginner excludes U-turn even at low total score") {
        assertFalse(DifficultyEvaluator.evaluate(fixture(listOf(6)), spec.copy(difficulty = 1), origin).eligible)
    }
    test("beginner excludes merge") {
        assertFalse(DifficultyEvaluator.evaluate(fixture(listOf(81)), spec.copy(difficulty = 2), origin).eligible)
    }
    test("time outside tolerance rejected") {
        assertFalse(DifficultyEvaluator.evaluate(fixture(minutes = 45), spec, origin).eligible)
    }
    test("distance hard cap") {
        assertFalse(DifficultyEvaluator.evaluate(fixture(), spec.copy(maxDistanceKm = 2.0), origin).eligible)
    }
    test("requested turn counts are hard constraints") {
        assertFalse(DifficultyEvaluator.evaluate(fixture(), spec.copy(leftTurns = 3..3), origin).eligible)
    }
    test("near-start loop endpoint verified") {
        val r = fixture(); val pts = r.points.dropLast(1) + Geo.offset(origin, 0.0, 1000.0)
        assertFalse(DifficultyEvaluator.evaluate(r.copy(points = pts), spec, origin).eligible)
    }
    test("close successive maneuvers use geometry rather than guide count") {
        val r = fixture(); val pts = r.points.toMutableList(); pts[2] = Geo.offset(pts[1], 90.0, 100.0)
        val a = DifficultyEvaluator.evaluate(r.copy(points = pts), spec, origin)
        assertEquals(1, a.closePairs); assertEquals(1, a.laneChangeBurdenEstimate)
    }
    test("duplicate guide entries are not counted twice") {
        val r = fixture(); val a = DifficultyEvaluator.evaluate(r.copy(guides = r.guides + r.guides), spec, origin)
        assertEquals(1, a.left); assertEquals(1, a.right)
    }
    test("missing or unknown guide codes rejected") {
        assertFalse(DifficultyEvaluator.evaluate(fixture().copy(guides = emptyList()), spec, origin).eligible)
        assertFalse(DifficultyEvaluator.evaluate(fixture(listOf(999)), spec, origin).eligible)
    }
    test("invalid geometry rejected") {
        assertFalse(DifficultyEvaluator.evaluate(fixture().copy(points = emptyList()), spec, origin).eligible)
        assertFalse(DifficultyEvaluator.evaluate(fixture().copy(points = listOf(origin, LatLon(Double.NaN, 0.0))), spec, origin).eligible)
    }
    test("candidate budget, two waypoints and near-start destination") {
        val p = FakeRouteProvider { null }; val g = DifficultyCourseGenerator(p, 8)
        val c = g.candidates(origin, 0.0, spec)
        assertEquals(8, c.size); assertTrue(c.all { it.waypoints.size == 2 && Geo.distanceM(origin, it.goal) in 99.0..101.0 })
        val r = g.generate(origin, null, spec)
        assertEquals(8, p.callCount); assertEquals(8, r.apiCalls); assertNull(r.best)
    }
    test("auth/quota errors stop remaining calls") {
        val p = FakeRouteProvider { throw RouteProviderException("HTTP 401") }
        val r = DifficultyCourseGenerator(p).generate(origin, null, spec)
        assertEquals(1, p.callCount); assertTrue(r.aborted); assertNull(r.best); assertEquals("HTTP 401", r.failure)
    }
    test("no invalid candidate silently recommended") {
        val p = FakeRouteProvider { fixture(listOf(6)) }
        val r = DifficultyCourseGenerator(p, 2).generate(origin, null, spec.copy(difficulty = 1))
        assertNull(r.best); assertEquals(1, r.scored.size); assertNotNull(r.failure)
    }
    test("matching route selected and assessment returned") {
        val p = FakeRouteProvider { fixture() }
        val r = DifficultyCourseGenerator(p, 2).generate(origin, null, spec)
        val best = assertNotNull(r.best); assertTrue(best.meetsSpec); assertNotNull(best.assessment)
    }
    test("congestion not rewarded for advanced routes") {
        var index = 0
        val p = FakeRouteProvider {
            val r = fixture(traffic = if (index++ == 0) 1 else 3)
            // Different geometry to avoid deliberate candidate deduplication.
            if (index == 1) r else r.copy(points = r.points.mapIndexed { i, pt -> if (i == 2) Geo.offset(pt, 0.0, 1.0) else pt })
        }
        val r = DifficultyCourseGenerator(p, 2).generate(origin, null, spec.copy(difficulty = 5))
        assertEquals(0.0, r.best!!.assessment!!.components.getValue("traffic"), 1e-9)
    }
    test("cancel before request consumes no budget") {
        val p = FakeRouteProvider { fixture() }
        assertFailsWith<CancellationException> { DifficultyCourseGenerator(p, isCancelled = { true }).generate(origin, null, spec) }
        assertEquals(0, p.callCount)
    }
    test("conflicting beginner merge intent rejected before API call") {
        val p = FakeRouteProvider { fixture() }
        assertNull(DifficultyCourseGenerator(p).generate(origin, null, spec.copy(difficulty = 1, includeMerge = true)).best)
        assertEquals(0, p.callCount)
    }
    test("API key missing does not invent a route") {
        assertNull(ApiRequiredCourseSource().generate(origin, null, spec).best)
    }
    test("invalid request and call budget rejected") {
        assertFailsWith<IllegalArgumentException> { spec.copy(targetMinutes = 0) }
        assertFailsWith<IllegalArgumentException> { spec.copy(maxDistanceKm = Double.NaN) }
        assertFailsWith<IllegalArgumentException> { DifficultyCourseGenerator(FakeRouteProvider { null }, 0) }
        assertFailsWith<IllegalArgumentException> { DifficultyCourseGenerator(FakeRouteProvider { null }).generate(LatLon(100.0, 0.0), null, spec) }
    }
    // (11/15 오류 검수) 직진 위주(페달 헷갈림): 편도 후보, 회전 2번 넘으면 탈락
    test("pedal confusion candidates go straight without side waypoints") {
        val pedal = spec.copy(fearFocus = Fear.PEDAL_CONFUSION, difficulty = 1, loop = false)
        val c = DifficultyCourseGenerator(FakeRouteProvider { null }).candidates(origin, 0.0, pedal)
        assertTrue(c.isNotEmpty()); assertTrue(c.all { it.waypoints.isEmpty() })
    }
    test("pedal confusion rejects more than two turns") {
        val pedal = spec.copy(fearFocus = Fear.PEDAL_CONFUSION, loop = false)
        val a = DifficultyEvaluator.evaluate(fixture(listOf(3, 3, 3)), pedal, origin)
        assertTrue(a.rejectionReasons.any { it.contains("직진 위주") })
        val ok = DifficultyEvaluator.evaluate(fixture(listOf(3)), pedal, origin)
        assertFalse(ok.rejectionReasons.any { it.contains("직진 위주") })
    }
    println("DifficultyCourseGenerator: $count scenarios passed")
}
