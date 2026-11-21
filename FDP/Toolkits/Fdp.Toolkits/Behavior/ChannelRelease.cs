using System;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-1411</c> — THE one implementation of <i>"a <c>[WritesChannel]</c> node that returns <c>Failure</c> releases its
    /// channels"</i> (<c>CE-417</c>): the channel's <c>ActiveAction</c> is cleared and its <c>ActionInstanceId</c> stepped, so the
    /// dispatcher sees a new instance and runs the abandoned action's OnExit (the same two writes <c>ChannelArbitrationSystem</c> makes).
    /// <para>Called by every path that binds such a node: the JSON asset's generated thunks and the analyzer's 4-param wrapper (both emit a
    /// call here through <c>ChannelClearEmit</c>, linked into the generators), and a C#-built tree's <see cref="SharedNodeBinder"/> thunks.
    /// ⛔ HISTORY: the emitter wrote the two writes inline into every generated thunk and the binder had no such arm at all, so a curated
    /// tree's channel-writing node kept its channel claimed on Failure (CE-1411).</para>
    /// </summary>
    public static class ChannelRelease
    {
        /// <summary>When <paramref name="status"/> is <see cref="NodeStatus.Failure"/>, releases <paramref name="kind"/>'s channel on
        /// <paramref name="self"/>. Returns <paramref name="status"/> unchanged.</summary>
        public static NodeStatus OnFailure(NodeStatus status, EntityRepository world, Entity self, ChannelKind kind)
        {
            if (status == NodeStatus.Failure) Release(world, self, kind);
            return status;
        }

        /// <summary>Releases <paramref name="kind"/>'s channel on <paramref name="self"/>: <c>ActiveAction</c> = 0, <c>ActionInstanceId</c> + 1.
        /// ⭐ <c>CE-1432</c> — also the home of the HSM exit cleanup (<c>HsmActionGenerator</c>, through <c>ChannelClearEmit.EmitRelease</c>),
        /// the blueprint AI primitive's exit cleanup (<c>AiPrimitiveEmitter</c>) and <c>ChannelArbitrationSystem</c>'s stale-claim release.</summary>
        public static void Release(EntityRepository world, Entity self, ChannelKind kind)
        {
            switch (kind)
            {
                case ChannelKind.Locomotion:
                {
                    ref var ch = ref world.GetComponentRW<LocomotionChannel>(self);
                    ch.ActiveAction     = 0;
                    ch.ActionInstanceId = unchecked(ch.ActionInstanceId + 1u);
                    break;
                }
                case ChannelKind.Weapon:
                {
                    ref var ch = ref world.GetComponentRW<WeaponChannel>(self);
                    ch.ActiveAction     = 0;
                    ch.ActionInstanceId = unchecked(ch.ActionInstanceId + 1u);
                    break;
                }
                case ChannelKind.Interaction:
                {
                    ref var ch = ref world.GetComponentRW<InteractionChannel>(self);
                    ch.ActiveAction     = 0;
                    ch.ActionInstanceId = unchecked(ch.ActionInstanceId + 1u);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "CE-1411: a channel kind with no channel component.");
            }
        }

        /// <summary>The channel component a <see cref="ChannelKind"/> names — the runtime twin of <c>ChannelClearEmit.ChannelKindToType</c>.</summary>
        public static Type ComponentTypeOf(ChannelKind kind) => kind switch
        {
            ChannelKind.Locomotion  => typeof(LocomotionChannel),
            ChannelKind.Weapon      => typeof(WeaponChannel),
            ChannelKind.Interaction => typeof(InteractionChannel),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "CE-1410: a channel kind with no channel component."),
        };
    }
}
