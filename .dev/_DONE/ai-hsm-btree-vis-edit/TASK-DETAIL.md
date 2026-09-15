# Blackboard Authoring — Task Detail

> **Purpose:** Per-task descriptions for implementing the visual blackboard authoring feature across the BTree and HSM editors. Each task references the relevant chapter of the design rather than duplicating it. Hand-off-ready: an implementer opens the referenced design section plus this entry and can start working.
> **Design:** [`Blackboard_Authoring_Detailed_Design.md`](./Blackboard_Authoring_Detailed_Design.md) — referenced below as **BB §x**.
> **Companion:** [`TASK-TRACKER.md`](./TASK-TRACKER.md) — the one-line-per-task tracker with status checkboxes.
> **Debt:** [`DEBT-TRACKER.md`](./DEBT-TRACKER.md) — deferred P2/P3 issues discovered during implementation.
> **Other specs referenced:**
> - `docs/blueprints/AI_Editor_Shared_Infrastructure.md` (shared infra)
> - `docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` (BTH)
> - `docs/blueprints/HSM_Editor_NodeEditor_Host_Design.md` (HSH)
> - `docs/blueprints/Blueprint_Subsystem_Editor_Detailed_Design.md` (BPE)

---

## §0 Reconciliation with the existing codebase

The design was written without direct access to the source tree, so a few names and paths in BB differ from what is actually committed and tested. **The committed code is correct and stays as-is** — use the real names/paths below. These are not bugs and do not require corrective code changes.

| BB says | Actual in codebase | Use |
|---------|--------------------|-----|
| Shared code lives in `Hrot/Subsystems/AI/Hrot.Editor.AiShared/Blackboard/` (BB header) | Shared infra project is `Hrot/Editor/Hrot.Editor.AiShared/` | New shared blackboard code goes in **`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/`** |
| `Hrot.BTree.Editor/Blackboard/`, `Hrot.Hsm.Editor/Blackboard/` | Editors are under `Hrot/Subsystems/AI/Hrot.BTree.Editor/` and `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` | New subsystem blackboard code goes in `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/` and `…/Hrot.Hsm.Editor/Blackboard/` |
| `FluentCSharpEmitter` (BB §6, §14.1) | `FluentCSharpEmitterBase` + `IFluentCSharpEmitter` in `Hrot/Editor/Hrot.Editor.AiShared/Emit/` | Extend **`FluentCSharpEmitterBase`** / implement `IFluentCSharpEmitter` |
| Add **two** `SubElementKind` values: `BlackboardVariable` *and* `BlackboardField` (BB §11.4, §14.1) | `SubElementKind` already contains `BlackboardField` (`Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs`) | Add **only `BlackboardVariable`**; reuse the existing `BlackboardField` |

### Project dependency constraints (avoid circular references)

The reference direction is **subsystem editors → shared infra**, never the reverse (verified: `Hrot.BTree.Editor.csproj` references `..\..\..\Editor\Hrot.Editor.AiShared\Hrot.Editor.AiShared.csproj`; the shared project has no reference back). Consequences for where code lands:

- **Goes in shared** (`Hrot.Editor.AiShared`, may depend downward on `Fdp.Toolkits` and the FastBTree/FastHSM kernels): `IActionSchemaExporter`, the bin-packer, the source-text parser, the DTO emitter, the panel/`VariablesPanelControl`, and the **abstractions** for aggregation (`IBlackboardAggregator`, `AggregationResult`, `DtoRequirement`).
- **Must go in the subsystem editors** (they know the concrete asset types `BehaviorTreeAsset` / `HsmAsset` and the kernel-specific orchestrator APIs): the concrete BTree/HSM aggregation walkers (registered with the shared aggregator via the existing contributor/strategy pattern, dispatched over `IEditableAsset`) and the BTree-vs-HSM orchestrator emit templates (BB §14.4 calls these out as subsystem-specific). Putting a concrete walker or emitter that references `BehaviorTreeAsset`/`HsmAsset` into the shared project would create a circular reference and will not compile.

Verified-present anchors the tasks build on:
- `BrainBlackboard` (`FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`) — `[StructLayout(Explicit, Size=128)]`, `BehaviorParameters[100]` at offset 0, tail registers `ExpectedThreatLevel`, `Interrupt_MobilityLost`, `Interrupt_Reserved`. Confirms BB §6.1 / §6.6.
- `BehaviorConstants` (`Fdp.Toolkit.Behavior`) — `MaxBehaviorParamByteSize = 100`, `BrainBlackboardByteSize = 128`. Confirms BB §4.7 / §6.
- `Blackboard1024` (`BehaviorComponents.cs`) and `BehaviorIngressSystem` (`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/`). Confirms BB §6.3 / §6.6.
- `BlackboardSchemaBuilder` (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/`) — the existing read-only reflection path BB §1.1 extends.
- `BlackboardFieldPickerAttribute` (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/`) — the picker BB §11.2 type-filters.
- `BehaviorTreeAsset` / `BehaviorTreeAssetProjector`, `HsmAsset` / `HsmAssetProjector` / `HsmAssetValidator` (under `Hrot/Subsystems/AI/`).
- `IAssetCatalog` (+ `Changed` event), `IRefactorService` (Preview/Apply), `EditorSelectionStore` (`IAssetSubSelection`), `FluentCSharpEmitterBase`, `UsingDirectiveSet` — all in `Hrot/Editor/Hrot.Editor.AiShared/`.
- Kernel attributes `[BTreeDefinition]` (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/`), `[SharedAiHeavyAction]` (`Fbt.Kernel/SharedAiAttributes.cs`), `HsmActionDispatcher` (static class, `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/`), `BehaviorRegistry` (`Fdp.Toolkits/Behavior/`).

---

## Table of Contents

- Phase 0 — Kernel / attribute prerequisites (BB §14.5, §3.1)
- Phase 1.5a — Action schema and read-only Variables panel
- Phase 1.5b — Editor-managed DTO emit + add/remove
- Phase 1.5c — Recursive aggregation + heavy tier
- Phase 1.5d — Approach A whole-DTO aliasing
- Phase 1.5e — Approach B field-level synchronization
- Phase 1.5f — Validation, diagnostics, recovery
- Phase 1.5g — Blueprint UX parity (Tier A)
- §99 Design-coverage matrix (final check)

**Ordering rationale:** Phase 0 additive kernel/attribute work must land before the editor can declare `BlackboardManaged`, register heavy DTOs, or read access annotations. 1.5a builds the schema + read-only panel reusing today's reflection path (no writing). 1.5b adds the write path (emit + add/remove/rename + inline bin-packer + round-trip CI). 1.5c adds aggregation and the heavy tier. 1.5d/1.5e add the two sharing models (A then B; B's allocation reuses A's suppression logic). 1.5f layers validation/diagnostics/recovery over everything. 1.5g extracts the shared panel control and brings Blueprint to UX parity. Slices are independently shippable in order; each acceptance line in BB §15 is the slice's exit gate.

---

## Phase 0 — Kernel / attribute prerequisites

All additive; default values preserve current behavior. These live in the kernels / shared attribute assemblies, not the editor. See BB §14.5 and BB §3.1.

### TASK-BB-K-01 — `BlackboardManaged` flag on `[BTreeDefinition]` / `[HsmDefinition]`

Add an optional `bool BlackboardManaged` property (default `false`) signalling "this asset uses the `{AssetName}.Blackboard.cs` companion file" (BB §3.1).

- **Spec:** BB §3.1, §14.2 (BTH §3), §14.3.
- **Where:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs` and the FastHSM equivalent; source generators that read these attributes.
- **Default:** `false` → existing assets behave exactly as today.
- **Verifies:** existing FastBTree/FastHSM attribute tests still pass; new test confirms the property defaults to `false` and round-trips an explicit `true` through the generator metadata.

### TASK-BB-K-02 — `HeavyDtoType` argument on `[BTreeDefinition]` / `[HsmDefinition]`

Add optional `Type HeavyDtoType` (default `null`). When set, the source generator wires the runtime so `BehaviorIngressSystem` provisions a `Blackboard1024` component for the behavior (BB §6.3, §14.5).

- **Spec:** BB §6.3, §14.5.
- **Where:** same attributes as TASK-BB-K-01; generator + `BehaviorIngressSystem` provisioning path.
- **Default:** `null` → no heavy component, current behavior.
- **Verifies:** unit test that a definition with `HeavyDtoType` set causes a `Blackboard1024` to be attached on behavior assignment; definition without it attaches none (regression).

### TASK-BB-K-03 — `[BlackboardDtoStruct]` marker attribute

A new optional marker attribute placed on user-defined DTO structs that should appear in the Add-Variable type picker even before any action references them (BB §4a.4, §10, §14.5).

- **Spec:** BB §4a.4, §10.2, §14.5; BB §17 open-question #2.
- **Where:** shared annotations assembly reachable by both kernels and the editor (alongside `SharedAiAttributes`).
- **Verifies:** reflection test that a `[BlackboardDtoStruct]`-decorated struct is discoverable; absence falls back to the action-signature heuristic (covered in TASK-BB-1a-01).

### TASK-BB-K-04 — `[BlackboardReadOnly]` / `[BlackboardReadWrite]` parameter attributes

Optional annotations on an action method's first `ref` parameter declaring its access pattern. Both kernels ignore them at runtime; only the editor's schema exporter reads them (BB §9.6, §14.5).

- **Spec:** BB §9.6, §10.2, §14.5.
- **Where:** `Fbt.Annotations` / `Fhsm.Annotations` (shared attribute assembly). Apply-to: parameters.
- **Default:** unannotated → editor treats the parameter as **ReadWrite** (conservative, BB §9.6).
- **Verifies:** kernel build unaffected; editor schema test (TASK-BB-1a-01) reads the annotation into `ParamAccess`.

---

## Phase 1.5a — Action schema and read-only Variables panel

Goal (BB §15 acceptance): opening an asset shows its reflected blackboard fields; BTree/HSM action pickers filter to compatible variables; no editor-side authoring yet.

New shared code: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/`. Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/`.

### TASK-BB-1a-01 — `IActionSchemaExporter` with reflection-based population

Build the action schema service that records, per registered action FQN, its parameter DTO type, hosting flags, access pattern, and optional heavy DTO type.

- **Spec:** BB §10.1–§10.4.
- **Where:** `Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` (+ production impl).
- **Public surface:** `IActionSchemaExporter` (`Lookup(fqn)`, `All`, `Rebuild()`, `Changed`); `ActionSchemaEntry` record; `ActionHosting` `[Flags]`; `BlackboardAccess` enum (per BB §10.2).
- **Populated by:** reflecting `[BTreeAction]`, `[BTreeCondition]`, `[BTreeObserver]`, `[HsmAction]`, `[HsmGuard]`, `[SharedAiAction]`, `[SharedAiCondition]`, `[SharedAiHeavyAction]`, `[SharedAiHeavyCondition]`; first `ref` param → `DtoType`; `[BlackboardReadOnly/ReadWrite]` → `ParamAccess` (default `Unknown`/conservative); `[SharedAiHeavyAction]` heavy param → `HeavyDtoType`.
- **Dependencies:** TASK-BB-K-04 (annotations), reads `BehaviorRegistry` / `HsmActionDispatcher` registrations.
- **Verifies (`ActionSchemaExporterTests`, BB §16.1):** fixture assembly with each attribute kind → correct FQN keys (`{DeclaringType.FullName}.{Method}`) and short-name index; correct `DtoType` extraction; hosting flags composed across multiple attributes; `[BlackboardReadOnly]` read into `ParamAccess`; heavy DTO extracted for `[SharedAiHeavyAction]`; unannotated parameter resolves to ReadWrite-conservative.

### TASK-BB-1a-02 — Schema rebuild on `IAssetCatalog.Changed`

Wire `IActionSchemaExporter.Rebuild()` to fire on catalog change (hot reload) and re-raise `Changed` so pickers/aggregation refresh together.

- **Spec:** BB §10.7.
- **Where:** DI wiring in `Hrot.Editor.AiShared` + subscription to `IAssetCatalog.Changed`.
- **Dependencies:** TASK-BB-1a-01; existing `IAssetCatalog`.
- **Verifies:** unit test that a catalog `Changed` event triggers exactly one `Rebuild()` + one `Changed`; new action FQNs appear in `All` after rebuild; types removed from the assembly drop out (feeds the `VariableTypeNotFound` diagnostic in TASK-BB-1f-03).

### TASK-BB-1a-03 — `BlackboardAuthoringWindow` shell (read-only mode)

Register the docked window `ai_blackboard_variables` in both BTree and HSM perspectives; render the Defined Variables list from reflected `FieldInfo[]` (read-only — no add/remove yet).

- **Spec:** BB §4.1, §4.2 (glyphs), §4.6 (layout-kind indicator), §4.7 (memory budget — static read).
- **Where:** `Hrot.Editor.AiShared/Blackboard/BlackboardAuthoringWindow.cs`; window registration via the shared registrar.
- **Dependencies:** TASK-BB-1a-01 (types), `EditorSelectionStore` (active asset).
- **Verifies:** manual/visual test — opening an asset lists its reflected fields with size annotations, header shows `LayoutKind` and `Memory: X / 100 B`; window dockable in both editors. Unit test that the window's view-model projects `FieldInfo[]` in declaration order.

### TASK-BB-1a-04 — `BlackboardSourceTextParser` (verbatim span capture)

Parse a `.cs` companion file to (a) locate the struct, (b) extract `///` doc comments above each field, (c) capture the verbatim source span (leading comment/attributes → trailing `;`) of each field for read-only-passthrough emit.

- **Spec:** BB §3.2 (load steps 3,6), §3.5.
- **Where:** `Hrot.Editor.AiShared/Blackboard/BlackboardSourceTextParser.cs`.
- **Public surface:** parse result with per-field `{ name, leadingComment, verbatimSpan, singleLineDeclaration }` and struct-level locate result.
- **Verifies (`BlackboardSourceTextParserTests`, BB §16.1):** fixture `.cs` files — clean field, field with `///` block, field with attributes, multi-line field, field with initializer; correct span boundaries (byte-exact); struct-not-found case surfaces a locate failure (feeds State C, TASK-BB-1f-07).

### TASK-BB-1a-05 — Per-field classification (editor-managed vs read-only-passthrough)

Implement the six-condition "plain field" rule classifying each reflected field as editor-managed or read-only-passthrough.

- **Spec:** BB §3.4 (the six conditions), §3.5.
- **Where:** `Hrot.Editor.AiShared/Blackboard/` classifier consuming the parser (TASK-BB-1a-04) + the known-type set (primitives, vectors, project enums, `[BlackboardDtoStruct]` / action-DTO structs from the schema).
- **Dependencies:** TASK-BB-1a-01 (known DTO set), TASK-BB-1a-04, TASK-BB-K-03.
- **Verifies (`BlackboardSourceTextParserTests` / classifier tests, BB §16.1):** each of the six rules independently forces read-only when violated (visibility, unknown type, attributes, initializer, multi-line, malformed shape); plain known-typed field classifies editor-managed; read-only field exposes name+reflected type+comment but no edit affordance.

### TASK-BB-1a-06 — Picker filtering by action `DtoType`

Type-filter `BlackboardFieldPickerAttribute`'s dropdown to variables whose type matches the action's schema `DtoType`; show `(no compatible variables)` + promote affordance when none. Repoint BTree/HSM pickers at `IActionSchemaExporter.All` (BB §10.5).

Also render the per-node binding badge on the canvas (BB §11.5): a `NodeAttachment` (`Custom` category) showing the bound variable name under each action/condition/guard node; `(unbound)` in red when `ExpressionTargetField` is null; clicking it selects the variable in the panel. Subtree nodes show no single-field badge — their binding is the Approach B sync table, not one field (BB §11.6).

- **Spec:** BB §11.2, §11.5 (canvas badge), §11.6 (subtree vs action binding); BB §10.5 (picker source consolidation); BTH §10.3.
- **Where:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/` (picker) + HSM picker; canvas badge via the NodeAttachments extension; consume the schema exporter.
- **Dependencies:** TASK-BB-1a-01.
- **Verifies:** unit test that for an action with `DtoType = MoveToLocationParams` the dropdown lists only `MoveToLocationParams` variables; incompatible variables hidden; empty-set shows the promote affordance (full promote flow lands in TASK-BB-1b-02/§11.3). Visual test: bound nodes show the variable badge, unbound show red `(unbound)`, clicking selects in the panel; subtree nodes show no field badge. Existing picker tests updated, still green.

---

## Phase 1.5b — Editor-managed DTO emit + add/remove

Goal (BB §15 acceptance): designer creates a `BlackboardManaged = true` asset, visually adds variables of all primitive/known DTO types, renames, removes, saves, and reloads with byte-identical no-op round-trip.

### TASK-BB-1b-01 — `BlackboardDtoEmitter` (HROT_EDITOR_GENERATED file)

Emit the `{AssetName}.Blackboard.cs` partial struct: four-line marker block, deterministic usings, `[StructLayout(Sequential)]`, fields in canonical order — editor-managed regenerated (with `///` comments), read-only-passthrough emitted verbatim from captured spans. Atomic write.

- **Spec:** BB §2.2 (marker), §3.3 (save pipeline), §3.4/§3.5 (per-field emit modes), §4.6 (always Sequential), §4a.2 (naming).
- **Where:** `Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs`, extending `FluentCSharpEmitterBase` and reusing `UsingDirectiveSet`.
- **Dependencies:** TASK-BB-1a-04/05 (classification + spans); `FluentCSharpEmitterBase`.
- **Verifies (`BlackboardDtoEmitterTests`, BB §16.1):** marker block correct (token + AssetId + AssetName lines); usings deterministic across runs; editor-managed fields regenerate with comments as `<summary>`; read-only fields emitted byte-identically from spans; output stable for identical model (determinism).

### TASK-BB-1b-02 — Add Variable + Remove Variable workflows

The `[+] Add variable…` popup (name validation, type dropdown, optional comment) appends to canonical order; Remove via the `⋮` menu with a dangling-reference report. Includes drag-reorder within Defined Variables (silent for editor-managed). Wires the picker's inline "Promote to new variable" (BB §11.3).

- **Spec:** BB §4.4, §4.3 (comments), §4.5 (row interactions, reorder), §11.3 (promote).
- **Where:** `BlackboardAuthoringWindow` + command records on the editor model.
- **Dependencies:** TASK-BB-1a-03, TASK-BB-1b-01, TASK-BB-1b-04 (offset display).
- **Verifies:** unit tests — add appends at end (existing offsets stable); name validated as C# identifier; remove emits dangling-reference list; reorder reindexes canonical order; promote-from-picker creates a variable of the action's known type and binds it. Integration (BB §16.2): "add a variable via panel, save, reload" → persists, type-correct.

### TASK-BB-1b-03 — Variable rename via the refactor service

Inline rename routes through `IRefactorService`: all nodes referencing the old `ExpressionTargetField` retarget to the new name across assets.

- **Spec:** BB §11.4, §4a.5; BB §17 open-question #4 (silent apply, no preview pane), #6 (rename = Soft reload).
- **Where:** `Hrot.BTree.Editor` / `Hrot.Hsm.Editor` rename command → existing `IRefactorService` keyed on `SubElementKind.BlackboardVariable` (added in TASK-BB-1b-05's catalog wiring).
- **Dependencies:** TASK-BB-1b-05 (the `BlackboardVariable` reference kind), existing `IRefactorService`.
- **Verifies:** integration test that renaming a variable referenced by N nodes rewrites all N `ExpressionTargetField`s and re-emits affected assets; reload classifier reports Soft (names change, offsets stable).

### TASK-BB-1b-04 — `BlackboardBinPacker` (inline-only)

Compute byte offsets with correct C# alignment for the inline tier only (no heavy promotion yet); enforce master-vars ≤ 100 B; surface `InlineMemoryExceeded` as a warning.

- **Spec:** BB §6.1, §6.2 (algorithm, alignment), §6.6 (tail registers off-limits).
- **Where:** `Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs`.
- **Public surface:** `BlackboardBinPacker.Pack(masterVars, aggregatedVars)` → `PackResult`/`PackedVariable` (per BB §6.2), `RequiresHeavyComponent = false` in this slice.
- **Dependencies:** none beyond the variable model.
- **Verifies (`BlackboardBinPackerTests`, BB §16.1):** offsets match `Marshal.SizeOf` alignment (8-byte aligned to 8, etc.); master vars always inline; >100 B masters → `PackWarning.Error` (`InlineMemoryExceeded`); the `BrainBlackboard` tail bytes (`ExpectedThreatLevel` etc.) are never allocated.

### TASK-BB-1b-05 — `BlackboardManaged` asset wiring + `BlackboardVariable` reference kind

Surface the asset-level `IsBlackboardEditorManaged` flag (mirrors `[BTreeDefinition].BlackboardManaged`), maintain the per-variable allocation list, and add the new `SubElementKind.BlackboardVariable` (`{AssetId}::{VariableName}`) registered with the reference catalog. (`BlackboardField` already exists — reuse it.)

- **Spec:** BB §3.1, §11.4, §14.1, §14.2 (BTH §3), §14.3.
- **Where:** `SubElementKind.cs` (+`BlackboardVariable`); `BehaviorTreeAsset`/`HsmAsset` flag + allocation list; catalog reference tracking of `ExpressionTargetField`.
- **Dependencies:** TASK-BB-K-01; existing reference catalog.
- **Verifies:** unit tests — `BlackboardVariable` references keyed/found per asset; flag round-trips from the attribute; allocation list reflects add/remove. Confirms only one new enum value added (no duplicate `BlackboardField`).

### TASK-BB-1b-06 — Round-trip determinism property tests (RT-1, RT-2)

CI property tests for the two guarantees: no-edit round-trip byte-identical; single-edit round-trip produces a clean confined diff with read-only spans untouched.

- **Spec:** BB §3.7 (RT-1, RT-2).
- **Where:** `Hrot.Editor.AiShared.Tests/Blackboard/` (`BlackboardDtoEmitterTests` round-trip section).
- **Dependencies:** TASK-BB-1b-01.
- **Verifies:** RT-1 — load→save (no edit) byte-identical across mixes (all editor-managed / all read-only / mixed); RT-2 — add/remove/rename/comment one editor-managed field confines the diff to affected lines and follow-on offset shifts; read-only verbatim spans byte-identical in output.

---

## Phase 1.5c — Recursive aggregation + heavy tier

Goal (BB §15 acceptance): nested subtrees surface aggregated requirements; promote-to-variable works; aggregations over 100 inline bytes auto-promote to heavy with correct companion-file emit.

### TASK-BB-1c-01 — `IBlackboardAggregator` for BTree

Walk a `BehaviorTreeAsset` and statically-resolvable descendants, emitting one `DtoRequirement` per action/condition/observer DTO and recursing into resolved `Subtree` nodes; cycle-guard via visited `HashSet<Guid>`.

- **Spec:** BB §5.1, §5.2 (BTree branch), §5.3 (cycles), §5.5, §5.6 (grouping).
- **Where:** `Hrot.Editor.AiShared/Blackboard/IBlackboardAggregator.cs` (+ BTree walker, possibly in `Hrot.BTree.Editor/Blackboard/`).
- **Public surface:** `IBlackboardAggregator.Aggregate(IEditableAsset)` → `AggregationResult(Requirements, Warnings)`; `DtoRequirement`, `AggregationWarning` (per BB §5.2).
- **Dependencies:** TASK-BB-1a-01 (schema lookup), `IAssetCatalog` (subtree resolution), existing `BTreeSubtreeResolver`.
- **Verifies (`BlackboardAggregatorTests`, BB §16.1):** nested-subtree fixtures produce expected requirements with human-readable paths; unresolved/dynamic subtree → warning + skip; cycle → warning + skip; same-DTO requirements grouped per sub-tree (BB §5.6).

### TASK-BB-1c-02 — `IBlackboardAggregator` for HSM

Walk an `HsmAsset`: enumerate each state's `OnEntry/OnExit/Activity/TimerAction`, each transition's `Guard/Action`, and global transitions; recurse into states invoking a sub-BTree orchestrator.

- **Spec:** BB §5.2 (HSM branch), §5.4 (when scan runs), §10.4 (Blueprint-hosted).
- **Where:** HSM walker in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Blackboard/`.
- **Dependencies:** TASK-BB-1c-01 (shared result types), TASK-BB-1a-01.
- **Verifies (`BlackboardAggregatorTests`):** HSM fixture with state/transition/global actions yields all requirements; sub-BTree-invoking state recurses; mixed BTree/HSM nesting resolves; debounced re-scan on edit and on catalog `Changed` (BB §5.4).

### TASK-BB-1c-03 — Unbound Sub-Tree Requirements panel section

Render the aggregation result as the panel's lower section with grouping, "Required by" provenance, and the right-click "promote to new variable" / drag-to-alias affordances (alias drop handled in 1.5d).

- **Spec:** BB §4.1 (layout), §4.5 (interactions), §5.6 (grouping), §11.3 (promote).
- **Where:** `BlackboardAuthoringWindow` second section.
- **Dependencies:** TASK-BB-1c-01/02, TASK-BB-1b-02 (promote).
- **Verifies:** integration (BB §16.2) — 3-level subtree nesting surfaces all DTOs; promote creates a master variable and clears the unbound row; visual test of `◇` glyph + grouping.

### TASK-BB-1c-04 — Heavy-tier bin-packing + `Blackboard1024` companion emit

Extend the bin-packer to spill aggregated variables to the heavy tier (928 usable) when inline (100) is exhausted; emit `{AssetName}.HeavyBlackboard.cs`; set `HeavyDtoType` on the definition.

Also add the one-shot **Re-pack** toolbar action (BB §6.5): an optimization pass that reorders variables to minimize alignment padding within the inline tier, then re-emits — user-invoked only, never automatic on save (byte offsets are code-visible state; silent reorders would churn diffs).

- **Spec:** BB §6.1–§6.4 (tiers, spill, tier-change), §6.3 (companion emit + registration), §6.5 (re-pack action).
- **Where:** `BlackboardBinPacker` (heavy path + optimization pass) + `BlackboardDtoEmitter` (heavy file); definition attribute argument; panel toolbar.
- **Dependencies:** TASK-BB-1b-04, TASK-BB-1b-01, TASK-BB-K-02.
- **Verifies (`BlackboardBinPackerTests`):** masters always inline; aggregated vars try inline-first then heavy at the 100-byte threshold; correct heavy offsets within 928; `RequiresHeavyComponent` toggles the companion file and `HeavyDtoType`; tier+offset reported per variable (BB §6.4); Re-pack reduces padding for a known mis-ordered fixture and is not triggered on a normal save.

### TASK-BB-1c-05 — Memory budget indicator with tier breakdown

Live header indicator: single `X / 100 B` bar when inline-only; dual `Inline a/100` + `Heavy b/928` when heavy is in use; amber at 80%, red at 100%.

- **Spec:** BB §4.7, §6.3.
- **Where:** `BlackboardAuthoringWindow` header.
- **Dependencies:** TASK-BB-1c-04.
- **Verifies:** visual test — bar fills as variables added; color thresholds fire; dual indicator appears on heavy spill; advanced "(tier @ offset)" annotations gated behind the Advanced-view toggle (BB §17 #5).

---

## Phase 1.5d — Approach A whole-DTO aliasing

Goal (BB §15 acceptance): dragging an unbound requirement onto a compatible variable creates an alias; emit produces one field with multiple bindings; aliased sub-trees share memory at runtime.

### TASK-BB-1d-01 — Drag-onto-variable aliasing UX

Drag an unbound requirement onto a Defined Variable to alias; update the "aliased by" badge; support multiple aliasers; reverse via the badge's "Remove alias".

- **Spec:** BB §7.1, §7.2.
- **Where:** `BlackboardAuthoringWindow` drag handlers + alias model on the asset.
- **Dependencies:** TASK-BB-1c-03.
- **Verifies:** unit tests — alias adds a binding without new allocation (BB §7.6); second aliaser appends to the badge; remove-alias returns the requirement to Unbound.

### TASK-BB-1d-02 — Type-match validation on drop

Drop target highlights green only on exact DTO-type equality; red otherwise; no structural/subtype matching.

- **Spec:** BB §7.3.
- **Where:** drag-validation in the panel.
- **Dependencies:** TASK-BB-1d-01.
- **Verifies:** unit test — exact type → allowed/green; any other type → rejected/red; the cross-region refusal (BB §7.7/§9.4) is layered in TASK-BB-1f-02.

### TASK-BB-1d-03 — Orchestrator emit for aliased sub-trees (BTree)

Emit the auto-generated orchestrator action that projects the master's aliased slice as the sub-tree's blackboard (no copy) into `{AssetName}.Orchestrators.g.cs`.

- **Spec:** BB §7.5 (emit shape), §4a.2 (`.g.cs`).
- **Where:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/` orchestrator template; BTreeFluentEmitter hook (BTH §4).
- **Dependencies:** TASK-BB-1d-01, TASK-BB-1b-01.
- **Verifies (BB §16.2):** alias across two sub-trees → emitted orchestrator projects the same offset for both; runtime tick shares memory (pointer identity).

### TASK-BB-1d-04 — Orchestrator emit for state-hosted sub-BTrees (HSM)

Same projection for HSM states that host a sub-BTree via orchestrator, accounting for the differing kernel API.

- **Spec:** BB §7.5, §14.3 (HSH §4 HsmFluentEmitter).
- **Where:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Blackboard/` orchestrator template.
- **Dependencies:** TASK-BB-1d-03 (shared template logic).
- **Verifies:** emitted HSM orchestrator projects the aliased slice; integration parity with the BTree case.

### TASK-BB-1d-05 — "Aliased by" badge rendering

Render the alias provenance line on the variable row (`↳ aliased by: Shoot_BT, Reload_BT`).

- **Spec:** BB §7.2, §4.2.
- **Where:** `BlackboardAuthoringWindow` row renderer.
- **Dependencies:** TASK-BB-1d-01.
- **Verifies:** visual test — badge lists all aliasers; updates on add/remove.

---

## Phase 1.5e — Approach B field-level synchronization

Goal (BB §15 acceptance): a Subtree with field-level sync produces an orchestrator with correct Sync In/Out copies around the tick; A and B compose on one asset.

### TASK-BB-1e-01 — Inspector Parameter Synchronization sub-panel

Add the **Parameter Synchronization** section to the Inspector when a Subtree node (BTree) or sub-BTree-hosting HSM state is selected: one row per sub-DTO field.

- **Spec:** BB §8.2; BB §11.6 (subtree binding ≠ action binding); BB §14.3 (HSH §11 facet extension).
- **Where:** BTree subtree-node inspector facet + HSM `TransitionFacet`/`StateFacet` extension.
- **Dependencies:** TASK-BB-1a-01 (sub-DTO field enumeration via schema).
- **Verifies:** unit test — selecting a Subtree shows a row per sub-DTO field; non-subtree nodes show the normal single picker.

### TASK-BB-1e-02 — "Bound to" dropdown with type filtering

Per field, a dropdown of master variables whose type exactly matches the sub-DTO field's type; `(none)` default.

- **Spec:** BB §8.2, §8.5 (strict type match).
- **Where:** the sync sub-panel.
- **Dependencies:** TASK-BB-1e-01.
- **Verifies:** unit test — `int` field lists only `int` masters; no implicit coercion; `(none)` leaves the field unsynced.

### TASK-BB-1e-03 — Sync In / Sync Out checkboxes per field

`☑↓` Sync In (pre-tick copy master→sub) and `☑↑` Sync Out (post-tick copy sub→master); both off by default; both may be on.

- **Spec:** BB §8.2, §8.7 (ordering guarantees).
- **Where:** the sync sub-panel; bindings persisted in the `[…Layout]` method via `.SubtreeSync(...)` (BB §14.6).
- **Dependencies:** TASK-BB-1e-02.
- **Verifies:** unit test — checkbox state persists to layout metadata; multiple Sync-In on one subtree order by declared field order.

### TASK-BB-1e-04 — Orchestrator emit with sync copies

Emit the orchestrator that performs Sync-In copies immediately before the tick and Sync-Out copies immediately after, in declared field order; no orchestrator when zero sync bindings (falls back to the §7.5 aliased path).

- **Spec:** BB §8.3, §8.7.
- **Where:** BTree + HSM orchestrator templates (extends TASK-BB-1d-03/04).
- **Dependencies:** TASK-BB-1e-03, TASK-BB-1d-03.
- **Verifies (BB §16.2):** orchestrator emits Sync-In then tick then Sync-Out in correct order; zero-binding subtree emits no orchestrator; A+B combined on one asset emits a single composite DTO + correct orchestrators.

### TASK-BB-1e-05 — Per-Subtree DTO allocation when no aliasing

When a subtree's DTO is not Approach-A-aliased, reserve a dedicated slice `{SubTreeName}_{SubDtoTypeName}` shown dimmed under "Sub-tree allocations"; suppress it when aliasing is configured, restore it when aliasing is cleared.

- **Spec:** BB §8.4.
- **Where:** bin-packer allocation list + panel sub-section.
- **Dependencies:** TASK-BB-1e-04, TASK-BB-1d-01.
- **Verifies:** unit test — non-aliased subtree gets an auto-allocation referenced by its orchestrator; configuring Approach-A aliasing removes it; clearing restores it.

---

## Phase 1.5f — Validation, diagnostics, recovery

Goal (BB §15 acceptance): cross-region conflicts surface as suppressible warnings; unused vars surface as Info; hand-edited files load through States A/B/C/D correctly.

### TASK-BB-1f-01 — Cross-region blackboard conflict validator

Detect concurrent writes to the same variable across simultaneously-active HSM states (parallel regions), spanning Approach A aliases, Approach B sync-out, and master writers.

- **Spec:** BB §9.1–§9.2 (rule), §9.5 (sync-out), §9.6 (reader/writer via annotations), §12.3 (`CrossRegionBlackboardConflict` code).
- **Where:** extend `HsmAssetValidator` (`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/`); BB §14.3 (HSH §12).
- **Dependencies:** TASK-BB-1a-01 (`ParamAccess`), TASK-BB-1d/1e (writer enumeration).
- **Verifies (`CrossRegionConflictDetectorTests`, BB §16.1):** parallel-region writers to same variable → diagnostic; sequential-state aliasing → no conflict (BB §7.8); read-only-by-both → no conflict; unannotated → conservative warning.

### TASK-BB-1f-02 — Drop-target validator (refuse unsafe cross-region alias)

Refuse, by default, an alias drop that would create concurrent cross-region writes; offer the explicit "Allow concurrent writes" override on the variable's `⋮` menu.

- **Spec:** BB §9.4, §7.7; BB §17 #3 (conservative default confirmed).
- **Where:** panel drop validation + variable menu.
- **Dependencies:** TASK-BB-1d-02, TASK-BB-1f-01.
- **Verifies (BB §16.2):** drop across parallel regions turns red with the explanatory message; override flips it to allowed and records metadata.

### TASK-BB-1f-03 — Unused-variable diagnostic + glyph

Reference-count each variable via the catalog; zero refs → `○` hollow-diamond, dimmed, Info-level `UnusedVariable` diagnostic; also surface `VariableTypeNotFound` when the schema rebuild drops a type.

- **Spec:** BB §12.1–§12.3, §10.7 (type-not-found).
- **Where:** validators (BTree + HSM) + panel renderer; `BlackboardDiagnosticCode` enum (BB §12.3).
- **Dependencies:** TASK-BB-1b-05 (reference catalog), TASK-BB-1a-02.
- **Verifies (`UnusedVariableDetectorTests`, BB §16.1):** zero-ref → Info diagnostic + glyph; adding a referencing node clears it; removed DTO type → `VariableTypeNotFound`, field preserved verbatim.

### TASK-BB-1f-04 — "Remove unused" toolbar action

One-click batch removal of all zero-reference variables behind a confirmation dialog; single undoable command; single Hard reload.

- **Spec:** BB §12.4, §12.6 (Hard reload, single batch).
- **Where:** panel header action + batch command.
- **Dependencies:** TASK-BB-1f-03.
- **Verifies:** unit test — only zero-ref vars removed; confirmation reports count + freed bytes; one reload event, not many; Ctrl+Z restores.

### TASK-BB-1f-05 — Suppression metadata persistence

Persist per-pair conflict suppressions (`.SuppressBlackboardConflict`) and per-variable unused suppressions (`.SuppressUnusedWarning`) in the `[…Layout]` method.

- **Spec:** BB §9.3 (per-pair), §12.5 (per-variable), §14.6.
- **Where:** layout-method emit + load.
- **Dependencies:** TASK-BB-1f-01, TASK-BB-1f-03.
- **Verifies:** round-trip test — suppressions persist across save/reload; conflict suppression is per (variable, writer-pair); a new aliasing relationship re-surfaces a fresh diagnostic.

### TASK-BB-1f-06 — `[BlackboardReadOnly]` / `[BlackboardReadWrite]` handling

Schema exporter reads the access annotations; the conflict validator uses them to distinguish readers from writers; absence → conservative ReadWrite.

- **Spec:** BB §9.6, §10.2.
- **Where:** `IActionSchemaExporter` (`ParamAccess`) + validator consumption.
- **Dependencies:** TASK-BB-K-04, TASK-BB-1a-01, TASK-BB-1f-01.
- **Verifies:** unit test — annotated read-only excluded from writer set; absent annotation → writer (false-positive-then-suppress path works).

### TASK-BB-1f-07 — Failure-state handling (States A/B/C/D)

Implement the four load outcomes and their save protections.

- **Spec:** BB §13.1 (States A–D), §13.2 (recovery loop), §13.3 (save protections incl. the State-B lossy save), §13.4 (sub-tree DTO unavailable).
- **Where:** load pipeline + save guard in `Hrot.Editor.AiShared/Blackboard/`.
- **Dependencies:** TASK-BB-1a-04/05, TASK-BB-1b-01.
- **Verifies (BB §16.2 failure-mode ladder):** State A clean; State B → asset-wide read-only fallback + diagnostic, lossy save gated behind explicit warning; State C → reflection-only read-only, no save; State D → build-error message, panel content preserved, no save; sub-tree DTO unloadable → sync bindings preserved-but-unvalidated.

---

## Phase 1.5g — Blueprint UX parity (Tier A)

Goal (BB §15 acceptance): Blueprint variable authoring gains comments, drag-reorder-with-warnings, and live memory budget via a shared control; Blueprint Params renames propagate to master Sync bindings. **Does not** migrate Blueprint to the C# source-of-truth model (Tier B deferred, BB §17).

### TASK-BB-1g-01 — Extract `VariablesPanelControl`

Extract the panel's reusable affordances into a configurable control (single-vs-dual list, aliasing on/off, schema source).

- **Spec:** BB §15 (1.5g), §4 (affordances).
- **Where:** `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`.
- **Dependencies:** Phases 1.5a–1.5f panel surface.
- **Verifies:** unit test — control honors each configuration flag; existing BTree/HSM panel behavior unchanged after extraction.

### TASK-BB-1g-02 — Migrate BTree/HSM panel to `VariablesPanelControl`

Re-host the BTree/HSM Variables panel on the control with `single-list + aliasing-on`.

- **Spec:** BB §15 (1.5g).
- **Dependencies:** TASK-BB-1g-01.
- **Verifies:** regression — all prior BTree/HSM panel tests still pass against the migrated control.

### TASK-BB-1g-03 — Migrate Blueprint variable panel to `VariablesPanelControl`

Re-host the Blueprint Params/WorkingState panel with `dual-list + aliasing-off`; per-section budget (Params → 100 B inline; WorkingState → selected `BlackboardTier`).

- **Spec:** BB §15 (1.5g); BPE (Blueprint editor variable panel).
- **Dependencies:** TASK-BB-1g-01.
- **Verifies:** visual test — Blueprint panel shows comments, drag-reorder warnings, dual budget bars.

### TASK-BB-1g-04 — Blueprint JSON `Comment` field + emit

Extend the Blueprint JSON schema with per-variable `Comment`; `AiPrimitiveEmitter` emits `///` blocks above generated fields.

- **Spec:** BB §15 (1.5g), §4.3.
- **Dependencies:** TASK-BB-1g-03.
- **Verifies:** unit test — comment round-trips JSON→emitted `///`; absent comment emits no doc block.

### TASK-BB-1g-05 — Blueprint JSON `VariableOrder` + emit

Extend the schema with an explicit `VariableOrder` array honored by `AiPrimitiveEmitter`.

- **Spec:** BB §15 (1.5g).
- **Dependencies:** TASK-BB-1g-04.
- **Verifies:** unit test — reordering in JSON reorders emitted fields deterministically.

### TASK-BB-1g-06 — Blueprint Params rename → `BlackboardField` refactor

Wire Blueprint Params field rename through the existing `SubElementKind.BlackboardField` catalog kind so renames propagate to Approach B Sync In/Out bindings in all master BTree/HSM orchestrators.

- **Spec:** BB §11.4, §15 (1.5g), §17 resolved-item (Blueprint parity).
- **Where:** Blueprint editor rename command → `IRefactorService` on `BlackboardField` (`{DtoTypeFqn}::{FieldName}`).
- **Dependencies:** existing `BlackboardField` kind; TASK-BB-1e-03 (sync bindings).
- **Verifies (BB §16.2):** renaming a Params field updates every master Sync In/Out binding referencing it.

---

## §99 Design-coverage matrix (final check)

Every BB chapter's final ideas mapped to the task(s) that implement it. Used to confirm no design idea is orphaned.

| BB chapter | Final idea | Task(s) |
|------------|-----------|---------|
| §2 | Two-category ownership; marker is sole authority; no collaborative tier | 1a-04, 1a-05, 1b-01 |
| §3.2 | Load pipeline (locate, reflect, parse, classify) | 1a-04, 1a-05, 1f-07 |
| §3.3 | Save pipeline (canonical order, per-field emit, atomic) | 1b-01 |
| §3.4/§3.5 | Six-rule plain-field classification; read-only-passthrough verbatim | 1a-05, 1b-01 |
| §3.6 | Reorder read-only with per-reorder warning modal | 1b-02 (read-only reorder warning), 1f-07 |
| §3.7 | RT-1 / RT-2 round-trip guarantees | 1b-06 |
| §4.1–4.7 | Panel layout, glyphs, comments, add, row interactions, layout-kind, budget | 1a-03, 1b-02, 1c-05 |
| §4a | File/folder conventions, marker authority, DTO picker source, refactor | 1b-01, 1a-06, 1b-03 |
| §5 | Recursive aggregation (BTree + HSM), cycles, schedule, grouping | 1c-01, 1c-02, 1c-03 |
| §6 | Two tiers, bin-packing, heavy promotion, tier-change, re-pack (§6.5), ingress budget (§6.6) | 1b-04, 1c-04, 1c-05 |
| §7 | Approach A aliasing, type-match, promotion, emit, bin-pack, region rules | 1d-01..05, 1f-02 |
| §8 | Approach B sync, inspector UX, emit, allocation, type-match, compose, ordering | 1e-01..05 |
| §9 | Cross-region conflict rule, diagnostic+override, drop refusal, sync-out, readers | 1f-01, 1f-02, 1f-06 |
| §10 | Action schema exporter, population, Blueprint actions, picker source, rebuild | 1a-01, 1a-02, 1a-06, K-03, K-04 |
| §11 | Per-node binding, type-filtered picker, promote, ref catalog kinds, canvas badge (§11.5), subtree-vs-action binding (§11.6) | 1a-06, 1b-02, 1b-03, 1b-05, 1e-01 |
| §12 | Unused-variable detection, Info diagnostic, remove-unused, suppression, reload | 1f-03, 1f-04, 1f-05 |
| §13 | Failure States A–D, recovery loop, save protections, sub-tree DTO unavailable | 1f-07 |
| §14 | Required infra additions (shared, BTree, HSM, new types, kernel, layout) | K-01..04, 1a-01, 1b-05, 1e-03, 1f-05 |
| §15 | Slice plan + out-of-scope | All phases (this doc mirrors §15) |
| §16 | Unit / integration / visual test strategy | "Verifies" lines of each task |
| §17 | Open questions / resolved items | Noted inline (1b-03 #4/#6, 1c-05 #5, 1f-02 #3, 1a-01 #2, 1b-04 #1) |

**Out-of-scope (BB §15) — intentionally no tasks:** live-edit of running-entity values (Slice 3 host), automatic DTO inference, multi-asset shared variables, on-canvas data-pin wiring, per-region heavy blackboards, Tier B Blueprint C# adoption.
