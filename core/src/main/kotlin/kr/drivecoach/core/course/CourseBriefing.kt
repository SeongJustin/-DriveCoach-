// 코스 자동 생성
package kr.drivecoach.core.course

import kotlin.math.roundToInt

/**
 * 출발 전 브리핑(5-2) 한 줄. phraseId 는 core/tts/Phrases.kt 카탈로그 ID(문자열) 이고 params 는 그 문장의 {자리표시자} 값.
 * 문장 자체는 여기 두지 않는다(하드코딩 멘트 금지).
 */
data class BriefingNote(val phraseId: String, val params: Map<String, String>)

/** 생성된 코스에서 브리핑 멘트 ID 와 파라미터를 뽑는다. 발화 순서·개수 제한은 SpeechPolicy 몫. */
object CourseBriefing {
    const val ID_SUMMARY = "briefing_course_summary"                       // {distance} {right_turns} {left_turns} {lane_changes}
    const val ID_QUICK_LANE_CHANGE = "briefing_course_note_quick_lane_change" // {location}
    const val ID_UNPROTECTED_LEFT = "briefing_course_note_unprotected_left"   // {location}
    const val ID_MERGE = "briefing_course_note_merge"                         // {location} {accel_length} {target_speed}
    const val ID_MERGE_FALLBACK = "briefing_course_note_merge_fallback"

    /** 합류 안내에 쓰는 기본값. 가속차로 길이는 API 가 주지 않으므로 고정값, 목표 속도는 도시고속 기준. */
    const val DEFAULT_ACCEL_LENGTH_M = 200
    const val DEFAULT_TARGET_SPEED = "80"

    /**
     * 코스 특이사항 목록. 첫 줄은 요약, 이어서 "우회전 뒤 바로 좌회전", 비보호 좌회전, 합류 순.
     * location 은 guide 의 instructions(도로명 포함 문구) 가 있으면 그것, 없으면 "N번째 회전".
     */
    fun notes(course: Course): List<BriefingNote> {
        val s = course.summary
        val out = ArrayList<BriefingNote>()
        out += BriefingNote(ID_SUMMARY, mapOf(
            "distance" to ((s.distanceKm * 10).roundToInt() / 10.0).toString(),
            "right_turns" to s.rightTurns.toString(),
            "left_turns" to s.leftTurns.toString(),
            "lane_changes" to s.laneChangeEstimate.toString(),
        ))
        val guides = course.route.guides
        // quickSuccessiveTurns 는 반대 방향 회전이 500 m 안에 이어지는 (앞, 뒤) 쌍. 뒤가 좌회전이면 "우회전 뒤 바로 좌회전".
        // 종류마다 첫 곳만(같은 안내가 되풀이되어 화면에서 잘리지 않게)
        // 같은 곳(같은 안내 글)은 한 번만.
        //   비보호 좌회전이 더 위험하므로 겹치면 비보호 쪽을 남긴다
        val uIdx = guides.indexOfFirst { it.type == ManeuverType.UNPROTECTED_LEFT }
        val uLoc = if (uIdx >= 0) location(course, uIdx) else null
        NaviHandoff.quickSuccessiveTurns(course.route).firstOrNull { (_, second) -> guides[second].type.isLeftish }?.let { (_, second) ->
            val loc = location(course, second)
            if (second != uIdx && loc != uLoc) out += BriefingNote(ID_QUICK_LANE_CHANGE, mapOf("location" to loc))
        }
        if (uLoc != null) out += BriefingNote(ID_UNPROTECTED_LEFT, mapOf("location" to uLoc))
        val mergeIdx = guides.indexOfFirst { it.type == ManeuverType.MERGE || it.type == ManeuverType.HIGHWAY_ENTER }
        if (mergeIdx >= 0) {
            out += BriefingNote(ID_MERGE, mapOf(
                "location" to location(course, mergeIdx),
                "accel_length" to DEFAULT_ACCEL_LENGTH_M.toString(),
                "target_speed" to DEFAULT_TARGET_SPEED,
            ))
            out += BriefingNote(ID_MERGE_FALLBACK, emptyMap())
        }
        return out
    }

    private fun location(course: Course, guideIndex: Int): String {
        val g = course.route.guides[guideIndex]
        // 안내 글에서 회전 말을 떼고 도로·방면만(뒤에 붙이는 회전 말과 겹치지 않게)
        val place = g.instructions.replace(Regex("\\s*(으로|로)?\\s*(비보호\\s*)?(좌회전|우회전|유턴|직진)\\s*$"), "").replace(Regex("방면\\s*$"), "방면").trim()
        if (place.isNotBlank()) return place
        val turnOrdinal = course.route.guides.take(guideIndex + 1).count { it.type.isTurn || it.type == ManeuverType.MERGE || it.type == ManeuverType.HIGHWAY_ENTER }
        return "${turnOrdinal}번째 회전"
    }
}
