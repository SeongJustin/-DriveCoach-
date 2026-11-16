// 코스 자동 생성
package kr.drivecoach.core.course

import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/** 테스트용 출발지(강남역 근처). */
private val START = LatLon(37.4979, 127.0276)

/** 요청 경유지가 시계(+1)/반시계(-1) 중 어느 방향으로 도는지. */
private fun orientationOf(req: RouteRequest): Int {
    val b0 = Geo.bearingDeg(req.start, req.waypoints[0])
    val b1 = Geo.bearingDeg(req.start, req.waypoints[1])
    return if (Geo.normDeg(b1 - b0) > 0) 1 else -1
}

/** 첫 경유지 방위각(0~360). */
private fun firstBearing(req: RouteRequest): Double = Geo.bearingDeg(req.start, req.waypoints[0])

/** 세 경유지를 도는 결정적 삼각 순환 경로를 만든다. 모서리 회전 유형은 corners 로 지정. */
private fun triangle(req: RouteRequest, corners: List<ManeuverType>, congestions: List<Int> = listOf(1, 1, 1, 1)): Route {
    val b = RouteBuilder(req.start)
    val names = listOf("A로", "B로", "C로", "D로")
    req.waypoints.forEachIndexed { i, w ->
        b.road(names[i], congestions[i], if (congestions[i] >= 3) 10.0 else 40.0)
        b.leg(w, corners[i])
    }
    b.road(names[3], congestions[3])
    b.leg(req.goal, ManeuverType.DESTINATION, 88)
    return b.build()
}

private fun testLeftTurnDrillPicksCounterClockwise() {
    val provider = FakeRouteProvider { req ->
        if (orientationOf(req) == -1) triangle(req, listOf(ManeuverType.LEFT, ManeuverType.LEFT, ManeuverType.LEFT))
        else triangle(req, listOf(ManeuverType.RIGHT, ManeuverType.RIGHT, ManeuverType.RIGHT))
    }
    val gen = CourseGenerator(provider, GeneratorConfig(maxApiCalls = 8))
    val result = gen.generate(START, CourseSpec.leftTurnDrill(3))
    val best = assertNotNull(result.best, "코스가 나와야 함")
    assertTrue(best.meetsSpec, "조건 충족: ${best.reasons}")
    assertEquals(3, best.course.summary.leftTurns)
    assertEquals(0, best.course.summary.rightTurns)
    assertTrue(best.course.summary.returnsToStart)
    assertEquals(1, result.apiCalls, "반시계 후보를 먼저 시도하므로 1회 호출로 끝나야 함")
    assertTrue(result.apiCalls <= 8)
    println("  좌회전 3회 코스: ${best.course.summary.distanceKm}km, 난이도 ${best.course.summary.difficultyScore}, 호출 ${result.apiCalls}회")
}

private fun testMergeDrillFindsMergeRoute() {
    val provider = FakeRouteProvider { req ->
        val east = Geo.normDeg(firstBearing(req) - 90.0).let { it > -30 && it < 30 }
        if (east) triangle(req, listOf(ManeuverType.HIGHWAY_ENTER, ManeuverType.MERGE, ManeuverType.HIGHWAY_EXIT))
        else triangle(req, listOf(ManeuverType.RIGHT, ManeuverType.LEFT, ManeuverType.RIGHT))
    }
    val gen = CourseGenerator(provider, GeneratorConfig(maxApiCalls = 10))
    val result = gen.generate(START, CourseSpec.mergeDrill())
    val best = assertNotNull(result.best)
    assertTrue(best.meetsSpec, "조건 충족: ${best.reasons}")
    assertTrue(best.course.summary.merges >= 1, "합류 구간 포함")
    assertEquals(1, best.course.summary.highwayEnters)
    assertEquals(3, best.course.summary.laneChangeEstimate, "램프 진입·합류·진출 각 1회로 추정")
    assertTrue(result.apiCalls in 2..10, "방위각 0 후보가 실패한 뒤 동쪽 후보에서 찾음: ${result.apiCalls}")
    // 합류 없는 후보들은 조건 미달로 기록되어야 한다.
    assertTrue(result.scored.filter { !it.meetsSpec }.all { it.course.summary.merges == 0 })
    println("  합류 코스: ${best.course.summary.distanceKm}km, 난이도 ${best.course.summary.difficultyScore}, 호출 ${result.apiCalls}회")
}

private fun testAvoidsCongestedRoutes() {
    val handler: (RouteRequest) -> Route = { req ->
        val b = firstBearing(req)
        val congested = Geo.normDeg(b - 0.0).let { kotlin.math.abs(it) < 1 } || kotlin.math.abs(Geo.normDeg(b - 90.0)) < 1
        val cong = if (congested) listOf(1, 3, 3, 1) else listOf(1, 1, 1, 1)
        if (orientationOf(req) == -1) triangle(req, listOf(ManeuverType.LEFT, ManeuverType.LEFT, ManeuverType.RIGHT), cong)
        else triangle(req, listOf(ManeuverType.RIGHT, ManeuverType.RIGHT, ManeuverType.LEFT), cong)
    }
    // 1) 첫 충족 후보에서 멈추는 기본 설정
    val p1 = FakeRouteProvider(handler)
    val r1 = CourseGenerator(p1, GeneratorConfig(maxApiCalls = 12)).generate(START, CourseSpec.cityIntro())
    val b1 = assertNotNull(r1.best)
    assertTrue(b1.meetsSpec, b1.reasons.toString())
    assertEquals(1, b1.course.summary.maxCongestion, "혼잡 경로를 피해야 함")
    assertTrue(b1.course.summary.leftTurns in 1..2)
    assertEquals(5, r1.apiCalls, "북·동쪽 4개 후보(혼잡)를 거른 뒤 남쪽 후보")
    assertTrue(r1.scored.take(4).all { !it.meetsSpec && it.course.summary.maxCongestion == 3 })
    // 2) 예산 끝까지 돌려도 최고점은 혼잡하지 않은 경로
    val p2 = FakeRouteProvider(handler)
    val r2 = CourseGenerator(p2, GeneratorConfig(maxApiCalls = 6, stopAtFirstMatch = false)).generate(START, CourseSpec.cityIntro())
    assertEquals(6, r2.apiCalls)
    assertEquals(1, assertNotNull(r2.best).course.summary.maxCongestion)
    assertTrue(r2.best!!.score > r2.scored.first().score)
    println("  혼잡 회피: 선택 코스 혼잡도 ${b1.course.summary.maxCongestion}, 호출 ${r1.apiCalls}회 / 전수 ${r2.apiCalls}회")
}

private fun testLaneChangeEstimateRules() {
    val s = START
    val n600 = Geo.offset(s, 0.0, 600.0)
    val e300 = Geo.offset(n600, 90.0, 300.0)
    val n2000 = Geo.offset(e300, 0.0, 2000.0)
    val route = RouteBuilder(s)
        .road("A로").leg(n600, ManeuverType.RIGHT)        // 규칙2: A로는 경로 시작(0m)에서 시작 → 제외. 규칙3: 이전 회전 없음 → 0
        .road("B로").leg(e300, ManeuverType.LEFT)         // 규칙2(300m 전 도로 변경) + 규칙3(반대 회전 300m 안) → 1회만
        .road("C로").leg(n2000, ManeuverType.LEFT)        // 2000m 전 도로 변경, 같은 방향 → 0
        .build()
    assertEquals(1, CourseSummarizer.laneChangeEstimate(route))
    assertEquals(listOf(0 to 1), NaviHandoff.quickSuccessiveTurns(route), "우회전 직후 좌회전 = 브리핑 특이사항")
}

private const val SAMPLE_JSON = """
{"code":0,"message":"길찾기를 성공하였습니다.","currentDateTime":"2026-11-02T10:00:00",
 "route":{"traoptimal":[{
   "summary":{"start":{"location":[127.0276,37.4979]},"goal":{"location":[127.0300,37.5000],"dir":1},
              "distance":3210,"duration":540000,"tollFare":0,"taxiFare":5800,"fuelPrice":350},
   "path":[[127.0276,37.4979],[127.0280,37.4990],[127.0290,37.4995],[127.0300,37.5000]],
   "section":[{"pointIndex":0,"pointCount":2,"distance":1200,"name":"강남대로","congestion":2,"speed":22},
              {"pointIndex":2,"pointCount":2,"distance":1000,"name":"테헤란로","congestion":1,"speed":45}],
   "guide":[{"pointIndex":1,"type":2,"instructions":"좌회전","distance":1200,"duration":120000},
            {"pointIndex":2,"type":8,"instructions":"비보호 좌회전","distance":900,"duration":100000},
            {"pointIndex":2,"type":82,"instructions":"오른쪽에 본선으로 합류","distance":10,"duration":1000},
            {"pointIndex":3,"type":88,"instructions":"목적지","distance":1100,"duration":110000}]
 }]}}
"""

private fun testNaverMapperParsesSample() {
    val route = assertNotNull(NaverDirectionsMapper.fromJson(SAMPLE_JSON, "traoptimal"))
    assertEquals(4, route.points.size)
    assertEquals(LatLon(37.4979, 127.0276), route.points[0], "경도,위도 순서가 뒤집혀 들어와야 함")
    assertEquals(3210, route.totalDistanceM)
    assertEquals(540000L, route.totalDurationMs)
    assertEquals(listOf(ManeuverType.LEFT, ManeuverType.UNPROTECTED_LEFT, ManeuverType.MERGE, ManeuverType.DESTINATION), route.guides.map { it.type })
    assertEquals(2, route.sections[0].congestion)
    val sum = CourseSummarizer.summarize(route)
    assertEquals(2, sum.leftTurns); assertEquals(1, sum.unprotectedLefts); assertEquals(1, sum.merges)
    assertEquals(2, sum.maxCongestion)
    assertTrue(sum.laneChangeEstimate >= 1)
    // 실패 응답과 깨진 JSON
    assertEquals(null, NaverDirectionsMapper.fromJson("""{"code":1,"message":"출발지와 도착지가 동일"}"""))
    assertEquals(null, NaverDirectionsMapper.fromJson("{not json"))
    // 코드표 몇 개 확인
    assertEquals(ManeuverType.ROUNDABOUT, NaverDirectionsMapper.maneuver(25))
    assertEquals(ManeuverType.HIGHWAY_EXIT, NaverDirectionsMapper.maneuver(80))
    assertEquals(ManeuverType.TOLLGATE, NaverDirectionsMapper.maneuver(122))
    assertEquals(ManeuverType.OTHER, NaverDirectionsMapper.maneuver(999))
    assertEquals(ManeuverType.LEFT, TmapDirectionsMapper.maneuver(12))
    assertEquals(ManeuverType.HIGHWAY_ENTER, TmapDirectionsMapper.maneuver(113))
}

private fun testNaviHandoff() {
    assertEquals(listOf(1, 3, 5), NaviHandoff.thin(listOf(1, 2, 3, 4, 5), 3))
    assertEquals(listOf(1, 2), NaviHandoff.thin(listOf(1, 2), 5))
    assertEquals(listOf(9), NaviHandoff.thin(listOf(1, 5, 9), 1))
    val provider = FakeRouteProvider { req -> triangle(req, listOf(ManeuverType.LEFT, ManeuverType.LEFT, ManeuverType.LEFT)) }
    val best = assertNotNull(CourseGenerator(provider).generate(START, CourseSpec.leftTurnDrill(3)).best)
    val navi = NaviHandoff.toNaviRequest(best.course, maxWaypoints = 2)
    assertEquals(2, navi.waypoints.size)
    assertEquals(best.course.route.end, navi.destination, "목적지 = 경로의 마지막 path 점(도로 위)")
    assertTrue(Geo.distanceM(START, navi.destination) <= CourseSummarizer.LOOP_CLOSE_M, "순환 코스의 목적지는 출발지 근처")
    assertTrue(navi.waypoints.all { w -> best.course.route.points.contains(w) }, "경유지는 경로 좌표에 스냅")
}

/** 경유지 2개를 요청한 네이버 응답 샘플: path 는 도로 위 점, guide 87(경유지)이 경로상 어디에 붙었는지 알려 준다. */
private const val SAMPLE_WAYPOINT_JSON = """
{"code":0,"message":"길찾기를 성공하였습니다.",
 "route":{"traoptimal":[{
   "summary":{"start":{"location":[127.0276,37.4979]},"goal":{"location":[127.0321,37.4982],"dir":2},"distance":2600,"duration":420000},
   "path":[[127.0276,37.4979],[127.0278,37.4990],[127.0281,37.5001],[127.0295,37.5003],[127.0310,37.5004],[127.0318,37.4995],[127.0321,37.4982]],
   "section":[{"pointIndex":0,"pointCount":3,"distance":900,"name":"강남대로","congestion":1,"speed":35},
              {"pointIndex":2,"pointCount":3,"distance":1000,"name":"테헤란로","congestion":1,"speed":40},
              {"pointIndex":4,"pointCount":3,"distance":700,"name":"역삼로","congestion":1,"speed":30}],
   "guide":[{"pointIndex":2,"type":3,"instructions":"우회전","distance":900,"duration":120000},
            {"pointIndex":3,"type":87,"instructions":"경유지","distance":400,"duration":60000},
            {"pointIndex":4,"type":3,"instructions":"우회전","distance":600,"duration":90000},
            {"pointIndex":5,"type":87,"instructions":"경유지","distance":400,"duration":60000},
            {"pointIndex":6,"type":88,"instructions":"목적지","distance":300,"duration":90000}]
 }]}}
"""

/** 네이버 응답에서 내비에 넘길 점을 만들면 전부 path 위의 점이어야 한다(오프셋 경유지는 도로 밖이라 내비가 거부). */
private fun testHandoffPointsAreOnRoutePath() {
    val route = assertNotNull(NaverDirectionsMapper.fromJson(SAMPLE_WAYPOINT_JSON, "traoptimal"))
    // 요청 때 쓴 가상 경유지: 도로에서 수십 m 떨어진 점
    val rawWps = listOf(LatLon(37.5006, 127.0296), LatLon(37.4998, 127.0316))
    val rawGoal = LatLon(37.4985, 127.0325)
    val course = Course(route, START, rawWps, rawGoal, CourseSummarizer.summarize(route))
    val navi = NaviHandoff.toNaviRequest(course, maxWaypoints = 2)
    assertEquals(2, navi.waypoints.size)
    assertEquals(listOf(route.points[3], route.points[5]), navi.waypoints, "경유지 = guide 87 의 pointIndex 좌표")
    assertEquals(route.points.last(), navi.destination, "목적지 = 마지막 path 점")
    assertEquals(LatLon(37.4982, 127.0321), navi.destination)
    assertTrue(navi.waypoints.none { it in rawWps } && navi.destination != rawGoal, "원래 오프셋 점은 하나도 넘기지 않음")
    // guide 87 이 없으면(가짜 경로) 가장 가까운 path 점으로
    val noWpGuides = route.copy(guides = route.guides.filter { it.type != ManeuverType.WAYPOINT })
    val navi2 = NaviHandoff.toNaviRequest(course.copy(route = noWpGuides), maxWaypoints = 2)
    assertTrue(navi2.waypoints.all { it in route.points })
    assertEquals(listOf(route.points[3], route.points[5]), navi2.waypoints, "가장 가까운 path 점")
    // 경유지 3개 요청(순환) → 2개로 솎아도 전부 path 위
    val threeWps = listOf(rawWps[0], LatLon(37.5004, 127.0309), rawWps[1])
    val navi3 = NaviHandoff.toNaviRequest(course.copy(route = noWpGuides, waypoints = threeWps), maxWaypoints = 2)
    assertEquals(2, navi3.waypoints.size)
    assertTrue(navi3.waypoints.all { it in route.points })
    assertEquals(2, CourseSummarizer.summarize(route).rightTurns)
}

/** 요청 URL: 경도,위도 순서·경유지 '|' 구분·옵션. Directions 15 가 기본 엔드포인트. */
private fun testNaverRequestUrlFormat() {
    val start = LatLon(37.4979, 127.0276)
    val goal = LatLon(37.5, 127.03)
    val wps = listOf(LatLon(37.49, 127.02), LatLon(37.51, 127.04))
    val url = NaverDirectionsApi.buildUrl(start, wps, goal, "traoptimal")
    assertEquals(
        "https://maps.apigw.ntruss.com/map-direction-15/v1/driving?start=127.027600%2C37.497900&goal=127.030000%2C37.500000" +
            "&waypoints=127.020000%2C37.490000%7C127.040000%2C37.510000&option=traoptimal&cartype=1&lang=ko",
        url,
    )
    assertTrue(url.startsWith(NaverDirectionsApi.ENDPOINT_DIRECTIONS_15_MAPS_HOST))
    assertEquals(NaverDirectionsApi.ENDPOINT_DIRECTIONS_15_MAPS_HOST, NaverDirectionsApi.endpoint, "기본은 Directions 15 (maps 호스트)")
    // 경유지 없으면 waypoints 파라미터 자체가 없다
    assertTrue(!NaverDirectionsApi.buildUrl(start, emptyList(), goal, "trafast").contains("waypoints"))
    // Directions 5 로 바꾸면 경유지 5개 제한
    val six = List(6) { LatLon(37.5 + it * 0.001, 127.0) }
    assertEquals(5, NaverDirectionsApi.maxWaypoints(NaverDirectionsApi.ENDPOINT_DIRECTIONS_5))
    assertEquals(15, NaverDirectionsApi.maxWaypoints(NaverDirectionsApi.ENDPOINT_DIRECTIONS_15))
    val threw = try { NaverDirectionsApi.buildUrl(start, six, goal, "traoptimal", endpoint = NaverDirectionsApi.ENDPOINT_DIRECTIONS_5); false } catch (e: IllegalArgumentException) { true }
    assertTrue(threw, "Directions 5 에 경유지 6개는 거부")
    assertTrue(NaverDirectionsApi.buildUrl(start, six, goal, "traoptimal").contains("waypoints="), "Directions 15 는 6개 허용")
}

/** 좌표 순서 변환: LatLon(위도 먼저) ↔ 네이버 "경도,위도" / path [경도, 위도]. */
private fun testNaverCoordinateOrder() {
    val p = LatLon(lat = 37.4979, lon = 127.0276)
    assertEquals("127.027600,37.497900", NaverDirectionsApi.toQuery(p), "쿼리는 경도,위도")
    assertEquals(p, NaverDirectionsApi.fromPathPair(listOf(127.0276, 37.4979)), "path [경도, 위도] → LatLon(위도, 경도)")
    assertEquals(null, NaverDirectionsApi.fromPathPair(listOf(127.0276)))
    assertEquals(null, NaverDirectionsApi.fromPathPair(null))
    // 왕복: query 로 만든 문자열을 path 형태로 되돌리면 같은 점(소수점 6자리)
    val back = NaverDirectionsApi.toQuery(p).split(",").map { it.toDouble() }
    assertEquals(p, NaverDirectionsApi.fromPathPair(back))
    // 오류 문구
    assertEquals(null, NaverDirectionsApi.failureFromBody("""{"code":0,"route":{}}"""))
    assertEquals("응답 code 2: 출발지 또는 도착지가 도로 주변이 아님", NaverDirectionsApi.failureFromBody("""{"code":2,"message":"x"}"""))
    assertEquals("Authentication Failed", NaverDirectionsApi.failureFromBody("""{"error":{"errorCode":"200","message":"Authentication Failed"}}"""))
    assertTrue(NaverDirectionsApi.failureFromBody("<html>")!!.startsWith("JSON 아님"))
    assertTrue(NaverDirectionsApi.isFatalHttp(401) && NaverDirectionsApi.isFatalHttp(429) && !NaverDirectionsApi.isFatalHttp(400))
}

/** 공급자가 RouteProviderException(일일 상한 등)을 던지면 생성기는 남은 후보를 시도하지 않고 이유를 올린다. */
private fun testProviderAbortStopsGenerator() {
    val p = FakeRouteProvider { throw RouteProviderException("일일 호출 상한 (100회)") }
    val r = CourseGenerator(p, GeneratorConfig(maxApiCalls = 12)).generate(START, CourseSpec.cityIntro())
    assertEquals(1, p.callCount, "첫 호출에서 중단")
    assertEquals(null, r.best)
    assertTrue(r.aborted)
    assertEquals("일일 호출 상한 (100회)", r.failure)
    // 2회 성공 뒤 상한: 그때까지 받은 후보 중 최고를 돌려준다
    var n = 0
    val p2 = FakeRouteProvider { req ->
        n++
        if (n > 2) throw RouteProviderException("일일 호출 상한 (100회)")
        triangle(req, listOf(ManeuverType.RIGHT, ManeuverType.RIGHT, ManeuverType.RIGHT))
    }
    val r2 = CourseGenerator(p2, GeneratorConfig(maxApiCalls = 12)).generate(START, CourseSpec.leftTurnDrill(3))
    assertEquals(3, p2.callCount)
    assertNotNull(r2.best); assertTrue(r2.aborted); assertEquals(null, r2.failure)
    // null 실패는 lastFailure 로 이유가 올라온다
    val p3 = object : RouteProvider {
        override var lastFailure: String? = null
        override fun route(start: LatLon, waypoints: List<LatLon>, goal: LatLon, option: String): Route? { lastFailure = "HTTP 500"; return null }
    }
    val r3 = CourseGenerator(p3, GeneratorConfig(maxApiCalls = 3)).generate(START, CourseSpec.cityIntro())
    assertEquals(null, r3.best); assertEquals("HTTP 500", r3.failure); assertTrue(!r3.aborted); assertEquals(3, r3.apiCalls)
}

private fun testBriefingNotes() {
    val route = assertNotNull(NaverDirectionsMapper.fromJson(SAMPLE_JSON))
    val course = Course(route, START, emptyList(), route.end!!, CourseSummarizer.summarize(route))
    val notes = CourseBriefing.notes(course)
    assertEquals(CourseBriefing.ID_SUMMARY, notes[0].phraseId)
    assertEquals("2", notes[0].params["left_turns"])
    assertTrue(notes.any { it.phraseId == CourseBriefing.ID_UNPROTECTED_LEFT && !it.params["location"].isNullOrBlank() })
    assertTrue(notes.any { it.phraseId == CourseBriefing.ID_MERGE })
    assertEquals(CourseBriefing.ID_MERGE_FALLBACK, notes.last().phraseId)
}

private fun testBudgetRespected() {
    val provider = FakeRouteProvider { null } // 항상 실패
    val r = CourseGenerator(provider, GeneratorConfig(maxApiCalls = 4)).generate(START, CourseSpec.cityIntro())
    assertEquals(4, r.apiCalls); assertEquals(4, provider.callCount)
    assertEquals(null, r.best)
}

fun main() {
    val tests = listOf(
        "좌회전 3회 코스 선택" to ::testLeftTurnDrillPicksCounterClockwise,
        "합류 포함 코스" to ::testMergeDrillFindsMergeRoute,
        "혼잡 경로 회피" to ::testAvoidsCongestedRoutes,
        "차선 변경 추정 규칙" to ::testLaneChangeEstimateRules,
        "네이버 응답 파싱" to ::testNaverMapperParsesSample,
        "내비 전달" to ::testNaviHandoff,
        "브리핑 멘트 ID" to ::testBriefingNotes,
        "호출 예산" to ::testBudgetRespected,
        "내비 전달 점은 경로 위" to ::testHandoffPointsAreOnRoutePath,
        "네이버 요청 URL 형식" to ::testNaverRequestUrlFormat,
        "네이버 좌표 순서 변환" to ::testNaverCoordinateOrder,
        "공급자 중단(일일 상한)" to ::testProviderAbortStopsGenerator,
    )
    var failed = 0
    for ((name, t) in tests) {
        try { t(); println("PASS $name") } catch (e: Throwable) { failed++; println("FAIL $name: $e") }
    }
    if (failed > 0) { println("CourseGeneratorTest: $failed 개 실패"); kotlin.system.exitProcess(1) }
    println("CourseGeneratorTest: 전부 통과")
}
