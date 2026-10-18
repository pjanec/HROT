using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Diagnostics;

namespace Fdp.Toolkit.Behavior;

/// <summary>Who owns a slot: the running task, the SOP, or the task a reaction paused. ⚠ Member names unique across the file's enums (the DDS IDL generator emits them into one scope).</summary>
public enum SlotOwnerKind : byte { OwnerUnknown = 0, Task = 1, Sop = 2, PausedTask = 3 }

/// <summary>What a slot is to its owner.</summary>
public enum SlotRole : byte { RoleUnknown = 0, Params = 1, State = 2, Hsm = 3, Working = 4, HostedChild = 5 }

/// <summary>One occurrence slot of a unit, named when its owner is known, its value decoded when its layout is.</summary>
public sealed record OccurrenceSlotView(
    int Key, OccurrenceKind Kind, int PayloadSize, uint StructureHash,
    SlotOwnerKind Owner, string? Behaviour, SlotRole Role, string? Label, string? LayoutType, object? Value);

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

    public static unsafe List<OccurrenceSlotView> Read(EntityRepository world, Entity unit, BehaviorRegistry registry)
    {
        var result = new List<OccurrenceSlotView>();
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
                object? value = null;
                if (c?.Layout != null && Marshal.SizeOf(c.Layout) <= entry.PayloadSize - c.Offset)
                {
                    try
                    {
                        object dto = Marshal.PtrToStructure((IntPtr)(mem + entry.PayloadOffset + c.Offset), c.Layout)!;
                        value = DtoDiagnosticMapper.MapObject(dto, c.Layout, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    }
                    catch (ArgumentException) { value = null; }   // a layout PtrToStructure cannot read: listed, not decoded
                }
                result.Add(new OccurrenceSlotView(entry.BlueprintId, kind, entry.PayloadSize, entry.StructureHash,
                    c?.Owner ?? SlotOwnerKind.OwnerUnknown, c?.Behaviour, c?.Role ?? SlotRole.RoleUnknown, c?.Label, c?.Layout?.Name, value));
            }
        }
        return result;
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
