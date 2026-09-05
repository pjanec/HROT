# Paste-ready prompt — Batch CF-2 (Zoo)

> Paste everything below the line into Zoo. It is self-contained.

---

You are implementing **Batch CF-2** in the repo `IOS-IG-SimHost-FDP-2` on branch `blueprint-integ-1`.

**First, read your working contract:** `.dev/.guides/DEV-GUIDE.md` — follow it exactly (build/test gates, reporting,
no snapshot regeneration, do not weaken tests). Then read the full batch spec: `.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md`
→ section **"Batch CF-2 — Preserve authored node identity end-to-end (the fix)"** (under "CORRECTIVE BATCHES (CF)").

Also read the CF-1 diagnostic report at `.dev/_DONE/blueprint-dbg-1/reports/CF1-NODE-IDENTITY-REPORT.md` — it tells you
exactly which nodes lose identity and where.

## Mission

Blueprint breakpoints never pause because the node ID set by the editor differs from the node ID the runtime probes.
CF-1 proved: only 2 `NodeEnter` probes for 7 nodes. 4 exec nodes have NO probe at all. Delay/Sequence IDs are
replaced by synthesized GUIDs. The fix must preserve authored node identity through the entire compiler pipeline.

## Root causes (from CF-1)

1. **DebugProbeInsertion** gates per-block probe on `block.Statements[0].Debug.NodeId`. Many blocks' first statement
   has `NodeId == null` → entire block gets no probe (EventEntry, SetVariable, Return, FunctionCall missing probes).
2. **WaitLowering_Instance.Synth()** creates statements with `NodeId = null` → Delay/Sequence lose their authored IDs
   (`0b561966→976ef338`, `da9a9c0b→0ec3b253`).
3. **CSharpEmitter.EmitNodeStart** gates DebugMap entry on `debug?.NodeId != null` → no NodeId = no DebugMap entry.

## Design (follow this exactly)

### Part 1 — Add `OriginNodeId` to `IrDebugAnnotation`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrDebugAnnotation.cs`

Add a nullable `Guid? OriginNodeId` property (default null). This carries the authored node ID through lowering
passes that synthesize new statements without a direct node association.

```csharp
public Guid? OriginNodeId { get; init; }
```

### Part 2 — Add `SourceNodeId` to `IrBlock`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrBlock.cs`

Add `Guid? SourceNodeId` — the authored exec node that owns this block. Set by Stage5.

```csharp
public Guid? SourceNodeId { get; init; }
```

### Part 3 — Set `SourceNodeId` in Stage5 (`Stage5_Schedule.cs`)

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`

Find every place an `IrBlock` is constructed (search for `new IrBlock` or `new IrBlockId` patterns) and thread
the authored `Node.Id` as `SourceNodeId`. Key locations:
- `ScheduleBlock` → the entry block for the graph
- `ScheduleLatentNode` → blocks for Delay/Wait nodes (the pre-suspend block carries the authored node)
- `ScheduleSequenceNode` → the block carrying the sequence's children

**Important:** synthesized infrastructure blocks (dispatch blocks, resume-check blocks created in lowering) should
NOT have `SourceNodeId` set — only blocks that directly represent an authored exec node.

The `DebugOf(node)` helper (used throughout Stage5) produces `IrDebugAnnotation` with `NodeId = node.Id`. Find how
blocks are created and ensure the authored node ID reaches the block.

**How to find the right blocks:** In Stage5, search for `IrBlock` constructions. Each exec node's scheduling creates
one or more blocks. The FIRST block created for an exec node (before any latent split) should get
`SourceNodeId = node.Id`. For latent nodes (Delay, WaitForChannel), the pre-suspend block gets the authored id;
the resume block does NOT (it's infrastructure).

### Part 4 — Set `OriginNodeId` in lowering passes

**4a. `WaitLowering_Instance.cs`**

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/WaitLowering_Instance.cs`

The `Synth()` helper creates `IrDebugAnnotation` with `Synthesized = "stage6-wait-lower-inst"` and `NodeId = null`.
Modify `Synth()` to accept an optional `Guid? originNodeId` parameter, and set `OriginNodeId` on the annotation.

When calling `Synth()` for statements that are emitted as part of a specific suspend block (the block that
originally belonged to the Delay/Sequence node), pass the block's `SourceNodeId`. The suspend block's
`SourceNodeId` should be available from the `IrBlock` being modified.

**How:** The suspend block `sb` being modified in the loop at line 73 knows its original node. Before lowering,
add the `SourceNodeId` to the block (Part 3 ensures it's there). Then in the modified block's statements,
any `Synth()` call that replaces the original node's statement should carry `OriginNodeId = sb.SourceNodeId`.

**4b. `Stage3_Normalize.cs` (if needed)**

The `SynthesizedGuid` method creates synthesized nodes (LiteralNode for defaults, CastNode for coercions). These
are data-only nodes — they should NOT get probes. No change needed here, but verify: the report shows `...0003`
(FunctionCall/Add) has DebugMap entry but no probe — which is correct for pure data nodes. Do NOT force probes
onto synthesized data nodes.

### Part 5 — Fix `DebugProbeInsertion.cs`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/DebugProbeInsertion.cs`

Change `InsertProbes` (line 19-62) to use the **block's owning exec node ID** instead of `Statements[0].Debug.NodeId`:

```csharp
// OLD (line 23-24):
var firstStmt = block.Statements[0];
if (firstStmt.Debug?.NodeId is null) return block;

// NEW:
// Prefer the block's owning exec node. Fall back to first statement's NodeId/OriginNodeId.
Guid? probeNodeId = block.SourceNodeId
    ?? block.Statements[0].Debug?.NodeId
    ?? block.Statements[0].Debug?.OriginNodeId;
if (probeNodeId is null) return block;
```

Then use `probeNodeId.Value` for the probe operation. The `Debug` annotation on the probe statement should carry
the probeNodeId so downstream passes see it.

**Also:** ensure pure data-only blocks (blocks with NO `SourceNodeId` and NO exec-relevant NodeId) get NO probe.
If `SourceNodeId` is null AND the first statement has no NodeId that maps to an exec node, skip the probe.
This is already handled by the fallback chain above.

### Part 6 — Fix `CSharpEmitter.EmitNodeStart` / `EmitNodeEnd`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs`

Change the gate from `debug?.NodeId is null` to also accept `OriginNodeId`:

```csharp
// OLD:
if (debug?.NodeId is null) return;

// NEW:
var effectiveNodeId = debug?.NodeId ?? debug?.OriginNodeId;
if (effectiveNodeId is null) return;
```

Use `effectiveNodeId.Value` for `RecordNodeStart`/`RecordNodeEnd`. Same pattern for `EmitNodeEnd`.

Find the exact lines (~43-54) and update both methods.

### Part 7 — Fix `StatementEmitter.cs` (if it also gates on NodeId)

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`

Check if `EmitNodeStart`/`EmitNodeEnd` are also called from `StatementEmitter`. If so, apply the same fix there.
Search for `Debug?.NodeId` in StatementEmitter.cs and update.

## New test file

Create: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF2_AuthoredIdProbeTests.cs`

The test must:
1. Load `Count4.bp.json` and compile in Debug mode (reuse the pattern from `CF1_NodeIdentityDiagnosticsTests.cs`)
2. Assert all 5 success conditions below

```csharp
[Fact]
public void CF2_DelayAuthoredId_HasDebugMapEntry()
{
    // DebugMap.Entries contains entry with NodeId == 0b561966-b00b-4c84-a1a0-87042220ba9f (Delay)
}

[Fact]
public void CF2_SequenceAuthoredId_HasDebugMapEntry()
{
    // DebugMap.Entries contains entry with NodeId == da9a9c0b-25f8-4a81-9a52-75c715456f18 (Sequence)
}

[Fact]
public void CF2_DelayAuthoredId_HasNodeEnterProbe()
{
    // Generated source contains: DebugProbe.NodeEnter(self, "0b561966-b00b-4c84-a1a0-87042220ba9f")
}

[Fact]
public void CF2_SequenceAuthoredId_HasNodeEnterProbe()
{
    // Generated source contains: DebugProbe.NodeEnter(self, "da9a9c0b-25f8-4a81-9a52-75c715456f18")
}

[Fact]
public void CF2_AllExecNodes_HaveExactlyOneProbe_NoDataNodeProbes()
{
    // For every authored EXEC node (EventEntry ...0001, SetVariable ...0002, Sequence da9a9c0b,
    // Delay 0b561966, Return 7b6da53f), there is exactly one DebugProbe.NodeEnter with that id.
    // GetVariable ...0004 must NOT have a probe (it's a pure data node).
    // FunctionCall ...0003 — if CF-1 classified it as pure data, no probe; if impure/exec, one probe.
    // Check the generated source with regex.
}

[Fact]
public void CF2_EndToEnd_DelayBreakpointPauses()
{
    // Mirror BreakpointTests style: 
    // - Create a BlueprintTestFixture for Count4 compiled in Debug
    // - Set breakpoint on Delay (0b561966) via session.SetBreakpoint(assetId, graphId, delayGuid)
    // - Drive the compiled blueprint one tick
    // - Assert MockTimeController.PauseRequestCount >= 1
}
```

For the end-to-end test, study how `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BreakpointTests.cs`
sets up the fixture, wires the `DataBreakpointManager` + `MockTimeController`, and asserts pause. Mirror that
pattern exactly.

## What NOT to change

- Do NOT modify `DebugProbe.NodeEnter` signature or `IBlueprintProbeSink`
- Do NOT change `BlueprintDebugSession.OnNodeEnter` / `HandleBreakpointHit` / the breakpoint dictionary
- Do NOT change the `DataBreakpointManager` wiring
- Do NOT add probes to pure data-only nodes (GetVariable, LiteralNode, CastNode)
- Do NOT change snapshot/golden tests — if a golden changes, report it, don't regenerate

## SUCCESS CONDITIONS (must all hold)

1. `dotnet build IOS-IG-SimHost.sln -c Debug` → **0 errors** (close editor first; it locks DLLs)
2. All 6 CF2 tests pass
3. `DebugMap.Entries` contains `NodeId == 0b561966-b00b-4c84-a1a0-87042220ba9f` (Delay authored id)
4. `DebugMap.Entries` contains `NodeId == da9a9c0b-25f8-4a81-9a52-75c715456f18` (Sequence authored id)
5. Generated C# source contains `DebugProbe.NodeEnter(self, "0b561966-b00b-4c84-a1a0-87042220ba9f")` AND
   `DebugProbe.NodeEnter(self, "da9a9c0b-25f8-4a81-9a52-75c715456f18")`
6. For every authored EXEC node (EventEntry, SetVariable, Sequence, Delay, Return), exactly one NodeEnter probe
   with that id. GetVariable ...0004 has NO probe. FunctionCall ...0003: no probe if pure, one if impure.
7. End-to-end pause: `SetBreakpoint(assetId, graphId, 0b561966...)` + one tick → `PauseRequestCount >= 1`
8. **No existing test is deleted, skipped, or weakened.** If a test's expected count changes (probe count / step count),
   list EVERY such test by name with old→new expectation in the report. Do NOT change test intent.

## Reporting (per DEV-GUIDE)

Write `.dev/_DONE/blueprint-dbg-1/reports/CF2-REPORT.md` with: what you changed (file-by-file), the exact `dotnet build` /
`dotnet test` command lines and results, the full failing-test set by name (before/after), and every probe-count test
whose expected value changed (old→new). Do NOT set `BLUEPRINT_REGENERATE_SNAPSHOTS`. Do NOT regenerate golden
snapshots. If a golden changes, report the diff and STOP.
