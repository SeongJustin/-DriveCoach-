// 나라별 도로 법규 묶음 시험
package kr.drivecoach.core.law

import kr.drivecoach.core.tts.Phrases
import kotlin.test.assertEquals
import kotlin.test.assertFails
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

private fun koreaIsFirstAndKeepsOldSeenKeys() {
    val kr = LawSets.all.first()
    assertEquals("KR", kr.country)
    assertEquals(TrafficRules.topics.size, kr.topics.size)
    assertEquals("law:right_turn", kr.seenKey("right_turn"))
    assertEquals(kr, LawSets.of("kr"))
    assertEquals(kr, LawSets.of(null))
    assertEquals(kr, LawSets.of("없는나라"))
}

private fun foreignSetsHaveSpeakableTextAndUniqueIds() {
    for (set in LawSets.all.drop(1)) {
        assertTrue(set.topics.isNotEmpty(), set.country)
        assertEquals(set.topics.size, set.topics.map { it.id }.toSet().size, "주제 ID 중복: ${set.country}")
        assertTrue(set.seenKey("x").startsWith("law:${set.country.lowercase()}:"))
        for (t in set.topics) {
            val p = assertNotNull(Phrases.find(t.phraseId), "멘트 없음: ${t.phraseId.id}")
            assertTrue(p.text.isNotBlank() && p.text.length <= 170, t.phraseId.id)
            assertTrue(t.basis.isNotBlank(), "근거 없음: ${t.phraseId.id}")
        }
    }
}

private fun registerLawIsIdempotentAndRejectsConflicts() {
    val a = Phrases.registerLaw("law_zz_test_topic", "시험용 문장이다.")
    val b = Phrases.registerLaw("law_zz_test_topic", "시험용 문장이다.")
    assertEquals(a, b)
    assertFails { Phrases.registerLaw("law_zz_test_topic", "다른 문장") }
    assertFails { Phrases.registerLaw("zz_not_law", "문장") }
    assertFails { Phrases.registerLaw("law_zz_too_long", "가".repeat(171)) }
    assertFails { Phrases.registerLaw("law_right_turn", "한국 법규와 같은 ID") }
}

private fun keysAndArtNames() {
    val ny = LawSet("US-NY", "미국 뉴욕주", emptyList())
    assertEquals("law:us-ny:right_turn", ny.seenKey("right_turn"))
    assertEquals("law_usny_right_turn", ny.artName("right_turn"))
    assertEquals("law_right_turn", LawSets.korea.artName("right_turn"))
    assertEquals(LawSets.all.size, LawSets.all.map { it.country.uppercase() }.toSet().size, "나라 코드 중복")
    assertEquals(1, LawSets.all.count { it.country == LawSet.KOREA })
}

fun main() {
    koreaIsFirstAndKeepsOldSeenKeys()
    foreignSetsHaveSpeakableTextAndUniqueIds()
    registerLawIsIdempotentAndRejectsConflicts()
    keysAndArtNames()
    println("LawSetsTest OK (나라 ${LawSets.all.size}개: ${LawSets.all.joinToString { it.country + " " + it.topics.size }})")
}
