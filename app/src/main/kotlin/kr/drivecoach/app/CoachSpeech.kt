// 발화 파이프라인: 요청 → SpeechPolicy 판정 → Speaker, 직렬 줄, 감시견, 로그
// (11/5 정리) CoachController 를 기능별 파일로 나눴다. 상태는 CoachController 클래스에, 동작은 이 파일의 확장 함수에 있다.
package kr.drivecoach.app

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kr.drivecoach.app.speech.Speaker
import kr.drivecoach.core.input.NoInputDecision
import kr.drivecoach.core.mode.DriveMode
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.StateDiff
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.Decision
import kr.drivecoach.core.warning.WarningLamps
import kr.drivecoach.core.tts.PhraseCategory
import kr.drivecoach.core.tts.PhraseId
import kr.drivecoach.core.tts.Phrases
import kr.drivecoach.core.tts.SpeechContext
import kr.drivecoach.core.tts.SpeechPolicy
import kr.drivecoach.core.tts.SpeechRequest
import kr.drivecoach.core.tts.DriveMode as TtsMode

internal fun CoachController.speakWelcome() {
    welcomePending = false
    val name = carName ?: return
    request(if (carDetected) Phrases.systemWelcomeCar.id else Phrases.systemWelcomeDefaultCar.id, mapOf("car_model" to name))
}

internal fun CoachController.requestQueued(id: PhraseId) = requestQueuedVars(id, emptyMap())

/** {자리표시자} 값이 있는 멘트(브리핑·리포트)를 직렬 줄에 넣는다. */
internal fun CoachController.requestQueuedVars(id: PhraseId, vars: Map<String, String>) {
    subModeQueue.addLast(id to vars)
    if (!subModeQueueBusy) drainSubModeQueue()
}

internal fun CoachController.drainSubModeQueue() {
    if (subModeQueue.isEmpty()) { subModeQueueBusy = false; return }
    val (next, vars) = subModeQueue.removeFirst()
    subModeQueueBusy = true
    request(next, vars, force = true, onDone = { drainSubModeQueue() })
}

/** 운전자 입력·활동 종료 때: 아직 안 나간 멘트는 버린다(지금 나가는 것은 interruptCoaching 이 끊는다). */
internal fun CoachController.clearSubModeQueue() { subModeQueue.clear(); screenTextWhenSpoken.clear() }

/** 지금 말하는 멘트가 코칭(위험·공황·경고등이 아닌 것)이면 끊는다. */
internal fun CoachController.interruptCoaching() {
    requestGen++   // 미뤄진(DEFER) 이전 요청은 더 이상 유효하지 않다(베타 C-11: '다음' 연타 때 옛 주제가 뒤늦게 나오던 문제)
    val s = speaking ?: return
    if (SpeechPolicy.priorityOf(s.category) >= SpeechPolicy.priorityOf(PhraseCategory.GUIDE)) speaker.stop()
}

internal fun CoachController.handleNoInput(decision: NoInputDecision) {
    when (decision) {
        // R/D 에서는 화면이 가려져 버튼을 누를 수 없으니 재안내를 하지 않는다(베타 B-50).
        is NoInputDecision.Reprompt -> if (currentState().gear == Gear.P) {
            // 주차 단계 화면에는 '다음' 버튼이 없고 '확인'이 있다
            request(if (decision.prompt.promptId.startsWith("parking_")) Phrases.systemNoInputRepromptConfirm.id else decision.phraseId)
        }
        is NoInputDecision.Deferred -> Log.i(COACH_TAG, "입력 없음 → 다음 기회: ${decision.promptId} (주행 중=${decision.wasMoving})")
        else -> {}
    }
}

/** 지금 상황을 SpeechPolicy 용 컨텍스트로. 플랫폼 규제가 걸리면 core 가 주차라고 봐도 주행으로 취급(더 엄격한 쪽). */
internal fun CoachController.speechContext(): SpeechContext {
    val parked = !restricted && _mode.value == DriveMode.PARKED_LEARN
    // 직각 주차 슬로우 모드 중 R/D 로 움직이는 동안은 "저속 연습"(PRACTICE) 모드: PARKING 멘트가 허용되고 브리핑·리포트는 막힌다.
    val ttsMode = when {
        parked -> TtsMode.PARKED
        _subMode.value == SubMode.PARKING -> TtsMode.PRACTICE
        _subMode.value == SubMode.PRACTICE -> TtsMode.PRACTICE   // 기어 드릴 예외(속도 0, R/N/D)
        else -> TtsMode.DRIVING
    }
    return SpeechContext(
        mode = ttsMode,
        quietMode = _quiet.value,
        coachingPaused = coachingPaused,
        currentlySpeaking = speaking?.category,
    )
}

/**
 * 멘트 ID 하나를 말해 달라고 요청한다. SpeechPolicy 가 SPEAK/DEFER/DROP 을 정한다.
 * DEFER 는 retryAfterMs 뒤 같은 요청을 다시 넣는다(최대 [MAX_DEFERS] 회).
 * [onDone] 은 말이 끝났을 때(또는 DROP/포기했을 때) 한 번 호출된다.
 */
fun CoachController.request(id: PhraseId, vars: Map<String, String> = emptyMap(), force: Boolean = false, onDone: (() -> Unit)? = null) {
    val phrase = Phrases.find(id)
    if (phrase == null) {
        Log.w(COACH_TAG, "카탈로그에 없는 멘트: ${id.id}")
        onDone?.invoke()
        return
    }
    decide(SpeechRequest(phrase.id, phrase.category, force), vars, onDone, attempt = 0, gen = requestGen)
}

internal fun CoachController.decide(req: SpeechRequest, vars: Map<String, String>, onDone: (() -> Unit)?, attempt: Int, gen: Int) {
    val now = clock()
    if (gen != requestGen && SpeechPolicy.priorityOf(req.category) >= SpeechPolicy.priorityOf(PhraseCategory.GUIDE)) {
        log("STALE ${req.phraseId.id}")
        onDone?.invoke()
        return
    }
    val outcome = policy.decide(req, now, speechContext())
    when (outcome.decision) {
        Decision.SPEAK -> speakNow(req, vars, outcome.interrupt, onDone)
        Decision.DEFER -> {
            // 재생 중인 문장이 길면(설명 30~40초) 그만큼 더 기다린다(베타 D-19: 환영·재안내가 8초 만에 버려지던 문제).
            val maxDefers = speaking?.let { maxOf(MAX_DEFERS, (it.expectedMs / 1_000L).toInt() + 2) } ?: MAX_DEFERS
            if (attempt >= maxDefers) {
                log("GIVEUP ${outcome.reason} ${req.phraseId.id}")
                onDone?.invoke()
            } else {
                val wait = (outcome.retryAfterMs ?: 1_000L).coerceIn(100L, 10_000L)
                scope.launch(Dispatchers.Main) {
                    delay(wait)
                    decide(req, vars, onDone, attempt + 1, gen)
                }
            }
        }
        Decision.DROP -> {
            log("DROP ${outcome.reason} ${req.phraseId.id}")
            onDone?.invoke()
        }
    }
}

internal fun CoachController.speakNow(req: SpeechRequest, vars: Map<String, String>, interrupt: Boolean, onDone: (() -> Unit)?) {
    val text = Phrases.render(req.phraseId, vars)
    val uid = "u${++utteranceSeq}:${req.phraseId.id}"
    speaking?.let { prev ->
        // 끊고 말하는 경우: 이전 문장은 끝난 것으로 정리한다(Speaker 가 onFinished 를 또 보내면 ID 가 달라 무시됨).
        // 단, 위험·공황·경고등이 연습/주차 안내를 끊은 것이면 끊긴 안내를 직렬 줄 맨 앞에 되돌려 다시 들려준다(베타 B-52).
        val preempted = SpeechPolicy.priorityOf(req.category) < SpeechPolicy.priorityOf(PhraseCategory.GUIDE) &&
            (prev.category == PhraseCategory.PRACTICE || prev.category == PhraseCategory.PARKING) && isInstruction(prev.id)
        afterUtterance.remove(prev.utteranceId)?.invoke()
        if (preempted) {
            subModeQueue.addFirst(prev.id to prev.vars)
            val inner = onDone
            afterUtterance[uid] = { inner?.invoke(); if (!subModeQueueBusy) drainSubModeQueue() }
        }
    }
    // 클립 실측 약 5자/초. 느린 속도(0.85)·여유 5초를 더해 글자 수 기준으로 감시 시간을 잡되 최소 20초.
    // (11/7) 예전 식은 속도를 Long 으로 잘라(0.85 → 0 → 1) 느린 속도가 반영되지 않았다. 실수로 나눈다.
    val expected = maxOf(SPEAK_WATCHDOG_MS, (text.length * 250.0 / maxOf(0.5f, _speechRate.value)).toLong() + 5_000L)
    speaking = CoachController.Speaking(req.phraseId, req.category, uid, clock(), expectedMs = expected, vars = vars)
    if (onDone != null && uid !in afterUtterance) afterUtterance[uid] = onDone
    lastPhraseSpoken = req.phraseId to vars
    _lastSpoken.value = text
    screenTextWhenSpoken.remove(req.phraseId)?.invoke()
    _speakerName.value = speaker.name
    recorder.onCoachSpoke(req.phraseId.id)
    log("SPEAK ${req.phraseId.id}")
    runCatching { onWillSpeak?.invoke() }
    speaker.speak(text, uid, interrupt)
}

/**
 * (11/16) 오디오 포커스를 잃었을 때(통화·음성 비서): 줄에 남은 안내와 미뤄진 코칭을 버린다.
 * 위험·공황·경고등 안내는 세대와 무관해 그대로 나오고, 새 버튼·음성 명령 응답도 새로 요청되므로 나온다.
 */
internal fun CoachController.onAudioFocusLost() {
    requestGen++
    clearSubModeQueue()
    log("FOCUS_LOST 남은 줄 비움")
}

internal fun CoachController.speakWarningLamps(state: VehicleState) {
    val prev = lastLampState
    lastLampState = state
    if (prev == null) return
    val turnedOn = StateDiff.warningLampsTurnedOn(prev, state)
    for (id in turnedOn) {
        val lamp = WarningLamps.byId(id) ?: continue
        request(Phrases.warningLampOn.id, mapOf("lamp" to lamp.spokenName), onDone = { request(lamp.actionPhraseId) })
    }
}

/** 끊기면 다시 들려줄 가치가 있는 안내(설명·해보기·확인 요청·주차 단계 문장). 통과·건너뜀 같은 결과 멘트는 제외. */
internal fun CoachController.isInstruction(id: PhraseId): Boolean {
    val i = id.id
    return i.endsWith("_explain") || i.endsWith("_try") || i.endsWith("_confirm") ||
        i.startsWith("parking_step") || i.startsWith("parking_escape") || i.startsWith("law_")
}

internal fun CoachController.onUtteranceFinished(utteranceId: String, completed: Boolean) {
    val s = speaking
    if (s != null && s.utteranceId == utteranceId) {
        speaking = null
        policy.onSpoken(s.id, clock())
    }
    afterUtterance.remove(utteranceId)?.invoke()
}

internal fun CoachController.log(line: String) {
    Log.i(COACH_TAG, line)
    _log.value = (_log.value + line).takeLast(12)
}

/** (11/15 대표) 3D 연습의 할 일을 화면 글 대신 음성으로 한 번 읽는다. 조용히 모드면 말하지 않는다. */
fun CoachController.speakGameHint(text: String) {
    if (text.isBlank() || _quiet.value) return
    runCatching { onWillSpeak?.invoke() }
    speaker.speak(text.take(200), "u${++utteranceSeq}:game_hint", false)
}
