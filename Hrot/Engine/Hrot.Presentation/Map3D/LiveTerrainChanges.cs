using System.Numerics;
using Fdp.Core;
using Hrot.Map.Common.Services;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ Terrain authoring U-6 (docs/DESIGN_Terrain_Authoring.md §15a; §18e-2 B-5) — the 3-D terrain's answer to "where does the new world differ from the one your chunks were built for?": the dirty rectangles of the editor's LIVE bake
/// (<see cref="LiveTerrainBakeInfo"/>, published after each committed bake). ⚠ It answers only for the very NEXT bake (<c>Bakes == seen + 1</c>) and only when the bake knew where it changed (<see cref="LiveTerrainBakeInfo.Dirty"/> non-null);
/// a gap in the count (a bake the layer did not see), a bake with unknown change, or no live bake at all (a terrain switch, any other host) ⇒ null ⇒ the layer rebuilds every chunk, as before — a wrong answer is slow, never wrong.
/// </summary>
public sealed class LiveTerrainChanges
{
    private readonly Func<EntityRepository?> _world;
    private int _seen;

    public LiveTerrainChanges(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>The rectangles the world the layer is about to adopt differs in; null = unknown. Called once per identity change.</summary>
    public IReadOnlyList<(Vector2 Min, Vector2 Max)>? Take(object previousIdentity)
    {
        _ = previousIdentity;
        var repo = _world();
        if (repo is null || !repo.HasSingletonManaged<LiveTerrainBakeInfo>()) return null;
        var info = repo.GetSingletonManaged<LiveTerrainBakeInfo>();
        if (info is null) return null;
        bool next = info.Bakes == _seen + 1;
        _seen = info.Bakes;
        return next ? info.Dirty : null;
    }
}
