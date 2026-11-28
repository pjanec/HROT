using System.Numerics;
using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Devices.Contracts
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119, §3r D-57 / D-58) — the FIRST device-to-device DTO: a target's
    /// LOCATION as a ranging device measured it — the point the laser met, its range, when (sim seconds) and the sender's TKB type. Unmanaged, 32 B, the payload
    /// of a <c>DeviceMessageEvent</c> of <see cref="Kind"/> (<c>DeviceMessages.Send</c>). ⭐ Shared by every device folder (the rangefinder sends it, the binoculars
    /// and — SD-S5c — the designator receive it), so the two sides cannot disagree on its layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TargetLocation
    {
        /// <summary>The message kind of this DTO.</summary>
        public const ushort Kind = 1;

        /// <summary>The world point the sender's laser met.</summary>
        public Vector3 Point;
        /// <summary>Metres from the sender to <see cref="Point"/>.</summary>
        public float Range;
        /// <summary>When it was measured (sim seconds).</summary>
        public double TakenAt;
        /// <summary>The sender's TKB type (a receiver's HUD names it).</summary>
        public long FromType;
    }
}
