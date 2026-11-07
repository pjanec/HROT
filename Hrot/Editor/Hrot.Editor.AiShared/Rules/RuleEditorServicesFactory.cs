using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Rules;

namespace Hrot.Editor.AiShared.Rules;

/// <summary>
/// ⭐ CE-4292 / R2 (docs/UX/DESIGN_Rules_Bundle.md §2, §3) — what a host hands the rules windows: its world, its timeline, its selection, its seat and
/// the capabilities it HAS (entity creation, the macro expander, the time transport, the pickers, the map picks, the undo history). ⭐ A capability the
/// host has it passes (the silent-default rule); one it lacks stays null and the action is greyed with its reason (R-4119).
/// </summary>
public sealed record RulesHostServices(
    Func<EntityRepository?> World,
    Func<Fdp.Toolkit.ReplayBrowser.Timeline.ITimelineSource?> TimelineSource,
    Func<Fdp.Presentation.Panels.Timeline.ITimelineCursor?> Cursor,   // a NEW cursor per window (each window owns its cursor, as the hand-written sites did)
    string IdPrefix,
    string Perspective,
    System.Numerics.Vector4 Colour,
    Action<int> SelectEntity,
    Action<int> ShowOnMap,
    Func<Seat> Seat,
    Func<string?>? Blocked = null,
    Func<BehaviorRegistry?>? Registry = null,
    Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? Blueprints = null,
    Func<Hrot.Common.EntityCreation.EntityCreation?>? EntityCreation = null,
    Func<Hrot.UI.Common.Adapters.ScenarioSpawnAdapter?>? SpawnAdapter = null,
    Func<Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter?>? TimeTransport = null,
    Func<NodeEditor.UI.Picker.PickerRegistry?>? Pickers = null,
    Func<Hrot.UI.Common.Facades.IMapPickService?>? MapPicks = null,
    Hrot.Presentation.Edits.EditHistory? History = null,
    bool Recording = false,                                        // a recording host: read-only by construction, no Game master
    Func<(int NetworkId, ForceId Side)?>? Selection = null,        // the Game master's "for the selected unit"
    Func<bool>? GamePaletteEnabled = null,                         // the Game master's feature gate (HudFeatures.GamePalette)
    Func<Hrot.Common.Interactions.HostActions?>? Actions = null,   // ⭐ CE-4308 (RX6) — the host's action registry: the map's "Add rule about this unit…" / "When someone enters this area…" land on the bundle
    bool TraineeOnly = false);                                     // ⭐ L-2 (LC-18) — the Player: only the Trainee debrief, opened when the exercise ends

/// <summary>
/// ⭐ CE-4292 / R2 — THE rule editor services, built ONCE for every host (was the editor's private <c>BuildRuleEditorServices</c> /
/// <c>BuildMacroPreviewServices</c> / <c>BuildRuleLiveServices</c>). ⛔ No "if editor": who may edit is the SEAT (<see cref="MayEdit"/>), read every frame
/// through <see cref="RuleEditorServices.SeatRefusal"/> (the seat is a live feature — a host composes its windows before its node exists).
/// ⭐ Null only for a host that can never edit (no world reader, or a recording: <see cref="RulesHostServices.Blocked"/> set at compose).
/// </summary>
public static class RuleEditorServicesFactory
{
    /// <summary>The seats that may edit rules (R-4131: the Author everywhere, the Instructor live) — the edit gate's Full and Instructor levels.</summary>
    public static bool MayEdit(Seat seat)
        => Hrot.Common.Editing.HrotEditGate.LevelOf(seat) is Hrot.Common.Editing.EditingLevel.Full or Hrot.Common.Editing.EditingLevel.Instructor;

    /// <summary>Why <paramref name="seat"/> may not edit rules, or null.</summary>
    public static string? SeatRefusal(Seat seat) => MayEdit(seat) ? null : $"your seat ({seat}) cannot edit rules";

    public static RuleEditorServices? Build(RulesHostServices host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Recording || host.TraineeOnly) return null;   // a recording is read-only by construction (R-4148); a trainee edits no rules
        var world = host.World;
        Func<BehaviorRegistry?> registry = host.Registry ?? (() => null);
        var editService = Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build();
        var aiModel = new Hrot.Editor.Scenario.EntityAiEditModel(world, registry, host.Blueprints);   // the edit-mode apply (R1 moved it here)
        var picks = host.MapPicks is { } mp ? new Hrot.Presentation.Facades.MapPickBroker(mp) : null;
        string? macroBar = null;

        string? LiveRefusal() => world() is { } w && w.IsComponentTypeRegistered<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>() && w.Query().With<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>().Build().Any()
            ? null : "inserting changes a running exercise: start it first";
        var macro = new MacroPreviewServices(
            Preview: (rule, step) =>
            {
                if (world() is not { } w || host.SpawnAdapter?.Invoke() is not { } spawn || spawn.ExpanderOrNull is not { } ex) { macroBar = "this host cannot expand macros"; return macroBar; }
                var pv = Hrot.Common.Macros.MacroPreview.OfStep(ex, w, step.Args);
                spawn.ShowPreview(pv.Result, pv.Anchor);
                macroBar = pv.Result.Ok ? $"{pv.Result.Ghosts.Count} to create at {pv.Anchor.X:0},{pv.Anchor.Y:0}" : pv.Result.Failure;
                return macroBar;
            },
            Bar: () => macroBar,
            InsertNow: (rule, step) =>
            {
                if (LiveRefusal() is { } r) return r;
                if (world() is not { } w || host.SpawnAdapter?.Invoke() is not { } spawn || spawn.ExpanderOrNull is not { } ex) return "this host cannot expand macros";
                var pv = Hrot.Common.Macros.MacroPreview.OfStep(ex, w, step.Args);
                if (!pv.Result.Ok) return pv.Result.Failure;
                string? refused = Hrot.AI.Behaviors.Brains.Objectives.Verbs.MacroInsertion.Request(w, rule, pv.Macro, new WorldPoint(pv.Anchor), new Bearing(pv.HeadingDeg), pv.ArgsJson, out _, salt: "preview");
                spawn.ClosePreview(); macroBar = null;
                return refused;
            },
            Close: () => { host.SpawnAdapter?.Invoke()?.ClosePreview(); macroBar = null; },
            InsertNowRefusal: LiveRefusal);

        HashSet<string>? keepers = null;
        string? KeeperRefusal(string kind)
        {
            if (registry() is { } r && r.TryGetKeeper(kind, out _)) return null;
            keepers ??= Hrot.Presentation.Behavior.BehaviorSchemaDiscovery.KeeperNames(typeof(Hrot.AI.Behaviors.Brains.Objectives.ObjectiveStateKeepers).Assembly).ToHashSet();
            return keepers.Contains(kind) ? null : "not editable live, restart the rule";
        }
        var live = new RuleLiveServices(KeeperRefusal,
            on => host.TimeTransport?.Invoke()?.SetFeatureOverride(Hrot.AI.Behaviors.Brains.Objectives.RuleLeafSampler.FeatureId, on));

        return new RuleEditorServices(
            () => editService, registry,
            (entity, behaviour, json) => aiModel.ApplyTask(entity, behaviour, json, running: false),
            host.History ?? new Hrot.Presentation.Edits.EditHistory(),   // ⭐ undo only where Edit mode exists (R-4133 Q2): the editor passes its ONE history; elsewhere a window-local one
            entity =>
            {
                if (world() is not { } w || !w.IsAlive(entity)) return Array.Empty<RuleJournalEntry>();
                string? Name(int hash) => registry() is { } r && r.TryGetName(hash, out var n) ? n : null;
                var snap = Hrot.Editor.AiShared.Shell.BrainSnapshot.Read(w, entity, Name, hash => registry() is { } r && r.TryGetDefinition(hash, out var d) ? d : null);
                return snap?.Changes.Select(c => new RuleJournalEntry(c.Seq, c.At, c.Kind, c.Text)).ToList() ?? (IReadOnlyList<RuleJournalEntry>)Array.Empty<RuleJournalEntry>();
            },
            picks != null ? () => new Fdp.Presentation.Editing.ScopedMapPickContext(picks, "$.rule") : null,
            OpenPicker: host.Pickers is { } pk ? () => pk() is { } pickers ? pickers.OpenPicker : null : null,
            Live: live,
            MacroPreview: macro,
            CreateRule: (tkb, kind, json) =>
            {
                if (host.EntityCreation?.Invoke() is not { } creation) return false;
                creation.RequestEntityCreation(tkb, initialComponents: new object[] { new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 } });
                return true;
            },
            SeatRefusal: () => SeatRefusal(host.Seat()));
    }
}
