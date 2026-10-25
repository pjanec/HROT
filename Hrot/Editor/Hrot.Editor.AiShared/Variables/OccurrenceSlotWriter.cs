using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>Stages <paramref name="bytes"/> at a component-absolute offset — <c>IDataBreakpointManager.StageFieldMutation</c> in
/// production (the drain lands it on the next advancing tick).</summary>
public delegate void FieldStager(Entity entity, Type componentType, int componentOffset, ReadOnlySpan<byte> bytes);

/// <summary>What one live write did.</summary>
/// <param name="Address">Where it was staged.</param>
/// <param name="StartRecordUpdated">A task-params write also rewrote the start record's JSON (the scenario save and the
/// "same order again?" checks read it).</param>
public sealed record SlotWriteResult(OccurrenceSlotCatalog.SlotFieldAddress? Address, string? Error, bool StartRecordUpdated)
{
    public bool Staged => Error is null;
}

/// <summary>
/// ⭐⭐ <c>CE-1320</c> (Brain View §12g BV-65, R-1041) — THE ONE LIVE WRITE for a running behaviour's parameters and working state, any
/// slot (task, SOP, paused task, hosted child, attached blueprint), any tier. Addresses through the reader
/// (<see cref="OccurrenceSlotCatalog.ResolveField"/>) — a slot the Brain tab and <c>GET /entities/{id}/slots</c> list is a slot this
/// writes — and stages through the staged write, so it lands on the next advancing tick, recorded like every component write.
/// ⛔ It NEVER restarts the behaviour (a params struct is read in place each access); restarting is the caller's separate act.
/// Its callers: <c>POST /entities/{id}/variable</c> with <c>owner</c>, and the Brain tab's <i>Edit params…</i>.
/// </summary>
public static class OccurrenceSlotWriter
{
    /// <summary>One field by <paramref name="path"/> (dotted; <c>[n]</c> into an inline array), the value as JSON of the field's
    /// own type (<see cref="FdpJsonOptionsRegistry.DefaultRelaxed"/>).</summary>
    public static unsafe SlotWriteResult WriteField(
        EntityRepository world, Entity unit, BehaviorRegistry registry, BlueprintRegistry? blueprints, FieldStager stage,
        SlotOwnerKind owner, string? behaviour, string? part, OccurrenceSlotCatalog.SlotFieldRole role, string path, JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var address = OccurrenceSlotCatalog.ResolveField(world, unit, registry, blueprints, owner, behaviour, part, role, path, out var error);
        if (address is null) return new SlotWriteResult(null, error, false);

        object? converted;
        try { converted = JsonSerializer.Deserialize(value.ToJsonString(), address.FieldType, FdpJsonOptionsRegistry.DefaultRelaxed); }
        catch (Exception ex) { return new SlotWriteResult(address, $"Cannot read {value.ToJsonString()} as {address.FieldType.Name}: {ex.Message}", false); }
        if (converted is null) return new SlotWriteResult(address, "The value converted to null.", false);

        var bytes = new byte[address.Size];
        fixed (byte* p = bytes)
            StructEdit.Core.Memory.RuntimeTypeOpsFactory.Get(address.FieldType).CopyObjectToNative(converted, p);
        stage(unit, address.ComponentType, address.ComponentOffset, bytes);

        bool record = owner == SlotOwnerKind.Task && part is null && role == OccurrenceSlotCatalog.SlotFieldRole.FieldParams
                      && path.Length > 0 && TryPatchStartRecord(world, unit, path, value);
        return new SlotWriteResult(address, null, record);
    }

    /// <summary>
    /// A behaviour's (or attached blueprint's) whole parameter set from <paramref name="json"/> — THE behaviour's own parser
    /// (<c>ParseParams</c>, exactly as an assignment runs it) into a scratch region, staged over the live params; the task's start
    /// record takes the new JSON. ⛔ No restart.
    /// </summary>
    public static unsafe SlotWriteResult WriteParamsJson(
        EntityRepository world, Entity unit, BehaviorRegistry registry, BlueprintRegistry? blueprints, FieldStager stage,
        SlotOwnerKind owner, string? behaviour, string json)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var address = OccurrenceSlotCatalog.ResolveField(world, unit, registry, blueprints, owner, behaviour, null,
            OccurrenceSlotCatalog.SlotFieldRole.FieldParams, "", out var error);
        if (address is null) return new SlotWriteResult(null, error, false);

        ParseParamsDelegate? parse = null;
        if (owner == SlotOwnerKind.InstanceBlueprint)
        {
            if (blueprints is not null && blueprints.TryGetByName(address.Behaviour, out var bp)) parse = bp?.ParseParams;
        }
        else if (registry.TryGetId(address.Behaviour, out int id) && registry.TryGetDefinition(id, out var def)) parse = def.ParseParams;
        if (parse is null) return new SlotWriteResult(address, $"'{address.Behaviour}' has no parameter parser to apply JSON through.", false);

        var scratch = new byte[address.Size];
        try
        {
            fixed (byte* s = scratch) parse(json, s, scratch.Length, world, unit);
        }
        catch (Exception ex) { return new SlotWriteResult(address, $"The parameters did not parse — {ex.GetType().Name}: {ex.Message}", false); }
        stage(unit, address.ComponentType, address.ComponentOffset, scratch);

        bool record = false;
        if (owner == SlotOwnerKind.Task && world.TryGetTable(typeof(BehaviorStartRecord), out _)
            && world.HasManagedComponent<BehaviorStartRecord>(unit)
            && ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(unit) is { } r)
        {
            world.SetManagedComponent(unit, new BehaviorStartRecord { BehaviorName = r.BehaviorName, JsonParams = json, InstanceId = r.InstanceId });
            record = true;
        }
        return new SlotWriteResult(address, null, record);
    }

    /// <summary>The task's start record keeps the parameter TEXT it was started with — a field write patches that text by name
    /// (case-insensitive, added when the field was at its default and so absent).</summary>
    private static bool TryPatchStartRecord(EntityRepository world, Entity unit, string path, JsonNode value)
    {
        if (!world.TryGetTable(typeof(BehaviorStartRecord), out _) || !world.HasManagedComponent<BehaviorStartRecord>(unit)
            || ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(unit) is not { } r)
            return false;
        JsonObject root;
        try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(r.JsonParams) ? "{}" : r.JsonParams) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { root = new JsonObject(); }

        var segments = path.Split('.');
        JsonObject node = root;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Contains('[')) return false;   // an indexed element: not patchable by name — the record keeps its text
            string key = FindKey(node, segments[i]) ?? segments[i];
            if (i == segments.Length - 1) { node[key] = value.DeepClone(); break; }
            if (node[key] is not JsonObject child) { child = new JsonObject(); node[key] = child; }
            node = child;
        }
        world.SetManagedComponent(unit, new BehaviorStartRecord { BehaviorName = r.BehaviorName, JsonParams = root.ToJsonString(), InstanceId = r.InstanceId });
        return true;
    }

    private static string? FindKey(JsonObject node, string name)
    {
        foreach (var kv in node)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return null;
    }
}
