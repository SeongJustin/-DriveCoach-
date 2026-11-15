// 담당: A (신호 소스·차종·주행 규제 생명주기, 권한 요청, 진단) → S (CoachController·TTS 연결, 첫 실제 화면, 흰 바탕 스타일·연습 그림, 포그라운드 서비스 바인딩, 핸들 감각·직각 주차·코스 주행 화면)
// Compose 는 compose-bom 2024.06.00(ui 1.6.8, material3 1.2.1) 의 표준 API 만 쓰고 모든 심볼을 명시 import 했다.
package kr.drivecoach.app

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Surface
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.runtime.getValue
import androidx.core.content.ContextCompat
import kr.drivecoach.app.game.GameLauncher
import kr.drivecoach.app.input.WheelButtonReceiver
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.CoachTheme
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.app.ui.PracticeArt
import kotlinx.coroutines.launch

/**
 * 앱 진입점(화면 담당). 신호 소스·차종·주행 규제·TTS·CoachController 는 [CoachService](포그라운드 서비스)가 소유하고,
 * 이 Activity 는 서비스를 띄운 뒤 바인딩해서 **같은 StateFlow 를 보여 주기만** 한다. 그래서 기어 D 에서 Pleos 런처가 화면을 덮거나
 * 사용자가 앱을 닫아도 음성 코칭은 계속된다. 두 가지 화면:
 *  - P 상태 화면(미니멀, 11/3~4): "인식 차종: …" + 설정(톱니바퀴) 한 줄, 그 아래 메뉴 3개(차 기능 익히기 / 도로 법규 / 코스 주행)가 화면을 꽉 채운다(설정: 코칭 음성·음성 속도·진단·버전·앱 종료).
 *    "차 기능 익히기" → 선택 화면(검색창, 처음부터 순서대로/이어서, 핸들 감각, 직각 주차, 장별 항목 칩). 열린 활동에 따라
 *      연습 카드(그림 + 문장) / 핸들 감각 카드(궤적 캔버스 + 문장·조향각) / 직각 주차 카드(단계 일러스트 + 문장·체크리스트) + 확인/다음/다시
 *      / 코스 주행 카드(5-1 선택지 2열 → 브리핑 요약 + "내비로 보내기" → 경로 계산/주행 상태 → 도착 리포트 / 오류·다시 시도)
 *  - 주행 화면(11/5): 미니멀 정지 그림(drawable/driving_still) + 작은 자막 "기어 P단 상태에서만 … 실행이 가능합니다". 안내는 음성뿐(런처가 앱을 덮어도 음성은 계속).
 *    직각 주차 슬로우 모드 중(R/D)에는 단계 한 줄을 더 보여 주고, 차가 서 있고 "확인"으로만 넘어가는 단계면 확인 버튼 하나를 둔다
 *    (STT·휠 버튼 연결 전 대체. 정지 상태에서만 보이므로 0장 "주행 중 화면 조작 없음" 과 충돌하지 않는다).
 * 앱 시작 직후에는 앱 이름 화면을 2.5초 보여 준다(탭으로 건너뛰기). 바인딩 전에는 "서비스 연결 중…" 한 줄만 보인다(같은 프로세스라 보통 수십 ms).
 * 스타일(흰 바탕, 검정 글씨, 라임 포인트)은 ui/CoachTheme.kt 한 곳에서 정한다. 글자 크기는 에뮬레이터(2560×1440, 160dpi 를 축소해서 봄)를 감안해 크게.
 * 연습 항목 그림은 ui/PracticeArt.resIdFor(itemId) 가 주는 drawable 을 쓰고, 없는 항목은 문장만 보여 준다.
 */
@OptIn(ExperimentalLayoutApi::class)
class MainActivity : ComponentActivity() {
    /** 바인딩된 서비스. null 이면 아직 연결 전. Compose 가 읽으므로 mutableStateOf. */
    private val service = mutableStateOf<CoachService?>(null)
    private var bound = false
    /** 권한 결과가 바인딩보다 먼저 오면 연결된 뒤에 신호 소스를 재시작한다. */
    private var pendingSignalRestart = false
    private lateinit var wheel: WheelButtonReceiver
    internal val showDiag = mutableStateOf(false)
    /** 인트로 표시 중. 시간이 지나거나 화면을 누르면 false. */
    internal val showIntro = mutableStateOf(true)
    /** 설정 화면이 열려 있는지(헤더 톱니바퀴). */
    internal val settingsOpen = mutableStateOf(false)
    /** 설정 안 "앱 종료" 확인 대화상자. */
    internal val confirmExit = mutableStateOf(false)
    /** "차 기능 익히기" 선택 화면이 열려 있는지(하위 연습이 끝나면 이 화면으로 돌아온다). */
    internal val practiceMenuOpen = mutableStateOf(false)
    /** (11/15 대표: 직각·평행 주차는 주차·핸들 연습으로) "주차·핸들 연습" 선택 화면(3D 연습·직각 주차·평행 주차)이 열려 있는지. */
    internal val parkingMenuOpen = mutableStateOf(false)
    /** 선택 화면의 검색어. */
    internal val practiceQuery = mutableStateOf("")
    /** (11/14 베타: 항목을 보고 목록으로 돌아오면 맨 위로 돌아간다) 목록 스크롤 자리를 화면 밖에 둔다 */
    internal val practiceScroll = androidx.compose.foundation.ScrollState(0)

    /** 서비스가 붙기 전에 온 Gleo 음성 호출(붙자마자 처리, Gleo 는 7초 기다린다) */
    private var pendingGleo: Intent? = null

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val svc = (binder as? CoachService.LocalBinder)?.service ?: return
            service.value = svc
            if (pendingSignalRestart) { pendingSignalRestart = false; svc.restartSignals() }
            pendingGleo?.let { pendingGleo = null; runGleo(svc, it) }
        }

        /** 같은 프로세스라 실제로는 프로세스가 죽을 때만 온다. */
        override fun onServiceDisconnected(name: ComponentName?) { service.value = null }
    }

    /** 런타임 권한이 허용되면 신호 소스를 다시 시작해 속도 구독을 건다(기존 `signals.stop(); signals.start()` 패턴을 서비스가 수행). */
    private fun restartSignals() {
        val svc = service.value
        if (svc != null) svc.restartSignals() else pendingSignalRestart = true
    }

    /** 권한 진단: 매니페스트에 선언한 권한이 기기(에뮬레이터)에 정의돼 있는지·보호 수준·부여 여부. SDK 는 checkSelfPermission 으로만 검사한다. */
    internal val permDiag = mutableStateOf<List<String>>(emptyList())

    private val multiPermission = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        val granted = result.filterValues { it }.keys
        permDiag.value = permDiag.value + "요청 결과: 허용 ${granted.size}/${result.size}"
        // POST_NOTIFICATIONS 는 알림 표시에만 영향. 차량 권한이 하나라도 새로 허용됐을 때만 신호 소스를 다시 건다.
        if (granted.any { it != POST_NOTIFICATIONS }) restartSignals()
        permDiag.value = permDiag.value + inspectPermissions().filter { !it.startsWith("OK") }
    }

    /** 에뮬레이터에 어떤 Pleos 패키지가 있고, 그 패키지가 어떤 권한을 정의하는지 (QUERY_ALL_PACKAGES 필요, 진단용). */
    internal fun inspectPleosPackages(): List<String> {
        val out = mutableListOf<String>()
        val pkgs = runCatching { packageManager.getInstalledPackages(PackageManager.GET_PERMISSIONS) }.getOrElse { return listOf("패키지 조회 실패 ${it.message}") }
        out += "설치 패키지 ${pkgs.size}개, Build ${android.os.Build.DISPLAY} / API ${android.os.Build.VERSION.SDK_INT}"
        for (p in pkgs) {
            val defined = p.permissions?.map { it.name }.orEmpty()
            val pleosDefined = defined.filter { it.startsWith("pleos.") }
            if (p.packageName.contains("pleos", ignoreCase = true) || pleosDefined.isNotEmpty()) {
                out += "PKG ${p.packageName} v${p.versionName} 정의권한 ${defined.size}개" +
                    (if (pleosDefined.isNotEmpty()) ": " + pleosDefined.joinToString { it.substringAfterLast('.') } else "")
            }
        }
        val svc = android.content.Intent().setClassName("ai.pleos.playground.service.vehicle", "ai.pleos.playground.service.vehicle.VehicleService")
        out += "VehicleService resolve: " + runCatching { packageManager.queryIntentServices(svc, 0).size }.getOrElse { "err ${it.message}" }
        return out
    }

    internal fun inspectPermissions(): List<String> {
        val requested = runCatching {
            packageManager.getPackageInfo(packageName, PackageManager.GET_PERMISSIONS).requestedPermissions?.toList()
        }.getOrNull().orEmpty()
        return requested.filter { it.startsWith("pleos.") || it.startsWith("android.car.") }.map { name ->
            val short = name.substringAfterLast('.')
            val granted = ContextCompat.checkSelfPermission(this, name) == PackageManager.PERMISSION_GRANTED
            val level = runCatching {
                val info = packageManager.getPermissionInfo(name, 0)
                val base = info.protection
                val flags = info.protectionFlags
                "def=${info.packageName} lvl=$base${if (flags != 0) "+$flags" else ""}"
            }.getOrElse { "미정의(${it.javaClass.simpleName})" }
            "${if (granted) "OK" else "FAIL"} $short $level"
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // 시스템이 화면을 다시 만들면(메모리 회수 뒤 복귀 등) 보던 화면으로 돌아간다(Pleos 가이드: 백그라운드 뒤 마지막 상태 복원).
        // 연습·주차·코스 진행 자체는 CoachService 가 들고 있으므로 여기서는 화면 쪽 상태(인트로·설정·선택 화면·검색어)만 되살린다.
        savedInstanceState?.let { b ->
            showIntro.value = false
            settingsOpen.value = b.getBoolean(KEY_SETTINGS, false)
            practiceMenuOpen.value = b.getBoolean(KEY_PRACTICE_MENU, false)
            parkingMenuOpen.value = b.getBoolean(KEY_PARKING_MENU, false)
            practiceQuery.value = b.getString(KEY_PRACTICE_QUERY) ?: ""
        }
        // 코칭 본체(신호·TTS·CoachController)는 서비스가 든다. 먼저 포그라운드 서비스로 띄우고(화면이 가려져도 살아남게), 바인딩해서 상태를 본다.
        ContextCompat.startForegroundService(this, CoachService.startIntent(this))
        bound = bindService(CoachService.startIntent(this), connection, Context.BIND_AUTO_CREATE)
        wheel = WheelButtonReceiver(onPress = { press -> service.value?.let { it.coach.onWheelButton(press) } })

        // 패키지 전체 스캔(진단용)은 설정 > 진단 보기를 켤 때만 백그라운드에서 한다(베타 D-27). 여기서는 거부된 권한만 가볍게.
        permDiag.value = inspectPermissions().filter { !it.startsWith("OK") }
        // 부여되지 않은 권한 전부를 런타임으로 요청해 본다 (dangerous 면 대화상자, normal 이면 즉시 결과, 미정의면 거부).
        // API 33+ 에서는 알림 권한(POST_NOTIFICATIONS)도 같이 요청한다. 거부돼도 서비스는 동작한다(알림만 안 보임).
        val missing = runCatching {
            packageManager.getPackageInfo(packageName, PackageManager.GET_PERMISSIONS).requestedPermissions?.toList()
        }.getOrNull().orEmpty().filter { (it.startsWith("pleos.") || it.startsWith("android.car.")) &&
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }.toMutableList()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) missing += POST_NOTIFICATIONS
        if (missing.isNotEmpty()) multiPermission.launch(missing.toTypedArray())
        // 패키지·권한 조사(진단용)는 설정에서 '진단 보기' 를 켤 때만, 백그라운드에서(베타 D-27: 매 시작마다 메인 스레드 전체 스캔).

        // Pleos CCP 가이드(11/8): Back 은 설정·선택 화면·열린 활동이면 한 단계 뒤로, 최상단이면 앱을 숨긴다
        // (moveTaskToBack — finish 로 끝내면 심사 반려 사유). 숨겨도 CoachService(코칭·음성)는 계속 돈다.
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                val svc = service.value
                when {
                    settingsOpen.value -> settingsOpen.value = false
                    // (11/15 오류 검수) 연습 카드가 열려 있으면 카드부터 닫는다(전에는 첫 Back 에 화면 변화가 없었다)
                    svc != null && svc.coach.subMode.value != SubMode.NONE -> svc.coach.endActivity()
                    practiceMenuOpen.value -> practiceMenuOpen.value = false
                    parkingMenuOpen.value -> parkingMenuOpen.value = false
                    else -> moveTaskToBack(true)
                }
            }
        })
        // (11/15 오류 검수) 화면을 되살릴 때나 최근 앱에서 다시 열 때는 처음 받은 Gleo 인텐트가 그대로 다시 온다 → 처음 한 번만 처리
        val fromHistory = ((intent?.flags ?: 0) and Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY) != 0
        if (savedInstanceState == null && !fromHistory) handleGleo(intent)

        setContent {
            CoachTheme {
                Surface(modifier = Modifier.fillMaxSize(), color = CoachColors.Background) {
                    val svc = service.value   // mutableStateOf 읽기 → 바인딩되면 재구성
                    val intro by showIntro
                    when {
                        intro -> IntroScreen()
                        svc == null -> ConnectingScreen()
                        else -> CoachRoot(svc)
                    }
                }
            }
        }
    }

    /** singleTask: 이미 떠 있을 때 Gleo 호출은 여기로 온다 */
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleGleo(intent)
    }

    // ═══════════════════════════ Gleo 음성 연동(assets/schema.json) ═══════════════════════════

    private fun handleGleo(i: Intent?) {
        if (!GleoActions.isGleo(i)) return
        val svc = service.value
        if (svc == null) pendingGleo = i else runGleo(svc, i!!)
    }

    private fun runGleo(svc: CoachService, i: Intent) {
        val fn = i.action ?: return
        val st = svc.coach.stateFlow.value
        val moving = (st.speedKmh ?: 0.0) > 3.0 || st.gear == Gear.D || st.gear == Gear.R
        showIntro.value = false
        if (moving) {
            GleoActions.respond(this, fn, "fail", "This can only be opened while the car is stopped and in park (P), for safety.")
            return
        }
        when (fn) {
            GleoActions.LEARN -> {
                val name = GleoActions.params(i).optString("featureName", "").trim().take(40)   // 11/15: 밖에서 온 글이라 길이 제한
                settingsOpen.value = false
                svc.coach.endActivity()
                practiceQuery.value = name
                practiceMenuOpen.value = true
                GleoActions.respond(this, fn, "success",
                    if (name.isEmpty()) "Opened the list of car feature practices." else "Opened the car feature practices that match the requested feature.",
                    if (name.isEmpty()) emptyList() else listOf(mapOf("featureName" to name)))
            }
            GleoActions.PARKING -> {
                settingsOpen.value = false
                practiceMenuOpen.value = false
                GameLauncher.open(this)
                GleoActions.respond(this, fn, "success", "Opening the 3D parking and steering practice.")
            }
            GleoActions.COURSE -> {
                settingsOpen.value = false
                practiceMenuOpen.value = false
                // (11/15 오류 검수) 키가 없는 제출 APK 에는 길이(연습 시간) 칩이 없다. 화면에서 고르는 것은 연습 목적이다
                val already = svc.coach.courseUi.value != null
                svc.coach.startCourse()
                GleoActions.respond(this, fn, "success",
                    if (already) "The practice driving course screen is already open." else "Opened the practice driving course. The driver can choose a practice purpose on the screen.")
            }
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putBoolean(KEY_SETTINGS, settingsOpen.value)
        outState.putBoolean(KEY_PRACTICE_MENU, practiceMenuOpen.value)
        outState.putBoolean(KEY_PARKING_MENU, parkingMenuOpen.value)
        outState.putString(KEY_PRACTICE_QUERY, practiceQuery.value)
    }

    /** 휠 버튼(KEYCODE_MEDIA_PLAY_PAUSE 등)을 먼저 받는다. 짧게 = 확인/다음, 길게 = 세울래요. */
    override fun dispatchKeyEvent(event: KeyEvent): Boolean =
        if (wheel.onKeyEvent(event)) true else super.dispatchKeyEvent(event)

    /** 바인딩만 끊는다. 서비스는 **멈추지 않는다** — 화면이 닫혀도(런처가 덮어도) 코칭은 계속. */
    override fun onDestroy() {
        wheel.reset()
        unbind()
        super.onDestroy()
    }

    private fun unbind() {
        if (bound) {
            runCatching { unbindService(connection) }
            bound = false
        }
        service.value = null
    }

    /** 설정 "앱 종료": 바인딩을 끊고(끊지 않으면 stopService 가 미뤄진다) 서비스를 멈춘 뒤 화면을 닫는다. */
    internal fun stopCoaching() {
        unbind()
        stopService(CoachService.startIntent(this))
        finish()
    }

    // ═══════════════════════════ 화면 ═══════════════════════════
    // 색·글자 크기는 ui/CoachTheme.kt 의 CoachColors / CoachType 만 쓴다(여기에 색 상수를 두지 않는다).

    companion object {
        /** API 33+ 알림 권한. 문자열 상수로 두어 minSdk 28 에서도 참조에 문제가 없게 한다. */
        private const val POST_NOTIFICATIONS = "android.permission.POST_NOTIFICATIONS"
        private const val KEY_SETTINGS = "settings_open"
        private const val KEY_PRACTICE_MENU = "practice_menu_open"
        private const val KEY_PARKING_MENU = "parking_menu_open"
        private const val KEY_PRACTICE_QUERY = "practice_query"
    }
}
