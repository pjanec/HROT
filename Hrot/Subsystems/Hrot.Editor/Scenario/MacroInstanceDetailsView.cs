using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using ImGuiNET;
using StructEdit.Core;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐⭐ CE-4210 MA-8 (SR-39a) — the <b>Macro</b> section of Details for an entity that carries a <see cref="MacroInstance"/>: the expansion status at the top (<see cref="MacroExpansionStatus"/>), the macro (the SAME picker, its Macros
/// group), and the args form — one row per header param of the picked macro, each drawn by the ONE component drawer over a one-field typed holder (<see cref="DefaultValueAuthoring"/>, the precedent of a variable's default). A change
/// builds a NEW immutable <see cref="MacroInstance"/> and writes it (<see cref="MacroArgs.Write"/> — only where this node owns it); the expander's reconcile (SR-150/157) does the rest.
/// </summary>
public sealed class MacroInstanceDetailsView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<IInspectableSession?> _session;
    private readonly Func<string?> _macrosRoot;
    private readonly Func<IComponentEditService?> _editService;
    private readonly Func<IMapPickContext?> _picks;
    private readonly Action<Action<string>>? _pickMacro;
    private readonly ParamArgsTable _table;   // ⭐ the ONE typed args form (shared with the picker's run setup)
    private Entity _entity = Entity.Null;
    private string? _macro;

    /// <summary>What the last <see cref="Draw"/> showed (read by the frame rail): the status line, the rows drawn, the reset buttons, whether it was read-only.</summary>
    public string StatusText { get; private set; } = "";
    public int RowsDrawn => _table.RowsDrawn;
    public int ResetButtons => _table.ResetButtons;
    public bool ReadOnly { get; private set; }

    /// <param name="pickMacro">Opens the scenario picker's Macros group and calls back with the chosen path (null ⇒ no Change… button). ⛔ A host that edits macros passes it.</param>
    public MacroInstanceDetailsView(Func<EntityRepository?> world, Func<IInspectableSession?> session, Func<string?> macrosRoot,
                                    Func<IComponentEditService?> editService, Func<IMapPickContext?>? picks = null, Action<Action<string>>? pickMacro = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _macrosRoot = macrosRoot ?? throw new ArgumentNullException(nameof(macrosRoot));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        _picks = picks ?? (() => null);
        _pickMacro = pickMacro;
        _table = new ParamArgsTable(_picks, "$.macroarg");
    }

    /// <summary>The expansion status as one line: <c>Done</c>, <c>Failed: reason</c>, <c>Expanding…</c> or <c>Not expanded</c>.</summary>
    public static string StatusLine(EntityRepository world, Entity e)
    {
        if (!world.IsComponentTypeRegistered<MacroExpansionStatus>() || !world.HasComponent<MacroExpansionStatus>(e)) return "Not expanded";
        var s = world.GetComponentRO<MacroExpansionStatus>(e);
        return s.State switch
        {
            MacroExpansionState.Done => "Done",
            MacroExpansionState.Failed => "Failed: " + s.Reason.ToString(),
            MacroExpansionState.Expanding => "Expanding…",
            _ => "Not expanded",
        };
    }

    public void Draw(DetailsContext context, string idScope)
    {
        _table.ResetCounts();
        if (context.Entities is not { Count: 1 }) { ImGui.TextDisabled("Select one macro instance."); return; }
        var entity = context.Entities[0];
        if (entity != _entity) { ClearRows(); _entity = entity; _macro = null; }
        var session = _session();
        if (_world() is not { } world || session is null || !session.IsAlive(entity) || !session.HasComponent(entity, typeof(MacroInstance))
            || session.GetComponent(entity, typeof(MacroInstance)) is not MacroInstance instance)
        { ImGui.TextDisabled("This entity is not a macro instance."); return; }
        if (!string.Equals(_macro, instance.Macro, StringComparison.Ordinal)) { ClearRows(); _macro = instance.Macro; }

        ImGui.PushID(idScope + "/macro");
        StatusText = StatusLine(world, entity);
        bool failed = StatusText.StartsWith("Failed", StringComparison.Ordinal);
        ImGui.TextColored(failed ? new Vector4(1f, 0.45f, 0.35f, 1f) : new Vector4(0.6f, 0.85f, 0.6f, 1f), "Expansion: " + StatusText);

        bool owned = session.HasAuthority(entity, typeof(MacroInstance));
        ReadOnly = !owned;
        if (!owned) ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), "Edit on the owning node: this node does not own this instance.");

        ImGui.TextUnformatted("Macro: " + (instance.Macro.Length == 0 ? "(none)" : instance.Macro));
        if (_pickMacro is not null)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(!owned);
            if (ImGui.SmallButton("Change...##macro")) _pickMacro(path => Write(session, entity, MacroArgs.Retarget(instance, path)));
            ImGui.EndDisabled();
        }

        var (header, problem) = MacroArgs.ReadHeader(_macrosRoot(), instance.Macro);
        if (problem is not null) { ImGui.TextColored(new Vector4(1f, 0.45f, 0.35f, 1f), problem); ImGui.PopID(); return; }
        var rows = MacroArgs.Rows(header, instance.ArgsJson);
        if (rows.Count == 0) { ImGui.TextDisabled("This macro declares no parameters."); ImGui.PopID(); return; }

        ImGui.Separator();
        ImGui.TextUnformatted("Arguments");
        ImGui.BeginDisabled(!owned);
        _table.Draw("args", rows,
            onSet: (row, json) => Write(session, entity, MacroArgs.With(instance, row.Name, json, row.DefaultJson)),
            onReset: row => Write(session, entity, MacroArgs.Reset(instance, row.Name)));
        ImGui.EndDisabled();
        ImGui.PopID();
    }

    private void Write(IInspectableSession session, Entity entity, MacroInstance next)
    {
        MacroArgs.Write(session, entity, next);
        // the table compares the next frame's rows with what it shows: an accepted write is echoed back (no rebuild), a refused one differs and the rows reopen from the entity's own record
    }

    private void ClearRows() => _table.Clear();

    public void Dispose() => ClearRows();
}

public static class MacroInstanceDetailsViewDescriptor
{
    public const string ViewId = "details.macro";
    public const int Rank = 20;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<IInspectableSession?> session, Func<string?> macrosRoot,
                                            Func<IComponentEditService?> editService, Func<IMapPickContext?>? picks = null, Action<Action<string>>? pickMacro = null)
    {
        var instance = new MacroInstanceDetailsView(world, session, macrosRoot, editService, picks, pickMacro);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Macro",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx)
                           && world() is { } w && w.IsAlive(ctx.Entities[0]) && w.HasManagedComponent<MacroInstance>(ctx.Entities[0]),
            Create:    () => instance);
    }
}
