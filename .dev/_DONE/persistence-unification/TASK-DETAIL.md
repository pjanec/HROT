# Persistence Unification (BTree/HSM to JSON) — Task Detail

> **Design:** [`BTree_HSM_JSON_Persistence_Detailed_Design.md`](./BTree_HSM_JSON_Persistence_Detailed_Design.md) (referenced by `§` below — do not duplicate; read the design chapter first).
> **Tracker:** [`TASK-TRACKER.md`](./TASK-TRACKER.md) · **Debt:** [`DEBT-TRACKER.md`](./DEBT-TRACKER.md) · **Dev contract:** [`../.guides/DEV-GUIDE_claude.md`](../.guides/DEV-GUIDE_claude.md)
> **Conventions:** branch `blueprint-integ-1`; rebase on Thread 2 first; GizmoMap.Contracts stays 0.2.2; no `Hrot.IG`/DDS/`Stride/`. Codebase Memory MCP first (`search_graph`/`get_code_snippet`, **never** `search_code`). Delegate implementation/test-fix loops to sonnet; lead reviews/commits per phase.
> **Baseline (NOT regressions):** DEBT-006 (10 Blueprints golden), DEBT-008, SpatialHashSystem AV in EditorPreview, ClusterOpE2e DDS crash, flaky sub-80 ns perf (DEBT-014), ~26 pre-existing warnings (DEBT-BCP-004).
> **Global success gate (every task):** `dotnet build IOS-IG-SimHost.sln` 0 errors; touched projects 0 *new* warnings; no regression in the baseline failures above; new/affected tests green.

---

## Phase 1: JSON substrate and emit core
*Keystone; zero behavior change. Design: §3 (D1–D3), §4, §5, §6.1, §6.4. Goal: JSON fully serializable/round-trippable and the emit logic relocated to a `netstandard2.0` core — nothing decommitted, no load-path or generator change yet.*

### PU-101: Emit-core extraction
**Refs:** §6.1, §2.2. **Scope:** Extract the deterministic C# emission logic from `BTreeFluentEmitter`/`HsmFluentEmitter` (and `FluentCSharpEmitterBase`) into a new `netstandard2.0` library (the "emit core") that takes a persisted DTO and returns the C# string for `CreateBuilder()` plus the `[BTreeDefinition]`/`[HsmDefinition]` thunk. No editor/UI/ImGui dependencies. The net8 editor emitters become thin adapters that call the core.
**Success conditions:**
- New `netstandard2.0` project compiles with no reference to any editor (net8) assembly (verify via project refs).
- For every existing editor-owned fixture asset, `emitCore.Emit(dto)` is **byte-identical** to the current `BTreeFluentEmitter.Emit(model)` / `HsmFluentEmitter.Emit(model)` (parametrized test over all `Trees/*.cs` + `Machines/*.cs` fixtures), including the `[*Layout]` method and the `[HsmDefinition(... AssetId = AssetId)]` const form.
- `WriteAtomic` byte-identical no-op behavior preserved (returns `false` when content unchanged).

### PU-102: Persisted DTO and mapping for BTree
**Refs:** §3 (D3), §5.2, §5.3, §5.4. **Scope:** Define the `netstandard2.0` BTree persisted DTO (identity; topology incl. node `kind` polymorphism, pills, child links by `VisualId`; layout as `EditorMetadata`; subtree sync bindings; suppressions; forward-compatible blackboard block with array+default-capable type-ref) and the editor `BehaviorTreeAsset` to/from DTO mapping.
**Success conditions:**
- Mapping round-trip `model -> DTO -> model` preserves every **persisted** field per §5.2 (topology, layout positions/pan/zoom/comment/collapse/color, sync bindings, suppressions, blackboard block). Asserted field-by-field on fixtures.
- **Runtime-only fields are NOT in the DTO** (no `Blob`, `KernelBlobIndex`, derived `*PinId`, `_syncNodeMeta`, `IsDirty`, events) — compile-time plus reflection test over DTO members.
- Blackboard type-ref expresses `TypeId` + `IsArray` + `FixedLength` + `DefaultValueJson` (schema present though unused this thread).

### PU-103: Persisted DTO and mapping for HSM
**Refs:** §3 (D3), §5.2, §5.4. **Scope:** As PU-102 for `HsmAsset` (states/transitions/regions/global-transitions/events; `StableId`/`VisualId` identity; transition waypoints/kind/priority/sync-group; region structure).
**Success conditions:** mapping round-trip preserves all persisted HSM fields incl. regions, global transitions, transition waypoints; runtime-only fields excluded; blackboard block as PU-102.

### PU-104: JSON services and discovery
**Refs:** §5.1, §2.6. **Scope:** `BTreeJsonServices` / `HsmJsonServices` mirroring `BlueprintJsonServices` (System.Text.Json; `JsonStringEnumConverter`; case-insensitive; `WriteIndented=false`; `$meta` envelope `docType` = `Hrot.BTree`/`Hrot.Hsm`, `schemaVersion` 1; `[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]` nodes). Header-lazy contributor discovery for `*.btree.json`/`*.hsm.json` reading only `AssetId`+`Name`.
**Success conditions:**
- `Deserialize(Serialize(dto))` equals `dto` (structural) for all fixtures; tolerates unknown properties and missing `$meta` (legacy).
- `$meta` is the first property; `docType`/`schemaVersion` correct.
- Discovery enumerates `*.btree.json`/`*.hsm.json`, returns `AssetId`+`Name` without full deserialization, and never throws on a malformed file (skips it).

### PU-105: Round-trip and determinism tests
**Refs:** §6.4. **Scope:** Test suite for byte-stability + determinism. Re-base `SaveBTreeEmitTests`/`SaveHsmEmitTests` onto the emit core.
**Success conditions:**
- **RT byte-stability:** `Serialize -> Deserialize -> Serialize` is byte-identical for every fixture.
- `SaveBTreeEmitTests`/`SaveHsmEmitTests` green against the emit core (or reviewed re-baseline documented in the report).
- `Hrot.Editor.AiShared.Tests` green.

---

## Phase 2: Build-time generation
*JSON to C#. Design: §3 (D1,D2,D14), §4, §6.2, §6.3. Goal: MSBuild generates runtime C# from JSON; editor-owned `.cs` becomes a non-committed artifact.*

### PU-201: IncrementalGenerator topology and thunk for BTree
**Refs:** §6.2. **Scope:** New `netstandard2.0` Roslyn `IncrementalGenerator` consuming `*.btree.json` as `AdditionalFiles`, deserializing via the JSON services, emitting `CreateBuilder()` + `[BTreeDefinition]` thunk via the emit core to `obj/GeneratedFiles`. **No `[BTreeLayout]`** (layout is JSON-only). Per-asset deserialize failure becomes a reported diagnostic, never a build crash.
**Success conditions:**
- For a fixture `.btree.json`, the generator emits a `.g.cs` whose `CreateBuilder()`+thunk are byte-identical to the legacy committed `.cs` core (excludes `[BTreeLayout]` and the PU-203 bridge).
- A deliberately malformed `.btree.json` yields a Roslyn diagnostic and does **not** fail sibling assets' generation.

### PU-202: IncrementalGenerator topology and thunk for HSM
**Refs:** §6.2. **Scope:** As PU-201 for `*.hsm.json` -> `CreateBuilder()` + `[HsmDefinition]` thunk.
**Success conditions:** as PU-201 for HSM fixtures.

### PU-203: BlueprintRegistrar self-registration bridge
**Refs:** §3 (D14), §6.3, §14. **Scope:** The generator emits, per editor-owned asset, an isolated class tagged **`[BlueprintRegistrar]`** with a `Register(BehaviorRegistry, BlueprintRegistryStaging)`-compatible signature that (a) compiles+registers the tree/HSM definition (since `FbtTreeCatalog` can't see JSON-owned defs), (b) registers BTree thunks via `BehaviorRegistry.RegisterAction/RegisterCondition(…BlueprintBTree{Action,Condition}Delegate)`, (c) registers HSM thunks via static `HsmActionDispatcher.RegisterAction/RegisterGuard`.
**Success conditions:**
- The generated bridge carries `[BlueprintRegistrar]` only (no `[FbtRegistrar]`/`[HsmActionRegistrar]`) and requests only coordinator-injectable params (`BehaviorRegistry`, `BlueprintRegistryStaging`) — never `BlueprintRegistry`/`HsmActionDispatcher` (coordinator throws; §14 item 4).
- Integration test: a JSON-owned tree is discovered by `AiHotReloadCoordinator.ScanForRegistrars`, registered into the staging `BehaviorRegistry`, and is tickable; a JSON-owned HSM likewise via `HsmActionDispatcher`.
- **(Verify-first, §14 items 1-HSM/2):** before coding, confirm via code that editor-owned trees are otherwise unwired today and the exact `HsmActionDispatcher` static signatures. Record findings in the batch report.

### PU-204: Hrot.AI.Behaviors csproj wiring
**Refs:** §9, §2.7. **Scope (the "good to add"):** Add `<AdditionalFiles Include="Trees/**/*.btree.json" />` and `Machines/**/*.hsm.json`; route generated `.cs` to `obj/GeneratedFiles`; stop compiling the to-be-decommitted editor-owned `Trees/*.cs`/`Machines/*.cs`; keep hand-written `.cs` compiling.
**Success conditions:**
- Solution builds 0 errors with editor-owned trees compiled **only** from JSON via the generator (verify by removing a committed editor-owned `.cs` and confirming the type still exists in the assembly).
- Hand-authored (non-marker) `Trees/`/`Machines/` `.cs` still compile and remain editor-read-only.
- No committed `.btree.json` vs `.cs` base-name collision introduced (ties to PU-502).

### PU-205: Migration-equivalence test harness
**Refs:** §6.4, §11. **Scope:** Test that for each editor-owned asset `json -> generated .cs` **topology core** is byte-identical to the legacy committed `.cs` (excluding `[*Layout]` and the bridge), plus a runtime smoke test that the generated definition registers and ticks.
**Success conditions:**
- Core byte-identity holds for all editor-owned fixtures (§14 item 3 scope: `CreateBuilder`+thunk only).
- Runtime smoke: a migrated tree/HSM produces the same `BehaviorTreeBlob`/`HsmDefinitionBlob` structure hash as the legacy build.

---

## Phase 3: Editor load path and reconciliation stitching
*Design: §3 (D4,D13), §4, §6.6. Goal: editor opens editor-owned assets from JSON (never needs the assembly to compile); hand-authored stays reflection; debug overlay keeps working after reload.*

### PU-301: JSON load path dual-load
**Refs:** §4, §3 (D4). **Scope:** Editor-owned assets (marker present / `.json` exists) load by JSON deserialize -> editor model. Hand-authored (no marker) retain the reflection/blob projector, read-only. `SourceFilePath` set from the `.json` path.
**Success conditions:**
- Opening an editor-owned asset whose generated C# **does not compile** still succeeds (the data-loss fix — core acceptance of the whole thread). Test: corrupt a referenced action FQN so the assembly fails to build; the `.btree.json` still opens with full topology+layout.
- Hand-authored `.cs` (no marker) still opens read-only via reflection (`IsEditorOwned == false`).
- `SourceFilePath` is the absolute `.json` path for editor-owned assets (no more `string.Empty`).

### PU-302: Post-reload stitching
**Refs:** §6.6, §3 (D13). **Scope:** On `OnReloadCompleted`, stitch the JSON-loaded editor model (authoritative topology+layout) to the recompiled runtime blob by matching `VisualId` (BTree) / `StableId` + transition `VisualId` (HSM), assigning `KernelBlobIndex`/`FlatIndex` + runtime hashes; surface a diagnostic for editor nodes with no blob match (kept visible, overlay inert).
**Success conditions:**
- After a reload, the live debug overlay maps runtime state to the correct visual nodes (assert `KernelBlobIndex` assigned for every node with a matching blob `NodeDebugMetadata.VisualId`).
- An editor node with no blob match (e.g. asset not yet built) stays visible and is flagged, not dropped.

### PU-303: Load-path tests
**Refs:** §3 (D4), §6.6. **Scope:** Tests for dual-load routing + stitching + reopen-while-broken.
**Success conditions:** `EditorSubsystemBoot` filter 10/10; new dual-load, reopen-while-broken, and stitching tests green.

---

## Phase 4: Migration of existing assets
*Design: §11, §6.4. Goal: existing editor-owned `.cs` BTree/HSM assets become `.json`; generated C# proven equivalent; old `.cs` decommitted.*

### PU-401: Migration pass cs to json
**Refs:** §11. **Scope:** One-time pass: for each editor-owned `[BTreeDefinition]`/`[HsmDefinition]` asset, load via the current reflection path -> map to DTO -> serialize `.btree.json`/`.hsm.json` at the correct root/subpath/name.
**Success conditions:**
- Every existing editor-owned tree/HSM has a corresponding `.json` at `Trees/`/`Machines/` with `AssetId`/`Name`/topology/layout preserved (re-emit byte-identical core vs the original `.cs`, per PU-205).
- Migrated blackboards recorded as **Category-1** (`Managed: false`, `TypeName` = referenced struct, empty `Variables`).

### PU-402: Decommit generated cs
**Refs:** §11, §14 item 3. **Scope:** Remove the now-generated editor-owned `Trees/*.cs`/`Machines/*.cs` from source control after PU-401/PU-205 prove equivalence; rely on generator output thereafter.
**Success conditions:**
- Build green with editor-owned `.cs` removed; all migrated assets still register and tick (runtime smoke).
- `git` shows the editor-owned `.cs` removed and the `.json` added; hand-authored `.cs` untouched.

---

## Phase 5: Path-at-creation and fixed roots
*Design: §3 (D5,D6), §9. Goal: every asset gets a real path at creation under fixed roots; no `.cs`/`.json` base-name collisions.*

### PU-501: Fixed roots and path-at-creation
**Refs:** §9, §3 (D6). **Scope:** New-asset flow assigns `SourceFilePath = <root>/<user subfolder>/<name>.<ext>` for all three kinds, under fixed roots `Trees/`/`Machines/`/`Blueprints/`; user-driven subfolders+names.
**Success conditions:**
- Creating a new BTree/HSM/Blueprint writes a `.json` at the chosen root+subfolder+name and the asset's `SourceFilePath` is set immediately (no `string.Empty`).
- Roots are fixed (the three above); subfolders/names are free-form and honored by discovery + browser.

### PU-502: Base-name collision guard
**Refs:** §3 (D5). **Scope:** Hard validation forbidding a `.cs` and a `.json` sharing a base name in the same location; clear diagnostic; blocks creation/save into a collision.
**Success conditions:**
- Attempting to create/save an asset whose base name collides with a sibling `.cs` (or vice versa) is refused with an explicit message; unit test covers both directions.

---

## Phase 6: Unified Save and Save-All
*Design: §8, §3 (D1). Goal: every dirty open document flushes to JSON on demand and on close; no debounce data-loss window.*

### PU-601: RegenerationScheduler FlushNow
**Refs:** §8. **Scope:** Synchronous drain of pending debounced work.
**Success conditions:** calling `FlushNow()` writes all pending dirty assets immediately (no 500 ms wait); unit test asserts pending queue empty + files written.

### PU-602: Save-All command
**Refs:** §8. **Scope:** Iterate `AiDocumentManager.OpenDocuments`, filter `IsDirty`, dispatch by `Kind` -> write JSON (projection-only per §5.2), mark clean. Generalize `SaveActiveBlueprintCommand` to any blueprint doc.
Also re-point the **debounced auto-flush**: `RegenerationScheduler.flushAction` for BTree/HSM must change from `emitService.Emit` (writes `.cs`) to a **JSON write** (mark clean), matching the JSON-source-of-truth model (`.cs` is now build-generated, not editor-written). Blueprint routing is unchanged.
**Success conditions:**
- With N dirty docs across all three kinds open, Save-All writes all N to their `.json` and clears dirty; verified by test. Pins/runtime-only fields excluded (projection-only preserved for blueprint).
- A debounced auto-flush of a dirty BTree/HSM produces a `.json` write (not a `.cs` write); verified by test. (Until PU-901 lands, runtime pickup of the change is via the build; latency-neutral with today.)

### PU-603: Save-All wiring and flush-on-close
**Refs:** §8. **Scope:** Toolbar + `Ctrl+Shift+S` (hook `CommandCatalog.SaveAll = "editor.save-all"`); `Ctrl+S` = active-only; flush-on-close.
**Success conditions:** `Ctrl+Shift+S` triggers Save-All; closing a dirty document flushes it first; manual-verify checklist in the report.

---

## Phase 7: Unified tree asset browser
*Design: §10. Goal: one folder-tree browser across all three kinds, replacing the flat blueprint-only one.*

### PU-701: Folder-tree asset browser
**Refs:** §10. **Scope:** Replace flat `AssetBrowserWindow` with a subfolder tree across blueprint/btree/hsm built on NodeEdit tree widgets (`TreeLayout`, `MyBlueprintPanel` sections); double-click opens via JSON load; dirty markers (`*`). Keep `MyBlueprintPanel` for intra-asset outline.
**Success conditions:**
- Browser shows all three kinds under their roots honoring subfolders; double-click opens via the JSON load path; dirty assets show `*`.
- No regression to intra-asset `MyBlueprintPanel`. Manual-verify checklist (screenshots) in the report.

---

## Phase 8: Rename and refactor across json and cs
*Design: §12, §3 (D7). Goal: FQN rename rewrites references across both JSON (editor-owned) and `.cs` (hand-authored).*

### PU-801: JSON-aware refactor writer
**Refs:** §12. **Scope:** Extend `IRefactorService`: editor-owned assets -> JSON-aware mutation of FQN references (point `SourceFilePath` at `.json`); hand-authored `.cs` -> existing string-replace retained.
**Success conditions:**
- Renaming a referenced action/guard FQN updates all editor-owned `.json` references **and** hand-authored `.cs` references atomically; round-trips byte-stable JSON; unit test over a mixed `.json`+`.cs` reference set.

---

## Phase 9: In-process quick reload
*Meets the ≤100 ms target. Design: §3 (D12,D14), §2.5, §6.5. Goal: edit-to-live latency ≤100 ms for BTree/HSM, via the shared emit core + masquerade registration. Not required to avoid regression; required to meet the documented target.*

### PU-901: In-process Roslyn quick reload
**Refs:** §6.5, §3 (D12). **Scope:** Add an in-process quick-reload path mirroring `QuickReloadService`: editor model -> emit core C# -> in-memory Roslyn -> collectible ALC -> register via the PU-203 `[BlueprintRegistrar]` bridge -> commit (staging). Route BTree/HSM dirty flush to this path for the edit loop; MSBuild generator remains for full rebuild.
**Success conditions:**
- A topology/blackboard edit goes live without an MSBuild subprocess; measured author-perceived turnaround ≤100 ms on the dev machine (report the measurement).
- Registration uses the masquerade (no HR-001 change); staging isolation preserved (a throwing reload doesn't corrupt the live registry).
- Compile failure falls back cleanly (diagnostic, last-good retained); asset still reopenable from JSON.

---

## Phase 10: Blackboard DD revision handoff
*Pre-Slice-1.5. Design: §7, §13. Goal: the Blackboard Authoring DD is aligned to the JSON substrate before Slice 1.5 begins.*

### PU-1001: Verify revised Blackboard DD consistency
**Refs:** §7, §13. **Scope:** **The Blackboard DD revision itself is performed by the lead in the design session** (full context), at [`../../docs/blueprints/Blackboard_Authoring_Detailed_Design.md`](../../docs/blueprints/Blackboard_Authoring_Detailed_Design.md). This task is the downstream **check** that the revised DD is consistent with what Phases 1-9 actually shipped.
**Success conditions:**
- Revised DD: §2/§3/§13/§14.6 describe **JSON-backed Category-2** (vars in `.btree.json`/`.hsm.json`; struct + offset thunks emitted by the generator via the `[BlueprintRegistrar]` bridge); the source-text parser / verbatim-span / State-B/C / RT-over-C# machinery removed.
- Category-1 (hand-written, reflected, read-only) and all editor-facing logic (panel, aggregation, aliasing, sync, bin-pack, validation) explicitly **preserved**.
- Verified specifics present: `FixedString32/64` for strings; defaults in generated `ParseParams` (+ heavy-tier `StructureHash` init); `fixed[N]`/`[InlineArray(N)]` with Sequential alignment + the `[InlineArray]` mutation trap; 100-byte `MaxBehaviorParamByteSize` ceiling.
- Architect re-review recorded. No contradiction between the revised DD and the implemented substrate (Phases 1-6).
