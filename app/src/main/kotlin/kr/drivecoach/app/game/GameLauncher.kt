// 주차·핸들 연습 열기: 3D(Unity). 3D 가 들어 있지 않은 빌드면 안내만 띄운다.
// 3D 가 준비 신호(onUnityReady)를 못 보내고 끝나면(멈춤·강제 종료·시간 초과) 실패로 기록한다. 두 번 연속이면(시간 초과는 한 번에)
// [use3d] 가 false 가 된다(자가 검사가 읽는다). 3D 가 한 번이라도 정상으로 열리면 기록을 지운다.
// 기록은 앱 버전 + 3D 빌드 시각(UNITY_STAMP)마다 따로: 3D 를 새로 만들어 넣으면 다시 3D 부터 시도한다.
// 기록은 파일(filesDir): 게임은 다른 프로세스(:game3d)라 SharedPreferences 캐시가 어긋날 수 있어서.
package kr.drivecoach.app.game

import android.content.Context
import android.content.Intent
import android.util.Log
import kr.drivecoach.app.BuildConfig
import kr.drivecoach.app.game3d.Game3DActivity
import java.io.File

object GameLauncher {
    private const val TAG = "GameLauncher"
    private const val PENDING = "game3d_pending"
    private const val FAILS = "game3d_fails_v"

    /** 3D 를 쓸 수 있나(들어 있고, 이 버전에서 두 번 연속 실패하지 않았나) */
    fun use3d(ctx: Context): Boolean = BuildConfig.HAS_UNITY && fails(ctx) < 2

    /** [menu]: "road" = 도로 주행(나라·도시·구·동 고르기), null = 주차·핸들 연습 */
    fun open(ctx: Context, unityArgs: String? = null, menu: String? = null) {
        val pending = File(ctx.filesDir, PENDING)
        if (pending.exists()) {
            // 지난번 3D 가 준비 신호 없이 끝났다
            pending.delete()
            setFails(ctx, fails(ctx) + 1)
            Log.w(TAG, "지난번 3D 가 열리지 않음 → 실패 ${fails(ctx)}번")
        }
        // 3D 가 없는 빌드면 알림만.
        if (BuildConfig.HAS_UNITY) {
            runCatching { pending.writeText(System.currentTimeMillis().toString()) }
            val i = Intent(ctx, Game3DActivity::class.java)
            i.putExtra("dc_menu", menu ?: "practice")
            if (unityArgs != null) i.putExtra("unity", unityArgs)   // Unity 명령줄(자가 검사: -dcScreen stage:7 -dcAutoExit 20)
            ctx.startActivity(i)
            // 공회전 제한: 엔진이 도는지는 앱이 읽을 수 없어(엔진 회전수는 권한 밖) 들어갈 때 한 번 알린다
            if (unityArgs == null) android.widget.Toast.makeText(ctx, ctx.getString(kr.drivecoach.app.R.string.game_idle_notice), android.widget.Toast.LENGTH_LONG).show()
        } else {
            android.widget.Toast.makeText(ctx, "이 빌드에는 3D 연습이 들어 있지 않습니다", android.widget.Toast.LENGTH_LONG).show()
        }
    }

    /** 3D 가 열렸다(게임 프로세스에서 부른다) */
    fun markReady(ctx: Context) {
        File(ctx.filesDir, PENDING).delete()
        setFails(ctx, 0)
    }

    /** 3D 가 시간 안에 열리지 않았다(기록만) */
    fun markFailed(ctx: Context) {
        File(ctx.filesDir, PENDING).delete()
        setFails(ctx, 2)
    }

    /** 자가 검사용: 3D 준비 기록이 남아 있나(true = 아직 열리지 않음) */
    fun pending(ctx: Context): Boolean = File(ctx.filesDir, PENDING).exists()

    fun fails(ctx: Context): Int =
        runCatching { File(ctx.filesDir, FAILS + BuildConfig.VERSION_CODE + "_" + BuildConfig.UNITY_STAMP).readText().trim().toInt() }.getOrDefault(0)

    private fun setFails(ctx: Context, n: Int) {
        val f = File(ctx.filesDir, FAILS + BuildConfig.VERSION_CODE + "_" + BuildConfig.UNITY_STAMP)
        runCatching { if (n <= 0) f.delete() else f.writeText(n.toString()) }
    }
}
