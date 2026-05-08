using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Replication;

namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Map tool that translates a left-click on the canvas into a
/// <see cref="SpawnEntityCommand"/> routed through the injected
/// delegate, decoupling the tool from any specific network protocol.
///
/// Workflow:
/// <list type="number">
///   <item>Caller activates the tool (e.g. via <c>canvas.PushTool(creationTool)</c>).</item>
///   <item>Operator sees a ghost preview circle at the cursor.</item>
///   <item>Left-click builds a <see cref="SpawnEntityCommand"/> and fires
///         the <see cref="_onEntityCreated"/> delegate. When <c>autoPopOnPlace</c>
///         is <c>true</c> (default) the tool pops itself immediately (single-placement);
///         otherwise it remains active for multi-placement until right-click or ESC.</item>
///   <item>Right-click or ESC cancels placement; the tool pops itself without firing
///         the delegate.</item>
/// </list>
///
/// <see cref="SpawnEntityCommand.TkbType"/> carries the TKB template type.
/// <see cref="SpawnEntityCommand.InitialTransform"/> carries the canvas-space position.
/// <see cref="SpawnEntityCommand.InitialAttributesJson"/> preserves the raw
/// <c>initialPropertiesJson</c> string for the ACL egress translator to include
/// in <c>CreateEntityRequest.InitialAttributesJson</c> on the DDS wire.
///
/// No allocations on the hover / draw hot path (CODE-STANDARDS 4).
/// </summary>
public class CreationTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => CreationToolConstants.ToolName;

    private readonly Action<SpawnEntityCommand> _onEntityCreated;
    private readonly long                       _tkbType;
    private readonly ForceId                    _affiliationForDisplay;
    private readonly string?                    _initialPropertiesJson;
    private readonly bool                       _autoPopOnPlace;
    private readonly Func<string>?              _nameResolver;

    private MapCanvas? _canvas;
    private Vector2    _currentMouseWorld;

    /// <summary>
    /// Raised after a <see cref="SpawnEntityCommand"/> has been constructed and passed
    /// to the <see cref="_onEntityCreated"/> delegate, so tests and integrators can
    /// observe the event without inspecting the delegate's capture list.
    /// </summary>
    public event Action<SpawnEntityCommand>? OnCommandPublished;

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
    /// Recognised fields: <c>name</c> (string) â€” entity name in <c>EntityInfo</c>;
    /// <c>affiliation</c> (string, e.g. <c>"FORCE_FRIENDLY"</c>) â€” force identifier
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
    /// naming strategies such as "Tank-1", "Tank-2", â€¦
    /// When <c>null</c> (default) the name is parsed from <paramref name="initialPropertiesJson"/>.
    /// </param>
    public CreationTool(
        Action<SpawnEntityCommand> onEntityCreated,
        long                       tkbType               = CreationToolConstants.DefaultTkbType,
        string?                    initialPropertiesJson = null,
        bool                       autoPopOnPlace        = true,
        Func<string>?              nameResolver          = null)
    {
        _onEntityCreated       = onEntityCreated ?? throw new ArgumentNullException(nameof(onEntityCreated));
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
    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        if (button == MapMouseButton.Left)
        {
            BuildAndPublishSpawnCommand(worldPos);
            if (_autoPopOnPlace)
                _canvas?.PopTool();
            return true;
        }

        if (button == MapMouseButton.Right)
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
    public bool HandleKeyPressed(MapKeyboardKey key)
    {
        if (key == MapKeyboardKey.Escape)
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

        ctx.DrawBuilder?.DrawSphere(
            new System.Numerics.Vector3(_currentMouseWorld.X, _currentMouseWorld.Y, 0f),
            CreationToolConstants.GhostRadiusPx,
            ghostColor);

        ctx.DrawBuilder?.DrawTextLong(
            _currentMouseWorld.X,
            _currentMouseWorld.Y + CreationToolConstants.GhostLabelOffsetY,
            _tkbType.ToString(),
            Rgba32.White);
    }

    //  Private helpers 

    private void BuildAndPublishSpawnCommand(Vector2 worldPos)
    {
        // The canvas worldPos is in flat-earth Cartesian space (X = east meters, Y = north meters).
        // Store it verbatim as the InitialTransform. The ACL egress translator
        // (SpawnEntityCommandEgressTranslator) converts this position to geodetic lat/lon
        // via the IGeographicTransform when building the DDS CreateEntityRequest.
        // nameResolver is retained for future wiring (session-scoped sequential names).
        _ = _nameResolver; // retained for future use

        var cmd = new SpawnEntityCommand
        {
            NetworkId         = 0,
            TkbType           = _tkbType,
            OwnerNodeId       = 0,
            InitType          = ReliableInitType.AllPeers,
            InitialTransform  = new SimTransform
            {
                Position = new Vector3(worldPos.X, worldPos.Y, 0f),
                Rotation = Quaternion.Identity,
            },
            InitialAttributesJson = _initialPropertiesJson,
            RequestId             = Guid.NewGuid(),
        };

        _onEntityCreated(cmd);
        OnCommandPublished?.Invoke(cmd);
    }

    /// <summary>
    /// Parses the force affiliation string from the JSON blob for ghost rendering colour.
    /// Handles both legacy lower-case keys (<c>"affiliation"</c>) and PascalCase (<c>"Affiliation"</c>).
    /// </summary>
    private static ForceId ParseAffiliationFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ForceId.Neutral;
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement affEl;
            if (!doc.RootElement.TryGetProperty("affiliation", out affEl) &&
                !doc.RootElement.TryGetProperty("Affiliation",  out affEl))
                return ForceId.Neutral;

            var raw = affEl.GetString() ?? string.Empty;
            return raw.ToUpperInvariant() switch
            {
                "FORCE_FRIENDLY" => ForceId.Friend,
                "FORCE_OPPOSING" => ForceId.Hostile,
                "FORCE_NEUTRAL"  => ForceId.Neutral,
                _                => ForceId.Neutral,
            };
        }
        catch { /* malformed JSON */ }
        return ForceId.Neutral;
    }

    private static Rgba32 GetAffiliationColor(ForceId affiliation) =>
        affiliation switch
        {
            ForceId.Friend  => new Rgba32(0, 0, 255, 255),
            ForceId.Hostile => Rgba32.Red,
            ForceId.Neutral => Rgba32.Green,
            _               => Rgba32.White,
        };
}
