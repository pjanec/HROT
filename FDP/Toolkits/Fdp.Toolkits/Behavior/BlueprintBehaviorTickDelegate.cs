using Fbt;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-446</c> (<c>Q77</c>) — ticks ONE behaviour implemented by a blueprint (<see cref="BehaviorConstants.BrainTierBlueprint"/>)
    /// for one entity, over its ROOT BLOCK.
    ///
    /// <para>
    /// ⭐ <paramref name="block"/> is the root params slot — the behaviour's <c>[In][St]</c> block, allocated and filled by
    /// ingress exactly as for BTree/HSM (<c>R-151</c>, <c>R-153</c>); the latent phase lives inside <c>St</c>, so there is no
    /// separate cursor slot. ⭐ The return is the whole contract of ending (<c>Q77</c> D, <c>Q33</c> ruling 2): <c>Running</c>
    /// (including suspended on a latent node) keeps it alive; <c>Success</c>/<c>Failure</c> finishes it, and
    /// <c>BrainTickSystem</c> publishes <c>BehaviorFinishedEvent</c> once per <c>InstanceId</c>.
    /// </para>
    /// </summary>
    public delegate NodeStatus BlueprintBehaviorTickDelegate(
        ref byte block, EntityRepository world, Entity self, float time, float deltaTime);
}
