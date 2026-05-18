# BATCH-18: Project Finalization — DEBT-027 Resolution + Documentation Hardening

**Batch Number:** BATCH-18  
**Type:** Finalization — no new features  
**Tasks:**
- **Feature (P2):** DEBT-027 — Carry full `Entity` handles through the entire LOS pipeline — from `VisionBroadphaseSystem` all the way to `TargetVisibleEvent` consumer (`ThreatEvaluationSystem`)
- **Docs (P3):** Lessons-learned addendum to `DEV-GUIDE.md` — HSM action dispatch (`[HsmAction]` + `Fhsm.SourceGen`) must be documented as a standard step for any future HSM feature batch
- **Docs (P3):** `DEBT-007-HSM-ANALYSIS.md` — update status header to reflect full resolution in BATCH-17
- **Housekeeping (P3):** Project closure note in `TASK-TRACKER.md`

**Phase:** Post-Phase-7 finalization  
**Estimated Effort:** 5–7 hours  
**Priority:** MEDIUM — DEBT-027 is the only non-trivial open item  
**Dependencies:** BATCH-17 ✅

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER — MANDATORY)

1. **DEBT-TRACKER:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md` — DEBT-027 entry.
2. **`PerceptionEvents.cs`:** `FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs` — the three event structs: `AudioStimulusEvent`, `LosCheckRequestEvent`, `TargetVisibleEvent`. Read them first.
3. **`VisionBroadphaseSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs` — the **root** of the raw-index smell. Line 122 emits `LosCheckRequestEvent` with `.ObserverEntityIndex = observer.Index` and `.TargetEntityIndex = target.Index`.
4. **`LosRequestBatchingSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs` — both consume `LosCheckRequestEvent` (mock mode: immediately re-emits as `TargetVisibleEvent`) and would feed `RaycastBatchData` (production mode: currently commented out).
5. **`PhysicsConstants.cs`:** `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` lines 54–68 — `PackLosRayId(int, int)` packs two raw ints into a `long RayId`. This is the point where generation information is **lost** in the physics pipeline path.
6. **`HitResolutionSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` lines 75–88 — unpacks `observerIdx` and `targetIdx` from `hit.RayId`, emits `TargetVisibleEvent` with raw ints.
7. **`ThreatEvaluationSystem.cs`** (locate it) — consumes `TargetVisibleEvent`, reads by raw index.

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-18-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. Read all referenced files in full (Required Reading above) ✅
2. DEBT-027 pipeline fix (Steps A–F below) ✅
3. Documentation updates (DEV-GUIDE, DEBT-007-HSM-ANALYSIS, TASK-TRACKER) ✅
4. Full solution: 0 errors, all tests green ✅

---

## ✅ Tasks

### DEBT-027 — Full `Entity` Handles Through the LOS Pipeline

**Background:**

The raw-index problem originates at `VisionBroadphaseSystem` lines 122–123 and propagates through four structures without ever carrying generation counters:

```
VisionBroadphaseSystem
  emits LosCheckRequestEvent { ObserverEntityIndex : int, TargetEntityIndex : int }
      ↓
LosRequestBatchingSystem (mock: re-emits directly; production: packs into RayId)
  emits TargetVisibleEvent { ObserverEntityIndex : int, TargetEntityIndex : int }
      ↓ (or via physics)
PhysicsConstants.PackLosRayId(int, int) → packs indices into RayId : long
  ↓ (RaycastSolverSystem passes RayId through unchanged)
HitResolutionSystem
  unpacks: observerIdx = (int)(hit.RayId >> 32)
           targetIdx   = (int)(hit.RayId & 0xFFFF_FFFFL)
  emits TargetVisibleEvent { ObserverEntityIndex = observerIdx, TargetEntityIndex = targetIdx }
      ↓
ThreatEvaluationSystem — consumes TargetVisibleEvent by raw int index
```

**The principle:** `GetEntityByIndex` is almost always a code smell. Full `Entity` handles (Index + Generation) must flow from the point where the entity is *known* to be alive — which is `VisionBroadphaseSystem`, where both `observer` and `target` are already `Entity` values with generation. From there, the generation must never be discarded.

**There is one justified exception to raw-ints in this pipeline:** `RayId : long` is a 64-bit field shared between LOS and bullet rays. It has a fixed bit layout (`PackLosRayId`, `PackBulletRayId`). `Entity` structs contain an `int Index` and a `ushort Generation` — that's 6 bytes per entity × 2 entities = 12 bytes, too wide for a single `long`. This means the `RayId` field cannot carry full generation information. The correct solution is:

**Do not recover entity identity from `RayId` in `HitResolutionSystem`.** Instead, attach the full `Entity` handles directly to `RaycastRequest` as separate fields (alongside `RayId`), propagate them to `RaycastHit`, and read them in `HitResolutionSystem` from those fields — never from `RayId` bit-unpacking.

---

#### Step A — Update `LosCheckRequestEvent` to carry full `Entity` handles

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs`

```csharp
// BEFORE:
public struct LosCheckRequestEvent
{
    public int ObserverEntityIndex;
    public int TargetEntityIndex;
}

// AFTER:
public struct LosCheckRequestEvent
{
    /// <summary>The observer entity performing the LOS check (full handle: index + generation).</summary>
    public Entity Observer;
    /// <summary>The potential target entity (full handle: index + generation).</summary>
    public Entity Target;
}
```

**Why:** `VisionBroadphaseSystem` already has both values as full `Entity` handles. No generation information needs to be re-acquired. The moment we discard generation at emission time, the pipeline is permanently unsafe.

---

#### Step B — Update `VisionBroadphaseSystem` emit site

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs` (lines 120–124)

```csharp
// BEFORE:
ecb.PublishEvent(new LosCheckRequestEvent
{
    ObserverEntityIndex = observer.Index,
    TargetEntityIndex   = target.Index,
});

// AFTER:
ecb.PublishEvent(new LosCheckRequestEvent
{
    Observer = observer,
    Target   = target,
});
```

No other change needed in this file.

---

#### Step C — Update `LosRequestBatchingSystem` (mock mode path)

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs`

Mock-mode path (lines 51–58): pass `Entity` handles straight through. No generation checks needed here — the event was just emitted this frame, both entities were alive at emission.

```csharp
// BEFORE:
World.Bus.Publish(new TargetVisibleEvent
{
    ObserverEntityIndex = req.ObserverEntityIndex,
    TargetEntityIndex   = req.TargetEntityIndex,
});

// AFTER:
World.Bus.Publish(new TargetVisibleEvent
{
    Observer = req.Observer,
    Target   = req.Target,
});
```

Production-mode (commented-out) stub: update the comment to reference `req.Observer` / `req.Target` so future implementors know the correct pattern.

---

#### Step D — Update `RaycastRequest` and `RaycastHit` to carry `Entity` handles

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs`  
(read the current struct definitions first to understand the existing layout)

Add `Entity` fields to `RaycastRequest` and `RaycastHit`:

```csharp
// In RaycastRequest (alongside existing fields):
/// <summary>
/// For LOS rays: the observer entity (full handle: index + generation).
/// Zero/Null for bullet rays.
/// Propagated unchanged to <see cref="RaycastHit.Observer"/> for recovery in
/// <see cref="Systems.HitResolutionSystem"/> without bit-unpacking from <see cref="RayId"/>.
/// </summary>
public Entity Observer;

/// <summary>
/// For LOS rays: the target entity (full handle: index + generation).
/// Zero/Null for bullet rays.
/// Propagated unchanged to <see cref="RaycastHit.Target"/>.
/// </summary>
public Entity Target;
```

Add matching fields to `RaycastHit`:
```csharp
public Entity Observer;
public Entity Target;
```

> ⚠️ Check whether `RaycastRequest` and `RaycastHit` are `unmanaged` structs used in `NativeArray` or `stackalloc`. If so, adding `Entity` fields is safe as `Entity` is `unmanaged` (it contains only `int Index` and `ushort Generation`).

---

#### Step E — Propagate `Entity` handles through `RaycastSolverSystem`

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs`

The solver copies `RaycastRequest` fields to `RaycastHit`. Verify the copy of `RayId` (line 135: `RayId = req.RayId`) and add:

```csharp
Observer = req.Observer,
Target   = req.Target,
```

`RayId` continues to be used for `IsBulletRay` discrimination. It still encodes raw indices in its payload — but those are now **unused for entity recovery**. They can be kept as-is (backward compatible) or cleaned up. **Do not change the RayId packing format** — that is a separate concern. The key point is that `HitResolutionSystem` must read `Observer`/`Target` from the hit struct, not unpack from `RayId`.

---

#### Step F — Update `HitResolutionSystem` and `TargetVisibleEvent`

**Update `TargetVisibleEvent`:**	

```csharp
// BEFORE:
public struct TargetVisibleEvent
{
    public int ObserverEntityIndex;
    public int TargetEntityIndex;
}

// AFTER:
public struct TargetVisibleEvent
{
    /// <summary>The observer entity that has confirmed LOS to <see cref="Target"/>.</summary>
    public Entity Observer;
    /// <summary>The target entity confirmed visible to <see cref="Observer"/>.</summary>
    public Entity Target;
}
```

**Update `HitResolutionSystem`** (lines 74–88): Replace bit-unpacking with direct field reads.

```csharp
// BEFORE:
int observerIdx = (int)(hit.RayId >> 32);
int targetIdx   = (int)(hit.RayId & 0xFFFF_FFFFL);
// DEBT-027: raw indices ...
World.Bus.Publish(new TargetVisibleEvent
{
    ObserverEntityIndex = observerIdx,
    TargetEntityIndex   = targetIdx,
});

// AFTER:
// Full Entity handles propagated from RaycastRequest — no index-only recovery needed.
// IsAlive checks are intentionally deferred to ThreatEvaluationSystem (the consumer),
// since a one-frame entity destruction between solve and emit is possible but does not
// warrant a check here — the consumer applies the generational guard.
World.Bus.Publish(new TargetVisibleEvent
{
    Observer = hit.Observer,
    Target   = hit.Target,
});
```

Also remove the DEBT-027 comment block (it's now resolved).

---

#### Step G — Update `ThreatEvaluationSystem` (consumer)

**File:** Locate `ThreatEvaluationSystem.cs` (likely in `FDP.Toolkit.Perception/Systems/`).

Update field names from `ObserverEntityIndex : int` / `TargetEntityIndex : int` to `Observer : Entity` / `Target : Entity`. Add `IsAlive` generational guards:

```csharp
// Pattern for all TargetVisibleEvent consumers:
foreach (ref readonly var ev in visibleEvents)
{
    // Generational guard — entity may have been destroyed between LOS submission and now.
    if (!World.IsAlive(ev.Observer) || !World.IsAlive(ev.Target))
        continue;

    // Safe to access components — full Entity handle validated above.
    ref var memory = ref World.GetComponentRW<TargetMemory>(ev.Observer);
    // ... update memory with ev.Target ...
}
```

Also update `AudioStimulusEvent` if it follows the same pattern (it has `SourceEntityIndex : int`). Check if its consumer also uses a raw int — if so, fix it in the same batch for consistency. If audio perception does not have the recycling risk (e.g. it is a stimulus published and consumed within the same frame), document why it is acceptable to leave it as-is.

---

#### Step H — Fix existing tests

Update all tests that construct `LosCheckRequestEvent`, `TargetVisibleEvent` directly:
- Replace `.ObserverEntityIndex = e.Index` with `.Observer = e`
- Replace `.TargetEntityIndex = t.Index` with `.Target = t`

---

#### Step I — New tests

```csharp
[Fact]
public void ThreatEvaluationSystem_SkipsEvent_WhenObserverRecycled()
// Arrange: spawn Observer, Target. Emit TargetVisibleEvent{Observer, Target}. Destroy Observer.
// Act: run ThreatEvaluationSystem (consume TargetVisibleEvent).
// Assert: Target's TargetMemory unchanged (no crash; stale event skipped).

[Fact]
public void ThreatEvaluationSystem_SkipsEvent_WhenTargetRecycled()
// Same but Target entity destroyed between emit and consume.

[Fact]
public void ThreatEvaluationSystem_UpdatesThreatMemory_WhenBothAlive()
// Happy path: both entities alive; TargetMemory updated correctly.

[Fact]
public void LosCheckRequestEvent_CarriesFullEntityHandle_NotRawIndex()
// Verify that VisionBroadphaseSystem emits Observer/Target (Entity, not int).
// Construct a minimal scenario, run the broadphase, read the emitted event.
// Assert: ev.Observer == observerEntity (full handle, generation matches).
```

---

### Documentation Tasks

#### DEV-GUIDE.md — HSM Action Registration Pitfall

**File:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\DEV-GUIDE.md`

Add under "Common Pitfalls to Avoid" (before the closing section):

```markdown
### 9. HSM Action Delegates — Registration Is Not Automatic

❌ **Pitfall:** Implement `[HsmAction]`-decorated delegates but forget to register them.
FastHSM dispatches delegates by FNV-1a hash lookup — without a registration entry the kernel
silently no-ops. Tests fail with missing telemetry milestones; no error is thrown.

✅ **Solution:** For any assembly containing HSM action methods:
1. Add `Fhsm.SourceGen` as an `OutputItemType="Analyzer"` project reference in the `.csproj`.
2. Decorate all delegate methods with `[HsmAction]` (from `Fhsm.Kernel.Attributes`).
3. Call `{AssemblyName}.Generated.HsmActionRegistrar.RegisterAll()` early in the app's
   `Initialize()` (before any HSM tick).
4. Confirm the name string in `HsmSetup.RegisterAction("Method_Name")` matches the actual
   C# method name exactly (the FNV-1a hash is case-sensitive; a one-character difference
   causes a silent dispatch miss).

Discovered during BATCH-17 (DEBT-007 GCHandle fix). See `BATCH-17-REPORT.md` Q1.
```

#### `DEBT-007-HSM-ANALYSIS.md` — Status Header Update

**File:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\DEBT-007-HSM-ANALYSIS.md`

Replace the top summary block with:

```markdown
> **STATUS: ✅ FULLY RESOLVED in BATCH-17.**  
> `EntityRepository.UnmanagedHandle` (GCHandle.Normal, one-time alloc in constructor, freed in Dispose);  
> `HsmKernelBridge.WorldHandle : IntPtr`; `FdpHsmContext` deleted; `ApcBrainOutputSystem` deleted;  
> `ApcHsmActions.Activity_Cruise` and `OnEnter_Disabled` fully implemented with ECS writes;  
> 4 new tests pass. T9 `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` still passes.  
> This document is an architectural reference for the GCHandle pattern and the OnEntry/OnExit
> correctness argument that ruled out the external-bridge approach.
```

#### `TASK-TRACKER.md` — Project Closure Note

**File:** `D:\Work\IOS-IG-SimHost-FDP\FDP\Docs\projects\behavior-control\TASK-TRACKER.md`

After the summary table, add:

```markdown
---

## 🏁 Project Status

**All 43 Behavior Control Subsystem tasks complete (BATCH-01 — BATCH-17).**  
**All P2/P3 technical debt resolved (BATCH-17 completed DEBT-007, 036, 037, 038).**  
**Remaining:** DEBT-027 (LOS pipeline generational safety) resolved in BATCH-18.  
**Test count at BATCH-17 closure:** 30 passing, 0 failures.

Phase 7 Urban Ambush demo: 600-frame end-to-end test passes — 14 entities, 7 telemetry milestones,
HSM (APC), BTree (Insurgent), TrafficBrain (Pedestrians/Cars), EjectPassengers all functional.
```

---

## 🧪 Testing Requirements

- **DEBT-027:** 4 new tests (recycled observer, recycled target, happy path, LosCheckRequestEvent entity handle).
- **All existing 30 tests must remain green.**
- **Documentation tasks:** No new tests needed.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-18-REPORT.md`

**Q1:** Does `AudioStimulusEvent.SourceEntityIndex : int` have the same recycling risk as `LosCheckRequestEvent`? What did you decide and why?

**Q2:** `RayId` still encodes raw indices in `PackLosRayId`. Did you remove that encoding, or keep it as a now-unused legacy field? Justify your choice.

**Q3:** Were there consumers of `TargetVisibleEvent` other than `ThreatEvaluationSystem`? If so, list them.

**Q4:** Was `LosRequestBatchingSystem`'s production-mode code updated to carry `Entity` handles (even though it's commented out)? Show the updated comment.

**Q5:** Any surprises in the pipeline structure?

---

## 🎯 Success Criteria

- [ ] **DEBT-027 resolved:**
  - [ ] `LosCheckRequestEvent`: `Observer : Entity`, `Target : Entity` (no raw ints).
  - [ ] `TargetVisibleEvent`: `Observer : Entity`, `Target : Entity` (no raw ints).
  - [ ] `VisionBroadphaseSystem`: emits `Observer = observer`, `Target = target`.
  - [ ] `LosRequestBatchingSystem` mock path: passes `Entity` fields through directly.
  - [ ] `RaycastRequest` + `RaycastHit`: `Observer : Entity`, `Target : Entity` fields added.
  - [ ] `RaycastSolverSystem`: propagates `Observer`/`Target` fields to hit.
  - [ ] `HitResolutionSystem`: reads `hit.Observer`/`hit.Target`; no `RayId` bit-unpacking for entity identity. DEBT-027 comment removed.
  - [ ] `ThreatEvaluationSystem`: uses `ev.Observer`/`ev.Target`; `IsAlive` guards at consume site.
  - [ ] All existing tests updated to use new field names.
  - [ ] 4 new tests pass.
- [ ] **DEV-GUIDE updated:** HSM action registration pitfall documented.
- [ ] **DEBT-007-HSM-ANALYSIS updated:** Status header shows ✅ RESOLVED.
- [ ] **TASK-TRACKER updated:** Project closure note added.
- [ ] **Zero build errors; all tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **DEBT-TRACKER:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
- **`PerceptionEvents.cs`:** `FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs` — root structs
- **`VisionBroadphaseSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs` — Step B
- **`LosRequestBatchingSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs` — Step C
- **`PhysicsConstants.cs`:** `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` — `PackLosRayId`
- **`PhysicsComponents.cs`:** `FDP/Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs` — `RaycastRequest`/`RaycastHit` — Step D
- **`RaycastSolverSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` — Step E
- **`HitResolutionSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` — Step F
- **`DEV-GUIDE.md`:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\DEV-GUIDE.md`
- **`DEBT-007-HSM-ANALYSIS.md`:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\DEBT-007-HSM-ANALYSIS.md`
- **`TASK-TRACKER.md`:** `D:\Work\IOS-IG-SimHost-FDP\FDP\Docs\projects\behavior-control\TASK-TRACKER.md`
