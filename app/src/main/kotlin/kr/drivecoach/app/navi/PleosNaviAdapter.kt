// 담당: S
// 검증: NaviHelper-3.0.5 / navi-data-3.0.4 jar + Android 스텁으로 kotlinc 타입 검사. 콜백이 실제로 오는지는 에뮬레이터에서 확인할 것.
package kr.drivecoach.app.navi

import ai.pleos.playground.navi.constants.NaviErrorCode
import ai.pleos.playground.navi.constants.NavigationStatus
import ai.pleos.playground.navi.constants.TurnType
import ai.pleos.playground.navi.constants.WaypointIndex
import ai.pleos.playground.navi.data.CurrentLocationInfo
import ai.pleos.playground.navi.data.DestinationArrivedInfo
import ai.pleos.playground.navi.data.RequestWaypointInfo
import ai.pleos.playground.navi.data.RouteInfo
import ai.pleos.playground.navi.data.RouteStartInfo
import ai.pleos.playground.navi.data.RouteStateInfo
import ai.pleos.playground.navi.data.RouteWithWaypointsRequest
import ai.pleos.playground.navi.data.TBTInfo
import ai.pleos.playground.navi.helper.NaviHelper
import ai.pleos.playground.navi.helper.listener.NaviHelperEventListener
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kr.drivecoach.core.course.LatLon
import kr.drivecoach.core.course.ManeuverType

/** 내비가 알려 준 현재 위치. heading 은 북=0 시계방향(도). */
data class NaviLocation(val lat: Double, val lon: Double, val headingDeg: Double?, val address: String?)

/**
 * 다음 회전(TBT) 하나.
 * @param maneuver 공급자 독립 회전 유형(Pleos TurnType 을 접은 것).
 * @param rawType Pleos TurnType 이름(진단용).
 * @param distanceM 회전 지점까지 남은 거리(m).
 * @param description 내비가 준 안내 문구(도로명 포함, 없으면 빈 문자열).
 */
data class NaviTbt(val maneuver: ManeuverType, val rawType: String, val distanceM: Double, val description: String) {
    /** TbtGuide 가 "같은 회전" 을 구분하는 키. */
    val key: String get() = "$rawType|$description"
}

/** 경로 시작 정보(onRouteStarted). distanceM·durationS 는 내비 단위 그대로(m, 초로 추정). */
data class NaviRouteStart(val distanceM: Int, val durationS: Int, val name: String)

/** 도착 정보(onDestinationArrived). */
data class NaviArrival(val distanceM: Int, val durationS: Int, val name: String, val address: String)

/**
 * 어댑터 → CoachController 이벤트. 전부 **메인 스레드** 에서 불린다.
 * NaviHelper 가 어떤 이벤트를 주는지는 "내비 앱 구현에 따라 다름"(문서) 이라, 받은 것을 그대로 넘기고 해석은 컨트롤러가 한다.
 */
interface NaviPortListener {
    /** 경로 탐색이 끝나고 안내가 시작됨. */
    fun onRouteStarted(info: NaviRouteStart) {}
    /** 경로 계산 완료(onRouteStarted 가 안 오는 내비 앱 대비). */
    fun onRoutingCompleted() {}
    /** 경로 취소(사용자가 내비에서 취소했거나 우리가 cancelRoute). */
    fun onRouteCancelled() {}
    /** 다음 회전 갱신. */
    fun onTbt(tbt: NaviTbt) {}
    /** 현재 도로 제한속도(km/h). 0 이하는 "모름". */
    fun onRoadSpeedLimit(kmh: Int) {}
    /** 목적지 도착. */
    fun onDestinationArrived(info: NaviArrival) {}
    /** 내비 오류(경로 탐색 실패 등). */
    fun onNaviError(code: String) {}
    /** 현재 위치 갱신. */
    fun onLocation(loc: NaviLocation) {}
}

/**
 * CoachController 가 보는 내비 접점. 실제 구현은 [PleosNaviAdapter], 테스트는 가짜로 대체한다.
 */
interface NaviPort {
    /** NaviHelper 초기화 성공 여부. */
    val naviReady: StateFlow<Boolean>
    val currentLocation: StateFlow<NaviLocation?>
    /** 경로 안내 중인지(onRouteStarted ~ 취소/도착). */
    val routeActive: StateFlow<Boolean>
    val lastTbt: StateFlow<NaviTbt?>
    val roadSpeedLimit: StateFlow<Int?>
    /** NavigationStatus 이름(INACTIVE/INITIALIZING/FREE_DRIVE/ROUTE_DRIVE). 모르면 null. */
    val navigationStatus: StateFlow<String?>
    /** 마지막 도착 이벤트. 새 경로를 보내면 null 로 돌아간다. */
    val destinationArrived: StateFlow<NaviArrival?>
    /** 마지막 오류 코드 이름. */
    val lastError: StateFlow<String?>
    /** 진단 줄("OK …"/"FAIL …"/"EVT …"). */
    val diag: StateFlow<List<String>>

    fun setListener(listener: NaviPortListener?)
    /**
     * 코스를 차량 내비에 넣는다. 경유지가 없으면 requestRoute, 있으면 requestRouteWithWaypoints(≤2).
     * @return SDK 호출 자체가 성공했는지(결과는 리스너로 온다).
     */
    fun requestCourse(destination: LatLon, destinationLabel: String, waypoints: List<LatLon>): Boolean
    fun cancelRoute(): Boolean
    /** 현재 위치·경로 상태·TBT 를 다시 요청한다(결과는 리스너/StateFlow). */
    fun refresh()
}

/**
 * Pleos NaviHelper-3.0.5 어댑터.
 *
 * - `NaviHelper(context)` → `initialize(onSuccess, onFailure)` → `addListener(listener)`; 끝나면 `removeListener` + `release()`.
 * - 요청은 전부 반환값 없이 리스너로 결과가 온다. 모든 SDK 호출은 try/catch(Throwable) 로 감싸 권한(NAVI_ROUTE / NAVI_ROUTE_SEARCH)·미설치 내비에서 죽지 않게 한다.
 * - 리스너 콜백은 어느 스레드에서 올지 보장이 없으므로 메인 Handler 로 넘긴 뒤 StateFlow 갱신 + [NaviPortListener] 호출.
 * - 좌표: RouteInfo / RequestWaypointInfo 는 **latitude, longitude 순**(javap 확인). 별도의 Coordinate 클래스는 없다.
 * - 경유지 상한 2 (WaypointIndex.FIRST / SECOND). 3개 이상이면 앞 2개만 쓴다 — 호출측(NaviHandoff)이 이미 2개로 솎아 보낸다.
 */
class PleosNaviAdapter(context: Context) : NaviPort {
    private val appContext: Context = context.applicationContext
    private val main = Handler(Looper.getMainLooper())
    private var helper: NaviHelper? = null
    private var listener: NaviPortListener? = null
    private var started = false

    private val _naviReady = MutableStateFlow(false)
    override val naviReady: StateFlow<Boolean> = _naviReady.asStateFlow()
    private val _currentLocation = MutableStateFlow<NaviLocation?>(null)
    override val currentLocation: StateFlow<NaviLocation?> = _currentLocation.asStateFlow()
    private val _routeActive = MutableStateFlow(false)
    override val routeActive: StateFlow<Boolean> = _routeActive.asStateFlow()
    private val _lastTbt = MutableStateFlow<NaviTbt?>(null)
    override val lastTbt: StateFlow<NaviTbt?> = _lastTbt.asStateFlow()
    private val _roadSpeedLimit = MutableStateFlow<Int?>(null)
    override val roadSpeedLimit: StateFlow<Int?> = _roadSpeedLimit.asStateFlow()
    private val _navigationStatus = MutableStateFlow<String?>(null)
    override val navigationStatus: StateFlow<String?> = _navigationStatus.asStateFlow()
    private val _destinationArrived = MutableStateFlow<NaviArrival?>(null)
    override val destinationArrived: StateFlow<NaviArrival?> = _destinationArrived.asStateFlow()
    private val _lastError = MutableStateFlow<String?>(null)
    override val lastError: StateFlow<String?> = _lastError.asStateFlow()
    private val _diag = MutableStateFlow<List<String>>(emptyList())
    override val diag: StateFlow<List<String>> = _diag.asStateFlow()

    private fun diagAdd(line: String) {
        _diag.value = (_diag.value + line).takeLast(30)
        Log.i(TAG, "DIAG $line")
    }

    override fun setListener(listener: NaviPortListener?) { this.listener = listener }

    /** NaviHelper 생성·초기화·리스너 등록. 중복 호출 무시. 실패해도 예외를 밖으로 내지 않는다(진단 줄에 남김). */
    fun start() {
        if (started) return
        started = true
        safely("NaviHelper.initialize") {
            val h = NaviHelper(appContext)
            helper = h
            // javap: initialize(onSuccess: () -> Unit, onFailure: (Throwable) -> Unit). 동기 예외도 같이 잡는다.
            h.initialize(
                {
                    main.post {
                        _naviReady.value = true
                        diagAdd("OK NaviHelper.initialize.onSuccess")
                        safely("NaviHelper.addListener") { h.addListener(eventListener) }
                        refresh()
                    }
                },
                { t ->
                    main.post {
                        _naviReady.value = false
                        diagAdd("FAIL NaviHelper.initialize.onFailure: ${t.javaClass.simpleName} ${t.message}")
                    }
                },
            )
        }
    }

    /** 리스너 해제 + release. CoachService.onDestroy. */
    fun stop() {
        if (!started) return
        started = false
        helper?.let { h ->
            safely("NaviHelper.removeListener") { h.removeListener(eventListener) }
            safely("NaviHelper.release") { h.release() }
        }
        helper = null
        _naviReady.value = false
        _routeActive.value = false
    }

    override fun requestCourse(destination: LatLon, destinationLabel: String, waypoints: List<LatLon>): Boolean {
        val h = helper ?: run { diagAdd("FAIL requestCourse: NaviHelper 없음"); return false }
        if (!_naviReady.value) { diagAdd("FAIL requestCourse: 초기화 전"); return false }
        _destinationArrived.value = null
        _lastError.value = null
        _lastTbt.value = null
        // RouteInfo(latitude, longitude, …) — 위도 먼저(javap). poiId/address/poiSubId 는 필수 String 이라 빈 값으로 채운다.
        val dest = RouteInfo(
            latitude = destination.lat, longitude = destination.lon,
            poiName = destinationLabel, poiId = "", address = "", poiSubId = "0",
        )
        val wps = waypoints.take(MAX_WAYPOINTS).mapIndexed { i, w ->
            RequestWaypointInfo(
                latitude = w.lat, longitude = w.lon,
                poiName = "경유지 ${i + 1}",
                waypointIndex = if (i == 0) WaypointIndex.FIRST else WaypointIndex.SECOND,
                poiId = "", address = "", poiSubId = "0",
            )
        }
        return if (wps.isEmpty()) {
            safely("NaviHelper.requestRoute(목적지)") { h.requestRoute(dest) }
        } else {
            safely("NaviHelper.requestRouteWithWaypoints(경유지 ${wps.size}개)") {
                h.requestRouteWithWaypoints(RouteWithWaypointsRequest(destination = dest, waypoints = wps))
            }
        }
    }

    override fun cancelRoute(): Boolean {
        val h = helper ?: return false
        val ok = safely("NaviHelper.cancelRoute") { h.cancelRoute() }
        if (ok) _routeActive.value = false
        return ok
    }

    override fun refresh() {
        val h = helper ?: return
        if (!_naviReady.value) return
        safely("NaviHelper.getCurrentLocationInfo") { h.getCurrentLocationInfo() }
        safely("NaviHelper.getRouteStateInfo") { h.getRouteStateInfo() }
        safely("NaviHelper.getTBTInfo") { h.getTBTInfo() }
    }

    // ───────── NaviHelper 리스너 (javap 확인한 default 메서드만 override) ─────────
    private val eventListener = object : NaviHelperEventListener {
        override fun onRouteStarted(routeStartInfo: RouteStartInfo) { main.post {
            val info = NaviRouteStart(routeStartInfo.distance, routeStartInfo.duration, routeStartInfo.name)
            _routeActive.value = true
            diagAdd("EVT onRouteStarted ${info.distanceM}m ${info.durationS}s ${info.name}")
            listener?.onRouteStarted(info)
        } }

        override fun onRoutingCompleted() { main.post {
            diagAdd("EVT onRoutingCompleted")
            listener?.onRoutingCompleted()
        } }

        override fun onRouteCancelled() { main.post {
            _routeActive.value = false
            _lastTbt.value = null
            diagAdd("EVT onRouteCancelled")
            listener?.onRouteCancelled()
        } }

        override fun onRouteEnded() { main.post {
            _routeActive.value = false
            diagAdd("EVT onRouteEnded")
        } }

        override fun onDestinationArrived(destinationArrivedInfo: DestinationArrivedInfo) { main.post {
            val a = NaviArrival(destinationArrivedInfo.distance, destinationArrivedInfo.duration, destinationArrivedInfo.name, destinationArrivedInfo.address)
            _routeActive.value = false
            _destinationArrived.value = a
            diagAdd("EVT onDestinationArrived ${a.name}")
            listener?.onDestinationArrived(a)
        } }

        override fun onError(errorCode: NaviErrorCode) { main.post {
            _lastError.value = errorCode.name
            diagAdd("EVT onError ${errorCode.name}")
            listener?.onNaviError(errorCode.name)
        } }

        override fun onCurrentLocationInfo(currentLocationInfo: CurrentLocationInfo) { main.post {
            val loc = NaviLocation(currentLocationInfo.latitude, currentLocationInfo.longitude, currentLocationInfo.heading, currentLocationInfo.address)
            _currentLocation.value = loc
            listener?.onLocation(loc)
        } }

        override fun onRouteStateInfo(routeStateInfo: RouteStateInfo) { main.post {
            diagAdd("EVT onRouteStateInfo mode=${routeStateInfo.naviMode.name} routing=${routeStateInfo.isRouting} opt=${routeStateInfo.routeOption.name}")
        } }

        override fun onCurrentRoadSpeedLimit(speedLimit: Int) { main.post {
            val v = if (speedLimit > 0) speedLimit else null
            if (_roadSpeedLimit.value != v) diagAdd("EVT onCurrentRoadSpeedLimit $speedLimit")
            _roadSpeedLimit.value = v
            listener?.onRoadSpeedLimit(speedLimit)
        } }

        override fun onNavigationStatus(navigationStatus: NavigationStatus) { main.post {
            _navigationStatus.value = navigationStatus.name
            diagAdd("EVT onNavigationStatus ${navigationStatus.name}")
            if (navigationStatus == NavigationStatus.ROUTE_DRIVE) _routeActive.value = true
        } }

        override fun onTBTInfo(tbtInfoList: List<TBTInfo>) { main.post {
            val first = tbtInfoList.firstOrNull() ?: return@post
            val tbt = NaviTbt(maneuverOf(first.type), first.type.name, first.distance, first.description)
            _lastTbt.value = tbt
            listener?.onTbt(tbt)
        } }
    }

    /** SDK 호출 하나를 감싼다. 성공/실패를 진단 줄에 남기고, 예외는 절대 밖으로 내지 않는다. */
    private inline fun safely(what: String, block: () -> Unit): Boolean =
        try { block(); diagAdd("OK $what"); true } catch (e: Throwable) { diagAdd("FAIL $what: ${e.javaClass.simpleName} ${e.message}"); Log.w(TAG, "$what failed: ${e.message}"); false }


    companion object {
        private const val TAG = "PleosNaviAdapter"
        /** WaypointIndex 가 FIRST/SECOND 뿐이므로 2. */
        const val MAX_WAYPOINTS = 2

        /** Pleos TurnType(45종) → core ManeuverType. 안내 멘트가 있는 것만 구분하고 나머지는 OTHER. KEEP_R/L(차로 유지)은 회전이 아니므로 OTHER. */
        fun maneuverOf(t: TurnType): ManeuverType = when (t) {
            TurnType.TURN_STRAIGHT -> ManeuverType.STRAIGHT
            TurnType.TURN_RIGHT, TurnType.TURN_SHARP_R -> ManeuverType.RIGHT
            TurnType.TURN_LEFT, TurnType.TURN_SHARP_L -> ManeuverType.LEFT
            TurnType.TURN_UTURN -> ManeuverType.UTURN
            TurnType.TURN_ENTER_ROUNDABOUT_02, TurnType.TURN_ENTER_ROUNDABOUT_03, TurnType.TURN_ENTER_ROUNDABOUT_04,
            TurnType.TURN_ENTER_ROUNDABOUT_06, TurnType.TURN_ENTER_ROUNDABOUT_08, TurnType.TURN_ENTER_ROUNDABOUT_09,
            TurnType.TURN_ENTER_ROUNDABOUT_10, TurnType.TURN_ENTER_ROUNDABOUT_12, TurnType.TURN_EXIT_ROUNDABOUT -> ManeuverType.ROUNDABOUT
            TurnType.TURN_ENTER_IC_R, TurnType.TURN_ENTER_IC_L, TurnType.TURN_ENTER_IC_C -> ManeuverType.HIGHWAY_ENTER
            TurnType.TURN_EXIT_IC_R, TurnType.TURN_EXIT_IC_L, TurnType.TURN_EXIT_IC_C -> ManeuverType.HIGHWAY_EXIT
            TurnType.TURN_DEVIDE_MERGE -> ManeuverType.MERGE
            TurnType.TURN_TOLL -> ManeuverType.TOLLGATE
            TurnType.TURN_WAY -> ManeuverType.WAYPOINT
            TurnType.TURN_ARRIVE -> ManeuverType.DESTINATION
            else -> ManeuverType.OTHER
        }
    }
}
