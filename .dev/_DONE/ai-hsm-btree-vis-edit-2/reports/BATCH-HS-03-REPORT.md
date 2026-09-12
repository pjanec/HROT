# BATCH-HS-03 Report — Command sink: draw transition

**Date:** 2026-06-12 | **Branch:** `blueprint-integ-1` | **Task:** TASK-HS-03

## Resolvers added (HsmAsset.cs)

Two O(n)-scan pin→state resolvers placed after the existing `FindEventById` lookup, before the PU-302 stitch section:

- `FindStateByOutputPin(Guid pinId)` — matches `s.HiddenOutputPinId` across `AllStates`
- `FindStateByInputPin(Guid pinId)` — matches `s.HiddenInputPinId` across `AllStates`

No other mutators added to HsmAsset.

## ApplyAddLink flow (HsmCommandSink.cs)

Replaced the `{ /* TODO */ }` stub at line ~151 with a three-step body:

1. **Validate** — constructs `new HsmLinkValidator(_asset)`, calls `Validate(cmd.From, cmd.To)`, checks `validation.Verdict != LinkValidity.Valid`. Returns early on rejection (Final source, History/DeepHistory target, or unresolvable pins via the validator's own internal scan).
2. **Resolve** — calls `_asset.FindStateByOutputPin(cmd.From.Value)` and `_asset.FindStateByInputPin(cmd.To.Value)`. Returns early if either is null (unresolvable pin edge-case).
3. **Create** — instantiates `TransitionNode` with `VisualId = cmd.AssignedId.Value`, `Source = source`, `Target = target`, `Kind = TransitionKind.External`, `EventId = 0`, and calls `_asset.RegisterTransition(t)`.

### TransitionKind chosen

`TransitionKind.External` — the plain cross-state transition kind. The enum has `External | Internal | Local`. `External` is the normal default for HSM transitions drawn between two distinct states. `Internal` is for self-transitions; `Local` is for transitions that exit to the parent boundary. Neither fits the general draw-a-link case.

### Fields on `GraphCommand.AddLink` used

- `cmd.AssignedId` → `LinkId` → `.Value` gives the `Guid` for `TransitionNode.VisualId`
- `cmd.From` → `PinId` → `.Value` passed to `FindStateByOutputPin`
- `cmd.To` → `PinId` → `.Value` passed to `FindStateByInputPin`

### Validator result API

`LinkValidationResult` record struct with field `Verdict` (enum `LinkValidity`). Check: `validation.Verdict != LinkValidity.Valid`. The HSM validator only returns `Valid` or `Invalid`; `ValidWithCast` is a NodeEditor-level concept not used here.

## Test file

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkTransitionTests.cs` (new, 6 tests)

Reuses the `BuildTestAsset()` helper pattern and `RegisterTwoSimpleStates` convenience, matching the style of `HsmCommandSinkCreateStateTests`.

### Test names + assertions

| # | Test | Key assertions |
|---|------|---------------|
| 1 | `AddLink_happy_path_creates_transition` | `result.Success == true`; `FindTransitionByVisualId(g)` returns transition with `Source == A`, `Target == B`, `VisualId == g`, `Kind == External`, `EventId == 0`; present in `AllTransitions` and `A.OutgoingTransitions` |
| 2 | `AddLink_find_by_visual_id_resolves` | `FindTransitionByVisualId(g)` returns non-null with correct `VisualId` |
| 3 | `AddLink_projects_into_HsmGraphModel_Links` | `HsmGraphModel(asset).Links` contains exactly one link with `Id.Value == g` (rebuilds on `Changed`) |
| 4 | `AddLink_rejects_when_source_is_Final` | A has `IsFinal == true`; `AllTransitions.Count` unchanged after AddLink |
| 5 | `AddLink_rejects_when_target_is_History` | B has `IsHistory == true`; `AllTransitions.Count` unchanged after AddLink |
| 6 | `AddLink_noops_when_pin_unresolvable` | Random `Guid.NewGuid()` for output pin or input pin; `AllTransitions.Count` unchanged both ways; no throw |

## Before/after counts

- **Baseline:** 402 passed
- **After:** 408 passed (`+6`), 0 failures, 0 skipped
- **Build:** 0 errors, 0 warnings (1 pre-existing BTREE0002 in a different project, unrelated)

## What was NOT done

Nothing. All three parts (resolvers, ApplyAddLink implementation, test file) are complete. No other handlers (`ApplyAddNode`, `ApplyRemoveNodes`, `ApplyRemoveLinks`, `ApplySetContainerCollapsed`, region/move) were touched.

## Files modified

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` | +18 lines: `FindStateByOutputPin`, `FindStateByInputPin` resolvers |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` | +24 lines: `ApplyAddLink` implementation replacing `{ /* TODO */ }` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkTransitionTests.cs` | New file: 6 tests, ~180 lines |
