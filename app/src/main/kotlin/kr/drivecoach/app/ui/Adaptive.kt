// 화면 방향·폭에 맞추는 배치 도우미 (Pleos 설계 가이드 대응)
// - 가로·세로 화면 모두 대응(세로형 12.9" 디스플레이가 있다. 레터박스·호환 모드 금지)
// - 한 단락 3줄 이하: 긴 설명은 문장마다 끊어 짧은 단락으로 보여 준다(음성은 그대로 한 번에)
@file:OptIn(ExperimentalLayoutApi::class)

package kr.drivecoach.app.ui

import android.net.Uri
import android.widget.VideoView
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.FlowRowScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.layout.Placeable
import androidx.compose.ui.layout.layout
import androidx.compose.ui.unit.Constraints
import kotlin.math.roundToInt
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.material3.Text

/** 좁은 화면(세로형이거나 폭이 [NARROW_WIDTH] 미만)이면 true. [AdaptiveScreen] 이 정한다. */
val LocalNarrow = staticCompositionLocalOf { false }

/**
 * 넓은 화면에서 그림 칸의 최대 높이. 활동 화면(ParkedScreen)이 남은 높이에 맞춰 정해 준다(기본 420dp = 예전 값).
 * 화면이 크면 그림을 키워 카드 아래 빈 곳을 줄이고, 작으면 줄여 버튼이 화면 밖으로 밀려나지 않게 한다.
 */
val LocalArtMaxHeight = staticCompositionLocalOf { 420.dp }

/** 넓은 화면에서 그림 칸 : 글 칸 폭 비율(그림을 조금 넓게). */
private const val ART_WEIGHT = 1.25f

/**
 * 세로로 쌓되, 마지막 칸이 남은 높이(들어온 최소 높이 - 앞 칸들)를 최소 높이로 받는다.
 * 활동 카드가 화면 아래까지 차고, 카드 안 버튼 줄이 늘 화면 아래 같은 자리에 오게 한다(Pleos: 버튼 자리 고정).
 * 내용이 더 길면 그대로 늘어나 바깥 스크롤로 보인다(잘리지 않음).
 */
@Composable
fun FillLastColumn(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Layout(content = content, modifier = modifier) { measurables, constraints ->
        val loose = constraints.copy(minHeight = 0)
        val placeables = ArrayList<Placeable>(measurables.size)
        var used = 0
        measurables.forEachIndexed { i, m ->
            val c = if (i == measurables.lastIndex) loose.copy(minHeight = (constraints.minHeight - used).coerceIn(0, loose.maxHeight)) else loose
            val p = m.measure(c)
            placeables += p
            used += p.height
        }
        val width = (placeables.maxOfOrNull { it.width } ?: 0).coerceIn(constraints.minWidth, constraints.maxWidth)
        val height = used.coerceIn(constraints.minHeight, constraints.maxHeight)
        layout(width, height) {
            var y = 0
            placeables.forEach { it.placeRelative(0, y); y += it.height }
        }
    }
}

/** 이 폭보다 좁으면 그림·글을 위아래로, 버튼을 두 줄로 놓는다. */
val NARROW_WIDTH: Dp = 760.dp

/** 화면 크기를 재서 [LocalNarrow] 를 정해 준다. 앱 화면 맨 바깥에 한 번. */
@Composable
fun AdaptiveScreen(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    BoxWithConstraints(modifier) {
        val narrow = maxWidth < NARROW_WIDTH || maxWidth < maxHeight
        CompositionLocalProvider(LocalNarrow provides narrow) { content() }
    }
}

/**
 * 그림 + 글. 넓은 화면은 그림 왼쪽·글 오른쪽(반반), 좁은 화면은 그림 위·글 아래.
 * [artRes] 가 null 이면 글만 폭 전체.
 */
@Composable
fun ArtWithText(artRes: Int?, artDescription: String?, videoRes: Int = 0, keepArtSlot: Boolean = false, content: @Composable ColumnScope.() -> Unit) {
    if (LocalNarrow.current) {
        Column(modifier = Modifier.fillMaxWidth()) {
            if (artRes != null) {
                ArtOrVideo(artRes, artDescription, videoRes, Modifier.fillMaxWidth().heightIn(max = 300.dp))
                Spacer(Modifier.height(20.dp))
            }
            Column(modifier = Modifier.fillMaxWidth(), content = content)
        }
    } else {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(28.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (artRes != null) {
                ArtOrVideo(artRes, artDescription, videoRes, Modifier.weight(ART_WEIGHT).heightIn(max = LocalArtMaxHeight.current))
            } else if (keepArtSlot) {
                // 그림이 없는 단계에도 그림 자리를 같은 높이로 비워 둔다(아래 버튼이 위아래로 움직여 잘못 누르지 않게)
                Spacer(Modifier.weight(ART_WEIGHT).heightIn(max = LocalArtMaxHeight.current).aspectRatio(8f / 5f))
            }
            Column(modifier = Modifier.weight(1f), content = content)
        }
    }
}

/**
 * 그림 자리. [videoRes](res/raw 의 mp4, 0 = 없음)가 있으면 소리 없이 되풀이 재생하고, 재생이 안 되면 그림으로 돌아간다.
 * 직각 주차 단계 카드가 쓴다. 영상은 800x500(8:5).
 */
@Composable
fun ArtOrVideo(artRes: Int, artDescription: String?, videoRes: Int, modifier: Modifier = Modifier) {
    val failed = remember(videoRes) { mutableStateOf(false) }
    if (videoRes == 0 || failed.value) {
        // 그림 비율대로 칸 폭에 맞춰 키운다(원본 크기 800×500 보다 커질 수 있다). 높이는 modifier 의 최대 높이까지.
        val painter = painterResource(id = artRes)
        val sz = painter.intrinsicSize
        val ratio = if (sz.width > 0f && sz.height > 0f && sz.width.isFinite() && sz.height.isFinite()) sz.width / sz.height else 8f / 5f
        Image(painter = painter, contentDescription = artDescription, modifier = modifier.fitAspect(ratio), contentScale = ContentScale.Fit)
        return
    }
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        key(videoRes) {
            AndroidView(
                modifier = Modifier.aspectRatio(8f / 5f),
                factory = { ctx ->
                    VideoView(ctx).apply {
                        setVideoURI(Uri.parse("android.resource://${ctx.packageName}/$videoRes"))
                        setOnPreparedListener { mp -> mp.setVolume(0f, 0f); mp.isLooping = true; start() }
                        setOnErrorListener { _, _, _ -> failed.value = true; true }
                    }
                },
            )
        }
    }
}

/**
 * 칸 폭 전체 × (폭 / [ratio]) 크기, 단 들어온 최대 높이를 넘지 않는다(넘으면 그 높이에서 그림이 가운데 맞춤으로 줄어든다).
 * aspectRatio 는 폭이 정해진 칸에서 최대 높이를 무시하고 커져 버튼 줄을 밀어냈다.
 */
private fun Modifier.fitAspect(ratio: Float): Modifier = layout { measurable, constraints ->
    val w = if (constraints.hasBoundedWidth) constraints.maxWidth else (constraints.maxHeight * ratio).roundToInt()
    var h = (w / ratio).roundToInt()
    if (constraints.hasBoundedHeight) h = h.coerceAtMost(constraints.maxHeight)
    h = h.coerceAtLeast(constraints.minHeight)
    val p = measurable.measure(Constraints.fixed(w, h))
    layout(w, h) { p.place(0, 0) }
}

/** 버튼 줄. 넓은 화면은 한 줄, 좁은 화면은 한 줄에 두 개씩. 안에서 Modifier.weight 를 그대로 쓴다. */
@Composable
fun AdaptiveButtonRow(content: @Composable FlowRowScope.() -> Unit) {
    FlowRow(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
        maxItemsInEachRow = if (LocalNarrow.current) 2 else Int.MAX_VALUE,
        content = content,
    )
}

/** 문장 끝(. ? !) 뒤 공백에서 끊는다. "1,000" 같은 숫자 안의 점은 뒤에 공백이 없어 끊기지 않는다. */
private val SENTENCE_END = Regex("(?<=[.?!])\\s+")

/** 설명 글을 문장마다 짧은 단락으로 끊는다(빈 문장은 버림). */
fun sentences(text: String): List<String> = text.split(SENTENCE_END).map { it.trim() }.filter { it.isNotEmpty() }

/**
 * 긴 설명을 문장 단락으로 보여 준다(Pleos 가이드: 한 단락 3줄 이하). 두 문장 이하면 그냥 한 덩어리.
 * 음성은 원래 문장을 그대로 읽으므로 화면 글과 내용이 같다.
 */
@Composable
fun SentenceParagraphs(text: String, style: TextStyle, lineHeight: TextUnit, gap: Dp = 10.dp) {
    val parts = sentences(text)
    if (parts.size <= 2) {
        Text(text = text, style = style, lineHeight = lineHeight)
        return
    }
    Column {
        parts.forEachIndexed { i, s ->
            if (i > 0) Spacer(Modifier.height(gap))
            Text(text = s, style = style, lineHeight = lineHeight)
        }
    }
}
