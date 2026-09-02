# Paste-ready prompt — Batch CF-1 (Zoo)

> Paste everything below the line into Zoo. It is self-contained.

---

You are implementing **Batch CF-1** in the repo `IOS-IG-SimHost-FDP-2` on branch `blueprint-integ-1`.

**First, read your working contract:** `.dev/.guides/DEV-GUIDE.md` — follow it exactly (build/test gates, reporting,
no snapshot regeneration, do not weaken tests). Then read the full batch spec: `.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md`
→ section **"Batch CF-1 — Ground-truth diagnostic"** (under "CORRECTIVE BATCHES (CF)").

## Mission

Blueprint node breakpoints set in the editor never pause the sim. The cause is a node-identity mismatch: the node
ID the editor sets a breakpoint with is not the node ID the compiled/running blueprint fires `OnNodeEnter` probes
with. **CF-1 changes NO production code.** It is a pure diagnostic that produces an authoritative report so the
following fix batch (CF-2) has exact targets.

## Task

Create one xUnit test file:
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF1_NodeIdentityDiagnosticsTests.cs`

It must:

1. Load the asset `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count4.bp.json` and compile it with
   `CompilerMode.Debug`. Find how existing compiler tests load a `.bp.json` and obtain the `CompileResult`
   (look in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage7_EmitTests/` and
   `.../Compiler/` for the fixture/helper that deserializes an asset and calls the compiler — reuse it, do not
   invent a new loader).

2. From the compile result, write a Markdown report to
   `.dev/_DONE/blueprint-dbg-1/reports/CF1-NODE-IDENTITY-REPORT.md` (also echo via `ITestOutputHelper`) containing:
   - **Table A — DebugMap entries:** every `CompileResult.DebugMap.Entries` row → `NodeId` (D format), `NodeKind`,
     `DisplayName`, `StartLine`.
   - **Table B — authored nodes:** every node in the asset's graph(s) → `Id` (D format), `Kind`, and a column
     "DebugMap entry keyed by this exact authored Id? YES/NO".
   - **Table C — emitted probes:** every `DebugProbe.NodeEnter(self, "<id>")` literal found in the generated C#
     source (regex the emitted source string from the compile result).
   - **Section D — losses:** for each authored node that is exec-reachable but has NO matching DebugMap entry and
     NO matching `NodeEnter` literal under its authored id (expected: Sequence
     `da9a9c0b-25f8-4a81-9a52-75c715456f18`, Delay `0b561966-b00b-4c84-a1a0-87042220ba9f`), report the synthesized
     id that appears instead and the `IrDebugAnnotation.Synthesized` tag(s) on the statements of that node's block.
     (To see the IR/`Synthesized` tags you may need the lowered IR from the compile pipeline — if `CompileResult`
     doesn't expose it, get what you can from the DebugMap + generated source and clearly note what's unavailable.)

3. The test **asserts nothing about correctness** — it is a reporting test and must **pass** (so it stays green as
   a living map). Its job is to emit the report file.

## Known ground truth (for `Count4`, verify don't assume)

`AssetId 47fe9c55-c6ca-4c69-9c5a-d46de25745de`, `GraphId 10000006-0000-0000-0000-000000000001`. Authored nodes:
`...0001`=EventEntry, `...0002`=SetVariable, `...0003`=FunctionCall("Add"), `...0004`=GetVariable("Get Count"),
`da9a9c0b...`=Sequence, `0b561966...`=Delay, `7b6da53f...`=Return. The running sim currently fires `OnNodeEnter`
only for `...0004` and `976ef338-34f2-1469-973f-ee53538aab17` (a synthesized id not in the asset).

## SUCCESS CONDITION (must all hold)

- `dotnet build IOS-IG-SimHost.sln -c Debug` → **0 errors** (close the editor first; it locks DLLs).
- The new test passes; the file `.dev/_DONE/blueprint-dbg-1/reports/CF1-NODE-IDENTITY-REPORT.md` is produced and
  definitively answers: (a) which authored node ids have a DebugMap entry keyed by that **exact** id; (b) for
  Delay `0b561966` and Sequence `da9a9c0b`, the synthesized id that replaced them and the lowering stage/tag
  responsible; (c) the complete list of `DebugProbe.NodeEnter` ids actually emitted.
- **No other test's pass/fail result changes.** Run
  `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` and report the full failing-test set by
  name before and after (it must be identical except for the one new passing test).

## Reporting (per DEV-GUIDE)

Write `.dev/_DONE/blueprint-dbg-1/reports/CF1-REPORT.md` with: what you added, the exact `dotnet build`/`dotnet test`
command lines and their results, the full failing-test set by name (before/after), and a copy of the key findings
from `CF1-NODE-IDENTITY-REPORT.md`. Do **not** modify any production code, do **not** regenerate snapshots, do
**not** delete or weaken any existing test. If anything forces you to, STOP and report instead.
