using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Renderers;
using FDP.Toolkit.ImGui.Utils;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Panels;

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

    private readonly List<CapturedEvent> _history = new();
    private CapturedEvent? _selectedEvent;
    private bool _paused;
    private int  _capacity = 500;

    // ── Event type filter ─────────────────────────────────────────────────────
    // Types in this set are hidden from the event list.
    // Historical records are preserved so they appear again when re-enabled.
    private readonly HashSet<string> _disabledTypes = new();
    // All type names ever seen – drives the filter popup checkbox list.
    private readonly HashSet<string> _knownTypes = new();

    public void Update(FdpEventBus bus, uint currentFrame)
    {
        if (_paused || bus == null) return;

        foreach (var inspector in bus.GetDebugInspectors())
        {
            if (inspector.Count == 0) continue;
            
            bool isManaged = !inspector.EventType.IsValueType;

            foreach (var evt in inspector.InspectReadBuffer())
            {
                string typeName = inspector.EventType.Name;
                _knownTypes.Add(typeName);

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
                {
                    _selectedEvent = null;
                }
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
        if (ImGuiApi.Button("Clear"))
        {
            _history.Clear();
            _selectedEvent = null;
        }

        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Pause", ref _paused);

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
        int visible = _history.Count(e => !_disabledTypes.Contains(e.TypeName));
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

        ImGuiApi.TextDisabled("Show / hide event types:");
        ImGuiApi.Separator();

        if (ImGuiApi.SmallButton("Enable All"))
            _disabledTypes.Clear();
        ImGuiApi.SameLine();
        if (ImGuiApi.SmallButton("Disable All"))
        {
            foreach (var t in _knownTypes) _disabledTypes.Add(t);
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
                        if (_disabledTypes.Contains(evt.TypeName)) continue;

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
