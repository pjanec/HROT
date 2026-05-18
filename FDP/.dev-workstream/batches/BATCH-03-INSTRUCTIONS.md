# BATCH-03: SimMath Helper + GetComponentRW Fix + Three Dispatcher Systems

**Batch Number:** BATCH-03  
**Tasks:** CORRECTIVE (GetComponentRW + SimMath), BCS-P1-T3, BCS-P1-T4  
**Phase:** Phase 1 — FDP.Toolkit.Behavior Core Infrastructure  
**Estimated Effort:** 9–11 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-02 ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

Two parts:

1. **Corrective (1 h):** Update `ChannelArbitrationSystem` to use `GetComponentRW<T>()` for in-place channel mutation — eliminate the copy round-trip identified in BATCH-02. Apply the same pattern throughout the dispatcher systems you build in this batch.
2. **Core dispatcher systems (6–8 h):** Implement all three dispatcher systems (`Locomotion`, `Weapon`, `Interaction`). These are the heart of Phase 1 — every brain output flows through them.

### Required Reading (IN ORDER)

1. **BATCH-02 Review:** `.dev-workstream/reviews/BATCH-02-REVIEW.md` — understand Issue 1 (GetComponentRW) before writing a single line of system code
2. **SimComponents coordinate convention:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` — read the inline comments defining yaw/pitch/roll before implementing `SimMath`
3. **Design §3.2 dispatcher pattern:** `FDP/Docs/projects/behavior-control/DESIGN.md` — lines 187–194 (O(1) executor lookup table, `IActionExecutor<T>[]` indexed by `ActionKind`)
4. **Task details BCS-P1-T3 and BCS-P1-T4:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 338–392
5. **`EntityRepository.cs`:** `FDP/Kernel/Fdp.Kernel/EntityRepository.cs` — search for `GetComponentRW`

### Source Code Locations

| Area | Path |
|---|---|
| **New:** SimMath helper | `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs` ← **create** |
| Fix target (arbitration) | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` |
| Migration targets | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs` |
| Migration targets (tests) | `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Systems/CarKinematicsSystemTests.cs`, `ParallelCorrectnessTests.cs`, `VehicleStateRefactorTests.cs` |
| New dispatcher systems | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/` |
| Executor interface | `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/IActionExecutor.cs` |
| Behavior test project | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/` |
| TestWorldFactory | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/TestWorldFactory.cs` |
| EntityRepository API | `FDP/Kernel/Fdp.Kernel/EntityRepository.cs` |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln

# Targeted
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/
dotnet test Toolkits/FDP.Toolkit.CarKinem.Tests/
```

### Report Submission

`.dev-workstream/reports/BATCH-03-REPORT.md`  
Questions: `.dev-workstream/questions/BATCH-03-QUESTIONS.md`

---

## Context

**Related tasks:**
- Corrective: fixes `ChannelArbitrationSystem` — see [BATCH-02-REVIEW.md](../reviews/BATCH-02-REVIEW.md) Issue 1
- [BCS-P1-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t3--locomotiondispatchersystem) — LocomotionDispatcherSystem
- [BCS-P1-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t4--weapondispatchersystem--interactiondispatchersystem) — WeaponDispatcherSystem + InteractionDispatcherSystem

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Corrective 0a:** Create `SimMath.cs`, write its unit tests → **all pass** ✅
2. **Corrective 0b:** Migrate all `CreateFromYawPitchRoll` call sites + raw angle magic numbers → solution still builds and tests still pass ✅
3. **Corrective 0c:** Update `ChannelArbitrationSystem` to use `GetComponentRW` → existing 3 arbitration tests still pass ✅
4. **BCS-P1-T3:** Implement `LocomotionDispatcherSystem` → write 4 dispatcher tests → **all pass** ✅
5. **BCS-P1-T4:** Implement `WeaponDispatcherSystem` + `InteractionDispatcherSystem` → write 2 tests → **all pass** ✅

---

## 🎯 Batch Objectives

- `SimMath.FromYawPitchRoll` exists in `Fdp.Kernel` and is the single, authoritative way to construct a rotation quaternion from human-readable angles in our coordinate system.
- Zero occurrences of `Quaternion.CreateFromYawPitchRoll` remain anywhere in production code; tests use `SimMath.FromYawPitchRoll` or named constants.
- Zero magic number literals (raw `float`/`int` constants without a named symbol) in any production code file touched or created this batch.
- `ChannelArbitrationSystem` uses zero-copy `ref` mutation via `GetComponentRW`.
- All three dispatcher systems follow the O(1) executor lookup pattern with capability gating and correct `OnEnter`/`OnExit` lifecycle.

---

## ✅ Tasks

### Task 0a (Corrective): `SimMath` — Quaternion Helper for Our Coordinate Convention

**File to create:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs`

**Problem:** `System.Numerics.Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)` does **not** match our coordinate convention. Its `yaw` rotates around the Y axis; our yaw rotates around Z. Its parameter order is counter-intuitive relative to the axes defined in `SimComponents.cs`. This forces callers to pass arguments in confusing positions (e.g. the `roll` slot used to express what we call yaw), and any reader must understand the System.Numerics convention to follow the code.

**Deliverable:** A `public static class SimMath` with at minimum:

```csharp
namespace Fdp.Kernel
{
    /// <summary>
    /// Math helpers using the FDP world coordinate convention:
    /// right-handed, X=east, Y=north, Z=up.
    /// Yaw = rotation around Z (0 = east, +90° = north).
    /// Pitch = rotation around Y (0 = horizontal, +90° = straight down).
    /// Roll = rotation around X (0 = level, +90° = right wing down).
    /// </summary>
    public static class SimMath
    {
        /// <summary>Construct a rotation quaternion from our yaw/pitch/roll convention (radians).</summary>
        public static Quaternion FromYawPitchRoll(float yawRad, float pitchRad, float rollRad)
        {
            // Apply Z-Y-X (yaw, then pitch, then roll) in our coordinate system.
            return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitY, pitchRad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rollRad);
        }

        /// <summary>Convenience: yaw-only rotation (most common case for ground vehicles).</summary>
        public static Quaternion FromYaw(float yawRad) => FromYawPitchRoll(yawRad, 0f, 0f);

        // Named compass directions — eliminates magic numbers at call sites:
        public static readonly Quaternion FacingEast  = FromYaw(0f);
        public static readonly Quaternion FacingNorth = FromYaw(MathF.PI / 2f);
        public static readonly Quaternion FacingWest  = FromYaw(MathF.PI);
        public static readonly Quaternion FacingSouth = FromYaw(-MathF.PI / 2f);
    }
}
```

Add any other helpers that emerge as clearly needed (e.g. `ExtractYaw(Quaternion q) → float`).

**Tests required** (new file `FDP/Kernel/Fdp.Kernel.Tests/SimMathTests.cs`):
```csharp
[Fact] void FromYaw_Zero_ProducesEastFacing()   // Vector3.Transform(UnitX, FromYaw(0)) ≈ (1,0,0)
[Fact] void FromYaw_90deg_ProducesNorthFacing()  // Vector3.Transform(UnitX, FromYaw(PI/2)) ≈ (0,1,0)
[Fact] void FromYaw_Neg90deg_ProducesSouthFacing()
[Fact] void FacingNorth_Constant_MatchesFromYaw90()
[Fact] void ExtractYaw_RoundTrips_ThroughFromYaw() // if ExtractYaw is implemented
```

---

### Task 0b (Corrective): Migrate All `CreateFromYawPitchRoll` Call Sites and Fixed Buffer Magic Numbers

**Part A — Angle literals and `CreateFromYawPitchRoll`:**

| File | Line(s) | Fix |
|---|---|---|
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs` | 61 | Replace with `SimMath.FromYaw(yaw)` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Systems/CarKinematicsSystemTests.cs` | 56, 133, 148, 207 | Replace with `SimMath.FacingNorth` / `SimMath.FacingEast` as appropriate |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Systems/ParallelCorrectnessTests.cs` | 52 | Replace with `SimMath.FacingNorth` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/VehicleStateRefactorTests.cs` | 49 | Replace with `SimMath.FacingNorth` |
| `FDP/ModuleHost/ModuleHost.Benchmarks/CarKinemPerformance.cs` | 66 | Replace with `SimMath.FromYaw(...)` |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/CombatInputSystem.cs` | 76 | Replace with `SimMath.FromYaw(newYaw)` |

All tests must pass after this part. `Quaternion.Identity` expresses "facing east" and equals `SimMath.FacingEast` — use whichever communicates intent more clearly at the call site.

**Part B — Fixed buffer sizes and capacity magic numbers in Behavior components:**

`ChannelComponents.cs` currently has `fixed byte Params[32]` and `fixed byte State[32]`. `BehaviorComponents.cs` has `fixed byte Memory[128]`. `ComponentLayoutTests.cs` hard-codes `96`. The dispatcher array (Task 1) will be `new IActionExecutor[64]`. All of these are magic numbers.

Create a single `BehaviorConstants.cs` file in `FDP/Toolkits/FDP.Toolkit.Behavior/`:

```csharp
// FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs
namespace FDP.Toolkit.Behavior
{
    public static class BehaviorConstants
    {
        /// <summary>Byte budget for action parameter inline storage per channel.</summary>
        public const int ActionParamsByteSize  = 32;

        /// <summary>Byte budget for per-action executor state inline storage per channel.</summary>
        public const int ActionStateByteSIze   = 32;

        /// <summary>Maximum total size of any channel struct (enforced by ComponentLayoutTests).</summary>
        public const int MaxChannelSizeBytes   = 96;

        /// <summary>Size of BrainBlackboard inline memory.</summary>
        public const int BrainBlackboardByteSize = 128;

        /// <summary>Maximum number of distinct action types per dispatcher.</summary>
        public const int MaxActionTypes        = 64;
    }
}
```

Then update the component files to reference these constants:

```csharp
// ChannelComponents.cs
public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
public fixed byte State[BehaviorConstants.ActionStateByteSIze];

// BehaviorComponents.cs
public fixed byte Memory[BehaviorConstants.BrainBlackboardByteSize];
```

And the layout test should reference the constant too so it tracks automatically if the budget changes:
```csharp
// ComponentLayoutTests.cs
Assert.True(Unsafe.SizeOf<LocomotionChannel>() <= BehaviorConstants.MaxChannelSizeBytes);
```

---

### Task 0c (Corrective): GetComponentRW in ChannelArbitrationSystem

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`

**Problem (from BATCH-02-REVIEW.md Issue 1):** The system calls `GetComponent<T>` (returns a value copy), modifies it on the stack, then calls `SetComponent` to write back. `EntityRepository` already exposes `GetComponentRW<T>()` returning `ref T` directly into chunk memory — no copy needed. This pattern is correct and safe for any synchronous main-thread system.

**Fix pattern** (apply to all three channel loops):
```csharp
// Before:
var channel = World.GetComponent<LocomotionChannel>(entity);
// ... modify channel ...
World.SetComponent(entity, channel);

// After — zero copy, in-place:
ref var channel = ref World.GetComponentRW<LocomotionChannel>(entity);
// ... modify channel fields directly ...
// No SetComponent call.
```

All three existing `ChannelArbitrationTests` must still pass after this change — they verify the observable outcome, not the internal mechanism.

**Background systems rule** (for your awareness, mentioned in BATCH-02 Q4 discussion): The `GetComponentRW` pattern is **only safe on the main thread**. Async/SoD background modules operate on a read-only snapshot shared across threads and must use `GetComponentRO` + `IEntityCommandBuffer.SetComponent`. This is correct, intentional, and the copy cost (~72 bytes) is negligible. Do not conflate the two contexts.

---

### Task 1: LocomotionDispatcherSystem (BCS-P1-T3)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P1-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t3--locomotiondispatchersystem) — lines 338–372

Full logic — query, capability check, `OnEnter`/`OnExit` lifecycle, `DispatchedInstanceId` tracking, `RegisterExecutor` — is specified there. Key implementation notes not repeated in the task doc:

**Use `GetComponentRW` throughout.** Both `LocomotionChannel` and `ActorCapabilityState` should be accessed via `ref var` for in-place mutation. Do not use `GetComponent`/`SetComponent` anywhere in dispatcher code.

**Previous-action tracking:** The spec says "store in a parallel array indexed by entity index". Use a `ushort[]` array sized to `World.EntityCapacity` (or a reasonable max). Resize-on-demand if the entity index exceeds capacity. Initialize entries to `0` (no action). This array lives as a field on the system instance.

**Executor registration:** `RegisterExecutor(ushort actionId, IActionExecutor<LocomotionChannel> executor)` stores into `IActionExecutor<LocomotionChannel>[]` indexed by `actionId`. Size the array to `BehaviorConstants.MaxActionTypes` (not a raw `64`). Null slots are valid — dispatcher skips gracefully.

**Tests required** (add to `FDP.Toolkit.Behavior.Tests/LocomotionDispatcherTests.cs`):  
See TASK-DETAIL.md lines 360–372. Required coverage:
- `OnEnter` called exactly once on first tick, `Execute` on subsequent ticks (not `OnEnter` again).
- `OnExit` of old executor called when `ActionInstanceId` changes to a new action.
- `CanMove=false` with `Status=Running` → `Status` becomes `Failure`, `Execute` not called.
- No registered executor for active action → no exception thrown.

Use a simple spy/stub `IActionExecutor<LocomotionChannel>` implementation in the test file to track calls.

---

### Task 2: WeaponDispatcherSystem + InteractionDispatcherSystem (BCS-P1-T4)

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/InteractionDispatcherSystem.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P1-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t4--weapondispatchersystem--interactiondispatchersystem) — lines 376–392

These are structurally identical to `LocomotionDispatcherSystem` with two differences:
- `WeaponDispatcherSystem` operates on `WeaponChannel` and checks `ActorCapabilities.CanShoot`.
- `InteractionDispatcherSystem` operates on `InteractionChannel` and checks `ActorCapabilities.CanInteract`.

**Consider a generic base class** (`DispatcherSystemBase<TChannel>`) if the FDP system registration pattern supports generic base classes. Check how existing generic systems (e.g., FormationTargetSystem) are registered and whether `[UpdateInGroup]` attribute inheritance works correctly. If generic base is problematic, three fully independent implementations are also fine.

**Tests required** (add to test project):
- `WeaponDispatcher_FailsChannel_WhenCannotShoot` — mirrors the `CanMove` test from Locomotion.
- `InteractionDispatcher_RunsExecutor_WhenCanInteract` — confirms executor receives `Execute` call when capability is set.

---

## 🧪 Testing Requirements

- Minimum **7 new tests** total: existing 3 arbitration tests must still pass after corrective; 4 new locomotion tests; 2 weapon/interaction tests.
- Spy executor pattern: implement a `class SpyExecutor<TChannel> : IActionExecutor<TChannel>` in a `TestHelpers.cs` file in the test project. Track `OnEnterCallCount`, `ExecuteCallCount`, `OnExitCallCount`. Reuse it across all dispatcher tests.
- `TestWorldFactory.Create()` — add `BrainBlackboard`, `SimTier` registrations if needed for dispatcher queries.
- All pre-existing tests must remain green.

---

## ⚠️ Quality Standards

### 🚫 NO MAGIC NUMBERS — standing rule for all batches from here onward

This applies to **all production code** created or modified in this batch and every subsequent batch:
- **No bare numeric literals** (`0.5f`, `128`, `32`, `96`, `64`, `255`, `4096`, etc.) in non-test `.cs` files.
- Every constant must be a named symbol declared with `const` or `static readonly` as close as possible to where it is semantically owned:
  - Struct size budgets: `private const int MaxChannelSizeBytes = 96;`
  - Fixed buffer sizes: `private const int ActionParamsByteSize = 32;` — used in `fixed byte Params[ActionParamsByteSize]`
  - Lookup table capacities: `private const int MaxActionTypes = 64;`
- **Fixed-size arrays in structs are the most common offender after angles.** `fixed byte Params[32]` is a magic number. `fixed byte Params[ActionParamsByteSize]` is not.
- **Enum values are the correct tool** for integer states — never compare against `1`, `2`, `3` where an enum exists.
- Angle literals (`MathF.PI`, `-MathF.PI / 2f`) — replace with `SimMath.FacingNorth`, `SimMath.FromYaw(...)`, or a named `const float`.
- **Tests are exempt** from this rule for simple, obvious numeric assertions (sizes, counts, expected movement distances). Where a production constant already exists (e.g. `MaxChannelSizeBytes`), tests *should* reference it so test assertions stay in sync automatically.

Do not let magic numbers accumulate — they impose a cognitive tax on every future reader and make refactoring dangerous. Changing a buffer size from 32 to 48 bytes should be a one-line edit to the constant, not a grep-and-pray across the codebase.

**❗ `GetComponentRW` everywhere in dispatcher code** — no `GetComponent`/`SetComponent` round-trips in any main-thread system from this batch onward.

**❗ Clean up the stale comment** in `ChannelArbitrationTests.cs` lines 86–88 (noted in BATCH-02 review).

**❗ `OnEnter`/`OnExit` must fire exactly once per transition.** A common mistake: calling `OnEnter` every tick instead of only when `ActionInstanceId != DispatchedInstanceId`. Verify with the spy test.

**❗ No allocation in `OnUpdate`.** The previous-action tracking array is pre-allocated at `OnCreate`. Do not `new[]` inside `OnUpdate`.

**❗ No `Quaternion.CreateFromYawPitchRoll` in production code** — use `SimMath.FromYaw` / `SimMath.FromYawPitchRoll`. In tests, use `SimMath.FacingNorth` etc. for directional setup to make intent clear.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-03-REPORT.md`:

- **Test results:** `dotnet test FDP.sln` summary.
- **Q1:** Did you find any `CreateFromYawPitchRoll` or magic angle literals in the codebase beyond the ones listed in Task 0b? Where?
- **Q2:** Did you implement a generic base class for the three dispatchers? What was the friction, if any, with generic system registration?
- **Q3:** How did you handle the previous-action array resize case? Was there a cleaner alternative given the ECS entity capacity API?
- **Q4:** Did you spot any ordering assumptions between `ChannelArbitrationSystem` and the dispatchers that aren't enforced by `[UpdateAfter]` attributes? Any risk of a one-frame stale channel reaching a dispatcher?
- **Q5:** What weak points, if any, did you identify in the `IActionExecutor<T>` interface design when implementing executors in the test spies?

---

## 🎯 Success Criteria

- [ ] **Corrective 0a** — `SimMath.cs` exists in `Fdp.Kernel`; `FromYaw`, `FromYawPitchRoll`, compass constants; ≥4 unit tests pass
- [ ] **Corrective 0b** — `BehaviorConstants.cs` created; `ChannelComponents.cs` and `BehaviorComponents.cs` use named constants for all buffer sizes; zero remaining `Quaternion.CreateFromYawPitchRoll` calls in production code; `ComponentLayoutTests` references `MaxChannelSizeBytes`; all tests pass
- [ ] **Corrective 0c** — `ChannelArbitrationSystem` uses `GetComponentRW`; no `SetComponent` write-back; all 3 arbitration tests pass
- [ ] **Cleanup** — stale comment removed from `ChannelArbitrationTests.cs` lines 86–88
- [ ] **BCS-P1-T3** — `LocomotionDispatcherSystem` exists; 0 magic numbers in implementation; all 4 locomotion dispatcher tests pass
- [ ] **BCS-P1-T4** — `WeaponDispatcherSystem` + `InteractionDispatcherSystem` exist; 2 capability tests pass
- [ ] **SpyExecutor** — reusable test helper in `TestHelpers.cs`
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-02 Review:** `.dev-workstream/reviews/BATCH-02-REVIEW.md`
- **SimComponents coord convention:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` — defines yaw/pitch/roll axes
- **Task Details (BCS-P1-T3, BCS-P1-T4):** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 338–392
- **Design §3.2:** `FDP/Docs/projects/behavior-control/DESIGN.md` — lines 172–204
- **EntityRepository API:** `FDP/Kernel/Fdp.Kernel/EntityRepository.cs`
- **Note on magic numbers in design/task docs:** The existing design and task documents contain numeric literals (sizes, IDs, angles) as illustration. These are reference material only — your production code must use named constants. The docs describe *what*; you decide *how to name it*.
