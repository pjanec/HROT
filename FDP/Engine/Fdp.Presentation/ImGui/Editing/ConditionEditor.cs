using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// ⭐ U-15 / C3 (docs/UX/DESIGN_Condition_Editor.md §4, condition-editor-visual.svg) — THE friendly condition editor: the condition as sentence rows
/// (WHO · IS/HAS · VALUE, every blank a chip), one group line (all / any / none), a dashed Custom row for what it cannot draw, <i>Add condition</i> and
/// <i>From preset</i>. ⭐ It stores nothing: the rows are <see cref="RowProjection.FromDto"/> of the value, re-projected only when the value's
/// <see cref="ConditionSentence.Stamp"/> moves; an edit writes <see cref="RowProjection.ToDto"/> back. One editor for every host (U-16: the mission task,
/// the replay search, breakpoints), filtered by <see cref="ConditionContexts"/>.
/// </summary>
public sealed class ConditionEditor
{
    private readonly ConditionContexts _contexts;
    private readonly List<ConditionRow> _rows = new();
    private readonly List<RowView> _views = new();
    private LogicalOperator _op;
    private long _stamp = long.MinValue;
    private object? _projected;
    private LiveRefScope? _projectedRefs;

    public ConditionEditor(ConditionContexts contexts = ConditionContexts.Rule) => _contexts = contexts;

    /// <summary>The first unfilled blank (<i>Choose an area</i>), or null: a host keeps Apply disabled while it is set (§4).</summary>
    public string? Problem { get; private set; }

    /// <summary>The person asked for the Expert view (a Custom row's <i>Open in Expert view</i>, a row menu's <i>Show in Expert view</i>).</summary>
    public bool ExpertRequested { get; set; }

    /// <summary>⭐ U-15 / C4 — one sampled leaf of the owner's plan (<c>RuleLeafPlan</c>'s flattening, in order): whether it was sampled, holds, and the number behind it.</summary>
    public readonly record struct LiveSample(bool Sampled, bool Holds, float Measured);

    /// <summary>⭐ U-15 / C4 (§4 <i>live</i>) — the owner's samples for this condition's leaves, in flattening order; null = not live (no column).</summary>
    public IReadOnlyList<LiveSample>? Live { get; set; }

    private readonly List<int> _leafCounts = new();

    /// <summary>⭐ C4 — each row's live text (<c>yes 62</c> · <c>no 0.4</c> · <c>-</c> not sampled · null without a sample for it) — rails, the panel probe.</summary>
    public IReadOnlyList<string?> RowLive
    {
        get { var l = new List<string?>(_rows.Count); for (int i = 0; i < _rows.Count; i++) l.Add(LiveOf(i, out _)); return l; }
    }

    /// <summary>A row's live state: its leaves are whole leaves of the owner's plan (a row never splits one), so the k-th row owns the next n_k samples.
    /// ⚠ A negated WORLD row flattens to its bare leaf (the <c>None</c> is a compound the plan descends through) ⇒ its sample is inverted here.</summary>
    private string? LiveOf(int row, out bool holds)
    {
        holds = false;
        if (Live is not { } live || row >= _leafCounts.Count || _rows[row].IsCustom || _leafCounts[row] != 1) return null;
        int at = 0;
        for (int i = 0; i < row; i++) at += _leafCounts[i];
        if (at >= live.Count) return null;
        var s = live[at];
        if (!s.Sampled) return "-";
        var r = _rows[row];
        holds = r.Who.Kind == SubjectKind.World && r.Negated ? !s.Holds : s.Holds;
        return (holds ? "yes " : "no ") + FactCatalogue.Num(s.Measured);
    }

    /// <summary>The sentence rows as drawn (rails, the panel probe).</summary>
    public IReadOnlyList<string> Sentences { get { var l = new List<string>(_views.Count); foreach (var v in _views) l.Add(v.Sentence); return l; } }

    private sealed class RowView
    {
        public string Who = "", Fact = "", Value = "", Sentence = "", WhoPath = "", UnitPath = "", AreaPath = "", AreaThingPath = "";
        public IconGlyph? WhoIcon, FactIcon;
        public string? Problem;
    }

    /// <summary>Draws <paramref name="value"/>; true (and <paramref name="value"/> replaced) when the person changed it.</summary>
    public bool Draw(string id, ref SearchPredicateDto? value, IMapPickContext? ctx)
    {
        var refs = ctx?.References;
        Project(value, refs, id);
        bool changed = false;
        ImGuiApi.PushID(id);

        // the group line: "When [all ▾] of these are true:"
        ImGuiApi.TextUnformatted("When");
        ImGuiApi.SameLine();
        string opWord = _op switch { LogicalOperator.Or => "any", LogicalOperator.None => "none", _ => "all" };
        if (NodeEditor.Core.Widgets.Chip.Dropdown("##op", opWord, null, 80f)) ImGuiApi.OpenPopup("op");
        if (ImGuiApi.BeginPopup("op"))
        {
            if (ImGuiApi.Selectable("all of these are true", _op == LogicalOperator.And)) { _op = LogicalOperator.And; changed = true; }
            if (ImGuiApi.Selectable("any of these is true", _op == LogicalOperator.Or)) { _op = LogicalOperator.Or; changed = true; }
            if (ImGuiApi.Selectable("none of these is true", _op == LogicalOperator.None)) { _op = LogicalOperator.None; changed = true; }
            ImGuiApi.EndPopup();
        }
        ImGuiApi.SameLine();
        ImGuiApi.TextUnformatted(_op == LogicalOperator.And ? "of these are true:" : _op == LogicalOperator.Or ? "of these is true:" : "of these is true:");

        int remove = -1, duplicate = -1;
        for (int i = 0; i < _rows.Count; i++)
        {
            ImGuiApi.PushID(i);
            ImGuiApi.Indent(12f);
            if (DrawRow(i, ctx, refs, ref remove, ref duplicate)) changed = true;
            ImGuiApi.Unindent(12f);
            ImGuiApi.PopID();
        }
        if (remove >= 0) { _rows.RemoveAt(remove); changed = true; }
        if (duplicate >= 0) { _rows.Insert(duplicate + 1, _rows[duplicate]); changed = true; }

        ImGuiApi.Spacing();
        if (DrawAddMenu()) changed = true;
        ImGuiApi.SameLine();
        if (DrawPresets()) changed = true;
        ImGuiApi.PopID();

        if (changed)
        {
            value = _rows.Count == 0 ? null : RowProjection.ToDto(_rows, _op);
            _stamp = long.MinValue;   // re-project next frame (the views' texts)
        }
        return changed;
    }

    // ── projection (only when the value moved) ───────────────────────────────────────────────────────────────────────────────

    private void Project(SearchPredicateDto? value, LiveRefScope? refs, string id)
    {
        long stamp = ConditionSentence.Stamp(value);
        if (stamp == _stamp && ReferenceEquals(value, _projected) && ReferenceEquals(refs, _projectedRefs)) return;
        _stamp = stamp; _projected = value; _projectedRefs = refs;
        var (rows, op) = RowProjection.FromDto(value);
        _rows.Clear(); _rows.AddRange(rows); _op = op;
        RebuildViews(refs, id);
    }

    private void RebuildViews(LiveRefScope? refs, string id)
    {
        _views.Clear();
        _leafCounts.Clear();
        Problem = null;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            var v = new RowView
            {
                WhoPath = $"$.{id}[{i}].who", UnitPath = $"$.{id}[{i}].unit", AreaPath = $"$.{id}[{i}].area", AreaThingPath = $"$.{id}[{i}].areaThing",
                Sentence = ConditionSentence.Of(r, refs),
            };
            if (!r.IsCustom)
            {
                var fact = FactCatalogue.Find(r.Fact)!;
                v.Who = WhoText(r.Who, refs);
                v.WhoIcon = r.Who.Kind == SubjectKind.One ? ReferenceLeafDrawers.ChipIcon(r.Who.One, refs) : r.Who.Kind == SubjectKind.World ? null : NodeEditor.Core.Widgets.Icons.Get("kind/count");
                v.Fact = Verb(r.Fact, r.Negated) ?? fact.Words(r.Values, r.Negated);   // a fact with a value chip shows its verb only: the value is the next chip (§4)
                v.FactIcon = NodeEditor.Core.Widgets.Icons.TryGet(fact.IconKey, out var g) ? g : null;
                v.Value = ValueText(r, refs);
                v.Problem = ProblemOf(r);
                Problem ??= v.Problem;
            }
            _views.Add(v);
            _leafCounts.Add(Fdp.Toolkit.ScenarioRules.RuleLeafPlan.LeavesOf(RowProjection.ToDto(r), out _).Count);
        }
    }

    private static string WhoText(Subject who, LiveRefScope? refs) => who.Kind switch
    {
        SubjectKind.World => "",
        SubjectKind.One => who.One.Ref.IsNone && who.One.Spec.IsNone ? "choose who..." : ConditionSentence.NameOf(who.One, refs),
        _ => (who.Kind switch { SubjectKind.Any => "any", SubjectKind.AtLeast => $"at least {who.N}", SubjectKind.All => "all", _ => "none" })
             + " of " + (who.Set == SetKind.Side ? FactCatalogue.SideName(who.Side) + " side"
                        : who.Unit.Ref.IsNone && who.Unit.Spec.IsNone ? "a unit's men..." : ConditionSentence.NameOf(who.Unit, refs) + "'s men"),
    };

    /// <summary>The value chip's text (empty = the fact has no blank of its own: its words already say it).</summary>
    private static string ValueText(ConditionRow r, LiveRefScope? refs) => r.Fact switch
    {
        "inside" => r.Values.Area.IsNone ? "pick an area..." : r.Values.Area.IsShape ? (r.Values.Area.Spec.Effective == RefForm.Circle ? $"a circle of {FactCatalogue.Num(r.Values.Area.Size.X)} m" : "a rectangle")
                                                       : ConditionSentence.NameOf(new EntityLink(r.Values.Area.Area, r.Values.Area.Spec), refs),
        _ => "",
    };

    /// <summary>The IS/HAS chip of a fact whose value is its own chip (the mockup: <i>Objective · Seize the bridge · is · achieved</i>); null = the words say it all.</summary>
    private static string? Verb(string fact, bool neg) => fact switch
    {
        "inside" => neg ? "is not inside" : "is inside",
        "objective-state" => neg ? "is not" : "is",
        "side" => neg ? "side is not" : "side is",
        "strength" => neg ? "strength is below" : "strength is above",
        "ammunition" => neg ? "ammunition is below" : "ammunition is above",
        "fired" => neg ? "has not fired" : "has fired",
        "time" => neg ? "it is not" : "it is",
        _ => null,
    };

    private static string? ProblemOf(ConditionRow r)
    {
        if (r.Who.Kind == SubjectKind.One && r.Who.One.Ref.IsNone && r.Who.One.Spec.IsNone) return "Choose who: the rule cannot be applied until every blank is filled";
        if (r.Who.IsCounted && r.Who.Set == SetKind.UnitsMen && r.Who.Unit.Ref.IsNone && r.Who.Unit.Spec.IsNone) return "Choose the unit: the rule cannot be applied until every blank is filled";
        if (r.Fact == "inside" && r.Values.Area.IsNone) return "Choose an area: the rule cannot be applied until every blank is filled";
        return null;
    }

    // ── one row ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private bool DrawRow(int i, IMapPickContext? ctx, LiveRefScope? refs, ref int remove, ref int duplicate)
    {
        var r = _rows[i];
        var v = _views[i];
        bool changed = false;
        _rowStart = ImGuiApi.GetCursorPosX();
        _rowWidth = ImGuiApi.GetContentRegionAvail().X;
        if (r.IsCustom)
        {
            ImGuiApi.TextDisabled("Custom condition (Expert): " + v.Sentence);
            ImGuiApi.SameLine();
            if (ImGuiApi.SmallButton("Open in Expert view")) ExpertRequested = true;
            changed |= RowMenu(i, ref remove, ref duplicate);
            return changed;
        }

        // WHO
        if (r.Who.Kind != SubjectKind.World)
        {
            if (NodeEditor.Core.Widgets.Chip.Dropdown("##who", v.Who, v.WhoIcon, 260f)) ImGuiApi.OpenPopup("who");
            if (WhoPopup(ref r, ctx, v)) changed = true;
            ImGuiApi.SameLine();
        }
        // IS / HAS (the fact and its verb)
        var fact = FactCatalogue.Find(r.Fact)!;
        if (NodeEditor.Core.Widgets.Chip.Dropdown("##fact", v.Fact, v.FactIcon, 300f)) ImGuiApi.OpenPopup("fact");
        if (FactPopup(ref r, fact)) changed = true;
        // VALUE
        if (FactValuePopupChip(ref r, ctx, refs, v)) changed = true;

        // picks finished this frame
        if (ctx != null)
        {
            if (ctx.TryConsumeEntityPick(v.WhoPath, out var who)) { r = r with { Who = r.Who with { Kind = SubjectKind.One, One = ReferenceLeafDrawers.PickToLink(who, refs) } }; changed = true; }
            if (ctx.TryConsumeEntityPick(v.UnitPath, out var unit)) { r = r with { Who = r.Who with { Set = SetKind.UnitsMen, Unit = ReferenceLeafDrawers.PickToLink(unit, refs) } }; changed = true; }
            if (ctx.TryConsumeAreaEdit(v.AreaPath, out var area)) { r = r with { Values = r.Values with { Area = area } }; changed = true; }
            if (ctx.TryConsumeEntityPick(v.AreaThingPath, out var thing)) { var l = ReferenceLeafDrawers.PickToLink(thing, refs); r = r with { Values = r.Values with { Area = AreaLink.OfEntity(l.Ref, l.Spec) } }; changed = true; }
        }
        changed |= RowMenu(i, ref remove, ref duplicate);
        if (v.Problem != null) ImGuiApi.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), v.Problem);
        if (changed) _rows[i] = r;
        return changed;
    }

    private float _rowStart, _rowWidth;

    /// <summary>Where the last item ended, in the window's local x (what <c>SameLine(x)</c> takes): a column never overlaps a long row, it moves right.</summary>
    private static float LastItemEnd() => ImGuiApi.GetItemRectMax().X - ImGuiApi.GetWindowPos().X + ImGuiApi.GetScrollX();

    private bool RowMenu(int i, ref int remove, ref int duplicate)
    {
        // ⭐ C4 — the live result at ONE x, then the row menu at ONE x (§4: "live … at one x", "row menu at one x")
        float frame = ImGuiApi.GetFrameHeight();
        if (LiveOf(i, out bool holds) is { } live)
        {
            ImGuiApi.SameLine(Math.Max(LastItemEnd() + 8f, _rowStart + _rowWidth - frame - 110f));
            if (live == "-") ImGuiApi.TextDisabled("not sampled");
            else
            {
                var glyph = holds ? new IconGlyph("\uf058", IconPalette.Green) : NodeEditor.Core.Widgets.Icons.Get("form/error");   // CircleCheck / CircleXmark
                ImGuiApi.TextColored(glyph.Color, glyph.Glyph);
                ImGuiApi.SameLine();
                ImGuiApi.TextUnformatted(live.Substring(live.IndexOf(' ') + 1));
            }
        }
        ImGuiApi.SameLine(Math.Max(LastItemEnd() + 8f, _rowStart + _rowWidth - frame));
        if (GlyphWidgets.GlyphButton("##rowmenu", new IconGlyph("", IconPalette.Grey), "Row menu")) ImGuiApi.OpenPopup("rowmenu");   // EllipsisVertical
        if (!ImGuiApi.BeginPopup("rowmenu")) return false;
        if (ImGuiApi.Selectable("Duplicate")) duplicate = i;
        if (ImGuiApi.Selectable("Delete")) remove = i;
        if (ImGuiApi.Selectable("Show in Expert view")) ExpertRequested = true;
        ImGuiApi.EndPopup();
        return false;
    }

    private bool WhoPopup(ref ConditionRow r, IMapPickContext? ctx, RowView v)
    {
        if (!ImGuiApi.BeginPopup("who")) return false;
        bool changed = false;
        if (ctx != null)
        {
            ImGuiApi.TextDisabled("one thing");
            if (ImGuiApi.Selectable("Pick it on the map")) ctx.RequestEntityPick(v.WhoPath, null);
            if (ctx.CanListEntities && ImGuiApi.Selectable("Choose it from a list...")) ctx.RequestEntityListPick(v.WhoPath, null);
            ImGuiApi.Separator();
        }
        ImGuiApi.TextDisabled("a group");
        foreach (var (kind, label) in Quantities)
            if (ImGuiApi.Selectable(label, r.Who.Kind == kind)) { r = r with { Who = r.Who with { Kind = kind } }; changed = true; }
        if (r.Who.Kind == SubjectKind.AtLeast)
        {
            int n = r.Who.N;
            ImGuiApi.SetNextItemWidth(90f);
            if (ImGuiApi.InputInt("N##n", ref n) && n >= 1) { r = r with { Who = r.Who with { N = n } }; changed = true; }
        }
        if (r.Who.IsCounted)
        {
            ImGuiApi.Separator();
            ImGuiApi.TextDisabled("of");
            foreach (var side in Sides)
                if (ImGuiApi.Selectable(FactCatalogue.SideName(side) + " side", r.Who.Set == SetKind.Side && r.Who.Side == side))
                { r = r with { Who = r.Who with { Set = SetKind.Side, Side = side } }; changed = true; }
            if (ctx != null && ImGuiApi.Selectable("a unit's men (pick the unit)...", r.Who.Set == SetKind.UnitsMen)) ctx.RequestEntityPick(v.UnitPath, null);
        }
        ImGuiApi.EndPopup();
        return changed;
    }

    private bool FactPopup(ref ConditionRow r, FactEntry fact)
    {
        if (!ImGuiApi.BeginPopup("fact")) return false;
        bool changed = false;
        if (fact.Negatable)
        {
            if (ImGuiApi.Selectable(fact.Words(r.Values, false), !r.Negated)) { r = r with { Negated = false }; changed = true; }
            if (ImGuiApi.Selectable(fact.Words(r.Values, true), r.Negated)) { r = r with { Negated = true }; changed = true; }
            ImGuiApi.Separator();
        }
        foreach (var e in FactCatalogue.Entries)
        {
            if ((e.Contexts & _contexts) == 0 || e.WorldOnly != (r.Who.Kind == SubjectKind.World) || e.Id == r.Fact) continue;
            if (ImGuiApi.Selectable(e.Words(DefaultsOf(e.Id), false))) { r = r with { Fact = e.Id, Negated = false, Values = DefaultsOf(e.Id) }; changed = true; }
        }
        ImGuiApi.EndPopup();
        return changed;
    }

    /// <summary>The value blank of facts that have one: the area, a percentage, a side, an objective state, a count, a time.</summary>
    private bool FactValuePopupChip(ref ConditionRow r, IMapPickContext? ctx, LiveRefScope? refs, RowView v)
    {
        string? text = r.Fact switch
        {
            "inside" => v.Value,
            "strength" or "ammunition" => FactCatalogue.Num(r.Values.Number * 100) + " %",
            "side" => FactCatalogue.SideName(r.Values.Side),
            "objective-state" => ((ObjectiveState)r.Values.State).ToString().ToLowerInvariant(),
            "fired" => FactCatalogue.Num(Math.Max(1, r.Values.Number))  + " times",
            "time" => FactCatalogue.TimeWords(r.Values.Number, r.Values.Number2),
            _ => null,
        };
        if (text == null) return false;
        ImGuiApi.SameLine();
        IconGlyph? icon = r.Fact == "inside" ? NodeEditor.Core.Widgets.Icons.Get("form/pick") : null;
        if (NodeEditor.Core.Widgets.Chip.Dropdown("##value", text, icon, 220f)) ImGuiApi.OpenPopup("value");
        if (!ImGuiApi.BeginPopup("value")) return false;
        bool changed = false;
        var vals = r.Values;
        switch (r.Fact)
        {
            case "inside":
                if (ctx != null && ImGuiApi.Selectable("Draw it on the map")) ctx.RequestAreaEdit(v.AreaPath, vals.Area.IsShape ? vals.Area : default);
                if (ctx != null && ImGuiApi.Selectable("An area on the map (pick it)...")) ctx.RequestEntityPick(v.AreaThingPath, null);
                break;
            case "strength": case "ammunition":
            {
                float pct = (float)(vals.Number * 100);
                ImGuiApi.SetNextItemWidth(120f);
                if (NumberInput.Float(ref pct, "%")) { vals = vals with { Number = Math.Clamp(pct, 0f, 100f) / 100.0 }; changed = true; }
                break;
            }
            case "side":
                foreach (var side in Sides)
                    if (ImGuiApi.Selectable(FactCatalogue.SideName(side), vals.Side == side)) { vals = vals with { Side = side }; changed = true; }
                break;
            case "objective-state":
                foreach (ObjectiveState s in Enum.GetValues(typeof(ObjectiveState)))
                    if (ImGuiApi.Selectable(s.ToString().ToLowerInvariant(), vals.State == (int)s)) { vals = vals with { State = (int)s }; changed = true; }
                break;
            case "fired":
            {
                int n = (int)Math.Max(1, vals.Number);
                ImGuiApi.SetNextItemWidth(120f);
                if (ImGuiApi.InputInt("times", ref n) && n >= 1) { vals = vals with { Number = n }; changed = true; }
                break;
            }
            case "time":
            {
                double from = vals.Number;
                bool open = vals.Number2 >= double.MaxValue / 2;
                ImGuiApi.SetNextItemWidth(140f);
                if (NumberInput.Double(ref from, "s")) { vals = vals with { Number = Math.Max(0, from) }; changed = true; }
                ImGuiApi.SameLine(); ImGuiApi.TextDisabled("from");
                if (ImGuiApi.Checkbox("no end", ref open)) { vals = vals with { Number2 = open ? double.MaxValue : Math.Max(vals.Number + 60, 60) }; changed = true; }
                if (!open)
                {
                    double to = vals.Number2;
                    ImGuiApi.SetNextItemWidth(140f);
                    if (NumberInput.Double(ref to, "s")) { vals = vals with { Number2 = Math.Max(vals.Number, to) }; changed = true; }
                    ImGuiApi.SameLine(); ImGuiApi.TextDisabled("until");
                }
                break;
            }
        }
        ImGuiApi.EndPopup();
        if (changed) r = r with { Values = vals };
        return changed;
    }

    // ── add / presets ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private bool DrawAddMenu()
    {
        if (ImGuiApi.Button("Add condition")) ImGuiApi.OpenPopup("add");
        if (!ImGuiApi.BeginPopup("add")) return false;
        ConditionRow? added = null;
        ImGuiApi.TextDisabled("A unit");
        foreach (var id in UnitFacts)
            if (Offered(id) && ImGuiApi.Selectable(FactCatalogue.Find(id)!.Words(DefaultsOf(id), false) + "##u" + id))
                added = new ConditionRow(Subject.Thing(default), id, Values: DefaultsOf(id));
        ImGuiApi.Separator();
        ImGuiApi.TextDisabled("A group of units");
        if (Offered("in-the-fight") && ImGuiApi.Selectable("at least N of a side are destroyed"))
            added = new ConditionRow(Subject.OfSide(SubjectKind.AtLeast, ForceId.Hostile, 3), "in-the-fight", Negated: true);
        if (Offered("inside") && ImGuiApi.Selectable("any of a side is inside an area"))
            added = new ConditionRow(Subject.OfSide(SubjectKind.Any, ForceId.Friend), "inside");
        ImGuiApi.Separator();
        ImGuiApi.TextDisabled("An objective or a rule");
        if (Offered("objective-state") && ImGuiApi.Selectable("an objective is achieved"))
            added = new ConditionRow(Subject.Thing(default), "objective-state", Values: DefaultsOf("objective-state"));
        if (Offered("fired") && ImGuiApi.Selectable("a rule has fired"))
            added = new ConditionRow(Subject.Thing(default), "fired", Values: DefaultsOf("fired"));
        ImGuiApi.Separator();
        ImGuiApi.TextDisabled("Time");
        if (Offered("time") && ImGuiApi.Selectable("it is after ..."))
            added = new ConditionRow(Subject.World, "time", Values: DefaultsOf("time"));
        ImGuiApi.EndPopup();
        if (added == null) return false;
        _rows.Add(added);
        return true;
    }

    private bool DrawPresets()
    {
        if (ImGuiApi.Button("From preset")) ImGuiApi.OpenPopup("presets");
        if (!ImGuiApi.BeginPopup("presets")) return false;
        ConditionRow? added = null;
        if (ImGuiApi.Selectable("Reach an area")) added = new ConditionRow(Subject.Thing(default), "inside");
        if (ImGuiApi.Selectable("Side has lost N men")) added = new ConditionRow(Subject.OfSide(SubjectKind.AtLeast, ForceId.Friend, 3), "in-the-fight", Negated: true);
        if (ImGuiApi.Selectable("Strength below 50 %")) added = new ConditionRow(Subject.Thing(default), "strength", Negated: true, Values: new FactValues(Number: 0.5));
        if (ImGuiApi.Selectable("Objective achieved")) added = new ConditionRow(Subject.Thing(default), "objective-state", Values: DefaultsOf("objective-state"));
        if (ImGuiApi.Selectable("5 minutes after the start")) added = new ConditionRow(Subject.World, "time", Values: new FactValues(Number: 300));
        ImGuiApi.EndPopup();
        if (added == null) return false;
        _rows.Add(added);
        return true;
    }

    private bool Offered(string id) => FactCatalogue.Find(id) is { } e && (e.Contexts & _contexts) != 0;

    private static readonly string[] UnitFacts = { "inside", "in-the-fight", "strength", "ammunition", "under-fire", "side" };
    private static readonly (SubjectKind, string)[] Quantities =
        { (SubjectKind.Any, "any of"), (SubjectKind.AtLeast, "at least N of"), (SubjectKind.All, "all of"), (SubjectKind.None, "none of") };
    private static readonly ForceId[] Sides = { ForceId.Friend, ForceId.Hostile, ForceId.Neutral };

    /// <summary>A fresh fact's values: strength 50 %, under fire above 0.5, the objective achieved, fired once, time after 0:00.</summary>
    public static FactValues DefaultsOf(string fact) => fact switch
    {
        "strength" or "ammunition" or "under-fire" => new FactValues(Number: 0.5),
        "objective-state" => new FactValues(State: (int)ObjectiveState.Achieved),
        "fired" => new FactValues(Number: 1),
        "side" => new FactValues(Side: ForceId.Hostile),
        _ => default,
    };
}
