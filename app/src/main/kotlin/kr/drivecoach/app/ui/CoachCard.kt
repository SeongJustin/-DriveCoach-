// 공용 화면 블록(11/15 코드 정리): 활동 화면(차량 조작법, 도로 법규, 주차, 코스)이 똑같이 쓰던 카드 틀을 한곳에 모았다.
package kr.drivecoach.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.dp

/** 활동 카드 틀: 폭 전체, 흰 바탕, 2dp 테두리, 둥근 모서리, 안쪽 여백 28dp. 내용은 세로로 쌓는다. */
@Composable
internal fun CoachCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    val shape = RoundedCornerShape(CoachCornerRadius)
    Column(
        modifier = modifier
            .fillMaxWidth()
            .clip(shape)
            .background(CoachColors.Background, shape)
            .border(2.dp, CoachColors.Outline, shape)
            .padding(28.dp),
        content = content,
    )
}
