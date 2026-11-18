// CarInfo 도메인 → core.signal.CarSpec. 실패하면 아반떼 CN8 기본값
// Vehicle 2.3.2 기준. checkVINInfoCapabilities 의 onComplete 는 인자 하나((CapabilityConfig)->Unit).
package kr.drivecoach.app.vehicle

import ai.pleos.playground.vehicle.Vehicle
import ai.pleos.playground.vehicle.api.model.capability.AccessType
import ai.pleos.playground.vehicle.api.model.capability.CapabilityConfig
import android.car.Car
import android.car.VehiclePropertyIds
import android.car.hardware.property.CarPropertyManager
import android.content.Context
import android.util.Log
import kr.drivecoach.core.signal.CarSpec

/**
 * 시작 시 차종을 알아내 [CarSpec] 을 만든다(첫 화면에 차종 이름을 보여 준다).
 *
 * javap 확인 결과 Pleos CarInfo 도메인의 공개 API 는 VIN/CRN 조회뿐이다
 * (checkVINInfoCapabilities / getVINInfo / checkCRNInfoCapabilities / getCRNInfo). getCapabilityInfo 는 PRIVATE_CAR_INFO 권한(비공개).
 * 차체 크기(길이·폭·축거) API 는 없다.
 * 따라서 제원(길이·폭·축거·조향비)은 차종 식별 → 내장 테이블로 정한다:
 *  1) AAOS INFO_MODEL / INFO_MAKE / INFO_MODEL_YEAR (android.car.permission.CAR_INFO, normal) 로 모델명을 읽는다.
 *  2) 실패하면 Pleos getVINInfo 의 VIN 앞 3자리(WMI)·VDS 로 현대 차종을 추정한다.   // TODO(미확인) VIN 디코딩 규칙
 *  3) 모두 실패하면 [CarSpec.AVANTE_CN8].
 *
 * 결과는 비동기 콜백 [onLoaded] 로 한 번만 전달된다. 항상 호출되며(실패해도 기본값), 메인 스레드 보장은 하지 않는다.
 */
class CarSpecLoader(private val context: Context, private val vehicle: Vehicle) {

    /**
     * 알려진 차종 제원 테이블. 모델명 키워드(소문자) → CarSpec.
     * 전장·전폭·축거·윤거: 현대닷컴 디지털 카탈로그(2026-11). 오버행: 측면도 비율 추정(그랜저만 카탈로그 값).
     * 최소회전반경: 차급 일반값(추정). 조향비: 핸들 480° 와 최소회전반경에서 역산.
     */
    private val knownSpecs: List<Pair<List<String>, CarSpec>> = listOf(
        listOf("avante", "아반떼", "elantra", "cn7", "cn8") to CarSpec.AVANTE_CN8,
        listOf("sonata", "쏘나타", "dn8") to CarSpec(
            "쏘나타 디 엣지", lengthM = 4.91, widthM = 1.86, wheelbaseM = 2.84, maxSteeringWheelDeg = 480.0, steeringRatio = 13.31,
            frontOverhangM = 1.005, rearOverhangM = 1.065, trackFrontM = 1.618, trackRearM = 1.625, minTurningRadiusM = 5.5,
        ),
        listOf("grandeur", "그랜저", "gn7") to CarSpec(
            "그랜저 GN7", lengthM = 5.05, widthM = 1.88, wheelbaseM = 2.895, maxSteeringWheelDeg = 480.0, steeringRatio = 14.21,
            frontOverhangM = 0.985, rearOverhangM = 1.17, trackFrontM = 1.624, trackRearM = 1.631, minTurningRadiusM = 5.9,
        ),
        listOf("tucson", "투싼", "nx4", "nx5") to CarSpec(
            "투싼 NX5", lengthM = 4.70, widthM = 1.905, wheelbaseM = 2.785, maxSteeringWheelDeg = 480.0, steeringRatio = 13.57,
            frontOverhangM = 0.89, rearOverhangM = 1.025, trackFrontM = 1.639, trackRearM = 1.646, minTurningRadiusM = 5.5,
        ),
        listOf("kona", "코나", "sx2") to CarSpec(
            "코나 SX2", lengthM = 4.35, widthM = 1.825, wheelbaseM = 2.66, maxSteeringWheelDeg = 480.0, steeringRatio = 13.69,
            frontOverhangM = 0.805, rearOverhangM = 0.885, trackFrontM = 1.589, trackRearM = 1.598, minTurningRadiusM = 5.3,
        ),
        listOf("santa fe", "santafe", "싼타페", "mx5") to CarSpec(
            "싼타페 MX5", lengthM = 4.83, widthM = 1.90, wheelbaseM = 2.815, maxSteeringWheelDeg = 480.0, steeringRatio = 14.32,
            frontOverhangM = 0.915, rearOverhangM = 1.10, trackFrontM = 1.643, trackRearM = 1.653, minTurningRadiusM = 5.8,
        ),
    )

    /** 차종을 읽어 [onLoaded] 에 CarSpec 을 넘긴다. 실패 시 [CarSpec.AVANTE_CN8]. */
    fun load(onLoaded: (CarSpec) -> Unit) {
        val fromAaos = runCatching { readModelFromCarPropertyManager() }.getOrNull()
        val spec = fromAaos?.let { match(it) }
        if (spec != null) { onLoaded(spec.copy(detected = true)); return }

        // Pleos VIN 경로 (비동기). 권한: pleos.car.permission.CAR_INFO
        // javap: checkVINInfoCapabilities(Function1<CapabilityConfig, Unit>, Function1<Exception, Unit>), getVINInfo(Function1<String?, Unit>, Function1<Exception, Unit>)
        try {
            val carInfo = vehicle.getCarInfo()
            carInfo.checkVINInfoCapabilities(
                { config: CapabilityConfig ->
                    if (config.accessType == AccessType.READ || config.accessType == AccessType.READ_WRITE) {
                        carInfo.getVINInfo(
                            { vin: String? -> onLoaded(vin?.let { specFromVin(it) } ?: CarSpec.AVANTE_CN8) },
                            { e: Exception -> Log.w(TAG, "getVINInfo failed: ${e.message}"); onLoaded(CarSpec.AVANTE_CN8) }
                        )
                    } else onLoaded(CarSpec.AVANTE_CN8)
                },
                { e: Exception -> Log.w(TAG, "checkVINInfoCapabilities failed: ${e.message}"); onLoaded(CarSpec.AVANTE_CN8) }
            )
        } catch (e: Exception) {
            Log.w(TAG, "CarInfo unavailable: ${e.message}")
            onLoaded(CarSpec.AVANTE_CN8)
        }
    }

    /** AAOS 정적 속성에서 "제조사 모델명" 문자열. 권한 android.car.permission.CAR_INFO (normal). */
    private fun readModelFromCarPropertyManager(): String? {
        val car = Car.createCar(context) ?: return null
        try {
            val pm = car.getCarManager(Car.PROPERTY_SERVICE) as CarPropertyManager
            val make = runCatching { pm.getProperty(String::class.java, VehiclePropertyIds.INFO_MAKE, 0).value }.getOrNull()
            val model = runCatching { pm.getProperty(String::class.java, VehiclePropertyIds.INFO_MODEL, 0).value }.getOrNull()
            return listOfNotNull(make, model).joinToString(" ").ifBlank { null }
        } finally {
            car.disconnect()
        }
    }

    private fun match(modelText: String): CarSpec? {
        val t = modelText.lowercase()
        return knownSpecs.firstOrNull { (keys, _) -> keys.any { t.contains(it) } }?.second
    }

    /** VIN 으로 차종 추정. 현대차 WMI 는 KMH(국내 생산). VDS 모델 코드 규칙은 미확인이라 기본값만 돌려준다. */
    private fun specFromVin(vin: String): CarSpec {
        // TODO(미확인) 현대 VIN 4~8자리 → 차종 코드 매핑. 확인되면 knownSpecs 와 연결한다.
        Log.i(TAG, "VIN prefix=${vin.take(3)} (model decoding not implemented)")
        return CarSpec.AVANTE_CN8
    }

    companion object { private const val TAG = "CarSpecLoader" }
}
