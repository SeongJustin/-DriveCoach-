// 담당: C+G
package kr.drivecoach.core.input

import kotlin.test.assertEquals
import kotlin.test.assertNull

/** 동의어 사전. */
private fun testSynonyms() {
    val r = CommandRecognizer()
    val cases = mapOf(
        "다시" to Command.AGAIN,
        "다시요" to Command.AGAIN,
        "한 번 더" to Command.AGAIN,
        "한번 더요" to Command.AGAIN,
        "조용히" to Command.QUIET,
        "조용히 해" to Command.QUIET,
        "그만" to Command.QUIET,
        "확인" to Command.CONFIRM,
        "확인했어요" to Command.CONFIRM,
        "했어요" to Command.CONFIRM,
        "네" to Command.CONFIRM,
        "네네" to Command.CONFIRM,
        "보여요" to Command.CONFIRM,
        "보여" to Command.CONFIRM,
        "다음" to Command.NEXT,
        "다음으로" to Command.NEXT,
        "세울래요" to Command.PULL_OVER,
        "힘들어" to Command.PULL_OVER,
        "힘들어요" to Command.PULL_OVER,
        "잠깐" to Command.PULL_OVER,
        "잠깐만요" to Command.PULL_OVER,
        // (11/8) 자연스러운 말투
        "한 번만 더 말해 줘" to Command.AGAIN,
        "못 들었어요" to Command.AGAIN,
        "잘 못 들었어" to Command.AGAIN,
        "못 알아들었어" to Command.AGAIN,
        "뭐라 했어?" to Command.AGAIN,
        "반복해 줘" to Command.AGAIN,
        "시끄러워" to Command.QUIET,
        "말하지 마" to Command.QUIET,
        "안전벨트 맸어요" to Command.CONFIRM,
        "켰어요" to Command.CONFIRM,
        "오토홀드 껐어" to Command.CONFIRM,
        "거울 맞췄어요" to Command.CONFIRM,
        "다 됐어요" to Command.CONFIRM,
        "완료" to Command.CONFIRM,
        "넘겨 줘" to Command.NEXT,
        "다음 거" to Command.NEXT,
        "넘어가자" to Command.NEXT,
        "스킵" to Command.SKIP,
        "전으로 가 줘" to Command.PREV,
        "이전으로" to Command.PREV,
    )
    for ((text, expected) in cases) {
        assertEquals(expected, r.recognizeVoice(text), "'$text'")
    }
    println("  synonyms OK")
}

/** 못 알아듣는 경우·애매한 경우·신뢰도. */
private fun testRejects() {
    val r = CommandRecognizer()
    assertNull(r.recognizeVoice(""))
    assertNull(r.recognizeVoice("   "))
    assertNull(r.recognizeVoice("오늘 날씨 좋다"))
    // "네" 는 전체 일치일 때만
    assertNull(r.recognizeVoice("네비 켜줘"))
    // 차 안 대화에 흔한 말은 명령이 아니어야 함
    assertNull(r.recognizeVoice("하이패스 카드 꽂았어?"))
    assertNull(r.recognizeVoice("에어컨 좀 켜 줘"))
    assertNull(r.recognizeVoice("음악 틀어 줘"))
    // 다른 명령이 섞이면 애매 → null ("다시 켰어")
    assertNull(r.recognizeVoice("다시 켰어"))
    // 서로 다른 명령이 섞이면 애매 → null
    assertNull(r.recognizeVoice("확인 다음"))
    // 단, 세울래요가 섞이면 안전 쪽
    assertEquals(Command.PULL_OVER, r.recognizeVoice("확인 아니 세울래요"))
    // 신뢰도 낮음
    assertNull(r.recognizeVoice("확인", confidence = 0.2))
    assertEquals(Command.CONFIRM, r.recognizeVoice("확인", confidence = 0.9))
    assertEquals(Command.CONFIRM, r.recognizeVoice("확인", confidence = null))
    println("  rejects OK")
}

/** 입력 모드 설정에 따른 경로 선택. */
private fun testInputModes() {
    val voice = CommandRecognizer(InputSettings(mode = InputMode.VOICE))
    assertEquals(Command.NEXT, voice.recognize(InputSource.Voice("다음")))
    assertNull(voice.recognize(InputSource.WheelButton(PressType.SHORT)), "음성 모드에서 짧게 누름은 무시")
    assertEquals(Command.PULL_OVER, voice.recognize(InputSource.WheelButton(PressType.LONG)), "길게 누름은 항상")

    val wheel = CommandRecognizer(InputSettings(mode = InputMode.WHEEL_BUTTON))
    assertNull(wheel.recognize(InputSource.Voice("다음")), "휠 모드에서 일반 음성은 무시")
    assertEquals(Command.PULL_OVER, wheel.recognize(InputSource.Voice("세울래요")), "세울래요는 항상")
    assertEquals(Command.CONFIRM, wheel.recognize(InputSource.WheelButton(PressType.SHORT), ShortPressContext.AWAITING_CONFIRM))
    assertEquals(Command.NEXT, wheel.recognize(InputSource.WheelButton(PressType.SHORT), ShortPressContext.IDLE_OR_AWAITING_NEXT))
    assertEquals(Command.PULL_OVER, wheel.recognize(InputSource.WheelButton(PressType.LONG)))

    // 설정 변경은 런타임에 반영
    wheel.settings = InputSettings(mode = InputMode.VOICE)
    assertEquals(Command.NEXT, wheel.recognize(InputSource.Voice("다음")))
    println("  inputModes OK")
}

fun main() {
    testSynonyms()
    testRejects()
    testInputModes()
    println("CommandRecognizerTest PASSED")
}
