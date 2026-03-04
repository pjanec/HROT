using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Abstractions;
using Bagira.IG.Components;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Bagira.IG.Tools;

/// <summary>
/// Map tool that translates a left-click on the canvas into a
/// <see cref="CreateEntityRequest"/> written to the DDS network,
/// asking the SimHost (authoritative node) to create a new entity at the clicked
/// world position.
///
/// Workflow:
/// <list type="number">
///   <item>Caller activates the tool (e.g. via <c>canvas.PushTool(creationTool)</c>).</item>
///   <item>Operator sees a ghost preview circle at the cursor.</item>
///   <item>Left-click builds and writes the <see cref="CreateEntityRequest"/> via the
///         injected <see cref="IDdsWriter{T}"/>; the tool then pops itself and returns
///         to the previous tool.</item>
///   <item>Right-click cancels without publishing, and the tool pops itself.</item>
/// </list>
///
/// <see cref="CreateEntityRequest.InitialDescriptors"/> is seeded with:
/// <list type="bullet">
///   <item>A <see cref="EntityDescriptorUnion"/> (dtEntityMaster) carrying the TKB type.</item>
///   <item>A <see cref="EntityDescriptorUnion"/> (dtGeoSpatial) with the click coordinates
///         converted to geodetic lat/lon via <see cref="IGeographicTransform.ToGeodetic"/>
///         when a transform is available, or mapped as
///         <c>Latitude = worldPos.Y, Longitude = worldPos.X</c> in offline/test mode.</item>
/// </list>
///
/// <see cref="CreateEntityRequest.Owner"/> is left as <c>default(NodeId)</c> (all-zeros)
/// so the SimHost assigns itself as the authoritative owner (ghost-node convention).
///
/// No allocations on the hover / draw hot path (CODE-STANDARDS 4).
/// The <see cref="List{T}"/> in <see cref="CreateEntityRequest.InitialDescriptors"/> is
/// only allocated on the click event path.
/// </summary>
public class CreationTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => CreationToolConstants.ToolName;

    private readonly IDdsWriter<CreateEntityRequest> _ddsWriter;
    private readonly IGeographicTransform?           _geoTransform;
    private readonly long                            _tkbType;
    private readonly ForceId                         _affiliation;

    private MapCanvas? _canvas;
    private Vector2    _currentMouseWorld;

    /// <summary>
    /// Raised after a <see cref="CreateEntityRequest"/> has been written so that
    /// tests and integrators can observe the event without parsing DDS traffic.
    /// </summary>
    public event Action<CreateEntityRequest>? OnCommandPublished;

    /// <param name="ddsWriter">
    /// DDS writer for the <c>CreateEntityRequest</c> topic; the SimHost listens on
    /// this topic and creates the entity as the authoritative node.
    /// </param>
    /// <param name="geoTransform">
    /// Optional geographic transform for converting flat map world-space metres to
    /// geodetic (lat/lon) coordinates.  When <c>null</c> (offline / test mode),
    /// <c>worldPos.Y</c> is used as latitude and <c>worldPos.X</c> as longitude.
    /// Pass <see cref="WGS84Transform"/> (or any <see cref="IGeographicTransform"/>)
    /// when running with live DDS so the entity is placed at the correct real-world
    /// geographic position.
    /// </param>
    /// <param name="tkbType">
    /// TKB template type to request.  Defaults to
    /// <see cref="CreationToolConstants.DefaultTkbType"/> when zero is passed.
    /// </param>
    /// <param name="affiliation">Force affiliation for the new entity.</param>
    public CreationTool(
        IDdsWriter<CreateEntityRequest> ddsWriter,
        IGeographicTransform?           geoTransform = null,
        long                            tkbType      = CreationToolConstants.DefaultTkbType,
        ForceId                         affiliation  = ForceId.Unknown)
    {
        _ddsWriter    = ddsWriter ?? throw new ArgumentNullException(nameof(ddsWriter));
        _geoTransform = geoTransform;
        _tkbType      = tkbType == 0 ? CreationToolConstants.DefaultTkbType : tkbType;
        _affiliation  = affiliation;
    }

    //  IMapTool lifecycle 

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas)
    {
        _canvas = canvas;
    }

    /// <inheritdoc/>
    public void OnExit()
    {
        _canvas = null;
    }

    /// <inheritdoc/>
    public void Update(float dt) { /* Stateless between frames. */ }

    //  Input handling 

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            PublishCreateRequest(worldPos);
            _canvas?.PopTool();
            return true;
        }

        if (button == MouseButton.Right)
        {
            // Cancel  return to previous tool without spawning.
            _canvas?.PopTool();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <inheritdoc/>
    /// <remarks>Tracks the cursor world position so <see cref="Draw"/> can render the ghost.</remarks>
    public bool HandleHover(Vector2 worldPos)
    {
        _currentMouseWorld = worldPos;
        return false;
    }

    //  Rendering 

    /// <inheritdoc/>
    /// <remarks>
    /// Draws a semi-transparent ghost circle at the current cursor world position,
    /// with the TKB type code as a label below it.
    /// Called inside <c>MapCanvas.Draw()</c>  <c>Camera.BeginMode()</c>.
    /// </remarks>
    public void Draw(RenderContext ctx)
    {
        var ghostColor = GetAffiliationColor(_affiliation);
        ghostColor.A = CreationToolConstants.GhostAlpha;

        Raylib.DrawCircle(
            (int)_currentMouseWorld.X,
            (int)_currentMouseWorld.Y,
            CreationToolConstants.GhostRadiusPx,
            ghostColor);

        Raylib.DrawText(
            _tkbType.ToString(),
            (int)_currentMouseWorld.X,
            (int)(_currentMouseWorld.Y + CreationToolConstants.GhostLabelOffsetY),
            CreationToolConstants.GhostLabelFontSize,
            Color.White);
    }

    //  Private helpers 

    private void PublishCreateRequest(Vector2 worldPos)
    {
        // Convert flat map world-space metres to geodetic coordinates.
        // When a geo transform is available (live DDS mode), use it to get
        // correct lat/lon.  When null (offline / unit-test mode) fall back to
        // treating worldPos.Y as latitude and worldPos.X as longitude so the
        // existing test suite continues to work without a geo transform stub.
        double lat, lon, alt;
        if (_geoTransform != null)
        {
            (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));
        }
        else
        {
            // Offline fallback: map canvas X  east (longitude), Y  north (latitude).
            lat = worldPos.Y;
            lon = worldPos.X;
            alt = 0.0;
        }

        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner     = default,   // zeroed NodeId  SimHost takes authoritative ownership
            Flags     = 0,
            InitialDescriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d           = EDescriptorType.dtEntityMaster,
                    EntityMaster = new EntityMaster { TkbType = _tkbType },
                },
                new EntityDescriptorUnion
                {
                    _d         = EDescriptorType.dtGeoSpatial,
                    GeoSpatial = new GeoSpatial
                    {
                        Pos = new GeoPosition
                        {
                            Latitude  = lat,
                            Longitude = lon,
                            Altitude  = alt,
                        },
                    },
                },
                new EntityDescriptorUnion
                {
                    _d         = EDescriptorType.dtEntityInfo,
                    EntityInfo = new EntityInfo
                    {
                        EntityId        = 0, // SimHost overwrites with allocated ID
                        Name            = string.Empty,
                        ForceIdentifier = MapAffiliation(_affiliation),
                        CommanderId     = 0,
                    },
                },
            },
        };

        _ddsWriter.Write(request);
        OnCommandPublished?.Invoke(request);
    }

    private static Color GetAffiliationColor(ForceId affiliation) =>
        affiliation switch
        {
            ForceId.Friend  => Color.Blue,
            ForceId.Hostile => Color.Red,
            ForceId.Neutral => Color.Green,
            _               => Color.White,
        };

    /// <summary>
    /// Maps a local <see cref="ForceId"/> to the DDS-layer <see cref="eForceIdentifier"/> enum.
    /// </summary>
    private static eForceIdentifier MapAffiliation(ForceId affiliation) =>
        affiliation switch
        {
            ForceId.Friend  => eForceIdentifier.FORCE_FRIENDLY,
            ForceId.Hostile => eForceIdentifier.FORCE_OPPOSING,
            ForceId.Neutral => eForceIdentifier.FORCE_NEUTRAL,
            _               => eForceIdentifier.FORCE_UNKNOWN,
        };
}
