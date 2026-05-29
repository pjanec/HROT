# Fdp.Toolkit.Utility -- Utility AI System

**Source folder**: `FDP/Toolkits/Fdp.Toolkits/Utility/`
**Primary namespace**: `Fdp.Toolkit.Utility`
**Integration namespace**: `Fdp.Toolkit.Utility.Integration`
**Design references**:
- `.dev/utility-ai/Utility_AI_Design_v1_1.md` (architecture v1.2)
- `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` (source generator)
- `.dev/utility-ai/Utility_AI_StarterPack_Examples_v1_1.md` (starter pack)
**Date**: 2026-05-30

---

## Overview

The Utility AI system is a **Brain-resident decision-scoring layer** for the FDP/HROT engine.
It scores competing action options -- *which target to eliminate, which weapon to fire, whether
to take cover or advance* -- by evaluating weighted **considerations** and selecting the
highest-scoring option.

It sits alongside the three existing AI authoring systems (FastBTree, FastHSM, Blueprint) and
is consumed by them rather than replacing any of them. The scoring core is synchronous,
allocation-free on the hot path, and targets the same agent scales as the Brain cognitive
pipeline.

### What problem it solves

BTree, HSM, and Blueprint are all **structural / Boolean** decision systems: they pick the
leftmost passing branch, or trigger on explicit guard conditions. Expressing "retreat only when
health is low *and* we are outnumbered *and* cover is available" requires deeply nested
selectors with thresholds that must be constantly retuned. Utility AI removes that brittleness:
each option is scored from weighted considerations, each consideration maps a raw input through
a response curve to a 0--1 utility, and the highest-scoring option wins.

### What it covers

| Decision kind | Description |
|---|---|
| **Threat ranking** | Score each known contact; rank "who to eliminate first" |
| **Weapon / effector selection** | Score each weapon mount against the chosen target |
| **Combat posture selection** | Score a fixed authored set of postures and pick one |
| **Group fire coordination** | Squad leader allocates targets across members using a greedy assignment pass |

### What it does NOT cover

Spatial candidate selection (cover points, flanking positions) is **EQS**'s job and remains
Muscle-side. Utility AI reads EQS results as considerations; it never re-implements position
scoring.

---

## Architecture

### Three pieces over one scoring core

```
+------------------------------------------------------------------+
|                        Brain node                                |
|                                                                  |
|  Leader entity (virtual)                                         |
|    +-- ThreatMatrixAssignmentSystem (greedy focus-fire assign.)  |
|         +-- writes per-member assignments --> Blackboard1024     |
|              (ThreatMatrixAssignmentState, 1024 bytes)           |
|                                                                  |
|  Member entity                                                   |
|    +-- ThreatRankingDecision   (candidate scorer)                |
|    +-- WeaponSelectionDecision  (candidate scorer)               |
|    +-- CombatPostureDecision    (posture selector)               |
|         +-- reads UtilityResultBuffer component                  |
|    +-- surfaced to BTree / HSM / Blueprint as helpers (see       |
|         UtilitySelectorNode, UtilityTransitionArbiter,           |
|         UtilityBlueprintBridge)                                  |
|                                                                  |
|  Scoring core (shared) + UtilityTraceWorkingMemory1024 ring buf  |
+------------------------------------------------------------------+
         ^
         | reads EQS top-score as one consideration among many
         |
  EqsCognitiveBuffer  <-- EQS solver result (Muscle)
```

### Pieces

| Piece | Shape | Output |
|---|---|---|
| **Scoring core** | consideration -> curve -> aggregate | single 0--1 score for one option |
| **Candidate scorer** | core run over a dynamic list (targets, weapons) | ranked Top-N in `UtilityResultBuffer` |
| **UtilitySelector / PostureSelect** | core run over a fixed authored set | one winning posture byte |
| **Group layer** | leader greedy assignment over (member x target) matrix | per-member target written to `Blackboard1024` |

---

## Core Data Structures

### Enumerations

```csharp
// CurveKind -- 9 response curve families
public enum CurveKind : byte
{
    Linear, InverseLinear, Threshold, Bell, Step,
    Logistic, Quadratic, InverseQuadratic, PiecewiseLinear
}

// ScoringMode -- two aggregation strategies
public enum ScoringMode : byte
{
    WeightedProduct,   // Dave Mark product-with-compensation (default)
    WeightedSum        // normalised weighted sum (additive escape hatch)
}

// InputContext -- which entity role provides a consideration's data
public enum InputContext : byte { Self, Target, Leader, Candidate }

// DecisionKind -- drives evaluation path in UtilityScorer
public enum DecisionKind : byte { ThreatRanking, WeaponSelection, PostureSelect }
```

### ResponseCurve (16 bytes, Sequential)

Maps a raw 0--1 input to a 0--1 utility output.

| Field | Type | Purpose |
|---|---|---|
| `Kind` | `CurveKind` (byte) | Which curve family to evaluate |
| `Padding0` | `byte` | Alignment padding |
| `CurveId` | `short` | PiecewiseLinear catalog key (0 for all other kinds) |
| `Slope` | `float` | Multiplier (m) |
| `Exponent` | `float` | Steepness / exponent (k) |
| `XShift` | `float` | Horizontal shift (b) |

Curve evaluation formulas (all output clamped to [0,1]):

| Kind | Formula |
|---|---|
| Linear | `m * (x - b)` |
| InverseLinear | `1 - m * (x - b)` |
| Threshold | `x >= b ? 1 : 0` |
| Bell | `m * exp(-k * (x - b)^2)` |
| Step | `x >= b ? (m > 0 ? m : 1) : 0` |
| Logistic | `1 / (1 + exp(-k * (x - b))) * m` |
| Quadratic | `m * (x - b)^2` |
| InverseQuadratic | `1 - m * (x - b)^2` |
| PiecewiseLinear | linear interpolation over control points in `PiecewiseCurveCatalog` |

A static `Curve` factory class provides shorthand constructors:
`Curve.Linear`, `Curve.Threshold`, `Curve.Bell`, `Curve.InverseLinear`, etc.

### InputParams (16-byte explicit-layout union)

Discriminated per-consideration parameters. The active union member is determined by which
input reader is referenced:

| Field | Offset | Used by |
|---|---|---|
| `BlueprintId` | 0 | `uint` FNV-1a-32 of EQS template name |
| `MaxRange` | 0 | `float` max range in metres (DistanceToContext) |
| `MountIndex` | 0 | `int` zero-based weapon mount index |

### UtilityConsideration (Sequential, unmanaged)

One row of an option's consideration table:

| Field | Type | Purpose |
|---|---|---|
| `InputId` | `ushort` | FNV-1a-16 of the input reader name |
| `Context` | `InputContext` | Entity role supplying the value |
| `Padding0` | `byte` | Alignment |
| `Weight` | `float` | WeightedProduct: exponent; WeightedSum: multiplier |
| `Curve` | `ResponseCurve` | Maps raw input to 0--1 utility |
| `Params` | `InputParams` | Per-consideration reader parameters |

### UtilityDecisionDef

Runtime representation of a decision, produced by the fluent builder and stored in
`UtilityRegistry`. Contains:

- `BlueprintId` (`int`) -- FNV-1a-32 of `AssetId`, used as lookup key
- `Kind` (`DecisionKind`)
- `Options` (`UtilityOption[]`) -- each option carries `OptionId`, `Mode`, `Considerations[]`
- `DebugName` (`string`)

---

## Response Curve Evaluation

### ResponseCurve.Evaluate(float x)

Defined as a `partial` method in `ResponseCurveEvaluate.cs`. Dispatches on `Kind` and returns
a value clamped to [0,1]. YShift (c) is implicitly zero for all Phase 1 curves.

### PiecewiseCurveCatalog

Thread-safe static side-table for `PiecewiseLinear` control points, keyed by `CurveId`.

```csharp
// Registration (startup or test setup)
PiecewiseCurveCatalog.Register(curveId: 1, points: new[] {
    (0f, 0f), (0.5f, 0.8f), (1f, 1f)
});

// Evaluation (called by ResponseCurve.Evaluate internally)
float y = PiecewiseCurveCatalog.Evaluate(curveId: 1, x: 0.3f);
```

Points must be sorted by X ascending. Returns 0 when `CurveId` is not registered; clamps to
first/last Y outside the control-point range.

---

## Standard Input Readers

### UtilityInputCtx

Context struct passed to every input reader during a scoring pass:

```csharp
public struct UtilityInputCtx
{
    public EntityRepository Repo;    // ECS repository
    public Entity Self;              // agent entity being scored
    public Entity Context;           // target / candidate / leader entity
    public InputParams Params;       // per-consideration parameters
}
```

### [UtilityInput] attribute

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class UtilityInputAttribute : Attribute
{
    public string Name { get; }  // catalog name, e.g. "AmmoFraction"
    public UtilityInputAttribute(string name) { Name = name; }
}
```

Marks a `static float(in UtilityInputCtx)` method as a named input reader. The Phase 2 source
generator picks up the attribute and emits registration code. The FNV-1a-16 of `Name` must
match the corresponding constant in `StandardInputIds`.

### StandardInputIds constants

FNV-1a-16 identifiers for all 17 Phase 1 standard input readers.

**Group A -- weapon / health / distance**

| Constant | ID | Source component |
|---|---|---|
| `AmmoFraction` | `0x2C39` | `WeaponState.Ammo / MaxAmmo` on Self |
| `WeaponHasAmmo` | `0xC96D` | `WeaponState.Ammo > 0` on Self |
| `WeaponReadiness` | `0xA563` | `WeaponState.CooldownSecondsRemaining <= 0` |
| `HealthFraction` | `0x13D9` | `Health.Current / Max` on Self |
| `ContactHealthFraction` | `0xA533` | `Health.Current / Max` on Context |
| `DistanceToContext` | `0x96DE` | distance between Self and Context positions |

**Group B -- perception**

| Constant | ID | Source component |
|---|---|---|
| `ContactThreatLevel` | `0x055B` | `TargetMemory` threat level for Context |
| `HasLineOfSight` | `0xF98D` | LOS flag from `TargetMemory.Modalities` |
| `HaveLiveTarget` | `0xC20C` | at least one live target in TargetMemory |
| `EnemyStrengthRatio` | `0x5635` | known enemies / (allies + 1), clamped 0--1 |

**Group C -- EQS**

| Constant | ID | Source component |
|---|---|---|
| `EqsTopScore` | `0x2227` | top score from the EqsCognitiveBuffer for a named sensor |
| `EqsResultCount` | `0x71F0` | fraction of result slots filled for a named sensor |

**Group D -- assignment / misc**

| Constant | ID | Source component |
|---|---|---|
| `IsAssignedTarget` | `0x76F0` | Context is the assigned target from `ThreatMatrixAssignmentState` |
| `AllyAdvancingNearby` | `0x141B` | at least one ally with advancing state nearby |
| `Constant` | `0xAB45` | injects a design-time constant via `Params.MaxRange` |
| `WeaponRangeBandFit` | `0x2C0C` | how well the weapon's range band fits the current engagement distance |
| `WeaponEffectivenessVsTarget` | `0xEE5F` | weapon effectiveness category vs. target armor type |

### UtilityInputReaderStore

Static registry mapping `ushort` input IDs to `delegate*<in UtilityInputCtx, float>` function
pointers. Populated at startup by the generated registrar (or by `StandardInputs.RegisterAll()`
in tests). Thread-safe for reads after initialization.

### StandardInputs.RegisterAll()

Registers all 17 Phase 1 standard readers. Called once at startup (or explicitly in tests).

```csharp
StandardInputs.RegisterAll();
```

---

## Aggregator

`Aggregator.Aggregate(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights, ScoringMode mode)`

### WeightedProduct (default)

Implements Dave Mark's product-with-compensation formula (§4.3):

```
rawProduct         = product of (curveOutput[i] ^ weight[i])
modificationFactor = 1 - (1 / n)
makeUpValue        = (1 - rawProduct) * modificationFactor
finalScore         = rawProduct + makeUpValue * rawProduct
```

The compensation factor prevents the score from collapsing toward zero as the number of
considerations grows.

### WeightedSum

Normalized weighted sum (escape hatch for additive scoring):

```
finalScore = sum(weight[i] * curveOutput[i]) / sum(weight[i])
```

---

## UtilityScorer

`public unsafe class UtilityScorer`

Central evaluator. Bound to a `UtilityRegistry` at construction time. All hot-path methods are
allocation-free (intermediate float arrays use `stackalloc`).

### Instance API

```csharp
// Evaluate a decision for an entity; writes ranked results into the entity's
// UtilityResultBuffer component. Dispatches to candidate iteration (ThreatRanking,
// WeaponSelection) or posture scoring (PostureSelect).
void Evaluate(EntityRepository repo, Entity self, int decisionId,
    Entity context = default, ushort tick = 0);

// Evaluate a PostureSelect decision with hysteresis; returns the winning posture byte.
byte SelectPosture(EntityRepository repo, Entity self, int decisionId, ushort tick = 0);
```

### Static API

```csharp
// Evaluate a pre-resolved UtilityDecisionDef (PostureSelect path).
static void Evaluate(EntityRepository repo, Entity self,
    in UtilityDecisionDef def, Entity context,
    ref UtilityResultBuffer output,
    UtilityTraceWorkingMemory1024* trace, ushort tick);

// Select winner from a pre-scored UtilityResultBuffer, applying hysteresisBonus
// to the currently-active posture before re-ranking.
static byte SelectPosture(ref UtilityResultBuffer buffer,
    byte activePosture, float hysteresisBonus);
```

### Evaluation flow

```
UtilityScorer.Evaluate()
   |
   +-- ThreatRanking / WeaponSelection:
   |     Iterate candidates (TargetMemory contacts or weapon-mount entities)
   |     For each candidate:
   |       For each consideration: invoke InputReader -> Curve.Evaluate -> weight
   |       Aggregator.Aggregate() -> one score
   |     Sort descending -> fill UtilityResultBuffer (TopN = 16)
   |
   +-- PostureSelect:
         For each authored option:
           For each consideration: invoke InputReader -> Curve.Evaluate -> weight
           Aggregator.Aggregate() -> one score
         Apply hysteresisBonus to currently-active posture
         Pick winner -> UtilityResultEntry.WinningPostureId
```

---

## Decision Definitions and Registration

### [UtilityDecision] attribute

Applied to a class that also implements `IUtilityDecisionDefinition` and provides a static
`Build(IUtilityDecisionBuilder)` method.

```csharp
[UtilityDecision(
    assetId:         "3c6f9e42-...",    // stable GUID string; FNV-1a-32 -> integer ID
    displayName:     "Combat posture",
    kind:            DecisionKind.PostureSelect,
    category:        "Tactical/Posture",
    hysteresisBonus: 0.08f)]
public sealed partial class CombatPostureDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .Option((ushort)Posture.AdvanceAndAttack, ScoringMode.WeightedProduct, o => o
            .Consider(In.HealthFraction(),    0.7f, Curve.Linear)
            .Consider(In.AmmoFraction(),      0.9f, Curve.Threshold)
            ...);
}
```

### Fluent builder interfaces

```csharp
public interface IUtilityDecisionBuilder
{
    // Adds a named posture option (PostureSelect decisions).
    IUtilityDecisionBuilder Option(ushort optionId, ScoringMode mode,
        Action<IUtilityOptionBuilder> configure);

    // Adds the single candidate option (ThreatRanking / WeaponSelection).
    IUtilityDecisionBuilder CandidateOption(ScoringMode mode,
        Action<IUtilityOptionBuilder> configure);
}

public interface IUtilityOptionBuilder
{
    // Appends a consideration: input reader + weight + response curve.
    IUtilityOptionBuilder Consider(InputRef input, float weight, ResponseCurve curve);
}
```

### In factory class

Static partial class `In` provides factory methods for all 17 standard inputs as `InputRef`
values:

```csharp
In.AmmoFraction()               // Group A
In.HealthFraction()
In.DistanceToContext(ctx)       // ctx defaults to InputContext.Candidate
In.HasLineOfSight()             // Group B
In.EqsTopScore("CoverQuery")   // Group C -- reads named EQS sensor result
In.IsAssignedTarget()           // Group D
In.Constant(0.2f)
```

### UtilityRegistry

```csharp
public sealed class UtilityRegistry
{
    void Register(int id, UtilityDecisionDef def, float hysteresisBonus = 0f);
    bool TryGet(int id, out UtilityDecisionDef? def, out float hysteresisBonus);
}
```

Thread-safe for reads after initial population. `UtilityDecisionCatalog.Shared` holds the
process-global shared instance.

### UtilityDecisionCatalog

Discovers all `[UtilityDecision]`-attributed classes in loaded assemblies via reflection and
registers their definitions. Intended for use in tests and in hosts that do not use the Phase 2
source generator.

```csharp
UtilityDecisionCatalog.RegisterAll(out UtilityRegistry registry);
// Also populates UtilityDecisionCatalog.Shared.
```

---

## Startup Handshake (Phase 2 Source Generator Path)

### UtilityRegistrarAttribute

Marks a generated class as a Utility AI registrar:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class UtilityRegistrarAttribute : Attribute { }
```

The Phase 2 source generator (`UtilityInputGenerator` and `UtilityDecisionGenerator` in
`Fdp.Toolkits.Analyzers`) emits two classes into each consuming assembly:
- `UtilityInputRegistrar.g.cs` -- calls `UtilityInputReaderStore.Register(...)` for each
  `[UtilityInput]` method.
- `UtilityDecisionCatalog.g.cs` -- calls `UtilityRegistry.Register(...)` for each
  `[UtilityDecision]` class.

Both generated classes carry `[UtilityRegistrar]`.

### UtilityAutoDiscovery.ScanAndRegister()

One-time reflective scan that finds all `[UtilityRegistrar]` classes and calls their static
`RegisterAll()` method. Thread-safe double-checked locking; safe to call multiple times.

```csharp
// At application startup, before any scoring:
UtilityAutoDiscovery.ScanAndRegister();
```

---

## UtilityResultBuffer

**ECS component** (`ComponentId` = 151, `DataPolicy.NoSave`). Stores the ranked output of
one `UtilityScorer.Evaluate` call.

```
UtilityResultBuffer layout:
  Count           (int)    -- number of valid entries (0..TopN)
  RunnerUpMargin  (float)  -- score gap between rank-0 and rank-1
  Results         (UtilityResultArray)  -- [InlineArray(16)] of UtilityResultEntry
```

```
UtilityResultEntry layout (16 bytes, Sequential):
  CandidateHandle  (long)   -- packed entity handle; 0 for PostureSelect
  Score            (float)  -- final aggregated utility in [0,1]
  WinningPostureId (byte)   -- posture option byte (PostureSelect only)
  _pad0..2         (3 bytes)
```

Access patterns:

```csharp
ref readonly var buf = ref repo.GetComponentRO<UtilityResultBuffer>(entity);

// Top result
UtilityResultEntry top = buf.Top();

// Iterate all ranked results
ReadOnlySpan<UtilityResultEntry> span = buf.GetSpanRO();
for (int i = 0; i < buf.Count; i++) { ... span[i] ... }

// Score of a specific candidate
float s = buf.ScoreOf(candidateHandle);
```

**Important**: never write through a direct `Results[i]` index assignment -- use `GetSpanRW()`
to bypass the C# `[InlineArray]` defensive-copy trap.

---

## Trace Buffer

### UtilityDebugFlags

**ECS component** (`ComponentId` = 149, `DataPolicy.NoSave`). Per-entity opt-in flag:

```csharp
[ComponentId(UtilityApplicationComponentIds.UtilityDebugFlags)]
public struct UtilityDebugFlags
{
    public byte TraceEnabled;   // non-zero = capture trace records
}
```

### UtilityTraceWorkingMemory1024

**ECS component** (`ComponentId` = 150, `DataPolicy.NoSave`). 1024-byte unmanaged ring buffer
of 32-byte `UtilityTraceRecord` entries (32 records maximum).

Each `UtilityTraceRecord` captures one step of the evaluation:

| OpCode | Fields captured |
|---|---|
| `Consideration` | OptionIndex, InputId, Tick, RawValue, NormalizedValue, CurveOutput, Weight, RunningAggregate |
| `Winner` | OptionIndex, Tick, RawValue (= winning score), RunningAggregate (= runner-up margin) |

When `UtilityDebugFlags.TraceEnabled` is non-zero on the entity, `UtilityScorer` writes
records into the trace buffer during evaluation.

---

## Starter-Pack Decisions

Four ready-to-use decision definitions ship in `Utility/StarterPack/`. All carry
`[UtilityDecision]` and implement `IUtilityDecisionDefinition`.

### ThreatRankingDecision

`assetId: "1a4f7c20-3b9e-4d18-8a01-threat0000001"` | `Kind: ThreatRanking`

Scores each contact in the agent's `TargetMemory` and ranks by threat priority.

| Consideration | Weight | Curve | Notes |
|---|---|---|---|
| `HasLineOfSight` | 1.0 | Step | Gate: must have LOS |
| `DistanceToContext` | 0.7 | Linear | Closer = higher score |
| `ContactThreatLevel` | 1.0 | Linear | Direct threat metric |
| `ContactHealthFraction` | 0.4 | InverseLinear | Lower health = easier target |
| `IsAssignedTarget` | 0.9 | Threshold | Bias toward squad-assigned target |

### WeaponSelectionDecision

`assetId: "2b5e8d31-4c0f-5e29-9b12-weapon0000001"` | `Kind: WeaponSelection`

Scores each weapon mount; `Self` = mount entity, `Context` = target entity.

| Consideration | Weight | Curve | Notes |
|---|---|---|---|
| `WeaponHasAmmo` | 1.0 | Step | Gate: must have ammo |
| `WeaponRangeBandFit` | 1.0 | Bell | Peak at ideal engagement range |
| `WeaponEffectivenessVsTarget` | 1.0 | Linear | Armor type match |
| `WeaponReadiness` | 0.6 | Linear | Cooldown penalty |

### CombatPostureDecision

`assetId: "3c6f9e42-5d10-6f3a-ac23-posture0000001"` | `Kind: PostureSelect` | `HysteresisBonus: 0.08`

Selects one of five tactical postures using `Posture` enum values as option IDs.

| Posture | Key inputs |
|---|---|
| `AdvanceAndAttack` (1) | HealthFraction, AmmoFraction, EnemyStrengthRatio, HaveLiveTarget |
| `TakeCover` (2) | HealthFraction (inverse), EqsTopScore("CoverQuery"), EnemyStrengthRatio |
| `Suppress` (3) | AmmoFraction, HaveLiveTarget, AllyAdvancingNearby |
| `Flee` (4) | HealthFraction (inverse quad), EqsTopScore("RetreatQuery"), EnemyStrengthRatio |
| `Hold` (5) | HealthFraction, Constant(0.2) (WeightedSum) |

### LeaderAssignmentDecision

`assetId: "4d70af53-6e21-7a4b-bd34-leader00000001"` | `Kind: ThreatRanking`

Used by `ThreatMatrixAssignmentSystem` to score (member, target) pairs; `Self` = member entity,
`Context` = target entity.

| Consideration | Weight | Curve |
|---|---|---|
| `HasLineOfSight` | 1.0 | Step |
| `ContactThreatLevel` | 0.9 | Linear |
| `DistanceToContext` | 0.6 | InverseLinear |

### Posture enum

```csharp
public enum Posture : byte
{
    AdvanceAndAttack = 1,
    TakeCover        = 2,
    Suppress         = 3,
    Flee             = 4,
    Hold             = 5
}
```

---

## Group Layer

### ThreatMatrixAssignmentSystem

Greedy squad-level target assignment. The squad leader entity runs this once per tick (or at
reduced cadence) to assign each member a priority target while respecting the focus-fire cap.

```csharp
var system = new ThreatMatrixAssignmentSystem(
    decisionId:        LeaderAssignmentDecision.Id,
    maxFocusFireCount: 2);   // at most 2 members on the same target

system.Run(repo, leaderEntity);
```

**Algorithm:**
1. Read `UnitRoster` (member list) and `TargetMemory` (contact list) from the leader.
2. For each member in roster order, score all targets using `LeaderAssignmentDecision`.
3. Assign the member to the highest-scoring target whose `focusFireCount` is below the cap.
4. Write all assignments into `ThreatMatrixAssignmentState` projected onto the leader's
   `Blackboard1024`.

### ThreatMatrixAssignmentState

Overlay on the squad leader's `Blackboard1024` (all 1024 bytes used):

```
ThreatMatrixAssignmentState = 16 x AssignmentSlot (64 bytes each = 1024 bytes)

AssignmentSlot (64 bytes, Sequential):
  AssignedTargetHandle  (long)   -- packed entity handle, 0 = unassigned
  AssignmentScore       (float)  -- score at assignment time
  FocusFireCount        (byte)   -- members targeting the same entity
  Flags                 (byte)
  [50 bytes padding to pad to 64]
```

Access:

```csharp
ref var bb    = ref repo.GetComponentRW<Blackboard1024>(leader);
ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
long target   = state.GetAssignedTarget(memberRosterIndex);
```

---

## Integration Nodes

Three helpers bridge Utility AI scoring into the existing authoring systems.

### UtilitySelectorNode (BTree)

`Fdp.Toolkit.Utility.Integration.UtilitySelectorNode`

Evaluates a decision and returns the 0-based index of the winning branch. Applies a hysteresis
bonus to prevent rapid branch switching.

```csharp
var selector = new UtilitySelectorNode(scorer, decisionId,
    optionIds: new byte[] { (byte)Posture.AdvanceAndAttack, (byte)Posture.TakeCover });

// Inside a [BTreeCondition] method:
return selector.IsActiveBranch(repo, entity, branchIndex: 0);

// Or obtain the branch index directly:
int branch = selector.SelectBranch(repo, entity, hysteresisBonus: 0.08f);
```

### UtilityTransitionArbiter (HSM)

`Fdp.Toolkit.Utility.Integration.UtilityTransitionArbiter`

Static helper for HSM guard methods. Returns `true` when the entity's `UtilityResultBuffer`
top entry matches the requested posture option.

```csharp
// From a custom transition evaluator:
bool shouldFlee = UtilityTransitionArbiter.Evaluate(repo, entity, (byte)Posture.Flee);
```

### UtilityBlueprintBridge (Blueprint)

`Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge`

Static helpers for Blueprint-generated code. Operates on `ISimulationView` (the Blueprint
execution context) without direct `EntityRepository` coupling.

```csharp
// Run decision; returns winning posture byte.
byte posture = UtilityBlueprintBridge.ScoreDecision(view, self, decisionId, tick);

// Read rank-N result.
var (handle, score, valid) = UtilityBlueprintBridge.ReadRankedResult(view, self, rank: 0);
```

---

## ECS Components

| Component | ID | DataPolicy | Size | Purpose |
|---|---|---|---|---|
| `UtilityDebugFlags` | 149 | NoSave | 1 byte | Per-entity trace enable flag |
| `UtilityTraceWorkingMemory1024` | 150 | NoSave | 1024 bytes | Scoring trace ring buffer (32 x 32-byte records) |
| `UtilityResultBuffer` | 151 | NoSave | ~260 bytes | Ranked scoring output (16 entries + count + margin) |

---

## Source Structure

```
Utility/
  Core/
    Aggregator.cs                    -- WeightedProduct and WeightedSum aggregation
    PiecewiseCurveCatalog.cs         -- Thread-safe side-table for PiecewiseLinear curves
    ResponseCurveEvaluate.cs         -- ResponseCurve.Evaluate(float) partial
    UtilityApplicationComponentIds.cs -- ECS component ID constants (149, 150, 151)
    UtilityCore.cs                   -- CurveKind, ScoringMode, InputContext, DecisionKind,
                                     --   ResponseCurve, InputParams, UtilityConsideration,
                                     --   UtilityDecisionDef, UtilityOption,
                                     --   UtilityInputCtx, UtilityInputReaderStore
    UtilityDecisionBuilderInfra.cs   -- UtilityDecisionAttribute, IUtilityDecisionDefinition,
                                     --   InputRef, In factory, IUtilityDecisionBuilder,
                                     --   IUtilityOptionBuilder, UtilityDecisionBuilder,
                                     --   Curve factory, Ctx constants
    UtilityDecisionCatalog.cs        -- UtilityRegistry, UtilityDecisionCatalog
    UtilityRegistrarAttribute.cs     -- UtilityRegistrarAttribute, UtilityAutoDiscovery
    UtilityResultBuffer.cs           -- UtilityResultEntry, UtilityResultArray, UtilityResultBuffer
    UtilityScorer.cs                 -- UtilityScorer class (instance + static APIs)
    UtilityTraceWorkingMemory1024.cs -- UtilityTraceOpCode, UtilityTraceRecord,
                                     --   UtilityTraceWorkingMemory1024, UtilityDebugFlags
  Group/
    ThreatMatrixAssignmentState.cs   -- AssignmentSlot, ThreatMatrixAssignmentState
    ThreatMatrixAssignmentSystem.cs  -- ThreatMatrixAssignmentSystem (greedy squad assignment)
  Inputs/
    StandardInputs.cs                -- StandardInputIds constants + 17 standard readers
    UtilityInputAttribute.cs         -- [UtilityInput] attribute
  Integration/
    UtilityBlueprintBridge.cs        -- Blueprint-generated code helpers
    UtilitySelectorNode.cs           -- BTree branch selector with hysteresis
    UtilityTransitionArbiter.cs      -- HSM guard helper
  StarterPack/
    CombatPostureDecision.cs         -- 5-posture starter decision
    LeaderAssignmentDecision.cs      -- Leader-to-member pair scorer
    Posture.cs                       -- Posture enum (5 values)
    ThreatRankingDecision.cs         -- Target threat-rank scorer
    WeaponSelectionDecision.cs       -- Weapon mount scorer
```

---

## Dependencies

| Assembly | Used for |
|---|---|
| `Fdp.Core` | `EntityRepository`, `Entity`, `ComponentId`, `DataPolicy` |
| `Fdp.ModuleHost.Abstractions` | `ISimulationView` (Blueprint bridge) |
| `Fdp.Toolkit.Combat.Components` | `WeaponState` (ammo, cooldown) |
| `Fdp.Toolkit.Perception` / `.Components` | `TargetMemory`, `EqsCognitiveBuffer` |
| `Fdp.Toolkit.Behavior.Components` | `Blackboard1024`, `UnitRoster` |
| `Fdp.Toolkit.Geographic.Components` | `Position` (distance inputs) |
| `Fdp.Toolkit.Replication.Components` | `Health` |
| `Fdp.Toolkit.Spatial.Eqs` | `EqsCognitiveBuffer` read in EQS input readers |

---

## Implementation Status

Phases completed as of 2026-05-30:

| Phase | Status | Content |
|---|---|---|
| Phase 0 (BATCH-01) | Complete | Prerequisite bundle: `WeaponState.MaxAmmo`, multi-mount weapons, `MaxTrackedTargets=16`, `UnitRoster.Add/IndexOf`, `Blackboard1024.Project<T>`, `UtilityTestWorld`, gate test |
| Phase 1 (BATCH-02 to BATCH-07) | Complete | Scoring core, curve evaluation, aggregator, trace buffer, `UtilityScorer`, 17 standard inputs, `ThreatMatrixAssignmentSystem`, 4 starter-pack decisions, BTree/HSM/Blueprint integration nodes |
| Phase 2 (BATCH-08 to BATCH-10) | Complete | `UtilityInputGenerator`, `UtilityDecisionGenerator`, `UtilityAuthoringAnalyzer`, `UtilityAutoDiscovery` startup handshake |
| Phase 3 | Planned | `CurveWidget.Draw` -- host-agnostic curve editing widget |
| Phase 4 | Planned | AI overlays (`AiOverlayFlags`), five overlay sources, `TuningRegistry` + `TuningConsoleGizmo` slice 1 |
| Phase 5 | Planned | Utility editor card-table (`UtilityDecisionAsset`, live preview, `UtilityFluentEmitter` round-trip) |
| Phase 6 | Planned | Visual curve editing, editor-console bridge, snapshot/restore |
