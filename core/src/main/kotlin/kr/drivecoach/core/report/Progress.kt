// 리포트 · 모의 시험 · 성장 기록
package kr.drivecoach.core.report

/** 성장 기록의 저장용 스냅샷. 직렬화·저장은 app 이 한다. */
data class ProgressState(
    val completedStages: Set<String> = emptySet(),
    val reports: List<TripReport> = emptyList(),
)

/** 약한 항목 추천 결과: 어떤 지표가 약하고, 어느 단계(번호)를 다시 연습하면 좋은지. */
data class WeakSpot(val key: MetricKey, val avgScore: Double, val practiceStage: String)

/**
 * 성장 기록(9장): 단계별 완료 체크, 코스별 리포트 누적, 약한 항목 추천, 초보 졸업 플래그.
 * 단계 ID 는 "2-1", "4-7", "5-3" 같은 번호 문자열이다.
 */
class Progress(initial: ProgressState = ProgressState()) {
    private val stages = initial.completedStages.toMutableSet()
    private val reportList = initial.reports.toMutableList()

    /** 현재 상태 스냅샷(저장용). */
    fun state() = ProgressState(stages.toSet(), reportList.toList())

    // ---- 단계별 완료 ----
    /** 단계를 완료 처리한다. 건너뛴 단계는 호출하지 않는다. */
    fun completeStage(stageId: String) { stages += stageId }
    fun isStageCompleted(stageId: String) = stageId in stages
    val completedStages: Set<String> get() = stages.toSet()

    // ---- 코스별 리포트 누적 ----
    /** 주행 리포트를 추가한다(시간순으로 들어온다고 가정). */
    fun addReport(report: TripReport) { reportList += report }
    val reports: List<TripReport> get() = reportList.toList()
    /** 특정 코스의 리포트만. */
    fun reportsForCourse(courseId: String) = reportList.filter { it.courseId == courseId }
    /** 가장 최근 리포트(ReportComposer.compose 의 previous 로 넘긴다). */
    fun lastReport(): TripReport? = reportList.lastOrNull()
    val tripCount: Int get() = reportList.size

    // ---- 약한 항목 추천 ----
    /**
     * 최근 [recent] 회 리포트에서 평균 점수가 가장 낮은 지표를 추천한다.
     * 한 번도 평가되지 않은 지표(해당 상황 없음)는 제외. 리포트가 없으면 null.
     */
    fun weakestMetric(recent: Int = RECENT_WINDOW): WeakSpot? {
        val window = reportList.takeLast(recent)
        if (window.isEmpty()) return null
        return MetricKey.values().mapNotNull { key ->
            val scores = window.mapNotNull { key.score(it.summary) }
            if (scores.isEmpty()) null else WeakSpot(key, scores.average(), key.practiceStage)
        }.minByOrNull { it.avgScore }
    }

    // ---- 초보 졸업 ----
    /** 주행 [GRADUATION_TRIPS] 회를 채우면 "초보 졸업". 이후 app 은 습관 교정 모드로 전환한다. */
    val graduated: Boolean get() = reportList.size >= GRADUATION_TRIPS
    /** 졸업까지 남은 주행 횟수. */
    val tripsToGraduate: Int get() = (GRADUATION_TRIPS - reportList.size).coerceAtLeast(0)

    companion object {
        const val GRADUATION_TRIPS = 10
        const val RECENT_WINDOW = 5
    }
}
