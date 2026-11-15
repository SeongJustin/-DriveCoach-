// 담당: S (화면 스타일 — 흰 바탕, 검정 글씨, 라임 포인트. 색과 글자 크기는 전부 이 파일에서만 바꾼다)
// 주의: 이 환경에서는 Android 빌드가 불가하다. material3 1.2.x(compose-bom 2024.06.00) 의 공개 API 만 썼다.
package kr.drivecoach.app.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.intl.LocaleList
import androidx.compose.ui.text.style.LineBreak
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 앱 공통 색. 사용자 요청: "흰 바탕에 검정 글씨로 깔끔하게". 색을 바꾸려면 여기만 고친다.
 */
object CoachColors {
    // 11/10 대표: 검정·흰색 바탕에 중요한 곳만 라임 그린. 다른 색은 쓰지 않는다(위험 알림의 빨강만 예외).
    // 11/11 대표: 흰색과 검정 중 더 넓게 쓰는 색은 흰색 → 흰 바탕·검정 글씨, 라임은 채운 면(버튼·진행 바)에만.
    /** 바탕(배경). Pure White. */
    val Background = Color(0xFFFFFFFF)
    /** 카드·눌린 면(바탕보다 한 단계 밝은 검정). */
    val Surface = Color(0xFFF4F4F5)
    /** (11/14 베타: 흰 칸과 회색 칸이 거의 같아 보인다) 건너뛴 항목 칸 */
    val Skipped = Color(0xFFD9D9DE)
    /** 본문 글씨. Midnight Black. */
    val OnBackground = Color(0xFF0B0B0C)
    /** 포인트(주 버튼·진행 바·강조). Lime Green. */
    val Primary = Color(0xFFC6F432)
    /** 주 버튼(라임) 위 글씨는 검정. */
    val OnPrimary = Color(0xFF0B0B0C)
    /** 보조 글씨(캡션·신호 한 줄·"최근 안내" 라벨). */
    val Secondary = Color(0xFF5F5F66)
    /** 카드·보조 버튼 테두리, 진행 바 트랙. */
    val Outline = Color(0xFFD8D8DD)
    /** 위험·실패(진단 FAIL). 안전 알림이라 빨강만 남긴다. */
    val Danger = Color(0xFFD93025)
    /** 정상·성공 글씨(진단 OK, "통과"). 흰 바탕에서 라임 글씨는 읽히지 않아 검정 굵은 글씨로 쓴다. */
    val Ok = Color(0xFF0B0B0C)
    /** 강조 글씨·아이콘(흰 바탕 위). 라임은 면에만 쓰고 글씨 강조는 검정. */
    val PrimaryInk = Color(0xFF0B0B0C)
}

/**
 * 앱 공통 글자 크기. 에뮬레이터(2560×1440, 160dpi, 런처 오른쪽 절반 ≈ 1300×1000px) 를 축소해서 보므로 전부 크게.
 */
object CoachType {
    /**
     * (11/15 베타 6f: "골반 부분/은", "확인하십/시오" 처럼 낱말 가운데서 줄이 바뀐다) 한국어는 띄어쓰기(어절) 단위로 줄을 바꾼다.
     * Android 13(API 33) 이상에서만 듣고, 그 아래는 예전처럼 글자 단위다. 기본 글씨(LocalTextStyle = bodyLarge)도 이 값을 쓴다.
     */
    val KoLineBreak = LineBreak(LineBreak.Strategy.HighQuality, LineBreak.Strictness.Strict, LineBreak.WordBreak.Phrase)
    private val ko = LocaleList("ko-KR")
    /** 제목(차종 헤더). 44sp 굵게. */
    val Title = TextStyle(fontSize = 44.sp, fontWeight = FontWeight.Bold, color = CoachColors.OnBackground, lineBreak = KoLineBreak, localeList = ko)
    /** 본문(연습 문장·메뉴 제목·버튼). 32sp. */
    val Body = TextStyle(fontSize = 32.sp, color = CoachColors.OnBackground, lineBreak = KoLineBreak, localeList = ko)
    /** 캡션(신호 한 줄·장/순번·메뉴 설명). 22sp 회색. */
    val Caption = TextStyle(fontSize = 22.sp, color = CoachColors.Secondary, lineBreak = KoLineBreak, localeList = ko)
}

/**
 * 글자 크기 단계(11/15 디자인 통일). 화면마다 제각각이던 36, 30, 28, 24, 20sp 를 아래 단계로 맞췄다.
 * 버튼 글씨는 같은 줄이면 같은 크기를 쓴다.
 */
object CoachSize {
    /** 인트로를 못 틀 때의 앱 이름. */
    val Hero = 72.sp
    /** 주 버튼과 그 줄의 버튼, 주행 화면 한 줄. */
    val Button = 32.sp
    /** 목록에서 고르는 버튼(코스 목적), 긴 본문. */
    val Option = 28.sp
    /** 보조 줄 버튼(자리 바로잡기, 목록으로), 안내 한 줄, 대화 상자. */
    val Label = 26.sp
    /** 머리글 칩, 작은 표시. */
    val Chip = 22.sp
    /** 진단 글씨(개발용, 고정폭). */
    val Mono = 16.sp
}

/** 버튼·카드 공통 모서리 반경. */
val CoachCornerRadius = 20.dp

/** 버튼 최소 높이(터치 영역). */
val CoachButtonMinHeight = 96.dp

private val coachColorScheme = lightColorScheme(
    primary = CoachColors.Primary,
    onPrimary = CoachColors.OnPrimary,
    background = CoachColors.Background,
    onBackground = CoachColors.OnBackground,
    surface = CoachColors.Background,
    surfaceVariant = CoachColors.Surface,
    surfaceContainer = CoachColors.Surface,
    surfaceContainerHigh = CoachColors.Surface,
    surfaceContainerHighest = CoachColors.Surface,
    surfaceContainerLow = CoachColors.Surface,
    onSurface = CoachColors.OnBackground,
    onSurfaceVariant = CoachColors.Secondary,
    outline = CoachColors.Outline,
    error = CoachColors.Danger,
    onError = CoachColors.OnPrimary,
)

private val coachTypography = Typography(
    titleLarge = CoachType.Title,
    bodyLarge = CoachType.Body,
    labelMedium = CoachType.Caption,
)

private val coachShapes = Shapes(
    small = RoundedCornerShape(CoachCornerRadius),
    medium = RoundedCornerShape(CoachCornerRadius),
    large = RoundedCornerShape(CoachCornerRadius),
)

/**
 * 앱 전체를 감싸는 MaterialTheme. 밝은 색 하나만 쓴다(11/11: 흰 바탕, 검정 글씨, 채운 면에만 라임).
 */
@Composable
fun CoachTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = coachColorScheme, typography = coachTypography, shapes = coachShapes, content = content)
}

/** 헤더 칩("조용히", "진단") 처럼 작은 버튼의 최소 높이. */
val CoachChipMinHeight = 64.dp

/**
 * 주 동작 버튼(라임 바탕, 검정 글씨). 화면당 하나만 두는 것이 원칙(예: 연습 카드의 "확인"). 글씨 색은 LocalContentColor 로 검정이 된다.
 */
@Composable
fun PrimaryButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    minHeight: Dp = CoachButtonMinHeight,
    content: @Composable RowScope.() -> Unit,
) {
    Button(
        onClick = onClick,
        modifier = modifier.heightIn(min = minHeight),
        shape = RoundedCornerShape(CoachCornerRadius),
        colors = ButtonDefaults.buttonColors(containerColor = CoachColors.Primary, contentColor = CoachColors.OnPrimary),
        content = content,
    )
}

/**
 * 보조 동작 버튼(흰 바탕·검정 글씨·옅은 테두리). "다음/다시", 헤더 칩 등.
 */
@Composable
fun SecondaryButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    minHeight: Dp = CoachButtonMinHeight,
    container: Color = CoachColors.Background,   // (11/14 대표) 끝낸 항목은 ✓ 대신 라임으로 채운다
    content: @Composable RowScope.() -> Unit,
) {
    OutlinedButton(
        onClick = onClick,
        modifier = modifier.heightIn(min = minHeight),
        shape = RoundedCornerShape(CoachCornerRadius),
        border = BorderStroke(2.dp, CoachColors.Outline),
        colors = ButtonDefaults.outlinedButtonColors(containerColor = container, contentColor = CoachColors.OnBackground),
        content = content,
    )
}
