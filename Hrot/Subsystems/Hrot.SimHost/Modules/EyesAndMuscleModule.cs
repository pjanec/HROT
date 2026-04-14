using System.Numerics;
using System.Threading;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using Hrot.Common;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Modules;

/// <summary>
/// Async Separation-of-Duties (SoD) PoC module that combines Eyes (IG rendering data)
/// and Muscle (ground kinematics) into a single background-threaded module.
///
/// <para><b>Execution policy:</b> <see cref="ExecutionPolicy.SlowBackground(int)"/> at 60 Hz
/// — runs asynchronously on a background thread reading a SoD snapshot, so the main thread
/// is never blocked.</para>
///
/// <para><b>Eyes:</b> Iterates all entities with <see cref="SimTransform"/> +
/// <see cref="NetworkIdentity"/> and increments <see cref="EyesTicks"/>. In a production
/// implementation, this data would be pushed to a Stride data bridge.</para>
///
/// <para><b>Muscle:</b> Iterates entities with <see cref="NavigationIntent"/> +
/// <see cref="SimTransform"/> and steps them toward their <c>DirectPoint</c> destination,
/// writing back via <see cref="IEntityCommandBuffer"/>. Increments <see cref="MuscleTicks"/>
/// only when the node role includes <see cref="NodeRole.MuscleGround"/>.</para>
/// </summary>
public sealed class EyesAndMuscleModule : IEcsModule
{
    // ── IEcsModule identity ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => "EyesAndMuscle";

    /// <inheritdoc/>
    /// <remarks>Async SoD at 60 Hz — background thread, snapshot data.</remarks>
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(60);

    // ── Role state ─────────────────────────────────────────────────────────────

    private readonly NodeRole _role;
    private readonly bool _muscleActive;

    // ── Test seams ─────────────────────────────────────────────────────────────

    /// <summary>Incremented every <see cref="Tick"/> call (both roles).</summary>
    public int EyesTicks { get; private set; }

    /// <summary>Incremented every <see cref="Tick"/> call when <see cref="NodeRole.MuscleGround"/> is active.</summary>
    public int MuscleTicks { get; private set; }

    /// <summary>
    /// <c>ManagedThreadId</c> of the thread that last called <see cref="Tick"/>.
    /// <c>null</c> until the first tick. Use in tests to assert async execution on a
    /// non-main thread.
    /// </summary>
    public int? LastTickThreadId { get; private set; }

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the module for the given node role.
    /// </summary>
    /// <param name="role">
    /// When this contains <see cref="NodeRole.MuscleGround"/> or equals
    /// <see cref="NodeRole.AllInOne"/>, the Muscle path is activated.
    /// </param>
    public EyesAndMuscleModule(NodeRole role)
    {
        _role         = role;
        _muscleActive = role.HasFlag(NodeRole.MuscleGround);
    }

    // ── IEcsModule ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>Direct Execution pattern — all logic in <see cref="Tick"/>.</remarks>
    public void RegisterSystems(ISystemRegistry registry) { }

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float deltaTime)
    {
        LastTickThreadId = Thread.CurrentThread.ManagedThreadId;

        // ── THE EYES — always runs ─────────────────────────────────────────────
        // Iterate all spatially-networked entities and (in production) push their
        // transforms to the Stride rendering data bridge.
        var eyesQuery = view.Query()
            .With<SimTransform>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in eyesQuery)
        {
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            // PoC: in Stride, push tf.Position / tf.Rotation to StrideDataBridge here.
            _ = tf;
        }
        EyesTicks++;

        if (!_muscleActive) return;

        // ── THE MUSCLE — only when MuscleGround role is active ────────────────
        // Step entities with an active DirectPoint navigation intent toward their destination.
        var cmd         = view.GetCommandBuffer();
        var muscleQuery = view.Query()
            .With<NavigationIntent>()
            .With<SimTransform>()
            .Build();

        foreach (var entity in muscleQuery)
        {
            ref readonly var intent = ref view.GetComponentRO<NavigationIntent>(entity);
            ref readonly var tf     = ref view.GetComponentRO<SimTransform>(entity);

            // Simplified step toward destination (DirectPoint mode only)
            if (intent.Mode == NavigationMode.DirectPoint)
            {
                // FinalDestination is in 2-D XY ground plane; grow to 3-D at entity height.
                var dest3d = new Vector3(intent.FinalDestination.X, tf.Position.Y, intent.FinalDestination.Y);
                var delta  = dest3d - tf.Position;
                if (delta.Length() > 0.01f)
                {
                    var step   = Vector3.Normalize(delta) * (deltaTime * 5.0f);
                    var newPos = tf.Position + step;
                    cmd.SetComponent(entity, new SimTransform { Position = newPos, Rotation = tf.Rotation });
                }
            }
        }
        MuscleTicks++;
    }
}
