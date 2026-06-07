# BF-BATCH-DIAGFAIL-REBUILD: Fail-loud on dropped exec successors + editor rebuild regenerates codegen
**Tasks:** FAILLOUD (compiler diagnostic), REBUILDREFRESH (editor tooling)   **Phase:** Blueprint correctness/tooling   **Est:** ~9h
**Dependencies:** builds on BF-BATCH-EXECFANOUT (BP1411 added there); independent of SEQ1.

## Background — two real bugs hit while testing
A user wired a graph through a **SequenceNode** and the generated `Tick` came out **empty** — the counter never
incremented — with **zero compiler diagnostics** and **stale generated files on disk**. Root causes (both verified):

1. **Silent dropped exec successors (FAILLOUD).** `Stage5_Schedule.ScheduleBlock`
   (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`) walks the exec chain
   via `GetSingleExecSuccessor` (line ~1330), which **returns null for any node with ≠1 exec-out pin** and for links
   whose target node id doesn't resolve. When `succ is null` the loop just sets an `IrTerm_FallThrough` and returns
   (the `EventEntryNode` case ~line 237-245 and the `default` case ~line 280-291). So if a node **has outgoing exec
   links that were not followed** (e.g. a `SequenceNode` with `Then0`/`Then1` linked — Sequence has no scheduler case
   and falls into `default`), the chain **dead-ends silently** and downstream nodes vanish from the emitted code. No
   error, no warning. (SEQ1, a separate batch, will add real Sequence scheduling; THIS task makes the *silent drop*
   loud so any unscheduled exec link is caught.)

2. **Editor "Full Rebuild" leaves generated code stale (REBUILDREFRESH).** `FullRebuildService.TriggerAsync`
   (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/FullRebuildService.cs`) shells out to an **incremental**
   `dotnet build`. The blueprint generator consumes `*.bp.json` as `AdditionalFiles`
   (`Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj:71`), but an incremental build that sees **only a
   `.bp.json` change** does **not** re-run `CoreCompile`/the source generator — so `obj/GeneratedFiles/.../X_*.g.cs`
   stays stale and the runtime keeps running old code. Verified: a plain `dotnet build` after editing the `.bp.json`
   was a no-op; `dotnet build --no-incremental` regenerated the file. The user saved, clicked Full Rebuild, saw
   "0 errors", but the generated code never updated.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE.md` — your working contract (report format, test-quality bar, autonomy, definition of done).
2. This file. Root-cause sites are cited with file:line above.
3. `.dev/blueprint-finalize/reports/BF-BATCH-EXECFANOUT-REPORT.md` — sibling diagnostic (BP1411) you'll mirror for FAILLOUD's diagnostic-code choice and Stage-2/Stage-5 patterns.

## Tasks
Complete tasks in sequence; do NOT start REBUILDREFRESH until FAILLOUD is implemented, its tests are written, and the
full `Hrot.Blueprints.Tests` suite passes.

### Task 1: FAILLOUD — diagnostic when exec successors are dropped — files: `Stage5_Schedule.cs` (UPDATE) + `Compiler/Diagnostics/DiagnosticCodes.cs` (UPDATE)
- In `ScheduleBlock`, at **both** points where the loop sets `IrTerm_FallThrough` because the exec successor is null
  (the `EventEntryNode` case and the `default` case), add a check: **if the node has one or more outgoing exec links
  in `_graph.Links` that were not scheduled** (i.e. `succ is null` but
  `graph.Links.Any(l => l.FromNodeId == node.Id && <pin is an exec-out pin of node>)`), emit a **diagnostic** before
  falling through. Factor this into one helper (e.g. `private void ReportDroppedExecSuccessors(Node node, ...)`) and
  call it from both sites to avoid duplication.
  - The check must distinguish a **legitimate chain end** (a node with no outgoing exec link — fall through silently,
    NO diagnostic) from a **silent drop** (outgoing exec link(s) present but not followed — diagnostic). Only the
    latter fires.
- Add a new diagnostic constant to `DiagnosticCodes` — pick the **next free** code after `BP1411` in the structural
  range (confirm unused by grepping the solution; suggested `BP1412`). Suggested name/text:
  `BP1412` — *"Exec output of node '{nodeId}' ({NodeType}) has {n} outgoing link(s) that the scheduler did not follow; those successors are dropped from the generated code. (A node type with multiple exec-out pins, e.g. Sequence, is not yet schedulable, or a link references an unresolved pin.)"*
- **Severity: Error** (consistent with BP1411; the silent drop produces fundamentally wrong runtime behaviour). If you
  find a strong reason it must be a Warning, document it in the report and default to Error.
- Do **not** implement Sequence scheduling here (that's SEQ1). FAILLOUD only makes the existing silent drop loud.
- **Verification gate = the unit test suite, NOT building the AI.Behaviors `.bp.json` assets.** Some `.bp.json` files
  under `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/` are user **experiment files in flux** (e.g. `Counting.bp.json`
  may contain an unsupported Sequence and is *expected* to trip BP1412 — that is correct, not a regression). Validate
  FAILLOUD with in-memory graph tests; do not gate on compiling those assets.
**Tests required** (`Hrot.Blueprints.Tests`, Stage5 schedule test area — see existing `Stage5_ScheduleTests`):
- **Dropped-successor fires:** build a graph `EventEntry --exec--> Sequence`, with `Sequence.Then0 --> Return1` and
  `Sequence.Then1 --> Return2` (pins populated). Run the schedule/compile and assert **BP1412 Error** is emitted
  (the Sequence's linked branches are dropped today). *(When SEQ1 lands it will change this expectation — that is
  SEQ1's responsibility; reference this test there.)*
- **Unresolved-link fires (stable):** `EventEntry`'s exec-out is linked to a `ToNodeId` that is **not present** in the
  graph → `succ` is null but an outgoing exec link exists → assert **BP1412**.
- **Legitimate chain-end: no diagnostic:** `EventEntry --exec--> Return` (normal) → assert **no BP1412**. Also a node
  with no outgoing exec link at the end of a chain → no BP1412.
- Assert the message contains the offending node id + node type name (locatability).

### Task 2: REBUILDREFRESH — editor Full Rebuild regenerates codegen on a `.bp.json`-only change — file: `FullRebuildService.cs` (UPDATE) + possibly `Hrot.AI.Behaviors.csproj` / a `.targets` (UPDATE), driven by your diagnosis
- **First, REPRODUCE and DIAGNOSE (do not guess the fix):**
  1. Edit a **non-recipe** `.bp.json` under `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/` in a way that changes the
     emitted code (e.g. change a Literal value), without touching any `.cs`.
  2. Run the **same command the editor runs** — `dotnet build` (incremental) with the target that
     `FullRebuildService` uses (see `buildTarget` passed at `EditorSubsystem.cs:2526` — determine its actual value).
  3. Inspect `Hrot/Subsystems/Hrot.AI.Behaviors/obj/GeneratedFiles/Hrot.Blueprints.Generators/.../<Asset>_*.g.cs` —
     confirm it did NOT update. Then confirm `dotnet build --no-incremental` (or `-t:Rebuild`) DOES update it.
  4. Determine the **actual** root cause: MSBuild up-to-date / `FastUpToDateCheck` not treating the `AdditionalFiles`
     change as a `CoreCompile` input, vs. Roslyn generator-driver caching, vs. the build server (VBCSCompiler)
     reusing a cached generator state. State your evidence in the report.
- **Then fix it** so that editing a `.bp.json` and triggering the editor's Full Rebuild path regenerates the affected
  `.g.cs` **without** the user needing `--no-incremental`. Choose the least-invasive correct fix based on the
  diagnosis, e.g.:
  - csproj/targets: ensure `*.bp.json` `AdditionalFiles` are declared as `CoreCompile` inputs / `UpToDateCheckInput`
    so a change invalidates the compile (preferred if the root cause is the up-to-date check); **or**
  - `FullRebuildService`: pass the specific project and a flag/target that forces the generator to re-run on
    AdditionalFile changes (e.g. an explicit incremental-correct invocation). Avoid a blanket `--no-incremental`/
    `-t:Rebuild` of the whole solution unless nothing else works — if you must, scope it to the AI.Behaviors project
    and document the build-time cost.
  - Do **not** silently swallow build failures; `FullRebuildService` must keep surfacing the exit code + output.
- **Acceptance (scripted repro, not an xUnit test — build tooling):** add a small repro script under
  `.dev/blueprint-finalize/` (PowerShell or `dotnet script`) that: snapshots a target `.g.cs`, edits a temp
  non-recipe `.bp.json` (or a fixture), runs the editor's build command, and asserts the `.g.cs` changed. Document how
  to run it in the report. If a `FullRebuildService` code change is made, add/extend a unit test for any pure logic
  you introduce (e.g. argument construction), but the end-to-end proof is the scripted repro.
- **Note for the agent:** recipes (`Blueprints\Recipes\*.bp.json`) are **excluded** from compilation by design
  (csproj line 71 `Exclude`), so they are Content templates, not compiled assets — don't "fix" that.

## Success Criteria
- [ ] FAILLOUD: a node with unfollowed outgoing exec links emits **BP1412 (Error)** with node id + type; legitimate
      chain-ends do not; unresolved-target links do. Proven by in-memory Stage5 tests.
- [ ] REBUILDREFRESH: diagnosis documented with evidence; editing a `.bp.json` then running the editor's Full Rebuild
      path regenerates the affected `.g.cs` without `--no-incremental`; proven by the scripted repro.
- [ ] `Hrot.Blueprints.Tests` full suite passes (4 known pre-existing failures may remain: 2 CRLF snapshot, 1 locale
      decimal, 1 zero-alloc — confirm count unchanged); no new warnings.
- [ ] Report at `.dev/blueprint-finalize/reports/BF-BATCH-DIAGFAIL-REBUILD-REPORT.md`, every section filled.

## Report Requirements
Per `DEV-GUIDE.md`: implementation summary per task; the BP1412 **code + severity** chosen and why it was free; the
**REBUILDREFRESH root-cause diagnosis with concrete evidence** (what command did/didn't regenerate, why); deviations
(WHAT/WHY/BENEFIT/RISK); **actual test-run counts** (total + the new scenarios, not "all pass"); the scripted-repro
command + its output; weak points spotted; suggested one-line commit message. Do NOT ask comprehension questions.

## Autonomy
Finish in one go: implement, run the full `Hrot.Blueprints.Tests` suite, fix root causes until green, then write the
report. Do not stop for permission. Never swallow an error or fall back to a dead path to pass a test — fail early and
loud. Only stop on a genuine breaking design flaw (document it and stop). Note: `Counting.bp.json` tripping BP1412 is
EXPECTED (it is a broken experiment file) — do not edit user experiment assets to make a build pass.
