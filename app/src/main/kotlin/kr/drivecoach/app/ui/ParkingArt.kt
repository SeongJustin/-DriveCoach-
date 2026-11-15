// 담당: S
package kr.drivecoach.app.ui

import kr.drivecoach.app.R

/**
 * core/parking 의 `ParkingStep.illustration`("step03_gear_r_steer_right.svg" 같은 SVG 파일명)을
 * `res/drawable-nodpi/parking_<파일명에서 .svg 제거>.png` 리소스 ID 로 바꾼다.
 * 모르는 키는 null — 화면은 그림 자리를 비우고 문장만 보여 준다.
 */
object ParkingArt {

    private val byKey: Map<String, Int> = mapOf(
        "step01_approach.svg" to R.drawable.parking_step01_approach,
        "step02_steer_left_forward45.svg" to R.drawable.parking_step02_steer_left_forward45,
        "step03_gear_r_steer_right.svg" to R.drawable.parking_step03_gear_r_steer_right,
        "step04_reverse_in_mirror.svg" to R.drawable.parking_step04_reverse_in_mirror,
        "step05_center_wheel.svg" to R.drawable.parking_step05_center_wheel,
        "step06_straight_back.svg" to R.drawable.parking_step06_straight_back,
        "step07_gear_p_done.svg" to R.drawable.parking_step07_gear_p_done,
        "recover01_too_close.svg" to R.drawable.parking_recover01_too_close,
        "recover02_angled.svg" to R.drawable.parking_recover02_angled,
        "recover03_too_far.svg" to R.drawable.parking_recover03_too_far,
        // (11/15) 평행 주차 7장
        "par01_approach" to R.drawable.parking_par01_approach,
        "par02_gear_r_back" to R.drawable.parking_par02_gear_r_back,
        "par03_steer_right" to R.drawable.parking_par03_steer_right,
        "par04_reverse45" to R.drawable.parking_par04_reverse45,
        "par05_center_back" to R.drawable.parking_par05_center_back,
        "par06_steer_left_in" to R.drawable.parking_par06_steer_left_in,
        "par07_adjust_p" to R.drawable.parking_par07_adjust_p,
        // (11/15 밤) 자리 바로잡기 6가지: 위에서 본 설명 영상의 한 장면
        "rec_perp_close" to R.drawable.parking_rec_perp_close,
        "rec_perp_angled" to R.drawable.parking_rec_perp_angled,
        "rec_perp_far" to R.drawable.parking_rec_perp_far,
        "rec_par_close" to R.drawable.parking_rec_par_close,
        "rec_par_angled" to R.drawable.parking_rec_par_angled,
        "rec_par_far" to R.drawable.parking_rec_par_far,
    )

    /** 일러스트 키 → drawable. 없으면 null. */
    fun resIdFor(key: String?): Int? = key?.let { byKey[it] }

    /**
     * (11/14) 단계 영상: "step03_gear_r_steer_right.svg" → res/raw/parking_step03.mp4 의 리소스 ID. 없으면 0(그림만 보여 준다).
     * 영상은 빌드 도구 + video 로 만든다.
     */
    fun videoResFor(ctx: android.content.Context, key: String?): Int {
        // 직각 "stepNN_…" → parking_stepNN, 평행 "parNN_…" → parking_parNN
        if (key != null && key.startsWith("rec_")) return ctx.resources.getIdentifier("parking_$key", "raw", ctx.packageName)
        val m = Regex("^(step|par)(\\d\\d)_").find(key ?: return 0) ?: return 0
        return ctx.resources.getIdentifier("parking_" + m.groupValues[1] + m.groupValues[2], "raw", ctx.packageName)
    }

    /** 그림이 있는 키 전체(점검용). */
    val keys: Set<String> get() = byKey.keys
}
