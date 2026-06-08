# CF3-REPORT — Reconcile tests + editor gating + cleanup

**Date:** 2026-06-08
**Branch:** `blueprint-integ-1`

## Summary

✅ CF-3 complete. All three sub-tasks done:
1. Fixed 2 ProbeFormatIntegrationTests (updated for block-based probing)
2. Added editor breakpoint gating via DebugMap (`IsNodeBreakpointable`)
3. Removed DiagLog diagnostics and bp-diag.log

## Files changed

| File | Change |
|---|---|
| `IBlueprintDebugSession.cs` | Added `IsNodeBreakpointable(Guid, Guid, Guid) -> bool` to interface |
| `BlueprintDebugSession.cs` | Implemented `IsNodeBreakpointable`; removed DiagLog/`_diagCount`/`_diagLogPath` and 4 DiagLog calls |
| `BlueprintDocumentFactory.cs` | F9 handler `isEnabled` now checks `debugSession.IsNodeBreakpointable` |
| `CapturingDebugSession.cs` | Implemented `IsNodeBreakpointable` |
| `MockDebugSession.cs` | Implemented `IsNodeBreakpointable` (always true) |
| `DebugWindowDrawUITests.cs` | Implemented `IsNodeBreakpointable` on SpyDebugSession |
| `ProbeIntegrationTests.cs` | Updated `BuildProbeAsset` to return entry node ID; renamed `branchNodeId`→`probeNodeId` |
| `bp-diag.log` | **DELETED** |

## Test results

### CF-3-specific tests: PASS ✅
```
CF1_NodeIdentityDiagnosticsTests           ✅
CF2_AuthoredIdProbeTests (x6)              ✅
ProbeFormatIntegrationTests (x2)           ✅ (were broken by CF-2, now fixed)
Stage6_DebugProbe_InsertsNodeEnterInDebug  ✅
ToggleBreakpoint_Command_Registered        ✅ (was broken by gating, now fixed)
```

### Full suite: 8 failures (down from 22 post-CF2, 26 pre-CF2)
All remaining failures are pre-existing (golden/snapshot/perf/PDB):
- `AiPrimitiveEmitGoldenTests` (x2)
- `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`
- `WhenNodePerfTests.WhenNode_ZeroAllocOnHotPath`
- `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
- `Stage8Tests` (x2)

### Test expectation changes documented

| Test | Old expectation | New expectation | Reason |
|---|---|---|---|
| `CompiledProbe_EmitsNodeId_InDFormat` | Probe fires for Branch node | Probe fires for EventEntry (entry block owner) | CF-2: `SourceNodeId` on entry block takes priority over `Statements[0].NodeId` |
| `Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring` | Breakpoint set on Branch fires | Breakpoint set on EventEntry fires | Same as above |

### Editor gating behavior
- Before compile (no DebugMap): all nodes are breakpointable (optimistic)
- After compile (DebugMap registered): only nodes with DebugMap entries can be toggled
- F9 menu item is disabled (grayed out) when no breakpointable nodes are selected

### DiagLog cleanup: ✅
- `grep -r DiagLog` → 0 matches in `.cs` files
- `grep -r _diagCount` → 0 matches
- `bp-diag.log` deleted

## Commands
```
dotnet build IOS-IG-SimHost.sln -c Debug     → 0 errors
dotnet test ...Blueprints.Tests -c Debug      → 8 failed (1664 pass, 8 skip)
```
