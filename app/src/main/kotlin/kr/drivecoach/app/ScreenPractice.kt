// 차 기능 익히기(선택 화면·연습 카드) + 도로 법규 화면
// (11/5 정리) MainActivity 의 Compose 화면을 기능별 파일로 나눴다. 각 화면은 MainActivity 의 확장 함수다(상태·getString 을 그대로 쓴다).
@file:OptIn(ExperimentalLayoutApi::class)

package kr.drivecoach.app

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.IconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import kr.drivecoach.app.ui.AdaptiveButtonRow
import kr.drivecoach.app.ui.ArtWithText
import kr.drivecoach.app.ui.CoachChipMinHeight
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.CoachCard
import kr.drivecoach.app.ui.CoachCornerRadius
import kr.drivecoach.app.ui.CoachSize
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.app.ui.PracticeArt
import kr.drivecoach.app.ui.PrimaryButton
import kr.drivecoach.app.ui.SecondaryButton
import kr.drivecoach.app.ui.SentenceParagraphs
import kr.drivecoach.core.input.Command
import kr.drivecoach.core.law.TrafficRules
import kr.drivecoach.core.practice.Chapter2Items
import kr.drivecoach.core.practice.Phase
import kr.drivecoach.core.practice.PracticeItem
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.Headlight
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.signal.Wiper

/**
 * 도로 법규 화면(11/4): 위에 주제 칩 12개(현재 주제는 라임 채움), 아래 카드에 제목 + 그림(law_<id>.webp 또는 .png) + 설명 글(= 음성), 이전 / 다음 / 닫기.
 * 퀴즈는 다음 빌드. 화면 글과 음성이 같은 문장이라 수정표 "멘트" 시트에서 함께 바뀐다.
 */
@Composable
internal fun MainActivity.LawScreen(coach: CoachController, u: LawUi) {
    Column(modifier = Modifier.fillMaxWidth()) {
        // (11/14 베타: 들어가자마자 "우회전"이 열리고 "도로 법규" 제목이 없다)
        Text(text = getString(R.string.menu_law), style = CoachType.Title, maxLines = 1)
        Spacer(Modifier.height(12.dp))
        val seen by coach.lessonsDone.collectAsState()
        FlowRow(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            TrafficRules.topics.forEach { t ->
                // 본 주제는 ✓(베타 2차 A-22)
                // (11/14 대표) 본 주제는 ✓ 대신 옅은 라임으로 채운다(지금 보는 주제는 진한 라임)
                HeaderChip(text = t.title, selected = t.id == u.topicId, done = "law:${t.id}" in seen, onClick = { if (t.id != u.topicId) coach.startLaw(t.id) })
            }
        }
        Spacer(Modifier.height(20.dp))
        CoachCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(text = u.heading, style = CoachType.Title, maxLines = 1)
                Spacer(Modifier.weight(1f))
                Text(text = "${u.index + 1} / ${u.total}", style = CoachType.Caption)
            }
            Spacer(Modifier.height(20.dp))
            // 그림(res/drawable-nodpi/law_<id>: 11/13 부터 7개는 3D 장면 사진, 나머지는 자체 제작 선화) 왼쪽 + 설명 오른쪽. 그림이 없으면 글만.
            val art = remember(u.topicId) { resources.getIdentifier("law_${u.topicId}", "drawable", packageName) }
            ArtWithText(art.takeIf { it != 0 }, u.title) {
                SentenceParagraphs(u.text, CoachType.Body.copy(fontSize = CoachSize.Option, lineBreak = CoachType.KoLineBreak), 42.sp)
                // (11/15 대표) 근거 조문 — 화면에만(음성으로 읽지 않음)
                if (u.basis.isNotBlank()) {
                    Spacer(Modifier.height(14.dp))
                    Text(text = getString(R.string.law_basis, u.basis), style = CoachType.Caption.copy(color = CoachColors.OnBackground))
                }
            }
            Spacer(Modifier.height(28.dp))
            AdaptiveButtonRow {
                SecondaryButton(onClick = { if (u.index > 0) coach.lawPrev() }, modifier = Modifier.weight(1f)) {
                    Text(text = getString(R.string.btn_prev), fontSize = CoachSize.Button, color = if (u.index > 0) CoachColors.OnBackground else CoachColors.Secondary)
                }
                PrimaryButton(onClick = { coach.lawNext() }, modifier = Modifier.weight(1f)) {
                    Text(text = getString(if (u.index + 1 < u.total) R.string.btn_next else R.string.btn_done), fontSize = CoachSize.Button)
                }
                SecondaryButton(onClick = { coach.endLaw() }, modifier = Modifier.weight(0.6f)) {
                    Text(text = getString(R.string.btn_close), fontSize = CoachSize.Option)
                }
            }
        }
    }
}

/**
 * "차 기능 익히기" 선택 화면(11/4 대표 요청): 위에 "← 메뉴"·제목·검색창, 아래는 스크롤.
 *  - 처음부터 순서대로(또는 이어서 하기) / 핸들 감각 / 직각 주차 카드 한 줄
 *  - 장별 묶음(2-1 운전석 체형 맞추기 …) 아래 항목 칩. 칩을 누르면 그 항목부터 시작(이후는 순서대로 이어진다). 통과한 항목은 ✓.
 *  - 검색어가 있으면 제목·장 제목에 검색어가 들어간 항목만 한 묶음으로 보여 준다(띄어쓰기 무시).
 */
@Composable
internal fun MainActivity.PracticeChooser(coach: CoachController, modifier: Modifier = Modifier) {
    val items = remember { coach.practiceItemList() }
    val done by coach.practiceDone.collectAsState()
    var query by practiceQuery
    Column(modifier = modifier.fillMaxWidth()) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            HeaderChip(text = getString(R.string.btn_back_menu), selected = false, onClick = { practiceMenuOpen.value = false })
            Spacer(Modifier.width(20.dp))
            Text(text = getString(R.string.practice_menu_title), style = CoachType.Title, maxLines = 1)
        }
        Spacer(Modifier.height(16.dp))
        // 검색창(11/4): 돋보기 + "기능 검색", 좌우로 꽉 차게. 현대차 OS 검색 느낌에 맞춤.
        OutlinedTextField(
            value = query,
            onValueChange = { query = it },
            singleLine = true,
            leadingIcon = { Icon(imageVector = Icons.Filled.Search, contentDescription = null, tint = CoachColors.Secondary, modifier = Modifier.size(32.dp)) },
            placeholder = { Text(getString(R.string.practice_search_hint), fontSize = CoachSize.Label, color = CoachColors.Secondary) },
            textStyle = CoachType.Body.copy(fontSize = CoachSize.Label),
            shape = RoundedCornerShape(CoachCornerRadius),
            colors = OutlinedTextFieldDefaults.colors(
                focusedBorderColor = CoachColors.Primary, unfocusedBorderColor = CoachColors.Outline,
                focusedTextColor = CoachColors.OnBackground, unfocusedTextColor = CoachColors.OnBackground,
                focusedContainerColor = CoachColors.Background, unfocusedContainerColor = CoachColors.Background,
                cursorColor = CoachColors.Primary,
            ),
            // (11/14 베타: 지우기 버튼이 없다)
            trailingIcon = if (query.isNotEmpty()) ({
                IconButton(onClick = { query = "" }) { Icon(imageVector = Icons.Filled.Close, contentDescription = getString(R.string.practice_search_clear), tint = CoachColors.Secondary, modifier = Modifier.size(32.dp)) }
            }) else null,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Column(modifier = Modifier.weight(1f).verticalScroll(practiceScroll)) {
            val q = query.replace(" ", "")
            if (q.isBlank()) {
                // (11/15 대표: "진도 표시는 없는 걸로, 이어서 하기는 빼고, 직각·평행 주차는 주차·핸들 연습으로") 맨 위 버튼 줄과 칸 색 설명을 뺐다.
                //   항목 칩을 누르면 그 항목부터 시작하고, '다음'은 목록 순서대로 이어진다.
                Chapter2Items.chapterTitles.forEach { (chapter, title) ->
                    val chapterItems = items.filter { it.chapter == chapter }
                    if (chapterItems.isNotEmpty()) {
                        Text(text = title, style = CoachType.Body, fontWeight = FontWeight.Bold, maxLines = 1)
                        Spacer(Modifier.height(10.dp))
                        ItemChips(chapterItems, done, coach)
                        Spacer(Modifier.height(24.dp))
                    }
                }
            } else {
                // 제목·별칭(깜빡이, 쌍라이트 …) 우선, 장 제목은 그 뒤(베타 A-11/D-25)
                val norm = { t: String -> t.replace(" ", "") }
                val byTitle = items.filter { it ->
                    norm(it.title).contains(q, ignoreCase = true) ||
                        Chapter2Items.aliases[it.id].orEmpty().any { a -> norm(a).contains(q, ignoreCase = true) }
                }
                // (11/14 베타: "등" 한 글자에 이름에 없는 속도계·경음기까지 나온다) 장 제목으로는 두 글자 이상일 때만 찾는다
                // (11/15 베타 6f: "와이퍼" 에 장 제목만 같은 방향지시등 3개가 섞여 나온다) 장 제목으로는 제목·별칭에 맞는 것이 없을 때만 찾는다
                val byChapter = if (q.length < 2 || byTitle.isNotEmpty()) emptyList() else items.filter { it -> it !in byTitle && Chapter2Items.chapterTitles[it.chapter].orEmpty().replace(" ", "").contains(q, ignoreCase = true) }
                val hits = byTitle + byChapter
                if (hits.isEmpty()) {
                    Text(text = getString(R.string.practice_no_match), style = CoachType.Body, color = CoachColors.Secondary)
                } else {
                    ItemChips(hits, done, coach)
                }
            }
        }
    }
}

/** 항목 칩 묶음(줄바꿈). 통과한 항목은 "✓ " 를 붙이고 회색 글씨. */
@Composable
internal fun MainActivity.ItemChips(list: List<PracticeItem>, done: Set<String>, coach: CoachController) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        list.forEach { item ->
            // (11/15 대표: "노란색이 쨍하다, 진도 표시는 없는 걸로") 끝냄·건너뜀과 상관없이 흰 바탕에 검정 글씨
            SecondaryButton(onClick = { practiceQuery.value = ""; coach.startPractice(item.id) }, minHeight = CoachChipMinHeight, container = CoachColors.Background) {
                Text(text = item.title, fontSize = CoachSize.Label, maxLines = 1, color = CoachColors.OnBackground)
            }
        }
    }
}

/**
 * 진행 중인 연습 항목 카드: 캡션(장·순번·제목·설명/해보기/확인·자동/직접), 진행 바, 그림(있으면) + 문장, 확인/다음/다시/닫기.
 * 그림은 PracticeArt.resIdFor(itemId) 가 null 이 아닐 때만 왼쪽에 두고, 없으면 문장이 카드 폭 전체를 쓴다.
 */
@Composable
internal fun MainActivity.PracticeCard(coach: CoachController, p: PracticeUi) {
    CoachCard {
        // 항목 이름은 큰 제목으로, 번호·단계·확인 방식은 그 아래 한 줄(베타 2차 A-5: 작은 회색 한 줄에만 있어 안 읽히던 문제)
        Text(text = p.title, style = CoachType.Title, maxLines = 1)
        // (11/14 대표) 번호·단계·확인 방식 한 줄은 뺐다. 센서가 확인하는 항목은 지금 차 상태를 표시등으로 보여 준다(밟으면 "브레이크 밟힘"이 켜짐).
        val vs by coach.stateFlow.collectAsState()
        // (11/14 베타 2차: 표시등이 있는 항목과 없는 항목에서 버튼 줄이 위아래로 움직인다) 표시등 자리는 늘 같은 높이로 비워 둔다
        Spacer(Modifier.height(10.dp))
        // (11/15 대표) '지금 차 상태' 표시등 줄은 뺐다
        Spacer(Modifier.height(12.dp))
        // 얇은 진행 바: 몇 번째 항목인지 / 전체
        val fraction = if (p.total > 0) (p.index - 1).toFloat() / p.total.toFloat() else 0f
        LinearProgressIndicator(
            progress = { fraction.coerceIn(0f, 1f) },
            modifier = Modifier.fillMaxWidth().height(6.dp),
            color = CoachColors.Primary,
            trackColor = CoachColors.Outline,
        )
        Spacer(Modifier.height(20.dp))
        // 그림 + 문장: 넓은 화면은 좌우, 좁은(세로) 화면은 위아래(11/6 Pleos 가이드)
        ArtWithText(PracticeArt.resIdFor(p.itemId), p.title) {
            // 11/4: 정차 설명이 2~4문장으로 길어져 글자를 줄임. 11/6: 문장마다 짧은 단락으로(가이드: 한 단락 3줄 이하)
            SentenceParagraphs(p.text, CoachType.Body.copy(fontSize = CoachSize.Option, lineBreak = CoachType.KoLineBreak), 40.sp)
            // 진행 줄: 미감지·센서 진행 안내가 없으면, 수동 항목의 해보기에는 "해 본 뒤 '다음'", 통과 화면에는 "잠시 후 다음 항목"(베타 2차 A-1·A-8)
            val hint = p.progress ?: when {
                p.phase == Phase.TRY && !p.detectable -> getString(R.string.practice_hint_manual_try)
                p.phase == Phase.PASSED || p.phase == Phase.SKIPPED -> getString(R.string.practice_hint_auto_next)
                else -> null
            }
            hint?.let {
                Spacer(Modifier.height(8.dp))
                Text(text = it, fontSize = CoachSize.Label, color = CoachColors.PrimaryInk, maxLines = 2)
            }
        }
        Spacer(Modifier.height(24.dp))
        AdaptiveButtonRow {
            // 11/4: 이전 / 다음 / 건너뛰기 ("다시"는 음성 명령으로만). 다음 = 설명→해보기→통과, 통과 후엔 다음 항목.
            SecondaryButton(onClick = { coach.onCommand(Command.PREV) }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_prev), fontSize = CoachSize.Button, color = if (p.index > 1) CoachColors.OnBackground else CoachColors.Secondary)   // (11/14 베타: 첫 항목에도 "이전"이 눌리는 모양)
            }
            // (11/14 베타: "다음"이 같은 항목의 다음 장으로 갈지 다음 항목으로 갈지 모르겠다) 단계마다 이름을 바꾼다
            PrimaryButton(onClick = { coach.onCommand(Command.NEXT) }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_next), fontSize = CoachSize.Button)
            }
            SecondaryButton(onClick = { coach.onCommand(Command.SKIP) }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_skip), fontSize = CoachSize.Button)
            }
            // 닫기도 같은 크기 버튼으로(베타 2차 A-6: 작은 흐린 글자라 버튼인 줄 몰랐음)
            SecondaryButton(onClick = { coach.endPractice() }, modifier = Modifier.weight(1f)) {
                Text(text = getString(R.string.btn_to_list), fontSize = CoachSize.Button)
            }
        }
    }
}

/** 센서 표시등 하나: 켜지면 라임 바탕 + 채운 점, 꺼져 있으면 테두리만 + 빈 점. */
@Composable
internal fun SensorChip(label: String, on: Boolean) {
    // (11/14 베타 4차: 누를 수 있는 동그라미 버튼처럼 보인다) 테두리 없는 상태 표시로 — 켜진 것만 연두 점
    Row(
        modifier = Modifier.padding(horizontal = 4.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = "●", fontSize = CoachSize.Chip, color = if (on) CoachColors.PrimaryInk else CoachColors.Outline)
        Spacer(Modifier.width(8.dp))
        Text(text = label, fontSize = CoachSize.Label, color = if (on) CoachColors.OnBackground else CoachColors.Secondary, fontWeight = if (on) FontWeight.Bold else FontWeight.Normal, maxLines = 1)
    }
}

/** 이 항목이 보는 차 신호를 (이름, 켜짐)으로. 신호가 안 오는 차(null)는 표시하지 않는다. */
internal fun sensorChips(itemId: String, s: VehicleState): List<Pair<String, Boolean>> {
    val out = mutableListOf<Pair<String, Boolean>>()
    fun add(label: String, on: Boolean?) { if (on != null) out.add(label to on) }
    when {
        itemId == "2-2-seatbelt" -> add("안전벨트 매짐", s.seatBeltWarning?.let { !it })
        itemId == "2-3-ignition" -> { add("브레이크 밟힘", s.brakePressed); add("시동 켜짐", s.ignitionOn) }
        itemId.startsWith("2-3-epb") -> { add("브레이크 밟힘", s.brakePressed); add("주차 브레이크 걸림", s.parkingBrakeOn) }
        itemId.startsWith("2-4-") -> {
            add("브레이크 밟힘", s.brakePressed)
            if (s.gear != Gear.UNKNOWN) listOf(Gear.P, Gear.R, Gear.N, Gear.D).forEach { g -> out.add(g.name to (s.gear == g)) }
        }
        itemId.startsWith("2-5-headlight") || itemId == "2-5-passing" -> if (s.headlight != Headlight.UNKNOWN) {
            out.add("오토" to (s.headlight == Headlight.AUTO)); out.add("하향등" to (s.headlight == Headlight.LOW)); out.add("상향등" to (s.headlight == Headlight.HIGH))
        }
        itemId == "2-5-hazard" -> add("비상 경고등 켜짐", s.hazardOn)
        itemId.startsWith("2-6-turn") || itemId == "2-6-lane-change-tap" -> if (s.turnSignal != TurnSignal.UNKNOWN) {
            out.add("왼쪽 방향지시등" to (s.turnSignal == TurnSignal.LEFT)); out.add("오른쪽 방향지시등" to (s.turnSignal == TurnSignal.RIGHT))
        }
        itemId == "2-6-wiper" -> if (s.wiper != Wiper.UNKNOWN) out.add("와이퍼 작동" to (s.wiper != Wiper.OFF))
        itemId == "2-7-window" -> add("창문 열림", s.windowsClosed?.let { !it })
        itemId == "2-7-door-lock" -> add("문 잠김", s.doorsLocked)
        itemId == "2-7-hvac" -> add("공조 켜짐", s.hvacOn)
    }
    return out
}

internal fun MainActivity.phaseLabel(phase: Phase): String = getString(
    when (phase) {
        Phase.EXPLAIN -> R.string.phase_explain
        Phase.TRY -> R.string.phase_try
        Phase.CONFIRM -> R.string.phase_confirm
        Phase.PASSED -> R.string.phase_passed
        Phase.SKIPPED -> R.string.phase_skipped
    },
)
