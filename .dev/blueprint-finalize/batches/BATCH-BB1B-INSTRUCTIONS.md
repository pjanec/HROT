# BATCH-BB1B: Promote→bind completion + StructEdit authoring of the bound variable's default
**Tasks:** Corrective Task 0 (B-2 binding), B-3   **Phase:** 7 (BB1)   **Est:** ~12h
**Dependencies:** BATCH-BB1A (committed `07f56325`), SE1 (InspectorWindow StructEdit), SE2 (per-asset picker drawers).

> **TOOLING:** Do NOT use the codebase-memory MCP — it HANGS in this environment. Use Grep / Glob / Read for all
> code exploration. (Overrides the "codebase-memory first" guidance in DEV-GUIDE_claude.md.)

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (autonomy; test quality; report).
2. `.dev/blueprint-finalize/reviews/BATCH-BB1A-REVIEW.md` — **fix Issue 1 first** (Corrective Task 0 below).
3. `docs/blueprints/Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` — §2.3 (static params = the bound
   variable's `DefaultValueJson`, edited via StructEdit), §3.3 (Promote binds), §4 (runtime path), §6 B-3.
4. `.dev/blueprint-finalize/reports/SE1-REPORT.md` — how `InspectorWindow` drives the StructEdit
   `IComponentEditService` facet render loop (the surface B-3 reuses).
5. The BB1A code you build on: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreePickerDrawers.cs`,
   `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmPickerDrawers.cs` (the `Promote`/`PromoteRequested`/
   `*FacetFqnContext` members), and the facet apply paths (`HsmFacetDispatcher.ApplyTransitionFacet`/
   `ApplyGlobalTransitionFacet`; the BTree facet apply path — find it via Grep for where `BTreeActionFacet`/
   `BTreeConditionFacet` is written back to the model).

> Complete tasks in sequence; do NOT start B-3 until Corrective Task 0 is implemented, its tests written, and
> ALL tests (including prior batches') pass. Work autonomously — run the full suite, fix root causes to
> completion, do not stop for permission; only stop on a genuine breaking design flaw.

---

## Corrective Task 0: Promote must BIND `ExpressionTargetField` (completes B-2)
**Problem (BB1A Issue 1):** `Promote()` creates the `_auto_{id}` variable but does NOT set the node/facet's
`ExpressionTargetField`, and `PromoteRequested` is observed by no consumer. Per spec §3.3/§6 B-2, Promote must
**create AND bind**, in one gesture, and it must actually work in the running editor.

**Files:** `BTreePickerDrawers.cs`, `HsmPickerDrawers.cs` (drawers + `*FacetFqnContext`), the facet apply paths,
`Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` (live wiring), `EditorSubsystem.cs` if the wiring
must be registered there.

**Do:**
1. Thread the **owning node's VisualId/StableId** into the facet context (alongside `CurrentActionFqn`) so the
   drawer can promote the currently-selected node without the caller passing an id. Set it in the same mapper
   `GetFacet`/`GetTransitionFacet` site that already sets the FQN.
2. Make the Promote gesture **create the variable and set the field value** so the binding persists through the
   normal StructEdit write-back → `ApplyFacet` path (i.e. after Promote, the facet's `ExpressionTargetField`
   equals the new `_auto_` name and that reaches the model). Choose the cleanest mechanism (drawer `DrawInput`
   promote-branch sets `value = newName; return true;` after creating the var, OR InspectorWindow observes
   `PromoteRequested`, calls `Promote(...)`, and applies the binding). Document the choice.
3. **Live wiring:** ensure the running editor path actually invokes this — `InspectorWindow` (or the facet
   edit-service consumer) must observe `PromoteRequested`/the promote result and apply the binding + mark the
   asset dirty. If a piece is genuinely only verifiable in the running editor, isolate it behind a headless-
   testable seam and note what remains for visual review — do NOT leave a dead flag.

**Tests required (headless):**
- After a promote on an Action node (BTree) / transition (HSM): the new `_auto_{id}` var exists AND the
  node/facet's `ExpressionTargetField` equals that name AND it survives a model→DTO→model round-trip. (Drive the
  real apply path, not a hand-set field.)
- Promote when a variable is already bound → rebinds to the new auto var (or is a no-op if already the auto var;
  pick and assert the defined behavior).
- The `PromoteRequested` flag has a real consumer (assert the seam that consumes it produces the binding).

---

## Task 1: StructEdit authoring of the bound variable's default (B-3)
**Goal (spec §2.3, §4, §6 B-3):** author an action's **static parameters** by editing the bound variable's
`DefaultValueJson` through the SE1 StructEdit surface (DTO fields → enum combos, vectors, FixedString, etc.),
writing the edited values back to `DefaultValueJson`. Works for both node-owned (`_auto_`) and shared variables.

**Files:** `InspectorWindow.cs` / the SE1 `IComponentEditService` facet wiring; the bound-variable lookup
(`BehaviorTreeAsset`/`HsmAsset` blackboard vars by name); the variable's `DefaultValueJson` read/write seam
(`BlackboardVariableEntry` currently has no DefaultValueJson — it lives on `BlackboardVariableDto`; determine
how the editor model carries/exposes a variable's default, and extend the model if needed so the editor can
read+write it). Reuse the existing StructEdit reflection drawer set (enums already render as combos per SE1).

**Do:**
1. Given the action node's bound variable (via `ExpressionTargetField`), resolve its DTO type (the variable's
   `FieldType`/`DtoType`) and build a StructEdit `EditDocument`/edit-service over an instance hydrated from the
   variable's current `DefaultValueJson` (empty/default instance when null).
2. Render that document in the Inspector under the action facet (reuse the SE1 render loop); on edit, serialize
   the instance back to the variable's `DefaultValueJson`.
3. Ensure the editor model can persist `DefaultValueJson` for a variable (extend `BlackboardVariableEntry` +
   the asset model + mappers if the round-trip doesn't already carry it — note `BlackboardVariableDto` already
   has `DefaultValueJson`, so wire the editor side to match). Keep byte-stability (null/empty omitted).

**Tests required (headless):**
- Build the edit service over a DTO type, set a field (incl. an **enum** field — assert it round-trips by name
  or int per the existing ENUM-NAME convention) → serialize → the variable's `DefaultValueJson` contains the new
  value; deserialize → the value is present. Use the REAL StructEdit edit-service + REAL JSON, not a hand-built
  string.
- A node-owned (`_auto_`) variable and a shared variable both accept default edits (same path).
- `DefaultValueJson` round-trips through the asset model→DTO→model (extend BB1A's round-trip test pattern).
- Default-null variable → editing produces a non-null `DefaultValueJson`; an untouched variable keeps null
  (byte-stability).

---

## Success Criteria
- [ ] Corrective Task 0: Promote creates AND binds `ExpressionTargetField` with a real live consumer; binding
      round-trips; B-2 is now fully complete.
- [ ] B-3: the bound variable's `DefaultValueJson` is authored via the real StructEdit surface (enums as combos),
      persists, and round-trips; works for node-owned and shared vars.
- [ ] Full suite green with the Stability filter (0 failed, 0 new); no warnings; report submitted to
      `reports/BATCH-BB1B-REPORT.md`.

## Report Requirements (`reports/BATCH-BB1B-REPORT.md`)
The chosen Promote-binding mechanism + how it's live-wired (and what, if anything, remains for visual review);
how the editor model carries `DefaultValueJson`; how you hydrated/serialized the DTO instance for StructEdit;
enum-default handling; edge cases; exact test-run counts; suggested commit message. Do NOT ask comprehension
questions.

## Hard rules
- Grep/Glob/Read only (codebase-memory hangs). Byte-stability: new/edited persisted fields default-omitted; do
  not perturb existing goldens/snapshots — document any byte-stability handling.
- Tests verify real behavior/values via real edit-service + real JSON + real apply path — not string-presence or
  "object exists". If you broke the impl, the test must fail.
- Run the FULL affected suites (filtered per `.dev/test-health/README.md`); fix root causes; do not stop for
  permission. Affected projects include Hrot.BTree.Editor.Tests, Hrot.Hsm.Editor.Tests, Hrot.Editor.AiShared.Tests,
  Hrot.AiEditor.Persistence.Tests (run all your changes touch).
