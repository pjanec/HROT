using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Player
{
    /// <summary>
    /// Published by <see cref="Hrot.Network.NED.Player.WeaponFireIngressTranslator"/> when a
    /// <c>WeaponFire</c> DDS message is received. The IG visual layer consumes this
    /// event to trigger a muzzle-flash particle effect on the shooter entity.
    /// </summary>
    [EventId(6001)]
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct PlayerWeaponFireEvent
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public int TargetEntityId;

        /// <summary>Zero-based weapon slot index.</summary>
        public int WeaponIndex;
    }

    // ContextActionsUpdate and ContextActionTriggered have been promoted to
    // Hrot.Common.Events (see Hrot/Engine/Hrot.Common/Events/ContextMenuEvents.cs).
}
