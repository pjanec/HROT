using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Blackboard;
using ImGuiNET;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐ CE-4210 P2 (MA-5) → ⭐ CE-4253 → ⭐ N-6 (CE-3383) — the scenario / macro's PARAMETERS editor: the ONE Define table (<see cref="FieldTable"/>) over the declared params
/// (<see cref="HeaderParamsSchemaSource"/> through <see cref="SchemaSourceDeclarations"/>); every edit — a rename moves the declaration and every binding in ONE step — is the source's own, with its ONE undo history (the shell's Edit ▸ Undo / Redo).
/// ⭐ CE-4253 — this body is a DRAWER the Details ▸ Scenario view calls inside its collapsible <i>Parameters</i> header (<see cref="ScenarioEnvironmentView"/>).
/// </summary>
public sealed class ParametersDrawer
{
    private readonly HeaderParamsSchemaSource _source;
    private readonly SchemaSourceDeclarations _decls;
    private readonly FieldTable _table = new();
    private readonly FieldTableSettings _settings;

    /// <summary>⭐ N-6 — the table (frame rails read its probes).</summary>
    internal FieldTable Table => _table;

    /// <summary>Why the last rename was refused (CE-4254): the reason under the row while its rename box is still open; null otherwise.</summary>
    public string? RenameRefusal => _table.RenamingName != null && _table.Error is { } e ? e.Text : null;

    /// <summary>Frame-rail probe (CE-4254): where the name cells were drawn.</summary>
    public IReadOnlyList<(string Name, System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)> NameCells => _table.NameCells;

    /// <param name="editService">Kept for the call sites: the value cells draw from the type catalogue now, not from a StructEdit service.</param>
    public ParametersDrawer(HeaderParamsSchemaSource source, Func<StructEdit.Core.IComponentEditService?>? editService = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _decls = new SchemaSourceDeclarations(source, id => Fdp.Toolkit.Scenario.ParamTypes.TryResolve(id, out var t) ? t : null);
        var choices = new List<TypeChoice>();
        foreach (var (id, type) in Fdp.Toolkit.Scenario.ParamTypes.All()) choices.Add(new TypeChoice(id, type, GroupOf(type)));
        _settings = new FieldTableSettings
        {
            AddLabel = "Add parameter", NewNameStem = "param", ValueHeader = "Default", UsedBy = true,
            AllowStructs = false, AllowLists = false,
            StructsOffReason = "structures and lists are off for scenario parameters", ListsOffReason = "structures and lists are off for scenario parameters",
            TypeChoices = choices,
        };
    }

    /// <summary>The picker's group of a parameter type: enums · the links and the bearing / rotation (references and places) · plain values.</summary>
    private static TypeGroup GroupOf(Type t)
        => t.IsEnum ? TypeGroup.Enums
         : t == typeof(Fdp.Toolkit.Replication.EntityLink) || t == typeof(Fdp.Toolkit.Replication.PointLink) || t == typeof(Fdp.Toolkit.Replication.AreaLink)
           || t == typeof(Fdp.Core.Bearing) || t == typeof(Fdp.Core.WorldRotation) ? TypeGroup.References
         : FieldTableSettings.GroupOf(t);

    /// <summary>For rails: how many times the body was drawn (the header closed ⇒ not at all).</summary>
    internal int DrawCount { get; private set; }

    private bool _openRequested;

    /// <summary>⭐ CE-4253 — asks the Scenario view to show its Parameters header EXPANDED the next time it draws (the bind popup's <i>New parameter…</i> calls this).</summary>
    public void RequestOpen() => _openRequested = true;

    /// <summary>⭐ True once per <see cref="RequestOpen"/> — the view expands the header for that frame.</summary>
    public bool ConsumeOpenRequest() { bool r = _openRequested; _openRequested = false; return r; }

    /// <summary>The drawer's content: the table (its refusals are drawn under the row they belong to).</summary>
    public void Draw()
    {
        DrawCount++;
        ImGui.Separator();
        if (_source.IsReadOnly) { ImGui.TextDisabled("No scenario is loaded."); return; }
        _table.Draw(_decls, _settings, null);
        // ⛔ B-14 — Ctrl+Z / Ctrl+Y are the shell's Edit ▸ Undo / Redo now (the ONE key mechanism over the SAME history): the drawer's own chord would undo twice.
    }

}
