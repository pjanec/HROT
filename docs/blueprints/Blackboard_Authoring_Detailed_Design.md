# Blackboard Authoring — Detailed Design

> **Status:** Detailed design. **v2 — JSON-backed (revised 2026-06-04).** Originally derived from `AI_Editor_Shared_Infrastructure.md` + `BTree_Editor_NodeEditor_Host_Design.md` + `HSM_Editor_NodeEditor_Host_Design.md` + the `design-talk.md` brainstorm sessions.
> **Audience:** Implementation agent and human reviewer.
> **Drives:** Visual blackboard authoring across BTree and HSM editors. Adds a Blackboard Variables panel, recursive sub-tree aggregation, opt-in DTO aliasing, field-level synchronization, and **JSON-backed editor-owned blackboard schema** — the param/heavy DTO struct is *generated from JSON at build* (no editor-emitted `.Blackboard.cs`).
> **Doesn't cover:** Kernel-side DTO projection / pointer arithmetic (owned by FastBTree / FastHSM). Blueprint AiPrimitive blackboard authoring (the Blueprint editor already supports visual `Params`/`WorkingState` declaration).
> **Companion code lives in:** `Hrot/Subsystems/AI/Hrot.Editor.AiShared/Blackboard/` for the panel and aggregation services; `Hrot.BTree.Editor/Blackboard/` and `Hrot.Hsm.Editor/Blackboard/` for subsystem-specific projection.
> **➤ See also (authoritative refinement):** [`Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md`](./Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md) — action-parameter authoring (whole-DTO binding; per-field on action nodes is rejected) + **node-owned / auto-managed variables** (the "+ Promote to new variable" → hidden, auto-deleted variable model). Refines §11 + §4.

---

## ⚠ REVISION NOTICE — v2: JSON-backed Category-2 (authoritative; overrides conflicting text below)

This DD predates the **Persistence Unification** thread, which makes **JSON the source of truth for BTree/HSM** (see `.dev/_DONE/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md`, decisions D1–D14). The blackboard feature is **re-based onto that JSON substrate**. Where the sections below describe a C#-source-of-truth `.Blackboard.cs` mechanism, **this notice wins.**

**What changes (editor-owned, "Category 2"):**
- Editor-owned blackboard variables are **serialized into the asset's `.btree.json` / `.hsm.json`** as a `Blackboard` block (mirroring Blueprint `VariableDecl`), **not** emitted to a `{AssetName}.Blackboard.cs` companion file. There is no editor-owned `.cs` for the blackboard.
- The param-DTO struct (and the optional heavy struct + offset-projection thunks) is **generated from the JSON at build** by the Persistence-Unification Roslyn generator, emitted to `obj/GeneratedFiles` — **not committed**.
- **Superseded / obsolete** (these existed only to round-trip a hand-touchable editor-owned `.cs`):
  - §1.3 "C# is the source of truth" → **JSON is the source of truth** for Category 2.
  - §2.1–§2.3 "Category 2 = a marker'd `.cs`" → Category 2 = the `Blackboard` block in the asset JSON. The `HROT_EDITOR_GENERATED` marker still governs *hand-written* `.cs` (Category 1) and generated artifacts, but editor-owned blackboard schema is no longer a marker'd `.cs`.
  - §3.1 companion `{AssetName}.Blackboard.cs` → **none**; vars live inline in the asset JSON.
  - §3.2 load (reflect + read source text) → **JSON deserialize** into the editor model.
  - §3.3 save (emit `.cs`) → **serialize the `Blackboard` block** into the asset JSON.
  - §3.4 plain-field rule, §3.5 read-only-passthrough, §3.6 reorder warning, §3.7 RT-over-C# → **obsolete for Category 2** (no editor-owned `.cs` to classify/preserve). Exotic fields are handled by **extending the editor** (see "Exotic fields" below) or by **Category-1 composition** (hand-write a blittable struct, embed it by reference). Round-trip determinism now applies to **JSON** (Persistence-Unification round-trip tests).
  - §13 State B (span-capture fail) / State C (struct-parse fail) → **N/A** (no source-text parse). The data-loss problem is *gone*: a JSON-owned blackboard **always opens**, even if the generated C# won't compile. State D becomes "build failed → runtime thunks not live," not "authoring blocked."
  - §14.6 layout-method entries (variable order, sync bindings, conflict/unused suppressions) → **first-class fields in the asset JSON** (Persistence-Unification schema §5). Order = JSON array order = byte offsets.
  - §14.4 `BlackboardDtoEmitter` and `BlackboardSourceTextParser` → **obsolete**; replaced by the JSON→C# generator.

**What is PRESERVED, unchanged:**
- **Category 1** (hand-written DTO, no marker): reflected via `ActionSchemaExporter` / `[BlackboardDtoStruct]`, surfaced **read-only**, never written by the editor. Designers pick Category-1 structs into Category-2 blackboards. (§2.1, §4a.4)
- **All editor-facing logic** — the Variables panel (§4), recursive aggregation (§5), memory-tier bin-packing (§6), Approach-A aliasing (§7), Approach-B field sync (§8), cross-region conflict validation (§9), action-DTO discovery (§10), per-node binding (§11), unused diagnostics (§12). These operate on the **in-memory variable model** and are persistence-agnostic; only their *backing store* moved from `.Blackboard.cs` to JSON.

**Verified specifics (all confirmed against the v250+Thread-2 sources):**
- **Strings:** use `Fdp.Core.FixedString32` / `FixedString64` (blittable, UTF-8 inline). **Never `System.String`** (managed → breaks zero-alloc, AAR replay, replication). Mind the `Fdp.Core.FixedString32` vs `GizmoMap.Contracts.FixedString32` name collision — use `Fdp.Core`; GizmoMap.Contracts stays 0.2.2.
- **Blittable + fixed-size invariant:** editor-owned blackboards (and any embedded Category-1 struct) contain only blittable value types + fixed-length inline arrays — so the whole `BrainBlackboard`(+`Blackboard1024`) region is byte-copyable for AAR replay and network replication.
- **Arrays (Exotic fields, editor-authorable):** `fixed {prim}[N]` for primitives, `[InlineArray(N)]` for blittable structs. Generator must replicate `Sequential` alignment (natural align, capped at 8; pad to 8 before a `fixed long[]`) or editor offsets diverge from the compiler → flight-recorder corruption. **`[InlineArray]` mutation trap:** generated accessors must mutate via `Span<T>` / `MemoryMarshal.CreateSpan` / `Unsafe.As` (never direct index → `ldobj` defensive copy silently lost). Genuinely-exotic layouts (`[FieldOffset]`, unions, interop) stay **Category-1**.
- **Defaults (editor-authored):** applied in the generated `ParseParamsDelegate` — instantiate DTO → apply defaults → overlay JSON params → `Unsafe.Write`. **Inline tier** only is covered by `BehaviorIngressSystem`; **heavy-tier** defaults need an inline init-check in the execution thunks (verify the 8-byte `StructureHash`; apply if uninitialized), mirroring Blueprint `InitDefaultWorkingState`.
- **Inline ceiling:** `BehaviorConstants.MaxBehaviorParamByteSize = 100` (`BrainBlackboard` component = 128; tail registers `ExpectedThreatLevel`@120, `Interrupt_MobilityLost`@126, `Interrupt_Reserved`@127). The bin-packer uses **100**.
- **Registration:** the generator emits a per-asset isolated class tagged **`[BlueprintRegistrar]`** (NOT `[FbtRegistrar]`/`[HsmActionRegistrar]`) with a `Register(BehaviorRegistry, BlueprintRegistryStaging)` signature; it compiles+registers the definition and the blackboard struct's offset thunks — BTree via `BehaviorRegistry.RegisterAction/RegisterCondition(…BlueprintBTree{Action,Condition}Delegate)`, HSM via static `HsmActionDispatcher.RegisterAction/RegisterGuard`. Discovered by `AiHotReloadCoordinator` on both full rebuild and in-process quick reload — **no HR-001 change**. (Kernel hooks `BlackboardManaged`, `HeavyDtoType`, `[BlackboardDtoStruct]`, `[BlackboardReadOnly/ReadWrite]` already exist.)

**Sequencing:** the Persistence-Unification thread lands the JSON substrate first; this feature (the Slice 1.5 tasks below) is implemented/activated **on top of it**. The slice-plan §15 task list stays valid except the persistence-coupled tasks (the `.Blackboard.cs` emitter, source-text parser/classification, State-B/C handling, layout-method order/sync entries), which are **superseded** by the JSON substrate.

### v2.1 — architect-review resolutions (fold into the relevant sections)
- **Orchestrator emission is generator-side, not editor-side** (supersedes §14.2/§14.3 wording). The editor only serializes `SubtreeSyncBinding` / alias data into the asset JSON; the Roslyn generator (and the in-process Quick Reload emit core) produces the `{AssetName}.Orchestrators.g.cs` thunks at build into `obj/`. The editor never writes orchestrator `.cs`.
- **Approach-B sync of fixed-array fields** (§8.3): when a Sync In/Out binding targets a `fixed`/`[InlineArray(N)]` field, the generated orchestrator MUST copy via `Span<T>` / `MemoryMarshal.CreateSpan` / `Unsafe.CopyBlock` — never plain assignment or element indexing (the `ldobj` defensive-copy trap silently drops the write).
- **Suppressions persist to JSON, not the layout method** (supersedes §9.3 `.SuppressBlackboardConflict()` and §12.5 unused-suppression persistence). They are first-class fields in the asset JSON (Persistence-Unification schema §5).
- **Heavy-tier default init:** lazy `StructureHash` init-in-thunk is **safe under the current kernel** — `Fhsm.Kernel` ticks regions and instances **single-threaded** (`HsmKernelCore` `for` loops; no `Parallel`/jobs/threads), so two regions can't init the same `Blackboard1024` slot simultaneously. **Assumption recorded:** if a parallel HSM scheduler is ever added, revisit. **Preferred (more robust) alternative:** initialize heavy defaults at **component provisioning** time (when `BehaviorIngressSystem` attaches `Blackboard1024`), removing the order/threading dependence entirely — confirm with the architect whether provisioning can zero+default-init the heavy payload. If lazy-init is retained, the Approach-B orchestrator (which *is* the execution thunk for a heavy synced sub-DTO) must perform the init-check before any Sync In/Out (R1-4).
- **`HeavyDtoType` is not a reference loop:** the param/heavy struct AND the `[BTreeDefinition(HeavyDtoType=typeof(...))]`/`[HsmDefinition(...)]` thunk are emitted by the **same** generator into the **same** compilation, so `typeof` resolves (no ordering problem). The generator must co-emit them in one unit.
- **Offsets are extracted, not predicted (AAR integrity):** the editor bin-packer's `Sequential`-alignment math is **advisory** (for the live memory-budget UI). The **authoritative** layout — and the AAR flight-recorder schema — must be derived from the *actual compiled struct layout* (reflected `Marshal.OffsetOf` / `Marshal.SizeOf` after build/quick-reload), never from the editor's predicted offsets, to prevent silent offset drift.
- **Aggregation discovery latency (known characteristic):** because `ActionSchemaExporter` reflects the loaded assembly, newly-authored Blueprint param structs become visible to the BTree/HSM aggregation walker only after a reload bakes them (§10.7 rebuild on `IAssetCatalog.Changed`). Acceptable; a future enhancement could read schemas from JSON to avoid the reload.

### v2.2 — architect re-review confirmations (closes PU-1001 re-review)
- **Tier/offset are recomputed, not persisted (Q1).** The generator re-runs bin-packing from the **JSON declaration (array) order** using `Sequential` alignment math + the 100-byte `MaxBehaviorParamByteSize` ceiling; spill to `Blackboard1024` is deterministic. No tier hint is stored in JSON. **Reconciliation with the "extract, don't predict" rule:** the math *drives* generation and the editor's memory-budget UI, but the **authoritative** layout for AAR/runtime projection is the **reflected compiled-struct layout** (`Marshal.OffsetOf`/`SizeOf` post-build). Build-time should **validate** predicted == reflected and emit a diagnostic on any mismatch (guards against a subtle math drift silently corrupting AAR).
- **Alias + sync bindings are first-class JSON (Q2).** `SubtreeSyncBinding` and `BlackboardAliasBinding` collections serialize directly into the asset JSON (no dedicated schema beyond Thread-1's reserved fields); the generator parses them to emit orchestrators. Confirms the §8/§14.6 supersession.
- **Category-2 panel renders from the JSON in-memory model only (Q3).** The Variables panel binds to `IBlackboardManagedAsset.BlackboardVariables` loaded from JSON; **no reflection of the generated struct** for Category-2 (that would reintroduce the compilation-lockout). Reflection (`BlackboardSchemaBuilder` / `[BlackboardDtoStruct]` discovery) is **Category-1 only**, surfaced read-only.

---

## Table of Contents

1. Scope and design goals
2. Source-of-truth model — two categories
3. The DTO file — load and save lifecycle
4. The Blackboard Variables panel
4a. File and folder layout conventions
5. Recursive aggregation across nested behaviors
6. Memory-tier bin-packing
7. Approach A — whole-DTO aliasing
8. Approach B — field-level synchronization
9. Cross-region conflict validation
10. Action-DTO discovery
11. Per-node binding to blackboard variables
12. Unused-variable diagnostics
13. Failure modes and recovery
14. Required additions to existing infrastructure
15. Slice plan
16. Test strategy
17. Open questions

---

## 1. Scope and design goals

### 1.1 What this DD adds

The existing BTree and HSM editors (per their respective host design docs) allow the designer to visually author tree topology and state machines. But the *blackboard DTO* — the C# struct that holds an asset's parameters and runtime state — has remained hand-written. The visual editors reflect a user-authored struct via `BlackboardSchemaBuilder` and surface its fields in pickers; the designer never touches the actual data layout.

This is friction for non-programmer designers: they can author logic but not the memory it operates on. Worse, when a designer adds an action that needs a parameter DTO not yet in the blackboard, they have to leave the editor, write C#, save the .cs, wait for reload, then bind the action.

This DD lands four things:

- **A Blackboard Variables panel** in both editors that visually declares the DTO fields. Add, remove, rename, retype variables — and write per-field comments — without leaving the editor.
- **Recursive aggregation** of parameter requirements from nested behaviors (sub-BTrees, HSM-embedded BTrees) into the parent's blackboard variable list at *edit time*, with opt-in aliasing.
- **Two sharing models** — whole-DTO aliasing (Approach A, true pointer sharing) and field-level synchronization (Approach B, copy-down/copy-up via orchestrator) — with visual UX for each.
- **Editor-owned C# DTO emission** that produces a readable .cs file. Comments authored in the panel round-trip as `///` XML doc blocks. A technical user can hand-introduce fields with attributes or non-standard types; the editor displays them read-only and preserves them byte-for-byte on save.

### 1.2 What this DD does NOT change

- The kernel's DTO projection model is unchanged. Generic actions still receive `ref TValue` slices via `Unsafe.AddByteOffset` pointer math.
- The BrainBlackboard / Blackboard1024 component layout is unchanged.
- `BehaviorIngressSystem`'s JSON parameter parsing is unchanged.
- The `[BTreeAction]` / `[HsmAction]` / `[SharedAiAction]` attribute model is unchanged. Actions still declare their DTO via the first `ref` parameter of their method signature.
- Existing hand-written blackboards continue to work without migration. The editor recognizes them (Category 1, see §2), lets designers wire action nodes to them via the picker, but never writes them.

### 1.3 Design goals

- **C# is the source of truth.** No sidecar files, no editor-private state files. The .cs blackboard DTO file is what the editor loads, what the editor saves, what gets committed to git, what the source generator and runtime consume. — **⚠ SUPERSEDED (v2): JSON is the source of truth for Category 2; the C# struct is generated from JSON at build. See the REVISION NOTICE at the top.**
- **Two file categories, sharply separated.** Either the editor owns the file (Category 2 — full visual authoring, marker present, editor regenerates on save) or the user owns it (Category 1 — no marker, editor never writes). No collaborative-ownership middle ground; no per-field partial ownership across two files.
- **Within an editor-owned file, the user can hand-introduce fields the editor can't model.** Those fields are displayed read-only in the Variables panel and emitted verbatim from captured source text on save. They cannot be deleted or modified via the panel; only via the source. Editor-managed fields and read-only fields coexist in one file under one marker.
- **Comments are first-class.** The Variables panel offers comment editing for editor-managed fields. Comments emit as `///` XML doc blocks above the field declaration so they appear in IDE tooltips for programmers consuming the DTO.
- **Round-trip determinism.** A load → save round-trip with no changes produces byte-identical output. A round-trip with a single visual edit produces a clean diff confined to the edited field.
- **Designer-friendly aggregation.** Recursive scanning of nested behaviors surfaces all required parameter DTOs in one panel. Aliasing is explicit (drag-drop), never automatic. Field name collisions don't auto-merge.
- **Failure modes are forgiving.** If the user edits the .cs file into a partially-supported state, the editor loads what it can, surfaces what it can't, and never destroys user content.

### 1.4 Required reading

This DD assumes familiarity with:

- BTH §9 (BTree blackboard reflection — read-only mode, the starting point for this DD's full authoring mode).
- HSH §11 (HSM facets, picker attributes — gets extended for blackboard variable binding).
- Shared infra §4 (FQN reference catalog — extended with a new `BlackboardVariable` SubElementKind).
- Shared infra §6 (FluentCSharpEmitter — the deterministic-emit framework; this DD extends with blackboard DTO emit).

---

## 2. Source-of-truth model — two categories

> **⚠ v2:** Category 1 (hand-written, reflected, read-only) is unchanged. **Category 2 is now JSON-backed** — vars live in the asset's `.btree.json`/`.hsm.json`, not a marker'd `.Blackboard.cs`. The `.cs`-ownership prose below describes the obsolete v1 mechanism; see the REVISION NOTICE.

### 2.1 The two categories

The editor's interaction with any blackboard DTO file falls into exactly one of two categories, determined solely by whether a marker comment is present at the top of the file.

**Category 1 — hand-written, editor read-only.**

A `.cs` file containing a struct definition with no marker comment at the top. The user owns the file outright. The editor:

- Reflects the struct via the loaded assembly's `FieldInfo[]`.
- Surfaces the struct's fields in the Variables panel of any asset that uses this struct as its blackboard.
- Lets BTree/HSM action nodes bind to the fields via the dropdown picker.
- **Never writes to the file.**

This category covers: shared DTOs in `Shared/DTOs/`, legacy hand-written blackboards from before the editor existed, custom DTOs the technical user wants under their direct control. The user writes whatever C# they want and takes responsibility for it.

**Category 2 — editor-owned, full visual authoring.**

A `.cs` file containing a struct definition with the marker comment at the top:

```csharp
// HROT_EDITOR_GENERATED — managed by the AI editor; manual edits will be overwritten on next save.
// OwningAssetId: f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21
// OwningAssetName: OrcGuard_BT
```

The editor owns the file. On every save, the editor regenerates the whole file from its in-memory model. Fields the editor manages (per §3.4) get regenerated in a canonical form; fields the editor cannot model (per §3.5) are emitted verbatim from captured source text.

This category covers: per-asset blackboard files created by the editor as part of visual authoring. One such file per asset that uses visual blackboard authoring.

### 2.2 Same marker as asset files

The marker is `HROT_EDITOR_GENERATED` — identical to what BTree and HSM asset files use (per BTH §4.1 and HSH §4.1). Same semantics: editor owns the file, hand edits to editor-managed parts are overwritten. The blackboard DTO joins the existing editor-owned file convention rather than introducing a new ownership level.

The four-line marker block at the file's top:
1. The marker token (`HROT_EDITOR_GENERATED`) plus a one-line policy reminder.
2. (continuation of the reminder, mentioning hand-introduced fields are preserved if the editor can't model them.)
3. The owning asset's AssetId.
4. The owning asset's name.

Lines 3 and 4 enable `grep -r OwningAssetId` to locate a blackboard file's parent asset and vice versa.

### 2.3 What the user can and cannot do within a Category 2 file

A technical user can hand-edit a Category 2 file (the marker doesn't physically prevent it), but the editor's regeneration on save means most hand edits get clobbered. The exception is **non-plain fields** — fields the editor can't model. Those are preserved verbatim. See §3.5.

For comments specifically: a `///` XML doc block above an editor-managed field is read on load (so the comment appears in the panel), and on save the editor emits the comment from its in-memory model. If the user hand-edits the comment in the .cs and then makes any panel edit, the user's hand-edited comment loses to whatever's in the panel. The Variables panel is the source of truth for comments on editor-managed fields.

For comments on read-only fields: the comment is read from source on load, displayed in the panel as read-only, and emitted verbatim from the captured source span on save. The panel cannot edit it. To change such a comment, edit the source.

### 2.4 Why no `HROT_EDITOR_MANAGED` collaborative model

An earlier draft of this DD introduced a separate `HROT_EDITOR_MANAGED` marker for collaborative ownership — the editor and the user both write to the same file, the editor parses source text to classify each field into three tiers (known / tolerated / opaque), and the editor preserves tolerated-tier fields verbatim while regenerating known-tier fields.

This was dropped after design review for two reasons:

1. **The two-category model is sharper.** Having three ownership states (user-owned, collaboratively-owned, editor-owned) created a "what does this marker actually mean?" question for every file. Two states (user-owned, editor-owned) are unambiguous.
2. **The middle tier (editor-tolerated) is rarely useful in practice.** It existed to let the user add attributes to fields the editor otherwise managed (e.g., `[FieldOffset]` on a primitive field). The same effect is achievable by hand-writing the entire field — including its declaration line — as a non-plain field the editor preserves verbatim. The user gives up panel-side editing of that one field's name and type in exchange for full control over its attributes.

The remaining `HROT_EDITOR_GENERATED` model handles all the cases the collaborative model handled, just via a different mechanism: the verbatim preservation applies to whole field declarations, not to attribute subsets of editor-managed fields.

---

## 3. The DTO file — load and save lifecycle

> **⚠ v2 — this entire section is the obsolete C#-source-of-truth lifecycle.** Under JSON-backed Category 2: load = JSON deserialize into the editor model; save = serialize the `Blackboard` block into the asset JSON; the C# struct (+offset thunks) is generated at build. §3.4 (plain-field rule), §3.5 (read-only-passthrough), §3.6 (reorder warning), §3.7 (RT-over-C#) no longer apply — exotic fields are editor-extended (arrays) or Category-1-composed. See the REVISION NOTICE.

### 3.1 File location and naming

For an asset `OrcGuard_BT.cs` at `Combat/`, the blackboard DTO file is at:

```
Combat/
    OrcGuard_BT.cs                       (the asset, HROT_EDITOR_GENERATED)
    OrcGuard_BT.Blackboard.cs            (the DTO, HROT_EDITOR_GENERATED)
    OrcGuard_BT.Orchestrators.g.cs       (auto-emitted orchestrator actions)
```

The DTO file's naming convention is `{AssetName}.Blackboard.cs`. The editor creates this file alongside the asset on first save if blackboard authoring is in use. If the asset doesn't use visual blackboard authoring (it references a hand-written external struct by name), no `.Blackboard.cs` file exists; the asset references whatever type the user pointed it at, which lives wherever the user put it (typically a Category 1 file under `Shared/DTOs/` or similar).

The asset's `[BTreeDefinition(...)]` / `[HsmDefinition(...)]` attribute gains an optional `BlackboardManaged = true` flag indicating "this asset uses the .Blackboard.cs companion file." When true, the type referenced by the asset's `<TBlackboard>` generic argument resolves to the editor-managed struct.

### 3.2 Load pipeline

When the editor opens an asset with `BlackboardManaged = true`:

1. **Locate the companion file** `{AssetName}.Blackboard.cs` in the same directory.
2. **Reflect the blackboard struct** from the loaded assembly. Get `FieldInfo[]` for all fields, in declaration order.
3. **Read the .cs source text** of the companion file. Used for two purposes: extracting `///` XML doc comments above editor-managed fields, and capturing verbatim source spans of fields the editor cannot model.
4. **For each reflected field, classify** as either *editor-managed* (§3.4) or *read-only-passthrough* (§3.5).
5. **For editor-managed fields**, attach the `///` comment (if any) and discard the verbatim span.
6. **For read-only-passthrough fields**, capture the verbatim source span — from the first character of the field's leading comment or attribute through the trailing semicolon. Discard the comment as a separate property (it's part of the span).
7. **Build the editor's `BlackboardVariable` list** in source-declaration order. Each entry knows its category, its current type and name (from reflection), its panel-editable comment (for editor-managed fields), and its captured span (for read-only fields).
8. **Build the asset's blackboard view.** This is what the Variables panel renders.

If step 1 fails (no companion file), the asset is treated as having a hand-written external blackboard — same as today. The panel goes read-only and reflects the external type via `BlackboardSchemaBuilder`.

If step 2 fails (assembly didn't load, struct not found), the editor surfaces the load error and disables blackboard authoring for this asset until the build is fixed.

If step 3 fails (file exists but is malformed at the struct level), see §13.

### 3.3 Save pipeline

When the editor saves an asset whose blackboard has changed:

1. **Determine the canonical field order.** This is the order in which fields appear in the emitted file and (for `LayoutKind.Sequential`) determines the byte offsets. The editor maintains this order as part of the asset's editor model — typically the order of declaration in the panel.
2. **For each field, choose emit mode based on its category.**
   - Editor-managed → regenerate from the editor model. Includes the panel-authored `///` comment if present.
   - Read-only-passthrough → emit the captured verbatim source span exactly.
3. **Emit the file header.** Marker, AssetId comment, namespace, using directives. Using directives are computed deterministically per shared infra §6.4.
4. **Emit the struct opening.** `[StructLayout(LayoutKind.Sequential)]` plus `public partial struct {Name}`. (The `partial` modifier is harmless when no other partial exists; it allows future evolution if needed without breaking existing assemblies.)
5. **Emit each field in canonical order** using its chosen emit mode.
6. **Emit the struct closing.**
7. **Atomic write** — temp file + rename, per shared infra §6.5.

The output is deterministic: same editor model → same byte output.

### 3.4 Editor-managed fields — the "plain field" decision rule

A field is editor-managed only if **all six** of the following conditions hold:

1. The declaration's outer shape is exactly `public {Type} {Name};` — `public` visibility, single type token, single name token, trailing semicolon.
2. `{Type}` is in the editor's known set:
   - C# primitives: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`.
   - Vector types: `Vector2`, `Vector3`, `Vector4`, `Quaternion`.
   - Any enum type declared in the project.
   - Any struct marked `[BlackboardDtoStruct]`, OR any struct used as the first `ref` parameter type by some registered action (auto-detected via the schema exporter, §10).
3. An optional `///` XML doc comment block (one or more consecutive `///` lines) may appear immediately above the declaration, with no blank line between the comment block and the declaration.
4. No attributes on the field declaration.
5. No initializer (`= 42`).
6. The declaration is on a single line — no embedded newlines, no multi-line attribute lists, no statement continuation.

If any of these six fails, the field is classified read-only-passthrough.

The rule is intentionally narrow. It captures the "plain data field" use case the editor manages well and rejects everything else.

### 3.5 Read-only-passthrough fields

A field that fails any of the six rules in §3.4 is read-only-passthrough. The editor:

- Captures the field's full verbatim source span on load — from the first character of leading comments/attributes through the trailing semicolon.
- Displays the field in the Variables panel with a 🔒 glyph and dimmed text.
- Shows the field's name and reflected type for human reference.
- Shows the field's leading `///` comment if present (read from the captured span; displayed as the row tooltip).
- Refuses panel-side editing of name, type, attributes, or comment.
- **Allows reorder** via drag in the Variables panel — see §3.6 below.
- **Refuses delete** via the panel. Removing such a field requires hand-editing the source. The Variables panel's delete affordance is grayed out for read-only fields with a tooltip explaining why.
- Emits the captured span verbatim on save, placed at the canonical position.

The bin-packer (§6) reads the field's reflected size and alignment from `FieldInfo` and accounts for it in layout calculations. The verbatim text is only used for emit; the runtime layout uses reflection metadata.

### 3.6 Reordering read-only fields — with a strong warning

The Variables panel allows the user to drag a read-only field to a new position in the canonical order. This is offered because designers naturally want their variables in a specific order for readability, and pinning read-only fields to source declaration order would be frustrating.

However, reordering a read-only field changes its byte offset in the emitted struct. If the user wrote `[FieldOffset(N)]` into the field's source declaration expecting it at offset N, reordering breaks that intent — the field's attribute and its actual offset disagree.

When the user attempts to reorder a read-only field, the editor surfaces a modal warning:

```
Reordering this field will change its byte offset in the emitted struct.
If this field has [FieldOffset], [StructLayout(Pack=...)], or other
layout-sensitive attributes, the reorder may invalidate them.

Field: CustomFlags
Current position: 5  (offset 24)
New position: 2  (offset 8)

[ Reorder anyway ]   [ Cancel ]
```

The warning fires every time. There's no "don't show again" — the consequence is real and the user should consciously confirm each reorder. Cheap insurance against a subtle footgun.

For editor-managed fields, reorder is silent — the editor controls those fields' attributes, so reorder is always safe.

### 3.7 Round-trip determinism guarantees

The DD commits to two round-trip guarantees, both verified by CI:

**RT-1: No-edit round-trip is byte-identical.**
Load a file. Save without making any edits via the panel. Output is byte-identical to input. This holds whether the file contains only editor-managed fields, only read-only fields, or a mix.

**RT-2: Pure-editor-edit round-trip produces clean diffs.**
Load a file. Add, remove, rename, or comment an editor-managed field via the panel. Save. The diff is confined to the affected lines plus any necessary follow-on changes to subsequent fields' canonical positions. Read-only fields' verbatim spans are byte-identical to the original in the output.

The original DD's RT-3 (preserving tolerated fields' verbatim through edits) and RT-4 (hand-edit + editor reload cycle) collapse into RT-1 and RT-2 because there's no longer a separate tolerated tier — read-only fields are handled the same way regardless of whether the user edited the file or the editor previously emitted it.

---

## 4. The Blackboard Variables panel

### 4.1 Window registration and layout

A new docked window registered as `ai_blackboard_variables`, available in both BTree and HSM editor perspectives. Default dock location: the right side, below the Inspector. The Inspector and the Variables panel share the right column; the Variables panel is taller and the Inspector is shorter when both are docked together.

```
┌──────────────────────────────────────────────────────────┐
│ ≡ BLACKBOARD VARIABLES — OrcGuard_BT                      │
├──────────────────────────────────────────────────────────┤
│  Layout: Sequential                    Memory: 78 / 100 B │
├──────────────────────────────────────────────────────────┤
│ ▼ DEFINED VARIABLES                                       │
│                                                           │
│    [+] Add variable...                                    │
│                                                           │
│   ◆ ThreatVisible       bool       (4 B)              ⋮  │
│     ↳ "True when an enemy is in line of sight"           │
│   ◆ AmmoCount           int        (4 B)              ⋮  │
│     ↳ "Bullets remaining in the magazine"                │
│   ◆ FlankTactics        MoveToLoc… (16 B)             ⋮  │
│     ↳ used by: 3 nodes                                    │
│   ◆ RetreatTactics      MoveToLoc… (16 B)             ⋮  │
│     ↳ used by: 2 nodes                                    │
│   ◆ SharedTarget        long       (8 B)              ⋮  │
│     ↳ aliased by: Shoot_BT, Reload_BT                     │
│   🔒 CustomFlags        ushort     (2 B)              ⋮  │
│     ↳ read-only: hand-introduced field with attributes    │
│   ○ UnusedTimer         float      (4 B)              ⋮  │
│     ↳ unreferenced — consider removing                    │
│                                                           │
├──────────────────────────────────────────────────────────┤
│ ▼ UNBOUND SUB-TREE REQUIREMENTS                           │
│   (Drag onto Defined Variables to alias, or right-click   │
│    to promote to a new variable)                          │
│                                                           │
│   ◇ FireTactics (FireAtTargetParams)                      │
│       Required by: [Shoot_BT (Subtree)]                   │
│   ◇ AmmoCount   (int)                                     │
│       Required by: [Reload_BT (Subtree)]                  │
│                                                           │
├──────────────────────────────────────────────────────────┤
│ Master DTO: BrainBlackboard.BehaviorParameters (inline)   │
│ Heavy DTO:  Blackboard1024 (not allocated)                │
└──────────────────────────────────────────────────────────┘
```

The header shows the asset name, the struct's layout kind, and a live memory budget indicator. The body has two sections: **Defined Variables** (what the asset's blackboard currently declares) and **Unbound Sub-Tree Requirements** (what nested sub-behaviors need but the master hasn't yet bound). The footer shows the current ECS placement decided by the bin-packer (§6).

### 4.2 Variable glyph semantics

The leading glyph on each row signals the variable's state:

- **◆** filled diamond: editor-managed, fully editable variable, referenced by at least one node.
- **○** hollow diamond: editor-managed, currently unreferenced (candidate for removal). See §12.
- **🔒** lock: read-only-passthrough; preserved from source on save; not editable in the panel. See §3.5.
- **◇** outlined diamond: unbound sub-tree requirement, not yet a defined variable.

### 4.3 Comments as a first-class feature

Each editor-managed variable carries an optional comment string in the editor's model. The comment renders in the panel as a dimmed italic line below the variable's name (the "↳" lines in the ASCII layout above) and emits as a `///` XML doc comment above the field in the generated .cs file.

Authoring a comment:
- Click a variable's row → the Inspector (the shared editor inspector, not this panel) shows the variable's full details including a multi-line comment editor.
- Or: double-click the variable's comment row in this panel → inline edit mode.
- Comments support standard `///` XML doc tags (`<summary>`, `<remarks>`, `<para>`) if the user knows what they're doing. The default is a single-line summary; the panel emits as `<summary>...</summary>`.

Comments visible everywhere a variable appears:
- In the Variables panel as a sub-row.
- In the action-node picker dropdown as a tooltip when hovering the variable's name.
- In a programmer's IDE tooltip when their code references the field (standard `///` behavior).

This is single-source-of-truth documentation surfaced consistently across the visual editor and the programmer's IDE.

For read-only-passthrough fields, the comment is read from source (extracted from the verbatim span) and displayed as tooltip-only — not editable in the panel. To change such a comment, edit the source.

### 4.4 Add Variable workflow

Clicking `[+] Add variable...` opens a popup with:

- **Name** — free text. Must be a valid C# identifier. Editor validates as the user types.
- **Type** — dropdown of editor-known types. Lists primitives, vector types, all enums in the project, and all DTO structs registered as `[BlackboardDtoStruct]` or used by any action in the project (the editor's schema exporter surfaces these — see §10).
- **Comment** — optional one-line description; emitted as a `///` doc comment.

On confirm, the variable is added to the Defined Variables list, placed at the end of the canonical order (so byte-offset stability of existing fields is preserved). The user can drag it to reorder if they care about layout.

### 4.5 Variable row interactions

Each editor-managed variable row supports:

- **Single-click** — selects the variable; the Inspector shows the variable's details, including which nodes reference it and a multi-line comment editor.
- **Double-click name** — inline rename. Renames go through the refactor service: all nodes that reference the old name automatically retarget to the new name (shared infra §16.2).
- **Double-click comment row** — inline comment edit.
- **Drag** — reorder within the Defined Variables list. Order in this list = order in the emitted file = byte offsets for `LayoutKind.Sequential`. Silent for editor-managed fields.
- **`⋮` menu** — right-click or click the trailing menu glyph: "Edit type" (offered only for editor-managed), "Find references" (opens the shared Find Results window), "Rename" (same as double-click), "Delete" (with dangling-reference report if currently referenced), "Convert to alias" (offered when this variable's DTO type matches an unbound sub-tree requirement of the same type — see §7).

For read-only-passthrough variable rows:

- **Single-click** — selects, Inspector shows reflected name, type, comment (all read-only), and a "Edit this field in source" link.
- **Double-click** — same as single-click (no inline edit available).
- **Drag** — reorder offered with the strong warning modal of §3.6.
- **`⋮` menu** — limited entries: "Find references", "Edit in source" (opens the file in the user's IDE), "Why is this read-only?" (shows the diagnostic explaining which of the six rules in §3.4 the field violates). No rename, no delete, no edit-type, no convert-to-alias.

### 4.6 Layout-kind indicator

The header shows the struct's `LayoutKind`. For Category 2 files, this is always `Sequential` — the editor emits `[StructLayout(LayoutKind.Sequential)]` and does not offer an explicit-layout toggle. Users who need explicit layout (with `[FieldOffset]` on each field) write the entire struct hand (Category 1) where they have full control. This matches the project owner's "keep things simple; ignore advanced stuff" guidance.

If a hand-introduced read-only field carries a `[FieldOffset]` attribute, the field's offset is what the user wrote; the bin-packer's sequential calculation for other fields still places them sequentially, which may conflict with the user's explicit offset. The runtime layout reflects whatever the C# compiler produces from the struct as a whole — if conflicts exist, that's the user's problem to resolve.

### 4.7 Memory budget indicator

The header shows `Memory: X / Y B` where X is the current total used and Y is the budget for the current tier:

- If the asset has only the master DTO (no aggregation), Y = `MaxBehaviorParamByteSize` (100 bytes, the BrainBlackboard parameter ceiling).
- If aggregation adds sub-tree DTOs that bin-pack to inline, Y = 100 bytes total.
- If aggregation overflows to heavy, two indicators appear: `Inline: a / 100 B` and `Heavy: b / 928 B`.

The bar visually fills as variables are added. At 80% full, the bar turns amber; at 100%, red. Over 100% inline, the bin-packer (§6) auto-promotes to heavy and the heavy indicator appears.

---

## 4a. File and folder layout conventions

A short standalone section because file layout is a recurring source of confusion. The rules are simple but worth stating in one place.

### 4a.1 Folder structure is purely organizational

Reflection finds tagged types regardless of folder. Use folder structure for human navigation only. A typical project organizes by feature:

```
Hrot.AI.Behaviors/
├── Combat/
│   ├── OrcGuard_BT.cs                ← editor-generated asset (HROT_EDITOR_GENERATED)
│   ├── OrcGuard_BT.Blackboard.cs     ← editor-generated DTO (HROT_EDITOR_GENERATED)
│   ├── OrcGuard_BT.Orchestrators.g.cs ← auto-emitted orchestrators (.g.cs convention)
│   ├── OrcAmbush_BT.cs               ← another asset, same pattern
│   ├── OrcAmbush_BT.Blackboard.cs
│   ├── OrcAmbush_BT.Orchestrators.g.cs
│   ├── CombatActions.cs              ← hand-written actions (no marker = Category 1)
│   └── CombatGuards.cs               ← hand-written guards for HSM combat states
│
├── Patrol/
│   ├── GuardPatrol_HSM.cs            ← editor-generated HSM
│   ├── GuardPatrol_HSM.Blackboard.cs
│   ├── GuardPatrol_HSM.Orchestrators.g.cs
│   ├── PatrolActions.cs              ← hand-written
│   └── EmergencyFlee_BT.cs           ← hand-written behavior (Category 1)
│
└── Shared/
    ├── CombatLocomotionParams.cs     ← hand-written DTO used by multiple editor-managed
    │                                   blackboards via "Promote DTO type to variable"
    ├── FireAtTargetParams.cs         ← hand-written DTO (Category 1)
    └── SharedTargetingActions.cs     ← [SharedAiAction]s used by both BTree and HSM
```

Editor-generated files and hand-written files coexist in the same feature folder. Filename conventions and marker comments do the gatekeeping. Code review sees them side-by-side; humans navigate by feature, not by ownership.

### 4a.2 Filename conventions

The conventions are human-readable hints, not editor logic. The editor's ownership decisions are based on the marker comment (§2.1), not on filename.

- `{AssetName}.cs` — an asset (BTree, HSM, Blueprint). Contains the fluent builder method, the `[XxxDefinition]` attribute-bearing thunk, and the `[XxxLayout]` method. Marker present → editor-owned.
- `{AssetName}.Blackboard.cs` — an editor-owned blackboard DTO. Marker present.
- `{AssetName}.Orchestrators.g.cs` — auto-emitted orchestrator actions for the asset's Subtree-aliasing or Subtree-syncing (§7, §8). The `.g.cs` extension signals "pure source-generator output, skip during code review."
- `{AnyName}.cs` (no marker) — hand-written. Could be a DTO, an action collection, a hand-written behavior, anything.

### 4a.3 The marker comment is the sole authority

When the editor loads a file or decides whether to write a file, it consults the marker comment at the top, not the filename. Possible states:

- **Marker present** → editor owns the file. Regenerates on save.
- **Marker absent** → user owns the file. Editor reflects via the assembly; never writes.

A user can convert a Category 1 file to Category 2 by hand-adding the marker — though they shouldn't, because the editor will then regenerate the file using its in-memory model, which may not include everything the user hand-wrote. The project owner's guidance is to leave this alone: technical users edit C# directly; non-technical users use the panel. Ownership conversion via marker-add is not a supported workflow.

### 4a.4 Hand-written DTOs in the editor's variable-type picker

A struct in any Category 1 .cs file becomes available as a variable type in the editor's Add Variable popup (§4.4) if either:

- The struct is decorated with the `[BlackboardDtoStruct]` attribute (explicit declaration), OR
- The struct is used as the first `ref` parameter of any registered action (auto-detected via the schema exporter, §10).

This is what makes hand-written shared DTOs (like `CombatLocomotionParams` in `Shared/`) work as building blocks for editor-managed blackboards. The technical user hand-writes the DTO once; designers visually embed it via the panel.

### 4a.5 Refactoring across files

Renaming a variable in an editor-managed blackboard rewrites the asset's blackboard file. References in other assets' fluent builders (action bindings) are updated via the refactor service (shared infra §16.2).

Renaming a field in a hand-written DTO (Category 1) is done by the user in their IDE; the editor doesn't initiate it. After the rename, the editor's reference catalog rebuilds and any action bindings now pointing at a missing field name surface as validation errors. The user fixes those by re-binding via the picker.

---
## 5. Recursive aggregation across nested behaviors

### 5.1 What gets aggregated

When an asset references other assets statically — a BTree's `Subtree` node pointing at another BTree, an HSM state hosting an action that's a Blueprint-hosted `AiPrimitive`, an HSM state whose `Activity` action invokes a sub-BTree via an orchestrator action — the master asset's blackboard must contain enough memory for all the parameter DTOs the nested behaviors need.

The editor performs this aggregation **at edit time** by walking statically-resolvable references. The result populates the **Unbound Sub-Tree Requirements** section of the Variables panel (§4.5).

### 5.2 The traversal algorithm

The aggregation service is `BlackboardAggregator`, registered as a singleton in `Hrot.Editor.AiShared.Blackboard`:

```csharp
public interface IBlackboardAggregator
{
    /// <summary>
    /// Walks an asset and its statically-linked descendants. Returns the
    /// flat list of parameter DTO requirements found.
    /// </summary>
    AggregationResult Aggregate(IEditableAsset rootAsset);
}

public sealed record AggregationResult(
    IReadOnlyList<DtoRequirement> Requirements,
    IReadOnlyList<AggregationWarning> Warnings);

public sealed record DtoRequirement(
    Type DtoType,
    string RequiredByPath,    // human-readable: "Shoot_BT → Action#7 (FireAtTarget)"
    Guid RequiringAssetId,
    Guid RequiringElementId);
```

The walker handles each asset kind:

**For a `BehaviorTreeAsset`:**
- Walk all `BTreeEditorNode`s.
- For each Action / Condition / Observer node: look up its `MethodFqn` in the action schema (§10). Emit a `DtoRequirement` for the action's declared DTO type. The requiring path is the node's location in the tree.
- For each Subtree node: resolve its `SubtreeAssetId` via the asset catalog. If resolved, **recurse** into the referenced asset. If unresolved (asset missing or the reference is dynamic), emit an `AggregationWarning` and skip the subtree's requirements.

**For an `HsmAsset`:**
- Walk all `StateNode`s.
- For each state's `OnEntryAction`, `OnExitAction`, `Activity`, `TimerAction`: look up the FQN in the schema. Emit a `DtoRequirement`.
- For each `TransitionNode`'s `GuardFunction` and `ActionFunction`: same lookup.
- For each `GlobalTransitionNode`: same.
- For states that invoke an "execute sub-BTree" orchestrator action: detect the orchestrator action's referenced `SubtreeAssetId` (which is part of the orchestrator's binding, not part of the state) and **recurse** into the sub-asset.

**For a Blueprint-hosted action used in BTree or HSM:**
- The Blueprint already declares its `Params` and `WorkingState` via the Blueprint editor. The schema includes the Blueprint's `ParamsType`. The aggregator emits a requirement for that type. (Blueprint-side authoring of `Params`/`WorkingState` is the Blueprint editor's responsibility, not this DD's — Blueprint already supports it.)

### 5.3 Cycle prevention

Asset references can form cycles in principle (BTree A invokes Subtree B which invokes Subtree A). The walker maintains a `HashSet<Guid>` of visited AssetIds. On encountering a visited asset, it skips and emits an `AggregationWarning` noting the cycle. The cycle isn't a hard error (HSM-style "delegated execution" can have recursive structures); it's a warning the designer can ignore or restructure.

### 5.4 When does the scan run?

The scan runs:

- **Once on asset open** — populates the initial state of the Variables panel.
- **On every asset change** — when the editor receives a `GraphCommand` that adds or removes a node, changes a node's referenced action, or modifies a Subtree's `SubtreeAssetId`. The scan is debounced (200 ms idle); rapid edits coalesce into one scan.
- **On hot reload of any asset in the catalog** — the catalog's `Changed` event triggers re-aggregation on all open assets that might depend on the changed one. (The reference catalog from shared infra §4.3 already tracks this dependency graph.)

The scan cost is proportional to the size of the static tree rooted at the asset. For a 200-node BTree with depth-3 subtree nesting, the scan typically completes in 5–20 ms — well within frame budget.

### 5.5 Resolution against the action schema

For the scan to know each action's required DTO type, it queries an action schema (§10). The schema is populated by reflecting `[BTreeAction]`, `[BTreeCondition]`, `[HsmAction]`, `[HsmGuard]`, `[SharedAiAction]`, `[SharedAiCondition]`, `[SharedAiHeavyAction]` attributes across the loaded assembly. Each registered method's first `ref` parameter type is recorded as the action's `DtoType`.

### 5.6 What the scan produces, visually

The aggregation result becomes the Unbound Sub-Tree Requirements list. The list groups multiple requirements of the same DTO type together when they come from the same sub-tree. Example: if `Shoot_BT` has three action nodes all using `FireAtTargetParams`, the panel shows one row:

```
◇ FireTactics (FireAtTargetParams)
    Required by: [Shoot_BT — 3 nodes]
```

…rather than three separate rows. The designer can bind once and all three node references are satisfied.

When requirements come from different sub-trees with the same DTO type, the panel groups them separately so the designer can choose to alias or split:

```
◇ MoveTactics (MoveToLocationParams)
    Required by: [FlankEnemy_BT — 2 nodes]

◇ MoveTactics (MoveToLocationParams)
    Required by: [Retreat_BT — 1 node]
```

The designer can drag both onto the same defined variable to alias (Approach A), or promote each to its own variable (separate memory slices).

---

## 6. Memory-tier bin-packing

### 6.1 The two tiers

The framework provides two memory tiers for blackboard data:

- **Inline tier:** `BrainBlackboard.BehaviorParameters` — 100 bytes (`MaxBehaviorParamByteSize`) of fast inline memory in the entity's `BrainBlackboard` ECS component. Always present on an AI entity.
- **Heavy tier:** `Blackboard1024.Memory` — 1024 bytes (928 usable after header) in a separate generic component that's only attached when needed. Allocated on-demand by `BehaviorIngressSystem`.

The convention from earlier design rounds: **top-level master parameters always go to the inline tier**; sub-tree aggregated parameters can spill to the heavy tier. The bin-packer implements this convention.

**Role matters, not just size.** The inline-vs-heavy choice above is for **params** (role = *input*): small stays inline, large spills heavy. **Mutable *state* variables** (role = *state*: local / shared working state — `BTree_AiActionParameterBinding_Detailed_Design.md §4.4`) are **always heavy regardless of size**, because they must persist across ticks and be slot-keyed by scope (`Node`/`Behavior`/`Entity`), which the transient inline region cannot host. So a variable's tier is decided by `(role, size)`: `input` → size-driven; `state` → always heavy.

### 6.2 The bin-packing algorithm

When the editor needs to decide where each variable lives, it runs the bin-packer:

```csharp
public sealed class BlackboardBinPacker
{
    public PackResult Pack(
        IReadOnlyList<BlackboardVariable> masterVariables,
        IReadOnlyList<BlackboardVariable> aggregatedVariables);
}

public sealed record PackResult(
    IReadOnlyList<PackedVariable> InlineFields,    // go to BehaviorParameters
    IReadOnlyList<PackedVariable> HeavyFields,     // go to Blackboard1024
    int InlineBytesUsed,
    int InlineBytesAvailable,    // 100
    int HeavyBytesUsed,
    int HeavyBytesAvailable,     // 928
    bool RequiresHeavyComponent,
    IReadOnlyList<PackWarning> Warnings);

public sealed record PackedVariable(
    BlackboardVariable Variable,
    int ByteOffset,
    int ByteSize);
```

The algorithm:

1. **Sort variables by category.** Master variables (declared on the asset itself) come first, then aggregated sub-tree variables.
2. **Master variables always go inline.** This is the convention; the bin-packer enforces it. If the masters alone exceed 100 bytes, that's a hard error: "Master DTO exceeds 100-byte inline budget. Reduce or restructure." The packer surfaces this as a `PackWarning.Error`.
3. **For aggregated variables, try inline first.** Iterate aggregated vars in declared order. For each, if it fits in the remaining inline budget (≤ 100 bytes total), place it inline. Otherwise, place it in heavy.
4. **Compute byte offsets.** For `LayoutKind.Sequential` (default), offsets are sequential within each tier, accounting for C# struct alignment rules (8-byte fields aligned to 8, 4-byte to 4, etc.). The packer uses the same alignment math `Marshal.SizeOf` uses, ensuring runtime layout matches.
5. **Emit the result.** The editor's Generated.cs file emit (§3.3) uses the `InlineFields` list for the master struct; the `HeavyFields` list goes to a separate `{AssetName}.HeavyBlackboard.cs` companion file if `RequiresHeavyComponent` is true.

### 6.3 Heavy-tier promotion

The bin-packer promotes to heavy as a last resort, but the promotion is transparent. The designer sees the Variables panel showing the breakdown:

```
Inline: 78 / 100 B
Heavy:  240 / 928 B
```

…and the editor handles the rest: emitting a second .cs file for the heavy struct, registering the heavy type with the source generator so `BehaviorIngressSystem` knows to provision a `Blackboard1024` component when this behavior is assigned.

The asset's `[BTreeDefinition(...)]` attribute gains a `HeavyDtoType = typeof(OrcGuard_BT_HeavyBlackboard)` argument when the heavy struct is in use. This is what the source generator and runtime pick up.

### 6.4 What happens when variables move tiers

A design hazard: if the inline tier is at 95 bytes and the designer adds a 20-byte variable, that variable spills to heavy. Subsequent aggregated variables might or might not also spill, depending on the packing order. This means **the tier a variable lives in can change as the designer edits**.

This isn't a problem for the kernel (the pointer projection respects whatever tier the source generator emits), but it's worth being explicit to the designer. The panel surfaces tier in each row's metadata:

```
◆ FlankTactics  MoveToLoc… (16 B, inline @ 32)
◆ ExtraData     ExtraParams (64 B, heavy @ 0)    ← spilled to heavy
```

Each variable shows both its size and its tier+offset.

### 6.5 Reordering for tighter packing

The designer can manually reorder variables to keep semantically-related ones together or to pack more efficiently. The panel allows drag-reorder within each tier.

A "Re-pack" toolbar action runs the bin-packer with an optimization pass: it sorts variables to minimize alignment padding within the inline tier, then re-emits. This is offered as a one-shot user action; the editor doesn't auto-repack on every save because byte offsets are part of the source-code-visible state and unexplained reorderings would be annoying in code review.

### 6.6 What about the `BehaviorIngressSystem` parsing path?

`BehaviorIngressSystem` parses incoming JSON parameters into a stack-allocated shadow `BrainBlackboard`, then commits on success. The shadow copy is fixed at 128 bytes (matching `BrainBlackboard`'s component size). The bin-packer's inline tier is bounded at 100 bytes specifically because of this — `BehaviorIngressSystem`'s commit logic preserves the system-level fields at the tail of `BrainBlackboard` (`ExpectedThreatLevel`, `Interrupt_MobilityLost`, etc.).

The bin-packer treats those tail-bytes as off-limits. The editor's panel never offers them as authorable variables. They remain part of the runtime convention, not part of the editor-managed DTO.

The heavy tier has no equivalent shadow-copy mechanism because heavy components are pre-allocated and JSON-parameter-parsing populates them in-place. The 928-byte usable budget accounts for the `Blackboard1024`'s internal header reserved for runtime use.

---

## 7. Approach A — whole-DTO aliasing

### 7.1 What aliasing means

A defined variable of type `T` becomes an alias when more than one sub-tree's parameter requirement of type `T` is bound to it. At runtime, every reference to that variable — whether from the master tree or from any aliasing sub-tree — projects the same byte slice. The sub-trees share live state.

This is the cheapest form of sharing: no copy-down, no copy-up, no orchestrator code. The kernel's pointer projection already does the right thing; the editor's emit just registers two different action-binding slots pointing at the same byte offset.

### 7.2 The visual workflow

The designer creates an alias by dragging an Unbound Sub-Tree Requirement onto an existing Defined Variable of the same DTO type:

1. Designer drags `◇ FireTactics (FireAtTargetParams) — Required by: [Shoot_BT]` from the Unbound section.
2. Hover over `◆ SharedFireState (FireAtTargetParams)` in the Defined section — the row highlights green if the types match, red if they don't.
3. Drop on the green-highlighted row.
4. Unbound row disappears. Defined row's badge updates: `↳ aliased by: Shoot_BT`.
5. If a second sub-tree also requires `FireAtTargetParams`, dragging its unbound requirement onto the same variable adds to the alias: `↳ aliased by: Shoot_BT, Reload_BT`.

Reverse operation: clicking the alias badge's `Reload_BT` entry offers "Remove alias" → the sub-tree's requirement returns to the Unbound section, and the variable's badge updates.

### 7.3 Type-match strictness

The drop target highlights green only if the DTO types are exactly equal. No implicit subtype matching, no field-by-field structural matching. `FireAtTargetParams` aliases only with `FireAtTargetParams`. This is intentional: byte-level memory layout matters; structurally-similar-but-distinct types could produce subtle misalignment bugs.

### 7.4 Explicit promotion is required for non-alias cases

If two sub-trees both require `FireAtTargetParams` and the designer wants them to use *separate* memory (so `Shoot_BT`'s fire state doesn't bleed into `Reload_BT`'s), the designer must promote each unbound requirement to its own variable:

1. Right-click `◇ FireTactics — Required by: [Shoot_BT]` → "Promote to new variable" → creates `◆ ShootFireState`.
2. Right-click `◇ FireTactics — Required by: [Reload_BT]` → "Promote to new variable" → creates `◆ ReloadFireState`.

The two variables now occupy different byte ranges; the kernel projects them as distinct slices. **The editor never auto-merges requirements with the same DTO type or the same field names.** Even if both sub-trees registered their requirement as `FireTactics`, the unbound rows remain visually distinct and the designer must explicitly choose alias or split.

This is the explicit answer to the design-talk concern (line 239): same DTO type and same field name don't imply same semantic — the editor stays out of that judgment.

### 7.5 What the emit looks like

For a master BTree `OrcGuard_BT` with a `SharedFireState` variable aliased by `Shoot_BT` and `Reload_BT`:

```csharp
// In OrcGuard_BT.Blackboard.cs (HROT_EDITOR_GENERATED)
public partial struct OrcGuard_BT_Blackboard
{
    public FireAtTargetParams SharedFireState;
    // ... other master variables
}
```

The asset's `CreateBuilder()` registers the master tree with `<OrcGuard_BT_Blackboard, BTreeContext>`. Both `Shoot_BT` and `Reload_BT` are statically-linked subtrees whose own `[BTreeDefinition]`s declare them generic over `<FireAtTargetParams, BTreeContext>` (their own DTO).

The runtime orchestrator action that ticks the sub-tree (per the existing "subtrees need external orchestration" pattern from FastBTree) projects the master's `SharedFireState` slice and passes it as the sub-tree's blackboard:

```csharp
// In an auto-generated orchestrator action (emitted by the editor)
[BTreeAction(Name = "Orchestrate_Shoot_BT")]
public static NodeStatus Orchestrate_Shoot_BT_Tick(
    ref OrcGuard_BT_Blackboard master,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
{
    // Project the master's SharedFireState as the sub-tree's full blackboard
    ref var subBb = ref Unsafe.As<FireAtTargetParams, FireAtTargetParams>(ref master.SharedFireState);
    return Shoot_BT.GetInterpreter().Tick(ref subBb, ref state, ref ctx);
}
```

The pointer is the same address. No copying. Aliased state.

If the master variable is `SharedFireState` and the sub-tree was originally declared with a different field name (`FireTactics`), the editor's binding metadata records `Shoot_BT → SharedFireState` and emits the orchestrator with that exact projection. The sub-tree never knows the master named it differently.

### 7.6 Aliasing's interaction with bin-packing

The bin-packer (§6) treats an aliased variable as a single allocation. If `SharedFireState` is 20 bytes, the bin-packer reserves 20 bytes once. The aliased-by count doesn't affect memory size, only the number of distinct action-binding slots pointing at the same offset.

### 7.7 Aliasing across parallel HSM regions — hard validator rule

This is a real correctness hazard. If `FlankEnemy_BT` runs in HSM region 0 and `Retreat_BT` runs in HSM region 1, and both are aliased to `SharedMoveState`, the two BTrees tick in the same RTC microstep and race-write the same memory. The result is non-deterministic.

The validator rejects this by default. See §9.

### 7.8 Aliasing across sequential HSM transitions — fine

If `Idle` state's `OnEntry` uses an action with `MoveToLocationParams` and `Alert` state's `OnEntry` uses the same DTO type aliased to the same variable, that's fine — the two states are never active simultaneously, so there's no race. The validator doesn't flag this.

The distinction is whether the aliasing happens across **concurrently-executing** behaviors (parallel regions, parallel composites) or **sequentially-executing** ones (states transitioning over time). Only the former is flagged.

---

## 8. Approach B — field-level synchronization

### 8.1 When you'd use this

Approach A aliases entire DTOs. But sometimes you want to share *one field* of one DTO with *one field* of another DTO. The classic example: the HSM's `SharedTargetEntityId` should propagate to `Shoot_BT`'s `FireTactics.TargetNetworkId` before each tick, and any change the sub-tree makes should propagate back after.

This can't be done with whole-DTO aliasing because the two DTOs aren't the same type. It has to be done with explicit copy operations. The orchestrator action that ticks the sub-tree does the copy.

### 8.2 The visual workflow

Configured from the **Inspector** when a Subtree node (BTree) or an HSM state with an embedded sub-tree action is selected. The Inspector adds a new section: **Parameter Synchronization**.

```
┌──────────────────────────────────────────────────────────┐
│ INSPECTOR — Subtree node: Shoot_BT                        │
├──────────────────────────────────────────────────────────┤
│ Subtree Asset: [ Fire At Target .fdp     ▾ ]              │
│                                                           │
│ ▼ PARAMETER SYNCHRONIZATION                               │
│                                                           │
│   Sub-tree DTO field        Bound to               Sync   │
│   ─────────────────────────────────────────────────────── │
│   TargetNetworkId           [SharedTarget    ▾]   ☑↓ ☐↑   │
│   WeaponSlot                [(none)          ▾]   ☐  ☐    │
│   StatusOut                 [LastFireStatus  ▾]   ☐  ☑↑   │
│   AmmoSpent                 [(none)          ▾]   ☐  ☐    │
│                                                           │
│ ▼ EXECUTION                                               │
│   Visual Id: a3f2-…-9c01                                  │
└──────────────────────────────────────────────────────────┘
```

For each field of the sub-tree's DTO:

- **Bound to** — dropdown of master variables of matching type. `(none)` means the field is not synchronized; whatever value it had before the tick remains. The dropdown is filtered: a field of type `long` shows only `long` master variables; a field of `Vector3` shows only `Vector3` master variables. (For DTO-struct fields nested inside the sub-tree's DTO, the dropdown shows master variables of that exact DTO struct type.)
- **`☑↓` Sync In** — before each sub-tree tick, copy from `master.BoundVariable` into `subDto.field`. Checkbox; off by default.
- **`☑↑` Sync Out** — after each sub-tree tick, copy from `subDto.field` back into `master.BoundVariable`. Checkbox; off by default.
- Both checkboxes can be on simultaneously for bidirectional sync.

### 8.3 What the emit looks like

For each Subtree node with field-level synchronization configured, the editor emits an orchestrator action:

```csharp
// In an auto-generated orchestrator action
[BTreeAction(Name = "Orchestrate_Shoot_BT")]
public static NodeStatus Orchestrate_Shoot_BT_Tick(
    ref OrcGuard_BT_Blackboard master,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
{
    // Project the sub-tree's DTO from a dedicated slice in master memory.
    // The slice was reserved by the bin-packer.
    ref var subDto = ref master.Shoot_BT_FireTactics;

    // Sync In (pre-tick)
    subDto.TargetNetworkId = master.SharedTarget;

    // Tick the sub-tree
    var result = Shoot_BT.GetInterpreter().Tick(ref subDto, ref state, ref ctx);

    // Sync Out (post-tick)
    master.LastFireStatus = subDto.StatusOut;

    return result;
}
```

The orchestrator is auto-generated when any field of the Subtree has Sync In or Sync Out enabled. If a Subtree has zero sync bindings, the editor doesn't generate an orchestrator action — the simple aliased-tick path (§7.5) is used instead.

### 8.4 Sub-tree DTO allocation when no aliasing

If the sub-tree's DTO is *not* aliased via Approach A, the bin-packer reserves a dedicated slice for it in the master's blackboard. The slice's field name is auto-generated: `{SubTreeName}_{SubDtoTypeName}`. For example, `Shoot_BT_FireTactics`. This is the field name the orchestrator references.

The auto-generated field appears in the Variables panel under a "Sub-tree allocations" sub-section, dimmed to indicate it's editor-controlled (the designer doesn't manage it directly — adding or removing a Subtree node adds or removes this allocation).

If the same Subtree node is configured with Approach A aliasing (the whole DTO bound to a master variable), the auto-generated allocation is suppressed and the orchestrator targets the aliased variable instead. The editor handles the switch transparently: configuring whole-DTO aliasing through drag-and-drop removes the per-Subtree allocation; clearing the aliasing restores it.

### 8.5 Type-match strictness

The "Bound to" dropdown shows only master variables whose type exactly matches the sub-DTO field's type. No implicit casts, no struct-to-primitive coercion. If a sub-DTO field is `int` and the master only has `long` variables, the dropdown shows `(none)` only — the designer has to explicitly add an `int` master variable or change the master's existing `long` to `int`.

### 8.6 Combining Approach A and B

The two approaches compose freely. A master can:
- Alias `SharedMoveState : MoveToLocationParams` to both `FlankEnemy_BT` and `Retreat_BT` (Approach A).
- Have `SharedTarget : long` field-synchronized into `Shoot_BT.FireTactics.TargetNetworkId` with both `☑↓` and `☑↑` (Approach B).
- Also have a non-shared `OrcGuard_BT_Blackboard.LocalAmmoCount : int` that only the master tree reads.

The editor generates a single composite `OrcGuard_BT_Blackboard` containing all these fields with their bin-packed offsets. The orchestrator actions perform the Approach B sync copies; the Approach A path needs no orchestrator (direct pointer projection).

### 8.7 Sync direction guarantees

`Sync In` runs **immediately before** the tick — no other code between the copy and the tick. `Sync Out` runs **immediately after** the tick returns — no other code between the tick and the copy. The orchestrator is the only place these copies happen; runtime tooling doesn't insert hooks between them.

If multiple Sync In bindings exist on the same Subtree, they execute in declared field order (the order they appear in the sub-DTO's struct definition). Same for Sync Out. Deterministic ordering.

---

## 9. Cross-region conflict validation

### 9.1 The hazard

HSM parallel composites have regions that execute concurrently within a single RTC microstep. If two regions both write to the same blackboard variable — whether via Approach A whole-DTO aliasing, Approach B sync-out, or both regions hosting sub-trees that alias the same memory — the writes race. The result depends on tick ordering and is not deterministic.

This is the blackboard-variable analog of the `OutputLaneMask` conflict the HSM already detects for `CommandLane` writes. We extend the same validation infrastructure to cover blackboard variables.

### 9.2 The validation rule

For each blackboard variable in the asset:

1. Build the set of *writing actions* — every action method that mutates this variable. For Approach A aliases, all actions of all aliasing sub-trees. For Approach B sync-out, the orchestrator action of the host Subtree node. For non-shared variables, all actions in the master tree that target this variable.
2. For each writing action, determine which states host it — for an HSM, walk every state whose `OnEntry`, `OnExit`, `Activity`, `TimerAction`, transition `ActionFunction`, or sub-tree-hosted action chain reaches this writer.
3. Determine which of those states can be **simultaneously active**. Two states are simultaneously active if they're in different regions of the same parallel composite, OR they're descendants of states meeting the same condition transitively.
4. If any pair of writing actions can be simultaneously active, emit a `CrossRegionBlackboardConflict` diagnostic listing both writers and the variable.

The algorithm walks the HSM's state hierarchy once per variable, building a "which states can write this" set, then checking pairs against the parallel-region relation. Cost: O(states × variables) per validation pass, typically negligible.

### 9.3 The diagnostic and the override

> **⚠ v2.1:** conflict suppressions persist to the **asset JSON**, not the `[*Layout]` method via `.SuppressBlackboardConflict()`. The validation logic is unchanged; only the storage moved. (Same for §12.5 unused-suppressions.) See the REVISION NOTICE.

A `CrossRegionBlackboardConflict` renders as a Warning in the Variables panel:

```
◆ SharedTarget  long  (8 B)
    ⚠ Conflict: written by [FlankEnemy_BT] in region "Locomotion"
              AND by [Retreat_BT] in region "Tactics" — concurrent writes are non-deterministic.
    [ Suppress this conflict ]
```

The default is to surface the warning. A designer who *knows* the race is benign (e.g., both regions write the same constant value, or one region writes deterministically before the other) can click "Suppress this conflict" — this adds a per-conflict suppression entry to the asset's layout method as editor metadata. The diagnostic is no longer surfaced for that specific (variable, writer-pair) tuple.

Suppression is per-pair, not per-variable. A new aliasing relationship on the same variable would surface a fresh diagnostic.

### 9.4 Approach A aliasing across parallel regions — refused by default

The drop-target validator catches the simplest case: if the designer tries to drag an unbound requirement from a sub-tree that's already in one parallel region onto a master variable that's already aliased by another sub-tree in a different region of the same parallel composite, the drop target turns red and shows: "This alias would create concurrent writes across parallel regions of [composite name]. Use 'Configure Aliasing Across Regions' to enable explicitly."

The "explicitly enable" path exists for the cases where the designer genuinely wants the race (synchronized read-mostly access patterns, for example) — they go to the variable's `⋮` menu → "Allow concurrent writes" → checkbox.

### 9.5 Approach B sync-out conflicts

A subtler case: two Subtree nodes in different parallel regions, both with Approach B Sync Out enabled to the same master variable. The same diagnostic fires. The validator scans Sync Out bindings as part of the "writing actions" enumeration.

### 9.6 Read-only access is safe

If a variable is *only read* by both regions and never written, no conflict exists. The validator distinguishes writers from readers by examining the action's method signature: if the first ref parameter is mutated (the action calls `p.X = ...`), it's a writer; if it's only read (`var y = p.X;`), it's a reader.

This requires static analysis of action method bodies. Rather than embedding a full analyzer in the editor, the editor uses a hint mechanism: action authors mark methods with `[BlackboardReadOnly]` or `[BlackboardReadWrite]` attributes on the parameter:

```csharp
[BTreeAction]
public static NodeStatus Action_FireAtTarget(
    [BlackboardReadWrite] ref FireAtTargetParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex);
```

If unannotated, the editor conservatively treats the parameter as read-write (worst case). Annotation lets the validator be more precise.

These attribute additions are optional and can land later; in their absence, the validator is conservative and may surface false-positive warnings. The "Suppress" affordance covers the false positives.

---

## 10. Action-DTO discovery

### 10.1 The problem

The aggregation walker (§5) needs to know, for each action method an asset references, what parameter DTO that method takes. This information lives in the C# method signature (the first `ref` parameter's type). The editor needs to extract it without compiling C# at edit time and without manually maintaining a parallel registry.

The existing `BehaviorRegistry` (BTree) and `HsmActionDispatcher` (HSM) hold method references but not their DTO type metadata — they were designed for runtime dispatch, not editor introspection.

### 10.2 The action schema

A shared service in `Hrot.Editor.AiShared.Blackboard`:

```csharp
public interface IActionSchemaExporter
{
    /// <summary>
    /// Returns the schema entry for an action FQN, or null if not registered.
    /// </summary>
    ActionSchemaEntry? Lookup(string actionFqn);

    /// <summary>Enumerate all known actions, for the action-picker UI.</summary>
    IReadOnlyList<ActionSchemaEntry> All { get; }

    /// <summary>Re-scan the loaded assembly. Called on hot reload.</summary>
    void Rebuild();

    event Action? Changed;
}

public sealed record ActionSchemaEntry(
    string Fqn,                          // "Hrot.Game.Combat.CombatActions.FireAtTarget"
    string ShortName,                    // "FireAtTarget"
    Type DtoType,                        // typeof(FireAtTargetParams) — the first ref parameter
    ActionHosting Hostings,              // BTreeAction | HsmAction | SharedAi | Heavy
    BlackboardAccess ParamAccess,        // ReadOnly | ReadWrite | Unknown
    Type? HeavyDtoType);                 // for [SharedAiHeavyAction]; null otherwise

[Flags]
public enum ActionHosting
{
    BTreeAction      = 1 << 0,
    BTreeCondition   = 1 << 1,
    BTreeObserver    = 1 << 2,
    HsmAction        = 1 << 3,
    HsmGuard         = 1 << 4,
    Heavy            = 1 << 5,
}

public enum BlackboardAccess { ReadOnly, ReadWrite, Unknown }
```

### 10.3 How the schema is populated

The exporter reflects the loaded assembly on editor startup and after every hot reload:

1. Find every static method decorated with `[BTreeAction]`, `[BTreeCondition]`, `[BTreeObserver]`, `[HsmAction]`, `[HsmGuard]`, `[SharedAiAction]`, `[SharedAiCondition]`, `[SharedAiHeavyAction]`, or `[SharedAiHeavyCondition]`.
2. For each, inspect the method signature:
   - Verify the expected shape (first parameter is `ref TValue` for a recognized TValue; subsequent parameters match the `NodeLogicDelegate` or HSM dispatch convention).
   - Extract `TValue` as the `DtoType`.
   - Read the `[BlackboardReadOnly]` / `[BlackboardReadWrite]` annotation on the parameter, if any, for `ParamAccess`. Otherwise `Unknown`.
   - For `[SharedAiHeavyAction]`, the method also has a heavy parameter; extract that as `HeavyDtoType`.
   - Combine the attribute set into the `Hostings` flag value.
3. Build the FQN as `{DeclaringType.FullName}.{Method.Name}`.
4. Index by FQN and by short name. The picker UIs typically search by short name with fuzzy match; the aggregator looks up by FQN (canonical identity).

The cost is one reflection scan per assembly load — sub-second for projects with thousands of action methods.

### 10.4 Blueprint-hosted actions

A Blueprint configured as `AiPrimitive` with `Hostings = [BTreeAction]` registers itself via the Blueprint source generator into `BehaviorRegistry` as a regular `[BTreeAction]`. From the schema exporter's perspective, this is just another registered method — the exporter doesn't care that it's Blueprint-generated.

The `ActionSchemaEntry.DtoType` for a Blueprint-hosted action is the Blueprint's auto-generated `Params` struct type. The Blueprint editor emits this struct visibly (the existing flow); the BTree/HSM editor just sees the resulting type and treats it the same as a hand-written DTO.

This is the seamless interoperability the design-talk transcript references: Blueprints visually define their DTOs; BTree/HSM editors visually aggregate those DTOs; everything flows through the schema.

### 10.5 The schema is the source for pickers, too

The existing `BehaviorHashPicker` (BTree inspector) and `HsmActionPicker` (HSM inspector) currently query `BehaviorRegistry.AllActions` / `HsmActionDispatcher.AllActions` for picker content. Both move to consuming `IActionSchemaExporter.All` filtered by hosting:

- BTree action picker → `All.Where(e => (e.Hostings & ActionHosting.BTreeAction) != 0)`.
- HSM guard picker → `All.Where(e => (e.Hostings & ActionHosting.HsmGuard) != 0)`.

This consolidates picker content with aggregation content into a single source of truth. When the schema changes (hot reload), pickers and aggregations update together.

### 10.6 Cycles between asset references and the schema

A subtle edge case: a Blueprint hosted as a `BTreeAction` declares its `Params` struct. If that struct contains a field of another DTO struct, the aggregator might recurse through nested struct fields. It does not. The aggregator treats the action's declared `DtoType` as atomic — it doesn't decompose the DTO into sub-fields and look for nested action requirements inside it.

Sub-fields of a DTO struct are part of the same memory allocation; they don't need separate aggregation. If a Blueprint's `Params` struct contains a `MoveToLocationParams` field, that's just 16 bytes of move-state inside the Blueprint's DTO — not a separate aggregation requirement.

### 10.7 Schema rebuild on hot reload

The schema rebuilds when `IAssetCatalog.Changed` fires (which fires after every successful hot reload). The Variables panel updates accordingly:

- Variables of types that still exist in the schema: unchanged.
- Variables of types that *no longer exist* (the user removed a DTO struct from the project): flagged as opaque with a "DTO type not found in current assembly" diagnostic. Preserved in the file verbatim until the user resolves.
- New action FQNs added since last reload: appear in pickers immediately. Aggregation re-runs on next debounced scan.

---

## 11. Per-node binding to blackboard variables

### 11.1 The existing model

Per BTH §10.2 and HSH §11.1, each action/condition/guard node in an asset carries an `ExpressionTargetField` string identifying which blackboard variable it's wired to. The Inspector renders a `[BlackboardFieldPicker]` dropdown over the asset's blackboard variables; selecting a variable sets the field.

The BTreeFluentEmitter (BTH §4.2) reads this field and emits the binding lambda: `.Action(bb => bb.SelectedField, MyAction)`.

This DD extends the model in three ways:

### 11.2 The picker filters by type compatibility

The `[BlackboardFieldPicker]` dropdown now consults the action's schema entry to determine which variables are valid. For an action whose `DtoType` is `MoveToLocationParams`, the dropdown shows only variables of type `MoveToLocationParams`. Variables of other types are hidden.

This is a behavior change from the existing implementation, which lists all fields and lets the runtime fail. The new behavior prevents the user from picking incompatible variables in the first place.

If the asset's blackboard has no variable of the action's required type, the dropdown shows `(no compatible variables)` plus a "Promote to new variable" affordance that creates one on the spot.

### 11.3 The picker offers "Promote to new variable"

A common workflow: the designer adds a new action node, the action requires a DTO the master doesn't have, the designer needs to add a new variable. Today, that requires switching to the Variables panel, clicking `+ Add variable`, choosing the type, naming it, then switching back to the Inspector and binding.

The new picker offers an inline "+ Promote to new variable" entry at the top of the dropdown. Clicking it:

1. Opens a small popup with a Name field (the type is already known from the action's schema).
2. The designer enters a name.
3. On confirm: a new variable is added to the master's blackboard, the picker selects it, the binding is set.

One workflow, one popup, no panel-switching. The Variables panel reflects the addition on its next render.

### 11.4 The reference catalog tracks per-node bindings

The shared infra reference catalog (§4.3) gains two new `SubElementKind` entries to handle blackboard authoring:

```csharp
public enum SubElementKind
{
    // ... existing ...
    BlackboardVariable,
    BlackboardField,
}
```

**`BlackboardVariable`** — references to top-level variables in an asset's blackboard. Keyed as `{AssetId}::{VariableName}` (scoped to the asset; same convention as HSM events). Used to track which action/condition/guard nodes bind to which master variables via their `ExpressionTargetField`.

**`BlackboardField`** — references to individual fields *inside* a DTO struct. Keyed as `{DtoTypeFqn}::{FieldName}`. Used to track Approach B sync bindings (§8) from master Subtree nodes into specific fields of sub-tree Params structs, AND references from Blueprint-author UI back to consumers of their Params fields. The key intentionally uses the DTO type FQN rather than a specific subtree's AssetId, so that renaming a field in a hand-written DTO finds all references across every subtree using that DTO in one query.

Both kinds flow through the same refactor service. Renaming a master variable rewrites all `BlackboardVariable` references; renaming a DTO field rewrites all `BlackboardField` references including the serialized field names in `.SubtreeSync(visualId, [field bindings])` layout-method entries across all master orchestrators.

This is what makes "rename a variable" cheap: change the variable's name in the panel → the catalog's `FindReferences` returns all binding nodes → the refactor service rewrites each node's `ExpressionTargetField` → all assets re-emit. The user sees one rename action affecting N nodes; the editor handles the cascade. The same flow applies symmetrically for DTO field renames — including those originating from a Blueprint editor renaming a Params field, which propagates to every master BTree/HSM that was Sync In/Out-binding to it.

### 11.5 Visualizing bindings on the canvas

A small badge appears on each action/condition/guard node showing which variable it's bound to:

```
┌────────────────────────────┐
│ Action: FireAtTarget       │
│ ↳ FireTactics              │
└────────────────────────────┘
```

The badge is hosted as a `NodeAttachment` (from the NodeAttachments extension) with category `Custom`. Clicking the badge selects the bound variable in the Variables panel (writes `ActiveSubSelection`).

Unbound action nodes — those whose `ExpressionTargetField` is null — render the badge with `(unbound)` in red, signaling the validator will fail this asset until the user binds it.

### 11.6 Sub-tree binding versus action binding

A regular action node binds to a master variable. A Subtree node binds differently: it doesn't have a single `ExpressionTargetField` because the sub-tree as a whole has a DTO (its blackboard struct), not just one field.

For Subtree nodes, the inspector's binding section is the Approach B Parameter Synchronization table (§8.2), not a single picker. The "binding" is the set of field-level bindings configured there, plus optionally an Approach A whole-DTO alias chosen via drag-drop in the Variables panel.

---

## 12. Unused-variable diagnostics

### 12.1 The hazard

The variables panel maintains the master's blackboard memory. Designers add variables; they may stop using them later (delete the node that referenced them) without remembering to remove the variable. Over time, the blackboard accumulates dead variables — bytes consumed but never read.

This isn't a runtime correctness issue (dead variables are just unused memory) but it's a cleanliness issue. With the bin-packer (§6) and the 100-byte inline budget, dead variables can push live variables into the heavy tier prematurely.

### 12.2 Detecting unused variables

The editor maintains a reference count per variable. The reference catalog (§11.4) already tracks bindings; the count is `FindReferences(variableKey).Count`.

A variable is **unused** when its reference count is zero. The variable is shown in the Variables panel with the hollow-diamond glyph (`○` per §4.2) and dimmed text. The row's hover-tooltip explains: "Not referenced by any node — consider removing."

### 12.3 The validator surfaces unused variables as Info-level diagnostics

A new diagnostic code in BTreeValidator and HsmValidator:

```csharp
public enum BlackboardDiagnosticCode
{
    UnusedVariable,
    VariableTypeNotFound,        // Schema rebuild dropped the type
    UnboundActionNode,           // Action node with null ExpressionTargetField
    UnboundSubTreeRequirement,   // Subtree has unresolved DTO requirement
    CrossRegionBlackboardConflict, // §9
    InlineMemoryExceeded,        // Master vars exceed 100B
    DuplicateAliasAcrossRegions, // §7.7
}
```

Severity: `UnusedVariable` is Info (the lowest level — designer is informed but not blocked). The other codes are Warning or Error per their severity.

The Info-level diagnostic appears as a small `○` glyph next to the variable's row. The asset's overall diagnostic count (visible in the Asset Browser as a badge per BTH §11.2) includes these.

### 12.4 The "Remove unused" toolbar action

A one-click cleanup affordance in the Variables panel header: `[ Remove unused ]`. Clicking it:

1. Identifies all variables with reference count zero.
2. Shows a confirmation dialog: "Remove N unused variables? They will be removed from the blackboard and the file will be regenerated. This frees X bytes."
3. On confirm, removes them all and triggers a save.

The action is destructive but bounded: only variables with zero references are touched, and the user explicitly opts in. Undo via Ctrl+Z restores them (the command is part of the undo stack like any other variable removal).

### 12.5 The "Suppress unused" affordance

A designer might intentionally keep an unused variable around — for example, a value parsed from JSON that the runtime reads via `BehaviorIngressSystem` even though no editor node references it explicitly. (Some system-level fields are populated by the framework, not by actions.)

For these cases, the variable's `⋮` menu has "Suppress unused warning" → a checkbox-like persistent suppression. Suppressed variables don't appear with the hollow-diamond glyph; they're treated as in-use for the purpose of "Remove unused."

Suppression is persisted in the editor metadata section of the layout method, scoped per variable.

### 12.6 What happens when a variable is removed

When the user removes a variable (via the `⋮` menu, the Remove-unused action, or the file is hand-edited to remove it):

1. The bin-packer re-runs, computing new byte offsets for remaining variables.
2. The emitter regenerates the file. The struct now has fewer fields; remaining fields may have shifted offsets.
3. The asset's hot reload classifier (BTH §14, HSH §16) compares the new structure hash to the previous. The result is **Hard reload** if any variable was removed or any offsets changed — running instances reset because their inline data layout is no longer valid.

The editor warns the designer of the Hard-reload consequence before applying: "Removing this variable will reset N live instances. Continue?"

If the designer also removes the action nodes that were referencing the variable (bringing the reference count to zero before the variable removal), the removal sequence may produce two Hard reloads if not batched. The Remove-unused action is internally a single batch so it produces one reload, not many.

---

## 13. Failure modes and recovery

### 13.1 The four states

> **⚠ v2:** States **B (span-capture fail)** and **C (struct-parse fail)** are **N/A** — there is no source-text parse of an editor-owned `.cs`. A JSON-owned blackboard **always opens** (the data-loss fix). State D is reframed: a build failure means the generated runtime thunks aren't live, **not** that authoring/reopening is blocked. See the REVISION NOTICE.

When the editor loads a Category 2 file, four outcomes are possible. The original DD had a five-level failure ladder built around the three-tier classification (known/tolerated/opaque); the two-category model collapses this to a simpler picture.

**State A — Clean load.** Marker present, file parses, all fields classify cleanly into editor-managed or read-only-passthrough. Variables panel fully functional. The common case.

**State B — Per-field span capture fails.** Marker present, file parses at the struct level, but the source-text scanner can't isolate one specific field's verbatim span — unusual whitespace, comment placement, or a parser bug. Editor surfaces a diagnostic naming the affected field. The whole asset's blackboard panel falls back to **read-only-passthrough for everything** (no field's verbatim span can be trusted to be re-emit-safe). User fixes the file manually; on next reload, classification re-runs.

**State C — Struct parse fails.** Marker present, but the source-text scanner can't even locate the struct declaration — syntax error in the file, malformed top-level structure. Editor falls back to **reflection-only read-only mode**: the panel shows reflected field info (names and types from `FieldInfo`), the user understands they need to fix the file, no save attempted.

**State D — Reflection fails.** Assembly didn't compile, type not found. Editor shows the build error in the panel: "Cannot load blackboard for {AssetName}. Build error: {error}." Nothing else to display.

States B and C are similar — both are "read-only fallback with a diagnostic." They differ in what the panel can display (per-field detail vs. asset-level error). State D is the catastrophic case where there's nothing to fall back to.

### 13.2 The recovery loop

A typical recovery sequence after the user introduces an error:

1. User edits `OrcGuard_BT.Blackboard.cs` adding a new hand-written field with an attribute the editor doesn't recognize.
2. Compile succeeds (the attribute is valid C#, just outside the editor's model). State A: clean load, the new field becomes read-only-passthrough.
3. User saves their edit and continues editing — now they accidentally drop the trailing semicolon.
4. Compile fails. Editor reloads, hits State D. Panel shows the build error.
5. User fixes the syntax in their IDE.
6. MSBuild rebuilds. Editor reloads, returns to State A.

The editor's job during steps 4–5 is to be quiet, accurate, and non-destructive. The Variables panel doesn't try to render bogus state from a stale assembly load. The current panel content is preserved (the user can still see what they had); but no save is attempted.

### 13.3 Save protections

The editor refuses to save when:
- The file is in State C or D. Saving from State C would discard the user's malformed content; saving from State D has no current model to save.
- A pending validation error blocks save (e.g., `InlineMemoryExceeded`, `DuplicateAliasAcrossRegions` without override). The editor explains which.

The editor saves opportunistically when:
- The file is in State A. Editor regenerates editor-managed fields, emits read-only fields verbatim from captured spans.
- The file is in State B. Editor regenerates the whole file from reflection alone, treating every field as if it were editor-managed of its reflected type, omitting attributes, omitting initializers. This is **lossy** — read-only fields lose their attributes/initializers/comments. The editor warns explicitly before applying: "Saving in this state will strip attributes and initializers from N fields. Continue?" Most users will say no and fix the file manually first.

State B's lossy save is the only intentionally-lossy save path. It exists because outright refusing to save would block the designer from making any progress when one field's span capture failed. The user can choose between fixing the file (the right answer) or accepting the loss (the escape hatch).

### 13.4 What about the sub-tree DTO availability for sync metadata?

If a Subtree node is configured with field-level synchronization (§8) and the sub-tree's DTO type later becomes unloadable, the existing sync bindings can't be validated. The editor preserves them in the asset's layout method but shows the Subtree's inspector section with a warning: "Sub-tree DTO not available — sync bindings preserved but not validated."

When the sub-tree DTO becomes loadable again, validation resumes. Bindings that no longer match (the sub-tree's DTO removed a field that was bound) become diagnostics; the user resolves.

---
## 14. Required additions to existing infrastructure

### 14.1 Shared infrastructure additions

Per `AI_Editor_Shared_Infrastructure.md`:

- **§4.3 Reference Catalog** — add two SubElementKinds: `BlackboardVariable` (key `{AssetId}::{VariableName}`) for top-level master-variable bindings, and `BlackboardField` (key `{DtoTypeFqn}::{FieldName}`) for Approach B sync bindings into specific fields of DTO structs. Track references from action/condition/guard nodes' `ExpressionTargetField` (BlackboardVariable) and from Subtree-node Sync In/Out bindings (BlackboardField).
- **§5 EditorSelectionStore** — add `BlackboardVariableSelection(Guid AssetId, string VariableName) : IAssetSubSelection`. The Inspector dispatches on this for variable detail editing.
- **§6 FluentCSharpEmitter** — extend the deterministic-output rules to cover the new blackboard DTO file kind. The `HROT_EDITOR_GENERATED` marker (same as other editor-owned files), per-field emit logic distinguishing editor-managed (regenerated) from read-only-passthrough (emitted verbatim from captured source span).
- **§16 Refactor service** — rename a blackboard variable propagates to all binding nodes; rename a DTO field propagates to all sync bindings across master orchestrators. Both supported by the generic refactor service; requires only the two new `SubElementKind` registrations.

### 14.2 BTree host additions

> **⚠ v2.1:** the editor emitters do **not** write orchestrator/DTO `.cs`. "Emit orchestrator actions" / "emit the blackboard struct" below = produced by the **Roslyn generator / Quick Reload emit core** from JSON, to `obj/`. The editor only serializes sync/alias data into the asset JSON. Applies equally to §14.3 (HSM). See the REVISION NOTICE (v2.1).

Per `BTree_Editor_NodeEditor_Host_Design.md`:

- **§9 Blackboard reflection** — promotes from "reflect a user-authored struct" (read-only) to "manage an editor-managed struct" (full authoring). The schema-builder remains; the Variables panel becomes editable. Hand-written blackboards (no `HROT_EDITOR_GENERATED` marker) continue to work in Category 1 read-only mode for backward compatibility.
- **§10.3 BlackboardFieldPicker** — type-filter the dropdown per §11.2, add the "Promote to new variable" affordance per §11.3.
- **§4 BTreeFluentEmitter** — emit orchestrator actions for Subtree nodes with Approach B sync bindings (§8.3). The orchestrator emit is conditional on whether any Sync In or Sync Out is configured.
- **§3 BehaviorTreeAsset** — add the `IsBlackboardEditorManaged` flag (mirrors the `BlackboardManaged = true` on `[BTreeDefinition]`). Add the per-variable allocation list for the bin-packer.
- **§14 Quick reload classification** — variable removal or offset change is a Hard reload (live instances reset).

### 14.3 HSM host additions

Per `HSM_Editor_NodeEditor_Host_Design.md`:

- **§9 Events** — no change.
- **§11 Facet structs** — extend `TransitionFacet` and `StateFacet` action-picker fields with the Approach B Parameter Synchronization sub-section when the selected action targets a Subtree (or sub-BTree orchestrator).
- **§12 Validation** — extend with the cross-region conflict rules per §9 of this DD.
- **§4 HsmFluentEmitter** — emit orchestrator actions for state-hosted sub-BTrees with sync bindings. Same pattern as BTree's orchestrator emit.
- **§5.1 HsmNodeCatalog** — pickers now consume the shared schema exporter (§10.5).

### 14.4 New types and services

> **⚠ v2:** `BlackboardDtoEmitter` and `BlackboardSourceTextParser` are **obsolete** — the JSON→C# generator emits the struct (+offset thunks) instead. The other services (`IActionSchemaExporter`, `IBlackboardAggregator`, `BlackboardBinPacker`, `BlackboardAuthoringWindow`) remain. See the REVISION NOTICE.

In `Hrot.Editor.AiShared.Blackboard`:

- `IActionSchemaExporter` and its production implementation (§10).
- `IBlackboardAggregator` (§5).
- `BlackboardBinPacker` (§6).
- `BlackboardAuthoringWindow` (§4).
- `BlackboardDtoEmitter` (extending the shared FluentCSharpEmitter) — knows how to emit the partial-struct file with the new marker and per-field emit modes.
- `BlackboardSourceTextParser` — extracts verbatim declarations and classification metadata from a .cs file.

In `Hrot.BTree.Editor.Blackboard` and `Hrot.Hsm.Editor.Blackboard`:

- Subsystem-specific orchestrator emission templates (the actual orchestrator action methods are emitted differently for BTree vs. HSM due to differing kernel APIs).
- Subsystem-specific bin-packer integration (which DTOs allowed where, hot reload coupling).

### 14.5 Kernel-side additions

Three small additive changes to FastBTree / FastHSM (analogous to the kernel additions in earlier phases):

- **`[BlackboardReadOnly]` and `[BlackboardReadWrite]` attributes** — optional annotations on action method parameters indicating access patterns. Live in a shared `Fbt.Annotations` / `Fhsm.Annotations` namespace. Both kernels ignore them at runtime; only the editor's schema exporter reads them. Default behavior (no attribute) is `ReadWrite` for validator conservatism.
- **Heavy DTO type registration via `[BTreeDefinition]` and `[HsmDefinition]`** — add optional `HeavyDtoType = typeof(X)` argument. Source generator wires this into the runtime so `BehaviorIngressSystem` provisions a `Blackboard1024` when required.
- **`[BlackboardDtoStruct]` marker attribute** — optional, on user-defined DTO structs that should appear in the action-type-picker dropdown. The editor's schema exporter uses this to filter "blackboard-usable structs" from all types in the assembly. Without it, the exporter falls back to heuristic (used as the first ref parameter of any registered action method).

### 14.6 No required asset-format changes

> **⚠ v2 — REVERSED:** there *is* an asset-format change. Variable order, subtree sync bindings, and conflict/unused suppressions move from the `[*Layout]` method into **first-class fields of the asset `.btree.json`/`.hsm.json`** (Persistence-Unification schema §5). Variable order = JSON array order = byte offsets. See the REVISION NOTICE.

The `[BTreeLayout]` and `[HsmLayout]` methods gain optional new entries for:
- Variable order (`.Variable(name, position, comment, color?)` — order in this list is the canonical order for emit).
- Per-Subtree Sync bindings (`.SubtreeSync(visualId, [field bindings])`).
- Cross-region conflict suppressions (`.SuppressBlackboardConflict(variableName, writerPairKey)`).
- Per-variable suppression flags (`.SuppressUnusedWarning(variableName)`).

These are additive to the existing layout method schema; existing layout methods load unchanged.

---

## 15. Slice plan

> **⚠ v2 / v2.1 — persistence-coupled tasks superseded by the JSON substrate:** `TASK-BB-1a-04` (source-text parser), `TASK-BB-1a-05` (plain/passthrough classification), `TASK-BB-1b-01` (`BlackboardDtoEmitter` → instead: JSON→C# generator emits the struct + offset thunks to `obj/`), `TASK-BB-1f-05` (suppression persistence → JSON), `TASK-BB-1f-07` State-B/C handling, and the `[*Layout]` order/sync entries. All other tasks (panel, aggregation, bin-pack, aliasing, sync, validation, diagnostics) stand. See the REVISION NOTICE.

The blackboard authoring slots between the existing host Slice 1 (authoring without debug) and Slice 2 (runtime read-only) for both hosts. A new sub-slice **Slice 1.5** brings up the visual blackboard model.

### Slice 1.5a — Action schema and basic Variables panel

- **TASK-BB-1a-01** — `IActionSchemaExporter` with reflection-based population. (§10)
- **TASK-BB-1a-02** — Schema rebuild on `IAssetCatalog.Changed`. (§10.7)
- **TASK-BB-1a-03** — `BlackboardAuthoringWindow` shell registration; read-only mode rendering reflected variables. (§4)
- **TASK-BB-1a-04** — `BlackboardSourceTextParser` for verbatim capture. (§3)
- **TASK-BB-1a-05** — Per-field classification (editor-known / tolerated / opaque). (§3.4)
- **TASK-BB-1a-06** — Picker filtering by action `DtoType` (BTH §10.3 update). (§11.2)

Acceptance: opening an asset shows its reflected blackboard fields in the panel; the BTree/HSM action pickers correctly filter to compatible variables; no editor-side authoring yet (still hand-written DTOs).

### Slice 1.5b — Editor-managed DTO emit + basic add/remove

- **TASK-BB-1b-01** — `BlackboardDtoEmitter` producing the new `HROT_EDITOR_GENERATED` file. (§3.3)
- **TASK-BB-1b-02** — Add Variable workflow + Remove Variable. (§4.3, §4.4)
- **TASK-BB-1b-03** — Variable rename via refactor service integration. (§11.4)
- **TASK-BB-1b-04** — `BlackboardBinPacker` with inline-only mode (no heavy promotion yet). (§6 partial)
- **TASK-BB-1b-05** — Asset-level `BlackboardManaged = true` flag wiring. (§3.1)
- **TASK-BB-1b-06** — Round-trip determinism property tests (RT-1, RT-2). (§3.5)

Acceptance: a designer can create an asset with `BlackboardManaged = true`, visually add variables of all primitive and known DTO types, rename them, remove them, save and reload with byte-identical round-trip on no-op saves.

### Slice 1.5c — Recursive aggregation + heavy tier

- **TASK-BB-1c-01** — `IBlackboardAggregator` for BTree (Subtree recursion). (§5.2)
- **TASK-BB-1c-02** — `IBlackboardAggregator` for HSM (state-action enumeration + sub-BTree recursion). (§5.2)
- **TASK-BB-1c-03** — Unbound Sub-Tree Requirements panel section. (§4.5)
- **TASK-BB-1c-04** — Heavy-tier bin-packing + `Blackboard1024` companion file emit. (§6.3)
- **TASK-BB-1c-05** — Memory budget indicator with tier breakdown. (§4.7)

Acceptance: a master asset with nested subtrees surfaces aggregated requirements in the panel; promoting unbound to a new variable works; aggregations exceeding 100 inline bytes auto-promote to heavy with correct companion-file emit.

### Slice 1.5d — Approach A whole-DTO aliasing

- **TASK-BB-1d-01** — Drag-onto-variable aliasing UX. (§7.2)
- **TASK-BB-1d-02** — Type-match validation on drop. (§7.3)
- **TASK-BB-1d-03** — Orchestrator emit for aliased sub-trees (BTree). (§7.5)
- **TASK-BB-1d-04** — Orchestrator emit for state-hosted sub-BTrees (HSM).
- **TASK-BB-1d-05** — "Aliased by" badge rendering on variable rows. (§7.2, §4.2)

Acceptance: dragging an unbound requirement onto a compatible variable creates an alias; saved file emits a single field with multiple action bindings pointing at it; runtime tick of aliased sub-trees shares memory correctly.

### Slice 1.5e — Approach B field-level synchronization

- **TASK-BB-1e-01** — Inspector Parameter Synchronization sub-panel for Subtree nodes. (§8.2)
- **TASK-BB-1e-02** — Bound-to dropdown with type filtering. (§8.5)
- **TASK-BB-1e-03** — Sync In / Sync Out checkboxes per field.
- **TASK-BB-1e-04** — Orchestrator emit with sync copies. (§8.3)
- **TASK-BB-1e-05** — Per-Subtree DTO allocation when no aliasing. (§8.4)

Acceptance: a Subtree with field-level sync configured produces an orchestrator with the correct Sync In / Sync Out copies surrounding the tick call; combining Approach A and Approach B on the same asset works.

### Slice 1.5f — Validation, diagnostics, and recovery

- **TASK-BB-1f-01** — Cross-region blackboard conflict validator. (§9.2)
- **TASK-BB-1f-02** — Drop-target validator preventing unsafe aliasing across regions. (§9.4)
- **TASK-BB-1f-03** — Unused-variable diagnostic + glyph. (§12.2, §12.3)
- **TASK-BB-1f-04** — Remove unused toolbar action. (§12.4)
- **TASK-BB-1f-05** — Suppression metadata persistence in layout methods. (§9.3, §12.5)
- **TASK-BB-1f-06** — `[BlackboardReadOnly]` / `[BlackboardReadWrite]` annotation handling.
- **TASK-BB-1f-07** — Failure-state handling (§13.1) — read-only-passthrough preservation, State B/C/D fallbacks.

Acceptance: cross-region conflicts surface as warnings with suppression option; unused variables surface as Info diagnostics; the editor handles hand-edited files transparently when all fields parse cleanly (State A), falls back to read-only mode gracefully on per-field or struct-level parse failures (States B and C), and surfaces build errors clearly on reflection failure (State D).

### Slice 1.5g — Blueprint UX parity (Tier A)

The Blueprint editor's existing `Params` / `WorkingState` variable panel adopts the same UX affordances as the new BTree/HSM Variables panel: comments, drag-reorder with warnings, and live memory budget indicators. Implementation strategy: extract the shared affordances into a reusable `VariablesPanelControl` consumed by both editor families.

- **TASK-BB-1g-01** — Extract `VariablesPanelControl` into `Hrot.Editor.AiShared.Blackboard` with configuration flags for single-list-vs-dual-list, aliasing-on-vs-off, and schema source.
- **TASK-BB-1g-02** — Migrate the BTree/HSM Variables panel to consume `VariablesPanelControl` with `single-list + aliasing-on` configuration.
- **TASK-BB-1g-03** — Migrate the Blueprint variable panel to consume `VariablesPanelControl` with `dual-list (Params + WorkingState) + aliasing-off` configuration. Each sub-section has its own memory budget indicator (Params → 100B inline; WorkingState → selected BlackboardTier).
- **TASK-BB-1g-04** — Extend Blueprint JSON schema with per-variable `Comment` field. `AiPrimitiveEmitter` reads this and emits `///` XML doc blocks above generated fields.
- **TASK-BB-1g-05** — Extend Blueprint JSON schema with explicit `VariableOrder` array. `AiPrimitiveEmitter` honors this order when emitting fields.
- **TASK-BB-1g-06** — Wire Blueprint Params field rename through the `BlackboardField` reference catalog kind. Renames in the Blueprint editor propagate to Approach B sync bindings in all master BTree/HSM orchestrators via the refactor service.

Acceptance: a designer authoring a Blueprint's variables can add comments, drag-reorder fields, and watch the memory budget fill up — the same affordances available in the BTree/HSM blackboard panel. Renaming a Params field in the Blueprint editor automatically updates all master BTree/HSM Sync In/Out bindings referencing that field.

This slice does *not* migrate Blueprint to the C# source-of-truth model. Blueprint continues to use JSON as its variable definition source; the source generator continues to emit the `Params` and `WorkingState` C# from JSON. The deferred Tier B work (Blueprint adopting Category 1/2 ownership) is gated on three trigger conditions tracked in §17:

1. **Explicit user demand** — programmers hitting hard blockers because they need `unsafe` fixed buffers, `[MarshalAs]` interop, or specific `[FieldOffset]` alignments directly in Blueprint Params/WorkingState.
2. **Blueprint orchestration capability** — if Blueprints gain the ability to orchestrate sub-behaviors (embedding child DTOs), they would need the aggregation/aliasing machinery and JSON would become insufficient as the source of truth.
3. **Opportunistic refactor window** — a future milestone that touches the Blueprint editor or compiler pipeline for other reasons, amortizing the migration cost.

### Out of scope for the blackboard slice

The following are explicit non-goals for Slice 1.5, deferred to later polish or future versions:

- Live-edit of blackboard values on running entities (covered by Slice 3 host "Make Editable" toggle — orthogonal to this DD).
- Automatic DTO-type inference from same-named fields across unrelated DTOs.
- Multi-asset shared blackboard variables (across-asset aliasing — would require a new top-level shared blackboard concept).
- Visual data-pin wiring of DTO fields on the canvas (the Option-2 NodeEditor graph variant from the design-talk; the Inspector-based approach is sufficient for v1).
- Generation of separate per-region heavy blackboards (today, all heavy fields share one `Blackboard1024`).
- **Tier B Blueprint adoption** — migrating Blueprint to the C# Category 1/2 ownership model is deferred indefinitely per the trigger conditions in Slice 1.5g and §17.

---

## 16. Test strategy

### 16.1 Unit tests

In `Hrot.Editor.AiShared.Blackboard.Tests`:

- **`ActionSchemaExporterTests`** — given a fixture assembly with `[BTreeAction]` / `[HsmAction]` / `[SharedAiAction]` methods, verify FQN keys, DTO type extraction, hosting flag composition, `[BlackboardReadOnly]` annotation reading.
- **`BlackboardAggregatorTests`** — fixtures with nested Subtree references, mixed BTree/HSM, cycle detection, unresolved subtree handling.
- **`BlackboardBinPackerTests`** — alignment correctness, master-vars-always-inline rule, heavy tier promotion threshold, mixed master+aggregated packing.
- **`BlackboardDtoEmitterTests`** — round-trip determinism property tests (RT-1 through RT-4 from §3.5).
- **`BlackboardSourceTextParserTests`** — classification of fixture .cs files into editor-managed / read-only-passthrough categories.
- **`CrossRegionConflictDetectorTests`** — fixtures with parallel HSM states writing to same variable, sync-out collisions, parallel-but-non-aliased cases (no conflict).
- **`UnusedVariableDetectorTests`** — reference counting via the catalog; suppression handling.

### 16.2 Integration tests

In `Hrot.Editor.AiShared.IntegrationTests`:

- **Add a variable via panel, save, reload** — verify the variable persists and is type-correct.
- **Hand-edit add a non-plain field, reload** — verify the field appears in the panel as read-only-passthrough with the 🔒 glyph, save preserves it byte-identically.
- **Recursive aggregation across 3-level subtree nesting** — verify all DTOs surface in the panel.
- **Approach A alias across two sub-trees** — verify the orchestrator emit produces shared pointer access.
- **Approach B sync on one Subtree** — verify orchestrator emits Sync In/Out copies in the right order.
- **Cross-region conflict** — fixture with parallel HSM states aliasing the same variable; verify the validator surfaces the diagnostic and the drop-target validator refuses the drag.
- **Schema rebuild on hot reload** — change a DTO struct in a fixture assembly; verify the panel updates and the picker shows new types.
- **Failure-mode ladder** — synthetic .cs files at each level (1-5); verify the editor falls to the right level and authoring degrades correctly.

### 16.3 Visual / manual tests

A "Blackboard Authoring" scenario in the test harness:

- An asset with mixed editor-managed variables and a hand-edited read-only-passthrough field.
- An asset with three nested sub-BTrees aggregating into one master.
- An asset with Approach A aliasing across two sub-trees plus Approach B sync to a third.
- An HSM with parallel regions hosting sub-BTrees, intentionally triggering a cross-region conflict.

Manual checklist:
- Variables panel renders correctly with all glyph states (◆ ○ ⚠ ◇).
- Drag-and-drop aliasing produces correct visual feedback (green/red highlight).
- "Promote to new variable" popup works from both the panel and the picker.
- Memory budget indicator updates live as variables are added; tier promotion fires at the right threshold.
- Cross-region conflict diagnostic appears with the correct affected nodes; clicking "Suppress" persists.
- Hand-editing the .cs file outside the editor reloads correctly; read-only-passthrough fields render with the 🔒 glyph and preserve verbatim on next save.

---

## 17. Open questions

A handful of items remain open or are worth flagging for future review. Earlier drafts of this DD had a longer list; most were resolved in design review with the project owner and the architect.

1. **Bin-packing overflow handling.** Currently the panel surfaces `InlineMemoryExceeded` as a persistent Warning when master vars exceed 100 bytes, but doesn't refuse the variable addition. The designer can break the layout temporarily while restructuring. Confirm this is the right friction level; the alternative is to refuse adds that would overflow.

2. **`[BlackboardDtoStruct]` attribute discoverability.** §10 lists this as an optional attribute on user-defined DTO structs. The schema exporter auto-detects DTO types via action signatures; the attribute is for pre-declared types not yet referenced. Default behavior — auto-detect plus optional attribute — was confirmed in design review. The attribute is documented but not required.

3. **Approach A aliasing UI for cross-region cases.** §9.4 refuses the drag-drop alias across parallel regions by default, with an explicit "Allow concurrent writes" override. This conservative default was confirmed by the project owner. If user research shows experienced designers find it patronizing, the default could flip with a per-asset preference; for now, refusal stands.

4. **Variable rename refactor flow.** Variable rename auto-applies to all referencing nodes silently (no preview pane). Confirmed in design review. Behaves the same as the existing inline rename for action FQNs — references update transparently via the refactor service.

5. **Exposing bin-packer offsets in the panel.** Today the panel can optionally show "(tier @ offset)" annotations per variable. Confirmed default: hidden behind an "Advanced view" toggle. Useful for debugging but visually noisy for non-technical designers.

6. **Hot reload of a variable rename.** Currently classified as Soft reload (no structure change; field names change but offsets are stable; runtime registries reference by hash, not name). Worth confirming with the hot-reload classifier maintainer that field rename doesn't perturb StructureHash, since the .cs source text changes.

Items resolved by design review (no longer open):

- The `HROT_EDITOR_MANAGED` collaborative-ownership marker is dropped in favor of the simpler two-category model with `HROT_EDITOR_GENERATED` as the sole marker. (§2.4)
- The three-tier field classification (known / tolerated / opaque) collapses into the two-state classification (editor-managed / read-only-passthrough). (§3.4, §3.5)
- Hand-edit support for in-file field attributes is removed in favor of composition: users hand-write whole DTOs as Category 1 files and the editor embeds them. (§4a.4)
- Comments are first-class via the panel and emit as `///` XML doc blocks. Comments on read-only-passthrough fields are displayed but not panel-editable. (§4.3, §3.5)
- Read-only-passthrough fields can be reordered via the panel with a strong warning per reorder. Cannot be deleted via the panel; deletion requires hand-edit. (§3.6)
- Editor-side "Reset editor management" or "Take editor ownership" buttons are not implemented. Ownership conversions happen via direct .cs edits by technical users. (§4a.3)
- LayoutKind toggle is removed. Editor-owned files are always `LayoutKind.Sequential`. Users needing explicit layout hand-write the whole DTO as Category 1. (§4.6)
- **Blueprint AiPrimitive UX parity.** The Blueprint editor adopts the UX-only parts of the BTree/HSM blackboard authoring (comments, drag-reorder with warnings, memory budget indicators) via the shared `VariablesPanelControl`. Blueprint Params field-level renames propagate through the refactor service via the new `SubElementKind.BlackboardField` reference kind. The full C# source-of-truth ownership model (Category 1/2) is deferred for Blueprint indefinitely. See Slice 1.5g and §11.4.
- **Tier B Blueprint adoption triggers.** Migrating Blueprint to the C# Category 1/2 ownership model is deferred indefinitely. Three trigger conditions are pinned for revisiting: (a) explicit user demand for hand-introduced exotic fields in Blueprint Params/WorkingState; (b) Blueprint gaining orchestration capability (would require aggregation/aliasing); (c) opportunistic refactor of the Blueprint editor or compiler pipeline. See Slice 1.5g.

---
