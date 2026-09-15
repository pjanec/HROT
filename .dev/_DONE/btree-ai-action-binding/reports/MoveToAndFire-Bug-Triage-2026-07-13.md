# MoveToAndFire Bug Triage — 7 Interacting Bugs (Read-Only Analysis)

> **Date:** 2026-07-13
> **Workstream:** btree-ai-action-binding
> **Scope:** `Hrot.Blueprints.Tests.Compiler.MoveToAndFire_BTreeTick_Tests` (2 skipped `[Fact]`s, `MoveToAndFire_EndToEndTests.cs:101,116`)
> **Method:** read-only source/IR/emit inspection + non-destructive runtime probe (no repo files modified; `git status` clean throughout)
> **Headline finding:** **all 7 listed bugs are already fixed in the current tree.** The skip attributes are stale documentation debt, not an accurate description of current behavior. A standalone reflection probe running the exact steps of both skipped tests against the current build produced `Tick1 = Running`, `Tick2 = Success` — i.e. both tests would pass unmodified today.

## 0. Method note — how "already fixed" was verified without editing source

The task is read-only, so the skipped tests could not be un-skipped in place to observe them run. Instead:
1. Read `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/MoveToAndFire.bp.json`, the committed IR/emit snapshots, and every stage of the compiler pipeline named in the bug list.
2. Built a throwaway console probe in the scratchpad directory (never touched the repo) that loads the already-built `Hrot.Blueprints.Tests.dll` via `Assembly.LoadFrom` and drives `BlueprintTestFixture` purely through reflection, replaying **exactly** the two skipped tests' bodies: `CompileAndLoad` → `CreateEntity` → add `LocomotionChannel` → `InvokeBTreeAction` (tick 1) → set channel `Status = Success` → `InvokeBTreeAction` (tick 2).
3. Result:
   ```
   Tick1 status: Running
   Tick2 status: Success
   PROBE COMPLETE - NO EXCEPTIONS
   ```
   This is precisely what `BTreeTick_FirstCall_ReturnsRunning_WhenChannelIsIdle` and `BTreeTick_AfterChannelComplete_ReturnsSuccess` assert. `git status --short` was empty before and after — the probe lived entirely under `/tmp/.../scratchpad/probe` and only referenced the pre-existing build output.
4. Also ran the non-skipped suite (`dotnet test --filter FullyQualifiedName~MoveToAndFire`): 12 passed, 2 skipped (the two under triage), 1 failed — and that one failure (`MoveToAndFire_GeneratedSource_Snapshot`) is an unrelated **stale golden-snapshot diff** (current codegen now also emits `DebugProbe.NodeEnter(...)` calls the committed `Snapshots/Demos/MoveToAndFire.cs.txt` predates), not a functional regression.

Each bug below is triaged against the **current source**, not the (now outdated) description in the Skip reason.

## 1. Bug 1 — Stage5 `GetSingleExecSuccessor` / empty-Pins BFS termination

**As described:** "Stage5 `GetSingleExecSuccessor` returns null for JSON nodes with empty Pins — BFS terminates after EventEntry, producing empty TickCore body (returns Failure immediately)."

**Current status: FIXED.** `MoveToAndFire.bp.json` nodes are indeed authored with `"Pins": []` (confirmed at lines 54, 62, 69, 75). But `Stage0_Rehydrate.cs` now runs as **the first pass** of `BlueprintCompiler.Compile` (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/BlueprintCompiler.cs:54`, `Stage0_Rehydrate.Run(asset, options);` — before `Stage2_Validate`) and rebuilds every pin-less node's `Pins` list from the node-registry's static shapes plus link-derived GUIDs (`Stage0_Rehydrate.cs:39-83`, `BuildCanonicalPins`/`AssignLinkGuids`). `BuiltInNodeRegistry.GetStaticPins` (`Compiler/Catalogs/BuiltInNodeRegistry.cs:51-52`) explicitly maps `ChannelCommandNode` and `WaitForChannelNode` to `{ExecIn(), ExecOut()}`, so by the time `Stage5_Schedule.GetSingleExecSuccessor` (`Stage5_Schedule.cs:1621`) runs, `node.Pins` is fully populated and BFS walks the whole EventEntry→ChannelCommand→WaitForChannel→Return chain.
- **Symptom if this regressed:** `TickCore` body would be `{ }` followed by `return Fbt.NodeStatus.Failure;` (this is exactly what the *separate*, Stage0-bypassing `AiPrimitiveEmitGoldenTests`/`Snapshots/Emit/MoveToAndFire.cs.txt` golden test still pins as its baseline — that test calls `Stage2_Validate.Run` directly without `Stage0_Rehydrate`, so it is a deliberately-frozen "raw IR" fixture, not evidence of a live bug).
- **Classification:** MECHANICAL (was; now moot — already landed).
- **Fix location:** `Stage0_Rehydrate.cs` (whole file, esp. lines 39-83, 118-121 case `WaitForChannelNode`/`ChannelCommandNode` fall through to the registry-driven default path).
- **Proof:** `Snapshots/Demos/MoveToAndFire.cs.txt` (captured through the *full* pipeline) shows a fully-populated `TickCore` with 7 blocks, not an empty body; runtime probe confirms `Tick1 = Running`.

## 2. Bug 2 — `IrOp_ChannelCommand.ChannelComponentTypeFqn` short-name emission

**As described:** "uses short name (\"LocomotionChannel\") → emits invalid `global::LocomotionChannel` (needs full FQN from catalog)."

**Current status: FIXED.** `Stage5_Schedule.cs:1051` resolves `cc.ChannelType` through `ResolveChannelTypeFqn` (`Stage5_Schedule.cs:1218-1240`), which scans `_ctx.ChannelCommands.GetEntries()` (the injected `BuiltInChannelCommandCatalog`) for an exact or short-name match and returns the entry's full `ChannelTypeFqn`. The catalog is populated (`Compiler/Catalogs/BuiltInChannelCommandCatalog.cs:18`: `new("MoveTo", "Fdp.Toolkit.Behavior.Components.LocomotionChannel", 1, "Fdp.Toolkit.Navigation.MoveToParams")`), so `IrOp_ChannelCommand.ChannelComponentTypeFqn` (record field defined at `Compiler/Ir/IrOperation.cs:110`) already carries the full FQN by the time it reaches emission.
- **Classification:** MECHANICAL (already landed).
- **Fix location:** `Stage5_Schedule.cs:1051` + `BuiltInChannelCommandCatalog.cs:18`.
- **Proof:** generated code emits `world.HasComponent<global::Fdp.Toolkit.Behavior.Components.LocomotionChannel>(self)` — confirmed in the fresh `dotnet test` run's captured actual-output diff and in `Snapshots/Demos/MoveToAndFire.cs.txt:60-64`.

## 3. Bug 3 — `ActionId = "MoveTo"` emitted verbatim

**As described:** "`ActionId = \"MoveTo\"` is emitted verbatim → invalid `__ch.ActiveAction = MoveTo;` (needs numeric value from catalog lookup)."

**Current status: FIXED.** `Stage5_Schedule.cs:1048-1058` looks up `cc.ActionId` ("MoveTo") in the same channel-command catalog and builds `actionIdLiteral` as `$"(ushort){catalogEntry.ActionId} /* {cc.ActionId} */"` — a valid ushort literal with the human-readable name preserved only as a trailing C# comment (so IR-level string assertions like `Contains("MoveTo")` still pass without corrupting the emitted rvalue).
- **Classification:** MECHANICAL (already landed).
- **Fix location:** `Stage5_Schedule.cs:1048-1058` (feeds `IrOp_ChannelCommand.ActionIdConstantName`, `IrOperation.cs:111`).
- **Proof:** generated code emits `__ch_0.ActiveAction = (ushort)1 /* MoveTo */;` — matches catalog's `ActionId=1` for `"MoveTo"` on `LocomotionChannel`.

## 4. Bug 4 — `IrOp_PureCall("op_Eq_NodeStatus", ...)` → invalid `global::op_Eq_NodeStatus(...)`

**Current status: FIXED.** `WaitLowering_AiPrimitive.cs` still *synthesizes* IR-level pseudo-calls named `op_Eq_NodeStatus` (e.g. lines 213, 247, 345, 381 — this part of the bug report's premise is accurate at the IR layer). The fix is one layer down, in emission: `StatementEmitter.EmitOp`'s `IrOp_PureCall` case (`Compiler/Emit/StatementEmitter.cs:80-100`) calls `TryGetSynthesizedOpInfix` (`StatementEmitter.cs:773-855`) *before* falling back to a `global::{fqn}(...)` call. `TryGetSynthesizedOpInfix` recognizes the `op_<Operation>_<Type>` naming convention, maps `Eq`→`==`, and for the `NodeStatus` type suffix specifically emits `((int)__tX == (int)__tY)` (`StatementEmitter.cs:836-839`) instead of a method call.
- **Classification:** MECHANICAL (already landed).
- **Fix location:** `StatementEmitter.cs:80-100` (interception point) + `StatementEmitter.cs:767-855` (`TryGetSynthesizedOpInfix`).
- **Proof:** generated code line `var __t8 = ((int)__t6 == (int)__t7);` — no `global::op_Eq_NodeStatus` anywhere in the emitted source.

## 5. Bug 5 — `IrOp_Const("NodeStatus.Running", ...)` unqualified

**As described:** "emits unqualified `NodeStatus.Running` — unresolved."

**Current status: FIXED.** `StatementEmitter.cs:28-37`, the `IrOp_Const` case, special-cases any literal starting with `"NodeStatus."` and rewrites it to `global::Fbt.{literal}` before emission (`literal.StartsWith("NodeStatus.") ? $"global::Fbt.{op.CSharpLiteral}" : op.CSharpLiteral`). `WaitLowering_AiPrimitive.cs` still constructs the raw string `"NodeStatus.Running"` / `"NodeStatus.Failure"` (e.g. lines 212, 246, 344, 380) — again, IR-level premise correct, emission-level fix already in place.
- **Classification:** MECHANICAL (already landed).
- **Fix location:** `StatementEmitter.cs:28-37`.
- **Proof:** generated code line `var __t7 = global::Fbt.NodeStatus.Running;`.

## 6. Bug 6 — `IrOp_PureCall("op_Eq_Byte", ...)` → invalid `global::op_Eq_Byte(...)`

**Current status: FIXED.** Same mechanism as Bug 4: `TryGetSynthesizedOpInfix` matches `op_Eq_Byte` (type suffix `"Byte"`, not `"NodeStatus"`) and falls through to the generic two-arg infix path (`StatementEmitter.cs:842`): `"(__tX == __tY)"` — a plain `byte == byte` comparison, valid C#.
- **Classification:** MECHANICAL (already landed).
- **Fix location:** `StatementEmitter.cs:773-844` (same function as Bug 4, generic branch).
- **Proof:** generated code line `var __t3 = (__t1 == __t2);` where `__t1` is `ws.__phase` (byte) and `__t2` is a byte-typed `0` constant.

## 7. Bug 7 — `Fbt.NodeStatus.Success=1` vs `Hrot...Assets.NodeStatus.Failure=1` enum-value collision

**As described:** "enum mismatch makes the not-running branch route Success→Failure at runtime."

**Current status: MITIGATED / NOT REACHABLE as described.** Two independent facts:
1. All generated runtime code uses a **single** enum consistently — `global::Fbt.NodeStatus` — for every comparison and every `TickCore`/`BTreeTick` return value (`AiPrimitiveEmitter.cs:105` return type, `StatementEmitter.cs:33` qualifies constants to `global::Fbt.*`). The compile-time-only `Hrot.Blueprints.Core.Assets.NodeStatus` enum (used by test-fixture code and `ReturnNode.Status` authoring) never appears inside generated `TickCore` bodies — `BuildReturnTerminator` (`Stage5_Schedule.cs:1246-1262`) converts the authored `ReturnNode.Status` into an `IrTerm_ReturnStatus` that emission renders through the same `global::Fbt.NodeStatus` literal path, so no cross-enum comparison is ever emitted for this asset.
2. Where the emitter *does* compare a `Fbt.NodeStatus` value against a synthesized constant (the `op_Eq_NodeStatus` sites from Bug 4), both sides are cast to `int` first (`StatementEmitter.cs:838`), which is defensive against exactly this class of bug even if a second enum ever entered the comparison.
- The only place the *test-only* `Hrot...Assets.NodeStatus` and `Fbt.NodeStatus` meet is in `BlueprintTestFixture.InvokeBTreeAction` (`BlueprintTestFixture.cs:529`), which converts the generated method's raw `Fbt.NodeStatus` return value to the assets-side `NodeStatus` **by name** (`Enum.Parse(typeof(NodeStatus), rawStatus.ToString())`), which is name-safe regardless of numeric value collisions — and in the second skipped test's own body, which explicitly casts `(Fbt.NodeStatus)(int)NodeStatus.Success` to write the channel field, exercising exactly the seam the bug worried about.
- **Classification:** MECHANICAL (already landed; the fix is "don't cross the enums in codegen," which is what happened).
- **Fix location:** `StatementEmitter.cs:32-34`, `:838`; `Stage5_Schedule.cs:1246-1262`.
- **Proof:** runtime probe — after `SetChannelStatus<LocomotionChannel>(entity, Fbt.NodeStatus.Success)`, `Tick2` correctly returns `Success` (not `Failure`), i.e. the not-running branch's `Fbt.NodeStatus.Failure` check does not misfire.

## 8. The "known top-level defect" (BehaviorRegistry vs ActionRegistry) — separately real, but does not block these tests

The task brief also names a distinct, still-open defect: AiPrimitive thunks register into `Fdp.Toolkit.Behavior.BehaviorRegistry`'s int-keyed dictionaries (`CSharpEmitter.EmitAiPrimitiveRegistration`, `Compiler/Emit/CSharpEmitter.cs:207-231`, e.g. line 221 `behReg.RegisterAction({className}.BlueprintId, "{asset.Name}", {className}.BTreeTick);`), while the FastBTree `Interpreter` resolves method names via a completely different type, `ActionRegistry<BrainBlackboard,BTreeContext>.TryGetAction` (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs:703`). This is the subject of `DEBT-AIB-025`/`005`/`009` and the pending **I1** ("route AiPrimitive registration into ActionRegistry") architecture decision.

**This defect is real but irrelevant to the two skipped tests.** `BlueprintTestFixture.InvokeBTreeAction` (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs:510-535`) never goes through `BehaviorRegistry`, `ActionRegistry`, or the FastBTree `Interpreter` at all — it looks up the generated `..._Bp` type via reflection and calls `TickCore` directly (`tickCore.Invoke(null, args)`, line 526). It is a compiler/codegen integration test, not a FastBTree-interpreter integration test. The I1 decision only matters once a *JSON-authored BTree asset* tries to dispatch to `MoveToAndFire` by name through the real `Interpreter`/`ActionRegistry` path — a different, larger demo than the one gated by these two `[Fact]`s.

## Summary table

| # | Symptom (as described in Skip reason) | Root cause file:line (current) | Class | Fix sketch | Risk/Effort | Proof |
|---|---|---|---|---|---|---|
| 1 | BFS terminates after EventEntry; empty `TickCore`, returns Failure | Already fixed by `Stage0_Rehydrate.cs` (run at `BlueprintCompiler.cs:54`), populating `node.Pins` via `BuiltInNodeRegistry.cs:51-52` before `Stage5_Schedule.GetSingleExecSuccessor` (`Stage5_Schedule.cs:1621`) runs | MECHANICAL (landed) | n/a — already done | — | `Snapshots/Demos/MoveToAndFire.cs.txt`; runtime probe Tick1=Running |
| 2 | `global::LocomotionChannel` (short name) | Already fixed by `ResolveChannelTypeFqn` (`Stage5_Schedule.cs:1218-1240`) + populated `BuiltInChannelCommandCatalog.cs:18` | MECHANICAL (landed) | n/a | — | Generated `global::Fdp.Toolkit.Behavior.Components.LocomotionChannel` |
| 3 | `__ch.ActiveAction = MoveTo;` (bare identifier) | Already fixed by catalog-driven `actionIdLiteral` (`Stage5_Schedule.cs:1048-1058`) | MECHANICAL (landed) | n/a | — | Generated `(ushort)1 /* MoveTo */` |
| 4 | `global::op_Eq_NodeStatus(...)` | Already fixed by `TryGetSynthesizedOpInfix` NodeStatus branch (`StatementEmitter.cs:836-839`) | MECHANICAL (landed) | n/a | — | Generated `((int)__t6 == (int)__t7)` |
| 5 | Unqualified `NodeStatus.Running` | Already fixed by `IrOp_Const` FQN-qualify (`StatementEmitter.cs:32-34`) | MECHANICAL (landed) | n/a | — | Generated `global::Fbt.NodeStatus.Running` |
| 6 | `global::op_Eq_Byte(...)` | Already fixed by `TryGetSynthesizedOpInfix` generic branch (`StatementEmitter.cs:842`) | MECHANICAL (landed) | n/a | — | Generated `(__t1 == __t2)` |
| 7 | Cross-enum Success/Failure=1 collision misroutes branch | Not reachable: codegen only ever emits `global::Fbt.NodeStatus` (`StatementEmitter.cs:32-34`, `:838`; `Stage5_Schedule.cs:1246-1262`) | MECHANICAL (landed) | n/a | — | Runtime probe Tick2=Success after setting channel Success |
| — | (context) `BehaviorRegistry`/`ActionRegistry` mismatch | `CSharpEmitter.cs:207-231` vs `Interpreter.cs:703` | DESIGN-BLOCKED (I1, DEBT-AIB-025/005/009) | Route AiPrimitive registration into `ActionRegistry<BrainBlackboard,BTreeContext>` | L | N/A — does not gate these 2 tests (bypassed by `InvokeBTreeAction`'s direct `TickCore` reflection call) |

## Dependencies between the bugs

None of bugs 1–7 depend on each other for *these two tests* — each was an independent point-fix at a different pipeline stage (Stage0 pin rehydration; Stage5 catalog lookups ×2; StatementEmitter operator/constant qualification ×3; and a non-issue for #7). They all happen to be exercised by the same tiny 4-node graph, which is why the original bug list reads like a chain, but nothing here is a "must fix N before M" ordering — they were evidently fixed together in one pass (all trace to the same `Stage0_Rehydrate.cs` / `StatementEmitter.cs` / catalog-population work), which is consistent with the DEBT-TRACKER's `DEBT-AIB-006` Slice-2 kickoff-verification commit that first brought these files into this shape.

The one genuine dependency is architectural, not among the 7 bugs: the **I1 registration-wire decision** (item 8 above) is unrelated to unblocking these two unit tests, but *is* a prerequisite for any demo that dispatches to `MoveToAndFire` through a real JSON `BTree` asset via the FastBTree `Interpreter` (i.e., `DEBT-AIB-025`'s "full BTree-node→blueprint-`TickCore` composition"). That is a materially bigger, unrelated piece of work and should not be conflated with these two tests.

## Recommended tonight vs morning

**Tonight (safe, mechanical, zero architecture risk):**
- Remove the `Skip = "..."` argument from both `[Fact]` attributes in `MoveToAndFire_EndToEndTests.cs:99-100` and `:115-116` (and delete/rewrite the now-inaccurate 7-bug comment at lines 86-98). This is a one-line-per-test change with **no production code touched** — the bug list it references is stale, not aspirational. Based on the runtime probe, both tests should go green immediately.
- Re-run `dotnet test --filter FullyQualifiedName~MoveToAndFire` after un-skipping to get a real, in-repo confirmation (the probe used here was a scratch/throwaway harness outside the tracked test project and should not be treated as a substitute for an actual CI run).
- Separately: refresh the stale `Snapshots/Demos/MoveToAndFire.cs.txt` / `Snapshots/Demos/LibraryMath.cs.txt`-style snapshot (currently failing `MoveToAndFire_GeneratedSource_Snapshot` only because of newly-added `DebugProbe.NodeEnter` lines, unrelated to this triage) via `BLUEPRINT_REGENERATE_SNAPSHOTS=1`, if that snapshot test is in scope for tonight's cleanup.
- No new architecture, no catalog changes, no emitter changes required — this is pure "delete stale debt" work.

**Morning / needs sign-off first:**
- The **I1 registration-wire decision** (route `BehaviorRegistry` AiPrimitive registrations into `ActionRegistry<BrainBlackboard,BTreeContext>` so a real JSON-authored BTree can dispatch to a blueprint-authored `MoveToAndFire`-style action through the FastBTree `Interpreter`) is unrelated to these two tests passing, but is the real remaining gap for the *end-to-end production demo* implied by `DEBT-AIB-025`. That is L-effort, cross-cuts `CSharpEmitter.cs`, `Interpreter.cs`, and whatever registrar wiring currently populates `ActionRegistry` instances, and should go through the normal architecture-review path per the DEBT-TRACKER rather than being done autonomously overnight.
- Once un-skipped and green, consider whether `MoveToAndFire_EndToEndTests.cs`'s now-stale bug-list comment should be replaced with a short "historical — fixed, see MoveToAndFire-Bug-Triage-2026-07-13.md" note rather than deleted outright, so the CP-Phase5 history isn't lost.
