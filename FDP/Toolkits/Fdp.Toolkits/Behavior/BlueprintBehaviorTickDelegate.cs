using Fbt;
using Fdp.Core;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-446</c> (<c>Q77</c>) — ticks ONE behaviour implemented by a blueprint (<see cref="BehaviorConstants.BrainTierBlueprint"/>)
    /// for one entity.
    ///
    /// <para>
    /// ⭐ S2 (<c>DESIGN_Unified_Behaviour_Run</c> U-1, <c>R-151</c>): <paramref name="block"/> is the root params slot — the
    /// behaviour's blackboard <c>{ In; St }</c>, exactly as for BTree/HSM; <paramref name="exec"/> is the root STATE slot —
    /// its brain state (latent cursor, When memory, suspended-graph locals), sized by
    /// <see cref="BehaviorDefinition.BrainStateBytes"/>. ⛔ The brain state is no longer inside the block.
    /// ⭐ The return is the whole contract of ending (<c>Q77</c> D, <c>Q33</c> ruling 2): <c>Running</c> (including suspended
    /// on a latent node) keeps it alive; <c>Success</c>/<c>Failure</c> finishes it.
    /// </para>
    /// <para>⭐ <paramref name="ecb"/> — the frame's command buffer, as an Instance tick receives it (EQS spawns etc.);
    /// <paramref name="instanceId"/> — <c>BehaviorState.InstanceId</c>, the latent cursor's version (bumps on every assign).</para>
    /// </summary>
    public delegate NodeStatus BlueprintBehaviorTickDelegate(
        ref byte block, ref byte exec, EntityRepository world, IEntityCommandBuffer ecb, Entity self,
        float time, float deltaTime, uint instanceId);
}
