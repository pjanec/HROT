using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Systems;

/// <summary>
/// Holds a usable position for entities this node does <b>not</b> own, between the sparse samples
/// their owner publishes.
///
/// <para>
/// This is not a rendering nicety — it is what lets the network publish sparsely at all. Every node
/// that carries replicas owes it, which is why it is registered from the role-independent path on
/// both replication stacks rather than from a presentation role.
/// See <c>docs/DESIGN_Dead_Reckoning.md</c>.
/// </para>
///
/// <para>
/// <b>Each frame, per non-owned entity:</b> age the last received sample by
/// <c>simNow - NetworkTransform.SimStamp</c>, project it forward along <c>NetworkVelocity</c>, and
/// blend <c>SimTransform</c> toward that target. The target is recomputed fresh from the sample
/// every frame, so it stays a pure function of replicated data: two nodes at different frame rates
/// agree for the same <c>simNow</c>, and there is no accumulator to diverge. <c>NetworkTransform</c>
/// is read-only here (rule R2).
/// </para>
///
/// <para>
/// <b>Pausing needs no special case.</b> Both the stamp and <c>simNow</c> are simulation time, which
/// does not advance while the cluster is paused or stepped — so the age freezes and the projection
/// stops moving on its own. Note this deliberately avoids <c>GlobalTime.IsPaused</c>, which is
/// <c>TimeScale == 0</c> and is <b>false</b> while paused (a pause switches to stepping with a zero
/// delta); the honest predicate elsewhere is <c>IsAdvancing</c>, and here it is not needed at all.
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class DeadReckoningSyncSystem : IEcsModuleSystem
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Default blend rate, in units of "fraction of the remaining gap per second".</summary>
    public const float DefaultSmoothingRate = 10.0f;

    /// <summary>
    /// How fast <c>SimTransform</c> is pulled toward the projected target. Per-host rather than
    /// per-role: a node rendering at a high frame rate may legitimately want to smooth harder than a
    /// headless one, and that is a property of the host, not of what the node is for.
    /// </summary>
    public float SmoothingRate { get; }

    /// <summary>
    /// When <c>true</c>, dead reckoning runs on every entity without local authority. When
    /// <c>false</c>, it is narrowed to entities in <c>EntityLifecycle.Ghost</c>, so that on a node
    /// which also owns entities locally it cannot fight the local kinematics.
    ///
    /// <para>
    /// The caller derives this from <b>ownership</b> — <c>!roleHasMuscle &amp;&amp; !roleHasBrain</c>,
    /// i.e. "this node owns nothing" — never from a presentation role.
    /// </para>
    /// </summary>
    public bool DriveFromNetwork { get; }

    /// <summary>Parameterless constructor — backward-compatible; <see cref="DriveFromNetwork"/> defaults to <c>true</c>.</summary>
    public DeadReckoningSyncSystem() : this(driveFromNetwork: true) { }

    /// <param name="driveFromNetwork">
    /// Pass <c>false</c> when the node also owns entities locally, so DR only processes ghosts.
    /// </param>
    /// <param name="smoothingRate">Blend rate; defaults to <see cref="DefaultSmoothingRate"/>.</param>
    public DeadReckoningSyncSystem(bool driveFromNetwork, float smoothingRate = DefaultSmoothingRate)
    {
        DriveFromNetwork = driveFromNetwork;
        SmoothingRate    = smoothingRate;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        var queryBuilder = view.Query()
            .With<SimTransform>()
            .With<NetworkTransform>()
            .With<NetworkVelocity>()
            .With<NetworkAuthority>();

        if (!DriveFromNetwork)
            queryBuilder = queryBuilder.WithLifecycle(EntityLifecycle.Ghost);

        var query = queryBuilder.Build();

        var cmd = view.GetCommandBuffer();

        // Cluster-synced simulation time for THIS frame, read from the singleton the kernel pushed.
        // Asking a time controller instead would return a state built with a zero delta.
        double simNow = SimClock.Of(view).TotalTime;

        foreach (var entity in query)
        {
            ref readonly var authority = ref view.GetComponentRO<NetworkAuthority>(entity);
            if (authority.HasAuthority)
                continue;

            ref readonly var netTf  = ref view.GetComponentRO<NetworkTransform>(entity);
            ref readonly var netVel = ref view.GetComponentRO<NetworkVelocity>(entity);
            ref readonly var simTf  = ref view.GetComponentRO<SimTransform>(entity);

            // Age of the last received sample. Clamped at zero: a stamp ahead of local sim time
            // means the publisher is ahead of us, and extrapolating backwards would rewind the
            // entity rather than hold it still.
            float age = (float)(simNow - netTf.SimStamp);
            if (age < 0f)
                age = 0f;

            // Recomputed from the sample every frame — never from the previous projection.
            var projectedNetPos = netTf.LastPosition + (netVel.Value * age);

            var blendedPos = Vector3.Lerp(simTf.Position, projectedNetPos, deltaTime * SmoothingRate);
            cmd.SetComponent(entity, new SimTransform
            {
                Position = blendedPos,
                Rotation = simTf.Rotation
            });

            cmd.SetComponent(entity, new SimVelocity { Linear = netVel.Value });
        }
    }
}
