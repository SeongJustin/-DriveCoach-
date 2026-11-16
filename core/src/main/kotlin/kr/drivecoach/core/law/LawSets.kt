package kr.drivecoach.core.law

/**
 * 나라별 도로 법규 묶음. 한국은 [TrafficRules] 그대로, 다른 나라는 [ForeignLaws](조사 자료에서 만든 코드)에서 온다.
 * 3D 도로 주행을 다른 나라로 넓힐 때 그 나라 법규를 같은 화면에서 보여 주려는 것.
 *
 * @param country ISO 국가 코드(KR, JP, US-NY …), @param label 화면 이름(한국, 일본 …)
 * @param trafficSide "right" 또는 "left"(통행 방향), @param speedUnit "km/h" 또는 "mph"
 */
data class LawSet(
    val country: String,
    val label: String,
    val topics: List<RuleTopic>,
    val trafficSide: String = "right",
    val speedUnit: String = "km/h",
    val asOf: String = "",
) {
    fun byId(id: String): RuleTopic? = topics.firstOrNull { it.id == id }
    fun indexOf(id: String): Int = topics.indexOfFirst { it.id == id }

    /** 주제 그림 리소스 이름. 한국은 law_<id>, 다른 나라는 law_<나라 코드에서 영숫자만, 소문자>_<id>(멘트 ID 와 같은 규칙). */
    fun artName(topicId: String): String = if (country == KOREA) "law_$topicId" else "law_${country.lowercase().filter { it.isLetterOrDigit() }}_$topicId"

    /** 본 주제 기록 키. 한국은 예전 키("law:<id>")를 그대로 써서 이미 본 표시가 유지된다. */
    fun seenKey(id: String): String = if (country == KOREA) "law:$id" else "law:${country.lowercase()}:$id"

    companion object { const val KOREA = "KR" }
}

object LawSets {
    val korea: LawSet = LawSet(LawSet.KOREA, "한국", TrafficRules.topics, asOf = "2026-10-13")

    /** 화면에 보이는 순서: 한국 다음 조사·검수를 마친 나라. */
    val all: List<LawSet> by lazy { listOf(korea) + ForeignLaws.sets }

    fun of(country: String?): LawSet = all.firstOrNull { it.country.equals(country, ignoreCase = true) } ?: korea
}
