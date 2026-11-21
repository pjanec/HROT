using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <c>CE-3163</c> — the ONE spelling of "the block this running HSM's action reads", the HSM twin of
/// <see cref="BehaviorBlock"/>. 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.7 (D1, D2).
///
/// <para>🔴 <b>What it replaces.</b> Every emitted HSM thunk read <see cref="RootParamsAccess.RequireRootBytes(EntityRepository, Entity, out int)"/>
/// — the ENTITY's root block — so a machine hosted as a child (a state or a tree hosting it) read its HOST's bytes at its own
/// offsets. ⭐ <c>HsmRunner</c> is handed the machine's own block (root, or the child's slot) and now puts it on the
/// <see cref="HsmKernelBridge"/>; every action, emitted or fluent (<see cref="HsmSharedBinder"/>), reads it here.</para>
///
/// <para>⚠ A bridge built OUTSIDE the runner (tests, fixtures that call <c>HsmKernel.Update</c> directly) carries no block:
/// it falls back to the root block — exactly what those callers read before.</para>
/// </summary>
public static unsafe class HsmBlock
{
    /// <summary>The running machine's block and its extent; throws (naming the causes) when there is none.</summary>
    /// <param name="context">the <c>HsmKernelBridge*</c> the kernel hands every action and guard.</param>
    public static byte* Require(void* context, out int length)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var bridge = (HsmKernelBridge*)context;
        if (bridge->Block != null)
        {
            length = bridge->BlockLength;
            return bridge->Block;
        }
        var world = (EntityRepository)System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
        return RootParamsAccess.RequireRootBytes(world, bridge->Self, out length);
    }
}
