// 리포트 · 모의 시험 · 성장 기록
package kr.drivecoach.core.report

/**
 * 도로주행시험 감점표의 항목 1개.
 * 출처: 도로교통법 시행규칙 [별표 26] 도로주행시험의 시험항목·채점기준 및 합격기준(제68조제1항 관련, 개정 2026.2.24).
 * [eventType] 이 null 이면 센서로 판정할 수 없는 항목 → 모의 시험 결과에 "판정 불가" 로 표시하고 감점에서 제외.
 */
data class DeductionItem(
    val no: Int,
    val task: String,
    val name: String,
    val points: Int,
    val eventType: TripEventType? = null,
) {
    /** 센서(차량 신호·GPS)로 판정 가능한가. */
    val detectable: Boolean get() = eventType != null
}

/** 별표 26 감점표 전체(46개 항목)와 실격 기준. */
object DeductionTable {
    const val SOURCE = "도로교통법 시행규칙 [별표 26] (개정 2026.2.24) — law.go.kr"
    const val FULL_SCORE = 100
    const val PASS_SCORE = 70
    /** 실격 사유 8호: 최고속도를 시속 10km 초과. */
    const val DISQUALIFY_OVERSPEED_KMH = 10.0

    val ITEMS: List<DeductionItem> = listOf(
        // 가. 출발 전 준비
        DeductionItem(1, "출발 전 준비", "문 닫힘 확인 소홀", 5),
        DeductionItem(2, "출발 전 준비", "출발 전 차량·안전 확인 소홀", 7),
        DeductionItem(3, "출발 전 준비", "주차브레이크 미해제", 10, TripEventType.PARKING_BRAKE_MOVING),
        // 나. 운전자세
        DeductionItem(4, "운전자세", "정차 중 기어 조작 불량", 5),
        // 다. 출발
        DeductionItem(5, "출발", "20초 내 미출발", 10),
        DeductionItem(6, "출발", "10초 내 재시동 실패", 7),
        DeductionItem(7, "출발", "주변 교통 방해(지연 출발)", 7),
        DeductionItem(8, "출발", "엔진 정지", 7),
        DeductionItem(9, "출발", "급조작·급출발", 7),
        DeductionItem(10, "출발", "심한 진동", 5),
        DeductionItem(11, "출발", "출발 시 방향지시등 미이행", 5),
        DeductionItem(12, "출발", "진입 중 신호 조기 해제", 5),
        DeductionItem(13, "출발", "신호 과다 유지", 5),
        DeductionItem(14, "출발", "시동 재조작", 5),
        // 라. 가속 및 속도 유지
        DeductionItem(15, "가속·속도 유지", "저속 주행", 5),
        DeductionItem(16, "가속·속도 유지", "속도 유지 불능", 5),
        DeductionItem(17, "가속·속도 유지", "부적절한 변속", 5),
        // 마. 제동 및 정지
        DeductionItem(18, "제동·정지", "엔진브레이크·제동 준비 미숙", 5),
        DeductionItem(19, "제동·정지", "제동 준비 미흡", 5),
        DeductionItem(20, "제동·정지", "정지 중 브레이크 미조작", 5),
        DeductionItem(21, "제동·정지", "급제동", 7, TripEventType.HARD_BRAKE),
        // 바. 조향
        DeductionItem(22, "조향", "핸들 조작 미숙·불량", 7),
        // 사. 차체 감각
        DeductionItem(23, "차체 감각", "우측 안전 확인 소홀", 7),
        DeductionItem(24, "차체 감각", "1미터 간격 미유지", 7),
        // 아. 통행 구분
        DeductionItem(25, "통행 구분", "지정 차로 위반", 7),
        DeductionItem(26, "통행 구분", "앞지르기 방법 위반", 7),
        DeductionItem(27, "통행 구분", "끼어들기 금지 위반", 7),
        DeductionItem(28, "통행 구분", "차로 유지 미숙", 5),
        // 자. 진로 변경
        DeductionItem(29, "진로 변경", "진로변경 시 안전 미확인", 10),
        DeductionItem(30, "진로 변경", "진로변경 신호 미이행", 7, TripEventType.TURN_SIGNAL_MISSED),
        DeductionItem(31, "진로 변경", "30미터 전 신호 미이행", 7, TripEventType.TURN_SIGNAL_OK), // value(깜빡이 후 이동 ms) 로 추정
        DeductionItem(32, "진로 변경", "신호 유지 미숙", 7),
        DeductionItem(33, "진로 변경", "신호 해제 지연", 7),
        DeductionItem(34, "진로 변경", "연속 진로변경", 7),
        DeductionItem(35, "진로 변경", "금지 장소 진로변경", 7),
        DeductionItem(36, "진로 변경", "진로변경 미숙", 7),
        // 차. 교차로 통행 등
        DeductionItem(37, "교차로 통행", "서행 위반", 10),
        DeductionItem(38, "교차로 통행", "일시정지 위반", 10),
        DeductionItem(39, "교차로 통행", "교차로 통행방법 위반", 7),
        DeductionItem(40, "교차로 통행", "신호 차량 방해", 7),
        DeductionItem(41, "교차로 통행", "꼬리 물기", 7),
        DeductionItem(42, "교차로 통행", "교차로 양보 불이행", 7),
        DeductionItem(43, "교차로 통행", "횡단보도·정지선 침범", 10, TripEventType.STOP_LINE_VIOLATION),
        // 카. 주행 종료
        DeductionItem(44, "주행 종료", "종료 시 주차브레이크 미작동", 5, TripEventType.END_PARKING_BRAKE_OFF),
        DeductionItem(45, "주행 종료", "종료 시 엔진 미정지", 5),
        DeductionItem(46, "주행 종료", "종료 시 기어 미조작(P)", 5, TripEventType.END_GEAR_NOT_P),
    )

    /** 센서로 판정 불가한 항목(결과 화면에 "판정 불가" 로 표시). */
    val NOT_DETECTABLE: List<DeductionItem> get() = ITEMS.filter { !it.detectable }

    fun byNo(no: Int): DeductionItem = ITEMS.first { it.no == no }
}

/** 감점 후보 1건: 어떤 이벤트가 어떤 항목에 해당하는지. */
data class ExamDeduction(val item: DeductionItem, val event: TripEvent)

/** 실격 후보 1건. */
data class Disqualification(val reason: String, val event: TripEvent)

/**
 * 모의 시험 결과(5-5). 시험장 코스를 코칭 없이 달린 뒤 정차 후 한 번에 보여준다.
 * [notDetectable] 은 표에 "판정 불가" 로 함께 보여 사용자가 스스로 점검하게 한다.
 */
data class MockExamResult(
    val deductions: List<ExamDeduction>,
    val disqualifications: List<Disqualification>,
    val notDetectable: List<DeductionItem>,
) {
    val totalDeduction: Int get() = deductions.sumOf { it.item.points }
    val score: Int get() = (DeductionTable.FULL_SCORE - totalDeduction).coerceAtLeast(0)
    val disqualified: Boolean get() = disqualifications.isNotEmpty()
    /** 합격선 70점 이상이고 실격 사유가 없으면 합격. */
    val passed: Boolean get() = !disqualified && score >= DeductionTable.PASS_SCORE
    /** 항목별 감점 합계(결과 표용). */
    fun byItem(): Map<DeductionItem, Int> = deductions.groupBy { it.item }.mapValues { (k, v) -> k.points * v.size }
}

/**
 * [TripEvent] 목록에 별표 26 감점표를 적용한다.
 * - TURN_SIGNAL_MISSED → 30번(7점), HARD_BRAKE → 21번(7점), STOP_LINE_VIOLATION → 43번(10점)
 * - TURN_SIGNAL_OK 이라도 깜빡이 후 이동까지 거리가 30m 미만이면 31번(7점) 후보
 * - OVERSPEED: 초과량이 10km/h 넘으면 실격(8호), 그 이하는 별표에 감점 항목이 없어 감점하지 않고 넘어간다
 * - SEATBELT_WARNING_MOVING → 실격(11호)
 * 같은 이벤트가 두 항목에 중복 감점되지는 않는다(비고의 중복 감점은 시험관 판단이라 적용하지 않음).
 */
class MockExamScorer(
    /** 30m 전 신호 판정에 쓰는 평균 속도(km/h). 리포트 summary.avgSpeedKmh 를 넘긴다. */
    private val avgSpeedKmh: Double = 30.0,
) {
    /** 31번(30m 전 신호) 판정 기준 거리. */
    val signalLeadMinMeters = 30.0

    fun score(events: List<TripEvent>): MockExamResult {
        val deductions = mutableListOf<ExamDeduction>()
        val dq = mutableListOf<Disqualification>()
        for (e in events) {
            when (e.type) {
                TripEventType.TURN_SIGNAL_MISSED -> deductions += ExamDeduction(DeductionTable.byNo(30), e)
                TripEventType.TURN_SIGNAL_OK -> {
                    val leadMs = e.value ?: continue
                    val leadM = avgSpeedKmh / 3.6 * (leadMs / 1000.0)
                    if (leadM < signalLeadMinMeters) deductions += ExamDeduction(DeductionTable.byNo(31), e)
                }
                TripEventType.HARD_BRAKE -> deductions += ExamDeduction(DeductionTable.byNo(21), e)
                TripEventType.STOP_LINE_VIOLATION -> deductions += ExamDeduction(DeductionTable.byNo(43), e)
                TripEventType.PARKING_BRAKE_MOVING -> deductions += ExamDeduction(DeductionTable.byNo(3), e)
                TripEventType.END_PARKING_BRAKE_OFF -> deductions += ExamDeduction(DeductionTable.byNo(44), e)
                TripEventType.END_GEAR_NOT_P -> deductions += ExamDeduction(DeductionTable.byNo(46), e)
                TripEventType.OVERSPEED -> {
                    if ((e.value ?: 0.0) > DeductionTable.DISQUALIFY_OVERSPEED_KMH) dq += Disqualification("최고속도 10km/h 초과(실격 8호)", e)
                }
                TripEventType.SEATBELT_WARNING_MOVING -> dq += Disqualification("좌석안전띠 미착용(실격 11호)", e)
                TripEventType.STOP_LINE_OK, TripEventType.COACH_SPEAK -> Unit
            }
        }
        return MockExamResult(deductions, dq, DeductionTable.NOT_DETECTABLE)
    }
}
