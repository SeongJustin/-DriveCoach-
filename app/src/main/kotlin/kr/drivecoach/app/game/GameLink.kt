// 게임 프로세스(:game3d) ↔ CoachService(메인 프로세스) 연결.
// Unity 는 화면을 닫을 때 자기 프로세스를 끝내므로 3D 화면(Game3DActivity)은 별도 프로세스(:game3d)에서 돌리고, 차량 신호·제원은 Messenger 로 받는다.
// 이 파일의 GameLink 는 메인 프로세스(CoachService) 쪽 송신기, GameLink.Receiver 는 게임 쪽 수신기 규약(상수·번들 키)이다.
package kr.drivecoach.app.game

import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.Message
import android.os.Messenger
import android.os.RemoteException
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kr.drivecoach.core.signal.CarSpec
import kr.drivecoach.core.signal.VehicleState
import org.json.JSONObject

/**
 * CoachService 가 갖고 있는 송신기. 게임이 [MSG_REGISTER] 로 자기 Messenger 를 등록하면,
 * 차량 상태가 바뀔 때마다 [MSG_STATE], 차종이 바뀌면 [MSG_SPEC] 을 보낸다. 게임이 없으면 구독을 멈춘다.
 * 모든 처리는 메인 스레드(CoachService 규약).
 */
class GameLink(
    private val scope: CoroutineScope,
    private val states: () -> StateFlow<VehicleState>,
    private val specs: StateFlow<CarSpec?>,
) {
    /** 3D 연습이 할 일을 음성으로 한 번 읽어 달라고 보낸 글. CoachService 가 받아 Pleos TTS 로 말한다. */
    var onSpeak: ((String) -> Unit)? = null
    private val clients = mutableListOf<Messenger>()
    private var job: Job? = null

    private val handler = Handler(Looper.getMainLooper()) { msg ->
        when (msg.what) {
            MSG_REGISTER -> msg.replyTo?.let { register(it) }
            MSG_UNREGISTER -> msg.replyTo?.let { clients.remove(it); stopIfIdle() }
            MSG_SPEAK -> msg.data?.getString(KEY_TEXT)?.let { t -> onSpeak?.invoke(t) }
        }
        true
    }

    /** onBind 에서 돌려줄 바인더의 주인. */
    val messenger = Messenger(handler)

    private fun register(m: Messenger) {
        if (m !in clients) clients += m
        send(m, specMessage(specs.value))
        if (job == null) {
            job = scope.launch {
                launch { states().collect { s -> broadcast { stateMessage(s) } } }
                launch { specs.collect { sp -> broadcast { specMessage(sp) } } }
            }
        }
    }

    private fun broadcast(make: () -> Message) {
        for (c in clients.toList()) send(c, make())
        stopIfIdle()
    }

    private fun send(to: Messenger, msg: Message) {
        try {
            to.send(msg)
        } catch (e: RemoteException) {
            // 게임 프로세스가 끝났다(닫기). 목록에서 뺀다.
            Log.i(TAG, "game client gone: ${e.javaClass.simpleName}")
            clients.remove(to)
        }
    }

    private fun stopIfIdle() {
        if (clients.isEmpty()) {
            job?.cancel()
            job = null
        }
    }

    companion object {
        private const val TAG = "GameLink"

        /** 게임 쪽(GameLinkClient)이 CoachService 에 바인딩할 때 쓰는 액션(LocalBinder 대신 Messenger 를 받는다). */
        const val ACTION = "kr.drivecoach.app.action.GAME_LINK"
        const val MSG_REGISTER = 1
        const val MSG_UNREGISTER = 2
        const val MSG_STATE = 10
        const val MSG_SPEC = 11
        const val MSG_SPEAK = 20
        const val KEY_TEXT = "text"

        const val KEY_STEER = "steer"
        const val KEY_GEAR = "gear"
        const val KEY_BRAKE = "brake"
        const val KEY_SPEED = "speed"
        const val KEY_RPM = "rpm"
        const val KEY_ACCEL = "accel"   // 액셀 페달 0~100 %, 모르면 -1
        // 방향지시등·비상등·전조등·와이퍼·안전벨트를 게임에도
        const val KEY_SWITCHES = "sw"   // "방향;비상;전조등;와이퍼;벨트" — L/R/O/U · 1/0/-1 · OFF/AUTO/LOW/HIGH/UNKNOWN · OFF/INT/LOW/HIGH/UNKNOWN · 1/0/-1
        const val KEY_SPEC = "spec"

        fun stateMessage(s: VehicleState): Message = Message.obtain(null, MSG_STATE).apply {
            data = Bundle().apply {
                putDouble(KEY_STEER, s.steeringAngleDeg ?: 0.0)
                putString(KEY_GEAR, s.gear.name)
                putBoolean(KEY_BRAKE, s.brakePressed == true)
                putDouble(KEY_SPEED, s.speedKmh ?: 0.0)
                putDouble(KEY_RPM, s.rpm ?: -1.0)
                putDouble(KEY_ACCEL, s.accelPedalPct ?: -1.0)
                putString(KEY_SWITCHES, switches(s))
            }
        }

        /** 실차 스위치 한 줄(게임이 그대로 쓴다). 벨트: 경고등 켜짐 = 미착용(0), 꺼짐 = 착용(1), 모름 -1 */
        fun switches(s: VehicleState): String {
            val turn = when (s.turnSignal) {
                kr.drivecoach.core.signal.TurnSignal.LEFT -> "L"
                kr.drivecoach.core.signal.TurnSignal.RIGHT -> "R"
                kr.drivecoach.core.signal.TurnSignal.OFF -> "O"
                else -> "U"
            }
            val hazard = when (s.hazardOn) { true -> 1; false -> 0; null -> -1 }
            val belt = when (s.seatBeltWarning) { true -> 0; false -> 1; null -> -1 }
            return "$turn;$hazard;${s.headlight.name};${s.wiper.name};$belt"
        }

        fun specMessage(spec: CarSpec?): Message = Message.obtain(null, MSG_SPEC).apply {
            data = Bundle().apply { putString(KEY_SPEC, specJson(spec)) }
        }

        /** 게임이 쓰는 제원(m, 도). 인식 전이면 기본 차종. 없는 값은 넣지 않는다(게임이 전장·축거로 나눠 쓴다). */
        fun specJson(spec: CarSpec?): String {
            val s = spec ?: CarSpec.AVANTE_CN8
            val o = JSONObject()
                .put("name", s.name)
                .put("lengthM", s.lengthM)
                .put("widthM", s.widthM)
                .put("wheelbaseM", s.wheelbaseM)
                .put("maxSteeringWheelDeg", s.maxSteeringWheelDeg)
                .put("steeringRatio", s.steeringRatio)
                .put("turningRadiusEstimated", s.turningRadiusEstimated)
            s.frontOverhangM?.let { o.put("frontOverhangM", it) }
            s.rearOverhangM?.let { o.put("rearOverhangM", it) }
            s.trackFrontM?.let { o.put("trackFrontM", it) }
            s.trackRearM?.let { o.put("trackRearM", it) }
            s.minTurningRadiusM?.let { o.put("minTurningRadiusM", it) }
            return o.toString()
        }
    }
}
