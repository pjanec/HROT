using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Vis2D.Abstractions;
using Hrot.Map.Common.Components;
using Fdp.ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Rendering;

/// <summary>
/// Always-on map overlay layer that renders LOS obstacle entities created by
/// <c>EditorZoneAuthoringSystem</c> as translucent red circles on the canvas.
///
/// <para>
/// Zone obstacles carry <see cref="ZoneMembership"/> (managed), <see cref="PhysicsCollider"/>
/// (for the radius), and <see cref="SimTransform"/> (for world position). Because they lack
/// <c>NetworkIdentity</c>, the default <c>EntityRenderLayer</c> silently ignores them; this
/// dedicated layer handles them instead.
/// </para>
///
/// <para><c>LayerBitIndex = -1</c> (always visible, never toggled by the layer mask).</para>
/// </summary>
public sealed class ZoneObstacleRenderLayer : IMapLayer
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly Color FillColor   = new(255, 0, 0, 80);
    private static readonly Color BorderColor = Color.Red;

    // ── IMapLayer ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => "Zone Obstacles";

    /// <inheritdoc/>
    /// <remarks>-1 means always-on; the editor layer mask does not gate this layer.</remarks>
    public int LayerBitIndex => -1;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ISimulationView _view;
    private EntityQuery? _query;

    // ── Test hooks ────────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, Raylib draw calls are skipped and only the counter is incremented.
    /// Set by unit tests so rendering can be asserted headlessly.
    /// </summary>
    public bool TestHook_SkipRaylibCalls { get; set; }

    /// <summary>Total obstacle-circle draw calls made in the last <see cref="Draw"/> pass.</summary>
    public int TestHook_DrawCount { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="view">Simulation view used to query obstacle entities.</param>
    public ZoneObstacleRenderLayer(ISimulationView view)
    {
        _view = view ?? throw new System.ArgumentNullException(nameof(view));
    }

    // ── IMapLayer methods ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Update(float dt) { }

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        // Build the query lazily so all component types are registered before first use.
        _query ??= _view
            .Query()
            .WithManaged<ZoneMembership>()
            .With<PhysicsCollider>()
            .With<SimTransform>()
            .Build();

        TestHook_DrawCount = 0;

        foreach (var entity in _query)
        {
            ref readonly var tf  = ref _view.GetComponentRO<SimTransform>(entity);
            ref readonly var col = ref _view.GetComponentRO<PhysicsCollider>(entity);

            var center = new Vector2(tf.Position.X, tf.Position.Y);
            float radius = col.Radius;

            TestHook_DrawCount++;

            if (TestHook_SkipRaylibCalls) continue;

            Raylib.DrawCircleV(center, radius, FillColor);
            Raylib.DrawCircleLines((int)center.X, (int)center.Y, radius, BorderColor);
        }
    }

    /// <inheritdoc/>
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    public Entity? PickEntity(Vector2 worldPos)
    {
        if (_query == null) return null;

        foreach (var entity in _query)
        {
            ref readonly var tf  = ref _view.GetComponentRO<SimTransform>(entity);
            ref readonly var col = ref _view.GetComponentRO<PhysicsCollider>(entity);

            var center = new Vector2(tf.Position.X, tf.Position.Y);
            if (Vector2.DistanceSquared(center, worldPos) <= col.Radius * col.Radius)
                return entity;
        }
        return null;
    }
}
