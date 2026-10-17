using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Traffic
{
    /// <summary>
    /// ⭐ AQ93 R-4021 — the scenario's TRAFFIC SETTINGS: one positionless entity (TKB "Traffic") whose presence asks for SUMO
    /// background traffic over the terrain's SUMO net. 🔒 R-4047: no entity ⇒ SUMO does no work at all; the roads still draw.
    /// <para>Authored and saved as it stands (the scenario serializer's default policy), like <c>ScenarioClock</c>. Registered on
    /// every node (<c>HrotSharedComponentRegistry</c>) so a scenario that carries it loads anywhere; only a node that composes the
    /// traffic pack acts on it. 📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §10 (EM-1), §11.3 (D4).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.TrafficDemand)]
    public struct TrafficDemand
    {
        /// <summary>How many cars SUMO keeps on the whole net at once (its live-city closed-loop target).</summary>
        public int TargetCars;

        /// <summary>The trip generator's seed: the same scenario gives the same traffic.</summary>
        public uint Seed;

        /// <summary>SUMO's step length in seconds. Cars move smoothly between steps on every receiver (dead reckoning,
        /// R-4020), so this is a fidelity knob, not a frame rate.</summary>
        public float StepSeconds;

        /// <summary>A starting point: 200 cars, seed 1, half-second steps (the live-city demo's step).</summary>
        public static TrafficDemand Default => new() { TargetCars = 200, Seed = 1, StepSeconds = 0.5f };
    }
}
