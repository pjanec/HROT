using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <c>CE-1286</c> (DECIDED A, user 2026-11-18; docs/DESIGN_Entity_Brain_View.md §12t) — a Watch row for one field of a RUNNING behaviour,
/// C# behaviours included: the row section <see cref="Section"/>, addressed by (owner slot, behaviour, part, params / variables) + the field
/// path. ⭐ READ through <see cref="OccurrenceSlotCatalog"/> (the one slot reader, R-1037) and WRITTEN through <see cref="OccurrenceSlotWriter"/>
/// (R-1041's one live write) — never a second addressing. The Brain tab's dump offers it (<i>Add to Watch</i> on a row); the production live
/// writer (<c>BlueprintLiveValueWriter</c>) and the yellow staged view answer a row of this section through <see cref="TryAddress"/>.
/// ⚠ The row's <c>AssetId</c> is derived from its address, so the same field added twice is the same row; the address table is in memory —
/// ⛔ a pin of this section is not restored by a later session (it is not persistable: the binding is a plain concrete one).
/// </summary>
public static class OccurrenceWatchRows
{
    /// <summary>The Watch row section of a running behaviour's field.</summary>
    public const string Section = "Occurrence";

    /// <summary>Which slot of the unit and which half: the task / SOP / paused task / attached blueprint, a part (a node's working state or a
    /// hosted child, by its slot label; null = the behaviour's own block) and params or variables.</summary>
    public sealed record Address(SlotOwnerKind Owner, string Behaviour, string? Part, OccurrenceSlotCatalog.SlotFieldRole Role);

    private static readonly ConcurrentDictionary<Guid, Address> s_addresses = new();

    /// <summary>The row identity's asset id for <paramref name="address"/> — deterministic (the same address ⇒ the same id).</summary>
    public static Guid AssetIdOf(Address address)
    {
        var text = $"{Section}|{address.Owner}|{address.Behaviour}|{address.Part}|{address.Role}";
        var id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(text)));
        s_addresses.TryAdd(id, address);
        return id;
    }

    /// <summary>The address of a row of <see cref="Section"/> (false for any other row, or one this process never built).</summary>
    public static bool TryAddress(in VariableRowOrigin origin, out Address address)
    {
        address = null!;
        return string.Equals(origin.Section, Section, StringComparison.Ordinal) && s_addresses.TryGetValue(origin.AssetId, out address!);
    }

    /// <summary>
    /// A concrete Watch row for <paramref name="path"/> (dotted, as the dump names it, without the <c>$.</c>) of <paramref name="unit"/>'s
    /// slot at <paramref name="address"/>; null with <paramref name="error"/> when the field does not resolve now. The value is read live,
    /// its byte address cached and re-resolved only when the slot moves (a frame's read allocates nothing once warm).
    /// </summary>
    public static unsafe VariableRow? Build(Func<EntityRepository?> world, Entity unit, Func<BehaviorRegistry?> registry, Func<BlueprintRegistry?> blueprints,
        Address address, string path, out string? error)
    {
        error = null;
        var w = world(); var r = registry();
        if (w is null || r is null) { error = "No world or behaviour registry on this host."; return null; }
        var first = OccurrenceSlotCatalog.ResolveField(w, unit, r, blueprints(), address.Owner, address.Behaviour, address.Part, address.Role, path, out error);
        if (first is null) return null;
        var fieldType = first.FieldType;
        var cached = first;
        uint resolvedAt = BehaviorFrame.Current;
        ReadOnlySpan<byte> Read()
        {
            var wr = world();
            if (wr is null || !wr.IsAlive(unit)) return ReadOnlySpan<byte>.Empty;
            // ⭐ a slot can move (a behaviour restarted, a blueprint attached): re-resolve every ReResolveFrames behaviour frames — the
            //   resolve walks the slot list (it allocates), so it is never done per drawn frame (R-2084)
            bool stale = unchecked(BehaviorFrame.Current - resolvedAt) >= ReResolveFrames;
            if (stale || !TrySpan(wr, unit, cached, out var span))
            {
                resolvedAt = BehaviorFrame.Current;
                var rr = registry();
                var again = rr is null ? null : OccurrenceSlotCatalog.ResolveField(wr, unit, rr, blueprints(), address.Owner, address.Behaviour, address.Part, address.Role, path, out _);
                if (again is null || !TrySpan(wr, unit, again, out span)) return ReadOnlySpan<byte>.Empty;   // the behaviour ended: the row reads empty
                cached = again;
            }
            return span;
        }
        string label = address.Part is null ? address.Behaviour : $"{address.Behaviour} · {address.Part}";
        return new VariableRow(
            Origin:    new VariableRowOrigin(AssetIdOf(address), unit, Section, path, label),
            ShortName: path,
            TypeText:  fieldType.Name,
            ClrType:   fieldType,
            ReadValue: Read);
    }

    /// <summary>How many behaviour frames a cached field address is trusted before it is resolved again.</summary>
    public const uint ReResolveFrames = 30;

    /// <summary>The field's live bytes at <paramref name="a"/>, when the component is there and the slot still holds it at that offset.</summary>
    private static unsafe bool TrySpan(EntityRepository world, Entity unit, OccurrenceSlotCatalog.SlotFieldAddress a, out ReadOnlySpan<byte> span)
    {
        span = default;
        int typeId = ComponentTypeRegistry.GetId(a.ComponentType);
        if (typeId < 0 || !world.HasComponentByTypeId(unit, typeId)) return false;
        byte* p = (byte*)world.GetComponentPointer(unit, typeId);
        if (p == null) return false;
        span = new ReadOnlySpan<byte>(p + a.ComponentOffset, a.Size);
        return true;
    }

    /// <summary>⭐ The WRITE of a row of <see cref="Section"/>: R-1041's one live write (<see cref="OccurrenceSlotWriter.WriteFieldBytes"/>),
    /// staged through <paramref name="stage"/>. Null when <paramref name="origin"/> is not such a row.</summary>
    public static SlotWriteResult? Write(EntityRepository world, Entity unit, BehaviorRegistry registry, BlueprintRegistry? blueprints, FieldStager stage,
        in VariableRowOrigin origin, ReadOnlySpan<byte> bytes)
        => TryAddress(origin, out var a)
            ? OccurrenceSlotWriter.WriteFieldBytes(world, unit, registry, blueprints, stage, a.Owner, a.Behaviour, a.Part, a.Role, origin.VariablePath, bytes)
            : null;

    /// <summary>⭐ Where a row of <see cref="Section"/> lives (the yellow staged view's lookup). Null when it is not such a row or does not resolve.</summary>
    public static OccurrenceSlotCatalog.SlotFieldAddress? Resolve(EntityRepository world, Entity unit, BehaviorRegistry registry, BlueprintRegistry? blueprints,
        in VariableRowOrigin origin)
        => TryAddress(origin, out var a)
            ? OccurrenceSlotCatalog.ResolveField(world, unit, registry, blueprints, a.Owner, a.Behaviour, a.Part, a.Role, origin.VariablePath, out _)
            : null;
}

/// <summary>⭐ <c>CE-1286</c> — what the host's live writer needs for a row of <see cref="OccurrenceWatchRows.Section"/>: the world, the
/// registries the slot reader names behaviours with, and the staged write (<c>IDataBreakpointManager.StageFieldMutation</c> in production —
/// the same stager <c>POST /entities/{id}/variable</c> uses).</summary>
public sealed record OccurrenceWriteServices(Func<EntityRepository?> World, Func<BehaviorRegistry?> Registry, Func<BlueprintRegistry?> Blueprints, FieldStager? Stage)
{
    /// <summary>R-1041's one live write for <paramref name="origin"/> on <paramref name="unit"/>; null when it is not an occurrence row.</summary>
    public SlotWriteResult? Write(Entity unit, in VariableRowOrigin origin, ReadOnlySpan<byte> bytes)
    {
        if (!OccurrenceWatchRows.TryAddress(origin, out _)) return null;
        var w = World(); var r = Registry();
        if (w is null || r is null) return new SlotWriteResult(null, "No world or behaviour registry on this host.", false);
        if (Stage is null) return new SlotWriteResult(null, "This host has no staged-write target.", false);
        return OccurrenceWatchRows.Write(w, unit, r, Blueprints(), Stage, origin, bytes);
    }

    /// <summary>Where an occurrence row lives now (for the yellow staged view), or null.</summary>
    public OccurrenceSlotCatalog.SlotFieldAddress? Resolve(Entity unit, in VariableRowOrigin origin)
    {
        var w = World(); var r = Registry();
        return w is null || r is null ? null : OccurrenceWatchRows.Resolve(w, unit, r, Blueprints(), origin);
    }
}
