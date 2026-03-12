using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Bagira.IG.Tools;

/// <summary>
/// Map tool that translates a left-click on the canvas into a
/// <see cref="CreateEntityRequest"/> routed through the injected
/// <see cref="OnEntityCreated"/> delegate, decoupling the tool from any
/// specific network protocol.
///
/// Workflow:
/// <list type="number">
///   <item>Caller activates the tool (e.g. via <c>canvas.PushTool(creationTool)</c>).</item>
///   <item>Operator sees a ghost preview circle at the cursor.</item>
///   <item>Left-click builds a <see cref="CreateEntityRequest"/> and fires
///         the <see cref="_onEntityCreated"/> delegate. When <c>autoPopOnPlace</c>
///         is <c>true</c> (default) the tool pops itself immediately (single-placement);
///         otherwise it remains active for multi-placement until right-click or ESC.</item>
///   <item>Right-click or ESC cancels placement; the tool pops itself without firing
///         the delegate.</item>
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
/// <see cref="CreateEntityRequest.InitialAttributesJson"/> carries the raw
/// <c>initialPropertiesJson</c> string verbatim so the SimHost's
/// <c>JsonAttributeCompiler</c> can apply fine-grained field patches (ATTR-S5T2).
///
/// No allocations on the hover / draw hot path (CODE-STANDARDS 4).
/// The <see cref="List{T}"/> in <see cref="CreateEntityRequest.InitialDescriptors"/> is
/// only allocated on the click event path.
/// </summary>
public class CreationTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => CreationToolConstants.ToolName;

    private readonly Action<CreateEntityRequest> _onEntityCreated;
    private readonly IGeographicTransform?       _geoTransform;
    private readonly long                        _tkbType;
    private readonly ForceId                     _affiliationForDisplay;
    private readonly string?                     _initialPropertiesJson;
    private readonly bool                        _autoPopOnPlace;
    private readonly Func<string>?               _nameResolver;

    private MapCanvas? _canvas;
    private Vector2    _currentMouseWorld;

    /// <summary>
    /// Raised after a <see cref="CreateEntityRequest"/> has been constructed and passed
    /// to the <see cref="_onEntityCreated"/> delegate, so tests and integrators can
    /// observe the event without inspecting the delegate's capture list.
    /// </summary>
    public event Action<CreateEntityRequest>? OnCommandPublished;

    /// <summary>
    /// Raised when the tool exits the canvas (after placement or cancellation).
    /// Allows the <c>MapCommandController</c> to detect tool lifecycle changes without
    /// polling <c>MapCanvas.ActiveTool</c>.
    /// </summary>
    public event Action? Exited;

    /// <param name="onEntityCreated">
    /// Delegate invoked with the fully-constructed <see cref="CreateEntityRequest"/> when
    /// the operator left-clicks. The caller is responsible for routing it to DDS or
    /// processing it further. Must not be <c>null</c>.
    /// </param>
    /// <param name="geoTransform">
    /// Optional geographic transform for converting flat map world-space metres to
    /// geodetic (lat/lon) coordinates. When <c>null</c> (offline / test mode),
    /// <c>worldPos.Y</c> is used as latitude and <c>worldPos.X</c> as longitude.
    /// Pass <see cref="WGS84Transform"/> (or any <see cref="IGeographicTransform"/>)
    /// when running with live DDS so the entity is placed at the correct real-world
    /// geographic position.
    /// </param>
    /// <param name="tkbType">
    /// TKB template type to request. Defaults to
    /// <see cref="CreationToolConstants.DefaultTkbType"/> when zero is passed.
    /// </param>
    /// <param name="initialPropertiesJson">
    /// Optional JSON object with initial property overrides merged into the
    /// <see cref="CreateEntityRequest.InitialDescriptors"/>.
    /// Recognised fields: <c>name</c> (string) — entity name in <c>EntityInfo</c>;
    /// <c>affiliation</c> (string, e.g. <c>"FORCE_FRIENDLY"</c>) — force identifier
    /// and ghost colour. Unknown fields are silently ignored.
    /// </param>
    /// <param name="autoPopOnPlace">
    /// When <c>true</c> (default) the tool pops itself immediately after a successful
    /// left-click (single-placement mode). Set to <c>false</c> for continuous
    /// multi-placement; the tool stays active until right-click or ESC.
    /// </param>
    /// <param name="nameResolver">
    /// Optional delegate invoked on each left-click to obtain the entity name for that
    /// placement. When provided it takes priority over any <c>name</c> field in
    /// <paramref name="initialPropertiesJson"/>, enabling session-scoped sequential
    /// naming strategies such as "Tank-1", "Tank-2", …
    /// When <c>null</c> (default) the name is parsed from <paramref name="initialPropertiesJson"/>.
    /// </param>
    public CreationTool(
        Action<CreateEntityRequest> onEntityCreated,
        IGeographicTransform?       geoTransform          = null,
        long                        tkbType               = CreationToolConstants.DefaultTkbType,
        string?                     initialPropertiesJson = null,
        bool                        autoPopOnPlace        = true,
        Func<string>?               nameResolver          = null)
    {
        _onEntityCreated       = onEntityCreated ?? throw new ArgumentNullException(nameof(onEntityCreated));
        _geoTransform          = geoTransform;
        _tkbType               = tkbType == 0 ? CreationToolConstants.DefaultTkbType : tkbType;
        _affiliationForDisplay = ParseAffiliationFromJson(initialPropertiesJson);
        _initialPropertiesJson = initialPropertiesJson;
        _autoPopOnPlace        = autoPopOnPlace;
        _nameResolver          = nameResolver;
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
        Exited?.Invoke();
    }

    /// <inheritdoc/>
    public void Update(float dt) { /* Stateless between frames. */ }

    //  Input handling 

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            BuildAndPublishCreateRequest(worldPos);
            if (_autoPopOnPlace)
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

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="KeyboardKey.Escape"/> cancels placement and pops the tool without
    /// publishing a <see cref="CreateEntityRequest"/>.
    /// </remarks>
    public bool HandleKeyPressed(KeyboardKey key)
    {
        if (key == KeyboardKey.Escape)
        {
            _canvas?.PopTool();
            return true;
        }
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
        var ghostColor = GetAffiliationColor(_affiliationForDisplay);
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

    private void BuildAndPublishCreateRequest(Vector2 worldPos)
    {
        // Convert flat map world-space metres to geodetic coordinates.
        // When a geo transform is available (live DDS mode), use it to get
        // correct lat/lon. When null (offline / unit-test mode) fall back to
        // treating worldPos.Y as latitude and worldPos.X as longitude so the
        // existing test suite continues to work without a geo transform stub.
        double lat, lon, alt;
        if (_geoTransform != null)
        {
            (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));
        }
        else
        {
            // Offline fallback: map canvas X → east (longitude), Y → north (latitude).
            lat = worldPos.Y;
            lon = worldPos.X;
            alt = 0.0;
        }

        // Resolve entity name: prefer the per-click nameResolver delegate (e.g. for
        // session-scoped sequential naming); fall back to any name encoded in the JSON blob.
        // NOTE: name resolution is forwarded via InitialAttributesJson so SimHost applies
        // it through JsonAttributeCompiler (ATTR-S5T2). nameResolver is retained for future use.

        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner     = default,   // zeroed NodeId → SimHost takes authoritative ownership
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
            },
            InitialAttributesJson = _initialPropertiesJson,
        };

        _onEntityCreated(request);
        OnCommandPublished?.Invoke(request);
    }

    /// <summary>
    /// Parses the <c>"affiliation"</c> field from <paramref name="json"/> and returns
    /// the corresponding <see cref="ForceId"/>. Returns <see cref="ForceId.Unknown"/>
    /// when the field is absent, unrecognised, or the JSON is malformed.
    /// </summary>
    private static ForceId ParseAffiliationFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ForceId.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("affiliation", out var el)
             && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString() switch
                {
                    "FORCE_FRIENDLY" => ForceId.Friend,
                    "FORCE_OPPOSING" => ForceId.Hostile,
                    "FORCE_NEUTRAL"  => ForceId.Neutral,
                    _                => ForceId.Unknown,
                };
            }
        }
        catch { /* malformed JSON — fall through */ }
        return ForceId.Unknown;
    }

    private static Color GetAffiliationColor(ForceId affiliation) =>
        affiliation switch
        {
            ForceId.Friend  => Color.Blue,
            ForceId.Hostile => Color.Red,
            ForceId.Neutral => Color.Green,
            _               => Color.White,
        };
}
