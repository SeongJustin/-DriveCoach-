// 담당: C+G
package kr.drivecoach.core.input

import kr.drivecoach.core.signal.Gear
import kr.drivecoach.core.signal.VehicleState
import kr.drivecoach.core.tts.PhraseId
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

private fun mgr() = AwaitingInputManager(defaultTimeoutMs = 10_000, repromptTimeoutMs = 20_000)

/** 정차 중: 마감 → 재안내 1회 → 그래도 없으면 다음 기회. */
private fun testStoppedReprompt() {
    val m = mgr()
    assertEquals(NoInputDecision.Idle, m.tick(0, vehicleStopped = true))
    m.open("mirror_check", nowMs = 0)
    assertTrue(m.tick(5_000, vehicleStopped = true) is NoInputDecision.Waiting)

    val d1 = m.tick(10_000, vehicleStopped = true)
    assertTrue(d1 is NoInputDecision.Reprompt, "마감 시 재안내")
    d1 as NoInputDecision.Reprompt
    assertEquals(AwaitingInputManager.REPROMPT_PHRASE, d1.phraseId)
    assertEquals(PhraseId("system_no_input_reprompt"), d1.phraseId)
    assertEquals(1, d1.prompt.repromptCount)
    assertEquals(30_000, d1.prompt.deadlineMs)

    assertTrue(m.tick(20_000, vehicleStopped = true) is NoInputDecision.Waiting)
    val d2 = m.tick(30_000, vehicleStopped = true)
    assertEquals(NoInputDecision.Deferred("mirror_check", wasMoving = false), d2, "재안내는 최대 1회")
    assertNull(m.current)
    assertEquals(NoInputDecision.Idle, m.tick(31_000, vehicleStopped = true))
    println("  stoppedReprompt OK")
}

/** 주행 중: 재촉 없이 바로 다음 기회. */
private fun testMovingDefers() {
    val m = mgr()
    m.open("lane_change_confirm", nowMs = 0)
    assertTrue(m.tick(9_999, vehicleStopped = false) is NoInputDecision.Waiting)
    assertEquals(NoInputDecision.Deferred("lane_change_confirm", wasMoving = true), m.tick(10_000, vehicleStopped = false))
    assertNull(m.current)
    println("  movingDefers OK")
}

/** 정차 중 재안내 후 출발하면, 두 번째 마감에서는 조용히 넘어간다. */
private fun testRepromptThenMoving() {
    val m = mgr()
    m.open("p1", nowMs = 0)
    assertTrue(m.tick(10_000, vehicleStopped = true) is NoInputDecision.Reprompt)
    assertEquals(NoInputDecision.Deferred("p1", wasMoving = true), m.tick(30_000, vehicleStopped = false))
    println("  repromptThenMoving OK")
}

/** 명령 처리: 확인/다음은 닫고, 다시는 연장, 조용히는 무관, 세울래요는 취소. */
private fun testCommands() {
    val m = mgr()
    m.open("p1", nowMs = 0)
    assertFalse(m.onCommand(Command.QUIET, 1_000))
    assertTrue(m.current != null)
    assertFalse(m.onCommand(Command.AGAIN, 8_000), "다시 → 계속 대기")
    assertEquals(18_000, m.current!!.deadlineMs, "다시 → 마감 연장")
    assertTrue(m.tick(10_000, vehicleStopped = true) is NoInputDecision.Waiting)
    assertTrue(m.onCommand(Command.CONFIRM, 12_000))
    assertNull(m.current)
    assertFalse(m.onCommand(Command.CONFIRM, 13_000), "대기 없으면 소비 안 함")

    m.open("p2", nowMs = 0, accepts = setOf(Command.NEXT))
    assertFalse(m.onCommand(Command.CONFIRM, 1_000), "accepts 에 없는 명령은 무시")
    assertTrue(m.onCommand(Command.NEXT, 2_000))

    m.open("p3", nowMs = 0)
    assertTrue(m.onCommand(Command.PULL_OVER, 1_000))
    assertNull(m.current, "세울래요는 대기 취소")

    // 새 대기를 열면 이전 것은 교체
    m.open("p4", nowMs = 0)
    m.open("p5", nowMs = 1_000)
    assertEquals("p5", m.current!!.promptId)
    m.cancel()
    assertNull(m.current)
    println("  commands OK")
}

/** VehicleState 로 정지 판정. */
private fun testIsStopped() {
    val m = mgr()
    assertTrue(m.isStopped(VehicleState(0, speedKmh = 0.0, gear = Gear.D)))
    assertTrue(m.isStopped(VehicleState(0, speedKmh = 0.4, gear = Gear.D)))
    assertFalse(m.isStopped(VehicleState(0, speedKmh = 3.0, gear = Gear.P)))
    assertTrue(m.isStopped(VehicleState(0, speedKmh = null, gear = Gear.P)))
    assertFalse(m.isStopped(VehicleState(0, speedKmh = null, gear = Gear.D)), "모르면 주행 중으로")
    m.open("p", nowMs = 0)
    assertTrue(m.tick(10_000, VehicleState(10_000, speedKmh = 0.0)) is NoInputDecision.Reprompt)
    println("  isStopped OK")
}

fun main() {
    testStoppedReprompt()
    testMovingDefers()
    testRepromptThenMoving()
    testCommands()
    testIsStopped()
    println("AwaitingInputTest PASSED")
}
