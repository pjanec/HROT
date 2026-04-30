using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

public class EventBrowserPanel
{
    private class CapturedEvent
    {
        public uint   Frame;
        public string TypeName  = "";
        public Type   EventType = typeof(object);
        public bool   IsManaged;
        public string Summary   = "";
        public object? RawEvent;   // kept for property-tree rendering
    }

    // ── Per-bus filter state ──────────────────────────────────────────────────
    // Each registered bus gets its own KnownTypes / DisabledTypes so that filter
    // settings survive a bus switch and never bleed between buses.
    private class BusFilterState
    {
        public HashSet<string> KnownTypes    { get; } = new();
        public HashSet<string> DisabledTypes { get; } = new();
    }

    // ── Bus registry ──────────────────────────────────────────────────────────
    private readonly Dictionary<string, FdpEventBus>    _registeredBuses  = new();
    private readonly Dictionary<string, BusFilterState> _busFilterStates  = new();
    private string?        _selectedBusName;
    private BusFilterState? _activeFilterState;

    private readonly List<CapturedEvent> _history = new();
    private CapturedEvent? _selectedEvent;
    private bool _paused;
    private int  _capacity = 500;

    /// <summary>
    /// Registers an event bus under a display name for the UI combo box.
    /// The first bus registered becomes the default selection.
    /// </summary>
    public void RegisterBus(string displayName, FdpEventBus bus)
    {
        if (bus == null) return;
        _registeredBuses[displayName] = bus;
        if (!_busFilterStates.ContainsKey(displayName))
            _busFilterStates[displayName] = new BusFilterState();
        if (_selectedBusName == null)
        {
            _selectedBusName   = displayName;
            _activeFilterState = _busFilterStates[displayName];
        }
    }

    public void Update(uint currentFrame)
    {
        if (_paused || _selectedBusName == null || _activeFilterState == null) return;
        if (!_registeredBuses.TryGetValue(_selectedBusName, out var activeBus)) return;

        foreach (var inspector in activeBus.GetDebugInspectors())
        {
            if (inspector.Count == 0) continue;

            bool isManaged = !inspector.EventType.IsValueType;

            foreach (var evt in inspector.InspectReadBuffer())
            {
                string typeName = inspector.EventType.Name;
                _activeFilterState.KnownTypes.Add(typeName);

                // Compute summary using a custom renderer if registered
                var renderer = ImGuiRendererRegistry.GetRenderer(inspector.EventType);
                string summary = renderer?.GetSummary(evt)
                              ?? GetGenericEventSummary(evt, inspector.EventType);

                var record = new CapturedEvent
                {
                    Frame     = currentFrame,
                    TypeName  = typeName,
                    EventType = inspector.EventType,
                    IsManaged = isManaged,
                    Summary   = summary,
                    RawEvent  = evt,
                };

                _history.Add(record);
            }
        }

        // Trim history
        if (_history.Count > _capacity)
        {
            int removeCount = _history.Count - _capacity;
            if (removeCount > 0)
            {
                if (_selectedEvent != null && _history.IndexOf(_selectedEvent) < removeCount)
                    _selectedEvent = null;
                _history.RemoveRange(0, removeCount);
            }
        }
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
        DrawToolbar();
        ImGuiApi.Separator();

        if (ImGuiApi.BeginTable("EventBrowserLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInner))
        {
            ImGuiApi.TableSetupColumn("Event List", ImGuiTableColumnFlags.WidthFixed, 400);
            ImGuiApi.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            
            ImGuiApi.TableNextRow();
            
            ImGuiApi.TableSetColumnIndex(0);
            DrawEventList();

            ImGuiApi.TableSetColumnIndex(1);
            DrawEventDetails();

            ImGuiApi.EndTable();
        }
    }

    private void DrawToolbar()
    {
        // ── Bus selector combo ─────────────────────────────────────────────
        if (_registeredBuses.Count > 0)
        {
            var names = _registeredBuses.Keys.ToArray();
            int selectedIdx = _selectedBusName != null ? Array.IndexOf(names, _selectedBusName) : 0;
            if (selectedIdx < 0) selectedIdx = 0;

            ImGuiApi.SetNextItemWidth(150);
            if (ImGuiApi.Combo("##Bus", ref selectedIdx, names, names.Length))
            {
                string newName = names[selectedIdx];
                if (newName != _selectedBusName)
                {
                    _selectedBusName   = newName;
                    _activeFilterState = _busFilterStates[_selectedBusName];
                    // Clear history and selection to prevent cross-contamination
                    // between buses; filter state for each bus is preserved.
                    _history.Clear();
                    _selectedEvent = null;
                }
            }
            ImGuiApi.SameLine();
        }

        if (ImGuiApi.Button("Clear"))
        {
            _history.Clear();
            _selectedEvent = null;
        }

        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Pause", ref _paused);

        // ── Event-type filter ─────────────────────────────────────────────
        ImGuiApi.SameLine();
        int hiddenCount = _activeFilterState?.DisabledTypes.Count ?? 0;
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
        int visible = _activeFilterState != null
            ? _history.Count(e => !_activeFilterState.DisabledTypes.Contains(e.TypeName))
            : _history.Count;
        ImGuiApi.Text($"| Showing: {visible} / {_history.Count}");

        if (_selectedEvent != null)
        {
            ImGuiApi.SameLine();
            ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), $"| {_selectedEvent.TypeName}");
        }
    }

    private void DrawFilterPopup()
    {
        if (!ImGuiApi.BeginPopup("##EventTypeFilter")) return;
        if (_activeFilterState == null) { ImGuiApi.EndPopup(); return; }

        ImGuiApi.TextDisabled("Show / hide event types:");
        ImGuiApi.Separator();

        if (ImGuiApi.SmallButton("Enable All"))
            _activeFilterState.DisabledTypes.Clear();
        ImGuiApi.SameLine();
        if (ImGuiApi.SmallButton("Disable All"))
        {
            foreach (var t in _activeFilterState.KnownTypes)
                _activeFilterState.DisabledTypes.Add(t);
        }
        ImGuiApi.Separator();

        foreach (var typeName in _activeFilterState.KnownTypes.OrderBy(n => n))
        {
            bool visible = !_activeFilterState.DisabledTypes.Contains(typeName);
            if (ImGuiApi.Checkbox(typeName, ref visible))
            {
                if (visible) _activeFilterState.DisabledTypes.Remove(typeName);
                else         _activeFilterState.DisabledTypes.Add(typeName);
            }
        }

        ImGuiApi.EndPopup();
    }

    private void DrawEventList()
    {
        if (ImGuiApi.BeginChild("EventListScroll", new Vector2(0, 0)))
        {
            if (_history.Count == 0)
            {
                ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No events captured.");
            }
            else
            {
                if (ImGuiApi.BeginTable("EventListTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGuiApi.TableSetupColumn("Frame/Type", ImGuiTableColumnFlags.WidthFixed, 180);
                    ImGuiApi.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);

                    for (int i = _history.Count - 1; i >= 0; i--)
                    {
                        var evt = _history[i];
                        // Apply event type filter (disabled types are hidden but not deleted)
                        if (_activeFilterState != null && _activeFilterState.DisabledTypes.Contains(evt.TypeName)) continue;

                        bool isSelected = (evt == _selectedEvent);

                        ImGuiApi.TableNextRow();
                        ImGuiApi.TableSetColumnIndex(0);

                        var color = evt.IsManaged
                            ? new Vector4(0.5f, 1f, 0.5f, 1f)
                            : new Vector4(1f, 1f, 1f, 1f);

                        if (isSelected)
                            ImGuiApi.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
                        else
                            ImGuiApi.PushStyleColor(ImGuiCol.Text, color);

                        string label = $"[{evt.Frame}] {evt.TypeName}##{i}";
                        
                        // Use SpanAllColumns to make the whole row clickable if possible, or just the first cell
                        if (ImGuiApi.Selectable(label, isSelected, ImGuiSelectableFlags.SpanAllColumns))
                        {
                            _selectedEvent = evt;
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
        ImGuiApi.EndChild();
    }

    private void DrawEventDetails()
    {
        if (ImGuiApi.BeginChild("EventDetailsScroll", new Vector2(0, 0)))
        {
            if (_selectedEvent == null)
            {
                ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Select an event to view details.");
            }
            else
            {
                var evt = _selectedEvent;
                
                ImGuiApi.TextColored(new Vector4(0, 1, 1, 1), evt.TypeName);

                // ── Copy JSON button ───────────────────────────────────
                ImGuiApi.SameLine();
                if (ImGuiApi.Button("Copy JSON"))
                {
                    var dumpDict = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["EventType"] = evt.EventType.FullName ?? evt.EventType.Name,
                        ["Frame"]     = evt.Frame,
                        ["Payload"]   = evt.RawEvent,
                    };
                    var jsonOpts = new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        IncludeFields = true,
                    };
                    string json = System.Text.Json.JsonSerializer.Serialize(dumpDict, jsonOpts);
                    ImGuiApi.SetClipboardText(json);
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

    // ── Summary helpers ───────────────────────────────────────────────────────

    private static string GetGenericEventSummary(object evt, Type type)
    {
        if (evt == null) return "null";

        // Try primitive fields first (struct events)
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum)
            .Take(3)
            .Select(f => $"{f.Name}:{ImGuiPropertyTree.FormatLeaf(f.GetValue(evt))}")
            .ToList();

        if (fields.Count > 0)
            return string.Join("  ", fields);

        // Fall back to non-indexed public properties (managed events)
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 &&
                       (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum))
            .Take(3)
            .Select(p =>
            {
                try   { return $"{p.Name}:{ImGuiPropertyTree.FormatLeaf(p.GetValue(evt))}"; }
                catch { return $"{p.Name}:<err>"; }
            })
            .ToList();

        if (props.Count > 0)
            return string.Join("  ", props);

        return evt.ToString() ?? "null";
    }
}
