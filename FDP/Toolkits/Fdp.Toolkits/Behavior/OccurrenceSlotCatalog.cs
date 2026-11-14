using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Diagnostics;

namespace Fdp.Toolkit.Behavior;

/// <summary>Who owns a slot: the running task, the SOP, or the task a reaction paused. ⚠ Member names unique across the file's enums (the DDS IDL generator emits them into one scope).</summary>
public enum SlotOwnerKind : byte { OwnerUnknown = 0, Task = 1, Sop = 2, PausedTask = 3, InstanceBlueprint = 4 }

/// <summary>What a slot is to its owner.</summary>
public enum SlotRole : byte { RoleUnknown = 0, Params = 1, State = 2, Hsm = 3, Working = 4, HostedChild = 5 }

/// <summary>One occurrence slot of a unit, named when its owner is known, its value decoded when its layout is.
/// ⭐ <c>CE-1315</c> (Brain View §12b BV-44): <see cref="Boxed"/> is the slot's value as its OWN CLR struct — the exact managed image
/// (<c>RuntimeTypeOpsFactory.BoxFromNative</c>, never <c>Marshal.PtrToStructure</c>, which reads the marshalled layout and cannot read an
/// <c>[InlineArray]</c>) — for a display that draws C# values with their registered renderers; <see cref="Value"/> stays the JSON map.</summary>
public sealed record OccurrenceSlotView(
    int Key, OccurrenceKind Kind, int PayloadSize, uint StructureHash,
    SlotOwnerKind Owner, string? Behaviour, SlotRole Role, string? Label, string? LayoutType, object? Value, object? Boxed = null);

/// <summary>
/// ⭐⭐ <c>CE-2288</c> (CC-16, R-2050) — EVERY occurrence slot a unit carries, read-only: the running task's, the SOP's, a paused task's,
/// their hosted children's and working-state slots. A slot stores no owner (its key is a one-way hash, <c>HsmOccurrence</c>), so each
/// is NAMED by recomputing the candidate keys of the behaviours the unit runs (the same keys ingress attached), and DECODED through the
/// layout the root path's projection uses (<c>BrainDiagnosticsTranslator</c>: <c>PtrToStructure</c> + <see cref="DtoDiagnosticMapper"/>).
/// An unmatched slot is still listed (key, kind, size). 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §7c, §8a.
/// </summary>
public static class OccurrenceSlotCatalog
{
    private sealed record Candidate(SlotOwnerKind Owner, string Behaviour, SlotRole Role, Type? Layout, int Offset, string? Label);

    /// <param name="blueprints">⭐ <c>CE-1315</c> — when given, an attached Instance blueprint's slot (keyed by its blueprint id, kind
    /// <see cref="OccurrenceKind.Blueprint"/>) is named and decoded through its generated <c>State</c> (<c>[Cursor][Params][vars]</c>).</param>
    public static List<OccurrenceSlotView> Read(EntityRepository world, Entity unit, BehaviorRegistry registry,
        Fdp.Toolkit.Blueprints.BlueprintRegistry? blueprints = null)
        => ReadNamed(world, unit, registry, blueprints).Select(x => x.View).ToList();

    /// <summary>Each slot with the layout and in-payload offset it was named with — what <see cref="ResolveField"/> walks.</summary>
    private static unsafe List<(OccurrenceSlotView View, Type? Layout, int Offset)> ReadNamed(EntityRepository world, Entity unit,
        BehaviorRegistry registry, Fdp.Toolkit.Blueprints.BlueprintRegistry? blueprints)
    {
        var result = new List<(OccurrenceSlotView, Type?, int)>();
        if (!world.IsAlive(unit)) return result;
        var named = Candidates(world, unit, registry);
        OccurrenceStoreAccess.GetBlocksReadOnly(world, unit, out var blocks);
        for (int b = 0; b < blocks.Count; b++)
        {
            byte* mem = blocks.Memory(b);
            int n = BlueprintBlackboardPartitions.GetSlotCount(mem);
            for (int i = 0; i < n; i++)
            {
                ref var entry = ref BlueprintBlackboardPartitions.GetSlot(mem, i);
                if (entry.BlueprintId == 0) continue;
                var kind = BlueprintBlackboardPartitions.GetSlotKind(mem, i);
                named.TryGetValue(entry.BlueprintId, out var c);
                if (c is null && kind == OccurrenceKind.Blueprint && blueprints is not null
                    && blueprints.TryGetById(entry.BlueprintId, out var bp) && bp is { StateClrType: { } st })
                    c = new Candidate(SlotOwnerKind.InstanceBlueprint, bp.Name, SlotRole.State, st, 0, "blueprint");
                object? value = null, boxed = null;
                if (c?.Layout != null && Marshal.SizeOf(c.Layout) <= entry.PayloadSize - c.Offset)
                {
                    try
                    {
                        object dto = Marshal.PtrToStructure((IntPtr)(mem + entry.PayloadOffset + c.Offset), c.Layout)!;
                        value = DtoDiagnosticMapper.MapObject(dto, c.Layout, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    }
                    catch (ArgumentException) { value = null; }   // a layout PtrToStructure cannot read: listed, not decoded
                }
                if (c?.Layout is { IsValueType: true } layout && Fdp.Core.TypeLayout.SizeOf(layout) <= entry.PayloadSize - c.Offset)
                    boxed = BoxExact(mem + entry.PayloadOffset + c.Offset, layout);
                result.Add((new OccurrenceSlotView(entry.BlueprintId, kind, entry.PayloadSize, entry.StructureHash,
                    c?.Owner ?? SlotOwnerKind.OwnerUnknown, c?.Behaviour, c?.Role ?? SlotRole.RoleUnknown, c?.Label, c?.Layout?.Name, value, boxed),
                    c?.Layout, c?.Offset ?? 0));
            }
        }
        return result;
    }

    // ── ⭐ CE-1320 (Brain View §12g BV-65) — the ADDRESS of one field of one slot, for the ONE live write ─────────────────────────

    /// <summary>Which half of a behaviour a write addresses: its authored inputs or its working values.</summary>
    public enum SlotFieldRole : byte { FieldParams = 1, FieldVariables = 2 }   // ⚠ unique member names (the IDL rule above)

    /// <summary>One field's live address: the tier component that holds it, the byte offset inside that component, the field's type.
    /// ⭐ The same addressing the staged write and the pending read use (component + component-absolute offset).</summary>
    public sealed record SlotFieldAddress(
        Type ComponentType, int ComponentOffset, Type FieldType, int Size, SlotOwnerKind Owner, string Behaviour, string? Part, SlotFieldRole Role);

    /// <summary>
    /// ⭐ CE-1320 — resolves <paramref name="path"/> (dotted field names, <c>[n]</c> into an inline array) inside one slot of
    /// <paramref name="unit"/>: the root block of <paramref name="owner"/>'s behaviour (or of <paramref name="part"/>, a working-state
    /// or hosted-child slot named by its label), on its <paramref name="role"/> half. Reuses <see cref="Read"/>'s naming, so a slot the
    /// reader lists is a slot the writer reaches. <c>null</c> with <paramref name="error"/> on anything that does not resolve.
    /// </summary>
    public static unsafe SlotFieldAddress? ResolveField(
        EntityRepository world, Entity unit, BehaviorRegistry registry, Fdp.Toolkit.Blueprints.BlueprintRegistry? blueprints,
        SlotOwnerKind owner, string? behaviour, string? part, SlotFieldRole role, string path, out string? error)
    {
        error = null;
        path ??= "";   // ⭐ empty = the half itself (the params region a JSON apply writes whole)
        var slots = ReadNamed(world, unit, registry, blueprints);
        var match = slots.Where(s => s.View.Owner == owner
                                     && (behaviour is null || string.Equals(s.View.Behaviour, behaviour, StringComparison.Ordinal))
                                     && (part is null
                                         ? s.View.Role is SlotRole.Params || (owner == SlotOwnerKind.InstanceBlueprint && s.View.Role == SlotRole.State)
                                         : s.View.Role is SlotRole.Working or SlotRole.HostedChild && s.View.Label == part))
                         .ToList();
        if (match.Count == 0) { error = $"No {owner} slot{(part is null ? "" : $" '{part}'")}{(behaviour is null ? "" : $" of '{behaviour}'")} on this unit."; return null; }
        if (match.Count > 1) { error = $"{match.Count} slots match — name the behaviour."; return null; }
        var (view, layout, offset) = match[0];
        if (layout is not { IsValueType: true }) { error = $"Slot '{view.Label}' has no known layout to write through."; return null; }

        // ⭐ the root of each half — the SAME split the reader's display uses (BrainRunningBehaviours.SplitBlock)
        Type root = layout; int rootOffset = offset;
        bool isBlock = layout.GetField("In") is not null && layout.GetField("St") is not null;
        if (isBlock)
        {
            string half = role == SlotFieldRole.FieldParams ? "In" : "St";
            rootOffset += TypeLayout.OffsetOf(layout, half); root = layout.GetField(half)!.FieldType;
        }
        else if (owner == SlotOwnerKind.InstanceBlueprint && part is null)
        {
            if (role == SlotFieldRole.FieldVariables && path.Length == 0) { error = "Name the variable (a blueprint's State also holds its cursor)."; return null; }
            if (role == SlotFieldRole.FieldParams)
            {
                if (layout.GetField("Params") is not { } pf) { error = "This blueprint declares no parameters."; return null; }
                rootOffset += TypeLayout.OffsetOf(layout, "Params"); root = pf.FieldType;
            }
            else if (path.StartsWith("Cursor", StringComparison.Ordinal) || path.StartsWith("Params", StringComparison.Ordinal))
            { error = "Cursor and Params are not variables (write Params through role 'params')."; return null; }
        }
        else if (view.Role == SlotRole.Params && role == SlotFieldRole.FieldVariables)
        { error = $"'{view.Behaviour}' keeps no variables in its params block (its working state is in its node slots — name one with part)."; return null; }
        else if (view.Role == SlotRole.Working && role == SlotFieldRole.FieldParams)
        { error = "A node's working state has no parameters — use role 'variables'."; return null; }

        if (!TryWalk(root, path, out int fieldOffset, out Type fieldType, out error)) return null;

        // ⭐ the component that holds the slot — the tier ladder, ascending, the debug session's own order
        foreach (var spec in BlueprintTierTable.Ascending)
        {
            if (!spec.Has(world, unit)) continue;
            byte* mem = spec.MemoryReadOnly(world, unit);
            if (mem == null || !BlueprintBlackboardPartitions.TryGetSlotOffset(mem, view.Key, out int payload)) continue;
            return new SlotFieldAddress(spec.ComponentType, payload + rootOffset + fieldOffset, fieldType, TypeLayout.SizeOf(fieldType),
                owner, view.Behaviour ?? "", part, role);
        }
        error = "The slot is listed but no tier component holds it (detached this frame?).";
        return null;
    }

    /// <summary>Walks <c>A.B[2].C</c> from <paramref name="type"/>: field offsets by the managed layout, <c>[n]</c> into an
    /// <c>[InlineArray]</c>.</summary>
    internal static bool TryWalk(Type type, string path, out int offset, out Type fieldType, out string? error)
    {
        offset = 0; fieldType = type; error = null;
        if (path.Length == 0) return true;
        foreach (var raw in path.Split('.'))
        {
            string name = raw; int index = -1;
            int b = raw.IndexOf('[');
            if (b > 0 && raw.EndsWith(']') && int.TryParse(raw.AsSpan(b + 1, raw.Length - b - 2), out index)) name = raw[..b];
            var f = fieldType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f is null) { error = $"{fieldType.Name} has no field '{name}'."; return false; }
            offset += TypeLayout.OffsetOf(fieldType, name);
            fieldType = f.FieldType;
            if (index >= 0)
            {
                if (fieldType.GetCustomAttribute<InlineArrayAttribute>() is not { } ia) { error = $"'{name}' is not an inline array."; return false; }
                if (index >= ia.Length) { error = $"'{name}[{index}]' is past its capacity {ia.Length}."; return false; }
                var elem = fieldType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0].FieldType;
                offset += index * TypeLayout.SizeOf(elem);
                fieldType = elem;
            }
        }
        return true;
    }

    /// <summary>The value at <paramref name="at"/> as a boxed <paramref name="layout"/>, its exact managed image; <c>null</c> for a layout
    /// that is not unmanaged (StructEdit's ops refuse it).</summary>
    private static unsafe object? BoxExact(byte* at, Type layout)
    {
        try { return StructEdit.Core.Memory.RuntimeTypeOpsFactory.Get(layout).BoxFromNative(at); }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>The keys the unit's behaviours attached, each with its owner, role and layout.</summary>
    private static Dictionary<int, Candidate> Candidates(EntityRepository world, Entity unit, BehaviorRegistry registry)
    {
        var map = new Dictionary<int, Candidate>();
        if (world.IsComponentTypeRegistered<BehaviorState>() && world.HasComponent<BehaviorState>(unit))
            Add(map, registry, SlotOwnerKind.Task, world.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash);
        if (world.IsComponentTypeRegistered<SopState>() && world.HasComponent<SopState>(unit))
            Add(map, registry, SlotOwnerKind.Sop, world.GetComponentRO<SopState>(unit).SopHash);
        if (BehaviorIngressSystem.PausedTaskOf(world, unit) is { } paused)
            Add(map, registry, SlotOwnerKind.PausedTask, paused.Hash);
        return map;
    }

    private static void Add(Dictionary<int, Candidate> map, BehaviorRegistry registry, SlotOwnerKind owner, int hash)
    {
        if (hash == BehaviorIds.None || !registry.TryGetDefinition(hash, out var def)) return;
        string name = def.Name;
        Put(map, RootParamsAccess.KeyForBehaviour(hash), new Candidate(owner, name, SlotRole.Params, def.BlackboardLayoutType, 0, "params"));
        Put(map, RootStateAccess.KeyForBehaviour(hash), new Candidate(owner, name, SlotRole.State, null, 0, "tree state"));
        Put(map, RootHsmAccess.KeyForBehaviour(hash), new Candidate(owner, name, SlotRole.Hsm, null, 0, "HSM instance"));
        if (def.StatefulWorkingSlots is not { } slots) return;
        foreach (var s in HostedSubtree.EffectiveSlots(slots))
        {
            if (HostedSubtree.IsTreeStateSlot(s))
            {
                // a hosted child: its params block sits after its brain and start word (HostedSubtree's slot layout)
                HostedChildren.TryGetDefinition(s.SlotKey, out var child);
                Put(map, s.SlotKey, new Candidate(owner, child?.Name ?? name, SlotRole.HostedChild, child?.BlackboardLayoutType,
                    child != null ? HostedSubtree.BlockOffsetFor(child) : 0, s.NodeLabel ?? "hosted"));
            }
            else
            {
                Put(map, s.SlotKey, new Candidate(owner, name, SlotRole.Working, s.WorkingStateType, 0, s.NodeLabel));
            }
        }
    }

    /// <summary>The first claim wins: the running task before the SOP before the paused task (a shared key belongs to the one running).</summary>
    private static void Put(Dictionary<int, Candidate> map, int key, Candidate c)
    {
        if (key != 0) map.TryAdd(key, c);
    }
}
