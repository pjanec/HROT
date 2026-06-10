# BATCH-04: Editor UI surfacing for node-granular stepping

**Tasks:** NGS-2.4a (inspector redirect), NGS-2.4b (highlight follows pointer), NGS-2.4c (Step Back + position UI)   **Phase:** Editor UI   **Est:** ~10h
**Dependencies:** BATCH-00..03 (`040f6f82`, `c839c122`, `7b1aae5b`, `5007c22f`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-dbg-2/PLAN.md` + `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md` (the feature).
3. `.dev/blueprint-dbg-2/reviews/BATCH-03-REVIEW.md` (what the session already exposes).
4. This file.

## Why
The node-granular logic is implemented and headlessly proven, but the editor UI does NOT surface it: the inspector reads live state, the node highlight follows live execution, and there is no Step Back. This batch wires the three UI seams so a human can SEE per-node state while paused. The session already exposes everything needed (all on `IBlueprintDebugSession`): `IsPaused`, `GetCurrentStateSnapshot()` (returns the pointer's restored per-node state while paused, else null), `StepBack()`, `StepOver/Into/Out()` (move pointer forward when recordings exist), `CurrentNodePointer` (int, -1 when inactive), `CurrentNodeId` (string?, the pointer's node), `RecordedNodeCount`.

## Tasks

### Task 1: Inspector shows the pointer's per-node state (NGS-2.4a) — file: `Hrot.Blueprints.Editor/Inspector/BlueprintRuntimeInspectorPane.cs`
- Today (line ~60) it always calls `_session.CaptureLiveState(entity, assetId)`. Change so that **while paused**, it shows the virtual pointer's per-node snapshot, else live.
- Extract a small **testable** static helper (no ImGui), e.g.:
  `static BlueprintStateSnapshot? ResolveInspectorSnapshot(IBlueprintDebugSession session, Entity entity, Guid assetId)` →
  return `session.IsPaused ? (session.GetCurrentStateSnapshot() ?? session.CaptureLiveState(entity, assetId)) : session.CaptureLiveState(entity, assetId)`.
  (`GetCurrentStateSnapshot()` already targets the paused/pointer entity; if the selected entity differs from the paused entity, fall back to live — keep the logic simple and documented.)
- `Draw()` calls the helper instead of `CaptureLiveState` directly. Optionally show a small "(paused — node X/N)" hint in the header when paused.

**Tests required** (`Hrot.Blueprints.Tests`, no ImGui needed — call the helper directly): using the real compiled Sequence A:0→10→20 asset + a real `BlueprintDebugSession` (reuse `VirtualPointerTests`/`SubTickRecorderIntegrationTests` fixture pattern), pause on the breakpoint, move the pointer, and assert `ResolveInspectorSnapshot(...)` returns the EXACT per-node `A` value at each pointer position (0 → 0 → 10), and returns live state after `Continue()`.

### Task 2: Node highlight follows the virtual pointer (NGS-2.4b) — file: `Hrot.Blueprints.Editor/Debug/BlueprintDebugToNodeEditAdapter.cs`
- `CurrentlyExecutingNode` (line ~34) currently returns `PausedAt.NodeId` then history. Change so that **when paused AND the virtual pointer is active** (`_session.CurrentNodePointer >= 0` / `CurrentNodeId` non-null & parseable), it returns the POINTER's node (`CurrentNodeId`) — so the canvas highlight follows Step/StepBack. Keep the existing PausedAt/history fallback for the non-pointer case.
- Ensure pointer moves trigger a redraw: confirm `StepOver/Into/Out/StepBack` raise `OnSessionStateChanged` (the adapter already forwards that to `StateChanged`). If they do NOT, add the raise in `BlueprintDebugSession` so the canvas refreshes on pointer move. (ImGui is immediate-mode so panes redraw anyway, but raise the event for correctness/consumers that gate on it.)

**Tests required** (no ImGui — query the adapter directly): with a paused session + recordings, assert `adapter.CurrentlyExecutingNode` equals the GUID of `session.CurrentNodeId`, and that it CHANGES as you `StepBack()`/`StepInto()` (i.e., tracks the pointer, not a fixed paused node). Assert it falls back correctly when not paused / no recordings.

### Task 3: Step Back button + node position indicator (NGS-2.4c) — file: `Hrot.Blueprints.Editor/Debug/DebugStepControls.cs`
- Add a **"Step Back"** button (place it before/after the Step buttons) that calls `session.StepBack()` and invokes `onStepAction?.Invoke("StepBack")`.
- Add a node-position indicator shown while paused with recordings, e.g. text `node {CurrentNodePointer + 1} / {RecordedNodeCount}` (only when `RecordedNodeCount > 0`).
- Extract the indicator text into a **testable** static helper, e.g. `static string FormatNodePosition(IBlueprintDebugSession session)` returning `""` when no recordings, else `"node X / N"`.
- Keep existing buttons/behavior intact.

**Tests required** (follow the existing `Editor/DebugWindowDrawUITests.cs` pattern which uses the `onStepAction` capture):
- Clicking "Step Back" invokes `session.StepBack()` (captured action == "StepBack"). If the existing tests drive buttons via a headless ImGui context, follow that; if not feasible, at minimum assert the `onStepAction` wiring and test `FormatNodePosition` returns the correct "node X / N" string for given pointer/count values.

## Success Criteria
- [ ] Inspector shows per-node pointer state while paused (exact values via the helper test), live otherwise.
- [ ] `CurrentlyExecutingNode` tracks the virtual pointer while paused; pointer moves raise `OnSessionStateChanged`.
- [ ] Step Back button wired; node position indicator correct.
- [ ] No regression to existing debug UI (DebugWindowDrawUITests, DebugPanelWindow) or the headless logic suites.
- [ ] Full affected suite green (`Failed: 0` except documented pre-existing reds).
- [ ] Report submitted.

## How to run tests (no regen flags)
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
Known pre-existing reds (NOT yours — confirmed against clean baseline): `AiPrimitive_EmitMatchesGoldenSource`×2, `Stage8_*`×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`. A NEW failure ⇒ root-cause it. A transient first-build `MapKeyboardKey.idl` (DDS codegen) error can occur — just re-run.

## Report Requirements (`reports/BATCH-04-REPORT.md`)
Per DEV-GUIDE §4, plus: the inspector resolve logic + selected-vs-paused-entity handling; how the highlight tracks the pointer + whether you had to add the state-changed raise; the controls additions; which seams are unit-tested vs left for human visual smoke; exact test counts; suggested commit message.

**Autonomy:** finish in one go — implement, test, fix root causes until green, then report. Pure-ImGui rendering that can't be headlessly tested is fine to leave for the human smoke, but extract and unit-test the decision logic (snapshot resolution, highlight node selection, position string). Do NOT commit.
