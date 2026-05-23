# BATCH-08 Report

## Tasks Completed
- **TASK-NEC-01** - IContainerNodeModel + ParentContainerId invariant change
- **TASK-NEC-02** - GraphView transform helpers

## Files Created
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IContainerNodeModel.cs` (NEW)
  - `IContainerNodeModel : INodeModel` interface with `IsContainer`, `ChildNodeIds`, `Regions`,
    `GetRegionIndexForChild`, `Padding`, `MinimumInteriorSize`
  - `RegionDescriptor` sealed record (Index, Name, Priority, CustomColor)
  - `ContainerPadding` sealed record (Top, Right, Bottom, Left) with `Default` static (8,12,12,12)
  - `INodeModelExtensions` static class: `IsContainerNode()`, `AsContainer()`
- `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Interfaces/ContainerNodeModelTests.cs` (NEW)
  - 9 tests covering extension methods, default ParentContainerId, ContainerPadding, RegionDescriptor
- `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/View/ContainerTransformTests.cs` (NEW)
  - 6 tests covering NodeCanvasPosition (root, child with offset), GetParentContainer,
    NodeLocalPosition, unknown node behavior

## Files Modified
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/INodeModel.cs`
  - Added `NodeId? ParentContainerId => null;` default interface member (backwards compatible)
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/GraphView.cs`
  - Added `using NodeEditor.Primitives;` and `using System.Numerics;`
  - Added `NodeCanvasPosition(NodeId)` - walks ancestor chain to compute canvas-absolute position
  - Added `NodeLocalPosition(NodeId)` - returns INodeModel.Position directly
  - Added `GetParentContainer(NodeId)` - returns ParentContainerId or null
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeNodeModel.cs`
  - Added `NodeId? ParentContainerId { get; set; }` mutable property

## Test Results
- Before: 95 tests (85 Core + 10 UI)
- After: 110 tests (100 Core + 10 UI)
- New tests: 15 (9 ContainerNodeModelTests + 6 ContainerTransformTests)
- All 110 tests passing, 0 failures, 0 errors

## Commit
`65d87b7e` - feat: IContainerNodeModel, ParentContainerId, GraphView transform helpers (BATCH-08)

## Notes
- `IContainerNodeModel` does NOT redeclare `IsCollapsed` (already inherited from `INodeModel`).
  The container semantics for collapse are expressed through the same `IsCollapsed` property.
- `NodeCanvasPosition` uses `Host.Theme.NodeHeaderHeight` (not a hard-coded constant) for the
  vertical offset to the container interior origin.
- `AsContainer()` correctly returns null for a node with `IsContainer == false` even if it
  implements `IContainerNodeModel`.
