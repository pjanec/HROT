using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Serialization;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>⭐ One event row, projected for the dump. Mirrors <see cref="EventBrowserPanel.DrawEventList"/>'s
/// filter (provider + disabled-type + current-frame-only), newest-first, capped at
/// <see cref="EventBrowserPanelViewModel.MaxRows"/>.</summary>
public sealed record EventBrowserRowViewModel(uint Frame, string TypeName, string ProviderName, string Summary, bool IsManaged);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="EventBrowserPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
public sealed record EventBrowserPanelViewModel(
    string PanelId,
    string PanelKind,
    bool Paused,
    bool CurrentFrameOnly,
    string SelectedProvider,
    int TotalEventCount,
    int VisibleEventCount,
    IReadOnlyList<string> DisabledTypes,
    IReadOnlyList<EventBrowserRowViewModel> Events,
    int SelectedCount,
    string? SelectedEventTypeName) : IPanelViewModel
{
    /// <summary>⭐ Cap applied to <see cref="Events"/> — mirrors <c>MessageLogPanelViewModel.MaxRowsPerTab</c>:
    /// the dump is for assertions/conformance, not a full export.</summary>
    public const int MaxRows = 500;

    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

public class EventBrowserPanel
{
    private IDiagnosticEventHistoryService? _historyService;

    /// <summary>
    /// The diagnostic event history service used to fetch snapshots.
    /// Can be swapped at runtime (e.g. when the federation manager changes the active node).
    /// </summary>
    public IDiagnosticEventHistoryService? HistoryService
    {
        get => _historyService;
        set => _historyService = value;
    }

    // ── Per-type filter state ─────────────────────────────────────────────
    private readonly HashSet<string> _knownTypes    = new();
    private readonly HashSet<string> _disabledTypes = new();
    private readonly HashSet<string> _knownProviders = new();
    private string _selectedProvider = "World";
    
    public string SelectedProvider
    {
        get => _selectedProvider;
        set => _selectedProvider = value ?? "All";
    }

    /// <summary>
    /// Optional delegate to provide the exact current frame index from the host application.
    /// Required for accurate "Current Frame Only" filtering when the current frame has no events.
    /// </summary>
    public Func<uint>? CurrentFrameProvider { get; set; }

    // ── Multi-select state ────────────────────────────────────────────────
    internal readonly HashSet<CapturedEventDto> _selectedEvents = new();
    internal int _lastClickedIndex = -1;

    private bool _paused;
    private bool _currentFrameOnly;
    private CapturedEventDto[] _cachedSnapshot = Array.Empty<CapturedEventDto>();

    /// <summary>
    /// Fired when the user clicks a clickable entity-handle link inside an event payload.
    /// Wire this in the composition root to propagate selection to the entity history.
    /// </summary>
    public Action<Entity>? OnEntityLinkClicked { get; set; }
    
    /// <summary>
    /// Fired when the user selects the "Step Forward and Diff Target" causality action.
    /// </summary>
    public Action<int, Entity>? OnCausalityJumpRequested { get; set; }

    private static Entity? TryExtractTargetEntity(object? rawEvent)
    {
        if (rawEvent == null) return null;
        var type = rawEvent.GetType();
        
        var field = type.GetField("Entity", BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(Entity))
            return (Entity)field.GetValue(rawEvent)!;

        var prop = type.GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.PropertyType == typeof(Entity))
            return (Entity)prop.GetValue(rawEvent)!;

        return null;
    }

    /// <summary>Creates a panel with no history service; set <see cref="HistoryService"/> before drawing.</summary>
    public EventBrowserPanel() { _historyService = null; }

    public EventBrowserPanel(IDiagnosticEventHistoryService historyService)
    {
        _historyService = historyService;
    }

    /// <param name="title">Optional window title override. Default: "Event Browser".</param>
    public void Draw(string title = "Event Browser")
    {
        ImGuiApi.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);
        if (!ImGuiApi.Begin(title, ImGuiWindowFlags.None)) { ImGuiApi.End(); return; }
        DrawContent();
        ImGuiApi.End();
    }

    /// <summary>
    /// Renders the browser content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        // Fetch a snapshot from the service each frame unless paused.
        if (!_paused && _historyService != null)
            _cachedSnapshot = _historyService.GetHistory();

        CapturedEventDto[] snapshot = _cachedSnapshot;
        if (snapshot.Length == 0 && _selectedEvents.Count > 0)
        {
            _selectedEvents.Clear();
            _lastClickedIndex = -1;
        }

        // Update known types for the filter popup.
        foreach (var e in snapshot)
        {
            _knownTypes.Add(e.TypeName);
            _knownProviders.Add(e.ProviderName);
        }

        DrawToolbar(snapshot);
        ImGuiApi.Separator();

        if (ImGuiApi.BeginTable("EventBrowserLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInner))
        {
            ImGuiApi.TableSetupColumn("Event List", ImGuiTableColumnFlags.WidthFixed, 400);
            ImGuiApi.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);

            ImGuiApi.TableNextRow();

            ImGuiApi.TableSetColumnIndex(0);
            DrawEventList(snapshot);

            ImGuiApi.TableSetColumnIndex(1);
            DrawEventDetails();

            ImGuiApi.EndTable();
        }
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>
    /// ⭐⭐⭐ <b>BUILD — a pure projection of the (filtered, newest-first) event list and the current
    /// selection. No ImGui.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
    /// ⭐ Mirrors <see cref="DrawContent"/>'s own snapshot-refresh (pause-gated) and
    /// <see cref="DrawEventList"/>'s filter (provider + disabled-type + current-frame-only) by hand,
    /// kept in sync since that filter is private to this class. ⚠ Calling this independently of
    /// <see cref="DrawContent"/> re-reads the history service exactly as <c>DrawContent</c> does —
    /// same duplicate-fetch shape as <c>MessageLogPanel.BuildViewModel</c>, tolerated because the
    /// service is a read of the current buffer, not a mutating pop.
    /// </summary>
    public EventBrowserPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        if (!_paused && _historyService != null)
            _cachedSnapshot = _historyService.GetHistory();

        CapturedEventDto[] snapshot = _cachedSnapshot;
        foreach (var e in snapshot)
        {
            _knownTypes.Add(e.TypeName);
            _knownProviders.Add(e.ProviderName);
        }

        uint? targetFrame = CurrentFrameProvider?.Invoke();
        if (_currentFrameOnly && !targetFrame.HasValue && snapshot.Length > 0)
            targetFrame = snapshot.Max(e => e.Frame);

        var rows = new List<EventBrowserRowViewModel>();
        int visibleCount = 0;
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            var evt = snapshot[i];
            if (_currentFrameOnly && targetFrame.HasValue && evt.Frame != targetFrame.Value)
                continue;
            if (_selectedProvider != "All" && evt.ProviderName != _selectedProvider)
                continue;
            if (_disabledTypes.Contains(evt.TypeName))
                continue;

            visibleCount++;
            if (rows.Count < EventBrowserPanelViewModel.MaxRows)
                rows.Add(new EventBrowserRowViewModel(evt.Frame, evt.TypeName, evt.ProviderName, evt.Summary, evt.IsManaged));
        }

        int selCount = _selectedEvents.Count;
        string? selectedTypeName = selCount == 1 ? _selectedEvents.First().TypeName : null;

        return new EventBrowserPanelViewModel(
            panelId, panelKind, _paused, _currentFrameOnly, _selectedProvider,
            snapshot.Length, visibleCount, _disabledTypes.OrderBy(t => t, StringComparer.Ordinal).ToList(),
            rows, selCount, selectedTypeName);
    }

    private void DrawToolbar(CapturedEventDto[] snapshot)
    {
        ImGuiApi.Checkbox("Pause", ref _paused);
        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Current Frame Only", ref _currentFrameOnly);

        ImGuiApi.SameLine();
        ImGuiApi.SetNextItemWidth(150f);
        if (ImGuiApi.BeginCombo("##Provider", _selectedProvider))
        {
            if (ImGuiApi.Selectable("All", _selectedProvider == "All"))
                _selectedProvider = "All";

            var providerOptions = new HashSet<string>(_knownProviders, StringComparer.OrdinalIgnoreCase)
            {
                "World",
                "Orchestration",
                "Interaction" // quarantined UI interaction bus
            };
            foreach (var provider in providerOptions.OrderBy(p => p))
            {
                if (ImGuiApi.Selectable(provider, _selectedProvider == provider))
                    _selectedProvider = provider;
            }
            ImGuiApi.EndCombo();
        }

        // ── Event-type filter ─────────────────────────────────────────────
        ImGuiApi.SameLine();
        int hiddenCount = _disabledTypes.Count;
        string filterLabel = hiddenCount > 0
            ? $"Filter [{hiddenCount} hidden]###FilterBtn"
            : "Filter###FilterBtn";
        if (hiddenCount > 0)
            ImGuiApi.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.3f, 0.1f, 1f));
        if (ImGuiApi.Button(filterLabel))
            ImGuiApi.OpenPopup("##EventTypeFilter");
        if (hiddenCount > 0)
            ImGuiApi.PopStyleColor();

        DrawFilterPopup();


        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Clear"))
        {
            _historyService?.ClearHistory();
            _selectedEvents.Clear();
            _lastClickedIndex = -1;
            _knownTypes.Clear();
            _knownProviders.Clear();
            _cachedSnapshot = Array.Empty<CapturedEventDto>();
        }

        ImGuiApi.SameLine();
        uint? targetFrame = CurrentFrameProvider?.Invoke();
        if (_currentFrameOnly && !targetFrame.HasValue && snapshot.Length > 0)
            targetFrame = snapshot.Max(e => e.Frame);

        int visible = snapshot.Count(e =>
            (!_currentFrameOnly || (targetFrame.HasValue && e.Frame == targetFrame.Value))
            &&
            (_selectedProvider == "All" || e.ProviderName == _selectedProvider)
            && !_disabledTypes.Contains(e.TypeName));

        // Select All button — populates selection with all currently visible events.
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Select All"))
        {
            _selectedEvents.Clear();
            foreach (var evt in snapshot)
            {
                if ((_selectedProvider == "All" || evt.ProviderName == _selectedProvider)
                    && !_disabledTypes.Contains(evt.TypeName))
                {
                    _selectedEvents.Add(evt);
                }
            }
            _lastClickedIndex = -1;
        }
        if (ImGuiApi.IsItemHovered())
            ImGuiApi.SetTooltip("Select all visible events (respects provider and type filters)");

        ImGuiApi.SameLine();
        ImGuiApi.Text($"| Showing: {visible} / {snapshot.Length}");

        int selCount = _selectedEvents.Count;
        if (selCount == 1)
        {
            ImGuiApi.SameLine();
            ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), $"| {_selectedEvents.First().TypeName}");
        }
        else if (selCount > 1)
        {
            ImGuiApi.SameLine();
            ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), $"| {selCount} selected");
        }
    }

    private void DrawFilterPopup()
    {
        if (!ImGuiApi.BeginPopup("##EventTypeFilter")) return;

        ImGuiApi.TextDisabled("Show / hide event types:");
        ImGuiApi.Separator();

        if (ImGuiApi.SmallButton("Enable All"))
            _disabledTypes.Clear();
        ImGuiApi.SameLine();
        if (ImGuiApi.SmallButton("Disable All"))
        {
            foreach (var t in _knownTypes)
                _disabledTypes.Add(t);
        }
        ImGuiApi.Separator();

        foreach (var typeName in _knownTypes.OrderBy(n => n))
        {
            bool visible = !_disabledTypes.Contains(typeName);
            if (ImGuiApi.Checkbox(typeName, ref visible))
            {
                if (visible) _disabledTypes.Remove(typeName);
                else         _disabledTypes.Add(typeName);
            }
        }

        ImGuiApi.EndPopup();
    }

    private void DrawEventList(CapturedEventDto[] snapshot)
    {
        if (ImGuiApi.BeginChild("EventListScroll", new Vector2(0, 0)))
        {
            if (snapshot.Length == 0)
            {
                ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No events captured.");
            }
            else
            {
                // Build the filtered view list (newest first), preserving index semantics
                // for Shift+Click range selection.
                var viewList = new List<CapturedEventDto>();
                uint? targetFrame = CurrentFrameProvider?.Invoke();
                if (_currentFrameOnly && !targetFrame.HasValue && snapshot.Length > 0)
                    targetFrame = snapshot.Max(e => e.Frame);

                for (int i = snapshot.Length - 1; i >= 0; i--)
                {
                    var evt = snapshot[i];
                    if (_currentFrameOnly && targetFrame.HasValue && evt.Frame != targetFrame.Value)
                        continue;
                    if ((_selectedProvider == "All" || evt.ProviderName == _selectedProvider)
                        && !_disabledTypes.Contains(evt.TypeName))
                        viewList.Add(evt);
                }

                if (viewList.Count == 0)
                {
                    string message = _currentFrameOnly ? "No events this frame" : "No match for current filters.";
                    ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), message);
                }
                else
                {
                    if (ImGuiApi.BeginTable("EventListTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                    {
                        ImGuiApi.TableSetupColumn("Frame/Type", ImGuiTableColumnFlags.WidthFixed, 180);
                        ImGuiApi.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);

                        bool ctrl  = ImGuiApi.GetIO().KeyCtrl;
                        bool shift = ImGuiApi.GetIO().KeyShift;

                        for (int vi = 0; vi < viewList.Count; vi++)
                        {
                            var evt = viewList[vi];
                            bool isSelected = _selectedEvents.Contains(evt);

                            ImGuiApi.TableNextRow();
                            ImGuiApi.TableSetColumnIndex(0);

                            var color = evt.IsManaged
                                ? new Vector4(0.5f, 1f, 0.5f, 1f)
                                : new Vector4(1f, 1f, 1f, 1f);

                            if (isSelected)
                                ImGuiApi.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
                            else
                                ImGuiApi.PushStyleColor(ImGuiCol.Text, color);

                            // Only show the provider name tag if we are viewing "All" providers
                            string providerTag = _selectedProvider == "All" ? $" [{evt.ProviderName}]" : "";
                            string label = $"[{evt.Frame}]{providerTag} {evt.TypeName}##{vi}";

                            if (ImGuiApi.Selectable(label, isSelected, ImGuiSelectableFlags.SpanAllColumns))
                            {
                                HandleRowClick(viewList, vi, ctrl, shift);
                            }

                            if (ImGuiApi.BeginPopupContextItem($"##ctx_evt_{vi}"))
                            {
                                var targetEntity = TryExtractTargetEntity(evt.RawEvent);
                                if (targetEntity.HasValue && !targetEntity.Value.IsNull)
                                {
                                    if (ImGuiApi.MenuItem("Step Forward and Diff Target"))
                                    {
                                        OnCausalityJumpRequested?.Invoke((int)evt.Frame, targetEntity.Value);
                                    }                                
                                }
                                ImGuiApi.EndPopup();
                            }

                            ImGuiApi.PopStyleColor();

                            if (ImGuiApi.IsItemHovered())
                            {
                                ImGuiApi.SetTooltip(evt.Summary);
                            }

                            ImGuiApi.TableSetColumnIndex(1);
                            ImGuiApi.TextDisabled(evt.Summary);
                        }

                        ImGuiApi.EndTable();
                    }
                }
            }
        }
        ImGuiApi.EndChild();
    }

    /// <summary>
    /// Applies multi-select click logic for a row in the event list.
    /// Exposed as internal for unit testing.
    /// </summary>
    internal void HandleRowClick(List<CapturedEventDto> viewList, int clickedIndex, bool ctrl, bool shift)
    {
        if (clickedIndex < 0 || clickedIndex >= viewList.Count) return;

        if (shift && _lastClickedIndex >= 0 && _lastClickedIndex < viewList.Count)
        {
            // Shift+Click: add inclusive range; do NOT update _lastClickedIndex.
            int lo = Math.Min(_lastClickedIndex, clickedIndex);
            int hi = Math.Max(_lastClickedIndex, clickedIndex);
            for (int i = lo; i <= hi; i++)
                _selectedEvents.Add(viewList[i]);
        }
        else if (ctrl)
        {
            // Ctrl+Click: toggle item; update _lastClickedIndex.
            var item = viewList[clickedIndex];
            if (!_selectedEvents.Remove(item))
                _selectedEvents.Add(item);
            _lastClickedIndex = clickedIndex;
        }
        else
        {
            // Plain click: clear selection, add item, update _lastClickedIndex.
            _selectedEvents.Clear();
            _selectedEvents.Add(viewList[clickedIndex]);
            _lastClickedIndex = clickedIndex;
        }
    }

    /// <summary>
    /// Builds the copy-to-clipboard JSON string for the given events.
    /// Single event → JSON object; multiple events → JSON array sorted by Frame ascending.
    /// Exposed as internal for unit testing.
    /// </summary>
    internal static string BuildCopyJson(IReadOnlyList<CapturedEventDto> events)
    {
        if (events.Count == 1)
        {
            var evt = events[0];
            var dumpDict = new Dictionary<string, object?>
            {
                ["EventType"] = evt.TypeName,
                ["Frame"]     = evt.Frame,
                ["Payload"]   = evt.RawEvent,
            };
            string raw = JsonSerializer.Serialize(dumpDict, FdpJsonOptionsRegistry.Indented);
            return JsonAestheticFormatter.FlattenNumericArrays(raw);
        }
        else
        {
            var sorted = events.OrderBy(e => e.Frame).Select(evt => new Dictionary<string, object?>
            {
                ["EventType"] = evt.TypeName,
                ["Frame"]     = evt.Frame,
                ["Payload"]   = evt.RawEvent,
            }).ToList();
            string raw = JsonSerializer.Serialize(sorted, FdpJsonOptionsRegistry.Indented);
            return JsonAestheticFormatter.FlattenNumericArrays(raw);
        }
    }

    private void DrawEventDetails()
    {
        if (ImGuiApi.BeginChild("EventDetailsScroll", new Vector2(0, 0)))
        {
            int selCount = _selectedEvents.Count;

            if (selCount == 0)
            {
                ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Select an event to view details.");
            }
            else if (selCount > 1)
            {
                ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Multiple events selected.");

                // ── Copy JSON array for multi-select ──────────────────────
                ImGuiApi.SameLine();
                if (ImGuiApi.Button($"Copy JSON ({selCount} items)"))
                {
                    ImGuiApi.SetClipboardText(BuildCopyJson(_selectedEvents.ToList()));
                }
                if (ImGuiApi.IsItemHovered())
                    ImGuiApi.SetTooltip("Copy selected events as a JSON array (sorted by Frame)");
            }
            else
            {
                var evt = _selectedEvents.First();

                ImGuiApi.TextColored(new Vector4(0, 1, 1, 1), evt.TypeName);

                // ── Copy JSON button ───────────────────────────────────
                ImGuiApi.SameLine();
                if (ImGuiApi.Button("Copy JSON"))
                {
                    ImGuiApi.SetClipboardText(BuildCopyJson(new[] { evt }));
                }
                if (ImGuiApi.IsItemHovered())
                    ImGuiApi.SetTooltip("Copy exact event state to clipboard as JSON");
                // ──────────────────────────────────────────────────────

                ImGuiApi.Text($"Frame: {evt.Frame} | {(evt.IsManaged ? "Managed" : "Unmanaged")}");
                ImGuiApi.Separator();

                ImGuiApi.TextDisabled(evt.Summary);
                ImGuiApi.Spacing();
                ImGuiApi.Separator();

                // ── Hierarchical property tree ─────────────────────────────
                if (evt.RawEvent != null)
                    ImGuiPropertyTree.Render(evt.RawEvent, contextType: null);
                else
                    ImGuiApi.TextDisabled("(no raw event data)");
            }
        }
        ImGuiApi.EndChild();
    }
}
