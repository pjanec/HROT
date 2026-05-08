using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Named constants for <see cref="EntityRotationTool"/>.
/// </summary>
internal static class EntityRotationToolConstants
{
    /// <summary>Display name reported via <see cref="IMapTool.Name"/>.</summary>
    public const string ToolName = "Rotate Entity";

    /// <summary>
    /// Stroke thickness of the heading preview line (world units at zoom 1).
    /// </summary>
    public const float LineThickness = 2.0f;

    /// <summary>Font size of the heading label drawn at the line midpoint.</summary>
    public const int LabelFontSize = 14;

    /// <summary>
    /// Vertical pixel offset applied to the midpoint label so it does not sit
    /// exactly on the line.
    /// </summary>
    public const int LabelOffsetY = 4;

    /// <summary>
    /// Colour of the heading preview line (orange — clearly distinct from the
    /// cyan measurement line and entity tints).
    /// </summary>
    public static readonly Rgba32 LineColor = new Rgba32(255, 128, 0, 255);
}

/// <summary>
/// Map tool that lets the operator set an entity's heading by clicking on the map.
///
/// <para>
/// A live preview line is drawn from the entity's world-space reference point to
/// the cursor with the computed compass heading (0 = North, 90 = East, clockwise)
/// displayed in degrees.
/// </para>
///
/// <para>
/// On left-click the tool publishes an <see cref="UpdateEntityAttributeCommand"/> on
/// the simulation event bus; in distributed mode the egress translator converts this
/// into a DDS <c>UpdateEntityAttributeRequest</c>.  When the local node already has
/// authority over <see cref="SimTransform"/> (Editor offline, SimHost AllInOne) the
/// rotation is also applied directly to the ECS component for instant feedback.
/// </para>
///
/// <para>Right-click or <c>Escape</c> cancels without making any changes.</para>
///
/// <para>
/// All draw calls are made inside <c>MapCanvas.Draw()</c> -> <c>Camera.BeginMode()</c>
/// so coordinates are in world space.
/// </para>
/// </summary>
public class EntityRotationTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => EntityRotationToolConstants.ToolName;

    private readonly Entity           _entity;
    private readonly EntityRepository _world;
    private MapCanvas?                _canvas;
    private Vector2                   _currentPoint;

    /// <summary>
    /// Initialises the tool for the given entity.
    /// </summary>
    /// <param name="entity">Local ECS entity handle of the entity to rotate.</param>
    /// <param name="world">ECS repository; used to read <see cref="SimTransform"/>
    /// and to publish the <see cref="UpdateEntityAttributeCommand"/> event.</param>
    public EntityRotationTool(Entity entity, EntityRepository world)
    {
        _entity = entity;
        _world  = world ?? throw new ArgumentNullException(nameof(world));
    }

    // -- IMapTool lifecycle ---------------------------------------------------

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas) => _canvas = canvas;

    /// <inheritdoc/>
    public void OnExit() => _canvas = null;

    /// <inheritdoc/>
    public void Update(float dt) { }

    // -- Input handling -------------------------------------------------------

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        if (button == MapMouseButton.Left)
        {
            if (_world.IsAlive(_entity) && _world.HasComponent<SimTransform>(_entity))
            {
                var   tf         = _world.GetComponent<SimTransform>(_entity);
                var   origin     = new Vector2(tf.Position.X, tf.Position.Y);
                float headingDeg = ComputeHeadingDeg(origin, worldPos);

                long networkId = _world.HasComponent<NetworkIdentity>(_entity)
                    ? _world.GetComponent<NetworkIdentity>(_entity).Value
                    : 0L;

                // Publish managed event for the network egress translator.
                _world.Bus.PublishManaged(new UpdateEntityAttributeCommand
                {
                    NetworkId          = networkId,
                    AttributePatchJson = FormatHeadingJson(headingDeg),
                });

                // Local fast-path: apply heading directly when this node has authority
                // (offline Editor, SimHost AllInOne).  In distributed Brain-only mode
                // the remote Muscle applies the change via UpdateEntityAttributeRequestSystem.
                if (_world.HasAuthority<SimTransform>(_entity))
                {
                    float mathYawRad = (90f - headingDeg) * (MathF.PI / 180f);
                    ref var transform = ref _world.GetComponentRW<SimTransform>(_entity);
                    transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, mathYawRad);
                }
            }

            _canvas?.PopTool();
            return true;
        }

        if (button == MapMouseButton.Right)
        {
            _canvas?.PopTool();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <inheritdoc/>
    /// <remarks>Tracks cursor world position for the live heading preview.</remarks>
    public bool HandleHover(Vector2 worldPos)
    {
        _currentPoint = worldPos;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks><see cref="KeyboardKey.Escape"/> cancels and pops the tool.</remarks>
    public bool HandleKeyPressed(MapKeyboardKey key)
    {
        if (key == MapKeyboardKey.Escape)
        {
            _canvas?.PopTool();
            return true;
        }
        return false;
    }

    // -- Rendering ------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Draws an orange line from the entity reference point to the cursor and a
    /// heading label at the midpoint.
    /// Called inside <c>MapCanvas.Draw()</c> -> <c>Camera.BeginMode()</c>.
    /// </remarks>
    public void Draw(RenderContext ctx)
    {
        if (!_world.IsAlive(_entity) || !_world.HasComponent<SimTransform>(_entity)) return;

        var   tf         = _world.GetComponent<SimTransform>(_entity);
        var   origin     = new Vector2(tf.Position.X, tf.Position.Y);
        float headingDeg = ComputeHeadingDeg(origin, _currentPoint);

        ctx.DrawBuilder?.DrawLine(
            new System.Numerics.Vector3(origin.X, origin.Y, 0f),
            new System.Numerics.Vector3(_currentPoint.X, _currentPoint.Y, 0f),
            EntityRotationToolConstants.LineColor,
            EntityRotationToolConstants.LineThickness);

        string label    = $"{headingDeg:F1} deg";
        var    midpoint = (origin + _currentPoint) * 0.5f;
        ctx.DrawBuilder?.DrawTextLong(
            midpoint.X,
            midpoint.Y + EntityRotationToolConstants.LabelOffsetY,
            label,
            Rgba32.White);
    }

    // -- Private helpers ------------------------------------------------------

    /// <summary>
    /// Computes compass heading in degrees [0, 360) from <paramref name="from"/>
    /// to <paramref name="to"/> in world-space (X = East, Y = North).
    /// 0 = North, 90 = East, clockwise positive.
    /// </summary>
    private static float ComputeHeadingDeg(Vector2 from, Vector2 to)
    {
        var   delta      = to - from;
        float mathYawRad = MathF.Atan2(delta.Y, delta.X);
        return (90f - mathYawRad * (180f / MathF.PI) + 360f) % 360f;
    }

    /// <summary>
    /// Formats <paramref name="headingDeg"/> as a JSON attribute patch string,
    /// e.g. <c>{"Heading":340.70}</c>.  Uses invariant culture to avoid
    /// locale-dependent decimal separators.
    /// </summary>
    private static string FormatHeadingJson(float headingDeg) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"Heading\":{0:F2}}}",
            headingDeg);
}
