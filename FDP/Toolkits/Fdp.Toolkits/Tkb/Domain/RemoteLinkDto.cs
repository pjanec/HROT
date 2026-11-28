using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-106 / D-107) — a type that can be flown by RADIO from a ground
    /// station: the link's range from the station (the quality falls to 0 over its last 20 %) and how long it hovers when the link is lost before it returns
    /// home (the failsafe). ⭐ A radio link is what a jammer's bubble cuts (<c>DroneJamming.InBubble</c>) — the type a jammer can beat, whatever the AI drones'
    /// <c>DronesUnjammable</c> rule says of the optical-fibre FPVs. Read by <c>RemotePilot</c> only.
    /// </summary>
    [TkbDescriptor("Gen.RemoteLink")]
    public record RemoteLinkDto
    {
        /// <summary>The link's range (m) from the station.</summary>
        public float RangeM { get; init; } = 3000f;

        /// <summary>Link lost: it hovers this long (s), then returns home and lands.</summary>
        public float FailsafeSeconds { get; init; } = 5f;
    }
}
