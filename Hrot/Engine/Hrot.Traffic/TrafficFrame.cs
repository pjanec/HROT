using Fdp.Core;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 — what SUMO looks like THIS frame, for the maps: every car from every sender (dead-reckoned, terrain-local), which of them
/// are entities now (the entity layer draws those), and the traffic-light state per approach lane. A managed world singleton written by
/// <see cref="TrafficMirrorSystem"/> each frame, so every map layer reads the world, never the traffic pack (T3: one display path).
/// <para>⛔ Never persisted or replayed: it is re-derived every frame.</para>
/// </summary>
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
[ComponentId(GlobalComponentIds.TrafficFrame)]
public sealed class TrafficFrame
{
    public TrafficFrame(IReadOnlyList<TrafficCarPose> cars, Func<TrafficCarPose, bool> isEntity, IReadOnlyDictionary<int, byte> lights)
    {
        Cars = cars; IsEntity = isEntity; Lights = lights;
    }

    public IReadOnlyList<TrafficCarPose> Cars { get; }

    /// <summary>True for a car that is a replica entity this frame.</summary>
    public Func<TrafficCarPose, bool> IsEntity { get; }

    /// <summary>SUMO signal per lane handle (the net is the same for every sender): <c>'G'</c>/<c>'g'</c> green, <c>'y'</c> yellow,
    /// <c>'r'</c> red.</summary>
    public IReadOnlyDictionary<int, byte> Lights { get; }

    /// <summary>The frame resident in <paramref name="world"/>, or null when no SUMO wire is being mirrored.</summary>
    public static TrafficFrame? Of(EntityRepository world)
        => world.HasSingletonManaged<TrafficFrame>() ? world.GetSingletonManaged<TrafficFrame>() : null;

    /// <summary>A SUMO signal letter as a lamp colour class: 2 green, 1 yellow, 0 red, -1 off/unknown.</summary>
    public static int LampOf(byte signal) => (char)signal switch
    {
        'G' or 'g' => 2,
        'y' or 'Y' => 1,
        'r' or 'R' or 'u' => 0,
        _ => -1,
    };
}
