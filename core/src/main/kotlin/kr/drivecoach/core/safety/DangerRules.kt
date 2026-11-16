// 위험 피드백
package kr.drivecoach.core.safety

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId
import kotlin.math.abs

/**
 * 위험 이벤트 종류(위험 상황 피드백은 바로 한다).
 *
 * **늦은 브레이크(LATE_BRAKE)는 규칙에 넣지 않는다.**
 * 정지선·앞차 거리를 앱이 알 수 없어 정확히 잡을 수 없으므로, 정차 후 리포트에서
 * [HARD_BRAKE] 횟수로 간접 평가한다. (리포트 모듈은 [DangerRules.eventCount] 를 쓰면 된다.)
 */
enum class DangerType {
    /** 제한속도 + 여유를 N초 이상 넘김. */
    SPEEDING,
    /** 브레이크를 밟은 상태에서 감속도가 임계 이상. */
    HARD_BRAKE,
    /** 30km/h 이상에서 조향각 변화율이 임계 이상(급차로변경·급조향). */
    ABRUPT_LANE_CHANGE,
    /** R 기어에서 RPM 이 공회전보다 크게 오름 = 후진 중 엑셀을 밟은 것으로 추정. */
    REVERSE_THROTTLE,
}

/** 위험 등급. 멘트 선택과 리포트 가중치에 쓴다. */
enum class Severity { LOW, MEDIUM, HIGH }

/**
 * 위험 이벤트 한 건.
 * @property evidence 판정 근거 수치(예: "speedKmh", "decelMps2"). 리포트·디버그용. 키는 [DangerRules] 의 KDoc 참고.
 */
data class DangerEvent(
    val type: DangerType,
    val timestampMs: Long,
    val severity: Severity,
    val phraseId: PhraseId,
    val evidence: Map<String, Double>,
)

/** 위험 이벤트를 받는 콜백. app 모듈이 이걸 TTS 어댑터에 연결한다. */
fun interface DangerListener {
    fun onDanger(event: DangerEvent)
}

/**
 * 위험 피드백 멘트 ID. 문장은 K 작업자의 Phrases 카탈로그가 채운다.
 */
object DangerPhrases {
    /** 제한속도 초과(MEDIUM). Phrases.dangerSpeeding 과 같은 ID. */
    val SPEEDING = PhraseId("danger_speeding")
    /** 20km/h 이상 크게 초과(HIGH). K 에게 요청한 신규 ID. */
    val SPEEDING_HIGH = PhraseId("danger_speeding_high")
    /** 급제동. Phrases.dangerHardBrake 와 같은 ID. */
    val HARD_BRAKE = PhraseId("danger_hard_brake")
    /** 급차로변경, 깜빡이는 켰음(MEDIUM). Phrases.dangerHardLaneChange 와 같은 ID. */
    val LANE_CHANGE = PhraseId("danger_hard_lane_change")
    /** 깜빡이 없이 급차로변경(HIGH). K 에게 요청한 신규 ID. */
    val LANE_CHANGE_NO_SIGNAL = PhraseId("danger_hard_lane_change_no_signal")
    /** 후진 중 RPM 상승. Phrases.dangerReverseRpm 과 같은 ID. */
    val REVERSE_THROTTLE = PhraseId("danger_reverse_rpm")
}

/**
 * 위험 규칙 임계값. 모두 상수 기본값을 가지며, 근거는 각 항목 주석과 참고.
 */
data class DangerConfig(
    // ── SPEEDING ──
    /** 제한속도 위 여유(km/h). 차량 속도계는 실제보다 약간 높게 표시되고, 제한속도 데이터도 오차가 있어 5km/h 를 둔다. */
    val speedMarginKmh: Double = 5.0,
    /** 초과 상태가 이만큼 지속돼야 발화(ms). 순간 초과(추월·내리막 한 순간)는 지적하지 않는다. */
    val speedingHoldMs: Long = 3_000L,
    /** 이 값(km/h) 이상 초과하면 HIGH. 한국 과속 단속 기준이 20km/h 초과부터 범칙금이 커지는 데서 가져옴. */
    val speedingHighOverKmh: Double = 20.0,

    // ── HARD_BRAKE ──
    /** 급제동 감속도 임계(m/s²). 0.35g ≈ 3.4m/s². 텔레매틱스(UBI) 업계에서 통용되는 '급제동' 기준(0.3~0.4g). */
    val hardBrakeDecelMps2: Double = 3.4,
    /** 이 값(m/s²) 이상이면 HIGH. 0.5g ≈ 4.9m/s², 일반 운전자가 '비상 제동'으로 느끼는 수준. */
    val hardBrakeHighDecelMps2: Double = 4.9,
    /** 감속 시작 속도가 이 값(km/h) 미만이면 무시. 저속 정지 직전 센서 잡음·크리핑 제동을 제외. */
    val hardBrakeMinSpeedKmh: Double = 10.0,
    /**
     * 임계를 처음 넘은 뒤 이 시간(ms) 동안 유지돼야 발화. 제동 시작 순간은 창 안 기준점 대부분이 '아직 안 밟던'
     * 샘플이라 감속도가 과소 측정되므로, 한 샘플(100ms) 더 보고 등급(MEDIUM/HIGH)을 정한다. 음성 지연으로는 무시할 수준.
     */
    val hardBrakeConfirmMs: Long = 100L,

    // ── ABRUPT_LANE_CHANGE ──
    /** 이 속도(km/h) 이상에서만 판정. 주차장·골목 저속 조향은 제외 (요구사항). */
    val laneChangeMinSpeedKmh: Double = 30.0,
    /** 핸들(스티어링 휠) 각 변화율 임계(deg/s). 보통의 차로 변경은 핸들 20~40° 를 1~2초에 돌려 20~50deg/s, 긴급 회피는 100deg/s 이상. 그 사이 120 을 택했다. */
    val laneChangeSteerRateDegPerS: Double = 120.0,
    /** 창 안에서 핸들이 최소 이만큼(deg)은 움직여야 한다. 작은 각의 순간 잡음(몇 도가 20ms 에 튐)이 큰 변화율로 잡히는 것을 막는다. */
    val laneChangeMinDeltaDeg: Double = 20.0,
    /** 조향 전 이 시간(ms) 안에 방향지시등이 켜져 있었으면 '신호 있음'("깜빡이 3초" 기준). */
    val turnSignalLookbackMs: Long = 3_000L,

    // ── REVERSE_THROTTLE ──
    /** 공회전 대비 RPM 상승 임계. 크리핑 후진은 idle 그대로이고, 엑셀을 살짝만 밟아도 +300 은 넘는다. */
    val reverseRpmRise: Double = 300.0,
    /** 이 값 이상 오르면 HIGH (확실히 밟은 것). */
    val reverseRpmHighRise: Double = 600.0,

    // ── 공통 ──
    /**
     * 변화율(Δv/Δt, Δθ/Δt) 계산 창(ms). 창 안의 모든 과거 샘플을 기준점으로 삼아 변화율을 각각 계산하고
     * **가장 큰 값**을 쓴다. 에피소드 시작 직후 창에 섞인 '아직 안 밟던' 구간이 피크를 희석하는 것을 막기 위해서다.
     * 100ms 샘플이면 5개 정도. 너무 짧으면 잡음, 길면 피크가 뭉개진다.
     */
    val rateWindowMs: Long = 500L,
    /**
     * 변화율 계산에 필요한 최소 시간 간격(ms). 속도 신호가 1km/h 단위로 양자화돼 있으면 100ms 간격에서는
     * 2.8m/s² 의 양자화 잡음이 생긴다. 200ms 면 1.4m/s² 로 줄어 임계(3.4) 대비 안전하다.
     */
    val rateMinDtMs: Long = 200L,
    /** 같은 타입 재발화 금지 간격(ms). "한 번만, 짧게". */
    val cooldownMs: Long = 20_000L,
) {
    companion object {
        /** 기본 설정. 리포트 모듈 등이 같은 임계값(예: 급제동 감속도)을 참조할 때 쓴다. */
        val DEFAULT = DangerConfig()
    }
}

/**
 * 차량 상태 스트림에서 위험 이벤트를 뽑는 순수 Kotlin 엔진.
 *
 * 사용: `signalSource.addListener(rules)`, `rules.addListener { event -> tts.speak(event.phraseId) }`.
 * 제한속도는 내비/지도에서 알게 될 때 [speedLimitKmh] 에 넣는다. 모르면 null 이고 SPEEDING 규칙은 꺼진다.
 *
 * "한 번만" 원칙 구현:
 * 1. **에피소드 1회**: 조건이 참인 동안(예: 계속 과속 중) 처음 한 번만 발화하고, 조건이 풀려야 다시 무장된다.
 * 2. **쿨다운**: 같은 타입은 마지막 발화 후 [DangerConfig.cooldownMs](기본 20초) 안에는 다시 발화하지 않는다.
 *
 * evidence 키:
 * - SPEEDING: speedKmh, limitKmh, overKmh, heldMs
 * - HARD_BRAKE: decelMps2, fromKmh, toKmh, dtMs, brakePressed(1=밟음, 0=안 밟음, -1=모름)
 * - ABRUPT_LANE_CHANGE: speedKmh, steerRateDegPerS, deltaDeg, dtMs, signaled(1/0)
 * - REVERSE_THROTTLE: rpm, idleRpm, riseRpm
 *
 * 시각은 전부 [VehicleState.timestampMs] 기준이라 테스트에서 결정적으로 재현된다.
 */
class DangerRules(
    private val config: DangerConfig = DangerConfig(),
    /** 공회전 RPM 공급자. 보통 [IdleRpmEstimator.idleRpm] 을 넘긴다. */
    private val idleRpmProvider: () -> Double = { IdleRpmEstimator.DEFAULT_IDLE_RPM },
) : StateListener {

    /** 현재 도로 제한속도(km/h). 외부(내비 어댑터)가 주입. null 이면 SPEEDING 비활성. */
    var speedLimitKmh: Double? = null
        set(value) {
            field = value
            if (value == null) { speedingSinceMs = null; active.remove(DangerType.SPEEDING) }
        }

    private val listeners = mutableListOf<DangerListener>()
    private val log = mutableListOf<DangerEvent>()

    /** 타입별 마지막 발화 시각(쿨다운용). */
    private val lastFiredMs = HashMap<DangerType, Long>()
    /** 현재 조건이 참인(에피소드 진행 중인) 타입. 조건이 풀려야 빠진다. */
    private val active = HashSet<DangerType>()

    private data class Sample(val tMs: Long, val speedKmh: Double?, val steerDeg: Double?)
    /** 최근 [DangerConfig.rateWindowMs] 안의 샘플. 맨 앞이 가장 오래된 것. */
    private val history = ArrayDeque<Sample>()

    private var speedingSinceMs: Long? = null
    private var hardBrakeSinceMs: Long? = null
    private var lastSignalMs: Long? = null
    private var lastSignalDir: TurnSignal = TurnSignal.OFF
    private var prevTs: Long? = null

    fun addListener(listener: DangerListener) { listeners += listener }
    fun removeListener(listener: DangerListener) { listeners -= listener }

    /** 지금까지 발화된 이벤트 전체(리포트용, 시간순). */
    val events: List<DangerEvent> get() = log.toList()

    /** 타입별 발화 횟수. 리포트의 "급제동 N회" 등에 쓴다. */
    fun eventCount(type: DangerType): Int = log.count { it.type == type }

    /** 주행 세션을 새로 시작할 때 상태를 모두 비운다(제한속도는 유지). */
    fun reset() {
        log.clear(); lastFiredMs.clear(); active.clear(); history.clear()
        speedingSinceMs = null; hardBrakeSinceMs = null; lastSignalMs = null; lastSignalDir = TurnSignal.OFF; prevTs = null
    }

    override fun onState(state: VehicleState) {
        val now = state.timestampMs
        // 시각이 거꾸로 가면(재접속·클럭 보정) 변화율 창을 비우고 새로 시작한다.
        if (prevTs?.let { now < it } == true) history.clear()
        prevTs = now

        rememberTurnSignal(state)
        pruneHistory(now)

        checkSpeeding(state)
        checkHardBrake(state)
        checkAbruptLaneChange(state)
        checkReverseThrottle(state)

        history.addLast(Sample(now, state.speedKmh, state.steeringAngleDeg))
    }

    // ───────────────────────── 규칙 1: SPEEDING ─────────────────────────

    private fun checkSpeeding(s: VehicleState) {
        val limit = speedLimitKmh
        val v = s.speedKmh
        if (limit == null || v == null) { speedingSinceMs = null; active.remove(DangerType.SPEEDING); return }

        when {
            v > limit + config.speedMarginKmh -> {
                val since = speedingSinceMs ?: s.timestampMs.also { speedingSinceMs = it }
                val held = s.timestampMs - since
                if (held >= config.speedingHoldMs) {
                    val over = v - limit
                    val sev = if (over >= config.speedingHighOverKmh) Severity.HIGH else Severity.MEDIUM
                    val phrase = if (sev == Severity.HIGH) DangerPhrases.SPEEDING_HIGH else DangerPhrases.SPEEDING
                    tryFire(DangerType.SPEEDING, s.timestampMs, sev, phrase,
                        mapOf("speedKmh" to v, "limitKmh" to limit, "overKmh" to over, "heldMs" to held.toDouble()))
                }
            }
            v <= limit -> {
                // 제한속도 아래로 완전히 내려와야 에피소드 종료(여유 구간은 히스테리시스).
                speedingSinceMs = null
                active.remove(DangerType.SPEEDING)
            }
            else -> speedingSinceMs = null // 여유 구간: 지속 시간만 리셋
        }
    }

    // ───────────────────────── 규칙 2: HARD_BRAKE ─────────────────────────

    private fun checkHardBrake(s: VehicleState) {
        val vNow = s.speedKmh
        val peak = if (vNow == null) null else peakRate(s.timestampMs, { it.speedKmh }) { vOld -> (vOld - vNow) / 3.6 }
        if (vNow == null || peak == null) { active.remove(DangerType.HARD_BRAKE); hardBrakeSinceMs = null; return }
        val decel = peak.rate // m/s², 양수 = 감속
        val vOld = peak.from

        // 브레이크 신호와 결합: '안 밟음'이 확실하면 급제동으로 보지 않는다(신호 튐·오르막 감속 등).
        // 신호를 모르면(null) 감속도만으로 판정한다.
        val brakeOk = s.brakePressed != false
        val cond = brakeOk && vOld >= config.hardBrakeMinSpeedKmh && decel >= config.hardBrakeDecelMps2
        if (cond) {
            val since = hardBrakeSinceMs ?: s.timestampMs.also { hardBrakeSinceMs = it }
            if (s.timestampMs - since < config.hardBrakeConfirmMs) return
            val sev = if (decel >= config.hardBrakeHighDecelMps2) Severity.HIGH else Severity.MEDIUM
            tryFire(DangerType.HARD_BRAKE, s.timestampMs, sev, DangerPhrases.HARD_BRAKE, mapOf(
                "decelMps2" to decel, "fromKmh" to vOld, "toKmh" to vNow, "dtMs" to peak.dtMs.toDouble(),
                "brakePressed" to when (s.brakePressed) { true -> 1.0; false -> 0.0; null -> -1.0 },
            ))
        } else {
            hardBrakeSinceMs = null
            if (decel < config.hardBrakeDecelMps2 / 2) active.remove(DangerType.HARD_BRAKE)
        }
    }

    // ───────────────────────── 규칙 3: ABRUPT_LANE_CHANGE ─────────────────────────

    private fun checkAbruptLaneChange(s: VehicleState) {
        val v = s.speedKmh
        val theta = s.steeringAngleDeg
        // 창 안 기준점 중 |Δθ| ≥ 최소각 인 것만 놓고 가장 큰 변화율을 본다.
        val peak = if (theta == null) null else peakRate(
            s.timestampMs,
            { it.steerDeg?.takeIf { old -> abs(theta - old) >= config.laneChangeMinDeltaDeg } },
        ) { old -> abs(theta - old) }
        if (v == null || theta == null || v < config.laneChangeMinSpeedKmh) {
            active.remove(DangerType.ABRUPT_LANE_CHANGE); return
        }
        val rate = peak?.rate ?: 0.0

        if (peak != null && rate >= config.laneChangeSteerRateDegPerS) {
            val delta = theta - peak.from
            val signaled = wasSignaled(s, delta)
            // 방향지시등 없이 급조향 = 옆 차가 예측할 수 없으므로 한 등급 높인다.
            val sev = if (signaled) Severity.MEDIUM else Severity.HIGH
            val phrase = if (signaled) DangerPhrases.LANE_CHANGE else DangerPhrases.LANE_CHANGE_NO_SIGNAL
            tryFire(DangerType.ABRUPT_LANE_CHANGE, s.timestampMs, sev, phrase, mapOf(
                "speedKmh" to v, "steerRateDegPerS" to rate, "deltaDeg" to delta,
                "dtMs" to peak.dtMs.toDouble(), "signaled" to if (signaled) 1.0 else 0.0,
            ))
        } else if (rate < config.laneChangeSteerRateDegPerS / 2) {
            active.remove(DangerType.ABRUPT_LANE_CHANGE)
        }
    }

    /** 조향 방향(+: 우)과 같은 쪽 방향지시등이 지금 켜져 있거나 lookback 안에 켜져 있었는지. */
    private fun wasSignaled(s: VehicleState, deltaDeg: Double): Boolean {
        val wanted = if (deltaDeg > 0) TurnSignal.RIGHT else TurnSignal.LEFT
        if (s.turnSignal == wanted) return true
        val t = lastSignalMs ?: return false
        return lastSignalDir == wanted && s.timestampMs - t <= config.turnSignalLookbackMs
    }

    private fun rememberTurnSignal(s: VehicleState) {
        if (s.turnSignal == TurnSignal.LEFT || s.turnSignal == TurnSignal.RIGHT) {
            lastSignalMs = s.timestampMs
            lastSignalDir = s.turnSignal
        }
    }

    // ───────────────────────── 규칙 4: REVERSE_THROTTLE ─────────────────────────

    private fun checkReverseThrottle(s: VehicleState) {
        // 엑셀 페달은 감지 불가 → R 기어에서 RPM 이 공회전보다 오르면 밟은 것으로 본다.
        // 속도 기준은 쓰지 않는다 (크리핑 속도에서 수시로 지적하면 스트레스).
        val rpm = s.rpm
        if (s.gear != Gear.R || rpm == null) { active.remove(DangerType.REVERSE_THROTTLE); return }
        val idle = idleRpmProvider()
        val rise = rpm - idle
        if (rise >= config.reverseRpmRise) {
            val sev = if (rise >= config.reverseRpmHighRise) Severity.HIGH else Severity.MEDIUM
            tryFire(DangerType.REVERSE_THROTTLE, s.timestampMs, sev, DangerPhrases.REVERSE_THROTTLE,
                mapOf("rpm" to rpm, "idleRpm" to idle, "riseRpm" to rise))
        } else if (rise < config.reverseRpmRise / 2) {
            active.remove(DangerType.REVERSE_THROTTLE)
        }
    }

    // ───────────────────────── 공통 ─────────────────────────

    /**
     * 조건이 참일 때 호출. 에피소드 중 첫 호출이고 쿨다운이 지났을 때만 실제 발화한다.
     * @return 발화했으면 true
     */
    private fun tryFire(type: DangerType, now: Long, severity: Severity, phrase: PhraseId, evidence: Map<String, Double>): Boolean {
        if (!active.add(type)) return false // 같은 에피소드 안에서는 한 번만
        val last = lastFiredMs[type]
        if (last != null && now - last < config.cooldownMs) return false // 쿨다운
        lastFiredMs[type] = now
        val event = DangerEvent(type, now, severity, phrase, evidence)
        log += event
        listeners.toList().forEach { it.onDanger(event) }
        return true
    }

    private fun pruneHistory(now: Long) {
        while (history.isNotEmpty() && now - history.first().tMs > config.rateWindowMs) history.removeFirst()
    }

    /** 변화율 계산 결과. [from] 은 기준 샘플 값, [rate] 는 단위/초. */
    private data class Peak(val from: Double, val dtMs: Long, val rate: Double)

    /**
     * 창 안의 모든 과거 샘플을 기준점으로 [delta](기준값 → 변화량)를 계산해 변화량/Δt 가 가장 큰 것을 돌려준다.
     * [value] 가 null 을 주는 샘플과 [DangerConfig.rateMinDtMs] 보다 가까운 샘플은 건너뛴다. 하나도 없으면 null.
     */
    private fun peakRate(now: Long, value: (Sample) -> Double?, delta: (Double) -> Double): Peak? {
        var best: Peak? = null
        for (sample in history) {
            val dt = now - sample.tMs
            if (dt < config.rateMinDtMs) continue
            val v = value(sample) ?: continue
            val rate = delta(v) / (dt / 1000.0)
            if (best == null || rate > best.rate) best = Peak(v, dt, rate)
        }
        return best
    }
}
