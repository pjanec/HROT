using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Kernel;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Panels;

public class EventBrowserPanel
{
    private class CapturedEvent
    {
        public uint Frame;
        public string TypeName = "";
        public bool IsManaged;
        public string Summary = "";
        public Dictionary<string, string> Details = new();
    }

    private readonly List<CapturedEvent> _history = new();
    private CapturedEvent? _selectedEvent = null;
    private bool _paused = false;
    private int _capacity = 500;

    public void Update(FdpEventBus bus, uint currentFrame)
    {
        if (_paused || bus == null) return;

        foreach (var inspector in bus.GetDebugInspectors())
        {
            if (inspector.Count == 0) continue;
            
            bool isManaged = !inspector.EventType.IsValueType;

            foreach (var evt in inspector.InspectReadBuffer())
            {
                var record = new CapturedEvent
                {
                    Frame = currentFrame,
                    TypeName = inspector.EventType.Name,
                    IsManaged = isManaged,
                    Summary = GetGenericEventSummary(evt),
                    Details = GetGenericEventDetails(evt, inspector.EventType)
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

    public void Draw()
    {
        ImGuiApi.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);

        if (ImGuiApi.Begin("Event Browser", ImGuiWindowFlags.NoCollapse))
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
        ImGuiApi.End();
    }

    private void DrawToolbar()
    {
        if (ImGuiApi.Button("Clear"))
        {
            _history.Clear();
            _selectedEvent = null;
        }
        
        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Pause Capture", ref _paused);
        
        ImGuiApi.SameLine();
        ImGuiApi.Text($"| Total: {_history.Count}");
        
        if (_selectedEvent != null)
        {
            ImGuiApi.SameLine();
            ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), $"| Selected: {_selectedEvent.TypeName}");
        }
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
                
                ImGuiApi.TextWrapped(evt.Summary);
                ImGuiApi.Spacing();
                ImGuiApi.Separator();

                if (ImGuiApi.BeginTable("EventDetailsTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
                {
                    ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGuiApi.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                    ImGuiApi.TableHeadersRow();

                    foreach (var detail in evt.Details)
                    {
                        ImGuiApi.TableNextRow();
                        ImGuiApi.TableSetColumnIndex(0);
                        ImGuiApi.TextColored(new Vector4(0.7f, 1f, 1f, 1f), detail.Key);
                        
                        ImGuiApi.TableSetColumnIndex(1);
                        ImGuiApi.TextWrapped(detail.Value);
                    }

                    ImGuiApi.EndTable();
                }
            }
        }
        ImGuiApi.EndChild();
    }

    private string GetGenericEventSummary(object evt)
    {
        if (evt == null) return "null";

        var type = evt.GetType();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum)
            .Take(3)
            .Select(p => $"{p.Name}: {p.GetValue(evt)}")
            .ToList();

        if (props.Count == 0 && type.IsValueType)
        {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum)
                .Take(3)
                .Select(f => $"{f.Name}: {f.GetValue(evt)}")
                .ToList();
                props.AddRange(fields);
        }

        if (props.Count > 0)
            return string.Join(", ", props);

        return evt.ToString() ?? "null";
    }

    private Dictionary<string, string> GetGenericEventDetails(object evt, Type type)
    {
        var details = new Dictionary<string, string>();
        if (evt == null) return details;

        if (type.IsValueType)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = field.GetValue(evt);
                details[field.Name] = FormatValue(value);
            }
        }
        else
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (prop.GetCustomAttributes(true).Any(a => a.GetType().Name == "IgnoreMemberAttribute"))
                        continue;
                    var value = prop.GetValue(evt);
                    details[prop.Name] = FormatValue(value);
                }
                catch
                {
                    details[prop.Name] = "<error>";
                }
            }
        }

        return details;
    }

    private string FormatValue(object? value)
    {
        if (value == null) return "null";
        if (value is Vector2 v2) return $"({v2.X:F2}, {v2.Y:F2})";
        if (value is Vector3 v3) return $"({v3.X:F2}, {v3.Y:F2}, {v3.Z:F2})";
        if (value is float f) return f.ToString("F2");
        if (value is double d) return d.ToString("F2");
        return value.ToString() ?? "null";
    }
}
