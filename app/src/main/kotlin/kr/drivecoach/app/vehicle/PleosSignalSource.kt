// 담당: A+B → R
// 검증: Vehicle-2.3.2 / FusedLocation-1.1.3 jar 의 시그니처에 맞췄다. 값의 의미(단위·부호·0=닫힘)는 에뮬레이터에서 확인할 것.
package kr.drivecoach.app.vehicle

import ai.pleos.playground.location.fused.FusedLocation
import ai.pleos.playground.location.fused.listener.FusedLocationListener
import ai.pleos.playground.location.fused.model.LocationData
import ai.pleos.playground.vehicle.Vehicle
import ai.pleos.playground.vehicle.api.model.area.DoorArea
import ai.pleos.playground.vehicle.api.model.area.SeatArea
import ai.pleos.playground.vehicle.api.model.area.WindowArea
import ai.pleos.playground.vehicle.api.model.capability.AreaBounds
import ai.pleos.playground.vehicle.api.model.capability.CapabilityConfig
import ai.pleos.playground.vehicle.constant.area.DoorValue
import ai.pleos.playground.vehicle.constant.area.SeatValue
import ai.pleos.playground.vehicle.constant.area.TurnSignalValue
import ai.pleos.playground.vehicle.constant.area.WindowValue
import ai.pleos.playground.vehicle.constant.control.LightState
import ai.pleos.playground.vehicle.constant.control.LightSwitch
import ai.pleos.playground.vehicle.constant.control.OilLevelState
import ai.pleos.playground.vehicle.constant.control.VehicleGear as PleosGear
import ai.pleos.playground.vehicle.constant.control.WiperState
import ai.pleos.playground.vehicle.constant.control.WiperSwitch
import ai.pleos.playground.vehicle.listener.BrakePedalStatusListener
import ai.pleos.playground.vehicle.listener.DistanceDrivenListener
import ai.pleos.playground.vehicle.listener.DoorLockStateListener
import ai.pleos.playground.vehicle.listener.FuelPercentListener
import ai.pleos.playground.vehicle.listener.GearSelectionListener
import ai.pleos.playground.vehicle.listener.HvacPowerStateListener
import ai.pleos.playground.vehicle.listener.LightStateListener
import ai.pleos.playground.vehicle.listener.OilLevelStateListener
import ai.pleos.playground.vehicle.listener.SeatBackrestAnglePositionListener
import ai.pleos.playground.vehicle.listener.SeatBeltNotBuckledWarningListener
import ai.pleos.playground.vehicle.listener.SeatForeAftPositionListener
import ai.pleos.playground.vehicle.listener.SeatHeightPositionListener
import ai.pleos.playground.vehicle.listener.SteeringWheelAngleListener
import ai.pleos.playground.vehicle.listener.TurnSignalStateListener
import ai.pleos.playground.vehicle.listener.WindowPositionListener
import ai.pleos.playground.vehicle.listener.WiperStateListener
import android.car.Car
import android.car.VehicleGear
import android.car.VehicleIgnitionState
import android.car.VehiclePropertyIds
import android.car.hardware.CarPropertyValue
import android.car.hardware.property.CarPropertyManager
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.Headlight
import kr.drivecoach.core.signal.SignalSource
import kr.drivecoach.core.signal.StateListener
import kr.drivecoach.core.signal.TurnSignal
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.signal.Wiper
import java.util.concurrent.CopyOnWriteArrayList

/**
 * 차량 신호를 한 곳에 모아 [VehicleState] 스냅샷으로 만들어 core 에 넘기는 어댑터.
 *
 * 신호 출처:
 *  - Pleos Vehicle SDK 2.3.2 (1순위): Gear(기어), SteeringWheel(조향각), Brake(브레이크 페달), TurnSignal, Light(하향/상향 + AUTO 스위치),
 *    Wiper(앞유리), Window(4창 위치), Door(운전석 잠금), HVAC(운전석 전원), Seat(앞뒤·등받이·높이), Safety(안전벨트 경고·엔진오일·브레이크액),
 *    Odometer(누적 주행거리), Fuel(연료 %)
 *  - AAOS CarPropertyManager: PERF_VEHICLE_SPEED, PARKING_BRAKE_ON, IGNITION_STATE
 *    + 대체(fallback): GEAR_SELECTION(Pleos Gear 값이 아직 없을 때)
 *    (ENGINE_RPM·PERF_STEERING_ANGLE 은 signature|privileged 권한이라 공모전 규정상 쓰지 않는다 — 11/5 사무국 답변. rpm 은 항상 null)
 *  - Pleos FusedLocation SDK: lat/lon
 *
 * Vehicle SDK 2.3.2 에는 시동(IgnitionState enum 만 있고 API 없음)·주차브레이크·속도·RPM API 가 없어 이 넷은 AAOS 만 쓴다.
 * 경고등 전체(Telltales) 는 PRIVATE_CAR_SAFETY 권한이라 쓰지 않고 Safety 공개 항목으로 SEAT_BELT / OIL_LEVEL / BRAKE / LOW_FUEL 만 만든다.
 *
 * 스레드: SDK 콜백은 임의 스레드에서 올 수 있으므로 모든 갱신을 메인 Handler 로 직렬화한다.
 * 사용: `start()` → (리스너 등록) → `stop()` (Activity onDestroy). 모든 SDK 호출은 try/catch 로 감싸 권한·미지원 차량에서 죽지 않게 한다.
 */
class PleosSignalSource(context: Context) : SignalSource {
    private val appContext: Context = context.applicationContext
    private val main = Handler(Looper.getMainLooper())
    private val listeners = CopyOnWriteArrayList<StateListener>()

    @Volatile
    override var current: VehicleState = VehicleState(timestampMs = System.currentTimeMillis())
        private set

    private val _stateFlow = MutableStateFlow(current)
    /** Compose/코루틴용 Flow 래퍼. core 는 [addListener] 를 쓴다. */
    val stateFlow: StateFlow<VehicleState> = _stateFlow.asStateFlow()

    /** 진단용: 어떤 SDK 호출이 성공/실패했는지 (에뮬레이터 확인 단계에서 화면에 표시). */
    private val _diag = MutableStateFlow<List<String>>(emptyList())
    val diag: StateFlow<List<String>> = _diag.asStateFlow()
    private fun diagAdd(line: String) {
        val next = (_diag.value + line).takeLast(40)
        _diag.value = next
        Log.i(TAG, "DIAG $line")
    }

    override fun addListener(listener: StateListener) { listeners += listener }
    override fun removeListener(listener: StateListener) { listeners -= listener }

    // ---- SDK 핸들 ----
    /** Vehicle SDK 진입점. 한 앱에 하나만 두고 CarSpecLoader 등이 공유한다. (javap: Vehicle(Context) / initialize() / release()) */
    val vehicle: Vehicle = Vehicle(appContext)
    private var fusedLocation: FusedLocation? = null
    private var car: Car? = null
    private var props: CarPropertyManager? = null
    private var started = false
    private val unregisterActions = ArrayList<() -> Unit>()

    // Pleos 값이 한 번이라도 오면 AAOS 대체 신호는 무시한다
    @Volatile private var gearFromPleos = false
    @Volatile private var steeringFromPleos = false

    // 시트 위치는 "변화 이벤트"로만 쓰므로 마지막 값을 따로 기억한다
    private var lastSeatForeAft: Int? = null
    private var lastSeatBackrest: Int? = null
    private var lastSeatHeight: Int? = null
    // 창문은 네 창이 모두 닫혀야 windowsClosed=true
    private val windowPositions = HashMap<WindowValue, Int?>()
    private var lowBeam: LightState? = null
    private var highBeam: LightState? = null
    private var lowBeamSwitch: LightSwitch? = null
    private val lamps = HashSet<String>()

    /** 구독에 성공한 Pleos 기능 이름(설정 화면 "Pleos 연결" 표시용, 11/6). 진단 줄은 40줄만 남아 여기 따로 모은다. */
    private val _connected = MutableStateFlow<Set<String>>(emptySet())
    val connected: StateFlow<Set<String>> = _connected.asStateFlow()

    /** SDK 초기화 + 모든 리스너 등록. 중복 호출은 무시. */
    fun start() {
        if (started) return
        started = true
        safely("Vehicle.initialize") { vehicle.initialize() }
        registerVehicleSdkListeners()
        startCarPropertyManager()
        startFusedLocation()
    }

    /** 리스너 해제 + SDK release. Activity onDestroy 에서 호출. */
    fun stop() {
        if (!started) return
        started = false
        _connected.value = emptySet()
        unregisterActions.forEach { a -> safely("unregister") { a() } }
        unregisterActions.clear()
        safely("Car.disconnect") { car?.disconnect() }
        car = null; props = null
        safely("FusedLocation.release") { fusedLocation?.release() }
        fusedLocation = null
        safely("Vehicle.release") { vehicle.release() }   // 문서: 모든 리스너를 unregister 한 뒤 release
        gearFromPleos = false; steeringFromPleos = false
    }

    // ------------------------------------------------------------------
    // 1) Pleos Vehicle SDK — 리스너 인터페이스/콜백 이름은 전부 javap 로 확인
    // ------------------------------------------------------------------
    private fun registerVehicleSdkListeners() {
        // 기어 (Gear, CAR_DRIVE): getCurrentGearSelectionState((VehicleGear?)->Unit, (Exception)->Unit), registerGearSelection(GearSelectionListener)
        //   주의: 2.3.2 에서 세 메서드 모두 @Deprecated("use the Google GEAR_SELECTION property") — 아직 동작하므로 1순위로 쓰되,
        //   AAOS GEAR_SELECTION 을 항상 함께 구독해 Pleos 값이 안 오면 그쪽을 쓴다 (Pleos 가 AAOS 속성 사용을 권장 = 서드파티에 열려 있을 가능성 높음).
        @Suppress("DEPRECATION")
        safely("Gear") {
            val gear = vehicle.getGear()
            gear.getCurrentGearSelectionState(
                { g: PleosGear? -> onPleosGear(g) },
                { e: Exception -> warn("Gear.getCurrent", e) }
            )
            val l = object : GearSelectionListener {
                override fun onSelected(gear: PleosGear?) = onPleosGear(gear)
                override fun onFailed(e: Exception) = warn("Gear", e)
            }
            gear.registerGearSelection(l)
            unregisterActions += { gear.unregisterGearSelection(l) }
        }

        // 조향각 (SteeringWheel, CAR_DRIVE): checkSteeringWheelAngleCapabilities((CapabilityConfig, AreaBounds<Float>)->Unit, ...),
        //   getCurrentSteeringWheelAngle((Float?)->Unit, ...), registerSteeringWheelAngle(SteeringWheelAngleListener)
        safely("SteeringWheel") {
            val sw = vehicle.getSteeringWheel()
            sw.checkSteeringWheelAngleCapabilities(
                { config: CapabilityConfig, bounds: AreaBounds<Float> ->
                    Log.i(TAG, "SteeringWheelAngle access=${config.accessType} range=${bounds.minValue}..${bounds.maxValue}")
                },
                { e: Exception -> warn("SteeringWheel.check", e) }
            )
            sw.getCurrentSteeringWheelAngle(
                { deg: Float? -> onPleosSteering(deg) },
                { e: Exception -> warn("SteeringWheel.getCurrent", e) }
            )
            val l = object : SteeringWheelAngleListener {
                override fun onSteeringWheelAngleUpdated(angle: Float?) = onPleosSteering(angle)
                override fun onFailed(e: Exception) = warn("SteeringWheel", e)
            }
            sw.registerSteeringWheelAngle(l)
            unregisterActions += { sw.unregisterSteeringWheelAngle(l) }
        }

        // 브레이크 페달 (Brake, CAR_DRIVE): registerBrakePedalStatus(BrakePedalStatusListener{ onBrakePedalStatusUpdated(Boolean?) })
        safely("Brake") {
            val brake = vehicle.getBrake()
            brake.getCurrentBrakePedalStatus(
                { pressed: Boolean? -> update { it.copy(brakePressed = pressed) } },
                { e: Exception -> warn("Brake.getCurrent", e) }
            )
            val l = object : BrakePedalStatusListener {
                override fun onBrakePedalStatusUpdated(status: Boolean?) = update { it.copy(brakePressed = status) }
                override fun onFailed(e: Exception) = warn("Brake", e)
            }
            brake.registerBrakePedalStatus(l)
            unregisterActions += { brake.unregisterBrakePedalStatus(l) }
        }

        // 방향지시등 (TurnSignal, CAR_LIGHTS): TurnSignalStateListener{ onTurnSignalStateUpdated(TurnSignalValue?) }, 값 OFF/RIGHT/LEFT (HAZARD 없음 → hazardOn 은 모름)
        safely("TurnSignal") {
            val ts = vehicle.getTurnSignal()
            ts.getCurrentTurnSignalState(
                { v: TurnSignalValue? -> update { it.copy(turnSignal = mapTurnSignal(v)) } },
                { e: Exception -> warn("TurnSignal.getCurrent", e) }
            )
            val l = object : TurnSignalStateListener {
                override fun onTurnSignalStateUpdated(state: TurnSignalValue?) = update { it.copy(turnSignal = mapTurnSignal(state)) }
                override fun onFailed(e: Exception) = warn("TurnSignal", e)
            }
            ts.registerTurnSignalState(l)
            unregisterActions += { ts.unregisterTurnSignalState(l) }
        }

        // 전조등 (Light, CAR_LIGHTS): registerLowBeamLightState / registerHighBeamLightState(LightStateListener{ onLightStateUpdated(LightState?) })
        //   LightState { OFF, ON, DAYTIME_RUNNING }. AUTO 는 상태가 아니라 스위치 → getLowBeamLightSwitch((LightSwitch?)->Unit, ...) 로 함께 읽는다.
        safely("Light") {
            val light = vehicle.getLight()
            val low = object : LightStateListener {
                override fun onLightStateUpdated(state: LightState?) { lowBeam = state; refreshLowBeamSwitch(light) }
                override fun onFailed(e: Exception) = warn("LowBeam", e)
            }
            val high = object : LightStateListener {
                override fun onLightStateUpdated(state: LightState?) { highBeam = state; update { it.copy(headlight = headlight()) } }
                override fun onFailed(e: Exception) = warn("HighBeam", e)
            }
            light.getCurrentLowBeamLightState({ s: LightState? -> low.onLightStateUpdated(s) }, { e: Exception -> warn("LowBeam.getCurrent", e) })
            // 스위치(OFF/AUTO/…)에는 리스너가 없어 하향등 상태가 안 바뀌면(낮에 OFF→AUTO) AUTO 를 못 본다 → 2초마다 스위치를 다시 읽는다(베타 A-06/D-46).
            val poll = object : Runnable {
                override fun run() {
                    if (!started) return
                    refreshLowBeamSwitch(light)
                    main.postDelayed(this, LIGHT_SWITCH_POLL_MS)
                }
            }
            main.postDelayed(poll, LIGHT_SWITCH_POLL_MS)
            unregisterActions += { main.removeCallbacks(poll) }
            light.getCurrentHighBeamLightState({ s: LightState? -> high.onLightStateUpdated(s) }, { e: Exception -> warn("HighBeam.getCurrent", e) })
            light.registerLowBeamLightState(low)
            light.registerHighBeamLightState(high)
            unregisterActions += { light.unregisterLowBeamLightState(low); light.unregisterHighBeamLightState(high) }
        }

        // 와이퍼 (Wiper, CAR_WINDOWS): registerWiperState(WindowArea, WiperStateListener{ onWiperStateUpdated(WiperState?) })
        //   WiperState { OTHER, OFF, ON, SERVICE } 는 켜짐/꺼짐만 → 단계는 getCurrentWiperSwitch(WindowArea, (WiperSwitch?)->Unit, ...) 로 읽는다.
        safely("Wiper") {
            val wiper = vehicle.getWiper()
            val area = WindowArea(WindowValue.FRONT_WINDSHIELD)
            val l = object : WiperStateListener {
                override fun onWiperStateUpdated(state: WiperState?) {
                    if (state == null || state == WiperState.OFF) { update { it.copy(wiper = if (state == null) Wiper.UNKNOWN else Wiper.OFF) }; return }
                    // ON 이면 스위치 단계를 읽어 INT/LOW/HIGH 로 세분화. 실패하면 LOW 로 둔다.
                    wiper.getCurrentWiperSwitch(
                        area,
                        { sw: WiperSwitch? -> update { it.copy(wiper = mapWiper(state, sw)) } },
                        { e: Exception -> warn("Wiper.getSwitch", e); update { it.copy(wiper = mapWiper(state, null)) } }
                    )
                }
                override fun onFailed(e: Exception) = warn("Wiper", e)
            }
            wiper.getCurrentWiperState(area, { s: WiperState? -> l.onWiperStateUpdated(s) }, { e: Exception -> warn("Wiper.getCurrent", e) })
            wiper.registerWiperState(area, l)
            unregisterActions += { wiper.unregisterWiperState(area, l) }
        }

        // 창문 (Window, CAR_WINDOWS): registerWindowPosition(WindowArea, WindowPositionListener{ onWindowPositionUpdated(Int?) }) — 창마다 등록
        safely("Window") {
            val window = vehicle.getWindow()
            for (v in WINDOWS) {
                val area = WindowArea(v)
                val l = object : WindowPositionListener {
                    // TODO(미확인) position 의미: 0 = 완전히 닫힘 으로 가정 (AAOS WINDOW_POS 규약). checkWindowPositionCapabilities 의 AreaBounds 로 실차에서 확인.
                    override fun onWindowPositionUpdated(position: Int?) { windowPositions[v] = position; update { it.copy(windowsClosed = allWindowsClosed()) } }
                    override fun onFailed(e: Exception) = warn("Window $v", e)
                }
                window.getCurrentWindowPosition(area, { p: Int? -> l.onWindowPositionUpdated(p) }, { e: Exception -> warn("Window.getCurrent $v", e) })
                window.registerWindowPosition(area, l)
                unregisterActions += { window.unregisterWindowPosition(area, l) }
            }
        }

        // 도어 잠금 (Door, CAR_DOORS): registerDoorLockState(DoorArea, DoorLockStateListener{ onDoorLockStateUpdated(Boolean?) }) — 운전석 기준
        safely("Door") {
            val door = vehicle.getDoor()
            val area = DoorArea(DoorValue.ROW_1_LEFT)
            val l = object : DoorLockStateListener {
                override fun onDoorLockStateUpdated(locked: Boolean?) = update { it.copy(doorsLocked = locked) }
                override fun onFailed(e: Exception) = warn("DoorLock", e)
            }
            door.getDoorLockState(area, { locked: Boolean? -> l.onDoorLockStateUpdated(locked) }, { e: Exception -> warn("DoorLock.getCurrent", e) })
            door.registerDoorLockState(area, l)
            unregisterActions += { door.unregisterDoorLockState(area, l) }
        }

        // 공조 전원 (HVAC, CAR_CLIMATE): registerHvacPowerState(SeatArea, HvacPowerStateListener{ onHvacPowerStateUpdated(Boolean?) }) — 운전석 영역
        safely("HVAC") {
            val hvac = vehicle.getHVAC()
            val area = SeatArea(SeatValue.ROW_1_LEFT)
            val l = object : HvacPowerStateListener {
                override fun onHvacPowerStateUpdated(on: Boolean?) = update { it.copy(hvacOn = on) }
                override fun onFailed(e: Exception) = warn("HVAC", e)
            }
            hvac.getHvacPowerState(area, { on: Boolean? -> l.onHvacPowerStateUpdated(on) }, { e: Exception -> warn("HVAC.getCurrent", e) })
            hvac.registerHvacPowerState(area, l)
            unregisterActions += { hvac.unregisterHvacPowerState(area, l) }
        }

        // 시트 (Seat, CAR_SEATS): register{SeatForeAft|SeatBackrestAngle|SeatHeight}Position(SeatArea, XxxListener{ onXxxPositionUpdated(Int?) })
        safely("Seat") {
            val seat = vehicle.getSeat()
            val area = SeatArea(SeatValue.ROW_1_LEFT)
            val fa = object : SeatForeAftPositionListener {
                override fun onSeatForeAftPositionUpdated(position: Int?) = seatMoved(position, lastSeatForeAft) { lastSeatForeAft = position }
                override fun onFailed(e: Exception) = warn("SeatForeAft", e)
            }
            val br = object : SeatBackrestAnglePositionListener {
                override fun onSeatBackrestAnglePositionUpdated(position: Int?) = seatMoved(position, lastSeatBackrest) { lastSeatBackrest = position }
                override fun onFailed(e: Exception) = warn("SeatBackrest", e)
            }
            val hg = object : SeatHeightPositionListener {
                override fun onSeatHeightPositionUpdated(position: Int?) = seatMoved(position, lastSeatHeight) { lastSeatHeight = position }
                override fun onFailed(e: Exception) = warn("SeatHeight", e)
            }
            // 초기값을 먼저 읽어 두어야 첫 콜백이 "변화"로 오인되지 않는다
            seat.getCurrentSeatForeAftPosition(area, { p: Int? -> lastSeatForeAft = p }, { e: Exception -> warn("SeatForeAft.getCurrent", e) })
            seat.getCurrentSeatBackrestAnglePosition(area, { p: Int? -> lastSeatBackrest = p }, { e: Exception -> warn("SeatBackrest.getCurrent", e) })
            seat.getCurrentSeatHeightPosition(area, { p: Int? -> lastSeatHeight = p }, { e: Exception -> warn("SeatHeight.getCurrent", e) })
            seat.registerSeatForeAftPosition(area, fa)
            seat.registerSeatBackrestAnglePosition(area, br)
            seat.registerSeatHeightPosition(area, hg)
            unregisterActions += {
                seat.unregisterSeatForeAftPosition(area, fa)
                seat.unregisterSeatBackrestAnglePosition(area, br)
                seat.unregisterSeatHeightPosition(area, hg)
            }
        }

        // 안전 (Safety, CAR_SAFETY 공개 항목만): registerSeatBeltNotBuckledWarning(SeatArea, …{ onSeatBeltNotBuckledWarningUpdated(Boolean?) }),
        //   registerEngineOilLevelState / registerBrakeFluidLevelState(OilLevelStateListener{ onOilLevelUpdated(OilLevelState?) })
        //   Telltales(getTelltalesInfo/registerTelltales) 는 PRIVATE_CAR_SAFETY 권한 → 서드파티 앱은 못 쓴다 (javap @RequiresCustomPermission 확인)
        safely("Safety") {
            val safety = vehicle.getSafety()
            val area = SeatArea(SeatValue.ROW_1_LEFT)
            val belt = object : SeatBeltNotBuckledWarningListener {
                override fun onSeatBeltNotBuckledWarningUpdated(warning: Boolean?) {
                    update { it.copy(seatBeltWarning = warning, warningLamps = lampSet("SEAT_BELT", warning == true)) }
                }
                override fun onFailed(e: Exception) = warn("SeatBelt", e)
            }
            safety.hasSeatBeltNotBuckledWarning(area, { w: Boolean? -> belt.onSeatBeltNotBuckledWarningUpdated(w) }, { e: Exception -> warn("SeatBelt.getCurrent", e) })
            safety.registerSeatBeltNotBuckledWarning(area, belt)
            unregisterActions += { safety.unregisterSeatBeltNotBuckledWarning(area, belt) }

            val oil = object : OilLevelStateListener {
                override fun onOilLevelUpdated(state: OilLevelState?) = update { it.copy(warningLamps = lampSet("OIL_LEVEL", isLow(state))) }
                override fun onFailed(e: Exception) = warn("OilLevel", e)
            }
            safety.getEngineOilLevelState({ s: OilLevelState? -> oil.onOilLevelUpdated(s) }, { e: Exception -> warn("OilLevel.getCurrent", e) })
            safety.registerEngineOilLevelState(oil)
            unregisterActions += { safety.unregisterEngineOilLevelState(oil) }

            val brakeFluid = object : OilLevelStateListener {
                override fun onOilLevelUpdated(state: OilLevelState?) = update { it.copy(warningLamps = lampSet("BRAKE", isLow(state))) }
                override fun onFailed(e: Exception) = warn("BrakeFluid", e)
            }
            safety.getBrakeFluidLevelState({ s: OilLevelState? -> brakeFluid.onOilLevelUpdated(s) }, { e: Exception -> warn("BrakeFluid.getCurrent", e) })
            safety.registerBrakeFluidLevelState(brakeFluid)
            unregisterActions += { safety.unregisterBrakeFluidLevelState(brakeFluid) }
        }

        // 주행거리 (Odometer, CAR_DRIVE): registerDistanceDriven(DistanceDrivenListener{ onDistanceDrivenUpdated(Float?) })
        safely("Odometer") {
            val odo = vehicle.getOdometer()
            val l = object : DistanceDrivenListener {
                override fun onDistanceDrivenUpdated(distance: Float?) = update { it.copy(odometerKm = distance?.toDouble()) }  // TODO(미확인) 단위(km 가정)
                override fun onFailed(e: Exception) = warn("Odometer", e)
            }
            odo.getCurrentDistanceDriven({ d: Float? -> l.onDistanceDrivenUpdated(d) }, { e: Exception -> warn("Odometer.getCurrent", e) })
            odo.registerDistanceDriven(l)
            unregisterActions += { odo.unregisterDistanceDriven(l) }
        }

        // 연료 (Fuel, CAR_ENERGY): registerFuelPercent(FuelPercentListener{ onFuelPercentUpdated(Int?) }) — Int 퍼센트
        safely("Fuel") {
            val fuel = vehicle.getFuel()
            val l = object : FuelPercentListener {
                override fun onFuelPercentUpdated(percent: Int?) {
                    update { it.copy(fuelPercent = percent?.toDouble(), warningLamps = lampSet("LOW_FUEL", percent != null && percent <= LOW_FUEL_PERCENT)) }
                }
                override fun onFailed(e: Exception) = warn("Fuel", e)
            }
            fuel.getFuelPercent({ p: Int? -> l.onFuelPercentUpdated(p) }, { e: Exception -> warn("Fuel.getCurrent", e) })
            fuel.registerFuelPercent(l)
            unregisterActions += { fuel.unregisterFuelPercent(l) }
        }
    }

    private fun onPleosGear(g: PleosGear?) {
        if (g == null) return
        gearFromPleos = true
        update { it.copy(gear = mapPleosGear(g)) }
    }

    private fun onPleosSteering(deg: Float?) {
        if (deg == null) return
        steeringFromPleos = true
        update { it.copy(steeringAngleDeg = deg.toDouble() * STEERING_SIGN) }
    }

    /** 하향등 상태가 바뀔 때 스위치(AUTOMATIC 여부)도 같이 읽어 Headlight 를 다시 계산한다. */
    private fun refreshLowBeamSwitch(light: ai.pleos.playground.vehicle.api.Light) {
        safely("Light.getLowBeamSwitch") {
            light.getLowBeamLightSwitch(
                { sw: LightSwitch? -> lowBeamSwitch = sw; update { it.copy(headlight = headlight()) } },
                { e: Exception -> warn("LowBeamSwitch", e); update { it.copy(headlight = headlight()) } }
            )
        }
    }

    // ------------------------------------------------------------------
    // 2) AAOS CarPropertyManager — 속도·주차브레이크·시동 (+ 기어 대체)
    //    Pleos Vehicle SDK 2.3.2 에는 속도/RPM/주차브레이크/시동 API 가 없다 (IgnitionState enum 만 존재, 사용하는 공개 메서드 없음).
    // ------------------------------------------------------------------
    /** 시동 상태 원래 값(VehicleIgnitionState: LOCK·OFF·ACC·ON·START). CoachService 가 Suspend 진입·복귀를 판단한다(Pleos Suspend 모드 오디오 가이드). */
    var onIgnitionState: ((Int) -> Unit)? = null

    private val propCallback = object : CarPropertyManager.CarPropertyEventCallback {
        override fun onChangeEvent(value: CarPropertyValue<*>) {
            when (value.propertyId) {
                VehiclePropertyIds.PERF_VEHICLE_SPEED ->   // m/s (Float) → km/h
                    update { it.copy(speedKmh = (value.value as? Float)?.let { v -> Math.abs(v) * 3.6 }) }
                VehiclePropertyIds.GEAR_SELECTION ->
                    if (!gearFromPleos) update { it.copy(gear = mapAaosGear(value.value as? Int)) }
                VehiclePropertyIds.PARKING_BRAKE_ON ->
                    update { it.copy(parkingBrakeOn = value.value as? Boolean) }
                VehiclePropertyIds.IGNITION_STATE -> {
                    update { it.copy(ignitionOn = (value.value as? Int)?.let { s -> s == VehicleIgnitionState.ON || s == VehicleIgnitionState.START }) }
                    (value.value as? Int)?.let { s -> onIgnitionState?.invoke(s) }
                }
                ACCEL_PEDAL ->   // 0~100 %, 5% 단위로 묶어 발행(값이 조금씩 흔들려도 상태를 쏟아내지 않게)
                    update { it.copy(accelPedalPct = (value.value as? Float)?.let { v -> Math.round(v.coerceIn(0f, 100f) / 5f) * 5.0 }) }
            }
        }
        override fun onErrorEvent(propId: Int, zone: Int) { Log.w(TAG, "CarProperty error prop=$propId zone=$zone") }
    }

    private fun startCarPropertyManager() {
        safely("Car.createCar") {
            val c = Car.createCar(appContext) ?: return@safely
            car = c
            val pm = c.getCarManager(Car.PROPERTY_SERVICE) as CarPropertyManager
            props = pm
            // 각 속성은 권한이 거부될 수 있으므로 하나씩 등록 (CAR_SPEED 는 dangerous → MainActivity 에서 런타임 요청)
            val wanted = listOf(
                VehiclePropertyIds.PERF_VEHICLE_SPEED to CarPropertyManager.SENSOR_RATE_FAST,
                VehiclePropertyIds.GEAR_SELECTION to CarPropertyManager.SENSOR_RATE_ONCHANGE,
                VehiclePropertyIds.PARKING_BRAKE_ON to CarPropertyManager.SENSOR_RATE_ONCHANGE,
                VehiclePropertyIds.IGNITION_STATE to CarPropertyManager.SENSOR_RATE_ONCHANGE,
            )
            // (S, 11/8) 액셀 페달 밟은 정도: 3D 게임 "실제 액셀" 모드용. 차·에뮬레이터가 이 속성을 줄 때만(없으면 조용히 넘어감).
            //  권한 READ_CAR_PEDALS 는 dangerous(런타임 요청) — 사무국이 금지한 signature|privileged 가 아니다.
            safely("AccelPedal") {
                if (pm.getCarPropertyConfig(ACCEL_PEDAL) == null) throw IllegalStateException("속성 없음")
                if (!pm.registerCallback(propCallback, ACCEL_PEDAL, CarPropertyManager.SENSOR_RATE_NORMAL)) throw IllegalStateException("등록 실패")
                unregisterActions += { pm.unregisterCallback(propCallback, ACCEL_PEDAL) }
            }
            for ((id, rate) in wanted) {
                safely("registerCallback($id)") {
                    if (pm.registerCallback(propCallback, id, rate)) {
                        unregisterActions += { pm.unregisterCallback(propCallback, id) }
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // 3) Pleos FusedLocation — 위치 (javap: isSupported()/initialize()/registerFusedLocationCallback(FusedLocationListener)/release())
    // ------------------------------------------------------------------
    private val locationListener = object : FusedLocationListener {
        override fun onChanged(location: LocationData?) {
            location ?: return
            update { it.copy(lat = location.latitude, lon = location.longitude) }
        }
        override fun onError(exception: Exception) = warn("FusedLocation", exception)
    }

    private fun startFusedLocation() {
        safely("FusedLocation") {
            val fl = FusedLocation(appContext)
            if (!fl.isSupported()) { Log.i(TAG, "FusedLocation not supported on this vehicle"); return@safely }
            fl.initialize()
            fl.registerFusedLocationCallback(locationListener)
            fusedLocation = fl
            unregisterActions += { fl.unregisterFusedLocationCallback(locationListener) }
        }
    }

    // ------------------------------------------------------------------
    // 공통
    // ------------------------------------------------------------------
    /** 메인 스레드에서 스냅샷을 갱신하고 리스너에 전달한다. seatPositionChanged 는 한 스냅샷만 true. */
    private fun update(change: (VehicleState) -> VehicleState) {
        main.post {
            val next = change(current.copy(timestampMs = System.currentTimeMillis(), seatPositionChanged = false))
            if (next == current.copy(timestampMs = next.timestampMs)) return@post   // 값이 같으면 발행하지 않음
            current = next
            _stateFlow.value = next
            listeners.forEach { it.onState(next) }
        }
    }

    private fun seatMoved(pos: Int?, last: Int?, store: () -> Unit) {
        val changed = pos != null && last != null && pos != last
        store()
        if (changed) update { it.copy(seatPositionChanged = true) }
    }

    private fun lampSet(id: String, on: Boolean): Set<String> {
        if (on) lamps += id else lamps -= id
        return lamps.toSet()
    }

    private fun isLow(state: OilLevelState?): Boolean = state == OilLevelState.LOW || state == OilLevelState.CRITICALLY_LOW

    private fun headlight(): Headlight = when {
        highBeam == LightState.ON -> Headlight.HIGH
        lowBeamSwitch == LightSwitch.AUTOMATIC -> Headlight.AUTO   // 스위치가 AUTO 면 램프가 켜져 있어도 '오토'(베타 D-46)
        lowBeam == LightState.ON -> Headlight.LOW
        lowBeam != null && highBeam != null -> Headlight.OFF      // OFF 또는 DAYTIME_RUNNING(주간주행등만) 은 "전조등 꺼짐"
        else -> Headlight.UNKNOWN
    }

    private fun allWindowsClosed(): Boolean? {
        if (windowPositions.isEmpty()) return null
        if (windowPositions.values.any { it == null }) return null
        return windowPositions.values.all { it == 0 }
    }

    private fun mapTurnSignal(v: TurnSignalValue?): TurnSignal = when (v) {
        null -> TurnSignal.UNKNOWN
        TurnSignalValue.OFF -> TurnSignal.OFF
        TurnSignalValue.LEFT -> TurnSignal.LEFT
        TurnSignalValue.RIGHT -> TurnSignal.RIGHT
    }

    /** WiperState(켜짐/꺼짐) + WiperSwitch(단계) → core Wiper. */
    private fun mapWiper(state: WiperState?, sw: WiperSwitch?): Wiper = when (state) {
        null, WiperState.OTHER -> Wiper.UNKNOWN
        WiperState.OFF -> Wiper.OFF
        WiperState.SERVICE -> Wiper.OFF
        WiperState.ON -> when (sw) {
            WiperSwitch.OFF -> Wiper.OFF
            WiperSwitch.MIST, WiperSwitch.AUTO,
            WiperSwitch.INTERMITTENT_LEVEL_1, WiperSwitch.INTERMITTENT_LEVEL_2, WiperSwitch.INTERMITTENT_LEVEL_3,
            WiperSwitch.INTERMITTENT_LEVEL_4, WiperSwitch.INTERMITTENT_LEVEL_5 -> Wiper.INT
            WiperSwitch.CONTINUOUS_LEVEL_1, WiperSwitch.CONTINUOUS_LEVEL_2 -> Wiper.LOW
            WiperSwitch.CONTINUOUS_LEVEL_3, WiperSwitch.CONTINUOUS_LEVEL_4, WiperSwitch.CONTINUOUS_LEVEL_5 -> Wiper.HIGH
            else -> Wiper.LOW   // 스위치를 못 읽었지만 켜져 있음
        }
    }

    private fun mapPleosGear(g: PleosGear): Gear = when (g) {
        PleosGear.GEAR_PARK -> Gear.P
        PleosGear.GEAR_REVERSE -> Gear.R
        PleosGear.GEAR_NEUTRAL -> Gear.N
        PleosGear.GEAR_DRIVE, PleosGear.GEAR_1, PleosGear.GEAR_2, PleosGear.GEAR_3, PleosGear.GEAR_4, PleosGear.GEAR_5,
        PleosGear.GEAR_6, PleosGear.GEAR_7, PleosGear.GEAR_8, PleosGear.GEAR_9 -> Gear.D
        PleosGear.GEAR_UNKNOWN -> Gear.UNKNOWN
    }

    private fun mapAaosGear(g: Int?): Gear = when (g) {
        null -> Gear.UNKNOWN
        VehicleGear.GEAR_PARK -> Gear.P
        VehicleGear.GEAR_REVERSE -> Gear.R
        VehicleGear.GEAR_NEUTRAL -> Gear.N
        VehicleGear.GEAR_DRIVE, VehicleGear.GEAR_FIRST, VehicleGear.GEAR_SECOND, VehicleGear.GEAR_THIRD,
        VehicleGear.GEAR_FOURTH, VehicleGear.GEAR_FIFTH, VehicleGear.GEAR_SIXTH -> Gear.D
        else -> Gear.UNKNOWN
    }

    private inline fun safely(what: String, block: () -> Unit) {
        try { block(); diagAdd("OK $what"); _connected.value = _connected.value + what } catch (e: Throwable) { diagAdd("FAIL $what: ${e.javaClass.simpleName} ${e.message}"); Log.w(TAG, "$what failed: ${e.message}") }
    }

    private fun warn(what: String, e: Exception) { diagAdd("ERR $what: ${e.message}"); Log.w(TAG, "$what listener failed: ${e.message}") }

    companion object {
        /** AAOS VehiclePropertyIds.ACCELERATOR_PEDAL_COMPRESSION_PERCENTAGE (0x1160030f). compileSdk 34 에 상수가 없어 값으로 쓴다. */
        const val ACCEL_PEDAL = 0x1160030f
        /** 설정 화면에 "차량 신호 n/13" 으로 보여 줄 Vehicle SDK 기능(위 registerVehicleSdkListeners 의 safely 이름과 같아야 한다). */
        val VEHICLE_FEATURES = listOf("Gear", "SteeringWheel", "Brake", "TurnSignal", "Light", "Wiper", "Window", "Door", "HVAC", "Seat", "Safety", "Odometer", "Fuel")
        /** 전조등 스위치(AUTO 여부) 폴링 주기(ms). */
        const val LIGHT_SWITCH_POLL_MS = 2_000L
        private const val TAG = "PleosSignalSource"
        /** 연료 경고등 대용 임계값(%). 실제 경고등(Telltales)은 비공개 API 라 연료 퍼센트로 추정. */
        const val LOW_FUEL_PERCENT = 10
        /**
         * Pleos 조향각 부호를 "+ = 시계방향(우회전)" 으로 맞추는 계수.
         * 에뮬레이터 2.1.0 Virtual Console 에서 조향 슬라이더를 오른쪽 +411° 로 놓았을 때 SDK 가 -411 을 보냈다(11/2 확인)
         * → Pleos 는 ISO 8855 식 "+ = 반시계(좌회전)" 으로 보고 -1 을 곱한다. 실차에서 한 번 더 확인할 것.
         */
        const val STEERING_SIGN = -1.0
        /** windowsClosed 판정에 쓰는 창문 집합 (WindowValue 상수명 javap 확인). */
        private val WINDOWS = listOf(WindowValue.ROW_1_LEFT, WindowValue.ROW_1_RIGHT, WindowValue.ROW_2_LEFT, WindowValue.ROW_2_RIGHT)
    }
}
