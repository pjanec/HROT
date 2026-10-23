using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Perception.Components;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>One action channel's "right now" (an <see cref="ActionStatus"/> row).</summary>
public sealed record BrainActionRow(string Channel, int ActionId, string Reason, bool IsHold, float Progress, float Needed);

/// <summary>One utility decision's last choice (observed units only — <see cref="Fdp.Toolkit.Utility.UtilityDecisionLog"/>).</summary>
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
    IReadOnlyList<BrainChange> Changes)
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
            journalOn, changes);
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

    public EntityBrainView(Func<EntityRepository?> world, Func<int, string?> behaviorName, Func<int, BehaviorDefinition?>? definition = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _behaviorName = behaviorName ?? throw new ArgumentNullException(nameof(behaviorName));
        _definition = definition;
    }

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

    public void Dispose() { }

    public void Draw(DetailsContext context, string idScope)
    {
        var (_, b) = BuildAndPublish(context, idScope);
        if (b is null) { ImGui.TextDisabled("No brain on this unit."); return; }
        DrawDebugControls(context);
        if (!b.OnBrainNode)
            ImGui.TextColored(Amber, "This node does not run the unit's brain — only its task, SOP and ROE are known here.");

        Header("Doing");
        Row("Task", b.Doing + (b.SinceSeconds is { } s ? $"  ({s:0} s)" : ""));
        if (b.Origin is { } o) Row("Ordered by", o + (b.Urgency is { } u ? $"  · {u}" : ""));
        if (b.Paused is { } p) Row("Paused", p);
        if (b.Sop is { } sop) Row("SOP", sop);
        foreach (var g in b.Regions) Row($"Region {g.Region}", g.State);
        if (b.Roe is { } roe) Row("ROE", roe);

        if (b.RightNow.Count > 0)
        {
            Header("Right now");
            foreach (var r in b.RightNow)
            {
                var text = r.Reason + (r.Needed > 0 ? $"  {r.Progress:0.0}/{r.Needed:0.0}" : r.Progress > 0 ? $"  {r.Progress:0.0}" : "");
                ImGui.TextUnformatted(r.Channel);
                ImGui.SameLine(LabelWidth);
                if (r.IsHold) ImGui.TextColored(Amber, text); else ImGui.TextUnformatted(text);
            }
        }

        if (b.Squad is { } squad) { Header("Squad"); ImGui.TextWrapped(squad); }

        Header("Why this choice");
        if (b.Choices.Count == 0) ImGui.TextDisabled("Utility scores are kept only while the unit is observed (Debug this unit).");
        foreach (var c in b.Choices)
            Row($"Decision {c.DecisionId}", $"winner {c.Winner} (was {c.PreviousWinner}), margin {c.Margin:0.00}, {c.Switches} switches");

        Header("Knows");
        Row("Contacts", $"{b.Contacts} seen" + (b.HeardOnly > 0 ? $", {b.HeardOnly} heard only" : ""));

        Header("Recent changes");
        if (!b.JournalOn) ImGui.TextDisabled("Journal off on this host (or no brain here).");
        foreach (var c in b.Changes)
        {
            ImGui.TextDisabled($"{c.At,7:0.0}s");
            ImGui.SameLine();
            ImGui.TextWrapped(c.Text);
        }
    }

    private string? _breakNote;

    /// <summary>⭐ <c>CE-1257</c> / <c>CE-1258</c> — the two debugging buttons, where the host composes the controller (the editor).</summary>
    private void DrawDebugControls(DetailsContext context)
    {
        if (Hrot.Common.Diagnostics.Gizmos.UnitDebugController.Current is not { } c || _world() is not { } w) return;
        var unit = context.Entities[0];
        bool on = c.IsDebugged(unit);
        if (ImGui.Checkbox("Debug this unit", ref on)) c.Set(w, unit, on);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Arms its traces, pins its map gizmos and keeps the AI graph's live overlay on it (not on the selection).");
        // ⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-2) — watch him: the map's 3-D camera follows this unit (wheel: in to his eyes)
        if (Hrot.UI.Common.Map3D.ViewControl.Of(w) is { } view)
        {
            ImGui.SameLine();
            if (ImGui.Button("Follow"))
                _breakNote = view.FollowUnit(unit) ?? "the map camera follows it (W/A/S/D or Esc lets go)";
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The map's 3-D camera follows this unit: the wheel goes in to his eyes, RMB orbits.");
        }
        // ⭐ CE-1254 (BV-21, BV-22) — open THE RUNNING behaviour's graph, with the overlay pinned to this unit ("Debug this unit").
        if (c.OpenGraph is { } open && RunningBehaviourLocator.Resolve(w, unit, _behaviorName, _definition) is { } running)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open running behaviour"))
            {
                c.Set(w, unit, true);
                _breakNote = open(running.Name) ? $"opened {running.Name}" : $"{running.Name} has no graph in this editor";
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Opens {running.Name} ({running.Tier}) with this unit's live state lit — and keeps it on this unit.");
        }
        if (c.ArmJournalBreak is { } arm)
        {
            ImGui.SameLine();
            if (ImGui.Button("Break when it changes its mind"))
                _breakNote = arm(w, unit) is { } id ? $"breakpoint {id} armed" : "no breakpoint manager on this host";
            if (_breakNote is { } note) { ImGui.SameLine(); ImGui.TextDisabled(note); }
        }
    }

    private static readonly Vector4 Amber = new(1f, 0.75f, 0.2f, 1f);
    private const float LabelWidth = 110f;

    private static void Header(string text)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(text);
        ImGui.Separator();
    }

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

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<int, string?> behaviorName,
        Func<int, BehaviorDefinition?>? definition = null)
        => new(ViewId, "Brain", Rank,
               DetailsViewPredicates.OneEntityWithBrain(e => world() is { } w && BrainSnapshot.Has<BehaviorState>(w, e)),
               () => new EntityBrainView(world, behaviorName, definition));
}
