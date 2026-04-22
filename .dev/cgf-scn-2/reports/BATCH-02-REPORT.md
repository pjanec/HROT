# BATCH-02 Completion Report

**Batch:** BATCH-02 — MissionPlan Serialization + FdpAutoSerializer Upgrade
**Workstream:** cgf-scn-2
**Date:** 2026-04-23
**Status:** COMPLETE

---

## Task Completion Summary

| Task | Description | Status |
|------|-------------|--------|
| TASK-S201 | Implement MissionPlanTranslator | DONE |
| TASK-S202 | Register MissionPlanTranslator at 3 sites | DONE |
| TASK-S301 | FdpAutoSerializer: fixed-buffer expression trees | DONE |
| TASK-S302 | FdpAutoSerializer: [InlineArray] expression trees | DONE |

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/MissionPlanTranslator.cs` | NEW — custom `IEntityScenarioTranslator` for `ActiveMissionPlan` + `MissionPlanQueue` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Added `MissionPlanTranslator` registration (TASK-S202 site 1) |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Added `MissionPlanTranslator` registration (TASK-S202 site 2) |
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Added `MissionPlanTranslator` registration + `DoctrineRegistry` construction (TASK-S202 site 3) |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` | Extended to serialize `fixed` buffers and `[InlineArray]` fields via expression trees + `Holder<T>` pattern |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/InteractionComponents.cs` | Added `[ScenarioIgnore]` to `PassengerBuffer.Passengers` (Entity-in-InlineArray handled by `PassengerBufferTranslator`) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/MissionPlanTranslatorTests.cs` | NEW — 4 unit tests for TASK-S201 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/FdpAutoSerializerFixedBufferTests.cs` | NEW — 7 unit tests for TASK-S301 and TASK-S302 |

---

## Test Results

### Fdp.Toolkits.Tests
```
Failed:  7 (pre-existing), Passed: 753, Skipped: 0, Total: 760
```

Pre-existing failures (all unrelated to this batch — same 7 as BATCH-01):
- `CombatComponentTests.WeaponFireIntent_IsUnmanaged_AndHasCorrectSize`
- `CombatComponentTests.DetonationNotification_IsUnmanaged_AndHasCorrectSize`
- `CombatComponentTests.DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize`
- `CombatComponentTests.WeaponFireNotification_IsUnmanaged_AndHasCorrectSize`
- `FireProcessingSystemTests.FireProcessing_SkipsBullet_WhenShooterNotAuthoritative`
- `NavigationIntentBridgeSystemTests.NoneIntent_IsSkipped_NavStateUnchanged`
- `PhysicsQueryActionNodeTests.PhysicsQueryActionNode_GetRaycastResult_ReturnsDefaultForUnresolvedId`

New tests in this batch — all 7 pass:
- `FdpAutoSerializerFixedBufferTests.S301_SC1_ExtractFixedBuffer_ProducesJsonArray`
- `FdpAutoSerializerFixedBufferTests.S301_SC2_InjectFixedBuffer_RestoresValues`
- `FdpAutoSerializerFixedBufferTests.S301_SC3_Build_ComponentWithEntityInInlineArray_Throws` *(uses test-only component with Entity [InlineArray], no [ScenarioIgnore])*
- `FdpAutoSerializerFixedBufferTests.S301_SC4_BrainBlackboard_RoundTrip`
- `FdpAutoSerializerFixedBufferTests.S302_SC1_ExtractInlineArray_ProducesJsonArray`
- `FdpAutoSerializerFixedBufferTests.S302_SC2_InjectInlineArray_RestoresValues`
- `FdpAutoSerializerFixedBufferTests.S302_SC3_MissionPlanQueue_Phases_RoundTrip`

### Hrot.SimHost.Tests
```
Failed:  0, Passed: 407, Skipped: 3, Total: 410
```

All tests pass. The 3 skips are pre-existing DDS-domain tests that require a live
CycloneDDS network participant (`[Skip]`-attributed). New tests added in this batch:

- `MissionPlanTranslatorTests.Extract_ReturnsExpectedJsonObject`
- `MissionPlanTranslatorTests.Inject_RestoresMissionPlanAndQueue`
- `MissionPlanTranslatorTests.CanTranslate_ReturnsFalse_WhenNoActiveMissionPlan`
- `MissionPlanTranslatorTests.RoundTrip_ViaScenarioSerializer_PreservesMissionPlan`

All 4 pass.

---

## Developer Insights

### Q1: Issues Encountered and Resolutions

**Issue 1 — `GetManagedComponentRO<T>` is `internal` on `EntityRepository`.**
The design talk referenced `repo.GetManagedComponentRO<ActiveMissionPlan>(entity)` directly.
At compile time, this fails: `EntityRepository` exposes it only `internal`, and the method
is accessible only through the `ISimulationView` interface declared in
`Fdp.ModuleHost.Abstractions`. Fix: add `using Fdp.ModuleHost.Abstractions;` and cast —
`((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity)`.

**Issue 2 — Expression trees cannot call methods with `ref` or `out` parameters.**
The initial approach for `BuildInject` tried to emit expression-tree calls like
`FillFixedBuffer(ref comp.Field, ...)`. LINQ `Expression` trees forbid `ref` in
non-ByRef method invocations. Fix: the `Holder<T> where T : struct { public T Value; }`
wrapper class. The compiled helper methods receive a `Holder<TComp>` and mutate
`holder.Value` directly through `Unsafe.AddByteOffset`. This avoids `ref` at the
expression-tree call site while still performing in-place mutation of the struct.

**Issue 3 — `MemberInit` bindings are incompatible with fixed-buffer fields.**
`Expression.MemberInit` accepts `MemberAssignment` targets. The compiler-generated backing
field for a `fixed byte Mem[N]` is itself a struct type (`<Mem>e__FixedBuffer`); you
cannot bind it in a `MemberInit` block. Fix: `GetSerializableFields()` now explicitly
excludes fields with `FixedBufferAttribute` and fields whose types carry
`InlineArrayAttribute`. These fields go through the `Holder<T>` inject path exclusively.

**Issue 4 — `PassengerBuffer.Passengers` triggered the Entity-in-InlineArray safety check.**
My safety check in `Build()` throws `InvalidOperationException` when it encounters an
InlineArray field whose element type is `Entity` — enforcing that entity cross-references
in auto-serialized fields are forbidden. `PassengerBuffer.Passengers` (`PassengerSlots`,
`[InlineArray(8)] Entity`) hit this check. But `PassengerBufferTranslator` already
handles this field — it should never reach the auto-serializer. The safety check correctly
calls `GetInlineArrayFields()` which already respects `[ScenarioIgnore]`, but `Passengers`
was not yet annotated. Fix: added `[ScenarioIgnore]` to `PassengerBuffer.Passengers`
with a comment pointing to `PassengerBufferTranslator`. Also added `using Fdp.Toolkit.Scenario;`
to `InteractionComponents.cs`.

**Issue 5 — `MissionPlanTranslatorTests` interfered with `StagingEntityExtractorTests` in parallel runs.**
Original tests called `ComponentTypeRegistry.Clear()` in constructor and `Dispose()`. With
`MaxParallelThreads = 4`, this wiped the registry while `StagingEntityExtractorTests` was
mid-execution. Fix: removed `Clear()` calls entirely. `RegisterComponent<T>()` is
idempotent for attribute-declared component IDs; the tests only need a fresh
`EntityRepository` (not a fresh registry state) for isolation.

### Q2: How Is the InlineArray Element Type Detected at Runtime?

C# 12 `[InlineArray(N)]` structs contain exactly one backing field. The element type is
that field's type, regardless of whether the field is `public`, `private`, or
`internal`. Detection in `GetInlineArrayFields()`:

```csharp
var elemField = f.FieldType.GetFields(
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
    .FirstOrDefault();
if (elemField == null) continue;
// elemField.FieldType is the element type
```

This is robust: the compiler always emits exactly one instance field, and the
`[InlineArray]` attribute itself carries only the capacity (`attr.Length`).

### Q3: Why `Holder<T>` Rather Than Pure Expression Trees?

LINQ compiled expression trees do not support `ref` parameters in method invocations
(`Expression.Call` with a `ref`-returning or `ref`-taking method throws at tree
construction time). The Inject path must mutate the struct's backing memory in-place
after the `MemberInit` sets the non-special fields. `Holder<T>` sidesteps this: the
expression tree constructs a `Holder<TComp>`, assigns regular fields into `holder.Value`
via a `MemberInit`, then calls `FillFixedBuffer(holder, fieldOffset, len, jsonArr)` and
`FillInlineArray(holder, fieldOffset, len, jsonArr)` — methods that take the holder by
reference-to-class (not ref-struct) and use `Unsafe.AddByteOffset` internally. The final
`SetComponent(entity, holder.Value)` commits the result to ECS storage.

### Q4: EditorBootstrap DoctrineRegistry Is Empty — Is That Correct?

Yes. `EditorBootstrap.CreateFileService()` creates an empty `DoctrineRegistry` (no
`CgfDoctrineSetup.RegisterAll(...)` call). This is intentional: the Editor does not
load doctrine TOML files at bootstrap time. `MissionPlanTranslator.Inject` handles
unknown `BehaviorId` strings gracefully — `_registry.TryGetId(task.BehaviorId, out int id)`
returns `false`, and the code falls back to `doctrineId = 0`. Mission plans from
scenario files are still structurally restored; the resolved `DoctrineId` is
re-populated at simulation startup when `CgfDoctrineSetup` runs. This matches the
existing behavior of the Editor serializer (which saves but does not interpret runtime
doctrine state).

### Q5: Entity Safety Check Design — Why Not Throw for Translator-Handled Types?

The safety check in `Build()` calls `GetInlineArrayFields(type)` which already
filters `[ScenarioIgnore]` fields. The intended contract is:

> An InlineArray field containing `Entity` elements **must** be excluded from
> auto-serialization via `[ScenarioIgnore]`, signalling that a custom translator
> handles it.

This keeps the check simple and predictable: any InlineArray-of-Entity field without
`[ScenarioIgnore]` is an error at build time, regardless of whether a translator is
registered. The developer must make the exclusion explicit. This is preferable to
silently skipping entity fields or requiring `FdpAutoSerializer` to know the translator
list (which would create a coupling between the auto-serializer and the builder).

### Q6: Suggested Git Commit Message

```
feat(cgf-scn-2): MissionPlan translator + FdpAutoSerializer fixed-buffer/InlineArray

TASK-S201: Add MissionPlanTranslator — custom IEntityScenarioTranslator that
handles ActiveMissionPlan (managed class) and MissionPlanQueue (unmanaged
[InlineArray]) atomically. Serialises to JSON "MissionPlan" key with PlanData,
CurrentPhase, and PhaseElapsedSeconds fields. Uses Holder<T> + Unsafe on the
inject path to mutate InlineArray backing memory without ref-expression-tree
limitations.

TASK-S202: Register MissionPlanTranslator as the first translator in the
ScenarioSerializerBuilder chain at all 3 sites (SimHostApp.cs, CgfSubsystem.cs,
EditorBootstrap.cs). EditorBootstrap gets an empty DoctrineRegistry; re-resolved
at simulation startup.

TASK-S301: Extend FdpAutoSerializer to compile extract/inject delegates for
fixed-buffer fields. Detection via FixedBufferAttribute; uses Unsafe.As +
Unsafe.Add in ReadFixedBuffer<TFixed,TElem> and FillFixedBuffer<TComp,TElem>.
BrainBlackboard 128-byte buffer round-trips correctly.

TASK-S302: Extend FdpAutoSerializer to compile extract/inject delegates for
[InlineArray] fields. Element type detected from the struct's single backing
field (public or private). MissionPlanQueue.Phases ([InlineArray(8)] MissionPhase)
round-trips correctly.

Safety: Build() throws InvalidOperationException for Entity-element InlineArray
or fixed-buffer fields not marked [ScenarioIgnore]. Added [ScenarioIgnore] to
PassengerBuffer.Passengers (handled by PassengerBufferTranslator). Tests: +4
MissionPlanTranslatorTests, +7 FdpAutoSerializerFixedBufferTests; all pass.
```
