#nullable enable
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;

namespace Hrot.Stride.Core;

/// <summary>
/// <b>StridePhysicsBracket</b> — cohesive host-driven physics bracket for the Stride muscle
/// (STR-P1, BATCH refactor).
///
/// <para>
/// Encapsulates all host-driven muscle steps that run <em>around</em>
/// <see cref="Fdp.ModuleHost.ModuleHostKernel.Update()"/> because Bullet is stepped by
/// Stride's external loop.  A caller drives this bracket by calling
/// <see cref="RunPreKernelStep"/> immediately before the kernel tick and
/// <see cref="RunPostKernelStep"/> immediately after.
/// </para>
///
/// <para>
/// <b>Pre-kernel step order (identical to EditorStrideSubsystem.Tick steps 2, 2b, 3):</b>
/// <list type="number">
///   <item><see cref="PhysicsBodyLifecycle"/>.<c>Execute</c> — create/destroy Bullet bodies
///     (only when <see cref="PhysicsIsActive"/>).</item>
///   <item><see cref="VehicleNavIntentSystem"/>.<c>Execute</c> — write <c>VehicleState</c>
///     before the motor so the motor sees a fresh value this frame (STR-D21).</item>
///   <item><see cref="CharacterMotor"/>.<c>Execute</c> — push character intents into the
///     physics service.</item>
///   <item><see cref="VehicleMotor"/>.<c>Execute</c> — push vehicle commands into the
///     physics service.</item>
///   <item><see cref="ReverseSyncGroup"/>.<c>Execute</c> — write Bullet-resolved pose+velocity
///     into <c>SimTransform</c>/<c>SimVelocity</c> BEFORE <c>Kernel.Update()</c> so FDP
///     Simulation-phase consumers read post-physics data the same frame (design §8.3).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Post-kernel step order (identical to EditorStrideSubsystem.Tick step 5):</b>
/// <list type="number">
///   <item><see cref="SplitSync"/>.<c>Sync</c> — reconcile Stride visual entity set (Pass A)
///     and forward-sync non-owned entities (Pass B).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this bracket does NOT own</b> (stays in the host):
/// orchestration pump, <c>TimeController.Step</c>/<c>Kernel.Update</c>, animation bridge,
/// animation binder, gizmo renderer, selection highlight.
/// </para>
/// </summary>
public sealed class StridePhysicsBracket
{
    /// <summary>True only when a real (non-NoOp) physics service was supplied.</summary>
    public bool PhysicsIsActive { get; }

    /// <summary>
    /// The physics body lifecycle system (creates/destroys bodies on authority change).
    /// May be null when no visual factory was provided (headless runs without a GPU).
    /// </summary>
    public PhysicsBodyLifecycleSystem? PhysicsBodyLifecycle { get; }

    /// <summary>
    /// The Bullet character motor. May be null in headless mode (no lifecycle system).
    /// </summary>
    public BulletCharacterMotor? CharacterMotor { get; }

    /// <summary>
    /// The kinematic vehicle motor. May be null in headless mode (no lifecycle system).
    /// </summary>
    public KinematicVehicleMotor? VehicleMotor { get; }

    /// <summary>
    /// The vehicle navigation intent system executed PRE-kernel (STR-D21 fix).
    /// May be null; set by the host after construction.
    /// </summary>
    public VehicleNavigationIntentSystem? VehicleNavIntentSystem { get; set; }

    /// <summary>
    /// The togglable group wrapping <see cref="BulletReverseSyncSystem"/>.
    /// Always non-null — created empty when no lifecycle system is present so the
    /// replay handler (P5) still has a toggle to sever.
    /// </summary>
    public TogglablePostSimulationGroup ReverseSyncGroup { get; }

    /// <summary>
    /// The split-authority forward-sync script.
    /// May be null in headless mode (no visual factory).
    /// </summary>
    public SplitAuthorityStrideSyncScript? SplitSync { get; }

    /// <summary>
    /// Constructs the physics bracket from its constituent parts.
    /// All parameters except <paramref name="reverseSyncGroup"/> may be null for headless runs.
    /// </summary>
    /// <param name="physicsIsActive">
    /// True when a real (non-NoOp) physics service was supplied.
    /// When false, <see cref="RunPreKernelStep"/> skips the lifecycle call.
    /// </param>
    /// <param name="physicsBodyLifecycle">
    /// The lifecycle system; null in headless mode (no visual factory).
    /// </param>
    /// <param name="characterMotor">
    /// The Bullet character motor; null when no lifecycle system is available.
    /// </param>
    /// <param name="vehicleMotor">
    /// The kinematic vehicle motor; null when no lifecycle system is available.
    /// </param>
    /// <param name="reverseSyncGroup">
    /// The always-present togglable group wrapping <see cref="BulletReverseSyncSystem"/>
    /// (or an empty group in headless mode).
    /// </param>
    /// <param name="splitSync">
    /// The split-authority forward-sync script; null in headless mode.
    /// </param>
    public StridePhysicsBracket(
        bool                          physicsIsActive,
        PhysicsBodyLifecycleSystem?   physicsBodyLifecycle,
        BulletCharacterMotor?         characterMotor,
        KinematicVehicleMotor?        vehicleMotor,
        TogglablePostSimulationGroup  reverseSyncGroup,
        SplitAuthorityStrideSyncScript? splitSync)
    {
        PhysicsIsActive      = physicsIsActive;
        PhysicsBodyLifecycle = physicsBodyLifecycle;
        CharacterMotor       = characterMotor;
        VehicleMotor         = vehicleMotor;
        ReverseSyncGroup     = reverseSyncGroup;
        SplitSync            = splitSync;
    }

    /// <summary>
    /// Runs all host-driven muscle steps that must execute BEFORE <c>Kernel.Update()</c>
    /// (EditorStrideSubsystem.Tick steps 2, 2b, 3 — preserved verbatim).
    /// </summary>
    /// <param name="world">The ECS world / entity repository.</param>
    /// <param name="dt">Simulation delta-time in seconds.</param>
    public void RunPreKernelStep(EntityRepository world, float dt)
    {
        // Step 2: Physics body lifecycle — create/destroy bodies before motors.
        // Guard matches the original: only when a real physics service is active.
        if (PhysicsIsActive)
            PhysicsBodyLifecycle?.Execute(world, dt);

        // Step 2b: Pre-physics motors (VehicleNavIntent first — STR-D21 F7 fix, then motors).
        VehicleNavIntentSystem?.Execute(world, dt);
        CharacterMotor?.Execute(world, dt);
        VehicleMotor?.Execute(world, dt);

        // Step 3: Reverse-sync BEFORE kernel tick — writes Bullet pose+velocity into
        // SimTransform/SimVelocity for owned entities (design §8.3).
        ReverseSyncGroup.Execute(world, dt);
    }

    /// <summary>
    /// Runs the host-driven muscle step that must execute AFTER <c>Kernel.Update()</c>
    /// (EditorStrideSubsystem.Tick step 5 — preserved verbatim).
    /// </summary>
    /// <param name="world">The ECS world / entity repository.</param>
    public void RunPostKernelStep(EntityRepository world)
    {
        // Step 5: Split-authority forward-sync — Pass A: visual existence; Pass B: non-owned.
        // Fallback branch (no SplitSync) is intentionally a no-op: headless mode has no visuals.
        SplitSync?.Sync(world);
    }
}
