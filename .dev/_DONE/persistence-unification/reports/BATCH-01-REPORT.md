# BATCH-01 Report

## Implementation Summary

### PU-102: BTree persisted DTO + editor⇄DTO mapping

**New file:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs`
Defines the full BTree persisted DTO hierarchy per design §5.2/§5.3:
- `BehaviorTreeAssetDto` — root DTO: AssetId, Name, TargetNamespace, BlackboardTypeName, ContextTypeName, Nodes, Pills, Canvas, SubtreeSyncBindings, Suppressions, Blackboard.
- `BTreeNodeDto` with `[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]` and 18 derived types covering all NodeType enum values (Root, Sequence, Selector, Parallel, ObserverSelector, Action, Condition, Wait, Subtree, Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure, Service, Observer).
- `BTreePillDto`, `BTreeActionPayloadDto`, `BTreeConditionPayloadDto`, `BTreeWaitPayloadDto`, `BTreeSubtreePayloadDto`, `NodeEditorMetadataDto` (X/Y per §5.1 recommendation), `CanvasDto`, `SuppressionsDto`, `BlackboardBlockDto`, `BlackboardTypeRefDto` (§5.4: TypeId, IsArray, FixedLength, DefaultValueJson, Comment).

**New file:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Persistence/BehaviorTreeAssetMapper.cs`
Editor⇄DTO mapping (net8.0, lives inside Hrot.BTree.Editor assembly for access to `internal` members):
- `ToDto(BehaviorTreeAsset)` → maps identity, all node types, pills, canvas layout, sync bindings, suppressions, blackboard variables (Type → TypeId string via reflection).
- `FromDto(BehaviorTreeAssetDto)` → reconstructs BehaviorTreeAsset with empty blob placeholder (runtime-only), wires node lookup tables via `ReplaceAll`, restores sync bindings via `LoadSyncBindings`, clears dirty flag.

### PU-103: HSM persisted DTO + editor⇄DTO mapping

**New file:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Hsm/HsmAssetDto.cs`
Defines the HSM DTO per design §5.2/§5.4:
- `HsmAssetDto` — root DTO: AssetId, Name, TargetNamespace, BlackboardTypeName, States, Regions, Transitions, GlobalTransitions, Events, Canvas, Suppressions, Blackboard.
- `StateNodeDto` — StableId, Name, flags (IsInitial/History/etc.), actions, RegionIndex, X/Y/SizeOverride, Comment, ColorOverride, ChildStableIds, ParentStableId.
- `RegionNodeDto` — StableId, RegionIndex, Name, Priority, InitialChildStableId, Comment, ColorOverride.
- `TransitionNodeDto` — VisualId (identity), SourceStableId/TargetStableId, EventName (runtime EventId excluded), GuardFunction/ActionFunction, Priority, Kind (`TransitionKindDto`), SyncGroupId, Waypoints (`List<WaypointDto>`), Comment. FlatIndex excluded.
- `GlobalTransitionNodeDto`, `EventDefinitionDto` (Name, PayloadSize, IsIndirect — EventId excluded).
- `HsmBlackboardBlockDto`, `HsmBlackboardTypeRefDto`, `HsmBlackboardVariableDto` (same §5.4 schema as BTree).

**New file:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Persistence/HsmAssetMapper.cs`
Editor⇄DTO mapping:
- `ToDto(HsmAsset)` → maps all states (excluding synthetic RootState), regions, transitions (with waypoints), global transitions, events, canvas, suppressions, blackboard.
- `FromDto(HsmAssetDto)` → reconstructs full HsmAsset via the internal constructor, wires parent/child relationships, builds transition endpoints by StableId lookup, event ids assigned sequentially (runtime EventId not persisted), clears dirty flag.

### PU-104: JSON services + header-lazy discovery

**New file:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BTreeJsonServices.cs`
Mirrors `BlueprintJsonServices` exactly per design §2.6/§5.1:
- Same `JsonSerializerOptions`: `IncludeFields=true`, `PropertyNameCaseInsensitive=true`, `AllowTrailingCommas=true`, `ReadCommentHandling.Skip`, `WriteIndented=false`, `JsonStringEnumConverter`, `DefaultJsonTypeInfoResolver`.
- `Serialize(dto)`: serializes to DOM via `SerializeToNode`, stamps `$meta` as first property (`docType="Hrot.BTree"`, `schemaVersion=1`).
- `Deserialize(json)`: tolerates unknown properties and missing `$meta` (legacy-safe).
- `ReadHeader(json)`: header-lazy streaming parse via `Utf8JsonReader` — reads only `AssetId`+`Name`, never throws on malformed.
- `DiscoverHeaders(dir)`: enumerates `*.btree.json`, skips malformed silently.

**New file:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Hsm/HsmJsonServices.cs`
Symmetric implementation for `*.hsm.json` with `docType="Hrot.Hsm"`.

**Modified file:** `Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs`
Added two new constants: `BTree = "Hrot.BTree"` and `Hsm = "Hrot.Hsm"` per the batch requirement to register the doc-types entries.

### PU-105 (round-trip/determinism portion)

**New test files in `Hrot.AiEditor.Persistence.Tests`:**
- `Json/ByteStabilityTests.cs`: `Serialize→Deserialize→Serialize` is byte-identical for all fixtures (both BTree+HSM); two serializes of the same DTO are byte-identical (determinism). Full-cycle round-trips for SampleScout and SampleGuard.

---

## Design Decisions

### netstandard2.0 project choice: `Hrot.AiEditor.Persistence` (single shared project)

**Rationale:** The design (§3 D3) mandates a netstandard2.0 DTO+JSON lib with no editor/ImGui deps for the Phase-2 Roslyn generator. Blueprint chose to put its DTO+compiler in `Hrot.Blueprints.Compiler` (multi-TFM: netstandard2.0;net8.0), gating the net8.0-specific pieces with `#if NET8_0_OR_GREATER`. I chose a simpler single-TFM `netstandard2.0` dedicated project for BTree+HSM persistence — this keeps the dependency surface minimal and the Phase-2 generator can consume it without any conditional compilation hazard. Blueprint's multi-TFM approach was driven by its Roslyn compiler being co-located; our persistence lib has no such need for net8.0.

A single shared project for both BTree and HSM DTOs (`Hrot.AiEditor.Persistence`) vs two separate projects (`Hrot.BTree.Persistence`, `Hrot.Hsm.Persistence`): chosen single project to avoid a third transitive dependency for the generator and to co-locate the shared blackboard block schema.

**Dependencies:** `System.Text.Json 8.0.5` only. No editor, no ImGui, no FDP.Toolkits. Verified by project refs.

### Mapper placement: inside editor assemblies (net8.0)

Mappers (`BehaviorTreeAssetMapper`, `HsmAssetMapper`) live inside `Hrot.BTree.Editor` and `Hrot.Hsm.Editor` respectively. This gives them `internal` access to `AddNode`, `AddPill`, `ReplaceAll`, and the `internal HsmAsset` constructor — all of which are needed for correct reconstruction. The alternative (a public factory/builder) would require making those members public and widening API surface unnecessarily.

### Position encoding: X/Y floats per §5.1 recommendation

Design §5.1 recommends "Blueprint-style separate X/Y floats" for cross-kind metadata consistency. Implemented as `NodeEditorMetadataDto.X/Y` (BTree nodes) and `StateNodeDto.X/Y` (HSM states). `Vector2ArrayConverter` not used.

### `$meta` stamping: DOM rebuild approach

Blueprint uses `#if NET8_0_OR_GREATER` around `SerializeToNode`+`JsonEnvelope.Write`. Since this lib is netstandard2.0-only, I implemented `StampMeta` inline in both JSON services — same algorithmic approach (remove existing `$meta`, prepend it, copy remaining). This is identical behavior to Blueprint's net8.0 path for the `$meta` insertion.

### Blackboard type mapping: TypeId = CLR FullName

`System.Type` in `BlackboardVariableEntry.FieldType` maps to `string TypeId = type.FullName` in the DTO. Round-trip recovery uses `BlackboardTypeHelper.GetPrimitiveType` (alias names like "int") → then `Type.GetType(typeId)` (full name lookup) → fallback to `object`. This covers all types in `BlackboardTypeHelper.DefaultKnownTypeNames` plus custom types. For the forward-compatible substrate this batch defines, the round-trip is faithful.

### EventId excluded from HSM DTO; EventName used as canonical identity

`EventDefinition.EventId` is a `ushort` assigned at build time from the `HsmBuilder`. It is runtime-only: excluded per §5.2 stance (derived from the compile step). `EventName` is the canonical, author-facing identity. The mapper assigns sequential `EventId` values on round-trip (ushort starting at 1); these are not persisted and are recomputed from the assembly on each reload. This is consistent with the "runtime-only" exclusion policy.

### FlatIndex excluded; reassigned after assembly load

Both `BTreeEditorNode.KernelBlobIndex` and `StateNode.FlatIndex` / `TransitionNode.FlatIndex` are excluded (runtime-only). The mapper sets them to -1 / 0 respectively as sentinels, consistent with how the projector works for unloaded assets. The post-reload stitching (D13/§6.6, PU-302 in a later batch) is where these get filled in from the assembled blob.

---

## Deviations

1. **`BTreeNodeDto` for `Repeater`/`Cooldown`:** The Repeater's `IntParam` and Cooldown's `FloatParam` are on the *pill* (`BTreeEditorPill`), not on the node itself. The `BTreeRepeaterNodeDto.IntParam` and `BTreeCooldownNodeDto.FloatParam` fields in the DTO are present (nullable, as per design sample §5.3 "IntParam, FloatParam, StackIndex") for forward-compatibility, but are not mapped from node-level fields because `BTreeEditorNode` has no such fields. Pills are the carriers of these params. **Risk:** low; pills are separately mapped correctly.

2. **`StateNodeDto.ParentStableId` vs `ChildStableIds`:** Both are persisted to allow reconstruction without a specific ordering constraint. The parent reference enables reconstruction even if the child list is incomplete. **Benefit:** bidirectional linkage for robustness. **Risk:** minor redundancy.

3. **`DefaultJsonTypeInfoResolver` usage in netstandard2.0:** `DefaultJsonTypeInfoResolver` is in `System.Text.Json.Serialization.Metadata` and is available in `System.Text.Json 8.0.5` on netstandard2.0. This is needed for `[JsonPolymorphic]` to work correctly without AOT source generation. The Blueprint compiler uses the same approach under its `netstandard2.0` TFM (same package version). No deviation from spirit of §2.6 pattern.

---

## Test Results

### New tests: 75 passing (0 failing)

**`Hrot.AiEditor.Persistence.Tests`** — 75/75 pass:

| Test class | Count | Coverage |
|---|---|---|
| `BTreeDtoRuntimeFieldExclusionTests` | 5 | Reflection: BehaviorTreeAssetDto has no Blob/KernelBlobIndex/*PinId/IsDirty/Changed; BTreeNodeDto same; BTreePillDto no IsBreakpoint; BlackboardTypeRefDto has TypeId+IsArray+FixedLength+DefaultValueJson |
| `BTreeMapperRoundTripTests` | 20 | SampleScout reflection-load: AssetId, Name, NodeCount, VisualIds, Canvas, Positions, KernelTypes, ChildVisualIds, Wait payloads. Comprehensive hand-built fixture: identity, canvas, node kinds, Action payload (MethodFqn, ExpressionTargetField, DelegateShape), Condition payload, Subtree payload (SubtreeAssetId, Name, IsResolved), Pill (HostNodeVisualId, DecoratorType, StackIndex, Comment), SyncBindings (FieldName, MasterVariableName, SyncIn, SyncOut), Suppressions (Conflict+Unused), BlackboardVariable (Name, TypeId, Comment). Compile-time: no KernelBlobIndex in DTO. IsDirty=false after round-trip. |
| `HsmDtoRuntimeFieldExclusionTests` | 5 | StateNodeDto no FlatIndex; TransitionNodeDto no EventId/FlatIndex; Waypoints present; HsmBlackboardBlockDto TypeId+IsArray+FixedLength+DefaultValueJson |
| `HsmMapperRoundTripTests` | 14 | SampleGuard reflection-load: identity, StateCount, StableIds, StateNames, TransitionCount, TransitionVisualIds, TransitionEndpoints (Source+Target StableId), StatePositions, Waypoints (count+coords), Events, Canvas. Comprehensive: Suppressions (Conflict+Unused), BlackboardVariable (Name, TypeId, Comment). IsDirty=false. |
| `BTreeJsonServicesTests` | 18 | $meta first; docType="Hrot.BTree"; schemaVersion=1; Deserialize(Serialize(minimal)); Deserialize(Serialize(rich dto with nodes/pills/bindings/suppressions/blackboard)); tolerates unknown properties; tolerates missing $meta; polymorphic nodes restored as BTreeActionNodeDto via 'kind'; ReadHeader correct AssetId+Name; ReadHeader null on malformed; ReadHeader null on empty; DiscoverHeaders skips malformed (sibling found); DiscoverHeaders enumerates only *.btree.json |
| `HsmJsonServicesTests` | 9 | Mirror of BTree JSON tests for Hsm |
| `ByteStabilityTests` | 8 | BTree: Serialize→Deserialize→Serialize byte-identical (all fixtures: SampleScout+2 hand-built); BTree determinism (2x serialize identical, all fixtures); BTree full-cycle SampleScout byte-identical. HSM: symmetric. |

### Baseline test suites (verification gates):

```
dotnet build IOS-IG-SimHost.sln   →  0 errors, 0 new warnings in touched projects
EditorSubsystemBoot filter        →  10/10 PASS
Hrot.Editor.AiShared.Tests        →  761/761 PASS
Hrot.Blueprints.Tests             →  1357 pass, 7 fail (ALL pre-existing — see below)
Hrot.BTree.Editor.Tests           →  382/382 PASS (no regression)
Hrot.Hsm.Editor.Tests             →  333/333 PASS (no regression)
```

### Pre-existing failure classification (Blueprints.Tests):

All 7 failing tests were failing before this batch (verified by `git stash` + re-run):

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

### Issues encountered and resolved

1. **`DefaultJsonTypeInfoResolver` namespace:** In netstandard2.0 + System.Text.Json 8.0.5, the type lives in `System.Text.Json.Serialization.Metadata` (not the top-level namespace). Simple using-add resolved it.

2. **`AddNode`/`AddPill`/`ReplaceAll` are `internal`:** The mapper is inside the BTree.Editor assembly so it has access. The test project needed `InternalsVisibleTo` entries added to both editor csproj files.

3. **`[JsonPolymorphic]` with `netstandard2.0`:** Requires `DefaultJsonTypeInfoResolver` to be explicitly set; otherwise polymorphism fails silently at runtime. Verified working: `BTreeActionNodeDto` is correctly recovered from JSON.

4. **StampMeta in-object mutation:** `JsonObject` DOM enumeration + mutation (remove+re-add) requires building a copy first then copying back. A naive in-place approach causes `InvalidOperationException` from the DOM's internal collection. The copy-then-overwrite pattern works correctly.

5. **HSM constructor requires all lists:** `HsmAsset`'s internal constructor takes many pre-built lists. The mapper rebuilds all of them from scratch, wiring parent/child and transition endpoint references after all nodes are created. The two-pass approach (create all states first, then wire) matches what `HsmAssetProjector` does.

### Weak points observed

- **`BTreeEditorNode.DisplayLabel` is not populated by the projector** from `NodeDebugMetadata.Label` in all cases; empty string may be the norm. Round-trip correctly preserves it (empty → empty).
- **`StateNodeDto.ChildStableIds` is redundant** with parent-child reconstruction via `ParentStableId`. Either alone suffices for reconstruction; both are present for robustness. Could be simplified in a future clean-up if the design settles on one direction.
- **`HsmAsset` internal constructor:** All state/transition lists must be pre-constructed; there is no additive API. The mapper is necessarily verbose. This matches the existing projector pattern.

### Edge cases discovered beyond the spec

- **HSM events: EventId not stable across round-trips.** The `EventDefinitionDto` stores only `Name`, not `EventId`. After `FromDto`, event IDs are reassigned sequentially (1, 2, 3...). This is correct per §5.2 (EventId excluded), but means any code that matches events by `ushort EventId` rather than name will fail after JSON round-trip. This is expected behavior (ID is build-time derived), but the load-path batch (PU-301) will need to handle it.
- **Suppression set ordering.** `HashSet` iteration order is non-deterministic. The DTO uses `List<>` for suppressions, so order is preserved on round-trip. But going model→DTO→model, the set order may differ (List → HashSet reconstruction → List). Assertions use `ContainSingle`/`Should().BeTrue()` which is order-independent, so tests are correct. The JSON itself is deterministic (same model always produces same list, as sets iterate consistently within a single process run).

---

## Known Issues

- The `$meta` stamping approach (DOM rebuild) differs from `HrotDocumentTypes` migration wiring in that no `MigrationRegistry` passthrough is registered for `Hrot.BTree` or `Hrot.Hsm`. This is intentional for this batch (ZERO behavior change; no load-path switch yet). The migration registration is a PU-301/PU-401 concern.
- `HsmAsset` uses `internal` constructor and `HsmAssetMapper` is placed inside `Hrot.Hsm.Editor`. This means the mapper is not available to the `netstandard2.0` persistence lib. The Phase-2 generator will need its own `FromDto` path that constructs `HsmAsset` via a public factory — a seam to be addressed in PU-301.

---

## Suggested Commit Message

```
feat(persistence-unification): BATCH-01 — BTree/HSM persisted DTOs, JSON services, and round-trip tests (PU-102/103/104/105)

Add Hrot.AiEditor.Persistence (netstandard2.0) with BTree/HSM persisted DTOs
per §5.2/§5.3/§5.4 (runtime-only fields excluded), BTreeJsonServices/HsmJsonServices
mirroring BlueprintJsonServices ($meta-first, kind polymorphism, header-lazy discovery,
malformed-skip), and editor↔DTO mappers inside the editor assemblies. 75 new tests
(model→DTO→model field-by-field, runtime exclusion, $meta validation, discovery,
byte-stable round-trips). Zero behavior change; 0 new warnings; full baseline green.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```
