// 화면 상태(UI State) 데이터 클래스·열거형: CoachController 가 StateFlow 로 내보내고 MainActivity/screens 가 그린다.
package kr.drivecoach.app

import kotlinx.coroutines.flow.StateFlow
import kr.drivecoach.app.navi.NaviArrival
import kr.drivecoach.core.course.CourseOptionId
import kr.drivecoach.core.course.LatLon
import kr.drivecoach.core.course.ManeuverType
import kr.drivecoach.core.parking.ParkingSlowMode
import kr.drivecoach.core.parking.ParkingKind
import kr.drivecoach.core.parking.SlowModePhase
import kr.drivecoach.core.parking.Tangle
import kr.drivecoach.core.practice.Phase
import kr.drivecoach.core.tts.Phrases

/** 화면에 보여줄 "지금 진행 중인 연습 항목" 요약. 문장은 전부 core/tts 카탈로그에서 가져온 것. */
data class PracticeUi(
    val itemId: String,
    val chapter: String,
    /** 항목 이름(core/practice 의 데이터). */
    val title: String,
    val phase: Phase,
    /** 이 단계에서 들려준(들려줄) 문장. */
    val text: String,
    /** 누적 판정 진행 상황(예: "P→R"). */
    val progress: String?,
    /** 센서로 자동 통과 가능한 항목인지. */
    val detectable: Boolean,
    /** 몇 번째 항목인지(1부터) / 전체. */
    val index: Int,
    val total: Int,
    /** 이 장의 항목을 전부 끝냈는지. */
    val allDone: Boolean = false,
    /** 이미 통과했던 항목을 다시 여는 중(캡션에 "✓ 통과한 항목"). */
    val passedBefore: Boolean = false,
)

/** P 화면에서 열려 있는 하위 활동. 한 번에 하나만(기능 연습 / 핸들 감각 / 직각 주차 / 코스 주행 / 도로 법규). */
enum class SubMode { NONE, PRACTICE, PARKING, COURSE, LAW }

/** 도로 법규 화면 상태: 몇 번째 주제(0부터)·제목·설명 글(= 음성). */
data class LawUi(val topicId: String, val index: Int, val total: Int, val title: String, val text: String, val heading: String = title, val basis: String = "", val country: String = "KR")

/**
 * 코스 주행(5-1~5-4) 단계. CHOOSE 고르기 → GENERATING 코스 계산 중(길찾기 API, 백그라운드) → READY 내비로 보내기 전
 * → ROUTING 경로 계산 중 → DRIVING 안내 중 → ARRIVED 도착 / ERROR.
 */
enum class CoursePhase { CHOOSE, GENERATING, READY, ROUTING, DRIVING, ARRIVED, ERROR }

/** 도착 후 리포트(5-4) 화면용 숫자. 문장은 Phrases(report_*) 로 따로 나간다. */
data class CourseResultUi(
    val distanceKm: Double,
    val durationMin: Double,
    val hardBrakeCount: Int,
    val overspeedCount: Int,
    val coachSpeakCount: Int,
    /** 내비가 준 도착 정보(없으면 null). */
    val arrival: NaviArrival?,
)

/**
 * 화면용 "코스 주행" 상태.
 * @param options 5-1 선택지(가장 무서운 것 / 목적). 라벨은 strings.xml 에서 ID 로 찾는다.
 * @param summary READY 에서 보여 줄 브리핑 문장(core/tts 카탈로그 문장을 이어 붙인 것).
 * @param tbtManeuver/tbtDistanceM/tbtDescription 마지막 TBT. 화면 문구("300 m 앞 우회전")는 MainActivity 가 만든다.
 * @param speedLimit 내비가 준 현재 도로 제한속도(km/h). 없으면 null.
 * @param error ERROR 단계의 이유(진단용 영문 코드 또는 짧은 설명. 길찾기 실패면 "HTTP 401: …" / "일일 호출 상한 (100회)" / "네트워크: …").
 * @param distanceKm/leftTurns/laneChanges/unprotectedLefts/rightTurns READY 에 보여 줄 코스 숫자(CourseSummary). 코스가 없으면 null.
 * @param apiCalls 이 코스를 만드는 데 쓴 길찾기 호출 수(오프셋 코스는 0).
 * @param generated true = 실제 길찾기 경로(Naver Directions), false = 오프셋 임시 코스.
 * @param fallbackAvailable ERROR 에서 "임시 코스로 진행"(오프셋 코스) 버튼을 보여 줄지. 길찾기 생성이 실패했고 오프셋 소스가 따로 있을 때.
 * @param routeDiag 길찾기 어댑터(NaverDirectionsProvider)의 진단 줄. 없으면 빈 목록.
 */
data class CourseUi(
    val phase: CoursePhase,
    val options: List<CourseOptionId>,
    val chosen: CourseOptionId?,
    val summary: String,
    val start: LatLon?,
    val destination: LatLon?,
    val waypoints: List<LatLon>,
    val routeDistanceM: Int?,
    val routeDurationS: Int?,
    val tbtManeuver: ManeuverType?,
    val tbtDistanceM: Int?,
    val tbtDescription: String?,
    val speedLimit: Int?,
    val naviReady: Boolean,
    val naviDiag: List<String>,
    val error: String?,
    val result: CourseResultUi?,
    val distanceKm: Double? = null,
    val leftTurns: Int? = null,
    val laneChanges: Int? = null,
    val unprotectedLefts: Int? = null,
    val rightTurns: Int? = null,
    val apiCalls: Int = 0,
    val generated: Boolean = false,
    val fallbackAvailable: Boolean = false,
    val routeDiag: List<String> = emptyList(),
    /** 고른 조건과 다른 점(조건을 다 맞춘 코스면 null). 예: "좌회전 4번(원함 1~2번), 거리 10.7 km(원함 6 km 이하)" */
    val mismatch: String? = null,
    /** 경로 모양 미리보기용 좌표(최대 400점). 지도·출발 위치가 없어도 어디를 도는지 보이게 */
    val routeShape: List<LatLon> = emptyList(),
    /** 난이도별 코스 생성기를 쓰는지(길찾기 키가 있을 때). false 면 난이도·시간 선택을 숨긴다. */
    val difficultyAvailable: Boolean = false,
    val difficulty: Int = 2,
    val targetMinutes: Int = 20,
)

/**
 * 화면용 "직각 주차 슬로우 모드(4-5)" 상태.
 * @param stepIndex 현재 단계(0부터). 복구 중이면 복구 절차 안에서의 번호.
 * @param stepTotal 현재 절차의 단계 수(공식 9, 복구는 3~4).
 * @param illustration 의 일러스트 키(예: "step03_gear_r_steer_right.svg"). ui/ParkingArt 가 drawable 로 바꾼다.
 * @param recovery 탈출(복구) 절차 진행 중이면 그 꼬임 종류, 아니면 null.
 * @param recoveryOptions "꼬였어요" 뒤 고를 수 있는 3가지(선택 단계에서만 비어 있지 않다).
 * @param expectedGear 체크리스트 "기어" 줄이 기다리는 기어(단계가 기어 조건이면 그 기어, 아니면 R).
 * @param needsConfirm 이 단계는 운전자 "확인"으로만 넘어간다(센서 판정 없음).
 */
data class ParkingUi(
    val phase: SlowModePhase,
    val stepIndex: Int,
    val stepTotal: Int,
    val text: String,
    val illustration: String?,
    val recovery: Tangle?,
    val recoveryOptions: List<Tangle>,
    /** 이 단계에서 보여 줄 체크 항목만(단계와 무관한 항목은 숨긴다). */
    val checks: List<ParkingCheck>,
    val needsConfirm: Boolean,
    val attempt: Int,
    /** 자기평가까지 끝남(힌트 문장이 text 에 있다). */
    val done: Boolean,
    /** 단계 문장을 구체적으로 풀어 쓴 기준점 설명(있는 단계만). */
    val detail: String? = null,
    /** 잠깐 보여 주는 보조 안내(아직 감지 안 됨·먼저 정지·처음부터 등). */
    val notice: String? = null,
    /** 직각 또는 평행 주차. */
    val kind: ParkingKind = ParkingKind.PERPENDICULAR,
)

/** 주차 체크 한 줄(라벨 + 충족 여부). */
data class ParkingCheck(val label: String, val ok: Boolean)

// ───────── CoachController 상수(이전 companion object) ─────────

internal const val COACH_TAG = "CoachController"
/** 음성 명령으로 받아들이는 문장의 최대 글자 수(띄어쓰기 제외). 이보다 길면 대화로 보고 무시한다. */
internal const val VOICE_COMMAND_MAX_CHARS = 10
/** 무응답 검사·감시견 주기. */
internal const val TICK_MS = 1_000L
/** TTS 종료 콜백이 이 시간 넘게 없으면 끝난 것으로 본다. */
internal const val SPEAK_WATCHDOG_MS = 20_000L
/** 같은 요청을 DEFER 로 미루는 최대 횟수. */
internal const val MAX_DEFERS = 8
/** 정차 판정(km/h). core ParkingSlowMode.STOPPED_SPEED_KMH 와 같다. */
internal const val STOPPED_SPEED_KMH = 0.5
/** 통과/건너뜀 화면을 최소 이만큼 보여 준 뒤 다음 항목으로(ms). */
internal const val PASS_HOLD_MS = 1_500L
/** 자동으로 다음 항목에 들어간 직후 '다음/확인' 을 무시하는 시간(ms). 넘어가는 순간 누른 버튼이 새 항목 설명을 건너뛰지 않게. */
internal const val AUTO_ADVANCE_GUARD_MS = 1_500L
/** 직각 주차 체크리스트 "속도" 줄 기준(km/h). 슬로우 모드 후진 허용 6 보다 살짝 아래의 표시용 값. */
internal const val PARKING_SPEED_OK_KMH = 5.0
/** 직각 주차 보조 안내(아직 감지 안 됨 등)를 보여 주는 시간(ms). */
internal const val PARKING_NOTICE_MS = 5_000L
/** 내비 목적지 표시 이름(화면 라벨, 음성 아님). */
internal const val COURSE_DESTINATION_LABEL = "연습 코스 종점"
