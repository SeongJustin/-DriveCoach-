// 3D 주차 게임·도로 주행 화면(Unity as a Library). 첫 화면 "주차 연습" → "주차 게임" 카드와 "도로 주행 연습" 카드에서 GameLauncher 가 연다.
// 별도 프로세스(:game3d, AndroidManifest): Unity 는 화면을 닫을 때 자기 프로세스를 끝내므로 메인 프로세스(CoachService)와 떨어뜨린다.
// 차량 신호·제원은 CoachService 의 GameLink 에서 받아(GameLinkClient) Unity 의 "DriveCoachBridge" 오브젝트(GameMain.cs)로 넘긴다.
// Unity 쪽은 준비되면 onUnityReady(), 닫을 때 closeGame() 을 부른다(C# AndroidLink.Call).
// READY_TIMEOUT_MS 안에 준비 신호가 없으면 실패를 기록하고(GameLauncher) 안내를 띄운 뒤 닫는다.
// 이 파일은 unity/export 가 있을 때만 빌드에 들어간다(app/build.gradle.kts). 없을 때는 src/nounity 의 같은 이름 대체 화면.
package kr.drivecoach.app.game3d

import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.widget.Toast
import androidx.annotation.Keep
import com.unity3d.player.UnityPlayer
import com.unity3d.player.UnityPlayerActivity
import kr.drivecoach.app.game.GameLauncher
import kr.drivecoach.app.game.GameLink
import kr.drivecoach.app.game.GameLinkClient
import java.util.Locale

class Game3DActivity : UnityPlayerActivity() {
    private lateinit var link: GameLinkClient
    private val main = Handler(Looper.getMainLooper())
    private var ready = false
    private var spec = ""
    private var lastState = ""
    private var sentState = ""
    private val fallback = Runnable { if (!ready) openFallback() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        link = GameLinkClient(this, ::onState, ::onSpec, ::onLinkLost)
        main.postDelayed(fallback, READY_TIMEOUT_MS)
    }

    override fun onStart() {
        super.onStart()
        link.start()
        requestFocus()
    }

    override fun onStop() {
        link.stop()
        abandonFocus()
        super.onStop()
    }

    // ── 오디오 포커스(Pleos 오디오 포커스 가이드): 효과음(경고 삐 소리)을 내기 전에 포커스를 잡고, 변화에 대응한다.
    // 게임 소리(USAGE_GAME → MUSIC 컨텍스트)는 짧은 효과음뿐이라 음악을 끊지 않고 줄이게(MAY_DUCK) 요청한다.
    // 잃으면(통화·Gleo·다른 미디어) Unity 소리를 멈추고, 다시 얻으면 켠다. 화면을 떠나면 반납한다.
    private val audio by lazy { getSystemService(Context.AUDIO_SERVICE) as? AudioManager }
    private var focusHeld = false
    private val focusListener = AudioManager.OnAudioFocusChangeListener { change ->
        when (change) {
            AudioManager.AUDIOFOCUS_LOSS, AudioManager.AUDIOFOCUS_LOSS_TRANSIENT -> { focusHeld = change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT; setGameSound(false) }
            AudioManager.AUDIOFOCUS_GAIN -> { focusHeld = true; setGameSound(true) }
        }
    }
    private val focusRequest: AudioFocusRequest? by lazy {
        runCatching {
            AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK)
                .setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
                .setOnAudioFocusChangeListener(focusListener, main)
                .setWillPauseWhenDucked(false)
                .build()
        }.getOrNull()
    }

    private fun requestFocus() {
        val am = audio ?: return
        val r = focusRequest ?: return
        val res = runCatching { am.requestAudioFocus(r) }.getOrDefault(AudioManager.AUDIOFOCUS_REQUEST_FAILED)
        focusHeld = res == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
        Log.i(TAG, "오디오 포커스 요청(게임 효과음) → ${if (focusHeld) "허용" else "거절/대기 $res"}")
        setGameSound(focusHeld)
    }

    private fun abandonFocus() {
        val am = audio ?: return
        val r = focusRequest ?: return
        runCatching { am.abandonAudioFocusRequest(r) }
        focusHeld = false
    }

    /** 앱 첫 화면에서 고른 메뉴(도로 주행 / 주차·핸들 연습)를 게임에 알린다. 이미 떠 있는 게임에 다시 들어올 때(onNewIntent)도. */
    private fun sendMenuMode() {
        if (ready) runCatching { UnityPlayer.UnitySendMessage(BRIDGE, "OnMenuMode", intent?.getStringExtra("dc_menu") ?: "practice") }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        sendMenuMode()
    }

    private fun setGameSound(on: Boolean) {
        if (ready) runCatching { UnityPlayer.UnitySendMessage(BRIDGE, "OnAudioFocus", if (on) "1" else "0") }
    }

    override fun onDestroy() {
        main.removeCallbacks(fallback)
        super.onDestroy()
    }

    /**
     * Unity 로 보내는 줄: "핸들각;기어;브레이크;속도;회전수;액셀%;방향;비상;전조등;와이퍼;벨트" (모르는 값은 -1 · U · UNKNOWN)
     * 뒤 다섯 칸(실차 스위치)은 GameLink.switches — 비어 있으면 붙이지 않는다(예전 Unity 도 앞 여섯 칸만 읽으면 된다).
     */
    private fun onState(steer: Double, gear: String, brake: Boolean, speedKmh: Double, rpm: Double, accelPct: Double, switches: String) {
        lastState = String.format(Locale.ROOT, "%.1f;%s;%d;%.1f;%.0f;%.0f", steer, gear, if (brake) 1 else 0, speedKmh, rpm, accelPct) +
            (if (switches.isNotEmpty()) ";$switches" else "")
        if (ready) send()
    }

    private fun send() {
        if (lastState.isEmpty() || lastState == sentState) return
        sentState = lastState
        UnityPlayer.UnitySendMessage(BRIDGE, "OnVehicleState", lastState)
    }

    private fun onSpec(json: String) {
        if (json.isEmpty()) return
        spec = json
        if (ready) UnityPlayer.UnitySendMessage(BRIDGE, "OnCarSpec", json)
    }

    /** Unity(GameMain.Start)가 부른다 — Unity 스레드에서 오므로 화면 스레드로 넘긴다. */
    @Keep
    fun onUnityReady() {
        runOnUiThread {
            if (ready) return@runOnUiThread
            ready = true
            main.removeCallbacks(fallback)
            GameLauncher.markReady(this)
            Log.i(TAG, "3D 준비됨")
            UnityPlayer.UnitySendMessage(BRIDGE, "OnCarSpec", spec.ifEmpty { GameLink.specJson(null) })
            setGameSound(focusHeld)
            sendMenuMode()
            sentState = ""
            send()
        }
    }

    /** Unity 가 판을 시작할 때 할 일을 넘긴다. 코치 음성으로 한 번 읽는다. */
    @Keep
    fun speakHint(text: String) {
        runOnUiThread { link.speak(text) }
    }

    /** Unity 의 닫기·뒤로 가기. 화면을 닫으면 Unity 가 이 프로세스(:game3d)를 끝낸다. */
    @Keep
    fun closeGame() {
        runOnUiThread { finish() }
    }

    /**
     * 코치 서비스와 연결이 끊기면 차량 신호(기어·속도)가 더 오지 않아, 실제 차가 움직여도 3D 를 멈출 수 없다.
     * 그래서 3D 화면을 닫는다. 다시 열면 새로 연결한다.
     */
    private fun onLinkLost() {
        if (isFinishing) return
        Log.w(TAG, "차량 신호 연결이 끊겨 3D 를 닫음")
        Toast.makeText(this, "차량 신호 연결이 끊겨 3D 연습을 닫았습니다.", Toast.LENGTH_LONG).show()
        finish()
    }

    private fun openFallback() {
        Log.w(TAG, "3D 가 ${READY_TIMEOUT_MS / 1000}초 안에 열리지 않음")
        GameLauncher.markFailed(this)
        Toast.makeText(this, "3D 화면을 열지 못했습니다. 다시 시도해 주세요.", Toast.LENGTH_LONG).show()
        finish()
    }

    companion object {
        private const val TAG = "Game3DActivity"
        private const val BRIDGE = "DriveCoachBridge"
        private const val READY_TIMEOUT_MS = 40_000L   // 에뮬레이터 첫 실행이 17초 걸림 → 넉넉히
    }
}
