# BATCH-27 — HS-S1-05 through HS-S1-08

## Tasks
- **HS-S1-05** `HsmFluentEmitter` (Emit\HsmFluentEmitter.cs) + tests
- **HS-S1-06** `HsmNodeCatalog` + `HsmKinds` (Host\HsmNodeCatalog.cs, Host\HsmKinds.cs)
- **HS-S1-07** `HsmTypeSystem` + `HsmLinkValidator` (Host\HsmTypeSystem.cs, Host\HsmLinkValidator.cs) + tests
- **HS-S1-08** `HsmCommandSink` (Host\HsmCommandSink.cs) — stub with Apply switch

## Repository root
`d:\Work\IOS-IG-SimHost-FDP-2\`

## AGENTS.md rules (MUST follow)
1. Preserve all existing comments exactly.
2. Do NOT use Unicode characters in comments or string literals — ASCII only.
3. Minimize textual diffs (only change lines required for the fix).
4. Build MUST compile with 0 errors and 0 warnings. Verify by running:
   `dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj`
   `dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj`
   then:
   `dotnet test Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj --no-build`

## Project paths
- Main: `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj`
- Tests: `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj`

## Relevant existing files to read before coding
Read all of these before starting any implementation:

1. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Model\HsmAsset.cs`
   — Contains HsmAsset, StateNode, TransitionNode, GlobalTransitionNode, RegionNode, EventDefinition, TransitionKind

2. `FDP\ExtDeps\FastHSM\src\Fhsm.Compiler\HsmBuilder.cs`
   — Contains HsmBuilder, StateBuilder, TransitionBuilder (the ACTUAL API to emit against)

3. `Hrot\Subsystems\AI\Hrot.BTree.Editor\Emit\BTreeFluentEmitter.cs`
   — Reference pattern for the emitter structure

4. `Hrot\Subsystems\AI\Hrot.BTree.Editor.Tests\BTreeFluentEmitterTests.cs`
   — Reference pattern for emitter tests

5. `Hrot\Subsystems\AI\Hrot.BTree.Editor\Host\BTreeNodeCatalog.cs`
   — Reference pattern for node catalog

6. `Hrot\Subsystems\AI\Hrot.BTree.Editor\Host\BTreeTypeSystem.cs`
   — Reference pattern for type system

7. `Hrot\Subsystems\AI\Hrot.BTree.Editor\Host\BTreeLinkValidator.cs`
   — Reference pattern for link validator

8. `Hrot\Subsystems\AI\Hrot.BTree.Editor\Host\BTreeCommandSink.cs`
   — Reference pattern for command sink

9. `Hrot\Editor\Hrot.Editor.AiShared\Emit\FluentCSharpEmitterBase.cs`
   — BuildHeader(), SortUsings() helpers

10. `Hrot\Editor\Hrot.Editor.AiShared\Layout\HsmEditorLayoutBuilder.cs`
    — The Layout builder API used by the emitter's Layout() method emission

11. `Hrot\Editor\Hrot.Editor.AiShared\Layout\HsmEditorLayout.cs`
    — StateLayoutEntry, TransitionLayoutEntry, RegionLayoutEntry

12. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\INodeCatalog.cs`
    — INodeCatalog interface

13. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\IGraphCommandSink.cs`
    — IGraphCommandSink, GraphCommandResult

14. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Commands\GraphCommand.cs`
    — All GraphCommand subtypes

15. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\ITypeSystem.cs`
    — ITypeSystem interface

16. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\ILinkValidator.cs`
    — ILinkValidator, LinkValidationResult, LinkValidity

17. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\IGraphModel.cs`
    — IGraphModel (needed by HsmLinkValidator)

18. `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\HsmAssetProjectionTests.cs`
    — Test patterns already in use

19. `FDP\ExtDeps\FastHSM\src\Fhsm.Compiler\Graph\StateNode.cs`
    — StateNode in compiler graph

## HS-S1-05: HsmFluentEmitter

### File to create
`Hrot\Subsystems\AI\Hrot.Hsm.Editor\Emit\HsmFluentEmitter.cs`

### Overview
Deterministic C# emitter. Produces a .cs file with three static methods:
- `CreateBuilder()` — fluent HSM definition
- `Compile()` — `[HsmDefinition]` thunk that calls `CreateBuilder().Build().Compile()`
- `Layout()` — `[HsmLayout]` thunk emitting layout entries

Implements `IFluentCSharpEmitter<HsmAsset>` from `Hrot.Editor.AiShared.Emit`.

### IMPORTANT: HsmBuilder API constraints
The ACTUAL `HsmBuilder` API in `FDP\ExtDeps\FastHSM\src\Fhsm.Compiler\HsmBuilder.cs` is:

```
HsmBuilder:
  .Event(name, id, payloadSize=0, isIndirect=false, isDeferred=false)
  .RegisterAction(fqn)
  .RegisterGuard(fqn)
  .State(name, stableId=default) => StateBuilder
  .GlobalTransition(eventName, targetStateName, visualId=default) -- takes event NAME, not ID!
  .Build()

StateBuilder:
  .OnEntry(fqn)
  .OnExit(fqn)
  .Activity(fqn)
  .Initial()
  .History()       -- sets IsHistory = true
  .Final()         -- sets IsFinal = true
  .Child(childName, Action<StateBuilder>, stableId=default) => returns parent StateBuilder
  .On(ushort eventId) => TransitionBuilder
  .On(string eventName) => TransitionBuilder

TransitionBuilder:
  .GoTo(string targetStateName, Guid visualId=default)
  .Guard(string guardName)
  .Action(string actionName)
  .Priority(byte priority)
```

IMPORTANT: There is NO `Parallel()`, `DeepHistory()`, `TimerAction()`, or `DeferEvent()` on `StateBuilder` yet. You MUST add the missing ones to `HsmBuilder.cs` before emitting code that uses them:

### Required additions to HsmBuilder.cs

Add these methods to `StateBuilder` (in `FDP\ExtDeps\FastHSM\src\Fhsm.Compiler\HsmBuilder.cs`):

```csharp
public StateBuilder Parallel()
{
    _state.IsParallel = true;
    return this;
}

public StateBuilder DeepHistory()
{
    _state.IsDeepHistory = true;
    return this;
}

public StateBuilder TimerAction(string actionName)
{
    _state.TimerAction = actionName;
    return this;
}
```

Do NOT add DeferEvent to the builder — DeferredEventIds is not in the compiler's StateNode
and the kernel handles event deferral differently. The emitter will skip DeferEvent calls.

### Emit shape (using ACTUAL API)

```csharp
// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten by the AI editor on next save.
// AssetId: <assetId.ToString("D")>

using System;
using System.Numerics;

using Fhsm.Compiler;
using Fhsm.Kernel;
using Hrot.AI.Behaviors.Machines.Layout;
using <namespace of game types>;

namespace <targetNamespace>;

public static class <ClassName>
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("<MachineName>");

        // Events (id-stable; order by EventId ascending)
        builder.Event("OnSight",     eventId: 1);
        builder.Event("Fire",        eventId: 3, payloadSize: 12);
        builder.Event("Reload",      eventId: 4, isDeferred: true);

        // Action and guard registrations (alphabetical by FQN)
        builder.RegisterAction("FQN.A");
        builder.RegisterAction("FQN.B");
        builder.RegisterGuard("FQN.Guard");

        // States (depth-first from root children)
        builder.State("Idle", stableId: new Guid("<guid>"))
               .Initial()
               .OnEntry("FQN.EnterIdle")
               .On(1).GoTo("Alert", visualId: new Guid("<guid>"));

        builder.State("Alert", stableId: new Guid("<guid>"))
               .Parallel()
               .Child("Walk", sb => sb
                   .Initial()
                   .Activity("FQN.WalkActivity"), stableId: new Guid("<guid>"))
               .Child("Sprint", sb => sb, stableId: new Guid("<guid>"))
               .On(2).GoTo("Search", visualId: new Guid("<guid>"));

        builder.State("Dead", stableId: new Guid("<guid>"))
               .Final();

        // Global transitions (by event name, sorted by EventId ascending)
        builder.GlobalTransition("Die", "Dead", visualId: new Guid("<guid>"));

        return builder;
    }

    [HsmDefinition("<MachineName>", AssetId = "<assetId.ToString("D")>")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout("<assetId.ToString("D")>")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .Canvas(panOffset: new Vector2(0f, 0f), zoomLevel: 1.0f)
        .State("<guid>", position: new Vector2(100f, 120f))
        .Transition("<guid>", waypoints: new Vector2[] { new Vector2(220f, 130f) })
        .Build();
}
```

### Deterministic rules
1. Events emitted in EventId ascending order.
2. RegisterAction + RegisterGuard: collect ALL FQNs used (OnEntry, OnExit, Activity, TimerAction,
   transition ActionFunction, global transition ActionFunction). Sort alphabetically.
   RegisterGuard: collect all GuardFunction FQNs, sort alphabetically.
   (If an FQN appears in both actions and guards, emit once in each category.)
3. States emitted depth-first from RootState.Children (DFS pre-order).
   For each state, the configuration order is:
   Initial() / DeepHistory() / History() / Final() / Parallel()
   then OnEntry / OnExit / Activity / TimerAction
   then outgoing transitions (in the order they appear in AllTransitions for this state)
   then Child() calls for children (DFS recursive, not flat)
4. Transitions: `.On(eventId).GoTo(targetName, visualId: guid)`,
   optionally chained with `.Guard(guardName)` then `.Action(actionName)` then `.Priority(priority)`.
   Priority is emitted only if != 0. Guard and Action only if non-null.
   IMPORTANT: The GoTo() chain must be SEPARATE from the state configuration chain.
   Write the state variable on its own line and call .On() on it separately:
   ```csharp
   var idle = builder.State("Idle", stableId: new Guid("..."))
                     .Initial()
                     .OnEntry("...");
   idle.On(1).GoTo("Alert", visualId: new Guid("..."));
   ```
   EXCEPT when there are also .Child() calls — use the returned parent builder:
   ```csharp
   var alert = builder.State("Alert", stableId: new Guid("..."))
                      .Parallel();
   alert.Child("Walk", sb => sb.Initial(), stableId: new Guid("..."));
   alert.On(2).GoTo("Search", visualId: new Guid("..."));
   ```
5. Global transitions emitted at end, sorted by EventId ascending.
   GlobalTransition takes eventName (look up by EventId in AllEvents).
6. Layout entries: States in StableId Guid.ToString("D") lexicographic ascending order.
   Transitions in VisualId Guid.ToString("D") lexicographic ascending order.
   Regions in StableId Guid.ToString("D") lexicographic ascending order.
   Only emit non-default values:
   - State: always emit position; emit size only if SizeOverride != null; emit comment only if non-null;
     emit collapsed=true only if IsCollapsed; emit color only if ColorOverride != null.
   - Transition: emit waypoints only if Waypoints.Count > 0; emit comment only if non-null.
   - Region: emit comment only if non-null; emit color only if ColorOverride != null.
7. Float literals use "f" suffix: `100f`, `1.5f`, etc.
8. Guid literals: new Guid("<guid.ToString("D")>") — always lowercase hyphenated form.
9. Target namespace: `asset.TargetNamespace` if non-empty, else "Hrot.AI.Behaviors.Machines".
10. Class name: sanitize `asset.Name` to a valid C# identifier (replace spaces/hyphens with underscores,
    strip non-identifier characters). Use the sanitized name.

### Usings to collect
- "System" (always)
- "System.Numerics" (always, for Vector2)
- "Fhsm.Compiler" (always, for HsmBuilder)
- "Fhsm.Kernel" (always, for HsmDefinitionBlob)
- "Fhsm.Kernel.Attributes" (always, for HsmDefinitionAttribute)
- Layout namespace: "Hrot.AI.Behaviors.Machines.Layout" (always)
- Scan all action/guard FQNs for their namespaces (strip the last dot-component).
  Add each unique namespace to the using set.

### HsmDefinition and HsmLayout attributes
The `[HsmDefinition]` attribute is `Fhsm.Kernel.Attributes.HsmDefinitionAttribute` (already created in BATCH-26).
For `[HsmLayout]`, check if `HsmLayoutAttribute` already exists in `Fhsm.Kernel.Attributes\`.
If it does not exist, CREATE it:

`FDP\ExtDeps\FastHSM\src\Fhsm.Kernel\Attributes\HsmLayoutAttribute.cs`:
```csharp
using System;

namespace Fhsm.Kernel.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HsmLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public HsmLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
```

### Test file to create
`Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\HsmFluentEmitterTests.cs`

Write the following tests (class name `HsmFluentEmitterTests`):

1. `Emit_is_deterministic` — build a simple HsmAsset with 2 states and 1 transition,
   call emitter.Emit() twice, assert both strings are equal.

2. `Emit_contains_header_marker` — assert the output contains
   "HROT_EDITOR_GENERATED".

3. `Emit_contains_asset_id_comment` — assert output contains `assetId.ToString("D")`.

4. `Emit_contains_event_declarations` — build asset with 2 events, assert both
   event names appear in output.

5. `Emit_contains_state_names` — build asset with states "Idle" and "Active",
   assert both appear in output.

6. `Emit_emits_layout_method` — assert output contains "HsmEditorLayoutBuilder".

7. `Emit_emits_compile_method` — assert output contains "[HsmDefinition".

8. `Emit_global_transition_appears` — add a GlobalTransitionNode,
   assert output contains "GlobalTransition".

9. `Emit_initial_state_flag` — assert that the initial state has ".Initial()" in output.

10. `Emit_final_state_flag` — assert that a final state has ".Final()" in output.

For building HsmAssets in tests, use HsmAssetProjector.Project(...) via
`internal static HsmAsset Project(...)` from `HsmAsset.cs`, OR construct
the HsmAsset directly by reading the builder-based construction pattern
already used in `HsmAssetProjectionTests.cs`.

IMPORTANT: `HsmAssetProjector` is internal, so tests can access it via
`InternalsVisibleTo` (already set in csproj). Use HsmBuilder + Project() or
build HsmAssets directly (whichever is simpler).

---

## HS-S1-06: HsmNodeCatalog

### Files to create
1. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmKinds.cs`
2. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmNodeCatalog.cs`

### HsmKinds.cs
```csharp
namespace Hrot.Hsm.Editor.Host;

internal static class HsmKinds
{
    public const string SimpleState   = "hsm.state.simple";
    public const string Composite     = "hsm.state.composite";
    public const string Parallel      = "hsm.state.parallel";
    public const string Final         = "hsm.state.final";
    public const string History       = "hsm.state.history";
    public const string DeepHistory   = "hsm.state.deepHistory";
}
```

### HsmNodeCatalog.cs
Implements `INodeCatalog`. No constructor parameters (dynamic entries from dispatcher
are deferred to Slice 2 — in Slice 1 only static kinds are registered).

Static kinds:
| Kind key | DisplayName | Category | Description |
|---|---|---|---|
| hsm.state.simple | Simple State | States | A regular leaf state. |
| hsm.state.composite | Composite State | States | A state with child states. |
| hsm.state.parallel | Parallel State | States | A parallel (orthogonal region) state. |
| hsm.state.final | Final State | States | Terminal state. Reaching it terminates the machine. |
| hsm.state.history | Shallow History | States | Restores last-active child on re-entry. |
| hsm.state.deepHistory | Deep History | States | Deeply restores nested active states on re-entry. |

- No pins (HSM uses invisible any-pins; don't register pin signatures).
  Use `Array.Empty<PinSignature>()` for both inputs and outputs on all entries.
- Category string: "States" for all 6.
- Keywords: kind-specific; e.g., simple: new[] { "state", "simple", "leaf" };
  composite: new[] { "state", "composite", "nested" };
  parallel: new[] { "state", "parallel", "orthogonal", "region" };
  final: new[] { "state", "final", "terminal" };
  history: new[] { "state", "history", "shallow" };
  deepHistory: new[] { "state", "deep", "history" };
- IconKey: "hsm/state_simple", "hsm/state_composite", etc.
- IsPure=false, IsLatent=false, IsDeprecated=false for all.

`Categories` property: return a single NodeCategoryDescriptor("States", "States", "hsm/states").

`Query(NodeSearchQuery q)`: filter `_all` by text search across DisplayName and Keywords
(case-insensitive contains). Ignore category/type filters for now.

`QueryForPinContext(PinContextQuery q)`: return all (`_all`) — HSM doesn't use pin context.

---

## HS-S1-07: HsmTypeSystem + HsmLinkValidator

### HsmTypeSystem.cs
File: `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmTypeSystem.cs`

Implements `ITypeSystem`. Pure stub per spec §5.2:
- `TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)`: `info = default; return false;`
- `GetPinColor(TypeKey key)`: `return Vector4.One;`
- `GetPinShape(TypeKey key, ContainerKind c)`: `return PinShape.Circle;`
- `GetDefaultEditor(TypeKey key)`: `return null;`
- `AreCompatible(TypeKey from, TypeKey to)`: `return true;`
- `IsImplicitCast(TypeKey from, TypeKey to)`: `return false;`

### HsmLinkValidator.cs
File: `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmLinkValidator.cs`

Before writing this, read `ILinkValidator.cs` and `IGraphModel.cs` from NodeEditor.Core.
Look them up if needed in `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\`.

Implements `ILinkValidator`. Constructor: `HsmLinkValidator(HsmAsset asset)`.

Logic:
```
Validate(PinId from, PinId to):
  sourceId = StateNode.DeriveOutputPinId^-1(from.PinGuid)
           = find StateNode where StateNode.HiddenOutputPinId == from.PinGuid
  targetId = find StateNode where StateNode.HiddenInputPinId == to.PinGuid

  if sourceId not found or targetId not found:
    return LinkValidationResult(LinkValidity.Invalid, "Endpoint not a state", false, null)

  sourceState = asset.FindStateByStableId(sourceId)
  targetState = asset.FindStateByStableId(targetId)

  if sourceState.IsFinal:
    return LinkValidationResult(LinkValidity.Invalid, "Final state cannot have outgoing transitions", false, null)

  if targetState.IsHistory:
    return LinkValidationResult(LinkValidity.Invalid, "History pseudo-state is not a normal transition target", false, null)

  return LinkValidationResult(LinkValidity.Valid, null, false, null)
```

To reverse the pin derivation: iterate `asset.AllStates`, call `DeriveOutputPinId(s.StableId)`,
compare to `from.PinGuid` — the matching state is the source. Similarly for input.

Note: `PinId` is a struct/record in NodeEditor.Primitives. Read the type to see what field
contains the Guid. It may be `PinId.Id` or `PinId.Value` or the `Guid` field — read the file
`FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Primitives\PinId.cs` (or similar) to find out.

### HsmLinkValidatorTests.cs
File: `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\HsmLinkValidatorTests.cs`

Tests:
1. `Normal_transition_is_valid` — source=simple state, target=simple state → Valid
2. `Final_state_source_is_invalid` — source=final state → Invalid
3. `History_target_is_invalid` — target=history state → Invalid
4. `Self_transition_is_valid` — source == target (same state) → Valid
5. `Unknown_pin_is_invalid` — pass Guid.NewGuid() as pin → Invalid

Build the HsmAsset used in tests by directly constructing HsmAsset and StateNodes.
Use `StateNode.DeriveOutputPinId()` and `StateNode.DeriveInputPinId()` to get the PinIds.
Construct `PinId` from the derived Guid — look at PinId's constructors/factory methods.

---

## HS-S1-08: HsmCommandSink (stub)

### File to create
`Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmCommandSink.cs`

This is a STRUCTURAL STUB — implement the switch-case dispatch but make each Apply*
helper method a no-op that marks the asset dirty. The full implementation comes in
later batches when IContainerNodeModel and IGraphModel wiring is in place.

Implements `IGraphCommandSink`. Internal sealed class.
Constructor: `internal HsmCommandSink(HsmAsset asset)`.

### Apply switch (match the spec §5.4 structure):
```csharp
public GraphCommandResult Apply(GraphCommand command)
{
    switch (command)
    {
        case GraphCommand.MoveNodes mn:    ApplyStateMoves(mn.Moves);    break;
        case GraphCommand.AddNode an:      ApplyAddState(an);             break;
        case GraphCommand.RemoveNodes rn:  ApplyRemoveStates(rn.Nodes);   break;
        case GraphCommand.AddLink al:      ApplyAddTransition(al.From, al.To); break;
        case GraphCommand.RemoveLinks rl:  ApplyRemoveTransitions(rl.Links); break;
        case GraphCommand.SetNodeProperty sp: ApplySetStateProperty(sp);  break;
        case GraphCommand.ChangeParent cp: ApplyStateReparent(cp);        break;
        case GraphCommand.SetContainerCollapsed sc: ApplySetCollapsed(sc); break;
        case GraphCommand.AddRegion ar:    ApplyAddRegion(ar);             break;
        case GraphCommand.RemoveRegion rr: ApplyRemoveRegion(rr);          break;
        case GraphCommand.ReorderRegions rr2: ApplyReorderRegions(rr2);   break;
        case GraphCommand.AddAttachment aa: ApplyAddFlagBadge(aa);         break;
        case GraphCommand.RemoveAttachments ra: ApplyRemoveFlagBadges(ra.AttachmentIds); break;
        case GraphCommand.Batch b:
            foreach (var sub in b.Commands) Apply(sub);
            break;
        default:
            return new GraphCommandResult(false, $"Unsupported: {command.GetType().Name}");
    }
    _asset.MarkDirty();
    return new GraphCommandResult(true, null);
}
```

Each private method is a stub:
```csharp
private void ApplyStateMoves(IReadOnlyList<NodeMove> moves) { /* TODO */ }
private void ApplyAddState(GraphCommand.AddNode cmd) { /* TODO */ }
private void ApplyRemoveStates(IReadOnlyList<NodeId> nodeIds) { /* TODO */ }
private void ApplyAddTransition(PinId from, PinId to) { /* TODO */ }
private void ApplyRemoveTransitions(IReadOnlyList<LinkId> linkIds) { /* TODO */ }
private void ApplySetStateProperty(GraphCommand.SetNodeProperty cmd) { /* TODO */ }
private void ApplyStateReparent(GraphCommand.ChangeParent cmd) { /* TODO */ }
private void ApplySetCollapsed(GraphCommand.SetContainerCollapsed cmd) { /* TODO */ }
private void ApplyAddRegion(GraphCommand.AddRegion cmd) { /* TODO */ }
private void ApplyRemoveRegion(GraphCommand.RemoveRegion cmd) { /* TODO */ }
private void ApplyReorderRegions(GraphCommand.ReorderRegions cmd) { /* TODO */ }
private void ApplyAddFlagBadge(GraphCommand.AddAttachment cmd) { /* TODO */ }
private void ApplyRemoveFlagBadges(IReadOnlyList<AttachmentId> ids) { /* TODO */ }
```

IMPORTANT: Check GraphCommand types carefully. `RemoveNodes` has field `Nodes` (not `NodeIds`).
`RemoveLinks` has field `Links` (not `LinkIds`). `RemoveAttachments` has field `AttachmentIds`.
Read `GraphCommand.cs` to confirm exact field names before coding.

Also check if `AttachmentId` is in `NodeEditor.Primitives` — if so, you need the using.
Check `NodeEditor.Core.Commands` for `NodeMove` type.

No test file needed for HsmCommandSink stub — it's tested indirectly later.

---

## Final verification steps

After writing all files:

1. Add `HsmFluentEmitter` using:
   ```
   using Hrot.Editor.AiShared.Emit;
   using Hrot.Hsm.Editor.Model;
   ```

2. Build the main project:
   ```
   dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj
   ```

3. Build the tests project:
   ```
   dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj
   ```

4. Run the tests (all must pass — existing 10 + new ones):
   ```
   dotnet test Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj
   ```

5. Report the PASS count in your batch report.

## Batch report format
When done, report:
- Files created/modified
- Test counts (existing + new)
- Any issues encountered and how resolved

## Key spec reference
`d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\HSM_Editor_NodeEditor_Host_Design.md`
- §4 — HsmFluentEmitter
- §5.1 — HsmNodeCatalog
- §5.2 — HsmTypeSystem
- §5.3 — HsmLinkValidator
- §5.4 — HsmCommandSink
