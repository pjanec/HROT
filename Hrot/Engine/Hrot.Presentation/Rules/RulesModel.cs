using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>How the rows are sectioned (RV-3).</summary>
public enum RulesGroupBy { Macro, Subject, Side }

/// <summary>What the rules view shows: how it groups, which side, what text.</summary>
public readonly record struct RulesOptions(RulesGroupBy GroupBy = RulesGroupBy.Macro, ForceId? Side = null, string Search = "", int SubtreeOf = 0);

/// <summary>A reference from a row to another entity (a <c>DependsOn</c> / <c>ActsOn</c> id): the id, the words it is shown by, and whether it is itself a rule row (a click selects it).</summary>
public readonly record struct RuleLink(int NetworkId, string Text, bool IsRule);

/// <summary>A stretch of a rule's life in one state, sim seconds; <see cref="Hatched"/> = it is waiting out a delay / hold / cooldown.</summary>
public readonly record struct RuleSpan(double From, double To, ObjectiveState State, bool Hatched = false);

/// <summary>One row of the rules view: a rule entity as the replicated tree shows it, with its words, colour, past and links.</summary>
public sealed record RuleRow(
    int NetworkId, Entity Entity, int Depth, bool HasChildren, ObjectiveStatus Status,
    string Title, string Icon, string StateWords, uint StateColour,
    int SubjectNetworkId, string? SubjectName,
    IReadOnlyList<RuleSpan> Spans, IReadOnlyList<double> Fires, double? ExpectedAt,
    IReadOnlyList<RuleLink> WaitsOn, IReadOnlyList<RuleLink> Starts, bool Authored = false, string? Macro = null, int MacroInstance = 0,
    IReadOnlyList<ClockPin>? Pins = null);

/// <summary>⭐ CE-4221 (R4) — an instant a rule is bound to the CLOCK: its deadline, or a <c>ScenarioTime</c> condition of a trigger's <c>when</c>. Drawn as a Pinned diamond.</summary>
public sealed record ClockPin(double At, string Label);

/// <summary>A section of rows: the name it is shown by and its rows in tree order.</summary>
public sealed record RuleGroup(string Name, IReadOnlyList<RuleRow> Rows);

/// <summary>The whole view this frame.</summary>
public sealed record RulesView(IReadOnlyList<RuleGroup> Groups, double Now)
{
    public IEnumerable<RuleRow> Rows => Groups.SelectMany(g => g.Rows);
    public RuleRow? Find(int networkId) => Rows.FirstOrDefault(r => r.NetworkId == networkId);
}

/// <summary>The past of the rules, as the lanes index holds it (<see cref="ScenarioRulesTimelineLanes"/>): per rule, its state changes and its fires, at sim seconds.</summary>
public interface IRulesHistory
{
    IReadOnlyList<(double Time, ObjectiveState State)> StateChanges(int networkId);
    IReadOnlyList<(double Time, int Count)> Fires(int networkId);
}

/// <summary>⭐ RV-4 — <see cref="IRulesHistory"/> over a <see cref="TimelineIndex"/>: the <c>objective.state</c> and <c>rules.fired</c> lanes, placed at the index's frame sim times. A fire of Value 0 (the first sight of a trigger that has not fired) is skipped, as the lane's contract says.</summary>
public sealed class IndexRulesHistory : IRulesHistory
{
    private readonly Dictionary<int, List<(double, ObjectiveState)>> _states = new();
    private readonly Dictionary<int, List<(double, int)>> _fires = new();

    public IndexRulesHistory(TimelineIndex index)
    {
        var sim = index.FrameSim;
        double At(int frame) => frame >= 0 && frame < sim.Count ? sim[frame] : double.NaN;
        if (index.Has(ScenarioRulesTimelineLanes.State.Id))
            foreach (var e in index.Lane(ScenarioRulesTimelineLanes.State.Id))
                if (!double.IsNaN(At(e.Frame))) (_states.TryGetValue(e.Subject, out var l) ? l : _states[e.Subject] = new()).Add((At(e.Frame), (ObjectiveState)(int)e.Value));
        if (index.Has(ScenarioRulesTimelineLanes.Fired.Id))
            foreach (var e in index.Lane(ScenarioRulesTimelineLanes.Fired.Id))
                if ((int)e.Value > 0 && !double.IsNaN(At(e.Frame))) (_fires.TryGetValue(e.Subject, out var l) ? l : _fires[e.Subject] = new()).Add((At(e.Frame), (int)e.Value));
    }

    public IReadOnlyList<(double Time, ObjectiveState State)> StateChanges(int networkId) => _states.TryGetValue(networkId, out var l) ? l : Array.Empty<(double, ObjectiveState)>();
    public IReadOnlyList<(double Time, int Count)> Fires(int networkId) => _fires.TryGetValue(networkId, out var l) ? l : Array.Empty<(double, int)>();
}

/// <summary>
/// ⭐⭐ CE-4216 (rules view R2, RV-3 / PH-1) — <b>THE reader of the replicated rule tree</b>, pure (no ImGui): every entity with an <see cref="ObjectiveStatus"/>, its <see cref="ObjectiveLinks"/> and the macro it came from. The rules view builds its rows
/// from it, and the player's HUD (PH-1) reads the SAME shape — ⛔ one reader, never a second parse of the status.
/// </summary>
public static class RulesReader
{
    /// <summary>One rule entity as stored: the status, its parent / subject network ids, and the macro path it was expanded from (null = authored in the scenario).</summary>
    public sealed record Entry(Entity Entity, int NetworkId, ObjectiveStatus Status, int ParentNetworkId, int SubjectNetworkId, string? Macro, bool Authored = false, int MacroInstance = 0);

    /// <param name="includeAuthored">⭐ H1 — false skips the RV-3a authored rows (a rule not yet running): the player's HUD shows what RUNS, and parsing every rule's JSON each frame is the rules window's cost, not the HUD's.</param>
    public static IReadOnlyList<Entry> Read(EntityRepository world, bool includeAuthored = true)
    {
        var list = new List<Entry>();
        var seen = new HashSet<int>();
        bool origin = world.IsComponentTypeRegistered<MacroOrigin>();
        string? MacroFor(Entity e) => origin && world.HasComponent<MacroOrigin>(e) ? MacroOf(world, world.GetComponentRO<MacroOrigin>(e).Instance.NetworkId) : null;
        int InstanceFor(Entity e) => origin && world.HasComponent<MacroOrigin>(e) ? world.GetComponentRO<MacroOrigin>(e).Instance.NetworkId : 0;
        if (world.IsComponentTypeRegistered<ObjectiveStatus>())
        {
            bool links = world.IsComponentTypeRegistered<ObjectiveLinks>();
            foreach (var e in world.Query().With<ObjectiveStatus>().Build())
            {
                int net = NetworkIdResolver.RuntimeNetworkIdOf(world, e);
                if (net == 0) continue;   // a row with no durable identity cannot be asked, linked or selected
                var l = links && world.HasComponent<ObjectiveLinks>(e) ? world.GetComponentRO<ObjectiveLinks>(e) : default;
                seen.Add(net);
                list.Add(new Entry(e, net, world.GetComponentRO<ObjectiveStatus>(e), l.Parent.NetworkId, l.Subject.NetworkId, MacroFor(e), MacroInstance: InstanceFor(e)));
            }
        }
        // ⭐ RV-3a — BEFORE a run no status exists: a rule entity carries only its AUTHORED params. ONE row type, two sources: the status (above) wins
        if (includeAuthored && world.TryGetTable(typeof(BehaviorStartRecord), out _))
            foreach (var e in world.Query().WithManaged<BehaviorStartRecord>().Build())
            {
                int net = NetworkIdResolver.RuntimeNetworkIdOf(world, e);
                if (net == 0 || seen.Contains(net)) continue;
                var rec = ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(e);
                if (rec == null || AuthoredRules.Parse(world, e, net, rec.BehaviorName, rec.JsonParams) is not { } a) continue;
                list.Add(new Entry(e, net, a.Status, a.ParentNetworkId, a.SubjectNetworkId, MacroFor(e), Authored: true, MacroInstance: InstanceFor(e)));
            }
        return list;
    }

    /// <summary>The macro path of the instance with network id <paramref name="instanceNetworkId"/>, or null.</summary>
    public static string? MacroOf(EntityRepository world, int instanceNetworkId)
    {
        var inst = NetworkIdResolver.FindEntityByNetworkId(world, instanceNetworkId);
        if (inst.IsNull || !world.IsAlive(inst) || !world.TryGetTable(typeof(MacroInstance), out _) || !world.HasManagedComponent<MacroInstance>(inst)) return null;
        var path = ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<MacroInstance>(inst)?.Macro;
        return string.IsNullOrEmpty(path) ? null : path;
    }
}

/// <summary>
/// ⭐⭐ CE-4216 (rules view R2; docs/DESIGN_Rules_View_UI.md RV-3, RV-5, RV-6) — the rules view as DATA: rows from the replicated tree (<see cref="RulesReader"/>), the tree from <c>ObjectiveLinks.Parent</c>, the three groupings, the side filter
/// (<c>Owner</c> and <c>VisibleSides</c>), the search, the state in words and colour, the PAST from the lanes' history plus the current span, and the Overview's lines. Pure: the window draws it, the rails drive it.
/// </summary>
public static class RulesModel
{
    public const string ScenarioGroup = "Scenario", NoSubjectGroup = "(no subject)", AuthoredWords = "Authored", NotRunning = "not running — start the exercise";

    // ABGR (ImGui's packed colour) of the mockup's legend (docs/designs/rules-view/mockup/Main.html)
    public static uint Rgb(byte r, byte g, byte b) => 0xFF000000u | (uint)b << 16 | (uint)g << 8 | r;
    public static readonly uint Achieved = Rgb(0x4F, 0xBF, 0x85), Active = Rgb(0x5A, 0xA8, 0xFF), Grey = Rgb(0x8A, 0x90, 0x99),
                                Acting = Rgb(0xF0, 0x90, 0x4A), Cooldown = Rgb(0xD9, 0xA8, 0x3F), Failed = Rgb(0xE5, 0x64, 0x5A);

    /// <summary>The icon of a role. ASCII on purpose: the panel's font has no glyph for the mockup's ◎ ◈ ⚡ (the picker pane met the same).</summary>
    public static string IconOf(ObjectiveRole role) => role switch { ObjectiveRole.Trigger => "T", ObjectiveRole.Measure => "M", _ => "O" };

    public static string SideName(ForceId side) => side switch { ForceId.Friend => "Blue", ForceId.Hostile => "Red", _ => "Neutral" };

    /// <summary>m:ss of a span in seconds.</summary>
    public static string Clock(double seconds) { long t = (long)Math.Max(0, Math.Floor(seconds)); return $"{t / 60}:{t % 60:00}"; }

    /// <summary>
    /// ⭐ RV-3 — the state in words: objectives and measures by <see cref="ObjectiveState"/> (<i>Active 6:10</i> = since <c>ChangedAt</c>); triggers by their machine (<i>Armed · Holding · Waiting · Acting 2/3 · Cooldown · 1× · Done</i>) while
    /// they are switched on. ⚠ <i>Cooldown · n/max</i> of the mockup needs the trigger's max fires, which the status does not carry: it shows <c>n×</c> (the fires so far).
    /// </summary>
    public static string StateWords(in ObjectiveStatus s, double now)
    {
        if (s.Role == ObjectiveRole.Trigger && s.State == ObjectiveState.Active)
            return s.TriggerState switch
            {
                TriggerState.Acting => s.StepCount > 0 ? $"Acting {s.StepCur}/{s.StepCount}" : "Acting",
                TriggerState.Cooldown => s.FireCount > 0 ? $"Cooldown · {s.FireCount}×" : "Cooldown",
                TriggerState.Disarmed => "Disarmed",
                _ => s.TriggerState.ToString(),   // Armed · Holding · Waiting · Fired · Done
            };
        return s.State == ObjectiveState.Active ? $"Active {Clock(now - s.ChangedAt)}" : s.State.ToString();
    }

    public static uint ColourOf(in ObjectiveStatus s)
    {
        if (s.Role == ObjectiveRole.Trigger && s.State == ObjectiveState.Active)
            return s.TriggerState switch
            {
                TriggerState.Acting => Acting, TriggerState.Cooldown => Cooldown, TriggerState.Holding => Achieved, TriggerState.Waiting => Cooldown,
                TriggerState.Done => Achieved, TriggerState.Fired => Acting, _ => Grey,
            };
        return s.State switch { ObjectiveState.Achieved => Achieved, ObjectiveState.Active => Active, ObjectiveState.Failed => Failed, _ => Grey };
    }

    /// <summary>The colour of a PAST span (the lane's state) — hatched spans (a wait) take the cooldown colour.</summary>
    public static uint ColourOfSpan(in RuleSpan span) => span.Hatched ? Cooldown : span.State switch { ObjectiveState.Achieved => Achieved, ObjectiveState.Active => Active, ObjectiveState.Failed => Failed, _ => Grey };

    /// <summary>⭐ W-H3 — is this rule for <paramref name="side"/>: a side its <c>VisibleSides</c> names, or its OWN side (<see cref="MissionSummaryModel.SideOfRule"/> — never <c>Owner</c>, a capture field). ONE rule for the rules view's filter and the player HUD.</summary>
    internal static bool ShownTo(EntityRepository world, RulesReader.Entry e, ForceId side)
        => (e.Status.VisibleSides != 0 && (e.Status.VisibleSides & (1 << (int)side)) != 0) || MissionSummaryModel.SideOfRule(world, e) == side;

    private static string TitleOf(in ObjectiveStatus s, int net) => s.Title.ToString() is { Length: > 0 } t ? t : $"rule #{net}";

    /// <summary>The names an entity is known by in the rows' links and subject column.</summary>
    /// <summary>The ids of <paramref name="root"/> and everything below it by <c>Parent</c>.</summary>
    public static HashSet<int> SubtreeIds(IReadOnlyList<RulesReader.Entry> entries, int root)
    {
        var set = new HashSet<int> { root };
        bool grew = true;
        while (grew) { grew = false; foreach (var e in entries) if (e.ParentNetworkId != 0 && set.Contains(e.ParentNetworkId) && set.Add(e.NetworkId)) grew = true; }
        return set;
    }

    internal static string NameOf(EntityRepository world, Entity e, int net)
    {
        if (!e.IsNull && world.IsAlive(e))
        {
            if (world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(e) && world.GetComponentRO<EntityInfo>(e).Name.ToString() is { Length: > 0 } n) return n;
        }
        return $"#{net}";
    }

    public static RulesView Build(EntityRepository world, in RulesOptions optionsIn, IRulesHistory? history, double now)
    {
        var options = optionsIn;
        var entries = RulesReader.Read(world);
        var byNet = entries.ToDictionary(e => e.NetworkId);

        // ── the filter: a side (Owner, or VisibleSides), the search; the ancestors of a match stay so the tree is intact
        bool Matches(RulesReader.Entry e)
            => (options.Side is not { } side || ShownTo(world, e, side))
            && (string.IsNullOrWhiteSpace(options.Search) || TitleOf(e.Status, e.NetworkId).Contains(options.Search.Trim(), StringComparison.OrdinalIgnoreCase));
        // ⭐ R3 — "Show the moments on the timeline": only the subtree of one rule (the rule and everything under it by Parent)
        var subtree = options.SubtreeOf == 0 ? null : SubtreeIds(entries, options.SubtreeOf);
        var kept = new HashSet<int>(entries.Where(e => (subtree == null || subtree.Contains(e.NetworkId)) && Matches(e)).Select(e => e.NetworkId));
        foreach (var id in kept.ToList())
            for (int p = byNet[id].ParentNetworkId, guard = 0; p != 0 && (subtree == null || subtree.Contains(p)) && byNet.TryGetValue(p, out var parent) && guard < 64; p = parent.ParentNetworkId, guard++) kept.Add(p);

        // ── which section a row is in
        string GroupOf(RulesReader.Entry e) => options.GroupBy switch
        {
            RulesGroupBy.Macro => e.Macro ?? ScenarioGroup,
            RulesGroupBy.Side => SideName(MissionSummaryModel.SideOfRule(world, e)),
            _ => e.SubjectNetworkId != 0 ? NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, e.SubjectNetworkId), e.SubjectNetworkId) : NoSubjectGroup,
        };
        var members = kept.Select(id => byNet[id]).GroupBy(GroupOf).ToList();
        int Order(string name) => options.GroupBy switch
        {
            RulesGroupBy.Macro => name == ScenarioGroup ? 0 : 1,
            RulesGroupBy.Side => name switch { "Blue" => 0, "Red" => 1, _ => 2 },
            _ => name == NoSubjectGroup ? 1 : 0,
        };

        // how many units each macro instance holds (for "Starts")
        var unitsOf = new Dictionary<int, int>();
        if (world.IsComponentTypeRegistered<MacroOrigin>())
            foreach (var e in world.Query().With<MacroOrigin>().Build()) { int inst = world.GetComponentRO<MacroOrigin>(e).Instance.NetworkId; unitsOf[inst] = unitsOf.GetValueOrDefault(inst) + 1; }

        RuleLink LinkOf(int net)
        {
            if (byNet.TryGetValue(net, out var rule)) return new RuleLink(net, TitleOf(rule.Status, net), true);
            var e = NetworkIdResolver.FindEntityByNetworkId(world, net);
            string name = NameOf(world, e, net);
            if (!e.IsNull && world.TryGetTable(typeof(MacroInstance), out _) && world.HasManagedComponent<MacroInstance>(e))
                name += $" (macro {RulesReader.MacroOf(world, net) ?? "?"}, {unitsOf.GetValueOrDefault(net)} units)";
            return new RuleLink(net, name, false);
        }

        var groups = new List<RuleGroup>();
        foreach (var g in members.OrderBy(g => Order(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var inGroup = g.ToDictionary(e => e.NetworkId);
            var childrenOf = g.Where(e => e.ParentNetworkId != 0 && inGroup.ContainsKey(e.ParentNetworkId) && e.ParentNetworkId != e.NetworkId)
                              .GroupBy(e => e.ParentNetworkId).ToDictionary(x => x.Key, x => x.OrderBy(e => e.NetworkId).ToList());
            var roots = g.Where(e => e.ParentNetworkId == 0 || !inGroup.ContainsKey(e.ParentNetworkId) || e.ParentNetworkId == e.NetworkId).OrderBy(e => e.NetworkId).ToList();
            var rows = new List<RuleRow>();
            var seen = new HashSet<int>();
            void Walk(RulesReader.Entry e, int depth)
            {
                if (!seen.Add(e.NetworkId)) return;   // a cycle in the links must not loop the view
                childrenOf.TryGetValue(e.NetworkId, out var kids);
                rows.Add(RowOf(world, e, depth, kids is { Count: > 0 }, history, now, LinkOf));
                if (kids != null) foreach (var k in kids) Walk(k, depth + 1);
            }
            foreach (var r in roots) Walk(r, 0);
            groups.Add(new RuleGroup(g.Key, rows));
        }
        return new RulesView(groups, now);
    }

    private static RuleRow RowOf(EntityRepository world, RulesReader.Entry e, int depth, bool hasChildren, IRulesHistory? history, double now, Func<int, RuleLink> link)
    {
        var s = e.Status;
        var waits = new List<RuleLink>(); var starts = new List<RuleLink>();
        for (int i = 0; i < Math.Min((int)s.DependsOnCount, 8); i++) if (s.DependsOn[i] != 0) waits.Add(link(s.DependsOn[i]));
        for (int i = 0; i < Math.Min((int)s.ActsOnCount, 8); i++) if (s.ActsOn[i] != 0) starts.Add(link(s.ActsOn[i]));
        string? subject = e.SubjectNetworkId != 0 ? NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, e.SubjectNetworkId), e.SubjectNetworkId) : null;
        var pins = ClockPins(world, e.Entity, s);
        if (e.Authored)   // ⭐ RV-3a — authored, not running: no state, no past, nothing expected
            return new RuleRow(e.NetworkId, e.Entity, depth, hasChildren, s, TitleOf(s, e.NetworkId), IconOf(s.Role), AuthoredWords, Grey,
                               e.SubjectNetworkId, subject, Array.Empty<RuleSpan>(), Array.Empty<double>(), null, waits, starts, Authored: true, Macro: e.Macro, MacroInstance: e.MacroInstance, Pins: pins);
        return new RuleRow(e.NetworkId, e.Entity, depth, hasChildren, s, TitleOf(s, e.NetworkId), IconOf(s.Role), StateWords(s, now), ColourOf(s),
                           e.SubjectNetworkId, subject, SpansOf(s, history?.StateChanges(e.NetworkId), now), FiresOf(s, history?.Fires(e.NetworkId)),
                           s.ExpectedAt > now ? s.ExpectedAt : null, waits, starts, Macro: e.Macro, MacroInstance: e.MacroInstance, Pins: pins);
    }

    /// <summary>
    /// ⭐ CE-4221 (R4) — the clock-pinned instants of a rule: its <c>Deadline</c> (any kind), and for a trigger every <c>ScenarioTime</c> leaf of its <c>when</c> that starts later than zero (read from the authored params the node holds; none when the node holds none).
    /// </summary>
    public static IReadOnlyList<ClockPin> ClockPins(EntityRepository world, Entity entity, in ObjectiveStatus s)
    {
        var pins = new List<ClockPin>();
        if (s.Deadline > 0f) pins.Add(new ClockPin(s.Deadline, $"deadline {Clock(s.Deadline)}"));
        if (s.Role == ObjectiveRole.Trigger && world.IsAlive(entity) && world.TryGetTable(typeof(BehaviorStartRecord), out _) && world.HasManagedComponent<BehaviorStartRecord>(entity)
            && ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(entity) is { } rec && !string.IsNullOrWhiteSpace(rec.JsonParams))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rec.JsonParams);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.NameEquals("when") || prop.Name.Equals("when", StringComparison.OrdinalIgnoreCase)) CollectClock(prop.Value, pins);
            }
            catch (System.Text.Json.JsonException) { /* tolerant: bad JSON pins nothing */ }
        }
        return pins;
    }

    private static void CollectClock(System.Text.Json.JsonElement el, List<ClockPin> into)
    {
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                bool clock = false; double from = 0;
                foreach (var p in el.EnumerateObject())
                {
                    if (p.Name == "$type" && p.Value.ValueKind == System.Text.Json.JsonValueKind.String && p.Value.GetString() == "ScenarioTime") clock = true;
                    else if (p.Name.Equals("fromSeconds", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.Number) from = p.Value.GetDouble();
                    else CollectClock(p.Value, into);
                }
                if (clock && from > 0) into.Add(new ClockPin(from, $"clock {Clock(from)}"));
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) CollectClock(item, into);
                break;
        }
    }

    /// <summary>
    /// The past at ACTUAL times: the lane's state changes become spans (<c>change i → change i+1</c>), the last one runs to now in the status's own state — and, for a trigger that is holding, waiting or cooling down, hatched (a wait, not work).
    /// No history (a node without the lanes, an index not scanned yet): the one span the status itself knows, <c>ChangedAt → now</c>.
    /// </summary>
    public static IReadOnlyList<RuleSpan> SpansOf(in ObjectiveStatus s, IReadOnlyList<(double Time, ObjectiveState State)>? changes, double now)
    {
        bool waiting = s.Role == ObjectiveRole.Trigger && s.State == ObjectiveState.Active && s.TriggerState is TriggerState.Holding or TriggerState.Waiting or TriggerState.Cooldown;
        var spans = new List<RuleSpan>();
        if (changes is { Count: > 0 })
        {
            var sorted = changes.OrderBy(c => c.Time).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
                if (sorted[i + 1].Time > sorted[i].Time) spans.Add(new RuleSpan(sorted[i].Time, sorted[i + 1].Time, sorted[i].State));
            var last = sorted[^1];
            if (now > last.Time) spans.Add(new RuleSpan(last.Time, now, s.State, waiting));   // the status is the truth about NOW, even when the index lags
        }
        else if (now > s.ChangedAt) spans.Add(new RuleSpan(s.ChangedAt, now, s.State, waiting));
        return spans;
    }

    /// <summary>The fire times of a trigger: the lane's, else (no history) the one the status remembers (<c>LastFired</c>).</summary>
    public static IReadOnlyList<double> FiresOf(in ObjectiveStatus s, IReadOnlyList<(double Time, int Count)>? fires)
        => fires is { Count: > 0 } ? fires.Select(f => f.Time).OrderBy(t => t).ToList()
         : s.FireCount > 0 ? new[] { s.LastFired } : Array.Empty<double>();

    // ── RV-5 the actions of a row

    /// <summary>One thing the instructor may ask of a row: its label, the request, and why it is not valid now (null = valid).</summary>
    public sealed record RuleAction(string Label, RuleRequest Request, string? DisabledReason);

    /// <summary>⭐ RV-5 — a trigger: Fire now · Disarm · Re-arm · Reset; an objective or a measure: Activate · Deactivate · Reset. Every item is always listed; an invalid one carries its reason (<see cref="RuleCommands.DisabledReason"/>).</summary>
    public static IReadOnlyList<RuleAction> ActionsOf(in ObjectiveStatus sIn)
    {
        var s = sIn;
        RuleAction A(string label, RuleRequest r) => new(label, r, RuleCommands.DisabledReason(s, r));
        return s.Role == ObjectiveRole.Trigger
            ? new[] { A("Fire now", RuleRequest.Fire), A("Disarm", RuleRequest.Disarm), A("Re-arm", RuleRequest.Rearm), A("Reset", RuleRequest.Reset) }
            : new[] { A("Activate", RuleRequest.Activate), A("Deactivate", RuleRequest.Deactivate), A("Reset", RuleRequest.Reset) };
    }

    /// <summary>⭐ RV-5 / RV-3a — the actions of a ROW: an authored one (no status yet) lists the same items, every one disabled with <see cref="NotRunning"/>.</summary>
    public static IReadOnlyList<RuleAction> ActionsOf(RuleRow row)
        => !row.Authored ? ActionsOf(row.Status)
         : ActionsOf(row.Status).Select(a => a with { DisabledReason = NotRunning }).ToList();

    // ── RV-6 the Overview

    /// <summary>One line of the Overview: a label and its value; <see cref="Colour"/> 0 = the default.</summary>
    public readonly record struct OverviewLine(string Label, string Value, uint Colour = 0);

    /// <summary>⭐ RV-6 — the Overview / Status tab of a row, as lines: title, kind, state, owner, subject, progress, value / target, score, pass, required / scored, reason, fired n× / last. (<i>Waits on</i> / <i>Starts</i> are the row's link lists.)</summary>
    public static IReadOnlyList<OverviewLine> OverviewOf(RuleRow row, double now)
    {
        var s = row.Status;
        var lines = new List<OverviewLine>
        {
            new("Title", row.Title),
            new("Kind", s.Kind.ToString() is { Length: > 0 } k && !k.Equals(s.Role.ToString(), StringComparison.OrdinalIgnoreCase) ? $"{s.Role} · {k}" : s.Role.ToString()),
            new("State", row.StateWords, row.StateColour),
            new("Owner", SideName(s.Owner)),
        };
        if (row.SubjectName != null) lines.Add(new("Subject", row.SubjectName));
        if (s.ProgressNeed > 0) lines.Add(new("Progress", $"{s.ProgressCur} / {s.ProgressNeed}"));
        if (s.Role == ObjectiveRole.Measure || s.Target != 0f) lines.Add(new("Value / target", $"{s.Value:0.##} / {s.Target:0.##}"));
        if (s.Role != ObjectiveRole.Trigger && !row.Authored)
        {
            lines.Add(new("Score", $"{s.Score:0.##}"));
            if (s.Pass > 0f) lines.Add(new("Pass", $"{s.Pass:0.##} — {(s.Score >= s.Pass ? "GO" : "NO-GO")}"));
            lines.Add(new("Required / scored", $"{(s.Required != 0 ? "required" : "optional")} · {(s.Scored != 0 ? "scored" : "not scored")}"));
        }
        if (s.Reason.ToString() is { Length: > 0 } reason) lines.Add(new("Reason", reason));
        if (s.Role == ObjectiveRole.Trigger && !row.Authored)
            lines.Add(new("Fired", s.FireCount == 0 ? "never" : $"{s.FireCount}× · last at {Clock(s.LastFired)}"));
        if (s.Deadline > 0f) lines.Add(new("Deadline", Clock(s.Deadline)));
        return lines;
    }
}
