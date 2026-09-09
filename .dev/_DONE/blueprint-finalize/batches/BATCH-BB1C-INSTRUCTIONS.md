# BATCH-BB1C: B-3 wiring/test completion + node-owned presentation & lifecycle + tooltip
**Tasks:** Corrective Task 0 (B-3 completion), B-4, B-5   **Phase:** 7 (BB1)   **Est:** ~14h
**Dependencies:** BB1A (`07f56325`), BB1B (`cb07da24`).

> **TOOLING:** Do NOT use the codebase-memory MCP — it HANGS in this environment. Use Grep / Glob / Read for all
> code exploration. (Overrides the "codebase-memory first" guidance in DEV-GUIDE_claude.md.)

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/_DONE/blueprint-finalize/reviews/BATCH-BB1B-REVIEW.md` — **fix Issues 1 & 2 first** (Corrective Task 0).
3. `docs/blueprints/Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` — §3.5 (auto-delete + re-pack
   lifecycle), §3.6 (presentation: dimmed "Node-Owned Allocations" group), §3.7 (exclude from Approach-A alias
   drop-targets; diagnostics/bin-pack interactions), §4.1 (static-vs-dynamic tooltip), §6 B-4/B-5.
4. Code you build on: `InspectorWindow.cs` (B-3 panel from BB1B), `PerspectiveWorkspaceRegistrar.cs:167`
   (constructs InspectorWindow — the wiring site), `VariablesPanelControl.cs` + `VariableViewModel`
   (`Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs:18`), `BTreeCommandSink.cs`
   (`ApplyRemoveNodes` → `_asset.RemoveNode`), `HsmCommandSink.cs` (HSM node/state delete path), the asset
   models (`BehaviorTreeAsset.cs`/`HsmAsset.cs`), and `BlackboardBinPacker.cs` (re-pack).

> Complete tasks in sequence; do NOT start the next until the current is implemented, tested, and the FULL suite
> passes. Work autonomously — run the suite, fix root causes, do not stop for permission; only stop on a genuine
> breaking design flaw.

---

## Corrective Task 0: complete B-3 (live wiring + headless edit-service test)
**From BB1B review Issues 1 & 2.**
1. **Wire** `expressionTargetFieldAccessor` in `PerspectiveWorkspaceRegistrar` (the `new InspectorWindow(...)` at
   ~:167) so the running editor's "Static Parameters" panel actually appears. The accessor takes the current
   facet object and returns its `ExpressionTargetField` (reflect the property, mirroring how the facet
   dispatchers read it — handle BTree Action/Condition facets + HSM transition/global-transition facets; return
   null for facet types without the field). Mark the asset dirty on default edits (already done in the panel via
   `UpdateVariableDefaultValueJson` → `MarkDirty`).
2. **Extract** the hydrate/serialize logic from `InspectorWindow.DrawClientArea` (BB1B lines ~326-399) into a
   headless-testable helper (e.g. `DefaultValueAuthoringSession` / a static `DefaultValueAuthoring` helper):
   hydrate an instance from `DefaultValueJson` (or default), open the StructEdit edit-service, and serialize the
   committed instance back to JSON. The Inspector calls the helper; the helper is unit-testable without ImGui.
3. **Test (headless):** drive the **real** StructEdit edit-service over a DTO that has an **enum** field (and a
   primitive): hydrate from a `DefaultValueJson`, edit a field via the edit document, commit → serialize →
   assert the JSON carries the new value (enum persisted per the ENUM-NAME convention), and re-hydrate
   round-trips. Plus: a headless test that the wired `expressionTargetFieldAccessor` returns the bound var name
   for a BTree Action facet and an HSM transition facet.

---

## Task 1: Node-owned variable presentation + lifecycle (B-4)
**Goal (spec §3.5–§3.7):** keep the panel clean and the auto var node-local; no orphans.

**Do:**
1. **Expose `IsAutoManaged` on `VariableViewModel`** (`BlackboardAuthoringWindow.cs:18`) and populate it from the
   `BlackboardVariableEntry`/asset variables wherever the view models are built (BTree + HSM window VM builders).
2. **Presentation (`VariablesPanelControl.cs`):** filter `IsAutoManaged==true` entries OUT of the main "Defined
   Variables" table and render them in a **dimmed, read-only "Node-Owned Allocations" sub-group** (a collapsing
   header or a styled section; reuse the existing `PushStyleVar(Alpha,…)` dimming pattern). Auto vars must not be
   renamable/removable from this group (read-only).
3. **Exclude from Approach-A alias drop-targets (§3.7):** in the alias drag-drop target logic (`DrawTable`,
   ~lines 289-328), do NOT accept an alias drop onto an `IsAutoManaged` variable row (UI filter only).
4. **Lifecycle — auto-delete + re-pack (§3.5):** when an action/condition node (BTree) / transition (HSM) that
   owns an `_auto_{id}` variable is **deleted**, the command sink (`BTreeCommandSink.ApplyRemoveNodes` /
   `HsmCommandSink` delete path) must remove that node-owned variable from the blackboard and trigger a re-pack.
   Resolve the owned var from the deleted node's `ExpressionTargetField` AND the `IsAutoManaged` flag AND the
   `_auto_{VisualId:N}` naming (only delete a var that is auto-managed and owned by THIS node — never a shared
   var). Re-pack via the same path the existing add/remove-variable flow uses (find it — likely the asset's
   variable-mutation → bin-pack trigger).

**Tests required (headless):**
- A blackboard with a shared var + an `IsAutoManaged` var → the panel section model exposes the auto var in the
  node-owned group and NOT in the main list (assert via the headless section/VM split, not ImGui).
- Alias drop onto an `IsAutoManaged` row is rejected; onto a normal row of matching type is accepted (assert the
  drop-acceptance predicate headlessly — extract it if needed).
- Delete the owning Action node (BTree) → its `_auto_` var is removed from the asset AND a re-pack ran (assert
  the var is gone and the pack/offsets updated). Repeat for HSM. Deleting a node that owns NO auto var, or whose
  `ExpressionTargetField` points at a SHARED var, must NOT delete that var.
- Unused-variable diagnostic does not flag a node-owned var while its node lives (§3.7); after node delete it's
  gone (no orphan).

---

## Task 2: Static-vs-dynamic tooltip (B-5)
**Goal (spec §4.1):** prevent designer surprise about timing.
**Do:** add a one-line Inspector tooltip on the param-binding row (the `[BlackboardFieldPicker]` /
"Static Parameters" area): "BTree/HSM static value = applied once at behavior assignment; bind a variable for
live/dynamic values." This is visual — keep the string in a const so it's assertable.
**Test:** assert the tooltip const text exists / is returned by the helper that supplies it (no ImGui needed).

---

## Success Criteria
- [ ] CT0: B-3 panel live-wired in the composition root + the StructEdit→JSON authoring path covered by a real
      headless edit-service test (enum round-trip). B-3 fully complete.
- [ ] B-4: node-owned vars shown dimmed/read-only in a separate group, excluded from alias drop-targets,
      auto-deleted + re-packed when the owning node is deleted (BTree + HSM), no diagnostic false-positives.
- [ ] B-5: static-vs-dynamic tooltip present (const asserted).
- [ ] Full suite green with the Stability filter (0 failed, 0 new); no warnings; report at
      `reports/BATCH-BB1C-REPORT.md`.

## Report Requirements (`reports/BATCH-BB1C-REPORT.md`)
How the accessor was wired + the extracted authoring helper; how you resolve "the var owned by THIS node" for
auto-delete (and how you avoid deleting shared vars); how re-pack is triggered; the panel section split; edge
cases; exact test-run counts; suggested commit message. Do NOT ask comprehension questions.

## Hard rules
- Grep/Glob/Read only (codebase-memory hangs). Byte-stability: do not perturb existing goldens/snapshots.
- Tests verify real behavior/values (real edit-service, real delete→repack, real drop predicate) — not
  string-presence or "object exists". If you broke the impl, the test must fail.
- Run the FULL affected suites (filtered per `.dev/_DONE/test-health/README.md`); fix root causes; do not stop for
  permission. Affected: Hrot.BTree.Editor.Tests, Hrot.Hsm.Editor.Tests, Hrot.Editor.AiShared.Tests,
  Hrot.AiEditor.Persistence.Tests.
