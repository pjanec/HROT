using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>What the ghost needs from the host to preview a block: the ground the block fits itself to and where the recipes and templates are read from.</summary>
public sealed class TerrainGhostContext
{
    public TerrainGhostContext(TerrainWorld baseWorld, string terrainFolder)
    {
        Base = baseWorld ?? throw new ArgumentNullException(nameof(baseWorld));
        Assets = TerrainAssets.ForFolder(terrainFolder);
        RecipeRoots = new List<string?>(TerrainRecipe.RootsFor(terrainFolder));
    }

    public TerrainWorld Base { get; }
    public TerrainAssets Assets { get; }
    public IEnumerable<string?> RecipeRoots { get; }
}

/// <summary>What one ghost frame shows (computed only when the cursor, the heading or the seed moved): the block's outline, its houses and woods, and the two hint lines.</summary>
public sealed class TerrainGhostShape
{
    public Vector2[] Outline { get; init; } = Array.Empty<Vector2>();
    public Vector2[][] Houses { get; init; } = Array.Empty<Vector2[]>();
    public Vector2[][] Woods { get; init; } = Array.Empty<Vector2[]>();
    /// <summary>Line 1: the heading and the keys. Line 2: whether it is clear of water and buildings.</summary>
    public string Line1 { get; init; } = "";
    public string Line2 { get; init; } = "";
    /// <summary>True when everything the recipe wanted found room (the mockup's green tick); false ⇒ the second line warns.</summary>
    public bool Fits { get; init; } = true;
    public int HousesPlaced { get; init; }
}

/// <summary>
/// ⭐⭐ Terrain authoring U-2 (docs/DESIGN_Terrain_Authoring.md §15.U-2, mockup 2 bottom-left) — the placing GHOST of a block / house: <see cref="FeatureExpander.Expand"/> (PURE, the SAME expansion the live overlay and the
/// compile run — ⛔ never a second one) called on the cursor, so the ghost fits itself to the site as you move it. ⭐ Computed only on a change (<see cref="TerrainPlacement.Preview"/> caches); it draws from arrays.
/// </summary>
public static class TerrainGhost
{
    public static TerrainGhostShape Compute(TerrainPickKind kind, string id, TerrainGhostContext ctx, Vector2 at, float headingDeg, uint seed, byte fit = TerrainBlock.FitAll)
    {
        string heading = $"heading {System.Math.Round(headingDeg)}° · R to rotate · road alignment later (CE-3431)";
        try
        {
            if (kind == TerrainPickKind.House)
            {
                var foot = FeatureExpander.HouseFootprint(ctx.Assets, id, at, headingDeg);
                return new TerrainGhostShape { Outline = Hull(foot), Houses = new[] { foot }, HousesPlaced = 1, Line1 = heading, Line2 = "one house, set on the ground here" };
            }
            var recipe = TerrainRecipe.Find(id, ctx.RecipeRoots);
            var x = FeatureExpander.Expand(recipe, seed, at, headingDeg, fit, ctx.Base, "ghost", ctx.Assets);
            var shapes = x.ShapesOrEmpty;
            var houses = new List<Vector2[]>(x.Houses);
            var woods = new List<Vector2[]>(x.Woods);
            for (int i = 0; i < shapes.Count; i++) (i < x.Houses ? houses : woods).Add(shapes[i]);
            var (wantMin, wantMax, _) = TerrainPickCatalog.HouseRange(recipe);
            bool fits = x.Houses >= wantMin;
            return new TerrainGhostShape
            {
                Outline = Hull(x.Points), Houses = houses.ToArray(), Woods = woods.ToArray(), HousesPlaced = x.Houses, Fits = fits,
                Line1 = heading,
                Line2 = fits ? "clear of water and buildings" : $"only {x.Houses} of {wantMin}-{wantMax} houses fit here",
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or System.IO.FileNotFoundException)
        {
            return new TerrainGhostShape { Line1 = heading, Line2 = e.Message.Length > 90 ? e.Message.Substring(0, 90) : e.Message, Fits = false };
        }
    }

    private static Vector2[] Hull(IReadOnlyList<Vector2> pts) => pts.Count == 0 ? Array.Empty<Vector2>() : OverlayExpansion.Hull(pts).ToArray();
}

/// <summary>
/// ⭐ Terrain authoring U-2 — the placement state of ONE armed terrain pick: the spec, the seed the next click will use (the ghost shows THAT layout), the extra heading <c>R</c> adds, and the cached ghost. After a click the
/// seed is renewed, so the next ghost is a new layout (always-multi placement, D5).
/// </summary>
public sealed class TerrainPlacement
{
    private readonly Func<TerrainGhostContext?>? _context;
    private (float X, float Y, float Heading, uint Seed, int Ctx) _key = (float.NaN, 0, 0, 0, 0);
    private TerrainGhostShape? _shape;

    public TerrainPlacement(TerrainPickKind kind, string id, Func<TerrainGhostContext?>? context, uint? seed = null)
    {
        Kind = kind; Id = id; _context = context;
        Seed = seed ?? TerrainBirth.NewSeed();
    }

    public TerrainPickKind Kind { get; }
    public string Id { get; }
    public string Title => TerrainPickCatalog.Title(Id);
    /// <summary>The seed the NEXT click gives the block.</summary>
    public uint Seed { get; private set; }
    /// <summary>Degrees <c>R</c> has added to the heading (a multiple of 90).</summary>
    public float RotationDeg { get; private set; }
    /// <summary>Ghost expansions made (a cache miss each) — a rail asserts a steady cursor makes none.</summary>
    public int Computations { get; private set; }

    /// <summary><c>R</c>: a quarter turn.</summary>
    public void Rotate() { RotationDeg = (RotationDeg + 90f) % 360f; }

    /// <summary>The click was taken: the next ghost is another layout.</summary>
    public void Renew() => Seed = TerrainBirth.NewSeed();

    /// <summary>The heading the placed block gets for a transform's yaw: its own (<see cref="TerrainBirth.HeadingOf"/>) plus <see cref="RotationDeg"/>.</summary>
    public float HeadingFor(Quaternion rotation) => (TerrainBirth.HeadingOf(rotation) + RotationDeg) % 360f;

    /// <summary>
    /// The ghost at <paramref name="cursor"/> (world metres) turned <paramref name="baseHeadingDeg"/> + <see cref="RotationDeg"/>; null when the host has no terrain to fit to. ⭐ Cached: a cursor that moved under half a
    /// metre, with the same heading and seed, returns the SAME shape (0 B).
    /// </summary>
    public TerrainGhostShape? Preview(Vector2 cursor, float baseHeadingDeg = 0f)
    {
        var ctx = _context?.Invoke();
        if (ctx is null) return null;
        float heading = (baseHeadingDeg + RotationDeg) % 360f;
        float qx = MathF.Round(cursor.X * 2f) * 0.5f, qy = MathF.Round(cursor.Y * 2f) * 0.5f;
        int cid = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ctx);
        var key = (qx, qy, heading, Seed, cid);
        if (_shape is not null && key == _key) return _shape;
        _key = key;
        Computations++;
        return _shape = TerrainGhost.Compute(Kind, Id, ctx, new Vector2(qx, qy), heading, Seed);
    }
}

/// <summary>
/// ⭐ Terrain authoring U-2 — hands the ghost the host's CURRENT base terrain: <paramref name="baseOf"/> is the host's one read of it (the editor passes <c>TerrainResidency.ExpansionBase</c>); the context (assets + recipe roots)
/// is rebuilt only when the base world or its folder changed, so a steady cursor allocates nothing.
/// </summary>
public sealed class TerrainGhostContextCache
{
    private readonly Func<(TerrainWorld? Base, string? Folder)> _baseOf;
    private TerrainGhostContext? _ctx;
    private TerrainWorld? _world;
    private string? _folder;

    public TerrainGhostContextCache(Func<(TerrainWorld? Base, string? Folder)> baseOf) => _baseOf = baseOf ?? throw new ArgumentNullException(nameof(baseOf));

    /// <summary>The context for the current base; null when none is resident or its folder is unknown.</summary>
    public TerrainGhostContext? Get()
    {
        var (world, folder) = _baseOf();
        if (world is null || string.IsNullOrEmpty(folder)) return null;
        if (_ctx is not null && ReferenceEquals(world, _world) && string.Equals(folder, _folder, StringComparison.Ordinal)) return _ctx;
        _world = world; _folder = folder;
        return _ctx = new TerrainGhostContext(world, folder);
    }
}
