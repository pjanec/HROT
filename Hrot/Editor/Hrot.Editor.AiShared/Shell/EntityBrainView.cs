using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Perception.Components;
using Hrot.Editor.AiShared.Variables;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>One action channel's "right now" (an <see cref="ActionStatus"/> row).</summary>
public sealed record BrainActionRow(string Channel, int ActionId, string Reason, bool IsHold, float Progress, float Needed);

/// <summary>One utility decision's last choice (<see cref="Fdp.Toolkit.Utility.UtilityDecisionLog"/> — on every brain unit, CE-1319 BV-61).</summary>
public sealed record BrainChoice(int DecisionId, long Winner, long PreviousWinner, float Margin, int Switches);

/// <summary>One HSM region's active state (CE-1261).</summary>
public sealed record BrainRegion(int Region, int StateIndex, string State);

/// <summary>One journal entry, decoded.</summary>
public sealed record BrainChange(uint Seq, float At, string Kind, string Text);

/// <summary>
/// ⭐⭐ <c>CE-1248</c> (R-1026, BV-1…BV-8) — what a unit is doing and why, from what the brain already records. ONE reader for the
/// Brain tab, its panel dump and <c>GET /entities/{id}/brain</c> (BV-8); it reads a world, so the replay sandbox works the same
/// (AV-6). 📄 docs/DESIGN_Entity_Brain_View.md.
/// </summary>
public sealed record BrainSnapshot(
    bool OnBrainNode,
    string Doing,
    string? Origin,
    string? Urgency,
    double? SinceSeconds,
    string? Paused,
    string? Sop,
    string? Roe,
    IReadOnlyList<BrainActionRow> RightNow,
    IReadOnlyList<BrainRegion> Regions,
    string? Squad,
    IReadOnlyList<BrainChoice> Choices,
    int Contacts,
    int HeardOnly,
    uint JournalSeq,
    bool JournalOn,
    IReadOnlyList<BrainChange> Changes,
    bool Traced = false)
{
    /// <summary>The unit's brain, or <c>null</c> when it has none (no <see cref="BehaviorState"/>).</summary>
    public static BrainSnapshot? Read(ISimulationView view, Entity unit, Func<int, string?>? behaviorName = null,
        Func<int, BehaviorDefinition?>? definition = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (unit == Entity.Null || !view.IsAlive(unit) || !Has<BehaviorState>(view, unit)) return null;
        string Name(int hash) => hash == BehaviorIds.None ? "nothing" : behaviorName?.Invoke(hash) ?? $"#{hash}";

        ref readonly var b = ref view.GetComponentRO<BehaviorState>(unit);
        double now = view is EntityRepository r && r.HasSingleton<GlobalTime>() ? r.GetSingleton<GlobalTime>().TotalTime : 0d;
        bool running = b.ActiveBehaviorHash != BehaviorIds.None;

        string? paused = null;
        if (view is EntityRepository repo && repo.TryGetTable(typeof(PausedTask), out _) && repo.HasManagedComponent<PausedTask>(unit)
            && view.GetManagedComponentRO<PausedTask>(unit) is { } p)
            paused = $"{p.BehaviorName} ({p.Origin})";

        string? sop = null;
        if (Has<SopState>(view, unit) && view.GetComponentRO<SopState>(unit) is { SopHash: not BehaviorIds.None } s)
            sop = $"{Name(s.SopHash)} ({s.SopOrigin}){(s.SopFaulted != 0 ? " — faulted" : "")}";

        string? roe = null;
        if (Has<Roe>(view, unit))
        {
            ref readonly var e = ref view.GetComponentRO<Roe>(unit);
            roe = $"fire {e.Fire}, reactions {e.Reactions}, force {e.ForceLevel} (set by {e.SetBy})";
        }

        var rows = new List<BrainActionRow>(3);
        bool onBrainNode = Has<ActionStatus>(view, unit) || Has<BrainJournal>(view, unit);
        if (Has<ActionStatus>(view, unit))
        {
            ref readonly var a = ref view.GetComponentRO<ActionStatus>(unit);
            AddRow(rows, "Locomotion", a.Locomotion);
            AddRow(rows, "Weapon", a.Weapon);
            AddRow(rows, "Interaction", a.Interaction);
        }

        var choices = new List<BrainChoice>();
        if (Has<Fdp.Toolkit.Utility.UtilityDecisionLog>(view, unit))
        {
            ref readonly var log = ref view.GetComponentRO<Fdp.Toolkit.Utility.UtilityDecisionLog>(unit);
            for (int i = 0; i < Fdp.Toolkit.Utility.UtilityDecisionLog.MaxDecisions; i++)
            {
                var slot = log.Slots[i];
                if (slot.DecisionId == 0) continue;
                choices.Add(new BrainChoice(slot.DecisionId, slot.Winner, slot.PreviousWinner, slot.Margin, slot.SwitchCount));
            }
        }

        int contacts = 0, heard = 0;
        if (Has<TargetMemory>(view, unit))
        {
            ref readonly var m = ref view.GetComponentRO<TargetMemory>(unit);
            for (int i = 0; i < m.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in m, i)) heard++;
                else contacts++;
            }
        }

        string? squad = null;
        if (view is EntityRepository sr && sr.TryGetTable(typeof(Fdp.Toolkit.Squad.SquadDrillTrace), out _)
            && sr.HasManagedComponent<Fdp.Toolkit.Squad.SquadDrillTrace>(unit)
            && view.GetManagedComponentRO<Fdp.Toolkit.Squad.SquadDrillTrace>(unit) is { Active: true } d)
            squad = $"play {d.Kind}, phase {d.Phase} (GET /entities/{{id}}/squad for the members' orders)";

        var changes = new List<BrainChange>();
        uint seq = 0;
        bool journalOn = Has<BrainJournal>(view, unit);
        if (journalOn)
        {
            ref readonly var j = ref view.GetComponentRO<BrainJournal>(unit);
            seq = j.Seq;
            for (int i = j.Count - 1; i >= 0; i--)   // newest first
            {
                var e = j.At(i);
                changes.Add(new BrainChange(e.Seq, e.At, e.Kind.ToString(), BrainJournalText.Describe(in e, Name, definition)));
            }
        }

        return new BrainSnapshot(
            onBrainNode,
            running ? Name(b.ActiveBehaviorHash) : "nothing",
            running ? b.Origin.ToString() : null,
            running && b.Urgency != ReactionUrgency.NotAReaction ? b.Urgency.ToString() : null,
            running && b.RunSince > 0 ? Math.Max(0d, now - b.RunSince) : null,
            paused, sop, roe, rows, BrainRegions.Of(view, unit, b.ActiveBehaviorHash, definition), squad, choices, contacts, heard, seq,
            journalOn, changes,
            // ⭐ CE-1319 (BV-61) — the graph's trace rings are observe-only; the tab says whether this unit's are on (in replay: were)
            Has<BTreeTraceWorkingMemory1024>(view, unit) || Has<HsmTraceWorkingMemory1024>(view, unit)
            || Has<Fdp.Toolkit.Utility.UtilityTraceWorkingMemory1024>(view, unit));
    }

    private static void AddRow(List<BrainActionRow> rows, string channel, in ActionStatusRow row)
    {
        if (row.ActionId == 0 && row.Reason == ActionReason.None) return;
        rows.Add(new BrainActionRow(channel, row.ActionId, ActionStatusOf.Label(row.Reason), ActionStatusOf.IsHold(row.Reason),
            row.Progress, row.Needed));
    }

    // ⚠ A host may never register a type — asking HasComponent of it would throw (the EntitySummary rule).
    internal static bool Has<T>(ISimulationView view, Entity entity) where T : unmanaged
        => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(entity);

    /// <summary>The snapshot as JSON — the panel dump and the debug API share it.</summary>
    public JsonObject ToJson()
    {
        var rows = new JsonArray();
        foreach (var r in RightNow)
            rows.Add(new JsonObject { ["channel"] = r.Channel, ["actionId"] = r.ActionId, ["reason"] = r.Reason, ["hold"] = r.IsHold,
                                      ["progress"] = r.Progress, ["needed"] = r.Needed });
        var regions = new JsonArray();
        foreach (var g in Regions) regions.Add(new JsonObject { ["region"] = g.Region, ["state"] = g.State, ["stateIndex"] = g.StateIndex });
        var choices = new JsonArray();
        foreach (var c in Choices)
            choices.Add(new JsonObject { ["decisionId"] = c.DecisionId, ["winner"] = c.Winner, ["previousWinner"] = c.PreviousWinner,
                                         ["margin"] = c.Margin, ["switches"] = c.Switches });
        var changes = new JsonArray();
        foreach (var c in Changes) changes.Add(ChangeJson(c));
        return new JsonObject
        {
            ["onBrainNode"] = OnBrainNode, ["doing"] = Doing, ["origin"] = Origin, ["urgency"] = Urgency, ["sinceSeconds"] = SinceSeconds,
            ["paused"] = Paused, ["sop"] = Sop, ["roe"] = Roe, ["rightNow"] = rows, ["regions"] = regions, ["squad"] = Squad,
            ["choices"] = choices, ["contacts"] = Contacts, ["heardOnly"] = HeardOnly,
            ["journal"] = new JsonObject { ["on"] = JournalOn, ["seq"] = JournalSeq, ["changes"] = changes },
            ["traced"] = Traced,
        };
    }

    public static JsonObject ChangeJson(BrainChange c)
        => new() { ["seq"] = c.Seq, ["at"] = Math.Round(c.At, 2), ["kind"] = c.Kind, ["text"] = c.Text };
}

/// <summary>⭐ <c>CE-1249</c> — a journal entry in words (names resolved here, never stored — BV-9).</summary>
public static class BrainJournalText
{
    public static string Describe(in BrainJournalEntry e, Func<int, string> name, Func<int, BehaviorDefinition?>? definition = null)
    {
        string origin = ((BehaviorOrigin)e.Origin).ToString();
        return e.Kind switch
        {
            BrainJournalKind.TaskStarted => $"started {name(e.A)} ({origin}"
                + (e.Urgency != 0 ? $", {(ReactionUrgency)e.Urgency}" : "") + ")"
                + (e.B != 0 ? $", replacing {name(e.B)}" : "")
                + (e.Cause != 0 ? $" after {(Fdp.Toolkit.Perception.Events.SensorChange)e.Cause}" : ""),
            BrainJournalKind.TaskPaused => $"paused {name(e.A)} for {name(e.B)}",
            BrainJournalKind.TaskResumed => $"resumed {name(e.A)}",
            BrainJournalKind.TaskEnded => $"{name(e.A)} ended: {(Fbt.NodeStatus)e.Urgency}"
                + (e.B != 0 ? $" (fault {(Fdp.Toolkit.Behavior.Events.BehaviorFaultCode)e.B})" : ""),
            BrainJournalKind.TaskCleared => $"{name(e.A)} cleared by {origin}",
            BrainJournalKind.TaskRefused => $"refused {name(e.A)} ({origin}): {(BrainRefusal)e.Cause}",
            BrainJournalKind.SopStarted => $"SOP {name(e.A)} set ({origin})",
            BrainJournalKind.SopEnded => $"SOP {name(e.A)} ended",
            BrainJournalKind.DecisionSwitched => $"decision {e.A}: option {e.Cause} → {e.B} (score {e.Point.X:0.##})",
            BrainJournalKind.MoveStarted => (e.Point == Vector3.Zero
                ? $"move action {e.A} started"
                : $"move action {e.A} to ({e.Point.X:0}, {e.Point.Y:0})")
                + (e.Cause != 0 ? $" ({(MoveSource)e.Cause})" : ""),
            BrainJournalKind.PlayStarted => $"play {e.A} started",
            BrainJournalKind.PlayPhase => $"play {e.A}: phase {e.B}",
            BrainJournalKind.PlayEnded => $"play {e.A} ended",
            BrainJournalKind.OrderIssued => $"ordered #{e.A}: {name(e.B)}",
            BrainJournalKind.StateChanged => (e.A & 0xFFFF) == 0xFFFF   // from no state: the region was entered
                ? $"{name(e.B)} region {e.Origin}: entered {BrainRegions.StateName(definition, e.B, (ushort)((uint)e.A >> 16))}"
                : $"{name(e.B)} region {e.Origin}: {BrainRegions.StateName(definition, e.B, (ushort)(e.A & 0xFFFF))}"
                  + $" → {BrainRegions.StateName(definition, e.B, (ushort)((uint)e.A >> 16))}",
            _ => e.Kind.ToString(),
        };
    }
}

/// <summary>⭐ <c>CE-1261</c> (BV-32) — the active state of each HSM region of the unit's running machine (empty for a BTree / blueprint
/// unit, or where the definition is unknown), named from the behaviour's <c>HsmMetadata</c>.</summary>
public static class BrainRegions
{
    public static unsafe IReadOnlyList<BrainRegion> Of(ISimulationView view, Entity unit, int behavior, Func<int, BehaviorDefinition?>? definition)
    {
        if (definition?.Invoke(behavior) is not { HsmDefinition: not null } def || view is not EntityRepository repo)
            return Array.Empty<BrainRegion>();
        if (!RootHsmAccess.TryGetInstance(repo, unit, out byte* instance, out int size)) return Array.Empty<BrainRegion>();
        ushort* leaves = Fhsm.Kernel.HsmKernel.GetActiveLeafIds(instance, size, out int count);
        if (leaves == null) return Array.Empty<BrainRegion>();
        var list = new List<BrainRegion>(count);
        for (int r = 0; r < count; r++)
            list.Add(new BrainRegion(r, leaves[r], def.HsmMetadata?.GetStateName(leaves[r]) ?? $"state {leaves[r]}"));
        return list;
    }

    /// <summary>The name of state <paramref name="state"/> of behaviour <paramref name="behavior"/>, or its index.</summary>
    public static string StateName(Func<int, BehaviorDefinition?>? definition, int behavior, ushort state)
        => definition?.Invoke(behavior)?.HsmMetadata is { } m ? m.GetStateName(state) : $"state {state}";
}

public sealed record EntityBrainViewPanelViewModel(string PanelId, string PanelKind, bool HasTarget, int EntityIndex, JsonObject? Brain)
    : IPanelViewModel
{
    public JsonNode Dump() => new JsonObject
    {
        ["panelId"] = PanelId, ["panelKind"] = PanelKind, ["hasTarget"] = HasTarget, ["entityIndex"] = EntityIndex,
        ["brain"] = Brain?.DeepClone(),
    };
}

/// <summary>⭐ <c>CE-1254</c> (BV-21) — the behaviour a unit is running, as the editor knows it: its registered name (the catalog opens a
/// graph by it) and its tier. Pure. 📄 docs/DESIGN_Entity_Brain_View.md §9.</summary>
public sealed record RunningRef(int BehaviorId, string Name, string Tier);

public static class RunningBehaviourLocator
{
    public static RunningRef? Resolve(EntityRepository world, Entity unit, Func<int, string?> name, Func<int, BehaviorDefinition?>? definition)
    {
        if (!world.IsComponentTypeRegistered<BehaviorState>() || !world.HasComponent<BehaviorState>(unit)) return null;
        int id = world.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash;
        if (id == 0 || name(id) is not { Length: > 0 } n) return null;
        string tier = definition?.Invoke(id)?.BrainTier switch
        {
            Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierHsm => "HSM",
            Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree => "BTree",
            Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBlueprint => "Blueprint",
            _ => "",
        };
        return new RunningRef(id, n, tier);
    }
}

/// <summary>⭐ <c>CE-1248</c> — the Brain tab (read-only; editing stays in the AI tab, BV-1).</summary>
public sealed class EntityBrainView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<int, string?> _behaviorName;
    private readonly Func<int, BehaviorDefinition?>? _definition;
    private readonly Func<BehaviorRegistry?>? _registry;
    private readonly Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? _blueprints;

    /// <param name="registry">⭐ <c>CE-1315</c> — the behaviour registry the slot reader names slots through; without it the
    /// <i>Running behaviours</i> section is absent.</param>
    /// <param name="blueprints">⭐ <c>CE-1315</c> — the blueprint registry, so attached blueprints are listed with their variables.</param>
    public EntityBrainView(Func<EntityRepository?> world, Func<int, string?> behaviorName, Func<int, BehaviorDefinition?>? definition = null,
        Func<BehaviorRegistry?>? registry = null, Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? blueprints = null,
        Func<DetailsViewRegistry?>? embeds = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _behaviorName = behaviorName ?? throw new ArgumentNullException(nameof(behaviorName));
        _definition = definition;
        _registry = registry;
        _blueprints = blueprints;
        _embeds = embeds;
    }

    // ── ⭐⭐ CE-1316 / CE-1314 (Brain View §12a BV-38, §12b BV-46, R-1036, R-1037) — THE TAB ABSORBS TWO VIEWS ─────────────────────────
    //   The AI tab's editing (task, SOP, ROE, instance params) and the Entity Blueprints panel (+ Add, Remove, Apply, Revert) are drawn
    //   HERE as sections, unchanged: the host hands their descriptors in as EMBEDS (they live in assemblies above this one, so the
    //   composition root is the only place that sees both ends — the same reference wall the blueprints view hit, BP-475).

    private readonly Func<DetailsViewRegistry?>? _embeds;
    private readonly Dictionary<string, IDetailsViewInstance> _embedInstances = new(StringComparer.Ordinal);

    /// <summary>The views this tab draws as its own sections (the host's composition; empty where it hands none).</summary>
    public IReadOnlyList<DetailsViewDescriptor> Embeds => _embeds?.Invoke()?.All ?? Array.Empty<DetailsViewDescriptor>();

    /// <summary>The section instance of embed <paramref name="id"/> (created on first use, one per tab).</summary>
    public IDetailsViewInstance EmbedInstance(string id)
    {
        if (_embedInstances.TryGetValue(id, out var v)) return v;
        var d = Embeds.First(x => x.Id == id);
        return _embedInstances[id] = d.Create();
    }

    private void DrawEmbeds(DetailsContext context, string idScope, bool replay)
    {
        foreach (var d in Embeds)
        {
            if (!d.AppliesTo(context) || !Section(d.Title, "embed." + d.Id)) continue;
            if (replay) ImGui.BeginDisabled();   // BV-46: managing and assigning is disabled in the replay browser
            EmbedInstance(d.Id).Draw(context, idScope + "/" + d.Id);
            if (replay) ImGui.EndDisabled();
        }
    }

    /// <summary>⭐ <c>CE-1315</c> — every behaviour running on <paramref name="unit"/> (task, SOP, paused task, attached blueprints),
    /// through <see cref="OccurrenceSlotCatalog"/>; empty without a registry.</summary>
    internal IReadOnlyList<RunningBehaviourView> Running(EntityRepository world, Entity unit)
        => _registry?.Invoke() is { } reg
            ? BrainRunningBehaviours.From(OccurrenceSlotCatalog.Read(world, unit, reg, _blueprints?.Invoke()))
            : Array.Empty<RunningBehaviourView>();

    private (EntityBrainViewPanelViewModel Vm, BrainSnapshot? Brain) BuildAndPublish(DetailsContext context, string idScope)
    {
        var panelId = $"{idScope}/{EntityBrainViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);
        bool hasTarget = context.Entities is { Count: 1 };
        var entity = hasTarget ? context.Entities[0] : Entity.Null;
        var brain = hasTarget && _world() is { } w ? BrainSnapshot.Read(w, entity, _behaviorName, _definition) : null;
        var vm = new EntityBrainViewPanelViewModel(panelId, EntityBrainViewDescriptor.ViewId, hasTarget, hasTarget ? entity.Index : -1,
            brain?.ToJson());
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return (vm, brain);
    }

    internal EntityBrainViewPanelViewModel SimulateDraw(DetailsContext context, string idScope) => BuildAndPublish(context, idScope).Vm;

    public void Dispose()
    {
        foreach (var v in _embedInstances.Values) v.Dispose();
        _embedInstances.Clear();
    }

    public void Draw(DetailsContext context, string idScope)
    {
        var (_, b) = BuildAndPublish(context, idScope);
        if (b is null)
        {
            // ⭐ CE-1316 (BV-47) — a unit with no brain still has its blueprints here (the tab applies to every single entity)
            ImGui.TextDisabled("No brain on this unit.");
            DrawEmbeds(context, idScope, context.Mode == VariableRunState.Replay);
            return;
        }
        var unit = context.Entities[0];
        var world = _world();
        bool replay = context.Mode == VariableRunState.Replay;
        // ⭐ CE-1317 (BV-48) — drawing this tab IS debugging the unit: its traces are armed while any tab draws it (released ~1 s after)
        if (!replay && world is not null) Hrot.Common.Diagnostics.Gizmos.BrainObservation.Current?.Viewed(world, unit);
        if (!b.OnBrainNode)
            ImGui.TextColored(Amber, "This node does not run the unit's brain: only its task, SOP and ROE are known here.");

        // ⭐⭐ CE-1310 (BV-34, BV-64) — every section is a collapsing header with a STABLE id (the label may carry a count); which are
        //   open is ImGui's own memory, nothing forced
        if (Section("Doing", "doing"))
        {
            Row("Task", b.Doing + (b.SinceSeconds is { } s ? $"  ({s:0} s)" : ""));
            if (b.Origin is { } o) Row("Ordered by", o + (b.Urgency is { } u ? $"  · {u}" : ""));
            if (b.Paused is { } p) Row("Paused", p);
            if (b.Sop is { } sop) Row("SOP", sop);
            foreach (var g in b.Regions) Row($"Region {g.Region}", g.State);
            if (b.Roe is { } roe) Row("ROE", roe);
        }

        if (b.RightNow.Count > 0 && Section("Right now", "now"))
        {
            foreach (var r in b.RightNow)
            {
                var text = r.Reason + (r.Needed > 0 ? $"  {r.Progress:0.0}/{r.Needed:0.0}" : r.Progress > 0 ? $"  {r.Progress:0.0}" : "");
                ImGui.TextUnformatted(r.Channel);
                ImGui.SameLine(LabelWidth);
                if (r.IsHold) ImGui.TextColored(Amber, text); else ImGui.TextUnformatted(text);
            }
        }

        if (b.Squad is { } squad && Section("Squad", "squad")) ImGui.TextWrapped(squad);

        if (Section("Why", "why"))
        {
            if (b.Choices.Count == 0) ImGui.TextDisabled("No utility decision has run on this unit.");
            foreach (var c in b.Choices)
                Row($"Decision {c.DecisionId}", $"winner {c.Winner} (was {c.PreviousWinner}), margin {c.Margin:0.00}, {c.Switches} switches");
            // ⭐ CE-1319 (BV-61) — the graph's step-by-step trace is kept only for an observed unit; say which, honestly, in replay too
            if (!b.Traced) ImGui.TextDisabled(TraceLine(context.Mode));
        }

        if (Section("Knows", "knows"))
            Row("Contacts", $"{b.Contacts} seen" + (b.HeardOnly > 0 ? $", {b.HeardOnly} heard only" : ""));

        // ⭐⭐ CE-1312 (BV-56…BV-58) — a COMMANDER's picture, his units' contacts, and his report / order histories (recorded: they replay)
        if (world is not null && BrainCommandView.Read(world, unit, _behaviorName, (float)NowOf(world)) is { } cmd)
            DrawCommand(world, unit, cmd, replay);

        if (Section($"Recent changes ({b.Changes.Count})", "changes"))
        {
            if (!b.JournalOn) ImGui.TextDisabled("Journal off on this host (or no brain here).");
            foreach (var c in b.Changes)
            {
                ImGui.TextDisabled($"{c.At,7:0.0}s");
                ImGui.SameLine();
                ImGui.TextWrapped(c.Text);
            }
        }

        if (world is not null && _settings.For(world, unit) is { Count: > 0 } groups && Section("Settings", "settings"))
            DrawSettings(world, unit, groups, editable: !replay);

        if (world is not null && Running(world, unit) is { Count: > 0 } running && Section($"Running behaviours ({running.Count})", "running"))
            DrawRunning(running);

        DrawEmbeds(context, idScope, replay);

        if (world is not null && !replay && Section("Show on map", "map")) DrawShowOnMap(world, unit);

        if (Section("Debug", "debug")) DrawDebugControls(context, idScope);
    }

    private static double NowOf(EntityRepository w) => w.HasSingleton<GlobalTime>() ? w.GetSingleton<GlobalTime>().TotalTime : 0d;

    private static void DrawCommand(EntityRepository world, Entity unit, BrainCommandView cmd, bool replay)
    {
        if (cmd.HasPicture && Section("Command picture", "picture"))
        {
            MapPin(world, unit, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.CommandPicture, replay);
            if (cmd.Picture.Count == 0) ImGui.TextDisabled("Nothing reported yet.");
            foreach (var row in cmd.Picture) ImGui.TextUnformatted(row);
        }
        if (Section($"My units' contacts ({cmd.Contacts.Count})", "contacts"))
        {
            MapPin(world, unit, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.SquadContacts, replay);
            if (!cmd.HasContacts || cmd.Contacts.Count == 0)
                ImGui.TextDisabled("Empty: his units have no contacts of their own eyes (above the first level they have none); see Command picture.");
            foreach (var row in cmd.Contacts) ImGui.TextUnformatted(row);
        }
        if (Section($"Reports ({cmd.Reports.Count})", "reports"))
        {
            foreach (var row in cmd.Subordinates) ImGui.TextUnformatted(row);
            History("reports", cmd.Reports, "No report to or from him yet.");
        }
        if (Section($"Orders ({cmd.OrdersReceived.Count + cmd.OrdersSent.Count})", "orders"))
        {
            ImGui.TextDisabled("Received");
            History("received", cmd.OrdersReceived, "None from his superior.");
            ImGui.TextDisabled("Sent");
            History("sent", cmd.OrdersSent, "He has ordered nobody yet.");
        }
    }

    /// <summary>A history list: newest first, a fixed height that scrolls (the last 32 — a longer one is the Timeline, BV-59).</summary>
    private static void History(string id, IReadOnlyList<BrainHistoryRow> rows, string empty)
    {
        if (rows.Count == 0) { ImGui.TextDisabled("  " + empty); return; }
        float h = Math.Min(rows.Count, 8) * ImGui.GetTextLineHeightWithSpacing() + 6f;
        if (ImGui.BeginChild("##hist." + id, new Vector2(0, h), ImGuiChildFlags.Border))
            foreach (var r in rows)
            {
                ImGui.TextDisabled($"{r.At,7:0.0}s");
                ImGui.SameLine();
                ImGui.TextUnformatted(r.Text);
            }
        ImGui.EndChild();
    }

    /// <summary>⭐ CE-1312 — a list's own "on map" checkbox: that one family pinned on this commander (BV-37, BV-50).</summary>
    private static void MapPin(EntityRepository world, Entity unit, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags family, bool replay)
    {
        if (replay) return;
        bool on = Hrot.Common.Diagnostics.Gizmos.GizmoPins.IsPinned(world, unit, family);
        if (ImGui.Checkbox("on map##pin." + family, ref on)) Hrot.Common.Diagnostics.Gizmos.GizmoPins.Set(world, unit, family, on);
    }

    /// <summary>⭐ <c>CE-1310</c> — a section's collapsing header: <paramref name="id"/> is the stable ImGui id, so a label carrying a count
    /// keeps its open state. ⛔ Nothing forced open (BV-64) — only a frame rail opens them, once, to photograph.</summary>
    private static bool Section(string label, string id)
    {
        if (ExpandForRail) ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        return ImGui.CollapsingHeader($"{label}###brain.{id}");
    }

    private readonly BrainSettingRegistry _settings = BrainSettingRegistry.Default;

    /// <summary>⭐⭐ <c>CE-1311</c> (BV-41) — the settings as the user's GROUPS (never components): one slider per setting with the type's
    /// value beside it and a reset when an authored value is set. A write is local, on the node that runs the brain (R-1036).</summary>
    private static void DrawSettings(EntityRepository world, Entity unit,
        IReadOnlyList<(string Group, IReadOnlyList<(IBrainSettingProvider Provider, BrainSetting Setting)> Settings)> groups, bool editable)
    {
        foreach (var (group, settings) in groups)
        {
            ImGui.TextDisabled(group);
            foreach (var (provider, s) in settings)
            {
                ImGui.PushID(provider.Group + "/" + s.Key);
                ImGui.TextUnformatted("  " + s.Label);
                ImGui.SameLine(LabelWidth);
                float v = s.Value;
                ImGui.SetNextItemWidth(160f);
                if (!editable) ImGui.BeginDisabled();
                string format = s.Presets is { Count: > 0 } presets ? $"%.2f  {Nearest(presets, v)}" : "%.2f";
                if (ImGui.SliderFloat("##v", ref v, s.Min, s.Max, format)) provider.Write(world, unit, s.Key, v);
                if (!editable) ImGui.EndDisabled();
                if (s.Tooltip is { } tip && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
                ImGui.SameLine();
                ImGui.TextDisabled($"type {s.TypeValue:0.00}");
                if (s.IsAuthored && editable)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("reset")) provider.Reset(world, unit, s.Key);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Back to the type's value (clears the authored value).");
                }
                ImGui.PopID();
            }
        }
    }

    private static string Nearest(IReadOnlyList<(string Name, float Value)> presets, float v)
        => presets.OrderBy(p => MathF.Abs(p.Value - v)).First().Name;

    /// <summary>⭐⭐ <c>CE-1317</c> (BV-49, BV-50) — ONE map-gizmo family per checkbox, the brain-debug families only (⛔ no Path, the user's
    /// ruling); nothing is pinned by opening the tab, and no control pins them all.</summary>
    internal static readonly Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags[] MapFamilies =
        Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All.Where(f => f != Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path).ToArray();

    private static void DrawShowOnMap(EntityRepository world, Entity unit)
    {
        for (int i = 0; i < MapFamilies.Length; i++)
        {
            var f = MapFamilies[i];
            bool on = Hrot.Common.Diagnostics.Gizmos.GizmoPins.IsPinned(world, unit, f);
            if (i > 0 && i % 4 != 0) ImGui.SameLine();
            if (ImGui.Checkbox(Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.Label(f) + "##map" + i, ref on))
                Hrot.Common.Diagnostics.Gizmos.GizmoPins.Set(world, unit, f, on);
        }
    }

    /// <summary>⭐ <c>CE-1319</c> (BV-61) — what the tab says when the unit's graph trace rings are absent.</summary>
    internal static string TraceLine(VariableRunState mode) => mode == VariableRunState.Replay
        ? "Graph trace: not traced during the recording (Observe all brain units while recording traces everyone)."
        : "Graph trace: armed while this tab shows the unit (it fills from the next decision).";

    /// <summary>A frame rail opens the nodes once to photograph them; the product never forces an open state (BV-64).</summary>
    internal static bool ExpandForRail;

    /// <summary>⭐ <c>CE-1315</c> (BV-42, BV-43) — one tree node per running behaviour; its parameters and variables are the inspector's
    /// own read-only dump (<see cref="Fdp.Presentation.Utils.ImGuiPropertyTree"/>: the registered renderers, fixed lists at their Count).
    /// ImGui remembers what is open (BV-64).</summary>
    private static void DrawRunning(IReadOnlyList<RunningBehaviourView> running)
    {
        for (int i = 0; i < running.Count; i++)
        {
            var r = running[i];
            ImGui.PushID(i);
            if (ExpandForRail) ImGui.SetNextItemOpen(true, ImGuiCond.Once);
            if (ImGui.TreeNodeEx($"{r.OwnerLabel} · {r.Name}##rb", ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                DrawValues(r.Parameters, r.Variables, r.HiddenVariableMembers);
                for (int p = 0; p < r.Parts.Count; p++)
                {
                    var part = r.Parts[p];
                    ImGui.PushID(p);
                    if (ExpandForRail) ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    if (ImGui.TreeNodeEx((part.IsHostedChild ? "hosted · " : "") + part.Label + "##part", ImGuiTreeNodeFlags.SpanAvailWidth))
                    {
                        DrawValues(part.Parameters, part.Variables, Array.Empty<string>());
                        ImGui.TreePop();
                    }
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    private static void DrawValues(object? parameters, object? variables, IReadOnlyCollection<string> hidden)
    {
        if (parameters is not null)
        {
            ImGui.TextDisabled("Parameters");
            ImGui.PushID("params");
            Fdp.Presentation.Utils.ImGuiPropertyTree.Render(parameters, null, null, out _);
            ImGui.PopID();
        }
        if (variables is not null)
        {
            ImGui.TextDisabled("Variables");
            ImGui.PushID("vars");
            Fdp.Presentation.Utils.ImGuiPropertyTree.Render(variables, null, m => !hidden.Contains(m.Name), out _);
            ImGui.PopID();
        }
        if (parameters is null && variables is null) ImGui.TextDisabled("(no parameters or variables)");
    }

    private string? _breakNote;

    /// <summary>⭐ <c>CE-1257</c> / <c>CE-1258</c> — the debugging buttons (follow, open the running graph, break when it changes its mind),
    /// where the host composes the controller (the editor). ⭐ <c>CE-1317</c> — in the tab's <b>Debug</b> section.</summary>
    private void DrawDebugControls(DetailsContext context, string idScope)
    {
        if (Hrot.Common.Diagnostics.Gizmos.UnitDebugController.Current is not { } c || _world() is not { } w)
        {
            ImGui.TextDisabled("No debugger on this host.");
            return;
        }
        var unit = context.Entities[0];
        // ⛔ CE-1317 (BV-48) — the "Debug this unit" checkbox is gone: viewing the tab arms the traces, and the map families are
        //   pinned one by one in "Show on map"
        // ⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-2) — watch him: the map's 3-D camera follows this unit (wheel: in to his eyes)
        if (Hrot.UI.Common.Map3D.ViewControl.Of(w) is { } view)
        {
            if (ImGui.Button("Follow"))
                _breakNote = view.FollowUnit(unit) ?? "the map camera follows it (W/A/S/D or Esc lets go)";
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The map's 3-D camera follows this unit: the wheel goes in to his eyes, RMB orbits.");
        }
        // ⭐⭐ CE-1318 (BV-55, R-1039) — open THE RUNNING behaviour's graph: from a PINNED tab the graph is pinned to this unit (its own
        //   document, the tab header names him); from the docked tab it follows the selection, as this tab does.
        //   ⛔ SUPERSEDED (CE-1254 BV-22): it set the ONE global debug pin, so every graph followed the last unit opened.
        if (c.OpenGraph is { } open && RunningBehaviourLocator.Resolve(w, unit, _behaviorName, _definition) is { } running)
        {
            bool pinned = IsPinnedScope(idScope);
            if (ImGui.Button("Open running behaviour"))
            {
                bool ok = pinned && c.OpenGraphPinned is { } openPinned
                    ? openPinned(running.Name, unit, PinLabelOf(w, unit))
                    : open(running.Name);
                _breakNote = ok ? $"opened {running.Name}{(pinned ? $" pinned to {PinLabelOf(w, unit)}" : "")}" : $"{running.Name} has no graph in this editor";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(pinned
                    ? $"Opens {running.Name} ({running.Tier}) PINNED to this unit: its own tab, its overlay stays on him."
                    : $"Opens {running.Name} ({running.Tier}); its live overlay follows the selection, as this tab does.");
        }
        if (c.ArmJournalBreak is { } arm)
        {
            if (ImGui.Button("Break when it changes its mind"))
                _breakNote = arm(w, unit) is { } id ? $"breakpoint {id} armed" : "no breakpoint manager on this host";
        }
        if (_breakNote is { } note) ImGui.TextDisabled(note);
    }

    /// <summary>⭐ <c>CE-1318</c> — this tab is a PIN (its window id is <see cref="Hrot.Editor.AiShared.Windows.DetailsWindow.PinIdFor"/>'s), not the
    /// docked tab that follows the selection.</summary>
    internal static bool IsPinnedScope(string idScope) => idScope.StartsWith("details_pin_", StringComparison.Ordinal);

    /// <summary>How a pinned graph's tab names the unit: its network id, else its local handle.</summary>
    internal static string PinLabelOf(EntityRepository world, Entity unit)
    {
        long net = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
            ? Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(world, unit) : 0;
        return net != 0 ? $"#{net}" : $"#{unit.Index}";
    }

    private static readonly Vector4 Amber = new(1f, 0.75f, 0.2f, 1f);
    private const float LabelWidth = 110f;

    private static void Row(string label, string value)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine(LabelWidth);
        ImGui.TextWrapped(value);
    }
}

/// <summary>⭐ <c>CE-1248</c> (BV-1) — <c>details.brain</c>, one entity with a brain, every host. ⚠ Rank 34, not the designed 44: the
/// Summary card (35) is the default card on every host and 44 would have replaced it for every brain unit — the tab is offered, the AI
/// tab (45, editor) and Summary stay defaults.</summary>
public static class EntityBrainViewDescriptor
{
    public const string ViewId = "details.brain";
    public const int Rank = 34;

    /// <param name="embeds">⭐ CE-1316 / CE-1314 — the views this tab draws as sections (the AI tab's editing, the Entity Blueprints
    /// panel). ⭐ BV-47: with embeds the tab applies to EVERY single entity a section applies to — a unit with no brain keeps its
    /// blueprints here; without, only to one with a brain (as before).</param>
    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<int, string?> behaviorName,
        Func<int, BehaviorDefinition?>? definition = null, Func<BehaviorRegistry?>? registry = null,
        Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? blueprints = null, Func<DetailsViewRegistry?>? embeds = null)
    {
        var withBrain = DetailsViewPredicates.OneEntityWithBrain(e => world() is { } w && BrainSnapshot.Has<BehaviorState>(w, e));
        return new(ViewId, "Brain", Rank,
               context => withBrain(context)
                          || (DetailsViewPredicates.ExactlyOneEntity(context) && embeds?.Invoke() is { } r && r.All.Any(d => d.AppliesTo(context))),
               () => new EntityBrainView(world, behaviorName, definition, registry, blueprints, embeds));
    }
}
