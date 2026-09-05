# BPC-IMPLICIT-RETURN: implicit Return at end of an exec chain (explicit Return optional, only for early-exit / non-default status)

**Type:** Compiler feature (single objective; TRY Zoo `pro`, hard-review, fall back to sonnet if it struggles)   **Est:** ~5h
**Onboarding:** `.dev/.guides/DEV-GUIDE.md` (your working contract). Touch the compiler only. One objective.

## Goal (user request)
Today every graph MUST contain an explicit `ReturnNode` reachable from entry, or compilation fails (`BP1601`). Make Return **optional**: when an exec chain simply reaches its end (a node with no exec successor and no fall-through redirect), the compiler **synthesizes an implicit return**. Explicit `ReturnNode` stays supported for (a) **early exit** mid-chain (already works — a Return anywhere terminates that path) and (b) a **non-default status / value** (e.g. `Failure`, or a Function graph returning an output value).

## Verified facts (do not re-derive; the machinery mostly exists)
- **Validation blocker:** `Stage2_Validate` emits error **BP1601** when no `ReturnNode` is exec-reachable from entry — `Stage2_Validate.cs:266-278`. This is the ONLY thing forcing an explicit Return.
- **Genuine end-of-chain already has a hook:** `Stage5_Schedule.SealFallThrough(blockId, bb, debug)` (`Stage5_Schedule.cs:549`) is called whenever an exec chain ends. It emits `IrTerm_Goto` if a fall-through **redirect** is registered (`_fallThroughTarget`, used by Sequence branch chaining), ELSE bare `IrTerm_FallThrough`. So **a bare `IrTerm_FallThrough` == a genuine end of an exec path** (Sequence redirects are already `Goto`, not bare FallThrough).
- **Return terminator shape per dispatch** (`Stage5_Schedule.BuildReturnTerminator`, `:1128`): AiPrimitive/Library → `IrTerm_ReturnStatus(rn.Status)`; Function/Instance → `IrTerm_Return(retVal?)` (value from the Return's output pin, or null = void).
- IR terminators (`Compiler/Ir/IrBlock.cs`): `IrTerm_Return(IrValue?)`, `IrTerm_ReturnStatus(NodeStatus)`, `IrTerm_FallThrough`.

## The fix (prescribed)
1. **Synthesize the implicit return at genuine end-of-chain.** In `Stage5_Schedule.SealFallThrough`, the branch that currently emits a bare `IrTerm_FallThrough` (no `_fallThroughTarget` redirect) must instead emit the **dispatch-appropriate implicit return**, mirroring `BuildReturnTerminator`'s default:
   - `AssetDispatchKind.AiPrimitive` or `AssetDispatchKind.Library` → `new IrTerm_ReturnStatus(NodeStatus.Success)`.
   - otherwise (Function/Instance) → `new IrTerm_Return(null)` (void implicit return).
   Keep the `_fallThroughTarget` → `IrTerm_Goto` path UNCHANGED (Sequence branch chaining must still fall through to the next branch).
2. **Relax `BP1601`.** Remove (or downgrade to non-error) the "no ReturnNode reachable" error in `Stage2_Validate.cs:275-278`, since every exec path now terminates via an explicit Return OR an implicit one. Keep any other Return-related validation (e.g. condition `Return Running` forbidden, BP1100) intact.
3. **Verify `IrTerm_FallThrough` downstream consumers.** `IrTerm_FallThrough` is referenced in block-ordering / WaitLowering (e.g. `WaitLowering_Instance.cs` `case IrTerm_FallThrough` enqueues the next block). After step 1, bare FallThrough should no longer be produced for genuine ends. Confirm no code path RELIES on a bare FallThrough meaning "continue to the next block in layout order" for a non-redirected block — if it does, preserve that behavior. Document what you found. Do NOT delete `IrTerm_FallThrough` itself.

Do NOT change `BuildReturnTerminator`, the explicit `ReturnNode` path, Sequence redirect chaining, or latent lowering semantics beyond the SealFallThrough terminator.

## Tests required (compile + run via the test harness; assert REAL behavior, not just "no BP1601")
Cover each dispatch kind. Reuse existing compiler/fixture test patterns (e.g. `BlueprintAssetBuilder`, `BlueprintTestFixture`).
1. **Void Instance graph, no Return:** `Entry → SetVariable(X=7)` (no ReturnNode). Compiles with NO BP1601 error; runs one tick; `X == 7` after. Proves implicit void return at end-of-chain.
2. **AiPrimitive action, no Return:** an AiPrimitive graph whose chain ends without a Return → compiles; the generated action returns **`NodeStatus.Success`** (assert the emitted terminator/return is Success — e.g. generated source returns Success, or run and assert the action's status).
3. **Explicit early-exit Return still works:** a Branch where one path hits an explicit `Return` mid-chain and the other falls off the end (implicit return) — both compile and behave (early path returns at the Return; fall-off path implicitly returns).
4. **Explicit non-default status still honored:** an AiPrimitive with an explicit `Return Failure` emits Failure (not overridden by the implicit-Success default).
5. **Function graph with output value + explicit Return** still returns the value (regression — implicit return must not interfere with explicit value returns).
6. **Regression:** existing compiler/Stage2/Stage5/emit tests green; the documented pre-existing reds unchanged.

## Do-not-stop-until-green
Run the FULL affected suite (no `BLUEPRINT_REGENERATE_SNAPSHOTS`, no regen flags) and loop until `Failed: 0` except the documented pre-existing reds (`AiPrimitive_EmitMatchesGoldenSource` ×2, `Stage8_*` ×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`):
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
NOTE: relaxing BP1601 may FIX some currently-skipped/negative tests or change golden output — if a golden/snapshot test now differs because implicit-return changed generated code, that is a REAL change: do NOT regen blindly; inspect the diff and confirm it is the intended implicit-return terminator before updating the golden, and explain it in the report. Any NEW failure unrelated to that ⇒ root-cause it. Transient `MapKeyboardKey.idl` build error ⇒ re-run.

## Constraints
- Touch `Stage5_Schedule.cs` (SealFallThrough), `Stage2_Validate.cs` (BP1601), and test files only. Do NOT exclude assets, suppress diagnostics, or weaken existing tests. Do NOT commit any `.bp.json`.
- Do NOT commit. Report → `.dev/_DONE/blueprint-dbg-2/reports/BPC-IMPLICIT-RETURN-REPORT.md` (the SealFallThrough change with the per-dispatch default, the BP1601 relaxation, what you found about FallThrough consumers, any golden-output changes + justification, exact test counts). The lead reviews and commits.
