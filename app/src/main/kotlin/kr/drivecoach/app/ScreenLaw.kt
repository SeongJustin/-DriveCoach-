// 도로 법규 화면 조각(나라별 법규와 함께): 나라 고르기 탭 줄, 주제 제목.
// 글은 바꾸지 않고 모양만: 나라 줄이 주제 칩 줄과 같은 모양이라 두 줄이 섞여 보이던 것, 다른 나라 제목(원어 괄호)이 한 줄을 넘어 잘리던 것.
package kr.drivecoach.app

import androidx.compose.foundation.background
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kr.drivecoach.app.ui.CoachChipMinHeight
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.CoachSize
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.core.law.LawSet

/**
 * 나라 고르기: 탭 모양 한 줄(줄바꿈 없이 가로로 밀림 — 나라가 늘어도 한 줄). 아래 주제 칩(둥근 칸)과 모양이 달라 '나라'와 '주제'가 갈린다.
 * 고른 나라는 굵은 글씨 + 아래 검정 띠(흰 바탕에서 라임 띠는 대비가 낮아 검정), 줄 전체 아래 옅은 선. 누르는 칸 높이는 칩과 같은 72dp.
 */
@Composable
internal fun LawCountryTabs(sets: List<LawSet>, selected: String, onSelect: (String) -> Unit) {
    Box(modifier = Modifier.fillMaxWidth()) {
        Box(modifier = Modifier.align(Alignment.BottomStart).fillMaxWidth().height(2.dp).background(CoachColors.Outline))
        Row(modifier = Modifier.horizontalScroll(rememberScrollState())) {
            sets.forEach { s ->
                val on = s.country == selected
                // 밑줄이 글 폭만큼: 칸 폭을 글의 고유 폭(IntrinsicSize.Max)으로 정하고 밑줄은 그 폭을 채운다(가로 스크롤 안이라 fillMaxWidth 만으로는 0 폭)
                Column(
                    modifier = Modifier
                        .height(CoachChipMinHeight)
                        .selectable(selected = on, role = Role.Tab) { if (!on) onSelect(s.country) }   // 고른 나라를 화면 읽기에도 알린다
                        .padding(horizontal = 22.dp)
                        .width(IntrinsicSize.Max),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Spacer(Modifier.weight(1f))
                    Text(text = s.label, fontSize = CoachSize.Label, fontWeight = if (on) FontWeight.Bold else FontWeight.Normal, color = CoachColors.OnBackground, maxLines = 1)
                    Spacer(Modifier.weight(1f))
                    Box(modifier = Modifier.fillMaxWidth().height(6.dp).background(if (on) CoachColors.OnBackground else Color.Transparent, RoundedCornerShape(topStart = 3.dp, topEnd = 3.dp)))
                }
            }
        }
    }
}

/**
 * 주제 제목. "통행 구분 (通行区分)" 처럼 끝에 원어가 괄호로 붙은 제목은 우리말 부분을 크게(두 줄까지), 괄호 원어는 그 아래 작은 검정 글씨로 나눈다.
 * 긴 우리말 제목(25자 이상)은 36sp. 괄호가 없는 한국 제목은 예전처럼 44sp 한 덩어리.
 */
@Composable
internal fun RowScope.LawHeading(heading: String) {
    val cut = heading.indexOf(" (")
    val split = cut > 0 && heading.endsWith(")")
    val main = if (split) heading.substring(0, cut) else heading
    Column(modifier = Modifier.weight(1f)) {
        Text(text = main, style = CoachType.Title, fontSize = if (main.length > 24) 36.sp else CoachType.Title.fontSize, maxLines = 2, overflow = TextOverflow.Ellipsis)
        if (split) {
            Spacer(Modifier.height(4.dp))
            Text(text = heading.substring(cut + 1), style = CoachType.Caption.copy(color = CoachColors.OnBackground), maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}
