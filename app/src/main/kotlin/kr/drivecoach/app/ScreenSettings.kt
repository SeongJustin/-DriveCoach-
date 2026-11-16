// 설정 화면 + 진단
// MainActivity 의 Compose 화면을 기능별 파일로 나눴다. 각 화면은 MainActivity 의 확장 함수다(상태·getString 을 그대로 쓴다).
@file:OptIn(ExperimentalLayoutApi::class)

package kr.drivecoach.app

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import kr.drivecoach.app.ui.CoachChipMinHeight
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.CoachSize
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.app.ui.PrimaryButton
import kr.drivecoach.app.ui.SecondaryButton
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kr.drivecoach.app.speech.VoiceCommandListener
import kr.drivecoach.app.vehicle.PleosSignalSource
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kotlin.math.abs

/**
 * 설정 화면: 코칭 음성(= 조용히) · 음성 속도 · 진단 보기 · 앱 버전 · 앱 종료(확인 대화상자).
 * 연습 기록 초기화는 넣지 않기로 함. 진단을 켜면 이 화면 아래에 진단 내용이 붙는다(스크롤).
 */
@Composable
internal fun MainActivity.SettingsScreen(svc: CoachService, state: VehicleState, lastSpoken: String, modifier: Modifier = Modifier) {
    val coach = svc.coach
    val quiet by coach.quiet.collectAsState()
    val rate by coach.speechRate.collectAsState()
    val diagOpen by showDiag
    var askExit by confirmExit
    Column(modifier = modifier.fillMaxWidth().verticalScroll(rememberScrollState())) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            HeaderChip(text = getString(R.string.btn_close_settings), selected = false, fontSize = CoachSize.Label, onClick = { settingsOpen.value = false })   // 뒤로 버튼 글씨 22 → 26sp
            Spacer(Modifier.width(20.dp))
            Text(text = getString(R.string.settings_title), style = CoachType.Title, maxLines = 1)   // 다른 화면 제목(차량 조작법, 주차 연습)과 같은 크기
        }
        Spacer(Modifier.height(16.dp))
        SettingRow(getString(R.string.setting_voice)) {
            HeaderChip(text = getString(R.string.setting_on), selected = !quiet, onClick = { coach.setQuiet(false) })
            Spacer(Modifier.width(12.dp))
            HeaderChip(text = getString(R.string.setting_off), selected = quiet, onClick = { coach.setQuiet(true) })
        }
        SettingRow(getString(R.string.setting_rate)) {
            // (마지막 칩 뒤에도 간격이 붙어 '빠르게'가 다른 줄보다 안쪽에 섰다) 칩 사이에만 간격
            listOf(0.85f to R.string.setting_rate_slow, 1f to R.string.setting_rate_normal, 1.2f to R.string.setting_rate_fast).forEachIndexed { i, (r, label) ->
                if (i > 0) Spacer(Modifier.width(12.dp))
                HeaderChip(text = getString(label), selected = kotlin.math.abs(rate - r) < 0.01f, onClick = { coach.setSpeechRate(r) })
            }
        }
        // 버튼 하나("꺼짐")라 지금 상태인지 누르는 버튼인지 헷갈리던 것 → 코칭 음성처럼 켜짐/꺼짐 두 개
        SettingRow(getString(R.string.setting_diag)) {
            HeaderChip(text = getString(R.string.setting_on), selected = diagOpen, onClick = {
                if (!diagOpen) {
                    showDiag.value = true
                    lifecycleScope.launch(Dispatchers.IO) {
                        val lines = inspectPleosPackages().take(1) + inspectPermissions().filter { !it.startsWith("OK") }
                        withContext(Dispatchers.Main) { permDiag.value = lines }
                    }
                }
            })
            Spacer(Modifier.width(12.dp))
            HeaderChip(text = getString(R.string.setting_off), selected = !diagOpen, onClick = { showDiag.value = false })
        }
        // Pleos 실험은 진단 보기 바로 아래(차 화면에서 스크롤 없이 보이게)
        if (diagOpen) PleosLabSection(svc)
        // (설정에 개발용 항목이 대부분) Pleos 연결 상태 줄은 진단 보기를 켰을 때만
        if (diagOpen) PleosStatus(svc)
        SettingRow(getString(R.string.setting_version)) {
            Text(text = BuildConfig.VERSION_NAME, style = CoachType.Body, color = CoachColors.Secondary)
        }
        SettingRow(getString(R.string.setting_exit)) {
            SecondaryButton(onClick = { askExit = true }, minHeight = CoachChipMinHeight) {
                Text(text = getString(R.string.btn_exit_app), fontSize = CoachSize.Chip, color = CoachColors.Danger)
            }
        }
        if (diagOpen) {
            Spacer(Modifier.height(24.dp))
            Text(text = getString(R.string.label_last_spoken), style = CoachType.Caption)
            Text(text = lastSpoken, fontSize = CoachSize.Label, color = CoachColors.OnBackground, maxLines = 2)
            Spacer(Modifier.height(24.dp))
            DiagSection(svc, state)
        }
    }
    if (askExit) {
        // 차 안에서 실수로 끄지 않게 한 번 묻는다. 예 → 서비스까지 완전히 종료(앱을 닫기만 하면 코칭은 계속되므로).
        AlertDialog(
            onDismissRequest = { askExit = false },
            containerColor = CoachColors.Background,
            title = { Text(text = getString(R.string.exit_confirm_title), style = CoachType.Body, fontWeight = FontWeight.Bold) },
            text = {
                KeepDialogOffIme()
                Text(text = getString(R.string.exit_confirm_text), style = CoachType.Caption, fontSize = CoachSize.Label)
            },
            confirmButton = {
                PrimaryButton(onClick = { askExit = false; stopCoaching() }, minHeight = CoachChipMinHeight) {
                    Text(text = getString(R.string.btn_yes), fontSize = CoachSize.Label)
                }
            },
            dismissButton = {
                SecondaryButton(onClick = { askExit = false }, minHeight = CoachChipMinHeight) {
                    Text(text = getString(R.string.btn_no), fontSize = CoachSize.Label)
                }
            },
        )
    }
}

/**
 * "Pleos 연결": 이 앱이 쓰는 Pleos Playground SDK 와 지금 연결 상태를 한눈에.
 * 차량 신호는 Vehicle SDK 기능 13개 중 구독에 성공한 수, 내비는 NaviHelper 초기화 여부, 위치는 FusedLocation 구독 여부, 음성은 지금 쓰는 엔진 이름.
 */
@Composable
internal fun MainActivity.PleosStatus(svc: CoachService) {
    val connected by svc.signals.connected.collectAsState()
    val naviReady by svc.navi.naviReady.collectAsState()
    val speakerName by svc.coach.speakerName.collectAsState()
    val features = PleosSignalSource.VEHICLE_FEATURES
    val vehicleOk = features.count { it in connected }
    val okOrWait = { ok: Boolean -> getString(if (ok) R.string.pleos_connected else R.string.pleos_waiting) }
    val colorOf = { ok: Boolean -> if (ok) CoachColors.Ok else CoachColors.Secondary }
    Spacer(Modifier.height(20.dp))
    Text(text = getString(R.string.setting_pleos), style = CoachType.Body, fontWeight = FontWeight.Bold)
    SettingRow(getString(R.string.pleos_vehicle)) {
        Text(text = getString(R.string.pleos_vehicle_value, vehicleOk, features.size), style = CoachType.Body, color = colorOf(vehicleOk > 0))
    }
    SettingRow(getString(R.string.pleos_navi)) {
        Text(text = okOrWait(naviReady), style = CoachType.Body, color = colorOf(naviReady))
    }
    SettingRow(getString(R.string.pleos_location)) {
        val loc = "FusedLocation" in connected
        Text(text = okOrWait(loc), style = CoachType.Body, color = colorOf(loc))
    }
    SettingRow(getString(R.string.pleos_stt)) {
        val st by svc.voice.state.collectAsState()
        val err by svc.voice.lastError.collectAsState()
        val always by svc.voice.alwaysOn.collectAsState()
        val ok = st == VoiceCommandListener.State.READY || st == VoiceCommandListener.State.LISTENING
        Text(
            text = when (st) {
                VoiceCommandListener.State.STARTING -> getString(R.string.pleos_waiting)
                VoiceCommandListener.State.UNAVAILABLE -> getString(R.string.pleos_unavailable) + if (err.isNotBlank()) " ($err)" else ""
                else -> getString(R.string.pleos_connected) + ", " + getString(if (always) R.string.voice_always_on else R.string.voice_off)
            },
            style = CoachType.Body, color = colorOf(ok), maxLines = 1,
        )
    }
    SettingRow(getString(R.string.pleos_voice)) {
        Text(text = speakerName, style = CoachType.Body, color = CoachColors.Secondary, maxLines = 1)
    }
    Spacer(Modifier.height(20.dp))
}

/**
 * "Pleos 실험"(진단 보기를 켰을 때만): Pleos 쪽 문제를 앱 안에서 바로 시험한다.
 * DO 점검 = 설치 출처·CarPackageManager 판정·허용 설치 출처 목록, TTS = Pleos 음성으로 시험 문장, STT = 8초 동안 한 번 듣기.
 */
@Composable
internal fun MainActivity.PleosLabSection(svc: CoachService) {
    val lines by svc.lab.lines.collectAsState()
    // 처음 열면 주행 중 화면(DO) 점검을 한 번 자동으로 돌린다(결과는 logcat 태그 PleosLab 에도 남음)
    LaunchedEffect(Unit) { if (lines.isEmpty()) svc.lab.checkDistractionOptimized() }
    Spacer(Modifier.height(12.dp))
    Text(text = getString(R.string.lab_title), style = CoachType.Body, fontWeight = FontWeight.Bold)
    Spacer(Modifier.height(8.dp))
    Row(modifier = Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
        HeaderChip(text = getString(R.string.lab_do), selected = false, onClick = { svc.lab.checkDistractionOptimized() })
        Spacer(Modifier.width(8.dp))
        HeaderChip(text = "TTS", selected = false, onClick = { svc.lab.tryTts() })
        Spacer(Modifier.width(8.dp))
        HeaderChip(text = "STT", selected = false, onClick = { svc.lab.trySst() })
        Spacer(Modifier.width(8.dp))
        HeaderChip(text = getString(R.string.lab_clear), selected = false, onClick = { svc.lab.clear() })
    }
    // 말 넣기(debug 빌드만): 시험 녹음을 마이크 대신 음성 명령 엔진에 넣는다 — 사람이 옆에서 말하지 않아도 음성 명령을 끝까지 시험
    val recordings = remember { svc.lab.commandRecordings() }
    if (recordings.isNotEmpty()) {
        Spacer(Modifier.height(8.dp))
        Row(modifier = Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
            Text(text = getString(R.string.lab_inject), style = CoachType.Caption)
            recordings.forEach { f ->
                Spacer(Modifier.width(8.dp))
                HeaderChip(text = svc.lab.label(f), selected = false, onClick = { svc.lab.injectCommand(svc.voice, f) })
            }
        }
    }
    Spacer(Modifier.height(8.dp))
    lines.forEach { Text(text = it, fontSize = CoachSize.Mono, color = CoachColors.Secondary) }
}

/** 설정 한 줄: 왼쪽 이름, 오른쪽 조작. 아래 옅은 구분선. */
@Composable
internal fun MainActivity.SettingRow(label: String, content: @Composable () -> Unit) {
    Row(modifier = Modifier.fillMaxWidth().padding(vertical = 18.dp), verticalAlignment = Alignment.CenterVertically) {   // 줄 간격 14 → 18dp
        Text(text = label, style = CoachType.Body, maxLines = 1)
        Spacer(Modifier.weight(1f))
        content()
    }
    Box(modifier = Modifier.fillMaxWidth().height(1.dp).background(CoachColors.Outline))
}

/** 진단(토글): TTS 엔진·위험 횟수·발화 판정 + 전체 신호 + SDK/권한 진단(기존 내용). 회색 고정폭 글씨, OK 초록 / FAIL 빨강. */
@Composable
internal fun MainActivity.DiagSection(svc: CoachService, state: VehicleState) {
    val coach = svc.coach
    val diag by svc.signals.diag.collectAsState()
    val perms by permDiag
    val speakerName by coach.speakerName.collectAsState()
    val speakerHistory by svc.speaker.history.collectAsState()
    val dangerCount by coach.dangerCount.collectAsState()
    val speechLog by coach.speechLog.collectAsState()
    val big = 20.sp
    val small = 16.sp
    val mono = FontFamily.Monospace
    val grey = CoachColors.Secondary
    val black = CoachColors.OnBackground
    Text("${getString(R.string.diag_tts)}: $speakerName, ${getString(R.string.diag_danger)} ${dangerCount}회", fontSize = big, fontFamily = mono, color = CoachColors.Ok)
    for (line in speakerHistory) Text(line, fontSize = small, fontFamily = mono, color = grey)
    Text(getString(R.string.diag_speech_log), fontSize = small, fontFamily = mono, color = grey)
    for (line in speechLog.takeLast(6)) Text(line, fontSize = small, fontFamily = mono, color = if (line.startsWith("SPEAK")) CoachColors.Ok else grey)
    Spacer(Modifier.height(12.dp))
    Row {
        Text("기어 ${state.gear}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("속도 ${state.speedKmh?.let { "%.1f".format(it) } ?: "-"} km/h", fontSize = big, fontFamily = mono, color = black)
        // RPM 은 뺐다(ENGINE_RPM 은 signature|privileged 권한이라 사용 불가)
    }
    Row {
        Text("조향 ${state.steeringAngleDeg?.let { "%.0f°".format(it) } ?: "-"}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("브레이크 ${state.brakePressed ?: "-"}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("P브레이크 ${state.parkingBrakeOn ?: "-"}", fontSize = big, fontFamily = mono, color = black)
    }
    Row {
        Text("방향지시등 ${state.turnSignal}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("전조등 ${state.headlight}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("와이퍼 ${state.wiper}", fontSize = big, fontFamily = mono, color = black)
    }
    Row {
        Text("벨트경고 ${state.seatBeltWarning ?: "-"}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("시동 ${state.ignitionOn ?: "-"}", fontSize = big, fontFamily = mono, color = black); Spacer(Modifier.width(32.dp))
        Text("연료 ${state.fuelPercent?.toInt() ?: "-"}%", fontSize = big, fontFamily = mono, color = black)
    }
    Text("창문닫힘 ${state.windowsClosed ?: "-"}  도어잠금 ${state.doorsLocked ?: "-"}  공조 ${state.hvacOn ?: "-"}  경고등 ${state.warningLamps.joinToString().ifEmpty { "없음" }}", fontSize = small, fontFamily = mono, color = grey)
    Spacer(Modifier.height(20.dp))
    val naviDiag by svc.navi.diag.collectAsState()
    Text(getString(R.string.course_diag_title), fontSize = small, fontFamily = mono, color = grey)
    for (line in naviDiag.takeLast(8)) {
        Text(line, fontSize = small, fontFamily = mono, color = if (line.startsWith("FAIL")) CoachColors.Danger else if (line.startsWith("OK")) CoachColors.Ok else grey)
    }
    Spacer(Modifier.height(12.dp))
    Text(getString(R.string.diag_sdk), fontSize = small, fontFamily = mono, color = grey)
    for (line in diag) {
        val c = when {
            line.startsWith("OK") -> CoachColors.Ok
            line.startsWith("FAIL") -> CoachColors.Danger
            else -> grey
        }
        Text(line, fontSize = small, fontFamily = mono, color = c)
    }
    Spacer(Modifier.height(12.dp))
    Text(getString(R.string.diag_perm), fontSize = small, fontFamily = mono, color = grey)
    for (line in perms) {
        Text(line, fontSize = small, fontFamily = mono, color = if (line.startsWith("OK") || line.startsWith("설치")) CoachColors.Ok else CoachColors.Danger)
    }
}

/**
 * Pleos "IME 동작에 따른 window resize 처리" 가이드: 글 입력칸이 없는 대화상자는 키보드가 떠도 밀려 올라가면 안 된다.
 * 대화상자 창이 맞출 여백을 상태 표시줄·내비게이션 바로만 한정한다(IME 여백 제외, API 30+).
 */
@Composable
internal fun KeepDialogOffIme() {
    val view = androidx.compose.ui.platform.LocalView.current
    androidx.compose.runtime.SideEffect {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.R) {
            val provider = (view as? androidx.compose.ui.window.DialogWindowProvider)
                ?: (view.parent as? androidx.compose.ui.window.DialogWindowProvider)
            val w = provider?.window ?: return@SideEffect
            runCatching {
                val p = w.attributes
                p.fitInsetsTypes = android.view.WindowInsets.Type.statusBars() or android.view.WindowInsets.Type.navigationBars()
                w.attributes = p
            }
        }
    }
}
