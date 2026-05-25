# WHEN-BATCH-14 — Reactive Guard vocabulary unification + documentation (M8)

**Tasks:** WHEN-M8-T1, WHEN-M8-T2  
**Design reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §14  
**Task detail:** `.dev/blueprints-3-when-node/TASK-DETAIL.md` §WHEN-M8-T1, §WHEN-M8-T2

---

## Context

M8 is vocabulary-only: no runtime changes, no automated tests. Success = compile + smoke.

Three editors share a "Reactive Guards" palette concept:
- **BTree editor** — Observer Selector node
- **HSM editor** — transition guard (set via inspector on transitions)
- **Blueprint editor** — WhenNode

The Blueprint editor already has a stub `ReactiveGuardVocabulary` at:
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/ReactiveGuardVocabulary.cs`

Both BTree and HSM editors reference `Hrot.Editor.AiShared` (confirmed in their `.csproj`).
Blueprint editor does NOT reference `Hrot.Editor.AiShared`.

---

## Deliverables checklist

### File 1 — NEW: `Hrot/Editor/Hrot.Editor.AiShared/ReactiveGuardVocabulary.cs`

Create this file with namespace `Hrot.Editor.AiShared`. Use the exact strings from DESIGN §14.1:

```csharp
namespace Hrot.Editor.AiShared;

/// <summary>
/// Shared string constants for the "Reactive Guards" palette category and tooltips.
/// Used by the BTree, HSM, and Blueprint editors to surface a consistent concept.
/// </summary>
public static class ReactiveGuardVocabulary
{
    public const string CategoryName = "Reactive Guards";

    public const string GenericTooltip =
        "Reactive guards re-evaluate their condition every tick. " +
        "When the condition transitions from false to true (rising edge), " +
        "the guard fires. Each subsystem has its own reactive guard implementation: " +
        "Observer Selectors in BTree, transition guards in HSM, and When nodes in " +
        "Instance Blueprints.";

    public const string BTreeObserverSelectorTooltip =
        "An Observer Selector re-evaluates its guard children every tick from the root, " +
        "preempting lower-priority running children if a higher-priority guard becomes true. " +
        "This is the BTree's reactive guard mechanism.";

    public const string HsmTransitionGuardTooltip =
        "A transition guard is re-evaluated every tick while its source state is active. " +
        "When the guard becomes true, the transition fires (subject to event matching). " +
        "This is the HSM's reactive guard mechanism.";

    /// <summary>Short display label for the Guard inspector field in the HSM editor.</summary>
    public const string HsmTransitionGuardDisplayName = "Guard (Reactive Guard)";

    public const string BlueprintWhenNodeTooltip =
        "A When node re-evaluates its condition every tick. When the condition transitions " +
        "from false to true (rising edge), the OnFired exec output triggers. " +
        "This is the Instance Blueprint's reactive guard mechanism. " +
        "(WhenNode is for Instance Blueprints only; use Observer Selectors in BTrees, " +
        "transition guards in HSMs.)";

    public const string CrossSubsystemHintBTree =
        "If you're familiar with HSM transition guards or Instance Blueprint When nodes, " +
        "Observer Selector children play the same role in a BTree.";

    public const string CrossSubsystemHintHsm =
        "If you're familiar with BTree Observer Selectors or Instance Blueprint When nodes, " +
        "transition guards play the same role in an HSM.";

    public const string CrossSubsystemHintBlueprint =
        "If you're familiar with BTree Observer Selectors or HSM transition guards, " +
        "When nodes play the same role in an Instance Blueprint.";
}
```

---

### File 2 — MODIFY: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/ReactiveGuardVocabulary.cs`

Update the existing stub. Replace its entire content with the updated version that has all constants matching the shared class (kept separate to avoid a cross-project reference):

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Blueprint-editor-local copy of the shared reactive guard vocabulary constants.
/// Kept separate from <c>Hrot.Editor.AiShared.ReactiveGuardVocabulary</c> to avoid
/// adding a project reference in <c>Hrot.Blueprints.Editor</c>.
/// See <c>Hrot/Docs/ReactiveGuards.md</c> for cross-subsystem usage.
/// </summary>
public static class ReactiveGuardVocabulary
{
    public const string CategoryName = "Reactive Guards";

    public const string BlueprintWhenNodeTooltip =
        "A When node re-evaluates its condition every tick. When the condition transitions " +
        "from false to true (rising edge), the OnFired exec output triggers. " +
        "This is the Instance Blueprint's reactive guard mechanism. " +
        "(WhenNode is for Instance Blueprints only; use Observer Selectors in BTrees, " +
        "transition guards in HSMs.)";

    public const string CrossSubsystemHintBlueprint =
        "If you're familiar with BTree Observer Selectors or HSM transition guards, " +
        "When nodes play the same role in an Instance Blueprint.";

    /// <summary>Kept for source-compat; same text as <see cref="CrossSubsystemHintBlueprint"/>.</summary>
    public const string CrossSubsystemHintWhen = CrossSubsystemHintBlueprint;
}
```

---

### File 3 — MODIFY: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs`

Make three targeted changes:

**a) Add using directive** at the top (with other usings):
```csharp
using Hrot.Editor.AiShared;
```

**b) Add a new category constant** after the existing category constants (`CatComposite`, `CatLeaf`, `CatDecorator`):
```csharp
private static readonly string CatReactiveGuard = ReactiveGuardVocabulary.CategoryName;
```

**c) Move ObserverSelector entry** from `CatComposite` to `CatReactiveGuard`, and update its description to include both the tooltip and the cross-system hint. Change the existing `entries.Add` call for ObserverSelector from:
```csharp
entries.Add(Make(BTreeKinds.ObserverSelector, "Observer Selector", CatComposite,
    "Selector with reactive re-evaluation of observer children.",
    new[] { "observer", "selector", "reactive" }, "bt/observer_selector", false, false, false,
    inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));
```
to:
```csharp
entries.Add(Make(BTreeKinds.ObserverSelector, "Observer Selector", CatReactiveGuard,
    ReactiveGuardVocabulary.BTreeObserverSelectorTooltip + "\n\n" + ReactiveGuardVocabulary.CrossSubsystemHintBTree,
    new[] { "observer", "selector", "reactive", "guard" }, "bt/observer_selector", false, false, false,
    inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));
```

**d) Add `CatReactiveGuard` to the `Categories` property** (after `CatDecorator`):
```csharp
public IReadOnlyList<NodeCategoryDescriptor> Categories { get; } = new[]
{
    new NodeCategoryDescriptor(CatComposite, "Composites", "bt/composite"),
    new NodeCategoryDescriptor(CatLeaf,      "Leaves",     "bt/leaf"),
    new NodeCategoryDescriptor(CatDecorator, "Decorators", "bt/decorator"),
    new NodeCategoryDescriptor(CatReactiveGuard, ReactiveGuardVocabulary.CategoryName, null),
};
```

Note: `Categories` is currently a `{ get; } = new[]` property — update it in place.

---

### File 4 — MODIFY: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmNodeCatalog.cs`

Make two targeted changes:

**a) Add using directive** at the top:
```csharp
using Hrot.Editor.AiShared;
```

**b) Add `"Reactive Guards"` to the `_categories` list**. Current `_categories`:
```csharp
private static readonly IReadOnlyList<NodeCategoryDescriptor> _categories =
    new[] { new NodeCategoryDescriptor(CatStates, "States", null) };
```
Change to:
```csharp
private static readonly IReadOnlyList<NodeCategoryDescriptor> _categories = new[]
{
    new NodeCategoryDescriptor(CatStates, "States", null),
    new NodeCategoryDescriptor(ReactiveGuardVocabulary.CategoryName, ReactiveGuardVocabulary.CategoryName, null),
};
```

---

### File 5 — MODIFY: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacets.cs`

Make two targeted changes:

**a) Add using directive** at the top:
```csharp
using Hrot.Editor.AiShared;
```

**b) Update the `[EditDisplayName("Guard")]` annotation** on `TransitionFacet.GuardFunction` to use the vocabulary constant:

Change:
```csharp
    [EditDisplayName("Guard")]
    [HsmGuardPicker]
    public string? GuardFunction;
```
To:
```csharp
    [EditDisplayName(ReactiveGuardVocabulary.HsmTransitionGuardDisplayName)]
    [HsmGuardPicker]
    public string? GuardFunction;
```

Note: `ReactiveGuardVocabulary.HsmTransitionGuardDisplayName` is `const string`, which is valid as an attribute argument in C#.

---

### File 6 — NEW: `Hrot/Docs/ReactiveGuards.md`

Create this Markdown reference (~80 lines). Per DESIGN §14.3:

```markdown
# Reactive Guards

A **reactive guard** is a condition that is re-evaluated every simulation tick.
When the condition transitions from `false` to `true` (rising edge), the guard _fires_,
triggering associated behavior. Guards in Hrot are **level-triggered evaluation** with
**edge-triggered firing**.

---

## Why reactive guards?

Imperative AI (sequences, coroutines, channel commands) works well for _directed_ tasks:
move here, aim there, wait for result. Reactive guards complement this with _opportunistic_
responses to world state changes: "whenever health drops below 30%, switch to cover behavior."

---

## The three implementations

Each Hrot AI subsystem has its own reactive guard primitive. They are equivalent at the
concept level; pick the one that fits your execution model.

### BTree — Observer Selector

An **Observer Selector** re-evaluates its guard children every tick from the root.
If a higher-priority guard child becomes true, it preempts any lower-priority running child.

- **When to use:** You're already authoring a BTree and need reactive branching.
- **Hosting rule:** Works in any BTree, including both `AiPrimitive` and full BTrees.
- **Note:** Non-observer selectors (plain Selectors) do NOT re-evaluate; they resume
  the currently running child.

### HSM — Transition Guard

A **transition guard** is a predicate bound to an HSM transition.
While the source state is active, the guard is re-evaluated every tick.
When it becomes true, the transition fires (subject to event matching).

- **When to use:** You're already authoring an HSM and want a self-resetting condition
  to trigger a state change.
- **Hosting rule:** Guards are attributes of transitions, not nodes. Set the Guard field
  in the transition inspector.
- **Performance note:** Guards are polled every tick while the source state is active.
  Keep predicates O(1).

### Instance Blueprint — When Node

A **When node** re-evaluates its condition every tick (or on rising/falling edges).
When the configured edge fires, the `OnFired` exec output triggers.

- **When to use:** You're already authoring an Instance Blueprint and need to respond
  to world state transitions.
- **Hosting rule:** Instance Blueprints only. `AiPrimitive` Blueprints stay imperative
  (use BTrees or HSMs for reactive behavior in primitives).
- **Modes:** Value Changed, Event Fired, Condition Met, EQS Result.
- **Cross-subsystem note:** If familiar with Observer Selectors or HSM transition guards,
  When nodes serve the same role in Instance Blueprints.

---

## Hosting rules summary

| Reactive guard type | AiPrimitive | Instance Blueprint | BTree | HSM |
|---|---|---|---|---|
| Observer Selector | ✅ | — | ✅ | — |
| Transition Guard | — | — | — | ✅ |
| When Node | — | ✅ | — | — |

---

## Performance characteristics

All three poll every tick. Keep guard predicates:
- Pure (no side effects)
- O(1) — cache results, do not iterate collections inside a guard

---

## EQS helpers: not reactive guards themselves

`SpawnEqsSensorNode` and `ReadEqsResultNode` are **EQS-specific helpers**, not reactive
guards. They live in the **"EQS" palette category** for this reason.

`WhenNode` in **EQS Result** mode is the reactive guard; the EQS nodes supply its input:

```
SpawnEqsSensor → [handle] → WhenNode (EQS Result mode) → OnFired → behavior
                 [handle] → ReadEqsResult → Entity, Position, Score
```

---

## Canonical patterns and recipes

- `CoverAwarePatrol.bp.json` — EQS pipeline with WhenNode (EQS Result mode)
- `HealthThresholdReaction.bp.json` — WhenNode (Condition Met mode)
- `SquadAwareEngagement.bp.json` — WhenNode (Value Changed, PeerBlueprintVariable source)

Recipe files live in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/`.
```

---

## Build verification

After implementing all files, run:

```powershell
Set-Location "d:\WORK\IOS-IG-SimHost-FDP"
dotnet build Hrot\Subsystems\AI\Hrot.BTree.Editor\Hrot.BTree.Editor.csproj 2>&1 | Select-String "error|warning" | Select-Object -Last 10
dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj 2>&1 | Select-String "error|warning" | Select-Object -Last 10
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Editor\Hrot.Blueprints.Editor.csproj 2>&1 | Select-String "error|warning" | Select-Object -Last 10
```

All three must produce zero errors. Warnings are acceptable.

---

## Notes

- M8 has no automated tests. Success = zero build errors.
- The `NodeCategoryDescriptor` record has no `Description` field, so HSM "Reactive Guards"
  category shows up as a filter heading only (no entries under it — this is intentional;
  transition guards are set via the transition inspector, not the node palette).
- `[EditDisplayName(ReactiveGuardVocabulary.HsmTransitionGuardDisplayName)]` is valid C# 
  because `const string` values in other classes are compile-time constants and can be
  used as attribute arguments.
- Do NOT add a project reference from `Hrot.Blueprints.Editor` to `Hrot.Editor.AiShared`.
  The Blueprint editor stub stays independent.
