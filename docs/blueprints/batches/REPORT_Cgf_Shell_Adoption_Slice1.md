<!--STATUS
state: LIVE
doc-type: batch report (ephemeral — the durable record is the DESIGN)
updated: 2026-08-25
current-answer: the whole file. ⛔ It carries NO design content: the as-built lives in
  docs/DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md §9 (obligation ⑤), and this report POINTS there.
-->
# REPORT — **cgf==editor slice 1: CGF adopts the AiShared shell** *(backend/CGF lane)*

> 📌 **Dispatch `df8efa938`** · started-marker `3f78c905d` · **ids allocated `CE-001`…`CE-010`** *(rule 5;
> new tracker **Area L**, so the `BP-`/`HN-`/`TM-`/`ST-` partitions are untouched)*.
> 📄 **The design is the record:**
> [`DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md`](../../DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md)
> — **§9 is new and is the as-built.**

## 1. ⭐⭐⭐ THE RESULT, IN ONE MEASUREMENT

📐 **`--mode all` went from 14 to 23 published panel kinds.** The ten it gained —
`blackboard-authoring · my-blueprint · variables · watch · ai-breakpoints · graph-canvas · details ·
runtime-inspector · diagnostics · bookmarks` — are the AiShared windows the editor has always had, now
constructed by `CgfSubsystem.BuildAiShell` and registered under the **BTree · HSM · Blueprint** asset
perspectives. ⛔ **Nothing inside `Hrot.Editor.AiShared` was modified.**

⭐ **Acceptance, per design §6:** `graph-canvas`, `my-blueprint` and `watch` are **`SAME` per `PanelKind`**,
editor vs `--mode all`, asserted by a rail that names them.

## 2. ⭐ OBLIGATION ③ — **the diagrams vs what was built**

> §3 carries **9 classes**, §4 carries **1 sequence**. ⭐ **The build MATCHES both** — every box is
> constructed in `BuildAiShell` and the method runs in the order §4 draws.

⚠ **Five deviations, none of which moves a box or an arrow.** All argued in **§9.2–§9.6** *(obligation ⑤ —
the design was updated, ⛔ not just this report)*:

| # | deviation | where |
|---|---|---|
| ① | ⛔⛔ **item ③ asked for capability-manifest cells that do not exist in that shape** — the manifest's availability layer is MEASURED, and its own doc says *"the known-absent BASELINE lives in the HARNESS, not here."* ⇒ executed as **nine deletions from `EditorOnlyKinds`** | §9.2 · `CE-005` |
| ② | **no `Scenario` registrar** — 📐 the editor has none either *(it gives Scenario a bare `PerspectiveWorkspace`)*; three registrars, not four | §9.3 · `CE-001` |
| ③ | **the per-perspective nulls**, each with a measured reason *(incl. `EntityPicker`: `IMapPickService` lives in `Hrot.ExCon`, which CGF does not reference)* | §9.4 · `CE-004` |
| ④ | 🔴 **`my-blueprint` was served by TWO DIFFERENT CLASSES on the two hosts** — found by measurement, fixed by construction | §9.5 · `CE-006` |
| ⑤ | **three newly-shared kinds still differ**, each DECLARED with its measured reason | §9.6 · `CE-007` |

## 3. ⭐⭐ THE RAILS, AND EACH SHOWN RED

📐 **Revert probe: the `BuildAiShell(windowManager)` call commented out, rebuilt, suite re-run.**
⇒ **4 of 10 red**, each with its own diagnostic:

| rail | its red message under the probe |
|---|---|
| ⭐⭐⭐ `The_asset_panels_are_the_same_on_both_hosts` | *"--mode all did not publish [graph-canvas, my-blueprint, watch]"* |
| `The_cluster_offers_the_asset_perspectives` | *"--mode all does not offer the 'BTree' perspective"* |
| `The_ported_kinds_are_really_published_by_the_cluster` | *"kind(s) […10 named…] were removed from the known-absent baseline but --mode all does not publish them"* |
| `The_two_modes_agree_on_every_shared_panel_kind` *(existing)* | undeclared editor-only kinds |

⭐ **The `my-blueprint` half was shown red WITHOUT a probe** — 📐 the first conformance run, before the two
Blueprint windows were registered, reported it `DIFFERENT` on the real tree
*(`$.emptyReason: golden="No blueprint open." actual="No asset selected." | $.sections: length 7 vs 0`)*.

⚠ **Two goldens added, deliberately** — `ai_canvas_blueprint`, `ai_watch_blueprint`. 📌 The conformance
rail compares the two hosts **to each other**, so it stays green if BOTH regress — and after this slice
both render those panels from the **same AiShared classes**, which makes an identical regression the
LIKELY shape. ⭐ `Every_golden_in_the_budget_is_paired_with_assertions` caught the first cut *(goldens with
no assertions)*, so each carries its D7 pairing case.

## 4. GATES *(rule 8 contract)*

| # | gate | command | result | `--no-build`? | delta vs `df8efa938` |
|---|---|---|---|---|---|
| 1 | solution build | `dotnet build IOS-IG-SimHost.sln --no-restore` | ✅ **0 errors**, 52 warnings | n/a | unchanged |
| 2 | ⭐⭐⭐ **conformance (the acceptance vehicle)** | `scripts/run-system-tests.sh --no-build ClusterConformanceRails` | ✅ **10 / 0** | yes | **baseline 7 / 0** ⇒ +3 rails, all green |
| 3 | ⭐ **T0 baseline, run BEFORE any edit** | same, at the dispatch sha | ✅ **7 / 0** | no *(built)* | — |
| 4 | panel goldens | `scripts/run-system-tests.sh --no-build PanelGoldenRails` | ✅ **19 / 0** | yes | 17 → 19 *(the two new goldens + their two pairing cases; the 6→8 budget row)* |
| 5 | ⭐ **the revert probe** | probe applied, rebuilt, `ClusterConformanceRails` | 🔴 **4 / 10 red, as designed** | yes | — |
| 6 | full system suite | `scripts/run-system-tests.sh --no-build` | *(see §4b)* | yes | |
| 7 | `Hrot.ClusterRunner.Tests` | `dotnet test … --no-build` | ⚠ **271 pass / 2 fail** | yes | **both PRE-EXISTING — proven by diff, not by rebuild** *(below)* |
| 8 | `Hrot.ClusterRunner.Integration.Tests` | `dotnet test … --no-build` | *(see §4b)* | yes | |
| 9 | `tracker-counts.py --check` | | ✅ **OK — open 102 / done 346** | n/a | unchanged *(the `CE-` rows are not `BP-`, so they do not move the counts — Area L says so)* |
| 10 | `rulings-check.py` | | ✅ **24 / 24** | n/a | ⚠ 4 staleness WARNs, all pre-existing *(`.claude/CLAUDE.md`, `DataBreakpointManager.cs`, `DESIGN_Headless_Testability.md`, `SOLUTION-OVERVIEW.md`)* |
| 11 | `design-digest.py --check` | | ✅ **clean** — 81 docs carry STATUS; every buildable design carries both diagrams | n/a | unchanged |
| 12 | `mermaid-check.mjs` | | ⚠ **SKIPPED — no npm/node in this container.** ⛔ Stated, not implied: **no Mermaid block was added or edited** *(§9 is tables and prose; §3/§4's diagrams are untouched)*, so nothing new is unparsed | n/a | — |

### ⭐⭐ Row 7 — **the two reds, proven pre-existing WITHOUT the A/B rebuild dance**

`DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` and
`…D003_Predicate_True_AllowsUpdateAndDraw`, both
`InvalidCastException: D003NoOpDrawBuilder → DebugPrimitiveBuffer` thrown inside
`DataDrivenGizmoSystem.Execute` *(`DataDrivenGizmoSystem.cs:314`)* — ⭐ **a test double that no longer
satisfies a cast in production code.**

📐 **The proof:** `git diff --name-only df8efa938..HEAD` names **7 files**, and neither
`DataDrivenGizmoSystem.cs` nor `DataDrivenGizmoPredicateTests.cs` is among them — ⛔ nor is anything they
reference. ⇒ ⭐ **this batch cannot have caused them**, and establishing that cost one `git diff` instead
of a worktree checkout + full rebuild.

### ⭐⭐⭐ Row 8 (rule 8, row 8) — **the integration suite this cross-cutting change must name**

⭐ This slice changes what a **cluster node publishes over MCP**, so the suite that would break if the
invariant broke is the **conformance suite itself** *(`ClusterConformanceRails`, run against a real
`--mode all` cluster of five nodes)* — ⭐ **it is row 2, and it is the acceptance vehicle, not a
side-check.** ⚠ `Hrot.ClusterRunner.Integration.Tests` carries the **pre-existing DDS-allocator crash**
that makes it un-gateable *(named as such by the previous batch)*; its result is reported in §4b with that
caveat rather than allowed to stand in for "verified".

## 4b. ⏳ RESULTS PENDING AT THE TIME OF WRITING

*(filled in below before the batch closes — ⛔ a row left blank is what sends the coordinator to the
terminal.)*

## 5. ⭐ IDS ALLOCATED *(rule 5)*

**`CE-001` … `CE-010`**, in the tracker's **new Area L — cgf == editor**.
✅ Done: `CE-001` shell · `CE-002` hoisted edit service · `CE-003` the two clock signals · `CE-004` the
passed/null decisions · `CE-005` item ③'s deviation · `CE-006` the `my-blueprint` two-class finding ·
`CE-007` the three declared divergences · `CE-008` the rails + goldens.
⚠ Open: **`CE-009`** *(CGF's `AssetCatalog` is EMPTY — the shell is real but every window can only show its
empty state; indexing is a design question colliding with ruling 67)* · **`CE-010`** *(the `_gizmo` frame is
still editor-only — `BP-487`'s wiring, which its own text hands to whoever owns cross-host)*.

## 6. ⛔ WHAT THIS SLICE DID **NOT** DO

⭐ Named so nobody reads §1's measurement as more than it is:

| | |
|---|---|
| ⛔ **asset editing / hot-reload writes on CGF** | later slice *(design §1)* |
| ⛔ **map / entity parity** | Axis B of the gap map |
| ⛔ **anything that would populate those windows** | ⚠ **this is the honest limit** — `CE-009`. The shell is constructed and asserted; there is no asset catalog behind it, and no debug-API endpoint that opens an AI asset *(`MX-013`)* ⇒ the panels publish their empty state on **both** hosts, which is what makes them comparable and also what bounds the claim |
| ⛔ **`Hrot.Editor.AiShared`** | untouched, by construction — the freeze owner is the variable-model lane |
