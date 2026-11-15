// 담당: J → T (코스 자동 생성 — 네이버 Directions 어댑터. T 가 11/3 완성: 일일 호출 상한, 실패 이유, 진단 줄, 엔드포인트 상수.)
package kr.drivecoach.app.navi

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kr.drivecoach.app.BuildConfig
import kr.drivecoach.core.course.LatLon
import kr.drivecoach.core.course.NaverDirectionsApi
import kr.drivecoach.core.course.NaverDirectionsMapper
import kr.drivecoach.core.course.Route
import kr.drivecoach.core.course.RouteProvider
import kr.drivecoach.core.course.RouteProviderException
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URI
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * 하루 호출 상한. [NaverDirectionsProvider] 가 호출 전에 [tryConsume] 로 1회를 소비하고, 거부되면 "일일 호출 상한" 으로 즉시 실패한다.
 * 상한은 네이버 무료 구간(Directions 15 는 유료, Directions 5 는 월 60,000회 무료)과 무관한 **우리 쪽 안전장치** 다.
 */
interface DailyCallBudget {
    /** 하루 상한. */
    val limit: Int
    /** 오늘 지금까지 쓴 횟수. */
    val usedToday: Int
    /** 1회를 소비한다. 상한에 닿았으면 false(소비하지 않음). */
    fun tryConsume(): Boolean

    /** 상한 없음(테스트·개발용). */
    object Unlimited : DailyCallBudget {
        override val limit: Int get() = Int.MAX_VALUE
        override val usedToday: Int get() = 0
        override fun tryConsume(): Boolean = true
    }
}

/**
 * SharedPreferences 에 "날짜 + 횟수" 를 저장하는 일일 상한. 날짜(yyyyMMdd, 기기 로컬)가 바뀌면 0 부터 다시 센다.
 * 앱을 다시 설치하면 초기화된다(의도: 개발 중 실수로 하루치를 다 쓰는 것만 막으면 된다).
 */
class PrefsDailyCallBudget(context: Context, override val limit: Int = NaverDirectionsProvider.DAILY_CALL_CAP) : DailyCallBudget {
    private val prefs: SharedPreferences = context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    override val usedToday: Int
        get() = synchronized(this) { if (prefs.getString(KEY_DATE, "") == today()) prefs.getInt(KEY_COUNT, 0) else 0 }

    override fun tryConsume(): Boolean = synchronized(this) {
        val today = today()
        val used = if (prefs.getString(KEY_DATE, "") == today) prefs.getInt(KEY_COUNT, 0) else 0
        if (used >= limit) return false
        prefs.edit().putString(KEY_DATE, today).putInt(KEY_COUNT, used + 1).apply()
        true
    }

    private fun today(): String = SimpleDateFormat("yyyyMMdd", Locale.US).format(Date())

    companion object {
        const val PREFS = "naver_directions_budget"
        const val KEY_DATE = "date"
        const val KEY_COUNT = "count"
    }
}

/**
 * 네이버 클라우드 Maps Directions (자동차 길찾기) 를 HttpURLConnection 으로 호출해 core 의 Route 로 바꾸는 어댑터.
 * 요청 URL·좌표 순서(경도,위도)·오류 문구는 core 의 [NaverDirectionsApi](단위 테스트 있음), 응답 파싱은 [NaverDirectionsMapper](MiniJson).
 *
 * - **동기 호출**이므로 반드시 백그라운드(CoachController 는 Dispatchers.IO)에서 부른다. 메인이면 NetworkOnMainThreadException.
 * - 연결 5 s / 읽기 10 s 타임아웃.
 * - 실패 처리(RouteProvider 계약):
 *     · HTTP != 2xx → null, [lastFailure] = "HTTP {code}: {본문의 message}". 401/403/429 는 더 불러도 소용없으니 [RouteProviderException].
 *     · 응답 code != 0(출발지가 도로 밖 등) → null, lastFailure = "응답 code N: …" (생성기는 다음 후보로).
 *     · IOException(타임아웃·DNS·연결 거부) → null, lastFailure = "네트워크: …".
 *     · 일일 상한([DailyCallBudget]) 초과 → 호출 없이 [RouteProviderException]("일일 호출 상한 (N회)").
 * - [diag] 에 "OK/FAIL …" 진단 줄(최근 30개)을 남긴다(PleosNaviAdapter 와 같은 방식). 화면 "내비 진단" 에 같이 보인다.
 *
 * @param keyId X-NCP-APIGW-API-KEY-ID (네이버 클라우드 콘솔 Application 의 Client ID)
 * @param key   X-NCP-APIGW-API-KEY (Client Secret)
 * @param endpoint 기본 Directions 15([NaverDirectionsApi.ENDPOINT_DIRECTIONS_15]). Directions 5 로 바꾸려면 [NaverDirectionsApi.ENDPOINT_DIRECTIONS_5].
 */
class NaverDirectionsProvider(
    private val keyId: String,
    private val key: String,
    private val endpoint: String = NaverDirectionsApi.endpoint,
    private val budget: DailyCallBudget = DailyCallBudget.Unlimited,
    private val connectTimeoutMs: Int = CONNECT_TIMEOUT_MS,
    private val readTimeoutMs: Int = READ_TIMEOUT_MS,
    private val cartype: Int = 1,
) : RouteProvider {

    override var lastFailure: String? = null
        private set

    private val _diag = MutableStateFlow<List<String>>(emptyList())
    /** 진단 줄(최근 30개). "OK GET 200 …" / "FAIL HTTP 400: …" / "FAIL 네트워크: …" / "FAIL 일일 호출 상한 …". */
    val diag: StateFlow<List<String>> = _diag.asStateFlow()

    /** 이 프로세스에서 지금까지 실제로 보낸 HTTP 요청 수(진단용). */
    @Volatile var requestCount: Int = 0
        private set

    override fun route(start: LatLon, waypoints: List<LatLon>, goal: LatLon, option: String): Route? {
        val maxWp = NaverDirectionsApi.maxWaypoints(endpoint)
        if (waypoints.size > maxWp) {
            // 생성기 후보는 최대 3개라 여기 오지 않지만, 계약 위반이면 다음 후보도 같은 모양이므로 중단이 맞다.
            throw RouteProviderException("경유지 ${waypoints.size}개 > 상한 $maxWp ($endpoint)")
        }
        if (!budget.tryConsume()) {
            val reason = "일일 호출 상한 (${budget.limit}회)"
            diagAdd("FAIL $reason: 오늘 ${budget.usedToday}회 사용")
            lastFailure = reason
            throw RouteProviderException(reason)
        }
        val url = NaverDirectionsApi.buildUrl(start, waypoints, goal, option, endpoint = endpoint, cartype = cartype)
        requestCount++
        val response = get(url)
        return when (response) {
            is HttpResult.Ok -> {
                val route = NaverDirectionsMapper.fromJson(response.body, option)
                if (route == null) {
                    val why = NaverDirectionsApi.failureFromBody(response.body) ?: "경로 없음"
                    lastFailure = why
                    diagAdd("FAIL GET 200 $why (오늘 ${budget.usedToday}/${budgetLimitText()})")
                    Log.w(TAG, "경로 없음: $why / ${response.body.take(200)}")
                    null
                } else {
                    lastFailure = null
                    diagAdd("OK GET 200 ${route.totalDistanceM}m guide ${route.guides.size} path ${route.points.size} (오늘 ${budget.usedToday}/${budgetLimitText()})")
                    route
                }
            }
            is HttpResult.HttpError -> {
                val detail = NaverDirectionsApi.failureFromBody(response.body)
                val reason = "HTTP ${response.code}" + (detail?.let { ": $it" } ?: "")
                lastFailure = reason
                // (11/15 보안 점검) 좌표는 진단 줄·logcat 어디에도 남기지 않는다(예전엔 출발·도착 좌표를 함께 남겼음)
                diagAdd("FAIL $reason")
                Log.w(TAG, "$reason / ${response.body?.take(200)}")
                if (NaverDirectionsApi.isFatalHttp(response.code)) throw RouteProviderException(reason)
                null
            }
            is HttpResult.Network -> {
                val reason = "네트워크: ${response.error.javaClass.simpleName} ${response.error.message ?: ""}".trim()
                lastFailure = reason
                diagAdd("FAIL $reason")
                Log.w(TAG, "Directions 호출 실패: $reason")
                null
            }
        }
    }

    /** 요청 URL 조립(로그·테스트용). 키는 헤더로 가므로 URL 에는 없다. */
    fun buildUrl(start: LatLon, waypoints: List<LatLon>, goal: LatLon, option: String): String =
        NaverDirectionsApi.buildUrl(start, waypoints, goal, option, endpoint = endpoint, cartype = cartype)

    private sealed class HttpResult {
        class Ok(val body: String) : HttpResult()
        class HttpError(val code: Int, val body: String?) : HttpResult()
        class Network(val error: Exception) : HttpResult()
    }

    private fun get(url: String): HttpResult {
        var conn: HttpURLConnection? = null
        return try {
            conn = (URI(url).toURL().openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = connectTimeoutMs
                readTimeout = readTimeoutMs
                useCaches = false
                setRequestProperty(NaverDirectionsApi.HEADER_KEY_ID, keyId)
                setRequestProperty(NaverDirectionsApi.HEADER_KEY, key)
                setRequestProperty("Accept", "application/json")
            }
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }
            if (code in 200..299) HttpResult.Ok(text.orEmpty()) else HttpResult.HttpError(code, text)
        } catch (e: IOException) {
            HttpResult.Network(e)
        } catch (e: SecurityException) {
            // INTERNET 권한 누락 등
            HttpResult.Network(e)
        } finally {
            conn?.disconnect()
        }
    }

    private fun budgetLimitText(): String = if (budget.limit == Int.MAX_VALUE) "∞" else budget.limit.toString()

    private fun diagAdd(line: String) {
        Log.i(TAG, "DIAG $line")
        _diag.value = (_diag.value + line).takeLast(30)
    }

    companion object {
        private const val TAG = "NaverDirections"
        /** 하루 호출 상한(우리 쪽 안전장치). 넘으면 "일일 호출 상한" 으로 즉시 실패. 사용자 지정 100. */
        const val DAILY_CALL_CAP = 100
        /** 연결 타임아웃(ms). */
        const val CONNECT_TIMEOUT_MS = 5_000
        /** 읽기 타임아웃(ms). */
        const val READ_TIMEOUT_MS = 10_000
        /** 기본 엔드포인트(Directions 15). Directions 5 로 바꾸려면 core 의 NaverDirectionsApi.endpoint 를 바꾼다. */
        const val ENDPOINT = NaverDirectionsApi.ENDPOINT_DIRECTIONS_15
        /** 기본 엔드포인트의 경유지 상한. */
        const val MAX_WAYPOINTS = NaverDirectionsApi.MAX_WAYPOINTS_15

        /** BuildConfig 에 키가 둘 다 있는지(코스 소스를 고를 때 쓴다). */
        fun hasKeys(): Boolean = BuildConfig.NCP_KEY_ID.isNotBlank() && BuildConfig.NCP_KEY.isNotBlank()

        /**
         * BuildConfig(NCP_KEY_ID / NCP_KEY, 루트 local.properties 에서 주입) 로 공급자를 만든다.
         * 키가 비어 있으면 null → 호출측(CoachService)은 OffsetCourseProvider 로 대체한다.
         * @param context 일일 상한 카운터(SharedPreferences)용. null 이면 상한 없음(테스트).
         */
        fun fromBuildConfig(context: Context?): NaverDirectionsProvider? {
            if (!hasKeys()) {
                Log.i(TAG, "NCP_KEY_ID/NCP_KEY 가 local.properties 에 없음 — 코스 자동 생성은 오프셋 코스로 대체")
                return null
            }
            val budget: DailyCallBudget = if (context != null) PrefsDailyCallBudget(context) else DailyCallBudget.Unlimited
            return NaverDirectionsProvider(BuildConfig.NCP_KEY_ID, BuildConfig.NCP_KEY, budget = budget)
        }
    }
}
