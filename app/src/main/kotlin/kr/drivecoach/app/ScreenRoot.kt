// 앱 뼈대 화면: 인트로, 연결 중, P/주행 화면 분기, P 상태 메뉴
// (11/5 정리) MainActivity 의 Compose 화면을 기능별 파일로 나눴다. 각 화면은 MainActivity 의 확장 함수다(상태·getString 을 그대로 쓴다).
@file:OptIn(ExperimentalLayoutApi::class)

package kr.drivecoach.app

import android.content.Intent
import android.net.Uri
import android.widget.VideoView
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.delay
import kr.drivecoach.app.game.GameLauncher
import kr.drivecoach.app.ui.CoachChipMinHeight
import kr.drivecoach.app.ui.AdaptiveScreen
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.LocalNarrow
import kr.drivecoach.app.ui.CoachCornerRadius
import kr.drivecoach.app.ui.CoachSize
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.app.ui.PrimaryButton
import kr.drivecoach.app.ui.SecondaryButton
import kr.drivecoach.core.parking.ParkingKind
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.mode.DriveMode
import kr.drivecoach.core.parking.SlowModePhase
import kr.drivecoach.core.signal.CarSpec
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.app.speech.VoiceCommandListener


/** 인트로 영상이 없을 때 그림/앱 이름을 보여 주는 시간(ms). */
internal const val INTRO_FALLBACK_MS = 2_500L
/** 인트로 안전장치: 영상 완료 콜백이 안 와도 이 시간이 지나면 넘어간다(ms). */
internal const val INTRO_MAX_MS = 6_000L

/**
 * 앱을 켠 직후의 첫 화면. 지금 빌드에는 인트로 영상·그림이 없어 앱 이름을 [INTRO_FALLBACK_MS] 동안 보여 준다. 화면을 누르면 바로 건너뛴다.
 * raw `intro`(영상)가 있으면 화면 가득 한 번 무음 재생하고, 없거나 재생에 실패하면 drawable `intro` 그림, 그것도 없으면 앱 이름을 보여 준다.
 * 이 시간 동안 뒤에서 서비스 바인딩·차종 인식·주차 판정(2초)이 끝나므로 대기 화면을 겸한다. 안전장치로 [INTRO_MAX_MS] 가 지나면 무조건 넘어간다.
 */
@Composable
internal fun MainActivity.IntroScreen() {
    LaunchedEffect(Unit) { delay(INTRO_MAX_MS); showIntro.value = false }
    Box(modifier = Modifier.fillMaxSize().background(CoachColors.Background).clickable { showIntro.value = false }) {
        IntroVisual(onFinished = { showIntro.value = false })
    }
}

/**
 * 인트로 영상/그림 한 장. raw `intro`(mp4) 가 있으면 VideoView 로 무음 1회 재생, 없거나 오류면 drawable `intro`, 그것도 없으면 앱 이름.
 * 영상 비율(16:9)과 화면 비율이 다르면 흰 여백이 생기는데 영상 바탕도 흰색이라 티가 나지 않는다.
 */
@Composable
internal fun MainActivity.IntroVisual(onFinished: () -> Unit) {
    val videoRes = remember { resources.getIdentifier("intro", "raw", packageName) }
    val imageRes = remember { resources.getIdentifier("intro", "drawable", packageName) }
    val videoFailed = remember { mutableStateOf(false) }
    when {
        videoRes != 0 && !videoFailed.value -> AndroidView(
            modifier = Modifier.fillMaxSize(),
            factory = { ctx ->
                VideoView(ctx).apply {
                    setVideoURI(Uri.parse("android.resource://$packageName/$videoRes"))
                    setOnPreparedListener { mp -> mp.setVolume(0f, 0f); start() }
                    setOnCompletionListener { onFinished() }
                    setOnErrorListener { _, _, _ -> videoFailed.value = true; true }
                }
            },
        )
        imageRes != 0 -> {
            LaunchedEffect(Unit) { delay(INTRO_FALLBACK_MS); onFinished() }
            Image(painter = painterResource(imageRes), contentDescription = null, modifier = Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
        }
        else -> {
            LaunchedEffect(Unit) { delay(INTRO_FALLBACK_MS); onFinished() }
            // (11/15 대표: 앱 이름 여섯 글자만 있어 허전하다) 라임 띠 + 앱 이름 + 한 줄 소개, 아래쪽에 라임 띠
            Box(modifier = Modifier.fillMaxSize()) {
                Column(modifier = Modifier.align(Alignment.Center), horizontalAlignment = Alignment.CenterHorizontally) {
                    Box(modifier = Modifier.width(120.dp).height(12.dp).background(CoachColors.Primary, androidx.compose.foundation.shape.RoundedCornerShape(6.dp)))
                    Spacer(Modifier.height(28.dp))
                    Text(text = getString(R.string.app_name), style = CoachType.Title, fontSize = CoachSize.Hero, fontWeight = androidx.compose.ui.text.font.FontWeight.Bold)
                    Spacer(Modifier.height(16.dp))
                    Text(text = getString(R.string.intro_tagline), fontSize = CoachSize.Option, color = CoachColors.Secondary)
                }
                Box(modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(16.dp).background(CoachColors.Primary))
            }
        }
    }
}

/** 서비스 바인딩 전 한 줄. */
@Composable
internal fun MainActivity.ConnectingScreen() {
    Column(modifier = Modifier.fillMaxSize().padding(48.dp)) {
        Text(text = getString(R.string.label_connecting_service), style = CoachType.Body)
    }
}

/** 바인딩된 서비스의 StateFlow 로 화면 모드를 정한다: core 판정(P 에서만 화면 학습) OR 플랫폼 규제(requiresDO) — 더 엄격한 쪽. */
@Composable
internal fun MainActivity.CoachRoot(svc: CoachService) {
    val state by svc.signals.stateFlow.collectAsState()
    val spec by svc.carSpec.collectAsState()
    val isRestricted by svc.restricted.collectAsState()
    val mode by svc.coach.mode.collectAsState()
    val lastSpoken by svc.coach.lastSpoken.collectAsState()
    // 인트로가 끝나 이 화면이 처음 그려질 때 환영 멘트("현재 차량은 …")를 하게 한다(11/4 대표 요청). 여러 번 불려도 한 번만 말한다.
    LaunchedEffect(svc) { svc.coach.onIntroFinished() }
    // 앱을 켠 직후에는 판정기가 UNKNOWN 에서 PARKED 로 넘어가는 데 2초(히스테리시스)가 걸려 "P · 0 km/h" 한 줄 화면이 먼저 보였다.
    // 기어가 P 면 차는 서 있으므로 UNKNOWN 이어도 바로 메뉴를 보여 준다(11/3 대표 피드백). 움직임·규제는 그대로 주행 화면.
    // P 에 두고 서 있으면 판정기의 2초 유지와 무관하게 바로 메뉴(베타 A-10: P 복귀 뒤 2초간 주행 화면이 보이던 문제).
    val parkedNow = mode == DriveMode.PARKED_LEARN || (state.gear == Gear.P && (state.speedKmh ?: 0.0) <= STOPPED_SPEED_KMH)
    val driving = isRestricted || !parkedNow
    if (driving) DrivingScreen(svc, state, lastSpoken) else ParkedScreen(svc, state, spec, lastSpoken)
}

/**
 * 주행 중(P 가 아니거나 플랫폼 규제): 정지 그림(drawable/driving_still, 11/5 영상 반복에서 교체)을 보여 주고 아래에 작은 검정 자막 "기어 P단 상태에서만 … 실행이 가능합니다" 를 띄운다
 * (11/3 대표 요청, 메뉴·신호·안내문 없음. 코칭은 서비스가 음성으로 계속한다).
 * 예외 하나: 직각 주차 슬로우 모드에서 차가 서 있고 "확인"으로만 넘어가는 단계면 단계 문장 + 확인/다시 버튼을 자막 위에 둔다(STT·휠 버튼 연결 전 대체).
 */
@Composable
internal fun MainActivity.DrivingScreen(svc: CoachService, state: VehicleState, lastSpoken: String) {
    val parking by svc.coach.parkingUi.collectAsState()
    Box(modifier = Modifier.fillMaxSize().background(CoachColors.Background)) {
        // 주행 중에는 영상을 틀지 않는다(차량 OS 의 주행 중 영상 금지 원칙, 베타 D-04 / 11/5 대표 결정). 미니멀 정지 그림 한 장.
        Image(painter = painterResource(R.drawable.driving_still), contentDescription = null, modifier = Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
        Column(
            modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth().padding(horizontal = 48.dp, vertical = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            val p = parking
            val stopped = (state.speedKmh ?: 0.0) <= STOPPED_SPEED_KMH
            if (p != null && (p.phase == SlowModePhase.STEP || p.phase == SlowModePhase.RECOVERY_STEP) && p.needsConfirm && stopped) {
                Text(text = p.text, fontSize = CoachSize.Button, color = CoachColors.OnBackground, maxLines = 2)
                Spacer(Modifier.height(16.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                    PrimaryButton(onClick = { svc.coach.onCommand(Command.CONFIRM) }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_confirm), fontSize = CoachSize.Button)
                    }
                    SecondaryButton(onClick = { svc.coach.onCommand(Command.AGAIN) }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_again), fontSize = CoachSize.Button)
                    }
                }
                Spacer(Modifier.height(24.dp))
            }
            Text(text = getString(R.string.driving_caption), fontSize = CoachSize.Button, color = CoachColors.OnBackground, maxLines = 1)
        }
    }
}

/** P 상태: 차종 이름 + 설정 아이콘, 메뉴(화면 채움) / 연습 선택 화면 / 설정 화면 / 열린 활동 카드. 진단은 설정에서 켠다. */
@Composable
internal fun MainActivity.ParkedScreen(svc: CoachService, state: VehicleState, spec: CarSpec?, lastSpoken: String) {
    val coach = svc.coach
    val practice by coach.practiceUi.collectAsState()
    val subMode by coach.subMode.collectAsState()
    val parking by coach.parkingUi.collectAsState()
    val course by coach.courseUi.collectAsState()
    val law by coach.lawUi.collectAsState()
    val parkLearn by coach.parkLearnUi.collectAsState()
    // 미니멀 레이아웃(11/3~4 대표 피드백): 차량명 + 설정 아이콘 한 줄, 그 아래 메뉴 4개(11/5)가 화면을 꽉 채운다.
    // 신호 한 줄·조용히 칩·메뉴 설명·최근 안내는 뺐다. 조용히·진단·앱 종료는 설정 화면에.
    // 화면 폭을 재서 좁은(세로형) 화면이면 메뉴·그림·버튼 배치를 바꾼다(ui/Adaptive.kt, 11/6)
    AdaptiveScreen(Modifier.fillMaxSize()) {
        Column(modifier = Modifier.fillMaxSize().padding(32.dp)) {
            val settings by settingsOpen
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = spec?.let { if (it.detected) getString(R.string.header_car_detected, it.name) else getString(R.string.header_car_default, it.name) }
                        ?: getString(R.string.header_car_unknown),
                    style = CoachType.Title, maxLines = 1,
                )
                Spacer(Modifier.weight(1f))
                // 마이크(11/7): 누르면 "항상 듣기"를 켜고 끈다 — 켜져 있으면 코치가 말하지 않는 동안 "다음·다시·확인·조용히·세울래요"를 언제든 받는다.
                // Pleos STT 를 못 쓰면 흐리게 보이고 눌러도 반응 없음.
                val voiceState by svc.voice.state.collectAsState()
                val alwaysOn by svc.voice.alwaysOn.collectAsState()
                val listening = voiceState == VoiceCommandListener.State.LISTENING
                val usable = voiceState == VoiceCommandListener.State.READY || listening
                // (11/14 대표) "듣는 중… 다음, 다시, 확인" 글은 뺐다. 듣는 중이면 마이크 뒤를 라임으로 채운다.
                //   (11/15 대표: "마이크 옆에 왜 알아들은 말이 있나, 사용 중이면 칠해 주기만") 알아들은 말도 화면에 쓰지 않는다
                IconButton(
                    onClick = { svc.toggleVoice() },
                    enabled = usable,
                    // (11/14 베타: 누르지 않아도 연두·검정을 오간다 — 듣는 중/잠깐 쉼) 항상 듣기를 켜 두면 늘 연두 원
                    modifier = Modifier.size(64.dp).background(if (alwaysOn && usable || listening) CoachColors.Primary else Color.Transparent, CircleShape),
                ) {
                    Icon(
                        painter = painterResource(if (alwaysOn || !usable) R.drawable.ic_mic else R.drawable.ic_mic_off),
                        contentDescription = getString(if (alwaysOn) R.string.voice_button_off else R.string.voice_button),
                        tint = when {
                            !usable -> CoachColors.Secondary.copy(alpha = 0.4f)
                            listening || alwaysOn -> CoachColors.OnPrimary
                            else -> CoachColors.Secondary
                        },
                        modifier = Modifier.size(44.dp),
                    )
                }
                // 톱니바퀴(11/4): 설정 화면 — 코칭 음성, 음성 속도, 진단, 버전, 앱 종료. 다시 누르면 닫힘.
                IconButton(onClick = { settingsOpen.value = !settingsOpen.value }, modifier = Modifier.size(64.dp)) {
                    Icon(
                        imageVector = Icons.Filled.Settings, contentDescription = getString(R.string.settings_title),
                        tint = if (settings) CoachColors.PrimaryInk else CoachColors.OnBackground, modifier = Modifier.size(44.dp),
                    )
                }
            }
            Spacer(Modifier.height(28.dp))

            // 열린 활동 하나만 카드로. StateFlow 가 잠깐 어긋나(subMode 는 바뀌었는데 ui 는 아직 null) 있으면 메뉴를 그린다.
            val p = practice
            val pk = parking
            val cu = course
            val lw = law
            val menu = when {
                subMode == SubMode.PRACTICE && p != null -> false
                subMode == SubMode.PARKING && (pk != null || parkLearn != null) -> false
                subMode == SubMode.COURSE && cu != null -> false
                subMode == SubMode.LAW && lw != null -> false
                else -> true
            }
            val chooser by practiceMenuOpen
            val parkingChooser by parkingMenuOpen
            if (settings) {
                SettingsScreen(svc, state, lastSpoken, modifier = Modifier.weight(1f))
            } else if (menu && parkingChooser && !chooser) {
                ParkingChooser(coach, modifier = Modifier.weight(1f))
            } else if (menu && !chooser) {
                // 메뉴: 스크롤 없이 남은 높이를 전부 채운다.
                MenuGrid(coach, modifier = Modifier.weight(1f))
            } else if (menu) {
                PracticeChooser(coach, modifier = Modifier.weight(1f))
            } else {
                Column(modifier = Modifier.weight(1f).verticalScroll(rememberScrollState())) {
                    when {
                        subMode == SubMode.PRACTICE && p != null -> PracticeCard(coach, p)
                        subMode == SubMode.PARKING && parkLearn != null -> ParkLearnScreen(coach, parkLearn!!)
                        subMode == SubMode.PARKING && pk != null -> ParkingScreen(coach, pk)
                        subMode == SubMode.COURSE && cu != null -> CourseScreen(coach, cu)
                        subMode == SubMode.LAW && lw != null -> LawScreen(coach, lw)
                        else -> MenuGrid(coach, modifier = Modifier.height(360.dp))
                    }
                }
            }
        }
    }
}

/** 헤더 오른쪽의 작은 칩. selected 면 라임 채움, 아니면 흰 바탕 테두리. */
@Composable
internal fun MainActivity.HeaderChip(text: String, selected: Boolean, done: Boolean = false, onClick: () -> Unit) {
    if (selected) {
        PrimaryButton(onClick = onClick, minHeight = CoachChipMinHeight) { Text(text = text, fontSize = CoachSize.Chip) }
    } else {
        SecondaryButton(onClick = onClick, minHeight = CoachChipMinHeight, container = if (done) CoachColors.Primary.copy(alpha = 0.45f) else CoachColors.Background) { Text(text = text, fontSize = CoachSize.Chip) }
    }
}

/**
 * 메뉴 5개: "차량 조작법"(→ 선택 화면), "도로 법규", "주차 연습"(→ 주차 게임·직각 주차·평행 주차 선택),
 * "도로 주행 연습"(3D), "코스 주행". 주어진 높이를 꽉 채운다.
 */
@Composable
internal fun MainActivity.MenuGrid(coach: CoachController, modifier: Modifier = Modifier) {
    val practice: () -> Unit = { practiceQuery.value = ""; parkingMenuOpen.value = false; practiceMenuOpen.value = true }
    val law: () -> Unit = { practiceMenuOpen.value = false; parkingMenuOpen.value = false; coach.startLaw() }
    // (11/15 대표: 직각·평행 주차는 주차·핸들 연습으로) 3D 연습·직각 주차·평행 주차를 고르는 화면을 먼저 연다
    val game: () -> Unit = { practiceMenuOpen.value = false; parkingMenuOpen.value = true }
    val course: () -> Unit = { practiceMenuOpen.value = false; parkingMenuOpen.value = false; coach.startCourse() }
    // 11/10 대표: 3D 도로 주행(내 동네)은 주차·핸들 연습에서 빼 첫 화면에 따로 둔다
    val road3d: () -> Unit = { practiceMenuOpen.value = false; parkingMenuOpen.value = false; GameLauncher.open(this@MenuGrid, menu = "road") }
    if (LocalNarrow.current) {
        // 세로형·좁은 화면(11/6 Pleos 가이드): 2 × 2
        Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(24.dp)) {
            Row(modifier = Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
                MenuCard(getString(R.string.menu_practice), practice, Modifier.weight(1f))
                MenuCard(getString(R.string.menu_law), law, Modifier.weight(1f))
            }
            Row(modifier = Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
                MenuCard(getString(R.string.menu_game), game, Modifier.weight(1f))
                MenuCard(getString(R.string.menu_road3d), road3d, Modifier.weight(1f))
            }
            Row(modifier = Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
                MenuCard(getString(R.string.menu_course), course, Modifier.weight(1f))
            }
        }
    } else {
        Row(modifier = modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
            MenuCard(getString(R.string.menu_practice), practice, Modifier.weight(1f))
            MenuCard(getString(R.string.menu_law), law, Modifier.weight(1f))
            MenuCard(getString(R.string.menu_game), game, Modifier.weight(1f))
            MenuCard(getString(R.string.menu_road3d), road3d, Modifier.weight(1f))
            MenuCard(getString(R.string.menu_course), course, Modifier.weight(1f))
        }
    }
}

/**
 * (11/15 대표: "직각 주차/평행 주차는 주차·핸들 연습으로") "주차·핸들 연습" 선택 화면: 위에 "← 메뉴로"·제목, 아래 카드 셋.
 *  3D 연습(게임: 핸들 감각·서킷·주차 단계) / 직각 주차 / 평행 주차(차를 세운 빈 곳에서 한 단계씩 음성 안내).
 */
@Composable
internal fun MainActivity.ParkingChooser(coach: CoachController, modifier: Modifier = Modifier) {
    Column(modifier = modifier.fillMaxWidth()) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            HeaderChip(text = getString(R.string.btn_back_menu), selected = false, onClick = { parkingMenuOpen.value = false })
            Spacer(Modifier.width(20.dp))
            Text(text = getString(R.string.parking_menu_title), style = CoachType.Title, maxLines = 1)
        }
        Spacer(Modifier.height(28.dp))
        Row(modifier = Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(24.dp)) {
            MenuCard(getString(R.string.parking_menu_game), { GameLauncher.open(this@ParkingChooser) }, Modifier.weight(1f))
            MenuCard(getString(R.string.menu_parking_learn), { coach.startParkLearn(ParkingKind.PERPENDICULAR) }, Modifier.weight(1f))
            MenuCard(getString(R.string.menu_parking_parallel_learn), { coach.startParkLearn(ParkingKind.PARALLEL) }, Modifier.weight(1f))
        }
    }
}

/** 메뉴 카드 하나: 흰 바탕·옅은 테두리·둥근 모서리, 왼쪽 라임 세로 띠, 제목 44sp 검정 가운데 정렬. 높이는 부모가 준다. */
@Composable
internal fun MainActivity.MenuCard(title: String, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(CoachCornerRadius)
    Row(
        modifier = modifier
            .fillMaxHeight()
            .clip(shape)
            .background(CoachColors.Background, shape)
            .border(2.dp, CoachColors.Outline, shape)
            .clickable(onClick = onClick),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(modifier = Modifier.fillMaxHeight().width(12.dp).background(CoachColors.Primary))
        Box(modifier = Modifier.weight(1f).padding(horizontal = 20.dp), contentAlignment = Alignment.Center) {
            // 좁은 칸에서 낱말 중간("연/습")이 끊기지 않게, 긴 이름은 마지막 띄어쓰기에서 줄을 나눈다
            val shown = if (title.length > 6 && title.contains(' ')) title.substringBeforeLast(' ') + "\n" + title.substringAfterLast(' ') else title
            // (11/15) "주차 및 스티어링 휠 연습"은 세 줄 — 두 줄이면 "연습"이 잘렸다
            Text(text = shown, style = CoachType.Title, maxLines = 3, textAlign = TextAlign.Center)
        }
    }
}
