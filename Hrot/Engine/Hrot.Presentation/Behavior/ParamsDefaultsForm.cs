using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using StructEdit.Core;

namespace Hrot.Presentation.Behavior;

/// <summary>
/// ⭐ <c>CE-1061</c> U5b (R-3001 / R-3002, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10c E1/E2) — an asset's params DEFAULTS,
/// without ImGui: an edit session over the EFFECTIVE value (<see cref="ParamsDefaults.Effective"/>: the DTO's initialisers, then the
/// asset's overrides) and the overrides themselves. An edited field becomes an override (only that key); <see cref="Without"/> resets
/// one. The owner writes the returned JSON to the asset (<c>ParamsDefaultJson</c>) — as an undoable edit.
/// </summary>
public sealed class ParamsDefaultsModel
{
    private readonly IComponentEditService _editService;
    private string? _source;                    // the overrides the session reflects
    private JsonObject _opened = new();         // the effective value as the session opened (or last committed) it

    public ParamsDefaultsModel(IComponentEditService editService)
        => _editService = editService ?? throw new ArgumentNullException(nameof(editService));

    /// <summary>The DTO edited, or null.</summary>
    public Type? Type { get; private set; }

    /// <summary>The open session (null when there is no DTO, or a class DTO with no default constructor).</summary>
    public IEditSession? Session { get; private set; }

    /// <summary>The asset's overrides (field → value), as last synced or committed.</summary>
    public JsonObject Overrides { get; private set; } = new();

    /// <summary>Binds to <paramref name="dto"/> with the asset's <paramref name="overridesJson"/>; reopens only when either changed from
    /// OUTSIDE the form (an undo, another surface) — the form's own commit does not reopen it.</summary>
    public void Sync(Type? dto, string? overridesJson)
    {
        if (dto == null || dto == typeof(object)) { Close(); return; }   // ⚠ an unresolved type id resolves to object
        if (Session != null && dto == Type && string.Equals(overridesJson, _source, StringComparison.Ordinal)) return;
        Close();
        var effective = ParamsDefaults.Effective(dto, overridesJson);
        if (effective is null) return;
        Type     = dto;
        Session  = _editService.Open(effective, dto, null);
        _source  = overridesJson;
        _opened  = Parse(BehaviorParamsForm.ToJson(effective, dto));
        Overrides = Parse(overridesJson);
    }

    /// <summary>The JSON key of the DTO member named <paramref name="clrName"/> (its <c>[JsonPropertyName]</c>, else the name).</summary>
    public string JsonNameOf(string clrName)
    {
        var member = Type?.GetField(clrName, BindingFlags.Public | BindingFlags.Instance) as MemberInfo
                  ?? Type?.GetProperty(clrName, BindingFlags.Public | BindingFlags.Instance);
        return member?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? clrName;
    }

    /// <summary>True when the asset overrides <paramref name="field"/> (CLR or JSON name; keys match case-insensitively).</summary>
    public bool IsOverridden(string field) => KeyOf(Overrides, JsonNameOf(field)) != null;

    /// <summary>
    /// The new overrides after the author edited the form: every field whose value differs from what the session opened with becomes an
    /// override (that key only). False when nothing changed. <paramref name="json"/> is null when no override is left.
    /// </summary>
    public bool TryEdited(out string? json, out string field)
    {
        json = null; field = "";
        if (Session is not { IsDirty: true } s || Type == null) return false;
        var now = Parse(BehaviorParamsForm.ToJson(s.Commit(), Type));
        var changed = now.Where(kv => !JsonNode.DeepEquals(kv.Value, _opened.TryGetPropertyValue(kv.Key, out var was) ? was : null))
                         .Select(kv => kv.Key).ToList();
        if (changed.Count == 0) return false;
        var next = (JsonObject)Overrides.DeepClone();
        foreach (var key in changed)
        {
            if (KeyOf(next, key) is { } existing) next.Remove(existing);
            next[key] = now[key]?.DeepClone();
        }
        field = changed[0];
        json = Serialize(next);
        // ⭐ the form's own commit: the next Sync with this JSON must not reopen the session (a drag would lose its widget)
        _opened = now;
        Overrides = next;
        _source = json;
        return true;
    }

    /// <summary>The overrides without <paramref name="field"/> (a reset: the DTO's default comes back); null when none is left.</summary>
    public string? Without(string field)
    {
        var next = (JsonObject)Overrides.DeepClone();
        if (KeyOf(next, JsonNameOf(field)) is { } existing) next.Remove(existing);
        return Serialize(next);
    }

    private void Close()
    {
        Session?.Dispose();
        Session = null;
        Type = null;
        _source = null;
        _opened = new JsonObject();
        Overrides = new JsonObject();
    }

    private static string? KeyOf(JsonObject o, string key)
        => o.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

    private static string? Serialize(JsonObject o) => o.Count == 0 ? null : o.ToJsonString();

    private static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json!) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }
}

/// <summary>What the defaults form asks its owner to write: the new overrides (null = none) and the undo label.</summary>
public sealed record ParamsDefaultsCommit(string? Json, string Label);

/// <summary>
/// ⭐ <c>CE-1061</c> U5b (§13.10c E2) — THE params-defaults form every asset kind draws (BTree / HSM variables panel, the blueprint outline):
/// one row per DTO field — the value editor (<see cref="ComponentEditDrawer"/>), then "*" + Reset where the asset overrides it.
/// It commits only when no widget is active, so a drag or a typed number is ONE undo entry.
/// </summary>
public static class ParamsDefaultsForm
{
    public static ParamsDefaultsCommit? Draw(string id, ParamsDefaultsModel model, IMapPickContext? picks = null, bool readOnly = false)
    {
        if (model?.Session is not { } session) return null;
        if (session.RebuildState == EditRebuildState.RebuildRequired) session.RebuildDocument();
        var root = session.Document.Root;
        var fields = root.Kind == EditNodeKind.SelectionRoot && root.Children.Count > 0 ? root.Children[0] : root;

        string? reset = null;
        ImGuiNET.ImGui.BeginDisabled(readOnly);
        if (ImGuiNET.ImGui.BeginTable(id, 3, ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
        {
            ImGuiNET.ImGui.TableSetupColumn("Field", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 140f);
            ImGuiNET.ImGui.TableSetupColumn("Default", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
            ImGuiNET.ImGui.TableSetupColumn("##ovr", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 64f);
            var drawer = new ComponentEditDrawer(session, picks);
            foreach (var node in fields.Children)
            {
                drawer.DrawEditNode(node);
                ImGuiNET.ImGui.TableSetColumnIndex(2);
                if (model.IsOverridden(node.Name))
                {
                    ImGuiNET.ImGui.PushID(node.Name);
                    // ⚠ "*", not "●": U+25CF is outside every glyph range the editor font loads (EditorFontService.UiRanges) — it drew as "?".
                    ImGuiNET.ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "*");
                    if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip("This asset overrides the DTO's default.");
                    ImGuiNET.ImGui.SameLine();
                    if (ImGuiNET.ImGui.SmallButton("Reset")) reset = node.Name;
                    ImGuiNET.ImGui.PopID();
                }
            }
            ImGuiNET.ImGui.EndTable();
        }
        ImGuiNET.ImGui.EndDisabled();

        if (readOnly) return null;
        if (reset != null) return new ParamsDefaultsCommit(model.Without(reset), $"Reset params default {reset}");
        if (!ImGuiNET.ImGui.IsAnyItemActive() && model.TryEdited(out var json, out var field))
            return new ParamsDefaultsCommit(json, $"Edit params default {field}");
        return null;
    }
}
