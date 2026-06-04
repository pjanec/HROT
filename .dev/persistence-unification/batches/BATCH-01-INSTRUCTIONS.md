# BATCH-01: BTree/HSM persisted DTOs + JSON services + round-trip tests
**Tasks:** PU-102, PU-103, PU-104, PU-105 (round-trip/determinism portion)  **Phase:** 1 (JSON substrate; keystone)  **Est:** ~16h
**Dependencies:** none (first batch of this thread). **Sequencing note:** the emit-core extraction (PU-101) is deferred to BATCH-02 because the emit core *consumes* the DTO this batch defines.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (verify-first, cite file:line, never fake a pass, run the full test suite to green, then report).
2. `.dev/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — the spec. Read **§2.6** (Blueprint JSON = the reference pattern), **§3** (decisions D3 model=B2, D9 blittable blackboard), **§5** (the JSON schema: §5.1 serializer/envelope, §5.2 persist-vs-drop, §5.3 BTree shape, §5.4 blackboard block). Do not re-derive — cite it.
3. `.dev/persistence-unification/TASK-DETAIL.md` — success conditions for PU-102, PU-103, PU-104, PU-105.
4. **Codebase Memory MCP first** (`search_graph`/`get_code_snippet`/`trace_path`; project `D-Work-IOS-IG-SimHost-FDP-2`; never `search_code`).

## Reference implementation to mirror (the Blueprint pattern — verify-first, cite)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs` — `Serialize`/`Deserialize`: System.Text.Json, `JsonStringEnumConverter`, `PropertyNameCaseInsensitive`, `AllowTrailingCommas`, `IncludeFields`, `WriteIndented=false`, `$meta` envelope (`docType`,`schemaVersion`) stamped first, unknown-property tolerant. Mirror its settings EXACTLY.
- Blueprint DTO/discovery: `[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]` nodes; `BlueprintAssetContributor` header-lazy enumerates `*.bp.json` reading only `AssetId`+`Name`. Mirror the header-lazy discovery pattern.
- Determine the netstandard2.0 home: the persisted DTOs + JSON services MUST live in a `netstandard2.0` library with **no editor (net8) / ImGui dependency**, so the Phase-2 Roslyn generator can consume them (design D3). Verify where Blueprint's DTO+JSON services live and their TargetFramework; create/choose an analogous `netstandard2.0` project (e.g. a new `Hrot.BTree.Persistence` / `Hrot.Hsm.Persistence` or a shared `Hrot.AiEditor.Persistence`). Record the project choice + why in the report.

## Source models to map (verify-first; quote the fields)
- BTree editor model: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` (+ node/pill types, `BehaviorTreeAssetProjector.cs`). Identity, topology (nodes/pills, child links by `VisualId`, action/condition keys, payloads, decorator pills), layout (positions, pan/zoom, comment, collapse, color), subtree sync bindings, suppressions, blackboard vars (`List<BlackboardVariableEntry>` — `Name`, `System.Type FieldType`, `Comment`; map `System.Type`↔`TypeId` via the existing `BlackboardTypeHelper`).
- HSM editor model: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` (+ `HsmAssetProjector.cs`). States/transitions/regions/global-transitions/events; `StableId` (states/regions) / `VisualId` (transitions) identity; transition waypoints/kind/priority/sync-group; region structure.
- **§5.2 persist-vs-drop is authoritative:** persist topology+layout+sync+suppressions+blackboard block; **exclude** runtime/projection-only fields (`Blob`/`Metadata`, `KernelBlobIndex`/`FlatIndex`, derived `*PinId`, `_syncNodeMeta`, `_aliases` hydration, `LoadDiagnosticMessage`, `IsDirty`, `Changed`, `IsBreakpoint`).

## Corrective Task 0
None (first batch).

## Tasks (complete in sequence; do NOT start the next task until the current task's implementation is done, its tests are written, and ALL tests — including prior tasks' — pass.)

### Task 1 — PU-102: BTree persisted DTO + editor⇄DTO mapping — files: new `netstandard2.0` DTO type(s) + a mapper in/near `Hrot.BTree.Editor` (NEW)
Define the BTree persisted DTO per design §5.2/§5.3: identity; node topology with `kind` polymorphism (`[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]`), decorator pills, child links by `VisualId`; layout as an `EditorMetadata` block (X/Y per §5.1 recommendation — Blueprint-style separate floats), pan/zoom/comment/collapse/color; `SubtreeSyncBindings`; `Suppressions`; the forward-compatible `Blackboard` block (§5.4: `Managed`, `TypeName`, `HeavyDtoType`, `Variables[]` each with `Type { TypeId, IsArray, FixedLength }` + `DefaultValueJson` + `Comment`). Add `BehaviorTreeAsset`⇄DTO mapping (both directions).
**Tests required:** mapping round-trip `model → DTO → model` preserves every persisted field per §5.2, asserted **field-by-field on real fixtures** (use the existing `Trees/*.cs` editor-owned sample assets via the current reflection/projector load to get a populated model, OR a hand-built model fixture if reflection-load is awkward — cite which). A reflection/compile-time test that the DTO contains **none** of the runtime-only fields listed in §5.2. Blackboard type-ref expresses `TypeId`+`IsArray`+`FixedLength`+`DefaultValueJson` (schema present, may be empty `Variables` for existing assets).

### Task 2 — PU-103: HSM persisted DTO + editor⇄DTO mapping — files: new `netstandard2.0` DTO + mapper near `Hrot.Hsm.Editor` (NEW)
As Task 1 for `HsmAsset` (§5.2/§5.4): states/transitions/regions/global-transitions/events; `StableId`/`VisualId` identity; transition waypoints/kind/priority/sync-group; region structure; same blackboard block.
**Tests required:** model→DTO→model preserves all persisted HSM fields incl. regions, global transitions, transition waypoints; runtime-only fields excluded; blackboard block as Task 1. Field-by-field on fixtures.

### Task 3 — PU-104: JSON services + header-lazy discovery — files: `BTreeJsonServices` / `HsmJsonServices` (+ discovery) (NEW)
`BTreeJsonServices`/`HsmJsonServices` mirroring `BlueprintJsonServices` exactly (§5.1): `$meta` first, `docType` = `Hrot.BTree`/`Hrot.Hsm` (add the entries to the doc-types enum/constants — find where `Hrot.Blueprints` is defined), `schemaVersion` 1, polymorphic `kind` nodes. Header-lazy discovery for `*.btree.json`/`*.hsm.json` reading only `AssetId`+`Name` without full deserialization, never throwing on a malformed file (skip it). Mirror `BlueprintAssetContributor`'s header-lazy approach.
**Tests required:** `Deserialize(Serialize(dto))` structurally equals `dto` for all fixtures; tolerates unknown properties and missing `$meta`. `$meta` is the FIRST property; `docType`/`schemaVersion` correct (assert on the raw JSON text). Discovery enumerates `*.btree.json`/`*.hsm.json`, returns `AssetId`+`Name` without full deserialize, and **skips a malformed file without throwing** (write a deliberately-corrupt file and assert it's skipped, siblings still found).

### Task 4 — PU-105 (round-trip/determinism portion): byte-stability tests — files: new test file(s) (NEW)
**RT byte-stability:** `Serialize → Deserialize → Serialize` is **byte-identical** for every fixture (both kinds). Also assert serializing the same DTO twice is byte-identical (determinism). (Re-basing `SaveBTreeEmitTests`/`SaveHsmEmitTests` onto the emit core is deferred to BATCH-02 with PU-101.)
**Tests required:** the byte-identical RT loop over every BTree+HSM fixture; a determinism assertion (two serializes identical).

## Success Criteria
- [ ] PU-102: BTree DTO + mapping; model→DTO→model field-by-field preserved; runtime-only fields excluded. + tests pass.
- [ ] PU-103: HSM DTO + mapping; same guarantees. + tests pass.
- [ ] PU-104: BTree/HsmJsonServices mirror Blueprint settings; `$meta` first; header-lazy discovery skips malformed. + tests pass.
- [ ] PU-105 (RT): serialize→deserialize→serialize byte-identical for all fixtures; determinism. + tests pass.
- [ ] DTOs + JSON services compile in a `netstandard2.0` library with **no net8/editor/ImGui reference** (verify via project refs — record in report).
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors, 0 new warnings in touched projects; `EditorSubsystemBoot` filter 10/10; `Hrot.Editor.AiShared.Tests` green; `Hrot.Blueprints.Tests` only the 10 pre-existing DEBT-006 (0 new); no other baseline regression. **Report exact failing-test counts.**
- [ ] Report submitted to `.dev/persistence-unification/reports/BATCH-01-REPORT.md`.

## Report Requirements (answer in the report)
Issues encountered; the netstandard2.0 project choice + why; how you obtained populated model fixtures (reflection-load vs hand-built) and which fixtures exist; any §5.2 field whose persist/drop classification was ambiguous and how you resolved it; the position-encoding decision (X/Y vs Vector2); weak points; design decisions beyond spec; edge cases discovered; suggested commit message. Do NOT ask comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts stays 0.2.2. No `Hrot.IG`/DDS/`Stride/`. No `editor_stride`. Keep edits in BTree/HSM editor projects + the new netstandard2.0 lib(s) + tests. **Do not touch the Blueprint path** (reuse its patterns; don't regress it). **Zero behavior change this batch** — no load-path switch, no generator, no `.cs` decommit. Do NOT commit (the lead commits).
