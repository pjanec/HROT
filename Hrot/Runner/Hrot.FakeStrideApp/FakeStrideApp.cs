using System.IO;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Presentation.Raylib;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Defaults;
using Hrot.Common.Infrastructure;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using Hrot.StrideMock;

namespace Hrot.FakeStrideApp;

/// <summary>
/// Standalone FdpApplication that runs a Stride mock node with a live Raylib window.
///
/// <para>
/// Wraps <see cref="StrideNodeBootstrapper"/> and <see cref="SyncFdpToStrideScript"/>
/// in the FdpApplication lifecycle, mirroring <c>StrideMockSubsystem</c> but as a
/// self-hosted windowed process rather than a subsystem tab.
/// </para>
///
/// <para>
/// OnLoad step order is mandatory (fragile init traps, see DESIGN.md §4.2):
/// DDS participant → network factory → node config → BootstrapNode → TKB → script.Start()
/// </para>
/// </summary>
public sealed class FakeStrideApp : FdpApplication
{
    private readonly int _domainId;
    private readonly int _nodeId;

    private DdsParticipant?        _participant;
    private StrideNodeBootstrapper? _core;
    private SyncFdpToStrideScript?  _script;
    private RaylibInputProvider?    _inputProvider;

    /// <summary>
    /// Initializes the application with window config and node identity.
    /// Does NOT call OnLoad() — the base class calls it inside Run().
    /// </summary>
    public FakeStrideApp(ApplicationConfig config, int domainId, int nodeId)
        : base(config)
    {
        _domainId = domainId;
        _nodeId   = nodeId;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Mandatory order: participant → factory → config → BootstrapNode → TKB → script.
    /// </remarks>
    protected override void OnLoad()
    {
        // 1. DDS participant.
        _participant = HrotEnvironment.CreateParticipant(_domainId);

        // 2. Network factory.
        var entityMap    = new NetworkEntityMap();
        var geoTransform = HrotEnvironment.CreateGeoTransform();
        var eventBus     = new FdpEventBus();
        var networkFactory = new NedNetworkFactory(
            _participant,
            entityMap,
            geoTransform,
            eventBus,
            _nodeId,
            StrideNodeBootstrapper.Role);

        // 3. Node config.
        var nodeConfig = new HrotNodeConfig
        {
            DomainId      = _domainId,
            NodeId        = _nodeId,
            Headless      = false,
            SubsystemName = "StrideMock",
            LocalTempRoot = Path.Combine(
                OrchestrationConstants.DefaultStagingDirectory,
                "nodes", $"node-{_nodeId}"),
            LogDirectory  = Path.Combine(AppContext.BaseDirectory, "logs"),
        };

        // 4. Bootstrap node (registers NED TKB catalog internally via HrotEnvironment.CreateTkb()).
        _core = new StrideNodeBootstrapper();
        _core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, networkFactory);

        // 5. Populate TKB AFTER BootstrapNode.
        // NOTE: Do NOT call DemoTkbSetup.RegisterAll(tkb) here.
        // HrotNodeBuilder.Build() already calls HrotEnvironment.CreateTkb() which calls
        // NedTkbCatalog.RegisterAll(tkb), registering TkbEntityTypes.Tank_M1Abrams = 100.
        // Calling DemoTkbSetup.RegisterAll again would throw a duplicate-key exception.
        var tkb = _core.Context.TkbDb;
        if (tkb != null)
            UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb); // IDs 1001-2003

        // 6. Script.
        _script = new SyncFdpToStrideScript(_core);
        _script.Start();

        // 7. Input provider (requires Raylib window already open).
        _inputProvider = new RaylibInputProvider();
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float dt)
    {
        if (_core == null || _script == null || _inputProvider == null)
            return;

        _core.Camera.HandleInput(_inputProvider);
        _core.Camera.Update(dt);
        _script.Update(dt);
        _core.Tick(dt);
    }

    /// <inheritdoc/>
    protected override void OnDrawWorld()
    {
        if (_core == null || _script == null)
            return;

        _core.Camera.BeginMode();

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
    protected override void OnDrawUI()
    {
        if (_script == null)
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
    /// <remarks>
    /// Do NOT call base.OnUnload() — FdpApplication.OnUnload() disposes Kernel/World
    /// which are owned by StrideNodeBootstrapper, not by FakeStrideApp directly.
    /// </remarks>
    protected override void OnUnload()
    {
        _core?.Dispose();
        _core   = null;
        _script = null;

        _participant?.Dispose();
        _participant = null;
    }
}
