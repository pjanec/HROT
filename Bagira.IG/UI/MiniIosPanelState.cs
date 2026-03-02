using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common.Commands;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.IG.UI;

/// <summary>
/// Pure-logic form state driving the Mini-IOS spawner panel (IG.5.3).
///
/// Holds user-supplied form data (TKB type, affiliation, spawn coordinates)
/// and exposes a <see cref="Submit"/> method that constructs and publishes a
/// <see cref="SpawnEntityCommand"/> onto the event bus — mirroring the
/// <see cref="Bagira.IG.Tools.CreationTool"/> click path without requiring a
/// canvas interaction.
///
/// The <see cref="OnCommandPublished"/> event allows tests and integrators to
/// observe emitted commands without subscribing to the bus.
/// </summary>
public class MiniIosPanelState
{
    // ── Form fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// TKB template type to spawn.  Defaults to <see cref="MiniIosPanelConstants.DefaultTkbType"/>.
    /// </summary>
    public long TkbType { get; set; } = MiniIosPanelConstants.DefaultTkbType;

    /// <summary>Force affiliation to assign to the spawned entity.</summary>
    public ForceId Affiliation { get; set; } = ForceId.Unknown;

    /// <summary>Initial world-space X position (metres) for the spawned entity.</summary>
    public float PositionX { get; set; }

    /// <summary>Initial world-space Y position (metres) for the spawned entity.</summary>
    public float PositionY { get; set; }

    /// <summary>Filter text for the TKB type browser list.</summary>
    public string SearchText { get; set; } = string.Empty;

    private IGeographicTransform? _geoTransform;

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
    ///         and an <see cref="Bagira.IG.Components.IgSymbolOverride"/> carrying the chosen affiliation.</item>
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
    /// Sends a <see cref="CreateEntityRequest"/> to SimHost via the DDS command gateway.
    /// Fire-and-forget: the gateway's ack is not awaited.
    /// Logs a warning and returns silently when <paramref name="gateway"/> is <c>null</c>
    /// (i.e. when the network is disabled).
    /// </summary>
    /// <param name="gateway">Live command gateway; may be <c>null</c> when network is off.</param>
    public void SubmitViaGateway(BdcCommandGateway? gateway)
    {
        if (gateway == null)
        {
            FdpLog<MiniIosPanelState>.Warn("[IG] Network disabled — spawn request ignored.");
            return;
        }

        var descriptors = new List<EntityDescriptorUnion>
        {
            new EntityDescriptorUnion
            {
                _d           = EDescriptorType.dtEntityMaster,
                EntityMaster = new EntityMaster { TkbType = TkbType, DisType = 0 },
            }
        };

        if (_geoTransform != null)
        {
            var local = new Vector3(PositionX, PositionY, 0f);
            var (lat, lon, alt) = _geoTransform.ToGeodetic(local);

            descriptors.Add(new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtGeoSpatial,
                GeoSpatial = new GeoSpatial
                {
                    Pos = new GeoPosition
                    {
                        Latitude  = lat,
                        Longitude = lon,
                        Altitude  = alt,
                    },
                },
            });
        }

        var request = new CreateEntityRequest
        {
            RequestId          = Guid.NewGuid(),
            InitialDescriptors = descriptors,
        };

        _ = gateway.CreateEntityAsync(request); // fire-and-forget
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
