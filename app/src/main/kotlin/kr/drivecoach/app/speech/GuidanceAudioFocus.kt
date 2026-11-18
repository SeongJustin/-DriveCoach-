// 앱 연결: 코치 음성의 오디오 포커스
package kr.drivecoach.app.speech

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.os.Handler
import android.os.Looper
import android.util.Log

/**
 * 코치 음성(Pleos TTS)이 나오는 동안 오디오 포커스를 잡는다.
 *
 * Pleos "Audio Focus Development" 가이드(document.pleos.ai, vehicle-app-developer-guide):
 *  - 소리를 내기 전에 AudioAttributes + AudioFocusRequest 로 포커스를 요청한다.
 *  - 내비 안내와 같은 NAVIGATION 컨텍스트(USAGE_ASSISTANCE_NAVIGATION_GUIDANCE)는 음악과 공존(C)해야 하므로
 *    AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK 으로 요청한다 → 음악·라디오는 끊기지 않고 소리만 줄어든다.
 *  - 포커스 변화는 반드시 처리: LOSS·LOSS_TRANSIENT(통화·Gleo 음성 비서 등) → 바로 멈추고 포커스 반납, LOSS_TRANSIENT_CAN_DUCK → 소리 줄이기(짧은 안내라 그대로 둔다).
 *  - Suspend(시동 꺼짐)에 들어가면 포커스를 반납하고 새로 요청하지 않는다([suspended]). 돌아와도 자동으로 다시 잡지 않는다.
 * Pleos 음성 서비스(caas)는 재생할 때 스스로 포커스를 잡지 않는다(다른 참가 팀 dumpsys 실측) → 앱이 잡아야 음악 위로 안내가 또렷하게 들린다.
 *
 * 문장 사이마다 놓았다 잡으면 음악이 커졌다 작아지기를 반복하므로, 마지막 문장이 끝나고 [RELEASE_DELAY_MS] 동안 다음 문장이 없을 때 놓는다.
 */
class GuidanceAudioFocus(
    context: Context,
    /** 포커스를 잃어 지금 말을 멈춰야 할 때(통화·음성 비서·다른 앱의 안내). */
    private val onMustStop: () -> Unit,
) {
    private val audio = context.applicationContext.getSystemService(Context.AUDIO_SERVICE) as? AudioManager
    private val main = Handler(Looper.getMainLooper())
    private var held = false

    private val listener = AudioManager.OnAudioFocusChangeListener { change ->
        when (change) {
            AudioManager.AUDIOFOCUS_LOSS, AudioManager.AUDIOFOCUS_LOSS_TRANSIENT -> {
                // Pleos 오디오 포커스 가이드: Gleo 음성 비서·통화 등으로 LOSS_TRANSIENT 를 받으면 즉시 멈추고 포커스를 반납한다.
                // 다음 문장 때 새로 요청한다(포커스를 쥔 채 기다리지 않는다).
                Log.i(TAG, "포커스 잃음($change) → 코치 음성 멈춤·포커스 반납")
                onMustStop()
                dropFocus()
            }
            AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK -> Log.i(TAG, "포커스: 다른 소리와 겹침(짧은 안내라 그대로)")
            AudioManager.AUDIOFOCUS_GAIN -> { held = true; Log.i(TAG, "포커스 다시 얻음") }
        }
    }

    private val request: AudioFocusRequest? = try {
        AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK)
            .setAudioAttributes(ATTRIBUTES)
            .setOnAudioFocusChangeListener(listener, main)
            .setWillPauseWhenDucked(false)
            .build()
    } catch (t: Throwable) { Log.w(TAG, "포커스 요청 만들기 실패: ${t.message}"); null }

    private val releaseLater = Runnable { abandon() }

    /** Suspend(시동 꺼짐) 중이면 포커스를 요청하지 않는다(Pleos Suspend 모드 오디오 포커스 가이드). */
    @Volatile var suspended = false

    /**
     * 말하기 직전에 부른다. 이미 잡고 있으면 놓기 예약만 취소한다. 실패해도 말은 한다(포커스는 충돌 방지용, Pleos 가이드).
     * @return 이번에 새로 포커스를 받았으면 true. 새 음성 세션의 시작이라 선행 묵음(pre-roll)을 넣을 때다.
     */
    fun acquire(): Boolean {
        main.removeCallbacks(releaseLater)
        if (held || suspended) return false
        val am = audio ?: return false
        val r = request ?: return false
        val result = try { am.requestAudioFocus(r) } catch (t: Throwable) { Log.w(TAG, "포커스 요청 예외: ${t.message}"); return false }
        held = result == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
        Log.i(TAG, "포커스 요청(내비 안내, 잠시·소리 줄이기) → ${resultName(result)}")
        return held
    }

    /** 문장이 끝났을 때 부른다. 곧 다음 문장이 오면 계속 잡고 있는다. */
    fun releaseSoon() {
        main.removeCallbacks(releaseLater)
        main.postDelayed(releaseLater, RELEASE_DELAY_MS)
    }

    fun abandon() {
        main.removeCallbacks(releaseLater)
        if (!held) return
        dropFocus()
        Log.i(TAG, "포커스 반납")
    }

    /** 잡고 있다고 기록돼 있든 아니든 반납 요청을 보낸다(포커스를 잃은 뒤의 반납도 해가 없다). */
    private fun dropFocus() {
        main.removeCallbacks(releaseLater)
        held = false
        val am = audio ?: return
        val r = request ?: return
        try { am.abandonAudioFocusRequest(r) } catch (t: Throwable) { Log.w(TAG, "포커스 반납 예외: ${t.message}") }
    }

    private fun resultName(r: Int) = when (r) {
        AudioManager.AUDIOFOCUS_REQUEST_GRANTED -> "허용"
        AudioManager.AUDIOFOCUS_REQUEST_FAILED -> "거절"
        AudioManager.AUDIOFOCUS_REQUEST_DELAYED -> "대기"
        else -> r.toString()
    }

    companion object {
        private const val TAG = "AudioFocus"
        /** 마지막 문장 뒤 이만큼 다음 문장이 없으면 포커스를 놓는다. */
        const val RELEASE_DELAY_MS = 1_500L
        /** 코치 음성의 출력 용도: 내비 안내 음성. */
        val ATTRIBUTES: AudioAttributes = AudioAttributes.Builder()
            .setUsage(AudioAttributes.USAGE_ASSISTANCE_NAVIGATION_GUIDANCE)
            .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
            .build()
    }
}
