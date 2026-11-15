// 담당: D (차량 조작법 2-1~2-9 연습 항목 데이터)
package kr.drivecoach.core.practice

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.Headlight
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.signal.Wiper
import kr.drivecoach.core.tts.PhraseId

/**
 * 차량 옵션(capability) 이름. 앱은 기준 차량의 옵션 집합([Chapter2Items.AVANTE_CN8_MODERN])을
 * [Chapter2Items.itemsFor] 에 넘긴다. 없는 옵션의 항목은 아예 보여주지 않는다.
 */
object Capability {
    const val SEAT_POWER = "seat.power"             // 전동 시트(시트 위치 신호 감지 가능)
    const val STEERING_TILT = "steering.tilt"
    const val STEERING_TELESCOPIC = "steering.telescopic"
    const val EPB = "brake.epb"                     // 전자식 주차브레이크
    const val AUTO_HOLD = "brake.autohold"
    const val FOG_LIGHT = "light.fog"
    const val REAR_WIPER = "wiper.rear"
    const val WINDOW_LOCK = "window.lock"
    const val LKA = "adas.lka"                      // 차로 이탈 경고/유지 보조
    const val FCA = "adas.fca"                      // 전방 충돌 보조
    const val BCW = "adas.bcw"                      // 후측방 경고
    const val CRUISE = "adas.cruise"                // 크루즈 컨트롤(일반/스마트)
}

/**
 * 기어를 P→R→N→D 순서로 모두 거쳤는지 추적하는 누적 판정(2-4).
 * 순서가 어긋나면(예: P→D) 처음부터 다시 센다. 브레이크를 밟은 채 변속해야 한다는 안내는 멘트로만 한다
 * (변속 자체가 브레이크 없이는 안 되므로 별도 판정하지 않음).
 */
class GearSequenceCheck : StatefulCheck {
    private val sequence = listOf(Gear.P, Gear.R, Gear.N, Gear.D)
    private var index = 0 // 다음에 기대하는 기어의 인덱스

    override fun reset() { index = 0 }

    override fun onState(prev: VehicleState, now: VehicleState): Boolean {
        if (now.gear == prev.gear) return false
        if (index == 0) {
            // 시작은 P 에 있는 상태. P 에서 R 로 가면 P, R 모두 인정.
            if (now.gear == Gear.R && prev.gear == Gear.P) index = 2
            return false
        }
        if (now.gear == sequence[index]) {
            index++
            return index == sequence.size
        }
        // 순서 이탈: P 로 돌아가면 처음부터, 아니면 유지(실수로 건너뛴 경우 다시 올 수 있게)
        if (now.gear == Gear.P) index = 0
        return false
    }

    // (11/15 베타 6f) 화면은 "지금 할 것: …" 으로 보여 준다 → 한 것만 늘어놓지 않고 다음에 넣을 기어를 말한다
    override fun progress(): String = when {
        index == 0 -> "P에서 R로"
        index < sequence.size -> sequence.take(index).joinToString("→") + " 다음 " + sequence[index].name
        else -> sequence.joinToString("→")
    }
}

/** 차량 조작법 전 항목 데이터. 감지 가능/불가는 [PracticeItem.detectable] 로 표기된다. */
object Chapter2Items {

    private fun p(itemId: String, kind: String) = PhraseId("practice_${itemId.replace('-', '_')}_$kind")

    /** 항목 생성 도우미. 멘트 ID 는 practice_<id(하이픈→밑줄)>_explain / _try / _pass / _confirm 규칙(K 의 snake_case 규약). */
    private fun item(
        id: String, chapter: String, title: String, check: CheckKind, cap: String? = null,
    ) = PracticeItem(id, chapter, title, p(id, "explain"), p(id, "try"), p(id, "pass"), check, cap)

    private fun manual(id: String) = CheckKind.Manual(p(id, "confirm"))
    private fun either(id: String, auto: CheckKind) = CheckKind.Either(auto, manual(id))
    private fun auto(pred: (VehicleState, VehicleState) -> Boolean) = CheckKind.Auto(pred)

    // ---- 2-1 운전석 체형 맞추기 ---------------------------------------------------------------
    private val seatFore = "2-1-seat-fore-aft"
    private val seatBack = "2-1-seat-backrest"
    private val seatHeight = "2-1-seat-height"
    private val tilt = "2-1-steering-tilt"
    private val telescopic = "2-1-steering-telescopic"
    private val posture = "2-1-posture"
    private val headrest = "2-1-headrest"
    private val sideMirror = "2-1-side-mirror"
    private val roomMirror = "2-1-room-mirror"
    private val blindSpot = "2-1-blind-spot"

    // ---- 2-2 안전벨트 -------------------------------------------------------------------------
    private val belt = "2-2-seatbelt"
    private val beltWrong = "2-2-seatbelt-wrong-examples"

    // ---- 2-3 시동과 주차브레이크 ----------------------------------------------------------------
    private val ignition = "2-3-ignition"
    private val epbRelease = "2-3-epb-release"
    private val epbApply = "2-3-epb-apply"
    private val autoHoldOn = "2-3-autohold-on"
    private val autoHoldOff = "2-3-autohold-off"

    // ---- 2-4 기어와 페달 ----------------------------------------------------------------------
    private val gearSeq = "2-4-gear-sequence"
    private val gearMeaning = "2-4-gear-meaning"
    private val neutralUse = "2-4-neutral-use"
    private val pedalPosition = "2-4-pedal-position"
    private val suddenAccel = "2-4-sudden-accel"

    // ---- 2-5 등화 ---------------------------------------------------------------------------
    private val lightAuto = "2-5-headlight-auto"
    private val lightLow = "2-5-headlight-low"
    private val lightHigh = "2-5-headlight-high"
    private val passing = "2-5-passing"
    private val fog = "2-5-fog-light"
    private val hazard = "2-5-hazard"
    private val horn = "2-5-horn"
    private val triangle = "2-5-warning-triangle"
    private val interior = "2-5-interior-light"

    // ---- 2-6 와이퍼·방향지시등 ------------------------------------------------------------------
    private val wiper = "2-6-wiper"
    private val washer = "2-6-washer"
    private val rearWiper = "2-6-rear-wiper"
    private val turnRight = "2-6-turn-signal-right"
    private val turnLeft = "2-6-turn-signal-left"
    private val laneTap = "2-6-lane-change-tap"

    // ---- 2-7 창문·도어락·공조 -------------------------------------------------------------------
    private val window = "2-7-window"
    private val windowLock = "2-7-window-lock"
    private val doorLock = "2-7-door-lock"
    private val hvac = "2-7-hvac"
    private val defog = "2-7-defog"
    private val recirc = "2-7-air-recirculation"

    // ---- 2-8 계기판과 경고등 --------------------------------------------------------------------
    private val speedo = "2-8-speedometer"
    private val rpmGauge = "2-8-rpm"
    private val fuelGauge = "2-8-fuel"
    private val trip = "2-8-trip"
    private val lampColors = "2-8-warning-lamp-colors"

    // ---- 2-9 운전자 보조 기능 -------------------------------------------------------------------
    private val lka = "2-9-lka"
    private val fca = "2-9-fca"
    private val bcw = "2-9-bcw"
    private val cruise = "2-9-cruise"

    /** 2장 전 항목(옵션 필터 전). 순서가 곧 연습 순서다. */
    val all: List<PracticeItem> = listOf(
        // 2-1 (대부분 감지 불가 → 사용자 확인. 시트 앞뒤는 전동시트면 위치 변화 이벤트로 자동 통과)
        item(seatFore, "2-1", "좌석 앞뒤 조절", either(seatFore, auto { _, now -> now.seatPositionChanged })),
        item(seatBack, "2-1", "등받이 각도 조절", either(seatBack, auto { _, now -> now.seatPositionChanged })),
        item(seatHeight, "2-1", "좌석 높이 조절", either(seatHeight, auto { _, now -> now.seatPositionChanged })),
        item(tilt, "2-1", "스티어링 휠 틸트(상하) 조절", manual(tilt), Capability.STEERING_TILT),
        item(telescopic, "2-1", "스티어링 휠 텔레스코픽(앞뒤) 조절", manual(telescopic), Capability.STEERING_TELESCOPIC),
        item(posture, "2-1", "올바른 자세와 피해야 할 자세", manual(posture)),
        item(headrest, "2-1", "머리받침 높이", manual(headrest)),
        item(sideMirror, "2-1", "실외 미러 맞추기", manual(sideMirror)),
        item(roomMirror, "2-1", "실내 미러 맞추기", manual(roomMirror)),
        item(blindSpot, "2-1", "사각지대 이해", manual(blindSpot)),

        // 2-2 (버클 체결은 경고등 꺼짐으로 감지, "제대로 맸는지"는 감지 불가 → Either)
        item(belt, "2-2", "안전벨트 매기", either(belt, auto { prev, now -> prev.seatBeltWarning == true && now.seatBeltWarning == false })),
        item(beltWrong, "2-2", "잘못된 착용 예시", manual(beltWrong)),

        // 2-3
        item(ignition, "2-3", "시동 걸기", either(ignition, auto { prev, now -> prev.ignitionOn == false && now.ignitionOn == true })),
        // 신호가 없거나(null) 이미 그 상태면 센서로는 영영 통과할 수 없으므로 수동 확인도 허용(베타 A-06/D-24)
        item(epbRelease, "2-3", "전자식 주차 브레이크 풀기", either(epbRelease, auto { prev, now -> prev.parkingBrakeOn == true && now.parkingBrakeOn == false }), Capability.EPB),
        item(epbApply, "2-3", "전자식 주차 브레이크 걸기", either(epbApply, auto { prev, now -> prev.parkingBrakeOn == false && now.parkingBrakeOn == true }), Capability.EPB),
        item(autoHoldOn, "2-3", "오토 홀드 켜기", manual(autoHoldOn), Capability.AUTO_HOLD),   // 오토홀드 신호 없음 → 수동
        item(autoHoldOff, "2-3", "오토 홀드 끄기", manual(autoHoldOff), Capability.AUTO_HOLD),

        // 2-4
        item(gearSeq, "2-4", "기어 바꾸기", CheckKind.Stateful { GearSequenceCheck() }),
        item(gearMeaning, "2-4", "각 기어의 의미", manual(gearMeaning)),
        item(neutralUse, "2-4", "중립 기어를 쓸 때", manual(neutralUse)),
        item(pedalPosition, "2-4", "페달 위치 및 오른발만 쓰기", manual(pedalPosition)),          // 엑셀 감지 불가
        // 급발진·헛밟음: 상세 흐름(30초 설명·퀴즈·복습)은 SuddenAccelDrill 이 담당. 여기서는 동작 감지만.
        item(suddenAccel, "2-4", "급발진 및 헛밟음 대처", CheckKind.Stateful { SuddenAccelDrill.ActionCheck() }),

        // 2-5
        item(lightAuto, "2-5", "전조등 오토", either(lightAuto, auto { prev, now -> prev.headlight != Headlight.AUTO && now.headlight == Headlight.AUTO })),
        item(lightLow, "2-5", "전조등 하향등", auto { prev, now -> prev.headlight != Headlight.LOW && now.headlight == Headlight.LOW }),
        item(lightHigh, "2-5", "상향등", auto { _, now -> now.headlight == Headlight.HIGH }),
        item(passing, "2-5", "패싱(상향등 깜빡)", manual(passing)),                        // 순간 동작, 샘플링에 안 잡힐 수 있어 수동
        item(fog, "2-5", "안개등", manual(fog), Capability.FOG_LIGHT),
        item(hazard, "2-5", "비상 경고등", either(hazard, auto { prev, now -> prev.hazardOn != true && now.hazardOn == true })),   // Pleos 에 비상등 신호 없음 → 수동 확인 허용
        item(horn, "2-5", "경음기", manual(horn)),
        item(triangle, "2-5", "안전 삼각대", manual(triangle)),
        item(interior, "2-5", "실내등", manual(interior)),

        // 2-6
        item(wiper, "2-6", "와이퍼 단계", auto { prev, now -> prev.wiper != now.wiper && now.wiper != Wiper.OFF && now.wiper != Wiper.UNKNOWN }),
        item(washer, "2-6", "워셔액", manual(washer)),
        item(rearWiper, "2-6", "뒷유리 와이퍼", manual(rearWiper), Capability.REAR_WIPER),
        item(turnRight, "2-6", "방향지시등 오른쪽(레버 위)", auto { prev, now -> prev.turnSignal != TurnSignal.RIGHT && now.turnSignal == TurnSignal.RIGHT }),
        item(turnLeft, "2-6", "방향지시등 왼쪽(레버 아래)", auto { prev, now -> prev.turnSignal != TurnSignal.LEFT && now.turnSignal == TurnSignal.LEFT }),
        item(laneTap, "2-6", "원터치 방향지시등", manual(laneTap)),                     // 반 누름 여부는 신호로 구분 불가

        // 2-7
        item(window, "2-7", "창문 내리고 올리기", either(window, auto { prev, now -> prev.windowsClosed != null && now.windowsClosed != null && prev.windowsClosed != now.windowsClosed })),
        item(windowLock, "2-7", "창문 잠금", manual(windowLock), Capability.WINDOW_LOCK),
        item(doorLock, "2-7", "도어락", either(doorLock, auto { prev, now -> prev.doorsLocked != null && now.doorsLocked != null && prev.doorsLocked != now.doorsLocked })),
        item(hvac, "2-7", "에어컨 및 히터 켜기", either(hvac, auto { prev, now -> prev.hvacOn != null && now.hvacOn != null && prev.hvacOn != now.hvacOn })),
        item(defog, "2-7", "앞유리 김서림 제거", manual(defog)),
        item(recirc, "2-7", "내기 및 외기 전환", manual(recirc)),

        // 2-8 (읽는 법 설명. 경고등 3색 퀴즈는 warning 모듈)
        item(speedo, "2-8", "속도계", manual(speedo)),
        item(rpmGauge, "2-8", "엔진 회전계", manual(rpmGauge)),
        item(fuelGauge, "2-8", "연료 게이지", manual(fuelGauge)),
        item(trip, "2-8", "트립 컴퓨터", manual(trip)),
        item(lampColors, "2-8", "경고등 3색의 뜻", manual(lampColors)),

        // 2-9 (ADAS 상태는 앱에 열려 있지 않음 → 모두 수동, 옵션 있을 때만)
        item(lka, "2-9", "차로 이탈 경고 및 유지 보조", manual(lka), Capability.LKA),
        item(fca, "2-9", "전방 충돌 보조", manual(fca), Capability.FCA),
        item(bcw, "2-9", "후측방 경고", manual(bcw), Capability.BCW),
        item(cruise, "2-9", "크루즈 컨트롤", manual(cruise), Capability.CRUISE),
    )

    /** 차량 옵션에 맞는 항목만. requiredCapability 가 null 이거나 capabilities 에 있으면 포함. */
    fun itemsFor(capabilities: Set<String>): List<PracticeItem> =
        all.filter { it.requiredCapability == null || it.requiredCapability in capabilities }

    /** 검색용 별칭(초보가 쓰는 말). 항목 ID → 단어들. 선택 화면 검색은 제목·별칭을 먼저, 장 제목은 그 다음에 본다(베타 A-11/D-25). */
    val aliases: Map<String, List<String>> = mapOf(
        "2-6-turn-signal-right" to listOf("깜빡이", "깜박이", "방향지시등", "우측", "오른쪽"),
        "2-6-turn-signal-left" to listOf("깜빡이", "깜박이", "방향지시등", "좌측", "왼쪽"),
        "2-6-lane-change-tap" to listOf("깜빡이", "반누름", "차선"),
        "2-5-hazard" to listOf("비상등", "비상 경고등", "비상깜빡이", "비상 깜빡이", "삼각", "경고등"),
        "2-5-horn" to listOf("클랙슨", "크락션", "빵빵", "경적"),
        "2-5-warning-triangle" to listOf("삼각대", "고장", "비상", "트렁크"),
        "2-5-headlight-high" to listOf("쌍라이트", "하이빔", "라이트", "상향"),
        "2-5-headlight-low" to listOf("라이트", "하향", "전조등"),
        "2-5-headlight-auto" to listOf("라이트", "오토", "자동"),
        "2-5-passing" to listOf("패싱", "라이트", "상향등"),
        "2-5-fog-light" to listOf("안개등", "포그"),
        "2-3-epb-release" to listOf("주차 브레이크", "사이드브레이크", "사이드", "주차브레이크", "EPB", "핸드브레이크"),
        "2-3-epb-apply" to listOf("주차 브레이크", "사이드브레이크", "사이드", "주차브레이크", "EPB", "핸드브레이크"),
        "2-3-autohold-on" to listOf("오토홀드", "홀드"),
        "2-3-autohold-off" to listOf("오토홀드", "홀드"),
        "2-3-ignition" to listOf("시동", "스타트", "버튼"),
        "2-4-pedal-position" to listOf("액셀", "엑셀", "가속", "페달", "브레이크"),
        "2-4-sudden-accel" to listOf("급발진", "액셀", "엑셀", "헛밟"),
        "2-4-gear-sequence" to listOf("기어", "변속", "PRND"),
        "2-4-gear-meaning" to listOf("기어", "변속", "PRND", "중립"),
        "2-4-neutral-use" to listOf("기어", "중립", "N단", "세차"),
        "2-7-hvac" to listOf("에어컨", "히터", "공조", "냉방", "난방", "온도"),
        "2-7-defog" to listOf("김서림", "김", "성에", "디포그", "서리"),
        "2-7-air-recirculation" to listOf("내기", "외기", "환기", "순환"),
        "2-7-window" to listOf("창문", "유리", "윈도우"),
        "2-7-window-lock" to listOf("창문", "잠금", "윈도우락"),
        "2-7-door-lock" to listOf("도어락", "문", "잠금"),
        "2-6-wiper" to listOf("와이퍼", "비", "유리"),
        "2-6-washer" to listOf("워셔", "워셔액", "와이퍼", "유리"),
        "2-6-rear-wiper" to listOf("와이퍼", "뒷유리", "후면"),
        "2-1-side-mirror" to listOf("사이드미러", "실외 미러", "거울", "미러"),
        "2-1-room-mirror" to listOf("룸미러", "실내 미러", "거울", "미러", "백미러"),
        "2-1-blind-spot" to listOf("사각", "사각지대", "미러"),
        "2-2-seatbelt" to listOf("벨트", "안전벨트", "안전띠"),
        "2-2-seatbelt-wrong-examples" to listOf("벨트", "안전벨트", "안전띠"),
        "2-8-speedometer" to listOf("속도계", "계기판", "클러스터", "속도"),
        "2-8-rpm" to listOf("RPM", "알피엠", "회전계", "계기판", "클러스터", "엔진"),
        "2-8-fuel" to listOf("연료", "기름", "주유", "계기판", "클러스터"),
        "2-8-trip" to listOf("트립", "주행거리", "연비", "계기판", "클러스터"),
        "2-8-warning-lamp-colors" to listOf("경고등", "계기판", "클러스터", "빨간불", "노란불"),
        "2-9-lka" to listOf("차로", "차선", "이탈", "LKA"),
        "2-9-fca" to listOf("충돌", "전방", "FCA", "긴급제동"),
        "2-9-bcw" to listOf("후측방", "사각", "BCW"),
        "2-9-cruise" to listOf("크루즈", "정속", "스마트크루즈"),
    )

    /** 장 번호 → 화면 제목. 연습 선택 화면의 묶음 제목·검색에 쓴다. */
    val chapterTitles: Map<String, String> = linkedMapOf(
        "2-1" to "운전석 체형 맞추기",
        "2-2" to "안전벨트",
        "2-3" to "시동과 주차 브레이크",
        "2-4" to "기어와 페달",
        "2-5" to "등화",
        "2-6" to "와이퍼 및 방향지시등",
        "2-7" to "창문, 도어락, 공조",
        "2-8" to "클러스터와 경고등",
        "2-9" to "운전자 보조 기능",
    )

    /** 특정 장(예: "2-5")의 항목만. */
    fun chapter(chapter: String, capabilities: Set<String>? = null): List<PracticeItem> =
        (capabilities?.let { itemsFor(it) } ?: all).filter { it.chapter == chapter }

    /** 기준 차량 아반떼 CN8 모던 트림의 기본 옵션 가정. 앱은 이 집합으로 보여 줄 항목을 고른다. */
    val AVANTE_CN8_MODERN: Set<String> = setOf(
        Capability.STEERING_TILT, Capability.STEERING_TELESCOPIC, Capability.EPB, Capability.AUTO_HOLD,
        // 세단이라 뒷유리 와이퍼 없음, 안개등·후측방 경고·전동시트는 모던 트림 기본 아님 → 제외
        Capability.WINDOW_LOCK, Capability.LKA, Capability.FCA, Capability.CRUISE,
    )
}
