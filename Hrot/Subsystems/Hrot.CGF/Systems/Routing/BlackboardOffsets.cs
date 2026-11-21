namespace Hrot.CGF.Systems.Routing;

/// <summary>
/// ⛔ <b>EMPTY, and as of <c>O2</c> (2026-09-20) its premise is gone too.</b>
///
/// <para>It held named byte offsets into <c>BrainBlackboard</c> for the route "soft advice" values
/// <see cref="RouteContextSystem"/> writes. Those values are now NAMED FIELDS of
/// <see cref="Fdp.Toolkit.Behavior.Components.BrainInterrupts"/>, so there is nothing left to offset
/// into — a blind byte poke was exactly what the split retired.</para>
///
/// <para>⚠ Kept rather than deleted: <c>ROUTES1-DESIGN</c> §"Blackboard offsets" is its owning design
/// record, and removing a type named by a design is not this task's call. ⭐ It is a deletion
/// CANDIDATE for whoever closes routes-1 — zero members, zero references (measured 2026-09-20,
/// <c>scripts/find.sh</c>, graph and grep agreeing).</para>
/// </summary>
public static class BlackboardOffsets
{
}
