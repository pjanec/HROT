using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.IG
{
    /// <summary>
    /// Published by <see cref="Hrot.Network.NED.IG.WeaponFireIngressTranslator"/> when a
    /// <c>WeaponFire</c> DDS message is received. The IG visual layer consumes this
    /// event to trigger a muzzle-flash particle effect on the shooter entity.
    /// </summary>
    [EventId(6001)]
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct IgWeaponFireEvent
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public long TargetEntityId;

        /// <summary>Zero-based weapon slot index.</summary>
        public int WeaponIndex;
    }

    /// <summary>
    /// Sent from ExCon to IG to update the list of context-menu actions available
    /// for a specific network entity.
    /// </summary>
    public sealed class ContextActionsUpdate
    {
        /// <summary>Network identity of the entity whose actions are being updated.</summary>
        public int EntityNetworkId { get; init; }

        /// <summary>Replacement action list (replaces any previously stored list).</summary>
        public System.Collections.Generic.List<Hrot.IG.Components.ContextAction> Actions { get; init; } = new();
    }
}
