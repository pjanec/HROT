# Hrot.Editor.AiShared

**Project path:** `Hrot/Editor/Hrot.Editor.AiShared/`
**Project file:** `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj`
**Target framework:** net8.0
**Date documented:** 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary architectural
reference for the project.

---

## Executive Overview

`Hrot.Editor.AiShared` is the shared infrastructure library for HROT's AI behaviour authoring
tools. It is consumed by every concrete AI editor -- currently `Hrot.BTree.Editor` (Behaviour
Tree) and `Hrot.Hsm.Editor` (Hierarchical State Machine) -- and provides the full set of
cross-cutting editor services so that neither subsystem needs to reimplement them.

### What is shared

| Domain | Description |
|--------|-------------|
| **Identity** | `IEditableAsset`, `AssetKind`, deterministic ID hashing (FNV-1a-32). |
| **Asset Catalog** | Aggregated registry of all open/known assets across all subsystems. |
| **Selection** | Active-asset tracking, per-asset sub-selection, IG entity selection bridge. |
| **References** | Cross-asset reference tracking for actions, conditions, guards, events, and blackboard fields. |
| **Refactoring** | Preview-then-apply rename/delete workflow with atomic multi-file write. |
| **Code Emission** | Fluent C# code generator base with deterministic `using`-sort, atomic write, and generation marker. |
| **Layout** | Canvas pan/zoom, per-node/state/transition/region visual positions for BTree and HSM canvases. Attribute-driven layout discovery. |
| **Hot-Reload** | Hash-delta classifier (Cosmetic / Soft / Hard) independent of subsystem topology. |
| **Validation** | Pluggable `IAssetValidator` interface with `AssetDiagnostic` aggregation. |
| **Debug / Trace** | Breakpoint model, session lifecycle (`IAiDebugSession`), reference-counted trace observer coordinator, live-entity tracking. |
| **Blackboard Authoring** | `IBlackboardManagedAsset` contract; per-asset variable list, alias bindings, sync bindings, and a dedicated `BlackboardAuthoringWindow` with bin-packing budget display. |
| **ImGui Windows** | Eight shared ImGui windows: Asset Browser, Inspector, Blackboard Variables, Runtime Inspector, Trace Timeline, Find Results, Diagnostics, plus their shared registrar. |
| **Visual Asset Comparison** | LLM-assisted asset comparison: sanitization pipeline, export builder, response parser, canvas annotation renderer, summary panel, sidebar, and all UI modals. See [Hrot.Editor.AiShared.Comparison.md](Hrot.Editor.AiShared.Comparison.md). |
| **DI** | Single `AddSharedAiEditor()` extension method that registers all of the above. |

The library has **no dependency on any AI kernel** (no `Hrot.BTree`, `Hrot.Hsm`, or `Hrot.Blueprint`
runtime assemblies). Its two project references are `Fdp.Core` (domain primitives, `Entity`) and
`Fdp.Presentation` (ImGui window manager base classes).

---

## Architecture

### Layering

```
+---------------------------------------------------------------+
|           Subsystem editors                                   |
|   Hrot.BTree.Editor        Hrot.Hsm.Editor                   |
|   (BTree canvas, emitter,  (HSM canvas, emitter,             |
|    debug session impl)      debug session impl)               |
+------------------------------+--------------------------------+
                               |  consumes
+------------------------------v--------------------------------+
|                  Hrot.Editor.AiShared                        |
|                                                               |
|  Catalog   Selection   References   Refactor   Emit          |
|  Layout    HotReload   Validation   Debug      Windows       |
|                                                               |
+----------------+-----------------------------+----------------+
                 |                             |
+----------------v------+         +-----------v-----------+
|      Fdp.Core         |         |    Fdp.Presentation   |
|  (Entity, primitives) |         |  (ManagedWindow, DI)  |
+-----------------------+         +-----------------------+
```

### Internal sub-system diagram

```
+---------------------+     Changed     +---------------------+
| IAssetCatalogContr. |--------------->| AssetCatalog        |
| (per subsystem)     |                | (IAssetCatalog)     |
+---------------------+                +----------+----------+
                                                   |
                          +------------------------+
                          |
            +-------------v-----------+
            | EditorSelectionStore    |
            |  ActiveAsset            |
            |  ActiveSubSelection     |
            |  SelectedEntity         |
            +-------------+-----------+
                          |
           +--------------+---------------+
           |                              |
+----------v---------+      +-------------v-----------+
| AssetBrowserWindow |      | InspectorWindow         |
| (catalog list,     |      | (active-asset context   |
|  rename / delete   |      |  menu, rename flow)     |
|  context menus)    |      +-------------------------+
+--------------------+

+---------------------+    FindReferences    +------------------+
| IRefactorService    +-+------------------->| FindResultsWin.  |
| (RefactorService)   | |  ShowRenamePreview |                  |
+---------------------+ +---->               +------------------+
        |
+-------v-----------+    Write     +------------------------+
| IReferenceCatalog |              | AtomicMultiFileWriter  |
| (ReferenceCatalog)|              | (tmp + File.Move)      |
+-------------------+              +------------------------+
```

### Debug / Trace pipeline

```
+------------------------+   AddObserver / RemoveObserver
| IAiTraceObserver       +-----------------------------------------+
| (subscriber per window)|                                         |
+------------------------+                                         v
                                                    +-----------------------------+
                                                    | AiTracerCoordinator        |
                                                    | (reference-counted,        |
                                                    |  effective TraceLevel)     |
                                                    +-----------+-----------------+
                                                                |
                                          BeginObservingAssetImpl (override)
                                                                |
+-----------------------------+          +-------------------+  |
| IAiDebugSession             |          | Subsystem-specific|<-+
| (AiDebugSessionBase)        |          | coordinator       |
|  Breakpoints, Pause/Step    |          | (in BTree/HSM     |
|  Continue, StepOver/In/Out  |          |  editor project)  |
+-----------------------------+          +-------------------+
             |
+------------v---------+   Factory  +-------------------------+
| DebugSessionRegistry |<-----------| Subsystem registers     |
| (IDebugSessionRegistry)|          | factory at startup      |
| ActiveSession,       |            +-------------------------+
| ActiveObservers,     |
| IDisposable tokens   |
+----------------------+
             |
+------------v---------+
| LiveSessionRegistry  |
| (ILiveSessionProvider|
|  assetId -> count)   |
+----------------------+
```

---

## Source Structure

All types live under the root namespace `Hrot.Editor.AiShared` with sub-namespaces matching
their folder.

### `Hrot.Editor.AiShared` (root)

| File | Type | Description |
|------|------|-------------|
| `Identity/IEditableAsset.cs` | `interface IEditableAsset` | Core asset contract used everywhere. |
| `Identity/AssetKind.cs` | `enum AssetKind` | Discriminator: Blueprint, BTree, Hsm, Blackboard. |
| `Identity/AssetIdHash.cs` | `static class AssetIdHash` | FNV-1a-32 hash primitive. |
| `Identity/AssetIdHasher.cs` | `static class AssetIdHasher` | Derives a deterministic `Guid` from an asset name. |
| `ReactiveGuardVocabulary.cs` | `static class ReactiveGuardVocabulary` | Shared string constants (category name, tooltips) for the Reactive Guards palette concept used across BTree, HSM, and Blueprint editors. |

### `Hrot.Editor.AiShared.Catalog`

| File | Type | Description |
|------|------|-------------|
| `IAssetCatalog.cs` | `interface IAssetCatalog` | Read-only catalog surface consumed by windows. |
| `IAssetCatalogContributor.cs` | `interface IAssetCatalogContributor` | Per-subsystem asset enumeration plug-in point. |
| `AssetCatalog.cs` | `sealed class AssetCatalog` | Concrete aggregating implementation; rebuilt on any contributor change. |

### `Hrot.Editor.AiShared.Selection`

| File | Type | Description |
|------|------|-------------|
| `IAssetSubSelection.cs` | `interface IAssetSubSelection` | Marker interface for per-asset selection records. |
| `SubSelectionRecords.cs` | 5 record types | `BlueprintNodeSelection`, `BTreeNodeSelection`, `HsmStateSelection`, `HsmTransitionSelection`, `HsmRegionSelection`. |
| `EditorSelectionStore.cs` | `sealed class EditorSelectionStore` | Central mutable selection state. |
| `IGSelectionBridge.cs` | `interface IGSelectionBridge` | Adapter contract for external (IG/DDS) entity-selection events. |
| `CallbackSelectionBridge.cs` | `sealed class CallbackSelectionBridge` | DDS-free `IGSelectionBridge` based on a subscription factory callback. |

### `Hrot.Editor.AiShared.References`

| File | Type | Description |
|------|------|-------------|
| `SubElementKind.cs` | `enum SubElementKind` | Action FQN, Condition FQN, Guard FQN, Event Name, Asset Reference, Blackboard Field. |
| `IAssetSubElement.cs` | `interface IAssetSubElement` | A referenceable element within an asset (key + kind + display name). |
| `AssetReference.cs` | `sealed record AssetReference` | One directed reference from a host element to a target key. |
| `IReferenceCatalog.cs` | `interface IReferenceCatalog` | Queryable reference graph. |
| `IReferenceCatalogContributor.cs` | `interface IReferenceCatalogContributor` | Subsystem-supplied element/reference enumeration. |
| `ReferenceCatalog.cs` | `sealed class ReferenceCatalog` | In-memory reference store; `Contribute()` for Phase 1, full contributor wiring in Phase 5/6. |

### `Hrot.Editor.AiShared.Refactor`

| File | Type | Description |
|------|------|-------------|
| `IRefactorService.cs` | `interface IRefactorService` + 8 record/enum types | Service contract plus all data-transfer types (preview, edits, issues, results). |
| `RefactorService.cs` | `sealed class RefactorService` | Concrete implementation: find-refs, preview rename, apply rename, preview delete, apply delete; async variants. |
| `AtomicMultiFileWriter.cs` | `sealed class AtomicMultiFileWriter` + `AtomicWriteResult` | Two-phase (write-tmp, move-final) multi-file write with rollback on error. |

### `Hrot.Editor.AiShared.Emit`

| File | Type | Description |
|------|------|-------------|
| `EmitterOptions.cs` | `sealed class EmitterOptions` | Newline and indent configuration. |
| `UsingDirectiveSet.cs` | `sealed class UsingDirectiveSet` | Accumulates `using` namespaces; produces sorted list via `FluentCSharpEmitterBase.SortUsings`. |
| `IFluentCSharpEmitter.cs` | `interface IFluentCSharpEmitter<TAsset>` | Subsystem emitter contract. |
| `FluentCSharpEmitterBase.cs` | `abstract class FluentCSharpEmitterBase` | Marker header, atomic write helper, `using`-sort logic. |

### `Hrot.Editor.AiShared.Layout`

| File | Type | Description |
|------|------|-------------|
| `BTreeLayoutAttribute.cs` | `[BTreeLayout(assetId)]` | Method-level attribute; marks a static factory for a BTree layout. |
| `HsmLayoutAttribute.cs` | `[HsmLayout(assetId)]` | Same pattern for HSM layouts. |
| `BlueprintLayoutAttribute.cs` | `[BlueprintLayout(assetId)]` | Same pattern for Blueprint layouts. |
| `NodeLayoutEntry.cs` | `sealed class NodeLayoutEntry` | BTree node visual data: position, size, comment, collapsed, color, expression target. |
| `StateLayoutEntry.cs` | `sealed class StateLayoutEntry` | HSM state visual data: position, size, comment, collapsed, color. |
| `TransitionLayoutEntry.cs` | `sealed class TransitionLayoutEntry` | HSM transition waypoints, comment, color. |
| `RegionLayoutEntry.cs` | `sealed class RegionLayoutEntry` | HSM region visual data: same fields as `StateLayoutEntry`. |
| `BTreeEditorLayout.cs` | `sealed class BTreeEditorLayout` | Canvas pan/zoom + node dictionary. |
| `HsmEditorLayout.cs` | `sealed class HsmEditorLayout` | Canvas pan/zoom + state/transition/region dictionaries. |
| `BTreeEditorLayoutBuilder.cs` | `sealed class BTreeEditorLayoutBuilder` | Fluent builder for `BTreeEditorLayout`; used by generated layout methods. |
| `HsmEditorLayoutBuilder.cs` | `sealed class HsmEditorLayoutBuilder` | Fluent builder for `HsmEditorLayout`. |
| `LayoutDiscovery.cs` | `static class LayoutDiscovery` | Reflection scanner: finds attribute-decorated static methods in an assembly and invokes them to obtain layout objects. |

### `Hrot.Editor.AiShared.Blackboard`

Editor-side blackboard variable management shared across BTree and HSM editors.

| File | Type | Description |
|------|------|-------------|
| `IBlackboardManagedAsset.cs` | `interface IBlackboardManagedAsset` | Implemented by any asset that carries an editor-managed variable list (`BehaviorTreeAsset`, `HsmAsset`). Exposes `IsBlackboardEditorManaged`, `BlackboardVariables`, mutation helpers (`AddVariable`, `RemoveVariable`, `RenameVariable`, `MoveVariable`, `UpdateVariableComment`, `RemoveVariables`), alias-binding helpers (`GetAliasesFor`, `AddAlias`, `RemoveAlias`), `CountNodesReferencingVariable`, and load-state properties. |
| `BlackboardVariableEntry.cs` | `record BlackboardVariableEntry` | Immutable: `Name`, `FieldType`, `Comment`. One entry per declared variable. |
| `BlackboardAliasBinding.cs` | `record BlackboardAliasBinding` | Records a sub-tree requirement bound to a variable: `RequiringAssetId`, `RequiringElementId`, `RequiringAssetName`, `RequiredByPath`, `DtoType`. |
| `BlackboardLoadState.cs` | `enum BlackboardLoadState` | Load-time health: `Clean`, `SpanCaptureFailed`, `StructParseFailed`, `AssemblyFailed`. Drives banner display and save-gate logic in `BlackboardAuthoringWindow`. |
| `BlackboardDiagnosticCode.cs` | `enum BlackboardDiagnosticCode` | Three codes: `UnusedVariable` (Info), `VariableTypeNotFound` (Warning), `CrossRegionConflict` (Error). |
| `BlackboardBinPacker.cs` | `static class BlackboardBinPacker` | Classifies variables into `Inline` vs `Heavy` tiers, computes byte sizes, and emits `PackWarning` when budgets are exceeded. Constants: `MaxInlineBytes = 100`, `MaxHeavyBytes` TBD per tier. |
| `BlackboardVariableDescriptor.cs` | `record BlackboardVariableDescriptor` | `Name` + `FieldType`; packing input. |
| `BlackboardFieldClassifier.cs` | `static class BlackboardFieldClassifier` | Maps a CLR type to a `BlackboardFieldKind` for display in the Variables panel. |
| `BlackboardNameValidator.cs` | `static class BlackboardNameValidator` | Validates that a proposed variable name is a legal C# identifier and does not collide with existing names. |
| `BlackboardTypeHelper.cs` | `static class BlackboardTypeHelper` | Derives the short display name for a CLR type (e.g. `"int"` for `System.Int32`). |
| `BlackboardSourceTextParser.cs` | `static class BlackboardSourceTextParser` | Parses an existing companion `.cs` file to extract verbatim field spans for safe round-trip editing. |
| `BlackboardDtoEmitter.cs` | `sealed class BlackboardDtoEmitter` | Generates the companion `.cs` struct file from `IBlackboardManagedAsset`. Extends `FluentCSharpEmitterBase`. |
| `IBlackboardAggregator.cs` | `interface IBlackboardAggregator` | Aggregates per-subtree DTO requirements across the catalog for display in the Blackboard Variables window. |
| `IBTreeSyncableAsset.cs` | `interface IBTreeSyncableAsset` | Implemented by `BehaviorTreeAsset`. Exposes subtree sync bindings and sub-tree DTO metadata needed by the orchestrator emitter. |
| `SubtreeSyncBinding.cs` | `record SubtreeSyncBinding` | One sync binding: `FieldName`, `SyncIn`, `SyncOut`. |
| `SubtreeNodeInfo.cs` | `record SubtreeNodeInfo` | Sub-tree identity metadata: `SubtreeName`, `SubDtoTypeName`, `SubDtoTypeNs`. |
| `BlackboardAliasDropValidator.cs` | `static class BlackboardAliasDropValidator` | Validates drag-and-drop alias assignments in the Variables panel. |
| `ApproachBSyncGroup.cs` | `sealed record ApproachBSyncGroup` | Groups Approach B sync bindings by sub-tree for orchestrator emission. |
| `ActionSchemaExporter.cs` | `sealed class ActionSchemaExporter` | Exports action schema JSON for external tool consumption. |
| `ActionSchemaExporterCatalogWatcher.cs` | `sealed class ActionSchemaExporterCatalogWatcher` | Triggers export on catalog change. |
| `VariablesPanelControl.cs` | `sealed class VariablesPanelControl` | Renders the variable table within `BlackboardAuthoringWindow` (testable, ImGui-free logic). |

### `Hrot.Editor.AiShared.HotReload`

| File | Type | Description |
|------|------|-------------|
| `HotReloadTier.cs` | `enum HotReloadTier` | `Cosmetic` (layout only), `Soft` (params), `Hard` (topology). |
| `HotReloadStatus.cs` | `sealed record HotReloadStatus` | Tier + live-instance count; `RequiresConfirmation` when hard and instances exist. |
| `HotReloadClassifier.cs` | `static class HotReloadClassifier` | Classifies a reload by comparing structure/param hash pairs; coalesces multiple tiers. |

### `Hrot.Editor.AiShared.Validation`

| File | Type | Description |
|------|------|-------------|
| `IAssetValidator.cs` | `interface IAssetValidator` + `AssetDiagnostic` + `AssetDiagnosticSeverity` | Pluggable per-kind validator contract and its output types. |

### `Hrot.Editor.AiShared.Debug`

| File | Type | Description |
|------|------|-------------|
| `TraceLevel.cs` | `[Flags] enum TraceLevel` | Lifecycle, Decisions, Values, Async, Errors, All. |
| `BreakpointId.cs` | `readonly record struct BreakpointId` | Strongly-typed integer breakpoint handle. |
| `Breakpoint.cs` | `sealed record Breakpoint` | Id, AssetId, ElementId, HitCount, Enabled, DisplayName. |
| `IAiTraceObserver.cs` | `interface IAiTraceObserver` | Passive trace subscriber: begin/end observing an asset, get active entities. |
| `IAiDebugSession.cs` | `interface IAiDebugSession` | Full debug session: extends `IAiTraceObserver`; adds breakpoints, pause/step/continue. |
| `AiDebugSessionBase.cs` | `abstract class AiDebugSessionBase` | Concrete breakpoint list, pause state, step delegation via abstract template methods. |
| `AiTracerCoordinator.cs` | `class AiTracerCoordinator` | Reference-counted observer registry; effective `TraceLevel` is bitwise OR of all observer levels. |
| `IDebugSessionRegistry.cs` | `interface IDebugSessionRegistry` | Session factory registry; allows acquiring and releasing one active session at a time. |
| `DebugSessionRegistry.cs` | `sealed class DebugSessionRegistry` | Concrete registry with type-keyed session factories and `IDisposable` observer tokens. |
| `ILiveSessionProvider.cs` | `interface ILiveSessionProvider` | Reports live entity count per asset. |
| `LiveSessionRegistry.cs` | `sealed class LiveSessionRegistry` | Maps `assetId -> IAiDebugSession`; reports count 1 when attached. |
| `IRuntimeInspectorPane.cs` | `interface IRuntimeInspectorPane` | Subsystem pane contract for `RuntimeInspectorWindow`. |
| `ITraceLaneProvider.cs` | `interface ITraceLaneProvider` + `TraceLaneDescriptor` | Subsystem swim-lane definitions for `TraceTimelineWindow`. |

### `Hrot.Editor.AiShared.Windows`

| File | Type | Description |
|------|------|-------------|
| `AssetBrowserWindow.cs` | `sealed class AssetBrowserWindow` | Lists all assets; context menus for Find References, Rename (preview), Delete (preview). Shows live-instance count badge when entities are active. |
| `BlackboardAuthoringWindow.cs` | `sealed class BlackboardAuthoringWindow` | Docked panel for editing the blackboard variable list of the active asset. Renders variable rows with type, byte size, alias badges, and inline rename. Shows inline/heavy byte budget gauges. Delegates to `VariablesPanelControl` and `BlackboardBinPacker`. Window ID: `ai_blackboard_variables`. |
| `InspectorWindow.cs` | `sealed class InspectorWindow` | Active-asset property panel; context menu for Find References, Rename. |
| `RuntimeInspectorWindow.cs` | `sealed class RuntimeInspectorWindow` | Shell that delegates to registered `IRuntimeInspectorPane` implementations. |
| `TraceTimelineWindow.cs` | `sealed class TraceTimelineWindow` | Shell that delegates to registered `ITraceLaneProvider` implementations. |
| `FindResultsWindow.cs` | `sealed class FindResultsWindow` | Renders find-references results and rename preview diffs. |
| `DiagnosticsWindow.cs` | `sealed class DiagnosticsWindow` | Runs all registered `IAssetValidator` instances against all catalog assets each frame; displays colored table. |
| `ShellCommandCoreBundle.cs` | `sealed class ShellCommandCoreBundle` | `IUiBundle` wrapping `CgfEditorShellToolbar.RegisterCommonCore`; composed by **both** hosts (`CE-069`). |
| ⛔ ~~`SharedAiWindowRegistrar.cs`~~ | *(deleted `2026-08-27`)* | **DELETED — `CE-070`.** A flat host-level `IWindowRegistrar` over 7 windows, with **zero** constructions in the repo; its windows declare `WindowScope.PerspectiveBound`, so the live path is `PerspectiveWorkspaceRegistrar` (both hosts, one per perspective). 📄 `docs/DESIGN_Subsystem_Composition_Unification.md` §5b.5–§5b.6. |

### `Hrot.Editor.AiShared.Di`

| File | Type | Description |
|------|------|-------------|
| `SharedAiEditorServiceCollectionExtensions.cs` | `static class SharedAiEditorServiceCollectionExtensions` | `AddSharedAiEditor()` extension; wires all singletons in one call. |

---

## Public API Reference

### `IEditableAsset`

```csharp
public interface IEditableAsset
{
    Guid       AssetId        { get; }
    string     Name           { get; }
    AssetKind  Kind           { get; }
    string     SourceFilePath { get; }
    bool       IsDirty        { get; }
    bool       IsEditorOwned  { get; }
    event Action? Changed;
}
```

### `AssetKind`

```csharp
public enum AssetKind { Blueprint, BTree, Hsm, Blackboard }
```

### `AssetIdHash` / `AssetIdHasher`

```csharp
// Low-level primitive
public static class AssetIdHash
{
    public static int Fnv1a32(ReadOnlySpan<byte> bytes);
}

// High-level helper: name -> Guid
public static class AssetIdHasher
{
    public static Guid FromName(string name);
}
```

### `IBlackboardManagedAsset`

```csharp
public interface IBlackboardManagedAsset
{
    bool                              IsBlackboardEditorManaged { get; }
    IReadOnlyList<BlackboardVariableEntry> BlackboardVariables  { get; }
    BlackboardLoadState               LoadState                 { get; }
    string?                           LoadDiagnosticMessage     { get; }

    void AddVariable(BlackboardVariableEntry entry);
    void RemoveVariable(string name);
    void RemoveVariables(IReadOnlyList<string> names);
    void UpdateVariableComment(string name, string? comment);
    void MoveVariable(int sourceIndex, int destIndex);
    void RenameVariable(string oldName, string newName);
    int  CountNodesReferencingVariable(string name);

    IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName);
    void AddAlias(string variableName, BlackboardAliasBinding binding);
    void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId);
}
```

`BehaviorTreeAsset` and `HsmAsset` both implement this interface. The default implementations
of `LoadState` and `LoadDiagnosticMessage` return `Clean` and `null` respectively.

### `BlackboardVariableEntry` / `BlackboardAliasBinding` / `BlackboardLoadState`

```csharp
public record BlackboardVariableEntry(string Name, Type FieldType, string? Comment);

public record BlackboardAliasBinding(
    Guid   RequiringAssetId,
    Guid   RequiringElementId,
    string RequiringAssetName,
    string RequiredByPath,
    Type   DtoType);

public enum BlackboardLoadState { Clean, SpanCaptureFailed, StructParseFailed, AssemblyFailed }
```

### `IAssetCatalog`

```csharp
public interface IAssetCatalog
{
    IReadOnlyList<IEditableAsset> All { get; }
    IEditableAsset?               FindByAssetId(Guid assetId);
    IEditableAsset?               FindByName(string name);
    IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId);
    event Action? Changed;
}
```

### `IAssetCatalogContributor`

```csharp
public interface IAssetCatalogContributor
{
    AssetKind                     Kind      { get; }
    IReadOnlyList<IEditableAsset> Enumerate();
    event Action?                 ContributorChanged;
}
```

### `EditorSelectionStore`

```csharp
public sealed class EditorSelectionStore
{
    public IEditableAsset?    ActiveAsset         { get; set; }
    public IAssetSubSelection? ActiveSubSelection { get; set; }
    public Entity?            SelectedEntity      { get; set; }
    public IAssetSubSelection? GetSubSelection(Guid assetId);
    public void SetSubSelection(Guid assetId, IAssetSubSelection? selection);
    public void RegisterOpenAsset(Guid assetId);
    public void UnregisterOpenAsset(Guid assetId);
    public void Forget(Guid assetId);
    public event Action? OnSelectionChanged;
}
```

### Sub-selection records

```csharp
public sealed record BlueprintNodeSelection(Guid GraphId, Guid NodeId)        : IAssetSubSelection;
public sealed record BTreeNodeSelection(Guid VisualId)                        : IAssetSubSelection;
public sealed record HsmStateSelection(Guid StableId)                         : IAssetSubSelection;
public sealed record HsmTransitionSelection(Guid VisualId)                    : IAssetSubSelection;
public sealed record HsmRegionSelection(Guid StableId, int RegionIndex)       : IAssetSubSelection;
```

### `IGSelectionBridge` / `CallbackSelectionBridge`

```csharp
public interface IGSelectionBridge : IDisposable
{
    bool IsConnected { get; }
    void Connect(EditorSelectionStore store);
    void Disconnect();
}

public sealed class CallbackSelectionBridge : IGSelectionBridge
{
    public CallbackSelectionBridge(Func<Action<Entity?>, IDisposable> subscribeFactory);
    // IDisposable -> Disconnect()
}
```

### `IReferenceCatalog`

```csharp
public interface IReferenceCatalog
{
    IReadOnlyList<IAssetSubElement> AllElements { get; }
    IAssetSubElement?               FindElement(string key);
    IReadOnlyList<AssetReference>   FindReferences(string targetKey);
    IReadOnlyList<AssetReference>   AllReferencesIn(Guid hostAssetId);
    event Action? Changed;
}
```

### `AssetReference` / `IAssetSubElement` / `SubElementKind`

```csharp
public sealed record AssetReference(
    Guid          HostAssetId,
    AssetKind     HostKind,
    Guid          HostElementId,
    string        HostDisplayPath,
    string        TargetKey,
    SubElementKind TargetKind);

public interface IAssetSubElement
{
    string      Key           { get; }
    SubElementKind Kind       { get; }
    string      DisplayName   { get; }
    Guid?       SourceAssetId { get; }
}

public enum SubElementKind
{
    ActionFqn, ConditionFqn, GuardFqn,
    EventName, AssetReference, BlackboardField,
}
```

### `IRefactorService`

```csharp
public interface IRefactorService
{
    IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey);
    IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId);

    RefactorPreview   PreviewRename(string fromKey, string toKey, RefactorOptions options);
    RefactorResult    ApplyRename(RefactorPreview preview);

    DeletePreview     PreviewDelete(Guid assetId, DeleteOptions options);
    RefactorResult    ApplyDelete(DeletePreview preview);

    Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default);
    Task<RefactorResult>  ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default);
}
```

Key data-transfer types:

```csharp
public sealed record RefactorOptions(
    bool IncludeBlueprint = true,
    bool IncludeBTree     = true,
    bool IncludeHsm       = true,
    bool DryRunOnly       = false);

public sealed record RefactorPreview(
    string                      FromKey,
    string                      ToKey,
    IReadOnlyList<RefactorFileEdit> Edits,
    IReadOnlyList<RefactorIssue>    Issues);

public sealed record RefactorResult(
    bool                     Success,
    IReadOnlyList<string>    WrittenFiles,
    string?                  FailureReason);

public sealed record RefactorFileEdit(
    string                       FilePath,
    Guid                         HostAssetId,
    IReadOnlyList<RefactorLineEdit> LineEdits);

public sealed record RefactorLineEdit(
    int    LineNumber,
    string OriginalText,
    string ReplacementText,
    string ContextDescription);

public sealed record RefactorIssue(
    RefactorIssueSeverity Severity,
    string                Description,
    Guid?                 RelatedAssetId);

public enum RefactorIssueSeverity { Info, Warning, Error }

public sealed record AssetReferenceInfo(
    Guid           HostAssetId,
    AssetKind      HostKind,
    Guid           HostElementId,
    string         HostDisplayPath,
    string         TargetKey,
    SubElementKind TargetKind,
    string         SourceFilePath);
```

### `AtomicMultiFileWriter`

```csharp
public sealed class AtomicMultiFileWriter
{
    public AtomicWriteResult Write(IReadOnlyDictionary<string, string> filePathToContent);
}

public sealed record AtomicWriteResult(
    bool                  Success,
    IReadOnlyList<string> SuccessfullyWritten,
    string?               FailureReason);
```

### `FluentCSharpEmitterBase`

```csharp
public abstract class FluentCSharpEmitterBase
{
    public const string EditorGeneratedMarker = "// HROT_EDITOR_GENERATED ...";

    protected abstract string EmitCore(IEditableAsset asset);

    public static IReadOnlyList<string> SortUsings(IEnumerable<string> namespaces);
    public static string BuildHeader(Guid assetId);
    public static bool   WriteAtomic(string filePath, string content);
}

public interface IFluentCSharpEmitter<TAsset>
{
    string Emit(TAsset asset);
}

public sealed class UsingDirectiveSet
{
    public void Add(string ns);
    public void AddRange(IEnumerable<string> namespaces);
    public IReadOnlyList<string> ToSortedList();
}

public sealed class EmitterOptions
{
    public static readonly EmitterOptions Default;
    public string NewLine { get; init; }
    public string Indent  { get; init; }
}
```

### Layout

```csharp
// Attributes (method-level)
[BTreeLayoutAttribute(string assetId)]
[HsmLayoutAttribute(string assetId)]
[BlueprintLayoutAttribute(string assetId)]

// Layout objects
public sealed class BTreeEditorLayout
{
    public Vector2 PanOffset  { get; init; }
    public float   ZoomLevel  { get; init; }
    public IReadOnlyDictionary<Guid, NodeLayoutEntry> Nodes { get; init; }
}

public sealed class HsmEditorLayout
{
    public Vector2 PanOffset  { get; init; }
    public float   ZoomLevel  { get; init; }
    public IReadOnlyDictionary<Guid, StateLayoutEntry>      States      { get; init; }
    public IReadOnlyDictionary<Guid, TransitionLayoutEntry> Transitions { get; init; }
    public IReadOnlyDictionary<Guid, RegionLayoutEntry>     Regions     { get; init; }
}

// Fluent builders
public sealed class BTreeEditorLayoutBuilder
{
    public BTreeEditorLayoutBuilder Canvas(Vector2 panOffset, float zoomLevel);
    public BTreeEditorLayoutBuilder Node(string visualId, Vector2 position,
        Vector2? sizeOverride = null, string? comment = null,
        bool collapsed = false, string? color = null, string? expressionTarget = null);
    public BTreeEditorLayout Build();
}

public sealed class HsmEditorLayoutBuilder
{
    public HsmEditorLayoutBuilder Canvas(Vector2 panOffset, float zoomLevel);
    public HsmEditorLayoutBuilder State(string stableId, Vector2 position,
        Vector2? sizeOverride = null, string? comment = null,
        bool collapsed = false, string? color = null);
    public HsmEditorLayoutBuilder Transition(string visualId, Vector2[] waypoints,
        string? comment = null, string? color = null);
    public HsmEditorLayoutBuilder Region(string stableId, int regionIndex, Vector2 position,
        Vector2? sizeOverride = null, string? comment = null,
        bool collapsed = false, string? color = null);
    public HsmEditorLayout Build();
}

// Reflection-based layout loader
public static class LayoutDiscovery
{
    public static TLayout? TryGetLayout<TAttr, TLayout>(Assembly assembly, Guid assetId)
        where TAttr   : Attribute
        where TLayout : class;
}
```

### Hot-Reload

```csharp
public enum HotReloadTier { Cosmetic, Soft, Hard }

public sealed record HotReloadStatus(HotReloadTier Tier, int LiveInstanceCount)
{
    public bool RequiresConfirmation { get; }
}

public static class HotReloadClassifier
{
    public static HotReloadTier Classify(
        int previousStructureHash, int newStructureHash,
        int previousParamHash,     int newParamHash);
    public static HotReloadTier MostImpactful(HotReloadTier a, HotReloadTier b);
}
```

### Validation

```csharp
public enum AssetDiagnosticSeverity { Info, Warning, Error }

public sealed record AssetDiagnostic(
    Guid                   AssetId,
    string                 AssetName,
    AssetDiagnosticSeverity Severity,
    string                 Code,
    string                 Message);

public interface IAssetValidator
{
    AssetKind SupportedKind { get; }
    IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset);
}
```

### Debug / Trace

```csharp
[Flags]
public enum TraceLevel
{
    None = 0, Lifecycle = 1, Decisions = 2, Values = 4, Async = 8, Errors = 16,
    All  = Lifecycle | Decisions | Values | Async | Errors,
}

public readonly record struct BreakpointId(int Value);

public sealed record Breakpoint(
    BreakpointId Id, Guid AssetId, Guid ElementId,
    int HitCount, bool Enabled, string DisplayName);

public interface IAiTraceObserver
{
    void BeginObservingAsset(Guid assetId, TraceLevel level);
    void EndObservingAsset(Guid assetId);
    IReadOnlyList<Entity> GetActiveEntities(Guid assetId);
}

public interface IAiDebugSession : IAiTraceObserver
{
    bool          IsAttached  { get; }
    void          Detach();
    BreakpointId  SetBreakpoint(Guid assetId, Guid elementId);
    void          ClearBreakpoint(BreakpointId id);
    void          ClearAllBreakpoints();
    IReadOnlyList<Breakpoint> GetBreakpoints();
    bool          IsAnyBreakpointActive { get; }
    bool          IsPaused   { get; }
    Breakpoint?   PausedAt   { get; }
    Entity?       PausedOnEntity { get; }
    void          Continue();
    void          StepOver();
    void          StepInto();
    void          StepOut();
    void          Pause();
    event Action? OnSessionStateChanged;
}

public class AiTracerCoordinator
{
    public void         AddObserver(Guid assetId, TraceLevel level);
    public void         RemoveObserver(Guid assetId);
    public TraceLevel   GetEffectiveLevel(Guid assetId);
    public bool         IsObserving(Guid assetId);
    protected virtual void BeginObservingAssetImpl(Guid assetId, TraceLevel level);
    protected virtual void EndObservingAssetImpl(Guid assetId);
}

public interface IDebugSessionRegistry
{
    bool                       TryAcquireSession<TSession>(out TSession? session)
                                   where TSession : class, IAiDebugSession;
    void                       ReleaseSession(IAiDebugSession session);
    IDisposable                RegisterObserver<TObserver>(TObserver observer)
                                   where TObserver : IAiTraceObserver;
    IReadOnlyList<IAiTraceObserver> ActiveObservers { get; }
    IAiDebugSession?           ActiveSession { get; }
    event Action?              Changed;
}

public interface ILiveSessionProvider
{
    int GetActiveEntityCount(Guid assetId);
}

public interface IRuntimeInspectorPane
{
    AssetKind TargetKind { get; }
    void Draw();
}

public sealed record TraceLaneDescriptor(string Id, string DisplayName, TraceLevel SupportedLevels);

public interface ITraceLaneProvider
{
    AssetKind Kind { get; }
    IReadOnlyList<TraceLaneDescriptor> Lanes { get; }
}
```

### DI

```csharp
public static class SharedAiEditorServiceCollectionExtensions
{
    // Call from subsystem composition root.
    public static IServiceCollection AddSharedAiEditor(this IServiceCollection services);
}
```

### `IActionSchemaExporter` / `ActionSchemaEntry`

Reflection-based registry of all action/condition/guard methods across the loaded assembly.
Populated on editor startup and rebuilt after every hot reload. Used by the Variables panel
picker and by `IBlackboardAggregator` to resolve DTO types.

```csharp
public interface IActionSchemaExporter
{
    ActionSchemaEntry? Lookup(string actionFqn);
    IReadOnlyList<ActionSchemaEntry> All { get; }
    void Rebuild();
    event Action? Changed;
}

public sealed record ActionSchemaEntry(
    string          Fqn,           // "Hrot.Game.Combat.CombatActions.FireAtTarget"
    string          ShortName,     // "FireAtTarget"
    Type            DtoType,       // first ref parameter type
    ActionHosting   Hostings,      // BTreeAction | HsmAction | SharedAi | Heavy
    BlackboardAccess ParamAccess,  // ReadOnly | ReadWrite | Unknown
    Type?           HeavyDtoType); // set for [SharedAiHeavyAction]; null otherwise

[Flags]
public enum ActionHosting
{
    BTreeAction    = 1 << 0,
    BTreeCondition = 1 << 1,
    BTreeObserver  = 1 << 2,
    HsmAction      = 1 << 3,
    HsmGuard       = 1 << 4,
    Heavy          = 1 << 5,
}

public enum BlackboardAccess { ReadOnly, ReadWrite, Unknown }
```

### `IBlackboardAggregator` / `AggregationResult`

Walks an asset and its statically-linked descendants to gather all parameter DTO
requirements. Used to populate the "Unbound Sub-Tree Requirements" section of
`BlackboardAuthoringWindow`.

```csharp
public interface IBlackboardAggregator
{
    AggregationResult Aggregate(IEditableAsset rootAsset);
}

public sealed record AggregationResult(
    IReadOnlyList<DtoRequirement>   Requirements,
    IReadOnlyList<AggregationWarning> Warnings);

public sealed record DtoRequirement(
    Type   DtoType,
    string RequiredByPath,     // "Shoot_BT -> Action#7 (FireAtTarget)"
    Guid   RequiringAssetId,
    Guid   RequiringElementId);
```

### `IBTreeSyncableAsset`

Implemented by `BehaviorTreeAsset`. Exposes the per-Subtree field-level sync bindings
(Approach B) and sub-tree DTO metadata used by the orchestrator emitter to generate
sync-copy code in `{AssetName}.Orchestrators.g.cs`.

```csharp
public interface IBTreeSyncableAsset
{
    IReadOnlyList<SubtreeSyncBinding> GetSyncBindings(Guid subtreeVisualId);
    void SetSyncBinding(Guid subtreeVisualId, SubtreeSyncBinding binding);
    void ClearSyncBindings(Guid subtreeVisualId);
    IReadOnlyList<SubtreeNodeInfo> GetSubtreeNodes();
}

public sealed record SubtreeSyncBinding(
    string FieldName,    // field name in the sub-tree's DTO
    bool   SyncIn,       // copy master -> sub before tick
    bool   SyncOut);     // copy sub -> master after tick

public sealed record SubtreeNodeInfo(
    string SubtreeName,
    string SubDtoTypeName,
    string SubDtoTypeNs);
```

### `BlackboardDiagnosticCode`

Diagnostic codes emitted by BTree/HSM validators for blackboard-related issues:

```csharp
public enum BlackboardDiagnosticCode
{
    UnusedVariable,               // Info: variable declared but not referenced
    VariableTypeNotFound,         // Warning: DTO type dropped from assembly after reload
    UnboundActionNode,            // Error: action/condition node with null ExpressionTargetField
    UnboundSubTreeRequirement,    // Warning: sub-tree DTO requirement not aliased or promoted
    CrossRegionBlackboardConflict, // Warning: concurrent writes to same variable across parallel regions
    InlineMemoryExceeded,         // Error: master variables exceed 100 B inline budget
    DuplicateAliasAcrossRegions,  // Error: same variable aliased by sub-trees in concurrent regions
}
```

---

## Blackboard Authoring Flow

The blackboard authoring subsystem enables visual DTO editing from within the BTree and HSM
editors. The following summarizes the key concepts; full detail is in the design document at
`.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md`.

### File ownership — two categories

Every blackboard DTO file falls into exactly one category, determined by the
`HROT_EDITOR_GENERATED` marker at the top of the file:

- **Category 1 (user-owned):** No marker. The editor reflects the struct from the
  assembly, surfaces fields in the Variables panel as read-only, and never writes
  the file.
- **Category 2 (editor-owned):** Marker present plus `OwningAssetId` comment. The
  editor regenerates the file on every save. User-introduced fields the editor cannot
  model (non-plain declarations) are captured verbatim and re-emitted unchanged.

Assets opt in to editor ownership by setting `BlackboardManaged = true` on their
`[BTreeDefinition]` or `[HsmDefinition]` attribute. The editor then creates and
manages `{AssetName}.Blackboard.cs` alongside the asset file.

### DTO load pipeline

On asset open with `BlackboardManaged = true`:
1. Locate `{AssetName}.Blackboard.cs` in the same directory.
2. Reflect the struct from the loaded assembly to get `FieldInfo[]`.
3. Read source text to extract `///` doc comments and verbatim spans.
4. Classify each field: *editor-managed* (plain `public {KnownType} {Name};`) or
   *read-only-passthrough* (anything else — preserved verbatim).
5. Build the `BlackboardVariable` list that the Variables panel renders.

Load health is reported via `BlackboardLoadState` (`Clean` / `SpanCaptureFailed` /
`StructParseFailed` / `AssemblyFailed`).

### Recursive aggregation

`IBlackboardAggregator.Aggregate()` walks the asset's Subtree nodes recursively,
resolving each referenced asset via `IAssetCatalog`, and collects all parameter DTO
requirements from action nodes. Results populate the "Unbound Sub-Tree Requirements"
panel section. Designers bind these to master variables via drag-drop (Approach A
aliasing) or promote them to new standalone variables.

### Memory tier bin-packing

`BlackboardBinPacker` partitions variables between:
- **Inline tier:** `BrainBlackboard.BehaviorParameters` — 100 bytes max.
- **Heavy tier:** `Blackboard1024.Memory` — 928 usable bytes, allocated on demand.

Master variables always stay inline. Aggregated sub-tree variables fill remaining
inline space first, then overflow to heavy. When heavy variables exist, the asset's
`[BTreeDefinition]` / `[HsmDefinition]` attribute carries `HeavyDtoType = typeof(X)`
so the source generator wires up the `Blackboard1024` component provisioning.

---

## Dependencies

### Project references

| Assembly | Role |
|----------|------|
| `Fdp.Core` | `Entity` type, domain primitives used by `IAiDebugSession` and `EditorSelectionStore`. |
| `Fdp.Presentation` | `ManagedWindow`, `WindowManager`, `IWindowRegistrar`, `WindowScope`, `IWindowRegistrar` from `Fdp.Toolkit.Runner`. |

### NuGet packages

| Package | Version | Role |
|---------|---------|------|
| `Microsoft.Extensions.DependencyInjection` | 8.0.0 | `IServiceCollection` / `AddSingleton` used in `SharedAiEditorServiceCollectionExtensions`. |

### Test project

`Hrot.Editor.AiShared.Tests` is granted `[InternalsVisibleTo]` access, which exposes
`RuntimeInspectorWindow.RegisteredPaneCount` and `TraceTimelineWindow.RegisteredProviderCount`
to the test assembly.

---

## Usage Examples

### Example 1 -- Bootstrapping the shared editor in a subsystem host

```csharp
// Inside Hrot.BTree.Editor composition root

var services = new ServiceCollection();

// Register shared AI editor services (catalog, selection, debug, refactor, windows).
services.AddSharedAiEditor();

// Register BTree-specific catalog contributor.
services.AddSingleton<IAssetCatalogContributor, BTreeAssetCatalogContributor>();

// Register BTree-specific validator.
services.AddSingleton<IAssetValidator, BTreeAssetValidator>();

// Register BTree-specific runtime inspector pane.
services.AddSingleton<IRuntimeInspectorPane, BTreeRuntimeInspectorPane>();

var sp = services.BuildServiceProvider();

// Wire the catalog contributor into the catalog.
var catalog = sp.GetRequiredService<IAssetCatalog>() as AssetCatalog;
catalog!.AddContributor(sp.GetRequiredService<IAssetCatalogContributor>());

// Register windows with the window manager.
var registrar = sp.GetRequiredService<IWindowRegistrar>();
registrar.RegisterWindows(windowManager);

// Plug the subsystem pane into the runtime inspector window.
var runtimeInspector = sp.GetRequiredService<RuntimeInspectorWindow>();
runtimeInspector.RegisterPane(sp.GetRequiredService<IRuntimeInspectorPane>());
```

### Example 2 -- Rename workflow (preview then apply)

```csharp
// Triggered from the Inspector context menu "Rename..." item.

var refactor = serviceProvider.GetRequiredService<IRefactorService>();
var findResults = serviceProvider.GetRequiredService<FindResultsWindow>();

// Step 1: preview -- shows affected files and line edits without touching disk.
var options = new RefactorOptions(IncludeBTree: true, IncludeHsm: true);
var preview = await refactor.PreviewRenameAsync("Hrot.Actions.MoveToTarget", "Hrot.Actions.MoveToPosition", options);

// Show the diff in the FindResults window before committing.
findResults.ShowRenamePreview(preview);

// Step 2: apply only if no errors and the user confirms.
if (!preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
{
    var result = await refactor.ApplyRenameAsync(preview);
    if (!result.Success)
        Console.Error.WriteLine($"Rename failed: {result.FailureReason}");
}
```

### Example 3 -- Implementing and registering a debug session

```csharp
// Hrot.BTree.Editor -- concrete debug session

public sealed class BTreeDebugSession : AiDebugSessionBase
{
    private readonly IBTreeKernel _kernel;

    public BTreeDebugSession(IBTreeKernel kernel, AiTracerCoordinator coordinator)
        : base(coordinator)
    {
        _kernel = kernel;
    }

    protected override void OnContinueImpl()   => _kernel.SetDebugPaused(false);
    protected override void OnPauseImpl()      => _kernel.SetDebugPaused(true);
    protected override void OnStepOverImpl()   => _kernel.StepOver();
    protected override void OnStepIntoImpl()   => _kernel.StepInto();
    protected override void OnStepOutImpl()    => _kernel.StepOut();

    public override IReadOnlyList<Entity> GetActiveEntities(Guid assetId)
        => _kernel.GetActiveEntities(assetId);
}

// In the composition root, register the session factory.
var registry = serviceProvider.GetRequiredService<DebugSessionRegistry>();
registry.RegisterSessionFactory(() =>
    new BTreeDebugSession(
        serviceProvider.GetRequiredService<IBTreeKernel>(),
        serviceProvider.GetRequiredService<AiTracerCoordinator>()));

// Acquire a session when the user opens the debug view.
if (registry.TryAcquireSession<BTreeDebugSession>(out var session))
{
    session!.SetBreakpoint(assetId, nodeId);
}
```

### Example 4 -- Declaring a canvas layout in a generated file

```csharp
// This file is typically emitted by the BTree emitter (FluentCSharpEmitterBase subclass).
// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten by the AI editor on next save.
// AssetId: 3fa85f64-5717-4562-b3fc-2c963f66afa6

public static class PatrolLayout
{
    [BTreeLayout("3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public static BTreeEditorLayout Build() =>
        new BTreeEditorLayoutBuilder()
            .Canvas(new Vector2(-120f, 0f), zoomLevel: 1.0f)
            .Node("a1b2c3d4-0000-0000-0000-000000000001", new Vector2(0f, 0f))
            .Node("a1b2c3d4-0000-0000-0000-000000000002", new Vector2(200f, 100f),
                comment: "Wait for order", collapsed: false)
            .Build();
}

// Loading the layout at canvas-open time:
var layout = LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(
    Assembly.GetExecutingAssembly(), assetId);
```

### Example 5 -- Hot-reload classification

```csharp
// Inside the BTree save pipeline:

int oldStructureHash = ComputeStructureHash(previousTree);
int oldParamHash     = ComputeParamHash(previousTree);
int newStructureHash = ComputeStructureHash(savedTree);
int newParamHash     = ComputeParamHash(savedTree);

var tier = HotReloadClassifier.Classify(oldStructureHash, newStructureHash, oldParamHash, newParamHash);

var liveCount = liveSessionProvider.GetActiveEntityCount(assetId);
var status    = new HotReloadStatus(tier, liveCount);

if (status.RequiresConfirmation)
{
    // Prompt user: "Hard reload will reset {liveCount} live instance(s). Proceed?"
}
else
{
    ApplyReload(tier, savedTree);
}
```

---

## Best Practices

### 1. Register subsystem-specific plug-ins before calling AddSharedAiEditor

`DiagnosticsWindow` is constructed with the registered `IAssetValidator` collection at DI
build time. Register all `IAssetValidator` and `IRuntimeInspectorPane` implementations
**before** calling `BuildServiceProvider()`.

### 2. Always use PreviewRename before ApplyRename

`RefactorService.PreviewRename` reads source files and computes line edits but makes no changes.
`ApplyRename` blocks on Error-severity issues. Show the preview in `FindResultsWindow` and let
the user confirm before calling `ApplyRename`.

### 3. Subsystem coordinators must override AiTracerCoordinator

The default `AiTracerCoordinator` is a no-op. Subsystem coordinators should override
`BeginObservingAssetImpl` / `EndObservingAssetImpl` to set the appropriate debug-state flags
on kernel entities. Pass the subsystem-specific coordinator to `AiDebugSessionBase`.

### 4. Layout attributes go on static factory methods only

`LayoutDiscovery.TryGetLayout` scans only `BindingFlags.Public | BindingFlags.Static` methods.
Place `[BTreeLayout]` / `[HsmLayout]` / `[BlueprintLayout]` only on public static parameterless
methods returning the matching layout type.

### 5. AssetIdHasher for deterministic IDs

When a subsystem creates new assets without a pre-existing persistent GUID, use
`AssetIdHasher.FromName(name)` to derive a deterministic ID from the asset's canonical name.
This prevents ID churn across editor restarts.

### 6. Emit files with FluentCSharpEmitterBase.WriteAtomic

Write generated `.cs` files through `WriteAtomic` to avoid partial writes: the method writes
to a `.tmp` file first and uses `File.Move` with `overwrite: true`. It also skips the write if
the content is identical, preventing unnecessary file-system events.

### 7. IGSelectionBridge keeps the library DDS-free

Do not add DDS or network dependencies to this project. When bridging an external selection
event, implement `IGSelectionBridge` in the consuming subsystem assembly and pass a subscription
factory via `CallbackSelectionBridge`. This keeps `Hrot.Editor.AiShared` testable without a
live DDS bus.

### 8. HotReloadTier.Hard requires user confirmation only when instances are live

Check `HotReloadStatus.RequiresConfirmation` before issuing a hard reload. A hard reload on a
tree with zero live instances is always safe to apply silently.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.BTree.Editor` | Subsystem editor; implements `IAssetCatalogContributor`, `IAiDebugSession`, `IFluentCSharpEmitter`, `IAssetValidator`, `IRuntimeInspectorPane`, `ITraceLaneProvider` for behaviour trees. |
| `Hrot.Hsm.Editor` | Subsystem editor; same plug-in points for hierarchical state machines. |
| `Hrot.Editor.AiShared.Tests` | Test project; exercises catalog, selection, references, refactor, layout, hot-reload, debug session, and window shells. Has `[InternalsVisibleTo]` access. |
| `Fdp.Core` | Provides `Entity`, shared domain primitives. Required by debug session and selection bridge. |
| `Fdp.Presentation` | Provides `ManagedWindow`, `WindowManager`, `WindowScope`, `IWindowRegistrar`. All seven shared windows extend `ManagedWindow`. |
| `Hrot.BTree` (runtime) | **Not a dependency.** Debug sessions talk to the runtime only through subsystem-specific kernel interfaces defined outside this library. |
| `Hrot.Hsm` (runtime) | **Not a dependency.** Same separation as BTree. |
