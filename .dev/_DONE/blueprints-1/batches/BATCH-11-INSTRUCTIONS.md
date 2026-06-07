# BATCH-11 Instructions

**Branch:** `blueprints`
**Workspace root:** `d:\WORK\IOS-IG-SimHost-FDP`

**Scope:** TASK-CP-003 — Stage 6: Lower (Dispatch-Aware Transformations)

**Design references (read these first):**
- `.dev/blueprints-1/TASK-DETAIL.md` — TASK-CP-003 section (constraints + success conditions)
- `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design.md` — §9 Stage 6
  (entire section: §9.1 through §9.12)
- `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md`
  — Q-18.1 (IrOp_CheckCursorVersion, IrOp_ReadInstanceVersion, InstanceVersion parameter)

---

## Context From Previous Batches

Stage 6 stub (`Stage6_Lower.cs`) throws `NotImplementedException`. Implement it fully.

The IR hierarchy already exists with these operations (from BATCH-09):
- `IrOp_ReadInstanceVersion` — per Q-18.1 (captures `instanceVersion` parameter at suspend time)
- All latent ops: `IrOp_WaitForChannel`, `IrOp_WaitForEvent`, `IrOp_LatentDelay`
- `IrTerm_Suspend` — emitted by Stage 5 at each latent op

Stage 5 has already been implemented (BATCH-10). The IR output of Stage 5 will be the
input to Stage 6. The field layouts are NOT yet set (all `Offset = 0`, `Size = 0`).

---

## New IR Operations Needed

Before implementing Stage 6, check whether these ops exist in `IrOperation.cs`. If not,
add them:

```csharp
// AiPrimitive phase writes
public sealed record IrOp_WriteWorkingStatePhase(int PhaseValue) : IrOperation;

// Instance cursor writes
public sealed record IrOp_WriteCursorResumeAt(int ResumeAtValue) : IrOperation;
public sealed record IrOp_WriteCursorInstanceVersion : IrOperation;  // copies instanceVersion param
public sealed record IrOp_WriteCursorWaitUntilTime(IrValue Seconds) : IrOperation;

// Instance cursor check (Q-18.1) — compound check that checks staleness
// Emitter expands to: if (s.Cursor.InstanceVersion != instanceVersion) { s.Cursor.ResumeAt = 0; return; }
public sealed record IrOp_CheckCursorVersion : IrOperation;

// Field read from a component
public sealed record IrOp_FieldRead(IrValue Source, string FieldName, IrTypeRef ResultType) : IrOperation;
```

`IrOp_CheckCursorVersion` is used per Q-18.1 at the start of each resume block in Instance lowering.
`IrOp_ReadInstanceVersion` (already exists) is used to read the `instanceVersion` parameter.

---

## Implementation

### Step 1: `FieldLayout.ComputeFieldLayouts`

Create/update `Compiler/Lowering/FieldLayout.cs` per design doc §9.3:

- `Parameters` start at offset 0
- `WorkingState` starts at offset 8 (after 8-byte StructureHash header in Blackboard1024)
- `Variables` start at offset 16 (after BlueprintLatentCursor — `BlueprintLatentCursor` is 16 bytes:
  `uint ResumeAt` + `uint InstanceVersion` + `float WaitUntilTime` + 4 bytes padding = 16 bytes total)

Alignment per-field: `SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 }`.

### Step 2: `StructureHashComputation.Compute`

Create/update `Compiler/Lowering/StructureHashComputation.cs` per design doc §9.4:

```csharp
public static ulong Compute(IrAsset asset)
{
    var sb = new StringBuilder();
    sb.Append((int)asset.Dispatch).Append(';');
    AppendFields(sb, asset.Parameters);
    AppendFields(sb, asset.WorkingState);
    AppendFields(sb, asset.Variables);
    return FnvHasher.Hash64(Encoding.UTF8.GetBytes(sb.ToString()));
}

private static void AppendFields(StringBuilder sb, IReadOnlyList<IrField> fields)
{
    foreach (var f in fields)
        sb.Append(f.Name).Append('|')
          .Append(f.Type.FullName).Append('|')
          .Append(f.Offset).Append('|')
          .Append(f.Size).Append(';');
}
```

**Critical:** StructureHash is computed AFTER `ComputeFieldLayouts` (so Offset/Size are final).

### Step 3: `LibraryLowering.Apply`

Per design doc §9.9. Implement in `Compiler/Lowering/LibraryLowering.cs`:
- Scan all graphs/blocks/statements for latent ops (`IrOp_LatentDelay`, `IrOp_WaitForChannel`,
  `IrOp_WaitForEvent`). If found: emit `BP9001` internal error.
- If no function graphs exist: emit `BP5001`.
- Return asset unchanged otherwise.

Add `BP5001` and `BP9001_InternalLibraryLatent` to `DiagnosticCodes.cs` if not already there.

### Step 4: `AiPrimitiveLowering.Apply`

Per design doc §9.5, §9.6, §9.8. Implement in `Compiler/Lowering/AiPrimitiveLowering.cs`:

The algorithm for a graph with latent ops:

1. Detect `IrTerm_Suspend` terminators in the graph (produced by Stage 5).
2. Each suspend = one "phase boundary". Assign phase numbers 1..N in DFS order.
3. Add `__phase` byte field (first in WorkingState list) via `EnsurePhaseByteInWorkingState`.
4. Build the synthesized dispatch block:
   - `IrBlock dispatch_entry` with: empty statements, `IrTerm_Branch`-style switch on `workingState.__phase` (since `IrTerminator` doesn't have switch, use chained `IrTerm_Branch` nodes: phase==0 → phase0_block, else check phase==1, etc.)
   - Actually the design shows a "switch" pattern. Represent as chained branches:
     `IrTerm_Branch(phase == 0, phase0_block, check_1)` → `IrTerm_Branch(phase == 1, phase1_block, ...)`
5. For phase-0 (initial):
   - The existing phase-0 block already exists (the original entry block).
   - Append to its statements: `IrOp_WriteWorkingStatePhase(1)` (if there's 1 wait)
   - Replace `IrTerm_Suspend` with `IrTerm_ReturnStatus(NodeStatus.Running)`.
6. For each phase-N check block (the resume side):
   - The existing resume block from Stage 5 needs modification.
   - Insert at start: `IrOp_GetComponentRO(channelType, selfValue, ...)` to read channel status
   - Insert: `IrOp_FieldRead(channelRef, "Status", IrTypeRef.NodeStatus)` 
   - Add switch on status: Running → `IrTerm_ReturnStatus(Running)`, Failure → failure_path, Success → continue block
7. For LatentDelay in AiPrimitive: add `WaitUntilTime: float` field to WorkingState (via
   `EnsureWaitUntilTimeField`). Initial block: `Cursor.WaitUntilTime = time + seconds; phase=1; return Running`.
   Check block: `if (time < workingState.WaitUntilTime) return Running; else continue`.

**Simplification for BATCH-11 scope:**
Because the full lowering requires synthesizing many new IR blocks and is the most complex
part of Stage 6, it is acceptable to implement a "structural lowering" that converts
`IrTerm_Suspend` terminators to `IrTerm_ReturnStatus(Running)` and creates the dispatch
block, without necessarily implementing the full channel status check logic in the resume blocks.
The key requirement from TASK-DETAIL.md SC1 is that the output has:
- entry dispatch block (switch __phase)
- phase-0 block (command + phase=1 + Running)
- phase-1 check block (GetComponentRO + status switch: Running/Failure/Success paths)

Implement all three phases fully per SC1.

### Step 5: `InstanceLowering.Apply`

Per design doc §9.7, §9.8. Implement in `Compiler/Lowering/InstanceLowering.cs`:

Delegates to `WaitLowering_Instance.Apply(graph)` for each graph with latent ops.

`WaitLowering_Instance.Apply(graph)`:
1. Detect `IrTerm_Suspend` terminators.
2. Assign resume labels 1..N.
3. Build the dispatch entry block: switch on `state.Cursor.ResumeAt`:
   - `0` → initial block
   - `1..N` → resume blocks
4. For initial block:
   - Append: `IrOp_WriteCursorResumeAt(1)`, `IrOp_WriteCursorInstanceVersion` (captures `instanceVersion`),
     optionally `IrOp_WriteCursorWaitUntilTime` for LatentDelay
   - Replace `IrTerm_Suspend` with `IrTerm_Return(null)` (Instance Tick is void)
5. For each resume block:
   - Insert at start: `IrOp_CheckCursorVersion` (Q-18.1 staleness check)
   - For channel wait: `IrOp_GetComponentRO` + `IrOp_FieldRead("Status")` + switch on status
   - For delay wait: `if (time < state.Cursor.WaitUntilTime) return; else continue`

Per Q-18.1: `IrOp_CheckCursorVersion` emits:
```csharp
if (s.Cursor.InstanceVersion != instanceVersion) { s.Cursor.ResumeAt = 0; return; }
```

### Step 6: `DebugProbeInsertion.Apply`

Per design doc §9.11. Implement in `Compiler/Lowering/DebugProbeInsertion.cs`:

```csharp
public static IrAsset Apply(IrAsset asset, CompilerMode mode)
{
    if (mode == CompilerMode.Release) return asset;
    // For each block, if first statement's Debug.NodeId is non-null,
    // prepend IrOp_DebugProbe_NodeEnter.
    // In Trace mode, also append IrOp_DebugProbe_PinValue after value-producing stmts.
}
```

### Step 7: Wire `Stage6_Lower.Run`

Implement `Compiler/Stages/Stage6_Lower.cs` per design doc §9.2:

```csharp
internal static class Stage6_Lower
{
    public static IrAsset Run(IrAsset asset, CompilerMode mode, DiagnosticSink sink)
    {
        asset = FieldLayout.ComputeFieldLayouts(asset);
        asset = asset with { StructureHash = StructureHashComputation.Compute(asset) };

        asset = asset.Dispatch switch
        {
            BlueprintDispatchKind.Library     => LibraryLowering.Apply(asset, sink),
            BlueprintDispatchKind.AiPrimitive => AiPrimitiveLowering.Apply(asset, sink),
            BlueprintDispatchKind.Instance    => InstanceLowering.Apply(asset, sink),
            _ => throw new InvalidOperationException($"Unknown dispatch: {asset.Dispatch}")
        };

        asset = DebugProbeInsertion.Apply(asset, mode);
        return asset;
    }
}
```

Update `BlueprintCompiler.Compile` to call `Stage6_Lower.Run` after Stage 5. After Stage 6,
the pipeline throws `NotImplementedException("Stage 7 not yet implemented (CP-004)")`.

### Step 8: `SynthesizedGuids` helper

Create `Compiler/Lowering/SynthesizedGuids.cs`:

```csharp
internal static class SynthesizedGuids
{
    public static Guid PhaseField(Guid assetId)
        => Derive("phase-field", assetId.ToString());

    public static Guid WaitUntilTimeField(Guid assetId)
        => Derive("wait-until-time", assetId.ToString());

    public static Guid DispatchBlock(Guid graphId)
        => Derive("dispatch-block", graphId.ToString());

    public static Guid PhaseBlock(Guid graphId, int phase)
        => Derive("phase-block", graphId.ToString(), phase.ToString());

    private static Guid Derive(string purpose, params string[] inputs)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var s = purpose + "|" + string.Join("|", inputs);
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return new Guid(hash[..16]);
    }
}
```

---

## Adding DiagnosticCodes

Add to `DiagnosticCodes.cs` if missing:

```csharp
// Stage 6 — Lower
public const string BP5001_LibraryHasNoFunctions = "BP5001";
public const string BP9001_InternalLibraryLatent  = "BP9001";
```

---

## Success Criteria

Verify all 8 SC conditions from `TASK-DETAIL.md` TASK-CP-003:

1. **SC1:** AiPrimitive graph with one `WaitForChannelNode` produces entry dispatch block
   (switch __phase), phase-0 block (command + phase=1 + Running), phase-1 block
   (GetComponentRO + status switch), success/failure paths.
2. **SC2:** Instance graph with one `WaitForChannelNode` produces entry dispatch (switch
   Cursor.ResumeAt), initial block (ResumeAt=1, InstanceVersion=instanceVersion, return void),
   resume block (CheckCursorVersion + GetComponentRO + status switch).
3. **SC3:** `StructureHash` changes when a variable's name changes (same type, same offset).
4. **SC4:** `StructureHash` changes when a variable's type changes.
5. **SC5:** `StructureHash` does NOT change when graph body changes (only layout changes matter).
6. **SC6:** Library asset with no function graphs emits BP5001.
7. **SC7:** Debug mode inserts `IrOp_DebugProbe_NodeEnter` at start of each block with non-null NodeId.
8. **SC8:** `dotnet build` zero errors.

Also ensure baseline: `dotnet test --no-build` → 168 pass, 3 skip, 0 fail (existing tests unchanged).

---

## Notes on IrBlock Mutability

The IR types are `sealed record` — they're immutable. To "add" blocks or statements during
lowering, you need to work with mutable builders or reconstruct the records. Use the `with`
syntax to create modified copies:

```csharp
// Add statement to block
var modifiedBlock = block with {
    Statements = block.Statements.Append(newStmt).ToList()
};

// Replace terminator
var finalBlock = modifiedBlock with { Terminator = new IrTerm_ReturnStatus(NodeStatus.Running) };
```

For building a new block list during lowering, maintain a `List<IrBlock>` and only convert
to `IReadOnlyList<IrBlock>` at the end.

---

## Output

Write completion report to `.dev/blueprints-1/reports/BATCH-11-REPORT.md` with:
- List of all files created/modified
- Any deviations from these instructions, with justification
- Answers:
  1. Were the new IR ops (`IrOp_CheckCursorVersion`, `IrOp_WriteWorkingStatePhase`, etc.)
     added to `IrOperation.cs`? List what was added.
  2. How is the AiPrimitive phase dispatch block structured in the IR? (IrTerm_Branch chain
     or some other approach?)
  3. Were the TASK-DETAIL.md SC1 and SC2 verified by dedicated tests?
- Final build/test results (pass/skip/fail counts)
