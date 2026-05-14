using System;
using System.Numerics;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.IG.Components;

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
public sealed class StrideMockSubsystem : ISubsystem, IMapCameraProvider
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

        _core = new StrideNodeBootstrapper();
        _core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, _networkFactory);

        // Populate TKB AFTER BootstrapNode.
        // NOTE: The Hrot NED catalog (TkbType 100-505, 8801-8803) is already pre-registered by
        // HrotEnvironment.CreateTkb() inside HrotNodeBuilder.Build(). Only add UrbanCombat
        // types (IDs 1001-2003) which are absent from the NED catalog.
        var tkb = _core.Context.TkbDb;
        if (tkb != null)
            UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb); // IDs 1001-2003

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
