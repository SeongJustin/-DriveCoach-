// 리포트 · 모의 시험 · 성장 기록
package kr.drivecoach.core.report

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/** 주행 중 기록되는 이벤트 종류. 지도에 핀으로 찍거나 모의 시험 감점표에 대응시킨다. */
enum class TripEventType {
    /** 회전·차로 변경 전 3초 내에 깜빡이를 켰음. value = 깜빡이 후 이동까지 걸린 ms. */
    TURN_SIGNAL_OK,
    /** 회전·차로 변경(조향각 변화) 전에 깜빡이가 없었음. */
    TURN_SIGNAL_MISSED,
    /** 급제동. value = 감속도(m/s²). */
    HARD_BRAKE,
    /** 과속 구간 1회. timestamp·위치는 구간 시작점, value = 제한속도 초과량 최대(km/h). */
    OVERSPEED,
    /** 정지선 반경 안에서 속도 0에 도달함. */
    STOP_LINE_OK,
    /** 정지선 반경을 지나는 동안 한 번도 멈추지 않음. value = 반경 내 최저 속도(km/h). */
    STOP_LINE_VIOLATION,
    /** 주차브레이크가 걸린 채 주행. */
    PARKING_BRAKE_MOVING,
    /** 안전벨트 경고등이 켜진 채 주행. */
    SEATBELT_WARNING_MOVING,
    /** 주행 종료 시 기어가 P 가 아님. */
    END_GEAR_NOT_P,
    /** 주행 종료 시 주차브레이크를 걸지 않음. */
    END_PARKING_BRAKE_OFF,
    /** 코치 발화 1회(외부에서 알려줌). detail = 멘트 ID. */
    COACH_SPEAK,
}

/**
 * 주행 중 발생한 이벤트 1건. 지도 표시용으로 위치를 함께 저장한다.
 * [value] 는 종류별 수치(감속도, 초과 속도 등)로 채점·문구 선택에 쓴다.
 */
data class TripEvent(
    val type: TripEventType,
    val timestampMs: Long,
    val lat: Double?,
    val lon: Double?,
    val detail: String = "",
    val value: Double? = null,
)

/** 정지선 1개의 위치. 코스 데이터(course 모듈)에서 주입한다. */
data class StopLine(val id: String, val lat: Double, val lon: Double)

/**
 * 리포트 요약 지표. 센서로 알 수 있는 것만 담는다.
 * 비율(rate) 은 분모가 0 이면 null — "해당 상황이 없었음" 을 뜻한다.
 */
data class Metrics(
    val distanceKm: Double = 0.0,
    val durationMs: Long = 0,
    val avgSpeedKmh: Double = 0.0,
    val maxSpeedKmh: Double = 0.0,
    /** 조향각 변화로 감지한 회전·차로 변경 횟수. */
    val turnCount: Int = 0,
    /** 그중 3초 전부터 깜빡이가 켜져 있던 횟수. */
    val turnWithSignalCount: Int = 0,
    /** 깜빡이 켠 뒤 실제 이동(조향)까지 걸린 평균 ms. 깜빡이 없이 이동한 경우는 제외. */
    val avgSignalToMoveMs: Long? = null,
    val hardBrakeCount: Int = 0,
    val overspeedCount: Int = 0,
    val overspeedDurationMs: Long = 0,
    val maxOverspeedKmh: Double = 0.0,
    /** 반경 안을 지나간 정지선 수. */
    val stopLineCount: Int = 0,
    val stopLineCompliedCount: Int = 0,
    val coachSpeakCount: Int = 0,
) {
    /** 방향지시등 사용률(0~1). 회전이 없었으면 null. */
    val signalUsageRate: Double? get() = if (turnCount == 0) null else turnWithSignalCount.toDouble() / turnCount
    /** 정지선 준수율(0~1). 정지선을 지나지 않았으면 null. */
    val stopLineComplianceRate: Double? get() = if (stopLineCount == 0) null else stopLineCompliedCount.toDouble() / stopLineCount
    /** 10km 당 급제동 횟수. 거리가 거의 0 이면 횟수 그대로. */
    val hardBrakePer10Km: Double get() = if (distanceKm < 0.1) hardBrakeCount.toDouble() else hardBrakeCount / distanceKm * 10.0
    /** 전체 주행 시간 중 과속 시간 비율(0~1). */
    val overspeedTimeRatio: Double get() = if (durationMs <= 0) 0.0 else overspeedDurationMs.toDouble() / durationMs
}

/**
 * 기록기 설정. 임계값은 하드코딩하지 않고 주입한다(급제동 감속도는 safety 모듈과 같은 상수를 쓴다).
 */
data class TripRecorderConfig(
    /** 급제동 판정 감속도(m/s²). safety 모듈의 상수를 그대로 넘긴다. */
    val hardBrakeDecelMps2: Double,
    /** 이 속도(km/h) 아래에서 시작한 감속은 급제동으로 세지 않는다(safety 의 hardBrakeMinSpeedKmh 와 같은 값). */
    val hardBrakeMinSpeedKmh: Double = 10.0,
    /** 감속도는 이 시간(ms) 이상 떨어진 속도 값끼리만 계산한다. 다른 신호가 바뀌어 상태가 몇 ms 만에 다시 오면 dt 가 작아져 감속도가 부풀던 것. */
    val hardBrakeMinWindowMs: Long = 200,
    /** 코스 제한속도(km/h). 모르면 null — 과속 기록을 하지 않는다(기본 50 은 시연 빌드에만). */
    val speedLimitKmh: Double?,
    /** 코스 상의 정지선 좌표. */
    val stopLines: List<StopLine> = emptyList(),
    /** 정지선 판정 반경(m). GPS 오차를 감안해 넉넉히. */
    val stopLineRadiusM: Double = 15.0,
    /** 이 조향각(절댓값, 도) 을 넘어서는 순간을 회전·차로 변경 이벤트로 본다. */
    val turnSteeringDeg: Double = 45.0,
    /** 회전 이벤트 전 이 시간 안에 깜빡이가 켜져 있었으면 "깜빡이 사용" 으로 본다. */
    val signalLookbackMs: Long = 3_000,
    /** 회전 이벤트 재감지 금지 간격. 한 번의 회전에서 핸들이 흔들려도 1회로 센다. */
    val turnCooldownMs: Long = 4_000,
    /** 급제동 재감지 금지 간격. */
    val hardBrakeCooldownMs: Long = 2_000,
    /** 과속 허용 오차(km/h). 계기판·GPS 오차 흡수용. */
    val overspeedToleranceKmh: Double = 2.0,
    /** 이 속도 이하는 "정지" 로 본다(km/h). */
    val stoppedSpeedKmh: Double = 0.5,
    /** 이 속도 이상이면 "주행 중" 으로 본다(km/h). 주차브레이크·벨트 판정용. */
    val movingSpeedKmh: Double = 5.0,
)

/**
 * 주행 중 [StateListener] 로 차량 상태를 받아 리포트 지표와 이벤트를 누적한다.
 * 센서로 판정할 수 없는 것(엑셀, 미러, 옆 차, 차선)은 절대 기록하지 않는다.
 * 사용: addListener(recorder) → 주행 → finish() 로 [Metrics] 를 얻는다.
 */
class TripRecorder(private val config: TripRecorderConfig) : StateListener {
    private val eventList = mutableListOf<TripEvent>()
    private var prev: VehicleState? = null
    private var startMs: Long? = null
    private var lastMs: Long = 0
    private var distanceM = 0.0
    private var startOdometerKm: Double? = null
    private var lastOdometerKm: Double? = null
    private var maxSpeed = 0.0

    // 방향지시등
    private val signalHistory = ArrayDeque<Pair<Long, TurnSignal>>()
    private var signalOnSinceMs: Long? = null
    private var turnCount = 0
    private var turnWithSignal = 0
    private val signalToMoveMs = mutableListOf<Long>()
    private var lastTurnMs = Long.MIN_VALUE / 2
    private var steeringOverThreshold = false

    // 급제동
    private var hardBrakeCount = 0
    private var lastHardBrakeMs = Long.MIN_VALUE / 2

    // 과속
    private var overspeedStart: VehicleState? = null
    private var overspeedMax = 0.0
    private var overspeedCount = 0
    private var overspeedDuration = 0L
    private var maxOverspeedAll = 0.0

    // 정지선: 현재 반경 안에 있는 정지선 id → (멈췄는지, 최저 속도)
    private val insideStopLine = mutableMapOf<String, StopLineVisit>()
    private var stopLineCount = 0
    private var stopLineComplied = 0

    private var parkingBrakeMovingReported = false
    private var seatBeltMovingReported = false
    private var coachSpeakCount = 0
    private var finished = false

    private class StopLineVisit(var stopped: Boolean = false, var minSpeed: Double = Double.MAX_VALUE)

    /** 지금까지 기록된 이벤트(시간순). */
    val events: List<TripEvent> get() = eventList.toList()

    /** 코치가 멘트를 1회 발화했음을 알린다. [phraseId] 는 tts 모듈의 멘트 ID. */
    fun onCoachSpoke(phraseId: String = "") {
        coachSpeakCount++
        val p = prev
        eventList += TripEvent(TripEventType.COACH_SPEAK, p?.timestampMs ?: lastMs, p?.lat, p?.lon, phraseId)
    }

    override fun onState(state: VehicleState) {
        if (finished) return
        val p = prev
        if (startMs == null) startMs = state.timestampMs
        lastMs = state.timestampMs
        state.odometerKm?.let { if (startOdometerKm == null) startOdometerKm = it; lastOdometerKm = it }
        state.speedKmh?.let { if (it > maxSpeed) maxSpeed = it }

        if (p != null) {
            val dtMs = state.timestampMs - p.timestampMs
            if (dtMs > 0) updateDistance(p, state, dtMs)
        }
        detectHardBrake(state)
        trackTurnSignal(state)
        detectTurn(p, state)
        trackOverspeed(p, state)
        trackStopLines(state)
        checkMovingSafety(state)
        prev = state
    }

    /**
     * 주행을 끝내고 요약 지표를 돌려준다. 열려 있던 과속 구간·정지선 방문을 닫고,
     * 종료 상태(기어 P, 주차브레이크) 이벤트를 남긴다. 두 번 호출해도 같은 값을 준다.
     */
    fun finish(): Metrics {
        if (!finished) {
            finished = true
            val last = prev
            if (last != null) {
                closeOverspeed(last.timestampMs)
                insideStopLine.keys.toList().forEach { leaveStopLine(it, last) }
                if (last.gear != Gear.P && last.gear != Gear.UNKNOWN) {
                    eventList += TripEvent(TripEventType.END_GEAR_NOT_P, last.timestampMs, last.lat, last.lon, last.gear.name)
                }
                if (last.parkingBrakeOn == false) {
                    eventList += TripEvent(TripEventType.END_PARKING_BRAKE_OFF, last.timestampMs, last.lat, last.lon)
                }
            }
        }
        return metrics()
    }

    /** 현재까지의 지표(주행 중 중간 확인용). */
    fun metrics(): Metrics {
        val durationMs = (startMs?.let { lastMs - it } ?: 0L).coerceAtLeast(0)
        val odoKm = startOdometerKm?.let { s -> lastOdometerKm?.let { e -> e - s } }
        val distKm = if (odoKm != null && odoKm > 0.0) odoKm else distanceM / 1000.0
        val avg = if (durationMs > 0) distKm / (durationMs / 3_600_000.0) else 0.0
        return Metrics(
            distanceKm = distKm,
            durationMs = durationMs,
            avgSpeedKmh = avg,
            maxSpeedKmh = maxSpeed,
            turnCount = turnCount,
            turnWithSignalCount = turnWithSignal,
            avgSignalToMoveMs = if (signalToMoveMs.isEmpty()) null else signalToMoveMs.sum() / signalToMoveMs.size,
            hardBrakeCount = hardBrakeCount,
            overspeedCount = overspeedCount,
            overspeedDurationMs = overspeedDuration,
            maxOverspeedKmh = maxOverspeedAll,
            stopLineCount = stopLineCount,
            stopLineCompliedCount = stopLineComplied,
            coachSpeakCount = coachSpeakCount,
        )
    }

    // ---- 거리 ----
    private fun updateDistance(p: VehicleState, s: VehicleState, dtMs: Long) {
        val v0 = p.speedKmh ?: s.speedKmh ?: return
        val v1 = s.speedKmh ?: v0
        distanceM += (v0 + v1) / 2.0 / 3.6 * (dtMs / 1000.0)
    }

    // ---- 급제동 ----
    private var speedRefMs: Long? = null
    private var speedRefKmh: Double? = null

    private fun detectHardBrake(s: VehicleState) {
        val v1 = s.speedKmh ?: return
        val t0 = speedRefMs
        val v0 = speedRefKmh
        if (t0 == null || v0 == null || s.timestampMs < t0) { speedRefMs = s.timestampMs; speedRefKmh = v1; return }
        val dtMs = s.timestampMs - t0
        if (dtMs < config.hardBrakeMinWindowMs) return
        speedRefMs = s.timestampMs; speedRefKmh = v1
        // DangerRules 와 같은 조건: 충분한 속도에서, 브레이크를 밟은 채(모르면 허용)
        if (v0 < config.hardBrakeMinSpeedKmh || s.brakePressed == false) return
        val decel = (v0 - v1) / 3.6 / (dtMs / 1000.0)
        if (decel >= config.hardBrakeDecelMps2 && s.timestampMs - lastHardBrakeMs >= config.hardBrakeCooldownMs) {
            lastHardBrakeMs = s.timestampMs
            hardBrakeCount++
            eventList += TripEvent(TripEventType.HARD_BRAKE, s.timestampMs, s.lat, s.lon, "%.1f m/s²".format(decel), decel)
        }
    }

    // ---- 방향지시등 ----
    private fun trackTurnSignal(s: VehicleState) {
        val sig = s.turnSignal
        val last = signalHistory.lastOrNull()?.second
        if (sig == TurnSignal.LEFT || sig == TurnSignal.RIGHT) {
            if (last != sig) signalOnSinceMs = s.timestampMs
        } else if (sig == TurnSignal.OFF) {
            signalOnSinceMs = null
        }
        signalHistory.addLast(s.timestampMs to sig)
        val cutoff = s.timestampMs - config.signalLookbackMs - 1
        while (signalHistory.size > 1 && signalHistory.first().first < cutoff) signalHistory.removeFirst()
    }

    private fun detectTurn(p: VehicleState?, s: VehicleState) {
        val angle = s.steeringAngleDeg ?: return
        val over = abs(angle) >= config.turnSteeringDeg
        val wasOver = steeringOverThreshold
        steeringOverThreshold = over
        if (!over || wasOver) return
        val speed = s.speedKmh ?: p?.speedKmh ?: 0.0
        if (speed <= config.stoppedSpeedKmh) return // 정차 중 핸들 돌림은 회전이 아님
        if (s.timestampMs - lastTurnMs < config.turnCooldownMs) return
        lastTurnMs = s.timestampMs
        turnCount++
        val want = if (angle > 0) TurnSignal.RIGHT else TurnSignal.LEFT
        val since = s.timestampMs - config.signalLookbackMs
        val signaled = signalHistory.any { (t, sig) -> t >= since && sig == want }
        val dir = if (want == TurnSignal.RIGHT) "우" else "좌"
        if (signaled) {
            turnWithSignal++
            val lead = signalOnSinceMs?.let { s.timestampMs - it }
            lead?.let { signalToMoveMs += it }
            eventList += TripEvent(TripEventType.TURN_SIGNAL_OK, s.timestampMs, s.lat, s.lon, "$dir ${lead ?: 0}ms", lead?.toDouble())
        } else {
            eventList += TripEvent(TripEventType.TURN_SIGNAL_MISSED, s.timestampMs, s.lat, s.lon, dir, angle)
        }
    }

    // ---- 과속 ----
    /**
     * 지금 도로의 제한속도(km/h). 처음에는 [TripRecorderConfig.speedLimitKmh], 주행 중에는 내비가 주는 값으로 바뀐다
     * (내비 제한속도로 과속 경고는 나가는데 리포트는 늘 "과속 0곳"이던 것). 바뀌면 열린 과속 구간을 닫는다.
     */
    var speedLimitKmh: Double? = config.speedLimitKmh
        set(value) {
            if (value == field) return
            closeOverspeed(lastMs)
            field = value
        }

    private fun trackOverspeed(p: VehicleState?, s: VehicleState) {
        val speed = s.speedKmh ?: return
        val limit = speedLimitKmh ?: return
        val over = speed - limit
        if (over > config.overspeedToleranceKmh) {
            if (overspeedStart == null) { overspeedStart = s; overspeedMax = 0.0 }
            if (over > overspeedMax) overspeedMax = over
            if (over > maxOverspeedAll) maxOverspeedAll = over
        } else {
            closeOverspeed(s.timestampMs)
        }
    }

    private fun closeOverspeed(endMs: Long) {
        val start = overspeedStart ?: return
        overspeedStart = null
        overspeedCount++
        val dur = (endMs - start.timestampMs).coerceAtLeast(0)
        overspeedDuration += dur
        eventList += TripEvent(
            TripEventType.OVERSPEED, start.timestampMs, start.lat, start.lon,
            "제한 ${speedLimitKmh?.toInt()} +${"%.0f".format(overspeedMax)} km/h, ${dur / 1000}s", overspeedMax,
        )
    }

    // ---- 정지선 ----
    private fun trackStopLines(s: VehicleState) {
        val lat = s.lat ?: return
        val lon = s.lon ?: return
        val speed = s.speedKmh
        for (line in config.stopLines) {
            val inside = distanceMeters(lat, lon, line.lat, line.lon) <= config.stopLineRadiusM
            val visit = insideStopLine[line.id]
            if (inside) {
                val v = visit ?: StopLineVisit().also { insideStopLine[line.id] = it }
                if (speed != null) {
                    if (speed < v.minSpeed) v.minSpeed = speed
                    if (speed <= config.stoppedSpeedKmh) v.stopped = true
                }
            } else if (visit != null) {
                leaveStopLine(line.id, s)
            }
        }
    }

    private fun leaveStopLine(id: String, s: VehicleState) {
        val v = insideStopLine.remove(id) ?: return
        stopLineCount++
        if (v.stopped) {
            stopLineComplied++
            eventList += TripEvent(TripEventType.STOP_LINE_OK, s.timestampMs, s.lat, s.lon, id)
        } else {
            val min = if (v.minSpeed == Double.MAX_VALUE) null else v.minSpeed
            eventList += TripEvent(TripEventType.STOP_LINE_VIOLATION, s.timestampMs, s.lat, s.lon, id, min)
        }
    }

    // ---- 주행 중 안전 상태 ----
    private fun checkMovingSafety(s: VehicleState) {
        val moving = (s.speedKmh ?: 0.0) >= config.movingSpeedKmh
        if (!moving) return
        if (s.parkingBrakeOn == true && !parkingBrakeMovingReported) {
            parkingBrakeMovingReported = true
            eventList += TripEvent(TripEventType.PARKING_BRAKE_MOVING, s.timestampMs, s.lat, s.lon)
        }
        if (s.seatBeltWarning == true && !seatBeltMovingReported) {
            seatBeltMovingReported = true
            eventList += TripEvent(TripEventType.SEATBELT_WARNING_MOVING, s.timestampMs, s.lat, s.lon)
        }
    }

    companion object {
        /** 두 위경도 사이 거리(m). 하버사인 공식. */
        fun distanceMeters(lat1: Double, lon1: Double, lat2: Double, lon2: Double): Double {
            val r = 6_371_000.0
            val dLat = Math.toRadians(lat2 - lat1)
            val dLon = Math.toRadians(lon2 - lon1)
            val a = sin(dLat / 2) * sin(dLat / 2) +
                cos(Math.toRadians(lat1)) * cos(Math.toRadians(lat2)) * sin(dLon / 2) * sin(dLon / 2)
            return 2 * r * atan2(sqrt(a), sqrt(1 - a))
        }
    }
}
