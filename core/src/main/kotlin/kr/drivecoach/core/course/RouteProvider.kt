// 코스 자동 생성
package kr.drivecoach.core.course

/** 길찾기 요청 한 건. FakeRouteProvider 가 시나리오를 고를 때 그대로 받는다. */
data class RouteRequest(
    val start: LatLon,
    val waypoints: List<LatLon>,
    val goal: LatLon,
    val option: String,
)

/**
 * 길찾기 공급자. app 모듈의 NaverDirectionsProvider 가 HTTP 로 구현하고, core 에서는 FakeRouteProvider 로 대체한다.
 * 동기 호출이며 네트워크 구현은 반드시 백그라운드 스레드에서 호출한다.
 */
interface RouteProvider {
    /**
     * 경로를 구한다. 실패(네트워크 오류, 응답 code != 0)면 null — 생성기는 다음 후보를 시도한다.
     * 더 호출해도 소용없는 실패(일일 호출 상한, 인증 실패)는 [RouteProviderException] 을 던진다 — 생성기는 즉시 중단한다.
     * @param option 공급자별 옵션 문자열. 네이버는 "traoptimal"/"trafast"/"tracomfort"/"traavoidtoll"/"traavoidcaronly".
     */
    fun route(start: LatLon, waypoints: List<LatLon>, goal: LatLon, option: String = "traoptimal"): Route?

    /** 마지막 route() 가 null 을 돌려준 이유(예: "HTTP 400", "응답 code 2: …", "네트워크: …"). 성공했거나 모르면 null. */
    val lastFailure: String? get() = null
}

/**
 * 길찾기 공급자가 "더 호출해도 소용없다" 고 판단했을 때 던진다(일일 호출 상한 초과, 401/403 인증 실패, 429).
 * [CourseGenerator] 는 이것을 받으면 남은 후보를 시도하지 않고 [CourseResult.failure] 에 reason 을 담아 돌려준다.
 */
class RouteProviderException(val reason: String, cause: Throwable? = null) : RuntimeException(reason, cause)

/**
 * 테스트용 공급자. 요청마다 handler 를 호출해 결정적인 Route 를 돌려주고 호출 횟수를 센다.
 * 호출 예산 검증(최대 N회)에도 쓴다.
 */
class FakeRouteProvider(private val handler: (RouteRequest) -> Route?) : RouteProvider {
    /** 지금까지 받은 요청 목록(호출 순서대로). */
    val requests = mutableListOf<RouteRequest>()
    /** 호출 횟수. */
    val callCount: Int get() = requests.size

    override fun route(start: LatLon, waypoints: List<LatLon>, goal: LatLon, option: String): Route? {
        val req = RouteRequest(start, waypoints, goal, option)
        requests += req
        return handler(req)
    }
}

/**
 * 테스트·시뮬레이션에서 Route 를 손으로 조립하는 빌더.
 * 경유지 순서대로 직선 좌표를 잇고, 지정한 지점에 회전 안내를 붙인다.
 */
class RouteBuilder(start: LatLon) {
    private val points = mutableListOf(start)
    private val guides = mutableListOf<Guide>()
    private val sections = mutableListOf<Section>()
    private var lastGuidePoint = 0
    private var sectionStart = 0
    private var sectionName = "도로1"
    private var sectionCongestion = 1
    private var sectionSpeed = 40.0

    /** 다음 좌표까지 직선으로 잇고(중간점 포함) 끝점에 회전 안내를 붙인다. */
    fun leg(to: LatLon, maneuver: ManeuverType, rawCode: Int = -1): RouteBuilder {
        val from = points.last()
        val mid = LatLon((from.lat + to.lat) / 2, (from.lon + to.lon) / 2)
        points += mid; points += to
        val idx = points.size - 1
        val cum = Geo.cumulativeM(points)
        guides += Guide(maneuver, to.lat, to.lon, (cum[idx] - cum[lastGuidePoint]).toInt(), idx, rawCode)
        lastGuidePoint = idx
        return this
    }

    /** 현재 위치부터 새 도로 구간을 시작한다(이전 구간은 여기서 닫힘). */
    fun road(name: String, congestion: Int = 1, speedKmh: Double = 40.0): RouteBuilder {
        closeSection()
        sectionStart = points.size - 1
        sectionName = name; sectionCongestion = congestion; sectionSpeed = speedKmh
        return this
    }

    private fun closeSection() {
        val endIdx = points.size - 1
        if (endIdx <= sectionStart) return
        val cum = Geo.cumulativeM(points)
        sections += Section(
            congestion = sectionCongestion, avgSpeedKmh = sectionSpeed,
            distanceM = (cum[endIdx] - cum[sectionStart]).toInt(), name = sectionName,
            pointIndex = sectionStart, pointCount = endIdx - sectionStart + 1,
        )
    }

    /** 도착지 안내를 붙이고 Route 를 만든다. */
    fun build(avgSpeedKmh: Double = 30.0): Route {
        closeSection()
        val last = points.last()
        val cum = Geo.cumulativeM(points)
        val total = cum.last().toInt()
        if (guides.lastOrNull()?.type != ManeuverType.DESTINATION) {
            guides += Guide(ManeuverType.DESTINATION, last.lat, last.lon, (cum.last() - cum[lastGuidePoint]).toInt(), points.size - 1, 88)
        }
        val durationMs = (total / (avgSpeedKmh / 3.6) * 1000).toLong()
        return Route(points.toList(), guides.toList(), sections.toList(), total, durationMs)
    }
}
