# BATCH-28 -- HS-S1-09 through HS-S1-12

## Tasks
- **HS-S1-09** StateNode implementing INodeModel + IContainerNodeModel
- **HS-S1-10** Parallel composites with regions (IContainerNodeModel.Regions, GetRegionIndexForChild)
- **HS-S1-11** Composite collapse (IsCollapsed on IContainerNodeModel, already in StateNode)
- **HS-S1-12** HsmTransitionLink (ILinkModel) + HsmGraphModel (IGraphModel)

## Repository root
`d:\Work\IOS-IG-SimHost-FDP-2\`

## AGENTS.md rules (MUST follow)
1. Preserve all existing comments exactly.
2. Do NOT use Unicode in comments or string literals (ASCII only).
3. Minimize textual diffs.
4. Build with 0 errors and 0 warnings. Verify by running:
   `dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj`
   `dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj`
   `dotnet test  Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj --no-build`

## Essential files to read before coding

1. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Model\HsmAsset.cs` -- StateNode, TransitionNode, HsmAsset
2. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmKinds.cs` -- kind key constants
3. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\INodeModel.cs` -- INodeModel
4. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\IContainerNodeModel.cs` -- IContainerNodeModel, RegionDescriptor, ContainerPadding
5. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\IPinModel.cs` -- IPinModel
6. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\ILinkModel.cs` -- ILinkModel, LinkStyle
7. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\Interfaces\IGraphModel.cs` -- IGraphModel, GraphKindDescriptor, GraphChangeNotification
8. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Primitives\Enums.cs` -- NodeCategory, NodeState, PinDirection, PinKind, PinShape, ContainerKind
9. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Primitives\NodeId.cs` -- NodeId(Guid)
10. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Primitives\PinId.cs` -- PinId(Guid)
11. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Primitives\LinkId.cs` -- LinkId(Guid)
12. `FDP\ExtDeps\NodeEdit\src\NodeEditor.Primitives\GraphId.cs` -- GraphId
13. `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\HsmAssetProjectionTests.cs` -- test patterns

---

## HS-S1-09/10/11: StateNode as IContainerNodeModel

### Overview
`StateNode` must implement `IContainerNodeModel` (which extends `INodeModel`).
This covers HS-S1-09 (INodeModel + basic container), HS-S1-10 (parallel/region support),
and HS-S1-11 (collapse is already on StateNode; just expose via interface).

### Changes to HsmAsset.cs

**Step 1**: Add `using NodeEditor.Core.Interfaces;` and `using NodeEditor.Primitives;` to HsmAsset.cs.

**Step 2**: Add `RegionIndex` property to `StateNode`:
```
// Zero-based index of the orthogonal region this state belongs to within its parent parallel composite.
// 0 for states that are not children of a parallel state.
public int RegionIndex;
```
This must be added to the StateNode class.

**Step 3**: Make `StateNode` implement `IContainerNodeModel`:
```csharp
public sealed class StateNode : IContainerNodeModel
```

**Step 4**: Add the following interface implementations to `StateNode`.
Add them as a region or section after the existing fields (after the `DeriveInputPinId` method).
All members must be explicitly implemented (or implicit -- your choice for readability).

```csharp
// ---- INodeModel ----

public NodeId Id => new NodeId(StableId);

// Resolve the catalog kind key from state flags.
public NodeKindKey Kind
{
    get
    {
        if (IsFinal)      return new NodeKindKey(HsmKinds.Final);
        if (IsDeepHistory) return new NodeKindKey(HsmKinds.DeepHistory);
        if (IsHistory)    return new NodeKindKey(HsmKinds.History);
        if (IsParallel)   return new NodeKindKey(HsmKinds.Parallel);
        if (Children.Count > 0) return new NodeKindKey(HsmKinds.Composite);
        return new NodeKindKey(HsmKinds.SimpleState);
    }
}

public string Title => Name;
public string? Subtitle => null;
public NodeCategory Category => NodeCategory.Custom;

// Position and SizeOverride are already fields; satisfy the interface via properties:
// NOTE: Position is currently a field (Vector2 Position), not a property.
// You need to change "public Vector2 Position;" to "public Vector2 Position { get; set; }"
// and "public Vector2? Size;" to "public Vector2? SizeOverride { get; set; }"
// (renaming Size to SizeOverride to match the INodeModel property name).

public NodeState State => IsBreakpoint ? NodeState.Warning : NodeState.Normal;
public string? StatusTooltip => null;
// IsCollapsed is already a field; change to property: "public bool IsCollapsed { get; set; }"
public bool ShowAdvancedPins => false;

// Two hidden pins: output (source of transitions FROM this state) and input (target TO this state).
// Lazy-initialized to avoid allocation on non-pinned code paths.
private IReadOnlyList<IPinModel>? _pins;
public IReadOnlyList<IPinModel> Pins => _pins ??= BuildPins();

private IReadOnlyList<IPinModel> BuildPins()
{
    return new IPinModel[]
    {
        new HsmPinModel(new PinId(HiddenOutputPinId), new NodeId(StableId), PinDirection.Output),
        new HsmPinModel(new PinId(HiddenInputPinId),  new NodeId(StableId), PinDirection.Input),
    };
}

// ParentContainerId is null for top-level states (Parent is RootState which has no parent).
public NodeId? ParentContainerId =>
    Parent?.Parent != null ? new NodeId(Parent!.StableId) : (NodeId?)null;

// ---- IContainerNodeModel ----

public bool IsContainer => Children.Count > 0 || IsParallel;

public IReadOnlyList<NodeId> ChildNodeIds =>
    Children.Select(c => new NodeId(c.StableId)).ToList();

// For parallel composites, expose region descriptors.
// For non-parallel composites, return empty.
public IReadOnlyList<RegionDescriptor> Regions
{
    get
    {
        if (!IsParallel || Regions_backing.Count == 0)
            return Array.Empty<RegionDescriptor>();
        return Regions_backing
            .Select(r => new RegionDescriptor(r.RegionIndex, r.Name, r.Priority, null))
            .ToList();
    }
}
```

Wait -- `Regions` conflicts with the existing `public List<RegionNode> Regions { get; } = new();` field.
IMPORTANT: Rename the existing `Regions` property to `RegionNodes` to avoid the conflict.

Here is the corrected approach:

**Step 3**: In HsmAsset.cs, rename `StateNode.Regions` (the `List<RegionNode>`) to `RegionNodes`.
All existing uses of `.Regions` on a `StateNode` in this file and related files must be updated.

After renaming, add the IContainerNodeModel.Regions implementation (which returns RegionDescriptors).

**GetRegionIndexForChild**:
```csharp
public int GetRegionIndexForChild(NodeId childId)
{
    // Find the child StateNode with matching StableId
    var child = Children.FirstOrDefault(c => c.StableId == childId.Value);
    if (child == null) return -1;
    return child.RegionIndex;
}
```

**Padding + MinimumInteriorSize**:
```csharp
public ContainerPadding Padding => ContainerPadding.Default;

public Vector2 MinimumInteriorSize =>
    IsParallel ? new Vector2(280f, 120f) : new Vector2(200f, 80f);
```

### IMPORTANT: Field-to-property conversions needed
Change these in StateNode:
- `public Vector2 Position;` -> `public Vector2 Position { get; set; }`
- `public Vector2? Size;` -> `public Vector2? SizeOverride { get; set; }`
- `public bool IsCollapsed;` -> `public bool IsCollapsed { get; set; }`

(IsBreakpoint can stay as a field since it's not part of the interface)

Also check all code in HsmAssetProjector.cs, HsmAutoLayout.cs, HsmFluentEmitter.cs, HsmAssetProjectionTests.cs
that references `.Size` -- update to `.SizeOverride`. References to `.Position` and `.IsCollapsed`
should continue to work since they become auto-properties.

### Changes to HsmAssetProjector.cs
Where projector sets layout fields on StateNode, change `.Size` to `.SizeOverride`:
```
node.Size = ...   -->   node.SizeOverride = ...
```

---

## New file: HsmPinModel.cs

Create: `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Model\HsmPinModel.cs`

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// A hidden any-pin for HSM state nodes.
// States have one output pin (source of outgoing transitions)
// and one input pin (target of incoming transitions).
// These pins are invisible in the canvas; they exist only to satisfy
// NodeEditor's pin-based link primitive.
internal sealed class HsmPinModel : IPinModel
{
    public PinId Id { get; }
    public NodeId OwnerNodeId { get; }
    public string Label { get; }
    public PinDirection Direction { get; }
    public PinKind Kind => PinKind.Data;
    public TypeKey? Type => null;
    public PinShape Shape => PinShape.Circle;
    public bool IsAdvanced => true;
    public bool IsOptional => true;
    public string? Tooltip => null;
    public IPinDefaultValue? Default => null;

    internal HsmPinModel(PinId id, NodeId ownerNodeId, PinDirection direction)
    {
        Id = id;
        OwnerNodeId = ownerNodeId;
        Direction = direction;
        Label = direction == PinDirection.Output ? "out" : "in";
    }
}
```

---

## New file: HsmTransitionLink.cs

Create: `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Model\HsmTransitionLink.cs`

```csharp
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// Adapts a TransitionNode to NodeEditor's ILinkModel interface.
// Links are pin-based: from the source state's hidden output pin
// to the target state's hidden input pin.
// VisualId is used as the LinkId for stable identity.
internal sealed class HsmTransitionLink : ILinkModel
{
    private readonly TransitionNode _transition;

    internal HsmTransitionLink(TransitionNode transition)
    {
        _transition = transition;
    }

    public LinkId Id     => new LinkId(_transition.VisualId);
    public PinId FromPin => new PinId(_transition.Source.HiddenOutputPinId);
    public PinId ToPin   => new PinId(_transition.Target.HiddenInputPinId);

    public LinkStyle Style => _transition.Kind == TransitionKind.Internal
        ? LinkStyle.Dashed
        : LinkStyle.Solid;

    public IReadOnlyList<Vector2> Waypoints => _transition.Waypoints;
}
```

---

## New file: HsmGraphModel.cs

Create: `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Model\HsmGraphModel.cs`

This implements `IGraphModel`, exposing the `HsmAsset` to NodeEditor's canvas.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// NodeEditor IGraphModel adapter for HsmAsset.
// Exposes all non-root StateNodes as INodeModel instances
// and all transitions as ILinkModel instances.
// This is the read-only view; mutations go through HsmCommandSink.
public sealed class HsmGraphModel : IGraphModel
{
    private readonly HsmAsset _asset;

    // Cache for link adapters keyed by VisualId.
    private readonly Dictionary<LinkId, HsmTransitionLink> _linkCache = new();

    public HsmGraphModel(HsmAsset asset)
    {
        _asset = asset;
        // Rebuild caches when asset changes.
        _asset.Changed += OnAssetChanged;
        BuildCaches();
    }

    private void OnAssetChanged()
    {
        BuildCaches();
        Changed?.Invoke(new GraphChangeNotification(
            GraphChangeKind.NodesModified,
            null, null, null, "HsmAsset changed"));
    }

    private void BuildCaches()
    {
        _linkCache.Clear();
        foreach (var t in _asset.AllTransitions)
            _linkCache[new LinkId(t.VisualId)] = new HsmTransitionLink(t);
    }

    // ---- IGraphModel ----

    public GraphId Id          => new GraphId(_asset.AssetId);
    public string  DisplayName => _asset.Name;
    public GraphKindDescriptor Kind { get; } =
        new("HsmGraph", "State Machine", AllowsLatent: false, RequiresEntryNode: false);

    // Nodes: all non-root states (RootState is synthetic and never shown).
    public IReadOnlyCollection<INodeModel> Nodes => _asset.AllStates;

    public IReadOnlyCollection<ILinkModel> Links => _linkCache.Values;

    public IReadOnlyCollection<ICommentModel> Comments =>
        Array.Empty<ICommentModel>();

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel? FindNode(NodeId id)
    {
        var state = _asset.FindStateByStableId(id.Value);
        return state;
    }

    public IPinModel? FindPin(PinId id)
    {
        // Search all states' pins (two per state: output then input).
        foreach (var state in _asset.AllStates)
        {
            foreach (var pin in state.Pins)
                if (pin.Id == id) return pin;
        }
        return null;
    }

    public ILinkModel? FindLink(LinkId id) =>
        _linkCache.TryGetValue(id, out var link) ? link : null;
}
```

IMPORTANT: Check that `GraphId` exists in NodeEditor.Primitives. Read `GraphId.cs`:
`FDP\ExtDeps\NodeEdit\src\NodeEditor.Primitives\GraphId.cs`
If `GraphId` takes a different constructor argument, adjust accordingly.

Also check `ICommentModel` -- it may be in `NodeEditor.Core.Interfaces`. Find it with grep.

---

## Test file: HsmGraphModelTests.cs

Create: `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\HsmGraphModelTests.cs`

Write a class `HsmGraphModelTests` with these tests:

**Container node tests (covers HS-S1-09, 10, 11):**

1. `Simple_state_IsContainer_false`
   Build a state with no children. `IsContainer` must be false.

2. `State_with_children_IsContainer_true`
   Add child states. `IsContainer` must be true.

3. `Parallel_state_IsContainer_true`
   Set IsParallel=true. `IsContainer` must be true even with no children.

4. `State_Id_wraps_StableId`
   `state.Id.Value` must equal `state.StableId`.

5. `State_Kind_simple_when_no_children`
   Kind.Value must equal HsmKinds.SimpleState.

6. `State_Kind_composite_when_has_children`
   Add a child; Kind.Value must equal HsmKinds.Composite.

7. `State_Kind_parallel`
   IsParallel=true; Kind.Value must equal HsmKinds.Parallel.

8. `State_Kind_final`
   IsFinal=true; Kind.Value must equal HsmKinds.Final.

9. `State_Kind_history`
   IsHistory=true; Kind.Value must equal HsmKinds.History.

10. `State_Kind_deepHistory`
    IsDeepHistory=true; Kind.Value must equal HsmKinds.DeepHistory.

11. `State_ChildNodeIds_match_children`
    After adding children, ChildNodeIds must contain all children's StableIds wrapped in NodeId.

12. `Top_level_state_ParentContainerId_is_null`
    Construct root -> topLevel (Parent = root, root.Parent = null).
    topLevel.ParentContainerId must be null.

13. `Nested_state_ParentContainerId_set`
    Construct root -> composite -> child.
    child.ParentContainerId must equal new NodeId(composite.StableId).

14. `State_Pins_count_is_two`
    state.Pins.Count must be 2.

15. `State_output_pin_Id_matches_HiddenOutputPinId`
    Pins[0].Id must equal new PinId(state.HiddenOutputPinId).

16. `State_input_pin_Id_matches_HiddenInputPinId`
    Pins[1].Id must equal new PinId(state.HiddenInputPinId).

**HsmTransitionLink tests (covers HS-S1-12):**

17. `TransitionLink_Id_equals_VisualId`
    HsmTransitionLink.Id.Value must equal transition.VisualId.

18. `TransitionLink_FromPin_equals_source_output`
    FromPin.Value must equal Source.HiddenOutputPinId.

19. `TransitionLink_ToPin_equals_target_input`
    ToPin.Value must equal Target.HiddenInputPinId.

20. `TransitionLink_external_is_Solid`
    Kind=External; Style must be Solid.

21. `TransitionLink_internal_is_Dashed`
    Kind=Internal; Style must be Dashed.

**HsmGraphModel tests:**

22. `GraphModel_Nodes_contains_all_states`
    Build HsmAsset via HsmAssetProjector; Nodes.Count must equal AllStates.Count.

23. `GraphModel_Links_contains_all_transitions`
    Links.Count must equal AllTransitions.Count.

24. `GraphModel_FindNode_returns_state`
    FindNode(new NodeId(state.StableId)) must return non-null.

25. `GraphModel_FindPin_finds_output_pin`
    FindPin(new PinId(state.HiddenOutputPinId)) must return non-null.

26. `GraphModel_FindLink_finds_transition`
    FindLink(new LinkId(transition.VisualId)) must return non-null.

For HsmGraphModel tests, build a real HsmAsset using the HsmBuilder + projector pattern
from HsmAssetProjectionTests.cs.

NOTE: HsmTransitionLink is `internal` -- tests can access it via InternalsVisibleTo
which is already set in the .csproj. If HsmGraphModel is public, test can use it directly.
Consider making HsmGraphModel `public` (it needs to be accessible from HsmEditorHostServices later).

---

## Usings needed

In `HsmAsset.cs`, add at the top:
```csharp
using System.Linq;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
```

In test file, add:
```csharp
using FluentAssertions;
using Fhsm.Compiler;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;
```

---

## Dependency check for HsmGraphModel

`HsmGraphModel` must reference `NodeEditor.Core`. Check that `Hrot.Hsm.Editor.csproj`
already has a `<ProjectReference>` to `NodeEditor.Core`. If not, look at how other
Hrot.*.Editor projects reference it (e.g., Hrot.BTree.Editor.csproj).

Read the .csproj to verify:
`Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj`

---

## References that may break after renaming StateNode.Regions to RegionNodes

Search the following files for `.Regions` on a StateNode and update to `.RegionNodes`:
1. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Model\HsmAssetProjector.cs`
2. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Layout\HsmAutoLayout.cs`
3. `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Emit\HsmFluentEmitter.cs`
4. `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\HsmAssetProjectionTests.cs`

Use grep to find all occurrences. The rename is from `StateNode.Regions` to `StateNode.RegionNodes`.
(The `HsmAsset.AllRegions` list contains `RegionNode` objects -- that is unchanged.)

---

## Final verification

1. `dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj`
2. `dotnet build Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj`
3. `dotnet test  Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Hrot.Hsm.Editor.Tests.csproj`
   All 51 (25 existing + 26 new) tests must pass.

Report:
- Files created + files modified
- Test pass count
- Any issues encountered and resolution

## Key spec reference
`d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\HSM_Editor_NodeEditor_Host_Design.md`
- Section 6 (containers)
- Section 7.1 and 7.2 (transition link bridge)
