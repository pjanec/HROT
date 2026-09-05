using System;
using System.Numerics;
using Fdp.Toolkit.Runner;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Core;
using Hrot.Common.Infrastructure;
using Hrot.Network.Infrastructure;
using Hrot.Map.Common;
using Hrot.Common;
using Hrot.SimHost;
using Hrot.SimHost.Modules;

using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;
using IMapCameraProvider = Fdp.Toolkit.Runner.IMapCameraProvider;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Subsystem that combines Eyes (IG presentation) and Muscle (ground kinematics) in a
/// single process, built directly on <see cref="HrotNodeBuilder"/> without an inner App class.
///
/// <para>This is the clean pattern for new subsystems introduced in Phase 3.
/// Unlike <see cref="SimHostSubsystem"/> (which wraps <c>SimHostApp</c>), this class
/// constructs all modules inline in <see cref="Initialize"/>.</para>
///
/// <para>The EyesAndMuscle module runs asynchronously via
/// <see cref="ExecutionPolicy.SlowBackground(int)"/> (SoD snapshot at 60 Hz), proving
/// the thread-safe snapshot pattern the Stride renderer will rely on.</para>
/// </summary>
public sealed class EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
{
    // ── Subsystem identity ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => "EyesAndMuscle";

    /// <inheritdoc/>
    /// <remarks>Teal-green — distinct from SimHost (dark red) and IG (green).</remarks>
    public Vector4 TitleBarColor => new(0.15f, 0.40f, 0.25f, 1f);

    // ── Infrastructure ─────────────────────────────────────────────────────────

    private HrotNodeContext?        _context;
    private EyesAndMuscleModule?    _eyesAndMuscleModule;
    private bool                    _initialized;

    // ── Public ECS access (for tests) ─────────────────────────────────────────

    /// <summary>The ECS world after <see cref="Initialize"/>; <c>null</c> before that.</summary>
    public EntityRepository? World => _context?.World;

    /// <summary>The async SoD module after <see cref="Initialize"/>; <c>null</c> before that.</summary>
    public EyesAndMuscleModule? Module => _eyesAndMuscleModule;

    // ── ISubsystem ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {
        if (_initialized) throw new InvalidOperationException("EyesAndMuscleSubsystem is already initialized.");

        // ── Step 1 — Build infrastructure via HrotNodeBuilder ─────────────────
        var nodeCfg = new HrotNodeConfig
        {
            DomainId  = config.DomainId,
            NodeId    = config.NodeId,
            Headless  = config.Headless,    // true → skip DDS + allocator wait
        };
        _context = new HrotNodeBuilder(nodeCfg)
            .WithRole("EyesAndMuscle", NodeRole.MuscleGround | NodeRole.ImageGenerator)
            .WithReplication(NodeRole.MuscleGround | NodeRole.ImageGenerator)
            .Build();

        // ── Step 2 — Register component types (domain-specific, not in builder) ─
        SimHostComponentRegistry.RegisterAll(_context.World);

        // ── Step 3 — Register base infrastructure modules on kernel ──────────
        foreach (var m in _context.BaseModules)
            _context.Kernel.RegisterModule(m);

        // ── Step 4 — Register NedReplicationModule (built by WithReplication) ────
        // MuscleGround | ImageGenerator: kinematic translators (SmartEgress) and IG
        // (EntityStatesIngressPack + DeadReckoning) in one replication layer.
        _context.Kernel.RegisterModule(_context.NedReplication!);

        // ── Step 5 — Create and register EyesAndMuscleModule (async SoD PoC) ───
        // Note: SimulationLogicModule (old SystemGroup API) is not used in this PoC.
        // EyesAndMuscleModule handles the simplified muscle path via its Tick() method.
        _eyesAndMuscleModule = new EyesAndMuscleModule(NodeRole.MuscleGround | NodeRole.ImageGenerator);
        _context.Kernel.RegisterModule(_eyesAndMuscleModule);

        // ── Step 6 — Initialize the kernel ────────────────────────────────────
        _context.Kernel.Initialize();

        _initialized = true;
    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        if (!_initialized || _context == null) return;

        _context.SlaveTranslator?.Tick();   // DDS → bus (null in headless mode)
        _context.ClusterSlave.Tick();       // cluster state machine
#pragma warning disable CS0618 // legacy Update(float) used intentionally in EyesAndMuscleSubsystem
        _context.Kernel.Update(deltaTime);
#pragma warning restore CS0618
        _context.EventBus.SwapBuffers();
    }

    /// <inheritdoc/>
    public void DrawWorld()
    {
        // No MapCanvas in the PoC — visual output is deferred to Stride integration.
    }

    /// <inheritdoc/>
    public void DrawUI()
    {
        // Minimal stub; a real implementation would show entity counts and module state.
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        if (!_initialized) return;

        // QA-001: dispose the whole node context — kernel THEN world. This used to be
        // `_context?.Kernel.Dispose()`, which leaked the EntityRepository on every teardown.
        _context?.Dispose();
        _context?.Participant?.Dispose();
        _initialized = false;
    }

    // ── IMapCameraProvider ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>Returns <c>null</c> — no MapCanvas in the PoC.</remarks>
    public MapCameraView? GetCameraView() => null;

    /// <inheritdoc/>
    public void ApplyCameraView(MapCameraView view) { }

    // Non-interface helper kept for backward-compat.
    public MapCamera? GetMapCamera() => null;

    // ── IWindowRegistrar ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>No windows registered in the PoC.</remarks>
    public void RegisterWindows(WindowManager windowManager) { }
}
