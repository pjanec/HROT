using System;
using System.Text.Json;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;
using StructEdit.Core;

namespace Hrot.Presentation.Behavior;

/// <summary>
/// ⭐⭐ <c>CE-3043</c> / <c>CE-1023</c> — THE ONE PARAMS FORM: edit a behaviour's authored params (its params type) as a StructEdit
/// document and produce the JSON an assignment carries. Every screen that edits a behaviour's params draws THIS — the AI section
/// (task / SOP / instance rows) and the mission panel's tasks. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6, §7.6b.
/// <para>⭐ Reads and writes with <see cref="BehaviorParams.JsonOptions"/> — the options every parse side uses — so what
/// the form writes is exactly what the behaviour reads (<c>DESIGN_Decision_Layer.md</c> §4.4: one options object both
/// ways).</para>
/// <para>⭐ A field is picked on the map by its TYPE (<c>EntityRef</c>, <c>PickableGeoPoint</c>) through the context passed to
/// <see cref="DrawFields"/>; open the session over <see cref="ComponentEditServices"/>' standard leaves or those fields are
/// read-only containers. ⛔ HISTORY: it lived in <c>Hrot.Editor.AiShared</c>, where the mission panel could not reach it, so the
/// panel kept a second form (<c>BehaviorUiCompiler</c>, deleted by CE-1023).</para>
/// <para>⚠ A params type is always a container (every <c>[Behavior(Params = …)]</c> is a DTO), so the root is bound and no
/// scalar box is needed — a leaf type is refused.</para>
/// </summary>
public static class BehaviorParamsForm
{
    /// <summary>
    /// The params type of the behaviour <paramref name="name"/>: the node's registry first (it also knows a blueprint
    /// behaviour's generated <c>Params</c>), else the declared contract (<see cref="ParamsDtoRegistry.Default"/>); null when
    /// neither knows one. ⭐ The one lookup — the AI section and the mission panel both ask here.
    /// </summary>
    public static Type? ParamsTypeOf(string? name, BehaviorRegistry? registry)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (registry != null && registry.TryGetId(name, out int id) && registry.TryGetDefinition(id, out var d)
            && d.JsonParamsDtoType is { } own)
            return own;
        return ParamsDtoRegistry.Default.TryGetBehavior(name, out var declared) ? declared.ParamsType : null;
    }

    /// <summary>The params type's value from <paramref name="json"/>; the type's default when the JSON is empty or invalid.</summary>
    public static object Hydrate(Type paramsType, string? json)
    {
        if (paramsType is null) throw new ArgumentNullException(nameof(paramsType));
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (JsonSerializer.Deserialize(json, paramsType, BehaviorParams.JsonOptions) is { } value) return value;
            }
            catch (JsonException) { }
            catch (NotSupportedException) { }
        }
        return Activator.CreateInstance(paramsType)!;
    }

    /// <summary>Opens an edit session over <paramref name="paramsType"/> seeded from <paramref name="json"/>.</summary>
    public static IEditSession Open(IComponentEditService editService, Type paramsType, string? json)
    {
        if (editService is null) throw new ArgumentNullException(nameof(editService));
        if (paramsType is null) throw new ArgumentNullException(nameof(paramsType));
        if (paramsType.IsPrimitive || paramsType.IsEnum || paramsType == typeof(string))
            throw new ArgumentException($"A behaviour's params type is a DTO, not the leaf {paramsType.Name}.", nameof(paramsType));
        return editService.Open(Hydrate(paramsType, json), paramsType, null);
    }

    /// <summary>
    /// ⭐ Draws the session's fields as a two-column table; a pickable field asks <paramref name="picks"/> (null ⇒ no Pick button).
    /// Give each form on one screen its own scope (<see cref="ScopedMapPickContext"/>) — one broker holds one pending pick.
    /// </summary>
    public static void DrawFields(IEditSession session, IMapPickContext? picks, string tableId = "params")
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (!ImGuiNET.ImGui.BeginTable(tableId, 2, ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg)) return;
        if (session.RebuildState == EditRebuildState.RebuildRequired) session.RebuildDocument();
        new ComponentEditDrawer(session, picks).DrawRoot(session.Document.Root);
        ImGuiNET.ImGui.EndTable();
    }

    /// <summary>Commits <paramref name="session"/> and returns the assignment's JSON.</summary>
    public static string CommitToJson(IEditSession session, Type paramsType)
        => ToJson(session.Commit(), paramsType);

    /// <summary>A params value as the assignment's JSON (the parse side's options).</summary>
    public static string ToJson(object value, Type paramsType)
        => JsonSerializer.Serialize(value, paramsType, BehaviorParams.JsonOptions);
}

/// <summary>
/// ⭐ <c>CE-1023</c> (§7.6b F4) — one params form bound to a JSON its owner keeps (a mission task's <c>BehaviorParams</c>): the
/// session is reopened only when that JSON changes from OUTSIDE the form (a snapshot sync, another behaviour), and an edit comes
/// back as new JSON once — opening never rewrites the owner's JSON. No ImGui here; the owner draws <see cref="Session"/> with
/// <see cref="BehaviorParamsForm.DrawFields"/>.
/// </summary>
public sealed class BehaviorParamsEditor
{
    private readonly IComponentEditService _editService;
    private string? _source;      // the owner's JSON the session reflects
    private string? _committed;   // the session's value as JSON when last opened / committed

    public BehaviorParamsEditor(IComponentEditService editService)
        => _editService = editService ?? throw new ArgumentNullException(nameof(editService));

    /// <summary>The open session (null before the first <see cref="Sync"/> with a type).</summary>
    public IEditSession? Session { get; private set; }

    /// <summary>The params type the session edits.</summary>
    public Type? Type { get; private set; }

    /// <summary>Binds to <paramref name="type"/> and the owner's <paramref name="json"/>; reopens when either changed outside.</summary>
    public void Sync(Type? type, string? json)
    {
        if (type == null) { Close(); return; }
        if (Session != null && type == Type && (json == _source || json == _committed)) return;
        Close();
        Type     = type;
        Session  = BehaviorParamsForm.Open(_editService, type, json);
        _source  = json;
        _committed = BehaviorParamsForm.ToJson(BehaviorParamsForm.Hydrate(type, json), type);
    }

    /// <summary>The new JSON when the form was edited since the last call; null when nothing changed.</summary>
    public string? Edited()
    {
        if (Session is not { IsDirty: true } s || Type == null) return null;
        string json = BehaviorParamsForm.CommitToJson(s, Type);
        if (json == _committed) return null;
        _committed = json;
        _source    = json;
        return json;
    }

    private void Close()
    {
        Session?.Dispose();
        Session = null;
        Type = null;
        _source = _committed = null;
    }
}
