// 게임 프로세스 쪽 GameLink 수신기(3D 게임용). CoachService 에 바인딩해 차량 신호·제원을 Messenger 로 받는다.
package kr.drivecoach.app.game

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.Message
import android.os.Messenger
import android.util.Log
import kr.drivecoach.app.CoachService

class GameLinkClient(
    private val ctx: Context,
    private val onState: (steer: Double, gear: String, brake: Boolean, speedKmh: Double, rpm: Double, accelPct: Double, switches: String) -> Unit,
    private val onSpec: (json: String) -> Unit,
    /** 서비스 연결이 끊겼을 때. 차량 신호가 더 오지 않으므로 3D 화면은 스스로 닫는다(실제 차가 움직여도 멈추지 못하는 일을 막는다). */
    private val onLost: () -> Unit = {},
) {
    private var service: Messenger? = null
    private var bound = false

    private val incoming = Messenger(Handler(Looper.getMainLooper()) { msg -> onMessage(msg); true })

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val m = Messenger(binder ?: return)
            service = m
            runCatching { m.send(Message.obtain(null, GameLink.MSG_REGISTER).apply { replyTo = incoming }) }
                .onFailure { Log.w(TAG, "register failed: ${it.message}") }
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            service = null
            Log.w(TAG, "서비스 연결 끊김")
            onLost()
        }

        override fun onBindingDied(name: ComponentName?) {
            service = null
            Log.w(TAG, "서비스 바인딩 죽음")
            onLost()
        }

        override fun onNullBinding(name: ComponentName?) {
            Log.w(TAG, "서비스가 바인더를 주지 않음")
            onLost()
        }
    }

    private fun onMessage(msg: Message) {
        val d = msg.data ?: return
        when (msg.what) {
            GameLink.MSG_STATE -> onState(
                d.getDouble(GameLink.KEY_STEER), d.getString(GameLink.KEY_GEAR) ?: "UNKNOWN", d.getBoolean(GameLink.KEY_BRAKE),
                d.getDouble(GameLink.KEY_SPEED), d.getDouble(GameLink.KEY_RPM, -1.0), d.getDouble(GameLink.KEY_ACCEL, -1.0),
                d.getString(GameLink.KEY_SWITCHES).orEmpty(),
            )
            GameLink.MSG_SPEC -> onSpec(d.getString(GameLink.KEY_SPEC).orEmpty())
        }
    }

    fun start() {
        if (bound) return
        val intent = Intent(ctx, CoachService::class.java).setAction(GameLink.ACTION)
        bound = ctx.bindService(intent, connection, Context.BIND_AUTO_CREATE)
    }

    /** 3D 연습의 할 일을 코치 음성으로 한 번 읽어 달라고 보낸다. */
    fun speak(text: String) {
        val m = service ?: return
        runCatching { m.send(Message.obtain(null, GameLink.MSG_SPEAK).apply { data = android.os.Bundle().apply { putString(GameLink.KEY_TEXT, text) } }) }
    }

    fun stop() {
        service?.let { m -> runCatching { m.send(Message.obtain(null, GameLink.MSG_UNREGISTER).apply { replyTo = incoming }) } }
        service = null
        if (bound) {
            runCatching { ctx.unbindService(connection) }
            bound = false
        }
    }

    companion object {
        private const val TAG = "GameLinkClient"
    }
}
