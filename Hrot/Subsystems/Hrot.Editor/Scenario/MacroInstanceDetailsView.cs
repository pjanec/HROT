using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
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
    private sealed class RowState : IDisposable
    {
        public IEditSession Session = null!;
        public ComponentEditDrawer Drawer = null!;
        public string ShownJson = "";
        public Type Type = null!;
        public void Dispose() => Session?.Dispose();
    }

    private readonly Func<EntityRepository?> _world;
    private readonly Func<IInspectableSession?> _session;
    private readonly Func<string?> _macrosRoot;
    private readonly Func<IComponentEditService?> _editService;
    private readonly Func<IMapPickContext?> _picks;
    private readonly Action<Action<string>>? _pickMacro;
    private readonly Dictionary<string, RowState> _rows = new();
    private Entity _entity = Entity.Null;
    private string? _macro;

    /// <summary>What the last <see cref="Draw"/> showed (read by the frame rail): the status line, the rows drawn, the reset buttons, whether it was read-only.</summary>
    public string StatusText { get; private set; } = "";
    public int RowsDrawn { get; private set; }
    public int ResetButtons { get; private set; }
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
        RowsDrawn = ResetButtons = 0;
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
        if (_editService() is not { } svc) { ImGui.TextDisabled("No editor service."); ImGui.PopID(); return; }
        ImGui.BeginDisabled(!owned);
        if (ImGui.BeginTable("args", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Param", ImGuiTableColumnFlags.WidthFixed, 160f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70f);
            foreach (var row in rows)
            {
                var st = PrepareRow(svc, row);
                if (st is null) continue;
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, row.IsOverridden ? 1f : 0.55f);   // the default is greyed until it is overridden
                st.Drawer.DrawRoot(VariableEditModal.ScalarRowOrRoot(st.Session.Document.Root, row.Name));
                ImGui.PopStyleVar();
                FinishRow(session, entity, instance, row, st);
            }
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        ImGui.PopID();
    }

    /// <summary>The row's session (opened or reopened from the effective JSON); null for an unknown type (its row is already written).</summary>
    private RowState? PrepareRow(IComponentEditService svc, MacroArgRow row)
    {
        if (row.Type is null)
        {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(row.Name);
            ImGui.TableSetColumnIndex(1); ImGui.TextDisabled($"unknown type '{row.TypeId}'");
            return null;
        }
        if (!_rows.TryGetValue(row.Name, out var st) || !string.Equals(st.ShownJson, row.EffectiveJson, StringComparison.Ordinal) || st.Type != row.Type)
        {
            st?.Dispose();
            var entry = new BlackboardVariableEntry(row.Name, row.Type, row.Comment, DefaultValueJson: row.EffectiveJson);
            var s = DefaultValueAuthoring.OpenSession(svc, entry);
            st = new RowState { Session = s, Drawer = new ComponentEditDrawer(s, _picks() is { } p ? new ScopedMapPickContext(p, "$.macroarg." + row.Name) : null), ShownJson = row.EffectiveJson, Type = row.Type };
            _rows[row.Name] = st;
        }
        if (st.Session.RebuildState == EditRebuildState.RebuildRequired) st.Session.RebuildDocument();
        return st;
    }

    /// <summary>After the drawer drew the row: the reset column and the commit of an edit.</summary>
    private void FinishRow(IInspectableSession session, Entity entity, MacroInstance instance, MacroArgRow row, RowState st)
    {
        RowsDrawn++;
        ImGui.TableSetColumnIndex(2);
        if (row.IsOverridden)
        {
            ResetButtons++;
            if (ImGui.SmallButton("reset##" + row.Name))
                Write(session, entity, MacroArgs.Reset(instance, row.Name));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset to the default" + (row.DefaultJson is null ? "" : " (" + row.DefaultJson + ")"));
        }
        else ImGui.TextDisabled("default");

        if (st.Session.IsDirty)
        {
            try
            {
                string json = DefaultValueAuthoring.CommitAndSerialize(st.Session, row.Type!);
                if (!string.Equals(json, st.ShownJson, StringComparison.Ordinal))
                {
                    st.ShownJson = json;   // what the form now shows; the next frame's record carries it
                    Write(session, entity, MacroArgs.With(instance, row.Name, json, row.DefaultJson));
                }
            }
            catch (EditValidationException) { /* an invalid edit stays in the form; nothing is written */ }
        }
    }

    private void Write(IInspectableSession session, Entity entity, MacroInstance next)
    {
        MacroArgs.Write(session, entity, next);
        ClearRows();   // the record changed (or the write was refused): reopen the rows from the entity's own record next frame
    }

    private void ClearRows()
    {
        foreach (var r in _rows.Values) r.Dispose();
        _rows.Clear();
    }

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
