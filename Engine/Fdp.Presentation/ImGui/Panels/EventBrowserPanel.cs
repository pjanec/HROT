using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Serialization;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

public class EventBrowserPanel
{
    private readonly IDiagnosticEventHistoryService _historyService;

    // ── Per-type filter state ─────────────────────────────────────────────
    private readonly HashSet<string> _knownTypes    = new();
    private readonly HashSet<string> _disabledTypes = new();

    private CapturedEventDto? _selectedEvent;
    private bool _paused;

    public EventBrowserPanel(IDiagnosticEventHistoryService historyService)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
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
        // Fetch a snapshot from the service each frame (copy-under-lock is O(N) only).
        CapturedEventDto[] snapshot = _paused
            ? Array.Empty<CapturedEventDto>()
            : _historyService.GetHistory();

        // Update known types for the filter popup.
        foreach (var e in snapshot)
            _knownTypes.Add(e.TypeName);

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

    private void DrawToolbar(CapturedEventDto[] snapshot)
    {
        if (ImGuiApi.Button("Clear"))
        {
            _historyService.ClearHistory();
            _selectedEvent = null;
            _knownTypes.Clear();
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
        int visible = snapshot.Count(e => !_disabledTypes.Contains(e.TypeName));
        ImGuiApi.Text($"| Showing: {visible} / {snapshot.Length}");

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
                if (ImGuiApi.BeginTable("EventListTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGuiApi.TableSetupColumn("Frame/Type", ImGuiTableColumnFlags.WidthFixed, 180);
                    ImGuiApi.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);

                    for (int i = snapshot.Length - 1; i >= 0; i--)
                    {
                        var evt = snapshot[i];
                        // Apply event type filter (disabled types are hidden but not deleted)
                        if (_disabledTypes.Contains(evt.TypeName)) continue;

                        bool isSelected = (_selectedEvent != null && evt == _selectedEvent);

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
                        ["EventType"] = evt.TypeName,
                        ["Frame"]     = evt.Frame,
                        ["Payload"]   = evt.RawEvent,
                    };
                    string rawJson = System.Text.Json.JsonSerializer.Serialize(dumpDict, FdpJsonOptionsRegistry.Indented);
                    ImGuiApi.SetClipboardText(JsonAestheticFormatter.FlattenNumericArrays(rawJson));
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
