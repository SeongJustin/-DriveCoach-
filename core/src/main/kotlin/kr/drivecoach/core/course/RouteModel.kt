// 코스 자동 생성
package kr.drivecoach.core.course

import kotlin.math.asin
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/** 위도·경도 한 점 (단위: 도). 네이버·TMAP 응답은 [경도, 위도] 순서이므로 매퍼에서 뒤집어 넣는다. */
data class LatLon(val lat: Double, val lon: Double)

/** 공급자 독립 회전 유형. 네이버 guide.type / TMAP turnType 을 이 13가지로 접는다. */
enum class ManeuverType {
    STRAIGHT, LEFT, RIGHT, UTURN, UNPROTECTED_LEFT, ROUNDABOUT,
    HIGHWAY_ENTER, HIGHWAY_EXIT, MERGE, TOLLGATE, WAYPOINT, DESTINATION, OTHER;

    /** 운전자가 핸들을 꺾는 "회전"으로 세는 유형인지. */
    val isTurn: Boolean get() = this == LEFT || this == RIGHT || this == UTURN || this == UNPROTECTED_LEFT
    /** 좌회전 계열(보호·비보호 모두). */
    val isLeftish: Boolean get() = this == LEFT || this == UNPROTECTED_LEFT
}

/**
 * 분기점 안내 하나.
 * @param distanceM 이전 분기점(또는 출발지)부터 이 분기점까지 거리(m).
 * @param pointIndex Route.points 안에서의 좌표 인덱스. 모르면 -1.
 * @param rawCode 공급자 원본 코드(디버깅용).
 */
data class Guide(
    val type: ManeuverType,
    val lat: Double,
    val lon: Double,
    val distanceM: Int,
    val pointIndex: Int = -1,
    val rawCode: Int = -1,
    val instructions: String = "",
)

/**
 * 도로명 기준 긴 구간 하나.
 * @param congestion 0 값없음 / 1 원활 / 2 서행 / 3 혼잡 (네이버 기준. TMAP 은 0).
 */
data class Section(
    val congestion: Int,
    val avgSpeedKmh: Double,
    val distanceM: Int,
    val name: String = "",
    val pointIndex: Int = -1,
    val pointCount: Int = 0,
)

/** 길찾기 API 응답 한 건을 공급자 독립 형태로 담은 경로. */
data class Route(
    val points: List<LatLon>,
    val guides: List<Guide>,
    val sections: List<Section>,
    val totalDistanceM: Int,
    val totalDurationMs: Long,
) {
    /** 출발점(첫 좌표). 좌표가 없으면 null. */
    val start: LatLon? get() = points.firstOrNull()
    /** 도착점(마지막 좌표). 좌표가 없으면 null. */
    val end: LatLon? get() = points.lastOrNull()
}

/** 지구 위 두 점 사이 거리·방위각 계산. 코스 생성에 필요한 최소한만 둔다. */
object Geo {
    private const val EARTH_R = 6371000.0

    /** 두 점 사이 대권 거리(m). */
    fun distanceM(a: LatLon, b: LatLon): Double {
        val dLat = Math.toRadians(b.lat - a.lat)
        val dLon = Math.toRadians(b.lon - a.lon)
        val la1 = Math.toRadians(a.lat); val la2 = Math.toRadians(b.lat)
        val h = sin(dLat / 2) * sin(dLat / 2) + cos(la1) * cos(la2) * sin(dLon / 2) * sin(dLon / 2)
        return 2 * EARTH_R * asin(sqrt(h))
    }

    /** 점 from 에서 방위각 bearingDeg(북=0, 시계방향)으로 distM 만큼 간 점. */
    fun offset(from: LatLon, bearingDeg: Double, distM: Double): LatLon {
        val br = Math.toRadians(bearingDeg)
        val d = distM / EARTH_R
        val la1 = Math.toRadians(from.lat); val lo1 = Math.toRadians(from.lon)
        val la2 = asin(sin(la1) * cos(d) + cos(la1) * sin(d) * cos(br))
        val lo2 = lo1 + atan2(sin(br) * sin(d) * cos(la1), cos(d) - sin(la1) * sin(la2))
        return LatLon(Math.toDegrees(la2), ((Math.toDegrees(lo2) + 540.0) % 360.0) - 180.0)
    }

    /** a → b 의 초기 방위각(0~360). */
    fun bearingDeg(a: LatLon, b: LatLon): Double {
        val la1 = Math.toRadians(a.lat); val la2 = Math.toRadians(b.lat)
        val dLon = Math.toRadians(b.lon - a.lon)
        val y = sin(dLon) * cos(la2)
        val x = cos(la1) * sin(la2) - sin(la1) * cos(la2) * cos(dLon)
        return (Math.toDegrees(atan2(y, x)) + 360.0) % 360.0
    }

    /** 경로 좌표열을 따라 누적 거리(m) 배열을 만든다. cum[i] = points[0]..points[i] 거리. */
    fun cumulativeM(points: List<LatLon>): DoubleArray {
        val cum = DoubleArray(points.size)
        for (i in 1 until points.size) cum[i] = cum[i - 1] + distanceM(points[i - 1], points[i])
        return cum
    }

    /** 각도 차이를 -180..180 으로 정규화. */
    fun normDeg(d: Double): Double { var x = d % 360.0; if (x > 180) x -= 360; if (x < -180) x += 360; return x }
}

/**
 * 네이버 클라우드 Directions 15 응답 → Route 변환.
 * 코드표는 1-5 와 동일하게 유지한다.
 */
object NaverDirectionsMapper {
    /** guide.type 정수 코드 → ManeuverType. 문서에 없는 코드는 OTHER. */
    fun maneuver(code: Int): ManeuverType = when (code) {
        1, 54, 55, 56 -> ManeuverType.STRAIGHT
        2, 4, 11, 12, 13, 41 -> ManeuverType.LEFT
        3, 5, 14, 15, 16, 42 -> ManeuverType.RIGHT
        6 -> ManeuverType.UTURN
        8 -> ManeuverType.UNPROTECTED_LEFT
        in 21..34, in 91..104 -> ManeuverType.ROUNDABOUT
        50, 52, 57, 59, 66, 68, 75, 76, 77 -> ManeuverType.HIGHWAY_ENTER
        51, 53, 58, 60, 67, 69, 78, 79, 80 -> ManeuverType.HIGHWAY_EXIT
        81, 82 -> ManeuverType.MERGE
        121, 122, 123 -> ManeuverType.TOLLGATE
        87 -> ManeuverType.WAYPOINT
        88 -> ManeuverType.DESTINATION
        else -> ManeuverType.OTHER
    }

    /**
     * 응답 전체(Map, MiniJson.parse 결과)에서 지정 option 의 첫 경로를 꺼내 Route 로 만든다.
     * code != 0 이거나 경로가 없으면 null.
     * @param option 요청에 쓴 option 값. null 이면 route 아래 첫 번째 키를 쓴다.
     */
    fun fromResponse(root: Any?, option: String? = null): Route? {
        val m = root as? Map<*, *> ?: return null
        if (MiniJson.int(m["code"], -1) != 0) return null
        val routeMap = m["route"] as? Map<*, *> ?: return null
        val list = (if (option != null) routeMap[option] else routeMap.values.firstOrNull()) as? List<*> ?: return null
        val first = list.firstOrNull() as? Map<*, *> ?: return null
        return fromRouteObject(first)
    }

    /** route.{option}[i] 객체 하나를 Route 로 만든다. */
    fun fromRouteObject(r: Map<*, *>): Route {
        // path 원소는 [경도, 위도]. 뒤집는 곳은 NaverDirectionsApi.fromPathPair 한 곳뿐이다.
        val points = (r["path"] as? List<*>).orEmpty().mapNotNull { p -> NaverDirectionsApi.fromPathPair(p as? List<*>) }
        val guides = (r["guide"] as? List<*>).orEmpty().mapNotNull { g ->
            val gm = g as? Map<*, *> ?: return@mapNotNull null
            val idx = MiniJson.int(gm["pointIndex"], -1)
            val pt = points.getOrNull(idx)
            val code = MiniJson.int(gm["type"], -1)
            Guide(
                type = maneuver(code),
                lat = pt?.lat ?: 0.0, lon = pt?.lon ?: 0.0,
                distanceM = MiniJson.int(gm["distance"]),
                pointIndex = idx, rawCode = code,
                instructions = gm["instructions"] as? String ?: "",
            )
        }
        val sections = (r["section"] as? List<*>).orEmpty().mapNotNull { s ->
            val sm = s as? Map<*, *> ?: return@mapNotNull null
            Section(
                congestion = MiniJson.int(sm["congestion"]).coerceIn(0, 3),
                avgSpeedKmh = MiniJson.double(sm["speed"]),
                distanceM = MiniJson.int(sm["distance"]),
                name = sm["name"] as? String ?: "",
                pointIndex = MiniJson.int(sm["pointIndex"], -1),
                pointCount = MiniJson.int(sm["pointCount"]),
            )
        }
        val summary = r["summary"] as? Map<*, *>
        return Route(
            points = points, guides = guides, sections = sections,
            totalDistanceM = MiniJson.int(summary?.get("distance")),
            totalDurationMs = MiniJson.long(summary?.get("duration")),
        )
    }

    /** JSON 문자열을 바로 Route 로. 파싱 실패 시 null. */
    fun fromJson(json: String, option: String? = null): Route? =
        try { fromResponse(MiniJson.parse(json), option) } catch (e: IllegalArgumentException) { null }
}

/**
 * TMAP 자동차 경로안내 turnType → ManeuverType. 응답 파싱(GeoJSON)은 app 어댑터 몫이며 여기서는 코드 매핑만 둔다.
 * TMAP 에는 비보호 좌회전·본선 합류 코드가 없어 조건 기반 코스 생성에는 네이버를 우선한다.
 */
object TmapDirectionsMapper {
    /** turnType 정수 코드 → ManeuverType. */
    fun maneuver(code: Int): ManeuverType = when (code) {
        11, 51, 119, 120, 121, 122, 142, 233 -> ManeuverType.STRAIGHT
        12, 16, 17, 44, 75, 76, 118, 137, 138, 139, 140, 141 -> ManeuverType.LEFT
        13, 18, 19, 43, 73, 74, 117, 131, 132, 133, 134, 135 -> ManeuverType.RIGHT
        14, 15, 136 -> ManeuverType.UTURN
        71, 72 -> ManeuverType.ROUNDABOUT
        101, 102, 103, 111, 112, 113 -> ManeuverType.HIGHWAY_ENTER
        104, 105, 106, 114, 115, 116 -> ManeuverType.HIGHWAY_EXIT
        in 184..189 -> ManeuverType.WAYPOINT
        182, 183, 201, 203 -> ManeuverType.DESTINATION
        else -> ManeuverType.OTHER
    }
}
