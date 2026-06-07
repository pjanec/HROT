# BATCH-12 Instructions — BATCH-11 Corrective Fixes + Phase 4 Part 1 (P4-01 + P4-02)

**Phase tasks covered:**
- Corrective fixes from BATCH-11-REVIEW.md (P1 + P2 issues)
- TASK-UAI-P4-01: `AiOverlayFlags` + per-entity gating
- TASK-UAI-P4-02: Five overlay sources + `OverlayBudgetArbiter`

**Design references:**
- `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` §5.2 — handles/locked-params table
- `.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §6, §7, §9
- Existing: `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BehaviorDebugFlags.cs`
- Existing: `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/DebugState.cs`
- Existing: `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/TraceBufferLifecycleSystem.cs`
- Existing: `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityTraceWorkingMemory1024.cs`
- Existing: `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Sources/IGizmoSource.cs`

**Report location:** `.dev/utility-ai/reports/BATCH-12-REPORT.md`

---

## Part A — Corrective fixes from BATCH-11

These are required fixes identified in `.dev/utility-ai/reviews/BATCH-11-REVIEW.md`.

### A1. Fix `IsParamEditable` for Linear and InverseLinear

**File:** `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidget.cs`

In `IsParamEditable`, the `"b"` and `"c"` cases are wrong for `Linear` and `InverseLinear`.

Per Editor DD §5.2:

| CurveKind | Handles | Locked params |
|---|---|---|
| Linear / InverseLinear | endpoint handles → `m`, `c` | `k=1`, **`b` from left endpoint** |

`b` appears in the Locked column → `b` is NOT editable in the numeric field for Linear/InverseLinear.
`c` has an endpoint handle → `c` IS editable for Linear/InverseLinear.

**Current (wrong):**
```csharp
"b" => true, // always true - wrong for Linear/InverseLinear
"c" => kind is CurveKind.Threshold or CurveKind.Bell or CurveKind.Step
            or CurveKind.PiecewiseLinear,  // misses Linear/InverseLinear
```

**Fix to:**
```csharp
"b" => kind is not (CurveKind.Linear or CurveKind.InverseLinear),
"c" => kind is CurveKind.Linear or CurveKind.InverseLinear
            or CurveKind.Threshold or CurveKind.Bell or CurveKind.Step
            or CurveKind.PiecewiseLinear,
```

Also update the comment block above the `return param switch` to match:
```
// Linear/InverseLinear : handles->m,c; locked: k=1, b from left endpoint  -> m=yes k=no b=NO c=YES
// Threshold/Step       : handles->b,c; locked: m, k                       -> m=no  k=no b=yes c=yes
// Bell                 : handles->b,k,c; locked: m                        -> m=no  k=yes b=yes c=yes
// Logistic             : handles->b,k; locked: m, c                       -> m=no  k=yes b=yes c=no
// Quadratic/InverseQ   : handles->k,b; locked: m, c                       -> m=no  k=yes b=yes c=no
// PiecewiseLinear      : none locked (points are the data)                 -> all yes
```

### A2. Fix and extend `IsParamEditable` Theory tests

**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs`

Update the two `InlineData` entries that assert wrong values and add two new InverseLinear cases:

```csharp
// Change these two (wrong values):
[InlineData(CurveKind.Linear, "b", true)]   // was true, must be false
[InlineData(CurveKind.Linear, "c", false)]  // was false, must be true

// Add these two (InverseLinear coverage):
[InlineData(CurveKind.InverseLinear, "b", false)]
[InlineData(CurveKind.InverseLinear, "c", true)]
```

Total Theory case count after fix: 23 cases (was 19).

### A3. Add cross-check tests: `CurveWidget.Evaluate` vs `ResponseCurve.Evaluate`

**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs`

Add the following two Theory tests to `CurveWidgetEvaluateTests`. They cross-check that
`CurveWidget.Evaluate` produces the same result as `ResponseCurve.Evaluate` at 16 evenly-
spaced sample points. These are the regression guards that catch any divergence between the
widget's delegation path and the runtime formula.

The 16 sample points (x values): 0, 1/15, 2/15, 3/15, 4/15, 5/15, 6/15, 7/15, 8/15,
9/15, 10/15, 11/15, 12/15, 13/15, 14/15, 1.

Use `[InlineData]` with all 16 float literals, or use a `[MemberData]` source that generates
`Enumerable.Range(0, 16).Select(i => new object[] { i / 15f })`.

```csharp
public static IEnumerable<object[]> SixteenSamples =>
    Enumerable.Range(0, 16).Select(i => new object[] { i / 15f });

[Theory]
[MemberData(nameof(SixteenSamples))]
public void Evaluate_Linear_MatchesResponseCurve(float x)
{
    var rc = new ResponseCurve(CurveKind.Linear, slope: 0.8f, exponent: 1f, xShift: 0.1f);
    var uc = new UtilityCurve { Kind = CurveKind.Linear, M = 0.8f, K = 1f, B = 0.1f, C = 0f };
    // CurveWidget.Evaluate delegates to ResponseCurve.Evaluate then clamps; C=0 so result is identical.
    float expected = Math.Clamp(rc.Evaluate(x), 0f, 1f);
    Assert.Equal(expected, CurveWidget.Evaluate(in uc, x), precision: 5);
}

[Theory]
[MemberData(nameof(SixteenSamples))]
public void Evaluate_Logistic_MatchesResponseCurve(float x)
{
    var rc = new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: 6f, xShift: 0.5f);
    var uc = new UtilityCurve { Kind = CurveKind.Logistic, M = 1f, K = 6f, B = 0.5f, C = 0f };
    float expected = Math.Clamp(rc.Evaluate(x), 0f, 1f);
    Assert.Equal(expected, CurveWidget.Evaluate(in uc, x), precision: 5);
}
```

After Part A, total test count in `Hrot.Utility.Editor.Tests`:
- Original 35 - 2 updated InlineData + 2 new InlineData + 2 new MemberData theories (32 cases each) = 35 - 0 removed + 2 InlineData added + 32 + 32 data rows = approximately 35 + 4 Theory cases + 64 data-driven = 35 + 68 = **103 total test executions** (xUnit counts each InlineData/MemberData row as 1 test).

---

## Part B — TASK-UAI-P4-01: `AiOverlayFlags` + per-entity gating

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §6.1

### B1. New `AiOverlayFlags` enum

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/AiOverlayFlags.cs` (new file)

Create in namespace `Fdp.Toolkit.Behavior.Diagnostics`, same namespace as `BehaviorDebugFlags`:

```csharp
using System;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Per-entity AI subsystem overlay toggles. Carried in <see cref="DebugState.Ai"/>
    /// alongside <see cref="BehaviorDebugFlags"/> in the <see cref="DebugState"/> family.
    /// Off-by-default; near-zero cost when all bits are zero (a single flag check per entity
    /// in the gizmo source query).
    /// </summary>
    [Flags]
    public enum AiOverlayFlags : ushort
    {
        None            = 0,
        Perception      = 1 << 0,   // FOV cone, LOS rays, sensor ring
        TargetMemory    = 1 << 1,   // known contacts, aging, threat value
        Eqs             = 1 << 2,   // scored candidate points, Top-K highlight
        UtilityDecision = 1 << 3,   // per-option bars, winner, consideration breakdown
        SquadAssignment = 1 << 4,   // leader-member-target assignment lines
        Channels        = 1 << 5,   // active locomotion/weapon/interaction action
    }
}
```

### B2. Extend `DebugState` with an `Ai` field

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/DebugState.cs`

Add `public AiOverlayFlags Ai;` as the second field:

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(BehaviorApplicationComponentIds.DebugState)]
[DataPolicy(DataPolicy.Transient)]
public struct DebugState
{
    public BehaviorDebugFlags Behavior;
    public AiOverlayFlags     Ai;
}
```

`DebugState` remains a blittable struct; the new field adds 2 bytes (padded to 4 for alignment
inside the ECS chunk; verify `sizeof(DebugState)` == 8 after the change in a test).

### B3. Tests for P4-01

Add a test class `AiOverlayFlagsTests` in `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Diagnostics/`
(or nearest appropriate location):

```csharp
// SC-P4-01 tests
public class AiOverlayFlagsTests
{
    [Fact]
    public void AiOverlayFlags_IsUshort_WithFlagsAttribute()
    {
        Assert.True(typeof(AiOverlayFlags).IsEnum);
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(AiOverlayFlags)));
        Assert.True(Attribute.IsDefined(typeof(AiOverlayFlags), typeof(FlagsAttribute)));
    }

    [Fact]
    public void DebugState_HasAiField_And_SizeIsEight()
    {
        Assert.Equal(8, Unsafe.SizeOf<DebugState>());
    }

    [Fact]
    public void DebugState_DefaultAiFieldIsNone()
    {
        var ds = default(DebugState);
        Assert.Equal(AiOverlayFlags.None, ds.Ai);
    }

    [Fact]
    public void DebugState_BehaviorFieldUnchanged_WhenAiSet()
    {
        // Setting Ai must not disturb Behavior bits and vice versa.
        var ds = new DebugState
        {
            Behavior = BehaviorDebugFlags.EnableTraceBuffer,
            Ai       = AiOverlayFlags.UtilityDecision,
        };
        Assert.Equal(BehaviorDebugFlags.EnableTraceBuffer, ds.Behavior);
        Assert.Equal(AiOverlayFlags.UtilityDecision, ds.Ai);
    }
}
```

---

## Part C — TASK-UAI-P4-02: Five overlay sources + `OverlayBudgetArbiter`

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §6, §7

### C1. New project: `Hrot.Diagnostics.Overlays`

**Location:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/`

**Project file** (`Hrot.Diagnostics.Overlays.csproj`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Hrot.Diagnostics.Overlays.Tests" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj" />
    <ProjectReference Include="..\..\..\FDP\Diagnostics\Fdp.Diagnostics.Contracts\Fdp.Diagnostics.Contracts.csproj" />
    <ProjectReference Include="..\..\..\FDP\ExtDeps\GizmoMap\GizmoMap.Contracts\GizmoMap.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**New test project** (`Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hrot.Diagnostics.Overlays\Hrot.Diagnostics.Overlays.csproj" />
    <ProjectReference Include="..\..\..\FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj" />
  </ItemGroup>
</Project>
```

Note: referencing `Fdp.Toolkits.Tests` gives access to `UtilityTestWorld` for ECS test scaffolding.

### C2. `OverlayBudgetArbiter`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/OverlayBudgetArbiter.cs`

Purpose: enforces `GlobalDebugSettings.MaxGizmoFrameMs`; sheds lowest-priority overlay families
first. Priority order (lowest first): `Channels`, `SquadAssignment`, `Eqs`, `TargetMemory`,
`Perception`, `UtilityDecision` (highest).

```csharp
namespace Hrot.Diagnostics.Overlays
{
    // Gating wrapper: tracks elapsed gizmo emit time and gates further emission.
    // Sheds lowest-priority flags first when the frame budget is exceeded.
    internal sealed class OverlayBudgetArbiter
    {
        // Priority order (shedding): Channels < SquadAssignment < Eqs < TargetMemory
        //                          < Perception < UtilityDecision (highest priority, shed last)
        private static readonly AiOverlayFlags[] ShedOrder = new[]
        {
            AiOverlayFlags.Channels,
            AiOverlayFlags.SquadAssignment,
            AiOverlayFlags.Eqs,
            AiOverlayFlags.TargetMemory,
            AiOverlayFlags.Perception,
            AiOverlayFlags.UtilityDecision,
        };

        private readonly float _budgetMs;
        private float _usedMs;
        private AiOverlayFlags _active; // flags still permitted this frame

        public OverlayBudgetArbiter(float budgetMs)
        {
            _budgetMs = budgetMs;
            _active   = (AiOverlayFlags)0xFFFF; // all enabled at frame start
        }

        // Call at the start of each frame to reset state.
        public void BeginFrame()
        {
            _usedMs = 0f;
            _active = (AiOverlayFlags)0xFFFF;
        }

        // Record that 'elapsedMs' milliseconds were spent emitting the given family.
        // Returns false if the family was shed due to budget exhaustion.
        public bool RecordAndCheck(AiOverlayFlags family, float elapsedMs)
        {
            _usedMs += elapsedMs;
            if (_usedMs <= _budgetMs)
                return true;

            // Over budget: shed the lowest-priority active family.
            foreach (var f in ShedOrder)
            {
                if ((_active & f) != 0)
                {
                    _active &= ~f;
                    break;
                }
            }

            return (_active & family) != 0;
        }

        // Returns true if the given overlay family is still permitted this frame.
        public bool IsPermitted(AiOverlayFlags family) => (_active & family) != 0;
    }
}
```

### C3. Five overlay sources

Each overlay source is a class implementing `IGizmoSource`:

```
Hrot/Diagnostics/Hrot.Diagnostics.Overlays/
├── PerceptionOverlaySource.cs
├── TargetMemoryOverlaySource.cs
├── EqsOverlaySource.cs
├── UtilityDecisionOverlaySource.cs
├── SquadAssignmentOverlaySource.cs
```

**Common constructor pattern:** Each source takes the `EntityRepository` as a constructor argument:
```csharp
public sealed class XxxOverlaySource : IGizmoSource
{
    private readonly EntityRepository _repo;
    private readonly OverlayBudgetArbiter _budget;

    public XxxOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
    {
        _repo   = repo;
        _budget = budget;
    }

    public void Emit(float deltaTime, IGizmoDrawBuilder draw)
    {
        if (!_budget.IsPermitted(AiOverlayFlags.Xxx)) return;

        // Query entities with DebugState.Ai having the relevant bit set.
        var q = _repo.Query().With<DebugState>().Build();
        foreach (var entity in q)
        {
            ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
            if ((ds.Ai & AiOverlayFlags.Xxx) == 0) continue;

            EmitForEntity(entity, draw);
        }
    }

    private void EmitForEntity(Entity entity, IGizmoDrawBuilder draw)
    {
        // (See per-source spec below)
    }
}
```

**Per-source implementation notes:**

#### `UtilityDecisionOverlaySource`
- Requires entity to also have `UtilityTraceWorkingMemory1024` component.
- Calls `UtilityTraceWorkingMemory1024.LatestSelected` to get the winner option.
- If no trace data (`RecordCount == 0`), emit nothing.
- Emit a `StructInspector`-anchored label via `draw.EmitRaw(DebugPrimitive.MakeSpatialAnchor(...))` then a text badge:
  Use `draw.DrawTextLong(...)` for the multi-line decision breakdown.
  Minimum: one `DrawText` per entity showing the decision name + winning option.
  Full layout from §7.4 wireframe is the stretch goal but not required for SC satisfaction.

#### `PerceptionOverlaySource`
- Gate: `(ds.Ai & AiOverlayFlags.Perception) != 0`
- If entity has `AutonomousPerceptionModule` component, draw an arrow from entity position showing
  FOV orientation. If not present, emit zero primitives (do NOT throw).
- Minimum for SC: zero primitives when flag is absent, at least one primitive when flag is set and
  the entity has relevant perception components.

#### `TargetMemoryOverlaySource`
- Gate: `(ds.Ai & AiOverlayFlags.TargetMemory) != 0`
- If entity has `TargetMemory` component, emit one `DrawSphere` per non-null contact entry at
  the contact's last-known position. If `TargetMemory` not present, emit zero.
- Minimum: gating test passes.

#### `EqsOverlaySource`
- Gate: `(ds.Ai & AiOverlayFlags.Eqs) != 0`
- Emit zero if no `EqsCognitiveBuffer` (or equivalent) is present on the entity.
- Minimum: gating test passes.

#### `SquadAssignmentOverlaySource`
- Gate: `(ds.Ai & AiOverlayFlags.SquadAssignment) != 0`
- If entity has `SquadState` (or commander blackboard), draw solid line to assigned target and
  dashed line to actually-engaged target when they differ.
- Minimum: gating test passes.

**Note on stubs:** For P4-02, the per-source `EmitForEntity` bodies may be minimal stubs that emit
at least one test primitive (e.g. a `DrawText` label) when the entity has the required component
AND the relevant flag. The important constraints are: (a) zero primitives when flag is unset, and
(b) the GizmoMap primitive API is used correctly. The rich visual fidelity of §7 comes in Phase 5+.

### C4. Add both new projects to `IOS-IG-SimHost.sln`

Add `Hrot.Diagnostics.Overlays` and `Hrot.Diagnostics.Overlays.Tests` to the solution file with:
- `Project(...)` entries using fresh GUIDs
- `GlobalSection(ProjectConfigurationPlatforms)` entries for `Debug|Any CPU.ActiveCfg` and `.Build.0`
- Nested under the existing `Diagnostics` solution folder (same as `Hrot.Diagnostics.Breakpoints`)

### C5. Tests for P4-02

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs`

Use a minimal counting `IGizmoDrawBuilder` stub:

```csharp
internal sealed class CountingDrawBuilder : IGizmoDrawBuilder
{
    public int EmitCount;
    public void DrawLine(...) => EmitCount++;
    public void DrawSphere(...) => EmitCount++;
    public void DrawText(...) => EmitCount++;
    public void DrawTextLong(...) => EmitCount++;
    public void DrawArrow(...) => EmitCount++;
    public void DrawBox2D(...) => EmitCount++;
    public void DrawLineGradient(...) => EmitCount++;
    public void DrawMainMenuBinding(string json) => EmitCount++;
    public void EmitRaw(in DebugPrimitive p) => EmitCount++;
    public void EndFrame(float dt) {}
}
```

**Required test cases:**

```
SC-P4-01-1: Entity without DebugState emits zero overlay primitives from UtilityDecisionOverlaySource
SC-P4-01-2: Entity with DebugState.Ai = AiOverlayFlags.UtilityDecision and UtilityTraceWorkingMemory1024
            with one scored option emits at least one primitive from UtilityDecisionOverlaySource.

SC-P4-02-1: UtilityDecisionOverlaySource reads UtilityTraceWorkingMemory1024; entity with
            written trace produces at least one primitive. (covers SC-P4-02-1)

SC-P4-02-2 (budget arbiter):
  - Create arbiter with budgetMs=1f
  - Record 2f ms for AiOverlayFlags.Channels -> returns true (first call)
  - Total is now 2f > 1f; arbiter sheds Channels (lowest priority)
  - IsPermitted(Channels) -> false
  - IsPermitted(UtilityDecision) -> still true (highest priority, not yet shed)
```

Additional gating tests for each of the 5 sources:
- Each source: entity without relevant AiOverlayFlags emits zero primitives.
- Each source: entity with flag but missing required ECS component emits zero primitives (no throw).

Minimum total: 15 test methods. Each should assert actual values, not just "no exception".

---

## Part D — Build and test requirements

1. `dotnet build IOS-IG-SimHost.sln` must succeed with zero errors and zero warnings.
2. `dotnet test Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj` must pass
   all tests (original 35 + new corrective/cross-check tests).
3. `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj` must remain green
   (the new `AiOverlayFlagsTests` are additions, no existing test should break).
4. `dotnet test Hrot\Diagnostics\Hrot.Diagnostics.Overlays.Tests\Hrot.Diagnostics.Overlays.Tests.csproj`
   must pass all 15+ tests.

---

## Key constraints

- Do NOT add `AiOverlayFlags` to `Hrot.Diagnostics.Overlays` — it lives in `Fdp.Toolkit.Behavior.Diagnostics`
  so that `DebugState` (in `Fdp.Toolkits`) can carry the `Ai` field without an upward reference.
- `DebugState` grows from 4 to 8 bytes (BehaviorDebugFlags=uint + AiOverlayFlags=ushort + 2 bytes padding for natural alignment). Assert this with `Unsafe.SizeOf<DebugState>() == 8`.
- Overlay sources must NOT throw when an expected component is absent — just emit zero primitives.
- The rich visual fidelity of each overlay (e.g. FOV cone geometry) is a stretch goal. The required
  minimum is: correct gating behavior (flag off → zero primitives, flag on + component present → at
  least one primitive), correct budget-arbiter shed order, and clean compile.
- Do NOT start Phase 4 P4-03 (TuningRegistry / TuningConsoleGizmo) — that is BATCH-13.

---

## Report checklist

Your BATCH-12 report (`.dev/utility-ai/reports/BATCH-12-REPORT.md`) must include:
- List of all files created/modified
- Test results for all three test projects (exact pass/fail counts)
- Note on `sizeof(DebugState)` value
- Any design decisions made about stub vs full implementations in overlay sources
