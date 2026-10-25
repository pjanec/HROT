using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Utility;

/// <summary>What a node does with a decision.</summary>
public enum DecisionRole { Chooses, Ranks, Reads, Holds }

/// <summary>A decision a node binds: which, how, through which params field, and (an <c>IsOption</c>) which option it reads.</summary>
public sealed record BoundDecision(int DecisionId, DecisionRole Role, string Via, int Option = -1);

/// <summary>
/// ⭐⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md §3, §4) — WHICH DECISIONS A NODE BINDS, read from the RUNNING behaviour (the
/// registry's blob), never from a document: a C#-built tree and an authored one take the same path.
/// <para>⭐ A shared node's method key is <c>{method}@{paramOffset}[@{stateOffset}]</c> (<c>SharedNodeBinder</c>, <c>HsmActionKey</c>): the
/// param offset names the field of the behaviour's block, and the block's DEFAULTS (its <c>ParseParams("{}")</c>) hold the
/// <see cref="UtilityDecisionRef"/> there. ⭐ An <c>IsOption</c> only reads the winner: its decision is the <c>ChooseOption</c> that writes
/// the SAME working state (the same state offset). ⭐ A custom action whose params hold decision refs (a field of type
/// <see cref="UtilityDecisionRef"/>, at any depth) binds each.</para>
/// </summary>
public static class UtilityBindings
{
    private const string Nodes = "Fdp.Toolkit.Utility.UtilityNodes.";

    /// <summary>The decisions <paramref name="selection"/> binds in behaviour <paramref name="behaviour"/> (empty: none, or unknown).</summary>
    public static IReadOnlyList<BoundDecision> Of(BehaviorRegistry? registry, string? behaviour, IAssetSubSelection? selection)
    {
        if (registry == null || string.IsNullOrEmpty(behaviour) || selection == null || !registry.TryGetId(behaviour, out int id)
            || !registry.TryGetDefinition(id, out var def))
            return Array.Empty<BoundDecision>();
        return selection switch
        {
            BTreeNodeSelection n => BTree(registry, id, def, n.VisualId),
            HsmStateSelection st => Hsm(def, st.StableId, transition: false),
            HsmTransitionSelection tr => Hsm(def, tr.VisualId, transition: true),
            _ => Array.Empty<BoundDecision>(),
        };
    }

    private static IReadOnlyList<BoundDecision> BTree(BehaviorRegistry registry, int id, BehaviorDefinition def, Guid visualId)
    {
        if (!registry.TryGetTreeBlob(id, out var blob) || blob.DebugMetadata is not { } meta) return Array.Empty<BoundDecision>();
        int index = Array.FindIndex(meta, m => m != null && Guid.TryParse(m.VisualId, out var v) && v == visualId);
        if (index < 0 || index >= blob.Nodes.Length) return Array.Empty<BoundDecision>();
        var node = blob.Nodes[index];
        if (node.Type is not (Fbt.NodeType.Action or Fbt.NodeType.Condition) || node.PayloadIndex >= blob.MethodNames.Length)
            return Array.Empty<BoundDecision>();
        return FromKey(def, blob.MethodNames[node.PayloadIndex], blob.MethodNames);
    }

    private static IReadOnlyList<BoundDecision> Hsm(BehaviorDefinition def, Guid element, bool transition)
    {
        if (def.HsmDefinition is not { } blob || def.HsmMetadata is not { } meta) return Array.Empty<BoundDecision>();
        var keys = meta.ActionNames.Values.ToArray();
        ushort actionId;
        if (!transition)
        {
            var hit = meta.StateStableIds.FirstOrDefault(kv => kv.Value == element);
            if (hit.Value != element || hit.Key >= blob.States.Length) return Array.Empty<BoundDecision>();
            actionId = blob.States[hit.Key].ActivityActionId;
        }
        else
        {
            var hit = meta.TransitionVisualIds.FirstOrDefault(kv => kv.Value == element);
            if (hit.Value != element) return Array.Empty<BoundDecision>();
            int k = hit.Key, t = blob.Transitions.Length;
            actionId = k < t ? blob.Transitions[k].GuardId : k - t < blob.GlobalTransitions.Length ? blob.GlobalTransitions[k - t].GuardId : (ushort)0;
        }
        return actionId != 0 && meta.ActionNames.TryGetValue(actionId, out var key) ? FromKey(def, key, keys) : Array.Empty<BoundDecision>();
    }

    /// <summary>The decisions one method key binds (pure over the key, the behaviour's default block and its sibling keys).</summary>
    public static IReadOnlyList<BoundDecision> FromKey(BehaviorDefinition def, string key, IReadOnlyList<string> siblings)
    {
        var (fqn, po, so) = Split(key);
        if (fqn == null || po < 0) return Array.Empty<BoundDecision>();
        var block = DefaultBlock(def);
        if (block == null) return Array.Empty<BoundDecision>();
        string field = FieldAt(def, po) ?? $"@{po}";
        if (fqn == Nodes + "ChooseOption" || fqn == Nodes + "ChooseOptionOnce")
            return Ref(block, po) is int d and not 0 ? new[] { new BoundDecision(d, DecisionRole.Chooses, field) } : Array.Empty<BoundDecision>();
        if (fqn == Nodes + "RankCandidates")
            return Ref(block, po) is int r and not 0 ? new[] { new BoundDecision(r, DecisionRole.Ranks, field) } : Array.Empty<BoundDecision>();
        if (fqn == Nodes + "IsOption")
        {
            int option = po < block.Length ? block[po] : -1;
            foreach (var s in siblings)
            {
                var (f2, p2, s2) = Split(s);
                if (s2 == so && so >= 0 && (f2 == Nodes + "ChooseOption" || f2 == Nodes + "ChooseOptionOnce") && Ref(block, p2) is int d and not 0)
                    return new[] { new BoundDecision(d, DecisionRole.Reads, FieldAt(def, p2) ?? $"@{p2}", option) };
            }
            return Array.Empty<BoundDecision>();
        }
        // a custom action: every UtilityDecisionRef inside its params field
        if (def.BlackboardLayoutType is not { } layout || FieldInfoAt(layout, po) is not { } fi) return Array.Empty<BoundDecision>();
        var found = new List<BoundDecision>();
        Walk(fi.FieldType, po, field, block, found, depth: 0);
        return found;
    }

    private static void Walk(Type t, int baseOffset, string path, byte[] block, List<BoundDecision> found, int depth)
    {
        if (depth > 4 || !t.IsValueType || t.IsPrimitive || t.IsEnum) return;
        if (t == typeof(UtilityDecisionRef))
        {
            if (Ref(block, baseOffset) is int d and not 0) found.Add(new BoundDecision(d, DecisionRole.Holds, path));
            return;
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            int off;
            try { off = (int)Marshal.OffsetOf(t, f.Name); } catch (ArgumentException) { continue; }
            Walk(f.FieldType, baseOffset + off, path + "." + f.Name, block, found, depth + 1);
        }
    }

    private static int? Ref(byte[] block, int offset) => offset >= 0 && offset + 4 <= block.Length ? BitConverter.ToInt32(block, offset) : null;

    /// <summary>"{fqn}@{po}[@{so}]" → its parts (-1 when absent).</summary>
    public static (string? Fqn, int ParamOffset, int StateOffset) Split(string key)
    {
        var parts = key.Split('@');
        int po = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : -1;
        int so = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : -1;
        return (parts[0], po, so);
    }

    private static string? FieldAt(BehaviorDefinition def, int offset)
        => def.BlackboardLayoutType is { } t && FieldInfoAt(t, offset) is { } f ? f.Name : null;

    private static FieldInfo? FieldInfoAt(Type t, int offset)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            try { if ((int)Marshal.OffsetOf(t, f.Name) == offset) return f; } catch (ArgumentException) { }
        }
        return null;
    }

    private static readonly Dictionary<BehaviorDefinition, byte[]?> Blocks = new(ReferenceEqualityComparer.Instance);
    private static EntityRepository? _scratch;

    /// <summary>The behaviour's block as a unit with no authored params starts it: <c>ParseParams("{}")</c>, else its baked defaults
    /// (cached per definition: a hot reload brings a new one).</summary>
    public static unsafe byte[]? DefaultBlock(BehaviorDefinition def)
    {
        lock (Blocks)
        {
            if (Blocks.TryGetValue(def, out var cached)) return cached;
            int size = def.BlackboardLayoutType is { } t ? Fdp.Core.TypeLayout.SizeOf(t) : def.BrainStateBytes;
            byte[]? block = null;
            if (size > 0)
            {
                block = new byte[size];
                try
                {
                    fixed (byte* p = block)
                    {
                        if (def.ParseParams is { } parse) parse("{}", p, size, _scratch ??= new EntityRepository(), Entity.Null);
                        else if (def.BakeDefaults is { } bake) bake(p, size);
                    }
                }
                catch (Exception) { block = null; }   // a resolver that needs a real world: no default to read
            }
            Blocks[def] = block;
            return block;
        }
    }
}
