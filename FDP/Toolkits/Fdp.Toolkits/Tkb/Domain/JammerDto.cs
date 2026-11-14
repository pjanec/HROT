using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-7014</c> (City Battle §11 R-2; DESIGN_FPV_Drone §1 "jamming … later") — a type that JAMS the control link of small drones within
    /// <see cref="RadiusM"/>: a drone of ANOTHER force inside the bubble loses its link (<c>DroneJamming</c>) — an FPV in its dive falls where it is.
    /// </summary>
    [TkbDescriptor("Electronic.DroneJammer")]
    public record JammerDto
    {
        /// <summary>The bubble's radius (m) around the jammer.</summary>
        public float RadiusM { get; init; } = 400f;
    }
}
