using System;
using NodeEditor.Core.Widgets;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using System.Runtime.CompilerServices;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Presentation.Behavior;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using ImGuiNET;
using StructEdit.Core;

namespace Hrot.Editor.Scenario;

// ⭐ CE-4292 / R1 (docs/UX/DESIGN_Rules_Bundle.md §4) — MOVED here from Hrot.Editor/Scenario/EntityAiDetailsView.cs so every host's rules bundle can
//   build the edit-mode apply (it was the one editor-only piece). ⚠ The namespace is KEPT (Hrot.Editor.Scenario): a move, not a rename — no caller changes.


/// <summary>
/// ⭐⭐ <c>CE-3043</c> — the testable half of the editor's AI section: READ a unit's live AI (task, SOP, ROE) and APPLY an
/// author's change through the one gate — the event when the sim runs, the ingress's own pipeline at once when it does not
/// (nothing ticks while paused). ⛔ It never writes a component itself. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6.
/// <para>⭐ Every edit is at origin <see cref="BehaviorOrigin.Superior"/> — R-193: an editor-authored assignment ranks as a
/// superior; an operator still outranks it.</para>
/// </summary>
public sealed class EntityAiEditModel
{
    /// <summary>The rank every editor edit carries.</summary>
    public const BehaviorOrigin EditorOrigin = BehaviorOrigin.Superior;

    private readonly Func<EntityRepository?> _world;
    private readonly Func<BehaviorRegistry?> _registry;
    private readonly Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?> _blueprints;
    private readonly Hrot.Presentation.Edits.EditHistory? _history;

    /// <param name="blueprints">⭐ <c>CE-2086</c> — the instance blueprints' registry, for the params rows. ⚠ Optional for
    /// headless fixtures (no rows); ⛔ a host that has one must pass it (the silent-default rule).</param>
    /// <param name="history">⭐ <c>CE-4219</c> ① — the editor's ONE shared undo history: an EDIT-mode apply (not <c>running</c>) pushes ONE entry whose undo re-applies the PREVIOUS value (the precedent of <c>RuleDetails</c>); a live
    /// edit lasts the run only and is not pushed. ⚠ Null ⇒ no undo — only where the host has no history, or pushes its own entry (the rule editor's <c>ApplyTask</c> call: <c>RuleDetails</c> pushes the rule's entry itself — two entries for one gesture
    /// would be wrong). ⛔ A host that HAS the history must pass it (the silent-default rule): the editor does.</param>
    public EntityAiEditModel(Func<EntityRepository?> world, Func<BehaviorRegistry?> registry,
                             Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? blueprints = null,
                             Hrot.Presentation.Edits.EditHistory? history = null)
    {
        _history    = history;
        _world      = world    ?? throw new ArgumentNullException(nameof(world));
        _registry   = registry ?? throw new ArgumentNullException(nameof(registry));
        _blueprints = blueprints ?? (() => null);
    }

    /// <summary>⭐ <c>CE-2086</c> — one attached instance blueprint: its params type (null = none) and params as JSON (only the
    /// fields that differ from the declared defaults — the save's own rule; <c>"{}"</c> = all defaults).</summary>
    public sealed record InstanceRow(int BlueprintId, string Name, Type? ParamsType, string ParamsJson);

    /// <summary>⭐ <c>CE-2086</c> — the unit's attached instances, walked as the scenario save walks them (§7.6a).</summary>
    public unsafe IReadOnlyList<InstanceRow> InstanceRows(Entity entity)
    {
        var rows = new List<InstanceRow>();
        if (_world() is not { } world || _blueprints() is not { } reg || !world.IsAlive(entity)) return rows;
        // ⭐ CE-3137 U-0: every block the unit carries.
        Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.GetBlocksReadOnly(world, entity, out var blocks);
        for (int b = 0; b < blocks.Count; b++)
        {
            byte* memory = blocks.Memory(b);
            if (Unsafe.AsRef<Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardHeader>(memory).MagicAndVersion != 0x42504257u)
                continue;
            int count = Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.GetSlotCount(memory);
            for (int i = 0; i < count; i++)
            {
                ref var slot = ref Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.GetSlot(memory, i);
                if (slot.BlueprintId == 0 || !reg.TryGetById(slot.BlueprintId, out var def) || def == null) continue;
                string? json = def.FormatParams != null && def.ParamsSize > 0
                    ? def.FormatParams(memory + slot.PayloadOffset + def.ParamsOffset, def.ParamsSize) : null;
                rows.Add(new InstanceRow(slot.BlueprintId, def.Name, def.ParamsClrType, json ?? "{}"));
            }
        }
        return rows;
    }

    /// <summary>
    /// ⭐ <c>CE-2086</c> (§7.6a A1) — commit an instance's params: paused ⇒ in place through the load's own
    /// <c>BlueprintInstanceService.ApplyParams</c> (the instance keeps its state); running ⇒ a replace of the instance by
    /// itself with the new params (a restart — its state was built from the old ones). <c>false</c> = not attached, or bad JSON.
    /// </summary>
    public bool ApplyInstanceParams(Entity entity, int blueprintId, string json, bool running)
    {
        // ⭐ CE-4219 ① — an edit-mode apply is ONE undo entry (undo re-applies the previous params, in place); captured BEFORE the write
        string? before = running || _history is null ? null : InstanceRows(entity).FirstOrDefault(r => r.BlueprintId == blueprintId)?.ParamsJson;
        bool ok = ApplyInstanceParamsRaw(entity, blueprintId, json, running);
        if (ok && !running && _history is not null && before is not null)
            _history.Push("Set blueprint params", () => ApplyInstanceParamsRaw(entity, blueprintId, json, false), () => ApplyInstanceParamsRaw(entity, blueprintId, before, false));
        return ok;
    }

    private unsafe bool ApplyInstanceParamsRaw(Entity entity, int blueprintId, string json, bool running)
    {
        if (_world() is not { } world || _blueprints() is not { } reg || !reg.TryGetById(blueprintId, out var def) || def == null)
            return false;
        if (running)
            return PublishManaged(new Fdp.Toolkit.Blueprints.Events.ReplaceInstanceBlueprintEvent
            {
                Entity = entity, OldBlueprintId = blueprintId, NewBlueprintId = blueprintId, ParamsJson = Json(json),
            });
        if (!Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.TryFindSlot(world, entity, blueprintId, out byte* memory, out int off, out _))
            return false;   // ⭐ CE-3137 U-0: any block
        return Fdp.Toolkit.Blueprints.BlueprintInstanceService.ApplyParams(memory + off, def, Json(json), world, entity) == null;
    }

    /// <summary>What a unit runs now — names and JSON from the start records, never a hash.</summary>
    public sealed record Snapshot(
        string? Task, string TaskParams, BehaviorOrigin TaskOrigin, string? PausedTask,
        string? Sop, string SopParams, BehaviorOrigin SopOrigin, bool SopFaulted,
        RoeFire Fire, RoeReactions Reactions, BehaviorOrigin RoeSetBy);

    public Snapshot? Read(Entity entity)
    {
        if (_world() is not { } world || !world.IsAlive(entity) || !world.HasComponent<BehaviorState>(entity)) return null;
        var view = (ISimulationView)world;
        var state = world.GetComponentRO<BehaviorState>(entity);

        string? task = null, taskJson = "{}";
        if (state.ActiveBehaviorHash != BehaviorIds.None)
        {
            if (world.HasManagedComponent<BehaviorStartRecord>(entity) && view.GetManagedComponentRO<BehaviorStartRecord>(entity) is { } r
                && r.InstanceId == state.InstanceId) { task = r.BehaviorName; taskJson = r.JsonParams; }
            else if (_registry() is { } reg && reg.TryGetName(state.ActiveBehaviorHash, out var n)) task = n;
        }

        string? sop = null, sopJson = "{}";
        BehaviorOrigin sopOrigin = BehaviorOrigin.Unmarked;
        bool faulted = false;
        if (world.IsComponentTypeRegistered<SopState>() && world.HasComponent<SopState>(entity))
        {
            var s = world.GetComponentRO<SopState>(entity);
            if (s.SopHash != BehaviorIds.None)
            {
                sopOrigin = s.SopOrigin;
                faulted   = s.SopFaulted != 0;
                if (world.HasManagedComponent<SopStartRecord>(entity) && view.GetManagedComponentRO<SopStartRecord>(entity) is { } sr)
                { sop = sr.BehaviorName; sopJson = sr.JsonParams; }
            }
        }

        var roe = world.IsComponentTypeRegistered<Roe>() && world.HasComponent<Roe>(entity) ? world.GetComponentRO<Roe>(entity) : default;
        var taskOrigin = state.Origin;
        var fire = RoeOf.Fire(world, entity); var reactions = RoeOf.Reactions(world, entity); var roeSetBy = roe.SetBy;
        // ⭐ CE-3494 (R3) — what the section shows is the PENDING intent where there is one (applied when time next advances), else what runs
        if (PendingIntent(entity) is { } pending)
        {
            if (pending.Behavior is { } pt) { task = pt.Clear ? null : pt.Name; taskJson = pt.Params; taskOrigin = pt.Origin; }   // a pending release: no task next
            if (pending.Sop is { } ps) { sop = ps.Clear ? null : ps.Name; sopJson = ps.Params; sopOrigin = ps.Origin; }
            if (pending.Roe is { } pr)
            {
                if (pr.Fire != RoeFire.FireUnset) fire = pr.Fire;
                if (pr.Reactions != RoeReactions.ReactionsUnset) reactions = pr.Reactions;
                roeSetBy = pr.SetBy;
            }
        }
        return new Snapshot(task, Json(taskJson), taskOrigin, BehaviorIngressSystem.PausedTaskOf(world, entity)?.BehaviorName,
                            sop, Json(sopJson), sopOrigin, faulted,
                            fire, reactions, roeSetBy);
    }

    /// <summary>Every registered behaviour that is not an SOP, by name (⭐ <c>CE-1019</c>: an SOP is not a task).</summary>
    public IReadOnlyList<string> TaskChoices()
        => _registry() is { } r
            ? r.GetRegisteredNames().Where(n => !IsSop(r, n)).OrderBy(n => n, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>⭐ The SOP row's choices: only SOPs (⭐ <c>CE-1019</c>: behaviours that carry SOP orders, the ruled mark) that are
    /// also KNOWN to drive no channel (§4.5 ②) — an unknown set is not offered.</summary>
    public IReadOnlyList<string> SopChoices()
    {
        if (_registry() is not { } r) return Array.Empty<string>();
        return r.GetRegisteredNames()
            .Where(n => r.TryGetId(n, out int id) && r.TryGetDefinition(id, out var d)
                        && d.CarriesSopOrders && d.WritesChannels is { Count: 0 })
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static bool IsSop(BehaviorRegistry r, string name)
        => r.TryGetId(name, out int id) && r.TryGetDefinition(id, out var d) && d is { CarriesSopOrders: true };

    /// <summary>⭐ <c>CE-3164</c> (R-268) — <paramref name="names"/> as picker choices: each with its technology and folder.</summary>
    public IReadOnlyList<Hrot.UI.Common.Facades.BehaviorChoice> AsChoices(IReadOnlyList<string> names)
    {
        if (_registry() is not { } r) return Array.Empty<Hrot.UI.Common.Facades.BehaviorChoice>();
        return names.Select(n => new Hrot.UI.Common.Facades.BehaviorChoice(n, Hrot.UI.Common.Adapters.ScenarioMissionService.TechnologyOf(r, n))
        {
            Group = r.GroupOf(n) ?? Hrot.UI.Common.Facades.BehaviorChoice.OtherGroup,
        }).ToList();
    }

    /// <summary>The behaviour's authored params type — what the params form edits; <c>null</c> = it takes none.</summary>
    /// <summary>⭐ CE-1023 — the one lookup (<see cref="BehaviorParamsForm.ParamsTypeOf"/>), the mission panel's too.</summary>
    public Type? ParamsTypeOf(string behaviour) => BehaviorParamsForm.ParamsTypeOf(behaviour, _registry());

    // ⭐⭐ CE-3494 (R-3114, R-3118; docs/DESIGN_Unit_Intent.md §3.2) — `running` means a LIVE run (running OR paused mid-run):
    //   ⭐⭐ R-3119 (§10) — EVERY edit is the unit's UnitIntent: origin Operator LIVE (a person's direct order — it holds a plan), Superior in EDIT
    //   MODE (R-193 authoring). Applied when time next advances; shown as Next, undoable, kept by a checkpoint. A live clear is the slot's Clear
    //   (a release); an edit-mode clear un-authors the slot. ⛔ SUPERSEDED (S1, undocumented until §10): live edits sent as order events — they were
    //   invisible until the step and a checkpoint on the paused frame lost them. ⛔ SUPERSEDED earlier: the paused direct apply (CE-3495).

    /// <summary>⭐ CE-3494 (R-3118) — the rank of a LIVE edit: a human's direct order.</summary>
    public const BehaviorOrigin LiveOrigin = BehaviorOrigin.Operator;

    public bool ApplyTask(Entity entity, string behaviour, string json, bool running)
        => Author(entity, "Set task", i => i with { Behavior = new IntentSlot { Name = behaviour, Params = Json(json), Origin = OriginOf(running) } });

    /// <summary>Live: a RELEASE (the slot's Clear — the running task ends when time advances). Edit mode: the starting task is un-authored.</summary>
    public bool ClearTask(Entity entity, bool running)
        => Author(entity, "Clear task", i => i with { Behavior = running ? new IntentSlot { Clear = true, Origin = LiveOrigin } : null });

    public bool ApplySop(Entity entity, string behaviour, string json, bool running)
    {
        if (!SopAllowed(behaviour)) return false;   // ⭐ CE-2084 — the SOP slot refuses a behaviour known to drive a channel, in both modes
        return Author(entity, "Set SOP", i => i with { Sop = new IntentSlot { Name = behaviour, Params = Json(json), Origin = OriginOf(running) } });
    }

    public bool ClearSop(Entity entity, bool running)
        => Author(entity, "Clear SOP", i => i with { Sop = running ? new IntentSlot { Clear = true, Origin = LiveOrigin } : null });

    public bool ApplyRoe(Entity entity, RoeFire fire, RoeReactions reactions, bool running)
        => Author(entity, "Set ROE", i => i with { Roe = (i.Roe ?? new SavedRoe()) with { Fire = fire, Reactions = reactions, SetBy = OriginOf(running) } });

    private static BehaviorOrigin OriginOf(bool running) => running ? LiveOrigin : EditorOrigin;

    /// <summary>⭐ CE-3494 (R-3117) — a live mission plan drives the unit's task slot (the intent's task would never start).</summary>
    public bool DrivenByPlan(Entity entity)
        => _world() is { } w && w.IsAlive(entity) && BrainIntentReader.DrivenByMissionPlan(w, entity);

    /// <summary>⭐ CE-3494 — the unit's pending authored intent (null = none).</summary>
    public UnitIntent? PendingIntent(Entity entity)
        => _world() is { } w && w.IsAlive(entity) && w.TryGetTable(typeof(UnitIntent), out _) && w.HasManagedComponent<UnitIntent>(entity)
            ? ((ISimulationView)w).GetManagedComponentRO<UnitIntent>(entity) : null;

    /// <summary>⭐ CE-3494 — writes the edited intent (a new record, replaced whole) and pushes ONE undo entry that restores the record as it
    /// was (⛔ not the running AI: in edit mode nothing runs). An intent with nothing left in it is removed.</summary>
    private bool Author(Entity entity, string label, Func<UnitIntent, UnitIntent> edit)
    {
        if (_world() is not { } w || !w.IsAlive(entity) || !w.HasComponent<BehaviorState>(entity) || !w.TryGetTable(typeof(UnitIntent), out _)) return false;
        var before = PendingIntent(entity);
        var after = edit(before ?? new UnitIntent());
        SetIntent(w, entity, after);
        _history?.Push(label, () => SetIntent(w, entity, after), () => SetIntent(w, entity, before));
        return true;
    }

    private static void SetIntent(EntityRepository w, Entity entity, UnitIntent? intent) => UnitIntents.Replace(w, entity, intent);   // the one store (R-3119)

    /// <summary>⭐ CE-2084 — a behaviour KNOWN to drive a channel is never an SOP (the ingress refuses it at apply; edit mode refuses it here).</summary>
    private bool SopAllowed(string behaviour)
        => _registry() is not { } r || !r.TryGetId(behaviour, out int id) || !r.TryGetDefinition(id, out var d)
           || d!.WritesChannels is not { Count: > 0 };

    private static string Json(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json!;

    private bool Publish<T>(T evt) where T : unmanaged
    {
        if (_world() is not { } w || !w.Bus.IsRegistered<T>()) return false;
        w.Bus.Publish(evt);
        return true;
    }

    private bool PublishManaged<T>(T evt) where T : class
    {
        if (_world() is not { } w || !w.Bus.IsRegisteredManaged<T>()) return false;
        w.Bus.PublishManaged(evt);
        return true;
    }
}

