using System;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-431</c> — the ONE spelling of "the blackboard block this running behaviour reads".</b>
/// 📄 <c>Architect_Question_76</c> §11.7.
///
/// <para>🔴 <b>What it replaces.</b> Every generated BTree thunk projected its parameters from
/// <c>RootParamsAccess.RootRef(ctx.World, ctx.Self)</c> — the ENTITY's root block — and ignored the
/// <c>ref byte bb</c> the interpreter hands it. ⇒ a hosted subtree read its HOST's bytes no matter
/// what it was ticked with. ⭐ The interpreter already threads <c>bb</c> by <c>ref</c> through every
/// frame, and the root tick already passes the root block as <c>bb</c>, so projecting from <c>bb</c>
/// gives the root exactly the bytes it read before and gives a child the block its host hands it.</para>
///
/// <para>⛔⛔ <b>"No block" is a SENTINEL, never a stack scratch byte.</b> A behaviour with no
/// parameters has no block, and a thunk projecting at an offset from a one-byte stack local would read
/// AND WRITE the stack silently — where <c>RootRef</c> used to throw. ⇒ callers with no block pass
/// <see cref="None"/>, and <see cref="Require"/> turns a projection from it back into a loud failure.</para>
/// </summary>
public static class BehaviorBlock
{
    // ⚠ A static field, so a `ref` to it is stable for the process and comparable by address.
    private static byte _none;

    /// <summary>⭐ The "this behaviour has no block" argument. ⛔ Never projected from — see <see cref="Require"/>.</summary>
    public static ref byte None => ref _none;

    /// <summary>⭐ <c>true</c> when <paramref name="bb"/> is a real block, not <see cref="None"/>.</summary>
    public static bool Has(ref byte bb) => !Unsafe.AreSame(ref bb, ref _none);

    /// <summary>
    /// ⭐⭐ <paramref name="bb"/>, or a THROW when it is <see cref="None"/>. Every emitted projection goes
    /// through this: one pointer compare per projection, and the price of never corrupting memory.
    /// </summary>
    public static ref byte Require(ref byte bb)
    {
        if (Unsafe.AreSame(ref bb, ref _none))
            throw new InvalidOperationException(
                "CE-431: a behaviour thunk projected a blackboard field, but the running behaviour has " +
                "no blackboard block. Either the behaviour declares no parameters/state (so nothing should " +
                "project), or it is a hosted child whose slot was provisioned without its block — check " +
                "HostedSubtree.EffectiveSlots.");
        return ref bb;
    }
}
