# Onboarding — Thread 1: Unify persistence on JSON (BTree/HSM → JSON) + unified Save + unified tree asset browser

> **New-chat brief.** Self-contained: assumes no prior conversation. Read top to bottom, then read the files it points at before changing anything.

## Mission

Today the three visual-editor asset kinds persist **inconsistently**, and two of them in a **fragile, work-losing** way:

- **Blueprint** → `.bp.json` is the source of truth; the editor loads it by JSON deserialization; C# is a *generated* artifact. ✅ safe.
- **BTree** and **HSM** → the **generated `.cs` file IS the source of truth**; the editor loads them by **reflecting over the compiled assembly** (invoke `[BTreeDefinition]`/`[HsmDefinition]` → blob → project to asset). The graph **and the canvas layout** (`[BTreeLayout]`/`[HsmLayout]`) live only in that `.cs`. ❌ fragile.

**The problem (the reason for this thread):** if the emitted C# doesn't compile — which happens routinely when you save **incomplete/in-progress work**, or when a referenced hand-written action/guard FQN was renamed/deleted — the assembly won't build, the reflection load can't run, and **the editor can't read the asset back. The saved work is lost.** The user named this the biggest issue.

**The fix:** make **JSON the universal source of truth and the editor's load path** for all three kinds; demote C# to a **regenerated build artifact**. Then *save always works and is always reopenable*, even for broken/incomplete graphs; compile failures become diagnostics, not lost work. Plus the cross-cutting asset-management work this enables:

1. **`.btree.json` / `.hsm.json`** round-trippable persistence (graph + layout), mirroring `.bp.json`.
2. C# becomes generated (build-time generator and/or on-demand), not hand-managed; **no colocation of `.json`↔`.cs` required** (user-confirmed).
3. **Path-at-creation** — every asset gets a path at creation: `<AI-source root>/<user subfolder>/<name>`, uniform across all three kinds (fixes the `SourceFilePath = ""` gap for assembly-loaded assets).
4. **Unified Save / Save-All** — flush every dirty open document to disk (JSON) on demand + on close; no debounce-window data loss.
5. **Unified tree asset browser** — folder tree across blueprint/btree/hsm, honoring subfolders (replaces the flat, blueprint-only browser).
6. **Migration** — one-time conversion of existing `.cs` BTree/HSM assets → `.json`.

**Ordering:** this thread runs **AFTER** Thread 2 (`.dev/blueprint-finalize/ONBOARDING.md` — blueprint finalization + DEBT-MVE-003). Rebase on the latest `blueprint-integ-1` before starting; Thread 2's blueprint-runtime/registry changes land first.

**Scope guardrails:** branch `blueprint-integ-1`. **GizmoMap.Contracts stays 0.2.2.** No `Hrot.IG` / DDS / `Stride/`. No `editor_stride`.

## Design & task documents (read in this order)
- **Detailed design (spec):** [`BTree_HSM_JSON_Persistence_Detailed_Design.md`](./BTree_HSM_JSON_Persistence_Detailed_Design.md) — verified current state, decisions D1–D14, JSON schema, generator/build wiring, migration. Cite its `§` chapters.
- **Task detail (per-task success conditions):** [`TASK-DETAIL.md`](./TASK-DETAIL.md) — 24 tasks across 10 phases, each with concrete success conditions; references the design `§`.
- **Task tracker (status):** [`TASK-TRACKER.md`](./TASK-TRACKER.md) — phase/task checklist with deep-links into TASK-DETAIL.
- **Debt:** [`DEBT-TRACKER.md`](./DEBT-TRACKER.md) — starts empty; record P2/P3 debt per batch.
- **Critical path:** Phase 1 (keystone) → 2 → 3 → 4. The thread's core promise — *save always works; assets reopen even when C# won't compile* — is acceptance-tested in **PU-301**.
- **Related (post-this-thread):** the Blackboard Authoring DD now lives at [`../../docs/blueprints/Blackboard_Authoring_Detailed_Design.md`](../../docs/blueprints/Blackboard_Authoring_Detailed_Design.md); it is revised to the JSON substrate before Slice 1.5 (see design §13, task PU-1001).

## How we work (non-negotiable conventions)

- **Read `.dev/.guides/DEV-GUIDE_claude.md` first** (coder contract: verify-first, cite file:line, never fake a pass, run implement→build→test→fix to green).
- **Codebase Memory MCP FIRST** (`.claude/CLAUDE.md`): `list_projects` → `get_architecture` → `search_graph`/`get_code_snippet`. Project `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`.
- **Delegate implementation + test-fix loops to `sonnet` agents** (user cost directive). Lead plans/reviews/verifies/commits.
- **Batch workflow:** `.dev/persistence-unification/batches/BATCH-XX-INSTRUCTIONS.md` → sonnet coder → review → `.dev/persistence-unification/reports/` → commit per batch (write `.git/PUxx_MSG.txt`, `git commit -F`). End messages with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Exclude `Stride/` + `no-tests-HROT.Engine.dumpfilter`.

## Current state (what exists — read before designing)

### Source-of-truth + load + emit, per kind
- **Blueprint (the model to copy):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintJsonServices.cs` (`Serialize`/`Deserialize`); editor loads via `BlueprintJsonServices.Deserialize` (e.g. `Hrot.Blueprints.Editor/AssetBrowserWindow.cs:79`); `.bp.json` discovered by `BlueprintAssetContributor` (`*.bp.json`, header-lazy). C# generated separately (in-memory by `QuickReloadService`; build-time per design).
- **BTree:** model `Hrot/Subsystems/Blueprints/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`; emitter `Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` (emits `CreateBuilder()` + `[BTreeDefinition] Build()` + `[BTreeLayout] Layout()`); load `Hrot.BTree.Editor/.../BTreeAssetContributor.cs` (reflection; `SourceFilePath = string.Empty` for assembly-loaded). Layout lives in the `[BTreeLayout]` method.
- **HSM:** model `Hrot.Hsm.Editor/Model/HsmAsset.cs`; emitter `Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs` (`CreateBuilder()` + `[HsmDefinition] Compile()` + `[HsmLayout] Layout()`); load `HsmAssetContributor.cs` (reflection; `SourceFilePath = string.Empty`). Layout in `[HsmLayout]`.

### Auto-emit (already wired)
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs` — debounced (500ms) flush; `Tick()` called per-frame from `EditorSubsystem.DrawUI` (`EditorSubsystem.cs:1412`).
- `Hrot.Editor.AiShared/Emit/AiAssetEmitService.cs` — `Emit(asset)` → kind-specific emitter → `FluentCSharpEmitterBase.WriteAtomic` (skips write if byte-identical). Deterministic emit is covered by `SaveBTreeEmitTests` / `SaveHsmEmitTests`.
- Wiring in `EditorSubsystem.cs:2048-2130`: `flushAction` routes **BTree/HSM → `emitService.Emit` (writes `.cs`)** and **Blueprint → `_blueprintQuickReloadTrigger` (compiles in-memory only — does NOT write `.bp.json`)**. So today blueprint source is **never auto-persisted** (only explicit active Save); btree/hsm `.cs` is auto-written debounced (data-loss window if the app closes inside the debounce or before a Tick).

### Document / dirty layer (the spine for unified Save)
- `Hrot.Editor.AiShared/Identity/IEditableAsset.cs` — `AssetId`, `Name`, `Kind` (`AssetKind`), `SourceFilePath`, `IsDirty`, `IsEditorOwned`, `event Changed`.
- `Hrot.Editor.AiShared/Documents/AiDocument.cs` — wraps an `IEditableAsset`; `IsDirty`, `MarkDirty`/`MarkClean`, `ViewState`, `ReconcileAsset`.
- `Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` — `OpenDocuments`, `Active`, `Open`/`Activate`/`Close`, events.
- `Hrot.Blueprints.Editor/DirtyTracker.cs` — blueprint-only per-asset id dirty set.
- `Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs` — `Save(asset, path)` (projection-only: clears pins on a temp swap restored in `finally`, then `BlueprintJsonServices.Serialize` → `File.WriteAllText`) and `SaveFromActiveDocument(...)` (resolves the **active** doc only).

### Asset browser (what to replace)
- `Hrot.Blueprints.Editor/AssetBrowserWindow.cs` — a **flat** ImGui table, filtered by path substring, **blueprint-only**, loads via `BlueprintJsonServices.Deserialize`. No subfolder tree, no btree/hsm.

### NodeEdit reuse (important — don't reinvent)
- `FDP/ExtDeps/NodeEdit/` is a **single-asset graph-editor toolkit**. It provides the **intra-asset "My Blueprint" outline** (`NodeEditor.UI/Panels/MyBlueprintPanel.cs`, model `IMyBlueprintModel`) — **already reused** by us via `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintWindow.cs` + `BlueprintMyBlueprintModel.cs`. Keep using it for intra-asset outline.
- NodeEdit has **no project/file asset browser** (the project tree of asset *files* is a host concern). But it has reusable **tree-rendering** building blocks to build ours on: the picker `NodeEditor.UI/Picker/Layouts/TreeLayout.cs` and `MyBlueprintPanel`'s collapsible-section rendering. Demo `S12_AssetGridPicker` is a value-picker popup (not a browser); `S25_MultiTab` shows multi-document.

## Tasks (suggested sequence — #1 is the keystone)

### 1. Round-trippable JSON for BTree + HSM (source of truth)
Add `.btree.json` / `.hsm.json` serializers mirroring `BlueprintJsonServices` — full round-trip of `BehaviorTreeAsset` / `HsmAsset` **including the canvas layout** that today lives in `[BTreeLayout]`/`[HsmLayout]`. Add a byte-stability round-trip test (serialize→deserialize→serialize identical) like the blueprint one. Decide the projection-only stance (do graphs hydrate any runtime-only data, à la blueprint pins?).

### 2. Editor load path: JSON, not reflection
Switch `BTreeAssetContributor`/`HsmAssetContributor` (and the editor open flow) to **load assets from `.btree.json`/`.hsm.json`** so the editor never needs the assembly to compile to reopen an asset. C# generation becomes a *separate* step (build-time generator and/or the existing on-demand emitter), emitting to an intermediate/`Generated` location — decide **build-time source generator vs runtime loader** (blueprint's build-time generator is the reference pattern). The runtime still needs the `[BTreeDefinition]`/`[HsmDefinition]` registration to exist, so the generator must produce it from the JSON.

### 3. Path-at-creation under a defined AI-source root
A "new asset" flow that requires **name + optional subfolder** under a single strict root in the Behavior-AI source tree (decide: one root vs per-kind roots — open question from the design chat; user leaned to a single strict root with user subfolders). Assign `IEditableAsset.SourceFilePath` at creation for **all three kinds**. No more `SourceFilePath = ""`.

### 4. Unified Save / Save-All
A command that **synchronously flushes every dirty open document to its JSON** (not just the active one, not just blueprints): iterate `AiDocumentManager.OpenDocuments`, filter `IsDirty`, dispatch by `Kind` → write JSON, mark clean. Add `RegenerationScheduler.FlushNow()` so Save drains pending work without the debounce. Wire **Save All** (toolbar + `Ctrl+Shift+S`; hook the existing `NodeEditor.Core/CommandCatalog.cs SaveAll = "editor.save-all"` constant) and **flush-on-close**. Generalize `SaveActiveBlueprintCommand` to handle any blueprint doc. Decide `Ctrl+S` = active-only vs all (user open question; recommend active-only + `Ctrl+Shift+S` = all). C# regeneration stays a separate (build/on-demand) step.

### 5. Unified tree asset browser
Replace the flat `AssetBrowserWindow` with a **folder tree** across blueprint/btree/hsm, honoring subfolder paths, built on NodeEdit's tree widgets; double-click opens the asset (JSON load). Keep `MyBlueprintPanel` for the intra-asset outline. Show dirty markers (`*`).

### 6. Migration of existing `.cs` BTree/HSM assets → JSON
One-time tool/pass: load each existing `[BTreeDefinition]`/`[HsmDefinition]` asset (via the current reflection path) → project to the asset model → serialize to `.btree.json`/`.hsm.json` at the appropriate path. Verify the migrated JSON re-generates byte-identical C# (so behavior is unchanged).

## Risks / gotchas
- **Generated-C# byte-stability:** `SaveBTreeEmitTests` / `SaveHsmEmitTests` assert deterministic emit. After moving the source to JSON, the C# must still emit identically (or those tests get a deliberate, reviewed re-baseline). The runtime `[BTreeDefinition]`/`[HsmDefinition]` thunks must still be produced.
- **Layout fidelity:** node positions currently in `[BTreeLayout]`/`[HsmLayout]` must round-trip through JSON without loss (and reconcile by `VisualId`/`StableId` as the existing `OnReloadCompleted` reconciliation does — see `EditorSubsystem.cs:2132+`).
- **Don't break the blueprint path:** blueprint already works the right way; reuse its patterns, don't regress it.

## Verification (reach green before reporting)
- `dotnet build IOS-IG-SimHost.sln` 0 errors; touched projects 0 new warnings (~26 pre-existing unrelated warnings on full rebuild — leave them, DEBT-BCP-004).
- New JSON round-trip + migration tests; `SaveBTreeEmitTests`/`SaveHsmEmitTests` (green or reviewed re-baseline); `EditorSubsystemBoot` filter 10/10; `Hrot.Editor.AiShared.Tests`; `Hrot.Blueprints.Tests` (only the 10 pre-existing DEBT-006).

## Pre-existing failures (NOT regressions)
DEBT-006 (10 Blueprints golden/snapshot), DEBT-008, SpatialHashSystem AV in EditorPreview, ClusterOpE2e DDS crash, flaky sub-80ns perf (DEBT-014), ~26 warnings (DEBT-BCP-004). Baseline against `git stash` if unsure.

## Done-definition for this thread
All three asset kinds persist as JSON (source of truth) and are reopenable regardless of compile state; C# is a regenerated artifact; every asset has a real path assigned at creation under the AI-source root; Save-All flushes all dirty docs (and on close); the asset browser is a unified subfolder tree; existing `.cs` assets are migrated to JSON with byte-identical regenerated C#.
