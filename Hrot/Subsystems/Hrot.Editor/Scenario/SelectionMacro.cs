using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;

namespace Hrot.Editor.Scenario;

/// <summary>One reference from a selected entity to something OUTSIDE the selection (MA-9): <c>entity · field · target</c>.</summary>
public sealed record OutsideRef(
    Entity Source, string SourceName, string Component, string Field, string LeafKind,
    string TargetName, string? TargetKey, Guid TargetGuid, string? Text, string? ParamTypeId, string? WhyNot)
{
    /// <summary>The line the dialog shows.</summary>
    public string Line => $"{SourceName} · {Component}.{Field} · {TargetName}{(TargetKey is null ? "" : " (" + TargetKey + ")")}";

    /// <summary>It can become a header param (an EntityLink or an area entity — the leaves that have a Param form).</summary>
    public bool CanParameterise => ParamTypeId is not null && Text is not null;
}

/// <summary>
/// ⭐⭐ CE-4210 MA-9 (SR-58: a macro is closed) — the CLOSURE WALK of a selection: every reference its entities hold to something outside it, found with the S0 walker (<see cref="EntityRefRemap"/>'s leaf visitor — the same
/// walk the binder and the remap use), and the PLAN to save the selection as a macro: the entity GUIDs, the macro header (Kind = macro, anchor = the selection's centroid, heading north) and — for each outside reference that can have
/// one — a header param whose default is the very text the file would hold for it (its <c>@Key</c>, else its GUID).
/// ⛔ Pure over the world: it reads and never writes. The rebinding (<c>^name</c>) happens in the saved DOM only (<c>ScenarioSaveCore.RebindOutsideReferences</c>).
/// </summary>
public static class MacroClosure
{
    private sealed class Collector : LeafApplier
    {
        public readonly List<(string Field, string Kind, EntityRef Ref, RefSpec Spec)> Found = new();
        private string _field = "";
        public override void Visiting(string member) => _field = member;
        public override EntityLink Link(EntityLink l) { if (!l.IsNone && l.Spec.Form != RefForm.Param) Found.Add((_field, "EntityLink", l.Ref, l.Spec)); return l; }
        public override AreaLink Area(AreaLink a) { if (!a.IsShape && !a.IsNone && a.Spec.Form != RefForm.Param) Found.Add((_field, "AreaLink", a.Area, a.Spec)); return a; }
        public override PointLink PointLink(PointLink p) { if (!p.Spec.IsNone && p.Spec.Form != RefForm.Param) Found.Add((_field, "PointLink", default, p.Spec)); return p; }
        public override EntityRef Ref(EntityRef r) { if (!r.IsNone) Found.Add((_field, "EntityRef", r, default)); return r; }
    }

    /// <summary>The selection's saveable entities: alive, not a transient one (<c>ScenarioIgnoreTag</c> — never saved), with a stable GUID.</summary>
    public static IReadOnlyList<(Entity Entity, Guid Guid)> Savable(EntityRepository world, IEnumerable<Entity> selection)
    {
        var list = new List<(Entity, Guid)>();
        foreach (var e in selection.Distinct())
        {
            if (!world.IsAlive(e)) continue;
            if (world.IsComponentTypeRegistered<ScenarioIgnoreTag>() && world.HasComponent<ScenarioIgnoreTag>(e)) continue;
            if (!world.IsComponentTypeRegistered<EntityGuid>() || !world.HasComponent<EntityGuid>(e)) continue;
            list.Add((e, world.GetComponent<EntityGuid>(e).Value));
        }
        return list;
    }

    private static string NameOf(EntityRepository w, Entity e)
        => w.IsAlive(e) && w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(e) && w.GetComponent<EntityInfo>(e).Name.ToString() is { Length: > 0 } n ? n : $"entity {e.Index}";

    /// <summary>Every reference of the selection to something outside it. Inside references are ignored; a reference already bound to a parameter (<c>^name</c>) is not a reference to anything here.</summary>
    public static IReadOnlyList<OutsideRef> Walk(EntityRepository world, IReadOnlyList<(Entity Entity, Guid Guid)> selected)
    {
        var inside = new HashSet<Entity>(selected.Select(s => s.Entity));
        var session = new RepositoryAdapter(world);
        var types = session.GetAllComponentTypes().Where(EntityRefRemap.HoldsLeaves).ToList();
        var result = new List<OutsideRef>();
        foreach (var (e, _) in selected)
            foreach (var t in types)
            {
                if (!session.HasComponent(e, t) || session.GetComponent(e, t) is not { } value) continue;
                var c = new Collector();
                EntityRefRemap.WalkObject(value, c);
                foreach (var (field, kind, reference, spec) in c.Found)
                {
                    var target = reference.IsNone ? Entity.Null : reference.Resolve(world);
                    if (!target.IsNull && inside.Contains(target)) continue;                       // inside the selection: closed
                    Guid guid = !target.IsNull && world.IsComponentTypeRegistered<EntityGuid>() && world.HasComponent<EntityGuid>(target) ? world.GetComponent<EntityGuid>(target).Value : Guid.Empty;
                    string? key = !target.IsNull && world.IsComponentTypeRegistered<RefKey>() && world.HasComponent<RefKey>(target) ? world.GetComponent<RefKey>(target).ToString() : null;
                    string targetName = !target.IsNull ? NameOf(world, target) : spec.IsNone ? "(unresolved)" : spec.ToString();
                    // the param's DEFAULT: its authored key when it has one (the link's own, else the target's RefKey), else its GUID — what a host scenario can resolve again
                    string? text = spec.Form == RefForm.Key ? spec.ToString()
                                 : key is not null ? key
                                 : !spec.IsNone ? spec.ToString()
                                 : guid != Guid.Empty ? guid.ToString() : null;
                    string? why = kind switch
                    {
                        "EntityLink" or "AreaLink" => text is null ? "the target cannot be named (no key, no stable id)" : null,
                        "PointLink" => "a point on an entity has no parameter form yet",
                        _ => "a plain entity reference (EntityRef) has no parameter form",
                    };
                    result.Add(new OutsideRef(e, NameOf(world, e), t.Name, field, kind, targetName, key ?? (spec.Form == RefForm.Key ? spec.ToString() : null), guid,
                                              text, why is null ? kind : null, why));
                }
            }
        return result;
    }

    /// <summary>The header params that would close the selection: one per distinct outside reference TEXT (default = that text); the references that cannot have one are the plan's blockers.</summary>
    public static (IReadOnlyList<ScenarioParamDecl> Params, IReadOnlyList<OutsideRef> Blockers) ParamsFor(IReadOnlyList<OutsideRef> outside)
    {
        var decls = new List<ScenarioParamDecl>();
        var byText = new Dictionary<string, string>(StringComparer.Ordinal);   // text -> param name
        var typeOfText = new Dictionary<string, string>(StringComparer.Ordinal);
        var blockers = new List<OutsideRef>();
        foreach (var o in outside)
        {
            if (!o.CanParameterise) { blockers.Add(o); continue; }
            if (byText.TryGetValue(o.Text!, out _))
            {
                if (typeOfText[o.Text!] != o.ParamTypeId) blockers.Add(o with { WhyNot = "the same target is referenced as both an EntityLink and an AreaLink" });
                continue;
            }
            string name = ParamBindModel.SuggestParamName(Identifier(o.TargetKey?.TrimStart('@') ?? o.TargetName), decls.Select(d => d.Name));
            byText[o.Text!] = name; typeOfText[o.Text!] = o.ParamTypeId!;
            decls.Add(new ScenarioParamDecl(name, o.ParamTypeId!, JsonValue.Create(o.Text!), "outside reference: " + o.TargetName));
        }
        return (decls, blockers);
    }

    /// <summary>A param name from free text: letters and digits, camel-cased, at most 31 bytes (a link binds by name).</summary>
    public static string Identifier(string text)
    {
        var sb = new StringBuilder();
        bool up = false;
        foreach (char ch in text)
        {
            if (char.IsAsciiLetterOrDigit(ch)) { sb.Append(sb.Length == 0 ? char.ToLowerInvariant(ch) : up ? char.ToUpperInvariant(ch) : ch); up = false; }
            else up = sb.Length > 0;
        }
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, "ref");
        return sb.Length > 28 ? sb.ToString(0, 28) : sb.ToString();
    }
}

/// <summary>The saved selection's plan: what is written, with which header, and what closes it.</summary>
public sealed record SelectionMacroPlan(
    string MacroPath, IReadOnlyList<(Entity Entity, Guid Guid)> Selected, IReadOnlyList<OutsideRef> Outside,
    IReadOnlyList<ScenarioParamDecl> Params, IReadOnlyList<OutsideRef> Blockers, MacroAnchor Anchor)
{
    public bool IsClosed => Outside.Count == 0;
    public bool CanMakeParameters => Outside.Count > 0 && Blockers.Count == 0;
    public Guid[] Guids => Selected.Select(s => s.Guid).ToArray();

    /// <summary>The macro header fields the slice uses (<c>ScenarioHeaderJson.Write</c>'s object), with or without the closing params.</summary>
    public string HeaderJson(bool withParams)
        => ScenarioDescription.HeaderJsonOf(new ScenarioHeader("")
        {
            Kind = ScenarioHeader.MacroKind,
            Params = withParams && Params.Count > 0 ? Params.ToArray() : null,
            Anchor = Anchor,
        });

    public static SelectionMacroPlan Build(EntityRepository world, IEnumerable<Entity> selection, string macroPath)
    {
        var selected = MacroClosure.Savable(world, selection);
        var outside = selected.Count == 0 ? Array.Empty<OutsideRef>() : MacroClosure.Walk(world, selected);
        var (ps, blockers) = MacroClosure.ParamsFor(outside);
        return new SelectionMacroPlan(macroPath, selected, outside, ps, blockers, CentroidOf(world, selected));
    }

    /// <summary>The anchor: x and y the centroid of the selection's positions, z their mean, heading 0 (north).</summary>
    public static MacroAnchor CentroidOf(EntityRepository world, IReadOnlyList<(Entity Entity, Guid Guid)> selected)
    {
        double x = 0, y = 0, z = 0; int n = 0;
        foreach (var (e, _) in selected)
            if (world.IsComponentTypeRegistered<SimTransform>() && world.HasComponent<SimTransform>(e))
            { var p = world.GetComponent<SimTransform>(e).Position; x += p.X; y += p.Y; z += p.Z; n++; }
        return n == 0 ? new MacroAnchor(default, default) : new MacroAnchor(new WorldPoint((float)(x / n), (float)(y / n), (float)(z / n)), default);
    }
}

/// <summary>
/// ⭐ CE-4210 MA-9 — the <i>Save selection as macro…</i> gesture as a small state machine (no ImGui): ask a name → walk the closure → (a closed selection saves; an open one asks: <i>Make parameters</i> or <i>Cancel</i>) → one
/// <c>SaveSelectionAs</c> through the cluster's one save path. A cancelled flow saves nothing; a macro is never saved with a dangling outside reference.
/// </summary>
public sealed class SaveSelectionAsMacroFlow
{
    public enum StepKind { Closed, Naming, Closure, Done }

    private readonly Func<EntityRepository?> _world;
    private readonly Func<IReadOnlyCollection<Entity>> _selection;
    private readonly Action<string, Guid[], string?> _save;

    public StepKind Step { get; private set; }
    public string Name { get; set; } = "";
    public string Message { get; private set; } = "";
    public SelectionMacroPlan? Plan { get; private set; }
    public int SaveCalls { get; private set; }

    public SaveSelectionAsMacroFlow(Func<EntityRepository?> world, Func<IReadOnlyCollection<Entity>> selection, Action<string, Guid[], string?> save)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public void Begin()
    {
        Plan = null; Message = "";
        if (_world() is not { } w || MacroClosure.Savable(w, _selection()).Count == 0) { Step = StepKind.Done; Message = "Select the entities to save as a macro first."; return; }
        Name = string.IsNullOrWhiteSpace(Name) ? "new-macro" : Name;
        Step = StepKind.Naming;
    }

    /// <summary>The macro path a typed name means (<c>test/sel-w5</c>), or null when it is not a usable name.</summary>
    public static string? PathOf(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var path = typed.Trim().Replace('\\', '/').Trim('/');
        if (path.StartsWith(ContentLayout.MacroNamePrefix, StringComparison.Ordinal)) path = path.Substring(ContentLayout.MacroNamePrefix.Length);
        if (path.Length == 0 || path.Split('/').Any(seg => seg.Length == 0 || seg == "." || seg == ".." || seg.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ' or '.')))) return null;
        return path;
    }

    /// <summary>From the naming step: walk the closure. A closed selection saves; an open one waits for the choice.</summary>
    public void Confirm()
    {
        if (Step != StepKind.Naming) return;
        if (PathOf(Name) is not { } path) { Message = "A macro name is letters, digits, - _ and / for folders."; return; }
        if (_world() is not { } w) return;
        Message = "";
        Plan = SelectionMacroPlan.Build(w, _selection(), path);
        if (Plan.Selected.Count == 0) { Step = StepKind.Done; Message = "Nothing in the selection can be saved (transient or unidentified entities)."; return; }
        if (Plan.IsClosed) Save(withParams: false);
        else Step = StepKind.Closure;
    }

    /// <summary>The closure dialog's <i>Make parameters</i>: every outside reference becomes a header param of the SAVED macro (the scenario on screen is not touched).</summary>
    public void MakeParameters()
    {
        if (Step != StepKind.Closure || Plan is not { CanMakeParameters: true }) return;
        Save(withParams: true);
    }

    public void Cancel() { Step = StepKind.Closed; Plan = null; Message = ""; }

    public void Dismiss() { if (Step == StepKind.Done) { Step = StepKind.Closed; Message = ""; } }

    private void Save(bool withParams)
    {
        var plan = Plan!;
        if (!plan.IsClosed && !withParams) return;   // ⛔ never a dangling outside reference
        _save(ContentLayout.MacroName(plan.MacroPath), plan.Guids, plan.HeaderJson(withParams));
        SaveCalls++;
        Step = StepKind.Done;
        Message = $"Saved {plan.Selected.Count} entit{(plan.Selected.Count == 1 ? "y" : "ies")} as {ContentLayout.MacroName(plan.MacroPath)}"
                + (withParams && plan.Params.Count > 0 ? $" with {plan.Params.Count} parameter(s)." : ".");
    }
}
