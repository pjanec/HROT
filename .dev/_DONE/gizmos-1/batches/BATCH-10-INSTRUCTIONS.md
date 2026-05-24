# BATCH-10: Stateless Gizmo Execution Path + P1 Concurrency Fix

**Batch Number:** BATCH-10
**Tasks:** TASK-GZ040 (P1 fix), TASK-GZ022 (IStatelessGizmo), TASK-GZ023 (migrate gizmos), TASK-GZ024 (Roslyn generator)
**Phase:** 14 (P1 fix) + 8 (Stateless Gizmo Execution Path)
**Estimated Effort:** 12-16 hours
**Priority:** HIGH — includes a P1 blocking concurrency hazard
**Dependencies:** BATCH-09 (complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch introduces the missing stateless gizmo execution path (Phase 8 from the design) and
fixes a P1 blocking concurrency hazard in `StringInternMap`. The four tasks build sequentially:
first fix the concurrency bug (independent), then introduce the new `IStatelessGizmo` contract,
then migrate the four existing pure-projector gizmos to it, and finally wire a Roslyn source
generator that replaces the hand-written `GizmoRegistrar.cs`.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Task Definitions:** `.dev/gizmos-1/TASK-DETAIL.md` — See TASK-GZ040, TASK-GZ022, TASK-GZ023, TASK-GZ024
3. **Design Document:** `.dev/gizmos-1/DESIGN.md` — §1.2 (string intern), §2.1 (Statefulness taxonomy), §2.3 (registration)
4. **Debt Tracker:** `.dev/gizmos-1/DEBT-TRACKER.md` — D-001 is the P1 item this batch resolves
5. **Previous Review:** `.dev/gizmos-1/reviews/BATCH-09-REVIEW.md` — Context for what's done

### Source Code Locations

| Task | Files to create/modify |
|------|----------------------|
| GZ040 | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs` |
| GZ022 | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatelessGizmo.cs` (NEW), `StatelessGizmoRegistry.cs` (NEW), `Systems/StatelessGizmoSystem.cs` (NEW) |
| GZ023 | `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/` (NEW files), `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/` (NEW files), `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` (MODIFY), `Hrot/Subsystems/Hrot.IG/Gizmos/*Instance.cs` + `*Definition.cs` files (DELETE) |
| GZ024 | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoProjectorAttribute.cs` (NEW), `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs` (NEW) |

**Test projects:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/` — extend `GizmosSystemTests.cs`, add tests for stateless system
- `Hrot/Subsystems/Hrot.IG.Tests/` — existing gizmo rendering tests (must remain passing); add new tests if needed

### Report Submission
**When done, submit your report to:**
`.dev/gizmos-1/reports/BATCH-10-REPORT.md`

**If you have questions, create:**
`.dev/gizmos-1/questions/BATCH-10-QUESTIONS.md`

---

## Context

Phase 8 of the design addresses a key architectural gap: all four concrete gizmos
(`HealthBarGizmo`, `EntityRotationGizmo`, `VisibilityConeGizmo`, `HillAttackGizmo`) are logically
stateless — they read ECS state each frame and emit primitives — but are shoehorned into the
stateful `IStatefulGizmo`/`IGizmoDefinition` path with empty `OnInitialize`/`OnTeardown` stubs.
The design explicitly calls for a `IStatelessGizmo` interface and a `StatelessGizmoSystem` that
performs bulk ECS queries instead of per-entity dictionary lookups.

The P1 fix (D-001) is unrelated to Phase 8 but is mandatory: `StringInternMap` uses a raw
`Dictionary` with a false "Thread-safe" comment. It will corrupt under parallel ECS iteration.

**Related Tasks:**
- [TASK-GZ040](../TASK-DETAIL.md#task-gz040--fix-stringinternmap-concurrency-hazard-d-001-p1-blocking) — P1 concurrency fix
- [TASK-GZ022](../TASK-DETAIL.md#task-gz022--istatelessgizmo-contract-and-statelessgizmosystem) — IStatelessGizmo contract
- [TASK-GZ023](../TASK-DETAIL.md#task-gz023--migrate-pure-projector-gizmos-to-stateless-and-correct-project-placement) — Migrate gizmos
- [TASK-GZ024](../TASK-DETAIL.md#task-gz024--unified-gizmoprojector-attribute-and-roslyn-source-generator) — Source generator

---

## 🎯 Batch Objectives

1. Fix the P1 concurrency hazard in `StringInternMap` so parallel ECS iteration is safe.
2. Introduce the stateless execution path (`IStatelessGizmo` + `StatelessGizmoSystem`).
3. Migrate the four pure-projector gizmos to their correct assemblies (`Hrot.Common`, `Hrot.AI.Behaviors`) as `IStatelessGizmo` implementations.
4. Replace the hand-written `GizmoRegistrar.cs` with a Roslyn source-generator that auto-discovers `[GizmoProjector]`-decorated classes.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **Task 1 (GZ040):** Fix StringInternMap → Write concurrency stress tests → ALL tests pass ✅
2. **Task 2 (GZ022):** Create IStatelessGizmo + StatelessGizmoSystem → Write unit tests → ALL tests pass ✅
3. **Task 3 (GZ023):** Migrate gizmos → Adapt call sites → ALL tests pass ✅
4. **Task 4 (GZ024):** Roslyn generator → Delete hand-written GizmoRegistrar.cs → ALL tests pass ✅

**DO NOT** move to the next task until current task's tests all pass.

---

## ✅ Tasks

### Task 1 — Fix StringInternMap Concurrency Hazard (TASK-GZ040)

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs` (MODIFY)
**Task Definition:** See [TASK-GZ040](../TASK-DETAIL.md#task-gz040--fix-stringinternmap-concurrency-hazard-d-001-p1-blocking)

See the full spec in TASK-DETAIL.md. Summary of changes:
- Replace `Dictionary<uint, string>` with `ConcurrentDictionary<uint, string>`.
- Change `Intern` to use `TryAdd` (atomic, no-op on duplicate).
- `TryResolve` and `Entries` and `Flush` need no API changes — `ConcurrentDictionary` is
  compatible with `IReadOnlyDictionary<TKey,TValue>` via the cast.
- Remove the false `// Thread-safe string intern side-channel` comment; add the correct comment:
  `// Concurrent-safe intern map; TryAdd/TryGetValue are lock-free.`

**Tests Required:**
- ✅ SC-GZ040-2: Parallel stress test — 32 threads calling `Intern` with the same hash simultaneously; no exception, exactly one entry.
- ✅ SC-GZ040-3: Concurrent read/write stress test — `Intern` and `TryResolve` racing from multiple threads; no exception.
- ✅ SC-GZ040-4: Verify false comment is absent (source check).
- ✅ SC-GZ040-5: `DrawTextLong` stress test — 10,000 iterations from parallel threads, no exception.

Place these tests in `FDP/Toolkits/Fdp.Toolkits.Tests/` (extend or add to the existing Gizmos test files).

---

### Task 2 — IStatelessGizmo Contract and StatelessGizmoSystem (TASK-GZ022)

**Files:** (all NEW)
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatelessGizmo.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StatelessGizmoRegistry.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs`

**Task Definition:** See [TASK-GZ022](../TASK-DETAIL.md#task-gz022--istatelessgizmo-contract-and-statelessgizmosystem) for the full spec including exact interfaces and struct shapes.

Key implementation points (do not skip reading the full TASK-DETAIL.md spec):
- `IStatelessGizmo.Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder)` — no state, no lifecycle.
- `StatelessGizmoRegistry.Register(IStatelessGizmo, Type[], IGizmoVisibilityPolicy?)` — converts types to `BitMask256`, throws on unknown component type.
- `StatelessGizmoSystem` — queries by `BitMask256` mask for each rule, evaluates global visibility ONCE per rule (not per entity), respects `SelectionState.IsSelected` when `ForceAllGizmosVisible = false`.
- Use `AlwaysVisiblePolicy.Instance` when no policy provided.

**Tests Required (place in `FDP/Toolkits/Fdp.Toolkits.Tests/GizmosSystemTests.cs` or a new `StatelessGizmoSystemTests.cs`):**
- ✅ SC-GZ022-1: Register with `SimTransform` → rule has `RequiredMask` with the SimTransform bit set.
- ✅ SC-GZ022-2: Register with unregistered type → `InvalidOperationException`.
- ✅ SC-GZ022-3: `Execute` calls `Draw` for every matching entity (mock projector counting invocations).
- ✅ SC-GZ022-4: Entity without all required components does NOT trigger Draw.
- ✅ SC-GZ022-5: `ForceAllGizmosVisible = false` → only selected entities trigger Draw.
- ✅ SC-GZ022-6: `ForceAllGizmosVisible = true` → all matching entities trigger Draw.
- ✅ SC-GZ022-7: Global visibility cache evaluated ONCE per rule per frame (count `IsGloballyEnabled` calls on mock policy with 100 entities — must be 1, not 100).
- ✅ SC-GZ022-8: `NeverVisiblePolicy` → zero Draw calls even with `ForceAllGizmosVisible = true`.

---

### Task 3 — Migrate Pure-Projector Gizmos to Stateless (TASK-GZ023)

**Task Definition:** See [TASK-GZ023](../TASK-DETAIL.md#task-gz023--migrate-pure-projector-gizmos-to-stateless-and-correct-project-placement) for the full migration table and constraints.

**Files to CREATE:**
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityRotationGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/VisibilityConeGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmoSettings.cs` (moved from Hrot.IG)
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityRotationGizmoSettings.cs` (moved from Hrot.IG)
- `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/HillAttackGizmo.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/HillAttackGizmoSettings.cs` (moved from Hrot.IG)

**Files to DELETE from `Hrot/Subsystems/Hrot.IG/Gizmos/`:**
- `HealthBarGizmoInstance.cs`, `HealthBarGizmoDefinition.cs`, `HealthBarGizmoSettings.cs`
- `EntityRotationGizmoInstance.cs`, `EntityRotationGizmoDefinition.cs`, `EntityRotationGizmoSettings.cs`
- `VisibilityConeGizmoInstance.cs`, `VisibilityConeGizmoDefinition.cs`
- `HillAttackGizmoInstance.cs`, `HillAttackGizmoDefinition.cs`, `HillAttackGizmoSettings.cs`

**File to MODIFY:**
- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` — switch from `registry.Register(new XGizmoDefinition(...))` to `statelessRegistry.Register(new XGizmo(settings), requiredComponents)`.
  Add `StatelessGizmoRegistry statelessRegistry` parameter.
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — pass the `StatelessGizmoRegistry` to `GizmoRegistrar.Register(...)`.

**`Hrot.Common.csproj`** must add a `<ProjectReference>` to `Fdp.Toolkits` if not already present.
**`Hrot.AI.Behaviors.csproj`** must add a `<ProjectReference>` to `Fdp.Toolkits` if not already present.

**Constraints:**
- Zero changes to the rendering math inside any gizmo's `Draw` method. Copy the logic from
  the former `*Instance.UpdateAndDraw(...)` into `IStatelessGizmo.Draw(...)` verbatim.
- Remove only the `OnInitialize` and `OnTeardown` stubs and the class hierarchy overhead.
- `Hrot.Common` and `Hrot.AI.Behaviors` must NOT reference `Hrot.IG`.
- Existing rendering tests in `Hrot.IG.Tests` (`HealthBarGizmoTests.cs`, `EntityRotationGizmoTests.cs`, `VisibilityConeGizmoTests.cs`, `HillAttackGizmoTests.cs`) must still pass; only their assembly reference changes.

**Tests Required:**
- ✅ SC-GZ023-1/2: Verify the four gizmos compile in correct assemblies with no Hrot.IG dependency (build succeeds).
- ✅ SC-GZ023-3: All existing rendering tests pass (run `dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj`).
- ✅ SC-GZ023-5: Add an integration test in `FDP/Toolkits/Fdp.Toolkits.Tests/GizmosSystemTests.cs` verifying that `StatelessGizmoSystem` registered with `HealthBarGizmo` calls `Draw` for a matching entity.

---

### Task 4 — [GizmoProjector] Attribute and Roslyn Source Generator (TASK-GZ024)

**Task Definition:** See [TASK-GZ024](../TASK-DETAIL.md#task-gz024--unified-gizmoprojector-attribute-and-roslyn-source-generator) for the full spec including the exact generated output format and the `FDP_002` warning.

**Files to CREATE:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoProjectorAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs`

**Files to MODIFY:**
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/Fdp.Toolkits.Analyzers.csproj` — ensure `<IsRoslynComponent>true</IsRoslynComponent>` (already set).
- `Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj` (or whichever project consumes the generator) — add a reference to the analyzer so the generator runs.

**Files to DELETE:**
- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` (replaced by the generated `GizmoRegistrar.g.cs`).

**Key generator logic (read full spec in TASK-DETAIL.md §TASK-GZ024):**
- Find classes decorated with `[GizmoProjectorAttribute]`.
- If class implements `IStatelessGizmo` → emit `statelessRegistry.Register(new T(settings), ...)` or `new T()` depending on constructor.
- If class implements `IGizmoDefinition` → emit `gizmoRegistry.Register(new T())`.
- Emit `partial static class GizmoRegistrar` with `RegisterAll(GizmoRegistry, StatelessGizmoRegistry, GizmoSettingsRegistry)`.
- Classes with `[GizmoProjector]` but neither interface → emit compiler warning `FDP_002`.

**Decorate the four migrated gizmo classes** with `[GizmoProjector(...)]` as part of this task (they were not decorated in Task 3 — this task adds the attribute).

**Tests Required (unit-test the generator output — standard Roslyn generator test pattern):**
- ✅ SC-GZ024-1: A `[GizmoProjector(typeof(SimTransform))]` class implementing `IStatelessGizmo` appears as `statelessRegistry.Register(...)` in generated output.
- ✅ SC-GZ024-2: A `[GizmoProjector]` class implementing `IGizmoDefinition` appears as `gizmoRegistry.Register(...)`.
- ✅ SC-GZ024-3: Generated `RegisterAll` compiles without errors (verify by running `dotnet build`).
- ✅ SC-GZ024-5: `[GizmoProjector]` class implementing neither interface triggers warning `FDP_002`.
- ✅ SC-GZ024-6 (regression): All existing gizmo system tests pass after `GizmoRegistrar.cs` is deleted.

For Roslyn generator tests, place them in `FDP/Toolkits/Fdp.Toolkits.Tests/` in a new `GizmoRegistrarGeneratorTests.cs` file. Use `Microsoft.CodeAnalysis.CSharp.Testing` or direct `CSharpGeneratorDriver` invocation — whichever the project already uses for generator tests.

---

## 🧪 Testing Requirements

- **Minimum:** All 8 success conditions for GZ022, 5 for GZ023 (SC-GZ023-1 through SC-GZ023-5), 5 for GZ024, 5 for GZ040.
- **Quality bar:** Tests must verify ACTUAL behavior — entity count processed, invocation counts, no exceptions from parallel code.
- **Regression:** Run the full `Hrot.IG.Tests` suite after GZ023 migration. All 36+ existing tests must still pass.
- **Build verification:** After GZ024, run `dotnet build IOS-IG-SimHost.sln`. Zero errors.

**DO NOT** skip any success conditions from TASK-DETAIL.md. They are the acceptance criteria.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only check "can I create this object" or "does Register not throw".
- **REQUIRED:** Tests that verify actual behavior — mock projectors with invocation counters, actual `BitMask256` bit checks, parallel stress tests that would catch real races.

**❗ MIGRATION QUALITY**
- The rendering math in each gizmo's `Draw` method must be identical to the former `UpdateAndDraw` content. Do not silently simplify or change behavior.
- After migration, the `Hrot.IG` project must NOT contain any `*Instance.cs` or `*Definition.cs` gizmo files.

**❗ ROSLYN GENERATOR**
- The generator runs at compile time. Test it by adding a decorated class and verifying the generated output — do NOT write a test that just calls the generator's internal methods directly.
- The generator must correctly handle classes with both parameterless and `GizmoSettingsRegistry`-parameter constructors.

**❗ NO STOPPING MID-BATCH**
- Do not stop and ask for permission to run tests, fix build errors, or delete files. Execute the full batch end to end, fix all errors you encounter, and submit the report when everything passes.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-GZ040 complete: `StringInternMap` uses `ConcurrentDictionary`, false comment removed, concurrency stress tests pass.
- [ ] TASK-GZ022 complete: `IStatelessGizmo`, `StatelessGizmoRegistry`, `StatelessGizmoSystem` created; 8 unit tests passing.
- [ ] TASK-GZ023 complete: Four gizmos migrated to correct assemblies as `IStatelessGizmo` implementations; all 36+ IG.Tests pass.
- [ ] TASK-GZ024 complete: `GizmoProjectorAttribute` + Roslyn generator emitting `GizmoRegistrar.g.cs`; hand-written `GizmoRegistrar.cs` deleted; generator tests pass.
- [ ] `dotnet build IOS-IG-SimHost.sln` → zero errors.
- [ ] `dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj` → all pass.
- [ ] `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj` → all pass.
- [ ] Report submitted to `.dev/gizmos-1/reports/BATCH-10-REPORT.md`.

---

## ⚠️ Common Pitfalls

- `ConcurrentDictionary` does NOT implement `IReadOnlyDictionary<K,V>` directly but can be cast to it — verify the `Entries` property compiles.
- `StatelessGizmoRegistry` is startup-only; the `bool[]` visibility cache is sized at first `Execute` call (or construction) from `registry.Rules.Count`. After that, the registry is sealed.
- `Hrot.Common.csproj` currently references `Fdp.Core` but NOT `Fdp.Toolkits`. You will need to add a `<ProjectReference Include="../../../FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj" />` line. Check if `Hrot.AI.Behaviors.csproj` also needs it.
- When the Roslyn generator emits `partial static class GizmoRegistrar`, the containing project must also have a matching `partial static class GizmoRegistrar` stub (or the full class can live only in the generator output). Verify this compiles.
- The generator project targets `netstandard2.0`. Do not use any API unavailable in that target.

---

## 📚 Reference Materials

- **Task Defs:** `.dev/gizmos-1/TASK-DETAIL.md` — TASK-GZ040, TASK-GZ022, TASK-GZ023, TASK-GZ024
- **Design:** `.dev/gizmos-1/DESIGN.md` — §1.2, §2.1, §2.3
- **Existing analyzer example:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BehaviorParameterSizeAnalyzer.cs`
- **Existing gizmo tests:** `Hrot/Subsystems/Hrot.IG.Tests/GizmosSystemTests.cs` (for patterns), `Hrot.IG.Tests/HealthBarGizmoTests.cs`
- **Existing system:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` — the stateless system follows same patterns
- **Existing GizmoRegistry:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoRegistry.cs`

## 📊 Report Requirements

**Submit `.dev/gizmos-1/reports/BATCH-10-REPORT.md` containing:**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** What design decisions did you make beyond the instructions (e.g., how you structured the generator output, how you handled the `Entries` property type compatibility)?

**Q3:** Did you spot any weak points in the existing codebase or gaps in the design?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Suggested commit message for this batch.
