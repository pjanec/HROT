#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using Hrot.Stride.Core.TestHarness;
using Hrot.NodeComposition;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Input;
using Stride.Physics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Graphics.GeometricPrimitives;
using Stride.Extensions;
using Stride.Rendering.Lights;
using Fdp.ModuleHost.Time; // BATCH-S2-AD

namespace HrotStrideApp;

/// <summary>
/// The Stride <see cref="Game"/> subclass for the <c>editor_stride</c> process.
///
/// <para>
/// <b>BATCH-10 — live bring-up:</b> This game class now boots <see cref="EditorStrideSubsystem"/>
/// and drives it on every frame via <see cref="Update(GameTime)"/>. The subsystem is initialized
/// in <see cref="BeginRun"/> where both <c>Content</c> and <c>SceneSystem.SceneInstance.RootScene</c>
/// are guaranteed valid (Stride's internal LoadContent has already run).
/// </para>
///
/// <para>
/// Two operating modes (backward-compatible):
/// <list type="bullet">
///   <item><b>Internal loop (BATCH-10):</b> call <c>game.Run()</c> without arguments;
///     <see cref="Update(GameTime)"/> drives <see cref="EditorStrideSubsystem.Tick(float)"/>
///     via the fixed-timestep accumulator.</item>
///   <item><b>External loop (pre-BATCH-10, P5):</b> call <see cref="Tick(float)"/> explicitly
///     from a host-managed RunCallback; <see cref="AttachBootstrapper"/> wires a
///     <see cref="StrideNodeBootstrapper"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Driven from an <b>external host loop</b> via <see cref="Tick"/> rather than
/// Stride's internal loop.  The caller (e.g. <c>Program.cs</c>) is responsible
/// for pumping OS events and calling <see cref="Tick"/> once per frame.
/// </para>
///
/// <para>
/// Stride 4.2 external-loop pattern (VERIFIED):
/// <list type="bullet">
///   <item>Pass a <see cref="GameContext"/> with <c>IsUserManagingRun = true</c>
///     and a <c>RunCallback</c> to <see cref="Run(GameContext?)"/>. This instructs
///     the internal loop to call the callback instead of taking control itself.</item>
///   <item>Inside the <c>RunCallback</c> the host loop calls <see cref="Tick"/>
///     (which delegates to <see cref="GameBase.Tick"/>) each iteration.</item>
///   <item>The internal <c>ThreadThrottler</c> that backs
///     <see cref="GameBase.WindowMinimumUpdateRate"/> is set to zero-delay so
///     <c>Tick</c> returns immediately without sleeping — the host loop governs
///     cadence instead.</item>
/// </list>
/// </para>
///
/// <para>
/// SDL2 OS event pump (VERIFIED): <see cref="Stride.Games.SDLMessageLoop.NextFrame"/>
/// is the SDL2 analog of <c>Application.DoEvents()</c>.  The external host loop
/// must call it once per iteration to drain the SDL2 event queue and keep the window
/// alive.  This class does NOT call it itself; the composition root (<c>Program.cs</c>)
/// drives the loop with its own <c>SDLMessageLoop</c> / <c>RunCallback</c>.
/// </para>
///
/// <para>
/// <b>GPU/window verification note:</b> full construction and render advancement of
/// <see cref="StrideHrotGame"/> requires a <see cref="Stride.Graphics.GraphicsDevice"/>
/// and cannot be tested headlessly.  Verification is deferred to the T8 end-to-end
/// smoke (BATCH-03).  The deterministic fixed-timestep clock is unit-tested separately
/// via <see cref="StrideHostLoopDriver"/> which has no GPU dependency.
/// </para>
/// </summary>
public sealed class StrideHrotGame : Game
{
    // ── Logging ───────────────────────────────────────────────────────────
    // Per-class NLog logger (idiomatic HROT pattern: GetCurrentClassLogger()).
    // The per-second diagnostics dump and boot warnings go through this so they
    // land in <BaseDirectory>/logs/editor_stride.log (the GPU-path debug channel).
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    // ── The FDP simulation node that this game drives ─────────────────────

    private StrideNodeBootstrapper? _bootstrapper;

    /// <summary>
    /// ⭐⭐ <c>CE-207</c> — when true this process boots as a MODE 2 cluster node (Muscle + Perception
    /// beside CGF) instead of the mode 1 hosted editor. Set by the composition root before Run().
    /// </summary>
    public bool NodeMode { get; set; }

    /// <summary>DDS domain for mode 2. Ignored in mode 1.</summary>
    public int NodeDomainId { get; set; }

    /// <summary>Cluster node id for mode 2. Ignored in mode 1.</summary>
    public int NodeId { get; set; } = StrideNodeShell.DefaultNodeId;

    private StrideNodeShell? _nodeShell;

    /// <summary>
    /// Exposes the bootstrapper for test/diagnostic inspection.
    /// Valid after <see cref="AttachBootstrapper"/> is called, which must happen
    /// before <see cref="Tick"/> is invoked for the first time.
    /// </summary>
    public StrideNodeBootstrapper? Bootstrapper => _bootstrapper;

    // ── Fixed-timestep driver ─────────────────────────────────────────────

    private readonly StrideHostLoopDriver _loopDriver;

    /// <summary>Exposes the fixed-timestep driver (readonly).</summary>
    public StrideHostLoopDriver LoopDriver => _loopDriver;

    // ── BATCH-10: EditorStrideSubsystem live bring-up ─────────────────────

    /// <summary>
    /// The EditorStrideSubsystem booted in <see cref="BeginRun"/>.
    /// Null until the first call to <see cref="BeginRun"/>.
    /// </summary>
    private EditorStrideSubsystem? _editorSubsystem;

    /// <summary>Guard so <see cref="BeginRun"/> only runs once.</summary>
    private bool _editorSubsystemBooted;

    // ── BATCH-18: baked navmesh provider ─────────────────────────────────

    /// <summary>
    /// The DotRecast navmesh provider baked from the arena's static colliders
    /// (BATCH-18, STR-D19). Null if baking failed or PhysicsProcessor was unavailable.
    /// Set in <see cref="BakeNavmesh"/> and exposed to the F4/F5 harness cases.
    /// </summary>
    private DotRecastNavmeshProvider? _navmeshProvider;

    // ── BATCH-19: Infantry crowd provider ────────────────────────────────

    /// <summary>
    /// True if the Infantry navmesh was successfully supplied to
    /// <see cref="EditorStrideSubsystem.InfantryCrowdProvider"/> during
    /// <see cref="BakeNavmesh"/> (BATCH-19, STR-D19 discharge).
    /// Used by the F5 "Navmesh Walk" harness case to decide whether to proceed.
    /// </summary>
    private bool _infantryCrowdProviderInitialized;

    // ── BATCH-12: in-app test harness ─────────────────────────────────────

    /// <summary>
    /// The in-app manual test harness (BATCH-12, STR-TEST-1). Constructed in
    /// <see cref="BootEditorSubsystem"/> after the subsystem boots; driven per-frame from
    /// <see cref="Update(GameTime)"/>. Null until then (and in any path that fails to boot
    /// the editor subsystem).
    /// </summary>
    private StrideTestHarness? _testHarness;

    // ── BATCH-22: optional second raylib/ImGui inspector window (STR-P5-T2) ──

    /// <summary>
    /// The optional second OS window (raylib/ImGui) showing the FDP entity list and
    /// a basic inspector (BATCH-22, STR-P5-T2).
    ///
    /// <para>
    /// Enabled by setting the <c>STRIDE_EDITOR_WINDOW=1</c> environment variable before
    /// launching the app, or by setting <see cref="StrideInspectorWindowConfig.ForceEnabled"/>
    /// to <c>true</c>.  Null when disabled (default) so headless tests and CI are unaffected.
    /// </para>
    ///
    /// <para>
    /// Opened in <see cref="BootEditorSubsystem"/> after the subsystem is live; pumped once
    /// per render frame in <see cref="Update(GameTime)"/>.  Closed and disposed in
    /// <see cref="EndRun"/> (via the Stride shutdown path).
    /// </para>
    /// </summary>
    private StrideInspectorWindow? _inspectorWindow;

    /// <summary>The overview camera entity created in <see cref="AddFixedCamera"/>.</summary>
    private global::Stride.Engine.Entity? _cameraEntity;

    private readonly CameraBookmarkStore _cameraBookmarks = new(); // BATCH-S2-AC

    /// <summary>Physics raycast service for 3D click-to-select / click-to-move (BATCH-S2-O).</summary>
    private IStrideRaycastService? _raycastService;

    // BATCH-S2-U: RMB click-vs-drag via accumulated mouse delta (camera orbit locks the cursor, so
    // down-vs-up position is unreliable; MouseDelta reports movement even when the cursor is locked).
    private bool  _rmbHeld;
    private float _rmbDragAccum;
    private const float RmbDragDeadzone = 0.01f; // accumulated normalized delta above which it's a drag

    // BATCH-S2-W: 3D drag-and-drop (LMB press selects + arms; LMB drag repositions along the ground).
    private Fdp.Core.Entity? _dragEntity;
    private bool  _dragging;
    private float _lmbDragAccum;
    private const float DragStartDeadzone = 0.01f; // accumulated normalized delta before a drag starts

    /// <summary>
    /// Frame counter used to throttle the spawn-diagnostics log in <see cref="Update"/>
    /// to roughly once per second (every <see cref="DiagnosticsLogIntervalFrames"/> frames).
    /// </summary>
    private int _diagnosticsFrameCounter;

    /// <summary>Throttle interval (in render frames) for the spawn-diagnostics log.</summary>
    private const int DiagnosticsLogIntervalFrames = 60;

    // ── Frame-timing diagnostics (DIAG-ONLY, no behaviour change) ────────
    // Reused Stopwatches — no per-frame alloc.
    // _frameDeltaSw: measures wall-clock time BETWEEN successive Update entries (true frame time).
    // _baseUpdateSw: measures duration of base.Update(gameTime) (Stride scene/physics pipeline).
    // _totalUpdateSw: measures the entire StrideHrotGame.Update body (base + our code).
    // _drawSw: measures base.Draw(gameTime) in the Draw override (3D render submission).
    private readonly Stopwatch _frameDeltaSw   = new Stopwatch();
    private readonly Stopwatch _baseUpdateSw   = new Stopwatch();
    private readonly Stopwatch _totalUpdateSw  = new Stopwatch();
    private readonly Stopwatch _drawSw         = new Stopwatch();

    // Running accumulators (reset every DiagnosticsLogIntervalFrames frames).
    private double _accumFrameDeltaMs;
    private double _maxFrameDeltaMs;
    private double _accumBaseUpdateMs;
    private double _maxBaseUpdateMs;
    private double _accumTotalUpdateMs;
    private double _accumDrawMs;
    private double _maxDrawMs;
    private int    _timingFrameCount;

    // ── Construction ──────────────────────────────────────────────────────

    /// <param name="fixedDt">
    /// Fixed simulation step in seconds (default 1/60 ≈ 16.7 ms).
    /// This is the FDP sim-kernel tick, independent of render rate.
    /// </param>
    public StrideHrotGame(float fixedDt = 1f / 60f)
    {
        // Defensive: ensure NLog file logging is configured even if this game is
        // constructed without going through the HrotStrideApp.Windows entry point
        // (e.g. a future host loop). Idempotent — the WinExe entry already calls it.
        StrideLogging.Configure();

        _loopDriver = new StrideHostLoopDriver(fixedDt);

        // Disable the internal throttler so Tick() returns immediately when called
        // from the external host loop.  The host loop governs frame cadence.
        //
        // WindowMinimumUpdateRate backs the ThreadThrottler that normally enforces a
        // minimum interval between ticks.  Setting it to zero disables any sleep.
        // (VERIFIED: Stride.Games.GameBase.WindowMinimumUpdateRate, Stride 4.2.1.2487)
        WindowMinimumUpdateRate.MinimumElapsedTime = TimeSpan.Zero;

        // BATCH-S2-AE: the raylib editor window's GLFW present already vsync-blocks ~16ms/frame;
        // if Stride's D3D present ALSO vsync-blocks we get two waits per frame (~32ms => ~20-31Hz).
        // When the editor window is active, disable Stride's vsync so the single GLFW present paces
        // the shared loop at ~60Hz.  When the editor window is OFF, keep Stride vsync ON (otherwise
        // the lone 3D window renders uncapped).
        // Set here in the constructor (device not yet created) — no ApplyChanges needed.
        // Uses the IDENTICAL condition as the _inspectorWindow creation gate in BootEditorSubsystem.
        if (StrideInspectorWindowConfig.IsEnabled)
        {
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
            Log.Info("[StrideHrotGame] Editor window active — disabling Stride D3D vsync (single GLFW pacer, BATCH-S2-AE).");
        }
    }

    // ── Bootstrapper injection ────────────────────────────────────────────

    /// <summary>
    /// Wires the FDP simulation node to this game.
    /// Must be called before the first <see cref="Tick"/> (typically in the
    /// composition root after both objects are constructed).
    /// </summary>
    public void AttachBootstrapper(StrideNodeBootstrapper bootstrapper)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
    }

    // ── External-loop tick ────────────────────────────────────────────────

    /// <summary>
    /// Advances one render frame and accumulates FDP simulation ticks.
    ///
    /// <para>Called by the external host loop once per iteration, <b>after</b>
    /// OS events have been pumped via <c>SDLMessageLoop.NextFrame()</c>.</para>
    ///
    /// <para>
    /// Delegates to <see cref="GameBase.Tick"/> for Stride's own Update/Draw cycle,
    /// then runs however many fixed-dt FDP simulation ticks the accumulator has
    /// banked via <see cref="StrideHostLoopDriver.AdvanceFrame"/>.
    /// </para>
    /// </summary>
    /// <param name="wallDelta">
    /// Wall-clock time elapsed since the last call (seconds).
    /// Pass 0 on the first call (or use a stopwatch delta).
    /// </param>
    public void Tick(float wallDelta)
    {
        // Advance Stride's own render/physics pipeline.
        // GameBase.Tick() calls Update() + Draw() on this frame.
        base.Tick();

        // Advance the FDP simulation for each accumulated fixed step.
        if (_bootstrapper != null)
        {
            _loopDriver.AdvanceFrame(wallDelta, dt => _bootstrapper.Tick(dt));
        }
    }

    // ── Stride overrides ──────────────────────────────────────────────────

    protected override void Initialize()
    {
        base.Initialize();
        // P0: no additional initialization; P1+ will attach physics bodies here.
    }

    /// <summary>
    /// Called by Stride when the game loop ends.  Disposes the inspector window if it
    /// is still open so the GLFW context is cleanly torn down before the process exits.
    /// </summary>
    protected override void EndRun()
    {
        // Close the second window before Stride tears down its own DirectX resources.
        // Raylib.CloseWindow() releases the GLFW context; safe to call even after
        // Stride's own cleanup has started (separate context).
        if (_inspectorWindow != null)
        {
            _inspectorWindow.Dispose();
            _inspectorWindow = null;
        }

        base.EndRun();
    }

    /// <summary>
    /// Called by Stride after all game systems are initialized and content is loaded
    /// (i.e. <c>Content</c> and <c>SceneSystem.SceneInstance.RootScene</c> are valid).
    ///
    /// <para>
    /// [VERIFY] Lifecycle hook choice: <c>BeginRun</c> is documented as
    /// "Called after all components are initialized, before the game loop starts"
    /// (Stride.Games.GameBase.BeginRun, Stride 4.2.1.2487).  In the Stride
    /// <c>Tick()</c> flow, <c>BeginRun()</c> fires after <c>LoadContent()</c> which
    /// in turn fires after all game systems' LoadContent (including SceneSystem's
    /// scene loading).  At the point <c>BeginRun</c> executes, the scene is loaded
    /// (asset database is populated, <c>Content.Load&lt;T&gt;</c> works) and
    /// <c>SceneSystem.SceneInstance.RootScene</c> is the active MainScene.
    /// Scripts attached to scene entities have NOT yet had their <c>Start()</c>
    /// called — that happens on the first <c>Update()</c> cycle.  This is the
    /// correct window to set up our subsystem: scene is ready, scripts haven't fired.
    /// </para>
    ///
    /// <para>
    /// Boots <see cref="EditorStrideSubsystem"/> with a concrete
    /// <see cref="StrideVisualFactory"/>, neutralizes the template player entity
    /// and its scripts (so ThirdPersonCamera doesn't crash looking for a player),
    /// adds a fixed overview camera, and enqueues the demo UrbanCombat spawns.
    /// </para>
    /// </summary>
    protected override void BeginRun()
    {
        base.BeginRun();

        if (_editorSubsystemBooted)
            return;
        _editorSubsystemBooted = true;

        // ⭐⭐ CE-207 / S4 — mode 2 boots a CLUSTER NODE instead of the hosted editor. Both modes share
        //   this Game, its scene setup and its brackets; only the composition root differs, which is
        //   what Q66 §3B means by "mirror the bracket rather than invent a composition root".
        if (NodeMode)
        {
            BootClusterNode();
            return;
        }

        BootEditorSubsystem();
    }

    /// <summary>
    /// Called each frame by Stride's internal game loop.
    /// Drives <see cref="EditorStrideSubsystem.Tick(float)"/> via the fixed-timestep
    /// accumulator (internal-loop mode, BATCH-10).
    /// Also drives the bootstrapper path if set (external-loop backward compat).
    /// </summary>
    protected override void Update(GameTime gameTime)
    {
        // ── Frame-delta timing (DIAG) ─────────────────────────────────────
        // Capture the elapsed time since the PREVIOUS Update call — this is the
        // true wall-clock frame time (includes GPU present, vsync wait, OS scheduling).
        // On the very first call _frameDeltaSw is not running; Elapsed returns zero.
        double frameDeltaMs = _frameDeltaSw.Elapsed.TotalMilliseconds;
        _frameDeltaSw.Restart();

        // ── Total StrideHrotGame.Update timing (DIAG) ─────────────────────
        _totalUpdateSw.Restart();

        // ⭐⭐⭐ CE-227 — STRIDE'S PHYSICS ADVANCES BY SIM SECONDS, NEVER BY THE FRAME TIME.
        //
        // 🔒 User ruling, 2026-09-08: "physics should run always just sometime with zero dt" and
        //    "no elapsed ticks of wall clock, always elapsed seconds sim time".
        //
        // 📐 Stride's physics game system does exactly one thing with time:
        //        scene.Simulation.Simulate((float)gameTime.WarpElapsed.TotalSeconds)
        //    and GameTime defines  WarpElapsed = Elapsed * Factor,  with Factor a public, clamped-to-
        //    non-negative multiplier whose own doc says it "controls how much the warped time flows,
        //    this includes physics, animations and particles".
        //    ⇒ setting  Factor = simDelta / wallDelta  makes  WarpElapsed == simDelta  EXACTLY: the
        //      frame time cancels, and Bullet integrates sim seconds. Paused ⇒ simDelta 0 ⇒ Factor 0 ⇒
        //      Simulate(0) ⇒ `carriedDelta += 0` ⇒ its fixed-step loop never runs ⇒ NOTHING INTEGRATES,
        //      while the rest of the physics update (removals, bones, characters, contacts, events)
        //      still ticks. That is "always run, sometimes with zero dt".
        //
        // ⛔⛔ This REPLACES Simulation.DisableSimulation as the pause mechanism. That flag makes the
        //    game system `return` before Simulate is even called, taking body readiness, contacts and
        //    events with it — measured: bodies never became physics-ready and vehicles could not move
        //    at all (CE-223 → CE-227). ⭐ Stride's integrator is fixed-step either way
        //    (StepSimulation(FixedTimeStep, 0, FixedTimeStep)), so wall time never reaches it; Factor
        //    only decides HOW MANY fixed steps a frame consumes, and zero is a legal answer.
        //
        // ⭐ Not editor-shaped: the input is "how far did SIM time move", read from the synced clock, so
        //    a cluster-wide pause stops physics identically on every node in mode 2 — no reference to
        //    who owns an entity or which node loaded the scenario.
        double wallSeconds = gameTime.Elapsed.TotalSeconds;

        // ⭐⭐⭐ CE-251 — MODE 2 NEEDS THIS TOO, and the guard below excluded it.
        //    `_editorSubsystem` is null in mode 2, so Factor kept its default of 1 and Stride fed
        //    Bullet WALL seconds on the one host that is a cluster TIME SLAVE — breaking R-143 ("no
        //    wall clock … not in stirede") and reopening CE-227's pause hole for this mode: a
        //    cluster-wide pause stops the kernel and the motors, but Simulate(wallDelta) would keep
        //    gravity and contacts running in a paused world.
        //    ⭐ Same formula, same meaning, different source of the sim delta: the node reads it from
        //    its own kernel, which is the CLUSTER's clock (Q66 §3A). ⚠ Previous frame's value, exactly
        //    as in mode 1 — §11.1a option B — because base.Update runs before the shell ticks.
        if (NodeMode && _nodeShell != null && wallSeconds > 0.0)
        {
            UpdateTime.Factor = _nodeShell.CurrentSimDeltaSeconds / wallSeconds;
        }
        else if (_editorSubsystem != null && wallSeconds > 0.0)
        {
            // ⚠ Guarded on wallSeconds > 0: on the first frame (and any zero-length frame) Elapsed is
            //   zero, so WarpElapsed is zero whatever the factor — the division would be meaningless.
            UpdateTime.Factor = _editorSubsystem.CurrentSimDeltaSeconds / wallSeconds;
        }

        // ── base.Update timing (DIAG) ─────────────────────────────────────
        _baseUpdateSw.Restart();
        base.Update(gameTime);
        _baseUpdateSw.Stop();
        double baseUpdateMs = _baseUpdateSw.Elapsed.TotalMilliseconds;

        // ⭐⭐⭐ R-143 / CE-230 — THE SIM DELTA DRIVES EVERYTHING BELOW. NOT THE FRAME TIME.
        //
        // 🔒 User, 2026-09-08: "no wall clock enywhere, whole sim driven by sim time ONLY. only use of
        //    wallclock us stamping the fdp recording" — "not in stirede, not in editor, not in cgf, not
        //    in simhost, never where simulation is related".
        //
        // 📐 Measured before converting, because the obvious worry was circularity:
        //    EditorSubsystem.Update(deltaTime) calls `_kernel?.Update()` with NO ARGUMENT — the kernel
        //    advances the clock itself — so this delta NEVER reaches the simulation clock. It feeds the
        //    canvas, the selection system, the gizmo producer buffer, the (already dt-ignoring)
        //    PreKernelUpdateHook and the cluster panel. ⇒ no feedback loop, and the conversion is safe.
        //
        // ⚠ It is the PREVIOUS frame's sim delta, which is deliberate and already the documented
        //   as-built: Stride's base.Update (and therefore the physics step) runs BEFORE the editor tick
        //   advances the clock. 📄 DESIGN_Stride_Node_Modes.md §11.1a, option B. Option A (hoisting the
        //   advance into a shell) is CE-207's and would make this the current frame's.
        // ⭐⭐ CE-207 — MODE 2: the node is a TIME SLAVE. Its bootstrapper calls Kernel.Update()
        //   parameterless, so the frame delta reaches only the gizmo producer buffer. There is no
        //   CurrentSimDeltaSeconds here because there is no local editor clock to read it from — the
        //   master supplies time. 📄 Q66 §3A.
        if (NodeMode)
        {
            // ⭐ The SHELL owns the frame: physics pre-step → Kernel.Update() → physics post-step,
            //   mirroring mode 1's documented order. Driving _bootstrapper directly would skip the
            //   brackets and the node would replicate without ever moving a body.
            _nodeShell?.Tick((float)gameTime.Elapsed.TotalSeconds);
            return;
        }

        float simDt = _editorSubsystem?.CurrentSimDeltaSeconds ?? 0f;

        // Internal-loop mode (BATCH-10): drive EditorStrideSubsystem.
        if (_editorSubsystem != null)
        {
            // FIX-PERF-1 (hosted-mode substepping): Tick ONCE per render frame — the fixed-step
            // loop driver would cause up to 8 sub-steps per frame, each running the full
            // editor.Update() (canvas + AI hot-reload + kernel + Bullet), which is a
            // spiral-of-death at low render rates.
            // ⭐ CE-209: the branch that chose between this and _loopDriver.AdvanceFrame is gone
            //   with the self-contained arm — the subsystem always hosts the real editor now.
            _editorSubsystem.Tick(simDt);

            // Spawn diagnostics (follow-up to BATCH-10): throttled to ~once per second.
            LogSpawnDiagnostics();
        }

        // BATCH-12: drive the in-app test harness (keyboard polling + continuous-case hooks +
        // on-screen DebugText status).
        // ⚠ R-143 — this used to take the render-frame WALL delta, with the stated reason that "the
        //   orbiting-ghost demo advances smoothly regardless of the fixed sim cadence". That reason is
        //   retired: the harness drives scripted SIMULATION probes, so a demo that advances while sim
        //   time is stopped is measuring the frame rate, not the simulation.
        _testHarness?.Update(simDt);

        // BATCH-S2-AG: mirror the paused-nav toast (BATCH-S2-AD) into the 3D Stride viewport (where the
        // operator is clicking). DebugTextSystem uses Stride's built-in font; auto-expiry already handled by
        // EditorStrideSubsystem.ToastSecondsRemaining (decremented in EmitMoveMarker).
        if (_editorSubsystem != null && _editorSubsystem.ToastSecondsRemaining > 0f && DebugTextSystem != null)
        {
            string msg = _editorSubsystem.ToastMessage;
            // Center-top of the back buffer (virtual pixels). ~8px per char is a fine estimate for centering.
            int bw = GraphicsDevice?.Presenter?.BackBuffer?.Width ?? 1280;
            int x = Math.Max(10, (bw / 2) - (msg.Length * 4));
            DebugTextSystem.Print(msg, new Int2(x, 30),
                new Color4(1f, 0.85f, 0.2f, 1f)); // amber
        }

        // BATCH-22 (STR-P5-T2): pump the optional second raylib/ImGui inspector window.
        // BeginDrawing/EndDrawing/PollInputEvents are non-blocking per-frame calls that
        // operate on the GLFW/OpenGL context — independent of Stride's DirectX context.
        // If the user has closed the inspector window (X button) we stop pumping but keep
        // the Stride window running.
        if (_inspectorWindow != null)
        {
            if (_inspectorWindow.IsOpen)
                _inspectorWindow.PumpFrame();
            else
            {
                // BATCH-S2-P: closing the 2D editor window quits the whole app (user choice "close 2D = close all").
                // Do NOT Dispose() the inspector here — its raylib CloseWindow() teardown mid-process crashes
                // natively while Stride's D3D context is live. Null the ref (shutdown disposal is guarded by a
                // null-check, so it won't call CloseWindow either) and let process exit reclaim the GL context.
                _inspectorWindow = null;
                Log.Info("[StrideHrotGame] 2D editor window closed by user — exiting application (close 2D = close all).");
                Exit(); // Stride Game.Exit — clean shutdown
            }
        }

        // BATCH-23 (STR-P5-T3): CenterOnEntityCommand — triggered by keyboard C (Stride window)
        // OR by the "Center [C]" button in the inspector panel (sets ConsumeCenter flag).
        // Both paths are funnelled through SelectionState.ConsumeCenter() so they are
        // never duplicated on the same frame.
        if (_editorSubsystem != null && _cameraEntity != null)
        {
            var selection = _editorSubsystem.SelectionState;

            // Keyboard trigger: C key (IsKeyPressed = fires once per press, not every frame).
            if (Input.IsKeyPressed(Keys.C) && selection.HasSelection)
                selection.RequestCenter();

            // Consume the pending center request (set by C key or inspector button).
            if (selection.ConsumeCenter() && selection.HasSelection)
            {
                ExecuteCenterOnEntity(selection.SelectedEntity);
            }

            // BATCH-S2-O: 3D click-to-select (LMB) and click-to-move (RMB).
            if (_raycastService != null)
            {
                var cam = _cameraEntity.Get<CameraComponent>();
                var world = _editorSubsystem.World;
                if (cam != null && world != null)
                {
                    bool lmb     = Input.IsMouseButtonPressed(MouseButton.Left);
                    bool lmbHeld = Input.IsMouseButtonDown(MouseButton.Left);
                    bool lmbUp   = Input.IsMouseButtonReleased(MouseButton.Left);
                    bool rmbDown = Input.IsMouseButtonPressed(MouseButton.Right);
                    bool rmbUp   = Input.IsMouseButtonReleased(MouseButton.Right);

                    // Track RMB hold + accumulate movement (camera orbit → large accum; click → ~0).
                    if (rmbDown) { _rmbHeld = true; _rmbDragAccum = 0f; }
                    if (_rmbHeld) _rmbDragAccum += Input.MouseDelta.Length();

                    // LMB-press: select the hit entity and arm a potential drag. (BATCH-S2-W)
                    if (lmb)
                    {
                        var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
                        var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
                        Log.Info("[ClickDiag] LMB mouse=({0:F3},{1:F3}) hasHit={2} hitEntity=#{3} point=({4:F2},{5:F2},{6:F2})",
                            Input.MousePosition.X, Input.MousePosition.Y, hit.HasHit,
                            (hit.HitEntity == Fdp.Core.Entity.Null ? -1 : hit.HitEntity.Index),
                            hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);
                        if (hit.HasHit && hit.HitEntity != Fdp.Core.Entity.Null && world.IsAlive(hit.HitEntity))
                        {
                            _editorSubsystem.SelectionState.Select(hit.HitEntity);
                            _dragEntity = hit.HitEntity;   // arm a possible drag of this entity
                            _lmbDragAccum = 0f;
                            _dragging = false;
                            Log.Info("[ClickDiag] LMB selected entity #{0}", hit.HitEntity.Index);
                        }
                        else { _dragEntity = null; _dragging = false; }
                    }

                    // LMB-held: once movement exceeds the deadzone, drag the entity along the FDP ground plane (Z=0). (BATCH-S2-W)
                    if (_dragEntity is { } dragE && lmbHeld)
                    {
                        _lmbDragAccum += Input.MouseDelta.Length();
                        if (!_dragging && _lmbDragAccum > DragStartDeadzone)
                        {
                            _dragging = true;
                            _editorSubsystem.CancelMove(dragE); // BATCH-S2-X: drop the old nav target so it doesn't resume on release
                            Log.Info("[StrideHrotGame] Drag started — cancelled nav for entity #{0}.", dragE.Index);
                        }
                        if (_dragging && world.IsAlive(dragE) && world.HasComponent<SimTransform>(dragE))
                        {
                            var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
                            // Intersect the FDP ground plane Z=0 (FDP Z is up). Avoids self-hit of the dragged body
                            // and is stable for the flat arena floor.
                            if (MathF.Abs(ray.Direction.Z) > 1e-4f)
                            {
                                float t = -ray.Origin.Z / ray.Direction.Z;
                                if (t > 0f)
                                {
                                    var p = ray.Origin + ray.Direction * t;
                                    var cur = world.GetComponent<SimTransform>(dragE);
                                    world.SetComponent(dragE, new SimTransform
                                    {
                                        Position = new System.Numerics.Vector3(p.X, p.Y, cur.Position.Z), // keep authored height
                                        Rotation = cur.Rotation,
                                    });
                                }
                            }
                        }
                    }

                    // LMB-release: finalize. (BATCH-S2-W)
                    if (lmbUp)
                    {
                        if (_dragging && _dragEntity is { } droppedE)
                            Log.Info("[StrideHrotGame] Drag-drop entity #{0} released.", droppedE.Index);
                        _dragEntity = null;
                        _dragging = false;
                        _lmbDragAccum = 0f;
                    }

                    // RMB move — only on release, only if it was a click (small accumulated movement), not a camera orbit.
                    if (rmbUp)
                    {
                        float accum = _rmbDragAccum;
                        _rmbHeld = false;
                        _rmbDragAccum = 0f;
                        if (accum <= RmbDragDeadzone)
                        {
                            var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
                            var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
                            Log.Info("[ClickDiag] RMB-click accum={0:F4} hasHit={1} point=({2:F2},{3:F2},{4:F2})",
                                accum, hit.HasHit, hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);
                            if (hit.HasHit)
                            {
                                var sel = _editorSubsystem.SelectionState;
                                if (sel.HasSelection && world.IsAlive(sel.SelectedEntity))
                                {
                                    IssueMoveOrder(world, sel.SelectedEntity, hit.PointFdp);
                                    _editorSubsystem.ShowMoveMarker(hit.PointFdp);
                                    // BATCH-S2-AD: if sim time is paused, the unit won't move yet — tell the operator (they kept hitting this).
                                    if (_editorSubsystem.TimeController.GetMode() == TimeMode.Deterministic)
                                        _editorSubsystem.ShowToast("Sim is PAUSED — move queued; the unit will move when you start time.");
                                }
                            }
                        }
                        else
                        {
                            Log.Info("[ClickDiag] RMB drag (accum={0:F4}) — camera orbit, no move order.", accum);
                        }
                    }
                }
            }
        }

        // BATCH-S2-AC: camera bookmarks. Ctrl+Alt+N saves slot N; Alt+N recalls slot N (N = 0..9).
        if (_cameraEntity != null)
        {
            bool ctrl = Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl);
            bool alt  = Input.IsKeyDown(Keys.LeftAlt)  || Input.IsKeyDown(Keys.RightAlt);
            if (alt)
            {
                // Digit keys D0..D9 map to slots 0..9.
                // Keys.D0..D9 mirror Windows VK_0..VK_9 (0x30..0x39) — contiguous and ordered.
                for (int slot = 0; slot <= 9; slot++)
                {
                    var key = (Keys)((int)Keys.D0 + slot);
                    if (!Input.IsKeyPressed(key)) continue;
                    if (ctrl)
                    {
                        _cameraBookmarks.Save(slot, _cameraEntity.Transform.Position, _cameraEntity.Transform.Rotation);
                        _editorSubsystem?.ShowToast($"Saved camera bookmark {slot}");
                    }
                    else if (_cameraBookmarks.TryGet(slot, out var pos, out var rot))
                    {
                        _cameraEntity.Transform.Position = pos;
                        _cameraEntity.Transform.Rotation = rot;
                        Log.Info("[StrideHrotGame] Recalled camera bookmark slot {0}.", slot);
                        _editorSubsystem?.ShowToast($"Recalled camera bookmark {slot}");
                    }
                    else
                    {
                        _editorSubsystem?.ShowToast($"Camera bookmark {slot} is empty");
                    }
                }
            }
        }

        // ── Accumulate frame-timing stats (DIAG) ──────────────────────────
        _totalUpdateSw.Stop();
        double totalUpdateMs = _totalUpdateSw.Elapsed.TotalMilliseconds;

        // Skip the very first call (frameDeltaMs is zero / unrepresentative).
        if (_timingFrameCount > 0)
        {
            _accumFrameDeltaMs  += frameDeltaMs;
            if (frameDeltaMs > _maxFrameDeltaMs)  _maxFrameDeltaMs  = frameDeltaMs;
            _accumBaseUpdateMs  += baseUpdateMs;
            if (baseUpdateMs > _maxBaseUpdateMs)  _maxBaseUpdateMs  = baseUpdateMs;
            _accumTotalUpdateMs += totalUpdateMs;
        }
        _timingFrameCount++;
    }

    /// <summary>
    /// Wraps <c>base.Draw</c> in a Stopwatch to measure the 3D render-submission cost
    /// (diagnostics only — no behaviour change, DIAG).
    /// </summary>
    protected override void Draw(GameTime gameTime)
    {
        _drawSw.Restart();
        base.Draw(gameTime);
        _drawSw.Stop();

        double drawMs = _drawSw.Elapsed.TotalMilliseconds;
        if (_timingFrameCount > 1)   // skip frame 0 (unrepresentative startup draw)
        {
            _accumDrawMs += drawMs;
            if (drawMs > _maxDrawMs) _maxDrawMs = drawMs;
        }
    }

    /// <summary>
    /// Emits a throttled (every <see cref="DiagnosticsLogIntervalFrames"/> frames, i.e.
    /// roughly once per second) NLog Info line (logger "StrideHrotGame", written to
    /// <c>logs/editor_stride.log</c>) reporting:
    /// <list type="bullet">
    ///   <item>the FDP world entity count (<see cref="EditorStrideSubsystem.World"/>.EntityCount);</item>
    ///   <item>the number of live Stride visuals
    ///     (<see cref="EditorStrideSubsystem.VisualBindingSystem"/>.Visuals); and</item>
    ///   <item>each visual's Stride <c>Transform.Position</c> (the owning Stride entity's
    ///     world position; the <see cref="StrideVisualReference.VisualHandle"/> is a
    ///     <see cref="global::Stride.Engine.Entity"/> created by
    ///     <see cref="StrideVisualFactory"/>).</item>
    /// </list>
    /// This lets a human confirm the spawned models actually materialized on the real
    /// engine and where they are, so the free-flight camera can be flown to them.
    /// All accesses are null-guarded so the log never throws.
    /// </summary>
    private void LogSpawnDiagnostics()
    {
        // Throttle: only log once every DiagnosticsLogIntervalFrames frames.
        if (++_diagnosticsFrameCounter < DiagnosticsLogIntervalFrames)
            return;
        _diagnosticsFrameCounter = 0;

        // ── Frame-timing diagnostics (DIAG) ───────────────────────────────
        // Log one summary line covering the measurement window (≈1 s / 60 frames).
        // Counts usable samples: _timingFrameCount starts at 0, first frame is skipped,
        // so usable = max(0, _timingFrameCount - 1).
        int usableFrames = Math.Max(0, _timingFrameCount - 1);
        if (usableFrames > 0)
        {
            double avgFrameDeltaMs  = _accumFrameDeltaMs  / usableFrames;
            double avgBaseUpdateMs  = _accumBaseUpdateMs  / usableFrames;
            double avgTotalUpdateMs = _accumTotalUpdateMs / usableFrames;
            double avgDrawMs        = _accumDrawMs        / usableFrames;

            double avgFps = avgFrameDeltaMs > 0.0 ? 1000.0 / avgFrameDeltaMs : 0.0;
            double minFps = _maxFrameDeltaMs > 0.0 ? 1000.0 / _maxFrameDeltaMs : 0.0;

            Log.Info(
                "[Frame timing] over {0} frames — " +
                "FrameDelta avg={1:F1}ms max={2:F1}ms (FPS avg={3:F1} min={4:F1}) | " +
                "BaseUpdate avg={5:F1}ms max={6:F1}ms | " +
                "StrideHrotGame.Update avg={7:F1}ms | " +
                "Draw avg={8:F1}ms max={9:F1}ms",
                usableFrames,
                avgFrameDeltaMs,  _maxFrameDeltaMs,
                avgFps,           minFps,
                avgBaseUpdateMs,  _maxBaseUpdateMs,
                avgTotalUpdateMs,
                avgDrawMs,        _maxDrawMs);
        }

        // Reset accumulators for the next window.
        _accumFrameDeltaMs  = 0; _maxFrameDeltaMs  = 0;
        _accumBaseUpdateMs  = 0; _maxBaseUpdateMs  = 0;
        _accumTotalUpdateMs = 0;
        _accumDrawMs        = 0; _maxDrawMs        = 0;
        _timingFrameCount   = 0;

        var subsystem = _editorSubsystem;
        if (subsystem == null)
            return;

        int entityCount = subsystem.World?.EntityCount ?? -1;

        var visuals = subsystem.VisualBindingSystem?.Visuals;
        if (visuals == null)
        {
            Log.Info("[diag] FDP entities={0}, visuals=<none> (VisualBindingSystem null — headless, no visual factory).",
                entityCount);
            return;
        }

        // Build one concise line: counts plus each visual's name + Stride world position.
        var sb = new System.Text.StringBuilder();
        sb.Append("[diag] FDP entities=").Append(entityCount)
          .Append(", visuals=").Append(visuals.Count).Append(':');

        foreach (var kvp in visuals)
        {
            var reference = kvp.Value;
            // The visual handle is a Stride Entity (see StrideVisualFactory); read its
            // world Transform.Position. Guard against an unexpected handle type.
            if (reference?.VisualHandle is global::Stride.Engine.Entity strideEntity)
            {
                var pos = strideEntity.Transform.Position;
                sb.Append(" '").Append(strideEntity.Name).Append("'@(")
                  .Append(pos.X.ToString("F2")).Append(',')
                  .Append(pos.Y.ToString("F2")).Append(',')
                  .Append(pos.Z.ToString("F2")).Append(')');
            }
            else
            {
                sb.Append(" <non-Entity handle>");
            }
        }

        Log.Info(sb.ToString());
    }

    // ── CenterOnEntityCommand (STR-P5-T3, BATCH-23) ──────────────────────

    /// <summary>
    /// Instantly repositions and reorients <see cref="_cameraEntity"/> to frame the selected
    /// FDP entity.  Called from <see cref="Update(GameTime)"/> when the C key is pressed
    /// (in the Stride window) or the "Center [C]" button is clicked (in the inspector panel).
    ///
    /// <para>
    /// The camera is moved to <c>entityStridePos + CenterOnEntityCommand.CameraOffset</c>
    /// (2 m above, 3 m south) and rotated to look at the entity via
    /// <see cref="CenterOnEntityCommand.Compute"/>.  This is instant (no smoothing) for v1.
    /// The <c>BasicCameraController</c> attached to the camera reads its entity's
    /// <c>Transform</c> each frame, so after this call it will use the new position as the
    /// starting point for free-flight controls — free-flight is NOT broken.
    /// </para>
    /// </summary>
    /// <param name="entity">The FDP entity to center on (must be alive).</param>
    private void ExecuteCenterOnEntity(Fdp.Core.Entity entity)
    {
        if (_editorSubsystem?.World == null)      return;
        if (_cameraEntity == null)                return;
        if (!_editorSubsystem.World.IsAlive(entity)) return;

        var world = _editorSubsystem.World;
        if (!world.IsComponentTypeRegistered<SimTransform>()) return;
        if (!world.HasComponent<SimTransform>(entity))         return;

        ref readonly var t = ref world.GetComponentRO<SimTransform>(entity);

        CenterOnEntityCommand.Compute(
            t.Position,
            out var newPos,
            out var newRot);

        _cameraEntity.Transform.Position = newPos;
        _cameraEntity.Transform.Rotation = newRot;

        Log.Info("[CenterOnEntity] Camera moved to Stride ({0:F2},{1:F2},{2:F2}) looking at FDP ({3:F2},{4:F2},{5:F2}).",
            newPos.X, newPos.Y, newPos.Z,
            t.Position.X, t.Position.Y, t.Position.Z);
    }

    // ── BATCH-S2-O: move-order routing ───────────────────────────────────

    /// <summary>
    /// Issues a move order to the given entity: character path (FdpNavigationOrders.IssueMoveTo)
    /// or vehicle path (NavigationIntent DirectPoint), discriminated by physics body shape.
    /// Shape-based discrimination is reliable; VehicleState is NOT (it is wrongly injected on
    /// infantry by the TKB translator — BATCH-S2-T).
    /// </summary>
    private void IssueMoveOrder(EntityRepository world, Fdp.Core.Entity entity, System.Numerics.Vector3 targetFdp)
    {
        const float Speed = 5f;
        const float ArrivalRadius = 2f;

        // Discriminate by physics body shape — VehicleState is wrongly injected on infantry by the
        // TKB translator, so it is NOT a reliable vehicle/character discriminator (BATCH-S2-T).
        bool isCharacter = false;
        var lifecycle = _editorSubsystem?.PhysicsBodyLifecycle;
        if (lifecycle != null && lifecycle.Bodies.TryGetValue(entity, out var bodyRef))
            isCharacter = bodyRef.ShapeKind == CollisionShapeKind.Capsule;

        if (isCharacter)
        {
            // F6 workaround: strip the bogus VehicleState so NavigationIntentBridgeSystem enrolls the
            // infantry in the DotRecast crowd (it skips entities that carry VehicleState).
            if (world.IsComponentTypeRegistered<VehicleState>() && world.HasComponent<VehicleState>(entity))
                world.RemoveComponent<VehicleState>(entity);

            // The crowd pipeline (CrowdAgentUpdateSystem) and BulletCharacterMotor both query With<CrowdMotorIntent>;
            // NavigationIntentBridgeSystem registers the agent but does NOT add this component, so add it here
            // (mirrors the working F5 'Navmesh Walk' / F6 'FDP Move Order char' harness cases). (BATCH-S2-W)
            if (world.IsComponentTypeRegistered<CrowdMotorIntent>() && !world.HasComponent<CrowdMotorIntent>(entity))
                world.AddComponent(entity, new CrowdMotorIntent());

            FdpNavigationOrders.IssueMoveTo(world, entity, targetFdp, Speed, ArrivalRadius, NavLayerMask.Infantry);
            Log.Info("[StrideHrotGame] Move order (CHARACTER) entity #{0} → FDP ({1:F2},{2:F2},{3:F2}) via IssueMoveTo.",
                entity.Index, targetFdp.X, targetFdp.Y, targetFdp.Z);
            return;
        }

        // Vehicle path: NavigationIntent DirectPoint (existing logic — keep exactly as before).
        if (!world.IsComponentTypeRegistered<NavigationIntent>()) return;
        var intent = world.HasComponent<NavigationIntent>(entity)
            ? world.GetComponent<NavigationIntent>(entity) : default;
        intent.Mode             = NavigationMode.DirectPoint;
        intent.FinalDestination = targetFdp;
        intent.TargetSpeed      = Speed;
        intent.ArrivalRadius    = ArrivalRadius;
        intent.IntentId         = intent.IntentId + 1;
        intent.ReverseAllowed   = 0;
        if (world.HasComponent<NavigationIntent>(entity)) world.SetComponent(entity, intent);
        else world.AddComponent(entity, intent);
        if (world.IsComponentTypeRegistered<NavigationStatus>() && !world.HasComponent<NavigationStatus>(entity))
            world.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });
        Log.Info("[StrideHrotGame] Move order (VEHICLE) entity #{0} → FDP ({1:F2},{2:F2},{3:F2}) via NavigationIntent.",
            entity.Index, targetFdp.X, targetFdp.Y, targetFdp.Z);
    }

    // ── BATCH-S2-Q: FDP entity resolver (reverse visuals-map lookup) ─────

    /// <summary>
    /// Resolves the FDP <see cref="Fdp.Core.Entity"/> that owns a hit Stride visual/physics entity,
    /// by reverse-looking-up the visual-binding map (FDP Entity → StrideVisualReference.VisualHandle).
    /// Returns <see cref="Fdp.Core.Entity.Null"/> for static scene geometry (floor/walls). (BATCH-S2-Q)
    /// </summary>
    private Fdp.Core.Entity ResolveFdpEntityFromStride(global::Stride.Engine.Entity strideEntity)
    {
        var visuals = _editorSubsystem?.VisualBindingSystem?.Visuals;
        if (visuals != null)
        {
            foreach (var kv in visuals)
                if (ReferenceEquals(kv.Value.VisualHandle, strideEntity))
                    return kv.Key;
        }
        return Fdp.Core.Entity.Null;
    }

    // ── Boot helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Boots the EditorStrideSubsystem on the live scene.
    /// Called from <see cref="BeginRun"/> after scene and content are valid.
    /// </summary>

    /// <summary>
    /// ⭐⭐⭐ <c>CE-231</c> — a ground slab, VISIBLE and SOLID, large enough for scenario coordinates.
    /// </summary>
    /// <remarks>
    /// <para>🔒 <b>User, 2026-09-08:</b> <i>"best if we could extend the stride's terrain floor to be way
    /// larger so our scenarios can be modelled outside of the current (and extremely small) stride
    /// arena"</i> — and, on a first draft that added only a collider: <i>"it can not be just a physics
    /// collider, it must be something visible in 3d otherwise entities would be floating in the air
    /// visually"</i>. ⇒ this entity carries BOTH a <c>ModelComponent</c> and a
    /// <c>StaticColliderComponent</c>, sharing one size.</para>
    ///
    /// <para><b>📐 The problem, measured.</b> <c>MainScene</c>'s floor is a hand-placed mosaic of
    /// <c>Floor1x0x1</c> / <c>Floor3x0x1</c> prefab tiles a few metres across, centred on the origin.
    /// <c>hill-attack-close</c> puts its vehicles at world coordinates around <b>(446, 420)</b> to
    /// <b>(668, 522)</b>, where there is nothing beneath them — so the moment physics integrates they
    /// fall, measured to <c>z = -180 m</c> and still accelerating at <c>-52 m/s</c>.</para>
    ///
    /// <para>⭐ <b>Why built in code rather than authored as tiles.</b> The tiles are an ART asset and the
    /// scene is the artist's; carpeting kilometres with prefab instances would bloat the asset and slow
    /// the asset build, and it still would not follow a scenario that moves. One slab is O(1) and
    /// trivially resized.</para>
    ///
    /// <para><b>⚠ AXIS MAPPING — measured, not assumed.</b> A live <c>BodyState</c> line shows FDP
    /// <c>(446.3, 420.9, 0.5)</c> arriving as Stride <c>(446.317, 0.500, 420.903)</c> ⇒
    /// <b>FDP.x → Stride.X, FDP.y → Stride.Z, FDP.z (up) → Stride.Y (up)</b>. The slab therefore spans
    /// Stride's X/Z and is thin in Y, with its TOP face at <c>Y = 0</c>, the plane entities are authored
    /// on.</para>
    ///
    /// <para>⚠ <b>Bounded on purpose.</b> A larger box is not free — Bullet's broadphase and contact
    /// precision degrade as extents grow — so this covers realistic scenario space with margin rather
    /// than being made astronomically large "to be safe". ⛔ It is also DELIBERATELY not a heightfield:
    /// it is flat, so it gives ground and a visual reference, not terrain relief.</para>
    /// </remarks>
    private void AddScenarioGroundPlane(Scene scene)
    {
        // Metres. 20 km across covers scenario coordinates with wide margin; 1 m thick so a
        // fast-falling body cannot tunnel through it within a single fixed physics step.
        const float ExtentMetres    = 20000f;
        const float ThicknessMetres = 1f;

        // ⭐ A unit cube scaled to the slab: no plane-orientation ambiguity, and the SAME size drives
        //   both the visual and the collider, so they cannot drift apart.
        var material = Material.New(GraphicsDevice, new MaterialDescriptor
        {
            Attributes = new MaterialAttributes
            {
                Diffuse      = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(0.22f, 0.25f, 0.20f, 1f))),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            },
        });

        // ⭐ The mesh is built the way this repo already builds one — PooledEntityDebugDrawSink3D
        //   .AssembleModel. ⛔ There is no ToMeshDraw() extension in this Stride version, and inventing a
        //   second construction path for the same job is the duplication this programme keeps removing.
        var primitive = GeometricPrimitive.Cube.New(GraphicsDevice);
        var half   = new Vector3(ExtentMetres, ThicknessMetres, ExtentMetres) * 0.5f;
        var bbox   = new BoundingBox(-half, half);
        var model  = new Model
        {
            new Mesh
            {
                Draw          = primitive.ToMeshDraw(),
                BoundingBox   = bbox,
                MaterialIndex = 0,
            },
        };
        model.BoundingBox = bbox;
        model.Materials.Add(new MaterialInstance(material));

        var collider = new StaticColliderComponent();
        collider.ColliderShape = new BoxColliderShape(
            is2D: false,
            size: new Vector3(ExtentMetres, ThicknessMetres, ExtentMetres));

        // ⭐⭐ CE-240 — the COLLIDER keeps its top face at exactly Y = 0; the VISUAL sits a few
        //   centimetres lower on a CHILD entity.
        //
        //   🔒 User, 2026-09-08: "the floor plane z-fights with the in-area floor; lowering the
        //   rendered floor plane a bit would help". MainScene's prefab floor tiles are also at Y = 0,
        //   so two coplanar surfaces fight for depth across the whole arena.
        //
        //   ⛔ Lowering the WHOLE entity would drop the collider too, and every body would rest that
        //   much lower — a physics change to fix a rendering artefact. Splitting the visual onto a
        //   child keeps the contact plane exactly where CE-231 measured it (bodies hold z = 0.5) while
        //   moving only what is drawn. ⚠ The offset is deliberately larger than typical depth-buffer
        //   precision at these extents but far below anything visible at human scale.
        const float VisualDropMetres = 0.05f;

        var ground = new Stride.Engine.Entity("ScenarioGroundPlane") { collider };
        // Top face at Y = 0: the slab centre sits half a thickness below it.
        ground.Transform.Position = new Vector3(0f, -ThicknessMetres * 0.5f, 0f);

        var groundVisual = new Stride.Engine.Entity("ScenarioGroundVisual")
        {
            new ModelComponent(model),
        };
        // The cube primitive is unit-sized, so scale carries the extent for the VISUAL half.
        groundVisual.Transform.Scale    = new Vector3(ExtentMetres, ThicknessMetres, ExtentMetres);
        groundVisual.Transform.Position = new Vector3(0f, -VisualDropMetres, 0f);
        ground.AddChild(groundVisual);

        scene.Entities.Add(ground);

        Log.Info(
            "[StrideHrotGame] CE-231: scenario ground slab added — {0:F0} x {0:F0} m, {1:F1} m thick, " +
            "collider top face at Y=0, visual dropped {2:F2} m to avoid z-fighting with MainScene's " +
            "coplanar floor tiles (CE-240). Those tiles span only a few metres; scenario coordinates " +
            "run to ~700 m.",
            ExtentMetres, ThicknessMetres, VisualDropMetres);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-207</c> — mode 2 boot: join a cluster as a Muscle + Perception node.
    /// </summary>
    /// <remarks>
    /// ⭐ Keeps the SAME scene preparation mode 1 uses — the template player is neutralised, the
    /// infinite plane walls are stripped (<c>CE-232</c>) and the scenario ground slab is added
    /// (<c>CE-231</c>/<c>CE-240</c>) — because those are properties of the WORLD, not of the editor.
    /// ⛔ It does not create the editor, its windows or its camera scripts.
    /// </remarks>
    private void BootClusterNode()
    {
        var scene = SceneSystem.SceneInstance.RootScene;
        NeutralizeTemplatePlayer(scene);
        NeutralizeInfinitePlaneColliders(scene);
        AddScenarioGroundPlane(scene);
        AddFixedCamera(scene);

        _nodeShell = new StrideNodeShell();
        _nodeShell.Boot(NodeDomainId, NodeId);
        bool physicsLive = _nodeShell.AttachPhysics(this, scene);
        AttachBootstrapper(_nodeShell.Bootstrapper);
        Log.Info("[StrideHrotGame] CE-207 stage 2: physics bracket attached (bulletLive={0}).", physicsLive);

        // ⭐⭐⭐ CE-248 — BAKE THE NAVMESH. Mode 1 does this at BootEditorSubsystem; mode 2 never did,
        //    and VehicleNavigationIntentSystem returns immediately without an INavmeshProvider, so the
        //    node planned no routes and commanded no motors while every earlier stage looked healthy.
        //    ⚠ AFTER the ground slab and the collider neutralisation above: the bake reads the scene's
        //    STATIC COLLIDERS, so it must see the 20 km scenario slab (CE-231) and must NOT see the
        //    template arena's infinite planes (CE-232), which would otherwise bound the whole bake to
        //    the few-metre arena.
        BakeNavmesh(scene, _nodeShell.Context?.World, _nodeShell.InfantryCrowdProvider);

        // ⭐⭐ CE-245 — the node's own debug/MCP surface, on the SAME environment variable every other
        //    host uses (the editor's and ClusterRunner's gate alike), so no tool needs a special case
        //    for a Stride node. Absent variable ⇒ not started, costing nothing in a normal run.
        _nodeShell.StartDebugApi(Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT"));

        // ⭐⭐⭐ CE-214 / S6 — THE OPERATOR WINDOW, unconditionally.
        //    🔒 R-S16: "window is not optional, sames as in stride editor." ⛔ So there is no flag here,
        //    unlike mode 1's STRIDE_EDITOR_WINDOW. ⭐ Safe: there is no headless mode 2 (this process
        //    sets Headless = false and always opens a Stride window), so §7.2's "headless/CI must be
        //    unaffected" caveat has nothing to protect.
        //    ⚠ Guarded so a window failure cannot cost the node its simulation — the node's JOB is to
        //    simulate; the operator surface is how a human watches it.
        try
        {
            _nodeShell.StartOperatorWindow();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "[StrideHrotGame] CE-214: the operator window failed to compose — the node " +
                         "continues to simulate without it.");
        }

        Log.Info("[StrideHrotGame] CE-207: mode 2 node attached — the frame loop now drives " +
                 "StrideNodeBootstrapper.Tick (parameterless Kernel.Update, time slave).");
    }

    private void BootEditorSubsystem()
    {
        // ── 1. Get the root scene ─────────────────────────────────────────
        // [VERIFY] SceneSystem.SceneInstance.RootScene — confirmed valid in BeginRun.
        // SceneSystem is a Game property (Stride.Engine.Game.SceneSystem).
        // SceneInstance is populated by SceneSystem once the scene asset is loaded.
        var scene = SceneSystem.SceneInstance.RootScene;

        // ── 2. Neutralize template player + camera scripts ────────────────
        // The template PlayerCharacter entity has PlayerController + PlayerInput scripts.
        // ThirdPersonCamera (child entity Camera) requires a valid parent + Bullet simulation.
        // To prevent boot errors from these scripts, we remove the PlayerCharacter entity
        // from the scene. We then create a fixed overview camera that can see the spawn area.
        NeutralizeTemplatePlayer(scene);

        // ── 2a. STRIP THE TEMPLATE ARENA'S INFINITE PLANE WALLS ───────────
        // CE-232. Must run BEFORE the ground slab and before any scenario loads: while these exist,
        // every world coordinate beyond ±20 m is the inside of a solid half-space.
        NeutralizeInfinitePlaneColliders(scene);

        // ── 2b. A GROUND PLANE BIG ENOUGH FOR REAL SCENARIO COORDINATES ───
        AddScenarioGroundPlane(scene);

        // ── 3. Add a fixed overview camera ────────────────────────────────
        // Camera position in Stride space: (0, 10, -5).
        // Looking toward the spawn area around Stride (0, 0, 5).
        // This gives a roughly 45° downward angle from the north/rear side.
        AddFixedCamera(scene);

        // BATCH-S2-AC: restore the default camera bookmark (slot 0) on load, if present.
        if (_cameraEntity != null && _cameraBookmarks.TryGet(0, out var camPos0, out var camRot0))
        {
            _cameraEntity.Transform.Position = camPos0;
            _cameraEntity.Transform.Rotation = camRot0;
            Log.Info("[StrideHrotGame] Restored default camera bookmark (slot 0) on load.");
        }

        // ── 4. Boot the visual factory and subsystem ──────────────────────
        // The blend-tree installer (BATCH-16 Fix A) is what makes mannequins ANIMATE: it loads the
        // Animations/* clips through this game's Content manager and attaches a
        // PerEntityBlendTreeBuilder to each mannequin's AnimationComponent so the backend's
        // per-frame idle/walk/run blend + jump montage drives the GPU skeleton.
        //
        // BATCH-17 (STR-D11): pass BulletPhysicsBodyService as an optional parameter to Initialize()
        // so the subsystem wires the real service into lifecycle/motor/reverse-sync at construction.
        // The Simulation is available here (BeginRun — scene is loaded, PhysicsProcessor
        // was initialised by Stride's system pipeline from the MainScene's static colliders).
        //
        // [VERIFY] Simulation access (Stride 4.2.1.2487):
        //   SceneSystem.SceneInstance.GetProcessor<PhysicsProcessor>() returns the live
        //   PhysicsProcessor. From it, .Simulation is the Bullet Simulation instance.
        //   The MainScene's 144 static colliders guarantee PhysicsProcessor is present in BeginRun.
        // ── BATCH-S2-H: autonomous self-test mode ────────────────────────────────
        // When STRIDE_SELFTEST=1 is set the app runs StrideSelfTest and exits automatically.
        // STRIDE_EDITOR_WINDOW remains gated by its own flag (defaults OFF — no raylib window
        // needed for the self-test, only the 3D Stride window + physics).
        // ⭐ CE-209 / R-S9: the self-test used to have to FORCE hostRealEditor=true because a
        //   self-contained arm existed to be forced away from. It no longer does — the hosted
        //   editor is the only composition, so the self-test simply gets it. Q2 predicted exactly
        //   this collapse: "STRIDE_HOST_REAL_EDITOR disappears (hosted becomes the only editor
        //   path), STRIDE_EDITOR_WINDOW stays (it is a window toggle), STRIDE_SELFTEST stays".
        bool selfTestEnabled = string.Equals(
            System.Environment.GetEnvironmentVariable("STRIDE_SELFTEST"),
            "1",
            StringComparison.Ordinal);
        if (selfTestEnabled)
            Log.Info("[StrideHrotGame] STRIDE_SELFTEST=1 — autonomous self-test mode ENABLED.");

        var visualFactory      = new StrideVisualFactory(this, scene);
        var blendTreeInstaller = new StrideMannequinBlendTreeInstaller(Content);
        _editorSubsystem       = new EditorStrideSubsystem();

        // Obtain the live Bullet Simulation for BulletPhysicsBodyService.
        // BulletPhysicsBodyService holds a reference to the IReadOnlyDictionary<Entity, StrideVisualReference>
        // exposed by VisualBindingSystem.Visuals. Since both are created during Initialize(),
        // we use a VisualBindingSystemAdapter: a lazily-resolved wrapper that defers the
        // visual-set lookup to the first CreateBody call. This avoids a chicken-and-egg problem
        // between the service needing the visual dict and Initialize() needing the service.
        var physicsProcessor = SceneSystem.SceneInstance.GetProcessor<Stride.Physics.PhysicsProcessor>();
        IPhysicsBodyService? bulletService = null;

        if (physicsProcessor?.Simulation != null)
        {
            Log.Info("[StrideHrotGame] PhysicsProcessor found; BulletPhysicsBodyService will be wired (STR-D11).");

            // VisualBindingSystemProvider: captures _editorSubsystem and lazily returns
            // VisualBindingSystem.Visuals when queried. This reference is captured by closure and
            // evaluated after Initialize() returns, so Visuals is already populated.
            bulletService = new BulletPhysicsBodyServiceDeferred(
                physicsProcessor.Simulation,
                () => _editorSubsystem?.VisualBindingSystem?.Visuals
                    ?? new System.Collections.Generic.Dictionary<Fdp.Core.Entity, Hrot.Stride.Core.StrideVisualReference>());

            // BATCH-S2-O: construct raycast service for 3D click-to-select / click-to-move.
            // BATCH-S2-Q: pass the resolver so hit Stride entities are mapped to FDP entities
            // via the live visuals map (reverse lookup) rather than the legacy name-parse.
            _raycastService = new StrideRaycastService(physicsProcessor.Simulation, ResolveFdpEntityFromStride);
            Log.Info("[StrideHrotGame] StrideRaycastService created (BATCH-S2-O).");
        }
        else
        {
            Log.Warn("[StrideHrotGame] PhysicsProcessor not found at BeginRun — NoOpPhysicsBodyService will be used. " +
                     "Physics will NOT move entities. Ensure the MainScene has PhysicsSettings and static colliders.");
        }

        // ── 4c. GPU debug-draw sink (STR-D16 resolution, BATCH-21) ─────────────
        // PooledEntityDebugDrawSink3D: pooled Stride entities with emissive materials so
        // gizmo shapes are actually visible in the Stride window. Created here (after
        // BeginRun, where GraphicsDevice is live) and passed into Initialize so the
        // GizmoRenderer3D emits to it instead of the headless logging sink.
        var debugDrawSink = new Hrot.Stride.Core.PooledEntityDebugDrawSink3D(this, scene);
        Log.Info("[StrideHrotGame] PooledEntityDebugDrawSink3D created (STR-D16 resolved).");

        // Initialize subsystem with the real physics service + concrete GPU draw sink.
        // Pass buildEditorUi so the hosted EditorSubsystem is initialized non-headless when
        // the second raylib window is also enabled (STRIDE_EDITOR_WINDOW=1) — this activates
        // MapCanvas, adapters, layers, and all ImGui panels inside the editor so that
        // RegisterWindows/DrawWorld/DrawUI work correctly.
        bool buildEditorUi = StrideInspectorWindowConfig.IsEnabled;
        _editorSubsystem.Initialize(visualFactory, blendTreeInstaller, bulletService, debugDrawSink,
            buildEditorUi: buildEditorUi);

        // ── 4b. Bake navmesh from arena static colliders (BATCH-18, STR-D19) ─────────
        // Runs after Initialize so the scene+physics are ready. The baked provider
        // overwrites the FakeNavmeshProvider set up by the simulation logic packs.
        // Guarded: bake failure logs Warn and leaves _navmeshProvider null (F4 demo
        // handles the null case gracefully with a loud log rather than crashing).
        BakeNavmesh(scene, _editorSubsystem?.World, _editorSubsystem?.InfantryCrowdProvider);

        // ⭐⭐ CE-209 / R-S9 — the 6 UrbanCombat DEMO SPAWNS are GONE with the self-contained arm.
        //   They only ever ran on the non-hosted path (BATCH-S2-J had already excluded them from
        //   hosted mode: 4 mannequins along FDP Y=5 + 2 APCs at Y=7 sat in the tiny arena as static
        //   OBSTACLES a loaded scenario vehicle wedged against). With hosted the only composition
        //   the guard had exactly one reachable arm, so the demo set is deleted rather than gated.

        // ── 6. Build the in-app test harness (BATCH-12, STR-TEST-1) ───────
        BuildTestHarness(scene);

        // ── 6a. BATCH-S2-H: register autonomous self-test if STRIDE_SELFTEST=1 ──
        // Registered AFTER BuildTestHarness so _testHarness context is live and
        // its RegisterUpdate pump is already wired into StrideHrotGame.Update.
        // The self-test drives via the same context (ScenarioSource, World) and
        // exits the process via game.Exit() when done.
        if (selfTestEnabled && _testHarness != null && _editorSubsystem != null)
        {
            var harnessCtx = _testHarness.Context;
            // The editor owns the NetworkEntityMap; resolve the LIVE one from the world singleton
            // the spawn pipeline uses (World.SetSingletonManaged<NetworkEntityMap>).
            // ⭐ CE-209: this used to say "in hosted mode ... not assigned" against an EntityMap
            //   property that the OFF arm did assign. Both the property and the OFF arm are gone.
            var emap = _editorSubsystem.World?.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>();
            if (emap != null)
            {
                StrideSelfTest.RegisterIfEnabled(harnessCtx, emap, this, _editorSubsystem.TimeController);
            }
            else
            {
                Log.Warn("[SELFTEST] RESULT initialHold=FAIL repos=FAIL reason=no-entitymap " +
                         "(could not resolve NetworkEntityMap at boot)");
                System.Environment.Exit(2);
            }
        }
        else if (selfTestEnabled)
        {
            Log.Warn("[StrideHrotGame] STRIDE_SELFTEST=1 but test harness or editor subsystem is null — " +
                     "self-test cannot be registered. Process will NOT exit automatically.");
        }

        // ── 7. Optional second raylib/ImGui inspector window (BATCH-22, STR-P5-T2) ──
        // Opened only when STRIDE_EDITOR_WINDOW=1 is set (or ForceEnabled=true).
        // Headless/CI/tests: the flag is false by default, so this block is skipped entirely.
        //
        // Threading note (Option A, design §8.3):
        //   Stride = Direct3D; raylib = GLFW/OpenGL.  Completely separate graphics APIs.
        //   BeginDrawing/EndDrawing/PollInputEvents are per-frame non-blocking — safe to
        //   interleave with Stride.Update() on the same thread.
        if (StrideInspectorWindowConfig.IsEnabled && _editorSubsystem != null)
        {
            try
            {
                // Pass the shared SelectionState so the window can write selection
                // and StrideHrotGame.Update can read it for the highlight + center command.
                _inspectorWindow = new StrideInspectorWindow(
                    _editorSubsystem,
                    _editorSubsystem.SelectionState);
                _inspectorWindow.Open();
                Log.Info("[StrideHrotGame] Inspector window opened (STRIDE_EDITOR_WINDOW=1).");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "[StrideHrotGame] Failed to open inspector window — continuing without it.");
                _inspectorWindow?.Dispose();
                _inspectorWindow = null;
            }
        }
        else
        {
            Log.Info("[StrideHrotGame] Inspector window disabled " +
                     "(set STRIDE_EDITOR_WINDOW=1 to enable, STR-P5-T2).");
        }
    }

    /// <summary>
    /// Constructs the in-app <see cref="StrideTestHarness"/>, registers the initial P0–P3
    /// test cases, and attaches the clickable-button overlay to <paramref name="scene"/>
    /// (BATCH-12). Must be called after <see cref="BootEditorSubsystem"/> has set
    /// <see cref="_editorSubsystem"/> and after <see cref="AddFixedCamera"/> has set
    /// <see cref="_cameraEntity"/>.
    /// </summary>
    private void BuildTestHarness(Scene scene)
    {
        if (_editorSubsystem == null)
            throw new InvalidOperationException("EditorStrideSubsystem must be initialized before building the test harness.");

        // Context: gives every case access to World / ScenarioSource / VisualBindingSystem,
        // the scene, the camera, and an NLog-backed Log helper (mirrors the existing per-class
        // NLog pattern). The Log helper writes through this class's logger so harness actions
        // land in logs/editor_stride.log alongside the diagnostics.
        var harnessLog = NLog.LogManager.GetLogger("StrideTestHarness");
        var context = new TestHarnessContext(
            world:               _editorSubsystem.World,
            scenarioSource:      _editorSubsystem.ScenarioSource,
            visualBindingSystem: _editorSubsystem.VisualBindingSystem,
            scene:               scene,
            cameraEntity:        _cameraEntity,
            log:                 msg => harnessLog.Info(msg));

        var registry = new TestHarnessRegistry();
        StrideTestHarnessCases.RegisterInitialCases(registry);

        // BATCH-14 (STR-P4-T3/T4): the visible Walk / Run / Jump animation cases. They drive
        // SimVelocity/SimTransform directly (physics is NoOp) so the locomotion bridge blends
        // idle→walk→run and the jump-traversal montage path fires.
        StrideAnimationHarnessCases.RegisterAnimationCases(
            registry,
            _editorSubsystem.AnimationBackend,
            _editorSubsystem.AnimationBridge);

        // BATCH-15 (STR-P5-T1/T4): the "Draw Test Gizmo" + "Record 3s / Replay" cases. They write
        // a known DebugPrimitive into the ProducerBuffer (3D renderer resolves+swizzles it), and
        // record/replay the orbiting ghost (reverse-sync severed; PlaybackTickSystem drives it).
        StrideGizmoReplayHarnessCases.RegisterGizmoReplayCases(registry, _editorSubsystem);

        // BATCH-17 (STR-D11 + STR-D13): "Physics Drop" + "Physics Walk" cases.
        // These drive the real Bullet physics path (CrowdMotorIntent → BulletCharacterMotor →
        // CharacterComponent.SetVelocity → Bullet → BulletReverseSyncSystem → SimTransform).
        // Requires BulletPhysicsBodyService to be wired (live app only).
        // PhysicsBodyLifecycle is non-null when a visual factory was provided (live app);
        // in headless runs it is null and these cases gracefully degrade (bodies are no-ops).
        if (_editorSubsystem.PhysicsBodyLifecycle != null)
        {
            StridePhysicsHarnessCases.RegisterPhysicsCases(
                registry,
                _editorSubsystem.PhysicsBodyLifecycle,
                _editorSubsystem.PhysicsBodyService);

            // BATCH-17 waypoint proof: "Drive To Waypoint" (index 12 → F3).
            // Spawns the MilitaryAPC and runs VehicleWaypointController in closed loop,
            // proving the dynamic-rigidbody vehicle converges to goal points.
            // Pass Content.Load<Model> so the waypoint markers get a real ModelComponent
            // and actually render (Fix 1: visible markers).
            StridePhysicsHarnessCases.RegisterDriveToWaypointCase(
                registry,
                _editorSubsystem.PhysicsBodyLifecycle,
                _editorSubsystem.PhysicsBodyService,
                loadModel: modelRef =>
                {
                    try   { return Content.Load<Model>(modelRef); }
                    catch { return null; }
                });

            // BATCH-18 (STR-D19): "Navmesh Drive" (index 13 → F4).
            // Spawns the MilitaryAPC and drives it along a DotRecast navmesh path around a
            // real arena obstacle. _navmeshProvider is null if the bake failed (F4 logs
            // that case loudly and aborts cleanly rather than crashing).
            StridePhysicsHarnessCases.RegisterNavmeshDriveCase(
                registry,
                _editorSubsystem.PhysicsBodyLifecycle,
                _editorSubsystem.PhysicsBodyService,
                navmeshProvider: _navmeshProvider,
                loadModel: modelRef =>
                {
                    try   { return Content.Load<Model>(modelRef); }
                    catch { return null; }
                });

            // BATCH-19 (STR-D19 discharge): "Navmesh Walk" (index 14 → F5).
            // Spawns an InfantrySoldier mannequin and registers it as a real DotRecast crowd
            // agent on the Infantry navmesh. The crowd pathfinds around a wall obstacle and the
            // mannequin walks (animated) to the goal — proving real FDP character navigation.
            StridePhysicsHarnessCases.RegisterNavmeshWalkCase(
                registry,
                _editorSubsystem.PhysicsBodyLifecycle,
                _editorSubsystem.PhysicsBodyService,
                navmeshProvider:       _navmeshProvider,
                infantryCrowdProvider: _editorSubsystem.InfantryCrowdProvider,
                loadModel: modelRef =>
                {
                    try   { return Content.Load<Model>(modelRef); }
                    catch { return null; }
                });

            // BATCH-20 (STR-D19): "FDP Move Order (char)" (index 15 → F6).
            // Drives the PRODUCTION NavigationIntent front door for a CHARACTER: issues a MoveTo on
            // the entity's LocomotionChannel (the BehaviorTree front door); NavigationIntentBridgeSystem
            // auto-registers the crowd agent and the mannequin pathfinds around a wall to the goal.
            StridePhysicsHarnessCases.RegisterFdpMoveOrderCharCase(
                registry,
                _editorSubsystem.PhysicsBodyLifecycle,
                _editorSubsystem.PhysicsBodyService,
                navmeshProvider:       _navmeshProvider,
                infantryCrowdProvider: _editorSubsystem.InfantryCrowdProvider,
                loadModel: modelRef =>
                {
                    try   { return Content.Load<Model>(modelRef); }
                    catch { return null; }
                });

            // BATCH-20 (STR-D19): "FDP Move Order (vehicle)" (index 16 → F7).
            // Drives the PRODUCTION NavigationIntent front door for a VEHICLE: sets NavigationIntent
            // (DirectPoint, goal behind a wall); the new VehicleNavigationIntentSystem plans the navmesh
            // path and steers the APC around the wall — NO manual PlanPath in the harness.
            StridePhysicsHarnessCases.RegisterFdpMoveOrderVehicleCase(
                registry,
                _editorSubsystem.PhysicsBodyLifecycle,
                _editorSubsystem.PhysicsBodyService,
                navmeshProvider:  _navmeshProvider,
                vehicleNavSystem: _editorSubsystem.VehicleNavIntentSystem,
                loadModel: modelRef =>
                {
                    try   { return Content.Load<Model>(modelRef); }
                    catch { return null; }
                });
        }
        else
        {
            Log.Info("[harness] Physics harness cases skipped (no PhysicsBodyLifecycle — headless mode).");
        }

        _testHarness = new StrideTestHarness(this, registry, context);
        _testHarness.BuildUi(scene);

        Log.Info("[harness] Test harness wired with {0} case(s): click buttons or press D1-D{1}.",
            registry.Count, System.Math.Min(registry.Count, 9));
    }

    /// <summary>
    /// Removes the template <c>PlayerCharacter</c> entity from the scene so its
    /// <c>PlayerController</c>, <c>PlayerInput</c>, and child <c>ThirdPersonCamera</c>
    /// scripts are never started and cannot throw.
    /// The static arena geometry (walls, floors) is left intact.
    /// </summary>
    private void NeutralizeTemplatePlayer(Scene scene)
    {
        // Find the PlayerCharacter entity by name.
        // Scene.Entities contains all root entities.
        global::Stride.Engine.Entity? playerCharacter = null;
        foreach (var entity in scene.Entities)
        {
            if (entity.Name == "PlayerCharacter")
            {
                playerCharacter = entity;
                break;
            }
        }

        if (playerCharacter != null)
        {
            // Remove the PlayerCharacter from the scene.
            // This also removes its children (CameraTarget → Camera) since they are
            // part of the entity hierarchy — they are scene entities but parented.
            // Stride's SceneInstance tracks all entities; removing the root entity
            // does not automatically remove child entities from the SceneInstance,
            // so we remove children explicitly.
            RemoveEntityAndChildren(scene, playerCharacter);
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-232</c> — strips <b>infinite half-space plane colliders</b> from the template arena,
    /// which were ejecting every scenario entity that owns a physics body back to the origin.
    /// </summary>
    /// <remarks>
    /// <para><b>📐 The defect, measured end to end on <c>hill-attack-close</c>.</b> Bodies are created at the
    /// RIGHT place — the log shows <c>CreateBody entity=#1 FDP=(446.317,420.903,0.000)</c> and the
    /// reverse-sync writing that same value back for the first frames. Within one second they are
    /// somewhere else entirely: <c>#1</c> reads <c>(289.62,302.90)</c> <b>0.27 s</b> later (≈780 m/s) and
    /// <c>#7</c> travels <c>x=668 → 587.8</c> in <b>0.6 s</b> (≈133 m/s). Nothing commands that — the
    /// vehicle motor was asking for <c>spd=2.90</c>. They are being EJECTED.</para>
    ///
    /// <para><b>⭐ The cause.</b> <c>MainScene</c> carries four walls — <c>Wall_East</c>/<c>West</c> at
    /// <c>X=±20</c> and <c>Wall_North</c>/<c>South</c> at <c>Z=±20</c> — and each is a
    /// <c>StaticPlaneColliderShapeDesc</c>. ⛔⛔ <b>A Bullet static plane is an INFINITE half-space, not a
    /// wall segment.</b> A scenario entity at <c>x=446</c> is therefore <b>426 m deep</b> inside solid
    /// matter, and Bullet's penetration recovery does exactly what it should: it expels the body at a
    /// speed proportional to the depth. Every body then comes to rest jammed just inside the ±20 box —
    /// which is precisely where they were all measured: <c>|x| ∈ [17.9, 19.5]</c>, one of them at
    /// <c>x = -18.95</c>, against the west wall.</para>
    ///
    /// <para>⭐⭐ <b>It explains the second symptom too, the one that looked separate.</b> A tank reported
    /// <c>SimVelocity</c> 2.39 m/s and <c>wz</c> 0.424 rad/s while its position moved less than a
    /// centimetre and its yaw oscillated ±0.2° over ten samples without ever accumulating. That is not a
    /// dead motor and not a stuck integrator — it is a body pinned against a wall, the motor pushing and
    /// the constraint cancelling. <b>One cause, both symptoms.</b></para>
    ///
    /// <para>⚠ <b>Why the partition proves it.</b> The two entities WITHOUT a physics body — the platoon
    /// marker and the objective — held their authored coordinates exactly (<c>(427.8,457.9)</c> and
    /// <c>(670,473.5)</c>). Only body-owning entities collapsed. Nothing but physics separates the two
    /// groups.</para>
    ///
    /// <para>⭐ <b>Design basis: searched <c>docs/</c> and <c>.dev/</c>, no design record claims these walls.</b>
    /// <c>MainScene.sdscene</c> has exactly one commit — <i>"feat: stride game project (initial, from
    /// template)"</i> — so they are unmodified Stride template furniture bounding a demo arena, never a
    /// designed constraint on the world. This host already neutralises template content that breaks
    /// hosted mode (<see cref="NeutralizeTemplatePlayer"/>, and the skipped demo UrbanCombat spawns);
    /// this is the same category and sits beside them.</para>
    ///
    /// <para>⛔ <b>Matched STRUCTURALLY, not by name.</b> The filter is "a static collider carrying a
    /// <c>StaticPlaneColliderShapeDesc</c>", not <c>Wall_*</c>: an infinite half-space is never a valid
    /// collider in a world whose scenarios span kilometres, so a fifth one added later is covered the day
    /// it appears rather than the day someone watches a tank fly sideways.</para>
    ///
    /// <para>⭐ <b>The COLLIDER is removed, the entity is not.</b> The visual wall is harmless decoration at
    /// the arena edge; only the infinite solid was doing damage. Keeping the entity is the smaller blast
    /// radius and leaves the arena looking as authored. ⚠ It also drops these from
    /// <c>StrideSceneGeometrySource</c>'s navmesh input, which is correct — they were bounding the bake
    /// to the same ±20 box.</para>
    /// </remarks>
    private void NeutralizeInfinitePlaneColliders(Scene scene)
    {
        var victims = new List<(global::Stride.Engine.Entity Entity, StaticColliderComponent Collider)>();
        foreach (var entity in scene.Entities)
        {
            CollectInfinitePlaneColliders(entity, victims);
        }

        foreach (var (entity, collider) in victims)
        {
            entity.Components.Remove(collider);
            Log.Info(
                "[StrideHrotGame] CE-232: removed an INFINITE plane collider from '{0}' at ({1:F1},{2:F1},{3:F1}). " +
                "A Bullet static plane is a half-space, so it made every scenario coordinate beyond it solid " +
                "matter and expelled body-owning entities back into the template arena.",
                entity.Name,
                entity.Transform.Position.X, entity.Transform.Position.Y, entity.Transform.Position.Z);
        }

        if (victims.Count == 0)
        {
            Log.Info("[StrideHrotGame] CE-232: no infinite plane colliders in the scene — nothing to neutralise.");
        }
        else
        {
            Log.Info(
                "[StrideHrotGame] CE-232: neutralised {0} infinite plane collider(s); scenario coordinates " +
                "outside the template arena are now free space.",
                victims.Count);
        }
    }

    /// <summary>
    /// Depth-first walk collecting every <see cref="StaticColliderComponent"/> that carries at least one
    /// <c>StaticPlaneColliderShapeDesc</c>. Collected first and mutated after, so the scene graph is never
    /// modified while it is being walked.
    /// </summary>
    /// <remarks>⭐ <c>internal</c> rather than <c>private</c> so
    /// <c>InfinitePlaneColliderNeutralisationTests</c> can assert the REAL filter. ⛔ A test-local copy of
    /// this predicate would pass while production widened — the exact blindness <c>CE-223</c> shipped on.</remarks>
    internal static void CollectInfinitePlaneColliders(
        global::Stride.Engine.Entity entity,
        List<(global::Stride.Engine.Entity Entity, StaticColliderComponent Collider)> victims)
    {
        var collider = entity.Get<StaticColliderComponent>();
        if (collider != null)
        {
            foreach (var shapeDesc in collider.ColliderShapes)
            {
                if (shapeDesc is StaticPlaneColliderShapeDesc)
                {
                    victims.Add((entity, collider));
                    break;
                }
            }
        }

        foreach (var childTransform in entity.Transform.Children)
        {
            CollectInfinitePlaneColliders(childTransform.Entity, victims);
        }
    }

    /// <summary>
    /// Recursively removes an entity and all its children from the scene.
    /// </summary>
    private static void RemoveEntityAndChildren(Scene scene, global::Stride.Engine.Entity root)
    {
        // Collect children first (copy to avoid modifying the collection while iterating).
        var children = new List<global::Stride.Engine.Entity>();
        foreach (var child in root.Transform.Children)
        {
            children.Add(child.Entity);
        }

        // Recurse into children first.
        foreach (var child in children)
        {
            RemoveEntityAndChildren(scene, child);
        }

        // Remove from scene.
        scene.Entities.Remove(root);
    }

    /// <summary>
    /// Adds a fixed overview camera to the scene at a position and orientation
    /// that frames the spawn area.
    ///
    /// <para>
    /// Camera placement reasoning:
    /// The static arena occupies roughly Stride Z=[0,15], X=[−10,+10], Y=0 (ground).
    /// We place the camera at Stride (0, 10, −5) looking toward the spawn area at
    /// Stride (0, 0, 5) — i.e. looking in the +Z (North) direction and downward.
    /// The look-vector (0,0,5)−(0,10,−5) = (0,−10,10) → normalized = (0, −0.707, 0.707).
    /// This is roughly a 45° downward angle, giving a good overview of units standing at
    /// FDP Y=5–7, Z=0 (Stride Z=5–7, Y=0).
    /// </para>
    ///
    /// <para>
    /// A directional light is also added pointing roughly downward-forward so the models
    /// are lit rather than appearing pitch-black.
    /// </para>
    /// </summary>
    private void AddFixedCamera(Scene scene)
    {
        // ── Camera entity ─────────────────────────────────────────────────
        var cameraEntity = new global::Stride.Engine.Entity("DemoCamera");

        // Position: Stride (0, 10, -5).
        cameraEntity.Transform.Position = new Vector3(0f, 10f, -5f);

        // Rotation: look from (0,10,-5) toward (0,0,5).
        // Direction vector = (0,0,5)-(0,10,-5) = (0,-10,10), normalized = (0,-0.707,0.707).
        // Pitch angle = atan2(-10, 10) ≈ -45° around X axis.
        // Yaw = 0° (facing +Z).
        // Stride rotation order: Quaternion from yaw(Y) * pitch(X) * roll(Z).
        var pitchRad = (float)(-Math.PI / 4.0);  // -45 degrees pitch
        cameraEntity.Transform.Rotation = Quaternion.RotationX(pitchRad);

        // Attach a CameraComponent.
        // [VERIFY] CameraComponent constructor, Stride 4.2.1.2487.
        var camera = new CameraComponent
        {
            Projection         = CameraProjectionMode.Perspective,
            VerticalFieldOfView = 60f,
            NearClipPlane       = 0.1f,
            FarClipPlane        = 1000f,
        };
        cameraEntity.Add(camera);

        // ── Free-flight controller (follow-up to BATCH-10) ────────────────
        // Attach the standard Stride free-look controller so the human can fly
        // the camera around to inspect the live scene and locate the spawned units.
        //   WASD / arrows — move (forward/back/strafe), in the camera's local frame
        //   Q / E         — move down / up (world-up stabilised)
        //   Right-mouse-drag — look (yaw/pitch); cursor is hidden while held
        //   Shift         — speed boost (SpeedFactor multiplier)
        // The camera keeps its initial overview position/orientation below, so it
        // starts looking at the spawn area but is now movable.
        cameraEntity.Add(new BasicCameraController());

        // ── Bind the camera to the GraphicsCompositor's camera slot ───────
        // A CameraComponent only renders if its Slot is bound to a camera slot
        // used by the GraphicsCompositor's SceneCameraRenderer. The template
        // PlayerCharacter (removed in NeutralizeTemplatePlayer) previously owned
        // the camera that filled the compositor's slot, so without this binding
        // the compositor would have no active camera and the window would render
        // black.
        //
        // VERIFIED (Stride 4.2.1.2487, Stride.Engine.dll metadata):
        //   - SceneSystem.GraphicsCompositor : GraphicsCompositor
        //   - GraphicsCompositor.Cameras      : SceneCameraSlotCollection (IList<SceneCameraSlot>)
        //   - SceneCameraSlot.ToSlotId()      : SceneCameraSlotId
        //   - CameraComponent.Slot            : SceneCameraSlotId (field)
        BindCameraToCompositorSlot(camera);

        scene.Entities.Add(cameraEntity);

        // Remember the camera entity so the test harness context can expose it.
        _cameraEntity = cameraEntity;

        // ── Directional light ─────────────────────────────────────────────
        // Add a light so spawned models are visible (not pitch-black).
        var lightEntity = new global::Stride.Engine.Entity("DemoDirectionalLight");

        // Point roughly downward and forward (-Y dominant, +Z component).
        var lightYaw   = 0f;
        var lightPitch = (float)(-Math.PI / 3.0);  // -60 degrees from horizontal
        lightEntity.Transform.Rotation =
            Quaternion.RotationYawPitchRoll(lightYaw, lightPitch, 0f);

        var light = new LightComponent
        {
            Type      = new LightDirectional(),
            Intensity = 1.0f,
        };
        lightEntity.Add(light);

        scene.Entities.Add(lightEntity);
    }

    /// <summary>
    /// Binds the supplied <see cref="CameraComponent"/> to the first camera slot of the
    /// active <see cref="Stride.Rendering.Compositing.GraphicsCompositor"/> so the
    /// compositor's <c>SceneCameraRenderer</c> actually renders through this camera.
    ///
    /// <para>
    /// VERIFIED against Stride 4.2.1.2487 (Stride.Engine.dll / Stride.Engine.xml):
    /// <list type="bullet">
    ///   <item><c>SceneSystem.GraphicsCompositor</c> — the active compositor for the running game.</item>
    ///   <item><c>GraphicsCompositor.Cameras</c> — a <c>SceneCameraSlotCollection</c> (indexable list of
    ///     <c>SceneCameraSlot</c>); the default DefaultGraphicsCompositorLevel10 archetype has one slot.</item>
    ///   <item><c>SceneCameraSlot.ToSlotId()</c> — produces the <c>SceneCameraSlotId</c> that a
    ///     <c>CameraComponent.Slot</c> field references.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// If the compositor has zero camera slots there is nothing to bind to (the compositor itself
    /// would need a camera slot to render any camera); in that case we emit a loud warning rather
    /// than silently producing a black screen.
    /// </para>
    /// </summary>
    private void BindCameraToCompositorSlot(CameraComponent camera)
    {
        var compositor = SceneSystem.GraphicsCompositor;
        if (compositor == null)
        {
            Log.Warn("SceneSystem.GraphicsCompositor is null — cannot bind DemoCamera to a " +
                     "camera slot. The scene will render black.");
            return;
        }

        if (compositor.Cameras.Count > 0)
        {
            // Bind to the first (and, for the default compositor, only) camera slot.
            camera.Slot = compositor.Cameras[0].ToSlotId();
        }
        else
        {
            Log.Warn("GraphicsCompositor has zero camera slots — DemoCamera cannot be bound and " +
                     "the scene will render black. The GraphicsCompositor needs at least one SceneCameraSlot.");
        }
    }


    // ── BATCH-18: Navmesh bake ────────────────────────────────────────────

    /// <summary>
    /// Extracts geometry from the arena's static colliders, bakes a DotRecast navmesh for
    /// the Vehicle layer (and Infantry layer), constructs a <see cref="DotRecastNavmeshProvider"/>,
    /// and registers it as the <c>INavmeshProvider</c> singleton in the FDP world.
    ///
    /// <para>
    /// Called from <see cref="BootEditorSubsystem"/> after <c>Initialize</c> so both the
    /// scene and the FDP world are ready. Failure is guarded: any exception logs a loud Warn
    /// and leaves <see cref="_navmeshProvider"/> null. The F4 demo checks for null and logs
    /// "navmesh unavailable" rather than crashing.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b><c>CE-248</c> — PARAMETERISED so BOTH modes bake the same navmesh from the same code.</b>
    /// It used to read <c>_editorSubsystem.World</c> and <c>_editorSubsystem.InfantryCrowdProvider</c>
    /// directly and <c>return</c> early when that field was null — which is precisely mode 2, where
    /// there is no <c>EditorStrideSubsystem</c> at all. The two things it actually needs are a world to
    /// publish the singleton into and a crowd provider to seed, so it now takes them.
    ///
    /// <para>📌 <b>Why this was the last gate.</b> Measured 2026-09-09: with <c>CE-242</c>..<c>CE-247</c>
    /// in place the mode-2 node owned its entities, promoted them, created six Bullet bodies and ran the
    /// motors — and still nothing moved, because <c>VehicleNavigationIntentSystem.Execute</c> opens with
    /// a <c>INavmeshProvider</c> lookup and returns immediately when there is none ("graceful no-op when
    /// no navmesh is available"). No navmesh ⇒ no route ⇒ <c>NavigationStatus.Phase</c> never leaves
    /// <c>Idle</c> ⇒ the vehicle motor is never commanded. ⛔ Nothing logged: three separate silent
    /// no-ops (this one, <c>TryCreateVisual</c>'s missing render-def, and the ownership fallback's empty
    /// grant list) sat in a row on the same path.</para>
    ///
    /// <para>⚠ Kept as one method rather than copied into the mode-2 boot: the bake, its layer mask, its
    /// failure guards and its crowd seeding are exactly the same work in both modes. 🔒 The user's own
    /// standing instruction on the human/vehicle kinematics split — "something shareable, parametrizing
    /// shared code" — applies unchanged here.</para>
    /// </remarks>
    private void BakeNavmesh(
        global::Stride.Engine.Scene scene,
        Fdp.Core.EntityRepository? world,
        Hrot.Stride.Core.DotRecastDtCrowdProvider? infantryCrowdProvider)
    {
        if (world == null)
        {
            Log.Warn("[StrideHrotGame] BakeNavmesh: no world — cannot bake.");
            return;
        }

        try
        {
            // Extract geometry from all static colliders in the scene.
            var geoSource = new StrideSceneGeometrySource(scene);
            if (!geoSource.TryGetTriangles(out float[] verts, out int[] indices))
            {
                Log.Warn("[StrideHrotGame] BakeNavmesh: TryGetTriangles returned false " +
                         "(no geometry extracted). Navmesh will be unavailable.");
                return;
            }

            // Bake Vehicle + Infantry layers.
            var baker  = new StrideNavmeshBaker();
            var meshes = baker.Bake(verts, indices,
                NavLayerMask.Vehicle | NavLayerMask.Infantry);

            if (meshes.Count == 0)
            {
                Log.Warn("[StrideHrotGame] BakeNavmesh: baker returned 0 meshes " +
                         "(geometry may be too small or entirely blocked). Navmesh will be unavailable.");
                return;
            }

            // Construct the provider and register as the INavmeshProvider singleton.
            _navmeshProvider = new DotRecastNavmeshProvider(meshes);
            world.SetSingletonManaged<INavmeshProvider>(_navmeshProvider);

            // BATCH-19: supply the Infantry DtNavMesh to the deferred crowd provider so
            // real DotRecast crowd steering is active for infantry entities.
            if (infantryCrowdProvider != null
                && _navmeshProvider.TryGetNavMesh(NavLayerMask.Infantry, out var infantryMesh)
                && infantryMesh != null)
            {
                bool crowdInit = infantryCrowdProvider.TryInitializeNavMesh(infantryMesh);
                _infantryCrowdProviderInitialized = crowdInit;
                if (crowdInit)
                    Log.Info("[StrideHrotGame] Infantry DotRecastDtCrowdProvider initialized (BATCH-19, STR-D19).");
                else
                    Log.Warn("[StrideHrotGame] DotRecastDtCrowdProvider.TryInitializeNavMesh returned false — " +
                             "crowd provider was already initialized (unexpected). F5 may still work.");
            }
            else
            {
                Log.Warn("[StrideHrotGame] Infantry navmesh layer not baked or InfantryCrowdProvider null — " +
                         "crowd steering will remain in no-op mode. F5 'Navmesh Walk' will degrade gracefully.");
            }

            // Log bake summary per layer.
            foreach (var kv in meshes)
            {
                int polyCount = 0;
                for (int t = 0; t < kv.Value.GetMaxTiles(); t++)
                {
                    var tile = kv.Value.GetTile(t);
                    if (tile?.data?.header != null)
                        polyCount += tile.data.header.polyCount;
                }
                Log.Info("[StrideHrotGame] Navmesh baked: layer={0} polys={1} verts={2}",
                    kv.Key, polyCount, verts.Length / 3);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "[StrideHrotGame] BakeNavmesh: unexpected exception — navmesh unavailable. " +
                         "F4 demo will log 'navmesh unavailable' and abort cleanly.");
            _navmeshProvider = null;
        }
    }
}
