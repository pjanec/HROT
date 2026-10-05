using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Events
{
    // ⚠ Its own file: the DDS code generator emits one IDL scope per source file (see SensorChangedEvent.cs).

    /// <summary>
    /// ⭐ <c>CE-3062</c> — <b>a unit HEARD something about here</b>: an ANONYMOUS estimate, never the source's identity (🔒 user,
    /// 2026-10-05: "I want Shot from north realism"). Published by the memory stage for every answer of an acoustic sensor, on
    /// the node that solves it, and carried to the Brain (DDS <c>AudioTargetDetected</c>), where the memory merges it
    /// (<c>CE-3063</c>). docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.1, §6 D.
    /// </summary>
    [EventId(PerceptionConstants.SoundContactEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SoundContactEvent
    {
        /// <summary>The unit that heard it.</summary>
        public Entity Observer;

        /// <summary>Where it seems to come from (metres, Sim Z-up) — off by up to <see cref="Radius"/>.</summary>
        public float X, Y, Z;

        /// <summary>How uncertain the estimate is (metres); grows with distance.</summary>
        public float Radius;

        /// <summary>What was heard (<c>Signatures.SoundKind</c>).</summary>
        public byte Kind;

        /// <summary>⭐ <c>CE-3063</c> (R-207) — what it sounded LIKE (<c>Tkb.Domain.SoundSourceClass</c>): coarse, never an identity.</summary>
        public byte SourceClass;
    }
}
