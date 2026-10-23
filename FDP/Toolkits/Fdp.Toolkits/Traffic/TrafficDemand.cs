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
    /// <para>⭐ <c>[PerInstanceValue]</c> (CE-4016): the values are the scenario's, so a ghost of the settings must not promote before they
    /// arrive. 📌 Measured 2026-10-17 on a five-process cluster: without it the traffic node's ghost promoted on <c>EntityMaster</c> alone,
    /// its <c>DeferredTakeoverSystem</c> recorded the grant in that frame, and the creator's one in-window sample was then dropped as the
    /// node's own (<c>IsRecordedOwner</c>) — the node owned settings it never received, so SUMO never started. The addition rule holds:
    /// <c>EntityTrafficDemandEgressTranslator</c> sends a baseline for a new entity (its last-sent map starts empty).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.TrafficDemand)]
    [PerInstanceValue]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct TrafficDemand
    {
        /// <summary>How many cars SUMO keeps on the whole net at once (its live-city closed-loop target).</summary>
        public int TargetCars;

        /// <summary>The trip generator's seed: the same scenario gives the same traffic.</summary>
        public uint Seed;

        /// <summary>SUMO's step length in seconds. Cars move smoothly between steps on every receiver (dead reckoning,
        /// R-4020), so this is a fidelity knob, not a frame rate.</summary>
        public float StepSeconds;

        /// <summary>⭐ AQ93 §15 K1 — how many pedestrians SUMO keeps walking; 0 = none (the default: every scenario before slice 5).</summary>
        public int TargetPedestrians;

        /// <summary>⭐ §15 K4 — a shot panics the pedestrians within this many metres; 0 = <see cref="DefaultShotPanicRadius"/>.</summary>
        public float ShotPanicRadius;

        /// <summary>⭐ §15 K4 — a blast panics the pedestrians within this many metres; 0 = <see cref="DefaultBlastPanicRadius"/>.</summary>
        public float BlastPanicRadius;

        /// <summary>⭐ §15 K6 (P7) — at most this many pedestrians become our civilians per session; 0 = <see cref="DefaultMaxConverted"/>.</summary>
        public int MaxConverted;

        public const float DefaultShotPanicRadius = 50f;
        public const float DefaultBlastPanicRadius = 150f;
        public const int DefaultMaxConverted = 30;

        /// <summary>The panic radius of a shot (the authored value, else the default).</summary>
        public readonly float ShotRadius => ShotPanicRadius > 0f ? ShotPanicRadius : DefaultShotPanicRadius;

        /// <summary>The panic radius of a blast (the authored value, else the default).</summary>
        public readonly float BlastRadius => BlastPanicRadius > 0f ? BlastPanicRadius : DefaultBlastPanicRadius;

        /// <summary>The conversion cap (the authored value, else the default).</summary>
        public readonly int ConversionCap => MaxConverted > 0 ? MaxConverted : DefaultMaxConverted;

        /// <summary>A starting point: 200 cars, no pedestrians, seed 1, half-second steps (the live-city demo's step).</summary>
        public static TrafficDemand Default => new() { TargetCars = 200, Seed = 1, StepSeconds = 0.5f };
    }
}

namespace Fdp.Toolkit.Traffic
{
    /// <summary>
    /// ⭐ AQ93 §12 S2 (R-4056) — the TKB marker of the "Traffic" template: it makes the TRAFFIC ownership group apply, so the Brain that
    /// creates the settings entity grants its <c>TrafficDemand</c> to the least-loaded Traffic node (push-only, G-6), which serves it.
    /// It carries no values: the settings are authored per scenario (<see cref="TrafficDemand"/>).
    /// </summary>
    [Fdp.Toolkit.Tkb.Attributes.TkbDescriptor("Traffic.Demand")]
    public sealed record TrafficDemandDto;

    /// <summary>
    /// ⭐ CE-4016 — the "Traffic" template PRODUCES <see cref="TrafficDemand"/>, so the promotion gate (<c>[PerInstanceValue]</c> ∩ produced
    /// by the template's translators ∩ ingressible ∩ registered) holds a ghost of the settings until the creator's sample arrives — the
    /// same shape as <c>ObstacleTkbTranslator</c> / <c>ObstacleShape</c>. ⭐ The instance's value wins: it only adds the default where the
    /// entity has none (the create leg's <c>InitialComponents</c> lands on top of it anyway).
    /// </summary>
    public sealed class TrafficDemandTkbTranslator : Fdp.Interfaces.ITkbEntityTranslator
    {
        public System.Collections.Generic.IEnumerable<System.Type> GetConsumedDescriptors() { yield return typeof(TrafficDemandDto); }

        public System.Collections.Generic.IEnumerable<System.Type> GetProducedComponents() { yield return typeof(TrafficDemand); }

        public void Inject(EntityRepository repo, Entity entity, Fdp.Interfaces.TkbTemplate template)
        {
            if (template.GetDescriptor<TrafficDemandDto>() == null || !repo.IsComponentTypeRegistered<TrafficDemand>()) return;
            if (!repo.HasComponent<TrafficDemand>(entity)) repo.AddComponent(entity, TrafficDemand.Default);
        }
    }
}

namespace Fdp.Toolkit.Traffic
{
    /// <summary>
    /// ⭐ AQ93 §12 T1 (R-4056) — "SUMO may drive this car type": on an ordinary car template (its own model, its own TKB id — the same
    /// catalogue HROT's own civilian cars use), the SUMO vehicle class it stands for and its share of the traffic. The traffic node turns
    /// every such template into a SUMO vehicle type whose length and width are the TEMPLATE's (<c>VehicleParametersDto</c>), so SUMO's
    /// footprint is our model's, spawns from their weighted mix, and every node's replica of the car takes that TKB type.
    /// </summary>
    [Fdp.Toolkit.Tkb.Attributes.TkbDescriptor("Traffic.Vehicle")]
    public sealed record TrafficVehicleDto
    {
        /// <summary>SUMO's vehicle class (<c>passenger</c>, <c>delivery</c>, <c>bus</c>, <c>truck</c>, …).</summary>
        public string VClass { get; init; } = "passenger";

        /// <summary>This type's share of the traffic, relative to the other mapped types (0 = never spawned).</summary>
        public float Weight { get; init; } = 1f;
    }
}
