using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common.Services;

/// <summary>What <see cref="BlockOperations.Unpack"/> expands a block over: the resident base terrain and the folder's side files (the same the live overlay reads).</summary>
public sealed record BlockExpansionContext(TerrainWorld Base, TerrainAssets Assets, IReadOnlyList<string?> Roots, TerrainRoads? Roads, IUniqueBuildings? Unique = null)
{
    /// <summary>The context of <paramref name="residency"/>'s resident terrain (its base world and terrain folder); null when none is resident or its folder is not found.</summary>
    public static BlockExpansionContext? Of(TerrainResidency residency, IUniqueBuildings? unique = null)
    {
        var (world, _, folder) = residency.ExpansionBase();
        if (world == null || string.IsNullOrEmpty(folder)) return null;
        return new BlockExpansionContext(world, TerrainAssets.ForFolder(folder), new List<string?>(TerrainRecipe.RootsFor(folder)), TerrainRoads.ForFolder(folder), unique);
    }
}

/// <summary>The outcome of a block operation: done, or refused with the words why (nothing was changed).</summary>
public readonly record struct BlockOpResult(bool Done, string? Why)
{
    public static readonly BlockOpResult Ok = new(true, null);
    public static BlockOpResult Refused(string why) => new(false, why);
}

/// <summary>
/// ⭐ T-4 (CE-4431, docs/DESIGN_Terrain_Authoring.md §18h H-1…H-4) — what an author does to a PLACED <see cref="TerrainBlock"/>: <see cref="Reroll"/>, <see cref="ChangeRecipe"/>, <see cref="Unpack"/>. Each is ONE authoring edit —
/// ⭐ one <see cref="EditSteps"/> bracket (the outer pair <see cref="Edits.Submit"/> nests into), so the editor's <c>WorldUndoRecorder</c> records the whole change (a seed, or a destroyed block plus N created features and
/// the re-keyed doors) as ONE step of the ONE <c>EditHistory</c>: undo restores the block by the world snapshot, with no undo code here. A world that is replaying, or a station whose <see cref="IEditGate"/> refuses, changes nothing.
/// <para>⭐ The entities are written DIRECTLY inside the bracket (synchronously), not through the creation-request path: that path completes after the bracket closed and would not be part of the step. ⚠ A feature made here has
/// no network identity — when the placing UI gives <c>TerrainFeature</c> a TKB type (U-2, B-5), <see cref="CreateFeature"/> is the one place to switch.</para>
/// </summary>
public static class BlockOperations
{
    /// <summary>Label of the re-roll step.</summary>
    public const string RerollLabel = "Re-roll block", ChangeRecipeLabel = "Change block recipe", UnpackLabel = "Unpack block";

    /// <summary>
    /// The seed after <paramref name="seed"/> — a PCG step (full period, so never the same seed back): ⭐ derived from the current one, so the redo of a re-roll is the SAME layout as the first do (H-1; a random seed would differ).
    /// </summary>
    public static uint NextSeed(uint seed) => unchecked(seed * 747796405u + 2891336453u);

    /// <summary>H-1 — the block's seed becomes <see cref="NextSeed"/> of its current one; place, heading and recipe are kept.</summary>
    public static BlockOpResult Reroll(EntityRepository world, Entity block)
    {
        if (Check(world, block, RerollLabel) is { } no) return no;
        EditSteps.Begin(world, RerollLabel);
        try { var b = world.GetComponent<TerrainBlock>(block); b.Seed = NextSeed(b.Seed); world.SetComponent(block, b); }
        finally { EditSteps.End(world); }
        return BlockOpResult.Ok;
    }

    /// <summary>H-2 — the block's recipe becomes <paramref name="recipeId"/>; place, heading, seed and fit are kept.</summary>
    public static BlockOpResult ChangeRecipe(EntityRepository world, Entity block, string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId)) return BlockOpResult.Refused("a recipe id is needed");
        if (System.Text.Encoding.UTF8.GetByteCount(recipeId) > FixedString32.MaxLength) return BlockOpResult.Refused($"the recipe id '{recipeId}' is longer than {FixedString32.MaxLength} bytes");
        if (Check(world, block, ChangeRecipeLabel) is { } no) return no;
        EditSteps.Begin(world, ChangeRecipeLabel);
        try { var b = world.GetComponent<TerrainBlock>(block); b.Recipe = new FixedString32(recipeId); world.SetComponent(block, b); }
        finally { EditSteps.End(world); }
        return BlockOpResult.Ok;
    }

    /// <summary>
    /// H-3 — the block becomes its parts: ONE <see cref="TerrainFeature"/> per part the ONE <see cref="OverlayExpansion"/> makes (a house → House with template, position and heading; a tree, a clutter prism with its type; a wall / fence / hedge line, a wood / scrub / water area, a lane → the kind with an <see cref="EditablePolyline"/>), then
    /// the block is destroyed — all in ONE bracket. ⭐ Same parts: the same geometry, materials and ORDER (the new guids rise in the order of the block's part labels, and a part label is <c>&lt;guid&gt;/0</c>); only the KEYS change,
    /// so every door entity of the block's houses is re-keyed to its house's new key in the same step, and keeps its state; ⭐ so is a damaged house's <c>BuildingDamage</c> entity (CE-3439): the bake that follows re-points it by that key (<c>BuildingDamages.Remap</c>).
    /// </summary>
    public static BlockOpResult Unpack(EntityRepository world, Entity block, BlockExpansionContext ctx)
    {
        if (Check(world, block, UnpackLabel) is { } no) return no;
        if (!world.HasComponent<EntityGuid>(block)) return BlockOpResult.Refused("the block has no scenario key");
        Guid guid = world.GetComponentRO<EntityGuid>(block).Value;
        var b = world.GetComponentRO<TerrainBlock>(block);
        var at = world.GetComponentRO<SimTransform>(block);

        // everything that can fail is done BEFORE the bracket opens: a refusal changes nothing
        List<JsonObject> parts;
        try
        {
            var x = OverlayExpansion.Block(guid.ToString("D"), b.Recipe.ToString() ?? "", b.Seed, b.HeadingDeg, b.Fit, new Vector2(at.Position.X, at.Position.Y), ctx.Base, ctx.Assets, ctx.Roots, ctx.Roads, ctx.Unique);
            if (x.Templates.Count > 0) return BlockOpResult.Refused("the block's unique houses are generated templates that exist only in its overlay: they cannot be unpacked to features");
            parts = new List<JsonObject>(x.Features);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return BlockOpResult.Refused($"the block does not expand: {ex.Message}"); }
        OverlayExpansion.SortByLabel(parts);   // ⭐ the overlay's own order — the new keys rise in it
        var made = new List<(string OldLabel, Guid NewGuid, byte Kind, string Template, float Heading, float Pad, Vector2 At, Vector2[]? Shape)>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            if (!TryRead(parts[i], PartGuid(guid, i), out var m, out string? why)) return BlockOpResult.Refused(why!);
            made.Add(m);
        }

        string terrain = ctx.Base.Name ?? "";
        var doors = new List<(Entity Door, TerrainObjectKey Key)>();
        foreach (var e in world.Query().WithManaged<TerrainObjectKey>().WithLifecycle(EntityLifecycle.All).Build())
            doors.Add((e, world.GetComponent<TerrainObjectKey>(e)));

        EditSteps.Begin(world, UnpackLabel);
        try
        {
            foreach (var m in made)
            {
                CreateFeature(world, m.NewGuid, m.Kind, m.Template, m.Heading, m.Pad, m.At, at.Position.Z, m.Shape);
                // the doors of this part (a house): "<terrain>/<old part key>/<doorId>" → "<terrain>/<new feature key>/0/<doorId>"
                string oldPrefix = Join(terrain, m.OldLabel) + "/", newPrefix = Join(terrain, m.NewGuid.ToString("D") + "/0") + "/";
                string oldBuilding = Join(terrain, m.OldLabel), newBuilding = Join(terrain, m.NewGuid.ToString("D") + "/0");
                foreach (var (door, key) in doors)
                {
                    if (!world.IsAlive(door)) continue;
                    if (key.Key.StartsWith(oldPrefix, StringComparison.Ordinal)) world.SetManagedComponent(door, key with { Key = newPrefix + key.Key.Substring(oldPrefix.Length) });
                    else if (key.Key == oldBuilding) world.SetManagedComponent(door, key with { Key = newBuilding });   // ⭐ CE-3439 — a damaged house's BuildingDamage entity: the next bake finds its house by this key (BuildingDamages.Remap)
                }
            }
            world.DestroyEntity(block);
        }
        finally { EditSteps.End(world); }
        return BlockOpResult.Ok;
    }

    /// <summary>The refusal an operation on <paramref name="block"/> meets (a recording, the station's gate, a dead or non-block entity), or null when it may go. ⭐ The same two checks <see cref="Edits.Submit"/> makes.</summary>
    private static BlockOpResult? Check(EntityRepository world, Entity block, string label)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(block) || !world.HasComponent<TerrainBlock>(block)) return BlockOpResult.Refused("not a terrain block");
        if (ReplayState.IsActive(world)) return BlockOpResult.Refused(Edits.ReplayRefusal);
        var why = EditGate.Of(world)?.Refuse(world, EditOp.Write(block, Array.Empty<Fdp.Toolkit.Replication.Patching.EntityAttributeChange>(), label));
        return why == null ? null : BlockOpResult.Refused(why);
    }

    /// <summary>
    /// The guid of the feature made from part <paramref name="index"/> (in label order) of the block <paramref name="block"/>: the block's guid with its last 32 bits set to <c>index + 1</c> — ⭐ ascending in the part order (the labels sort
    /// the same way) and DETERMINISTIC (an unpack that is undone and done again makes the same keys).
    /// </summary>
    public static Guid PartGuid(Guid block, int index)
    {
        var bytes = block.ToByteArray();   // little-endian first three groups; the LAST 4 bytes are the big-endian tail of the "D" text
        uint tail = (uint)(bytes[12] << 24 | bytes[13] << 16 | bytes[14] << 8 | bytes[15]);
        uint n = (uint)index + 1u;
        if (tail >= 1u && n >= tail) n++;   // never the block's own guid (a feature must not share the destroyed block's key): still strictly ascending
        bytes[12] = (byte)(n >> 24); bytes[13] = (byte)(n >> 16); bytes[14] = (byte)(n >> 8); bytes[15] = (byte)n;
        return new Guid(bytes);
    }

    private static string Join(string terrain, string rest) => terrain.Length == 0 ? rest : terrain + "/" + rest;

    private static bool TryRead(JsonObject part, Guid newGuid, out (string OldLabel, Guid NewGuid, byte Kind, string Template, float Heading, float Pad, Vector2 At, Vector2[]? Shape) m, out string? why)
    {
        m = default; why = null;
        var props = (JsonObject)part["properties"]!; var geo = (JsonObject)part["geometry"]!;
        string label = OverlayExpansion.Label(part), kind = (string?)props["kind"] ?? "";
        var coords = geo["coordinates"]!;
        Vector2 P(JsonNode n) => new(n[0]!.GetValue<float>(), n[1]!.GetValue<float>());

        // a point part: a house, a tree
        if (kind == "building")
        {
            string template = (string?)props["template"] ?? "";
            if (System.Text.Encoding.UTF8.GetByteCount(template) > FixedString32.MaxLength) { why = $"the house template '{template}' is longer than {FixedString32.MaxLength} bytes"; return false; }
            m = (label, newGuid, TerrainFeature.House, template, props["rotation"]?.GetValue<float>() ?? 0f, props["pad"]?.GetValue<float>() ?? 0f, P(coords), null);
            return true;
        }
        if (kind == "tree") { m = (label, newGuid, TerrainFeature.Tree, "", 0f, 0f, P(coords), null); return true; }

        // a clutter prism (T-3 hints: its type and turn ride in the part): its middle is the feature's place
        if (kind == "prism" && props["clutter"] != null)
        {
            var ring0 = Ring((JsonArray)((JsonArray)coords)[0]!, P);
            m = (label, newGuid, TerrainFeature.Clutter, (string)props["clutter"]!, props["heading"]?.GetValue<float>() ?? 0f, 0f, Middle(ring0), null);
            return true;
        }

        // a line part: a wall, a fence, a hedge (a wall of the hedge material)
        if (kind is "wall" or "fence")
        {
            var pts = new List<Vector2>(); foreach (var n in (JsonArray)coords) pts.Add(P(n!));
            string material = (string?)props["material"] ?? "";
            byte fk = kind == "fence" ? TerrainFeature.Fence : material == "hedge" ? TerrainFeature.Hedge : TerrainFeature.Wall;
            bool isDefault = fk == TerrainFeature.Fence ? material == TerrainMaterialLibrary.DefaultFenceMaterial : fk == TerrainFeature.Hedge || material == TerrainMaterialLibrary.DefaultMaterial;
            string tpl = isDefault ? "" : material;
            if (System.Text.Encoding.UTF8.GetByteCount(tpl) > FixedString32.MaxLength) { why = $"the material '{tpl}' is longer than {FixedString32.MaxLength} bytes"; return false; }
            var c0 = pts[0];
            var rel = new Vector2[pts.Count]; for (int i = 0; i < rel.Length; i++) rel[i] = pts[i] - c0;
            float dflt = fk == TerrainFeature.Fence ? FeatureExpander.DefaultFenceHeight : fk == TerrainFeature.Hedge ? FeatureExpander.DefaultHedgeHeight : FeatureExpander.DefaultWallHeight;
            float h = props["height"]?.GetValue<float>() ?? dflt;
            m = (label, newGuid, fk, tpl, 0f, MathF.Abs(h - dflt) < 0.0005f ? 0f : h, c0, rel);
            return true;
        }

        // an area part: a wood, scrub, water, a lane (an open surface)
        if (kind == "surface")
        {
            byte ak = (string?)props["surface"] switch { "forest" => TerrainFeature.Wood, "scrub" => TerrainFeature.Scrub, "water" => TerrainFeature.Water, "open" => TerrainFeature.Lane, _ => (byte)0 };
            if (ak == 0) { why = $"a surface part of type '{props["surface"]}' has no feature kind to unpack to"; return false; }
            var pts = Ring((JsonArray)((JsonArray)coords)[0]!, P);
            var centre = BoxMiddle(pts);
            var rel = new Vector2[pts.Count]; for (int i = 0; i < rel.Length; i++) rel[i] = pts[i] - centre;
            m = (label, newGuid, ak, "", 0f, ak == TerrainFeature.Wood ? props["density"]?.GetValue<float>() ?? 0f : 0f, centre, rel);
            return true;
        }
        why = $"a block part of kind '{kind}' has no feature kind to unpack to";
        return false;
    }

    /// <summary>A GeoJSON ring without its closing point.</summary>
    private static List<Vector2> Ring(JsonArray ring, Func<JsonNode, Vector2> p)
    {
        var pts = new List<Vector2>(ring.Count);
        for (int i = 0; i < ring.Count - 1; i++) pts.Add(p(ring[i]!));   // the closing point repeats the first
        return pts;
    }

    private static Vector2 BoxMiddle(List<Vector2> pts)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var q in pts) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
        return new Vector2(MathF.Round((min.X + max.X) * 500f) / 1000f, MathF.Round((min.Y + max.Y) * 500f) / 1000f);
    }

    private static Vector2 Middle(List<Vector2> pts) => BoxMiddle(pts);

    /// <summary>The ONE place a feature entity is made (⭐ switch it to the TKB creation path when the placing UI has a type for it): its position, <see cref="TerrainFeature"/>, scenario guid and — for a shape — the relative <see cref="EditablePolyline"/>.</summary>
    private static void CreateFeature(EntityRepository world, Guid guid, byte kind, string template, float heading, float pad, Vector2 at, float z, Vector2[]? shape)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(at.X, at.Y, z), Rotation = Quaternion.Identity });
        world.AddComponent(e, new TerrainFeature { Kind = kind, Template = new FixedString32(template), HeadingDeg = heading, Pad = pad });
        world.AddComponent(e, new EntityGuid(guid));
        if (shape != null) world.SetManagedComponent(e, new EditablePolyline { Points = shape });
    }
}
