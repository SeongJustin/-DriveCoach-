// 담당: A
// 주의: 이 환경에서는 Android 빌드가 불가하다. Pleos 주행 규제 가이드는 CarUxRestrictionsManager.registerListener /
//       onUxRestrictionsChanged / isRequiresDistractionOptimization / getActiveRestrictions 를 쓰라고만 명시한다.
package kr.drivecoach.app.vehicle

import android.car.Car
import android.car.drivingstate.CarUxRestrictions
import android.car.drivingstate.CarUxRestrictionsManager
import android.content.Context
import android.util.Log

/**
 * 플랫폼이 주는 주행 규제(CarUxRestrictions)를 구독한다.
 * core 의 DriveModeDetector(속도/기어 기반)와 별개로, 플랫폼 규제가 켜지면 화면을 한 줄 요약으로 강제한다(기획 0장 안전 원칙).
 * [onChanged] 는 (DO 필요 여부, 활성 제한 비트마스크) 를 받는다.
 */
class UxRestrictionsMonitor(context: Context, private val onChanged: (requiresDo: Boolean, activeRestrictions: Int) -> Unit) {
    private val appContext = context.applicationContext
    private var car: Car? = null
    private var manager: CarUxRestrictionsManager? = null

    private val listener = CarUxRestrictionsManager.OnUxRestrictionsChangedListener { r: CarUxRestrictions ->
        onChanged(r.isRequiresDistractionOptimization, r.activeRestrictions)
    }

    /** 구독 시작. 현재 값도 즉시 한 번 전달한다. */
    fun start() {
        try {
            val c = Car.createCar(appContext) ?: return
            car = c
            val m = c.getCarManager(Car.CAR_UX_RESTRICTION_SERVICE) as CarUxRestrictionsManager
            manager = m
            m.currentCarUxRestrictions?.let { onChanged(it.isRequiresDistractionOptimization, it.activeRestrictions) }
            m.registerListener(listener)
        } catch (e: Throwable) {
            Log.w(TAG, "CarUxRestrictions unavailable: ${e.message}")
        }
    }

    /** 구독 해제. */
    fun stop() {
        try { manager?.unregisterListener() } catch (e: Exception) { Log.w(TAG, "unregister failed: ${e.message}") }
        try { car?.disconnect() } catch (_: Exception) {}
        manager = null; car = null
    }

    companion object {
        private const val TAG = "UxRestrictionsMonitor"
        /** 예: 주행 중 긴 글 금지 플래그. 가이드 예시는 UX_RESTRICTIONS_NO_VIDEO. */
        const val NO_TEXT_MESSAGE = CarUxRestrictions.UX_RESTRICTIONS_NO_TEXT_MESSAGE
        const val LIMIT_STRING_LENGTH = CarUxRestrictions.UX_RESTRICTIONS_LIMIT_STRING_LENGTH
    }
}
