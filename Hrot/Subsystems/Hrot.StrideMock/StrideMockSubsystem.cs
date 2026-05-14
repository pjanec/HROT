using System;
using System.Numerics;
using Fdp.Core.Diagnostics;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.IG.Components;
using Hrot.Presentation.Windows;

namespace Hrot.StrideMock;

/// <summary>
/// Thin <see cref="ISubsystem"/> + <see cref="IMapCameraProvider"/> adapter that
/// embeds a <see cref="StrideNodeBootstrapper"/> core and a
/// <see cref="SyncFdpToStrideScript"/> integration script.
///
/// <para>All simulation logic lives in <see cref="StrideNodeBootstrapper"/> and
/// <see cref="SyncFdpToStrideScript"/>. This class only delegates lifecycle calls
/// and handles rendering.</para>
///
/// <para>Pattern identical to <c>SimHostSubsystem</c>: a thin adapter over the
/// core application class.</para>
/// </summary>
public sealed class StrideMockSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => "StrideMock";

    /// <inheritdoc/>
    /// <remarks>Orange — distinct from SimHost (dark red), IG (green), and ExCon (violet).</remarks>
    public Vector4 TitleBarColor => new Vector4(0.8f, 0.4f, 0.1f, 1f);

    // ── Core ──────────────────────────────────────────────────────────────────

    private readonly Hrot.Core.Network.INetworkFactory _networkFactory;
    private StrideNodeBootstrapper? _core;
    private SyncFdpToStrideScript? _script;
    private Func<bool> _isActiveMapOwner = () => true;
    private bool _headless;

    // ── FDP framework panels ──────────────────────────────────────────────────
    private readonly EntityInspectorPanel _fdpEntityInspector = new();
    private EventBrowserPanel _fdpEventBrowser = null!;
    private readonly DiagnosticEventHistoryService _fdpEventHistory = new();
    private RepositoryAdapter? _fdpRepoAdapter;
    private readonly InspectorState _fdpInspectorState = new();

    /// <summary>
    /// Initializes the subsystem with a network factory injected by the composition root.
    /// </summary>
    /// <param name="networkFactory">
    /// The DDS network factory for this subsystem's isolated participant.
    /// Must not be <see langword="null"/>.
    /// </param>
    public StrideMockSubsystem(Hrot.Core.Network.INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory ?? throw new ArgumentNullException(nameof(networkFactory));
    }

    // ── IMapCameraProvider ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public MapCameraView? GetCameraView() => _core?.Camera.GetCameraView();

    /// <inheritdoc/>
    public void ApplyCameraView(MapCameraView view) => _core?.Camera.ApplyCameraView(view);

    // ── IWindowRegistrar ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void RegisterWindows(WindowManager windowManager)
    {
        if (_core == null) return;

        // Existing Architecture Diagnostics
        windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
            "stridemock_architecture_diagnostics",
            "StrideMock Diagnostics",
            "StrideMock",
            new ArchitectureDiagnosticsPanel(
                new ArchitectureDiagnosticsService(() => _core.Context.Kernel)),
            TitleBarColor));

        // Ensure the repository adapter is created once the world exists
        _fdpRepoAdapter ??= new RepositoryAdapter(_core.Context.World);

        // 1. Entity Inspector
        windowManager.RegisterWindow(new FdpEntityInspectorWindow(
            "stridemock_fdp_inspector",
            "StrideMock Entity Inspector",
            "StrideMock",
            _fdpEntityInspector,
            () => _fdpRepoAdapter,
            () => _fdpInspectorState,
            TitleBarColor));

        // Wire component editor reflector and "Inspect..." context menu
        FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
            _fdpEntityInspector,
            windowManager,
            "StrideMock",
            () => _fdpRepoAdapter,
            null, // No map pick bridge for StrideMock yet
            TitleBarColor);

        // 2. Event Browser
        windowManager.RegisterWindow(new FdpEventBrowserWindow(
            "stridemock_fdp_events",
            "StrideMock Event Browser",
            "StrideMock",
            _fdpEventBrowser,
            TitleBarColor));
    }

    // ── ISubsystem ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {
        _headless = config.Headless;
        _isActiveMapOwner = config.IsActiveMapOwner;

        var nodeConfig = new HrotNodeConfig
        {
            DomainId             = config.DomainId,
            NodeId               = config.NodeId,
            Headless             = config.Headless,
            SubsystemName        = "StrideMock",
            SkipAllocatorRouting = config.Headless,
            LocalTempRoot        = System.IO.Path.Combine(
                OrchestrationConstants.DefaultStagingDirectory,
                "nodes", $"node-{config.NodeId}"),
            LogDirectory         = System.IO.Path.Combine(AppContext.BaseDirectory, "logs"),
        };

        _fdpEventBrowser = new EventBrowserPanel(_fdpEventHistory);

        _core = new StrideNodeBootstrapper();

        // Inject the history capture system into the kernel pipeline (Phase 6d)
        _core.ApplicationSystemsRegistrar = ctx =>
        {
            ctx.Kernel.RegisterGlobalSystem(
                new EventHistoryCaptureSystem("World", _fdpEventHistory, ctx.World.Bus));
        };

        _core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, _networkFactory);

        // Populate TKB AFTER BootstrapNode.
        var tkb = _core.Context.TkbDb;
        if (tkb != null)
        {
            Fdp.Examples.Common.Setup.DemoTkbSetup.RegisterAll(tkb);
            UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb); // IDs 1001-2003
        }

        _script = new SyncFdpToStrideScript(_core);
        _script.Start();
    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        if (_core == null || _script == null)
            return;

        // Gate camera input on active map ownership so background tabs do not
        // consume mouse events. Skip entirely in headless mode (no input provider).
        if (!_headless && _isActiveMapOwner())
            _core.Camera.HandleInput(new RaylibInputProvider());

        _core.Camera.Update(deltaTime);
        _script.Update(deltaTime);
        _core.Tick(deltaTime);
    }

    /// <inheritdoc/>
    /// <remarks>No-op in headless mode.</remarks>
    public void DrawWorld()
    {
        if (_headless || _core == null || _script == null)
            return;

        _core.Camera.BeginMode();

        // Draw consumer-buffer gizmos (remote debug primitives from DDS ingress).
        // DebugPrimitiveRenderer2D.Draw(_core.ConsumerBuffer);  // wire in SM-009

        // Draw fake entities as 2D circles (red, radius 5).
        foreach (var entity in _script.ActiveEntities)
        {
            Raylib_cs.Raylib.DrawCircleV(
                new Vector2(entity.Position.X, entity.Position.Y),
                5f,
                Raylib_cs.Color.Red);
        }

        // Draw visual effects.
        foreach (var effect in _script.ActiveEffects)
        {
            if (effect.Type == EffectType.Explosion)
            {
                // Orange expanding circle fading with alpha.
                var pos    = new Vector2(effect.Position.X, effect.Position.Y);
                float radius = 8f * (1f - effect.Alpha) + 5f;
                var orange = new Raylib_cs.Color(255, 165, 0, (int)(effect.Alpha * 255f));
                Raylib_cs.Raylib.DrawCircleV(pos, radius, orange);
            }
            else if (effect.Type == EffectType.Tracer)
            {
                // Yellow line from Position to TracerEnd.
                Raylib_cs.Raylib.DrawLineV(
                    new Vector2(effect.Position.X, effect.Position.Y),
                    new Vector2(effect.TracerEnd.X, effect.TracerEnd.Y),
                    Raylib_cs.Color.Yellow);
            }
        }

        _core.Camera.EndMode();
    }

    /// <inheritdoc/>
    /// <remarks>No-op in headless mode.</remarks>
    public void DrawUI()
    {
        if (_headless || _script == null)
            return;

        var msg = _script.CurrentStateMessage;
        if (!string.IsNullOrEmpty(msg))
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(20f, 20f));
            ImGuiNET.ImGui.Begin("##StrideMockSplash",
                ImGuiNET.ImGuiWindowFlags.NoTitleBar    | ImGuiNET.ImGuiWindowFlags.NoResize |
                ImGuiNET.ImGuiWindowFlags.NoMove        | ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiNET.ImGuiWindowFlags.NoFocusOnAppearing);
            ImGuiNET.ImGui.Text(msg);
            ImGuiNET.ImGui.End();
        }
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _core?.Dispose();
        _core   = null;
        _script = null;
    }
}
