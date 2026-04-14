using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Hrot.Core.Mission;
using Hrot.Core.Network;
using Hrot.IG.Components;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using Fdp.ModuleHost_Core.Network.Interfaces;

namespace Hrot.IG.UI;

/// <summary>
/// Pure-logic form state driving the Mini-ExCon spawner panel (IG.5.3).
///
/// Holds user-supplied form data (TKB type, affiliation, spawn coordinates)
/// and exposes a <see cref="Submit"/> method that constructs and publishes a
/// <see cref="SpawnEntityCommand"/> onto the event bus — mirroring the
/// <see cref="Hrot.IG.Tools.CreationTool"/> click path without requiring a
/// canvas interaction.
///
/// The <see cref="OnCommandPublished"/> event allows tests and integrators to
/// observe emitted commands without subscribing to the bus.
/// </summary>
public class MiniExConPanelState
{
    // ── Form fields ───────────────────────────────────────────────────────────

    private readonly long _localNodeId;

    /// <summary>
    /// TKB template type to spawn.  Defaults to <see cref="MiniExConPanelConstants.DefaultTkbType"/>.
    /// </summary>
    public long TkbType { get; set; } = MiniExConPanelConstants.DefaultTkbType;

    /// <summary>Force affiliation to assign to the spawned entity.</summary>
    public ForceId Affiliation { get; set; } = ForceId.Unknown;

    /// <summary>Initial world-space X position (metres) for the spawned entity.</summary>
    public float PositionX { get; set; }

    /// <summary>Initial world-space Y position (metres) for the spawned entity.</summary>
    public float PositionY { get; set; }

    /// <summary>Filter text for the TKB type browser list.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c> the spawn uses the explicit <see cref="PositionX"/> / <see cref="PositionY"/>
    /// values entered by the operator.
    /// When <c>false</c> (default) a random position within <see cref="RandomSpawnRadius"/> metres of
    /// the world origin is generated automatically — useful for quick-spawn without needing to
    /// pick a map location first.
    /// </summary>
    public bool UseSpecificCoordinates { get; set; } = false;

    /// <summary>Half-extent (metres) of the random spawn area when <see cref="UseSpecificCoordinates"/> is <c>false</c>.
    /// Default 1000 m.</summary>
    public float RandomSpawnRadius { get; set; } = 1000f;

    // RNG instance shared across calls; not thread-safe (panel state is single-threaded).
    private static readonly Random _rng = new();

    private IGeographicTransform? _geoTransform;

    /// <summary>Creates a new <see cref="MiniExConPanelState"/> with an optional node ID for log messages.</summary>
    public MiniExConPanelState(long localNodeId = 0) => _localNodeId = localNodeId;

    // ── Testability hook ──────────────────────────────────────────────────────

    /// <summary>
    /// Raised synchronously inside <see cref="Submit"/> immediately after the
    /// <see cref="SpawnEntityCommand"/> is published, so tests can inspect the
    /// command without consuming it from the bus.
    /// </summary>
    public event Action<SpawnEntityCommand>? OnCommandPublished;

    /// <summary>
    /// Supplies the geodetic transform used to convert map-space spawn coordinates
    /// into DDS GeoSpatial descriptors for networked spawns.
    /// </summary>
    /// <param name="geoTransform">Transform instance shared with DDS translators.</param>
    public void SetGeoTransform(IGeographicTransform? geoTransform) => _geoTransform = geoTransform;

    // ── Submit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a <see cref="SpawnEntityCommand"/> from the current form fields
    /// and publishes it to <paramref name="eventBus"/>.
    ///
    /// The command mirrors the <c>CreationTool</c> contract:
    /// <list type="bullet">
    ///   <item><see cref="SpawnEntityCommand.NetworkId"/> = 0 (SimHost allocates).</item>
    ///   <item><see cref="SpawnEntityCommand.OwnerNodeId"/> = <see cref="IgNetworkConstants.LocalNodeId"/>.</item>
    ///   <item><see cref="SpawnEntityCommand.InitType"/> = <see cref="ReliableInitType.None"/>.</item>
    ///   <item><c>InitialComponents</c> contains a <see cref="SimTransform"/> at the form position
    ///         and an <see cref="Hrot.IG.Components.IgSymbolOverride"/> carrying the chosen affiliation.</item>
    /// </list>
    /// </summary>
    /// <param name="eventBus">The application event bus; must not be <c>null</c>.</param>
    public void Submit(FdpEventBus eventBus)
    {
        if (eventBus is null) throw new ArgumentNullException(nameof(eventBus));

        var transform = new SimTransform
        {
            Position = new System.Numerics.Vector3(PositionX, PositionY, 0f),
            Rotation = SimMath.FacingEast,
        };

        var symbolOverride = new IgSymbolOverride
        {
            StyleSetId = AffiliationToStyleSetId(Affiliation),
        };

        var cmd = new SpawnEntityCommand
        {
            NetworkId         = 0,
            TkbType           = TkbType,
            DisType           = 0,
            OwnerNodeId       = IgNetworkConstants.LocalNodeId,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object> { transform, symbolOverride },
            RequestId         = Guid.NewGuid(),
        };

        eventBus.PublishManaged(cmd);
        OnCommandPublished?.Invoke(cmd);
    }

    // ── Submit via gateway (network path) ─────────────────────────────────────

    /// <summary>
    /// Sends a create-entity command to SimHost via the neutral command gateway.
    /// Fire-and-forget: the gateway ACK is not awaited.
    /// Logs a warning and returns silently when <paramref name="gateway"/> is <c>null</c>
    /// (i.e. when the network is disabled).
    /// </summary>
    /// <param name="gateway">Live command gateway; may be <c>null</c> when network is off.</param>
    public void SubmitViaGateway(ICommandGateway? gateway)
    {
        if (gateway == null)
        {
            FdpLog<MiniExConPanelState>.Warn("[Node-{0}] Network disabled -- spawn request ignored.", _localNodeId);
            return;
        }

        double lat = 0, lon = 0, alt = 0;
        if (_geoTransform != null)
        {
            float spawnX = UseSpecificCoordinates ? PositionX : (_rng.NextSingle() * 2f - 1f) * RandomSpawnRadius;
            float spawnY = UseSpecificCoordinates ? PositionY : (_rng.NextSingle() * 2f - 1f) * RandomSpawnRadius;
            var local = new Vector3(spawnX, spawnY, 0f);
            (lat, lon, alt) = _geoTransform.ToGeodetic(local);
        }

        var cmd = new CreateEntityCommand
        {
            TkbType   = TkbType,
            Latitude  = lat,
            Longitude = lon,
            Altitude  = alt,
            ForceId   = (int)(byte)Affiliation,
        };

        _ = gateway.CreateEntityAsync(cmd); // fire-and-forget
    }

    // ── Submit via gateway with wander mission (network path) ─────────────────

    /// <summary>
    /// Creates an entity via <see cref="CreateEntityRequest"/> and immediately assigns a
    /// <c>WanderMilitary</c> mission so the entity moves continuously around the origin.
    ///
    /// The method awaits the <see cref="CreateEntityAck"/> to obtain the allocated entity ID,
    /// then sends a <see cref="MissionControlRequest"/> with <c>CMD_REPLACE_MISSION</c> carrying
    /// a single-task <see cref="MissionPlan"/> using the <c>WanderMilitary</c> doctrine.
    ///
    /// Returns silently and logs a warning when <paramref name="gateway"/> is <c>null</c>.
    /// </summary>
    /// <param name="gateway">Live command gateway; may be <c>null</c> when network is off.</param>
    public async Task<long> SubmitWithWanderMissionViaGateway(ICommandGateway? gateway)
    {
        if (gateway == null)
        {
            FdpLog<MiniExConPanelState>.Warn("[Node-{0}] Network disabled -- wander spawn ignored.", _localNodeId);
            return 0L;
        }

        double lat = 0, lon = 0, alt = 0;
        if (_geoTransform != null)
        {
            float spawnX = UseSpecificCoordinates ? PositionX : (_rng.NextSingle() * 2f - 1f) * RandomSpawnRadius;
            float spawnY = UseSpecificCoordinates ? PositionY : (_rng.NextSingle() * 2f - 1f) * RandomSpawnRadius;
            var local = new Vector3(spawnX, spawnY, 0f);
            (lat, lon, alt) = _geoTransform.ToGeodetic(local);
        }

        var createCmd = new CreateEntityCommand
        {
            TkbType   = TkbType,
            Latitude  = lat,
            Longitude = lon,
            Altitude  = alt,
            ForceId   = (int)(byte)Affiliation,
        };

        int entityId;
        try
        {
            entityId = await gateway.CreateEntityAsync(createCmd);
        }
        catch (Exception ex)
        {
            FdpLog<MiniExConPanelState>.Error("[Node-{0}] CreateEntityAsync failed: {1}", _localNodeId, ex.Message);
            return 0L;
        }

        if (entityId <= 0)
        {
            FdpLog<MiniExConPanelState>.Warn(
                "[Node-{0}] CreateEntityAsync returned invalid entity id {1} -- mission assignment skipped.", _localNodeId, entityId);
            return 0L;
        }

        var taskId = Guid.NewGuid();
        var missionTask = new Hrot.Core.Mission.MissionTask
        {
            TaskId           = taskId,
            ExecutingEngine  = "CGFX",
            BehaviorId       = "WanderMilitary",
            BehaviorParams   = string.Empty,
            Triggers         = new List<Hrot.Core.Mission.MissionTrigger>(), // single-task plan – no advancement
            State            = Hrot.Core.Mission.eTaskState.TASK_PLANNED,
        };

        var missionPlan = new Hrot.Core.Mission.MissionPlan
        {
            ActiveTaskId = taskId,
            Tasks        = new List<Hrot.Core.Mission.MissionTask> { missionTask },
        };

        var missionCmd = new MissionControlCommand
        {
            EntityId    = entityId,
            CommandType = Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION,
            Plan        = missionPlan,
            BaseVersion = 0,
        };

        try
        {
            var result = await gateway.SendMissionControlRequestAsync(missionCmd);
            if (!result.Success)
            {
                FdpLog<MiniExConPanelState>.Warn(
                    "[Node-{0}] SendMissionControlRequestAsync returned failure for entity {1}: {2}",
                    _localNodeId, entityId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            FdpLog<MiniExConPanelState>.Error("[Node-{0}] SendMissionControlRequestAsync failed: {1}", _localNodeId, ex.Message);
        }

        return entityId;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string AffiliationToStyleSetId(ForceId affiliation) =>
        affiliation switch
        {
            ForceId.Friend  => IgSymbolOverride.StyleSetFriend,
            ForceId.Hostile => IgSymbolOverride.StyleSetHostile,
            ForceId.Neutral => IgSymbolOverride.StyleSetNeutral,
            _               => IgSymbolOverride.StyleSetUnknown,
        };
}
