# Paste-ready prompt — Batch CF-3 (Zoo)

> Paste everything below the line into Zoo. It is self-contained.

---

You are implementing **Batch CF-3** in the repo `IOS-IG-SimHost-FDP-2` on branch `blueprint-integ-1`.

**First, read your working contract:** `.dev/.guides/DEV-GUIDE.md` — follow it exactly (build/test gates, reporting,
no snapshot regeneration, do not weaken tests). Then read the full batch spec: `.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md`
→ section **"Batch CF-3 — Reconcile dependent tests, editor breakpoint gating, cleanup"** (under "CORRECTIVE BATCHES (CF)").

## Mission

CF-1 diagnosed the node-identity bug. CF-2 fixed the compiler pipeline so Delay and Sequence nodes now get probes
keyed to their authored IDs. CF-3 is the final reconciliation: fix tests whose counts changed, gate the editor UI so
breakpoints can only be set on DebugMap-eligible nodes, and remove temporary diagnostics.

## Part 1 — Reconcile probe-count tests (2 tests)

CF-2 changed how probes are attributed — blocks now get probes keyed to their owning exec node
(`IrBlock.SourceNodeId`) rather than `Statements[0].NodeId`. Two `ProbeFormatIntegrationTests` fail because their
test graphs (Instance BP with Entry+Branch) don't have the Branch in its own block, so the probe fires for the
entry node instead of the Branch.

### 1a. Fix `CompiledProbe_EmitsNodeId_InDFormat`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/ProbeIntegrationTests.cs:65`

This test creates an Instance BP (Entry + Branch), compiles it, runs one tick, and asserts the probe fires with
`branchNodeId` in D format. After CF-2, the entry block's SourceNodeId overrides Statements[0].NodeId, so the
probe is keyed to the entry node (EventEntry), NOT the Branch.

**Fix — change the test asset to use a node that gets its own block with SourceNodeId.**

Use a Sequence node (which CF-2 gives `SourceNodeId` in Stage5) instead of Branch. Or use a LatentDelay (which
also gets `SourceNodeId`). The simplest approach: change `BuildProbeAsset` to build a graph where the probe-eligible
node is a LatentDelay or Sequence.

Concretely, update `BuildProbeAsset` (line 39-54) to use a different graph structure that produces a probe with
the right ID. One option: Entry → LatentDelay(0.01) → Return. The LatentDelay gets `SourceNodeId = delayNode.Id`
in Stage5, so the probe fires for the delay node.

BUT: the test comment says LatentDelay "triggers op_LessThan_Single IR ops that Roslyn cannot resolve." Check if
this is still true. If LatentDelay doesn't work, use: Entry → Sequence(Then0: Return). The Sequence entry block
gets `SourceNodeId = seq.Id`.

The test then asserts `fixture.DebugSession.Hit(expectedId)` where `expectedId = sequenceNodeId.ToString("D")`.

Update:
- `BuildProbeAsset` to return the right node ID
- The test name/comment if needed
- The assertions to match

### 1b. Fix `Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring`

File: same, line 97

Same root cause. After fixing `BuildProbeAsset`, this test should also work because the breakpoint is set on the
correct node that actually gets a probe. Keep the test logic the same; just fix the asset builder.

## Part 2 — Editor: gate breakpoints to DebugMap-eligible nodes

**Goal:** The canvas context menu should only allow "Toggle Breakpoint" on nodes that have a DebugMap entry
(i.e., exec/breakpointable nodes). For non-breakpointable nodes (pure data nodes like GetVariable, LiteralNode,
CastNode), either hide the menu item or show it disabled with a tooltip "Not a breakpoint target (data node)".

**Touch points:**
1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintBreakpointContextMenuProvider.cs` —
   the right-click context menu provider that adds "Toggle Breakpoint". It receives the node ID from the canvas.
   Modify it to check whether the node is in the DebugMap before showing the toggle.

2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — the session holds the
   `_debugMapIndex` (a `DebugMapIndex`). Add a method `public bool IsNodeBreakpointable(Guid assetId, Guid graphId, Guid nodeId)`
   that checks `_debugMapIndex.AllNodes.TryGetValue(nodeId, out _)` or similar.

3. The canvas renderer or the command handler for `editor.toggle-breakpoint` — may also need gating.

**How to check DebugMap:** The debug session's `_debugMapIndex` field (of type `DebugMapIndex`) is populated after
a compile. Check how `DebugMapIndex` works — it likely has an `AllNodes` dictionary keyed by NodeId. Use
`_debugMapIndex.TryResolveNode(nodeId)` or similar.

**IMPORTANT:** The breakpoint context menu operates on NODE IDs from the canvas. The canvas has access to the
session via `IBlueprintDebugSession` (see how `BlueprintBreakpointContextMenuProvider` gets the session).
The provider should call `session.IsNodeBreakpointable(assetId, graphId, nodeId)` before adding the menu item.
If the node is NOT breakpointable, add a disabled menu item with a tooltip explaining why.

**Pattern to follow:** Look at how BTree does it in
`Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeBreakpointContextMenuProvider.cs` — see if it already has
similar gating. If the BTree provider doesn't gate, just add the check directly.

## Part 3 — Remove temporary diagnostics

### 3a. Clean `BlueprintDebugSession.cs`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`

Remove:
- The `DiagLog` method (entire method body)
- The `_diagCount` field
- The `_diagLogPath` field
- All calls to `DiagLog(...)` in `SetBreakpoint` and `OnNodeEnter` and any other method
- Any `using` directives that become unused after removal (e.g. `System.IO` if only used for DiagLog)

### 3b. Delete `bp-diag.log`

File: `bp-diag.log` (in repo root) — delete it entirely.

### 3c. Verify cleanup

Run `grep -r DiagLog` — must return nothing in source files.
Run `grep -r bp-diag` — must return nothing in source files (OK to remain in markdown docs).
Run `grep -r _diagCount` — must return nothing.
Run `grep -r _diagLogPath` — must return nothing.

## SUCCESS CONDITIONS (must all hold)

1. `dotnet build IOS-IG-SimHost.sln -c Debug` → **0 errors** (close editor first; it locks DLLs)
2. `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **CF-3 fixes the 2 ProbeFormatIntegrationTests**
   (they now PASS). **0 new failures** vs the documented pre-existing baseline (22 failures).
3. Every changed test listed by name with old→new expectation in the report.
4. Editor: right-click on a node with a DebugMap entry → "Toggle Breakpoint" is enabled.
   Right-click on a node WITHOUT a DebugMap entry → "Toggle Breakpoint" is disabled/hidden with tooltip.
5. `grep -r DiagLog` returns nothing in `.cs` files. `grep -r _diagCount` returns nothing.
6. `bp-diag.log` is deleted.
7. CF-1 and CF-2 tests still pass.

## What NOT to change
- Do NOT modify the BTree breakpoint gating (if any)
- Do NOT change the DebugMapIndex / DebugMapBuilder logic
- Do NOT regenerate golden snapshots
- Do NOT delete or weaken any test assertions except for count expectations

## Reporting

Write `.dev/_DONE/blueprint-dbg-1/reports/CF3-REPORT.md` with: what you changed (file-by-file), before/after test counts,
the exact `dotnet build`/`dotnet test` command lines and results, the full failing-test set by name (before/after),
and every probe/step count test whose expected value changed (old→new with justification).
