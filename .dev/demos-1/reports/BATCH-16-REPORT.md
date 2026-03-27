# BATCH-16 Report

**Batch:** BATCH-16 (DEM1 / repo-root)
**Developer:** GitHub Copilot
**Date:** 2026-03-27
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| Phase 0 — Item 1: Remove `NetworkDemo` ProjectReference | ✅ Investigated / Retargeted | Reference is actively used — see below |
| Phase 0 — Item 2: Fix CS8602 in `UrbanCombatNewScenario.cs` | ✅ Done | Null guard added at line 797 |
| Phase 1 — DEM1-DESIGN.md §6.5 latch table sync | ✅ Done | Table updated to match implementation with spec caveat notes |
| Phase 1 — DEM1-TASK-DETAIL.md D010 pseudo-code sync | ✅ Done | Comments + success conditions updated |
| Phase 2 — Latch3 / Latch4 tick-boundary test hardening | ⏭ Skipped | Documented per batch instructions |

---

## Phase 0 — Tech Debt

### Item 1: `Fdp.Examples.NetworkDemo` ProjectReference

**Finding — reference is NOT dead.**

BATCH-15 review noted "no .cs file in Fdp.Examples.Scenarios imports that assembly." Investigation revealed this was incorrect:

- `FDP/Examples/Fdp.Examples.Scenarios/Perception/TerrainClampingScenario.cs` line 6:
  ```csharp
  using Fdp.Examples.NetworkDemo.Systems;
  ```
- Uses `TransformSyncSystem` from that namespace (`_transformSync = new TransformSyncSystem(driveFromNetwork: true)`).

`TransformSyncSystem` is **only** defined in `Fdp.Examples.NetworkDemo.Systems` (not in `Fdp.Modules.Geographic.Systems`). Removing the `ProjectReference` would break the build.

**Decision:** Retargeted the DEBT-TRACKER row with an accurate description. True resolution would require moving `TransformSyncSystem` to a shared toolkit (e.g., `FDP.Toolkit.Replication`), which is out of scope for a 12–22 hour hygiene batch. Retargeted to BATCH-17+.

**DEBT-TRACKER:** Updated row to correct the description and set target to `BATCH-17+`.

---

### Item 2: CS8602 — `UrbanCombatNewScenario.cs` ~line 800

**Root cause:** `DoctrineDefinition.HsmDefinition` is declared `HsmDefinitionBlob?` (nullable reference type). The if-condition `_doctrineRegistry.TryGetDefinition(...)` guarded against the doctrine being absent, but not against the doctrine existing with a null `HsmDefinition`.

**Fix:**
```csharp
// Before:
if (_doctrineRegistry.TryGetDefinition(DoctrineConvoyEscort, out var convoyDef))

// After:
if (_doctrineRegistry.TryGetDefinition(DoctrineConvoyEscort, out var convoyDef)
    && convoyDef.HsmDefinition != null)
```

File: `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`

Verified: `dotnet build Fdp.Examples.Scenarios.csproj` → `Build succeeded.` with no CS8602 from this file.

**DEBT-TRACKER:** Marked ✅ BATCH-16.

---

## Phase 1 — DEM1-D010 Specification Alignment

All three table rows in BATCH-16 instructions had observable drift. Chosen strategy: **update docs to describe implemented observables**, with explicit spec caveat callouts so future agents know what was changed and why.

### DEM1-DESIGN.md §6.5 changes

| Latch | Before | After |
|-------|--------|-------|
| AmbushFired | `FireRequestEvent` from Insurgent | `WeaponChannel.ActiveAction == AimAndFire` on Insurgent *(spec note added)* |
| InsurgentHit | `HitEvent.HitEntity == Insurgent` | `Health.Current < SoldierMaxHealth` on Insurgent *(spec note added)* |
| InsurgentKilled | `world.IsAlive(insurgent) == false` | `!world.IsAlive(insurgent)` (equivalent; standardised syntax) |
| MissionResumed | `APC Loco == MoveTo or FollowRoute` | Log line `"Mission Resumed"` emitted *(spec caveat: HSM Disabled→Cruising recovery not yet implemented)* |

ApcHalted condition was already correct — no change needed.

### DEM1-TASK-DETAIL.md D010 changes

- Latch comments in pseudo-code block updated from `FireRequestEvent`, `HitEvent.HitEntity`, and `APC loco` to match the implementation (`WeaponChannel`, health drop, log line).
- Each comment includes a `(Note: ...)` explaining the original spec intent and why the implementation differs.
- `UrbanCombatNew_Latch1_InsurgentFiresWithin100Ticks` success condition updated to say `Insurgent WeaponChannel.ActiveAction == AimAndFire` (was "inspect via DemoScenarioTracker").
- `UrbanCombatNew_Latch5_MissionResumes` success condition clarified: asserts log contains "Mission Resumed"; note added that APC loco FollowRoute/MoveTo is not asserted and why.

**DEBT-TRACKER:** Marked P2 doc drift row ✅ BATCH-16.

---

## Phase 2 — Test Hardening (Optional)

Skipped per batch instructions ("optional — documented if skipped"):

- `UrbanCombatNew_Latch3_InsurgentHit`: The `LatchInsurgentHit` field is indirectly covered because `UrbanCombatNew_RunToCompletion_ExitsZero` and `UrbanCombatNew_Latch4_InsurgentDies` both require latch 3 before latch 4 can fire. Adding an isolated test adds little coverage signal given the cascade dependency.
- `UrbanCombatNew_Latch4_InsurgentDies` tick-boundary ≤400: Tick ordering is non-deterministic across hardware; adding a hard tick-boundary would make the test brittle on slower CI agents. Deferred.

---

## 🧪 Testing Results

**Unit / Integration Tests Passed:** 65 / 65 (`Fdp.Examples.Scenarios.Tests`)

```
Passed!  - Failed:     0, Passed:    65, Skipped:     0, Total:    65
```

All five `UrbanCombatNewScenario` tests included and passing.

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The BATCH-15 review claimed `Fdp.Examples.Scenarios.csproj` had a dead `NetworkDemo` reference, but investigation found `TerrainClampingScenario.cs` actively uses `TransformSyncSystem` from that assembly. The review's inspection likely excluded the `Perception/` subfolder, treating only the `Integrated/` files. Resolved by resetting the debt row description to accurately document the coupling.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

`TransformSyncSystem` lives in `Fdp.Examples.NetworkDemo` (an example project) but is used by another example project (`Fdp.Examples.Scenarios`). This cross-example coupling is fragile — it would be cleaner in `FDP.Toolkit.Replication` or `FDP.Toolkit.Geographic`. Not acted on in this batch (out of scope).

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

For Phase 1, the alternative was to change the code to match the spec (add event-bus listeners for `FireRequestEvent` and `HitEvent`). Chose doc-update approach because: (a) the implemented observables are semantically equivalent for this single-template scenario, (b) event-bus changes risk breaking the passing test suite, and (c) BATCH-16 instructions explicitly say "Minimum: Update docs to describe the implemented latches."

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

The CS8602 fix shows `TryGetDefinition` can return `true` while `HsmDefinition` is null (a BTree-only doctrine). The guard `&& convoyDef.HsmDefinition != null` correctly handles this without changing existing semantics — if the convoy doctrine has no HSM blob, the brain pre-init is simply skipped (the run still proceeds).

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

None introduced or discovered in this batch.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **NetworkDemo decoupling (BATCH-17+):** Move `TransformSyncSystem` out of `Fdp.Examples.NetworkDemo` into a proper toolkit to remove the cross-example project dependency.
- [ ] **HSM recovery (Latch 5 stretch, BD1-BATCH-04+):** Implement `Disabled → Cruising` HSM transition on `RecoveryComplete`-style event so Latch 5 can assert APC loco instead of a log line.
- [ ] **Latch3 explicit test (Phase 2, deferred):** If a future batch adds `LatchInsurgentHit` assertions, no structural changes needed — the field is already public on `UrbanCombatNewScenario`.
