// 코스 자동 생성
package kr.drivecoach.core.course

/**
 * 차량 내비(NaviHelper)에 넘길 요청. 경유지는 최대 maxWaypoints 개로 줄여져 있다.
 * @param destinationLabel 내비 목적지 표시용 이름(사용자 문장이 아니라 화면 라벨이므로 Phrases 를 거치지 않음).
 */
data class NaviRequest(val waypoints: List<LatLon>, val destination: LatLon, val destinationLabel: String)

/** 생성된 코스를 NaviHelper 입력 형태로 바꾸는 순수 함수 모음. */
object NaviHandoff {
    /** Pleos NaviHelper 경유지 상한. SDK 3.0.5 의 WaypointIndex 가 FIRST/SECOND 뿐이므로 2. */
    const val DEFAULT_MAX_WAYPOINTS = 2

    /**
     * 코스 → 내비 요청. **넘기는 점은 전부 길찾기 경로(path) 위의 점** 이다 — 가상 경유지(위경도 오프셋)를 그대로 주면
     * 도로 밖 점이라 차량 내비가 "경로 탐색 오류" 를 낸다(에뮬레이터에서 확인).
     * 1. 경유지: 경로의 WAYPOINT 안내(네이버 type 87)가 요청 경유지 수만큼 있으면 그 좌표(네이버가 도로에 붙인 점), 아니면 요청 경유지에서 가장 가까운 path 점.
     * 2. 경유지가 상한을 넘으면 순서를 유지한 채 고르게 솎는다(첫·마지막은 보존).
     * 3. 목적지: 경로의 마지막 path 점(= 길찾기가 도로에 붙인 goal). 순환 코스도 마찬가지(출발지에서 120 m 떨어진 도로 위 점).
     *    path 가 비어 있으면(가짜 경로) 코스의 goal.
     */
    fun toNaviRequest(course: Course, maxWaypoints: Int = DEFAULT_MAX_WAYPOINTS, destinationLabel: String = "연습 코스 종점"): NaviRequest {
        val snapped = onRoadWaypoints(course)
        val thinned = thin(snapped, maxWaypoints)
        val dest = onRoadDestination(course)
        return NaviRequest(thinned, dest, destinationLabel)
    }

    /** 경로 위에 있는 경유지 목록(요청 경유지 순서). WAYPOINT 안내 좌표 우선, 없으면 가장 가까운 path 점. */
    fun onRoadWaypoints(course: Course): List<LatLon> {
        val route = course.route
        val fromGuides = route.guides.filter { it.type == ManeuverType.WAYPOINT && it.pointIndex in route.points.indices }.map { route.points[it.pointIndex] }
        if (fromGuides.isNotEmpty() && fromGuides.size == course.waypoints.size) return fromGuides
        return snapToRoute(course.waypoints, route.points)
    }

    /** 경로 위의 목적지: 마지막 path 점. path 가 없으면 코스의 goal. */
    fun onRoadDestination(course: Course): LatLon = course.route.end ?: course.goal

    /** 각 경유지를 경로 좌표열에서 가장 가까운 점으로 바꾼다. 경로 좌표가 없으면 그대로. */
    fun snapToRoute(waypoints: List<LatLon>, routePoints: List<LatLon>): List<LatLon> {
        if (routePoints.isEmpty()) return waypoints
        return waypoints.map { w -> routePoints.minByOrNull { Geo.distanceM(it, w) } ?: w }
    }

    /** 리스트를 최대 max 개로 고르게 솎는다. max 가 1 이면 마지막만 남긴다. */
    fun <T> thin(list: List<T>, max: Int): List<T> {
        if (max <= 0) return emptyList()
        if (list.size <= max) return list
        if (max == 1) return listOf(list.last())
        val out = ArrayList<T>(max)
        for (k in 0 until max) {
            val idx = ((k.toDouble() * (list.size - 1)) / (max - 1)).toInt()
            out += list[idx]
        }
        return out
    }

    /**
     * 출발 전 브리핑(5-2)용 "코스 특이사항" 판단: 반대 방향 회전이 500 m 안에 이어지는 지점의 guide 인덱스 쌍.
     * 멘트 자체는 K 의 Phrases 로 만든다. 여기서는 위치만 준다.
     */
    fun quickSuccessiveTurns(route: Route, windowM: Double = CourseSummarizer.LANE_CHANGE_WINDOW_M): List<Pair<Int, Int>> {
        val pos = CourseSummarizer.guidePositionsM(route)
        val out = ArrayList<Pair<Int, Int>>()
        var prev = -1
        route.guides.forEachIndexed { i, g ->
            if (!g.type.isTurn || g.type == ManeuverType.UTURN) return@forEachIndexed
            if (prev >= 0) {
                val pg = route.guides[prev]
                if (pg.type.isLeftish != g.type.isLeftish && pos[i] - pos[prev] <= windowM) out += prev to i
            }
            prev = i
        }
        return out
    }
}
