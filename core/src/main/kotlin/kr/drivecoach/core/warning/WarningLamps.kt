// 경고등 3색 분류 데이터
package kr.drivecoach.core.warning

import kr.drivecoach.core.tts.PhraseId

/**
 * 경고등이 켜졌을 때 운전자가 해야 할 행동의 심각도.
 * - GREEN: 계속 가도 됨 / 단순 표시(작동 중 알림).
 * - YELLOW: 가까운 안전한 곳에 세우거나 가까운 정비소에서 점검.
 * - RED: 지금 바로 멈춤(안전하게 즉시 정차·시동 OFF, 또는 즉시 브레이크).
 * "경고등 3색 뜻"(2-8)의 앱 내부 기준이며, 실제 계기판 램프 색과 반드시 같지는 않다
 * (예: 에어백 경고등은 램프는 빨간색이지만 할 일은 "점검"이므로 YELLOW).
 */
enum class Severity { GREEN, YELLOW, RED }

/**
 * 경고등·표시등 한 개의 데이터.
 * @param id 앱 내부 ID. VehicleState.warningLamps 의 원소와 같은 체계.
 * @param nameKo 현대차 취급설명서 기준 한국어 명칭.
 * @param severity 운전자 행동 기준 3색 분류.
 * @param action 켜졌을 때 운전자가 할 일 한 문장(음성용, 20자 내외). TTS 로는 [actionPhraseId] 로 참조한다.
 * @param description 비개발자용 설명 한 줄.
 * @param aaosPropertyHint AAOS CarPropertyManager / Vehicle SDK 에서 어떤 신호로 감지할 수 있을지 추정. 모르면 null.
 */
data class WarningLamp(
    val id: String,
    val nameKo: String,
    val severity: Severity,
    val action: String,
    val description: String,
    val aaosPropertyHint: String?,
) {
    /** 음성용 짧은 이름: 괄호·슬래시 설명을 뗀 것("과속 경고(120km/h)" → "과속 경고", "방향지시등/비상등 표시등" → "방향지시등 표시등"). `warning_lamp_on` 의 {lamp} 에 넣는다. */
    val spokenName: String get() = nameKo.replace(Regex("\\([^)]*\\)"), "").replace(Regex("/[^ ]*"), "").trim()

    /** 이 경고등의 행동 멘트 ID(`warning_action_<id 소문자>`). 문장은 [action] 과 같고 Phrases 카탈로그에 같은 ID 로 들어 있다. */
    val actionPhraseId: PhraseId get() = WarningPhraseIds.lampAction(id)
}

/**
 * 경고등 모듈이 쓰는 멘트 ID 모음. 실제 문장은 core/tts/Phrases.kt에 있다.
 * 하드코딩 멘트 금지 규약에 따라 사용자에게 들리는 문장은 이 ID 로만 참조한다.
 * "카탈로그에 이미 있음"으로 표시한 것은 Phrases 에 이미 있던 ID 를 다시 쓴 것.
 */
object WarningPhraseIds {
    /** 경고등 행동 멘트 ID. 예: warning_action_oil_pressure (경고등마다 1개). */
    fun lampAction(lampId: String): PhraseId = PhraseId("warning_action_" + lampId.lowercase())

    /** 3색 뜻 설명(2-8). 카탈로그에 이미 있음: "녹색 표시등입니다. 계속 주행해도 됩니다." 등. */
    val SEVERITY_GREEN_MEANING = PhraseId("warning_lamp_green")
    val SEVERITY_YELLOW_MEANING = PhraseId("warning_lamp_yellow")
    val SEVERITY_RED_MEANING = PhraseId("warning_lamp_red")

    /** 퀴즈 보기 문장(행동 3가지, 색 이름 없이). */
    val CHOICE_GREEN = PhraseId("warning_quiz_choice_green")
    val CHOICE_YELLOW = PhraseId("warning_quiz_choice_yellow")
    val CHOICE_RED = PhraseId("warning_quiz_choice_red")

    /** 퀴즈 시작: "경고등 퀴즈 세 문제입니다." */
    val QUIZ_INTRO = PhraseId("warning_quiz_intro")
    /** 질문: "이 경고등이 켜지면 어떻게 해야 합니까?" (카탈로그에 이미 있음, 화면에 등 아이콘·이름 표시) */
    val QUIZ_QUESTION = PhraseId("practice_dash_quiz_ask")
    /** 정답: "정답입니다." (카탈로그에 이미 있음) */
    val QUIZ_CORRECT = PhraseId("practice_dash_quiz_correct")
    /** 오답: "오답입니다. 이것은 {color} 등이므로 {action}." (카탈로그에 이미 있음; {action} 에는 [lampAction] 문장을 넣는다) */
    val QUIZ_WRONG = PhraseId("practice_dash_quiz_wrong")
    /** 결과: "{total}문제 중 {correct}문제를 맞혔습니다." */
    val QUIZ_RESULT = PhraseId("warning_quiz_result")
    /** 재출제: "틀린 문제만 다시 풉니다." */
    val QUIZ_RETRY_INTRO = PhraseId("warning_quiz_retry_intro")
    /** P 아님: "퀴즈는 정차 후 P로 변속한 뒤에 진행합니다." */
    val QUIZ_NOT_PARKED = PhraseId("warning_quiz_not_parked")
    /** 주행 중 실제 점등 시: "{lamp}이 켜졌습니다." 뒤에 [lampAction] 을 잇는다. */
    val LAMP_ON = PhraseId("warning_lamp_on")

    /** Severity → 퀴즈 보기 멘트 ID. */
    fun choice(severity: Severity): PhraseId = when (severity) {
        Severity.GREEN -> CHOICE_GREEN
        Severity.YELLOW -> CHOICE_YELLOW
        Severity.RED -> CHOICE_RED
    }

    /** Severity → 3색 뜻 멘트 ID. */
    fun meaning(severity: Severity): PhraseId = when (severity) {
        Severity.GREEN -> SEVERITY_GREEN_MEANING
        Severity.YELLOW -> SEVERITY_YELLOW_MEANING
        Severity.RED -> SEVERITY_RED_MEANING
    }

    /** `practice_dash_quiz_wrong` 의 {color} 자리에 넣을 색 이름. 멘트 조각이라 Phrases 가 아닌 여기서 관리. */
    fun colorWord(severity: Severity): String = when (severity) {
        Severity.GREEN -> "녹색"
        Severity.YELLOW -> "노란색"
        Severity.RED -> "빨간색"
    }
}

/**
 * 현대 아반떼 CN8(내연기관, 2026, 모던 트림 기준) 계기판 경고등·표시등 목록.
 * 출처: 현대차 공식 취급설명서(ownersmanual.hyundai.com), 참고.
 * 대상 차량에 없는 사양(GPF, LPG, 리어 포그 등)은 넣지 않았다.
 */
object WarningLamps {
    private const val ADAS_NA = "ADAS 상태는 앱에 열려 있지 않음(기획 0장). 표준 AAOS 속성 없음"

    /** 전체 목록. RED → YELLOW → GREEN 순으로 적었다. */
    val all: List<WarningLamp> = listOf(
        // ---------- RED: 지금 바로 멈춤 ----------
        WarningLamp(
            id = "SEAT_BELT", nameKo = "안전벨트 미착용 경고등", severity = Severity.RED,
            action = "안전벨트를 먼저 매십시오.",
            description = "운전석 안전벨트 미착용. 20km/h 이상이면 깜빡이며 경고음. 주행 중이면 안전한 곳에 세우고 착용.",
            aaosPropertyHint = "VehiclePropertyIds.SEAT_BELT_BUCKLED(운전석) → VehicleState.seatBeltWarning",
        ),
        WarningLamp(
            id = "BRAKE", nameKo = "브레이크 경고등", severity = Severity.RED,
            action = "급제동하지 말고 천천히 정차하십시오.",
            description = "주차브레이크를 풀었는데도 켜지면 브레이크액 부족·브레이크 계통 이상. 엔진브레이크로 서서히 정차 후 점검, 운행 금지.",
            aaosPropertyHint = "VehiclePropertyIds.PARKING_BRAKE_ON=false 인데 점등이면 브레이크액 경고로 해석; Vehicle SDK Brake 도메인과 교차 확인",
        ),
        WarningLamp(
            id = "OIL_PRESSURE", nameKo = "엔진 오일 압력 경고등", severity = Severity.RED,
            action = "즉시 정차하고 시동을 끄십시오.",
            description = "엔진 오일 유압 저하. 엔진 보호를 위해 출력이 제한됨. 계속 달리면 엔진 손상. 정차 후 오일량 확인·보충, 그래도 켜지면 견인.",
            aaosPropertyHint = "VehiclePropertyIds.ENGINE_OIL_LEVEL=CRITICALLY_LOW 로 간접 추정(유압 자체 속성은 없음)",
        ),
        WarningLamp(
            id = "CHARGING", nameKo = "충전 경고등", severity = Severity.RED,
            action = "안전하게 정차 후 시동을 끄십시오.",
            description = "발전기·충전 장치 이상(드라이브 벨트 끊김 등). 계속 달리면 과열·배터리 방전으로 차가 멈출 수 있음. 긴급출동 요청.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "COOLANT_TEMP_HIGH", nameKo = "엔진 과열(냉각수 온도계 H 구간)", severity = Severity.RED,
            action = "과열, 정차 후 시동을 끄십시오.",
            description = "경고등이 아니라 냉각수 온도 게이지 바늘이 H(빨간 구간)에 닿은 상태. 보닛을 바로 열지 말고 식힌 뒤 긴급출동(기획 7장 과열 대응).",
            aaosPropertyHint = "VehiclePropertyIds.ENGINE_COOLANT_TEMP(℃) 임계값 초과",
        ),
        WarningLamp(
            id = "FCA_ACTIVE", nameKo = "전방 안전 경고등(빨간색 깜빡임)", severity = Severity.RED,
            action = "즉시 브레이크를 밟으십시오.",
            description = "전방 충돌방지 보조가 충돌 위험을 감지하면 빨간색으로 깜빡이며 경고음. 즉시 감속·정지.",
            aaosPropertyHint = ADAS_NA,
        ),

        // ---------- YELLOW: 가까운 안전한 곳에 세우거나 점검 ----------
        WarningLamp(
            id = "AIRBAG", nameKo = "에어백 경고등", severity = Severity.YELLOW,
            action = "가까운 정비소에서 점검을 받으십시오.",
            description = "에어백·안전벨트 프리텐셔너 이상. 시동 후 3~6초 뒤에도 켜져 있으면 사고 시 안 터질 수 있으니 점검.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "DOOR_OPEN", nameKo = "도어 열림 표시등", severity = Severity.YELLOW,
            action = "정차하고 도어를 닫으십시오.",
            description = "문이 열려 있거나 덜 닫힘. 출발 전이면 모두 닫고 소등 확인. 켜진 채 차를 떠나면 배터리 방전.",
            aaosPropertyHint = "VehiclePropertyIds.DOOR_POS(문별) / Vehicle SDK Door 도메인",
        ),
        WarningLamp(
            id = "EPB_FAULT", nameKo = "전자식 주차브레이크(EPB) 경고등", severity = Severity.YELLOW,
            action = "P로 변속하고 점검을 받으십시오.",
            description = "EPB 이상. 시동 후 3초 뒤에도 켜지거나 주행 중 켜지면 점검. ESC 표시등과 함께 켜지면 ESC 미작동 알림.",
            aaosPropertyHint = "VehiclePropertyIds.PARKING_BRAKE_ON / PARKING_BRAKE_AUTO_APPLY; Vehicle SDK Brake 도메인 parkingBrake",
        ),
        WarningLamp(
            id = "ABS", nameKo = "ABS 경고등", severity = Severity.YELLOW,
            action = "급제동을 피하고 점검을 받으십시오.",
            description = "ABS 고장. 일반 브레이크는 작동함. 안전한 곳에 정차 후 재시동해 확인, 계속 켜지면 점검.",
            aaosPropertyHint = "VehiclePropertyIds.ABS_ACTIVE 는 작동 여부만 제공, 고장 여부 속성은 없음",
        ),
        WarningLamp(
            id = "EBD", nameKo = "EBD 경고(브레이크+ABS 동시 점등)", severity = Severity.YELLOW,
            action = "감속하고 즉시 점검을 받으십시오.",
            description = "앞뒤 제동력 배분(EBD) 이상. 고속 주행·급제동 금지. 속도계가 안 보이거나 스티어링 휠이 무거워질 수 있음. 즉시 점검.",
            aaosPropertyHint = "BRAKE 와 ABS 램프 동시 점등으로 파생",
        ),
        WarningLamp(
            id = "MDPS", nameKo = "전동 파워 스티어링(MDPS) 경고등", severity = Severity.YELLOW,
            action = "조향이 무겁습니다. 점검받으십시오.",
            description = "조향 보조 이상. 스티어링 휠이 무거워질 수 있으니 천천히 운전해 가까운 정비소에서 점검.",
            aaosPropertyHint = "VehiclePropertyIds.PERF_STEERING_ANGLE 값 소실로 간접 추정",
        ),
        WarningLamp(
            id = "MIL", nameKo = "엔진 경고등", severity = Severity.YELLOW,
            action = "주유구 캡 확인 후 점검받으십시오.",
            description = "엔진 제어·배기·연료 장치 이상. 연료 캡이 헐거워도 켜지니 '딸깍' 소리 나게 잠그고 확인. 깜빡이면 출력 줄여 바로 정비소.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "OIL_LEVEL", nameKo = "엔진 오일 레벨 경고등", severity = Severity.YELLOW,
            action = "엔진 오일을 보충하십시오.",
            description = "엔진 오일량 부족. 가까운 곳에서 규정 오일을 F 선까지 보충. 보충 후에도 켜지면 점검.(사양에 따라 없음)",
            aaosPropertyHint = "VehiclePropertyIds.ENGINE_OIL_LEVEL=LOW",
        ),
        WarningLamp(
            id = "LOW_FUEL", nameKo = "연료 부족 경고등", severity = Severity.YELLOW,
            action = "가까운 주유소에서 주유하십시오.",
            description = "연료가 거의 없음. 완전히 소모하면 엔진·촉매 손상. 기획 6장 주유소 연습과 연결.",
            aaosPropertyHint = "VehiclePropertyIds.FUEL_LEVEL_LOW / FUEL_LEVEL → VehicleState.fuelPercent",
        ),
        WarningLamp(
            id = "TPMS", nameKo = "저압 타이어(TPMS) 경고등", severity = Severity.YELLOW,
            action = "정차하고 타이어 상태를 확인하십시오.",
            description = "타이어 공기압이 현저히 낮음(계기판에 바퀴 위치 표시). 약 1분 깜빡인 뒤 켜지면 TPMS 고장. 차가 흔들리면 서서히 감속해 정차.",
            aaosPropertyHint = "VehiclePropertyIds.TIRE_PRESSURE(바퀴별) / CRITICALLY_LOW_TIRE_PRESSURE",
        ),
        WarningLamp(
            id = "MASTER_WARNING", nameKo = "통합 경고등", severity = Severity.YELLOW,
            action = "클러스터의 메시지를 확인하십시오.",
            description = "충돌방지 보조·크루즈·LED 램프·TPMS 등 고장 또는 센서 가림. 계기판 상세 메시지를 보고 해당 항목 조치.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "ESC_FAULT", nameKo = "ESC 경고등(계속 켜짐)", severity = Severity.YELLOW,
            action = "미끄럼에 주의하고 점검을 받으십시오.",
            description = "차체 자세 제어 장치 고장. 시동 3초 뒤에도 켜지거나 주행 중 켜지면 점검.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "AUTO_HOLD_FAULT", nameKo = "오토 홀드 표시등(노란색)", severity = Severity.YELLOW,
            action = "직접 제동하고 점검을 받으십시오.",
            description = "자동 정차 기능 이상. 정차 시 브레이크를 직접 밟고 유지. 주행 중 노란색이 계속되면 점검.",
            aaosPropertyHint = "Vehicle SDK Brake 도메인(autoHold 상태 추정)",
        ),
        WarningLamp(
            id = "ISG_FAULT", nameKo = "공회전 제한(ISG) 표시등(노란색)", severity = Severity.YELLOW,
            action = "정비소에서 점검을 받으십시오.",
            description = "ISG 시스템 이상. 운행은 가능하나 점검.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "EXT_LAMP_FAULT", nameKo = "외부 램프 고장 경고등", severity = Severity.YELLOW,
            action = "전구를 점검하거나 교체하십시오.",
            description = "전조등·제동등·방향지시등 등 외부 전구 단선. 같은 용량 전구로 교체 또는 정비소.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "LED_HEADLAMP_FAULT", nameKo = "LED 전조등 고장 경고등", severity = Severity.YELLOW,
            action = "전조등 점검을 받으십시오.",
            description = "LED 전조등 이상(켜짐) 또는 부품 이상(깜빡임). 계속 주행하면 수명 단축. 야간 주행 전 점검.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "FCA_FAULT", nameKo = "전방 안전 경고등(노란색 지속)", severity = Severity.YELLOW,
            action = "센서를 닦고 점검을 받으십시오.",
            description = "전방 충돌방지 보조 미설정·이상·고장. 앞 범퍼 레이더·카메라 이물질 제거 후에도 켜지면 점검.",
            aaosPropertyHint = ADAS_NA,
        ),
        WarningLamp(
            id = "LKA_FAULT", nameKo = "차로 안전 표시등(노란색)", severity = Severity.YELLOW,
            action = "카메라를 닦고 점검을 받으십시오.",
            description = "차로 이탈방지 보조 미설정·이상·고장. 앞유리 카메라 앞 이물질 제거 후에도 켜지면 점검.",
            aaosPropertyHint = ADAS_NA,
        ),
        WarningLamp(
            id = "DAW", nameKo = "부주의 운전 경고 표시등", severity = Severity.YELLOW,
            action = "가까운 곳에서 휴식하십시오.",
            description = "노란색 깜빡임=휴식 권고(졸음·부주의). 노란색 지속=카메라 가림·고장이면 점검.",
            aaosPropertyHint = ADAS_NA,
        ),

        // ---------- GREEN: 계속 가도 됨 / 단순 표시 ----------
        WarningLamp(
            id = "ESC_ACTIVE", nameKo = "ESC 작동 표시등(깜빡임)", severity = Severity.GREEN,
            action = "미끄럽습니다. 속도를 줄이십시오.",
            description = "차체 자세 제어 장치가 작동 중(미끄러짐 감지). 스티어링 휠 급조작 없이 부드럽게 감속.",
            aaosPropertyHint = "VehiclePropertyIds.TRACTION_CONTROL_ACTIVE",
        ),
        WarningLamp(
            id = "ESC_OFF", nameKo = "ESC 작동 정지 표시등", severity = Severity.GREEN,
            action = "ESC 버튼을 눌러 다시 켜십시오.",
            description = "ESC OFF 버튼으로 끈 상태. 초보 운전자는 항상 켜 두는 것을 권장.",
            aaosPropertyHint = "VehiclePropertyIds.ELECTRONIC_STABILITY_CONTROL_ENABLED(AAOS 15+ 추정)",
        ),
        WarningLamp(
            id = "AUTO_HOLD", nameKo = "오토 홀드 표시등(흰색/초록색)", severity = Severity.GREEN,
            action = "오토 홀드가 작동 중입니다.",
            description = "흰색=준비, 초록색=정차 유지 중. 가속 페달을 밟으면 자동 해제.",
            aaosPropertyHint = "Vehicle SDK Brake 도메인(autoHold 상태 추정)",
        ),
        WarningLamp(
            id = "ISG", nameKo = "공회전 제한(ISG) 표시등(초록색)", severity = Severity.GREEN,
            action = "엔진 일시 정지, 정상입니다.",
            description = "정차 중 연료 절약을 위해 엔진이 자동 정지한 상태. 브레이크를 떼면 자동 재시동.",
            aaosPropertyHint = "VehiclePropertyIds.ENGINE_RPM=0 이면서 IGNITION_STATE=ON 으로 간접 추정",
        ),
        WarningLamp(
            id = "KEY", nameKo = "키 확인 표시등", severity = Severity.GREEN,
            action = "스마트 키를 차 안에 두십시오.",
            description = "켜짐=스마트 키 인식, 시동 가능. 깜빡임=키가 차 안에 없음. 계속 깜빡이면 점검.",
            aaosPropertyHint = "VehiclePropertyIds.IGNITION_STATE",
        ),
        WarningLamp(
            id = "TURN_SIGNAL", nameKo = "방향지시등/비상등 표시등", severity = Severity.GREEN,
            action = "회전 후 꺼졌는지 확인하십시오.",
            description = "방향지시등·비상등 작동 중. 비정상으로 빠르게 깜빡이면 전구 단선이니 점검.",
            aaosPropertyHint = "VehiclePropertyIds.TURN_SIGNAL_STATE → VehicleState.turnSignal / hazardOn",
        ),
        WarningLamp(
            id = "HIGH_BEAM", nameKo = "전조등 상향 표시등", severity = Severity.GREEN,
            action = "마주 오는 차가 있으면 하향등.",
            description = "상향등 켜짐. 마주 오는 차 운전자의 시야를 방해하니 시내에서는 하향 사용.",
            aaosPropertyHint = "VehiclePropertyIds.HIGH_BEAM_LIGHTS_STATE → VehicleState.headlight=HIGH",
        ),
        WarningLamp(
            id = "LIGHT_ON", nameKo = "미등/전조등 표시등", severity = Severity.GREEN,
            action = "전조등이 켜져 있습니다.",
            description = "미등 또는 전조등 켜짐. AUTO 위치면 어두워질 때 자동 점등.",
            aaosPropertyHint = "VehiclePropertyIds.HEADLIGHTS_STATE → VehicleState.headlight",
        ),
        WarningLamp(
            id = "HBA", nameKo = "하이빔 보조 표시등", severity = Severity.GREEN,
            action = "상향등 자동 전환이 작동 중입니다.",
            description = "흰색=준비, 초록색=작동 중. 앞차·마주 오는 차에 따라 자동 전환.",
            aaosPropertyHint = ADAS_NA,
        ),
        WarningLamp(
            id = "LKA", nameKo = "차로 안전 표시등(회색/초록색)", severity = Severity.GREEN,
            action = "차로 유지 보조가 작동 중입니다.",
            description = "회색=조건 미충족, 초록색=준비, 초록색 깜빡임=차로 이탈 방지 작동 중. 스티어링 휠은 계속 잡기.",
            aaosPropertyHint = ADAS_NA,
        ),
        WarningLamp(
            id = "CRUISE", nameKo = "크루즈 표시등", severity = Severity.GREEN,
            action = "크루즈 작동 중, 차간 거리 확인.",
            description = "크루즈 컨트롤 켜짐. 브레이크를 밟으면 해제.",
            aaosPropertyHint = "VehiclePropertyIds.CRUISE_CONTROL_ENABLED(AAOS 14+)",
        ),
        WarningLamp(
            id = "DRIVE_MODE", nameKo = "주행 모드 표시등(ECO/SPORT/SMART)", severity = Severity.GREEN,
            action = "주행 모드 표시등입니다.",
            description = "선택한 드라이브 모드 표시. 초보 운전자는 NORMAL/ECO 권장.",
            aaosPropertyHint = null,
        ),
        WarningLamp(
            id = "ICY_ROAD", nameKo = "도로 결빙 경고등", severity = Severity.GREEN,
            action = "결빙 주의, 천천히 주행하십시오.",
            description = "외기 온도 약 4℃ 이하. 경고음 1회 후 켜짐. 과속·급가속·급제동·급회전 피하기.",
            aaosPropertyHint = "VehiclePropertyIds.ENV_OUTSIDE_TEMPERATURE ≤ 4℃",
        ),
        WarningLamp(
            id = "OVER_SPEED", nameKo = "과속 경고(120km/h)", severity = Severity.GREEN,
            action = "속도를 줄이십시오.",
            description = "120km/h 초과 시 깜빡이며 약 5초 경고음. 고속도로 제한속도 이내로 감속.",
            aaosPropertyHint = "VehiclePropertyIds.PERF_VEHICLE_SPEED > 120",
        ),
    )

    private val byIdMap: Map<String, WarningLamp> = all.associateBy { it.id }

    /** ID 로 경고등을 찾는다. 모르는 ID 면 null. */
    fun byId(id: String): WarningLamp? = byIdMap[id]

    /** 심각도별 목록. */
    fun bySeverity(severity: Severity): List<WarningLamp> = all.filter { it.severity == severity }

    /**
     * 현재 켜진 경고등 ID 집합(VehicleState.warningLamps)에 해당하는 경고등을 RED → YELLOW → GREEN 순으로 돌려준다.
     * 같은 심각도 안에서는 [all] 의 순서를 유지한다. 모르는 ID 는 무시한다.
     */
    fun forVehicleState(lampIds: Set<String>): List<WarningLamp> =
        all.filter { it.id in lampIds }.sortedByDescending { it.severity.ordinal }
}
