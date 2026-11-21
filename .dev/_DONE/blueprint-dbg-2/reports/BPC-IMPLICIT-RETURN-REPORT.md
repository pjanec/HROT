# BPC-IMPLICIT-RETURN: implicit Return at end of an exec chain — Implementation Report

**Status:** ✅ Complete — all new tests pass, only documented pre-existing reds remain  
**Date:** 2026-06-10  
**Developer:** Claude (pjanec session)

---

## Implementation Summary

### What was built

Two compiler changes that make explicit `ReturnNode` optional in blueprint graphs:

1. **Stage5_Schedule.SealFallThrough** now synthesizes the dispatch-appropriate implicit return at genuine end-of-chain, instead of emitting a bare `IrTerm_FallThrough`.
2. **Stage2_Validate** no longer emits **BP1601** ("no ReturnNode is exec-reachable from entry") — the check is commented out because `SealFallThrough` guarantees every exec path terminates.

Additionally, **ScheduleBranchNode** was updated to seal empty branch blocks (when a branch path has no successor node) via `SealFallThrough`, so they also get the correct implicit-return terminator rather than falling through to the `BlockBuilder.Build()` default of `IrTerm_FallThrough`.

### Files changed

| File | Change |
|---|---|
| `Stage5_Schedule.cs:549-570` | `SealFallThrough` — else branches now emit `IrTerm_ReturnStatus(NodeStatus.Success)` for AiPrimitive/Library, `IrTerm_Return(null)` for Instance |
| `Stage5_Schedule.cs:470-473` | `ScheduleBranchNode` — empty branch successors now call `SealFallThrough` instead of leaving blocks unscheduled |
| `Stage2_Validate.cs:275-278` | BP1601 check replaced with explanatory comment |
| `V_AllValidatorsCoverageTests.cs:15` | BP1601 added to `KnownNotYetEmittedCodes` (code still defined, no longer emitted) |
| `V_DispatchKindCompatibilityTests.cs:317-330` | `Library_GraphWithNoReturn_EmitsBP1601` → `Library_GraphWithNoReturn_CompilesWithoutBP1601` |
| `SequenceSchedulingTests.cs:159` | `IrTerm_FallThrough` → `IrTerm_ReturnStatus` for last branch of two-branch sequence |
| `SequenceSchedulingTests.cs:659` | `IrTerm_FallThrough` → `IrTerm_ReturnStatus` for zero-connected-branches sequence |
| `BPC_ImplicitReturnTests.cs` | **New file** — 6 tests covering all dispatch kinds |
| `Snapshots/Schedule/MoveToAndFire.ir.txt` | Golden IR updated: `fall_through` → `return_status Success` |

**Total files touched:** 7 (2 compiler .cs, 4 test .cs, 1 golden snapshot .txt, +1 new test file)

---

## Per-dispatch implicit return defaults

| Dispatch kind | Implicit return terminator | Settled value |
|---|---|---|
| `AiPrimitive` | `IrTerm_ReturnStatus(NodeStatus.Success)` | Mirrors `BuildReturnTerminator` default for explicit Return |
| `Library` | `IrTerm_ReturnStatus(NodeStatus.Success)` | Same as AiPrimitive |
| `Instance` | `IrTerm_Return(null)` | Void return — mirrors Function graph default |

---

## IrTerm_FallThrough downstream consumers — findings

`IrTerm_FallThrough` is consumed in three places (none create new instances aside from one fallback):

| Consumer | File | What it does | Impact of this change |
|---|---|---|---|
| Dead-block filter (Instance) | `WaitLowering_Instance.cs:475` | Makes next block in layout order reachable for dead-block filtering | None harmful — genuine end-of-chain blocks now use Return/ReturnStatus, which are terminal and correctly do NOT keep the next block alive. Any block that relied on FallThrough for liveness has another edge (Goto/Branch/Suspend) or is genuinely dead. |
| Dead-block filter (AiPrimitive) | `WaitLowering_AiPrimitive.cs:516` | Same as Instance | Same reasoning |
| Terminator emission | `TerminatorEmitter.cs:37` | No-op — falls through to next block in generated code | Replaced by proper Return/ReturnStatus emission |
| BlockBuilder.Build() fallback | `Stage5_Schedule.cs:1696` | `Terminator ?? new IrTerm_FallThrough` — safety net for blocks with no terminator set | Still present as a safety net; no longer normally exercised for end-of-chain blocks |

**Conclusion:** No code path RELIES on a bare `IrTerm_FallThrough` meaning "continue to next block" for non-redirected end-of-chain blocks. The WaitLowering dead-block filters that follow FallThrough edges are safety nets, and replacing FallThrough with explicit Return terminators at genuine ends is the correct semantic — a return-terminated block should NOT implicitly keep the next block alive. `IrTerm_FallThrough` itself is preserved (not deleted) as the batch instructs.

---

## Test results

### Final run

```text
Failed!  - Failed:  7, Passed: 1752, Skipped: 8, Total: 1767
```

### Pre-existing reds (all 7 confirmed unchanged)

| # | Test | Reason |
|---|---|---|
| 1 | `AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` | Pre-existing golden source mismatch |
| 2 | `AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` | Pre-existing golden source mismatch |
| 3 | `Stage8_PdbContainsEmbeddedSource` | Pre-existing Roslyn/PDB issue |
| 4 | `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` | Pre-existing Roslyn/PE issue |
| 5 | `TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing allocation test |
| 6 | `MoveToAndFire_GeneratedSource_Snapshot` | Pre-existing snapshot mismatch |
| 7 | `WhenNode_ZeroAllocOnHotPath` | Pre-existing perf threshold |

### New tests — all pass (6/6)

All in `BPC_ImplicitReturnTests`:

| # | Test | What it proves |
|---|---|---|
| 1 | `Instance_VoidGraphNoReturn_EmitsImplicitVoidReturn` | Instance dispatch: `IrTerm_Return(null)` synthesized |
| 2 | `AiPrimitive_NoReturn_EmitsImplicitSuccessReturn` | AiPrimitive: `IrTerm_ReturnStatus(Success)` synthesized |
| 3 | `Branch_EarlyExitReturn_AndImplicitFallOff_CompileCorrectly` | Branch — explicit Return(Failure) + implicit fall-off both work; empty branch blocks sealed correctly |
| 4 | `AiPrimitive_ExplicitFailureReturn_NotOverriddenByImplicit` | Explicit Return(Failure) honored, not overridden by implicit Success |
| 5 | `Instance_ExplicitValueReturn_PreservesReturnValue` | Function graph — explicit Return with output data pin preserves value |
| 6 | `Library_NoReturn_EmitsImplicitSuccessReturn` | Library dispatch: same as AiPrimitive |

### Tests updated to match new semantics

| Test | Old assertion | New assertion |
|---|---|---|
| `Schedule_TwoSequenceBranches_ChainsInOrder_NoBP1412` | `IrTerm_FallThrough` on last branch | `IrTerm_ReturnStatus` on last branch |
| `Schedule_ZeroConnectedBranches_SealsFallThrough_NoBP1412` | `IrTerm_FallThrough` | `IrTerm_ReturnStatus` |
| `Library_GraphWithNoReturn_EmitsBP1601` | `Assert.Contains(BP1601)` | `Assert.DoesNotContain(BP1601)` |
| `Schedule_ProducesExpectedIr("MoveToAndFire")` | Golden had `fall_through` | Golden now has `return_status Success` |

---

## Golden/snapshot changes — justification

**MoveToAndFire.ir.txt ONLY changed.** LibraryMath and InstanceCounter were unaffected (they either have empty graphs or already have explicit Returns).

The change is exactly what's expected:

```
 Block 0 (entry):
-  fall_through
+  return_status Success
```

MoveToAndFire is an AiPrimitive blueprint with a single EventEntryNode and no explicit ReturnNode. Before this change, the block terminated with bare `IrTerm_FallThrough`. Now it terminates with `IrTerm_ReturnStatus(NodeStatus.Success)` — the correct implicit return for AiPrimitive dispatch.

---

## Design decisions

1. **ScheduleBranchNode also seals empty branches.** When a Branch path has no successor, the allocated block was never enqueued in the BFS queue, so it was never scheduled. It defaulted to `IrTerm_FallThrough` from `BlockBuilder.Build()`. Now `ScheduleBranchNode` explicitly calls `SealFallThrough(trueBlock/falseBlock)` when the corresponding successor is null. This ensures both branch arms get the correct dispatch-appropriate terminator even when one arm is empty.

2. **BP1601 added to KnownNotYetEmittedCodes rather than deleted.** The diagnostic code `BP1601` remains defined in `DiagnosticCodes` (for forward compatibility / documentation), but is no longer emitted by any stage. Adding it to the `KnownNotYetEmittedCodes` set keeps the coverage ratchet happy without false positives.

3. **Preserved `with { Debug = debug }` pattern for debug annotations.** When `SealFallThrough` receives a non-null `IrDebugAnnotation`, it's attached to the synthesized terminator (mirroring the original code's behavior of attaching it to `IrTerm_FallThrough`).

---

## Deviations from spec

None. The implementation follows the prescribed fix exactly. The only extension is the `ScheduleBranchNode` empty-branch sealing, which was necessary because SealFallThrough only applies to blocks that ARE scheduled — empty branch blocks were allocated but never scheduled, and would have defaulted to FallThrough from BlockBuilder.Build().

---

## Integration notes

- Explicit `ReturnNode` support is untouched — `BuildReturnTerminator` and the `ReturnNode` case in `ScheduleBlock` remain unchanged.
- Sequence branch chaining (fall-through redirect via `_fallThroughTarget`) is untouched — the `IrTerm_Goto` path in `SealFallThrough` is preserved.
- Latent/resume block fall-through propagation is preserved — the resume block's SealFallThrough call now produces the implicit return instead of FallThrough, which is the intended behavior (a latent node without a continuation is a genuine end of a path).
- `IrTerm_FallThrough` itself is NOT deleted — it remains available for future use cases or as a safety net in `BlockBuilder.Build()`.
