// 담당: M (리포트 · 모의 시험 · 성장 기록)
package kr.drivecoach.core.report

/**
 * 리포트에 쓰는 지표 키. 각 키는 [Metrics] 를 0~1 점수(높을수록 좋음)로 바꾸는 규칙을 가진다.
 * [practiceStage] 는 약한 항목일 때 추천할 단계 번호, [phraseKey] 는 tts 멘트 ID 의 꼬리말.
 */
enum class MetricKey(val practiceStage: String, val phraseKey: String) {
    /** 방향지시등 사용률. */
    SIGNAL_USAGE("4-7", "signal_usage"),
    /** 깜빡이 켠 뒤 이동까지 여유(3초 기준). */
    SIGNAL_LEAD("4-7", "signal_lead"),
    /** 급제동(10km 당 횟수). */
    HARD_BRAKE("4-6", "hard_brake"),
    /** 과속(주행 시간 중 과속 비율). */
    OVERSPEED("5-3", "overspeed"),
    /** 정지선 준수율. */
    STOP_LINE("5-3", "stop_line");

    /**
     * 0~1 점수. 해당 상황이 한 번도 없었으면 null(평가 불가).
     * 기준값은 단순 기본 규칙이다.
     */
    fun score(m: Metrics): Double? = when (this) {
        SIGNAL_USAGE -> m.signalUsageRate
        SIGNAL_LEAD -> m.avgSignalToMoveMs?.let { (it / IDEAL_SIGNAL_LEAD_MS.toDouble()).coerceIn(0.0, 1.0) }
        HARD_BRAKE -> if (m.durationMs <= 0) null else (1.0 - m.hardBrakePer10Km / HARD_BRAKE_PER_10KM_WORST).coerceIn(0.0, 1.0)
        OVERSPEED -> if (m.durationMs <= 0) null else (1.0 - m.overspeedTimeRatio / OVERSPEED_RATIO_WORST).coerceIn(0.0, 1.0)
        STOP_LINE -> m.stopLineComplianceRate
    }

    companion object {
        /** 깜빡이 후 이동까지 이 시간이면 만점(4-7 "깜빡이 3초"). */
        const val IDEAL_SIGNAL_LEAD_MS = 3_000L
        /** 10km 당 급제동 이 횟수면 0점. */
        const val HARD_BRAKE_PER_10KM_WORST = 5.0
        /** 주행 시간의 이 비율이 과속이면 0점. */
        const val OVERSPEED_RATIO_WORST = 0.10
    }
}

/**
 * 주행 1회의 리포트(5-4). 문구는 "잘한 것 1 / 나아진 점 1 / 고칠 것 1" 슬롯만 둔다.
 * 각 슬롯에는 사용자에게 보일 문장이 아니라 지표 이름 멘트 ID(예: "report_item_signal_usage") 가 들어간다.
 * app 은 이것을 tts 카탈로그의 report_good / report_improved / report_next 의 {item} 자리에 넣어 읽는다.
 * 실수 횟수를 나열하는 리포트는 만들지 않는다(지적은 짧게, 정차 후).
 */
data class TripReport(
    val summary: Metrics,
    val events: List<TripEvent>,
    /** 가장 잘한 지표의 멘트 ID. 평가할 지표가 없으면 null. */
    val bestOne: String?,
    /** 이전 리포트 대비 가장 나아진 지표의 멘트 ID. 이전 리포트가 없거나 나아진 게 없으면 null. */
    val improvedOne: String?,
    /** 다음에 고칠 지표 1개의 멘트 ID. 기준 이상이면 null(고칠 게 없음). */
    val fixOne: String?,
    /** 코스 ID(5-1). 성장 기록에서 코스별로 묶는 데 쓴다. */
    val courseId: String? = null,
    val createdAtMs: Long = 0,
)

/**
 * [Metrics] 와 이전 리포트로 [TripReport] 의 세 슬롯을 채우는 단순 기본 규칙.
 * - bestOne: 점수가 가장 높은 지표(동점이면 선언 순서)
 * - improvedOne: 이전 리포트 대비 점수가 가장 많이 오른 지표(최소 [IMPROVE_MIN_DELTA] 이상)
 * - fixOne: 점수가 가장 낮고 [FIX_THRESHOLD] 미만인 지표(bestOne 과 다른 것)
 * 실수 횟수는 나열하지 않는다. 숫자는 summary 에 있고, 화면 구성은 app 이 정한다.
 */
object ReportComposer {
    /** 이 점수 미만이면 "고칠 것" 후보. */
    const val FIX_THRESHOLD = 0.7
    /** 이전 대비 이만큼 올라야 "나아진 점" 으로 본다. */
    const val IMPROVE_MIN_DELTA = 0.1

    /** 지표 이름 멘트 ID 규칙. tts 모듈(K) 에 같은 ID 로 문장을 요청한다. */
    fun phraseId(key: MetricKey) = "report_item_${key.phraseKey}"

    /** 리포트를 만든다. [previous] 는 같은 사용자의 직전 리포트(없으면 null). */
    fun compose(
        summary: Metrics,
        events: List<TripEvent>,
        previous: TripReport? = null,
        courseId: String? = null,
        createdAtMs: Long = 0,
    ): TripReport {
        val scores = MetricKey.values().mapNotNull { k -> k.score(summary)?.let { k to it } }
        val best = scores.maxByOrNull { it.second }?.first
        val fix = scores.filter { it.first != best && it.second < FIX_THRESHOLD }.minByOrNull { it.second }?.first
        val improved = previous?.let { prev ->
            scores.mapNotNull { (k, now) -> k.score(prev.summary)?.let { before -> k to (now - before) } }
                .filter { it.second >= IMPROVE_MIN_DELTA }
                .maxByOrNull { it.second }?.first
        }
        return TripReport(
            summary = summary,
            events = events,
            bestOne = best?.let { phraseId(it) },
            improvedOne = improved?.let { phraseId(it) },
            fixOne = fix?.let { phraseId(it) },
            courseId = courseId,
            createdAtMs = createdAtMs,
        )
    }
}
