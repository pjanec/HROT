# BATCH-05: Phase 2 — Perception Toolkit (BCS-P2-T1 through BCS-P2-T4)

**Batch Number:** BATCH-05  
**Tasks:** CORRECTIVE (HSM test + integration minor fixes), BCS-P2-T1, BCS-P2-T2, BCS-P2-T3, BCS-P2-T4  
**Phase:** Phase 2 — FDP.Toolkit.Perception  
**Estimated Effort:** 10–13 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-04 ✅ (Phase 1 complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Two parts:

1. **Corrective (1 h):** Three small fixes from BATCH-04 review: fix the HSM event injection to not rely on a magic field offset, add one missing assertion to a Doctrine test, document cross-group ordering.
2. **Phase 2 — Perception (9–12 h):** Create `FDP.Toolkit.Perception` project with components, events, and all four perception systems — one main-thread, one async module, and two integration bridges.

This is the first batch that introduces a **`SlowBackground` async module** (`PerceptionModule`). Read the async/SoD rules in `ONBOARDING.md` carefully before writing the module. The read-only snapshot + command-buffer contract is non-negotiable.

### Required Reading (IN ORDER)

1. **BATCH-04 Review:** `.dev-workstream/reviews/BATCH-04-REVIEW.md` — understand the three correctives before touching anything
2. **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md` — all rules; §3 especially for the background module constraint
3. **Onboarding §async modules:** `FDP/Docs/projects/behavior-control/ONBOARDING.md` — SoD/SlowBackground lifecycle, snapshot semantics, ECB usage (read before Phase 2 starts)
4. **Task details BCS-P2-T1 through T4:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 488–626
5. **Design §4.1–4.4:** `FDP/Docs/projects/behavior-control/DESIGN.md` — Perception component types, systems, async module pattern

### Source Code Locations

| Area | Path |
|---|---|
| **Corrective** — HSM test | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/HsmTickSystemTests.cs` |
| **Corrective** — Doctrine test | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/DoctrineIngressSystemTests.cs` |
| **Corrective** — Group ordering | `FDP/Kernel/Fdp.Kernel/StandardSystemGroups.cs` |
| **New toolkit project** | `FDP/Toolkits/FDP.Toolkit.Perception/` ← **create** |
| **New test project** | `FDP/Toolkits/FDP.Toolkit.Perception.Tests/` ← **create** |
| SpatialHashGrid (2D) | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` |
| SpatialHashGrid API | `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashGrid.cs` |
| SimTransform (coordinate convention) | `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` |
| SimMath (forward extraction) | `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs` |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/
dotnet test Toolkits/FDP.Toolkit.Perception.Tests/
```

### Report Submission

`.dev-workstream/reports/BATCH-05-REPORT.md`  
Questions: `.dev-workstream/questions/BATCH-05-QUESTIONS.md`

---

## Context

**Related tasks:**
- Corrective: HSM event injection — see [BATCH-04-REVIEW.md](../reviews/BATCH-04-REVIEW.md) Issue 1  
- Corrective: Doctrine test gap — see [BATCH-04-REVIEW.md](../reviews/BATCH-04-REVIEW.md) Issue 2  
- Corrective: cross-group ordering doc — see [BATCH-04-REVIEW.md](../reviews/BATCH-04-REVIEW.md) Issue 4  
- [BCS-P2-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t1--perception-component-types)  
- [BCS-P2-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t2--audioperceptionsystem-main-thread)  
- [BCS-P2-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t3--perceptionmodule-async-vision-broadphase)  
- [BCS-P2-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t4--losrequestbatchingsystem--targetmemory-integration)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Corrective:** Fix three items → all existing tests still pass ✅
2. **BCS-P2-T1:** Component types + events → layout/unmanaged tests pass ✅
3. **BCS-P2-T2:** `AudioPerceptionSystem` → hearing-range tests pass ✅
4. **BCS-P2-T3:** `PerceptionModule` + `VisionBroadphaseSystem` + `ThreatEvaluationSystem` → broadphase + integration tests pass ✅
5. **BCS-P2-T4:** `LosRequestBatchingSystem` → batching test passes ✅

---

## 🎯 Batch Objectives

- `FDP.Toolkit.Perception` project exists and compiles.
- `TargetMemory` is unmanaged, uses named size constants for all fixed arrays.
- `AudioPerceptionSystem` updates `TargetMemory` for in-range listeners only.
- `PerceptionModule` runs async (SoD), reads a snapshot, uses ECB — never writes to main world directly.
- All `SimTransform`-based position extractions use `Position.X` / `Position.Y` (XY ground plane); forward vectors use `SimMath` or `Vector3.Transform(Vector3.UnitX, tf.Rotation)`.
- Design and task doc show `VehicleState.Position` and `VehicleState.Forward` in some places — **ignore those, always use `SimTransform`**.

---

## ✅ Tasks

### Task 0 (Corrective): Three Fixes from BATCH-04 Review

**Fix A — HSM event injection via named constant** (`HsmTickSystemTests.cs` line 82):

`brain.State.Reserved1 = 10` injects an event by writing to an internal field mapped to offset 58. This is a magic-offset dependency on FastHSM internals.

Check whether `HsmKernel` or `HsmInstance128` exposes a `PushEvent(uint eventId)` method or a typed `PendingEventId` property. If such an API exists, use it. If not, introduce a named constant in the test file:
```csharp
// Ties this test to FastHSM version where Reserved1 == CurrentEventId field.
// If HsmInstance128 layout changes, update this constant.
private const int EventXId = 10;
private const string HsmCurrentEventFieldName = nameof(HsmInstance128.Reserved1);
```
And leave a comment explaining the mapping. The test must still assert `ActiveLeafIds[0] == 1` after the tick.

**Fix B — Missing assertion in DoctrineIngress integration test** (`DoctrineIngressSystemTests.cs` Test 4):

After `arbitrationSys.Run()`, add:
```csharp
Assert.Equal(0u, channel.DoctrineInstanceId); // full channel = default, not selective clear
```

**Fix C — Cross-group ordering documentation** (`StandardSystemGroups.cs`):

Add an XML comment to `InputSystemGroup` and `SimulationSystemGroup` explaining the required registration order and the constraint that until cross-group attribute ordering is supported, host applications must register groups manually in `Input → Simulation` order:
```csharp
/// <summary>
/// System group for input processing (doctrine ingress, command buffering).
/// Must be registered before <see cref="SimulationSystemGroup"/> in the world setup
/// so that doctrine changes take effect within the same frame.
/// TODO: add [UpdateBefore(typeof(SimulationSystemGroup))] when cross-group sorting is supported.
/// </summary>
public class InputSystemGroup : SystemGroup { }
```

---

### Task 1: Perception Component Types (BCS-P2-T1)

**New project:** `FDP/Toolkits/FDP.Toolkit.Perception/FDP.Toolkit.Perception.csproj`  
**References:** `Fdp.Kernel`, `FDP.Toolkit.CarKinem`

**Files:**
- `Components/PerceptionComponents.cs` — `Faction`, `PerceptionReceptor`, `TargetMemory`
- `Events/PerceptionEvents.cs` — `AudioStimulusEvent`, `LosCheckRequestEvent`, `TargetVisibleEvent`

**Task Definition:** [TASK-DETAIL.md §BCS-P2-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t1--perception-component-types) — lines 495–522

**Magic numbers — use named constants.** Create `PerceptionConstants.cs`:
```csharp
public static class PerceptionConstants
{
    /// <summary>Maximum number of tracked targets in TargetMemory.</summary>
    public const int MaxTrackedTargets = 4;

    // EventId values as defined in DESIGN.md §4.1
    public const int AudioStimulusEventId  = 4001;
    public const int LosCheckRequestEventId = 4002;
    public const int TargetVisibleEventId  = 4003;
}
```
Use `PerceptionConstants.MaxTrackedTargets` in all `fixed` array declarations.

**`PerceptionReceptor.FieldOfViewCos`:** This is the *precomputed cosine* of the half-FOV. Do **not** store the angle — store the cosine. Example: 60° FOV → half-FOV = 30° → `FieldOfViewCos = MathF.Cos(MathF.PI / 6f)`. The system divides no trig functions on the hot path, only a `Vector2.Dot` comparison.

**`TargetMemory.AddOrUpdateTarget`:** Implement as a static method on the struct. Accumulation logic: if the entity ID is already in the memory, add to its score; if not and `Count < MaxTrackedTargets`, add a new slot; if full, replace the lowest-score slot. Sort descending by `ThreatScore` after each update.

**Tests** (new file `FDP.Toolkit.Perception.Tests/PerceptionComponentTests.cs`):
```csharp
[Fact] void TargetMemory_IsUnmanaged()
// Assert.True(typeof(TargetMemory).IsValueType)

[Fact] void TargetMemory_MaxTrackedTargets_MatchesConstant()
// Assert.Equal(PerceptionConstants.MaxTrackedTargets, /* actual fixed array length */ 4)
// This ties the constant to the struct definition.

[Fact] void AddOrUpdateTarget_AddNewSlot_WhenBelowCapacity()
// Start with empty TargetMemory, add one target
// Assert: Count == 1; EntityIds[0] == expected entity index

[Fact] void AddOrUpdateTarget_AccumulatesScore_ForKnownTarget()
// Add same entity twice with different boost values
// Assert: ThreatScores[0] == sum of boosts; Count still == 1

[Fact] void AddOrUpdateTarget_ReplacesLowestScore_WhenFull()
// Fill to MaxTrackedTargets with scores [10, 20, 30, 40]
// Add new entity with score 25
// Assert: lowest-score entity (score 10) was evicted; Count still == 4
```

---

### Task 2: AudioPerceptionSystem (BCS-P2-T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P2-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t2--audioperceptionsystem-main-thread) — lines 526–552

**Runs on main thread** — consumes `AudioStimulusEvent`, uses `GetComponentRW<TargetMemory>` for in-place mutation.

**Phase 0 adaptation (mandatory):** Task doc and design show `VehicleState.Position`. **Do not use this.** Extract listener position from `SimTransform`:
```csharp
var tf = World.GetComponent<SimTransform>(listener);
var listenerPos = new Vector2(tf.Position.X, tf.Position.Y); // XY ground plane
```
`AudioStimulusEvent.Origin` is `Vector3`; extract XY for 2D distance:
```csharp
var eventPos2D = new Vector2(evt.Origin.X, evt.Origin.Y);
float dist = Vector2.Distance(listenerPos, eventPos2D);
```

**Spatial query:** Use `SpatialHashGrid.QueryRadius(eventPos2D, evt.Intensity)` to find candidate listeners (not a brute-force full world query).

**Tests** (new file `AudioPerceptionSystemTests.cs`):
```csharp
[Fact] void AudioPerception_UpdatesTargetMemory_WhenWithinHearingRange()
// Listener at (0,0,0) with SimTransform + PerceptionReceptor.HearingRange=100
// AudioStimulusEvent.Origin=(50,0,0), Intensity=60
// sys.Run()
// Assert: TargetMemory.Count == 1
// Assert: TargetMemory.EntityIds[0] == source entity index

[Fact] void AudioPerception_IgnoresListener_OutsideHearingRange()
// Same setup with listener HearingRange=30 → distance 50 > 30
// Assert: TargetMemory.Count == 0

[Fact] void AudioPerception_IgnoresListener_OutsideEventRadius()
// Listener at (0,0,0), HearingRange=200, event at (200,0,0), Intensity=100
// Distance (200) > Intensity (100) → listener not in spatial query results
// Assert: TargetMemory.Count == 0
```

Note the distinction between the two "outside range" cases: one is the spatial grid cut-off (event radius), one is the entity's own hearing range. Both must be tested separately — they are two different code paths.

---

### Task 3: PerceptionModule + VisionBroadphaseSystem + ThreatEvaluationSystem (BCS-P2-T3)

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionModule.cs`
- `FDP/Toolkits/FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P2-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t3--perceptionmodule-async-vision-broadphase) — lines 556–600

**Async module rules — strictly enforced:**
- `PerceptionModule` inherits from `SlowBackgroundModule` (or equivalent async base).
- All reads use `ISimulationView.GetComponentRO<T>` — never `GetComponentRW`.
- All writes use `IEntityCommandBuffer.SetComponent` — never direct mutation.
- The snapshot may be shared across threads — assume it is always read-only.

**Phase 0 adaptations (mandatory):**

The task doc and design show `VehicleState.Position` and `VehicleState.Forward` in multiple places. Replace every occurrence:
- Observer/target **position**: `view.GetComponentRO<SimTransform>(e).Position` → project XY: `new Vector2(pos.X, pos.Y)`.
- Observer **forward vector**: 
  ```csharp
  var tf = view.GetComponentRO<SimTransform>(observer);
  Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation); // X-forward (our convention)
  Vector2 forward = Vector2.Normalize(new Vector2(fwd3D.X, fwd3D.Y));
  ```
  Do **not** use `Vector3.UnitY` — this was the bug fixed in BATCH-01. Use `Vector3.UnitX`.

**Threat score decay:** In `ThreatEvaluationSystem`, use a named constant for the decay rate:
```csharp
// In PerceptionConstants.cs:
public const float ThreatScoreDecayPerSecond = 0.1f;
```

**Tests:**
```csharp
[Fact] void VisionBroadphase_ExcludesSameFaction()
// Observer (Blue) + potential target (Blue) within vision range and FOV
// Assert: no LosCheckRequestEvent emitted (same faction skipped)

[Fact] void VisionBroadphase_ExcludesEntity_OutsideFOV()
// Observer facing East, target directly North (90° off axis)
// FieldOfViewCos = cos(30°) ≈ 0.866 → dot product ≈ 0 < 0.866
// Assert: no event emitted

[Fact] void VisionBroadphase_EmitsLosCheckRequest_ForEnemyInFOV()
// Observer (Blue) + target (Red) within range, within FOV
// Assert: LosCheckRequestEvent emitted with correct observer+target entity IDs
// Assert: event.ObserverEntity == observer, event.TargetEntity == target

[Fact] void ThreatEvaluation_DecaysExistingScore()
// Entity with TargetMemory.ThreatScores[0] = 100f
// dt = 1.0f → ThreatScoreDecayPerSecond applies
// Assert: after one run, score ≈ 100f - (100f * ThreatScoreDecayPerSecond * dt) == 90f
```

The `VisionBroadphase_EmitsLosCheckRequest_ForEnemyInFOV` test must check the **specific entity IDs** in the emitted event — not just that *an* event was emitted.

---

### Task 4: LosRequestBatchingSystem (BCS-P2-T4)

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P2-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p2-t4--losrequestbatchingsystem--targetmemory-integration) — lines 604–626

For Phase 2 (no real terrain geometry): implement `LOS_MOCK_MODE` as a compile-time or constructor-time flag. When enabled, skip ray submission and directly emit `TargetVisibleEvent` for each `LosCheckRequestEvent` received. This keeps the pipeline testable without a physics engine dependency.

**Test:**
```csharp
[Fact] void LosRequestBatching_InMockMode_EmitsTargetVisibleEvent_ForEachRequest()
// Publish two LosCheckRequestEvents
// sys.Run() in mock mode
// Assert: two TargetVisibleEvent published on the bus
// Assert: each event's ObserverEntity and TargetEntity match the source request
// (specific entity IDs, not just count)
```

---

## 🧪 Testing Requirements

- **Minimum 14 new tests:** 3 corrective fixes + 5 perception component + 3 audio + 4 broadphase/los.
- **`PerceptionTestWorldFactory.Create()`** in the test project — pre-registers `SimTransform`, `SimVelocity`, `Faction`, `PerceptionReceptor`, `TargetMemory`.
- Every test that checks "event was emitted" must verify **entity IDs inside the event**, not just count.
- Every test that checks "entity was ignored" must provide a clear setup that demonstrates the exact exclusion criterion (range vs. hearing vs. faction vs. FOV — each is a separate test).
- All pre-existing tests must remain green.

---

## ⚠️ Quality Standards

See `.dev-workstream/guides/CODE-STANDARDS.md` — all rules apply.

**❗ No `VehicleState.Position` or `VehicleState.Forward` anywhere in Perception code** — always use `SimTransform`. The design and task docs in some places still show the old API — ignore those references.

**❗ Forward vector from quaternion uses `Vector3.UnitX`**, not `Vector3.UnitY`. See SimComponents.cs and BATCH-01 corrective for the why.

**❗ `PerceptionModule` is async — only `GetComponentRO` + ECB.** Zero direct world mutations. One violation means the entire SoD model breaks.

**❗ `PerceptionConstants.MaxTrackedTargets = 4`** in all fixed array declarations — no raw `4`.

**❗ `ThreatScoreDecayPerSecond` is a named constant** — no raw `0.1f` in production code.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-05-REPORT.md`:

- **Test results:** `dotnet test FDP.sln` summary.
- **Q1:** Did `HsmKernel` / `HsmInstance128` expose a typed event push API, or did you have to use `Reserved1`? What exactly is the field mapping and how did you document it?
- **Q2:** When implementing `VisionBroadphaseSystem` in the async module, how did you access `SpatialHashGrid`? Is it part of the snapshot, or does the module receive it via constructor injection like `DoctrineRegistry`?
- **Q3:** The `ThreatEvaluationSystem` uses `ECB.SetComponent<TargetMemory>` — walk through the read-modify-write: where does the read happen (snapshot), where does the write happen (ECB), and at what point during the frame does the ECB flush?
- **Q4:** Was the `LOS_MOCK_MODE` a compile flag or a constructor parameter? What trade-offs did you consider?

---

## 🎯 Success Criteria

- [ ] **Corrective A** — `HsmTick_TransitionsState` uses named constant or proper API for event injection; no raw `Reserved1 = 10`
- [ ] **Corrective B** — `DoctrineIngress_Stale...` Test 4 asserts `channel.DoctrineInstanceId == 0`
- [ ] **Corrective C** — `InputSystemGroup` has XML comment documenting required registration order
- [ ] **BCS-P2-T1** — `PerceptionConstants.cs`; all fixed arrays use `MaxTrackedTargets`; `AddOrUpdateTarget` implemented; 5 component tests pass
- [ ] **BCS-P2-T2** — `AudioPerceptionSystem`; 3 hearing-range tests pass with specific entity assertions
- [ ] **BCS-P2-T3** — `PerceptionModule` + broadphase + threat evaluation; 4 tests pass including entity-ID assertions; async module uses only `GetComponentRO` + ECB
- [ ] **BCS-P2-T4** — `LosRequestBatchingSystem`; mock mode implemented; 1 test verifying entity IDs in emitted events
- [ ] **No `VehicleState` reads in Perception code** — grep confirms zero occurrences
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-04 Review:** `.dev-workstream/reviews/BATCH-04-REVIEW.md`
- **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Task Details (BCS-P2-T1–T4):** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 488–626
- **Design §4.1–4.4:** `FDP/Docs/projects/behavior-control/DESIGN.md`
- **SpatialHashGrid API:** `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashGrid.cs`
- **SimComponents:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` — coordinate convention
- **SimMath:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs` — forward extraction helpers
- **Note on design doc references to `VehicleState`:** Treat all `VehicleState.Position` and `VehicleState.Forward` references in the design and task doc as referring to `SimTransform` — the migration happened in Phase 0. The design docs were not updated to reflect this and will be corrected in a future documentation pass.
