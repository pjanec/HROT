using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1045 — the ground height for the 3-D map, resolving the terrain ONCE per tick instead of once per point.
/// 🔒 User, 2026-10-11: <i>"tried the 3d map on grenade or rifle scenario. the 3d view had extremely low fps (1 FPS or so)."</i>
/// <para>🔴 <b>Measured</b> (dotnet-stack on the spinning render thread): <c>GizmoRenderer3D.Drape → Ground → WorldQuery.Of</c> — every
/// draped point re-resolved the whole terrain query (singleton lookups, <c>DoorStates.Of</c>, component-type registry lookups). The
/// scene had 2 802 primitives, 2 252 of them lines, each subdivided every 3 m up to 160 points ⇒ hundreds of thousands of full
/// resolutions per frame. ⭐ <c>GroundHeightAt</c> itself is one grid sample and needs neither the doors nor the query wrapper.</para>
/// <para>The terrain singleton can be REPLACED between ticks (a scenario load), so the reference is re-read when the clock moves, never
/// held for good. Render thread only.</para>
/// </summary>
public sealed class CachedGroundHeight
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<long> _clock;
    private long _tick = long.MinValue;
    private TerrainWorld? _terrain;

    /// <param name="clock">A value that changes at least every frame (default: milliseconds); tests inject their own.</param>
    public CachedGroundHeight(Func<EntityRepository?> world, Func<long>? clock = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>How many times the terrain was resolved (rails count it).</summary>
    public int Resolves { get; private set; }

    public float At(float x, float y)
    {
        long now = _clock();
        if (now != _tick)
        {
            _tick = now;
            Resolves++;
            _terrain = _world() is { } w && w.HasSingletonManaged<TerrainWorld>() ? w.GetSingletonManaged<TerrainWorld>() : null;
        }
        return _terrain?.GroundHeightAt(x, y) ?? 0f;
    }
}
