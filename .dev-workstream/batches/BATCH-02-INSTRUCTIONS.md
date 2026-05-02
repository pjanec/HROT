# BATCH-02: Forward Vector Fix + Behavior Component Types + Channel Arbitration

**Batch Number:** BATCH-02  
**Tasks:** CORRECTIVE (UnitX fix), BCS-P1-T1, BCS-P1-T2  
**Phase:** Phase 1 — FDP.Toolkit.Behavior Core Infrastructure  
**Estimated Effort:** 6–8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two parts:

1. **Corrective (30 min):** Fix a forward-vector extraction bug left over from BATCH-01.
2. **Phase 1 start (6–7 h):** Define all Behavior component types and implement the `ChannelArbitrationSystem`. These are the structural foundation — everything in Phase 1 depends on the component layout being locked before systems are written.

### Required Reading (IN ORDER)

1. **BATCH-01 Review:** `.dev-workstream/reviews/BATCH-01-REVIEW.md` — understand Issue 1 before touching any code
2. **Onboarding:** `FDP/Docs/projects/behavior-control/ONBOARDING.md` — zero-alloc rule, 256-component limit (critical before you create any new component)
3. **Design §3.1 + §3.2 (partial):** `FDP/Docs/projects/behavior-control/DESIGN.md` — §3.1 "Component Types" (lines 118–202); §3.2 system table (lines 172–194 — read the dispatcher pattern, you'll implement it next batch; understand it now)
4. **Task details BCS-P1-T1 and BCS-P1-T2:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 253–334

### Source Code Locations

| Area | Path |
|---|---|
| Bug fix (CarKinematicsSystem) | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` line 281 |
| Bug fix (FormationTargetSystem) | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/FormationTargetSystem.cs` line 55 |
| New toolkit project | `FDP/Toolkits/FDP.Toolkit.Behavior/` ← **create project** |
| New test project | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/` ← **create project** |
| FastBTree source | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` |
| FastHSM source | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` |
| Kernel (ECS base) | `FDP/Kernel/Fdp.Kernel/` |
| SimComponents (coord convention) | `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` — **read the comments** |
| Solution file | `FDP/FDP.sln` |
| Existing toolkit csproj example | `FDP/Toolkits/FDP.Toolkit.CarKinem/FDP.Toolkit.CarKinem.csproj` |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln

# Targeted during development
dotnet test Toolkits/FDP.Toolkit.CarKinem.Tests/
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/
```

### Report Submission

`.dev-workstream/reports/BATCH-02-REPORT.md`

Questions: `.dev-workstream/questions/BATCH-02-QUESTIONS.md`

---

## Context

**Related tasks:**
- Corrective: fixes `CarKinematicsSystem` + `FormationTargetSystem` — see [BATCH-01-REVIEW.md](../reviews/BATCH-01-REVIEW.md) Issue 1
- [BCS-P1-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t1--behavior-component-types) — All behavior component structs + IActionExecutor interface
- [BCS-P1-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t2--channelarbitrationsystem) — ChannelArbitrationSystem

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Corrective:** Fix both `UnitY` → `UnitX` calls → add regression test → **ALL existing tests still pass** ✅
2. **BCS-P1-T1:** Create project + all component files → write layout tests → **ALL tests pass** ✅
3. **BCS-P1-T2:** Implement `ChannelArbitrationSystem` → write arbitration tests → **ALL tests pass** ✅

**DO NOT** move to the next task until all tests are green.

---

## 🎯 Batch Objectives

- Forward vector extraction is consistent with the X-forward coordinate convention documented in `SimComponents.cs` throughout all CarKinem systems.
- `FDP.Toolkit.Behavior` project exists, references `Fdp.Kernel`, `Fbt.Kernel`, `Fhsm.Kernel`.
- All component structs and the `IActionExecutor<T>` interface are defined and have correct sizes.
- `ChannelArbitrationSystem` clears stale channels and leaves valid channels untouched.

---

## ✅ Tasks

### Task 0 (Corrective): Fix UnitY → UnitX in Formation Path

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/FormationTargetSystem.cs`

**Problem (from BATCH-01-REVIEW.md Issue 1):**  
The coordinate convention chosen by the developer (documented in `SimComponents.cs`) is X-forward (yaw=0 → east → X axis). `CarKinematicsSystem.UpdateVehicle` correctly uses `Vector3.UnitX` to extract forward from the quaternion. However two fallback paths use `Vector3.UnitY` instead — the fallback in `GetFormationTarget` (line 281) and in `FormationTargetSystem` (line 55) — which extracts the left vector, not forward.

**Fix:**
```csharp
// CarKinematicsSystem.cs GetFormationTarget fallback (~line 281):
// BEFORE: var fwd3D = Vector3.Transform(Vector3.UnitY, tf.Rotation);
// AFTER:
var fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation);

// FormationTargetSystem.cs (~line 55): same change
```

**Regression test** (add to `FDP.Toolkit.CarKinem.Tests/Systems/FormationTargetSystemTests.cs`):
```csharp
[Fact]
public void GetFormationTarget_FallbackHeading_MatchesXForwardConvention()
{
    // Entity with Quaternion.Identity (yaw=0 = east = X+)
    // GetFormationTarget fallback should return heading ≈ (1, 0)
    // Assert: heading.X ≈ 1f, heading.Y ≈ 0f
}
```

---

### Task 1: Behavior Component Types (BCS-P1-T1)

**New project to create:** `FDP/Toolkits/FDP.Toolkit.Behavior/FDP.Toolkit.Behavior.csproj`  
**New test project:** `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/FDP.Toolkit.Behavior.Tests.csproj`

Project references required: `Fdp.Kernel`, `Fbt.Kernel` (`ExtDeps/FastBTree/src/Fbt.Kernel/`), `Fhsm.Kernel` (`ExtDeps/FastHSM/src/Fhsm.Kernel/`). Study the existing `.csproj` in `FDP.Toolkit.CarKinem` for the reference pattern.

**Task Definition:** [TASK-DETAIL.md §BCS-P1-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t1--behavior-component-types) — lines 253–296. The file names, namespace, struct fields, and size constraints are fully specified there.

Key dimensional constraints (sizes that drive Phase 1 memory layout — do not negotiate these):

| Component/Interface | Size constraint |
|---|---|
| `LocomotionChannel` | ≤ 96 bytes |
| `WeaponChannel` | Same size as `LocomotionChannel` |
| `InteractionChannel` | Same size as `LocomotionChannel` |
| `BrainBlackboard` | Exactly 128 bytes (`fixed byte Memory[128]`) |
| `ActorCapabilities` enum | `[Flags] enum : byte` |

⚠️ **Coordinate convention note** for action channels: `MoveToParams.Destination` is `Vector2` (XY ground plane). Callers derive it by projecting `SimTransform.Position.XY`. Do not store `Vector3` in channel params — it would waste the 32-byte budget. This is important for Phase 3 navigator executors; just be aware now.

⚠️ **256-component limit**: Every `RegisterComponent<T>()` call consumes one slot. The `fixed byte Params[32]` and `State[32]` design specifically avoids registering separate action-param components. Do not deviate.

**Tests required** (new file `FDP.Toolkit.Behavior.Tests/ComponentLayoutTests.cs`):  
See exact test code in TASK-DETAIL.md lines 277–296.

---

### Task 2: ChannelArbitrationSystem (BCS-P1-T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P1-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t2--channelarbitrationsystem) — lines 300–334.

Logic, query pattern, reset fields, and system group attribute are fully described in the task doc. Note the system must handle all three channel types (`LocomotionChannel`, `WeaponChannel`, `InteractionChannel`) independently — entities may have any subset of the three.

**Tests required** (new file `FDP.Toolkit.Behavior.Tests/ChannelArbitrationTests.cs`):  
See TASK-DETAIL.md lines 316–333. Required scenarios:
- Channel is cleared when `BehaviorInstanceId` mismatches `BehaviorState.InstanceId`.
- A matching channel is **not** touched.
- A channel with `ActiveAction == 0` is skipped (no redundant writes).

A `TestWorldFactory.Create()` helper should be introduced in the test project to avoid boilerplate component registration in every test. Minimum registrations: `BehaviorState`, `LocomotionChannel`, `WeaponChannel`, `InteractionChannel`, `ActorCapabilityState`.

---

## 🧪 Testing Requirements

- Minimum **5 new tests** total: 1 regression (corrective) + 4 for component/arbitration.
- Tests must check actual values (field counts, byte sizes, specific field states after system run).
- `TestWorldFactory.Create()` must be a proper helper — do not copy-paste 10 lines of `RegisterComponent` into every test.
- Run `dotnet test FDP.sln` before submission.

---

## ⚠️ Quality Standards

**❗ ALL prior tests must still pass** — do not break the CarKinem or example test suites.

**❗ Stale comment in BattleRoyale:** `EntityFactory.cs` line 37 has a leftover debug comment (`// Was RegisterComponent in snippet`). Remove it in this pass.

**❗ No heap allocation in component structs** — all fields must be unmanaged. If you feel the urge to add a `string` or `List<T>`, use `FixedString32` or a `fixed byte` buffer instead.

**❗ `IActionExecutor<TChannel>` is an interface, not an abstract class.** Keep executors cheap to instantiate — no state in the interface methods other than what comes through the parameters.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-02-REPORT.md`:

- **Test results:** full `dotnet test FDP.sln` summary.
- **Q1:** What was the structural impact of the 96-byte channel size constraint? Did you have to make any layout compromises?
- **Q2:** Did you look at how `FastBTree.BehaviorTreeState` and `FastHSM.HsmInstance128` are actually laid out in memory? Any surprises about their sizes?
- **Q3:** What design decisions did you make for `TestWorldFactory` — what's registered there by default and why?
- **Q4:** Any weak points spotted in the ECS kernel API that would make the arbitration logic or future dispatcher logic awkward?

---

## 🎯 Success Criteria

- [ ] **Corrective** — `CarKinematicsSystem` line 281 and `FormationTargetSystem` line 55 both use `Vector3.UnitX`; regression test passes
- [ ] **BCS-P1-T1** — All component files created; `LocomotionChannel` ≤ 96 bytes; all three channels same size; all layout tests pass
- [ ] **BCS-P1-T2** — `ChannelArbitrationSystem` exists; stale-channel cleared; valid channel untouched; all arbitration tests pass
- [ ] **BattleRoyale cleanup** — stale comment on `EntityFactory.cs` line 37 removed
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-01 Review:** `.dev-workstream/reviews/BATCH-01-REVIEW.md`
- **Task Details (BCS-P1-T1, BCS-P1-T2):** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 253–334
- **Design §3.1–3.2:** `FDP/Docs/projects/behavior-control/DESIGN.md` — lines 116–204
- **SimComponents coord convention:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`
- **FastBTree IAIContext:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/IAIContext.cs`
- **FastHSM HsmKernel:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs`
- **Existing csproj pattern:** `FDP/Toolkits/FDP.Toolkit.CarKinem/FDP.Toolkit.CarKinem.csproj`
