# BATCH-24 Instructions — Phase 3: Commander Utility Tick + Squad Inputs + Mission Override + Starter Pack

**Covers:** TASK-SQD-P3-01, TASK-SQD-P3-02, TASK-SQD-P3-03, TASK-SQD-P3-04  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §8.0  
**Task details:** `.dev/group-maneuvers/TASK-DETAIL.md` (search for P3-01 through P3-04)

---

## Context

All Phase 0-2 squad work is committed.  Key existing types you must know:

| Symbol | Location |
|---|---|
| `SquadCognitiveState` | `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` |
| `SquadContactPool` | same file |
| `UnitRoster` | `FDP/Toolkits/Fdp.Toolkits/CommandHierarchy/Components/...` (use `Fdp.Core.CommandHierarchy`) |
| `UtilityScorer.Evaluate` | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` |
| `UtilityResultBuffer` | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityResultBuffer.cs` |
| `UtilityTraceWorkingMemory1024` | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityResultBuffer.cs` |
| `SquadInputs` | `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs` |
| `DangerAreaCognitiveBuffer` | `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs` |
| `DangerAreaDescriptor` / `DangerAreaKind` | `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaDescriptor.cs` |
| `ITacticalOrderMapper` | `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs` |
| `Health` (CombatHealth) | `FDP/Toolkits/Fdp.Toolkits/Combat/Components/Health.cs` |
| `WeaponState` | `FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs` |
| `UtilityInputCtx` | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` |
| `InputParams` | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` |

`SquadCognitiveState.Flags` bit 0 is the MissionOverride bit — constant:
```csharp
private const uint MissionOverrideBit = 1u;
```

---

## Task 1 (P3-01 prerequisite): Add `LastManeuverSelectTick` to `SquadContactPool`

**File:** `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs`

In `SquadContactPool`, replace:
```csharp
        private ulong _r1;
```
with:
```csharp
        /// <summary>Tick at which the last ManeuverSelect scorer pass ran.</summary>
        public uint LastManeuverSelectTick;
        private uint _r1hi;
```

This is a reserved-field rename; total struct size stays 592 bytes. The layout test (`SquadCognitiveStateLayoutTests`) requires no changes — the 1024-byte total is preserved.

---

## Task 2 (P3-01): `CommanderUtilityTickSystem`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/CommanderUtilityTickSystem.cs`

```
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Squad.Systems
```

```csharp
/// <summary>
/// Runs the Utility AI ManeuverSelect scorer on a squad commander at a decimated
/// cadence (~10 Hz).  Writes the winning option id into
/// <see cref="SquadCognitiveState.ManeuverKind"/>.
/// </summary>
/// <remarks>
/// Mission-override guard: when <c>state.Flags</c> bit-0 (MissionOverrideBit) is set
/// the scorer is skipped and <c>state.ManeuverKind</c> retains its forced value.
/// Cadence: runs when <c>currentTick - state.Contacts.LastManeuverSelectTick >= tickInterval</c>
/// OR on first call (<c>LastManeuverSelectTick == 0</c>).
/// </remarks>
public static unsafe class CommanderUtilityTickSystem
{
    private const uint MissionOverrideBit = 1u;

    /// <param name="repo">Active ECS repository.</param>
    /// <param name="commander">Entity to evaluate (must carry Blackboard1024 and UtilityResultBuffer).</param>
    /// <param name="maneuverSelectDef">The ManeuverSelect UtilityDecisionDef to score.</param>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <param name="tickInterval">Minimum ticks between re-scores (default 6 ≈ 10 Hz at 60 tps).</param>
    public static void Run(
        EntityRepository repo,
        Entity commander,
        in UtilityDecisionDef maneuverSelectDef,
        uint currentTick,
        uint tickInterval = 6)
    {
        // Guards.
        if (!repo.HasComponent<Blackboard1024>(commander)) return;
        if (!repo.HasComponent<UtilityResultBuffer>(commander)) return;

        ref var state = ref SquadCognitiveState.Project(
            ref repo.GetComponentRW<Blackboard1024>(commander));

        // Mission-override: skip scoring, retain forced ManeuverKind.
        if ((state.Flags & MissionOverrideBit) != 0) return;

        // Cadence gate.
        bool firstRun = state.Contacts.LastManeuverSelectTick == 0;
        bool dwellElapsed = currentTick - state.Contacts.LastManeuverSelectTick >= tickInterval;
        if (!firstRun && !dwellElapsed) return;

        state.Contacts.LastManeuverSelectTick = currentTick;

        // Optional trace buffer.
        UtilityTraceWorkingMemory1024* tracePtr = null;
        if (repo.HasComponent<UtilityTraceWorkingMemory1024>(commander))
        {
            tracePtr = (UtilityTraceWorkingMemory1024*)Unsafe.AsPointer(
                ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(commander));
        }

        ref var output = ref repo.GetComponentRW<UtilityResultBuffer>(commander);
        UtilityScorer.Evaluate(repo, commander, in maneuverSelectDef,
            Entity.Null, ref output, tracePtr, (ushort)currentTick);

        if (output.Count > 0)
            state.ManeuverKind = output.Top().WinningPostureId;
    }
}
```

---

## Task 3 (P3-02): Five new squad-commander Utility input readers in `SquadInputs.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs`

### 3a. New `SquadInputIds` entries

Add five FNV-1a-16 constants.  Use the same algorithm: `Fnv1a32(name) & 0xFFFF` with basis `2166136261` and prime `16777619`.  You must compute these accurately.  Approximate expected values (verify by unit test or manual computation):

| Name | Expected constant |
|---|---|
| `SquadStrengthRatio` | compute |
| `SquadAmmoRollup` | compute |
| `ActiveFeatureThreatRating` | compute |
| `ActiveFeatureKindIs` | compute |
| `SquadPoolThreatAggregate` | compute |

Add them to `SquadInputIds` with comments documenting the FNV-1a provenance (same style as existing entries).

### 3b. New reader methods

Add the following to `SquadInputs`, and register all five in `RegisterAll()`.

**All readers have `ctx.Self = commander`** — they do NOT walk up to a commander via `UnitSubordinate`; instead the caller must pass the commander entity as `Self`.

---

#### `SquadStrengthRatio(in UtilityInputCtx ctx)`

Walk `ctx.Self`'s `UnitRoster`.  For each member (`roster.SubordinateEntities[m]`), accumulate `Health.Current` (numerator) and `Health.Max` (denominator) if the member entity has a `Health` component.  Return `sum_current / sum_max` clamped to `[0,1]`.  If no members have `Health`, return 1f (full strength assumed).

Default safe: if `ctx.Self` has no `UnitRoster` or no `Blackboard1024`, return 1f.

```csharp
[UtilityInput("SquadStrengthRatio")]
public static float SquadStrengthRatio(in UtilityInputCtx ctx) { ... }
```

---

#### `SquadAmmoRollup(in UtilityInputCtx ctx)`

Walk `ctx.Self`'s `UnitRoster`.  For each member, accumulate `WeaponState.Ammo` (numerator, clamped to 0) and `WeaponState.MaxAmmo` (denominator, skip if 0).  Return `sum_ammo / sum_maxAmmo` clamped `[0,1]`.  Return 1f if no members have a positive `MaxAmmo`.

Default safe: if `ctx.Self` has no `UnitRoster`, return 1f.

```csharp
[UtilityInput("SquadAmmoRollup")]
public static float SquadAmmoRollup(in UtilityInputCtx ctx) { ... }
```

---

#### `ActiveFeatureThreatRating(in UtilityInputCtx ctx)`

1. Check `ctx.Self` has `Blackboard1024`; project `SquadCognitiveState`.
2. Get `state.ActiveFeatureId`.  If 0, return 0f.
3. Check `ctx.Self` has `DangerAreaCognitiveBuffer`.  If not, return 0f.
4. Iterate `buffer.GetSpanRO()[0..buffer.Count]` looking for `descriptor.FeatureId == state.ActiveFeatureId`.
5. Return the matching `descriptor.ThreatRating` clamped `[0,1]`, or 0f if not found.

Default safe: any missing component returns 0f.

```csharp
[UtilityInput("ActiveFeatureThreatRating")]
public static float ActiveFeatureThreatRating(in UtilityInputCtx ctx) { ... }
```

---

#### `ActiveFeatureKindIs(in UtilityInputCtx ctx)`

Parameterized: the caller encodes the `DangerAreaKind` byte into `ctx.Params.BlueprintId` (low byte).

1. Same steps 1-4 as `ActiveFeatureThreatRating`.
2. Compare the found descriptor's `Kind` against `(DangerAreaKind)(ctx.Params.BlueprintId & 0xFF)`.
3. Return 1f if match, 0f otherwise.

```csharp
[UtilityInput("ActiveFeatureKindIs")]
public static float ActiveFeatureKindIs(in UtilityInputCtx ctx) { ... }
```

**Usage example in a decision:**
```csharp
new UtilityConsideration(SquadInputIds.ActiveFeatureKindIs, InputContext.Self, weight: 0.9f,
    curve: new ResponseCurve(CurveKind.Linear, slope: 1f),
    @params: new InputParams { BlueprintId = (uint)DangerAreaKind.StreetCrossing })
```

---

#### `SquadPoolThreatAggregate(in UtilityInputCtx ctx)`

1. Check `ctx.Self` has `Blackboard1024`; project `SquadCognitiveState`.
2. Sum `state.Contacts` pool threat scores for all valid contacts.  Max possible score = `16 * 1.0f = 16.0f`.
3. Return `sum / 16.0f` clamped `[0,1]`.

Default safe: missing `Blackboard1024` returns 0f.

```csharp
[UtilityInput("SquadPoolThreatAggregate")]
public static float SquadPoolThreatAggregate(in UtilityInputCtx ctx) { ... }
```

---

## Task 4 (P3-03): `ForceManeuverMapper` + `ClearForceManeuverMapper`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Mappers/ForceManeuverMapper.cs`

```
namespace Fdp.Toolkit.Squad.Mappers
```

Add `using System.Text.Json;` for JSON parsing.

### `ForceManeuverMapper`

```csharp
/// <summary>
/// Maps a "ForceManeuver" tactical intent to a direct write of
/// <see cref="SquadCognitiveState.ManeuverKind"/> + MissionOverride flag.
/// JSON payload: <c>{"maneuverKind":&lt;ushort&gt;,"featureId":&lt;uint?&gt;}</c>
/// </summary>
public sealed class ForceManeuverMapper : ITacticalOrderMapper
{
    public string TargetIntentId => "ForceManeuver";

    public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                       out AssignBehaviorEvent assignment)
    {
        assignment = null!;
        if (!repo.HasComponent<Blackboard1024>(self)) return false;

        // Parse JSON.
        ForceManeuverParams p;
        try { p = JsonSerializer.Deserialize<ForceManeuverParams>(jsonParams); }
        catch { return false; }

        ref var state = ref SquadCognitiveState.Project(
            ref repo.GetComponentRW<Blackboard1024>(self));

        state.ManeuverKind   = p.ManeuverKind;
        state.Flags         |= MissionOverrideBit;
        if (p.FeatureId.HasValue)
            state.ActiveFeatureId = p.FeatureId.Value;

        assignment = new AssignBehaviorEvent { Entity = self, BehaviorName = string.Empty };
        return true;
    }

    private const uint MissionOverrideBit = 1u;
}
```

Add a private DTO `ForceManeuverParams` in the same file:
```csharp
internal sealed class ForceManeuverParams
{
    public ushort ManeuverKind { get; set; }
    public uint?  FeatureId   { get; set; }
}
```

Use `JsonSerializerOptions` with `PropertyNameCaseInsensitive = true` when deserializing.

### `ClearForceManeuverMapper`

In the same file:
```csharp
/// <summary>
/// Clears the MissionOverride flag so the commander's Utility scorer resumes.
/// No JSON parameters required.
/// </summary>
public sealed class ClearForceManeuverMapper : ITacticalOrderMapper
{
    public string TargetIntentId => "ClearForceManeuver";

    public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                       out AssignBehaviorEvent assignment)
    {
        assignment = null!;
        if (!repo.HasComponent<Blackboard1024>(self)) return false;

        ref var state = ref SquadCognitiveState.Project(
            ref repo.GetComponentRW<Blackboard1024>(self));

        state.Flags &= ~MissionOverrideBit;

        assignment = new AssignBehaviorEvent { Entity = self, BehaviorName = string.Empty };
        return true;
    }

    private const uint MissionOverrideBit = 1u;
}
```

---

## Task 5 (P3-04): `ManeuverSelectStarterDecision` + integration test

### 5a. New file: `FDP/Toolkits/Fdp.Toolkits/Squad/StarterPack/ManeuverSelectStarterDecision.cs`

```
namespace Fdp.Toolkit.Squad.StarterPack
```

Expose a single public static property:

```csharp
/// <summary>
/// Worked-example ManeuverSelect decision for a squad commander.
/// Three options: DangerAreaCross (0), BoundOverwatch (1), Hold (2).
///
/// Considerations reference squad-commander Utility inputs registered by
/// <see cref="Fdp.Toolkit.Utility.SquadInputs.RegisterAll"/>.
///
/// Option weightings (WeightedProduct mode):
///   DangerAreaCross  — SquadStrengthRatio(Linear,0.6) + ActiveFeatureKindIs(SC|CP,0.9) + SquadAmmoRollup(Threshold@0.3,0.5)
///   BoundOverwatch   — SquadStrengthRatio(Linear,0.8) + ActiveFeatureKindIs(OpenGround,0.7) + ActiveFeatureThreatRating(Logistic,0.6)
///   Hold             — ActiveFeatureThreatRating(Linear,0.9) + SquadAmmoRollup(InverseLinear,0.5)
/// </summary>
public static class ManeuverSelectStarterDecision
{
    public const ushort OptionIdDangerAreaCross  = 0;
    public const ushort OptionIdBoundOverwatch   = 1;
    public const ushort OptionIdHold             = 2;

    public static UtilityDecisionDef Build() => new UtilityDecisionDef
    {
        DebugName = "ManeuverSelect",
        Kind      = DecisionKind.ManeuverSelect,
        Options   = new[]
        {
            new UtilityOption
            {
                OptionId = OptionIdDangerAreaCross,
                Mode     = ScoringMode.WeightedProduct,
                Considerations = new[]
                {
                    new UtilityConsideration(SquadInputIds.SquadStrengthRatio,
                        InputContext.Self, weight: 0.6f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                    // ActiveFeatureKindIs(StreetCrossing): BlueprintId encodes the DangerAreaKind byte.
                    new UtilityConsideration(SquadInputIds.ActiveFeatureKindIs,
                        InputContext.Self, weight: 0.9f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: 1f),
                        @params: new InputParams { BlueprintId = (uint)DangerAreaKind.StreetCrossing }),
                    new UtilityConsideration(SquadInputIds.SquadAmmoRollup,
                        InputContext.Self, weight: 0.5f,
                        curve: new ResponseCurve(CurveKind.Step, xShift: 0.3f)),
                }
            },
            new UtilityOption
            {
                OptionId = OptionIdBoundOverwatch,
                Mode     = ScoringMode.WeightedProduct,
                Considerations = new[]
                {
                    new UtilityConsideration(SquadInputIds.SquadStrengthRatio,
                        InputContext.Self, weight: 0.8f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                    new UtilityConsideration(SquadInputIds.ActiveFeatureKindIs,
                        InputContext.Self, weight: 0.7f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: 1f),
                        @params: new InputParams { BlueprintId = (uint)DangerAreaKind.OpenGround }),
                    new UtilityConsideration(SquadInputIds.ActiveFeatureThreatRating,
                        InputContext.Self, weight: 0.6f,
                        curve: new ResponseCurve(CurveKind.Logistic, slope: 6f, xShift: 0.5f)),
                }
            },
            new UtilityOption
            {
                OptionId = OptionIdHold,
                Mode     = ScoringMode.WeightedProduct,
                Considerations = new[]
                {
                    new UtilityConsideration(SquadInputIds.ActiveFeatureThreatRating,
                        InputContext.Self, weight: 0.9f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                    new UtilityConsideration(SquadInputIds.SquadAmmoRollup,
                        InputContext.Self, weight: 0.5f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: -1f, yShift: 1f)),
                }
            },
        }
    };
}
```

**Note:** `CurveKind.Linear` with `slope: -1f, yShift: 1f` is "InverseLinear" (1 - x).  Verify that `ResponseCurve` supports `yShift`; if not, use `slope: -1f` and clamp the output — check `ResponseCurve` constructor signature in `UtilityCore.cs`.

---

## Task 6: Tests

### 6a. `CommanderUtilityTickSystemTests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/CommanderUtilityTickSystemTests.cs`

Register squad inputs before tests (`SquadInputs.RegisterAll()`).

**SC-P3-01-1:** Commander with a stub `ManeuverSelect` def (two constant-score options, option 0 = 0.9f, option 1 = 0.1f) selects option 0; `state.ManeuverKind == 0`.
- Build the stub def using trivial constant-value readers (register two fresh reader IDs).
- Commander has `Blackboard1024` + `UtilityResultBuffer` (registered components).
- Call `CommanderUtilityTickSystem.Run(repo, commander, def, currentTick: 1)`.
- Assert `state.ManeuverKind == 0`.

**SC-P3-01-2:** Same setup but `state.Flags |= 1u` (MissionOverride) before calling.  Set `state.ManeuverKind = 99` before call.  Assert `state.ManeuverKind == 99` after call (scorer skipped).

**SC-P3-01-3:** Call Run at tick 1 (runs), call again at tick 3 (within interval=6, should NOT run — `ManeuverKind` stays unchanged if we flip the stub reader scores between calls).
- Call at tick 1 → option 0 wins.
- Swap stub reader values (option 1 now higher).
- Call at tick 3 → ManeuverKind still 0 (cadence gate blocks re-score).
- Call at tick 7 → ManeuverKind flips to 1 (interval elapsed).

**SC-P3-01-4:** Commander has `UtilityTraceWorkingMemory1024` component. After Run, `trace.RecordCount > 0`.

---

### 6b. `SquadInputsP3Tests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsP3Tests.cs`

Call `SquadInputs.RegisterAll()` in constructor.

Build a minimal ECS world for each test:
- Commander entity with `UnitRoster` (2 members), `Blackboard1024`, optionally `DangerAreaCognitiveBuffer`.
- Member entities with `Health` / `WeaponState` components.

**SC-P3-02-1 (SquadStrengthRatio):**
- Two members with `Health { Current=100, Max=100 }` → ratio = 1.0f.
- Kill one member (`Health { Current=0, Max=100 }`) → ratio = 0.5f.
- Commander with no `UnitRoster` → returns 1f (default safe).

**SC-P3-02-2 (ActiveFeatureThreatRating, no active feature):**
- Commander has `Blackboard1024` + `DangerAreaCognitiveBuffer` but `state.ActiveFeatureId == 0`.
- Returns 0f.

**SC-P3-02-3 (ActiveFeatureKindIs flip):**
- Set `state.ActiveFeatureId = 42`, add descriptor `{ FeatureId=42, Kind=StreetCrossing, ThreatRating=0.8f }` to buffer.
- `ActiveFeatureKindIs(Params.BlueprintId = (uint)DangerAreaKind.StreetCrossing)` → 1f.
- `ActiveFeatureKindIs(Params.BlueprintId = (uint)DangerAreaKind.OpenGround)` → 0f.
- Swap `state.ActiveFeatureId = 99` (descriptor not in buffer) → 0f.

**SC-P3-02-4 (SquadAmmoRollup):**
- Two members with `WeaponState { Ammo=100, MaxAmmo=100 }` → 1.0f.
- Set one member `Ammo=0` → 0.5f.
- No members with `WeaponState` → 1f.

**SC-P3-02-5 (zero-alloc):**
- Capture `GC.GetTotalAllocatedBytes()` before and after 1,000,000 calls to each reader.
- Assert zero difference (all readers must be zero-alloc).

---

### 6c. `ForceManeuverMapperTests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Mappers/ForceManeuverMapperTests.cs`

Build ECS world with a commander entity having `Blackboard1024`.

**SC-P3-03-1:** `ForceManeuverMapper.TryMap` with `"{\"maneuverKind\":1}"` sets `state.ManeuverKind == 1` and `(state.Flags & 1u) != 0`.

**SC-P3-03-2:** After step above, `ClearForceManeuverMapper.TryMap` clears the bit; `(state.Flags & 1u) == 0`.  Subsequent `CommanderUtilityTickSystem.Run` runs the scorer (ManeuverKind may change).

**SC-P3-03-3:** Commander without `Blackboard1024` → `TryMap` returns `false`.

---

### 6d. `Phase3IntegrationTests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Phase3IntegrationTests.cs`

**Setup:** `SquadInputs.RegisterAll()`. Two-member squad commander with:
- `UnitRoster` (2 full-health members with `Health { Current=100, Max=100 }`).
- `Blackboard1024`, `UtilityResultBuffer`, `UtilityTraceWorkingMemory1024`.
- `DangerAreaCognitiveBuffer` directly on commander (for test simplicity).
- Use `ManeuverSelectStarterDecision.Build()`.

**SC-P3-04-1 (StreetCrossing → DangerAreaCross wins):**
- Add descriptor `{ FeatureId=1, Kind=StreetCrossing, ThreatRating=0.5f }` to commander's `DangerAreaCognitiveBuffer` (buffer.Count=1).
- Set `state.ActiveFeatureId = 1`.
- Run `CommanderUtilityTickSystem.Run(repo, commander, def, currentTick: 1)`.
- Assert `state.ManeuverKind == ManeuverSelectStarterDecision.OptionIdDangerAreaCross (0)`.

**SC-P3-04-2 (OpenGround → BoundOverwatch wins):**
- Update the descriptor: `Kind=OpenGround, ThreatRating=0.4f` (moderate threat, full strength).
- Reset `state.Contacts.LastManeuverSelectTick = 0` (force re-score).
- Run `CommanderUtilityTickSystem.Run(repo, commander, def, currentTick: 100)`.
- Assert `state.ManeuverKind == ManeuverSelectStarterDecision.OptionIdBoundOverwatch (1)`.

**SC-P3-04-3 (trace populated):**
- After SC-P3-04-1, assert `trace.RecordCount > 0`.

**SC-P3-04-4 (MissionOverride → Hold forced):**
- Set `state.ManeuverKind = ManeuverSelectStarterDecision.OptionIdHold (2)` and `state.Flags |= 1u`.
- Run `CommanderUtilityTickSystem.Run(repo, commander, def, currentTick: 1000)`.
- Assert `state.ManeuverKind == 2` (not changed by scorer).

---

## Required namespace using patterns

The `CommanderUtilityTickSystem` needs:
```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Utility;
```

The `ForceManeuverMapper` needs:
```csharp
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Squad;
```

The `ManeuverSelectStarterDecision` needs:
```csharp
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Utility;
```

---

## Checklist

Before submitting the batch report verify:

- [ ] `SquadCognitiveStateLayoutTests` still passes (no size change).
- [ ] `CommanderUtilityTickSystemTests`: all 4 SC pass.
- [ ] `SquadInputsP3Tests`: all 5 SC pass (including zero-alloc).
- [ ] `ForceManeuverMapperTests`: all 3 SC pass.
- [ ] `Phase3IntegrationTests`: all 4 SC pass.
- [ ] Build produces zero errors and zero new warnings.
- [ ] Total new test count ≥ 16.

## File summary

| Action | File |
|---|---|
| MODIFY | `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/CommanderUtilityTickSystem.cs` |
| MODIFY | `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Mappers/ForceManeuverMapper.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/StarterPack/ManeuverSelectStarterDecision.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/CommanderUtilityTickSystemTests.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsP3Tests.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Mappers/ForceManeuverMapperTests.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Phase3IntegrationTests.cs` |
