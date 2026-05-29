# BATCH-05: Utility AI — Fluent Authoring Layer, ThreatMatrixAssignmentSystem, and Starter-Pack

**Batch Number:** BATCH-05
**Tasks:** D-04 (namespace fix), D-05 (OptionId assert), D-06 (close), TASK-UAI-P1-07 (`ThreatMatrixAssignmentSystem`), TASK-UAI-P1-08 (fluent builder infra + starter-pack decisions + integration tests)
**Phase:** Phase 1 — Runtime core completion
**Estimated Effort:** 18–22 hours
**Priority:** HIGH
**Dependencies:** BATCH-04 (StandardInputs + ThreatMatrixAssignmentState — APPROVED)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — sections `TASK-UAI-P1-07` and `TASK-UAI-P1-08`
3. **Architecture:** `.dev/utility-ai/Utility_AI_Design_v1_1.md`
   - §10 "Group fire coordination" (§10.1 – §10.4) — ThreatMatrixAssignmentSystem
   - §11.1 "C# is the source of truth" — fluent authoring pattern
   - §11.4 "Starter pack" — four decisions
4. **Starter Pack:** `.dev/utility-ai/Utility_AI_StarterPack_Examples_v1_1.md` — §0 test scaffolding, §1–§5 all decisions and tests
5. **Previous review:** `.dev/utility-ai/reviews/BATCH-04-REVIEW.md`
6. **Debt Tracker:** `.dev/utility-ai/DEBT-TRACKER.md` — D-04, D-05, D-06

### Source Code Locations

**Existing production files to read before modifying:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` — `UtilityDecisionDef`, `UtilityOption`, `UtilityConsideration`, `ResponseCurve`, `InputParams`, `InputContext`, `DecisionKind`, `ScoringMode`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — static scoring core (will be refactored)
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityResultBuffer.cs` — result buffer component
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityTraceWorkingMemory1024.cs` — trace buffer component
- `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — 17 registered catalog readers + `StandardInputIds`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs` — the 1024-byte struct (BATCH-04)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` — test helper (to be extended)

**New production files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — see Task 1
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` — see Task 2
- `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs` — see Task 3
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/Posture.cs` — `Posture` enum
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/WeaponSelectionDecision.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/CombatPostureDecision.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/LeaderAssignmentDecision.cs`

**Production files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — see Task 4
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` — D-05: `Debug.Assert` for `OptionId`

**New test files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs` — see Task 5

**Test files to modify:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` — see Task 5 onboarding extensions
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs` — D-04 namespace fix
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityResultBufferTests.cs` — D-04 namespace fix

### Build and Test Commands

```bat
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

All 100 prior utility tests PLUS all new BATCH-05 tests must pass. Run the full suite.

### Report Submission

When done, submit your report to: `.dev/utility-ai/reports/BATCH-05-REPORT.md`
If you have blocking questions: `.dev/utility-ai/questions/BATCH-05-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 0 (Debt):** Apply → verify compile → ALL prior tests pass ✅
2. **Task 1 (Builder Infra):** Implement → ALL tests pass ✅
3. **Task 2 (Catalog):** Implement → ALL tests pass ✅
4. **Task 3 (AssignmentSystem):** Implement → write tests → ALL tests pass ✅
5. **Task 4 (Scorer refactor):** Implement → verify prior tests still pass ✅
6. **Task 5 (StarterPack):** Implement decisions → update test world → write integration tests → ALL tests pass ✅

**DO NOT** move to the next task until:
- Current task implementation is complete
- ALL tests pass (including all prior batches)

**DO NOT** ask for permission to run tests, fix failures, or proceed with obvious steps. Complete everything, fix every failure at its root cause, then write the report.

---

## Context

BATCH-04 delivered the full `StandardInputs` catalog (17 readers), `ThreatMatrixAssignmentState`, and corrective fixes. Phase 1 is almost complete. This batch adds:

1. The **fluent authoring infrastructure** so that decision definitions can be written as C# classes with `[UtilityDecision]` + `IUtilityDecisionBuilder` (the source-of-truth authoring pattern from §11).
2. The **`UtilityDecisionCatalog`** that discovers registered decisions and builds a `UtilityRegistry` at startup.
3. The **instance-based `UtilityScorer` API** that resolves decisions by ID from the registry.
4. The **`ThreatMatrixAssignmentSystem`** for squad-level greedy target assignment.
5. The **four starter-pack decisions** and their integration tests.

After this batch, Phase 1 is complete and Phase 2 (source generator) begins.

---

## 🎯 Batch Objectives

- Complete the fluent C# authoring layer so decisions can be expressed as `[UtilityDecision]` classes.
- Deliver a working squad assignment system (`ThreatMatrixAssignmentSystem`) that writes to the leader's `Blackboard1024`.
- Prove the system end-to-end with 5 integration tests covering: threat ranking, weapon selection, posture, assignment, trace.
- Fix the three open P3 debt items from the tracker (D-04, D-05, D-06).

---

## ✅ Tasks

### Task 0: Debt Fixes (D-04, D-05, D-06)

**Files:** Multiple (see below)
**Debt Tracker:** `.dev/utility-ai/DEBT-TRACKER.md`

#### D-04 — Namespace normalization

**Files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityResultBufferTests.cs`

The two files above declare namespace `Fdp.Toolkit.Utility.Tests`. Change both to `Fdp.Toolkit.Tests` (matching all other utility test files in that directory). No other changes.

Verify: all tests still pass after namespace change.

#### D-05 — `OptionId` overflow guard

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` — in `UtilityOption` or in the builder (Task 1).

Add the following assert in `UtilityDecisionBuilder.BuildOption` (see Task 1) when assigning `OptionId`:
```csharp
Debug.Assert(optionId <= byte.MaxValue,
    $"OptionId {optionId} exceeds byte.MaxValue; WinningPostureId (byte) will truncate silently. " +
    $"Keep OptionId <= 255 in Phase 1.");
```

Place this assertion in the builder at the point an option is added. Mark D-05 as RESOLVED in DEBT-TRACKER.md.

#### D-06 — Dictionary comment (no code change needed)

D-06 notes that `UtilityInputRegistrar` uses `Dictionary<ushort, nint>` and Phase 2 will replace it. The code already has comments about this. Mark D-06 as RESOLVED in DEBT-TRACKER.md (the comment exists; no code change warranted).

---

### Task 1: Fluent Builder Infrastructure

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — NEW FILE
**Task Definition:** See `.dev/utility-ai/TASK-DETAIL.md` §TASK-UAI-P1-08; design §11.1

This file defines the C# authoring vocabulary used by all four starter-pack decisions.

#### 1a. `[UtilityDecision]` attribute

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UtilityDecisionAttribute : Attribute
{
    public string AssetId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DecisionKind Kind { get; set; }
    public string Category { get; set; } = string.Empty;
    public float HysteresisBonus { get; set; }
}
```

#### 1b. `IUtilityDecisionDefinition` marker interface

```csharp
public interface IUtilityDecisionDefinition
{
    // Marker. Implementing classes must provide:
    //   static void Build(IUtilityDecisionBuilder b)
    // The builder infra invokes it by convention via reflection.
}
```

#### 1c. `InputRef` struct

Carries the inputs needed to construct a `UtilityConsideration` (InputId + Context + optional Params):
```csharp
public readonly struct InputRef
{
    public readonly ushort       InputId;
    public readonly InputContext Context;
    public readonly InputParams  Params;
    public InputRef(ushort inputId, InputContext context, InputParams @params = default) { ... }
}
```

#### 1d. `In` static helper class

One factory method per catalog reader (use `StandardInputIds` constants). Signature:
```csharp
public static class In
{
    public static InputRef AmmoFraction(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.AmmoFraction, ctx);
    public static InputRef WeaponHasAmmo(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.WeaponHasAmmo, ctx);
    public static InputRef WeaponReadiness(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.WeaponReadiness, ctx);
    public static InputRef HealthFraction(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.HealthFraction, ctx);
    public static InputRef ContactHealthFraction(InputContext ctx = InputContext.Candidate)
        => new InputRef(StandardInputIds.ContactHealthFraction, ctx);
    public static InputRef DistanceToContext(InputContext ctx = InputContext.Candidate)
        => new InputRef(StandardInputIds.DistanceToContext, ctx);
    public static InputRef ContactThreatLevel(InputContext ctx = InputContext.Candidate)
        => new InputRef(StandardInputIds.ContactThreatLevel, ctx);
    public static InputRef HasLineOfSight(InputContext ctx = InputContext.Candidate)
        => new InputRef(StandardInputIds.HasLineOfSight, ctx);
    public static InputRef HaveLiveTarget(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.HaveLiveTarget, ctx);
    public static InputRef EnemyStrengthRatio(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.EnemyStrengthRatio, ctx);
    // EqsTopScore takes a string template name → computed as Fnv1a32 of name into Params.BlueprintId
    public static InputRef EqsTopScore(string templateName)
        => new InputRef(StandardInputIds.EqsTopScore, InputContext.Self,
               new InputParams { BlueprintId = Fnv1a32(templateName) });
    public static InputRef EqsResultCount(string templateName)
        => new InputRef(StandardInputIds.EqsResultCount, InputContext.Self,
               new InputParams { BlueprintId = Fnv1a32(templateName) });
    public static InputRef IsAssignedTarget(InputContext ctx = InputContext.Candidate)
        => new InputRef(StandardInputIds.IsAssignedTarget, ctx);
    public static InputRef AllyAdvancingNearby(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.AllyAdvancingNearby, ctx);
    public static InputRef Constant(float value)
        => new InputRef(StandardInputIds.Constant, InputContext.Self,
               new InputParams { MaxRange = value });
    public static InputRef WeaponRangeBandFit(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.WeaponRangeBandFit, ctx);
    public static InputRef WeaponEffectivenessVsTarget(InputContext ctx = InputContext.Self)
        => new InputRef(StandardInputIds.WeaponEffectivenessVsTarget, ctx);
    // Internal helper (also used by UtilityTestWorld.Fnv1a32)
    internal static uint Fnv1a32(string name) { ... }
}
```

All `In.Xxx` methods return `InputRef`; no code for validation. `Fnv1a32` uses basis `2166136261u`, prime `16777619u`.

#### 1e. `Curve` static helper class

```csharp
public static class Curve
{
    public static ResponseCurve Linear         => new ResponseCurve(CurveKind.Linear);
    public static ResponseCurve InverseLinear  => new ResponseCurve(CurveKind.InverseLinear);
    public static ResponseCurve Threshold      => new ResponseCurve(CurveKind.Threshold);
    public static ResponseCurve Bell           => new ResponseCurve(CurveKind.Bell);
    public static ResponseCurve Step           => new ResponseCurve(CurveKind.Step);
    public static ResponseCurve Logistic       => new ResponseCurve(CurveKind.Logistic);
    public static ResponseCurve Quadratic      => new ResponseCurve(CurveKind.Quadratic);
    public static ResponseCurve InverseQuadratic => new ResponseCurve(CurveKind.InverseQuadratic);
    // With params:
    public static ResponseCurve WithSlope(CurveKind kind, float slope)
        => new ResponseCurve(kind, slope: slope);
}
```

#### 1f. `Ctx` static helper class

```csharp
public static class Ctx
{
    public const InputContext Self      = InputContext.Self;
    public const InputContext Target    = InputContext.Target;
    public const InputContext Leader    = InputContext.Leader;
    public const InputContext Candidate = InputContext.Candidate;
}
```

#### 1g. `IUtilityOptionBuilder` interface and `IUtilityDecisionBuilder` interface

```csharp
public interface IUtilityOptionBuilder
{
    IUtilityOptionBuilder Consider(InputRef input, float weight, ResponseCurve curve);
}

public interface IUtilityDecisionBuilder
{
    // Fixed-option decision (PostureSelect, WeaponSelection with explicit ids)
    IUtilityDecisionBuilder Option(ushort optionId, ScoringMode mode,
                                   Action<IUtilityOptionBuilder> configure);
    // Candidate-iterating option (ThreatRanking, etc.) — uses optionId = 0
    IUtilityDecisionBuilder CandidateOption(ScoringMode mode,
                                             Action<IUtilityOptionBuilder> configure);
}
```

#### 1h. `UtilityDecisionBuilder` concrete implementation

Implements `IUtilityDecisionBuilder` and `IUtilityOptionBuilder`. Accumulates `UtilityOption` instances in a `List<UtilityOption>`. Provides `Build(UtilityDecisionAttribute attr) -> UtilityDecisionDef` method.

**Key behavior:**
- Each `Option(id, mode, configure)` creates a `UtilityOption { OptionId = id, Mode = mode }`, calls `configure` passing `this` (or a nested builder), populates `Considerations`.
- `CandidateOption` uses `OptionId = 0` (single template option for candidate decisions).
- `Build(attr)` creates `UtilityDecisionDef { BlueprintId = ComputeId(attr.AssetId), DebugName = attr.DisplayName, Kind = attr.Kind, Options = [...] }`.
- Apply D-05 assert inside `Option()` before assigning `OptionId`.
- `ComputeId(assetId)` uses FNV-1a-32 on the raw GUID string bytes (same formula as `In.Fnv1a32`).

**Note:** `UtilityDecisionBuilder` is entirely managed (lists, lambdas) — no allocation constraints, used only at startup.

---

### Task 2: `UtilityDecisionCatalog` and `UtilityRegistry`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` — NEW FILE
**Design reference:** `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` §5 (startup handshake)

#### 2a. `UtilityRegistry`

```csharp
public sealed class UtilityRegistry
{
    private readonly Dictionary<int, (UtilityDecisionDef def, float hysteresisBonus)> _entries = new();

    public void Register(int id, UtilityDecisionDef def, float hysteresisBonus = 0f)
        => _entries[id] = (def, hysteresisBonus);

    public bool TryGet(int id, out UtilityDecisionDef def, out float hysteresisBonus)
    {
        if (_entries.TryGetValue(id, out var entry)) { def = entry.def; hysteresisBonus = entry.hysteresisBonus; return true; }
        def = null!; hysteresisBonus = 0f; return false;
    }
}
```

#### 2b. `UtilityDecisionCatalog`

Reflective scanner — Phase 1 substitute for the Phase-2 source generator.

```csharp
public static class UtilityDecisionCatalog
{
    private static UtilityRegistry? _shared;

    /// <summary>
    /// Scans all loaded assemblies for types implementing <see cref="IUtilityDecisionDefinition"/>
    /// and carrying <see cref="UtilityDecisionAttribute"/>, calls their static Build method,
    /// and populates the output <paramref name="registry"/>.
    /// Also stores the registry as a process-wide shared instance for use by systems
    /// (e.g. ThreatMatrixAssignmentSystem) that don't hold an explicit reference.
    /// </summary>
    public static void RegisterAll(out UtilityRegistry registry)
    {
        registry = new UtilityRegistry();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in asm.GetTypes())
            {
                if (!typeof(IUtilityDecisionDefinition).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<UtilityDecisionAttribute>();
                if (attr == null) continue;
                var buildMethod = type.GetMethod("Build",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(IUtilityDecisionBuilder) }, null);
                if (buildMethod == null) continue;
                var builder = new UtilityDecisionBuilder();
                buildMethod.Invoke(null, new object[] { builder });
                var def = builder.Build(attr);
                registry.Register(def.BlueprintId, def, attr.HysteresisBonus);
            }
        }
        _shared = registry;
    }

    /// <summary>
    /// Returns the shared registry populated by the most recent <see cref="RegisterAll"/> call.
    /// Throws if <see cref="RegisterAll"/> has not been called.
    /// </summary>
    public static UtilityRegistry Shared
        => _shared ?? throw new InvalidOperationException(
               "UtilityDecisionCatalog.RegisterAll must be called before accessing Shared.");

    /// <summary>
    /// Computes the FNV-1a-32 integer ID for the given asset GUID string.
    /// Decision classes can use this to define their static <c>Id</c> field.
    /// </summary>
    public static int ComputeId(string assetId) => (int)In.Fnv1a32(assetId);
}
```

**Important:** `ThreatMatrixAssignmentSystem` will use `UtilityDecisionCatalog.Shared.TryGet(...)`.

---

### Task 3: `ThreatMatrixAssignmentSystem`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs` — NEW FILE
**Task Definition:** See `.dev/utility-ai/TASK-DETAIL.md` §TASK-UAI-P1-07
**Design Reference:** `.dev/utility-ai/Utility_AI_Design_v1_1.md` §10.2 and §10.3

The system runs on the leader entity and writes greedy assignments into `ThreatMatrixAssignmentState`.

#### 3a. Class structure

```csharp
public sealed class ThreatMatrixAssignmentSystem
{
    private readonly int _decisionId;
    private readonly int _focusFireCap;

    public ThreatMatrixAssignmentSystem(int decisionId, int focusFireCap = 2)
    {
        _decisionId    = decisionId;
        _focusFireCap  = focusFireCap;
    }

    public void Run(EntityRepository repo, Entity leader) { ... }
}
```

#### 3b. `Run` implementation

**Algorithm (match §10.2 exactly):**

1. Retrieve `UtilityDecisionDef def` from `UtilityDecisionCatalog.Shared` by `_decisionId`. If not found, return early.

2. Read `UnitRoster` from the leader to get the list of member entities (packed longs via `UnitRoster.IndexOf` / iterating slots). Build a `Span<Entity>` or local array of member entities (up to 16).

3. Read the leader's `TargetMemory` for the list of targets (`TargetMemory.EntityIds`, count from `TargetMemory.Count`). Up to 16 targets.

4. Compute the (n × m) score matrix on the stack:
   - For each (memberIdx, targetIdx) pair, call the static `UtilityScorer.Evaluate` with `self = member`, `context = target`, using a **temporary stack-allocated `UtilityResultBuffer`** (local variable, not from repo).
   - Store the resulting top score (from `output.GetSpanRO()[0].Score` if `output.Count > 0`, else 0f) into a `float[,]` or flattened stack buffer.

   > **Note on UtilityScorer:** Task 4 makes `UtilityScorer` a non-static class. The static `Evaluate` overload remains. Use it here: `UtilityScorer.Evaluate(repo, member, in def, target, ref tmpBuffer, null)`.

5. Apply greedy assignment with focus-fire bias (§10.2 steps 2-4):
   - Maintain a `focusFireCounts[]` array (one per target, up to 16) initialized to 0.
   - Maintain a `assigned[]` bool array (one per member) initialized to false.
   - Repeat until all members assigned or no targets remain:
     - Find the (member, target) pair with the highest score among unassigned members.
     - If `focusFireCounts[targetIdx] >= _focusFireCap`, skip that target for this member and find next best.
     - Assign the pair: mark member assigned, increment `focusFireCounts[targetIdx]`.
   - If a member has no scoreable target (all scores 0 or all targets exhausted), leave its slot as 0 (unassigned).

6. Write the assignments into `ThreatMatrixAssignmentState` projected from the leader's `Blackboard1024`:
   - For each assigned member, call `state.GetSlot(rosterIdx).AssignedTargetHandle = target.PackedValue`.
   - For unassigned members, set `AssignedTargetHandle = 0`.
   - Set `FocusFireCount` on each slot to `focusFireCounts[targetIdx]`.

**Success Conditions to verify:** SC-P1-07-1, SC-P1-07-2, SC-P1-07-3 (see TASK-DETAIL.md §TASK-UAI-P1-07).

**Tests for this task (in StarterPackIntegrationTests.cs):**
- `Assignment_Greedy_ObeysFocusFireCap`: 3 members, 2 targets, cap=2. Assert no target gets > 2 shooters.
- `Assignment_AllMembersGetAssigned_When_Targets_Available`: 3 members, 3 targets, cap=1. Each member gets a unique target.
- See also `LeaderAssignmentTests` in Task 5 for the full integration test.

---

### Task 4: Refactor `UtilityScorer` — Add Instance API

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — MODIFY
**Design Reference:** `.dev/utility-ai/TASK-DETAIL.md` §TASK-UAI-P1-05 (forward-looking API note)

The current `UtilityScorer` is a `static class`. The starter pack and `UtilityTestWorld` need `new UtilityScorer(registry)`. The change is additive:

**Change:** Remove `static` from `public static class UtilityScorer`.
All existing `public static` methods remain exactly as they are. Add:

```csharp
private readonly UtilityRegistry _registry;

public UtilityScorer(UtilityRegistry registry)
    => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

/// <summary>
/// Looks up the decision by <paramref name="decisionId"/>, resolves the entity's
/// UtilityResultBuffer and optional trace buffer, and runs the scoring core.
/// </summary>
public unsafe void Evaluate(EntityRepository repo, Entity self, int decisionId,
                             Entity context = default, ushort tick = 0)
{
    if (!_registry.TryGet(decisionId, out var def, out _)) return;
    ref var output = ref repo.GetComponentRW<UtilityResultBuffer>(self);
    UtilityTraceWorkingMemory1024* tracePtr = null;
    if (repo.HasComponent<UtilityDebugFlags>(self) &&
        repo.GetComponentRO<UtilityDebugFlags>(self).TraceEnabled &&
        repo.HasComponent<UtilityTraceWorkingMemory1024>(self))
    {
        ref var traceMem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(self);
        tracePtr = (UtilityTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref traceMem);
    }
    Evaluate(repo, self, in def, context, ref output, tracePtr, tick);
}

/// <summary>
/// Selects the winning posture option, applying the decision's authored hysteresis bonus.
/// Returns the winning OptionId as a byte (cast to Posture if needed).
/// </summary>
public unsafe byte SelectPosture(EntityRepository repo, Entity self, int decisionId,
                                  ushort tick = 0)
{
    if (!_registry.TryGet(decisionId, out var def, out float hysteresis)) return 0;
    ref var output = ref repo.GetComponentRW<UtilityResultBuffer>(self);
    UtilityTraceWorkingMemory1024* tracePtr = null;
    if (repo.HasComponent<UtilityDebugFlags>(self) &&
        repo.GetComponentRO<UtilityDebugFlags>(self).TraceEnabled &&
        repo.HasComponent<UtilityTraceWorkingMemory1024>(self))
    {
        ref var traceMem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(self);
        tracePtr = (UtilityTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref traceMem);
    }
    // Read current active posture from last result (WinningPostureId of top slot).
    byte activePostureId = output.Count > 0 ? output.GetSpanRO()[0].WinningPostureId : (byte)0;
    return SelectPosture(repo, self, in def, activePostureId, hysteresis, ref output, tracePtr, tick);
}
```

**Verify:** All existing tests in `UtilityScorerTests.cs` still compile and pass (they use static `UtilityScorer.Evaluate(...)` which remains unchanged).

---

### Task 5: StarterPack Decisions and Integration Tests

#### 5a. `Posture` enum

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/Posture.cs` — NEW FILE

```csharp
namespace Fdp.Toolkit.Utility
{
    public enum Posture : byte
    {
        AdvanceAndAttack = 1,
        TakeCover        = 2,
        Suppress         = 3,
        Flee             = 4,
        Hold             = 5,
    }
}
```

Values start at 1 (not 0) so that 0 remains an uninitialized sentinel.

#### 5b. Four Starter-Pack Decision Classes

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/WeaponSelectionDecision.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/CombatPostureDecision.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/LeaderAssignmentDecision.cs`

**Design Reference:** `.dev/utility-ai/Utility_AI_StarterPack_Examples_v1_1.md` §1.1, §2.1, §3.1, §4.1

Implement the four decisions exactly as specified in the starter pack design doc. Each class:
- Is annotated `[UtilityDecision(AssetId = "...", ...)]`
- Implements `IUtilityDecisionDefinition`
- Has `public static readonly int Id = UtilityDecisionCatalog.ComputeId("...");` (same AssetId)
- Has `public static void Build(IUtilityDecisionBuilder b)` — exact considerations from the design doc

**Notes on `LeaderAssignmentDecision`:**
The starter pack doc §4.1 shows `In.MemberHasLosToContact` and `In.MemberWeaponEffVsContact` — these are NOT in the Phase-1 catalog. Replace them with catalog-available equivalents:
- `In.HasLineOfSight(Ctx.Candidate)` approximates `MemberHasLosToContact`: when `self=member` and `context=target`, this checks if the target appears in the member's TargetMemory with Visual modality.
- `In.WeaponEffectivenessVsTarget(Ctx.Self)` approximates `MemberWeaponEffVsContact`: checks the member's weapon effectiveness vs the context target.

The `LeaderAssignmentDecision` for Phase 1 (only catalog inputs):
```csharp
public static void Build(IUtilityDecisionBuilder b) => b
    .CandidateOption(Mode.WeightedProduct, o => o
        .Consider(In.HasLineOfSight(Ctx.Candidate),          w: 1.0f, Curve.Step)
        .Consider(In.WeaponEffectivenessVsTarget(Ctx.Self),  w: 1.0f, Curve.Linear)
        .Consider(In.DistanceToContext(Ctx.Candidate),       w: 0.6f, Curve.InverseLinear)
        .Consider(In.ContactThreatLevel(Ctx.Candidate),      w: 0.9f, Curve.Linear));
```

#### 5c. `UtilityTestWorld` extensions

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` — MODIFY

Add the following to the existing `UtilityTestWorld`:

1. **Constructor additions** — after existing registrations, add:
   ```csharp
   Repo.RegisterComponent<UtilityResultBuffer>();
   StandardInputs.RegisterAll();
   UtilityDecisionCatalog.RegisterAll(out var registry);
   Scorer = new UtilityScorer(registry);
   ```

2. **New public field:**
   ```csharp
   public readonly UtilityScorer Scorer;
   ```

3. **`SpawnAgent` addition** — add after existing components:
   ```csharp
   Repo.AddComponent<UtilityResultBuffer>(entity);
   Repo.AddComponent(entity, new UtilityDebugFlags { TraceEnabled = true });
   Repo.AddComponent<UtilityTraceWorkingMemory1024>(entity);
   ```
   Check that the existing `SpawnAgent` doesn't already add these (BATCH-04 review confirmed they were added). If already present, skip.

4. **`SetHealth` method** — test-only shortcut:
   ```csharp
   public void SetHealth(Entity entity, float health01)
   {
       ref var h = ref Repo.GetComponentRW<Health>(entity);
       h.Current = health01 * h.Max;
   }
   ```

5. **`SetEnemyStrengthRatio` method** — test-only shortcut that adjusts the entity's TargetMemory threat scores to produce the desired ratio from `EnemyStrengthRatio`. See §7 of the starter pack doc for what this really does. A simple implementation: seed a contact with `threatBoost` such that `sum(ThreatScores)` relative to agent health produces the ratio. OR: since `EnemyStrengthRatio` computes `sum(ThreatScores) / (healthFraction * MaxTrackedTargets)`, set the contact's threat score directly:
   ```csharp
   public void SetEnemyStrengthRatio(Entity self, float ratio)
   {
       // EnemyStrengthRatio = sum(ThreatScores) / (healthFraction * MaxTrackedTargets)
       // => sum(ThreatScores) = ratio * healthFraction * MaxTrackedTargets
       // For a single contact seeded already, adjust its threat score to achieve the ratio.
       // Simplest: reseed a single dummy contact with the required aggregate threat.
       ref readonly var h   = ref Repo.GetComponentRO<Health>(self);
       float healthFraction = h.Max > 0 ? Math.Clamp(h.Current / h.Max, 0f, 1f) : 0f;
       float targetSum      = ratio * healthFraction * PerceptionConstants.MaxTrackedTargets;
       ref var tm = ref Repo.GetComponentRW<TargetMemory>(self);
       // Clear existing contacts and insert one with the aggregate score.
       tm = default;
       var dummy = Repo.CreateEntity(); // ephemeral stand-in
       TargetMemory.AddOrUpdateTarget(ref tm, (long)dummy.PackedValue, 0f, 0f,
                                      scoreBoost: targetSum, tick: ++Tick, modality: SensorModality.Visual);
   }
   ```
   This is a test-only shortcut. Document it clearly.

6. **`SpawnTarget` helper** (for leader assignment tests):
   ```csharp
   public Entity SpawnTarget()
   {
       var t = Repo.CreateEntity();
       Repo.AddComponent(t, new Health { Current = 100f, Max = 100f });
       Repo.AddComponent(t, new Position { Value = Vector3.Zero });
       return t;
   }
   ```

7. **`SeedSquadContacts` helper:**
   ```csharp
   public void SeedSquadContacts(Entity leader, Entity[] targets)
   {
       foreach (var t in targets)
           SeedContact(leader, t, 120f, 0.6f, 1f, hasLos: true);
   }
   ```

> **Warning:** `UtilityDecisionCatalog.RegisterAll` scans ALL loaded assemblies. In unit tests, this means it will find all `[UtilityDecision]` classes in the test assembly. This is intentional — the starter-pack tests rely on it. However, `StandardInputs.RegisterAll()` and `UtilityDecisionCatalog.RegisterAll()` may be called once per `UtilityTestWorld` construction. To avoid duplicate registration, add a guard: check `UtilityInputRegistrar._readers.Count > 0` before calling `RegisterAll()`, OR clear the registrar in `Dispose()`. The cleanest approach: add a static flag.

#### 5d. Integration tests

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs` — NEW FILE
**Namespace:** `Fdp.Toolkit.Tests` (D-04 normalization)
**Design Reference:** `.dev/utility-ai/Utility_AI_StarterPack_Examples_v1_1.md` §1.2, §2.2, §3.2, §4.3, §5

Implement all integration tests from the starter pack doc. See exact test code in the design document. Test class breakdown:

**`ThreatRankingTests`** — 2 tests (§1.2):
- `Closer_Visible_HighThreat_Contact_Ranks_First` — 3 contacts, LOS gate, distance ordering
- `Assigned_Target_Bias_Promotes_Leader_Choice` — leader assignment biases member's threat ranking

**`WeaponSelectionTests`** — 1 test (§2.2):
- `OutOfRange_And_Empty_Weapons_Are_Gated_Out` — empty pistol gated, launcher vs rifle range band

**`CombatPostureTests`** — 5 tests (§3.2):
- `Healthy_Outnumbering_Advances`
- `Hurt_With_Cover_Available_Takes_Cover`
- `NearDeath_With_Escape_Flees`
- `NearDeath_With_No_Escape_And_No_Cover_Does_Not_Flee_Into_Nothing` (Hold fallback)
- `Hysteresis_Prevents_Flip_Flop_On_Marginal_Inputs` (SC-P1-08-3)

**`LeaderAssignmentTests`** — 2 tests (§4.3):
- `Greedy_Assignment_Spreads_Fire_With_FocusFire_Bias` (SC-P1-07-1, SC-P1-07-2)
- `Wounded_Member_Vetoes_Assignment_And_Breaks_Off` (SC-P1-07-3, SC-P1-08-4)

**`TraceIntegrationTests`** — 1 test (§5):
- `Trace_Records_Per_Consideration_Breakdown_For_Winner` (SC-P1-08-2)

**Total new tests in this file:** at least 11.

> **On `Greedy_Assignment_Spreads_Fire_With_FocusFire_Bias`:**
> The starter pack doc shows m3 (launcher member) routed to t2 (heavy), but for Phase 1 without ArmorInfo, the routing may differ. Implement the test as follows:
> - 3 members (m1, m2 rifles; m3 with launcher child at effRange=350m), 2 targets at distance 0.
> - Seed contacts into the LEADER's TargetMemory AND into each MEMBER's TargetMemory (call `SeedContact` for each member).
> - Assert **focus-fire cap = 2**: no target has more than 2 members assigned. This is the core invariant (SC-P1-07-2).
> - Assert that all 3 members are assigned to some target (no nulls).
> - The exact routing (m3→t2) can be asserted only if the scoring produces a clear differentiation. If weapon-type differentiation doesn't work cleanly in Phase 1, assert the cap invariant only and note the limitation in the report.

---

## 🧪 Testing Requirements

**Minimum new tests:** 15 tests in `StarterPackIntegrationTests.cs` (including the 2 unit tests from Task 3).
**All 100 prior utility tests must still pass.**

### Test Quality Standards

**REQUIRED:**
- Each test constructs a real `UtilityTestWorld`, seeds a specific scenario, runs the scorer or assignment system, and asserts a behavioral outcome (not just that code compiles).
- Posture tests assert a specific `Posture` value, not just "non-null" or "non-zero".
- Hysteresis test (SC-P1-08-3) MUST assert that the same posture is returned on the second call after a 1% health nudge.
- Trace test (SC-P1-08-2) MUST assert `cover.RawValue` (or similar) and `cover.CurveOutput > 0.8f` — not just that a record exists.
- Veto test (SC-P1-08-4) MUST assert `Posture.Flee` after the assignment is written.

**NOT ACCEPTABLE:**
- Tests that just assert `Score > 0` or "not null".
- Tests that only verify compilation.
- Trivially passing tests (e.g., both options score 0).

---

## ⚠️ Quality Standards

**TEST QUALITY:** Every test must exercise a specific behavioral claim about the scoring system. The design doc is very explicit about expected outcomes — the tests should match those claims exactly.

**CORRECTNESS:** The fluent builder must produce `UtilityDecisionDef` instances that are functionally equivalent to hand-building them with `new UtilityOption { ... Considerations = [...] }`. Verify parity in the trace test by comparing against a hand-built equivalent.

**NAMESPACE:** All new test files must use namespace `Fdp.Toolkit.Tests`. Apply this to the two fixed files too.

**COMMENTS:** Do not add comments beyond what is needed to explain non-obvious decisions. Do not reformat existing comments.

---

## 📊 Report Requirements

Submit `.dev/utility-ai/reports/BATCH-05-REPORT.md` covering:

1. **Summary:** What was implemented; total test count.
2. **Per-task status:** Files created/modified with a one-liner on what changed.
3. **Issues encountered:** What was harder than expected? How did you resolve it?
4. **Design decisions beyond spec:** What choices did you make? What alternatives did you consider?
5. **Weak points:** What areas of the codebase could be improved?
6. **Edge cases discovered:** Anything not in the instructions?
7. **Suggested commit message.**

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] D-04: namespace normalized in both test files; all 100 prior tests pass
- [ ] D-05: `Debug.Assert` for `OptionId <= 255` in builder; DEBT-TRACKER updated
- [ ] D-06: DEBT-TRACKER.md row marked RESOLVED
- [ ] Task 1: `UtilityDecisionBuilderInfra.cs` compiles; all 17 `In.Xxx()` methods present
- [ ] Task 2: `UtilityDecisionCatalog.RegisterAll(out registry)` finds all 4 starter-pack decisions
- [ ] Task 3: `ThreatMatrixAssignmentSystem.Run` writes correct assignments; SC-P1-07-1 and SC-P1-07-2 pass
- [ ] Task 4: `new UtilityScorer(registry)` works; instance `Evaluate`/`SelectPosture` work; static API unchanged
- [ ] Task 5: All 4 decisions implement their `Build` method; all 11+ integration tests pass
- [ ] SC-P1-08-1: All starter-pack tests pass green
- [ ] SC-P1-08-2: Trace test reads per-consideration breakdown
- [ ] SC-P1-08-3: Hysteresis test passes
- [ ] SC-P1-08-4: Wounded-member veto test passes
- [ ] Full test suite: 115+ tests pass, no regressions

---

## 📚 Reference Materials

- **Task Definitions:** `.dev/utility-ai/TASK-DETAIL.md` — §TASK-UAI-P1-07, §TASK-UAI-P1-08
- **Architecture (§10, §11):** `.dev/utility-ai/Utility_AI_Design_v1_1.md`
- **Starter Pack (all sections):** `.dev/utility-ai/Utility_AI_StarterPack_Examples_v1_1.md`
- **Debt Tracker:** `.dev/utility-ai/DEBT-TRACKER.md`
- **Existing scorer:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs`
- **Existing core structs:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs`
- **Standard inputs:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs`
- **Assignment state:** `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs`
- **Test world:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`
