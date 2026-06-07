# WHEN-BATCH-12 — M6 Visual Extensions

## Context

Batch 12 implements Phase M6 of the When-Node blueprint feature. M6 adds canvas visual
extensions on top of the editor drawers from M5: attachment pills for the three new node
kinds plus a debug-mode firing pulse renderer.

**TASK-DETAIL reference:** `.dev/blueprints-3-when-node/TASK-DETAIL.md` §Phase M6  
**DESIGN reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §9

**All prior M0–M5 tasks are complete and committed.**

---

## Scope

| Task     | Deliverable                                      |
|----------|--------------------------------------------------|
| M6-T1    | `PreviewSynthesizer` + `ConditionSummaryAttachment` + `WhenNodeAttachmentProvider` |
| M6-T2    | `EqsTemplateAttachment` + `ReadEqsResultAttachment` + their providers             |
| M6-T3    | `CrossAssetDependencyAttachment` + `CrossAssetDependencyAttachmentProvider`        |
| M6-T4    | `WhenFiringPulseRenderer`                                                          |
| All      | `IAttachmentProvider` interface, `BlueprintEditorTheme` static class               |
| Tests    | `ConditionSummaryAttachmentTests`, `EqsVisualAttachmentTests`, `CrossAssetDependencyAttachmentTests`, `WhenFiringPulseRendererTests` |

---

## Required csproj change — add NodeEdit.Core reference

Add a `<ProjectReference>` to `Hrot.Blueprints.Editor.csproj`:

```xml
<ProjectReference Include="..\..\..\..\FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\NodeEditor.Core.csproj" />
```

Full reference path from `Hrot.Blueprints.Editor.csproj`:
`FDP\ExtDeps\NodeEdit\src\NodeEditor.Core\NodeEditor.Core.csproj`

The existing reference in `Hrot.Hsm.Editor.csproj` is identical; use the same relative
path pattern adjusted for the Blueprint project's location:
```
../../../../FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/NodeEditor.Core.csproj
```

`Hrot.Blueprints.Tests` already references `Hrot.Blueprints.Editor` transitively so no
additional change is needed in the test project.

---

## Actual NodeEdit interfaces (design doc simplifies these — use the ACTUAL ones)

### `IAttachmentModel` — actual interface from `NodeEditor.Core.Interfaces`

```csharp
public interface IAttachmentModel
{
    AttachmentId Id { get; }
    NodeId HostNodeId { get; }
    AttachmentCategory Category { get; }   // NOT "AttachmentColor"
    string? Glyph { get; }
    string? Label { get; }                 // NOT "DisplayText"
    string? Tooltip { get; }
    AttachmentState State { get; }
    int StackIndex { get; }
}

public enum AttachmentCategory { Decorator, Flag, Pure, Custom }

[Flags]
public enum AttachmentState
{
    Normal = 0, Disabled = 1<<0, Error = 1<<1, Warning = 1<<2,
    Executing = 1<<3, RecentlyExecuted = 1<<4, Selected = 1<<5
}
```

**Mapping from design's `AttachmentColor` to actual interface:**
- `AttachmentColor.Info`    → `Category = AttachmentCategory.Custom`, `State = AttachmentState.Normal`
- `AttachmentColor.Warning` → `Category = AttachmentCategory.Custom`, `State = AttachmentState.Warning`
- `AttachmentColor.Neutral` → `Category = AttachmentCategory.Custom`, `State = AttachmentState.Normal`

Design's `DisplayText` → implement as `Label` property.

### `ICustomCanvasRenderer` — actual interface from `NodeEditor.Core.Interfaces`

```csharp
public interface ICustomCanvasRenderer : IDisposable
{
    string Id { get; }                    // stable identifier e.g. "bp.when_firing_pulse"
    CanvasRenderPass Pass { get; }
    void Render(ICanvasRenderContext ctx); // NO deltaTime parameter
    bool IsActive => true;
    void IDisposable.Dispose() { }
}
```

**There is no `deltaTime` parameter.** Use `ImGui.GetIO().DeltaTime` inside `Render()`.

**There is no `ctx.TryGetNodeBounds()`.**  Use `ctx.Graph.FindNode(nodeId)?.Position` and
`ctx.Graph.FindNode(nodeId)?.SizeOverride` then transform with `ctx.Viewport.GraphToScreen()`.

`CanvasRenderPass` enum values: `BeforeContent`, `AfterWires`, `AfterNodes`, `TopMost`.
The pulse renderer uses `CanvasRenderPass.AfterNodes`.

---

## Domain interfaces to create (do NOT exist in NodeEdit)

### `IAttachmentProvider` — define in `Hrot.Blueprints.Editor.Visuals`

```csharp
namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Creates or refreshes attachment pills for a specific node type.
/// Providers are registered in the blueprint editor host.
/// </summary>
public interface IAttachmentProvider
{
    /// <summary>True when this provider can handle the given node.</summary>
    bool Handles(Node node);

    /// <summary>
    /// Returns a new or updated attachment for the node.
    /// If <paramref name="existing"/> is non-null and the same concrete type,
    /// providers should mutate and return it to avoid allocation churn.
    /// Returns null when no attachment should be shown (e.g., no template selected).
    /// </summary>
    IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing);
}
```

---

## M6-T1: `PreviewSynthesizer` + `ConditionSummaryAttachment` + provider

### `PreviewSynthesizer.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

The `PreviewSynthesizer` produces a compact one-line summary of a `WhenNode`'s current
configuration. It is used by `ConditionSummaryAttachment` and also by `WhenNodeSession`
(the drawer from M5) for the preview pill in the ImGui panel.

```csharp
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Synthesizes a short human-readable summary of a WhenNode's current configuration.
/// Used both by the canvas attachment pill and by the editor drawer's preview line.
/// </summary>
public static class PreviewSynthesizer
{
    /// <summary>
    /// Returns a summary string of at most <paramref name="maxLength"/> characters.
    /// Trailing characters beyond the limit are replaced with "…".
    /// </summary>
    public static string Synthesize(WhenNode node, int maxLength = 40)
    {
        var raw = node.Mode switch
        {
            WhenMode.ValueChanged  => SynthesizeValueChanged(node.ValueChanged),
            WhenMode.EventFired    => SynthesizeEventFired(node.EventFired),
            WhenMode.ConditionMet  => SynthesizeConditionMet(node.ConditionMet),
            WhenMode.EqsResult     => SynthesizeEqsResult(node.EqsResult),
            _                      => "(unknown mode)"
        };

        var edges = node.Edges == WhenEdge.None ? " (no edge)" :
                    node.Edges == WhenEdge.RisingEdge ? " ↑" :
                    node.Edges == WhenEdge.FallingEdge ? " ↓" : " ↑↓";

        var full = raw + edges;
        return full.Length <= maxLength ? full : full[..(maxLength - 1)] + "…";
    }

    private static string SynthesizeValueChanged(ValueChangedPayload? p)
    {
        if (p is null) return "Value Changed";
        if (string.IsNullOrEmpty(p.PropertyPath)) return "Value Changed";
        var propShort = p.PropertyPath.Contains('.')
            ? p.PropertyPath[(p.PropertyPath.LastIndexOf('.') + 1)..]
            : p.PropertyPath;
        return propShort;
    }

    private static string SynthesizeEventFired(EventFiredPayload? p)
    {
        if (p is null) return "Event Fired";
        if (string.IsNullOrEmpty(p.EventTypeId)) return "Event Fired";
        var eventShort = p.EventTypeId.Contains('.')
            ? p.EventTypeId[(p.EventTypeId.LastIndexOf('.') + 1)..]
            : p.EventTypeId;
        return eventShort;
    }

    private static string SynthesizeConditionMet(ConditionMetPayload? p)
    {
        return "Condition Met";
    }

    private static string SynthesizeEqsResult(EqsResultPayload? p)
    {
        if (p is null) return "EQS Result";
        var trigger = p.Trigger switch
        {
            EqsTrigger.FirstReady    => "Ready",
            EqsTrigger.TopChanged    => "TopChanged",
            EqsTrigger.ScoreCrossed  => "Score≥" + p.ScoreThreshold.ToString("F1"),
            EqsTrigger.BecomesStale  => "Stale",
            _                        => "EQS"
        };
        return string.IsNullOrEmpty(p.SensorVariableName) ? trigger : $"{p.SensorVariableName} {trigger}";
    }
}
```

### `ConditionSummaryAttachment.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for WhenNode: shows the active mode's compact summary (e.g., "Health ↑").
/// Glyph: ⚡   Category: Custom   State: Normal (healthy) or Warning (no edge selected).
/// </summary>
public sealed class ConditionSummaryAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => "⚡";
    public string? Label { get; private set; }
    public string? Tooltip { get; private set; }
    public AttachmentState State { get; private set; }
    public int StackIndex => 0;

    public ConditionSummaryAttachment(WhenNode node)
    {
        HostNodeId = new NodeId(node.Id);
        Refresh(node);
    }

    /// <summary>Updates Label/State when the node is edited.</summary>
    public void Refresh(WhenNode node)
    {
        Label   = PreviewSynthesizer.Synthesize(node, maxLength: 36);
        State   = node.Edges == WhenEdge.None
                    ? AttachmentState.Warning
                    : AttachmentState.Normal;
        Tooltip = $"Mode: {node.Mode}  Edges: {node.Edges}";
    }
}
```

### `WhenNodeAttachmentProvider.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment provider for WhenNode. Creates or refreshes a ConditionSummaryAttachment.
/// </summary>
public sealed class WhenNodeAttachmentProvider : IAttachmentProvider
{
    public bool Handles(Node node) => node is WhenNode;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var when = (WhenNode)node;
        if (existing is ConditionSummaryAttachment csa)
        {
            csa.Refresh(when);
            return csa;
        }
        return new ConditionSummaryAttachment(when);
    }
}
```

---

## M6-T2: `EqsTemplateAttachment` + `ReadEqsResultAttachment` + providers

### `EqsTemplateAttachment.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;  // EqsTemplateRegistry

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for SpawnEqsSensorNode: shows the chosen EQS template name.
/// Glyph: 📡   Category: Custom   State: Normal (template set) or Warning (none).
/// </summary>
public sealed class EqsTemplateAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => "📡";
    public string? Label { get; private set; }
    public string? Tooltip => Label;
    public AttachmentState State { get; private set; }
    public int StackIndex => 0;

    public EqsTemplateAttachment(SpawnEqsSensorNode node, EqsTemplateRegistry templates)
    {
        HostNodeId = new NodeId(node.Id);
        Refresh(node, templates);
    }

    public void Refresh(SpawnEqsSensorNode node, EqsTemplateRegistry templates)
    {
        if (node.TemplateAssetId == Guid.Empty)
        {
            Label = "(no template)";
            State = AttachmentState.Warning;
            return;
        }
        var entry = templates.TryGet(node.TemplateAssetId);
        Label = entry?.DisplayName ?? "(template not found)";
        State = entry is not null ? AttachmentState.Normal : AttachmentState.Warning;
    }
}
```

### `EqsTemplateAttachmentProvider.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>Attachment provider for SpawnEqsSensorNode.</summary>
public sealed class EqsTemplateAttachmentProvider : IAttachmentProvider
{
    private readonly EqsTemplateRegistry _templates;

    public EqsTemplateAttachmentProvider(EqsTemplateRegistry templates)
        => _templates = templates;

    public bool Handles(Node node) => node is SpawnEqsSensorNode;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var spawn = (SpawnEqsSensorNode)node;
        if (existing is EqsTemplateAttachment eta)
        {
            eta.Refresh(spawn, _templates);
            return eta;
        }
        return new EqsTemplateAttachment(spawn, _templates);
    }
}
```

### `ReadEqsResultAttachment.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for ReadEqsResultNode: shows the variable name it reads from.
/// Glyph: 📊   Category: Custom   State: Normal (set) or Warning (empty).
/// </summary>
public sealed class ReadEqsResultAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => "📊";
    public string? Label { get; private set; }
    public string? Tooltip => Label;
    public AttachmentState State { get; private set; }
    public int StackIndex => 0;

    public ReadEqsResultAttachment(ReadEqsResultNode node)
    {
        HostNodeId = new NodeId(node.Id);
        Refresh(node);
    }

    public void Refresh(ReadEqsResultNode node)
    {
        if (string.IsNullOrWhiteSpace(node.SensorVariableName))
        {
            Label = "(no variable)";
            State = AttachmentState.Warning;
        }
        else
        {
            Label = node.SensorVariableName;
            State = AttachmentState.Normal;
        }
    }
}
```

### `ReadEqsResultAttachmentProvider.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>Attachment provider for ReadEqsResultNode.</summary>
public sealed class ReadEqsResultAttachmentProvider : IAttachmentProvider
{
    public bool Handles(Node node) => node is ReadEqsResultNode;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var read = (ReadEqsResultNode)node;
        if (existing is ReadEqsResultAttachment rra)
        {
            rra.Refresh(read);
            return rra;
        }
        return new ReadEqsResultAttachment(read);
    }
}
```

---

## M6-T3: `CrossAssetDependencyAttachment` + provider

### `CrossAssetDependencyAttachment.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for nodes that transitively depend on a peer Blueprint asset.
/// Shown when a WhenNode has ValueChangedSource = PeerBlueprintVariable.
/// Glyph: 🔗   Category: Custom   State: Normal.
/// </summary>
public sealed class CrossAssetDependencyAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => "🔗";
    public string? Label { get; private set; }
    public string? Tooltip { get; private set; }
    public AttachmentState State => AttachmentState.Normal;
    public int StackIndex => 1;   // renders after ConditionSummaryAttachment (StackIndex 0)

    public CrossAssetDependencyAttachment(NodeId hostNodeId, string peerAssetName)
    {
        HostNodeId = hostNodeId;
        Label   = peerAssetName;
        Tooltip = $"Depends on peer Blueprint: {peerAssetName}";
    }
}
```

### `CrossAssetDependencyAttachmentProvider.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment provider for WhenNode when its source is PeerBlueprintVariable.
/// Requires a peer-name resolver callback (host supplies Blueprint asset name lookup).
/// </summary>
public sealed class CrossAssetDependencyAttachmentProvider : IAttachmentProvider
{
    private readonly Func<Guid, string?> _peerNameResolver;

    /// <summary>
    /// <paramref name="peerNameResolver"/> receives a Blueprint AssetId and returns
    /// its display name, or null if not found.
    /// </summary>
    public CrossAssetDependencyAttachmentProvider(Func<Guid, string?> peerNameResolver)
        => _peerNameResolver = peerNameResolver;

    public bool Handles(Node node)
        => node is WhenNode w
           && w.Mode == WhenMode.ValueChanged
           && w.ValueChanged?.Source == ValueChangedSource.PeerBlueprintVariable
           && w.ValueChanged?.PeerBlueprintAssetId is not null;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var when = (WhenNode)node;
        var peerId = when.ValueChanged!.PeerBlueprintAssetId!.Value;
        var name   = _peerNameResolver(peerId) ?? peerId.ToString("N")[..8];

        if (existing is CrossAssetDependencyAttachment cad
            && cad.HostNodeId == new NodeId(node.Id))
        {
            // CrossAssetDependencyAttachment is immutable once created; recreate if name changed.
            if (cad.Label == name) return cad;
        }
        return new CrossAssetDependencyAttachment(new NodeId(node.Id), name);
    }
}
```

---

## M6-T4: `WhenFiringPulseRenderer`

### `WhenFiringPulseRenderer.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

**Design note:** The design spec shows `void Render(ctx, deltaTime)` — this is WRONG.
The actual `ICustomCanvasRenderer` interface has `void Render(ICanvasRenderContext ctx)`.
Use `ImGui.GetIO().DeltaTime` inside `Render()` for the per-frame delta.

**Debug-mode control:** Pass `isDebugMode` in the constructor (default: `#if DEBUG true`).
This lets tests inject `false` to verify Release-mode behaviour without requiring a Release build.

```csharp
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Custom canvas renderer that overlays a brief visual pulse on a WhenNode when
/// it fires at runtime in Debug mode.
///
/// In non-Debug mode (isDebugMode = false) the renderer is inactive; no allocation
/// or rendering occurs.
/// </summary>
public sealed class WhenFiringPulseRenderer : ICustomCanvasRenderer
{
    private const float PulseDuration = 0.4f;
    private static readonly Vector2 DefaultNodeSize = new(160f, 60f);

    private readonly bool _isDebugMode;
    private readonly Dictionary<NodeId, float> _pulses = new();

    public string Id => "bp.when_firing_pulse";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive => _isDebugMode;

    /// <param name="isDebugMode">
    /// When false the renderer is permanently inactive (no-op).
    /// Defaults to true in DEBUG builds, false in RELEASE builds.
    /// Inject false in tests to verify Release-mode behaviour.
    /// </param>
    public WhenFiringPulseRenderer(
#if DEBUG
        bool isDebugMode = true
#else
        bool isDebugMode = false
#endif
    )
    {
        _isDebugMode = isDebugMode;
    }

    /// <summary>
    /// Records a WhenNode firing event. Call from the host's debug event handler.
    /// No-op when isDebugMode is false.
    /// </summary>
    public void OnNodeFired(NodeId nodeId)
    {
        if (!_isDebugMode) return;
        _pulses[nodeId] = PulseDuration;
    }

    /// <summary>Returns true when the given node has a pending pulse.</summary>
    public bool HasPulse(NodeId nodeId) => _pulses.ContainsKey(nodeId);

    /// <summary>Number of nodes currently pulsing.</summary>
    public int ActivePulseCount => _pulses.Count;

    public void Render(ICanvasRenderContext ctx)
    {
        if (_pulses.Count == 0) return;

        var deltaTime = ImGui.GetIO().DeltaTime;
        var toRemove  = new List<NodeId>();

        foreach (var (nodeId, remaining) in _pulses)
        {
            float t     = remaining / PulseDuration;
            float alpha = t;

            var nodeModel = ctx.Graph.FindNode(nodeId);
            if (nodeModel is not null)
            {
                var pos  = nodeModel.Position;
                var size = nodeModel.SizeOverride ?? DefaultNodeSize;
                var expand = (1.0f - t) * 8f;
                var minG = pos - new Vector2(expand, expand);
                var maxG = pos + size + new Vector2(expand, expand);
                var minS = ctx.Viewport.GraphToScreen(minG);
                var maxS = ctx.Viewport.GraphToScreen(maxG);

                ctx.DrawList.AddRect(minS, maxS,
                    ImGui.GetColorU32(BlueprintEditorTheme.WhenFiringPulse with { W = alpha }),
                    rounding: 4f * ctx.Zoom,
                    flags: ImDrawFlags.None,
                    thickness: 3f * ctx.Zoom);
            }

            var newRemaining = remaining - deltaTime;
            _pulses[nodeId] = newRemaining;
            if (newRemaining <= 0f) toRemove.Add(nodeId);
        }

        foreach (var id in toRemove) _pulses.Remove(id);
    }
}
```

### `BlueprintEditorTheme.cs` — namespace `Hrot.Blueprints.Editor.Visuals`

```csharp
using System.Numerics;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>Theme color constants for Blueprint editor visual extensions.</summary>
public static class BlueprintEditorTheme
{
    public static readonly Vector4 WhenAttachmentBg  = new(0.20f, 0.30f, 0.45f, 1.0f);
    public static readonly Vector4 EqsReadBg         = new(0.20f, 0.40f, 0.30f, 1.0f);
    public static readonly Vector4 EqsSpawnBg        = new(0.30f, 0.40f, 0.30f, 1.0f);
    public static readonly Vector4 CrossAssetBg      = new(0.35f, 0.30f, 0.45f, 1.0f);
    public static readonly Vector4 WhenFiringPulse   = new(0.95f, 0.85f, 0.20f, 1.0f);
}
```

---

## Tests

All tests go in `Hrot.Blueprints.Tests/Editor/` and are headless (no ImGui, no canvas).
Use `[Fact]` from xUnit. All 4 test classes must pass:
`ConditionSummaryAttachmentTests`, `EqsVisualAttachmentTests`,
`CrossAssetDependencyAttachmentTests`, `WhenFiringPulseRendererTests`.

### `ConditionSummaryAttachmentTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.Editor;

public sealed class ConditionSummaryAttachmentTests
{
    // ── PreviewSynthesizer ────────────────────────────────────────────────

    [Fact]
    public void Synthesize_ValueChanged_WithPropertyPath_ReturnsShortPropName()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload { PropertyPath = "Health.Current" }
        };
        var text = PreviewSynthesizer.Synthesize(node, maxLength: 40);
        Assert.Contains("Current", text);
        Assert.Contains("↑", text);
    }

    [Fact]
    public void Synthesize_EventFired_WithTypeId_ReturnsShortName()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload { EventTypeId = "Hrot.Ai.DamageEvent" }
        };
        var text = PreviewSynthesizer.Synthesize(node);
        Assert.Contains("DamageEvent", text);
    }

    [Fact]
    public void Synthesize_EqsResult_ScoreCrossed_IncludesThreshold()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload { Trigger = EqsTrigger.ScoreCrossed, ScoreThreshold = 0.75f, SensorVariableName = "CoverSensor" }
        };
        var text = PreviewSynthesizer.Synthesize(node);
        Assert.Contains("0.7", text);  // "0.75" or "Score≥0.75"
        Assert.Contains("CoverSensor", text);
    }

    [Fact]
    public void Synthesize_LongText_TruncatesWithEllipsis()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                PropertyPath = "VeryLongComponentPath.VeryLongPropertyName.Nested"
            }
        };
        var text = PreviewSynthesizer.Synthesize(node, maxLength: 20);
        Assert.True(text.Length <= 20, $"Expected ≤20 chars, got: {text}");
        Assert.EndsWith("…", text);
    }

    // ── ConditionSummaryAttachment ────────────────────────────────────────

    [Fact]
    public void Attachment_ForWhenNode_HasNonNullLabel()
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.NotNull(attachment.Label);
        Assert.NotEmpty(attachment.Label);
    }

    [Fact]
    public void Attachment_NoEdge_HasWarningState()
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.None };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.Equal(AttachmentState.Warning, attachment.State);
    }

    [Fact]
    public void Attachment_RisingEdge_HasNormalState()
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.Equal(AttachmentState.Normal, attachment.State);
    }

    [Fact]
    public void Attachment_HostNodeId_MatchesNodeId()
    {
        var id   = Guid.NewGuid();
        var node = new WhenNode { Id = id, Mode = WhenMode.ValueChanged, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.Equal(new NodeId(id), attachment.HostNodeId);
    }

    [Theory]
    [InlineData(WhenMode.ValueChanged)]
    [InlineData(WhenMode.EventFired)]
    [InlineData(WhenMode.ConditionMet)]
    [InlineData(WhenMode.EqsResult)]
    public void Attachment_AllModes_NonEmptyLabel(WhenMode mode)
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = mode, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.NotNull(attachment.Label);
        Assert.NotEmpty(attachment.Label);
    }

    [Fact]
    public void Attachment_Refresh_UpdatesLabel()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload { PropertyPath = "Alpha" }
        };
        var attachment = new ConditionSummaryAttachment(node);
        var firstLabel = attachment.Label;

        node.ValueChanged!.PropertyPath = "Beta";
        attachment.Refresh(node);

        Assert.NotEqual(firstLabel, attachment.Label);
        Assert.Contains("Beta", attachment.Label);
    }

    // ── WhenNodeAttachmentProvider ────────────────────────────────────────

    [Fact]
    public void Provider_Handles_WhenNode()
    {
        var provider = new WhenNodeAttachmentProvider();
        Assert.True(provider.Handles(new WhenNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Provider_DoesNotHandle_ReadEqsResultNode()
    {
        var provider = new WhenNodeAttachmentProvider();
        Assert.False(provider.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Provider_CreateOrRefresh_ReusesExistingAttachment()
    {
        var provider   = new WhenNodeAttachmentProvider();
        var node       = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge };
        var first      = provider.CreateOrRefresh(node, null);
        var second     = provider.CreateOrRefresh(node, first);
        Assert.Same(first, second);  // same instance reused
    }
}
```

### `EqsVisualAttachmentTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.Editor;

public sealed class EqsVisualAttachmentTests
{
    // ── EqsTemplateAttachment ────────────────────────────────────────────

    [Fact]
    public void EqsTemplate_NoTemplate_LabelIsNoTemplate()
    {
        var registry = new EqsTemplateRegistry();
        var node     = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = Guid.Empty };
        var att      = new EqsTemplateAttachment(node, registry);
        Assert.Equal("(no template)", att.Label);
        Assert.Equal(AttachmentState.Warning, att.State);
    }

    [Fact]
    public void EqsTemplate_WithTemplate_LabelIsTemplateName()
    {
        var registry   = new EqsTemplateRegistry();
        var templateId = Guid.NewGuid();
        registry.Register(new EqsTemplateEntry { AssetId = templateId, DisplayName = "CoverQuery" });

        var node = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = templateId };
        var att  = new EqsTemplateAttachment(node, registry);

        Assert.Equal("CoverQuery", att.Label);
        Assert.Equal(AttachmentState.Normal, att.State);
    }

    [Fact]
    public void EqsTemplate_UnknownTemplate_LabelIsNotFound()
    {
        var registry = new EqsTemplateRegistry();
        var node     = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = Guid.NewGuid() };
        var att      = new EqsTemplateAttachment(node, registry);
        Assert.Equal("(template not found)", att.Label);
        Assert.Equal(AttachmentState.Warning, att.State);
    }

    [Fact]
    public void EqsTemplate_HostNodeId_MatchesNodeId()
    {
        var id       = Guid.NewGuid();
        var registry = new EqsTemplateRegistry();
        var node     = new SpawnEqsSensorNode { Id = id, TemplateAssetId = Guid.Empty };
        var att      = new EqsTemplateAttachment(node, registry);
        Assert.Equal(new NodeId(id), att.HostNodeId);
    }

    [Fact]
    public void EqsTemplate_Provider_Handles_SpawnNode()
    {
        var provider = new EqsTemplateAttachmentProvider(new EqsTemplateRegistry());
        Assert.True(provider.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
        Assert.False(provider.Handles(new WhenNode { Id = Guid.NewGuid() }));
    }

    // ── ReadEqsResultAttachment ───────────────────────────────────────────

    [Fact]
    public void ReadEqs_EmptyVariableName_LabelIsNoVariable()
    {
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "" };
        var att  = new ReadEqsResultAttachment(node);
        Assert.Equal("(no variable)", att.Label);
        Assert.Equal(AttachmentState.Warning, att.State);
    }

    [Fact]
    public void ReadEqs_WithVariableName_LabelIsVariableName()
    {
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "CoverSensor" };
        var att  = new ReadEqsResultAttachment(node);
        Assert.Equal("CoverSensor", att.Label);
        Assert.Equal(AttachmentState.Normal, att.State);
    }

    [Fact]
    public void ReadEqs_Refresh_UpdatesLabel()
    {
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "Alpha" };
        var att  = new ReadEqsResultAttachment(node);
        Assert.Equal("Alpha", att.Label);

        node.SensorVariableName = "Beta";
        att.Refresh(node);
        Assert.Equal("Beta", att.Label);
    }

    [Fact]
    public void ReadEqs_Provider_Handles_ReadNode()
    {
        var provider = new ReadEqsResultAttachmentProvider();
        Assert.True(provider.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
        Assert.False(provider.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void ReadEqs_Provider_ReusesExistingAttachment()
    {
        var provider = new ReadEqsResultAttachmentProvider();
        var node     = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "Sensor1" };
        var first    = provider.CreateOrRefresh(node, null);
        var second   = provider.CreateOrRefresh(node, first);
        Assert.Same(first, second);
    }
}
```

### `CrossAssetDependencyAttachmentTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.Editor;

public sealed class CrossAssetDependencyAttachmentTests
{
    [Fact]
    public void Attachment_Label_IsPeerAssetName()
    {
        var nodeId = new NodeId(Guid.NewGuid());
        var att    = new CrossAssetDependencyAttachment(nodeId, "EntityState");
        Assert.Equal("EntityState", att.Label);
    }

    [Fact]
    public void Attachment_Glyph_IsLink()
    {
        var att = new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "X");
        Assert.Equal("🔗", att.Glyph);
    }

    [Fact]
    public void Attachment_State_IsNormal()
    {
        var att = new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "X");
        Assert.Equal(AttachmentState.Normal, att.State);
    }

    [Fact]
    public void Attachment_StackIndex_IsOne()
    {
        var att = new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "X");
        Assert.Equal(1, att.StackIndex);
    }

    [Fact]
    public void Attachment_HostNodeId_MatchesInput()
    {
        var id  = new NodeId(Guid.NewGuid());
        var att = new CrossAssetDependencyAttachment(id, "X");
        Assert.Equal(id, att.HostNodeId);
    }

    // ── Provider ────────────────────────────────────────────────────────

    [Fact]
    public void Provider_Handles_PeerBlueprintVariable_WhenNode()
    {
        var peerId   = Guid.NewGuid();
        var provider = new CrossAssetDependencyAttachmentProvider(_ => "EntityState");
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload
            {
                Source              = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId
            }
        };
        Assert.True(provider.Handles(node));
    }

    [Fact]
    public void Provider_DoesNotHandle_SelfComponent_WhenNode()
    {
        var provider = new CrossAssetDependencyAttachmentProvider(_ => null);
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload { Source = ValueChangedSource.SelfComponent }
        };
        Assert.False(provider.Handles(node));
    }

    [Fact]
    public void Provider_DoesNotHandle_EventFired_WhenNode()
    {
        var provider = new CrossAssetDependencyAttachmentProvider(_ => null);
        var node     = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.EventFired };
        Assert.False(provider.Handles(node));
    }

    [Fact]
    public void Provider_CreateOrRefresh_UsesResolvedPeerName()
    {
        var peerId   = Guid.NewGuid();
        var provider = new CrossAssetDependencyAttachmentProvider(id => id == peerId ? "EntityState" : null);
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload
            {
                Source              = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId
            }
        };
        var att = provider.CreateOrRefresh(node, null) as CrossAssetDependencyAttachment;
        Assert.NotNull(att);
        Assert.Equal("EntityState", att.Label);
    }

    [Fact]
    public void Provider_CreateOrRefresh_FallsBackToShortId_WhenNameUnresolved()
    {
        var peerId   = Guid.NewGuid();
        var provider = new CrossAssetDependencyAttachmentProvider(_ => null);
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload
            {
                Source              = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId
            }
        };
        var att = provider.CreateOrRefresh(node, null) as CrossAssetDependencyAttachment;
        Assert.NotNull(att);
        // Label is an 8-char hex short-id when name resolver returns null
        Assert.Equal(8, att.Label!.Length);
    }
}
```

### `WhenFiringPulseRendererTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.Editor;

public sealed class WhenFiringPulseRendererTests
{
    [Fact]
    public void Renderer_IsActive_InDebugMode()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.True(renderer.IsActive);
    }

    [Fact]
    public void Renderer_IsNotActive_InReleaseMode()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: false);
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void Renderer_Id_IsStable()
    {
        var a = new WhenFiringPulseRenderer(isDebugMode: true);
        var b = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public void OnNodeFired_DebugMode_AddsPendingPulse()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        var nodeId   = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(nodeId);
        Assert.True(renderer.HasPulse(nodeId));
        Assert.Equal(1, renderer.ActivePulseCount);
    }

    [Fact]
    public void OnNodeFired_ReleaseMode_DoesNotAddPulse()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: false);
        var nodeId   = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(nodeId);
        Assert.False(renderer.HasPulse(nodeId));
        Assert.Equal(0, renderer.ActivePulseCount);
    }

    [Fact]
    public void OnNodeFired_MultipleFires_AllTracked()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        var id1 = new NodeId(Guid.NewGuid());
        var id2 = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(id1);
        renderer.OnNodeFired(id2);
        Assert.Equal(2, renderer.ActivePulseCount);
    }

    [Fact]
    public void OnNodeFired_SameNode_ResetsTimer()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        var nodeId   = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(nodeId);
        renderer.OnNodeFired(nodeId);   // re-fires same node
        // Should still be 1 pulse (not 2), reset to full duration
        Assert.Equal(1, renderer.ActivePulseCount);
    }

    [Fact]
    public void Renderer_Pass_IsAfterNodes()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.Equal(CanvasRenderPass.AfterNodes, renderer.Pass);
    }

    [Fact]
    public void Renderer_NoFires_ZeroAllocations_ActivePulseCountIsZero()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.Equal(0, renderer.ActivePulseCount);
    }
}
```

---

## Necessary `using` statements for test files

Since the tests use types from multiple namespaces, each test file needs:

```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using NodeEditor.Core.Canvas;         // CanvasRenderPass
using NodeEditor.Core.Interfaces;     // IAttachmentModel, AttachmentCategory, AttachmentState
using NodeEditor.Primitives;          // NodeId, AttachmentId
using Xunit;
```

---

## Test run command

After implementing, run:

```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj `
  --filter "FullyQualifiedName~Attachment|FullyQualifiedName~Preview|FullyQualifiedName~Pulse" `
  2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3
```

Expected: all new tests pass, 0 failures, 0 errors.

Also verify the full WhenNode test suite still passes:

```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj `
  --filter "FullyQualifiedName~WhenNode|FullyQualifiedName~ReadEqs|FullyQualifiedName~SpawnEqs|FullyQualifiedName~Attachment|FullyQualifiedName~Pulse|FullyQualifiedName~Preview" `
  2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3
```

---

## Deliverables checklist

- [ ] `Hrot.Blueprints.Editor.csproj` — added NodeEditor.Core reference
- [ ] `Hrot.Blueprints.Editor/Visuals/IAttachmentProvider.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/PreviewSynthesizer.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/ConditionSummaryAttachment.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/WhenNodeAttachmentProvider.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/EqsTemplateAttachment.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/EqsTemplateAttachmentProvider.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/ReadEqsResultAttachment.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/ReadEqsResultAttachmentProvider.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/CrossAssetDependencyAttachment.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/CrossAssetDependencyAttachmentProvider.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/WhenFiringPulseRenderer.cs`
- [ ] `Hrot.Blueprints.Editor/Visuals/BlueprintEditorTheme.cs`
- [ ] `Hrot.Blueprints.Tests/Editor/ConditionSummaryAttachmentTests.cs`
- [ ] `Hrot.Blueprints.Tests/Editor/EqsVisualAttachmentTests.cs`
- [ ] `Hrot.Blueprints.Tests/Editor/CrossAssetDependencyAttachmentTests.cs`
- [ ] `Hrot.Blueprints.Tests/Editor/WhenFiringPulseRendererTests.cs`

## Batch report

Return a brief report with:
- List of files created/modified
- Test results (filter output)
- Any deviations from the spec above and why
