// AAOS 스티어링 휠 버튼 → core.input.InputSource.WheelButton 어댑터
// 문서 기준으로 작성했으며 실기기/에뮬레이터 검증이 필요하다.
package kr.drivecoach.app.input

import android.os.Handler
import android.os.Looper
import android.view.KeyEvent
import kr.drivecoach.core.input.InputSource
import kr.drivecoach.core.input.PressType

/**
 * 스티어링 휠 버튼 KeyEvent 를 받아 짧게/길게 누름으로 구분해 [onPress] 로 넘긴다.
 *
 * 사용법: Activity 의 `dispatchKeyEvent(event)` 에서 `receiver.onKeyEvent(event)` 를 먼저 부르고,
 * true 가 돌아오면 소비한다. 앱이 포그라운드가 아닐 때(내비가 전면)도 받으려면
 * MediaSession 의 `MediaSessionCompat.Callback.onMediaButtonEvent(intent)` 에서
 * `intent.getParcelableExtra(Intent.EXTRA_KEY_EVENT)` 를 꺼내 같은 함수에 넣는다.
 *
 * 어떤 키를 쓰는가:
 *  - AAOS 에서 스티어링 휠 물리 버튼은 보통 일반 KeyEvent 로 올라온다. 서드파티 앱이 받을 수 있는 것은
 *    미디어 키 계열(KEYCODE_MEDIA_PLAY_PAUSE, KEYCODE_MEDIA_NEXT/PREVIOUS)과, 포그라운드일 때의 일반 키다.
 *  - KEYCODE_VOICE_ASSIST / KEYCODE_CALL 은 시스템(기본 어시스턴트·전화)이 먼저 가로채므로 쓰지 않는다.
 *  - 기본값은 KEYCODE_MEDIA_PLAY_PAUSE(휠의 ▶❚❚ 버튼). 짧게 = 확인/다음, 길게 = 세울래요.
 *  - 【미확인】 Pleos Connect 가 스티어링 휠 "커스텀/즐겨찾기(★)" 버튼을 앱에 전용 키코드로 노출하는지
 * 및 공개 문서에서 확인하지 못했다. 확인되면 [acceptedKeyCodes] 에 추가한다.
 *    Vehicle SDK 에 HW 키 도메인이 있다면 그쪽을 통해 받는 어댑터로 교체 가능(인터페이스는 [onPress] 그대로).
 *
 * 길게 누름 판정: ACTION_DOWN 후 [longPressMs] 안에 ACTION_UP 이 오면 SHORT, 넘기면 LONG.
 * LONG 은 UP 을 기다리지 않고 타이머가 끝나는 순간 바로 보낸다(공황 상황 — 손을 떼기 전에 반응해야 함).
 * KeyEvent.isLongPress()(repeatCount 기반)도 보조로 쓴다.
 */
class WheelButtonReceiver(
    private val onPress: (InputSource.WheelButton) -> Unit,
    private val acceptedKeyCodes: Set<Int> = setOf(KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE),
    private val longPressMs: Long = DEFAULT_LONG_PRESS_MS,
    private val handler: Handler = Handler(Looper.getMainLooper()),
) {
    private var downKeyCode: Int? = null
    private var longFired = false
    private val longRunnable = Runnable {
        longFired = true
        onPress(InputSource.WheelButton(PressType.LONG))
    }

    /** KeyEvent 를 처리한다. 우리가 쓰는 키면 true(소비), 아니면 false. */
    fun onKeyEvent(event: KeyEvent): Boolean {
        if (event.keyCode !in acceptedKeyCodes) return false
        when (event.action) {
            KeyEvent.ACTION_DOWN -> {
                if (event.repeatCount == 0) {
                    downKeyCode = event.keyCode
                    longFired = false
                    handler.removeCallbacks(longRunnable)
                    handler.postDelayed(longRunnable, longPressMs)
                } else if (event.isLongPress && !longFired) {
                    // 시스템이 길게 누름을 먼저 알려준 경우
                    handler.removeCallbacks(longRunnable)
                    longRunnable.run()
                }
            }
            KeyEvent.ACTION_UP -> {
                handler.removeCallbacks(longRunnable)
                if (downKeyCode == event.keyCode && !longFired && !event.isCanceled) {
                    onPress(InputSource.WheelButton(PressType.SHORT))
                }
                downKeyCode = null
                longFired = false
            }
        }
        return true
    }

    /** 화면이 내려가거나 포커스를 잃을 때 호출해 대기 중인 타이머를 정리한다. */
    fun reset() {
        handler.removeCallbacks(longRunnable)
        downKeyCode = null
        longFired = false
    }

    companion object {
        /** 이 시간(ms) 이상 누르면 "길게". */
        const val DEFAULT_LONG_PRESS_MS = 700L
    }
}
