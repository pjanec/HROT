# BF-BATCH-EXECFANOUT: Single-successor exec-out enforcement + loud diagnostic
**Tasks:** EXEC1 (compiler diagnostic), EXEC2 (editor 1:1 exec-out)   **Phase:** Blueprint correctness   **Est:** ~7h
**Dependencies:** none (independent of BB1; touches the exec-link model only)

## Background — the bug this fixes
A user wired the **Tick** EventEntry's single exec-OUT pin to **two** successors: the count-increment chain
(SetVariable→Return) **and** a `DemoSharedAction` ChannelCommand node (a non-channel `[SharedAiAction]`). Result:
the generated code (`Count3_AE86DF64_Bp.g.cs`) emitted **only** the count chain; the DemoSharedAction node produced
**zero** code, with no error or warning.

Root cause (verified):
- The scheduler models exec-out as strictly **1:1**. `Stage5_Schedule.GetSingleExecSuccessor`
  (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs:1330`) takes the node's
  single exec-out pin and does `_graph.Links.FirstOrDefault(... FromPinId == execOut)` — the **first** link wins, any
  others are **silently dropped**. (Fan-out is meant to be authored with an explicit `SequenceNode`.)
- The editor never prevents the second wire: `BlueprintLinkValidator.Validate`
  (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintLinkValidator.cs:56-59`) returns `Valid()` for
  **every** exec link with no single-output check, while data-IN pins get the single-connection "replace" treatment
  (lines 64-73). `BlueprintCommandSink.ApplyAddLink` only removes the existing link on the **input** side
  (`RemoveExistingDataLink(link.To)`, `BlueprintCommandSink.cs:405`).

The fix is two-sided: **(EXEC1)** make the compiler fail loud instead of silently dropping, and **(EXEC2)** make the
editor enforce exec-out 1:1 (dragging a new wire from an already-connected exec-out **replaces** the old one — standard
blueprint UX; the data-IN replacement at `BlueprintLinkValidator.cs:64-73` + `BlueprintCommandSink.cs:405` is the
exact pattern to mirror, but on the **output** side).

> **Design note (do NOT "support fan-out" instead):** exec-out is deliberately 1:1 in this codebase — there is an
> explicit `SequenceNode` (`BlueprintNodePaletteEntries.cs:68`, "Sequence") for running multiple branches. Do not
> change the scheduler to traverse multiple exec successors. The correct authoring for "run two things off one pin"
> is a Sequence node; this batch just stops the silent footgun.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (report format, test-quality bar, autonomy).
2. This file (the spec). The two root-cause sites are quoted above with file:line.
3. Use the **codebase-memory MCP** first (`list_projects` → `get_architecture` → `search_graph`/`get_code_snippet`)
   to study `Stage5_Schedule`, `BlueprintLinkValidator`, `BlueprintCommandSink`, and the `DiagnosticCodes`/Stage2
   validation pattern before editing.

## Tasks
Complete tasks in sequence; do NOT start EXEC2 until EXEC1 is implemented, its tests are written, and ALL tests
(including prior batches') pass.

### Task 1: Loud diagnostic for multi-successor exec-out (EXEC1) — file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` (UPDATE) + the `DiagnosticCodes` class (UPDATE)
- In **Stage 2 (Validate)**, for every graph, for every node, for every exec-**OUT** pin, count outgoing links
  (`Links.Where(FromNodeId==node && FromPinId==pin)`). If **> 1**, emit a **new ERROR diagnostic** (not a warning —
  the current behavior silently produces wrong code, so this must block compilation until the author fixes it).
- Add a new diagnostic constant to the same `DiagnosticCodes` class Stage2 already uses (find it via the existing
  `BP1602`/`DiagnosticCodes.` references in `Stage2_Validate.cs`). Pick the **next free code** in the structural-
  validation range (≈`BP14xx`; confirm it is unused across the solution). Suggested name/text:
  `BP1411` — *"Exec output pin '{pin}' on node '{node}' drives {n} successors; an exec output drives exactly one. Use a Sequence node to fan out."*
- Include the node id/kind and pin id in the message so the author can locate it.
- Do **not** touch `GetSingleExecSuccessor` or the scheduler traversal — the diagnostic makes the invalid graph a
  hard error before scheduling, which is the correct guard.
- **Edge cases:** `BranchNode` (two exec-out pins, each 1:1), `SequenceNode` (N exec-out pins, each 1:1), and `WhenNode`
  (OnFired/OnEnded/Out pins) are **legitimate multi-exec-OUT-PIN nodes** — the rule is *per pin* (one pin → ≤1 link),
  NOT per node. Verify these do not false-positive.
**Tests required** (`Hrot.Blueprints.Tests`, Stage2 validation test area):
- A graph with one exec-out pin linked to **two** target nodes → assert the new `BP14xx` **error** is emitted (assert
  the code + that it is severity Error).
- The same graph with the fan-out removed (one link) → assert **no** `BP14xx`.
- A `SequenceNode` (or `BranchNode`) with each of its multiple exec-out pins linked to one target each → assert **no**
  false-positive `BP14xx` (proves the rule is per-pin, not per-node).

### Task 2: Editor enforces exec-out 1:1 with replace-on-reconnect (EXEC2) — files: `BlueprintLinkValidator.cs` (UPDATE), `BlueprintCommandSink.cs` (UPDATE)
- **Validator** (`BlueprintLinkValidator.cs:56-60`, the `fromPin.Kind == PinKind.Exec` branch): before returning
  `Valid()`, identify the exec **OUTPUT** pin of the pair (the one with `Direction == Output`) and check
  `_graph.Links.Any(l => l.FromPin == outputPin.Id)`. If it already has an outgoing link, return **`InvalidReplace`**
  with a message that the **sink can distinguish from the data case** — use exactly:
  `"Exec output pin already has a connection (will replace existing)."`
  Exec **input** fan-in must remain allowed (multiple sources into one exec-in → still `Valid()`).
- **Sink** (`BlueprintCommandSink.ApplyAddLink`, `:362-403`): on the replace path (currently lines 370-374, which
  always calls `RemoveExistingDataLink(link.To)`), branch on which side is being replaced:
  - data-input replace (existing behavior, reason contains `"Data input"`) → `RemoveExistingDataLink(link.To)` (remove
    by **To** pin),
  - exec-output replace (reason contains `"Exec output"`) → new method `RemoveExistingExecOutLink(link.From)` that does
    `_graph.Links.RemoveAll(l => l.FromPinId == fromPin.Value)` (remove by **From** pin).
  Keep the existing data path working unchanged.
- Net UX: dragging a new exec wire out of an already-connected exec-out pin removes the prior wire and connects the new
  one (Unreal-style). The user then uses a **Sequence node** if they want both branches.
**Tests required** (`Hrot.Blueprints.Tests`, command-sink + validator test areas — mirror the existing data-input
replacement tests):
- Validator: exec-out pin already has one outgoing link → `Validate(thatExecOut, otherExecIn)` returns
  `LinkValidity.Invalid` with the `InvalidReplace` "Exec output" reason. (And a fresh exec-out → `Valid`.)
- Validator regression: an exec-**in** pin with one source still accepts a second source (`Valid`) — fan-in preserved.
- Sink: add an exec link from an exec-out pin that already has a link → assert the graph ends with **exactly one**
  link from that `FromPinId`, pointing at the **new** target (old link removed, new added).
- Sink regression: the existing **data-input** single-connection replacement still removes by To-pin and works.

## Success Criteria
- [ ] EXEC1: a multi-successor exec-out pin produces a hard `BP14xx` **error** in Stage 2; per-pin (Sequence/Branch
      don't false-positive); single-successor unaffected. Tests prove it.
- [ ] EXEC2: exec-out is 1:1 in the editor (replace-on-reconnect via the validator+sink), exec-in fan-in preserved,
      data-input replacement unregressed. Tests prove it.
- [ ] Full `dotnet test` suite green (not just the new tests); no new warnings.
- [ ] Report written to `.dev/_DONE/blueprint-finalize/reports/BF-BATCH-EXECFANOUT-REPORT.md` (every section).

## Report Requirements
Per `DEV-GUIDE_claude.md` §4: implementation summary per task, the exact new diagnostic **code + severity** you chose
and why it was free, any deviation (WHAT/WHY/BENEFIT/RISK), the **actual `dotnet test` counts** (total + the new
scenarios, not "all pass"), weak points spotted (e.g. is `SequenceNode` scheduling itself correct for the 1:1 rule?),
edge cases discovered, and a suggested one-line commit message. Do NOT ask comprehension questions.

## Autonomy
Finish in one go: implement, run the **full** test suite, fix root causes until green, then write the report. Do not
stop for permission. Never swallow an error or fall back to a dead path to make a test pass — fail early and loud.
Only stop if you hit a genuine breaking design flaw (e.g. the 1:1 exec-out assumption is contradicted elsewhere) —
in that case, document it in the report and stop.
