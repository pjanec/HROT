using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Abstractions;
using Bagira.IG.Components;
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
///         mapped as <c>Latitude = worldPos.Y, Longitude = worldPos.X</c>.</item>
/// </list>
///
/// <see cref="CreateEntityRequest.Owner"/> is left as <c>default(NodeId)</c> (all-zeros)
/// so the SimHost assigns itself as the authoritative owner (ghost-node convention).
///
/// No allocations on the hover / draw hot path (§CODE-STANDARDS §4).
/// The <see cref="List{T}"/> in <see cref="CreateEntityRequest.InitialDescriptors"/> is
/// only allocated on the click event path.
/// </summary>
public class CreationTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => CreationToolConstants.ToolName;

    private readonly IDdsWriter<CreateEntityRequest> _ddsWriter;
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
    /// <param name="tkbType">
    /// TKB template type to request.  Defaults to
    /// <see cref="CreationToolConstants.DefaultTkbType"/> when zero is passed.
    /// </param>
    /// <param name="affiliation">Force affiliation for the new entity.</param>
    public CreationTool(
        IDdsWriter<CreateEntityRequest> ddsWriter,
        long                            tkbType     = CreationToolConstants.DefaultTkbType,
        ForceId                         affiliation = ForceId.Unknown)
    {
        _ddsWriter   = ddsWriter ?? throw new ArgumentNullException(nameof(ddsWriter));
        _tkbType     = tkbType == 0 ? CreationToolConstants.DefaultTkbType : tkbType;
        _affiliation = affiliation;
    }

    // ── IMapTool lifecycle ────────────────────────────────────────────────────

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

    // ── Input handling ────────────────────────────────────────────────────────

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
            // Cancel — return to previous tool without spawning.
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

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Draws a semi-transparent ghost circle at the current cursor world position,
    /// with the TKB type code as a label below it.
    /// Called inside <c>MapCanvas.Draw()</c> → <c>Camera.BeginMode()</c>.
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

    // ── Private helpers ───────────────────────────────────────────────────────

    private void PublishCreateRequest(Vector2 worldPos)
    {
        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner     = default,   // zeroed NodeId — SimHost takes authoritative ownership
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
                        // Map canvas: X = longitude (east), Y = latitude (north)
                        Pos = new GeoPosition { Latitude = worldPos.Y, Longitude = worldPos.X },
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
}
