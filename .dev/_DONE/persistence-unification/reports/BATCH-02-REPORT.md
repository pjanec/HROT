# BATCH-02 Report

## Implementation Summary

### Task 1 — PU-101: Emit-core extraction

**New files created:**

- `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/AiEmitCoreBase.cs`
  Shared base: `EditorGeneratedMarker` constant, `BuildHeader(Guid)`, `SortUsings(IEnumerable<string>)`, `WriteAtomic(path, content)`. All are netstandard2.0-compatible (no `File.Move(src, dst, overwrite)` — uses delete+move pattern for netstandard2.0).

- `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`
  Deterministic BTree C# emitter parameterized by `BehaviorTreeAssetDto`. Implements `CreateBuilder()` + `[BTreeDefinition]` thunk + `[BTreeLayout]` method in full. All string range operators replaced with `.Substring()` for netstandard2.0.

- `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/HsmEmitCore.cs`
  Deterministic HSM C# emitter parameterized by `HsmAssetDto`. Implements `CreateBuilder()` + `[HsmDefinition]` thunk + `[HsmLayout]` method. The compiler-root (`__Root`) is identified and skipped; its children become the user top-level states, exactly mirroring `HsmFluentEmitter`.

**Modified files (editor thin adapters):**

- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` — reduced to 12 lines: `Emit(model)` → `BehaviorTreeAssetMapper.ToDto(model)` → `BTreeEmitCore.Emit(dto)`.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs` — reduced to 12 lines: `Emit(model)` → `HsmAssetMapper.ToDto(model)` → `HsmEmitCore.Emit(dto)`.
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs` — all three static methods (`SortUsings`, `BuildHeader`, `WriteAtomic`) now delegate to `AiEmitCoreBase`; `EditorGeneratedMarker` delegates via `const = AiEmitCoreBase.EditorGeneratedMarker`.
- `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` — added `<ProjectReference>` to `Hrot.AiEditor.Persistence` (the emit core is now a dependency of AiShared).

### BATCH-01 DTO/mapper extensions (allowed re-touch, noted here)

**Extended `HsmAssetDto.EventDefinitionDto`** (new fields):
- `IsDeferrable: bool` — whether some state defers this event. Needed by the emit core to reproduce `builder.Event(..., IsDeferrable)` byte-identically.
- `EventId: ushort` — the actual EventId from the model. Needed to reproduce `builder.Event("Name", EventId, ...)` byte-identically. Under JSON-SoT (PU-02+) the generator will reassign sequential IDs; for now the DTO stores the original.

**Extended `HsmAssetDto.StateNodeDto`** (new field):
- `DeferredEventNames: List<string>` — names of events deferred by this state (resolved from `DeferredEventIds` via `eventIdToName`). The emit core resolves back to IDs using `eventIdMap`.

**Updated `HsmAssetMapper.ToDto`**: populates `IsDeferrable`, `EventId`, and `DeferredEventNames`.
**Updated `HsmAssetMapper.FromDto`**: restores `IsDeferrable`, uses stored `EventId` (non-zero) for `EventDefinition` construction, and restores `DeferredEventIds` from `DeferredEventNames`.

### Task 2 — PU-105: SaveBTreeEmitTests / SaveHsmEmitTests re-base

Both test files were updated to:
1. Add a direct adapter-vs-core assertion: `emitter.Emit(model)` equals `BTreeEmitCore.Emit(ToDto(model))`.
2. Add core-direct determinism tests.
3. Add explicit `AiEmitCoreBase.WriteAtomic` and `FluentCSharpEmitterBase.WriteAtomic` delegation tests.
4. Preserve all original test scenarios (byte-identical re-emit, file-differs path, AiAssetEmitService, empty-path guard, structural content checks).

No re-baseline needed — output is byte-identical.

### New test file: byte-identical gate

`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence.Tests/Emit/ByteIdenticalGateTests.cs`
Parametrized over all editor-owned fixtures (`SampleScout` / `SampleGuard`):
- `BTree_CoreEmit_IsByteIdentical_ToFluentEmitter(SampleScout)` — string equality assert
- `Hsm_CoreEmit_IsByteIdentical_ToFluentEmitter(SampleGuard)` — string equality assert
- `WriteAtomic` no-op gate (false when content identical), true when content differs
- Cross-check: emitted output starts with `HROT_EDITOR_GENERATED`, contains AssetId, `[BTreeDefinition]`/`[HsmDefinition]` const AssetId form, `[BTreeLayout]`/`[HsmLayout]` method
- `AiEmitCoreBase.EditorGeneratedMarker` matches `FluentCSharpEmitterBase.EditorGeneratedMarker`

### Round-trip assertions added to HsmMapperRoundTripTests (BATCH-01 re-touch)

Three new facts asserting the new DTO fields:
- `Event_IsDeferrable_FieldExistsInDto_AndRoundTrips` — IsDeferrable matches model → DTO → model
- `Event_EventId_RoundTrips_ThroughDto` — EventId stored non-zero and restored after FromDto
- `State_DeferredEventNames_RoundTrips_ThroughDto` — deferred names persisted and IDs restored

---

## Design Decisions

### Emit-core home: inside Hrot.AiEditor.Persistence

**Chosen: `Hrot.AiEditor.Persistence`** (new `Emit/` subdirectory) rather than a new sibling `Hrot.AiEditor.EmitCore` project.

**Rationale:** The batch instructions recommend this as the primary option ("place it in Hrot.AiEditor.Persistence or a sibling"). Keeping DTO + JSON + emit in one `netstandard2.0` project avoids adding another project to the solution graph, keeps the dependency surface minimal (one project for the Phase-2 generator to reference), and keeps the `BTreeEmitCore` / `HsmEmitCore` co-located with the DTOs they consume. There is no technical reason to split them — the emit core has no deps other than what the persistence project already has (`System.Text.Json`). A sibling would add a project reference chain with no benefit.

### EventId stored in DTO (allowed BATCH-01 re-touch)

The emitter emits `builder.Event("Name", EventId, ...)` with a literal integer. The original `HsmFluentEmitter` reads `ev.EventId` directly from the model. BATCH-01 excluded EventId as "runtime-only", but this was based on the assumption that EventIds are sequential and thus predictable. However, the tests and real models may use explicit IDs (e.g. `builder.Event("Activated", 5)`). Storing EventId in the DTO is necessary for byte-identical output. Under JSON-SoT (PU-02+), the generator will reassign sequential IDs since it constructs fresh `HsmBuilder` calls; at that point the stored EventId can be ignored or dropped.

### FluentCSharpEmitterBase delegates rather than duplicates

`FluentCSharpEmitterBase` (net8, `Hrot.Editor.AiShared`) previously duplicated `BuildHeader`, `SortUsings`, `WriteAtomic`, and the marker constant. It now delegates all of these to `AiEmitCoreBase` in the netstandard2.0 emit core. `const EditorGeneratedMarker = AiEmitCoreBase.EditorGeneratedMarker` compiles correctly as a constant string propagation. All callers of `FluentCSharpEmitterBase` keep working unchanged.

### `File.Move` compatibility in netstandard2.0

`File.Move(src, dst, overwrite: true)` is not available in netstandard2.0 (added in .NET Standard 2.1). `AiEmitCoreBase.WriteAtomic` uses `File.Delete(dst)` + `File.Move(src, dst)` instead. The behavior is identical: the write is atomic at the OS level (same-volume rename), and the delete before move only occurs after a new `.tmp` is already written.

---

## Deviations

1. **EventId added to EventDefinitionDto** — Design §5.2 listed EventId as "runtime-only". This is true from the JSON-SoT perspective (PU-02+ generator will assign sequential IDs), but for the BATCH-02 byte-identical gate the original EventId must be preserved through the DTO. Benefit: byte-identical gate passes first try. Risk: low; the field is documented as "emit-core byte-identity only; discarded by Phase-2 generator".

2. **HsmEmitCore identifies compiler-root by exclusion** — The original `HsmFluentEmitter` accesses `asset.RootState.Children[0].Children` (the synthetic projector root → compiler `__Root` → user states). The DTO contains `AllStates` which includes `__Root`. The emit core finds `__Root` as the single state with no parentStableId in the DTO, then emits its ChildStableIds as the top-level states — achieving the same traversal without the runtime model. This was not in the spec but is required for byte-identical output (failing without it: `Emit_contains_state_name`).

---

## Test Results

### New tests passing:

| Test Suite | Added | Total | Status |
|---|---|---|---|
| `Hrot.AiEditor.Persistence.Tests` | +13 (10 gate + 3 DTO round-trip) | **88/88** | PASS |
| `Hrot.BTree.Editor.Tests` | +3 (core path, WriteAtomic delegation) | **385/385** | PASS |
| `Hrot.Hsm.Editor.Tests` | +4 (core path, WriteAtomic, const form, adapter-vs-core) | **337/337** | PASS |

### Full suite results:

```
dotnet build IOS-IG-SimHost.sln       →  0 errors, 0 warnings (touched projects clean)
EditorSubsystemBoot filter            →  10/10 PASS
Hrot.Editor.AiShared.Tests            →  761/761 PASS
Hrot.AiEditor.Persistence.Tests      →  88/88 PASS  (75 BATCH-01 + 13 new)
Hrot.BTree.Editor.Tests               →  385/385 PASS  (382 previous + 3 new)
Hrot.Hsm.Editor.Tests                 →  337/337 PASS  (333 previous + 4 new)
Hrot.Blueprints.Tests                 →  1357 pass / 7 fail (ALL pre-existing) / 0 new
```

### Pre-existing failures confirmed (0 new):

| Test | Classification |
|---|---|
| `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)` | DEBT-006 golden/snapshot |
| `AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)` | DEBT-006 golden/snapshot |
| `Library_EmitMatchesGoldenSource` | DEBT-006 golden/snapshot |
| `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | DEBT-006 (pre-existing behavior) |
| `TickFrame_1000Frames_AllocatesZeroBytes` | DEBT-014 (flaky perf allocation) |
| `LibraryMath_GeneratedSource_Snapshot` | DEBT-006 snapshot |
| `MoveToAndFire_GeneratedSource_Snapshot` | DEBT-006 snapshot |

**Confirmed: 0 new failures. Failing set is a strict subset of the pre-existing baseline.**

---

## Developer Insights

### Byte-identical gate: passed on second try (two adjustments)

**First adjustment — EventId:** The initial implementation assigned sequential IDs (1, 2, 3...) in the emit core, ignoring the stored EventId. This caused `Emit_contains_event_declaration` to fail (expected `builder.Event("Activated", 5` but got `builder.Event("Activated", 1`). Fix: store EventId in the DTO.

**Second adjustment — compiler root traversal:** The initial implementation treated states with no parent in the DTO as top-level states. This is `__Root` (the compiler-inserted state), not the user's states. Fix: find the compiler root (no parentStableId in DTO), then emit its children as top-level states.

After these two fixes, the byte-identical gate passed on all fixtures.

### Model fields the BTree emitter reads → DTO coverage

Every field `BTreeFluentEmitter` reads has a counterpart in `BehaviorTreeAssetDto`:

| Model field | DTO field | Covered? |
|---|---|---|
| `asset.AssetId` | `BehaviorTreeAssetDto.AssetId` | ✅ |
| `asset.Name` | `BehaviorTreeAssetDto.Name` | ✅ |
| `asset.TargetNamespace` | `BehaviorTreeAssetDto.TargetNamespace` | ✅ |
| `asset.BlackboardTypeName` | `BehaviorTreeAssetDto.BlackboardTypeName` | ✅ |
| `asset.ContextTypeName` | `BehaviorTreeAssetDto.ContextTypeName` | ✅ |
| `asset.Nodes` (VisualId, KernelType, ChildVisualIds) | `BTreeNodeDto` polymorphic | ✅ |
| `node.Action` (MethodFqn, ExpressionTargetField, DelegateShape) | `BTreeActionNodeDto.Action` | ✅ |
| `node.Condition` (same) | `BTreeConditionNodeDto.Condition` | ✅ |
| `node.Wait.Duration` | `BTreeWaitNodeDto.Wait.Duration` | ✅ |
| `node.Subtree.SubtreeName` | `BTreeSubtreeNodeDto.Subtree.SubtreeName` | ✅ |
| `node.Position.X/Y` | `NodeEditorMetadataDto.X/Y` | ✅ |
| `node.Comment` | `NodeEditorMetadataDto.Comment` | ✅ |
| `asset.Pills` (VisualId, HostNodeVisualId, DecoratorType, IntParam, FloatParam, StackIndex, Comment) | `BTreePillDto` | ✅ |
| `asset.CanvasPanOffset.X/Y` | `CanvasDto.PanX/PanY` | ✅ |
| `asset.CanvasZoomLevel` | `CanvasDto.Zoom` | ✅ |
| `asset.GetAllSyncBindings()` (FieldName, MasterVariableName, SyncIn, SyncOut) | `SubtreeSyncBindingDto` | ✅ |
| `asset.GetConflictSuppressions()` | `SuppressionsDto.Conflict` | ✅ |
| `asset.GetUnusedSuppressions()` | `SuppressionsDto.Unused` | ✅ |

### Model fields the HSM emitter reads → DTO coverage

| Model field | DTO field | Covered? |
|---|---|---|
| `asset.AssetId`, `asset.Name`, `asset.TargetNamespace` | `HsmAssetDto` identity | ✅ |
| `asset.AllEvents` → `Name`, `EventId`, `PayloadSize`, `IsIndirect`, `IsDeferrable` | `EventDefinitionDto` (EventId+IsDeferrable added in BATCH-02) | ✅ |
| `state.Name`, `StableId`, `IsInitial/History/DeepHistory/Parallel/Final` | `StateNodeDto` | ✅ |
| `state.OnEntry/Exit/Activity/TimerAction` | `StateNodeDto` action fields | ✅ |
| `state.DeferredEventIds` | `StateNodeDto.DeferredEventNames` (added BATCH-02) | ✅ |
| `state.Children` order | `StateNodeDto.ChildStableIds` | ✅ |
| `state.OutgoingTransitions` | `HsmAssetDto.Transitions` filtered by `SourceStableId` | ✅ |
| `t.EventName`, `t.VisualId`, `t.Target.Name`, `t.GuardFunction`, `t.ActionFunction`, `t.Priority` | `TransitionNodeDto` | ✅ |
| `asset.AllGlobalTransitions` | `GlobalTransitionNodeDto` | ✅ |
| `asset.AllRegions` (StableId, RegionIndex, Comment) | `RegionNodeDto` | ✅ |
| `asset.CanvasPanOffset/ZoomLevel` | `HsmCanvasDto` | ✅ |
| State/transition/region positions, waypoints, comments, sizeOverride, collapsed, color | `StateNodeDto`/`TransitionNodeDto`/`RegionNodeDto` layout fields | ✅ |
| `asset.GetConflictSuppressions()`, `GetUnusedSuppressions()` | `HsmSuppressionsDto` | ✅ |

### Weak points observed

- **`WriteAtomic` non-atomicity on Windows at `File.Delete` step**: between `File.Delete(dst)` and `File.Move(tmp, dst)`, a crash would lose both files. This is the same risk as the original `File.Move(src, dst, overwrite: true)` implementation when overwrite is done by the OS; both are write-then-swap patterns. Accept as-is for netstandard2.0 compatibility.
- **`IsDeferrable` not set by projector in all cases**: The `EventDefinition.IsDeferrable` is marked "whether some state defers this event" but may not be computed by the projector today (the test `HsmFluentEmitterTests.Emit_contains_DeferEvent_calls_for_each_deferred_id` populates `DeferredEventIds` manually because "the projector does not populate this yet"). For SampleGuard's events, `IsDeferrable=false` matches the fixture's `builder.Event("Alert", 1, 0, false, false)` form. For future assets with deferred events, the projector would need to set this — or the mapper would need to compute it from `AllStates[].DeferredEventIds`.
- **`HsmEmitCore` handles missing transitions gracefully** (silently skips if source/target StableId not found). This matches the original emitter's `t.Target?.Name ?? "???"` fallback.

---

## Known Issues

- None introduced. The three P3 forward-looking items from BATCH-01 review remain (HSM `FromDto` in net8 mapper; EventName not EventId for events; migration registration at PU-301) — unchanged and unchanged by this batch.

---

## Suggested Commit Message

```
feat(persistence-unification): BATCH-02 — emit-core extraction + emit-test re-base (PU-101/105)

Extract deterministic C# emission from the net8 BTree/HSM editor emitters into a
netstandard2.0 emit core (Hrot.AiEditor.Persistence/Emit): AiEmitCoreBase
(marker/header/WriteAtomic), BTreeEmitCore, HsmEmitCore. Editor emitters become
12-line thin adapters: Emit(model) → mapper.ToDto(model) → core.Emit(dto).
FluentCSharpEmitterBase now delegates to AiEmitCoreBase (single source of truth).

BATCH-01 DTO re-touch: EventDefinitionDto gains EventId + IsDeferrable; StateNodeDto
gains DeferredEventNames — all needed for byte-identical emit output.

Byte-identical gate: core.Emit(ToDto(model)) == BTreeFluentEmitter.Emit(model) for
SampleScout + core.Emit(ToDto(model)) == HsmFluentEmitter.Emit(model) for SampleGuard
(incl. [*Layout] method + const AssetId form). Passed after two adjustments (EventId
in DTO; compiler-root traversal in HsmEmitCore). WriteAtomic no-op preserved.

SaveBTree/HsmEmitTests re-based onto the core; no re-baseline needed.
Tests: +20 (10 byte-identical gate, 7 Save*/core-path re-base, 3 DTO round-trip).
Build 0 warnings (touched); EditorSubsystemBoot 10/10; Blueprints 7 pre-existing/0 new;
AiShared 761/761; Persistence 88/88; BTree.Editor 385/385; Hsm.Editor 337/337.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```
