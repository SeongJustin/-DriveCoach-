// 실제 Directions API 연결: 요청 URL·좌표 순서 변환·오류 문구. 순수 Kotlin 이라 여기서 단위 테스트한다. HTTP 자체는 app/navi/NaverDirectionsProvider
package kr.drivecoach.core.course

import java.net.URLEncoder
import java.util.Locale

/**
 * 네이버 클라우드 Maps Directions 요청을 조립하는 순수 함수 모음.
 *
 * 좌표 순서(중요): 네이버는 요청 쿼리·응답 path 모두 경도,위도(lon,lat)이고,
 * 우리 모델 [LatLon] 과 Pleos NaviHelper 의 data class 는 위도 먼저다. 바뀌는 지점은 이 객체의 [toQuery] / [fromPathPair] 두 곳뿐이다.
 */
object NaverDirectionsApi {
    /** Directions 15 (경유지 최대 15개). 사용자의 네이버 Application 이 이 API 로 등록돼 있어 기본값. */
    const val ENDPOINT_DIRECTIONS_15 = "https://naveropenapi.apigw.ntruss.com/map-direction-15/v1/driving"
    /** Directions 5 (경유지 최대 5개, 월 60,000회 무료). Application 에 Directions 5 를 추가 등록하면 [NaverDirectionsApi.endpoint] 를 이것으로 바꾼다. */
    const val ENDPOINT_DIRECTIONS_5 = "https://naveropenapi.apigw.ntruss.com/map-direction/v1/driving"
    /** 같은 API 의 새 게이트웨이 호스트(문서 application-maps-directions15.md 기준). 구 호스트가 막히면 이쪽으로. */
    const val ENDPOINT_DIRECTIONS_15_MAPS_HOST = "https://maps.apigw.ntruss.com/map-direction-15/v1/driving"

    /** 실제로 쓰는 엔드포인트. 기본 Directions 15. */
    // 에뮬레이터 확인: 구 호스트(naveropenapi)는 HTTP 401 Permission Denied → 2025년 이후 신규 앱은 maps.apigw.ntruss.com 호스트를 쓴다.
    const val endpoint: String = ENDPOINT_DIRECTIONS_15_MAPS_HOST

    /** Directions 15 경유지 상한. */
    const val MAX_WAYPOINTS_15 = 15
    /** Directions 5 경유지 상한. */
    const val MAX_WAYPOINTS_5 = 5

    /** 인증 헤더 이름(Client ID). */
    const val HEADER_KEY_ID = "X-NCP-APIGW-API-KEY-ID"
    /** 인증 헤더 이름(Client Secret). */
    const val HEADER_KEY = "X-NCP-APIGW-API-KEY"

    /** 엔드포인트가 받는 경유지 상한. */
    fun maxWaypoints(endpoint: String): Int = if (endpoint.contains("map-direction-15")) MAX_WAYPOINTS_15 else MAX_WAYPOINTS_5

    /** [LatLon] → 네이버 쿼리 문자열 "경도,위도" (소수점 6자리 ≈ 10 cm). */
    fun toQuery(p: LatLon): String = String.format(Locale.US, "%.6f,%.6f", p.lon, p.lat)

    /** 응답 path 원소 `[경도, 위도]` → [LatLon]. 길이가 2 미만이면 null. */
    fun fromPathPair(pair: List<*>?): LatLon? {
        if (pair == null || pair.size < 2) return null
        val lon = pair[0] as? Number ?: return null
        val lat = pair[1] as? Number ?: return null
        return LatLon(lat = lat.toDouble(), lon = lon.toDouble())
    }

    /**
     * 요청 URL. 형식:
     * `{endpoint}?start=lon,lat&goal=lon,lat[&waypoints=lon,lat|lon,lat]&option={option}&cartype={cartype}&lang=ko`
     * ('|' 는 URL 인코딩되어 %7C 로 나간다.)
     */
    fun buildUrl(
        start: LatLon, waypoints: List<LatLon>, goal: LatLon, option: String,
        endpoint: String = NaverDirectionsApi.endpoint, cartype: Int = 1,
    ): String {
        require(waypoints.size <= maxWaypoints(endpoint)) { "경유지는 최대 ${maxWaypoints(endpoint)}개 ($endpoint)" }
        val sb = StringBuilder(endpoint)
        sb.append("?start=").append(enc(toQuery(start)))
        sb.append("&goal=").append(enc(toQuery(goal)))
        if (waypoints.isNotEmpty()) sb.append("&waypoints=").append(enc(waypoints.joinToString("|") { toQuery(it) }))
        sb.append("&option=").append(enc(option))
        sb.append("&cartype=").append(cartype)
        sb.append("&lang=ko")
        return sb.toString()
    }

    /** 응답 code(0 성공, 1~5 길찾기 실패) → 사람이 읽을 이유. 1-3. */
    fun responseCodeText(code: Int): String = when (code) {
        0 -> "성공"
        1 -> "출발지와 도착지가 동일"
        2 -> "출발지 또는 도착지가 도로 주변이 아님"
        3 -> "자동차 길찾기 결과 제공 불가"
        4 -> "경유지가 도로 주변이 아님"
        5 -> "경유지 포함 직선거리 합이 1,500 km 이상"
        else -> "알 수 없는 응답 code $code"
    }

    /**
     * 응답 본문(JSON)에서 실패 이유를 뽑는다. code 가 0 이면 null(실패 아님).
     * JSON 이 아니거나 code 가 없으면 본문 앞부분을 그대로 돌려준다.
     */
    fun failureFromBody(body: String?): String? {
        if (body.isNullOrBlank()) return "빈 응답"
        val root = try { MiniJson.parse(body) } catch (e: IllegalArgumentException) { return "JSON 아님: ${body.take(80)}" }
        val m = root as? Map<*, *> ?: return "객체 아님: ${body.take(80)}"
        val code = MiniJson.int(m["code"], Int.MIN_VALUE)
        if (code == 0) return null
        if (code == Int.MIN_VALUE) {
            // API Gateway 오류 본문: {"error":{"errorCode":"200","message":"Authentication Failed"}}
            val err = m["error"] as? Map<*, *>
            val msg = err?.get("message") as? String ?: m["message"] as? String
            return msg ?: "code 없음: ${body.take(80)}"
        }
        return "응답 code $code: ${responseCodeText(code)}"
    }

    /** HTTP 상태가 "더 호출해도 소용없는" 오류인지(인증 실패·쿼터 초과). 생성기는 이때 다음 후보를 시도하지 않는다. */
    fun isFatalHttp(code: Int): Boolean = code == 401 || code == 403 || code == 429

    private fun enc(s: String): String = URLEncoder.encode(s, "UTF-8")
}
