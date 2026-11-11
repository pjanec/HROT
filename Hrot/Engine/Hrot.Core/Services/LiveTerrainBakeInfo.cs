using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hrot.Map.Common.Services;

/// <summary>The ground rectangle one placed block's parts span (the union of its houses and woods) and how many of each — what the editor draws its outline from.</summary>
public readonly record struct BlockBounds(Vector2 Min, Vector2 Max, int Houses, int Woods);

/// <summary>
/// ⭐ Terrain authoring U-3 / U-6 (docs/DESIGN_Terrain_Authoring.md §15a, §18e-2 B-5) — what the editor's LIVE bake tells the views, published as an immutable managed singleton by <see cref="StaticObstacleBakeSystem"/>
/// after each committed bake (editor only — the system holds a <see cref="LiveOverlay"/> there alone): the bounds of every block (keyed by its entity guid — the part keys' prefix), the number of bakes, and the bake's DIRTY rectangles
/// (null = unknown = everything). ⭐ ONE publication for both readers: the block outline gizmo (U-3) and the 3-D view's by-area chunk rebuild (U-6) — neither derives it a second time. Read-only for them; a new instance each bake.
/// </summary>
[Fdp.Core.DataPolicy(Fdp.Core.DataPolicy.NoScenario | Fdp.Core.DataPolicy.NoReplay | Fdp.Core.DataPolicy.NodeLocal)]
[Fdp.Core.ComponentId(Fdp.Core.GlobalComponentIds.LiveTerrainBakeInfo)]
public sealed class LiveTerrainBakeInfo
{
    public static readonly LiveTerrainBakeInfo Empty = new(0, null, new Dictionary<Guid, BlockBounds>());

    private readonly Dictionary<Guid, BlockBounds> _blocks;

    private LiveTerrainBakeInfo(int bakes, IReadOnlyList<(Vector2 Min, Vector2 Max)>? dirty, Dictionary<Guid, BlockBounds> blocks)
    { Bakes = bakes; Dirty = dirty; _blocks = blocks; }

    /// <summary>Bakes committed when this was published — a reader that saw this number has seen this bake (the 3-D view's "have I redone the chunks for it?").</summary>
    public int Bakes { get; }

    /// <summary>The bake's dirty rectangles in world metres; null when unknown (everything may have changed).</summary>
    public IReadOnlyList<(Vector2 Min, Vector2 Max)>? Dirty { get; }

    /// <summary>The blocks the bake held, by entity guid.</summary>
    public IReadOnlyDictionary<Guid, BlockBounds> Blocks => _blocks;

    /// <summary>The bounds of block <paramref name="guid"/>; false when the last bake held none of that guid (not baked yet, or it has no part).</summary>
    public bool TryGet(Guid guid, out BlockBounds bounds) => _blocks.TryGetValue(guid, out bounds);

    /// <summary>The info of a bake: the parts are grouped by the guid before the <c>/</c> of their key (<c>&lt;guid&gt;/&lt;i&gt;</c> a house, <c>&lt;guid&gt;/w&lt;i&gt;</c> a wood); a key that is not one is skipped.</summary>
    public static LiveTerrainBakeInfo From(int bakes, IReadOnlyList<OverlayPart> parts, IReadOnlyList<(Vector2 Min, Vector2 Max)>? dirty)
    {
        var blocks = new Dictionary<Guid, BlockBounds>();
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            int slash = p.Key.IndexOf('/');
            if (slash <= 0 || !Guid.TryParse(p.Key.AsSpan(0, slash), out var guid)) continue;
            bool wood = slash + 1 < p.Key.Length && p.Key[slash + 1] == 'w';
            blocks[guid] = blocks.TryGetValue(guid, out var b)
                ? new BlockBounds(Vector2.Min(b.Min, p.Min), Vector2.Max(b.Max, p.Max), b.Houses + (wood ? 0 : 1), b.Woods + (wood ? 1 : 0))
                : new BlockBounds(p.Min, p.Max, wood ? 0 : 1, wood ? 1 : 0);
        }
        return new LiveTerrainBakeInfo(bakes, dirty, blocks);
    }
}
