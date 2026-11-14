using Fdp.Core;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 — what SUMO looks like THIS frame, for the maps: every car from every sender (dead-reckoned, terrain-local), which of them
/// are entities now (the entity layer draws those), and the traffic-light state per approach lane. A managed world singleton written by
/// <see cref="TrafficMirrorSystem"/> each frame, so every map layer reads the world, never the traffic pack (T3: one display path).
/// <para>⛔ Never persisted or replayed: it is re-derived every frame.</para>
/// <para>⭐⭐ N-9 (CE-4471; CLAUDE.md "NEW COMPONENTS") — the frame OWNS its data: its own car / pedestrian lists, each row's "is an entity
/// now" flag (resolved by the writer when it fills the frame — ⛔ it used to hold the writer's <c>Func</c>s and live lists, so a reader called
/// into the traffic module's replica table) and its own copy of the light states. The ONE writer refills it in place every frame (a new frame
/// per frame would allocate every frame, R-2084) and re-sets it, so its version moves. ⭐ It is <see cref="DataPolicy.SnapshotViaClone"/> +
/// <see cref="IValueCopy{T}"/>: wherever a table copy would share the reference with another world (a snapshot; the threaded node's staging
/// copy to the presentation world) it is copied INTO that world's own frame — so the drawing thread never reads the instance the sim refills.</para>
/// </summary>
// ⭐ G-7 / CE-4354 NodeLocal (DESIGN_Review_Perspective §6.4 I9): a per-node traffic frame scratch — never copied into a merged view.
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay | DataPolicy.NodeLocal | DataPolicy.SnapshotViaClone)]
[ComponentId(GlobalComponentIds.TrafficFrame)]
public sealed class TrafficFrame : IValueCopy<TrafficFrame>
{
    private readonly List<TrafficCarPose> _cars = new();
    private readonly List<bool> _carIsEntity = new();
    private readonly List<TrafficPedPose> _peds = new();
    private readonly List<bool> _pedIsEntity = new();
    private readonly Dictionary<int, byte> _lights = new();

    /// <summary>An empty frame (the writer fills it with <see cref="Fill"/>).</summary>
    public TrafficFrame() { }

    /// <summary>A frame holding <paramref name="cars"/> etc. (the predicates are applied now, once per row).</summary>
    public TrafficFrame(IReadOnlyList<TrafficCarPose> cars, Func<TrafficCarPose, bool> isEntity, IReadOnlyDictionary<int, byte> lights,
                        IReadOnlyList<TrafficPedPose>? pedestrians = null, Func<TrafficPedPose, bool>? isPedEntity = null)
        => Fill(cars, isEntity, lights, pedestrians, isPedEntity);

    /// <summary>This frame's cars, terrain-local.</summary>
    public IReadOnlyList<TrafficCarPose> Cars => _cars;

    /// <summary>⭐ AQ93 §15 K3 — every sender's pedestrians this frame, terrain-local.</summary>
    public IReadOnlyList<TrafficPedPose> Pedestrians => _peds;

    /// <summary>SUMO signal per lane handle (the net is the same for every sender): <c>'G'</c>/<c>'g'</c> green, <c>'y'</c> yellow,
    /// <c>'r'</c> red.</summary>
    public IReadOnlyDictionary<int, byte> Lights => _lights;

    /// <summary>True when car <paramref name="i"/> (of <see cref="Cars"/>) is a replica entity this frame — the entity layer draws it.</summary>
    public bool IsEntityAt(int i) => _carIsEntity[i];

    /// <summary>True when pedestrian <paramref name="i"/> (of <see cref="Pedestrians"/>) is a replica entity this frame.</summary>
    public bool IsPedEntityAt(int i) => _pedIsEntity[i];

    /// <summary>The writer: refills this frame in place (no allocation once the lists have grown).</summary>
    public void Fill(IReadOnlyList<TrafficCarPose> cars, Func<TrafficCarPose, bool> isEntity, IReadOnlyDictionary<int, byte> lights,
                     IReadOnlyList<TrafficPedPose>? pedestrians = null, Func<TrafficPedPose, bool>? isPedEntity = null)
    {
        _cars.Clear(); _carIsEntity.Clear();
        for (int i = 0; i < cars.Count; i++) { var c = cars[i]; _cars.Add(c); _carIsEntity.Add(isEntity(c)); }
        _peds.Clear(); _pedIsEntity.Clear();
        if (pedestrians != null)
            for (int i = 0; i < pedestrians.Count; i++) { var p = pedestrians[i]; _peds.Add(p); _pedIsEntity.Add(isPedEntity?.Invoke(p) ?? false); }
        _lights.Clear();
        foreach (var kv in lights) _lights[kv.Key] = kv.Value;
    }

    /// <inheritdoc/>
    public TrafficFrame CloneValue()
    {
        var copy = new TrafficFrame();
        copy.CopyFrom(this);
        return copy;
    }

    /// <inheritdoc/>
    public void CopyFrom(TrafficFrame source)
    {
        _cars.Clear(); _cars.AddRange(source._cars);
        _carIsEntity.Clear(); _carIsEntity.AddRange(source._carIsEntity);
        _peds.Clear(); _peds.AddRange(source._peds);
        _pedIsEntity.Clear(); _pedIsEntity.AddRange(source._pedIsEntity);
        _lights.Clear();
        foreach (var kv in source._lights) _lights[kv.Key] = kv.Value;
    }

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
