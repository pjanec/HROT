# WHEN-BATCH-11 — Editor drawers and palette (M5-T1 through M5-T4)

**Tasks covered:** WHEN-M5-T1, WHEN-M5-T2, WHEN-M5-T3, WHEN-M5-T4  
**Reference:** [TASK-DETAIL.md §M5](../TASK-DETAIL.md#when-m5-t1--whennodedrawer--whennodesession), [DESIGN §8, §15.5](../When_Reactivity_Iteration_Design_v2_2.md)

---

## Context

WHEN-BATCH-09 and WHEN-BATCH-10 completed M4 (EQS compiler + runtime).  
Current state: 108 tests pass, 2 skipped. Last commit: `6615a21d` (TASK-TRACKER M4-T5).

This batch implements the editor drawer layer for the three new node kinds:
- `WhenNodeDrawer` + `WhenNodeSession`
- `ReadEqsResultNodeDrawer` + `ReadEqsResultNodeSession`
- `SpawnEqsSensorNodeDrawer` + `SpawnEqsSensorNodeSession`
- Supporting infrastructure (interfaces, registry types, vocabulary stubs)
- Palette registration scaffolding (`NodeKindDescriptor`, `NodeKindRegistry`)
- Tests for all three drawers (headless; no ImGui context required)

---

## Files to Read First

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/InspectorWindow.cs` — how existing editor windows use ImGui
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/DrawerRegistry.cs` — existing drawer registry pattern (for `IStructEditDrawer<T>`, different from IBlueprintNodeDrawer)
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs` (lines 130–250) — `WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode` + enums + payloads
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/BlueprintAsset.cs` — `BlueprintAsset`, `BlueprintDispatchKind`, `VariableDecl`
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs` — `Pin` fields: `Guid Id`, `string Name`, `string Direction` ("In"/"Out"), `bool IsExec`, `BlueprintTypeRef TypeRef`, `List<Guid> LinkedToIds`
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/CatalogInterfaces.cs` — `IChannelCommandCatalog`, `IEngineEventCatalog`, `ChannelCommandCatalogEntry`, `EngineEventCatalogEntry`
7. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/DrawerRegistryTests.cs` — existing editor test pattern (headless, no ImGui)
8. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorWindowTests.cs` — constructor/structural test pattern
9. `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §8 (lines 1369–1740) — full drawer design spec

---

## New Files to Create

### Infrastructure (all in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/`)

**1. `IBlueprintNodeDrawer.cs`**

```csharp
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public interface IBlueprintNodeDrawer
{
    bool Handles(Node node);
    INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset);
}
```

**2. `INodeEditSession.cs`**

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

public interface INodeEditSession : IDisposable
{
    bool IsDirty { get; }
    void Draw();
    void ResetDirty();
}
```

**3. `EditorColors.cs`**

```csharp
using System.Numerics;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>ImGui color constants for editor drawer feedback.</summary>
public static class EditorColors
{
    public static readonly Vector4 Error   = new(0.9f, 0.2f, 0.2f, 1f);
    public static readonly Vector4 Warning = new(0.9f, 0.7f, 0.1f, 1f);
    public static readonly Vector4 Info    = new(0.5f, 0.8f, 1.0f, 1f);
}
```

**4. `ReactiveGuardVocabulary.cs`**

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Shared string constants for Reactive Guard palette categories and tooltips.
/// Full vocabulary wired in M8; stubs here allow M5 palette entries to compile.
/// </summary>
public static class ReactiveGuardVocabulary
{
    public const string CategoryName = "Reactive Guards";

    public const string BlueprintWhenNodeTooltip =
        "Observe a value, event, predicate, or EQS result. " +
        "Fires OnFired on the configured edge(s).";

    public const string CrossSubsystemHintWhen =
        "See Hrot/Docs/ReactiveGuards.md for cross-subsystem usage.";
}
```

**5. `EqsTemplateEntry.cs`**

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>Editor-side descriptor for a registered EQS query template.</summary>
public sealed class EqsTemplateEntry
{
    public Guid AssetId { get; init; }
    public string DisplayName { get; init; } = "";
}
```

**6. `EqsTemplateRegistry.cs`**

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Editor-side registry of known EQS template assets.
/// Populated at editor startup from the project's EQS template catalog.
/// Distinct from the runtime IEqsTemplateRegistry (which maps by uint blueprintId).
/// </summary>
public sealed class EqsTemplateRegistry
{
    private readonly List<EqsTemplateEntry> _entries = new();

    public void Register(EqsTemplateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    public IReadOnlyList<EqsTemplateEntry> EnumerateAll() => _entries;

    public EqsTemplateEntry? TryGet(Guid assetId)
        => _entries.Find(e => e.AssetId == assetId);
}
```

**7. `IEditService.cs`** (stub; full implementation deferred to M6+)

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Blueprint node edit command dispatcher. Provides undo/redo integration.
/// Stub for M5; full implementation deferred.
/// </summary>
public interface IEditService
{
    void MarkDirty(Hrot.Blueprints.Core.Assets.BlueprintAsset asset);
}
```

**8. `NodeKindDescriptor.cs`**

```csharp
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>Palette descriptor for a node kind.</summary>
public sealed class NodeKindDescriptor
{
    public string Kind { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public string Icon { get; init; } = "";
    public required Func<Node> CreateInstance { get; init; }
}
```

**9. `NodeKindRegistry.cs`**

```csharp
namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>Palette registry: maps node-kind strings to descriptors.</summary>
public sealed class NodeKindRegistry
{
    private readonly Dictionary<string, NodeKindDescriptor> _map = new();

    public void Register(NodeKindDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _map[descriptor.Kind] = descriptor;
    }

    public IReadOnlyCollection<NodeKindDescriptor> EnumerateAll() => _map.Values;

    public NodeKindDescriptor? TryGet(string kind)
        => _map.TryGetValue(kind, out var d) ? d : null;
}
```

---

### Drawers

**10. `WhenNodeDrawer.cs`**

```csharp
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public sealed class WhenNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IChannelCommandCatalog _channelCatalog;
    private readonly IEngineEventCatalog _eventCatalog;
    private readonly IEditService _editService;
    private readonly IPredicateCompiler _predicateCompiler;

    public WhenNodeDrawer(
        IChannelCommandCatalog channelCatalog,
        IEngineEventCatalog eventCatalog,
        IEditService editService,
        IPredicateCompiler predicateCompiler)
    {
        _channelCatalog    = channelCatalog    ?? throw new ArgumentNullException(nameof(channelCatalog));
        _eventCatalog      = eventCatalog      ?? throw new ArgumentNullException(nameof(eventCatalog));
        _editService       = editService       ?? throw new ArgumentNullException(nameof(editService));
        _predicateCompiler = predicateCompiler ?? throw new ArgumentNullException(nameof(predicateCompiler));
    }

    public bool Handles(Node node) => node is WhenNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new WhenNodeSession(
            (WhenNode)node, parentAsset,
            _channelCatalog, _eventCatalog, _editService, _predicateCompiler);
}

internal sealed class WhenNodeSession : INodeEditSession
{
    private readonly WhenNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IChannelCommandCatalog _channelCatalog;
    private readonly IEngineEventCatalog _eventCatalog;
    private readonly IEditService _editService;
    private readonly IPredicateCompiler _predicateCompiler;

    public bool IsDirty { get; private set; }

    public WhenNodeSession(
        WhenNode node,
        BlueprintAsset parentAsset,
        IChannelCommandCatalog channelCatalog,
        IEngineEventCatalog eventCatalog,
        IEditService editService,
        IPredicateCompiler predicateCompiler)
    {
        _node              = node;
        _parent            = parentAsset;
        _channelCatalog    = channelCatalog;
        _eventCatalog      = eventCatalog;
        _editService       = editService;
        _predicateCompiler = predicateCompiler;
    }

    public void Draw()
    {
        ImGui.Text("When");
        ImGui.Separator();
        DrawDispatchGuard();
        DrawModeSelector();
        ImGui.Separator();

        switch (_node.Mode)
        {
            case WhenMode.ValueChanged: DrawValueChangedForm(); break;
            case WhenMode.EventFired:   DrawEventFiredForm();   break;
            case WhenMode.ConditionMet: DrawConditionMetForm(); break;
            case WhenMode.EqsResult:    DrawEqsResultForm();    break;
        }

        ImGui.Separator();
        DrawEdgeSelector();
        ImGui.Separator();
        DrawPreviewPill();
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>
    /// Test hook: sets the node mode and marks the session dirty, simulating what
    /// DrawModeSelector() does when the user picks a different mode.
    /// </summary>
    internal void SetModeForTest(WhenMode mode)
    {
        _node.Mode = mode;
        IsDirty = true;
    }

    // ── Private draw helpers ─────────────────────────────────────────────────────

    private void DrawDispatchGuard()
    {
        if (_parent.Dispatch != BlueprintDispatchKind.Instance)
        {
            ImGui.TextColored(EditorColors.Error,
                "⚠ WhenNode is only allowed in Instance Blueprints.");
            ImGui.Separator();
        }
    }

    private void DrawModeSelector()
    {
        int modeIdx = (int)_node.Mode;
        string[] labels = { "Value Changed", "Event Fired", "Condition Met", "EQS Result" };
        if (ImGui.Combo("Mode", ref modeIdx, labels, labels.Length))
        {
            _node.Mode = (WhenMode)modeIdx;
            IsDirty = true;
        }
    }

    private void DrawEdgeSelector()
    {
        ImGui.Text("Edges:");
        ImGui.SameLine();

        bool rising  = _node.Edges.HasFlag(WhenEdge.RisingEdge);
        bool falling = _node.Edges.HasFlag(WhenEdge.FallingEdge);

        if (ImGui.Checkbox("Rising",  ref rising))
        {
            _node.Edges = rising
                ? _node.Edges | WhenEdge.RisingEdge
                : _node.Edges & ~WhenEdge.RisingEdge;
            IsDirty = true;
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Falling", ref falling))
        {
            _node.Edges = falling
                ? _node.Edges | WhenEdge.FallingEdge
                : _node.Edges & ~WhenEdge.FallingEdge;
            IsDirty = true;
        }

        if (_node.Edges == WhenEdge.None)
            ImGui.TextColored(EditorColors.Warning, "(no edge selected — WhenNode will never fire)");
    }

    private void DrawPreviewPill()
    {
        string preview = _node.Mode switch
        {
            WhenMode.ValueChanged => _node.ValueChanged is { } vc
                ? $"Changed: {vc.ComponentTypeId}.{vc.PropertyPath}"
                : "(unconfigured)",
            WhenMode.EventFired => _node.EventFired is { } ef
                ? $"Event: {ef.EventTypeId}"
                : "(unconfigured)",
            WhenMode.ConditionMet => "(predicate)",
            WhenMode.EqsResult => _node.EqsResult is { } er
                ? $"EQS {er.Trigger}: {er.SensorVariableName}"
                : "(unconfigured)",
            _ => "(unconfigured)",
        };
        ImGui.TextDisabled($"Preview: {preview}");
    }

    private void DrawValueChangedForm()
    {
        _node.ValueChanged ??= new ValueChangedPayload();
        ImGui.TextDisabled("(Value Changed form — component/property picker)");
    }

    private void DrawEventFiredForm()
    {
        _node.EventFired ??= new EventFiredPayload();
        var entries = _eventCatalog.GetEntries();
        ImGui.TextDisabled($"(Event Fired form — {entries.Count} events available)");
    }

    private void DrawConditionMetForm()
    {
        _node.ConditionMet ??= new ConditionMetPayload();
        ImGui.TextDisabled("(Condition Met form — predicate editor)");
    }

    private void DrawEqsResultForm()
    {
        _node.EqsResult ??= new EqsResultPayload();
        ImGui.TextDisabled("(EQS Result form — trigger and sensor picker)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
```

**11. `ReadEqsResultNodeDrawer.cs`**

```csharp
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public sealed class ReadEqsResultNodeDrawer : IBlueprintNodeDrawer
{
    public bool Handles(Node node) => node is ReadEqsResultNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ReadEqsResultNodeSession((ReadEqsResultNode)node, parentAsset);
}

internal sealed class ReadEqsResultNodeSession : INodeEditSession
{
    private readonly ReadEqsResultNode _node;
    private readonly BlueprintAsset _parent;

    public bool IsDirty { get; private set; }

    public ReadEqsResultNodeSession(ReadEqsResultNode node, BlueprintAsset parentAsset)
    {
        _node   = node;
        _parent = parentAsset;
    }

    /// <summary>
    /// Returns the names of all EqsSensorHandle-typed variables on the asset.
    /// Internal test hook (InternalsVisibleTo Hrot.Blueprints.Tests).
    /// </summary>
    internal string[] GetSensorVariableNamesForTest()
        => _parent.Variables
            .Where(v => v.Type.TypeId == "FDP.Eqs.EqsSensorHandle")
            .Select(v => v.Name)
            .ToArray();

    public void Draw()
    {
        ImGui.Text("Read EQS Result");
        ImGui.Separator();

        if (_parent.Dispatch != BlueprintDispatchKind.Instance)
        {
            ImGui.TextColored(EditorColors.Error,
                "⚠ ReadEqsResultNode is only allowed in Instance Blueprints.");
            ImGui.Separator();
        }

        var sensorVars = GetSensorVariableNamesForTest();

        int sensorIdx = Array.IndexOf(sensorVars, _node.SensorVariableName);
        if (ImGui.Combo("Sensor", ref sensorIdx, sensorVars, sensorVars.Length))
        {
            _node.SensorVariableName = sensorVars[sensorIdx];
            IsDirty = true;
        }

        if (sensorVars.Length == 0)
            ImGui.TextColored(EditorColors.Info, "(no EqsSensorHandle variables on this asset)");

        ImGui.TextDisabled("Index: drive via input pin (default 0)");
        ImGui.TextDisabled("Outputs: IsReady, ResultCount, Entity, Position, Score");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
```

**12. `SpawnEqsSensorNodeDrawer.cs`**

```csharp
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public sealed class SpawnEqsSensorNodeDrawer : IBlueprintNodeDrawer
{
    private readonly EqsTemplateRegistry _eqsTemplates;

    public SpawnEqsSensorNodeDrawer(EqsTemplateRegistry eqsTemplates)
    {
        _eqsTemplates = eqsTemplates ?? throw new ArgumentNullException(nameof(eqsTemplates));
    }

    public bool Handles(Node node) => node is SpawnEqsSensorNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SpawnEqsSensorNodeSession(
            (SpawnEqsSensorNode)node, parentAsset, _eqsTemplates);
}

internal sealed class SpawnEqsSensorNodeSession : INodeEditSession
{
    private readonly SpawnEqsSensorNode _node;
    private readonly BlueprintAsset _parent;
    private readonly EqsTemplateRegistry _templates;

    public bool IsDirty { get; private set; }

    public SpawnEqsSensorNodeSession(
        SpawnEqsSensorNode node,
        BlueprintAsset parentAsset,
        EqsTemplateRegistry templates)
    {
        _node      = node;
        _parent    = parentAsset;
        _templates = templates;
    }

    /// <summary>
    /// Test hook: simulates the designer selecting a template by AssetId.
    /// Sets TemplateAssetId on the node and marks session dirty.
    /// (InternalsVisibleTo Hrot.Blueprints.Tests)
    /// </summary>
    internal void SelectTemplateForTest(Guid assetId)
    {
        _node.TemplateAssetId = assetId;
        IsDirty = true;
    }

    public void Draw()
    {
        ImGui.Text("Spawn EQS Sensor");
        ImGui.Separator();
        DrawDispatchGuard();
        DrawTemplatePicker();
        ImGui.Separator();
        ImGui.TextDisabled("Inputs (wire via pins, or use literal defaults):");
        ImGui.TextDisabled("  • SearchRadius     (float)");
        ImGui.TextDisabled("  • FactionFilter    (uint)");
        ImGui.TextDisabled("  • ThreatThreshold  (float)");
        ImGui.TextDisabled("  • PublishPolicy    (byte)");
        ImGui.TextDisabled("  • Priority         (byte)");
        ImGui.TextDisabled("Output: Handle (EqsSensorHandle)");
    }

    private void DrawDispatchGuard()
    {
        if (_parent.Dispatch != BlueprintDispatchKind.Instance)
        {
            ImGui.TextColored(EditorColors.Error,
                "⚠ SpawnEqsSensorNode is only allowed in Instance Blueprints.");
            ImGui.Separator();
        }
    }

    private void DrawTemplatePicker()
    {
        var templates    = _templates.EnumerateAll();
        var displayNames = templates.Select(t => t.DisplayName).ToArray();

        int currentIdx = -1;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i].AssetId == _node.TemplateAssetId) { currentIdx = i; break; }
        }

        if (ImGui.Combo("Template", ref currentIdx, displayNames, displayNames.Length))
        {
            if (currentIdx >= 0)
            {
                var chosen = templates[currentIdx];
                if (chosen.AssetId != _node.TemplateAssetId)
                {
                    _node.TemplateAssetId = chosen.AssetId;
                    IsDirty = true;
                }
            }
        }

        if (_node.TemplateAssetId == Guid.Empty)
            ImGui.TextColored(EditorColors.Warning, "(no template selected)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
```

---

### Palette Registration Entries

**13. `WhenNodePaletteEntry.cs`** (static factory helper, not a registrar — just the descriptor)

```csharp
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Palette NodeKindDescriptor factories for WhenNode, ReadEqsResultNode,
/// SpawnEqsSensorNode.  Call NodeKindRegistry.Register(...) at editor startup.
/// </summary>
public static class WhenNodePaletteEntries
{
    public static NodeKindDescriptor WhenNode() => new()
    {
        Kind        = "When",
        DisplayName = "When",
        Category    = ReactiveGuardVocabulary.CategoryName,
        Tooltip     = ReactiveGuardVocabulary.BlueprintWhenNodeTooltip,
        Icon        = "icons/when.svg",
        CreateInstance = () => new Core.Assets.WhenNode
        {
            Id    = Guid.NewGuid(),
            Mode  = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            Pins  =
            [
                new Pin { Id = Guid.NewGuid(), Name = "In",      Direction = "In",  IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true  },
            ],
        },
    };

    public static NodeKindDescriptor ReadEqsResult() => new()
    {
        Kind        = "ReadEqsResult",
        DisplayName = "Read EQS Result",
        Category    = "EQS",
        Tooltip     = "Read a ranked result from an EQS sensor's cognitive buffer. " +
                      "Pass an index to read top, second-best, etc.",
        Icon        = "icons/eqs_read.svg",
        CreateInstance = () => new Core.Assets.ReadEqsResultNode
        {
            Id                 = Guid.NewGuid(),
            SensorVariableName = "",
            Pins               =
            [
                new Pin { Id = Guid.NewGuid(), Name = "Handle",      Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle"  } },
                new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32"             } },
                new Pin { Id = Guid.NewGuid(), Name = "IsReady",     Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean"           } },
                new Pin { Id = Guid.NewGuid(), Name = "ResultCount", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32"             } },
                new Pin { Id = Guid.NewGuid(), Name = "Entity",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity"           } },
                new Pin { Id = Guid.NewGuid(), Name = "Position",    Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Numerics.Vector2"  } },
                new Pin { Id = Guid.NewGuid(), Name = "Score",       Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single"            } },
            ],
        },
    };

    public static NodeKindDescriptor SpawnEqsSensor() => new()
    {
        Kind        = "SpawnEqsSensor",
        DisplayName = "Spawn EQS Sensor",
        Category    = "EQS",
        Tooltip     = "Spawn an EQS sensor as a child entity. Pick a template, " +
                      "set parameters via input pins, get back a handle.",
        Icon        = "icons/eqs_spawn.svg",
        CreateInstance = () => new Core.Assets.SpawnEqsSensorNode
        {
            Id              = Guid.NewGuid(),
            TemplateAssetId = Guid.Empty,
            Pins            =
            [
                new Pin { Id = Guid.NewGuid(), Name = "In",              Direction = "In",  IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "Out",             Direction = "Out", IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single"  } },
                new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.UInt32"  } },
                new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single"  } },
                new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte"    } },
                new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte"    } },
                new Pin { Id = Guid.NewGuid(), Name = "Handle",          Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } },
            ],
        },
    };
}
```

---

### Assembly InternalsVisibleTo

**14. Modify `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj`**

Add inside the existing `<PropertyGroup>`:

```xml
<AssemblyName>Hrot.Blueprints.Editor</AssemblyName>
```

**Create new file `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/AssemblyInfo.cs`:**

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Hrot.Blueprints.Tests")]
```

---

## Test Files to Create

All three test files go in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/`.

---

### Test file 1: `WhenNodeDrawerTests.cs`

Tests covered: `Drawer_HandlesWhenNode`, `Drawer_HandlesWhenNode_ExcludesOtherTypes`, `Drawer_CreateSession_ReturnsNonNull`, `Drawer_ModeChange_MarksDirty`, `Drawer_DispatchGuard_SessionCreated_ForNonInstance`

```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class WhenNodeDrawerTests
{
    // Minimal stub implementations for injected dependencies (not called in headless tests)
    private sealed class NullChannelCatalog : IChannelCommandCatalog
    {
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => [];
    }
    private sealed class NullEventCatalog : IEngineEventCatalog
    {
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() => [];
    }
    private sealed class NullEditService : IEditService
    {
        public void MarkDirty(BlueprintAsset asset) { }
    }
    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        // Implement IPredicateCompiler — read the actual interface methods and implement them as no-ops
        // IMPORTANT: read the interface before implementing stubs
    }

    private static WhenNodeDrawer MakeDrawer() => new(
        new NullChannelCatalog(),
        new NullEventCatalog(),
        new NullEditService(),
        new NullPredicateCompiler());

    private static BlueprintAsset MakeInstanceAsset() => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "TestBp",
        Dispatch = BlueprintDispatchKind.Instance,
    };

    [Fact]
    public void Drawer_HandlesWhenNode()
    {
        var drawer = MakeDrawer();
        Assert.True(drawer.Handles(new WhenNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_HandlesWhenNode_ExcludesOtherTypes()
    {
        var drawer = MakeDrawer();
        Assert.False(drawer.Handles(new BranchNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull()
    {
        var drawer = MakeDrawer();
        var node   = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ValueChanged };
        var asset  = MakeInstanceAsset();
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Drawer_ModeChange_MarksDirty()
    {
        var drawer = MakeDrawer();
        var node   = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ValueChanged };
        var asset  = MakeInstanceAsset();
        var session = (WhenNodeSession)drawer.CreateSession(node, asset);

        Assert.False(session.IsDirty);
        session.SetModeForTest(WhenMode.EqsResult);

        Assert.True(session.IsDirty);
        Assert.Equal(WhenMode.EqsResult, node.Mode);
    }

    [Fact]
    public void Drawer_DispatchGuard_SessionCreated_ForNonInstance()
    {
        // Session must be creatable even for non-Instance assets (guard shown in Draw(),
        // which is not called in headless tests).
        var drawer = MakeDrawer();
        var node   = new WhenNode { Id = Guid.NewGuid() };
        var asset  = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Dispatch = BlueprintDispatchKind.AiPrimitive,
        };
        // Should NOT throw
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
    }
}
```

**Important note for `NullPredicateCompiler`:** Before implementing, read `Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler` to see its method signatures. Implement all methods as no-ops (return `null` or `default` as appropriate).

---

### Test file 2: `ReadEqsResultNodeDrawerTests.cs`

```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class ReadEqsResultNodeDrawerTests
{
    private static ReadEqsResultNodeDrawer MakeDrawer() => new();

    private static BlueprintAsset MakeInstanceAsset(params VariableDecl[] vars) => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "TestBp",
        Dispatch = BlueprintDispatchKind.Instance,
        Variables = new List<VariableDecl>(vars),
    };

    [Fact]
    public void Drawer_HandlesReadEqsResultNode()
    {
        var drawer = MakeDrawer();
        Assert.True(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_HandlesReadEqsResultNode_ExcludesOtherTypes()
    {
        var drawer = MakeDrawer();
        Assert.False(drawer.Handles(new WhenNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_SensorPicker_OnlyShowsEqsSensorHandleVars()
    {
        var sensorVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };
        var otherVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "SomeInt",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var asset = MakeInstanceAsset(sensorVar, otherVar);
        var node  = new ReadEqsResultNode { Id = Guid.NewGuid() };
        var session = (ReadEqsResultNodeSession)MakeDrawer().CreateSession(node, asset);

        var names = session.GetSensorVariableNamesForTest();

        Assert.Single(names);
        Assert.Equal("MySensor", names[0]);
    }

    [Fact]
    public void Drawer_DispatchGuard_SessionCreated_ForNonInstance()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Dispatch = BlueprintDispatchKind.Library,
        };
        var node = new ReadEqsResultNode { Id = Guid.NewGuid() };
        using var session = MakeDrawer().CreateSession(node, asset);
        Assert.NotNull(session);
    }
}
```

---

### Test file 3: `SpawnEqsSensorNodeDrawerTests.cs`

```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class SpawnEqsSensorNodeDrawerTests
{
    private static EqsTemplateRegistry MakeRegistry(params EqsTemplateEntry[] entries)
    {
        var reg = new EqsTemplateRegistry();
        foreach (var e in entries) reg.Register(e);
        return reg;
    }

    private static SpawnEqsSensorNode MakeSpawnNode() => new()
    {
        Id              = Guid.NewGuid(),
        TemplateAssetId = Guid.Empty,
        Pins            =
        [
            new Pin { Id = Guid.NewGuid(), Name = "In",              Direction = "In",  IsExec = true  },
            new Pin { Id = Guid.NewGuid(), Name = "Out",             Direction = "Out", IsExec = true  },
            new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "Handle",          Direction = "Out", IsExec = false },
        ],
    };

    // SC1
    [Fact]
    public void Drawer_HandlesSpawnEqsSensor()
    {
        var reg    = MakeRegistry();
        var drawer = new SpawnEqsSensorNodeDrawer(reg);
        Assert.True(drawer.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new WhenNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
    }

    // SC2
    [Fact]
    public void Drawer_TemplatePicker_PopulatesFromRegistry()
    {
        var t1 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "CoverQuery"  };
        var t2 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "ThreatRadar" };
        var reg    = MakeRegistry(t1, t2);
        var drawer = new SpawnEqsSensorNodeDrawer(reg);

        // The registry must expose both entries
        var all = reg.EnumerateAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.DisplayName == "CoverQuery");
        Assert.Contains(all, e => e.DisplayName == "ThreatRadar");

        // Session must be creatable for both
        var node  = MakeSpawnNode();
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.Instance };
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
    }

    // SC3
    [Fact]
    public void Drawer_TemplateSwitch_UpdatesAssetIdOnly()
    {
        var t1 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "A" };
        var t2 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "B" };
        var reg    = MakeRegistry(t1, t2);
        var drawer = new SpawnEqsSensorNodeDrawer(reg);

        var node  = MakeSpawnNode();
        node.TemplateAssetId = t1.AssetId;

        var pinIdsBefore = node.Pins.Select(p => p.Id).ToArray();
        var asset        = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.Instance };

        var session = (SpawnEqsSensorNodeSession)drawer.CreateSession(node, asset);
        session.SelectTemplateForTest(t2.AssetId);

        // TemplateAssetId changed
        Assert.Equal(t2.AssetId, node.TemplateAssetId);
        Assert.True(session.IsDirty);

        // Pin set did NOT change (template switch is pin-independent)
        Assert.Equal(pinIdsBefore, node.Pins.Select(p => p.Id).ToArray());
    }

    // SC4
    [Fact]
    public void Drawer_PreservesPinConnectionsAcrossTemplateSwitch()
    {
        var t1 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "A" };
        var t2 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "B" };
        var reg    = MakeRegistry(t1, t2);
        var drawer = new SpawnEqsSensorNodeDrawer(reg);

        var node  = MakeSpawnNode();
        node.TemplateAssetId = t1.AssetId;

        // Simulate a connection on the SearchRadius pin
        var searchRadiusPin = node.Pins.First(p => p.Name == "SearchRadius");
        var fakeUpstreamPinId = Guid.NewGuid();
        searchRadiusPin.LinkedToIds.Add(fakeUpstreamPinId);

        var asset   = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.Instance };
        var session = (SpawnEqsSensorNodeSession)drawer.CreateSession(node, asset);
        session.SelectTemplateForTest(t2.AssetId);

        // Connection preserved
        var srPinAfter = node.Pins.First(p => p.Name == "SearchRadius");
        Assert.Contains(fakeUpstreamPinId, srPinAfter.LinkedToIds);
    }

    // SC5
    [Fact]
    public void Drawer_DispatchGuard_ShowsForNonInstance()
    {
        var reg    = MakeRegistry();
        var drawer = new SpawnEqsSensorNodeDrawer(reg);
        var node   = MakeSpawnNode();
        var asset  = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.AiPrimitive };

        // Session must be creatable even for non-Instance assets (guard shown in Draw()
        // which requires ImGui context; not tested here).
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
    }
}
```

---

## Key Implementation Notes

### 1. `IPredicateCompiler` interface discovery

Before implementing `NullPredicateCompiler`, read `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs` (or find it via grep for `interface IPredicateCompiler`). Implement all methods as no-ops:
- Methods returning `Func<…>?` should return `null`
- Methods returning bool should return `true` (success)
- Any `out` parameters should be set to `default`

### 2. No ImGui calls in tests

The test files MUST NOT call `session.Draw()`. All tests are headless. Tests verify:
- `Handles()` predicate correctness
- `CreateSession()` returns non-null, `IsDirty = false` initially
- Internal test hooks (`SetModeForTest`, `SelectTemplateForTest`, `GetSensorVariableNamesForTest`) produce correct mutations

### 3. `InternalsVisibleTo`

The `AssemblyInfo.cs` must be created BEFORE trying to compile the tests that cast to internal session types (`WhenNodeSession`, `ReadEqsResultNodeSession`, `SpawnEqsSensorNodeSession`).

### 4. Palette entries use correct Pin shape

The `Pin` class has: `Guid Id`, `string Name`, `string Direction` ("In"/"Out"), `bool IsExec`, `BlueprintTypeRef TypeRef`, `List<Guid> LinkedToIds`. Do NOT use `PinDirection` enum (doesn't exist) or `PinKind` enum (doesn't exist).

### 5. No DI container wiring in this batch

The `services.AddSingleton<IBlueprintNodeDrawer, WhenNodeDrawer>()` registrations from DESIGN §8.1 are NOT implemented in this batch — M5 scope is the drawer classes themselves + palette descriptors. DI wiring goes in the application composition root (deferred).

---

## Commit Instructions

After all tests pass, create ONE commit:

```
git -C d:\WORK\IOS-IG-SimHost-FDP add -A
git -C d:\WORK\IOS-IG-SimHost-FDP commit -m "WHEN-M5: editor drawers for WhenNode, ReadEqsResultNode, SpawnEqsSensorNode + palette entries"
```

---

## Verification

Run the full blueprint test suite. New tests will have filter `FullyQualifiedName~DrawerTests|FullyQualifiedName~WhenNodeDrawer|FullyQualifiedName~ReadEqsResult.*Drawer|FullyQualifiedName~SpawnEqsSensor.*Drawer`.

Expected: all existing 108 tests continue to pass; new drawer tests pass (total ≥ 122).

```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj 2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3
```

---

## Batch Report Format

Submit a report with:
1. Exact list of files created / modified
2. Final test count (passed / skipped / failed)
3. Any deviations from the instructions and why
