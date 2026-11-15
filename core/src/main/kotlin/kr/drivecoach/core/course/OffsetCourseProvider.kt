// 담당: S (앱 연결 — 길찾기 API 키가 없을 때 현재 위치 기준 오프셋으로 짧은 연습 코스를 만드는 임시 코스 소스. T 가 실제 Directions API 로 교체)
package kr.drivecoach.core.course

import kotlin.math.cos
import kotlin.math.sin

/**
 * 기획서 5-1 의 "가장 무서운 것 / 목적" 선택지 하나. 화면 라벨은 app 의 strings.xml 에서 이 ID 로 찾는다(여기에는 문장을 두지 않는다).
 * 각 선택지는 코스 조건([CourseSpec] 프리셋)과, API 키 없이 만들 때 쓰는 모양([OffsetShape])을 가진다.
 */
enum class CourseOptionId {
    /** 동네 한 바퀴(우회전 위주). */
    NEIGHBORHOOD_RIGHT,
    /** 시내 입문(좌회전 1~2개). */
    CITY_LEFT,
    /** 가장 무서운 것 = 좌회전 → 좌회전만 N번. */
    LEFT_TURN_DRILL,
    /** 가장 무서운 것 = 차선 변경 → 긴 직선 구간. */
    LANE_CHANGE,
    /** 가장 무서운 것 = 합류 → 합류 한 구간. */
    MERGE,
    /** 가장 무서운 것 = 페달 혼동·경적 압박 → 직진만(회전 없음). */
    STRAIGHT_ONLY,
    /** 가장 무서운 것 = 야간·우천 → 우회전 위주 + 야간·우천 난이도. */
    NIGHT_RAIN,
    /** 목적 = 재시험 → 시험장 느낌의 좌회전 포함 코스(난이도 3). */
    EXAM_RETAKE,
}

/** 오프셋 코스의 모양: 경유지를 진행 방향 기준 어느 쪽에 둘지. */
enum class OffsetShape { STRAIGHT, RIGHT, LEFT }

/** 선택지 ID → 코스 조건·모양. 기획서 5-1 의 추천 표와 1:1. */
object CourseOptions {
    /** 화면에 보여 주는 순서. 2열 격자 기준으로 왼쪽 위부터. */
    val ALL: List<CourseOptionId> = CourseOptionId.values().toList()

    /** 선택지의 코스 조건(기존 CourseSpec 프리셋). 실제 Directions API 를 붙이면 CourseGenerator 가 이 spec 으로 코스를 고른다. */
    fun spec(id: CourseOptionId): CourseSpec = when (id) {
        CourseOptionId.NEIGHBORHOOD_RIGHT -> CourseSpec.neighborhoodLoop()
        CourseOptionId.CITY_LEFT -> CourseSpec.cityIntro()
        CourseOptionId.LEFT_TURN_DRILL -> CourseSpec.leftTurnDrill(3)
        CourseOptionId.LANE_CHANGE -> CourseSpec.laneChangeDrill(3)
        CourseOptionId.MERGE -> CourseSpec.mergeDrill()
        CourseOptionId.STRAIGHT_ONLY -> CourseSpec(fearFocus = Fear.PEDAL_CONFUSION, leftTurns = 0..0, laneChanges = 0..1, maxDistanceKm = 3.0, loop = false, difficulty = 1)
        CourseOptionId.NIGHT_RAIN -> CourseSpec(fearFocus = Fear.NIGHT_RAIN, leftTurns = 0..1, preferRightTurns = true, maxDistanceKm = 4.0, difficulty = 2, nightOrRain = true)
        CourseOptionId.EXAM_RETAKE -> CourseSpec(leftTurns = 1..3, laneChanges = 1..3, maxDistanceKm = 6.0, difficulty = 3)
    }

    /** 선택지의 오프셋 모양. 우회전 위주면 오른쪽, 좌회전 연습이면 왼쪽, 나머지는 직진. */
    fun shape(id: CourseOptionId): OffsetShape = when (id) {
        CourseOptionId.NEIGHBORHOOD_RIGHT, CourseOptionId.NIGHT_RAIN -> OffsetShape.RIGHT
        CourseOptionId.CITY_LEFT, CourseOptionId.LEFT_TURN_DRILL, CourseOptionId.EXAM_RETAKE -> OffsetShape.LEFT
        CourseOptionId.LANE_CHANGE, CourseOptionId.MERGE, CourseOptionId.STRAIGHT_ONLY -> OffsetShape.STRAIGHT
    }

    /** 선택지가 대표하는 "가장 무서운 것". 없으면 null(목적 선택지). */
    fun fear(id: CourseOptionId): Fear? = spec(id).fearFocus
}

/**
 * 코스 소스. "출발지 + 조건 → 코스 결과" 한 가지 계약으로, 앱(CoachController)은 이것만 안다.
 * - [OffsetCourseProvider]: API 키 없이 현재 위치 기준 오프셋 코스(지금).
 * - [GeneratedCourseSource]: CourseGenerator + 실제 길찾기 API(T 가 교체할 것).
 * 둘 다 결과를 [NaviHandoff.toNaviRequest] 로 차량 내비에 넘긴다.
 */
interface CourseSource {
    /**
     * @param start 현재 위치.
     * @param headingDeg 현재 진행 방향(북=0, 시계방향). 모르면 null → 북쪽 기준.
     * @param spec 코스 조건.
     */
    fun generate(start: LatLon, headingDeg: Double?, spec: CourseSpec): CourseResult
}

/**
 * CourseGenerator(실제 길찾기 API)를 [CourseSource] 로 감싼다. 진행 방향은 쓰지 않는다(생성기가 8방위를 전부 시도).
 * 네트워크 호출(동기)이므로 앱은 백그라운드 스레드에서 부른다. 실패 이유는 [CourseResult.failure] 로 올라온다(T, 11/3).
 * 내비에 넘길 점은 [NaviHandoff.toNaviRequest] 가 경로 path 위의 점으로 만든다.
 */
class GeneratedCourseSource(private val generator: CourseGenerator) : CourseSource {
    // (11/14 베타: "재시험 대비"와 "시내 입문"이 같은 코스) 조건마다 첫 경유지 방위를 다르게 돌려 서로 다른 후보부터 본다
    override fun generate(start: LatLon, headingDeg: Double?, spec: CourseSpec): CourseResult =
        generator.generate(start, spec, ((spec.hashCode() and 0x7fffffff) % 8) * 22.5)
}

/**
 * 길찾기 API 없이 현재 위치에서 단순 위경도 오프셋으로 짧은 연습 코스를 만든다 (기획서 5-1 의 임시 구현).
 *
 * 모양(진행 방향 기준, 단위 km. (앞, 오른쪽)):
 *  - STRAIGHT: 경유지 (0.5, 0) → (1.0, 0), 목적지 (1.5, 0). 회전 없음.
 *  - RIGHT:    경유지 (0.7, 0) → (0.7, 0.7), 목적지 (0.5, 1.4). 모서리마다 우회전(시계 방향).
 *  - LEFT:     RIGHT 의 좌우 대칭. 모서리마다 좌회전.
 * 목적지는 출발지에서 약 1.5 km, 경유지 간격은 약 0.7 km. 경유지는 최대 [NaviHandoff.DEFAULT_MAX_WAYPOINTS] 개.
 *
 * 위경도 환산은 1 km ≈ 0.009° 위도, 경도는 0.009° / cos(위도) (37.4°N 에서 ≈ 0.0113°). 도로 스냅은 하지 않는다 —
 * 차량 내비가 가장 가까운 도로로 경유지를 붙인다(실제 Directions API 가 붙으면 [GeneratedCourseSource] 로 교체).
 * 결과 Route 는 직선 구간 + 모서리 회전으로 조립하므로 [CourseSummarizer]/[CourseBriefing] 이 그대로 동작한다.
 */
class OffsetCourseProvider(private val shapeOf: (CourseSpec) -> OffsetShape = { shapeFromSpec(it) }) : CourseSource {

    override fun generate(start: LatLon, headingDeg: Double?, spec: CourseSpec): CourseResult {
        val shape = shapeOf(spec)
        val pts = points(start, headingDeg ?: 0.0, shape)
        val course = buildCourse(start, pts, shape, spec)
        // 오프셋 코스는 편도(1.5 km 앞)라 순환 조건은 채점에서 뺀다 — 임시 구현이지 조건 위반이 아니다.
        val scored = CourseScorer.score(course, spec.copy(loop = false))
        return CourseResult(scored, listOf(scored), apiCalls = 0, candidatesTried = 1)
    }

    /** 경유지 2개 + 목적지 1개 = 총 3점(순서대로). 테스트·화면 표시용으로 공개. */
    fun points(start: LatLon, headingDeg: Double, shape: OffsetShape): List<LatLon> {
        val local: List<Pair<Double, Double>> = when (shape) {  // (앞 km, 오른쪽 km)
            OffsetShape.STRAIGHT -> listOf(0.5 to 0.0, 1.0 to 0.0, 1.5 to 0.0)
            OffsetShape.RIGHT -> listOf(0.7 to 0.0, 0.7 to 0.7, 0.5 to 1.4)
            OffsetShape.LEFT -> listOf(0.7 to 0.0, 0.7 to -0.7, 0.5 to -1.4)
        }
        return local.map { (fwd, right) -> offset(start, headingDeg, fwd, right) }
    }

    private fun buildCourse(start: LatLon, pts: List<LatLon>, shape: OffsetShape, spec: CourseSpec): Course {
        val corner = when (shape) {
            OffsetShape.STRAIGHT -> ManeuverType.STRAIGHT
            OffsetShape.RIGHT -> ManeuverType.RIGHT
            OffsetShape.LEFT -> ManeuverType.LEFT
        }
        val b = RouteBuilder(start)
        pts.forEachIndexed { i, p ->
            b.road("연습 구간 ${i + 1}", congestion = 1, speedKmh = 40.0)
            if (i < pts.lastIndex) b.leg(p, corner) else b.leg(p, ManeuverType.DESTINATION, 88)
        }
        val route = b.build(avgSpeedKmh = 30.0)
        val waypoints = pts.dropLast(1)
        val goal = pts.last()
        val summary = CourseSummarizer.summarize(route, spec.nightOrRain)
        return Course(route, start, waypoints, goal, summary)
    }

    companion object {
        /** 1 km 당 위도(도). */
        const val LAT_DEG_PER_KM = 0.009

        /** 1 km 당 경도(도). 위도에 따라 달라진다(37.4°N ≈ 0.0113). */
        fun lonDegPerKm(latDeg: Double): Double = LAT_DEG_PER_KM / cos(Math.toRadians(latDeg)).coerceAtLeast(0.1)

        /** 출발점에서 진행 방향 heading 기준으로 앞 fwdKm, 오른쪽 rightKm 만큼 간 점(단순 평면 오프셋). */
        fun offset(from: LatLon, headingDeg: Double, fwdKm: Double, rightKm: Double): LatLon {
            val h = Math.toRadians(headingDeg)
            // 앞 = (sin h 동, cos h 북), 오른쪽 = (cos h 동, −sin h 북)
            val eastKm = fwdKm * sin(h) + rightKm * cos(h)
            val northKm = fwdKm * cos(h) - rightKm * sin(h)
            return LatLon(from.lat + northKm * LAT_DEG_PER_KM, from.lon + eastKm * lonDegPerKm(from.lat))
        }

        /** 조건만으로 모양을 고른다(선택지 ID 없이 호출될 때): 우회전 위주 → RIGHT, 좌회전 원함 → LEFT, 그 외 STRAIGHT. */
        fun shapeFromSpec(spec: CourseSpec): OffsetShape = when {
            spec.preferRightTurns -> OffsetShape.RIGHT
            spec.fearFocus == Fear.LEFT_TURN || spec.leftTurns.first >= 1 -> OffsetShape.LEFT
            else -> OffsetShape.STRAIGHT
        }
    }
}
