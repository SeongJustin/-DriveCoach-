// 담당: F (위험 피드백)
package kr.drivecoach.core.safety

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.VehicleState

/**
 * 공회전(idle) RPM 학습기.
 *
 * 시동 후 "정차(P 또는 N, 속도 0)" 구간의 RPM 표본을 모아 **중앙값**을 공회전 기준으로 삼는다.
 * - R 기어에서 RPM 이 공회전보다 오르면 엑셀을 밟은 것으로 본다. 그 "공회전" 기준이 차량·온도마다
 *   다르므로(냉간 시동 직후 1,000~1,200, 워밍업 후 650~800) 고정값 대신 실제 차에서 학습한다.
 * - 학습 전에는 [defaultIdleRpm](기본 800)을 돌려준다.
 * - 최근 [windowSize]개 표본의 중앙값을 쓰므로, 냉간 고회전 idle 이 워밍업으로 내려가면 값도 따라 내려간다.
 *
 * 순수 Kotlin. [StateListener]로 신호 스트림에 붙여 두기만 하면 된다.
 */
class IdleRpmEstimator(
    /** 학습 전 기본 공회전 RPM. 아반떼 CN8 내연기관 기준 약 700~800. */
    val defaultIdleRpm: Double = DEFAULT_IDLE_RPM,
    /** 중앙값 계산에 쓰는 최근 표본 수. */
    private val windowSize: Int = DEFAULT_WINDOW_SIZE,
    /** 이 수 이상 표본이 모여야 "학습됨"으로 본다. */
    private val minSamples: Int = DEFAULT_MIN_SAMPLES,
    /** 엔진이 돈 뒤 이 시간(ms) 동안은 표본을 받지 않는다(시동 직후 RPM 튐 제외). */
    private val settleMs: Long = DEFAULT_SETTLE_MS,
) : StateListener {

    companion object {
        const val DEFAULT_IDLE_RPM = 800.0
        const val DEFAULT_WINDOW_SIZE = 60
        const val DEFAULT_MIN_SAMPLES = 10
        /** 시동 직후 2초는 크랭킹·초기 고회전이 섞이므로 버린다. */
        const val DEFAULT_SETTLE_MS = 2_000L
        /** 공회전으로 인정하는 RPM 범위. 이 밖이면 시동 꺼짐/크랭킹/엑셀 조작으로 보고 버린다. */
        const val MIN_PLAUSIBLE_IDLE_RPM = 400.0
        const val MAX_PLAUSIBLE_IDLE_RPM = 1_500.0
        /** 이 속도 이하를 "정차"로 본다 (센서 잡음 허용). */
        const val STOPPED_SPEED_KMH = 0.5
        /** 엔진이 돌고 있다고 보는 최소 RPM. */
        const val ENGINE_RUNNING_RPM = 300.0
    }

    private val samples = ArrayDeque<Double>()
    private var engineRunningSinceMs: Long? = null
    private var learned: Double? = null

    /** 현재 추정 공회전 RPM. 학습 전이면 [defaultIdleRpm]. */
    val idleRpm: Double get() = learned ?: defaultIdleRpm

    /** 표본이 [minSamples] 이상 모여 실제 차 값으로 갱신됐는지. */
    val isLearned: Boolean get() = learned != null

    /** 지금까지 받아들인 표본 수(윈도 크기로 상한). */
    val sampleCount: Int get() = samples.size

    /** 학습값을 버리고 처음 상태로 돌린다(시동을 다시 걸었을 때 등). */
    fun reset() {
        samples.clear()
        engineRunningSinceMs = null
        learned = null
    }

    override fun onState(state: VehicleState) {
        val rpm = state.rpm
        // 엔진이 꺼졌으면 "돌기 시작한 시각"을 지운다. 다시 켜지면 settle 부터 다시 센다.
        if (rpm == null || rpm < ENGINE_RUNNING_RPM || state.ignitionOn == false) {
            engineRunningSinceMs = null
            return
        }
        val since = engineRunningSinceMs ?: state.timestampMs.also { engineRunningSinceMs = it }
        if (state.timestampMs - since < settleMs) return

        if (!isIdleCondition(state)) return
        if (rpm < MIN_PLAUSIBLE_IDLE_RPM || rpm > MAX_PLAUSIBLE_IDLE_RPM) return

        samples.addLast(rpm)
        while (samples.size > windowSize) samples.removeFirst()
        if (samples.size >= minSamples) learned = median(samples)
    }

    /** 공회전 표본으로 쓸 수 있는 상태인지: P/N 기어 + 정차. 속도를 모르면(null) 정차로 보지 않는다. */
    private fun isIdleCondition(s: VehicleState): Boolean {
        if (s.gear != Gear.P && s.gear != Gear.N) return false
        val v = s.speedKmh ?: return false
        return v <= STOPPED_SPEED_KMH
    }

    private fun median(values: Collection<Double>): Double {
        val sorted = values.sorted()
        val n = sorted.size
        return if (n % 2 == 1) sorted[n / 2] else (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0
    }
}
