using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using ModuleHost.Core.Network.Interfaces;
using Raylib_cs;

namespace Bagira.IG.Tools;

/// <summary>
/// Map tool that translates a left-click on the canvas into a
/// <see cref="SpawnEntityCommand"/> published onto the <see cref="FdpEventBus"/>,
/// requesting the SimHost to create a new entity at the clicked world position.
///
/// Workflow:
/// <list type="number">
///   <item>Caller activates the tool (e.g. via <c>canvas.PushTool(creationTool)</c>).</item>
///   <item>Operator sees a ghost preview circle at the cursor.</item>
///   <item>Left-click builds and publishes the <see cref="SpawnEntityCommand"/>; the tool
///         then pops itself and returns to the previous tool.</item>
///   <item>Right-click cancels without publishing, and the tool pops itself.</item>
/// </list>
///
/// <see cref="SpawnEntityCommand.InitialComponents"/> is seeded with a
/// <see cref="SimTransform"/> at the clicked world position (Z = 0) facing east
/// (<see cref="SimMath.FacingEast"/>), matching the FDP right-handed coordinate
/// convention (§CODE-STANDARDS §2).
///
/// Node ownership is set to <see cref="IgNetworkConstants.LocalNodeId"/>; the SimHost
/// allocates the real network ID because <see cref="SpawnEntityCommand.NetworkId"/>
/// is left at zero.
///
/// No allocations on the hover / draw hot path (§CODE-STANDARDS §4).
/// The <see cref="List{T}"/> in <see cref="SpawnEntityCommand.InitialComponents"/> is
/// only allocated on the click event path.
/// </summary>
public class CreationTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => CreationToolConstants.ToolName;

    private readonly FdpEventBus _eventBus;
    private readonly long        _tkbType;
    private readonly ForceId     _affiliation;

    private MapCanvas? _canvas;
    private Vector2    _currentMouseWorld;

    /// <summary>
    /// Raised after a <see cref="SpawnEntityCommand"/> has been published so that
    /// tests and integrators can observe the event without subscribing to the bus.
    /// </summary>
    public event Action<SpawnEntityCommand>? OnCommandPublished;

    /// <param name="eventBus">
    /// The application's <see cref="FdpEventBus"/>; the command is published as a
    /// managed event consumed by <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>.
    /// </param>
    /// <param name="tkbType">
    /// TKB template type to request.  Defaults to
    /// <see cref="CreationToolConstants.DefaultTkbType"/> when zero is passed.
    /// </param>
    /// <param name="affiliation">Force affiliation for the new entity.</param>
    public CreationTool(
        FdpEventBus eventBus,
        long        tkbType      = CreationToolConstants.DefaultTkbType,
        ForceId     affiliation  = ForceId.Unknown)
    {
        _eventBus    = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
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
            PublishSpawnCommand(worldPos);
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

    private void PublishSpawnCommand(Vector2 worldPos)
    {
        var transform = new SimTransform
        {
            Position = new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f),
            Rotation = SimMath.FacingEast,
        };

        var cmd = new SpawnEntityCommand
        {
            NetworkId         = 0,               // SimHost allocates the real ID.
            TkbType           = _tkbType,
            OwnerNodeId       = IgNetworkConstants.LocalNodeId,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object> { transform },
            RequestId         = Guid.NewGuid(),
        };

        _eventBus.PublishManaged(cmd);
        OnCommandPublished?.Invoke(cmd);
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
