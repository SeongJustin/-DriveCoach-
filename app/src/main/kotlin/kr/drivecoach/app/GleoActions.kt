// 담당: S (11/8 밤, Pleos 가이드 재점검) — Gleo 음성 연동(Gleo Interface, 문서 "앱 연동 가이드").
// assets/schema.json 에 함수 3개를 적어 두면 Gleo 가 운전자의 말을 함수 호출로 바꿔 MainActivity 를 연다
// (인텐트 action = 함수 이름, extra "ACTION_PARAM" = JSON 문자열). 결과는 7초 안에 브로드캐스트
// ai.pleos.playground.intent.action.APP_ACTION_RESPONSE(extra packageName, response = JSON)로 돌려준다.
// 메시지는 영어(문서 규칙), Gleo 가 그걸 바탕으로 한국어로 대답한다. 스키마는 Pleos 검토·승인 뒤 켜진다.
package kr.drivecoach.app

import android.content.Context
import android.content.Intent
import android.util.Log
import org.json.JSONArray
import org.json.JSONObject

object GleoActions {
    const val LEARN = "LEARN_CAR_FEATURE"
    const val PARKING = "START_PARKING_PRACTICE"
    const val COURSE = "START_PRACTICE_COURSE"
    val ALL = setOf(LEARN, PARKING, COURSE)

    private const val RESPONSE_ACTION = "ai.pleos.playground.intent.action.APP_ACTION_RESPONSE"
    private const val TAG = "GleoActions"

    fun isGleo(intent: Intent?): Boolean = intent?.action in ALL

    /** ACTION_PARAM 읽기. 문서 예시처럼 따옴표로 한 번 더 감싼 문자열("\"{...}\"")도 받는다. */
    fun params(intent: Intent): JSONObject {
        val raw = intent.getStringExtra("ACTION_PARAM")?.trim() ?: return JSONObject()
        return runCatching { JSONObject(raw) }.recoverCatching {
            var s = raw
            if (s.startsWith("\"") && s.endsWith("\"") && s.length >= 2) s = s.substring(1, s.length - 1)
            JSONObject(s.replace("\\\"", "\""))
        }.getOrElse { Log.w(TAG, "ACTION_PARAM 해석 실패: ${it.message}"); JSONObject() }
    }

    /** Gleo 에 결과 알리기. status: success | asking | fail, mode: voice | beep | silent */
    fun respond(ctx: Context, functionName: String, status: String, message: String, data: List<Map<String, String>> = emptyList(), mode: String = "voice") {
        runCatching {
            val json = JSONObject().apply {
                put("functionName", functionName)
                put("status", status)
                put("message", message)
                if (data.isNotEmpty()) put("data", JSONArray(data.map { JSONObject(it) }).toString())
                if (mode != "voice") put("mode", mode)
            }
            ctx.sendBroadcast(Intent(RESPONSE_ACTION).apply {
                putExtra("packageName", ctx.packageName)
                putExtra("response", json.toString())
            })
            Log.i(TAG, "응답 $functionName $status")
        }.onFailure { Log.w(TAG, "응답 실패: ${it.message}") }
    }
}
