# BATCH-45 — UBP-P8T1 + P8T2 + P8T3 + P8T4: Manager UI window shell, Predicate Builder state, JSON clipboard, temporal banner wiring

## Overview

Phase P8 implements the Data Breakpoint Manager window and its supporting logic:
- **P8T1**: `DataBreakpointManagerWindow` + `DataBreakpointManagerPanel` (ImGui, in `Hrot.Presentation`)
  + `BreakpointConditionSummarizer` (pure logic, in `Hrot.Diagnostics.Breakpoints`)
- **P8T2**: `PredicateBuilderState` (pure logic, in `Hrot.Diagnostics.Breakpoints`)
  + `DataBreakpointManager.UpdateCondition` bug fix (must also remount the delegate)
- **P8T3**: `BreakpointJsonClipboard` (pure logic, in `Hrot.Diagnostics.Breakpoints`)
- **P8T4**: Wire `TemporalStatusBannerPanel` into `DataBreakpointManagerPanel.DrawContent()`

Design references: [DESIGN.md §13.1](../DESIGN.md#131-data-breakpoint-manager-window),
[§13.2](../DESIGN.md#132-predicate-builder-details-inspector)

**Test target project**: `Hrot.Diagnostics.Breakpoints.Tests`
(Pure-state helpers live in `Hrot.Diagnostics.Breakpoints`; ImGui window/panel live in `Hrot.Presentation`.)

---

## Pre-work research

Read these files to understand naming/signatures/patterns before coding:

1. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs`
2. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`
3. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` (lines 1-350)
4. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerState.cs`
5. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerPanel.cs`
6. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TemporalStatusBannerTests.cs`
7. `Hrot/Engine/Hrot.Presentation/Windows/ArchitectureDiagnosticsWindow.cs` (pattern for ManagedWindow)
8. `Hrot/Engine/Hrot.Presentation/Windows/FdpPanelWindows.cs` (pattern for panel windows)
9. `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj`
10. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj`
11. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` (all DTO types available)
12. Look at one existing test file in `Hrot.Diagnostics.Breakpoints.Tests/` to understand the `ManagerFactory.Create()` helper pattern.

---

## 1. Fix `DataBreakpointManager.UpdateCondition` (P8T2 pre-req)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

Current implementation only updates the stored DTO but does NOT remount the compiled delegate. Fix it to also remount:

```csharp
public void UpdateCondition(BreakpointId id, SearchPredicateDto? condition)
{
    if (!_breakpoints.TryGetValue(id, out var bp))
        return;

    var updated = bp with { Condition = condition };
    _breakpoints[id] = updated;

    // Remount the compiled delegate for the new condition.
    UnmountDelegate(id);
    if (condition != null && updated.Enabled)
        TryMountDelegate(id, updated);
}
```

---

## 2. `BreakpointConditionSummarizer.cs` — NEW (in `Hrot.Diagnostics.Breakpoints`)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointConditionSummarizer.cs`

```csharp
using System;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Converts a <see cref="SearchPredicateDto"/> to a short human-readable string
/// suitable for the "Condition Summary" column in the Data Breakpoint Manager window.
/// </summary>
public static class BreakpointConditionSummarizer
{
    public static string Summarize(SearchPredicateDto? dto) => dto switch
    {
        null                           => "(none)",
        PropertyMatchDto pm            => $"Component: {pm.ComponentType?.Name ?? "?"} {SummarizePredicate(pm.Predicate)}",
        TransientEventPredicateDto te  => $"Event: {te.EventType?.Name ?? "?"}",
        BehaviorParamPredicateDto bp   => $"BParam: {bp.BehaviorId}",
        StructuralPredicateDto st      => $"Structural: {st.ModificationType}",
        SpatialBoundingPredicateDto sp => $"Spatial",
        LifecyclePredicateDto lc       => $"Lifecycle: {lc.IdentifierType}",
        TraceBufferScanPredicateDto tr => $"Trace[0x{tr.OpCode:X2}]",
        CompoundPredicateDto cp        => $"Compound[{cp.Operator}]({cp.Conditions.Count})",
        BlueprintVariablePredicateDto bv => $"Blueprint: {bv.TargetBlueprintAssetId.ToString()[..8]}...",
        ExternalHitTagPredicateDto et  => $"Tag: {et.Tag}",
        _                              => dto.GetType().Name,
    };

    private static string SummarizePredicate(SearchPredicateDto? pred) => pred switch
    {
        NumericPredicateDto n  => $"[{n.MinValue}, {n.MaxValue}]",
        StringPredicateDto s   => $"\"{s.Substring}\"",
        _                      => string.Empty,
    };
}
```

---

## 3. `BreakpointJsonClipboard.cs` — NEW (in `Hrot.Diagnostics.Breakpoints`)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointJsonClipboard.cs`

```csharp
using System;
using System.Text.Json;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Serializes and deserializes <see cref="SearchPredicateDto"/> to/from JSON
/// for clipboard copy/paste in the Data Breakpoint Manager window.
/// Uses the polymorphic [JsonDerivedType] attributes already on <see cref="SearchPredicateDto"/>.
/// </summary>
public static class BreakpointJsonClipboard
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    /// <summary>Serializes <paramref name="dto"/> to a JSON string.</summary>
    public static string Serialize(SearchPredicateDto dto)
        => JsonSerializer.Serialize<SearchPredicateDto>(dto, _options);

    /// <summary>
    /// Attempts to deserialize a JSON string back to a <see cref="SearchPredicateDto"/>.
    /// Returns <c>null</c> on any parse or type error.
    /// </summary>
    public static SearchPredicateDto? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SearchPredicateDto>(json, _options);
        }
        catch
        {
            return null;
        }
    }
}
```

---

## 4. `PredicateBuilderState.cs` — NEW (in `Hrot.Diagnostics.Breakpoints`)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/PredicateBuilderState.cs`

```csharp
using System;
using System.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Predicate mode for the Data Breakpoint Manager's Details Inspector.
/// Determines which root <see cref="SearchPredicateDto"/> subtype is used.
/// </summary>
public enum PredicateMode
{
    Component,
    Event,
    Lifecycle,
    Spatial,
    Structural,
    Compound,
    BehaviorParam,
    BlueprintVariable,
    TraceBufferScan,
}

/// <summary>
/// Pure-logic state for the Predicate Builder panel.
/// Extracted from the ImGui panel so it can be unit-tested without an ImGui context.
///
/// Responsibilities:
///   - Tracks the current <see cref="PredicateMode"/> and the corresponding root DTO.
///   - <see cref="SwitchMode"/> discards the current DTO and creates a blank replacement.
///   - <see cref="Apply"/> calls <see cref="IDataBreakpointManager.UpdateCondition"/>
///     to remount the newly configured predicate.
/// </summary>
public sealed class PredicateBuilderState
{
    private PredicateMode _mode = PredicateMode.Component;
    private SearchPredicateDto _currentDto;

    /// <summary>The currently active predicate mode.</summary>
    public PredicateMode CurrentMode => _mode;

    /// <summary>
    /// The currently configured predicate DTO.
    /// May be modified in-place by the StructEdit session before <see cref="Apply"/> is called.
    /// </summary>
    public SearchPredicateDto CurrentDto
    {
        get  => _currentDto;
        set  => _currentDto = value ?? throw new ArgumentNullException(nameof(value));
    }

    public PredicateBuilderState()
    {
        _currentDto = CreateDefaultDto(_mode);
    }

    /// <summary>
    /// Switches the mode and replaces the current DTO with a blank instance of the
    /// corresponding subtype. Previous edits are discarded.
    /// </summary>
    public void SwitchMode(PredicateMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _currentDto = CreateDefaultDto(mode);
    }

    /// <summary>
    /// Loads an existing breakpoint's condition into the builder.
    /// Infers and sets <see cref="CurrentMode"/> from the DTO type.
    /// </summary>
    public void LoadBreakpoint(Breakpoint bp)
    {
        if (bp.Condition is null)
        {
            _mode = PredicateMode.Component;
            _currentDto = CreateDefaultDto(_mode);
            return;
        }

        _currentDto = bp.Condition;
        _mode = InferMode(bp.Condition);
    }

    /// <summary>
    /// Calls <see cref="IDataBreakpointManager.UpdateCondition"/> with the current DTO,
    /// which triggers a remount of the compiled delegate in the manager.
    /// </summary>
    public void Apply(BreakpointId id, IDataBreakpointManager manager)
    {
        if (manager is null) throw new ArgumentNullException(nameof(manager));
        manager.UpdateCondition(id, _currentDto);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SearchPredicateDto CreateDefaultDto(PredicateMode mode) => mode switch
    {
        PredicateMode.Component       => new PropertyMatchDto(),
        PredicateMode.Event           => new TransientEventPredicateDto(),
        PredicateMode.Lifecycle       => new LifecyclePredicateDto(),
        PredicateMode.Spatial         => new SpatialBoundingPredicateDto(),
        PredicateMode.Structural      => new StructuralPredicateDto(),
        PredicateMode.Compound        => new CompoundPredicateDto(),
        PredicateMode.BehaviorParam   => new BehaviorParamPredicateDto(),
        PredicateMode.BlueprintVariable => new BlueprintVariablePredicateDto(),
        PredicateMode.TraceBufferScan => new TraceBufferScanPredicateDto(),
        _                             => new PropertyMatchDto(),
    };

    private static PredicateMode InferMode(SearchPredicateDto dto) => dto switch
    {
        PropertyMatchDto            => PredicateMode.Component,
        TransientEventPredicateDto  => PredicateMode.Event,
        LifecyclePredicateDto       => PredicateMode.Lifecycle,
        SpatialBoundingPredicateDto => PredicateMode.Spatial,
        StructuralPredicateDto      => PredicateMode.Structural,
        CompoundPredicateDto        => PredicateMode.Compound,
        BehaviorParamPredicateDto   => PredicateMode.BehaviorParam,
        BlueprintVariablePredicateDto => PredicateMode.BlueprintVariable,
        TraceBufferScanPredicateDto => PredicateMode.TraceBufferScan,
        _                           => PredicateMode.Component,
    };
}
```

---

## 5. Project reference changes

### 5a. `Hrot.Presentation.csproj` — add `Hrot.Diagnostics.Breakpoints`

**File:** `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj`

Add to the existing ProjectReferences `<ItemGroup>`:
```xml
<ProjectReference Include="..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
```

Also add `InternalsVisibleTo` for the breakpoints test project:
```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
  <_Parameter1>Hrot.Diagnostics.Breakpoints.Tests</_Parameter1>
</AssemblyAttribute>
```

### 5b. `Hrot.Diagnostics.Breakpoints.Tests.csproj` — add `Hrot.Presentation`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj`

Add to ProjectReferences:
```xml
<ProjectReference Include="..\..\Engine\Hrot.Presentation\Hrot.Presentation.csproj" />
```

---

## 6. `DataBreakpointManagerPanel.cs` — NEW (in `Hrot.Presentation`)

**File:** `Hrot/Engine/Hrot.Presentation/Panels/Breakpoints/DataBreakpointManagerPanel.cs`

This is a pure ImGui rendering class. For testability, expose `internal` action methods so tests can drive it without an ImGui context.

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Diagnostics.Breakpoints;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Hrot.Presentation.Panels.Breakpoints;

/// <summary>
/// Data-grid panel for the Data Breakpoint Manager window.
/// Draws the toolbar row (Add / Remove / Enable All / Disable All / JSON),
/// the breakpoint data grid (Enabled, Scope, Type, Summary, Hits),
/// and the temporal status banner at the bottom.
/// </summary>
public sealed class DataBreakpointManagerPanel
{
    private readonly IDataBreakpointManager _manager;
    private readonly TemporalStatusBannerPanel _bannerPanel;
    private readonly Func<SearchPredicateDto>? _createDefaultPredicate;
    private BreakpointId _selectedId;

    public DataBreakpointManagerPanel(
        IDataBreakpointManager manager,
        TemporalStatusBannerState bannerState,
        Func<SearchPredicateDto>? createDefaultPredicate = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _bannerPanel = new TemporalStatusBannerPanel(bannerState);
        _createDefaultPredicate = createDefaultPredicate;
    }

    /// <summary>Currently selected breakpoint, or <see cref="BreakpointId.Invalid"/>.</summary>
    public BreakpointId SelectedId => _selectedId;

    /// <summary>
    /// Main draw entry-point. Must be called from within an active ImGui frame
    /// inside the owning <see cref="DataBreakpointManagerWindow.DrawClientArea"/>.
    /// </summary>
    public void DrawContent()
    {
        DrawToolbar();
        DrawGrid();
        DrawBanner();
    }

    // ── Internal action seams (used by tests) ─────────────────────────────────

    /// <summary>Adds a new breakpoint with the default predicate. Mirrors the "+Add" toolbar button.</summary>
    internal void AddBreakpoint()
    {
        var dto = _createDefaultPredicate?.Invoke() ?? new PropertyMatchDto();
        var id = _manager.AddBreakpoint(dto, displayName: "New Breakpoint");
        _selectedId = id;
    }

    /// <summary>Removes the currently selected breakpoint. Mirrors the "-Remove" toolbar button.</summary>
    internal void RemoveSelected()
    {
        if (!_selectedId.IsValid) return;
        _manager.Remove(_selectedId);
        _selectedId = BreakpointId.Invalid;
    }

    /// <summary>Enables all registered breakpoints. Mirrors the "Enable All" toolbar button.</summary>
    internal void EnableAll()
    {
        foreach (var bp in _manager.AllBreakpoints)
            _manager.SetEnabled(bp.Id, true);
    }

    /// <summary>Disables all registered breakpoints. Mirrors the "Disable All" toolbar button.</summary>
    internal void DisableAll()
    {
        foreach (var bp in _manager.AllBreakpoints)
            _manager.SetEnabled(bp.Id, false);
    }

    /// <summary>Toggles the Enabled state of a specific breakpoint. Called from the row checkbox.</summary>
    internal void ToggleEnabled(BreakpointId id)
    {
        var bps = _manager.AllBreakpoints;
        foreach (var bp in bps)
        {
            if (bp.Id == id)
            {
                _manager.SetEnabled(id, !bp.Enabled);
                return;
            }
        }
    }

    // ── ImGui drawing ─────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        if (ImGuiApi.Button("+ Add"))
            AddBreakpoint();

        ImGuiApi.SameLine();
        bool canRemove = _selectedId.IsValid;
        if (!canRemove) ImGuiApi.BeginDisabled();
        if (ImGuiApi.Button("- Remove"))
            RemoveSelected();
        if (!canRemove) ImGuiApi.EndDisabled();

        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Enable All"))
            EnableAll();

        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Disable All"))
            DisableAll();

        ImGuiApi.SameLine();
        if (ImGuiApi.Button("{ } JSON"))
            DrawJsonPopup();
    }

    private void DrawGrid()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;

        if (!ImGuiApi.BeginTable("##bpgrid", 5, flags)) return;

        ImGuiApi.TableSetupColumn("##en",  ImGuiTableColumnFlags.WidthFixed, 20f);
        ImGuiApi.TableSetupColumn("Scope", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGuiApi.TableSetupColumn("Type",  ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGuiApi.TableSetupColumn("Condition Summary", ImGuiTableColumnFlags.WidthStretch);
        ImGuiApi.TableSetupColumn("Hits", ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGuiApi.TableHeadersRow();

        foreach (var bp in _manager.AllBreakpoints)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0);

            bool enabled = bp.Enabled;
            if (ImGuiApi.Checkbox($"##en_{bp.Id}", ref enabled))
                ToggleEnabled(bp.Id);

            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.TextUnformatted(bp.FilterEntity.HasValue
                ? $"Entity {bp.FilterEntity.Value}"
                : "Global");

            ImGuiApi.TableSetColumnIndex(2);
            ImGuiApi.TextUnformatted(GetTypeName(bp.Condition));

            ImGuiApi.TableSetColumnIndex(3);
            bool isSelected = _selectedId == bp.Id;
            if (ImGuiApi.Selectable(
                    BreakpointConditionSummarizer.Summarize(bp.Condition) + $"##sel_{bp.Id}",
                    isSelected,
                    ImGuiSelectableFlags.SpanAllColumns))
            {
                _selectedId = bp.Id;
            }

            ImGuiApi.TableSetColumnIndex(4);
            ImGuiApi.TextUnformatted(bp.HitCount.ToString());
        }

        ImGuiApi.EndTable();
    }

    private void DrawBanner()
    {
        _bannerPanel.Draw(text =>
        {
            ImGuiApi.Separator();
            ImGuiApi.TextColored(new Vector4(1f, 0.85f, 0f, 1f), text);
        });
    }

    private void DrawJsonPopup()
    {
        // Copy selected breakpoint's condition to clipboard
        if (!_selectedId.IsValid) return;
        foreach (var bp in _manager.AllBreakpoints)
        {
            if (bp.Id != _selectedId) continue;
            if (bp.Condition != null)
                ImGuiApi.SetClipboardText(BreakpointJsonClipboard.Serialize(bp.Condition));
            return;
        }
    }

    private static string GetTypeName(SearchPredicateDto? dto) => dto switch
    {
        PropertyMatchDto          => "Component",
        TransientEventPredicateDto => "Event",
        BehaviorParamPredicateDto  => "BParam",
        StructuralPredicateDto     => "Structural",
        SpatialBoundingPredicateDto => "Spatial",
        LifecyclePredicateDto      => "Lifecycle",
        TraceBufferScanPredicateDto => "Trace",
        CompoundPredicateDto       => "Compound",
        BlueprintVariablePredicateDto => "Blueprint",
        ExternalHitTagPredicateDto => "ExtTag",
        _                          => "Unknown",
    };
}
```

**Note on `using` directives**: this file is in `Hrot.Presentation`, which has `Fdp.Presentation.Abstractions` etc. Use the namespace `Hrot.Presentation.Panels.Breakpoints`. Import `Fdp.Toolkit.ReplayBrowser.Search` for DTO types and `Hrot.Diagnostics.Breakpoints` for manager/banner types.

---

## 7. `DataBreakpointManagerWindow.cs` — NEW (in `Hrot.Presentation`)

**File:** `Hrot/Engine/Hrot.Presentation/Windows/DataBreakpointManagerWindow.cs`

```csharp
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Hrot.Presentation.Panels.Breakpoints;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Data Breakpoint Manager window. Registered per-perspective
/// (<see cref="WindowScope.PerspectiveBound"/>) so each AI/CGF subsystem
/// has its own isolated manager UI.
/// </summary>
public sealed class DataBreakpointManagerWindow : ManagedWindow
{
    private readonly DataBreakpointManagerPanel _panel;

    public DataBreakpointManagerWindow(
        string id,
        string owningPerspective,
        DataBreakpointManagerPanel panel,
        Vector4? titleBarColor = null)
        : base(id, "Data Breakpoints", owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = false;
        TitleBarColor = titleBarColor;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
```

---

## 8. Tests

### 8a. `ManagerWindowTests.cs` — `Hrot.Diagnostics.Breakpoints.Tests`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ManagerWindowTests.cs`

```csharp
[Collection("ComponentRegistry")]
public sealed class ManagerWindowTests
{
    private readonly DataBreakpointManager _manager;

    public ManagerWindowTests()
    {
        ComponentTypeRegistry.Clear();
        var (mgr, _, _, _) = ManagerFactory.Create();
        _manager = mgr;
    }

    // ── P8T1 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ManagerWindow_PerspectiveBound_WindowHasCorrectScopeAndPerspective()
    {
        // Instantiate the window and verify it has the correct ManagedWindow properties.
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());
        var window = new DataBreakpointManagerWindow(
            "dbm_test",
            "SimHost",
            panel);

        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
        Assert.Equal("SimHost", window.OwningPerspective);
        Assert.False(window.IsOpen); // starts closed
    }

    [Fact]
    public void ManagerWindow_AddRow_AppendsBreakpointToManager()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        Assert.Empty(_manager.AllBreakpoints);
        panel.AddBreakpoint();  // internal seam
        Assert.Single(_manager.AllBreakpoints);
    }

    [Fact]
    public void ManagerWindow_EnableCheckbox_TogglesManagerSetEnabled()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        // Add a breakpoint (starts enabled by default)
        var id = _manager.AddBreakpoint(new PropertyMatchDto(), displayName: "Test");
        var initial = _manager.AllBreakpoints[0];
        Assert.True(initial.Enabled);

        // Toggle off
        panel.ToggleEnabled(id);
        Assert.False(_manager.AllBreakpoints[0].Enabled);

        // Toggle on again
        panel.ToggleEnabled(id);
        Assert.True(_manager.AllBreakpoints[0].Enabled);
    }

    [Fact]
    public void ManagerWindow_EnableAll_EnablesAllBreakpoints()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        var id1 = _manager.AddBreakpoint(new PropertyMatchDto());
        var id2 = _manager.AddBreakpoint(new PropertyMatchDto());
        _manager.SetEnabled(id1, false);
        _manager.SetEnabled(id2, false);

        panel.EnableAll();

        Assert.All(_manager.AllBreakpoints, bp => Assert.True(bp.Enabled));
    }

    [Fact]
    public void ManagerWindow_DisableAll_DisablesAllBreakpoints()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        _manager.AddBreakpoint(new PropertyMatchDto());
        _manager.AddBreakpoint(new PropertyMatchDto());

        panel.DisableAll();

        Assert.All(_manager.AllBreakpoints, bp => Assert.False(bp.Enabled));
    }

    // ── P8T1 — BreakpointConditionSummarizer ─────────────────────────────────

    [Fact]
    public void ConditionSummarizer_Null_ReturnsNone()
    {
        Assert.Equal("(none)", BreakpointConditionSummarizer.Summarize(null));
    }

    [Fact]
    public void ConditionSummarizer_PropertyMatch_ContainsComponentName()
    {
        ComponentTypeRegistry.Register<StubComponent>();
        var dto = new PropertyMatchDto { ComponentType = typeof(StubComponent) };
        var summary = BreakpointConditionSummarizer.Summarize(dto);
        Assert.Contains("StubComponent", summary);
    }

    [Fact]
    public void ConditionSummarizer_Compound_ContainsOperatorAndCount()
    {
        var dto = new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new System.Collections.Generic.List<SearchPredicateDto>
            {
                new PropertyMatchDto(),
                new PropertyMatchDto(),
            },
        };
        var summary = BreakpointConditionSummarizer.Summarize(dto);
        Assert.Contains("Compound", summary);
        Assert.Contains("And", summary);
        Assert.Contains("2", summary);
    }
}

// Test-only unmanaged component
[ComponentId(221)]
file struct StubComponent { public int X; }
```

**Note**: `StubComponent` uses `[ComponentId(221)]`. Check if 221 is already used by another component in the test project. If so, use a different value (e.g. 222). You should check the `[ComponentId]` values in existing test files. In any case, use `file struct` scoping so it doesn't conflict with other test files.

### 8b. `PredicateBuilderStateTests.cs` — `Hrot.Diagnostics.Breakpoints.Tests`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PredicateBuilderStateTests.cs`

```csharp
[Collection("ComponentRegistry")]
public sealed class PredicateBuilderStateTests
{
    private readonly DataBreakpointManager _manager;

    public PredicateBuilderStateTests()
    {
        ComponentTypeRegistry.Clear();
        var (mgr, _, _, _) = ManagerFactory.Create();
        _manager = mgr;
    }

    // ── P8T2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PredicateBuilder_SwitchingMode_DiscardsAndOpensNewSession()
    {
        var state = new PredicateBuilderState();

        // Starts in Component mode with a PropertyMatchDto
        Assert.Equal(PredicateMode.Component, state.CurrentMode);
        Assert.IsType<PropertyMatchDto>(state.CurrentDto);

        // Switch to BehaviorParam
        state.SwitchMode(PredicateMode.BehaviorParam);
        Assert.Equal(PredicateMode.BehaviorParam, state.CurrentMode);
        Assert.IsType<BehaviorParamPredicateDto>(state.CurrentDto);
    }

    [Fact]
    public void PredicateBuilder_SwitchingToSameMode_IsNoOp()
    {
        var state = new PredicateBuilderState();
        var originalDto = state.CurrentDto;

        state.SwitchMode(PredicateMode.Component); // same mode
        Assert.Same(originalDto, state.CurrentDto); // same DTO instance
    }

    [Fact]
    public void PredicateBuilder_CompileAndApply_RemountsDelegate()
    {
        // Register a breakpoint with one condition
        var originalPred = new PropertyMatchDto
        {
            ComponentType = typeof(StubPredicateBuilderComponent),
            PropertyPath  = "Value",
            Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
        };
        ComponentTypeRegistry.Register<StubPredicateBuilderComponent>();
        var id = _manager.AddBreakpoint(originalPred, displayName: "original");

        // Capture the original delegate reference (pointer-based equality check)
        var originalDelegateHash = _manager.MountedComponentPredicates
            .First(x => x.Breakpoint.Id == id)
            .Compiled.Delegate.GetHashCode();

        // Load the breakpoint and switch to BehaviorParam (completely different condition)
        var state = new PredicateBuilderState();
        state.LoadBreakpoint(_manager.AllBreakpoints.First(b => b.Id == id));
        state.SwitchMode(PredicateMode.BehaviorParam);
        state.Apply(id, _manager);

        // Condition is now BehaviorParamPredicateDto
        var updated = _manager.AllBreakpoints.First(b => b.Id == id);
        Assert.IsType<BehaviorParamPredicateDto>(updated.Condition);

        // The compiled delegate should be gone (BehaviorParam currently compiles to null
        // since it requires a resolver — but the important thing is the old component
        // predicate was unmounted)
        Assert.DoesNotContain(_manager.MountedComponentPredicates, x => x.Breakpoint.Id == id);
    }

    [Fact]
    public void PredicateBuilder_LoadBreakpoint_InfersMode()
    {
        var bp = new Breakpoint
        {
            Id        = new BreakpointId(99),
            Condition = new CompoundPredicateDto(),
            Enabled   = true,
        };

        var state = new PredicateBuilderState();
        state.LoadBreakpoint(bp);

        Assert.Equal(PredicateMode.Compound, state.CurrentMode);
        Assert.IsType<CompoundPredicateDto>(state.CurrentDto);
    }

    [Fact]
    public void PredicateBuilder_AllModes_ProduceExpectedDtoType()
    {
        var state = new PredicateBuilderState();
        foreach (PredicateMode mode in Enum.GetValues<PredicateMode>())
        {
            state.SwitchMode(mode);
            Assert.NotNull(state.CurrentDto); // every mode must produce a non-null DTO
        }
    }
}

[ComponentId(222)]
file struct StubPredicateBuilderComponent { public int Value; }
```

### 8c. `JsonClipboardTests.cs` — `Hrot.Diagnostics.Breakpoints.Tests`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/JsonClipboardTests.cs`

```csharp
public sealed class JsonClipboardTests
{
    // No [Collection("ComponentRegistry")] needed — no ECS component registration here.

    [Fact]
    public void JSON_CopyPaste_RoundTrip_PreservesAllFields()
    {
        // Build a compound predicate with mixed child types
        var original = new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new System.Collections.Generic.List<SearchPredicateDto>
            {
                new PropertyMatchDto
                {
                    PropertyPath = "Health",
                    Predicate    = new NumericPredicateDto { MinValue = 0, MaxValue = 50 },
                },
                new BehaviorParamPredicateDto
                {
                    BehaviorId = 12345,
                },
                new ExternalHitTagPredicateDto { Tag = "BP:abc123" },
            },
            ReadOnlyChildIndices = new System.Collections.Generic.List<int> { 0 },
        };

        // Serialize → deserialize
        string json = BreakpointJsonClipboard.Serialize(original);
        var restored = BreakpointJsonClipboard.TryDeserialize(json);

        Assert.NotNull(restored);
        var compound = Assert.IsType<CompoundPredicateDto>(restored);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(3, compound.Conditions.Count);

        // Child 0: PropertyMatchDto
        var child0 = Assert.IsType<PropertyMatchDto>(compound.Conditions[0]);
        Assert.Equal("Health", child0.PropertyPath);
        var numPred = Assert.IsType<NumericPredicateDto>(child0.Predicate);
        Assert.Equal(0.0, numPred.MinValue);
        Assert.Equal(50.0, numPred.MaxValue);

        // Child 1: BehaviorParamPredicateDto
        var child1 = Assert.IsType<BehaviorParamPredicateDto>(compound.Conditions[1]);
        Assert.Equal(12345ul, (ulong)child1.BehaviorId);

        // Child 2: ExternalHitTagPredicateDto
        var child2 = Assert.IsType<ExternalHitTagPredicateDto>(compound.Conditions[2]);
        Assert.Equal("BP:abc123", child2.Tag);

        // ReadOnlyChildIndices preserved
        Assert.Single(compound.ReadOnlyChildIndices);
        Assert.Equal(0, compound.ReadOnlyChildIndices[0]);
    }

    [Fact]
    public void JSON_TryDeserialize_InvalidJson_ReturnsNull()
    {
        var result = BreakpointJsonClipboard.TryDeserialize("{ not valid json {{{");
        Assert.Null(result);
    }

    [Fact]
    public void JSON_TryDeserialize_UnknownType_ReturnsNull()
    {
        // A JSON object with a $type discriminator that doesn't match any known DTO
        const string badJson = """{"$type":"UnknownType","someField":1}""";
        var result = BreakpointJsonClipboard.TryDeserialize(badJson);
        Assert.Null(result);
    }

    [Fact]
    public void JSON_Serialize_ExternalHitTag_ProducesCorrectDiscriminator()
    {
        var dto = new ExternalHitTagPredicateDto { Tag = "my-tag" };
        string json = BreakpointJsonClipboard.Serialize(dto);

        // Verify the $type discriminator is included (needed for poly deserialization)
        Assert.Contains("ExternalHitTag", json);
        Assert.Contains("my-tag", json);
    }
}
```

**Note on `BehaviorParamPredicateDto.BehaviorId`**: Check the actual type of `BehaviorId` field in `BehaviorParamPredicateDto` (it might be `ulong` or `long` or some custom type). Adjust the assertion accordingly.

---

## 9. Build and test

After implementing all changes, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
```

Expected: ≥ 87 tests passing (72 previous + 15 new). Zero warnings (TreatWarningsAsErrors).

---

## Implementation notes

1. **`StubComponent` ComponentId values**: In `ManagerWindowTests.cs`, use `[ComponentId(221)]` for `StubComponent`. In `PredicateBuilderStateTests.cs`, use `[ComponentId(222)]` for `StubPredicateBuilderComponent`. If those IDs conflict with other test files (check all `[ComponentId(xxx)]` in the test project first), use different values. The safe range is 200-250; HealthComponent in ExternalHitTagTests.cs used 220 — avoid that.

2. **`BreakpointId` constructor**: `BreakpointId` has an internal constructor. In `PredicateBuilder_LoadBreakpoint_InfersMode`, create the `Breakpoint` by using `_manager.AddBreakpoint(...)` to get a real ID, then look it up in `_manager.AllBreakpoints`. Or use `new Breakpoint { Id = new BreakpointId(99), ... }` if the constructor is accessible. If it's internal, create via the manager and read back from `AllBreakpoints`.

3. **`BehaviorParamPredicateDto.BehaviorId` type**: Read `SearchPredicateDto.cs` to confirm the type. If it's a custom `BehaviorHash` struct rather than a plain integer, adjust the round-trip assertion.

4. **`DataBreakpointManagerPanel` `using` imports**: The panel is in `Hrot.Presentation` which has `Fdp.Presentation` and `Hrot.Diagnostics.Breakpoints` (new). Import `Fdp.Toolkit.ReplayBrowser.Search` for DTO types.

5. **`BreakpointId` in `BreakpointId.Internal` constructor**: If `BreakpointId(int)` is `internal`, the `PredicateBuilder_LoadBreakpoint_InfersMode` test cannot use `new BreakpointId(99)` directly. Instead, do:
   ```csharp
   // In the test (accessible because InternalsVisibleTo already includes Hrot.Diagnostics.Breakpoints.Tests)
   var realId = _manager.AddBreakpoint(new CompoundPredicateDto());
   var bp = _manager.AllBreakpoints.First(b => b.Id == realId);
   var state = new PredicateBuilderState();
   state.LoadBreakpoint(bp);
   Assert.Equal(PredicateMode.Compound, state.CurrentMode);
   ```
   This avoids the need for internal access to `BreakpointId(int)`.

6. **`TemporalStatusBannerState` in tests**: `TemporalStatusBannerState` is in `Hrot.Diagnostics.Breakpoints` (no ImGui), so `ManagerWindowTests` can use it without ImGui.

7. **`DataBreakpointManagerWindow` and `DataBreakpointManagerPanel` are in `Hrot.Presentation`**: For the test project to access them, `Hrot.Diagnostics.Breakpoints.Tests.csproj` must reference `Hrot.Presentation`. The window and panel are `public sealed` so no `InternalsVisibleTo` is needed for basic instantiation. But for `internal` seam methods (`AddBreakpoint()`, `ToggleEnabled()`, etc.), add `InternalsVisibleTo` for `Hrot.Diagnostics.Breakpoints.Tests` in `Hrot.Presentation.csproj` (step 5a).

8. **`PredicateBuilder_CompileAndApply_RemountsDelegate` test**: When `BehaviorParamPredicateDto` is applied via `UpdateCondition`, the `TryMountDelegate` switch handles it (the existing `case BehaviorParamPredicateDto _:` falls through to the component-predicate path and calls `_predicateCompiler.CompileComponentPredicate`). If the compiled delegate is null/no-op for BehaviorParam, the test still succeeds as long as the COMPONENT predicate from the original `PropertyMatchDto` is UNMOUNTED. Assert via `_manager.MountedComponentPredicates.All(x => x.Breakpoint.Id != id)`.

9. **`WindowScope` access**: `WindowScope` is in `Fdp.Presentation.WindowManager` namespace, which is already in `Hrot.Presentation`'s transitive dependencies via `Fdp.Presentation`. The `Hrot.Diagnostics.Breakpoints.Tests` project needs `using Fdp.Presentation.WindowManager;` for `ManagerWindow_PerspectiveBound_WindowHasCorrectScopeAndPerspective` test.
