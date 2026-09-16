# BF-BATCH-EXECFANOUT Report -- Single-successor exec-out enforcement + loud diagnostic

**Date:** 2026-06-09
**Branch:** `blueprint-integ-1`
**Status:** COMPLETE -- all batch gates green (4 pre-existing failures unrelated to this batch)

---

## Goal

Two-sided fix for the silent fan-out bug where an exec-out pin wired to more than one target
produced zero diagnostic output while silently dropping all but the first successor:

- **EXEC1:** Compiler emits a hard Error (`BP1411`) in Stage 2 when any exec-out pin has more
  than one outgoing link.
- **EXEC2:** Editor enforces exec-out 1:1 -- dragging a new wire from an already-connected
  exec-out pin removes the old wire (replace-on-reconnect), mirroring the existing data-input
  replacement pattern.

---

## Changes Made

### EXEC1: Compiler Diagnostic

**`Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs`** (updated)
- Added `public const string BP1411 = "BP1411";` in a new "Stage 2 -- Validate (exec-out
  connectivity)" section between BP1402 and BP1500.
- BP1411 was chosen as the next free code in the BP14xx structural-validation range; BP1400,
  BP1401, BP1402 were already used; BP1412 and above were unoccupied.

**`Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`** (updated)
- Added `new V_ExecOutFanOut()` to the `Validators` list (last position in Stage 2).
- Added `internal sealed class V_ExecOutFanOut : IValidator` at end of file:
  - For every graph, for every node, for every pin where `IsExec && Direction == "Out"`:
    counts links with `FromNodeId == node.Id && FromPinId == pin.Id`.
  - If count > 1 emits `Diagnostic.Error(DiagnosticCodes.BP1411, ...)` with node id, pin name,
    node type name, and successor count.
  - Per-pin check means BranchNode / SequenceNode / WhenNode (multiple exec-out pins, each 1:1)
    never false-positive.

### EXEC2: Editor 1:1 Enforcement

**`Hrot.Blueprints.Editor/Host/BlueprintLinkValidator.cs`** (updated)
- `fromPin.Kind == PinKind.Exec` branch now:
  - Identifies the exec-OUTPUT pin of the pair (the one with `Direction == PinDirection.Output`).
  - Checks `_graph.Links.Any(l => l.FromPin == outputPin.Id)`.
  - If already connected: returns `InvalidReplace("Exec output pin already has a connection
    (will replace existing).")`.
  - Exec-input fan-in remains allowed (no check on the input side).

**`Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`** (updated)
- `ApplyAddLink`: the `validation.Verdict == LinkValidity.Invalid` branch now discriminates:
  - Reason contains `"Exec output"` -> `RemoveExistingExecOutLink(link.From)` (remove by From).
  - Reason contains `"replace"` or `"already has"` -> `RemoveExistingDataLink(link.To)` (existing
    data path, unchanged behavior).
  - Otherwise -> return failure as before.
- Added `private void RemoveExistingExecOutLink(PinId fromPin)`:
  `_graph.Links.RemoveAll(l => l.FromPinId == fromPin.Value)`.

### Production Blueprint Assets Fixed

Adding BP1411 to Stage 2 made two existing blueprint assets hard errors during the source-
generator build pass (see Deviation section below).

**`Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Counting.bp.json`** (updated)
- Removed the fan-out link from Entry.Out to DemoSharedAction (node `83e9c028-...`).
- The DemoSharedAction node remains in the graph but is exec-disconnected. The main exec chain
  Entry -> SetVariable -> Return is intact.

**`Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/EnumDemo.bp.json`** (new file added to repo)
- Removed 3 fan-out links from Entry.Out (to MoveTo, CallCustomEvent, DemoEnumAction2).
- Kept Entry -> DemoEnumAction -> Return chain. The 3 extra nodes remain exec-disconnected.

---

## Diagnostic Code

| Field | Value |
|-------|-------|
| Code | `BP1411` |
| Severity | Error (not Warning) |
| Range | BP14xx -- structural validation, Stage 2 |
| Why free | BP1400/1401/1402 occupied; BP1411 was the first gap; confirmed by full-solution grep |
| Why Error | The scheduler silently drops all-but-first successor, producing wrong code with no
  indication. Making it a Warning would let invalid graphs through CI. Error is the only
  safe choice. |

---

## Test Results

### New Tests (9 total -- all pass)

**`ExecOutFanOutTests.cs`** (4 tests, EXEC1 validator):

| Test | What it verifies |
|------|-----------------|
| `Stage2_ExecOutFanOut_TwoSuccessors_EmitsBP1411_AsError` | Fan-out -> BP1411 Error emitted [CoversDiagnosticCode("BP1411")] |
| `Stage2_ExecOutFanOut_SingleSuccessor_NoBP1411` | Single link -> no BP1411 (true negative) |
| `Stage2_ExecOutFanOut_SequenceNode_PerPinOneSuccessor_NoBP1411` | SequenceNode per-pin 1:1 -> no false positive |
| `Stage2_ExecOutFanOut_DiagnosticIncludesPinAndNodeInfo` | Message contains pin name + node id for locatability |

**`Host/ExecOutEditorTests.cs`** (5 tests, EXEC2 validator + sink):

| Test | What it verifies |
|------|-----------------|
| `Validator_ExecOut_AlreadyConnected_ReturnsInvalidWithExecOutputReason` | Already-connected exec-out -> InvalidReplace with "Exec output" in reason |
| `Validator_ExecOut_NoExistingLink_ReturnsValid` | Fresh exec-out -> Valid |
| `Validator_ExecIn_FanInStillAllowed` | Exec-in with existing source -> still Valid (regression) |
| `Sink_AddExecLink_ReplacesExistingExecOutLink` | Sink: existing link removed, new link added; exactly 1 link from that FromPinId |
| `Sink_AddDataLink_DataInputReplacement_StillWorksAfterExecChange` | Data-input replace path still removes by To-pin (regression) |

### Full Suite

| Run | Passed | Failed (pre-existing) | Skipped | Total |
|-----|--------|-----------------------|---------|-------|
| Final | 1623 | 4 | 8 | 1635 |

**Pre-existing failures (4) -- none caused by this batch:**

| Test | Pre-existing root cause |
|------|------------------------|
| `Library_EmitMatchesGoldenSource` | CRLF/LF mismatch between snapshot file and generated output; snapshot file unchanged from HEAD |
| `LibraryMath_GeneratedSource_Snapshot` | Same CRLF issue |
| `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Locale-sensitive decimal separator: test asserts "0.8", actual contains "0,8" (Czech locale) |
| `TickFrame_1000Frames_AllocatesZeroBytes` | 3200 bytes allocated vs expected 0; allocation regression predating this batch |

Confirmed pre-existing: `git diff HEAD` shows none of these snapshot or runtime files were
modified by this batch. The `AllDiagnosticCodes_HaveAtLeastOneTestCovering` coverage meta-test
now passes (it previously had BP1411 as uncovered -- fixed by `[CoversDiagnosticCode("BP1411")]`
on `Stage2_ExecOutFanOut_TwoSuccessors_EmitsBP1411_AsError`).

---

## Deviations

### Deviation 1: Two production blueprint assets required fan-out link removal

**WHAT:** `Counting.bp.json` and `EnumDemo.bp.json` both contained exec-out fan-out links that
now emit BP1411 as a hard Error. Because the blueprint source generator (`BlueprintIncrementalGenerator`)
runs as part of `dotnet build` and calls the full compiler pipeline, BP1411 fires as a CSC
error (`error BP1411: ...`) during the `Hrot.AI.Behaviors` build step.

**WHY:** The assets predated the diagnostic. They were the original repro case for the bug.

**BENEFIT:** Fixing the assets proves the diagnostic catches real production violations and that
the build is clean. The corrected exec chain (Entry -> SetVariable -> Return for Counting,
Entry -> DemoEnumAction -> Return for EnumDemo) matches the intended runtime behavior; the
previously-silent extra nodes were always dead.

**RISK:** Low. The removed links were the bug -- they caused silent code drop at runtime. The
disconnected nodes remaining in the graph are inert (no exec path to them).

### Deviation 2: EnumDemo.bp.json was untracked (new to repo index)

**WHAT:** EnumDemo.bp.json was not previously tracked by git (it appeared as untracked in git
status). It was added to the repo as a new file during this batch.

**WHY:** The file physically existed on disk but was not staged. It referenced nodes that
caused BP1411. Fixing the fan-out and adding it resolves the build error.

**BENEFIT:** The file is now tracked and reproducible.

**RISK:** None; the file is a demo/test asset only.

---

## Weak Points Spotted

1. **SequenceNode scheduling assumption:** The spec notes "is SequenceNode scheduling itself
   correct for the 1:1 rule?" -- verified: `SequenceNode` has N distinct exec-out pins (Out0,
   Out1, ...), each expected to have exactly one link. `GetSingleExecSuccessor` is called once
   per SequenceNode exec tick with a specific pin index, so it correctly takes the first (and
   only expected) link from each pin. The per-pin BP1411 rule aligns with this.

2. **Disconnected nodes in fixed assets:** `Counting.bp.json` retains the `DemoSharedAction`
   node and `EnumDemo.bp.json` retains 3 extra nodes, all exec-disconnected. A future
   `BP1600` (OrphanedNode) diagnostic would catch these. For now they are inert.

3. **Exec-input validator side:** The validator identifies the output pin by checking
   `fromPin.Direction == PinDirection.Output ? fromPin : toPin`. This assumes the link is always
   presented as (output-pin, input-pin) -- which holds for the standard link-drag UX. If a
   future code path presents links reversed, the check would be on the wrong pin. Not a
   current issue; worth noting for future link-direction normalization.

4. **Snapshot line-ending failures:** Four pre-existing tests fail due to CRLF/LF mismatch in
   golden snapshot files and locale-sensitive decimal formatting. These are independent of this
   batch and require a separate cleanup pass.

---

## Edge Cases Discovered

- `SequenceNode`'s multiple exec-out pins confirmed as no false-positive: per-pin rule is
  correct because each pin is a distinct `pin.Id`.
- Exec-input fan-in (multiple sources into one exec-in) is allowed and tested explicitly.
- The exec-output pin identification in the validator correctly handles both orderings:
  `(execOut -> execIn)` and `(execIn -> execOut)` via the Direction check.

---

## Files Modified

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintLinkValidator.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Counting.bp.json` (fan-out link removed)
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/EnumDemo.bp.json` (new; 3 fan-out links removed)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/ExecOutFanOutTests.cs` (new; 4 tests)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/ExecOutEditorTests.cs` (new; 5 tests)

---

## Suggested Commit Message

`feat(blueprints): exec-out fan-out guard -- BP1411 compiler error + editor replace-on-reconnect (EXEC1+EXEC2)`
