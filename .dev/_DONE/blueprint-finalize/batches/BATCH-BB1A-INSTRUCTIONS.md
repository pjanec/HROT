# BATCH-BB1A: Type-filtered binding picker + Promote-to-node-owned-variable
**Tasks:** B-1, B-2   **Phase:** 7 (BB1 — Action-parameter authoring & node-owned variables)   **Est:** ~14h
**Dependencies:** SE1, SE2, FIX-A, HSM-TRANS (all DONE). Builds on the BTree/HSM facet Inspector.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (autonomy, test quality, report).
2. `docs/blueprints/Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` — **the spec.** §2 (whole-DTO
   binding, type-filtered picker), §3 (node-owned/auto-managed variables, `IsAutoManaged`, naming, creation),
   §6 checklist items **B-1** and **B-2**. Do NOT re-derive — implement to this doc.
3. `.dev/_DONE/blueprint-finalize/TASK-DETAIL.md` §"B-1" and §"B-2".

> **TOOLING:** Do NOT use the codebase-memory MCP — it HANGS in this environment. Use Grep / Glob / Read for all
> code exploration. (This overrides the "codebase-memory first" guidance in DEV-GUIDE_claude.md for this batch.)

## Context — current state (verified by the Lead via Grep/Read; confirm the same way)
- `IActionSchemaExporter` (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`) exposes
  `ActionSchemaEntry.DtoType` per action FQN — this is the type to filter by.
- `BlackboardFieldPickerAttribute.GetCompatibleVariables(actionFqn, vars, exporter)`
  (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BlackboardFieldPickerAttribute.cs`) **already implements the
  filtering logic and is unit-tested** (`...BTree.Editor.Tests/Inspector/BlackboardFieldPickerAttributeTests.cs`).
  The gap is that the **live drawer does not call it**: `BlackboardFieldPickerDrawer.GetItems()`
  (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreePickerDrawers.cs`) returns **all** variable names with no
  action-FQN context and no exporter.
- **HSM has no blackboard-field picker drawer** — `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmPickerDrawers.cs`
  has action/guard/state/event pickers only. The HSM action/guard facet carries `ExpressionTargetField` (see
  `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`).
- Persisted model: `BlackboardVariableDto` (`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs`)
  and any HSM blackboard DTO; editor model record `BlackboardVariableEntry`
  (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs`); panel row `VariableViewModel`.
  **None carry `IsAutoManaged` yet.**
- Asset add-variable seam: `IBlackboardManagedAsset.AddVariable(BlackboardVariableEntry)`
  (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs`), implemented by
  `BehaviorTreeAsset` and `HsmAsset`. Mappers: `BehaviorTreeAssetMapper.cs`, `HsmAssetMapper.cs`.
- The unbound-requirement "Promote to new variable" stub in `VariablesPanelControl.cs:201` is the **Approach-A**
  promote (sub-tree aliasing) — a DIFFERENT feature. Do not conflate. BB1 Promote lives at the action-node
  binding picker.

> Complete tasks in sequence; do NOT start Task 2 until Task 1's implementation is done, its tests are written,
> and ALL tests (including prior batches') pass. Work autonomously — run the full suite, fix root causes to
> completion, do not stop for permission; only stop on a genuine breaking design flaw.

---

## Task 1: Type-filtered binding picker (B-1)
**Files:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreePickerDrawers.cs`,
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmPickerDrawers.cs`, the facet mappers
(`BTreeFacetMapper.cs`, `HsmFacetDispatcher.cs`) and facet structs as needed, plus the drawer-factory wiring
(`BTreePickerDrawerFactory.BuildDrawers` and the HSM equivalent) and `EditorSubsystem` registration if the
exporter must be threaded in.

**Goal (spec §2.2, §6 B-1):** the `[BlackboardFieldPicker]` dropdown on an action/condition/guard node's
`ExpressionTargetField` shows **only blackboard variables whose CLR type matches the action's `DtoType`**, looked
up by the node's action FQN via `IActionSchemaExporter`. When none are compatible, the dropdown shows
`BlackboardFieldPickerAttribute.NoCompatibleVariablesDisplay` ("(no compatible variables)") and surfaces the
Promote affordance (the actual Promote action is Task 2; here, wire the entry point + empty-state).

**Do:**
1. Give the picker drawer access to (a) the active asset's blackboard variables **with their CLR types** (not
   just names) and (b) the `IActionSchemaExporter`, and (c) the **action FQN of the node being edited**. The FQN
   is on the same facet as the `ExpressionTargetField` (BTree: `BTreeActionPayload.MethodFqn` /
   `BTreeConditionPayload.MethodFqn`; HSM: the action/guard method FQN field). Resolve it at draw time from the
   `EditNode`'s owning facet instance — study how `StructEdit`/`EditNode` exposes the owning object, and how the
   facet is built in the mapper, then thread the FQN (a `Func<string?>` accessor or facet-scoped drawer is
   acceptable — pick the cleanest; document the choice in the report).
2. Drawer `GetItems()` (and the HSM equivalent) must return
   `BlackboardFieldPickerAttribute.GetCompatibleVariables(actionFqn, entries, exporter)` — i.e. the real filtered
   list — NOT all names. Build the `IReadOnlyList<BlackboardVariableEntry>` from the asset's variables (Name +
   FieldType).
3. Create the **HSM** blackboard-field picker drawer (mirror `BlackboardFieldPickerDrawer`) and register it in the
   HSM picker-drawer factory so HSM action/guard facets get the same filtered picker.
4. Empty state: when the filtered list is empty, render the `NoCompatibleVariablesDisplay` text and the Promote
   entry point (button/inline menu item). Promote's behavior is Task 2.

**Tests required (headless, `...BTree.Editor.Tests` + `...Hsm.Editor.Tests`):**
- Drive the **real drawer** (not just the static helper): construct the drawer for an asset whose blackboard has
  vars of types T and U, with a node whose action FQN resolves to `DtoType=T` in a stub exporter → `GetItems()`
  returns only the T vars. Repeat for HSM.
- Action FQN that the exporter doesn't know → returns all vars (documented fallback).
- No compatible vars → `GetItems()` empty AND the drawer reports the no-compatible state (assert the state/flag
  your empty-state logic exposes headlessly, not an ImGui call).
- Action FQN accessor resolves correctly from a facet built by the real mapper for an Action node and a
  Condition node (BTree) and an action/guard (HSM).

---

## Task 2: Promote to new node-owned variable + `IsAutoManaged` (B-2)
**Files:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs`,
`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs` (+ the HSM blackboard DTO),
`BehaviorTreeAssetMapper.cs` / `HsmAssetMapper.cs`, the `VariableViewModel`, the asset models
(`BehaviorTreeAsset.cs`, `HsmAsset.cs`), and the picker Promote entry point from Task 1.

**Goal (spec §3.2–§3.4, §6 B-2):** "+ Promote to new variable" creates a correctly-typed, **node-owned**
variable, binds the node's `ExpressionTargetField` to it, and persists `IsAutoManaged=true`.

**Do:**
1. Add **`bool IsAutoManaged`** to: `BlackboardVariableDto` (persisted; default `false`; keep byte/JSON
   back-compat — absent in old JSON ⇒ false) AND `BlackboardVariableEntry` (editor record). Carry it through the
   mappers (DTO↔model) in BOTH directions, and expose it on `VariableViewModel` (read-only) for B-4 later.
   Everything downstream of JSON (generator, bin-packer, `ParseParamsDelegate`) ignores it — do NOT add awareness
   there.
2. Implement Promote at the picker: create a variable named **`_auto_{VisualId:N}`** (BTree) /
   **`_auto_{StableId:N}`** (HSM) of the action's `DtoType` (from the exporter), `IsAutoManaged=true`, via the
   asset's add-variable seam; then set the node's `ExpressionTargetField` to that name. Use the owning node's
   VisualId/StableId for the name (uniqueness + save/reload stability per §3.4). If the action has no resolvable
   `DtoType`, Promote must be unavailable (no untyped auto var) — fail loud, don't create a garbage var.
3. The new var must round-trip through JSON with `IsAutoManaged=true`.

**Tests required (headless):**
- `IsAutoManaged` round-trips: build an asset (model) with an auto var → map to DTO → serialize → deserialize →
  map back → `IsAutoManaged==true`; and a non-auto var stays `false`. Do this for BTree AND HSM.
- **Back-compat:** deserialize a `BlackboardVariableDto` JSON **without** the `IsAutoManaged` property → defaults
  to `false` (assert via real `JsonSerializer`, not by setting the field).
- Promote on an Action node with `DtoType=T`: asserts (a) a new variable exists named `_auto_{VisualId:N}` of
  CLR type T with `IsAutoManaged==true`, AND (b) the node's `ExpressionTargetField` now equals that name. Repeat
  HSM with `StableId`.
- Promote twice on two different nodes → two distinct `_auto_` names (uniqueness).
- Promote when `DtoType` is unresolvable → no variable created, binding unchanged (assert the guard).

---

## Success Criteria
- [ ] B-1: live BTree **and** HSM blackboard-field pickers return only `DtoType`-compatible variables (real
      drawer path tested), with a `(no compatible variables)` empty state + Promote entry point.
- [ ] B-2: `IsAutoManaged` persisted on DTO + model + ViewModel, round-trips (BTree+HSM) and is back-compat;
      Promote creates a bound `_auto_{id}` node-owned var of the action's DtoType.
- [ ] Full suite green with the documented Stability filter (0 failed, 0 new); no warnings; report submitted.

## Report Requirements (answer in `reports/BATCH-BB1A-REPORT.md`)
How you threaded the action FQN into the picker (the chosen mechanism + why); any facet/EditNode-structure
surprises; HSM-vs-BTree divergences; whether the HSM blackboard DTO is shared with BTree or separate; edge cases
found; the exact test-run counts; a suggested commit message. Do NOT ask comprehension questions.

## Hard rules
- Use Grep/Glob/Read for exploration (codebase-memory MCP hangs — do not call it). Projection/byte-stability: do not perturb existing goldens/snapshots;
  if a BTree/HSM JSON byte-stability test trips on the new (default-false, omittable) property, prefer
  `[JsonIgnore(WhenWritingDefault)]`-style omission so unchanged assets serialize identically — document it.
- Tests verify real behavior/values, not string-presence or "object exists". Drive the real drawer + real
  JSON round-trip. If you broke the impl, the test must fail.
- Run the FULL suite (filtered per `.dev/_DONE/test-health/README.md`); fix root causes; do not stop for permission.
