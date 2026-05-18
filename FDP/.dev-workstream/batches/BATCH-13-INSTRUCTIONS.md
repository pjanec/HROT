# BATCH-13: Pre-Demo Debt Resolution

**Batch Number:** BATCH-13  
**Purpose:** Clear all open P2 and P3 debt items before Phase 7 (Demo App) begins.  
**Estimated Effort:** 8–10 hours  
**Priority:** MANDATORY — no Phase 7 work may begin until this batch is approved.

> **Governing rule (from user):** Tech debt items must be resolved before the demo app. This batch closes all remaining open P2 items (DEBT-006, DEBT-007, DEBT-024, DEBT-033) and all open P3 items (DEBT-008, DEBT-022, DEBT-031, DEBT-034).

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-12 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-12-REVIEW.md`
2. **DEBT-TRACKER.md (read every open item):** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
3. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`

### Relevant Source Files

Before starting, read each file touched by a debt item:

| Debt | File(s) to read |
|---|---|
| DEBT-006 | `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs` |
| DEBT-007 | `FDP/Toolkits/FDP.Toolkit.Behavior/FdpHsmContext.cs` (if exists); `BehaviorComponents.cs`; `HsmTickSystem.cs` |
| DEBT-008 | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs` |
| DEBT-022 | `FDP/Toolkits/FDP.Toolkit.Physics/Math/Intersection2D.cs`; `Intersection2DTests.cs` |
| DEBT-024 | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs` |
| DEBT-031 | `Kernel/Fdp.Kernel/Events/HitEvent.cs`; `FDP.Toolkit.Physics.csproj`; `FDP.Toolkit.Combat.csproj` |
| DEBT-033 | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`; `Kernel/Fdp.Kernel/` (for new shared type) |
| DEBT-034 | `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-13-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

Work through debts in priority order (P2 first, P3 second). After each fix, run the tests for the affected project before moving on. All tests must be green before submitting.

---

## ✅ Debt Items (P2 — Required)

### DEBT-006: `BehaviorRegistry` unstable hash key

**Source:** BATCH-04-REVIEW  
**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`

**Problem:** `BehaviorRegistry` keys behaviors on `string.GetHashCode()`. In .NET, `string.GetHashCode()` is process-randomised (changed at startup for security). This means:
- Behavior IDs are different every run.
- Any serialised or logged behavior ID cannot be reproduced.
- Cross-process or network-replicated behavior identity is impossible.

**Fix:** Replace the key with a **stable assigned `int` ID**. The simplest correct approach:
1. Add a `Id` field to the registration call: `BehaviorRegistry.Register(int id, string name, ...)`.
2. The registry stores `Dictionary<int, BehaviorEntry>` keyed by the assigned `int`.
3. Change `BehaviorState.ActiveBehaviorHash` from whatever it currently is to an `int` (if not already).
4. Update all existing `Register(...)` call sites with a unique `int` constant. Define these in a new `BehaviorIds` static class in the Behavior toolkit:

```csharp
public static class BehaviorIds
{
    public const int None         = 0;
    public const int WanderCivil  = 1001;
    public const int PanicFlee    = 1002;
    public const int ConvoyEscort = 2001;
    public const int InfantryCombat = 2002;
    public const int Ambush       = 2003;
}
```

**Tests required:**
```csharp
[Fact] void BehaviorRegistry_LookupById_ReturnsCorrectEntry()
// Register(id=42, ...) → Lookup(42) returns the registered entry.

[Fact] void BehaviorRegistry_LookupById_IsStableAcrossInstances()
// Registered id=42 in instance A matches id=42 in instance B.
// (Just verifies int key — two separate BehaviorRegistry instances)

[Fact] void BehaviorRegistry_ReturnsNull_ForUnregisteredId()
// Lookup(9999) returns null / false.
```

> ⚠️ **Migration required:** If `MissionDirectorSystem` compares `behavior.ActiveBehaviorHash` against `BehaviorId` values in `MissionPhase`, confirm those int values already align. Update `MissionDirectorSystemTests` to use `BehaviorIds` constants if available.

---

### DEBT-007: `FdpHsmContext` cannot access ECS world

**Source:** BATCH-04-REPORT  
**File:** Likely `FDP/Toolkits/FDP.Toolkit.Behavior/` — find files referencing `FdpHsmContext`

**Problem:** `FdpHsmContext` passed to HSM action delegates carries only `Entity Self`. HSM action delegates in the demo app (e.g., `Activity_Cruise`, `OnEnter_Disabled`) need to write to ECS components (`LocomotionChannel`, `InteractionChannel`). Without ECS access in the context, these delegates cannot function.

**Investigation step (MANDATORY):** First read `FdpHsmContext` and `HsmTickSystem` to understand the current API. Then determine the correct fix from these options:

**Option A (preferred if feasible):** Add a `WorldRef` or `IWorldAccess` field to `FdpHsmContext`:
```csharp
public ref struct FdpHsmContext
{
    public Entity Self;
    public EntityRepository World;  // or IEntityReader/IEntityWriter
}
```
Since `ref struct` cannot be stored on the heap, this is thread-safe and allocation-free.

**Option B:** Pass `EntityRepository` directly as an additional parameter to the action delegate signature if the delegate type supports it.

**Option C:** Use a thread-local or static ambient accessor (avoid — not thread-safe for parallel HSM evaluation).

Choose Option A unless the delegate signature is sealed by the `Fhsm` library. If `ref struct` is not supported in delegate constraints, document the limitation in Q1.

**Tests required:**
```csharp
[Fact] void FdpHsmContext_ExposesWorldAccess()
// Create context, assert World property is set and accessible.
// (Structural test — confirms the field exists and is populated by HsmTickSystem)
```

---

### DEBT-024: `DispatcherSystemBase` — no `OnExit` on entity destruction

**Source:** BATCH-08-REVIEW (Q1)  
**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs`

**Problem:** When an entity is destroyed mid-action (e.g., a soldier embarked in a vehicle is killed by a `DamageSystem` call), the dispatcher never calls `OnExit` on the active executor. Any executor-internal state stored in `channel.State[32]` bytes is silently abandoned. For executors like `EmbarkExecutor` this is acceptable, but for executors that hold external resources or reserve slots, it is a latent leak.

**Full fix** requires a kernel lifecycle hook (entity-death callback). That is a larger change. The acceptable **pragmatic fix** for this batch:

1. In `DispatcherSystemBase.OnUpdate()`, add a dead-entity check at the top of the per-entity loop. If the entity with an active channel is no longer alive, call `OnExit` and clear the channel before continuing.
2. Add a comment noting that this guard only fires on "already-dead" entities detected at the next dispatcher tick — there remains a one-frame gap between destruction and `OnExit`.

```csharp
// Guard: if entity was destroyed in a previous frame and its channel
// component was not yet cleaned up, call OnExit now to avoid state leaks.
// Note: there is still a one-frame gap where OnExit is not called in the
// same frame as destruction (DEBT-024 partial mitigation).
if (!World.IsAlive(entity))
{
    if (channel.ActiveAction != 0)
        _executors[channel.ActiveAction].OnExit(entity, ref channel, World);
    // Cannot write back — entity is dead. Log/assert if needed.
    continue;
}
```

> **Kernel lifecycle hook** is a future concern (Phase 8+). This partial fix reduces the risk for the demo.

**Tests required:**
```csharp
[Fact] void Dispatcher_CallsOnExit_WhenEntityDestroyedMidAction()
// Entity with active action. Destroy entity. Run dispatcher.
// Assert: OnExit was invoked (use a test executor that records OnExit calls).
// Assert: no exception thrown.
```

---

### DEBT-033: `MissionDirectorSystem.HealthCritical` trigger not implemented

**Source:** BATCH-11-REVIEW  
**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`

**Problem:** The `HealthCritical` trigger cannot be implemented because `FDP.Toolkit.Behavior` cannot reference `FDP.Toolkit.Combat` (circular dependency). The trigger silently never fires.

**Fix:** Add a lightweight `HealthData` value struct to `Fdp.Kernel` (where it can be accessed by both Behavior and Combat without circularity):

```csharp
// Fdp.Kernel/Components/HealthData.cs — thin universal health primitive
[StructLayout(LayoutKind.Sequential)]
public struct HealthData
{
    public float Current;
    public float Max;
    /// <summary>Fraction [0..1]; 0 = dead, 1 = full health.</summary>
    public float Fraction => Max > 0f ? Current / Max : 0f;
}
```

`FDP.Toolkit.Combat` already has `Health` (its own combat-specific component). The fix is:
1. Add `HealthData` to `Fdp.Kernel`.
2. Add `HealthData` as an **additional** component on combat entities (alongside `Health`), kept in sync by `DamageSystem`: after applying damage, write `World.SetComponent(evt.HitEntity, new HealthData { Current = health.Current, Max = health.Max })`.
3. `MissionDirectorSystem` reads `HealthData` (from Kernel, no circular dep) to evaluate `HealthCritical`.

> **Alternative (simpler):** Only add `HealthData` to `Fdp.Kernel`; have `DamageSystem` also set it as a parallel component. `MissionDirectorSystem` reads `HealthData`. The full `Health` component stays in Combat. No component duplication at the struct level — only two ECS registrations for the same logical data.

**Tests required (in `MissionDirectorSystemTests.cs`):**
```csharp
[Fact] void MissionDirector_AdvancesPhase_WhenHealthCritical()
// Entity with HealthData{Current=5f, Max=100f}. Phase trigger=HealthCritical(threshold=0.1f).
// 5/100 = 0.05 <= 0.10 → trigger fires.
// Assert: CurrentPhase == 1.

[Fact] void MissionDirector_DoesNotAdvance_WhenHealthAboveThreshold()
// HealthData{Current=50f, Max=100f}. threshold=0.1f.
// 50/100 = 0.5 > 0.1 → no advance.
```

---

## ✅ Debt Items (P3 — Required before Phase 7)

### DEBT-008: `BehaviorIngressSystem` — no try/catch around `ParseParams`

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs`

**Fix:** Wrap the `ParseParams` delegate invocation in a try/catch. On exception: log the behavior name and entity index, set `BehaviorState` to a known-safe default (e.g., clear `ActiveBehaviorHash`), skip the entity.

```csharp
try
{
    entry.ParseParams(json, blackboardPtr);
}
catch (Exception ex)
{
    // Log: $"BehaviorIngressSystem: ParseParams failed for entity {entity.Index}, behavior '{entry.Name}': {ex.Message}"
    // Fail safe: leave BehaviorState unchanged (do not bump InstanceId).
    continue;
}
```

**Tests required:**
```csharp
[Fact] void BehaviorIngress_DoesNotThrow_WhenParseParamsFails()
// Register a behavior with a ParseParams delegate that throws.
// Publish a BehaviorChangeEvent for that behavior.
// Run BehaviorIngressSystem.
// Assert: Record.Exception() returns null.
// Assert: entity's BehaviorState is unchanged (did not advance InstanceId).
```

---

### DEBT-022: `Intersection2DTests` — missing t=0 boundary test

**File:** `FDP/Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs`

**Fix:** Add one test for the degenerate case where the ray origin is exactly on the circle boundary (t=0):

```csharp
[Fact]
public void RaycastCircle_ReturnsZero_WhenRayStartsOnCircleEdge()
// Segment: from (radius, 0) pointing inward.
// Circle: centre=(0,0), radius=r.
// Ray starts exactly on the circle surface → first intersection t=0.
// Returned t must be approximately 0 (within epsilon) and HasHit==true.
```

Note: the current implementation may return t=0 or the far intersection t=2r/length. Verify actual behaviour and assert accordingly. If it returns the far intersection (t > 0), document this as the defined behaviour.

---

### DEBT-031: `HitEvent` in `Fdp.Kernel` — architectural resolution

**Files:** `Kernel/Fdp.Kernel/Events/HitEvent.cs`; `FDP.Toolkit.Physics.csproj`; `FDP.Toolkit.Combat.csproj`

**Problem:** `HitEvent` is a combat game event living in the engine kernel, violating kernel purity.

**Fix:** Move `HitEvent` to a new thin assembly `FDP.Toolkit.Combat.Contracts`:

1. Create `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/FDP.Toolkit.Combat.Contracts.csproj` — references only `Fdp.Kernel`.
2. Move `HitEvent.cs` there.
3. `FDP.Toolkit.Physics.csproj` adds `<ProjectReference>` to `FDP.Toolkit.Combat.Contracts`.
4. `FDP.Toolkit.Combat.csproj` adds `<ProjectReference>` to `FDP.Toolkit.Combat.Contracts`.
5. Remove `HitEvent.cs` from `Fdp.Kernel`.
6. Update all `using` directives.
7. Remove `<ProjectReference>` to `FDP.Toolkit.Combat` from `FDP.Toolkit.Physics.csproj` (it now only references Contracts, not the full Combat toolkit).

> **Verify no circular dependency after:** `Physics → Combat.Contracts`, `Combat → Combat.Contracts`. Neither `Combat.Contracts → Physics` nor `Combat.Contracts → Combat`.

This restores kernel purity and removes the semantic violation.

**Tests:** Existing tests must still pass (no new tests needed — this is a namespace move).

---

### DEBT-034: `EjectPassengersExecutor` XML doc comment wrong slot offsets

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` (lines 24–25)

**Fix** (one-liner): Change the XML doc to reflect the actual formula output:
```
For 2 passengers: offsets are −1.5 m and 0.0 m on X.
For 4 passengers: offsets are −3.0 m, −1.5 m, 0.0 m, +1.5 m on X.
```

No logic change. No new tests.

---

## 🧪 Testing Requirements

- **Minimum 9 new tests:** 3 BehaviorRegistry + 1 FdpHsmContext + 1 Dispatcher OnExit + 2 MissionDirector HealthCritical + 1 BehaviorIngress + 1 Intersection2D boundary.
- **All existing tests must remain green** (no regressions).
- **DEBT-031 (HitEvent move) must have zero test failures** — existing tests cover it; just confirm they still pass.

---

## ⚠️ Quality Standards

**❗ DEBT-006 fix must not use `string.GetHashCode()`** anywhere for behavior ID computation. The integer constant is the identity. If you see a `GetHashCode()` call in the behavior lookup path, it must be removed.

**❗ DEBT-007 fix** — do not use static/thread-local ambient state for `World` access. The `FdpHsmContext` field approach is the only acceptable pattern.

**❗ DEBT-024 fix** — the guard must call `OnExit` ONLY if `channel.ActiveAction != 0` (no executor registered for action 0). Never call `_executors[0]` — it may be null.

**❗ DEBT-033 fix** — `HealthData` goes in `Fdp.Kernel`, NOT in `FDP.Toolkit.Behavior`. Do not create a circular dependency.

**❗ DEBT-031 fix** — after the move, run `dotnet build FDP.sln` and verify zero errors before submitting. This is a cross-project structural change.

**❗ DEBT-034** — documentation only, no logic changes. Verify the new text matches Q3 output from the BATCH-12 report exactly.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-13-REPORT.md`

**Q1:** For DEBT-007 (`FdpHsmContext`), what was the constraint that made Option A/B/C the correct choice? Show the actual `FdpHsmContext` struct before and after.

**Q2:** For DEBT-006 (`BehaviorRegistry`), how many existing call sites used `string.GetHashCode()`-based lookup and needed updating? Did `MissionDirectorSystem` need any changes due to this?

**Q3:** For DEBT-031 (`HitEvent` move), list the exact files that changed and which direction each reference flowed before and after. Confirm the final dependency graph for `Physics`, `Combat`, and `Combat.Contracts`.

**Q4:** For DEBT-033 (`HealthCritical`), describe the `DamageSystem` change to sync `HealthData`. Is there a risk of `HealthData` being read one frame stale?

**Q5:** Any surprises or edge cases encountered?

---

## 🎯 Success Criteria

- [ ] **DEBT-006** — `BehaviorRegistry` uses stable `int` key; `BehaviorIds` constants class exists; 3 tests pass.
- [ ] **DEBT-007** — `FdpHsmContext` exposes `EntityRepository World`; 1 structural test passes.
- [ ] **DEBT-008** — `BehaviorIngressSystem` try/catch around `ParseParams`; 1 test passes.
- [ ] **DEBT-022** — `Intersection2DTests` t=0 boundary test added and passes.
- [ ] **DEBT-024** — `DispatcherSystemBase` dead-entity OnExit guard; 1 test passes.
- [ ] **DEBT-031** — `HitEvent` in `FDP.Toolkit.Combat.Contracts`; `Fdp.Kernel` clean; 0 new test failures.
- [ ] **DEBT-033** — `HealthData` in `Fdp.Kernel`; `DamageSystem` syncs it; 2 `MissionDirectorSystemTests` pass.
- [ ] **DEBT-034** — EjectPassengersExecutor XML doc corrected; no logic changes.
- [ ] **Full solution: 0 errors, all tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
- **BATCH-12 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-12-REVIEW.md`
- **DESIGN.md §3.2:** `FDP/Docs/projects/behavior-control/DESIGN.md` — BehaviorRegistry, dispatcher system
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
