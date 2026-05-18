# BATCH-12 Review

**Batch:** BATCH-12  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED — one P3 documentation fix noted

---

## Issues Found

### Issue 1: `EjectPassengersExecutor` XML doc comment has wrong slot offsets (P3)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` (line 24–25)  
**Problem:** The XML doc states:  
> *"For 2 passengers: offsets are −0.75 m and +0.75 m on X."*  
> *"For 4 passengers: offsets are −2.25 m, −0.75 m, +0.75 m, +2.25 m on X."*  

These values describe the **symmetric** formula `(i - (Count - 1) / 2f) * 1.5f`. The actual implementation uses `(i - Count / 2f) * 1.5f`, which per the developer's own Q3 analysis gives:
- Count=2: `− 1.5 m, 0.0 m` (asymmetric, not `−0.75 / +0.75`)
- Count=4: `−3.0 m, −1.5 m, 0.0 m, +1.5 m` (not `−2.25 / −0.75 / +0.75 / +2.25`)

The doc comment describes the symmetric version, but the code implements the asymmetric version — they are inconsistent. The spec deliberately chose the asymmetric formula and accepted it; the doc comment should be corrected to match the actual computed values.  
**Fix:** Update line 24–25 to say:
> *"For 2 passengers: offsets are −1.5 m and 0.0 m on X. For 4 passengers: −3.0 m, −1.5 m, 0.0 m, +1.5 m on X."*  
This is a P3 documentation fix — add to DEBT-034, resolve in BATCH-13.

---

## Test Quality Assessment

**`HsmDamageBridgeSystemTests` (4 tests):**  
Test 1 is the strongest: runs tick 1 with caps unchanged (count=0 verified), then strips `CanMove`, runs tick 2 (count=1 verified), then **dequeues and reads `EventId`** directly — proves the exact event content, not just count. ✅  
Test 2 starts with `CanMove` already clear in both `ActorCapabilityState` and `PreviousCapabilities` — no transition occurs, no event. Proves the shadow-diff logic fires only on transitions. ✅  
Test 3 clears `CanShoot` only — proves the `CanMove`-specific filter does not incorrectly trigger on other capability changes. ✅

**`EmbarkExecutor` / `EjectPassengersExecutor` tests (9 tests):**  
`Embark_DoesNotEmbark_WhenDistanceTooFar` asserts both `buffer.Count == 0` AND `channel.Status == Running` — verifying both the side-effect absence and the status. ✅  
`Eject_SkipsDeadPassengers_Gracefully` seeds 2 passengers (one dead, one alive), runs eject, asserts the live one's capabilities were restored and buffer cleared — a proper mixed-state test. ✅

**HSM API usage:** `GetComponentRW<BrainHsm128>(entity)` → `fixed (&brain.State)` → `HsmEventQueue.TryEnqueue` is the correct pinned-pointer pattern. The test helper `Unsafe.AsPointer` on a stack copy is also correct (no GC move risk for a local). ✅

**Two-pass init pattern (`_toInit` list):** Deferred structural changes collected into a reused `List<>`, added after iterator completes. Correct and avoids the iterator-invalidation issue. The managed `List<>` allocation is pre-constructed in the constructor and reused across frames — no per-frame heap pressure on the steady-state path. ✅

---

## Verdict

**APPROVED.** All three P6 tasks delivered cleanly. DEBT-034 added for doc comment correction (one-liner, no logic change).

---

## 📝 Commit Message

```
feat: Phase 6 complete — HsmDamageBridgeSystem + Embark + Eject (BATCH-12)

BCS-P6-T2 — HsmDamageBridgeSystem
  Shadow component: PreviousCapabilities (ActorCapabilities) added to BehaviorComponents.cs
  Two-pass per tier (128/64): init pass (Without<PreviousCapabilities>) + diff pass
  Deferred AddComponent via _toInit List<> (reused, no per-frame alloc) avoids iterator invalidation
  BrainHsm128: GetComponentRW → fixed(&brain.State) → HsmEventQueue.TryEnqueue(ptr, MobilityLost)
  [UpdateBefore(HsmTickSystem<BrainHsm128>)] + [UpdateBefore(HsmTickSystem<BrainHsm64>)]
  BehaviorConstants.EventId_MobilityLost = 1
  +4 tests: transition inject, no-inject (already clear), no-inject (CanShoot only), shadow update

BCS-P6-T3 — EmbarkExecutor + EjectPassengersExecutor
  InteractionComponents: PassengerBuffer (Capacity=8, [InlineArray(8)] PassengerSlots) + IsEmbarkedTag
  EmbarkParams: VehicleEntity + MaxBoardingRange (in InteractionChannel.Params)
  EmbarkExecutor: IsAlive guard → SimTransform distance → capacity → add → strip caps → IsEmbarkedTag tag
  EjectPassengersExecutor: per-passenger IsAlive guard → scatter SimTransform → restore caps → remove tag
  Slot offset: (i - Count/2f) * 1.5f (asymmetric per spec; documented in XML comment)
  +9 tests: 5 embark (range/caps/tag/dead-vehicle), 4 eject (restore/tag-remove/clear/dead-passenger)

Full solution: 0 errors; Behavior.Tests 29 → 42 (+13); all green
```

---

**Next Batch:** BATCH-13 — Pre-Demo Debt Resolution
