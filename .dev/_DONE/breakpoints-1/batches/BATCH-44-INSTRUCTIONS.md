# BATCH-44 — UBP-P7T1 + P7T2 + P7T3 + P7T4: Context-menu synthesis, gutter-renderer bridge, and probe-tag predicate

## Overview

This batch implements the four tasks of Phase P7:
- **P7T1**: BTree graph-editor context-menu items that synthesise universal breakpoints + gutter-renderer reads from `IDataBreakpointManager`
- **P7T2**: HSM context-menu (mirror of P7T1)
- **P7T3**: Blueprint canvas `Add Conditional Data Breakpoint...` + wire `BlueprintDebugSession` to route Slice 1 probe hits through `IDataBreakpointManager.OnExternalHit`
- **P7T4**: `ExternalHitTagPredicateDto`, compiler stub, full `OnExternalHit` implementation in `DataBreakpointManager`

Design references: [DESIGN.md §6.6](../DESIGN.md#66-blueprint-node-execution-breakpoints-slice-1-surface), [§9](../DESIGN.md#9-manager-api-idatabreakpointmanager), [§13.3](../DESIGN.md#133-graph-editor-context-menus-phase-p7)

---

## 1. `SearchPredicateDto.cs` — Add `ExternalHitTagPredicateDto` + `ReadOnlyChildIndices`

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`

### 1a. Add `[JsonDerivedType]` on the base class (after `BlueprintVariable`):
```csharp
[JsonDerivedType(typeof(ExternalHitTagPredicateDto),  "ExternalHitTag")]
```

### 1b. Add `ReadOnlyChildIndices` to `CompoundPredicateDto`:
```csharp
public sealed class CompoundPredicateDto : SearchPredicateDto
{
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    public List<SearchPredicateDto> Conditions { get; set; } = new();
    /// <summary>
    /// Zero-based indices of children that the editor should render as read-only.
    /// Auto-synthesised breakpoints mark the structural trace-buffer branch
    /// [EditReadOnly] so the operator cannot drift it away from the visual node.
    /// </summary>
    public List<int> ReadOnlyChildIndices { get; set; } = new();
}
```

### 1c. Add `ExternalHitTagPredicateDto` class (at the end of the file, before the closing `}`):
```csharp
    // ──────────────────────────────────────────────────────────────────────────
    // External-hit tag predicate
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Synthetic predicate used as a "fire from external probe" marker.
    /// The component-predicate compiler always returns <c>static (_, _) =&gt; false</c> for
    /// this type; it is never evaluated through <see cref="DataBreakpointSystem"/>.
    /// Instead, <see cref="IDataBreakpointManager.OnExternalHit"/> scans breakpoints
    /// whose <see cref="SearchPredicateDto"/> tree contains this DTO and fires them
    /// when the tag matches.
    /// </summary>
    public sealed class ExternalHitTagPredicateDto : SearchPredicateDto
    {
        /// <summary>
        /// Opaque string tag that must match the first argument of
        /// <see cref="IDataBreakpointManager.OnExternalHit"/>.
        /// Convention: Blueprint node probes use the raw <c>nodeId</c> string;
        /// future Slice 1 surfaces may use other prefixes.
        /// </summary>
        public string Tag { get; set; } = string.Empty;
    }
```

---

## 2. `BreakpointTypes.cs` — Add `SourceElementId` to `Breakpoint`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs`

Add after the `DisplayName` property:
```csharp
    /// <summary>
    /// Optional graph-element identity for auto-synthesised breakpoints.
    /// Set to the <c>VisualId</c> of the BTree/HSM/Blueprint node that was
    /// right-clicked when synthesising this breakpoint.
    /// The gutter renderers compare this value to <c>node.VisualId</c> to draw
    /// the red dot without querying the Slice 1 session.
    /// </summary>
    public Guid? SourceElementId { get; init; }
```

---

## 3. `PredicateCompiler.cs` — Add stub for `ExternalHitTagPredicateDto`

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`

In the `switch` statement inside `CompileComponentPredicate` (or the equivalent dispatch method), add **before** the existing `case TraceBufferScanPredicateDto`:

```csharp
case ExternalHitTagPredicateDto _:
    // ExternalHitTag predicates are never evaluated via the component-data path.
    // DataBreakpointManager.OnExternalHit handles them directly.
    return static (_, _) => false;
```

---

## 4. `DataBreakpointManager.cs` — `_externalHitPredicates`, `TryMountDelegate`, `UnmountDelegate`, `OnExternalHit`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

### 4a. Add field after `_lifecycleTrackers`:
```csharp
    // External-hit tag predicates: tag → list of (BreakpointId, optional remaining delegate).
    // Populated for ExternalHitTagPredicateDto conditions and for CompoundPredicateDto[And]
    // that contain at least one ExternalHitTagPredicateDto child.
    // The remaining delegate evaluates the non-ExternalHitTag children against the entity.
    private readonly Dictionary<string, List<(BreakpointId id, Func<EntityRepository, Entity, bool>? remainingDelegate)>>
        _externalHitPredicates = new(StringComparer.Ordinal);
```

### 4b. Update `TryMountDelegate`

**Before** the existing `case CompoundPredicateDto _:` (or wherever CompoundPredicateDto is handled), insert a more-specific guard pattern:

```csharp
case CompoundPredicateDto compound when HasExternalHitTag(compound):
{
    // Collect tags from ExternalHitTagPredicateDto children
    var externalTags = compound.Conditions
        .OfType<ExternalHitTagPredicateDto>()
        .Select(e => e.Tag)
        .ToList();

    // Build remaining predicate from non-ExternalHitTag children
    var remainingConditions = compound.Conditions
        .Where(c => c is not ExternalHitTagPredicateDto)
        .ToList();

    Func<EntityRepository, Entity, bool>? remainingDelegate = null;
    if (remainingConditions.Count > 0 && _predicateCompiler != null)
    {
        SearchPredicateDto remainingPredicate = remainingConditions.Count == 1
            ? remainingConditions[0]
            : new CompoundPredicateDto { Operator = compound.Operator, Conditions = remainingConditions };
        remainingDelegate = _predicateCompiler.CompileComponentPredicate(remainingPredicate);
    }

    foreach (var tag in externalTags)
    {
        if (!_externalHitPredicates.TryGetValue(tag, out var tagList))
        {
            tagList = new List<(BreakpointId, Func<EntityRepository, Entity, bool>?)>();
            _externalHitPredicates[tag] = tagList;
        }
        tagList.Add((id, remainingDelegate));
    }
    // Do NOT fall through to the component-predicate path;
    // ExternalHitTag compounds are evaluated only via OnExternalHit.
    break;
}

case ExternalHitTagPredicateDto tagDto:
{
    if (!_externalHitPredicates.TryGetValue(tagDto.Tag, out var tagListStandalone))
    {
        tagListStandalone = new List<(BreakpointId, Func<EntityRepository, Entity, bool>?)>();
        _externalHitPredicates[tagDto.Tag] = tagListStandalone;
    }
    tagListStandalone.Add((id, null)); // null = always fires when tag matches
    break;
}
```

Add the helper at the bottom of the class:
```csharp
private static bool HasExternalHitTag(CompoundPredicateDto compound)
    => compound.Conditions.Any(c => c is ExternalHitTagPredicateDto);
```

### 4c. Update `UnmountDelegate`

At the end of `UnmountDelegate`, add:
```csharp
    // Remove from external-hit registrations
    foreach (var tagList in _externalHitPredicates.Values)
        tagList.RemoveAll(entry => entry.id == id);
```

### 4d. Implement `OnExternalHit`

Replace the P7 stub with:
```csharp
/// <inheritdoc/>
public void OnExternalHit(string tag, Entity entity)
{
    bool anyFired = false;

    if (_externalHitPredicates.TryGetValue(tag, out var registrations))
    {
        foreach (var (bpId, remainingDelegate) in registrations)
        {
            if (!_breakpoints.TryGetValue(bpId, out var bp)) continue;
            if (!bp.Enabled) continue;

            bool shouldFire = remainingDelegate == null
                || remainingDelegate(_liveRepo, entity);
            if (shouldFire)
            {
                OnHit(bp, entity);
                anyFired = true;
            }
        }
    }

    // If no universal breakpoint fired, still perform the triple-buffer rewind
    // so Slice 1 Blueprint probe-driven hits get pre-execution inspection.
    if (!anyFired && !_isPaused)
    {
        _postTickSnapshot.SyncFrom(_liveRepo);
        _liveRepo.SyncFrom(_preTickSnapshot);
        _timeController.RequestPause();
        _isPaused = true;
        _pausedTick = _preTickSnapshot.GlobalVersion;
        OnPauseStateChanged?.Invoke(true);
    }
}
```

---

## 5. Project file updates

### 5a. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Hrot.BTree.Editor.csproj`
Add inside the existing `<ItemGroup>` that has ProjectReferences:
```xml
<ProjectReference Include="..\..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
<ProjectReference Include="..\..\..\..\FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj" />
```

### 5b. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj`
Add inside the existing `<ItemGroup>` that has ProjectReferences:
```xml
<ProjectReference Include="..\..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
<ProjectReference Include="..\..\..\..\FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj" />
```

### 5c. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj`
Add inside the existing `<ItemGroup>` that has ProjectReferences:
```xml
<ProjectReference Include="..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
```

### 5d. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj`
Add to the existing ProjectReferences ItemGroup:
```xml
<ProjectReference Include="..\..\Subsystems\AI\Hrot.BTree.Editor\Hrot.BTree.Editor.csproj" />
<ProjectReference Include="..\..\Subsystems\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj" />
```
(Blueprints.Editor is already referenced.)

---

## 6. `BTreeBreakpointMenuPopulator.cs` — NEW file

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeBreakpointMenuPopulator.cs`

```csharp
using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.BTree.Editor.Debug;

/// <summary>
/// Populates the BTree canvas node context-menu with "Add Breakpoint" and
/// "Add Conditional Data Breakpoint..." items.
/// Each item synthesises a <see cref="SearchPredicateDto"/> and registers it
/// with <see cref="IDataBreakpointManager"/>.
/// </summary>
public static class BTreeBreakpointMenuPopulator
{
    /// <summary>
    /// Adds breakpoint items to <paramref name="builder"/> for the given node.
    /// </summary>
    /// <param name="node">The right-clicked editor node.</param>
    /// <param name="builder">Context-menu builder; items are appended to the current level.</param>
    /// <param name="manager">Universal breakpoint manager to receive synthesised breakpoints.</param>
    /// <param name="onOpenConditionalInspector">
    /// Optional callback invoked when "Add Conditional Data Breakpoint..." is chosen.
    /// Receives the newly registered <see cref="BreakpointId"/> and the synthesised
    /// <see cref="CompoundPredicateDto"/> so the caller can open the details inspector.
    /// </param>
    public static void PopulateMenu(
        Model.BTreeEditorNode node,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        var submenu = builder.BeginSubmenu("Add Breakpoint");

        submenu.AddItem("Break on Activation (Enter)", () =>
        {
            var pred = BuildEnterPredicate(node);
            manager.AddBreakpoint(pred,
                displayName: $"BTree: {node.DisplayLabel} — Enter",
                sourceElementId: node.VisualId);
        });

        submenu.AddItem("Break on Completion (Exit)", () =>
        {
            var pred = BuildExitPredicate(node);
            manager.AddBreakpoint(pred,
                displayName: $"BTree: {node.DisplayLabel} — Exit",
                sourceElementId: node.VisualId);
        });

        submenu.AddItem("Break on Interruption (Abort)", () =>
        {
            var pred = BuildAbortPredicate(node);
            manager.AddBreakpoint(pred,
                displayName: $"BTree: {node.DisplayLabel} — Abort",
                sourceElementId: node.VisualId);
        });

        builder.EndSubmenu();

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            var compound = BuildConditionalCompound(node);
            var id = manager.AddBreakpoint(compound,
                displayName: $"BTree: {node.DisplayLabel} — Conditional",
                sourceElementId: node.VisualId);
            onOpenConditionalInspector?.Invoke(id, compound);
        });
    }

    // ── Predicate factories ──────────────────────────────────────────────────

    private static TraceBufferScanPredicateDto BuildEnterPredicate(Model.BTreeEditorNode node)
        => new TraceBufferScanPredicateDto
        {
            ComponentType    = typeof(BTreeTraceWorkingMemory1024),
            OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
            IndexField       = (ushort)node.KernelBlobIndex,
            MatchIndexField  = true,
            StatusField      = (byte)NodeStatus.Running,
            MatchStatusField = true,
        };

    private static CompoundPredicateDto BuildExitPredicate(Model.BTreeEditorNode node)
        => new CompoundPredicateDto
        {
            Operator = LogicalOperator.Or,
            Conditions = new List<SearchPredicateDto>
            {
                new TraceBufferScanPredicateDto
                {
                    ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                    OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                    IndexField       = (ushort)node.KernelBlobIndex,
                    MatchIndexField  = true,
                    StatusField      = (byte)NodeStatus.Success,
                    MatchStatusField = true,
                },
                new TraceBufferScanPredicateDto
                {
                    ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                    OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                    IndexField       = (ushort)node.KernelBlobIndex,
                    MatchIndexField  = true,
                    StatusField      = (byte)NodeStatus.Failure,
                    MatchStatusField = true,
                },
            },
        };

    private static TraceBufferScanPredicateDto BuildAbortPredicate(Model.BTreeEditorNode node)
        => new TraceBufferScanPredicateDto
        {
            ComponentType   = typeof(BTreeTraceWorkingMemory1024),
            OpCode          = (byte)BTreeTraceOpCode.ScopePopped,
            MatchIndexField = false,
        };

    /// <summary>
    /// Synthesises a Compound[And] whose Branch A is the enter-scan (read-only)
    /// and Branch B is an empty <see cref="BehaviorParamPredicateDto"/> (editable).
    /// </summary>
    private static CompoundPredicateDto BuildConditionalCompound(Model.BTreeEditorNode node)
        => new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto>
            {
                BuildEnterPredicate(node),       // Branch A — read-only
                new BehaviorParamPredicateDto(), // Branch B — editable placeholder
            },
            ReadOnlyChildIndices = new List<int> { 0 },
        };
}
```

**IMPORTANT**: `IDataBreakpointManager.AddBreakpoint` signature is:
```csharp
BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                            int occurrenceThreshold = 0, string displayName = "");
```
There is NO `sourceElementId` parameter on this method yet. You must **add** it:
```csharp
BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                            int occurrenceThreshold = 0, string displayName = "",
                            Guid? sourceElementId = null);
```

Update `IDataBreakpointManager` in `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` AND update `DataBreakpointManager.AddBreakpoint(...)` to accept and store the `sourceElementId` in the created `Breakpoint` record.

---

## 7. `HsmBreakpointMenuPopulator.cs` — NEW file

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmBreakpointMenuPopulator.cs`

Mirror of P7T1 but targeting `HsmTraceWorkingMemory1024` and HSM state nodes:

```csharp
using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Hsm.Editor.Debug;

/// <summary>
/// Populates the HSM canvas state/transition context-menu with "Add Breakpoint" and
/// "Add Conditional Data Breakpoint..." items.
/// </summary>
public static class HsmBreakpointMenuPopulator
{
    /// <summary>
    /// Adds breakpoint items to <paramref name="builder"/> for the given state node.
    /// </summary>
    public static void PopulateStateMenu(
        Model.StateNode state,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        var submenu = builder.BeginSubmenu("Add Breakpoint");

        submenu.AddItem("Break on Activation (Enter)", () =>
        {
            var pred = BuildEnterPredicate(state);
            manager.AddBreakpoint(pred,
                displayName: $"HSM: {state.Name} — Enter",
                sourceElementId: state.StableId);
        });

        submenu.AddItem("Break on Completion (Exit)", () =>
        {
            var pred = BuildExitPredicate(state);
            manager.AddBreakpoint(pred,
                displayName: $"HSM: {state.Name} — Exit",
                sourceElementId: state.StableId);
        });

        builder.EndSubmenu();

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            var compound = BuildConditionalCompound(state);
            var id = manager.AddBreakpoint(compound,
                displayName: $"HSM: {state.Name} — Conditional",
                sourceElementId: state.StableId);
            onOpenConditionalInspector?.Invoke(id, compound);
        });
    }

    // ── Predicate factories ──────────────────────────────────────────────────

    private static TraceBufferScanPredicateDto BuildEnterPredicate(Model.StateNode state)
        => new TraceBufferScanPredicateDto
        {
            ComponentType   = typeof(HsmTraceWorkingMemory1024),
            OpCode          = (byte)TraceOpCode.StateEnter,
            IndexField      = state.FlatIndex,
            MatchIndexField = true,
        };

    private static TraceBufferScanPredicateDto BuildExitPredicate(Model.StateNode state)
        => new TraceBufferScanPredicateDto
        {
            ComponentType   = typeof(HsmTraceWorkingMemory1024),
            OpCode          = (byte)TraceOpCode.StateExit,
            IndexField      = state.FlatIndex,
            MatchIndexField = true,
        };

    private static CompoundPredicateDto BuildConditionalCompound(Model.StateNode state)
        => new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto>
            {
                BuildEnterPredicate(state),      // Branch A — read-only
                new BehaviorParamPredicateDto(), // Branch B — editable placeholder
            },
            ReadOnlyChildIndices = new List<int> { 0 },
        };
}
```

---

## 8. `BTreeBreakpointGutterRenderer.cs` — Add `IDataBreakpointManager` integration

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs`

### 8a. Add using + field:
```csharp
using Hrot.Diagnostics.Breakpoints;
// ...
private IDataBreakpointManager? _manager;
```

### 8b. Add setter:
```csharp
public void SetManager(IDataBreakpointManager? manager) => _manager = manager;
```

### 8c. In `Render()`, after the existing loop over `_session.GetBreakpoints()`, add a second loop:
```csharp
        // Universal breakpoints with SourceElementId (synthesised via context menu)
        if (_manager != null)
        {
            foreach (var bp in _manager.AllBreakpoints)
            {
                if (!bp.Enabled) continue;
                if (bp.SourceElementId == null) continue;

                var node = _asset.FindNode(bp.SourceElementId.Value);
                if (node is null) continue;

                var screenPos = ctx.Viewport.GraphToScreen(node.Position);
                var center    = screenPos + new Vector2(-8f, 8f) * ctx.Zoom;
                float radius  = 5f * ctx.Zoom;
                var color     = new Vector4(0.9f, 0.15f, 0.15f, 1.0f);
                ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(color));
            }
        }
```

---

## 9. `HsmBreakpointGutterRenderer.cs` — Add `IDataBreakpointManager` integration

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs`

Same pattern as §8:

### 9a. Add using + field:
```csharp
using Hrot.Diagnostics.Breakpoints;
// ...
private IDataBreakpointManager? _manager;
```

### 9b. Add setter:
```csharp
public void SetManager(IDataBreakpointManager? manager) => _manager = manager;
```

### 9c. In `Render()`, after the existing loop, add:
```csharp
        // Universal breakpoints with SourceElementId (synthesised via context menu)
        if (_manager != null)
        {
            foreach (var bp in _manager.AllBreakpoints)
            {
                if (!bp.Enabled) continue;
                if (bp.SourceElementId == null) continue;

                // Try state first, then transition
                var state = _asset.FindStateByStableId(bp.SourceElementId.Value);
                if (state is not null)
                {
                    var screenPos   = ctx.Viewport.GraphToScreen(state.Position);
                    var stateCenter = screenPos + new Vector2(-8f, 8f) * ctx.Zoom;
                    float stateRad  = 5f * ctx.Zoom;
                    ctx.DrawList.AddCircleFilled(stateCenter, stateRad,
                        ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.15f, 1.0f)));
                    LastStateDotCount++;
                    continue;
                }

                var trans = _asset.FindTransitionByVisualId(bp.SourceElementId.Value);
                if (trans is not null)
                {
                    var midGraph    = (trans.Source.Position + trans.Target.Position) * 0.5f;
                    var transCenter = ctx.Viewport.GraphToScreen(midGraph) + new Vector2(-8f, 8f) * ctx.Zoom;
                    float transRad  = 5f * ctx.Zoom;
                    ctx.DrawList.AddCircleFilled(transCenter, transRad,
                        ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.15f, 1.0f)));
                    LastTransitionDotCount++;
                }
            }
        }
```

Also update `CountBreakpoints()` to include manager breakpoints for test use — add a second pass mirroring the above logic (without the ImGui draw calls).

---

## 10. `BlueprintDebugSession.cs` — Wire `IDataBreakpointManager`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`

### 10a. Add using:
```csharp
using Hrot.Diagnostics.Breakpoints;
```

### 10b. Add field (after `_entityFilter`):
```csharp
    private IDataBreakpointManager? _dataBreakpointManager;
```

### 10c. Add setter method (after `GetEntityFilter()`):
```csharp
    /// <summary>
    /// Wires the Universal Breakpoint manager. When set, Slice 1 probe-driven
    /// breakpoint hits are routed through
    /// <see cref="IDataBreakpointManager.OnExternalHit"/> instead of calling
    /// <see cref="IEngineDebugTimeController.RequestPause"/> directly, so the
    /// triple-buffer rewind provides pre-execution state to the inspector.
    /// </summary>
    public void SetDataBreakpointManager(IDataBreakpointManager? manager)
        => _dataBreakpointManager = manager;
```

### 10d. In `HandleBreakpointHit` — replace the two `_timeController.RequestPause()` call sites

**First site** (inside the `if (bp.Id.Value != 0 && _breakpoints.ContainsKey(bp.Id))` branch):
```csharp
// OLD:
_timeController.RequestPause();
OnBreakpointHit?.Invoke(new BreakpointHit(...));

// NEW:
if (_dataBreakpointManager != null)
    _dataBreakpointManager.OnExternalHit(nodeId, self);
else
    _timeController.RequestPause();
OnBreakpointHit?.Invoke(new BreakpointHit(...));
```

**Second site** (the `else` branch):
```csharp
// OLD:
_timeController.RequestPause();
OnBreakpointHit?.Invoke(new BreakpointHit(...));

// NEW:
if (_dataBreakpointManager != null)
    _dataBreakpointManager.OnExternalHit(nodeId, self);
else
    _timeController.RequestPause();
OnBreakpointHit?.Invoke(new BreakpointHit(...));
```

---

## 11. `IDataBreakpointManager.cs` — Add `sourceElementId` parameter to `AddBreakpoint`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`

Locate the `AddBreakpoint` declaration and add an optional parameter:
```csharp
BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                            int occurrenceThreshold = 0, string displayName = "",
                            Guid? sourceElementId = null);
```

Update `DataBreakpointManager.AddBreakpoint(...)` to accept and store it:
```csharp
public BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                                   int occurrenceThreshold = 0, string displayName = "",
                                   Guid? sourceElementId = null)
{
    // ... existing id/count logic ...
    var bp = new Breakpoint
    {
        Id                  = id,
        Condition           = condition,
        FilterEntity        = filter,
        OccurrenceThreshold = occurrenceThreshold > 0 ? occurrenceThreshold : 1,
        Enabled             = true,
        DisplayName         = displayName,
        SourceElementId     = sourceElementId,
    };
    // ... rest unchanged ...
}
```

---

## 12. Tests — `BTreeContextMenuTests.cs`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BTreeContextMenuTests.cs`

### Required test infrastructure:
- A `RecordingContextMenuBuilder` stub that captures callbacks (per level/submenu).
- A `FakeCanvasRenderContext` stub for gutter-renderer tests.

### `RecordingContextMenuBuilder`:
```csharp
file sealed class RecordingContextMenuBuilder : IContextMenuBuilder
{
    private readonly List<(string Label, Action Callback)> _items = new();
    private readonly Dictionary<string, RecordingContextMenuBuilder> _submenus = new();

    public IReadOnlyList<(string Label, Action Callback)> Items => _items;
    public IReadOnlyDictionary<string, RecordingContextMenuBuilder> Submenus => _submenus;

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add((label, callback));

    public IContextMenuBuilder BeginSubmenu(string label)
    {
        var sub = new RecordingContextMenuBuilder();
        _submenus[label] = sub;
        return sub;
    }

    public void EndSubmenu() { }
    public void AddSeparator() { }

    public Action? GetCallback(string label)
        => _items.FirstOrDefault(i => i.Label == label).Callback;

    public Action? GetSubmenuCallback(string submenuLabel, string itemLabel)
        => _submenus.TryGetValue(submenuLabel, out var sub)
            ? sub.GetCallback(itemLabel)
            : null;
}
```

### Tests in `[Collection("ComponentRegistry")]`:

```csharp
[Collection("ComponentRegistry")]
public sealed class BTreeContextMenuTests : IDisposable
{
    private readonly EntityRepository _live, _preTick, _postTick;
    private readonly FakeTimeController _clock;
    private readonly DataBreakpointManager _manager;

    public BTreeContextMenuTests()
    {
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<BTreeTraceWorkingMemory1024>();
        _live     = new EntityRepository();
        _preTick  = new EntityRepository();
        _clock    = new FakeTimeController();
        var snap  = new DebugSnapshotProvider(_preTick, _live);
        _manager  = new DataBreakpointManager(_live, _preTick, snap, _clock,
                        new PredicateCompiler());
    }

    public void Dispose()
    {
        _live.Dispose(); _preTick.Dispose();
        ComponentTypeRegistry.Clear();
    }

    private static BTreeEditorNode MakeNode(int kernelBlobIndex = 3)
        => new BTreeEditorNode
        {
            VisualId       = Guid.NewGuid(),
            KernelBlobIndex = kernelBlobIndex,
            DisplayLabel   = "TestAction",
        };

    [Fact]
    public void BTreeContextMenu_AddBreakOnActivation_RegistersWithManager()
    {
        var node    = MakeNode(kernelBlobIndex: 5);
        var builder = new RecordingContextMenuBuilder();

        BTreeBreakpointMenuPopulator.PopulateMenu(node, builder, _manager);

        // Invoke "Break on Activation (Enter)"
        var callback = builder.GetSubmenuCallback("Add Breakpoint", "Break on Activation (Enter)");
        Assert.NotNull(callback);
        callback!();

        // Manager should have exactly one breakpoint
        var bps = _manager.AllBreakpoints;
        Assert.Single(bps);
        var bp = bps[0];

        // Condition must be TraceBufferScanPredicateDto targeting Enter/Running
        var scan = Assert.IsType<TraceBufferScanPredicateDto>(bp.Condition);
        Assert.Equal(typeof(BTreeTraceWorkingMemory1024), scan.ComponentType);
        Assert.Equal((byte)BTreeTraceOpCode.NodeEvaluated, scan.OpCode);
        Assert.True(scan.MatchIndexField);
        Assert.Equal((ushort)5, scan.IndexField);
        Assert.True(scan.MatchStatusField);
        Assert.Equal((byte)NodeStatus.Running, scan.StatusField);

        // SourceElementId must match the node's VisualId
        Assert.Equal(node.VisualId, bp.SourceElementId);
    }

    [Fact]
    public void BTreeContextMenu_AddConditional_OpensDetailsInspectorWithEditReadOnlyA()
    {
        var node    = MakeNode(kernelBlobIndex: 7);
        var builder = new RecordingContextMenuBuilder();

        BreakpointId? openedId = null;
        SearchPredicateDto? openedPredicate = null;

        BTreeBreakpointMenuPopulator.PopulateMenu(node, builder, _manager,
            onOpenConditionalInspector: (id, pred) =>
            {
                openedId        = id;
                openedPredicate = pred;
            });

        var callback = builder.GetCallback("Add Conditional Data Breakpoint...");
        Assert.NotNull(callback);
        callback!();

        // Inspector callback was invoked
        Assert.NotNull(openedId);
        Assert.NotNull(openedPredicate);

        // Synthesised compound
        var compound = Assert.IsType<CompoundPredicateDto>(openedPredicate);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(2, compound.Conditions.Count);

        // Branch A is a trace-buffer scan (read-only)
        Assert.IsType<TraceBufferScanPredicateDto>(compound.Conditions[0]);
        Assert.Contains(0, compound.ReadOnlyChildIndices); // index 0 is EditReadOnly

        // Branch B is editable (BehaviorParamPredicateDto, not in ReadOnlyChildIndices)
        Assert.IsType<BehaviorParamPredicateDto>(compound.Conditions[1]);
        Assert.DoesNotContain(1, compound.ReadOnlyChildIndices);
    }

    [Fact]
    public void BTreeGutterRenderer_ReadsManagerForBreakpoints()
    {
        // Register a universal breakpoint with SourceElementId = some node VisualId
        var asset  = BuildMinimalAsset(out var node);
        var bpId   = _manager.AddBreakpoint(
            new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(BTreeTraceWorkingMemory1024),
                OpCode          = (byte)BTreeTraceOpCode.NodeEvaluated,
                MatchIndexField = false,
            },
            displayName: "Test",
            sourceElementId: node.VisualId);

        var renderer = new BTreeBreakpointGutterRenderer(asset);
        renderer.SetManager(_manager);

        // Use a counting render context
        int dotCount = 0;
        var ctx = new CountingCanvasRenderContext(onCircle: _ => dotCount++);
        renderer.Render(ctx);

        Assert.Equal(1, dotCount); // one dot for the universal breakpoint
    }

    // Helper: build a minimal BehaviorTreeAsset with one node
    private static BehaviorTreeAsset BuildMinimalAsset(out BTreeEditorNode node)
    {
        var assetId = Guid.NewGuid();
        node = new BTreeEditorNode
        {
            VisualId        = Guid.NewGuid(),
            KernelBlobIndex = 1,
            DisplayLabel    = "Root",
            Position        = System.Numerics.Vector2.Zero,
        };
        var nodes = new List<BTreeEditorNode> { node };
        return new BehaviorTreeAsset(
            assetId,
            name: "TestTree",
            sourceFilePath: "",
            isEditorOwned: false,
            nodes: nodes,
            pills: new List<BTreeEditorPill>(),
            layoutJson: null);
    }
}
```

**Note on `BuildMinimalAsset`**: The `BehaviorTreeAsset` constructor signature must be verified against the actual source. Look at `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` for the actual constructor parameters and adjust accordingly. If there is no public constructor that accepts a node list directly, use the internal constructor or use `HsmAssetProjector` equivalent.

**Note on `CountingCanvasRenderContext`**: You need a minimal fake `ICanvasRenderContext` that counts calls to `DrawList.AddCircleFilled`. Look at how existing tests create fake render contexts (check `Hrot.BTree.Editor.Tests` for examples). If there are no existing fakes, create a minimal inline stub that implements `ICanvasRenderContext`, providing a stub `IDrawList` whose `AddCircleFilled` increments a counter.

---

## 13. Tests — `HsmContextMenuTests.cs`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/HsmContextMenuTests.cs`

Mirror of `BTreeContextMenuTests.cs` for HSM. Key differences:

```csharp
[Collection("ComponentRegistry")]
public sealed class HsmContextMenuTests : IDisposable
{
    // Setup: register HsmTraceWorkingMemory1024, create manager with PredicateCompiler

    [Fact]
    public void HsmContextMenu_AddBreakOnActivation_RegistersWithManager()
    {
        var state = new StateNode("TestState") { FlatIndex = 4 };
        var builder = new RecordingContextMenuBuilder();

        HsmBreakpointMenuPopulator.PopulateStateMenu(state, builder, _manager);

        var callback = builder.GetSubmenuCallback("Add Breakpoint", "Break on Activation (Enter)");
        Assert.NotNull(callback);
        callback!();

        var bps = _manager.AllBreakpoints;
        Assert.Single(bps);
        var bp = bps[0];

        var scan = Assert.IsType<TraceBufferScanPredicateDto>(bp.Condition);
        Assert.Equal(typeof(HsmTraceWorkingMemory1024), scan.ComponentType);
        Assert.Equal((byte)TraceOpCode.StateEnter, scan.OpCode);
        Assert.True(scan.MatchIndexField);
        Assert.Equal((ushort)4, scan.IndexField);
        Assert.Equal(state.StableId, bp.SourceElementId);
    }

    [Fact]
    public void HsmContextMenu_AddConditional_OpensDetailsInspectorWithEditReadOnlyA()
    {
        var state   = new StateNode("TestState") { FlatIndex = 2 };
        var builder = new RecordingContextMenuBuilder();

        BreakpointId? openedId = null;
        SearchPredicateDto? openedPredicate = null;

        HsmBreakpointMenuPopulator.PopulateStateMenu(state, builder, _manager,
            onOpenConditionalInspector: (id, pred) => { openedId = id; openedPredicate = pred; });

        var callback = builder.GetCallback("Add Conditional Data Breakpoint...");
        Assert.NotNull(callback); callback!();

        Assert.NotNull(openedPredicate);
        var compound = Assert.IsType<CompoundPredicateDto>(openedPredicate);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.IsType<TraceBufferScanPredicateDto>(compound.Conditions[0]);
        Assert.Contains(0, compound.ReadOnlyChildIndices);
        Assert.IsType<BehaviorParamPredicateDto>(compound.Conditions[1]);
        Assert.DoesNotContain(1, compound.ReadOnlyChildIndices);
    }

    [Fact]
    public void HsmGutterRenderer_ReadsManagerForBreakpoints()
    {
        var state   = new StateNode("S1") { FlatIndex = 1, Position = System.Numerics.Vector2.Zero };
        var stableId = state.StableId;

        _manager.AddBreakpoint(
            new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(HsmTraceWorkingMemory1024),
                OpCode          = (byte)TraceOpCode.StateEnter,
                MatchIndexField = false,
            },
            displayName: "Test",
            sourceElementId: stableId);

        var asset    = BuildMinimalHsmAsset(state);
        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetManager(_manager);

        var (stateDots, _) = renderer.CountBreakpoints();
        // CountBreakpoints should also count manager breakpoints with matching SourceElementId
        Assert.Equal(1, stateDots);
    }
}
```

**Note on `HsmGutterRenderer_ReadsManagerForBreakpoints`**: The `CountBreakpoints()` method must be updated to also count manager breakpoints (not just session breakpoints). Specifically, `CountBreakpoints` should include manager breakpoints with non-null `SourceElementId` matching a state or transition. Update it in the implementation (§9c says to update `CountBreakpoints()` too).

**Note on `BuildMinimalHsmAsset`**: Create a helper that builds a minimal `HsmAsset` with the given state. Look at `HsmAsset`'s constructor in `Hrot.Hsm.Editor.Model.HsmAsset.cs`. If the constructor is internal, you'll need to use `HsmAssetProjector` or find another way. Check the existing `Hrot.BTree.Editor.Tests` or HSM tests for asset construction helpers.

---

## 14. Tests — `BlueprintContextMenuTests.cs`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs`

```csharp
[Collection("ComponentRegistry")]
public sealed class BlueprintContextMenuTests : IDisposable
{
    private readonly EntityRepository _live, _preTick;
    private readonly FakeTimeController _clock;
    private readonly DataBreakpointManager _manager;
    private readonly BlueprintDebugSession _session;

    public BlueprintContextMenuTests()
    {
        ComponentTypeRegistry.Clear();
        _live    = new EntityRepository();
        _preTick = new EntityRepository();
        _clock   = new FakeTimeController();
        var snap = new DebugSnapshotProvider(_preTick, _live);
        _manager = new DataBreakpointManager(_live, _preTick, snap, _clock,
                       new PredicateCompiler());

        var registry   = new BlueprintRegistry();
        var fakeView   = new FakeSimulationView(_live);
        _session = new BlueprintDebugSession(registry, fakeView, _clock);
        _session.SetDataBreakpointManager(_manager);
    }

    public void Dispose()
    {
        _live.Dispose(); _preTick.Dispose();
        ComponentTypeRegistry.Clear();
    }

    [Fact]
    public void Blueprint_NodeBP_RoutesToManager_TripleBufferRewindApplied()
    {
        var assetId = Guid.NewGuid();
        var nodeId  = "node-abc";

        // Register a Slice 1 breakpoint
        _session.SetBreakpoint(assetId, assetId, nodeId);

        // Pre-tick snapshot and live repo diverge
        // (Manager.PreTickSnapshot is internal; we just verify IsPaused triggers)
        var entity = _live.Create();
        _live.RegisterComponent<HealthComponent>();
        _preTick.RegisterComponent<HealthComponent>();
        _live.AddComponent(entity, new HealthComponent { Value = 50 });
        // Snapshot the current live into _preTick
        _preTick.SyncFrom(_live);
        // Mutate live to simulate tick advance
        _live.GetComponentRW<HealthComponent>(entity).Value = 10;

        // Fire the probe
        _session.OnNodeEnter(entity, nodeId);

        // Manager must be paused (triple-buffer rewind applied)
        Assert.True(_manager.IsPaused);
    }

    [Fact]
    public void Blueprint_AddConditional_SynthesizesCompoundWithReadOnlyA()
    {
        var assetId = Guid.NewGuid();
        var nodeId  = "node-xyz";
        var builder = new RecordingContextMenuBuilder();

        BreakpointId? openedId        = null;
        SearchPredicateDto? openedPred = null;

        // A blueprint populator would call AddItem("Add Conditional Data Breakpoint...", ...)
        // Here we directly invoke the synthesis logic as a unit test.
        BlueprintBreakpointMenuPopulator.PopulateNodeMenu(
            nodeId, assetId, builder, _manager,
            onOpenConditionalInspector: (id, pred) => { openedId = id; openedPred = pred; });

        var callback = builder.GetCallback("Add Conditional Data Breakpoint...");
        Assert.NotNull(callback); callback!();

        Assert.NotNull(openedPred);
        var compound = Assert.IsType<CompoundPredicateDto>(openedPred);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(2, compound.Conditions.Count);

        // Branch A: ExternalHitTagPredicateDto (read-only)
        var branchA = Assert.IsType<ExternalHitTagPredicateDto>(compound.Conditions[0]);
        Assert.Equal(nodeId, branchA.Tag);
        Assert.Contains(0, compound.ReadOnlyChildIndices);

        // Branch B: BlueprintVariablePredicateDto (editable)
        Assert.IsType<BlueprintVariablePredicateDto>(compound.Conditions[1]);
        Assert.DoesNotContain(1, compound.ReadOnlyChildIndices);
    }
}
```

**This test requires a new `BlueprintBreakpointMenuPopulator` class:**

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintBreakpointMenuPopulator.cs`

```csharp
using System;
using System.Collections.Generic;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Populates the Blueprint canvas node context-menu with "Add Conditional Data Breakpoint..."
/// which synthesises a compound [ExternalHitTag, BlueprintVariablePredicateDto].
/// The existing "Add Breakpoint" item (Slice 1 probe path) is left unchanged.
/// </summary>
public static class BlueprintBreakpointMenuPopulator
{
    /// <summary>
    /// Adds the "Add Conditional Data Breakpoint..." item to <paramref name="builder"/>.
    /// </summary>
    /// <param name="nodeId">The Blueprint node's string identity (used as the external-hit tag).</param>
    /// <param name="assetId">The Blueprint asset GUID (stored in the synthesised DTO).</param>
    /// <param name="builder">Context-menu builder.</param>
    /// <param name="manager">Universal breakpoint manager.</param>
    /// <param name="onOpenConditionalInspector">
    /// Callback invoked with the new <see cref="BreakpointId"/> and compound predicate.
    /// </param>
    public static void PopulateNodeMenu(
        string nodeId,
        Guid assetId,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            var compound = new CompoundPredicateDto
            {
                Operator = LogicalOperator.And,
                Conditions = new List<SearchPredicateDto>
                {
                    new ExternalHitTagPredicateDto { Tag = nodeId },   // Branch A — read-only
                    new BlueprintVariablePredicateDto                  // Branch B — editable
                    {
                        TargetBlueprintAssetId = assetId,
                    },
                },
                ReadOnlyChildIndices = new List<int> { 0 },
            };

            var id = manager.AddBreakpoint(compound,
                displayName: $"Blueprint Conditional: {nodeId}");
            onOpenConditionalInspector?.Invoke(id, compound);
        });
    }
}
```

---

## 15. Tests — `ExternalHitTagTests.cs`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ExternalHitTagTests.cs`

```csharp
[Collection("ComponentRegistry")]
public sealed class ExternalHitTagTests : IDisposable
{
    private readonly EntityRepository _live, _preTick;
    private readonly FakeTimeController _clock;
    private readonly DataBreakpointManager _manager;
    private readonly PredicateCompiler _compiler;

    public ExternalHitTagTests()
    {
        ComponentTypeRegistry.Clear();
        _live    = new EntityRepository();
        _preTick = new EntityRepository();
        _clock   = new FakeTimeController();
        var snap = new DebugSnapshotProvider(_preTick, _live);
        _compiler = new PredicateCompiler();
        _manager  = new DataBreakpointManager(_live, _preTick, snap, _clock, _compiler);
    }

    public void Dispose()
    {
        _live.Dispose(); _preTick.Dispose();
        ComponentTypeRegistry.Clear();
    }

    [Fact]
    public void ExternalHitTag_Standalone_TriggersOnTagMatch()
    {
        var entity = _live.Create();

        Breakpoint? hitBp = null;
        Entity hitEntity  = default;
        _manager.OnBreakpointHit += (bp, e) => { hitBp = bp; hitEntity = e; };

        var id = _manager.AddBreakpoint(
            new ExternalHitTagPredicateDto { Tag = "BP:node-1" },
            displayName: "ExternalTag test");

        _manager.OnExternalHit("BP:node-1", entity);

        Assert.NotNull(hitBp);
        Assert.Equal(entity, hitEntity);
        Assert.Equal(id, hitBp!.Id);
    }

    [Fact]
    public void ExternalHitTag_WrongTag_DoesNotFire()
    {
        var entity = _live.Create();

        bool fired = false;
        _manager.OnBreakpointHit += (_, _) => fired = true;

        _manager.AddBreakpoint(
            new ExternalHitTagPredicateDto { Tag = "BP:node-1" });

        _manager.OnExternalHit("BP:node-DIFFERENT", entity);

        Assert.False(fired);
    }

    [Fact]
    public void ExternalHitTag_InCompoundAnd_EvaluatesRemainingChildrenAgainstEntity()
    {
        // Setup: register Health component; entity with Health=0 → hit, Health=5 → no hit
        ComponentTypeRegistry.Register<HealthComponent>();
        _live.RegisterComponent<HealthComponent>();
        _preTick.RegisterComponent<HealthComponent>();

        var entity = _live.Create();
        _live.AddComponent(entity, new HealthComponent { Value = 0 });

        // Snapshot
        _preTick.SyncFrom(_live);

        List<(Breakpoint bp, Entity e)> hits = new();
        _manager.OnBreakpointHit += (bp, e) => hits.Add((bp, e));

        var id = _manager.AddBreakpoint(
            new CompoundPredicateDto
            {
                Operator = LogicalOperator.And,
                Conditions = new List<SearchPredicateDto>
                {
                    new ExternalHitTagPredicateDto { Tag = "BP:ammo-node" },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HealthComponent),
                        PropertyPath  = "Value",
                        Operator      = SearchOperator.Equals,
                        Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
                    },
                },
            },
            displayName: "CompoundExternalHit test");

        // Fire with entity having Health=0 → should hit
        _manager.OnExternalHit("BP:ammo-node", entity);
        Assert.Single(hits);

        // Reset paused state for second assertion
        _manager.RequestContinue();
        hits.Clear();

        // Mutate live entity: Health=5 → should not hit
        _live.GetComponentRW<HealthComponent>(entity).Value = 5;
        _manager.OnExternalHit("BP:ammo-node", entity);
        Assert.Empty(hits);
    }

    [Fact]
    public void ExternalHitTag_DisabledBreakpoint_DoesNotFire()
    {
        var entity = _live.Create();
        bool fired = false;
        _manager.OnBreakpointHit += (_, _) => fired = true;

        var id = _manager.AddBreakpoint(
            new ExternalHitTagPredicateDto { Tag = "BP:disabled" });
        _manager.SetEnabled(id, false);

        _manager.OnExternalHit("BP:disabled", entity);
        Assert.False(fired);
    }

    [Fact]
    public void ExternalHitTag_NoMatchingBreakpoint_StillPausesViaFallback()
    {
        // When OnExternalHit is called with a tag that has NO registered universal BP,
        // the manager should still rewind and pause (Slice 1 Blueprint probe path).
        var entity = _live.Create();

        _manager.OnExternalHit("unregistered-tag", entity);

        Assert.True(_manager.IsPaused);
    }

    [Fact]
    public void ExternalHitTag_Compiler_ReturnsAlwaysFalse()
    {
        var predicate = new ExternalHitTagPredicateDto { Tag = "whatever" };
        var compiled  = _compiler.CompileComponentPredicate(predicate);

        var entity = _live.Create();
        Assert.False(compiled(_live, entity));
    }
}
```

**Note**: `ExternalHitTag_InCompoundAnd_EvaluatesRemainingChildrenAgainstEntity` uses `RequestContinue()` to reset pause state. `RequestContinue()` calls `_liveRepo.SyncFrom(_postTickSnapshot)`, which requires `_postTickSnapshot` to be valid. Either call `RequestContinue()` after the first hit, or set up the test so it doesn't need to reset paused state. An alternative: use two separate test instances. For simplicity, keep them as two separate `[Fact]` methods and use independent manager instances.

Actually, the simplest approach: split the two assertions into two separate `[Fact]` methods (`ExternalHitTag_InCompoundAnd_Ammo0_Fires` and `ExternalHitTag_InCompoundAnd_Ammo5_DoesNotFire`) so each uses a fresh manager.

---

## 16. Shared test stubs (add to a shared file or inline in test files)

### `FakeTimeController` (already exists in the test project — reuse it)
### `HealthComponent` (already exists in the test project — reuse it)
### `FakeSimulationView` (may already exist — if not, create a minimal stub)

`FakeSimulationView` wraps an `EntityRepository` as an `ISimulationView`:
```csharp
file sealed class FakeSimulationView : ISimulationView
{
    private readonly EntityRepository _repo;
    public FakeSimulationView(EntityRepository repo) => _repo = repo;
    public uint Tick => 0;
    public float Time => 0f;
    public bool HasComponent<T>(Entity e) where T : unmanaged => _repo.HasComponent<T>(e);
    public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged => ref _repo.GetComponentRO<T>(e);
    public EntityCommandBuffer GetCommandBuffer() => _repo.GetCommandBuffer();
    // Implement all other ISimulationView members as no-ops or throw
}
```

### `RecordingContextMenuBuilder` — define once in a shared file:
Create `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TestStubs/RecordingContextMenuBuilder.cs` with `file sealed class` so it's file-scoped and doesn't pollute the namespace, **or** declare it at the top of the test file as a `file`-scoped class.

---

## Build & Test

After all changes, verify:
```
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
dotnet build Hrot/Subsystems/AI/Hrot.BTree.Editor/Hrot.BTree.Editor.csproj
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj
```

All existing tests must still pass (57+ in breakpoints tests + 1 in Fdp.Toolkits.Tests).

---

## Implementation notes

1. **`BehaviorTreeAsset` constructor**: Look at the actual constructor in `Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` to see how to build one for tests. If it's `internal`, the test project may not be able to use it directly — in that case, mock out the gutter renderer test using a pre-built asset (checking if `InternalsVisibleTo` includes the test project). If needed, the gutter renderer test can be simplified to just verify `renderer.SetManager(...)` doesn't throw and that `_manager.AllBreakpoints` is populated.

2. **`HsmAsset` constructor**: Same considerations as above. For the HSM gutter test, consider using the `CountBreakpoints()` method instead of rendering, since it doesn't need a real canvas context.

3. **`ICanvasRenderContext` fake for BTree test**: If creating a full fake render context is too involved, simplify `BTreeGutterRenderer_ReadsManagerForBreakpoints` to only verify that `renderer.SetManager(manager)` sets the field AND that when `_manager.AllBreakpoints` is non-empty with a matching `SourceElementId`, no exception is thrown. Alternatively, add a simple internal method `internal int CountManagerBreakpoints()` to `BTreeBreakpointGutterRenderer` (analogous to `HsmBreakpointGutterRenderer.CountBreakpoints()`) and test via that.

4. **`ExternalHitTag_InCompoundAnd` test**: The `RequestContinue()` path requires `_postTickSnapshot` to have entity data (otherwise SyncFrom from an empty snapshot will wipe the entity). For simplicity, split into two `[Fact]` methods using fresh managers. See note in §15.

5. **`BlueprintDebugSession.SetBreakpoint` signature**: Check the actual signature in `Hrot.Blueprints.Core/IBlueprintDebugSession.cs` before writing the test. It takes `(Guid assetId, Guid graphId, string nodeId)` or similar — adjust accordingly.

6. **Ordering in `TryMountDelegate`**: The `case CompoundPredicateDto compound when HasExternalHitTag(compound):` pattern must appear **before** the existing `case CompoundPredicateDto _:` pattern so C# pattern matching selects it first.

7. **`AllBreakpoints` return type**: The current signature returns `IReadOnlyList<Breakpoint>`. The `BTreeBreakpointGutterRenderer` iterates it with `foreach`. This is correct.
