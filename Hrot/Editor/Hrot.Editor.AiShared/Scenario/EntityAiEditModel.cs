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
    private BehaviorRegistry? _ingressFor;
    private BehaviorIngressSystem? _ingress;

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
        return new Snapshot(task, Json(taskJson), state.Origin, BehaviorIngressSystem.PausedTaskOf(world, entity)?.BehaviorName,
                            sop, Json(sopJson), sopOrigin, faulted,
                            RoeOf.Fire(world, entity), RoeOf.Reactions(world, entity), roe.SetBy);
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

    public bool ApplyTask(Entity entity, string behaviour, string json, bool running)
    {
        // ⭐ CE-4219 ① — edit mode: ONE undo entry, undo re-applies the previous task (or clears it when there was none); captured BEFORE the write
        var before = running || _history is null ? null : Read(entity);
        bool ok = ApplyTaskRaw(entity, behaviour, json, running);
        if (ok && !running && _history is not null)
            _history.Push("Set task", () => ApplyTaskRaw(entity, behaviour, json, false),
                () => { if (before?.Task is { } t) ApplyTaskRaw(entity, t, before.TaskParams, false); else ClearTask(entity, false); });
        return ok;
    }

    private bool ApplyTaskRaw(Entity entity, string behaviour, string json, bool running)
        => running
            ? PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = behaviour, JsonParams = Json(json), Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.AssignNow(w, entity, behaviour, Json(json), EditorOrigin);

    public bool ClearTask(Entity entity, bool running)
        => running
            ? Publish(new ClearBehaviorEvent { Entity = entity, Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.ClearNow(w, entity, EditorOrigin);

    public bool ApplySop(Entity entity, string behaviour, string json, bool running)
    {
        // ⭐ CE-4219 ① — edit mode: ONE undo entry, undo re-applies the previous SOP (or clears it)
        var before = running || _history is null ? null : Read(entity);
        bool ok = ApplySopRaw(entity, behaviour, json, running);
        if (ok && !running && _history is not null)
            _history.Push("Set SOP", () => ApplySopRaw(entity, behaviour, json, false),
                () => { if (before?.Sop is { } s) ApplySopRaw(entity, s, before.SopParams, false); else ClearSop(entity, false); });
        return ok;
    }

    private bool ApplySopRaw(Entity entity, string behaviour, string json, bool running)
        => running
            ? PublishManaged(new AssignSopEvent { Entity = entity, BehaviorName = behaviour, JsonParams = Json(json), Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.AssignSopNow(w, entity, behaviour, Json(json), EditorOrigin);

    public bool ClearSop(Entity entity, bool running)
        => running
            ? Publish(new ClearSopEvent { Entity = entity, Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.ClearSopNow(w, entity, EditorOrigin);

    public bool ApplyRoe(Entity entity, RoeFire fire, RoeReactions reactions, bool running)
    {
        // ⭐ CE-4219 ① — edit mode: ONE undo entry, undo re-applies the previous ROE; captured BEFORE the write
        var before = running || _history is null ? null : Read(entity);
        bool ok = ApplyRoeRaw(entity, fire, reactions, running);
        if (ok && !running && _history is not null && before is not null)
            _history.Push("Set ROE", () => ApplyRoeRaw(entity, fire, reactions, false), () => ApplyRoeRaw(entity, before.Fire, before.Reactions, false));
        return ok;
    }

    private bool ApplyRoeRaw(Entity entity, RoeFire fire, RoeReactions reactions, bool running)
    {
        var evt = new SetRoeEvent { Entity = entity, Fire = fire, Reactions = reactions, Origin = EditorOrigin };
        return running ? Publish(evt) : _world() is { } w && RoeSystem.Apply(w, evt);
    }

    private static string Json(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json!;

    private BehaviorIngressSystem? Ingress()
    {
        var reg = _registry();
        if (reg == null) return null;
        if (!ReferenceEquals(reg, _ingressFor)) { _ingress = new BehaviorIngressSystem(reg); _ingressFor = reg; }
        return _ingress;
    }

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

