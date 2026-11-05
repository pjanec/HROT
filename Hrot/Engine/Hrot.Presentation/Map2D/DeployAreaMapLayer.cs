using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Weather;
using Hrot.Common;
using Hrot.Gameplay;
using Hrot.UI.Common.Panels;
using Raylib_cs;

namespace Hrot.UI.Common.Map2D;

/// <summary>
/// ⭐⭐ CE-3370 (Player Join S3-d, docs/DESIGN_Player_Join.md §11) — <b>choose the spawn area by CLICKING it on the Player's 2-D map.</b> While the deploy flow is Choosing, the outlines of the chosen side's spawn areas
/// (<see cref="DeployFlowSystem.Offers"/> entries with <c>IsArea</c>, their <c>SpawnArea</c> entity's outline — the one <see cref="WeatherField.TryOutline"/> <c>SpawnPoints.Pick</c> places a man by) are drawn on the map,
/// the chosen one bold; a click inside one selects it — through <see cref="DeployAreaChoice"/>, the SAME selection the deploy screen's list holds, so the list follows the click and the click follows the list — and it rides the
/// next <see cref="DeployFlowSystem.Request"/> (<see cref="DeployScreen.Deploy"/>). Nothing else: not Choosing ⇒ nothing drawn, nothing consumed.
/// <para>Attached by <c>MapInteractionPack.AttachMapLayers</c> on every map host, so no host can lack it; only the Player's node has a <see cref="DeployFlowSystem"/>, so only there does it ever act. 2-D only (it has no 3-D path,
/// and a click in 3-D is a ray, not a ground point). Not an <c>ISceneLayer</c>: a film frame never shows it. ⛔ No allocation per frame — index loops over the offers, the drawn list reused.</para>
/// </summary>
public sealed class DeployAreaMapLayer : IMapLayer
{
    /// <summary>The colour of the chosen area's outline.</summary>
    public static readonly Color ChosenColour = new(255, 220, 0, 255);
    /// <summary>The colour of an area on offer that is not chosen.</summary>
    public static readonly Color OfferedColour = new(120, 200, 255, 255);

    private readonly Func<EntityRepository?> _world;
    private readonly Func<bool> _choosing;
    private readonly Func<IReadOnlyList<DeployOffer>> _offers;
    private readonly Func<DeployAreaChoice?> _choice;
    private readonly Func<bool> _is2D;
    private readonly List<int> _drawn = new();

    /// <param name="choosing">Whether the Player is choosing right now (the flow's state is Choosing).</param>
    /// <param name="offers">The offers (areas are the <c>IsArea</c> ones).</param>
    /// <param name="choice">The shared choice; null ⇒ nothing to show.</param>
    /// <param name="is2D">Whether the 2-D map is on screen (default: always).</param>
    public DeployAreaMapLayer(Func<EntityRepository?> world, Func<bool> choosing, Func<IReadOnlyList<DeployOffer>> offers, Func<DeployAreaChoice?> choice, Func<bool>? is2D = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _choosing = choosing ?? throw new ArgumentNullException(nameof(choosing));
        _offers = offers ?? throw new ArgumentNullException(nameof(offers));
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        _is2D = is2D ?? (() => true);
    }

    /// <summary>The production layer: reads the world's own <see cref="DeployFlowSystem"/> (none ⇒ never acts) and its <see cref="DeployAreaChoice"/>.</summary>
    public static DeployAreaMapLayer ForWorld(Func<EntityRepository?> world, Func<bool> is2D)
        => new(world,
            () => DeployFlowSystem.Of(world())?.State == DeployFlowState.Choosing,
            () => DeployFlowSystem.Of(world())?.Offers ?? Array.Empty<DeployOffer>(),
            () => DeployFlowSystem.Of(world()) is { } f ? DeployAreaChoice.Of(f) : null,
            is2D);

    public string Name => "DeployAreas";
    public int LayerBitIndex => -1;

    /// <summary>The area net ids the last <see cref="Draw"/> outlined (a rail's probe; empty when nothing was drawn).</summary>
    public IReadOnlyList<int> Drawn => _drawn;

    /// <summary>The area (net id) drawn bold by the last <see cref="Draw"/>; 0 = none (a rail's probe).</summary>
    public int DrawnChosen { get; private set; }

    public void Update(float dt) { }

    public void Draw(RenderContext ctx)
    {
        _drawn.Clear(); DrawnChosen = 0;
        if (!_choosing() || !_is2D() || _world() is not { } world || _choice() is not { } choice) return;
        var offers = _offers();
        if (choice.EffectiveSide(offers) is not { } side) return;
        float px = 1f / MathF.Max(ctx.Zoom, 0.0001f);   // one screen pixel in world metres: the outline keeps its width at every zoom
        for (int i = 0; i < offers.Count; i++)
        {
            var o = offers[i];
            if (!o.IsArea || o.Side != side || !TryOutline(world, o.UnitNetId, out var points, out var origin)) continue;
            bool chosen = choice.AreaNetId == o.UnitNetId;
            var colour = chosen ? ChosenColour : OfferedColour;
            float thickness = (chosen ? 4f : 2f) * px;
            for (int k = 0; k < points.Count; k++)
            {
                var a = origin + points[k]; var b = origin + points[(k + 1) % points.Count];
                Raylib.DrawLineEx(a, b, thickness, colour);
            }
            _drawn.Add(o.UnitNetId);
            if (chosen) DrawnChosen = o.UnitNetId;
        }
    }

    public void DrawOverlay(RenderContext ctx) { }

    /// <summary>A press of the left button inside one of the chosen side's areas on offer selects it (and is consumed); anywhere else nothing changes and the click goes on to the layers below.</summary>
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed)
    {
        if (!isPressed || button != MapMouseButton.Left || !_choosing() || !_is2D() || _world() is not { } world || _choice() is not { } choice) return false;
        var offers = _offers();
        if (choice.EffectiveSide(offers) is not { } side) return false;
        for (int i = 0; i < offers.Count; i++)
        {
            var o = offers[i];
            if (!o.IsArea || o.Side != side) continue;
            var area = NetworkIdResolver.ResolveNetworkId(world, o.UnitNetId);
            if (area.IsNull || !world.IsAlive(area) || !WeatherField.AreaEntityContains(world, area, worldPos)) continue;
            choice.Side = side;
            choice.Select(o.UnitNetId);
            return true;
        }
        return false;
    }

    public Entity? PickEntity(Vector2 worldPos) => null;

    private static bool TryOutline(EntityRepository world, int areaNetId, out IReadOnlyList<Vector2> points, out Vector2 origin)
    {
        points = Array.Empty<Vector2>(); origin = default;
        var area = NetworkIdResolver.ResolveNetworkId(world, areaNetId);
        if (area.IsNull || !world.IsAlive(area) || !WeatherField.TryOutline(world, area, out var outline)) return false;
        if (world.HasComponent<SimTransform>(area)) { var p = world.GetComponentRO<SimTransform>(area).Position; origin = new Vector2(p.X, p.Y); }
        points = outline.Points;
        return true;
    }
}
