// 코스 주행(5장) 화면 — 팀원 담당 영역
// (11/5 정리) MainActivity 의 Compose 화면을 기능별 파일로 나눴다. 각 화면은 MainActivity 의 확장 함수다(상태·getString 을 그대로 쓴다).
@file:OptIn(ExperimentalLayoutApi::class)

package kr.drivecoach.app

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kr.drivecoach.app.ui.CoachChipMinHeight
import kr.drivecoach.app.ui.CoachColors
import kr.drivecoach.app.ui.CoachCard
import kr.drivecoach.app.ui.CoachCornerRadius
import kr.drivecoach.app.ui.CoachSize
import kr.drivecoach.app.ui.CoachType
import kr.drivecoach.app.ui.PrimaryButton
import kr.drivecoach.app.ui.SecondaryButton
import kr.drivecoach.core.course.CourseOptionId
import kr.drivecoach.core.course.LatLon
import kr.drivecoach.core.course.ManeuverType


/**
 * 코스 주행 카드(흰 바탕). 단계별:
 *  - CHOOSE: 제목 + 5-1 선택지 버튼 2열 격자(가장 무서운 것 / 목적) + 닫기
 *  - READY: 캡션(선택지·경유지 수·목적지 거리) + 브리핑 요약 문장 + 큰 "내비로 보내기"(주) + 닫기
 *  - ROUTING / DRIVING: 상태 한 줄("경로 계산 중…" / "주행 중 · 다음: 300 m 앞 우회전 · 제한속도 50") + 닫기(경로 취소)
 *  - GENERATING: (T) "코스 계산 중…" + 진행 막대 + 닫기 — 길찾기 API 가 백그라운드에서 도는 동안
 *  - ARRIVED: 결과 한 줄(거리·시간·급제동·과속·코치 횟수) + 닫기(주)
 *  - ERROR: 이유 + 다시 시도(주) + (길찾기 실패면) 임시 코스로 진행 + 닫기
 * 에뮬레이터: Virtual Console 은 차를 못 움직이므로 DRIVING 이후(TBT·도착)는 실차에서만 보인다. 내비 앱에 경로가 뜨는 것까지가 시연 범위.
 */
@Composable
internal fun MainActivity.CourseScreen(coach: CoachController, c: CourseUi) {
    CoachCard {
        Text(text = courseCaption(c), style = CoachType.Caption, maxLines = 1)
        Spacer(Modifier.height(16.dp))
        when (c.phase) {
            CoursePhase.CHOOSE -> {
                Text(text = getString(R.string.course_title), style = CoachType.Body, fontWeight = FontWeight.Bold, maxLines = 1)
                Spacer(Modifier.height(16.dp))
                // (11/15 강명수) 난이도 1~5 와 연습 시간. 길찾기 키가 있을 때(난이도별 생성기)만 보인다.
                var difficulty by remember(c.difficulty) { mutableStateOf(c.difficulty) }
                var minutes by remember(c.targetMinutes) { mutableStateOf(c.targetMinutes) }
                if (c.difficultyAvailable) {
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(text = getString(R.string.course_difficulty_label), style = CoachType.Caption, maxLines = 1)
                        for (level in 1..5) HeaderChip(text = level.toString(), selected = level == difficulty, onClick = { difficulty = level })
                        Spacer(Modifier.width(24.dp))
                        Text(text = getString(R.string.course_minutes_label), style = CoachType.Caption, maxLines = 1)
                        for (m in listOf(10, 20, 30)) HeaderChip(text = getString(R.string.course_minutes, m), selected = m == minutes, onClick = { minutes = m })
                    }
                    Spacer(Modifier.height(16.dp))
                }
                // 2열 격자: 선택지를 둘씩 묶어 Row 로
                val rows = c.options.chunked(2)
                Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    for (pair in rows) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                            for (id in pair) {
                                SecondaryButton(onClick = { coach.chooseCourseOption(id, difficulty, minutes) }, modifier = Modifier.weight(1f)) {
                                    Text(text = courseOptionLabel(id), fontSize = CoachSize.Option, maxLines = 1)
                                }
                            }
                            if (pair.size == 1) Spacer(Modifier.weight(1f))
                        }
                    }
                }
                Spacer(Modifier.height(16.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(text = getString(if (c.naviReady) R.string.course_navi_ready else R.string.course_navi_not_ready), style = CoachType.Caption, color = if (c.naviReady) CoachColors.Ok else CoachColors.Danger, maxLines = 1)
                    Spacer(Modifier.weight(1f))
                    // (11/14 베타: 작은 글씨라 버튼처럼 안 보인다)
                    SecondaryButton(onClick = { coach.endActivity() }, minHeight = CoachChipMinHeight) {
                        Text(text = getString(R.string.btn_close), fontSize = CoachSize.Label)
                    }
                }
            }
            CoursePhase.GENERATING -> {
                // (담당 T) 길찾기 API 가 백그라운드에서 도는 동안. 닫기만 가능(닫으면 생성 작업 취소).
                Text(text = getString(R.string.course_status_generating), style = CoachType.Body, maxLines = 3)
                Spacer(Modifier.height(16.dp))
                // 무한(indeterminate) 진행 바 — progress 인자 없는 material3 오버로드
                LinearProgressIndicator(modifier = Modifier.fillMaxWidth().height(6.dp), color = CoachColors.Primary, trackColor = CoachColors.Outline)
                Spacer(Modifier.height(24.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_close), fontSize = CoachSize.Button)
                    }
                }
            }
            CoursePhase.READY -> {
              // (11/14 베타: 안내 글 길이에 따라 버튼 높이가 매번 달라진다) 글 칸 높이를 고정
              Row(modifier = Modifier.fillMaxWidth().height(330.dp), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
              if (c.routeShape.size >= 2) Column(modifier = Modifier.width(300.dp)) {
                  RouteShape(c.routeShape, Modifier.size(300.dp, 290.dp))
                  Text(text = getString(R.string.course_map_legend), style = CoachType.Caption, maxLines = 1)
              }
              Column(modifier = Modifier.weight(1f)) {
                Text(text = c.summary, style = CoachType.Body, maxLines = 6)
                Spacer(Modifier.height(8.dp))
                // (담당 T) 실제 경로의 숫자: 거리·좌회전·차선 변경(추정)·비보호 좌회전·우회전 + 출처(길찾기 N회 / 임시 코스)
                val dKm = c.distanceKm
                val lt = c.leftTurns
                val lc = c.laneChanges
                val ul = c.unprotectedLefts
                val rt = c.rightTurns
                // (11/14 베타 4차: 같은 숫자가 큰 글과 작은 줄에 두 번) 작은 줄은 진단 보기에서만
                if (showDiag.value && dKm != null && lt != null && lc != null && ul != null && rt != null) {
                    Text(text = getString(R.string.course_summary_numbers, dKm, lt, lc, ul, rt), style = CoachType.Caption, maxLines = 2)
                    Spacer(Modifier.height(4.dp))
                }
                // (11/14 베타: 고른 목적과 나온 코스가 다르다) 조건을 못 맞췄으면 무엇이 다른지 그대로 알린다
                c.mismatch?.let {
                    Text(text = getString(R.string.course_mismatch, it), style = CoachType.Caption, color = CoachColors.Danger, maxLines = 2)
                    Spacer(Modifier.height(4.dp))
                }
                // (11/14 베타: "실제 도로 경로 · 길찾기 8회"는 초보에게 뜻 없는 말) 임시 코스 경고만 늘 보이고, 길찾기 횟수는 진단 보기에서
                if (!c.generated || showDiag.value) Text(
                    text = if (c.generated) getString(R.string.course_source_generated, c.apiCalls) else getString(R.string.course_source_offset),
                    style = CoachType.Caption, color = if (c.generated) CoachColors.Secondary else CoachColors.Danger, maxLines = 2,
                )
                Spacer(Modifier.height(4.dp))
                Text(text = getString(if (c.naviReady) R.string.course_navi_ready else R.string.course_navi_not_ready), style = CoachType.Caption, color = if (c.naviReady) CoachColors.Ok else CoachColors.Danger, maxLines = 1)
              }
              }
                Spacer(Modifier.height(16.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    PrimaryButton(onClick = { coach.sendCourseToNavi() }, modifier = Modifier.weight(2f)) {
                        Text(text = getString(R.string.btn_course_send), fontSize = CoachSize.Button)
                    }
                    // (11/14 베타: 다른 목적을 보려면 매번 메뉴부터 다시 들어가야 한다)
                    SecondaryButton(onClick = { coach.endActivity(); coach.startCourse() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_course_rechoose), fontSize = CoachSize.Button)
                    }
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_close), fontSize = CoachSize.Button)
                    }
                }
                // (11/14 베타: 누르면 예고 없이 내비 화면으로 바뀐다)
                Spacer(Modifier.height(10.dp))
                Text(text = getString(R.string.course_send_hint), style = CoachType.Caption, maxLines = 2)
            }
            CoursePhase.ROUTING, CoursePhase.DRIVING -> {
                Text(text = courseStatusLine(c), style = CoachType.Body, maxLines = 3)
                val dist = c.routeDistanceM
                val dur = c.routeDurationS
                if (dist != null && dur != null) {
                    Spacer(Modifier.height(8.dp))
                    Text(text = getString(R.string.course_route_line, dist / 1000.0, (dur + 59) / 60), style = CoachType.Caption, maxLines = 1)
                }
                Spacer(Modifier.height(24.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_close), fontSize = CoachSize.Button)
                    }
                }
            }
            CoursePhase.ARRIVED -> {
                val r = c.result
                Text(
                    text = if (r != null) getString(R.string.course_result_line, r.distanceKm, r.durationMin, r.hardBrakeCount, r.overspeedCount, r.coachSpeakCount)
                    else getString(R.string.course_result_pending),
                    style = CoachType.Body, maxLines = 3,
                )
                Spacer(Modifier.height(24.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    PrimaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_close), fontSize = CoachSize.Button)
                    }
                }
            }
            CoursePhase.ERROR -> {
                // (11/15 오류 검수) 내부 코드(CAUSE_SEARCH_ROUTE, no_location …)를 그대로 보이지 않고 운전자용 문장으로. 원래 코드는 logcat(코스 오류: …)에
                Text(text = courseErrorText(c.error), style = CoachType.Body, color = CoachColors.Danger, maxLines = 3)
                Spacer(Modifier.height(24.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
                    // (11/15 오류 검수) 조건에 맞는 코스가 없거나 내비가 경로를 못 찾으면 같은 목적으로 다시 해도 같다 → '다른 목적'
                    if (courseErrorWantsOtherPurpose(c.error)) {
                        PrimaryButton(onClick = { coach.endActivity(); coach.startCourse() }, modifier = Modifier.weight(1f)) {
                            Text(text = getString(R.string.btn_course_rechoose), fontSize = CoachSize.Button)
                        }
                    } else {
                        PrimaryButton(onClick = { coach.retryCourse() }, modifier = Modifier.weight(1f)) {
                            Text(text = getString(R.string.btn_course_retry), fontSize = CoachSize.Button)
                        }
                    }
                    if (c.fallbackAvailable) {
                        // (담당 T) 길찾기가 실패했을 때의 마지막 수단: 오프셋 임시 코스
                        SecondaryButton(onClick = { coach.useFallbackCourse() }, modifier = Modifier.weight(1f)) {
                            Text(text = getString(R.string.btn_course_fallback), fontSize = CoachSize.Button, maxLines = 1)
                        }
                    }
                    SecondaryButton(onClick = { coach.endActivity() }, modifier = Modifier.weight(1f)) {
                        Text(text = getString(R.string.btn_close), fontSize = CoachSize.Button)
                    }
                }
            }
        }
        // 내비 진단 줄(최근 6줄): 초기화·요청·이벤트가 실제로 오는지 에뮬레이터에서 바로 본다
        // (11/14 베타: 개발 로그가 그대로 보인다) 설정의 "진단 보기"를 켰을 때만 보인다
        if (showDiag.value && c.naviDiag.isNotEmpty()) {
            Spacer(Modifier.height(16.dp))
            Text(getString(R.string.course_diag_title), fontSize = CoachSize.Mono, fontFamily = FontFamily.Monospace, color = CoachColors.Secondary)
            for (line in c.naviDiag.takeLast(6)) {
                Text(line, fontSize = CoachSize.Mono, fontFamily = FontFamily.Monospace, color = if (line.startsWith("FAIL")) CoachColors.Danger else CoachColors.Secondary, maxLines = 1)
            }
        }
        // (담당 T) 길찾기(Naver Directions) 진단 줄(최근 4줄): HTTP 코드·응답 code·오늘 호출 수
        if (showDiag.value && c.routeDiag.isNotEmpty()) {
            Spacer(Modifier.height(8.dp))
            Text(getString(R.string.course_route_diag_title), fontSize = CoachSize.Mono, fontFamily = FontFamily.Monospace, color = CoachColors.Secondary)
            for (line in c.routeDiag.takeLast(4)) {
                Text(line, fontSize = CoachSize.Mono, fontFamily = FontFamily.Monospace, color = if (line.startsWith("FAIL")) CoachColors.Danger else CoachColors.Secondary, maxLines = 1)
            }
        }
    }
}

/** 단계별 캡션. READY 는 선택지·경유지 수·목적지 거리. */
internal fun MainActivity.courseCaption(c: CourseUi): String = when (c.phase) {
    CoursePhase.CHOOSE -> getString(R.string.course_caption_choose)
    CoursePhase.GENERATING -> getString(R.string.course_caption_generating, c.chosen?.let { courseOptionLabel(it) } ?: "")
    CoursePhase.READY, CoursePhase.ROUTING, CoursePhase.DRIVING -> {
        // (11/14 베타: "목적지 약 0.1 km"·"경유지 2"는 코스 길이와 헷갈린다) 목적 이름만
        getString(R.string.course_caption_ready, c.chosen?.let { courseOptionLabel(it) } ?: "")
    }
    CoursePhase.ARRIVED -> getString(R.string.course_caption_arrived)
    CoursePhase.ERROR -> getString(R.string.course_caption_error)
}

/** "경로 계산 중…" / "주행 중 · 다음: 300 m 앞 우회전 (판교역로) · 제한속도 50". 주행 화면 한 줄에도 같은 문구를 쓴다. */
internal fun MainActivity.courseStatusLine(c: CourseUi): String {
    val parts = ArrayList<String>()
    parts += when (c.phase) {
        CoursePhase.ROUTING -> getString(R.string.course_status_routing)
        CoursePhase.ARRIVED -> getString(R.string.course_caption_arrived)
        else -> getString(R.string.course_status_driving)
    }
    if (c.phase == CoursePhase.DRIVING) {
        val m = c.tbtManeuver
        val d = c.tbtDistanceM
        parts += if (m != null && d != null) {
            val desc = c.tbtDescription?.takeIf { it.isNotBlank() }?.let { " ($it)" } ?: ""
            getString(R.string.course_tbt_line, d, maneuverLabel(m)) + desc
        } else getString(R.string.course_tbt_none)
    }
    c.speedLimit?.let { parts += getString(R.string.course_speed_limit, it) }
    return parts.joinToString(", ")
}

internal fun MainActivity.courseOptionLabel(id: CourseOptionId): String = getString(
    when (id) {
        CourseOptionId.NEIGHBORHOOD_RIGHT -> R.string.course_option_neighborhood_right
        CourseOptionId.CITY_LEFT -> R.string.course_option_city_left
        CourseOptionId.LEFT_TURN_DRILL -> R.string.course_option_left_turn_drill
        CourseOptionId.LANE_CHANGE -> R.string.course_option_lane_change
        CourseOptionId.MERGE -> R.string.course_option_merge
        CourseOptionId.STRAIGHT_ONLY -> R.string.course_option_straight_only
        CourseOptionId.NIGHT_RAIN -> R.string.course_option_night_rain
        CourseOptionId.EXAM_RETAKE -> R.string.course_option_exam_retake
    },
)

internal fun MainActivity.maneuverLabel(m: ManeuverType): String = getString(
    when (m) {
        ManeuverType.STRAIGHT -> R.string.course_maneuver_straight
        ManeuverType.LEFT -> R.string.course_maneuver_left
        ManeuverType.RIGHT -> R.string.course_maneuver_right
        ManeuverType.UTURN -> R.string.course_maneuver_uturn
        ManeuverType.UNPROTECTED_LEFT -> R.string.course_maneuver_unprotected_left
        ManeuverType.ROUNDABOUT -> R.string.course_maneuver_roundabout
        ManeuverType.HIGHWAY_ENTER -> R.string.course_maneuver_highway_enter
        ManeuverType.HIGHWAY_EXIT -> R.string.course_maneuver_highway_exit
        ManeuverType.MERGE -> R.string.course_maneuver_merge
        ManeuverType.TOLLGATE -> R.string.course_maneuver_tollgate
        ManeuverType.WAYPOINT -> R.string.course_maneuver_waypoint
        ManeuverType.DESTINATION -> R.string.course_maneuver_destination
        ManeuverType.OTHER -> R.string.course_maneuver_other
    },
)

/**
 * (11/14 베타 4차) 코스 모양 미리보기: 지도 없이 경로 선만(위가 북쪽). 출발점은 검은 점.
 * 위도·경도를 그 자리의 m 로 바꿔(경도는 cos(위도) 배) 칸에 맞춘다.
 */
@Composable
internal fun RouteShape(pts: List<LatLon>, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(CoachCornerRadius)
    Canvas(modifier = modifier.clip(shape).background(CoachColors.Surface, shape)) {
        val lat0 = pts.map { it.lat }.average()
        val k = kotlin.math.cos(Math.toRadians(lat0))
        val xs = pts.map { it.lon * k }; val ys = pts.map { it.lat }
        val minX = xs.min(); val maxX = xs.max(); val minY = ys.min(); val maxY = ys.max()
        val span = maxOf(maxX - minX, maxY - minY, 1e-6)
        val pad = 24f
        val sc = (minOf(size.width, size.height) - 2 * pad) / span
        val ox = (size.width - (maxX - minX) * sc) / 2f; val oy = (size.height - (maxY - minY) * sc) / 2f
        fun p(i: Int) = Offset((ox + (xs[i] - minX) * sc).toFloat(), (size.height - oy - (ys[i] - minY) * sc).toFloat())
        val path = Path().apply { moveTo(p(0).x, p(0).y); for (i in 1 until pts.size) lineTo(p(i).x, p(i).y) }
        drawPath(path, color = CoachColors.OnBackground, style = Stroke(width = 10f, cap = StrokeCap.Round, join = StrokeJoin.Round))
        drawPath(path, color = CoachColors.Primary, style = Stroke(width = 6f, cap = StrokeCap.Round, join = StrokeJoin.Round))
        drawCircle(color = CoachColors.OnBackground, radius = 12f, center = p(0))
    }
}

/** 다시 시도해도 같은 결과일 오류(조건에 맞는 코스 없음, 내비가 경로를 못 찾음, 생성기의 한국어 이유)면 true. */
internal fun courseErrorWantsOtherPurpose(code: String?): Boolean {
    val c = code.orEmpty()
    return c == "no_course" || c.startsWith("CAUSE_") || (c.any { it in '\uAC00'..'\uD7A3' } && !c.startsWith("네트워크"))
}

/** 코스 오류 코드 → 운전자에게 보일 문장. 생성기가 준 한국어 이유(조건에 맞는 코스 없음 등)는 그대로 보인다. */
internal fun MainActivity.courseErrorText(code: String?): String {
    val c = code.orEmpty()
    return when {
        c == "no_location" -> getString(R.string.course_err_location)
        c == "navi_unavailable" || c == "navi_not_ready" -> getString(R.string.course_err_navi_not_ready)
        c == "request_failed" -> getString(R.string.course_err_request)
        c == "no_course" -> getString(R.string.course_err_no_course)
        c.startsWith("CAUSE_") -> getString(R.string.course_err_navi_route)
        c.startsWith("HTTP") || c.startsWith("네트워크") -> getString(R.string.course_err_network)
        c.startsWith("course_generation") || c.isBlank() -> getString(R.string.course_err_other)
        c.any { it in '\uAC00'..'\uD7A3' } -> c
        else -> getString(R.string.course_err_other)
    }
}
