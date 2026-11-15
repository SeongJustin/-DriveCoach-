// 담당: S (도로 법규 설명 — 11/4 대표 결정: 퀴즈는 다음 빌드, 이번엔 중요한 것 위주 설명만)
package kr.drivecoach.core.law

import kr.drivecoach.core.tts.PhraseId

/**
 * 도로 법규 주제 하나. 화면 제목 + 설명 멘트(음성과 화면 글이 같다. 문장은 [Phrases] 의 `law_<id>` 에 있다).
 * 근거: 도로교통법(2026 기준) 중 초보가 자주 틀리는 것 — 우회전·비보호 좌회전·유턴·신호·차선·보호구역·횡단보도·회전교차로·긴급차 양보·고속도로·주정차·기본 상식.
 */
/** @param heading 글 위 제목(조문 표현), @param basis 근거 조문(화면에만, 음성으로 읽지 않는다) */
data class RuleTopic(val id: String, val title: String, val phraseId: PhraseId, val heading: String = title, val basis: String = "")

object TrafficRules {
    private fun t(id: String, title: String) = RuleTopic(id, title, PhraseId("law_$id"), HEADINGS[id] ?: title, BASIS[id] ?: "")

    // (11/15 대표: 그림 속 조문 글을 오른쪽으로 옮기고 근거 조문을 적는다)
    private val HEADINGS = mapOf(
        "right_turn" to "적색 등화의 우회전",
        "crosswalk" to "보행자의 보호",
        "signal_stopline" to "신호와 교차로",
        "u_turn" to "유턴",
        "unprotected_left" to "비보호 좌회전",
        "lanes" to "차로와 진로 변경",
        "roundabout" to "회전교차로",
        "highway" to "고속도로",
        "parking_ban" to "정차 및 주차의 금지",
        "emergency" to "긴급자동차의 우선 통행",
        "basics" to "운전자의 기본 준수사항",
        "school_zone" to "어린이 보호구역",
    )
    private val BASIS = mapOf(
        "right_turn" to "도로교통법 시행규칙 별표 2(적색의 등화), 도로교통법 제25조 제5항, 제27조 제2항",
        "unprotected_left" to "도로교통법 시행규칙 별표 2(녹색의 등화), 도로교통법 제25조 제2항, 제26조 제4항",
        "u_turn" to "도로교통법 제18조 제1항, 제38조 제1항",
        "signal_stopline" to "도로교통법 시행규칙 별표 2(적색의 등화), 도로교통법 제25조 제5항",
        "lanes" to "도로교통법 제13조 제3항, 제14조 제5항, 제19조 제3항",
        "crosswalk" to "도로교통법 제27조 제1항",
        "school_zone" to "도로교통법 제12조 제1항, 제27조 제7항, 제32조 제8호",
        "roundabout" to "도로교통법 제25조의2, 제38조 제1항",
        "emergency" to "도로교통법 제29조 제4항, 제5항",
        "highway" to "도로교통법 제21조 제1항, 제60조 제1항 및 제2항, 도로교통법 시행규칙 제19조",
        "parking_ban" to "도로교통법 제32조",
        "basics" to "도로교통법 제17조 제3항, 제38조 제1항, 제49조 제1항 제10호, 제50조 제1항, 도로교통법 시행령 별표 2",
    )

    /** 화면 순서 = 중요도·출발 전 자주 마주치는 순. */
    val topics: List<RuleTopic> = listOf(
        t("right_turn", "우회전"),
        t("unprotected_left", "비보호 좌회전"),
        t("u_turn", "유턴"),
        t("signal_stopline", "신호와 교차로"),
        t("lanes", "차로와 진로 변경"),   // (11/14 베타: "차선 종류"인데 그림·글은 진로 변경) 그림 제목과 같게
        t("crosswalk", "횡단보도"),
        t("school_zone", "어린이 보호구역"),
        t("roundabout", "회전교차로"),
        t("emergency", "긴급자동차 양보"),
        t("highway", "고속도로"),
        t("parking_ban", "주정차 금지"),
        t("basics", "기본 준수사항"),
    )

    fun byId(id: String): RuleTopic? = topics.firstOrNull { it.id == id }
    fun indexOf(id: String): Int = topics.indexOfFirst { it.id == id }
}
