// 직각 주차(4-5) 화면
// MainActivity 의 Compose 화면을 기능별 파일로 나눴다. 각 화면은 MainActivity 의 확장 함수다(상태·getString 을 그대로 쓴다).
@file:OptIn(ExperimentalLayoutApi::class)

package kr.drivecoach.app

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kr.drivecoach.app.ui.CoachChipMinHeight
import kr.drivecoach.app.ui.ArtWithText
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.CoachCard
import kr.drivecoach.app.ui.CoachSize
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.app.ui.ParkingArt
import kr.drivecoach.app.ui.PrimaryButton
import kr.drivecoach.app.ui.SecondaryButton
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.parking.ParkingKind
import kr.drivecoach.core.parking.SelfEvalItem
import kr.drivecoach.core.parking.SlowModePhase
import kr.drivecoach.core.parking.Tangle


/**
 * 직각 주차 카드: 캡션(N/9 단계·시도, 탈출 중이면 꼬임 이름) → 진행 바 → 왼쪽 단계 일러스트 +
 * 오른쪽 문장 32sp + 체크리스트 3줄(기어 / 조향 끝까지 / 속도 ≤ 5, ✓ 초록·○ 회색) → 단계별 버튼.
 *  - 단계 진행 중: 확인(주) / 다음 / 다시, 아래 줄에 꼬였어요 / 처음부터 / 닫기
 *  - 탈출 선택: 너무 붙음 / 각도 / 너무 멈
 *  - 자기평가: 연석 간격 / 각도 / 앞뒤 토글 + 평가 끝(주)
 *  - 완료: 닫기(주)
 * 에뮬레이터: 조향 슬라이더(±400° 이상)·기어(P/R/D)·확인 버튼으로 9단계 전부 시연 가능. 속도는 0 이라 후진 속도 경고만 시연 불가.
 */
@Composable
internal fun MainActivity.ParkingScreen(coach: CoachController, p: ParkingUi) {
    val selfEval = remember(p.attempt) { mutableStateOf(setOf<SelfEvalItem>()) }   // 시도마다 초기화
    CoachCard {
        Text(text = parkingCaption(p), style = CoachType.Caption, maxLines = 1)
        Spacer(Modifier.height(12.dp))
        val fraction = if (p.stepTotal > 0) (p.stepIndex + 1).toFloat() / p.stepTotal.toFloat() else 0f
        LinearProgressIndicator(
            progress = { fraction.coerceIn(0f, 1f) },
            modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)),
            color = if (p.recovery != null) CoachColors.Danger else CoachColors.Primary,
            trackColor = CoachColors.Outline,
        )
        Spacer(Modifier.height(20.dp))
        Spacer(Modifier.weight(1f))   // 남는 높이는 그림 위아래로 나눠 버튼 줄을 화면 아래에 붙인다
        // 그림 + 문장: 넓은 화면은 좌우, 좁은(세로) 화면은 위아래(Pleos 가이드)
        ArtWithText(ParkingArt.resIdFor(p.illustration), p.illustration, videoRes = ParkingArt.videoResFor(this@ParkingScreen, p.illustration), keepArtSlot = p.phase != SlowModePhase.IDLE) {
            // (자기평가 화면에 시작 전 안내문이 그대로 남는다)
            val bodyText = if (p.phase == SlowModePhase.SELF_EVAL) getString(if (p.kind == ParkingKind.PARALLEL) R.string.parking_par_selfeval_body else R.string.parking_selfeval_body) else p.text
            val longText = bodyText.length > 60   // 시작 전 설명 등
            Text(
                text = bodyText,
                style = if (longText) CoachType.Body.copy(fontSize = CoachSize.Option) else CoachType.Body,
                lineHeight = if (longText) 40.sp else 44.sp,
                maxLines = if (longText) 9 else 4,
            )
            // 기준점 설명(어느 선·어느 미러인지)과 잠깐 보이는 보조 안내(아직 감지 안 됨 등)
            p.detail?.let {
                Spacer(Modifier.height(8.dp))
                Text(text = it, fontSize = CoachSize.Label, color = CoachColors.Secondary, maxLines = 2)
            }
            p.notice?.let {
                Spacer(Modifier.height(8.dp))
                Text(text = it, fontSize = CoachSize.Label, fontWeight = FontWeight.Bold, color = CoachColors.PrimaryInk, maxLines = 2)
            }
            if (p.phase == SlowModePhase.STEP || p.phase == SlowModePhase.RECOVERY_STEP) {
                Spacer(Modifier.height(16.dp))
                p.checks.forEach { c -> CheckLine(ok = c.ok, label = c.label) }
                if (p.needsConfirm) {
                    Spacer(Modifier.height(4.dp))
                    Text(text = getString(R.string.parking_check_manual), style = CoachType.Caption, maxLines = 1)
                }
            }
        }
        Spacer(Modifier.height(24.dp))
        Spacer(Modifier.weight(1f))
        when (p.phase) {
            SlowModePhase.STEP, SlowModePhase.RECOVERY_STEP -> {
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    PrimaryButton(onClick = { coach.onCommand(Command.CONFIRM) }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_confirm), fontSize = CoachSize.Button)
                    }
                    SecondaryButton(onClick = { coach.onCommand(Command.SKIP) }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_skip), fontSize = CoachSize.Button)
                    }
                    // '다시' = 이번 단계 안내를 다시 듣기('처음부터' 와 헷갈리던 것)
                    SecondaryButton(onClick = { coach.onCommand(Command.AGAIN) }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_listen_again), fontSize = CoachSize.Button)
                    }
                }
                Spacer(Modifier.height(16.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    SecondaryButton(onClick = { coach.parkingHelp() }, modifier = Modifier.weight(1f), minHeight = CoachChipMinHeight) {
                        Text(text = getString(R.string.btn_parking_help), fontSize = CoachSize.Label)
                    }
                    SecondaryButton(onClick = { coach.parkingRestart() }, modifier = Modifier.weight(1f), minHeight = CoachChipMinHeight) {
                        Text(text = getString(R.string.btn_parking_restart), fontSize = CoachSize.Label)
                    }
                    // 닫기도 같은 크기 버튼으로
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f), minHeight = CoachChipMinHeight) {
                        Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Label)
                    }
                }
            }
            SlowModePhase.RECOVERY_CHOICE -> {
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    // 상태 이름만으로는 무엇인지 몰라 고르기 어렵던 것: 한 줄 설명을 같이
                    for (t in p.recoveryOptions) {
                        SecondaryButton(onClick = { coach.parkingChooseRecovery(t) }, modifier = Modifier.weight(1f)) {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Text(text = tangleLabel(t, p.kind), fontSize = CoachSize.Button, fontWeight = FontWeight.Bold)
                                Text(text = tangleDescription(t, p.kind), fontSize = CoachSize.Chip, color = CoachColors.Secondary, maxLines = 2)
                            }
                        }
                    }
                }
                Spacer(Modifier.height(16.dp))
                // 잘못 눌렀을 때 하던 단계로 돌아갈 길이 '목록으로' 밖에 없었다
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    SecondaryButton(onClick = { coach.parkingCancelHelp() }, modifier = Modifier.weight(1f), minHeight = CoachChipMinHeight) {
                        Text(text = getString(R.string.btn_parking_back_to_step), fontSize = CoachSize.Label)
                    }
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f), minHeight = CoachChipMinHeight) {
                        Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Label)
                    }
                }
            }
            SlowModePhase.SELF_EVAL -> {
                Text(text = getString(R.string.parking_selfeval_caption), style = CoachType.Caption)
                Spacer(Modifier.height(8.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    for (item in SelfEvalItem.values()) {
                        val selected = item in selfEval.value
                        val toggle: () -> Unit = { selfEval.value = if (selected) selfEval.value - item else selfEval.value + item }
                        if (selected) {
                            PrimaryButton(onClick = toggle, modifier = Modifier.weight(1f)) { Text(text = selfEvalLabel(item, p.kind), fontSize = CoachSize.Button) }
                        } else {
                            SecondaryButton(onClick = toggle, modifier = Modifier.weight(1f)) { Text(text = selfEvalLabel(item, p.kind), fontSize = CoachSize.Button) }
                        }
                    }
                }
                Spacer(Modifier.height(16.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    PrimaryButton(onClick = { coach.parkingSelfEvaluate(selfEval.value) }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_parking_selfeval_submit), fontSize = CoachSize.Button)
                    }
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(0.6f)) {
                        Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Button)
                    }
                }
            }
            // 시작 전 설명 화면
            SlowModePhase.IDLE -> {
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    PrimaryButton(onClick = { coach.parkingBegin() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_start), fontSize = CoachSize.Button)
                    }
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Button)
                    }
                }
            }
            SlowModePhase.DONE -> {
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    PrimaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Button)
                    }
                }
            }
        }
    }
}

/** 체크리스트 한 줄: ✓(초록) 또는 ○(회색) + 라벨. */
@Composable
internal fun MainActivity.CheckLine(ok: Boolean, label: String) {
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(
            text = if (ok) "✓" else "○",
            fontSize = CoachSize.Option, fontWeight = FontWeight.Bold,
            color = if (ok) CoachColors.Ok else CoachColors.Secondary,
        )
        Spacer(Modifier.width(12.dp))
        Text(text = label, fontSize = CoachSize.Option, color = if (ok) CoachColors.OnBackground else CoachColors.Secondary, maxLines = 1)
    }
}

/** "직각 주차 · 3/9 단계 · 1번째 시도" / "탈출 · 각도 · 1/4" / "탈출 방법 고르기" / "자기평가" / "완료". */
internal fun MainActivity.parkingCaption(p: ParkingUi): String = when (p.phase) {
    SlowModePhase.RECOVERY_CHOICE -> getString(R.string.parking_caption_choose)
    SlowModePhase.SELF_EVAL -> getString(R.string.parking_caption_selfeval)
    SlowModePhase.DONE -> getString(R.string.parking_caption_done)
    SlowModePhase.RECOVERY_STEP -> getString(R.string.parking_caption_recovery, p.recovery?.let { tangleLabel(it, p.kind) } ?: "", p.stepIndex + 1, p.stepTotal)
    SlowModePhase.IDLE -> getString(if (p.kind == ParkingKind.PARALLEL) R.string.parking_par_caption_intro else R.string.parking_caption_intro)
    SlowModePhase.STEP -> getString(if (p.kind == ParkingKind.PARALLEL) R.string.parking_par_caption else R.string.parking_caption, p.stepIndex + 1, p.stepTotal, p.attempt)
}

internal fun MainActivity.tangleLabel(t: Tangle, kind: ParkingKind = ParkingKind.PERPENDICULAR): String = getString(
    if (kind == ParkingKind.PARALLEL) when (t) {
        Tangle.TOO_CLOSE -> R.string.parking_par_tangle_too_close
        Tangle.ANGLED -> R.string.parking_par_tangle_angled
        Tangle.TOO_FAR -> R.string.parking_par_tangle_too_far
    } else when (t) {
        Tangle.TOO_CLOSE -> R.string.parking_tangle_too_close
        Tangle.ANGLED -> R.string.parking_tangle_angled
        Tangle.TOO_FAR -> R.string.parking_tangle_too_far
    },
)

/** 탈출 상태 한 줄 설명(버튼 아래). */
internal fun MainActivity.tangleDescription(t: Tangle, kind: ParkingKind = ParkingKind.PERPENDICULAR): String = getString(
    if (kind == ParkingKind.PARALLEL) when (t) {
        Tangle.TOO_CLOSE -> R.string.parking_par_tangle_too_close_desc
        Tangle.ANGLED -> R.string.parking_par_tangle_angled_desc
        Tangle.TOO_FAR -> R.string.parking_par_tangle_too_far_desc
    } else when (t) {
        Tangle.TOO_CLOSE -> R.string.parking_tangle_too_close_desc
        Tangle.ANGLED -> R.string.parking_tangle_angled_desc
        Tangle.TOO_FAR -> R.string.parking_tangle_too_far_desc
    },
)

internal fun MainActivity.selfEvalLabel(item: SelfEvalItem, kind: ParkingKind = ParkingKind.PERPENDICULAR): String = getString(
    when (item) {
        SelfEvalItem.CURB_GAP -> if (kind == ParkingKind.PARALLEL) R.string.parking_par_selfeval_curb_gap else R.string.parking_selfeval_curb_gap
        SelfEvalItem.ANGLE -> R.string.parking_selfeval_angle
        SelfEvalItem.FRONT_BACK -> R.string.parking_selfeval_front_back
    },
)


/** 주차 배우기 카드: 단계 영상 + 설명, 이전 / 다음 / 목록으로. 따라 하기와 확인은 없다. */
@Composable
internal fun MainActivity.ParkLearnScreen(coach: CoachController, p: ParkLearnUi) {
    CoachCard {
        val base = getString(if (p.kind == ParkingKind.PARALLEL) R.string.menu_parking_parallel else R.string.menu_parking)
        val caption = if (p.tangle != null) getString(R.string.parking_caption_recovery, tangleLabel(p.tangle, p.kind), p.no, p.of) else "$base > ${p.no}/${p.of}"
        Text(text = caption, style = CoachType.Caption, maxLines = 1)
        Spacer(Modifier.height(12.dp))
        LinearProgressIndicator(
            progress = { ((p.index + 1).toFloat() / p.total.toFloat()).coerceIn(0f, 1f) },
            modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)),
            color = CoachColors.Primary, trackColor = CoachColors.Outline,
        )
        Spacer(Modifier.height(20.dp))
        Spacer(Modifier.weight(1f))   // 남는 높이는 그림 위아래로 나눠 버튼 줄을 화면 아래에 붙인다
        ArtWithText(ParkingArt.resIdFor(p.illustration), p.illustration, videoRes = ParkingArt.videoResFor(this@ParkLearnScreen, p.illustration), keepArtSlot = true) {
            Text(text = p.text, style = CoachType.Body, lineHeight = 44.sp, maxLines = 5)
            p.detail?.let {
                Spacer(Modifier.height(8.dp))
                Text(text = it, fontSize = CoachSize.Label, color = CoachColors.Secondary, maxLines = 3)
            }
        }
        Spacer(Modifier.height(24.dp))
        Spacer(Modifier.weight(1f))
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
            SecondaryButton(onClick = { coach.parkLearnGo(-1) }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_prev), fontSize = CoachSize.Button, color = if (p.index > 0) CoachColors.OnBackground else CoachColors.Secondary)
            }
            PrimaryButton(onClick = { coach.parkLearnGo(+1) }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_next), fontSize = CoachSize.Button)
            }
            SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Button)
            }
        }
    }
}
