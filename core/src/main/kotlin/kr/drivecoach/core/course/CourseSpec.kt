// 담당: J (코스 자동 생성)
package kr.drivecoach.core.course

/** 코스 선택의 "가장 무서운 것". 코스 점수에 가중치를 준다. */
enum class Fear { PARKING, LANE_CHANGE, MERGE, LEFT_TURN, NIGHT_RAIN, HORN_PRESSURE, PEDAL_CONFUSION }

/**
 * 사용자가 고른 연습 조건(원하는 연습 + 난이도).
 *
 * @param fearFocus 가장 무서운 것. null 이면 가중치 없음.
 * @param leftTurns 원하는 좌회전 횟수 범위(비보호 포함).
 * @param laneChanges 원하는 차선 변경(추정치) 범위.
 * @param includeMerge 본선 합류(고속·자동차전용도로 진입 포함) 구간을 1개 이상 넣을지.
 * @param preferRightTurns "동네 한 바퀴(우회전 위주)": 우회전 가점, 좌회전 감점.
 * @param maxDistanceKm 코스 최대 길이(km). 넘으면 감점.
 * @param loop 출발지로 돌아오는 순환 코스인지.
 * @param difficulty 1(가장 쉬움)~5. 허용 혼잡도·복잡한 회전(비보호·회전교차로·고속도로) 허용 여부를 결정.
 * @param nightOrRain 야간·우천 연습. 난이도 옵션으로 취급해 코스 난이도 점수에 +1 하고, 혼잡 허용을 한 단계 낮춘다.
 */
data class CourseSpec(
    val fearFocus: Fear? = null,
    val leftTurns: IntRange = 0..99,
    val laneChanges: IntRange = 0..99,
    val includeMerge: Boolean = false,
    val preferRightTurns: Boolean = false,
    val maxDistanceKm: Double = 8.0,
    val loop: Boolean = true,
    val difficulty: Int = 2,
    val nightOrRain: Boolean = false,
    val targetMinutes: Int = 20,
) {
    init {
        require(targetMinutes in 5..90) { "targetMinutes 는 5..90" }
        require(maxDistanceKm.isFinite()) { "maxDistanceKm 는 유한수" }
        require(difficulty in 1..5) { "difficulty 는 1..5" }
        require(maxDistanceKm > 0) { "maxDistanceKm 는 양수" }
    }

    /** 야간·우천이면 실효 난이도를 한 단계 낮춰(보수적으로) 판단한다. 1 미만으로는 내려가지 않는다. */
    val effectiveDifficulty: Int get() = if (nightOrRain) (difficulty - 1).coerceAtLeast(1) else difficulty

    /** 실효 난이도에서 허용하는 최대 혼잡도(1 원활 / 2 서행 / 3 혼잡). */
    val allowedCongestion: Int get() = when (effectiveDifficulty) { 1, 2 -> 1; 3, 4 -> 2; else -> 3 }

    companion object {
        /** 코스 선택 프리셋: 동네 한 바퀴(우회전 위주). */
        fun neighborhoodLoop(difficulty: Int = 1) = CourseSpec(
            leftTurns = 0..1, preferRightTurns = true, maxDistanceKm = 4.0, loop = true, difficulty = difficulty,
        )

        /** 코스 선택 프리셋: 시내 입문(좌회전 1~2개). */
        fun cityIntro(difficulty: Int = 2) = CourseSpec(
            leftTurns = 1..2, maxDistanceKm = 6.0, loop = true, difficulty = difficulty,
        )

        /** 코스 선택 프리셋: 좌회전만 N번 도는 코스. */
        fun leftTurnDrill(count: Int = 3, difficulty: Int = 2) = CourseSpec(
            fearFocus = Fear.LEFT_TURN, leftTurns = count..count, maxDistanceKm = 6.0, loop = true, difficulty = difficulty,
        )

        /** 코스 선택 프리셋: 합류 한 구간. */
        fun mergeDrill(difficulty: Int = 3) = CourseSpec(
            fearFocus = Fear.MERGE, includeMerge = true, maxDistanceKm = 15.0, loop = true, difficulty = difficulty,
        )

        /** 코스 선택 프리셋: 차선 변경 M번. */
        fun laneChangeDrill(count: Int = 3, difficulty: Int = 2) = CourseSpec(
            fearFocus = Fear.LANE_CHANGE, laneChanges = count..(count + 1), maxDistanceKm = 8.0, loop = true, difficulty = difficulty,
        )
    }
}
