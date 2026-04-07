# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Tasks covered:** Corrective-0 (NetworkLifecycleSystemGroup), EAM-E001, EAM-E002, EAM-E003  
**Outcome:** ✅ APPROVED (minor deviations noted — P2 debt recorded)

---

## 1. Build & Test Results

| Suite | Count | Result |
|---|---|---|
| `Hrot.ClusterRunner.Tests` (filter: NedReplication\|EyesAndMuscle) | 12 | ✅ All passing |
| `Hrot.ClusterRunner.Integration.Tests` (filter: EyesAndMuscle) | 3 | ✅ All passing |
| `dotnet build IOS-IG-SimHost.sln` | — | ✅ Succeeded, 0 errors |

---

## 2. Corrective-0 — NetworkLifecycleSystemGroup (EAM-BATCH-01 P2 Debt)

**Result:** ✅ RESOLVED

`NetworkLifecycleSystemGroup` was added to `NedReplicationModule` via a private property + explicit `Tick()` call inside `NedReplicationModule.Tick()`. This is the correct approach: `NetworkLifecycleSystemGroup` does not implement `IEcsModuleSystem`, so it cannot be registered via `ISystemRegistry.RegisterSystem`. The property approach mirrors the existing pattern used by other non-system lifecycle managers in the codebase.

**P2 debt row removed from tracker** (closed).

---

## 3. EAM-E001 — EyesAndMuscleSubsystem

**Files:** `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`  
**Result:** ✅ Approved

### Deviation D1 — `NodeRole.AllInOne` used instead of `NodeRole.MuscleGround | NodeRole.ImageGenerator`

**Finding:** `NodeRole` is a **plain enum** (no `[Flags]` attribute). Bitwise OR of two enum members yields a raw integer (`MuscleGround=1, ImageGenerator=2 → 3`) not equal to any valid enum value. Writing `NodeRole.MuscleGround | NodeRole.ImageGenerator` would produce a silently-wrong comparison result at runtime.

**Assessment:** ✅ Developer decision was correct. `AllInOne` is the intended combined value per the enum's XML doc ("All subsystems in a single process — the default standalone mode"). No debt item created.

### Deviation D2 — `SimulationLogicModule` omitted

**Spec:** EAM-E001 spec required `SimulationLogicModule(role)` registered after `NedReplicationModule`.  
**Developer justification:** `SimulationLogicModule` uses the old SystemGroup API incompatible with `kernel.RegisterModule(IEcsModule)`. The muscle execution path is handled by `EyesAndMuscleModule.Tick()` instead.

**Assessment:** ⚠️ Acceptable for Phase 3 PoC — behavioural goals (SoD async snapshot, Eyes + Muscle Tick proofs) are met. However it means Phase 4 migration of `SimHostApp` must either:
(a) continue omitting `SimulationLogicModule` (risk: missing production simulation logic), or  
(b) fix the `SimulationLogicModule` API incompatibility as a prerequisite to EAM-M001.

**Debt recorded:** P2 — track in DEBT-TRACKER (see Section 5). BATCH-03 must include a Corrective Task 0 to resolve or explicitly scope it out.

### Other checks

- `Initialize` guard (double-init throws) ✅  
- `Shutdown` idempotent ✅  
- `Update` null-safe with early return ✅  
- `DrawWorld`/`DrawUI`/`GetMapCamera`/`RegisterWindows` documented stubs ✅  

---

## 4. EAM-E002 — EyesAndMuscleModule

**Files:** `Hrot.ClusterRunner/Services/EyesAndMuscleModule.cs`  
**Result:** ✅ Approved

- `Policy => ExecutionPolicy.SlowBackground(60)` — correct async+SoD+60 Hz policy ✅  
- Direct Execution pattern (`RegisterSystems` no-op) ✅  
- `EyesTicks`, `MuscleTicks`, `LastTickThreadId` test seams ✅  
- Muscle activation check: `(role == NodeRole.MuscleGround) || (role == NodeRole.AllInOne)` — correct given non-Flags enum ✅  
- Eyes query: `SimTransform + NetworkIdentity` — correct ✅  
- Muscle query: `NavigationIntent + SimTransform` with `DirectPoint` guard ✅  
- `IEntityCommandBuffer` used for write-back (no direct mutation) ✅ thread-safety maintained  

---

## 5. EAM-E003 — Integration Tests

**Files:** `Hrot.ClusterRunner.Integration.Tests/EyesAndMuscleIntegrationTests.cs`  
**Result:** ✅ Approved

| Test | Assertion | Result |
|---|---|---|
| `BootsAndRunsHeadless` | World non-null, entity count = 0 after 50 frames | ✅ |
| `TicksIncrementAfter60Frames` | EyesTicks > 0 AND MuscleTicks > 0 | ✅ |
| `AsyncModuleRunsOnBackgroundThread` | LastTickThreadId ≠ main thread ID | ✅ |

All tests use `HrotRunnerHarness` headless mode — no DDS dependency. ✅

---

## 6. Technical Debt

### New items from BATCH-02

| Priority | Category | Description | Target Fix |
|---|---|---|---|
| P2 | Architecture | `SimulationLogicModule` omitted from `EyesAndMuscleSubsystem`. If SimHostApp migration (EAM-M001) must preserve `SimulationLogicModule` usage, the old SystemGroup API incompatibility must be resolved before or during BATCH-03. | BATCH-03 Corrective-0 |

### Closed items from prior batches

| Item | Status |
|---|---|
| P2: `NetworkLifecycleSystemGroup` not registered in `NedReplicationModule` | ✅ Resolved — Corrective-0 |

---

## 7. Suggested Commit Message

```
feat: Phase 3 - EyesAndMuscleSubsystem + async SoD module + integration tests

- EyesAndMuscleSubsystem (EAM-E001): ISubsystem + IMapCameraProvider + IWindowRegistrar;
  uses HrotNodeBuilder directly (no inner App class); initialises NedReplicationModule
  and EyesAndMuscleModule; Update/Shutdown/DrawWorld/DrawUI all guarded correctly.

- EyesAndMuscleModule (EAM-E002): async SoD PoC at 60 Hz via ExecutionPolicy.SlowBackground;
  Direct Execution pattern (RegisterSystems no-op); EyesTicks/MuscleTicks/LastTickThreadId
  test seams; muscle path guarded by NodeRole.AllInOne|MuscleGround check.

- Integration tests (EAM-E003): 3 tests covering boot, tick increment, and async
  thread-ID assertion; all pass headless with HrotRunnerHarness.

- Corrective-0: NedReplicationModule now holds NetworkLifecycleSystemGroup via property
  and calls Tick() explicitly (not ISystemRegistry — NLSG is not IEcsModuleSystem).

Closes: EAM-E001, EAM-E002, EAM-E003
P2 debt: SimulationLogicModule omitted (old SystemGroup API incompatible) — tracked in DEBT-TRACKER
```

---

## 8. Next Batch

- **BATCH-03** covers:
  - Corrective-0: Investigate and resolve `SimulationLogicModule` API gap (or explicitly document it as out-of-scope for this migration)
  - **EAM-M001**: Migrate `SimHostApp.OnLoad` to `HrotNodeBuilder` + `NedReplicationModule`; delete `EnsureIdAllocatorRouting` private method
  - **EAM-M002**: Migrate `IgApplication.InitializeEmbedded` to `HrotNodeBuilder` + `NedReplicationModule(ImageGenerator)`
  - **EAM-M003**: Migrate `CgfSubsystem.Initialize` to `HrotNodeBuilder` + `NedReplicationModule(Brain)`
  - Move `DdsIdAllocatorHelper` to `Hrot.Common` to close circular-dependency P2 debt from BATCH-01
