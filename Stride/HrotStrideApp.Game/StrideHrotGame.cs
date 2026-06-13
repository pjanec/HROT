#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using Fdp.Toolkit.Navigation;
using Hrot.Stride.Core;
using Hrot.Stride.Core.TestHarness;
using Hrot.StrideMock;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Input;
using Stride.Physics;
using Stride.Rendering;
using Stride.Rendering.Lights;

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

    /// <summary>
    /// Frame counter used to throttle the spawn-diagnostics log in <see cref="Update"/>
    /// to roughly once per second (every <see cref="DiagnosticsLogIntervalFrames"/> frames).
    /// </summary>
    private int _diagnosticsFrameCounter;

    /// <summary>Throttle interval (in render frames) for the spawn-diagnostics log.</summary>
    private const int DiagnosticsLogIntervalFrames = 60;

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
        base.Update(gameTime);

        float wallDt = (float)gameTime.Elapsed.TotalSeconds;

        // Internal-loop mode (BATCH-10): drive EditorStrideSubsystem.
        if (_editorSubsystem != null)
        {
            // FIX-PERF-1 (hosted-mode substepping):
            // When the editor subsystem is hosting the real EditorSubsystem
            // (STRIDE_HOST_REAL_EDITOR=1), call Tick ONCE per render frame with the wall dt
            // — the fixed-step loop driver would cause up to 8 sub-steps per frame, each
            // running the full editor.Update() (canvas + AI hot-reload + kernel + Bullet),
            // causing a spiral-of-death at low render rates.
            // The OFF path (self-contained kernel) keeps the loop driver unchanged.
            if (_editorSubsystem.HostRealEditor)
            {
                _editorSubsystem.Tick(wallDt);
            }
            else
            {
                _loopDriver.AdvanceFrame(wallDt, dt => _editorSubsystem.Tick(dt));
            }

            // Spawn diagnostics (follow-up to BATCH-10): throttled to ~once per second.
            LogSpawnDiagnostics();
        }

        // BATCH-12: drive the in-app test harness (keyboard polling + continuous-case
        // hooks + on-screen DebugText status). Uses the render-frame wall delta so the
        // orbiting-ghost demo advances smoothly regardless of the fixed sim cadence.
        _testHarness?.Update(wallDt);

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
                // User closed the second window — dispose it and null out so subsequent frames skip.
                _inspectorWindow.Dispose();
                _inspectorWindow = null;
                Log.Info("[StrideHrotGame] Inspector window closed by user.");
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

    // ── Boot helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Boots the EditorStrideSubsystem on the live scene.
    /// Called from <see cref="BeginRun"/> after scene and content are valid.
    /// </summary>
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

        // ── 3. Add a fixed overview camera ────────────────────────────────
        // Camera position in Stride space: (0, 10, -5).
        // Looking toward the spawn area around Stride (0, 0, 5).
        // This gives a roughly 45° downward angle from the north/rear side.
        AddFixedCamera(scene);

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
        // ── 4a. Flag-gated hosted-editor mode (STRIDE_HOST_REAL_EDITOR=1) ─────────
        // When the env var is set, EditorStrideSubsystem boots the real EditorSubsystem
        // headlessly and reuses its World/Kernel/TimeController. Default = OFF (today's path).
        bool hostRealEditor = string.Equals(
            System.Environment.GetEnvironmentVariable("STRIDE_HOST_REAL_EDITOR"),
            "1",
            StringComparison.Ordinal);
        if (hostRealEditor)
            Log.Info("[StrideHrotGame] STRIDE_HOST_REAL_EDITOR=1 — hosted-editor mode ENABLED.");
        else
            Log.Info("[StrideHrotGame] STRIDE_HOST_REAL_EDITOR not set — self-contained kernel mode (default).");

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
        // Pass hostRealEditor so the subsystem knows whether to boot its own kernel or
        // delegate to the real EditorSubsystem (STRIDE_HOST_REAL_EDITOR=1 path).
        // Pass buildEditorUi so the hosted EditorSubsystem is initialized non-headless when
        // the second raylib window is also enabled (STRIDE_EDITOR_WINDOW=1) — this activates
        // MapCanvas, adapters, layers, and all ImGui panels inside the editor so that
        // RegisterWindows/DrawWorld/DrawUI work correctly.
        bool buildEditorUi = hostRealEditor && StrideInspectorWindowConfig.IsEnabled;
        _editorSubsystem.Initialize(visualFactory, blendTreeInstaller, bulletService, debugDrawSink,
            hostRealEditor: hostRealEditor,
            buildEditorUi: buildEditorUi);

        // ── 4b. Bake navmesh from arena static colliders (BATCH-18, STR-D19) ─────────
        // Runs after Initialize so the scene+physics are ready. The baked provider
        // overwrites the FakeNavmeshProvider set up by the simulation logic packs.
        // Guarded: bake failure logs Warn and leaves _navmeshProvider null (F4 demo
        // handles the null case gracefully with a loud log rather than crashing).
        BakeNavmesh(scene);

        // ── 5. Enqueue demo UrbanCombat spawns ────────────────────────────
        // Spawn 4 InfantrySoldiers (TkbType 2002) + 2 MilitaryAPC vehicles (TkbType 2001).
        // FDP coords: X=East, Y=North, Z=Up.
        // Swizzle to Stride: (fdp.X, fdp.Z, fdp.Y).
        //
        // Arena center is at Stride (0, 0, 5), which is FDP (0, 5, 0).
        // Camera is at Stride (0, 10, -5) looking toward Stride (0, 0, 5).
        // We place entities in a loose line at FDP Y=5, FDP Z=0 (ground level),
        // spread along FDP X (East).
        //
        // Infantry soldier spawn positions (FDP):
        //   Infantry 1: (−3, 5, 0) → Stride (−3, 0,  5)
        //   Infantry 2: (−1, 5, 0) → Stride (−1, 0,  5)
        //   Infantry 3: ( 1, 5, 0) → Stride ( 1, 0,  5)
        //   Infantry 4: ( 3, 5, 0) → Stride ( 3, 0,  5)
        // Vehicle spawn positions (FDP):
        //   Vehicle 1:  (−5, 7, 0) → Stride (−5, 0,  7)
        //   Vehicle 2:  ( 5, 7, 0) → Stride ( 5, 0,  7)
        //
        // All entities at FDP Z=0 (ground level, Stride Y=0).
        // The camera at Stride (0, 10, -5) looks roughly toward Stride Z+ (North in FDP),
        // so all spawns at Z=5 and Z=7 are directly in front of the camera.
        EnqueueDemoSpawns();

        // ── 6. Build the in-app test harness (BATCH-12, STR-TEST-1) ───────
        BuildTestHarness(scene);

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

    /// <summary>
    /// Enqueues 6 UrbanCombat demo spawn requests into <see cref="EditorStrideSubsystem.ScenarioSource"/>.
    /// See method body comments for the exact FDP → Stride position mapping.
    /// </summary>
    private void EnqueueDemoSpawns()
    {
        if (_editorSubsystem == null)
            throw new InvalidOperationException("EditorStrideSubsystem must be initialized before enqueueing spawns.");

        // FDP identity rotation (facing north = default).
        var identityRotation = System.Numerics.Quaternion.Identity;

        // Helper: enqueue one spawn.
        void Spawn(long tkbType, float fdpX, float fdpY, float fdpZ)
        {
            _editorSubsystem.ScenarioSource.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = 0,        // localNodeId=0 → authority granted immediately
                TkbType            = tkbType,
                InitialComponents  = new List<object>
                {
                    new SimTransform
                    {
                        Position = new System.Numerics.Vector3(fdpX, fdpY, fdpZ),
                        Rotation = identityRotation,
                    },
                    new TkbIdentity { TkbType = tkbType },
                },
            });
        }

        // 4 InfantrySoldiers (TkbType 2002 = mannequinModel) at FDP Y=5 (center of arena)
        Spawn(tkbType: 2002L, fdpX: -3f, fdpY: 5f, fdpZ: 0f); // → Stride (−3, 0,  5)
        Spawn(tkbType: 2002L, fdpX: -1f, fdpY: 5f, fdpZ: 0f); // → Stride (−1, 0,  5)
        Spawn(tkbType: 2002L, fdpX:  1f, fdpY: 5f, fdpZ: 0f); // → Stride ( 1, 0,  5)
        Spawn(tkbType: 2002L, fdpX:  3f, fdpY: 5f, fdpZ: 0f); // → Stride ( 3, 0,  5)

        // 2 MilitaryAPC vehicles (TkbType 2001 = Box2x1x1) slightly deeper in the arena
        Spawn(tkbType: 2001L, fdpX: -5f, fdpY: 7f, fdpZ: 0f); // → Stride (−5, 0,  7)
        Spawn(tkbType: 2001L, fdpX:  5f, fdpY: 7f, fdpZ: 0f); // → Stride ( 5, 0,  7)
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
    private void BakeNavmesh(global::Stride.Engine.Scene scene)
    {
        if (_editorSubsystem == null)
        {
            Log.Warn("[StrideHrotGame] BakeNavmesh: EditorStrideSubsystem is null — cannot bake.");
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
            _editorSubsystem.World.SetSingletonManaged<INavmeshProvider>(_navmeshProvider);

            // BATCH-19: supply the Infantry DtNavMesh to the deferred crowd provider so
            // real DotRecast crowd steering is active for infantry entities.
            if (_editorSubsystem.InfantryCrowdProvider != null
                && _navmeshProvider.TryGetNavMesh(NavLayerMask.Infantry, out var infantryMesh)
                && infantryMesh != null)
            {
                bool crowdInit = _editorSubsystem.InfantryCrowdProvider.TryInitializeNavMesh(infantryMesh);
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
