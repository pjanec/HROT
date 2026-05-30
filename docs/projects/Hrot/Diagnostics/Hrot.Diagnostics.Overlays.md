# Hrot.Diagnostics.Overlays -- AI Debug Overlay Sources

**Project path:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/`
**Project file:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/Hrot.Diagnostics.Overlays.csproj`
**Test project:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/`
**Primary namespace:** `Hrot.Diagnostics.Overlays`
**Design references:**
- `.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`
**Date:** 2026-05-30

---

## Executive Overview

`Hrot.Diagnostics.Overlays` contains all per-entity gizmo overlay sources for AI subsystem
debug visualization. When a debugger selects an entity, the active overlay sources read
the relevant ECS components and emit screen-space annotations (text labels, spheres, lines).

The library also manages a **budget arbiter** that sheds overlay sources when the combined
rendering time exceeds a configurable per-frame budget.

### What it covers

| Overlay source | Subsystem visualized |
|---|---|
| `PerceptionOverlaySource` | Perceived contacts from perception subsystem |
| `TargetMemoryOverlaySource` | Live contacts from `TargetMemory` (one sphere per contact) |
| `EqsOverlaySource` | EQS solver results (per-candidate score heat map) |
| `UtilityDecisionOverlaySource` | Utility AI scoring trace and option scores |
| `SquadAssignmentOverlaySource` | Squad leader-to-member target assignments |
| `SquadCoordinationOverlaySource` | High-level squad coordination state (P7 features) |
| `OverlayBudgetArbiter` | Budget enforcer -- sheds sources when over budget |

---

## AiOverlayFlags

`AiOverlayFlags` is a `[Flags] ushort` enum stored in `DebugState.Ai` on each entity.
Only the bits set in `DebugState.Ai` are rendered for that entity.

```csharp
[Flags]
public enum AiOverlayFlags : ushort
{
    None              = 0,
    Perception        = 1,
    TargetMemory      = 2,
    Eqs               = 4,
    UtilityDecision   = 8,
    SquadAssignment   = 16,
    Channels          = 32,
}
```

Toggle from the debugger UI:

```csharp
// Enable Utility Decision overlay for entity:
ref var ds = ref repo.GetComponentRW<DebugState>(entity);
ds.Ai |= AiOverlayFlags.UtilityDecision;
```

---

## Overlay Sources

### PerceptionOverlaySource

Reads the perception subsystem's sensor outputs for the selected entity and emits text labels
at perceived contact positions.

### TargetMemoryOverlaySource

Reads `TargetMemory` from the entity. For each tracked contact:
- Draws a wire sphere (radius proportional to threat level) at the contact world position.
- Labels the sphere with the contact handle, threat level, and LOS flag.

Guard: emits nothing when `AiOverlayFlags.TargetMemory` is not set.

### EqsOverlaySource

Reads `EqsCognitiveBuffer` from the entity. For each EQS query result:
- Draws a grid of colored markers at candidate positions (green = high score, red = low).
- Labels the top-scoring candidate.

Guard: emits nothing when `AiOverlayFlags.Eqs` is not set.

### UtilityDecisionOverlaySource

Reads `UtilityTraceWorkingMemory1024` (ECS component ID 150) and `UtilityResultBuffer` (ID
151) from the entity. Emits:

- A text panel listing the most-recent decision result: winning option, score, runner-up margin.
- Per-consideration raw value / curve output / weight for the winning option.
- The `onDecisionSelected` callback fires when the user clicks an entry, enabling the
  editor-console bridge.

Guard: emits nothing when `AiOverlayFlags.UtilityDecision` is not set.

#### Editor-console bridge

When the user clicks a decision result row in the overlay panel, `UtilityDecisionOverlaySource`
fires:

```csharp
public event Action<string>? onDecisionSelected;
// Parameter: decision debug-name (serves as TuningRegistry prefix)
```

The subscriber (typically `TuningConsoleGizmo`) receives the prefix and calls
`OpenForGroup(prefix)` to pre-filter the tuning console to that decision.

### SquadAssignmentOverlaySource

Reads `ThreatMatrixAssignmentState` from the leader's `Blackboard1024`. For each assigned
(member, target) pair draws a thin green line from the member to the target. Annotates with
assignment score.

Guard: emits nothing when `AiOverlayFlags.SquadAssignment` is not set.

### SquadCoordinationOverlaySource

Draws high-level squad coordination state for P7 (squad planning layer):

| Feature | Visual |
|---|---|
| Element coloring | Each squad element is tinted by its role color |
| Veto lines | Dashed red lines from element to vetoed-out candidate positions |
| Phase labels | Floating text labels showing the current coordination phase name |
| Contact pool markers | Star markers at all contacts in the merged squad contact pool |

Guard: reads `SquadCoordinationState` overlay bit (dedicated bit, not listed in `AiOverlayFlags`).

---

## OverlayBudgetArbiter

Enforces a per-frame time budget across all active overlay sources. Receives a measured render
time per source after each frame and sheds sources from the highest-cost end when over budget.

### Shedding order

Sources are shed in priority order (cheapest first, most valuable last):

```
1. Channels            (shed first)
2. SquadCoordination
3. SquadAssignment
4. Perception
5. EQS
6. TargetMemory
7. UtilityDecision     (shed last)
```

Shedding suspends the source for `CooldownFrames` (default 60 frames) before attempting to
re-enable.

### Configuration

```csharp
var arbiter = new OverlayBudgetArbiter(budgetMs: 2.0f, cooldownFrames: 60);
arbiter.RegisterSource(source, priority: 1);   // priority 1 = shed first
arbiter.EndFrame(sourceName, elapsedMs: 0.7f); // report measured cost
```

---

## Source Structure

```
Hrot.Diagnostics.Overlays/
  AiOverlayFlags.cs                    -- [Flags] AiOverlayFlags ushort enum
  EqsOverlaySource.cs
  OverlayBudgetArbiter.cs
  PerceptionOverlaySource.cs
  SquadAssignmentOverlaySource.cs
  SquadCoordinationOverlaySource.cs
  TargetMemoryOverlaySource.cs
  UtilityDecisionOverlaySource.cs
```

---

## Dependencies

| Assembly | Used for |
|---|---|
| `Fdp.Core` | `EntityRepository`, ECS component access |
| `Fdp.Toolkit.Utility` | `UtilityResultBuffer`, `UtilityTraceWorkingMemory1024`, `UtilityDebugFlags` |
| `Fdp.Toolkit.Perception` | `TargetMemory`, `EqsCognitiveBuffer` |
| `Fdp.Toolkit.Behavior.Components` | `Blackboard1024` (squad assignment read) |
| `Fdp.Toolkit.Squad` | `SquadCoordinationState` (P7) |
| `Fdp.Presentation` | `IGizmoSource`, gizmo rendering primitives |
| `Hrot.Diagnostics.Tuning` | `TuningConsoleGizmo.OpenForGroup` (bridge subscriber) |

---

## Implementation Status

All phases complete (2026-05-30):

| Phase | Content |
|---|---|
| Phase 4 | `AiOverlayFlags`, `PerceptionOverlaySource`, `TargetMemoryOverlaySource`, `EqsOverlaySource`, `UtilityDecisionOverlaySource`, `SquadAssignmentOverlaySource`, `OverlayBudgetArbiter` |
| Phase 6 | `UtilityDecisionOverlaySource.onDecisionSelected` editor-console bridge callback |
| Phase 7 | `SquadCoordinationOverlaySource` (element coloring, veto lines, phase labels, contact pool markers) |
