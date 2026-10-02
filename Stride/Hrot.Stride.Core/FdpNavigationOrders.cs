#nullable enable
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;

namespace Hrot.Stride.Core;

/// <summary>
/// Helpers for issuing PRODUCTION FDP navigation orders through the real command front door
/// (BATCH-20, STR-D19).
///
/// <para>
/// The character navigation production path is gated on a <see cref="LocomotionChannel"/>
/// carrying the <see cref="NavigationConstants.ActionIdMoveTo"/> action — this is the exact
/// signal a BehaviorTree / HSM node emits and the only trigger
/// <c>NavigationIntentBridgeSystem</c> uses to auto-register a DotRecast crowd agent for a
/// non-vehicle entity.  Writing into the channel's <c>fixed byte Params</c> buffer requires
/// an <c>unsafe</c> context, which the GPU app project does not enable; this helper lives in
/// <c>Hrot.Stride.Core</c> (which does) so the harness and headless tests can both call it
/// without leaking <c>unsafe</c> into the app project.
/// </para>
/// </summary>
public static class FdpNavigationOrders
{
    /// <summary>
    /// Writes a <see cref="NavigationConstants.ActionIdMoveTo"/> action into the entity's
    /// <see cref="LocomotionChannel"/> with a fresh (incremented) <c>ActionInstanceId</c> and the
    /// supplied <see cref="MoveToParams"/> — exactly the way a BehaviorTree/HSM node does.
    ///
    /// <para>
    /// This is the production trigger that <c>NavigationIntentBridgeSystem</c> consumes to
    /// auto-register a crowd agent (for non-vehicle entities) and set its target.  The component
    /// is added if absent (only when <see cref="LocomotionChannel"/> is registered in the world).
    /// </para>
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The target entity (must be alive).</param>
    /// <param name="destinationFdp">Goal position in FDP world space (X=East, Y=North, Z=Up).</param>
    /// <param name="speed">Desired travel speed (m/s).</param>
    /// <param name="arrivalRadius">Arrival radius (m).</param>
    /// <param name="layerMask">Navmesh layer mask (default Infantry).</param>
    /// <returns>
    /// The <c>ActionInstanceId</c> written (so callers can correlate the issued order), or 0 if
    /// the <see cref="LocomotionChannel"/> component type is not registered.
    /// </returns>
    public static unsafe uint IssueMoveTo(
        EntityRepository world,
        Entity entity,
        Vector3 destinationFdp,
        float speed,
        float arrivalRadius,
        NavLayerMask layerMask = NavLayerMask.Infantry)
    {
        if (!world.IsComponentTypeRegistered<LocomotionChannel>())
            return 0u;

        var ch = world.HasComponent<LocomotionChannel>(entity)
            ? world.GetComponent<LocomotionChannel>(entity)
            : default;

        ch.ActiveAction      = NavigationConstants.ActionIdMoveTo;
        ch.ActionInstanceId += 1; // fresh instance id → bridge treats it as a new action
        ch.Status            = NodeStatus.Running;

        // ── Claim the BehaviorState preemption token ──────────────────────────
        // ChannelArbitrationSystem clears any LocomotionChannel whose
        // BehaviorInstanceId != BehaviorState.InstanceId.  An external issuer (harness,
        // headless test, mission script) must therefore stamp the entity's current
        // BehaviorState.InstanceId into BehaviorInstanceId so the channel is not
        // immediately wiped on the next simulation tick.
        if (world.IsComponentTypeRegistered<BehaviorState>()
            && world.HasComponent<BehaviorState>(entity))
        {
            ch.BehaviorInstanceId = world.GetComponent<BehaviorState>(entity).InstanceId;
        }

        var p = new MoveToParams
        {
            Destination   = destinationFdp, // FDP Sim Z-up; goal Z carried, steering 2D-projected
            ArrivalRadius = arrivalRadius,
            Speed         = speed,
            LayerMask     = (uint)layerMask,
        };

        // Copy params into the fixed Params buffer (requires unsafe).
        LocomotionChannel* pCh = &ch;
        *(MoveToParams*)pCh->Params = p;

        if (world.HasComponent<LocomotionChannel>(entity))
            world.SetComponent(entity, ch);
        else
            world.AddComponent(entity, ch);

        return ch.ActionInstanceId;
    }
}
