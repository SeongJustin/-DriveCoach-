// 담당: S (앱 연결 — 차량 내비 TBT(다음 회전) 이벤트를 5-3 안내 멘트 ID 로 바꾸는 순수 규칙)
package kr.drivecoach.core.course

import kr.drivecoach.core.tts.PhraseId

/** TBT 안내 한 번: 멘트 ID + {자리표시자} 값. */
data class TbtAnnouncement(val phraseId: PhraseId, val params: Map<String, String>)

/**
 * "다음 동작은 미리 한참 전부터 안내한다": 내비가 주는 다음 회전(종류·남은 거리)을 받아
 * 회전 하나당 **멀리서 한 번**("{distance}m 앞 우회전…"), **직전에 한 번**("여기서 우회전…")만 멘트 ID 를 낸다.
 * 같은 회전에 대해 내비가 거리 갱신을 초마다 보내도 두 번 넘게 말하지 않는다.
 * 문장은 전부 core/tts 카탈로그의 guide_* ID 이고 여기서는 ID 와 {distance} 값만 만든다.
 *
 * @param farM 이 거리(m) 안으로 들어오면 "N m 앞" 안내. 기본 350 (300 m 에 여유를 둔 값).
 * @param nearM 이 거리(m) 안이면 "여기서" 안내. 기본 60.
 */
class TbtGuide(private val farM: Double = DEFAULT_FAR_M, private val nearM: Double = DEFAULT_NEAR_M) {
    private enum class Stage { NONE, FAR, NEAR }

    private var currentKey: String? = null
    private var stage = Stage.NONE

    /** 새 경로가 시작될 때 호출. 안내 기록을 비운다. */
    fun reset() { currentKey = null; stage = Stage.NONE }

    /**
     * 내비 TBT 하나를 넣는다. 말할 것이 있으면 멘트, 없으면 null.
     * @param maneuver 공급자 독립 회전 유형(app 어댑터가 Pleos TurnType 을 접은 것).
     * @param distanceM 회전 지점까지 남은 거리(m).
     * @param key 회전을 구분하는 키(유형 + 안내 문구). 키가 바뀌면 새 회전으로 본다.
     */
    fun onTbt(maneuver: ManeuverType, distanceM: Double, key: String): TbtAnnouncement? {
        if (key != currentKey) { currentKey = key; stage = Stage.NONE }
        val near = distanceM <= nearM
        val far = distanceM <= farM
        return when {
            near && stage != Stage.NEAR -> { stage = Stage.NEAR; nearPhrase(maneuver) }
            far && stage == Stage.NONE -> { stage = Stage.FAR; farPhrase(maneuver, distanceM) }
            else -> null
        }
    }

    private fun farPhrase(m: ManeuverType, distanceM: Double): TbtAnnouncement? {
        val d = mapOf("distance" to roundDistance(distanceM).toString())
        return when (m) {
            ManeuverType.RIGHT -> TbtAnnouncement(PHRASE_RIGHT_FAR, d)
            ManeuverType.LEFT -> TbtAnnouncement(PHRASE_LEFT_FAR, d)
            ManeuverType.UNPROTECTED_LEFT -> TbtAnnouncement(PHRASE_LEFT_FAR, d)
            ManeuverType.UTURN -> TbtAnnouncement(PHRASE_UTURN, d)
            ManeuverType.ROUNDABOUT -> TbtAnnouncement(PHRASE_ROUNDABOUT, emptyMap())
            else -> null
        }
    }

    private fun nearPhrase(m: ManeuverType): TbtAnnouncement? = when (m) {
        ManeuverType.RIGHT -> TbtAnnouncement(PHRASE_RIGHT_NOW, emptyMap())
        ManeuverType.LEFT -> TbtAnnouncement(PHRASE_LEFT_NOW, emptyMap())
        ManeuverType.UNPROTECTED_LEFT -> TbtAnnouncement(PHRASE_LEFT_UNPROTECTED, emptyMap())
        else -> null
    }

    companion object {
        const val DEFAULT_FAR_M = 350.0
        const val DEFAULT_NEAR_M = 60.0

        /** 카탈로그(core/tts/Phrases) 의 5-3 안내 ID. */
        val PHRASE_RIGHT_FAR = PhraseId("guide_turn_right_300m")
        val PHRASE_RIGHT_NOW = PhraseId("guide_turn_right_now")
        val PHRASE_LEFT_FAR = PhraseId("guide_turn_left_300m")
        val PHRASE_LEFT_NOW = PhraseId("guide_turn_left_now")
        val PHRASE_LEFT_UNPROTECTED = PhraseId("guide_turn_left_unprotected")
        val PHRASE_UTURN = PhraseId("guide_uturn")
        val PHRASE_ROUNDABOUT = PhraseId("guide_roundabout")

        /** "{distance}m" 에 넣을 값: 50 m 단위 반올림, 최소 50. (327 → 300, 180 → 200) */
        fun roundDistance(distanceM: Double): Int = ((distanceM / 50.0 + 0.5).toInt() * 50).coerceAtLeast(50)
    }
}
