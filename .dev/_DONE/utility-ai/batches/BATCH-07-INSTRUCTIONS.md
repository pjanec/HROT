# BATCH-07: Blueprint 1-C Completion (TASK-UAI-P1-09 Steps 4-7)

**Developer guide:** `.dev/.guides/DEV-GUIDE.md`  
**Design references:** All docs under `.dev/utility-ai/`  
**Previous review:** `.dev/utility-ai/reviews/BATCH-06-REVIEW.md` — verdict APPROVED WITH DEV-LEAD FIXES.

---

## Context

BATCH-06 completed Steps 1-C-1 through 1-C-3 of the Blueprint integration:
- ✅ **Nodes.cs** — `ScoreDecisionNode` and `ReadRankedResultNode` AST nodes added
- ✅ **IrOperation.cs** — `IrOp_ScoreDecision` and `IrOp_ReadRankedResult` IR records added
- ✅ **UtilityBlueprintBridge.cs** — Static runtime helpers created

This batch completes the remaining Steps 1-C-4 through 1-C-7 to fully satisfy
**SC-P1-09-3** and **SC-P1-09-4**.

---

## Overview

**Single task: Complete Blueprint pipeline for ScoreDecisionNode + ReadRankedResultNode**

The pattern is modeled exactly on `ReadEqsResultNode`. Read these files carefully before
writing any code — every naming convention, indentation, and structural choice comes from
the existing code:

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`
   — study the `case ReadEqsResultNode rer:` block at line 930 and the `case SpawnEqsSensorNode:` block at line 711
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`
   — study the `case IrOp_ReadEqsResult op:` case at line 471
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs`
   — study `CollectReadEqsResultOps` at line 375, `EmitReadEqsResultHelpers` at line 390,
     and the call site at lines 44-48
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/ReadEqsResultNodeRuntimeTests.cs`
   — use as structural template for the runtime tests

The BATCH-06 instructions at `.dev/utility-ai/batches/BATCH-06-INSTRUCTIONS.md`
contain the full pseudocode for all steps. **Reference it instead of duplicating content here.**
Sections to use: §1-C-4, §1-C-5, §1-C-6, §1-C-7.

---

## Step 4: Stage5_Schedule.cs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`

### 4-A: `ScoreDecisionNode` in `EmitNodeStatements`

Insert after `case SpawnEqsSensorNode:` (currently at line 711). See BATCH-06-INSTRUCTIONS §1-C-4
for the exact pseudocode. Key points:

- Compute decision ID at compile time: `UtilityDecisionCatalog.ComputeId(sdn.AssetId)`
  (`UtilityDecisionCatalog` is in `Fdp.Toolkit.Utility`; add the `using` if needed)
- The method `UtilityDecisionCatalog.ComputeId(string)` returns `int`
- Allocate a `System.Byte` typed result value for `WinningOptionId` output pin
- Emit `IrOp_ScoreDecision(decisionIdLiteral, id8)`
- Cache the output pin in `_pinValueCache`

### 4-B: `ReadRankedResultNode` in `ResolveNodeOutput`

Insert alongside `case ReadEqsResultNode rer:` (currently at line 930). See BATCH-06-INSTRUCTIONS §1-C-4
for the exact pseudocode. Key points:

- Generate a unique struct type name: `_RankedResultRead_{id8}`
- Use `SizeBytes = 16` (sequential layout: bool IsValid + long Entity + float Score + padding)
- Emit `IrOp_ReadRankedResult(rankLiteral, id8, structTypeName)`
- Emit `IrOp_FieldRead` for each non-exec output pin (`Entity`, `Score`, `IsValid`)
- Set `result` to the pin value (same pattern as `ReadEqsResultNode`)

---

## Step 5: StatementEmitter.cs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`

Add two cases after `case IrOp_ReadEqsResult op:` (currently at line 471). See
BATCH-06-INSTRUCTIONS §1-C-5 for the exact generated code strings.

- `IrOp_ScoreDecision`: `ScoreDecision_{op.NodeId8}(wv, self, time)`
- `IrOp_ReadRankedResult`: `ReadRankedResult_{op.NodeId8}(wv, self)`

Match the existing pattern for variable naming (`__t{idx}`) exactly.

---

## Step 6: InstanceEmitter.cs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs`

### 6-A: Add collector methods

Add `CollectScoreDecisionOps` and `CollectReadRankedResultOps` — both follow the exact same
structure as `CollectReadEqsResultOps` (line 375). See BATCH-06-INSTRUCTIONS §1-C-6.

### 6-B: Add emitter methods

Add `EmitScoreDecisionHelpers` and `EmitReadRankedResultHelpers`. See BATCH-06-INSTRUCTIONS §1-C-6
for the full emitter bodies. Notes:

- `EmitScoreDecisionHelpers` emits an `[AggressiveInlining]` method that calls
  `UtilityBlueprintBridge.ScoreDecision(view, self, decisionId, tick)` where `tick = (uint)(time * 60f)`
- `EmitReadRankedResultHelpers` emits both a result struct (`_RankedResultRead_{id8}`) with
  `IsValid/Entity/Score` fields AND the helper method that calls
  `UtilityBlueprintBridge.ReadRankedResult(view, self, rank)`

### 6-C: Wire into `Emit(IrAsset)`

At the call site (lines 44-48 where `readEqsOps` is collected and emitted), add two more
calls in the same pattern:

```csharp
var scoreDecisionOps   = CollectScoreDecisionOps(asset);
var readRankedResultOps = CollectReadRankedResultOps(asset);
// ... (in the emit section) ...
EmitScoreDecisionHelpers(e, scoreDecisionOps);
EmitReadRankedResultHelpers(e, readRankedResultOps);
```

Read the `Emit(IrAsset)` method top-to-bottom to understand exactly where in the class body
the collect calls happen vs. where the emit calls happen — mirror the pattern for `ReadEqsResult`.

---

## Step 7: Runtime tests

**New file:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/UtilityNodeRuntimeTests.cs`

Use `ReadEqsResultNodeRuntimeTests.cs` as the structural template. Read it completely before
writing any code.

### Test 1 — SC-P1-09-3: `ScoreDecisionNode` produces correct winning option

1. Register utility decisions: call `UtilityDecisionCatalog.RegisterAll()` (or ensure the
   fixture does this — check how the EQS tests initialize their registry)
2. Build a `BlueprintAsset` programmatically using the same builder pattern
3. Add a `ScoreDecisionNode` with `AssetId = CombatPostureDecision.AssetId` (the GUID string constant)
4. Wire its `WinningOptionId` output to a `SetVariableNode` writing to a byte variable `PostureOut`
5. Compile, register, tick on a `UtilityTestWorld` entity (fully set up via `SpawnAgent`)
6. After tick: read `PostureOut` from the Blueprint's variable storage
7. Run `w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id)` independently
8. Assert `PostureOut == (byte)selectedPosture`

**Important:** The entity needs all the components `CombatPostureDecision` reads:
`Health`, `TargetMemory`, `WeaponState`, EQS sensor components. Use `UtilityTestWorld.SpawnAgent`
and `SeedContact` + `SetEnemyStrengthRatio` to set up the entity. Seed conditions that
deterministically select `Posture.AdvanceAndAttack` (full health, ammo, clear contacts, no EQS).

### Test 2 — SC-P1-09-4: `ReadRankedResultNode` reads rank-0 buffer entry

1. Build a `BlueprintAsset` with a `ReadRankedResultNode` (`Rank = 0`)
2. Wire its outputs: `Entity` → variable `TopEntity`, `Score` → variable `TopScore`,
   `IsValid` → variable `TopIsValid`
3. Seed the entity's `UtilityResultBuffer` with known data — call
   `w.Scorer.Evaluate(w.Repo, self, ThreatRankingDecision.Id)` with a seeded contact in
   `TargetMemory` so the buffer has a non-empty top entry
4. Compile, register, tick
5. Assert `TopEntity == buf.Top().CandidateHandle`, `TopScore == buf.Top().Score`,
   `TopIsValid == true`

### Namespace and using directives

- Namespace: `Fdp.Toolkit.Tests` (matching the canonical convention)
- Usings: `Fdp.Toolkit.Utility`, `Fdp.Toolkit.Utility.Integration`, `Fdp.Toolkit.Tests.Utility`
  (for `UtilityTestWorld`), and the Blueprint compiler/runtime namespaces from the template file

---

## D-08 cleanup (optional, include if time permits)

Fix namespace in 4 older test files (all change `Fdp.Toolkit.Tests.Utility` → `Fdp.Toolkit.Tests`):
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/CurveEvaluationTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/AggregatorTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityCoreTests.cs`

If done, mark D-08 RESOLVED in `DEBT-TRACKER.md`.

---

## Build and test

```
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~StarterPackIntegrationTests|FullyQualifiedName~UtilitySelectorNodeTests|FullyQualifiedName~UtilityTransitionArbiterTests"
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~UtilityNodeRuntimeTests"
```

All 22 previously-passing tests must remain green. The 2 new runtime tests must pass.

---

## Success conditions for this batch

| Condition | Test | Expectation |
|-----------|------|-------------|
| SC-P1-09-3 | `UtilityNodeRuntimeTests.ScoreDecisionNode_Produces_WinningOption` | Compiled Blueprint calls UtilityBlueprintBridge.ScoreDecision, winner matches independent evaluation |
| SC-P1-09-4 | `UtilityNodeRuntimeTests.ReadRankedResultNode_Reads_TopBufferEntry` | rank=0 entity/score/isValid match buf.Top() |

---

## Report

Fill in `.dev/utility-ai/reports/BATCH-07-REPORT.md`. Answer:

- Q1: In Stage5, how did you find the `EmitNodeStatements` switch block? What was the exact
  line of the `SpawnEqsSensorNode` case you inserted after?
- Q2: Did you need to add a `using` directive for `UtilityDecisionCatalog` in Stage5? Which namespace?
- Q3: In the `ReadRankedResultNode` result struct, did you use `LayoutKind.Sequential`?
  Did the struct size 16 cover `bool(1) + pad(7) + long(8) + float(4) = 20`? If not, describe
  your actual memory layout choice.
- Q4: How does the `Emit(IrAsset)` call-site structure look after your additions?
  List the 6 lines (3 collect + 3 emit) in order.
- Q5: Did both runtime tests pass on first attempt? If not, describe what was wrong.
