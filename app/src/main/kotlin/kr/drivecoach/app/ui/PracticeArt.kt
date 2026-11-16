// 차량 조작법 연습 항목 52개 ↔ 그림 drawable 매핑
package kr.drivecoach.app.ui

import kr.drivecoach.app.R

/**
 * 연습 항목 ID([kr.drivecoach.core.practice.Chapter2Items] 의 id, 예: "2-6-turn-signal-right") 를
 * `res/drawable-nodpi/practice_<id 의 하이픈→밑줄>`(.webp/.png) 리소스 ID 로 바꾼다.
 *
 * .webp 47개 항목은 현대자동차 아반떼(CN8) 2027 취급설명서 그림(경고등 3색은 설명서 그림에 색 설명을 덧붙임),
 * .png 5개 항목(사각지대·안전벨트 틀린 예·페달·안개등·뒷유리 와이퍼)은 우리 그림. 출처는 docs/manual_figs/출처.md.
 * 그림 크기: 800x500.
 */
object PracticeArt {

    private val byId: Map<String, Int> = mapOf(
        "2-1-seat-fore-aft" to R.drawable.practice_2_1_seat_fore_aft,
        "2-1-seat-backrest" to R.drawable.practice_2_1_seat_backrest,
        "2-1-seat-height" to R.drawable.practice_2_1_seat_height,
        "2-1-steering-tilt" to R.drawable.practice_2_1_steering_tilt,
        "2-1-steering-telescopic" to R.drawable.practice_2_1_steering_telescopic,
        "2-1-posture" to R.drawable.practice_2_1_posture,
        "2-1-headrest" to R.drawable.practice_2_1_headrest,
        "2-1-side-mirror" to R.drawable.practice_2_1_side_mirror,
        "2-1-room-mirror" to R.drawable.practice_2_1_room_mirror,
        "2-1-blind-spot" to R.drawable.practice_2_1_blind_spot,
        "2-2-seatbelt" to R.drawable.practice_2_2_seatbelt,
        "2-2-seatbelt-wrong-examples" to R.drawable.practice_2_2_seatbelt_wrong_examples,
        "2-3-ignition" to R.drawable.practice_2_3_ignition,
        "2-3-epb-release" to R.drawable.practice_2_3_epb_release,
        "2-3-epb-apply" to R.drawable.practice_2_3_epb_apply,
        "2-3-autohold-on" to R.drawable.practice_2_3_autohold_on,
        "2-3-autohold-off" to R.drawable.practice_2_3_autohold_off,
        "2-4-gear-sequence" to R.drawable.practice_2_4_gear_sequence,
        "2-4-gear-meaning" to R.drawable.practice_2_4_gear_meaning,
        "2-4-neutral-use" to R.drawable.practice_2_4_neutral_use,
        "2-4-pedal-position" to R.drawable.practice_2_4_pedal_position,
        "2-4-sudden-accel" to R.drawable.practice_2_4_sudden_accel,
        "2-5-headlight-auto" to R.drawable.practice_2_5_headlight_auto,
        "2-5-headlight-low" to R.drawable.practice_2_5_headlight_low,
        "2-5-headlight-high" to R.drawable.practice_2_5_headlight_high,
        "2-5-passing" to R.drawable.practice_2_5_passing,
        "2-5-fog-light" to R.drawable.practice_2_5_fog_light,
        "2-5-hazard" to R.drawable.practice_2_5_hazard,
        "2-5-horn" to R.drawable.practice_2_5_horn,
        "2-5-warning-triangle" to R.drawable.practice_2_5_warning_triangle,
        "2-5-interior-light" to R.drawable.practice_2_5_interior_light,
        "2-6-wiper" to R.drawable.practice_2_6_wiper,
        "2-6-washer" to R.drawable.practice_2_6_washer,
        "2-6-rear-wiper" to R.drawable.practice_2_6_rear_wiper,
        "2-6-turn-signal-right" to R.drawable.practice_2_6_turn_signal_right,
        "2-6-turn-signal-left" to R.drawable.practice_2_6_turn_signal_left,
        "2-6-lane-change-tap" to R.drawable.practice_2_6_lane_change_tap,
        "2-7-window" to R.drawable.practice_2_7_window,
        "2-7-window-lock" to R.drawable.practice_2_7_window_lock,
        "2-7-door-lock" to R.drawable.practice_2_7_door_lock,
        "2-7-hvac" to R.drawable.practice_2_7_hvac,
        "2-7-defog" to R.drawable.practice_2_7_defog,
        "2-7-air-recirculation" to R.drawable.practice_2_7_air_recirculation,
        "2-8-speedometer" to R.drawable.practice_2_8_speedometer,
        "2-8-rpm" to R.drawable.practice_2_8_rpm,
        "2-8-fuel" to R.drawable.practice_2_8_fuel,
        "2-8-trip" to R.drawable.practice_2_8_trip,
        "2-8-warning-lamp-colors" to R.drawable.practice_2_8_warning_lamp_colors,
        "2-9-lka" to R.drawable.practice_2_9_lka,
        "2-9-fca" to R.drawable.practice_2_9_fca,
        "2-9-bcw" to R.drawable.practice_2_9_bcw,
        "2-9-cruise" to R.drawable.practice_2_9_cruise,
    )

    /** 모르는 ID 는 null. 화면은 null 이면 그림 자리를 비우거나 제목만 보여 주면 된다. */
    fun resIdFor(itemId: String): Int? = byId[itemId]

    /** 그림이 있는 항목 ID 전체(테스트·점검용). */
    val itemIds: Set<String> get() = byId.keys
}
