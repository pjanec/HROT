#nullable enable
using System.Diagnostics;
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
    // ── Diagnostics logger ───────────────────────────────────────────────────
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    // ── Sub-step Stopwatches (reused, no per-frame alloc) ────────────────────
    private readonly Stopwatch _lifecycleSw       = new();
    private readonly Stopwatch _vehicleNavIntentSw = new();
    private readonly Stopwatch _charMotorSw        = new();
    private readonly Stopwatch _vehicleMotorSw     = new();
    private readonly Stopwatch _reverseSyncSw      = new();

    // ── Accumulators for avg/max over the throttle window ────────────────────
    private double _accLifecycle,       _maxLifecycle;
    private double _accVehicleNavIntent, _maxVehicleNavIntent;
    private double _accCharMotor,       _maxCharMotor;
    private double _accVehicleMotor,    _maxVehicleMotor;
    private double _accReverseSync,     _maxReverseSync;
    private int    _bracketFrameCount;

    /// <summary>Log one breakdown line per this many frames (~1 s at 60 fps).</summary>
    private const int BracketLogIntervalFrames = 60;

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
    /// <param name="simRunning">
    /// When <see langword="true"/> (default), the sim is in Continuous (preview/running) mode and
    /// the motors advance normally. When <see langword="false"/> (edit/paused mode), the
    /// VehicleNavIntent and motors are gated so bodies are frozen; lifecycle and reverse-sync
    /// ALWAYS run regardless of this flag (drag/reposition must keep working while paused).
    /// </param>
    public void RunPreKernelStep(EntityRepository world, float dt, bool simRunning = true)
    {
        // Step 2: Physics body lifecycle — create/destroy bodies before motors.
        // Guard matches the original: only when a real physics service is active.
        // ALWAYS runs regardless of simRunning (drag/reposition must work while paused).
        _lifecycleSw.Restart();
        if (PhysicsIsActive)
            PhysicsBodyLifecycle?.Execute(world, dt);
        _lifecycleSw.Stop();

        // Step 2b: Pre-physics motors (VehicleNavIntent first — STR-D21 F7 fix, then motors).
        // VehicleNavIntent is gated on simRunning: when paused, don't advance navigation/steering.
        _vehicleNavIntentSw.Restart();
        if (simRunning) VehicleNavIntentSystem?.Execute(world, dt);
        _vehicleNavIntentSw.Stop();

        _charMotorSw.Restart();
        CharacterMotor?.Execute(world, dt, simRunning);
        _charMotorSw.Stop();

        _vehicleMotorSw.Restart();
        VehicleMotor?.Execute(world, dt, simRunning);
        _vehicleMotorSw.Stop();

        // Step 3: Reverse-sync BEFORE kernel tick — writes Bullet pose+velocity into
        // SimTransform/SimVelocity for owned entities (design §8.3).
        _reverseSyncSw.Restart();
        ReverseSyncGroup.Execute(world, dt);
        _reverseSyncSw.Stop();

        // ── Throttled breakdown log (~once per second at 60 fps) ─────────────
        double msLifecycle       = _lifecycleSw.Elapsed.TotalMilliseconds;
        double msVehicleNavIntent = _vehicleNavIntentSw.Elapsed.TotalMilliseconds;
        double msCharMotor       = _charMotorSw.Elapsed.TotalMilliseconds;
        double msVehicleMotor    = _vehicleMotorSw.Elapsed.TotalMilliseconds;
        double msReverseSync     = _reverseSyncSw.Elapsed.TotalMilliseconds;

        _accLifecycle       += msLifecycle;
        _accVehicleNavIntent += msVehicleNavIntent;
        _accCharMotor       += msCharMotor;
        _accVehicleMotor    += msVehicleMotor;
        _accReverseSync     += msReverseSync;

        if (msLifecycle        > _maxLifecycle)        _maxLifecycle        = msLifecycle;
        if (msVehicleNavIntent > _maxVehicleNavIntent) _maxVehicleNavIntent = msVehicleNavIntent;
        if (msCharMotor        > _maxCharMotor)        _maxCharMotor        = msCharMotor;
        if (msVehicleMotor     > _maxVehicleMotor)     _maxVehicleMotor     = msVehicleMotor;
        if (msReverseSync      > _maxReverseSync)      _maxReverseSync      = msReverseSync;

        if (++_bracketFrameCount >= BracketLogIntervalFrames)
        {
            double n = _bracketFrameCount;
            Log.Info(
                "[Bracket breakdown] avg/{0}f — " +
                "Lifecycle={1:F1} VehicleNavIntent={2:F1} CharMotor={3:F1} VehicleMotor={4:F1} ReverseSync={5:F1}  " +
                "(max: Lifecycle={6:F1} VehicleNavIntent={7:F1} CharMotor={8:F1} VehicleMotor={9:F1} ReverseSync={10:F1})  (all ms)",
                _bracketFrameCount,
                _accLifecycle        / n,
                _accVehicleNavIntent / n,
                _accCharMotor        / n,
                _accVehicleMotor     / n,
                _accReverseSync      / n,
                _maxLifecycle,
                _maxVehicleNavIntent,
                _maxCharMotor,
                _maxVehicleMotor,
                _maxReverseSync);

            _accLifecycle = _accVehicleNavIntent = _accCharMotor =
            _accVehicleMotor = _accReverseSync = 0;
            _maxLifecycle = _maxVehicleNavIntent = _maxCharMotor =
            _maxVehicleMotor = _maxReverseSync = 0;
            _bracketFrameCount = 0;
        }
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
