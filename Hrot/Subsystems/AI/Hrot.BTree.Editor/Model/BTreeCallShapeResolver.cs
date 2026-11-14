using System;
using System.Collections.Generic;
using Fbt;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Editor.AiShared;

namespace Hrot.BTree.Editor.Model;

/// <summary>
/// ⭐⭐ <b><c>CE-504</c> C-1 — the editor's side of the ONE call-shape rule (<see cref="BTreeCallShapes"/>).</b>
/// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §5 slice 1.
///
/// <para>The shape is no longer in the file, so the editor derives it: on open (every Action/Condition node), and when a
/// pick or a palette drop changes the bound method. 🔴 This is what lets a STATEFUL method be bound from the inspector —
/// before, a pick never moved the shape and the generator skipped the node (<c>BTREE0002</c>).</para>
///
/// <para>⚠ A method this process cannot resolve keeps the node's current shape: an editor without the behaviour assembly
/// loaded (a unit test) must not have its in-memory shape reset to a guess.</para>
/// </summary>
public static class BTreeCallShapeResolver
{
    /// <summary>The derived shape of one binding, or null when its method cannot be resolved here.</summary>
    public static BTreeActionDelegateShape? ShapeOf(
        BehaviorActionBinding? binding, Func<string, IReadOnlyList<CallParam>?> signatureOf)
        => binding is null ? null
         : BTreeCallShapes.Classify(binding.BlueprintAssetId, binding.MethodFqn, signatureOf) is { } s
             ? (BTreeActionDelegateShape)s : null;

    /// <summary>Re-derives every Action/Condition node's shape. ⭐ Not an edit: the shape is not persisted.</summary>
    public static int Resolve(BehaviorTreeAsset asset, Func<string, IReadOnlyList<CallParam>?> signatureOf)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        int n = 0;
        foreach (var node in asset.Nodes)
        {
            var binding = node.KernelType switch
            {
                NodeType.Action    => node.Action,
                NodeType.Condition => node.Condition,
                _                  => null,
            };
            if (ShapeOf(binding, signatureOf) is { } shape) { node.DelegateShape = shape; n++; }
        }
        return n;
    }
}
