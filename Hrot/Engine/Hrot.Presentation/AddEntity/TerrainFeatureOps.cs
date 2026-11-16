using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ CE-5010 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-8; mockup panel E) — the WRITES of the one <b>Terrain</b> Details tab for every NON-ground <see cref="TerrainFeature"/>: its kind (only within the SAME shape — a line never becomes a point, §8 R-16),
/// its material, the Pad (height / density / spacing / count by kind), the seed and the heading. Each is ONE edit step (<see cref="EditSteps"/>: Undo restores the feature) and the live bake re-expands it, like <see cref="GroundFeatureOps"/>.
/// </summary>
public static class TerrainFeatureOps
{
    /// <summary>The kinds a wall / fence / hedge LINE can be switched between, and a wood / scrub AREA — the families, by shape.</summary>
    public static readonly byte[] WallKinds = { TerrainFeature.Wall, TerrainFeature.Fence, TerrainFeature.Hedge };
    public static readonly byte[] WoodKinds = { TerrainFeature.Wood, TerrainFeature.Scrub };

    /// <summary>The kind's name as the tab shows it.</summary>
    public static string KindName(byte kind) => TerrainBirth.FeatureName((kind, "", TerrainShape.Point));

    /// <summary>A feature the Terrain tab offers: a live <see cref="TerrainFeature"/> that is not a ground shape.</summary>
    public static bool IsEditable(EntityRepository world, Entity e)
        => world.IsAlive(e) && world.IsComponentTypeRegistered<TerrainFeature>() && world.HasComponent<TerrainFeature>(e)
           && world.GetComponentRO<TerrainFeature>(e).Kind is not (0 or TerrainFeature.Ground) && FeatureExpander.IsKnown(world.GetComponentRO<TerrainFeature>(e).Kind);

    /// <summary>
    /// ⭐ CE-5017 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-9) — <paramref name="op"/> over every one of <paramref name="entities"/> as ONE edit step (the per-entity ops' own steps nest inside —
    /// <c>WorldUndoRecorder</c>: only the outermost bracket is the step). Returns how many it changed.
    /// </summary>
    public static int Apply(EntityRepository world, IReadOnlyList<Entity> entities, string label, Func<EntityRepository, Entity, bool> op)
    {
        int n = 0;
        EditSteps.Begin(world, label);
        try { for (int i = 0; i < entities.Count; i++) if (op(world, entities[i])) n++; }
        finally { EditSteps.End(world); }
        return n;
    }

    /// <summary>⭐ CE-5017 — the ONE family every one of <paramref name="entities"/> belongs to (all editable features of the same family), or null — what the Terrain tab can edit together.</summary>
    public static byte[]? CommonFamily(EntityRepository world, IReadOnlyList<Entity> entities)
    {
        byte[]? family = null;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!IsEditable(world, entities[i])) return null;
            var fam = FamilyOf(world.GetComponentRO<TerrainFeature>(entities[i]).Kind);
            if (fam is null || (family is not null && !ReferenceEquals(family, fam))) return null;
            family = fam;
        }
        return family;
    }

    /// <summary>The kinds <paramref name="kind"/> may be switched to (its whole family, itself included), or null when it has none — ⛔ never another shape.</summary>
    public static byte[]? FamilyOf(byte kind) => kind switch
    {
        TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge => WallKinds,
        TerrainFeature.Wood or TerrainFeature.Scrub => WoodKinds,
        _ => null,
    };

    /// <summary>The height a wall / fence / hedge has when none was set (Pad 0), metres.</summary>
    public static float DefaultHeight(byte kind) => kind switch
    {
        TerrainFeature.Wall => FeatureExpander.DefaultWallHeight, TerrainFeature.Fence => FeatureExpander.DefaultFenceHeight, TerrainFeature.Hedge => FeatureExpander.DefaultHedgeHeight, _ => 0f,
    };

    /// <summary>The material a wall / fence / hedge has when none was set (an empty Template).</summary>
    public static string DefaultMaterial(byte kind) => kind switch
    {
        TerrainFeature.Fence => TerrainMaterialLibrary.DefaultFenceMaterial, TerrainFeature.Hedge => "hedge", _ => TerrainMaterialLibrary.DefaultMaterial,
    };

    /// <summary>The Pad the tab SHOWS: the kind's default when the stored one is 0 (height, spacing, count); a wood's / scatter's 0 stays 0 (its own default is the recipe's).</summary>
    public static float PadShown(in TerrainFeature f) => f.Pad > 0f ? f.Pad : f.Kind switch
    {
        TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge => DefaultHeight(f.Kind),
        TerrainFeature.Row => FeatureExpander.DefaultRowSpacing,
        TerrainFeature.Group => FeatureExpander.DefaultGroupCount,
        _ => 0f,
    };

    /// <summary>Switches the kind within its family — one edit step; the material and height go back to the new kind's defaults (they differ by kind); refused for another shape, another family or no change.</summary>
    public static bool SetKind(EntityRepository world, Entity e, byte kind)
    {
        if (!IsEditable(world, e)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        var family = FamilyOf(f.Kind);
        if (family is null || f.Kind == kind || System.Array.IndexOf(family, kind) < 0) return false;
        EditSteps.Begin(world, "Change terrain kind");
        try
        {
            ref var w = ref world.GetComponentRW<TerrainFeature>(e);
            w.Kind = kind;
            if (family == WallKinds) { w.Template = default; w.Pad = 0f; }
        }
        finally { EditSteps.End(world); }
        RenameIfDefault(world, e, f.Kind, kind);
        return true;
    }

    private static void RenameIfDefault(EntityRepository world, Entity e, byte from, byte to)
    {
        if (!world.IsComponentTypeRegistered<EntityInfo>() || !world.HasComponent<EntityInfo>(e)) return;
        if (world.GetComponentRO<EntityInfo>(e).Name.ToString() != KindName(from)) return;   // a name the author chose stays
        ref var info = ref world.GetComponentRW<EntityInfo>(e);
        info.Name = new FixedString64(KindName(to));
    }

    /// <summary>Sets a wall / fence / hedge's material (a name of the library; empty = the kind's default) — one edit step; refused for an unknown name, another kind or no change.</summary>
    public static bool SetMaterial(EntityRepository world, Entity e, string material)
    {
        if (!IsEditable(world, e) || material is null) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (System.Array.IndexOf(WallKinds, f.Kind) < 0 || f.Template.ToString() == material) return false;
        if (material.Length > 0 && !TerrainMaterialLibrary.Shared.TryGet(material, out _)) return false;
        EditSteps.Begin(world, "Set material");
        try { world.GetComponentRW<TerrainFeature>(e).Template = new FixedString32(material); }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>The range the Pad may take by kind; 0 is always allowed (= the default).</summary>
    public static bool PadInRange(byte kind, float v) => !float.IsNaN(v) && !float.IsInfinity(v) && (v == 0f || kind switch
    {
        TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge => v >= 0.1f && v <= 50f,   // height
        TerrainFeature.Wood => v >= 1f && v <= 10000f,                                                    // trees per ha
        TerrainFeature.Row => v >= 0.5f && v <= 100f,                                                     // spacing
        TerrainFeature.Group => v >= 1f && v <= FeatureExpander.MaxScatterItems && v == MathF.Floor(v),  // count
        TerrainFeature.Scatter => v > 0f && v <= 1000f,                                                   // items per 100 m²
        _ => false,
    });

    /// <summary>Sets the Pad (height · density · spacing · count) — one edit step; refused when out of range, not a kind that has one, or unchanged.</summary>
    public static bool SetPad(EntityRepository world, Entity e, float value)
    {
        if (!IsEditable(world, e)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (!PadInRange(f.Kind, value) || f.Pad == value) return false;
        EditSteps.Begin(world, f.Kind switch { TerrainFeature.Row => "Set tree spacing", TerrainFeature.Group => "Set tree count", TerrainFeature.Wood or TerrainFeature.Scatter => "Set density", _ => "Set height" });
        try { world.GetComponentRW<TerrainFeature>(e).Pad = value; }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>Sets a scatter's recipe — one edit step; refused for another kind or no change.</summary>
    public static bool SetRecipe(EntityRepository world, Entity e, string recipe)
    {
        if (!IsEditable(world, e) || string.IsNullOrEmpty(recipe)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (f.Kind != TerrainFeature.Scatter || f.Template.ToString() == recipe) return false;
        EditSteps.Begin(world, "Set scatter recipe");
        try { world.GetComponentRW<TerrainFeature>(e).Template = new FixedString32(recipe); }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>Whether the kind draws from a seed (a wood, a scrub, a group of trees, a scatter).</summary>
    public static bool HasSeed(byte kind) => kind is TerrainFeature.Wood or TerrainFeature.Scrub or TerrainFeature.Group or TerrainFeature.Scatter;

    /// <summary>Sets the seed — one edit step; refused for a kind with none or no change.</summary>
    public static bool SetSeed(EntityRepository world, Entity e, uint seed)
    {
        if (!IsEditable(world, e)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (!HasSeed(f.Kind) || f.Seed == seed) return false;
        EditSteps.Begin(world, "Set seed");
        try { world.GetComponentRW<TerrainFeature>(e).Seed = seed; }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>The dice: a fresh four-digit seed (<see cref="TerrainBirth.NewSeed"/>) — one edit step.</summary>
    public static bool Reroll(EntityRepository world, Entity e)
    {
        uint seed = TerrainBirth.NewSeed();
        if (IsEditable(world, e) && world.GetComponentRO<TerrainFeature>(e).Seed == seed) seed++;
        return SetSeed(world, e, seed);
    }

    /// <summary>Whether the kind is a single item with a heading (a tree, a clutter item, a house).</summary>
    public static bool HasHeading(byte kind) => kind is TerrainFeature.Tree or TerrainFeature.Clutter or TerrainFeature.House;

    /// <summary>Sets the heading (degrees, normalised to 0…360) — one edit step; refused for a kind with none or no change.</summary>
    public static bool SetHeading(EntityRepository world, Entity e, float degrees)
    {
        if (!IsEditable(world, e) || float.IsNaN(degrees) || float.IsInfinity(degrees)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        degrees = ((degrees % 360f) + 360f) % 360f;
        if (!HasHeading(f.Kind) || f.HeadingDeg == degrees) return false;
        EditSteps.Begin(world, "Set heading");
        try { world.GetComponentRW<TerrainFeature>(e).HeadingDeg = degrees; }
        finally { EditSteps.End(world); }
        return true;
    }
}
