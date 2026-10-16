using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Contracts
{
    /// <summary>
    /// ⭐⭐ <c>CE-1128</c> (docs/DESIGN_Visual_Effects.md §10 IM-2, IM-3) — a round marked a surface of the static world: it ENTERED or LEFT a
    /// piece it passed through (a fence, a wall, a floor — R-217: it goes on), or STOPPED on one (a wall it could not pass, the ground).
    /// Published by <c>BallisticsSystem</c> on the muscle; crosses the wire as the <c>RoundImpact</c> topic; every map host draws a puff and a
    /// mark from it. ⛔ Never <see cref="DetonationNotification"/>: that is a COMBAT event (area effect, damage, heat, the fire-mission
    /// observer's 64-burst memory) and a burst of rifle fire would flood it. A strike on a UNIT stays the detonation (IM-8).
    /// </summary>
    [EventId(5010)]   // = CombatConstants.RoundImpactNotificationEventId; must not change.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RoundImpactNotification
    {
        /// <summary>Local ECS entity handle of the shooter (null when an ingress could not resolve it).</summary>
        public Entity Shooter;

        /// <summary>Where the round met the surface (world metres).</summary>
        public float X, Y, Z;

        /// <summary>The outward normal of the face it met (all 0 = unknown: the mark faces back along the travel).</summary>
        public float NormalX, NormalY, NormalZ;

        /// <summary>The direction the round was travelling (any length; 0 = unknown).</summary>
        public float TravelX, TravelY, TravelZ;

        /// <summary>The TKB type id of the munition (0 = unknown) — its effect set picks the puff and the mark (VE-D).</summary>
        public long Ammo;

        /// <summary><see cref="ImpactKind"/>: 0 Entry, 1 Exit, 2 Stop.</summary>
        public byte Kind;

        /// <summary>True when this notification came from network ingress.</summary>
        public bool IsRemote;
    }
}
