// 담당: S (앱 연결 — 오프셋 코스 소스 + TBT 안내 규칙 테스트)
package kr.drivecoach.core.course

import kr.drivecoach.core.tts.Phrases
import kotlin.math.abs
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** 에뮬레이터 기본 위치 근처(판교). */
private val START = LatLon(37.4, 127.1)

private fun testStraightShapeGeometry() {
    val p = OffsetCourseProvider()
    val pts = p.points(START, headingDeg = 0.0, shape = OffsetShape.STRAIGHT)
    assertEquals(3, pts.size)
    // 북쪽으로 0.5 / 1.0 / 1.5 km: 위도만 늘고 경도는 그대로
    assertTrue(abs(pts[2].lat - (START.lat + 1.5 * 0.009)) < 1e-9)
    assertTrue(abs(pts[2].lon - START.lon) < 1e-9)
    val d = Geo.distanceM(START, pts[2])
    assertTrue(d > 1400 && d < 1600, "목적지 ≈ 1.5 km, 실제 $d")
    val gap = Geo.distanceM(pts[0], pts[1])
    assertTrue(gap > 450 && gap < 560, "경유지 간격 ≈ 0.5 km, 실제 $gap")
}

private fun testRightShapeIsToTheRightOfHeading() {
    val p = OffsetCourseProvider()
    // 북쪽을 보고 있으면 오른쪽 = 동쪽(경도 증가)
    val north = p.points(START, 0.0, OffsetShape.RIGHT)
    assertTrue(north[1].lon > north[0].lon, "두 번째 경유지는 첫 경유지의 동쪽")
    assertTrue(abs(north[0].lon - START.lon) < 1e-9, "첫 경유지는 정면(북)")
    val dest = Geo.distanceM(START, north[2])
    assertTrue(dest > 1350 && dest < 1650, "목적지 ≈ 1.5 km, 실제 $dest")
    val gap = Geo.distanceM(north[0], north[1])
    assertTrue(gap > 600 && gap < 800, "경유지 간격 ≈ 0.7 km, 실제 $gap")
    // 동쪽(90°)을 보고 있으면 오른쪽 = 남쪽(위도 감소)
    val east = p.points(START, 90.0, OffsetShape.RIGHT)
    assertTrue(east[1].lat < east[0].lat, "동향일 때 오른쪽은 남쪽")
    assertTrue(east[0].lon > START.lon, "동향일 때 정면은 동쪽")
    // 왼쪽은 대칭
    val left = p.points(START, 0.0, OffsetShape.LEFT)
    assertTrue(left[1].lon < left[0].lon)
}

private fun testCourseSummaryAndHandoff() {
    val p = OffsetCourseProvider()
    val spec = CourseOptions.spec(CourseOptionId.NEIGHBORHOOD_RIGHT)
    val result = p.generate(START, headingDeg = null, spec = spec)
    val best = assertNotNull(result.best)
    val s = best.course.summary
    assertEquals(2, s.rightTurns, "우회전 위주: 모서리 2개가 우회전")
    assertEquals(0, s.leftTurns)
    assertTrue(s.distanceKm > 1.5 && s.distanceKm < 3.0, "코스 길이 ${s.distanceKm}")
    assertEquals(0, result.apiCalls)
    // 같은 NaviHandoff 경로: 경유지 ≤ 2, 목적지는 코스 goal(편도)
    val req = NaviHandoff.toNaviRequest(best.course)
    assertEquals(2, req.waypoints.size)
    assertTrue(req.waypoints.size <= NaviHandoff.DEFAULT_MAX_WAYPOINTS)
    assertEquals(best.course.goal, req.destination)
    // 브리핑 멘트 ID 는 카탈로그에 있어야 한다
    val notes = CourseBriefing.notes(best.course)
    assertTrue(notes.isNotEmpty())
    for (n in notes) assertNotNull(Phrases.find(kr.drivecoach.core.tts.PhraseId(n.phraseId)), "카탈로그에 없음: ${n.phraseId}")
    val left = p.generate(START, 0.0, CourseOptions.spec(CourseOptionId.LEFT_TURN_DRILL)).best!!.course.summary
    assertEquals(2, left.leftTurns)
    val straight = p.generate(START, 0.0, CourseOptions.spec(CourseOptionId.STRAIGHT_ONLY)).best!!.course.summary
    assertEquals(0, straight.leftTurns + straight.rightTurns)
}

private fun testOptionShapesMatchSpecRule() {
    for (id in CourseOptions.ALL) {
        assertEquals(CourseOptions.shape(id), OffsetCourseProvider.shapeFromSpec(CourseOptions.spec(id)), "선택지 $id 의 모양 규칙 불일치")
    }
    assertEquals(Fear.LEFT_TURN, CourseOptions.fear(CourseOptionId.LEFT_TURN_DRILL))
    assertNull(CourseOptions.fear(CourseOptionId.EXAM_RETAKE))
}

private fun testGeneratedCourseSourceWrapsGenerator() {
    val provider = FakeRouteProvider { req ->
        val b = RouteBuilder(req.start)
        req.waypoints.forEach { b.leg(it, ManeuverType.RIGHT) }
        b.leg(req.goal, ManeuverType.DESTINATION, 88)
        b.build()
    }
    val source: CourseSource = GeneratedCourseSource(CourseGenerator(provider, GeneratorConfig(maxApiCalls = 3)))
    val result = source.generate(START, 45.0, CourseSpec.neighborhoodLoop())
    assertNotNull(result.best)
    assertTrue(result.apiCalls >= 1)
}

private fun testTbtGuideSpeaksTwicePerTurn() {
    val g = TbtGuide()
    val key = "RIGHT|판교역로"
    assertNull(g.onTbt(ManeuverType.RIGHT, 900.0, key), "멀면 아직 말하지 않음")
    val far = assertNotNull(g.onTbt(ManeuverType.RIGHT, 318.0, key))
    assertEquals(TbtGuide.PHRASE_RIGHT_FAR, far.phraseId)
    assertEquals("300", far.params["distance"])
    assertNotNull(Phrases.find(far.phraseId))
    assertNull(g.onTbt(ManeuverType.RIGHT, 280.0, key), "같은 회전 거리 갱신은 무시")
    assertNull(g.onTbt(ManeuverType.RIGHT, 120.0, key))
    val near = assertNotNull(g.onTbt(ManeuverType.RIGHT, 40.0, key))
    assertEquals(TbtGuide.PHRASE_RIGHT_NOW, near.phraseId)
    assertNull(g.onTbt(ManeuverType.RIGHT, 10.0, key), "직전 안내는 한 번만")
    // 다음 회전(키가 바뀜)은 처음부터
    val next = assertNotNull(g.onTbt(ManeuverType.LEFT, 200.0, "LEFT|대왕판교로"))
    assertEquals(TbtGuide.PHRASE_LEFT_FAR, next.phraseId)
    assertEquals("200", next.params["distance"])
    // 처음부터 가까우면 직전 안내만
    val g2 = TbtGuide()
    assertEquals(TbtGuide.PHRASE_LEFT_NOW, g2.onTbt(ManeuverType.LEFT, 30.0, "k")?.phraseId)
    // 직진·도착은 말하지 않음
    assertNull(g2.onTbt(ManeuverType.STRAIGHT, 100.0, "s"))
    assertNull(g2.onTbt(ManeuverType.DESTINATION, 100.0, "d"))
    assertEquals(50, TbtGuide.roundDistance(10.0))
    assertEquals(350, TbtGuide.roundDistance(340.0))
}

fun main() {
    val tests = listOf(
        "직진 모양 좌표" to ::testStraightShapeGeometry,
        "우회전 모양은 진행 방향의 오른쪽" to ::testRightShapeIsToTheRightOfHeading,
        "코스 요약·내비 전달·브리핑" to ::testCourseSummaryAndHandoff,
        "선택지 모양 규칙" to ::testOptionShapesMatchSpecRule,
        "CourseGenerator 래퍼" to ::testGeneratedCourseSourceWrapsGenerator,
        "TBT 안내는 회전당 두 번" to ::testTbtGuideSpeaksTwicePerTurn,
    )
    var failed = 0
    for ((name, t) in tests) {
        try { t(); println("PASS $name") } catch (e: Throwable) { failed++; println("FAIL $name: $e") }
    }
    if (failed > 0) { println("OffsetCourseProviderTest: $failed 개 실패"); kotlin.system.exitProcess(1) }
    println("OffsetCourseProviderTest: 전부 통과")
}
