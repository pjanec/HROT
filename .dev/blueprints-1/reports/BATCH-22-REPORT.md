# BATCH-22 Report

**Tasks:** TASK-ED-002, TASK-ED-003
**Status:** COMPLETED

## Files Created

### Production code (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`)

- `GraphEditor/IGraphCommand.cs` -- command interface (Description, Execute, Undo)
- `GraphEditor/CommandHistory.cs` -- undo/redo ring buffer, capacity 64, evicts oldest
- `GraphEditor/SelectionState.cs` -- SelectedNodes/SelectedLinks HashSets, ClearAll, SelectNode
- `GraphEditor/GraphCommands.cs` -- AddNodeCommand + DeleteNodeCommand (adapted to `Graph`/`Node` from `Hrot.Blueprints.Core.Assets`)
- `AssetBrowserWindow.cs` -- RefreshCatalog, CatalogEntries, OnActivated, stub DrawUI
- `GraphEditorWindow.cs` -- OpenAsset, Selection, Commands, stub DrawUI
- `Inspector/DrawContext.cs` -- record with IsReadOnly, IdPrefix, TypeRegistry
- `Inspector/IStructEditDrawer.cs` -- generic drawer interface
- `Inspector/DrawerRegistry.cs` -- type-keyed Register<T>/TryGet<T>
- `Inspector/PrimitiveDrawers.cs` -- FloatDrawer, IntDrawer, BoolDrawer, StringDrawer stubs
- `InspectorWindow.cs` -- skeleton with DrawerRegistry dependency

### Tests (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/`)

- `CommandHistoryTests.cs` -- SC1-SC5 (undo/redo, CanUndo/CanRedo, discard redo, clear)
- `DrawerRegistryTests.cs` -- SC1-SC3 (register, missing, overwrite)
- `AssetBrowserWindowTests.cs` -- SC1-SC2 (empty catalog, OnActivated refreshes)

## Notes

The batch instructions referenced `BlueprintGraph`/`BlueprintNode` from `Fdp.Toolkit.Blueprints`, but the actual data model uses `Graph`/`Node` from `Hrot.Blueprints.Core.Assets`. `GraphCommands.cs` was adapted accordingly (`Graph.Nodes` is `List<Node>`, `Node.Id` is `Guid`). `BranchNode` (a concrete subclass of `Node`) is used in tests.

## Test Results

- Before: 439 total / 434 pass / 0 fail / 5 skip
- After:  449 total / 444 pass / 0 fail / 5 skip

## Commit

`e4cb6caa` feat(blueprints): BATCH-22 ED-002 asset browser graph editor + ED-003 inspector drawers
