using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ Terrain authoring U-2 (docs/DESIGN_Terrain_Authoring.md §15.U-2) — what a placed terrain BLOCK / HOUSE is BORN with, in ONE place (the sibling of <see cref="MacroBirth"/>): the placement JSON carries the pick's spec
/// under <see cref="Key"/> (<c>block:hamlet</c> / <c>house:house-2f</c>); <see cref="Split"/> takes it out (the attribute compiler never sees it) and <see cref="BornWith"/> adds the <c>TerrainBlock</c> /
/// <c>TerrainFeature</c> and a name to the new entity's components. Both are called by <c>ScenarioSpawnAdapter.ArmPlacement</c> — the point commit every host passes. ⭐ One click = ONE entity = one undo step (B-2, B-6).
/// </summary>
public static class TerrainBirth
{
    /// <summary>The placement JSON's key for the pick spec (not a component attribute).</summary>
    public const string Key = "$terrain";

    public static string PlacementJson(TerrainPick pick)
        => JsonSerializer.Serialize(new Dictionary<string, string> { [Key] = pick.Spec }, FdpJsonOptionsRegistry.DefaultRelaxed);

    /// <summary>The placement JSON without the terrain key, and the spec it carried (null when none). Unparseable JSON passes through untouched.</summary>
    public static (string? Json, string? Spec) Split(string? placementJson)
    {
        if (string.IsNullOrWhiteSpace(placementJson) || !placementJson.Contains(Key, StringComparison.Ordinal)) return (placementJson, null);
        try
        {
            if (JsonNode.Parse(placementJson) is not JsonObject obj || !obj.Remove(Key, out var node)) return (placementJson, null);
            string? spec = node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
            return (obj.Count == 0 ? null : obj.ToJsonString(), spec);
        }
        catch (JsonException) { return (placementJson, null); }
    }

    /// <summary>(kind, id) of a spec (<c>block:hamlet</c>); null when it is not one.</summary>
    public static (TerrainPickKind Kind, string Id)? Parse(string? spec)
    {
        if (spec is null) return null;
        if (spec.StartsWith("block:", StringComparison.Ordinal) && spec.Length > 6) return (TerrainPickKind.Block, spec.Substring(6));
        if (spec.StartsWith("house:", StringComparison.Ordinal) && spec.Length > 6) return (TerrainPickKind.House, spec.Substring(6));
        if (spec.StartsWith("feature:", StringComparison.Ordinal) && FeatureOf(spec) != null) return (TerrainPickKind.Feature, spec.Substring(8));
        return null;
    }

    /// <summary>⭐ U-7 — (kind, template, shape) of a <c>feature:&lt;kind&gt;:&lt;template&gt;:&lt;Shape&gt;</c> spec; null when it is not one.</summary>
    public static (byte Kind, string Template, TerrainShape Shape)? FeatureOf(string? spec)
    {
        if (spec is null || !spec.StartsWith("feature:", StringComparison.Ordinal)) return null;
        var parts = spec.Substring(8).Split(':');
        if (parts.Length != 3 || !byte.TryParse(parts[0], out var kind) || kind == 0 || !Enum.TryParse<TerrainShape>(parts[2], out var shape)) return null;
        return (kind, parts[1], shape);
    }

    /// <summary>The heading (degrees, 0 = the recipe as drawn) a placed transform's yaw stands for — a placement faces NORTH (yaw 90°) by default and that IS heading 0.</summary>
    public static float HeadingOf(System.Numerics.Quaternion rotation)
    {
        float yaw = 2f * MathF.Atan2(rotation.Z, rotation.W) * (180f / MathF.PI);
        float h = (yaw - 90f) % 360f;
        if (h < 0f) h += 360f;
        return MathF.Round(h * 1000f) / 1000f;
    }

    /// <summary>The name a placed feature gets: its kind (<c>Wall</c>, <c>Hedge</c>), a clutter item its type (<c>Barrel</c>).</summary>
    public static string FeatureName((byte Kind, string Template, TerrainShape Shape) f)
        => f.Kind == TerrainFeature.Clutter && f.Template.Length > 0 ? TerrainPickCatalog.Title(f.Template) : f.Kind switch
        {
            TerrainFeature.Wall => "Wall", TerrainFeature.Fence => "Fence", TerrainFeature.Hedge => "Hedge", TerrainFeature.Wood => "Wood", TerrainFeature.Tree => "Tree",
            TerrainFeature.Scrub => "Scrub", TerrainFeature.Water => "Water", TerrainFeature.Bridge => "Bridge", TerrainFeature.Scatter => "Scatter",
            TerrainFeature.Ground => f.Template switch { "level" => "Level ground", "lower" => "Lowered ground", "ditch" => "Ditch", "at-ditch" => "Anti-tank ditch", "foxhole" => "Foxhole", _ => "Raised ground" },   // ⭐ CE-4592
            _ => "Terrain feature",
        };

    /// <summary>A fresh seed for a placed block — four digits, as the mockup's "seed 4471". ⭐ A user action, so <see cref="Random.Shared"/> is fine (the block's layout is a function of the seed ever after).</summary>
    public static uint NewSeed() => (uint)Random.Shared.Next(1000, 10000);

    /// <summary>⭐ CE-4592 (P-5) — a raise is 2 m, a lower 1 m, a level the ground where it was clicked (the placed transform's Z) — edited in Details after.</summary>
    public const float DefaultRaise = 2f, DefaultLower = 1f;

    /// <summary>
    /// ⭐ CE-4592 (P-5, G-14, V-28) — a new ground shape: its mode, its default value, and its DRAW ORDER (<see cref="TerrainFeature.Seed"/>) = the authoring moment in seconds since 2026,
    /// so the shape drawn last is the newest and wins where shapes overlap (an authoring clock — never the simulation's).
    /// </summary>
    public static TerrainFeature GroundShape(string mode, List<object> components)
    {
        float z = 0f;
        foreach (var c in components) if (c is SimTransform t) { z = t.Position.Z; break; }
        float value = mode switch { "raise" => DefaultRaise, "lower" => DefaultLower, "level" => z, _ => 0f };   // ⭐ CE-4593 — a ditch / foxhole: 0 = its preset's depth
        return new TerrainFeature { Kind = TerrainFeature.Ground, Template = new FixedString32(mode), Pad = value, Seed = DrawOrderNow() };
    }

    /// <summary>The draw order of a shape drawn now: seconds since 2026-01-01 (monotonic across sessions; a later shape is a higher number).</summary>
    public static uint DrawOrderNow() => (uint)Math.Max(1.0, (DateTime.UtcNow - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);

    /// <summary>
    /// Adds the <see cref="TerrainBlock"/> (<paramref name="tkbType"/> 8817) or <see cref="TerrainFeature"/> (8818) for <paramref name="spec"/> to <paramref name="components"/>, heading from
    /// <paramref name="rotation"/>; also its name (the recipe's / template's title) unless one is there. Never twice. ⛔ A spec of the other kind than the type adds nothing.
    /// </summary>
    public static void BornWith(long tkbType, string? spec, System.Numerics.Quaternion rotation, List<object> components, uint? seed = null, float extraHeadingDeg = 0f)
    {
        if (Parse(spec) is not { } p) return;
        if (p.Kind == TerrainPickKind.Block && tkbType == TkbEntityTypes.TerrainBlock && !components.Exists(c => c is TerrainBlock))
            components.Add(new TerrainBlock { Recipe = new FixedString32(p.Id), Seed = seed ?? NewSeed(), HeadingDeg = (HeadingOf(rotation) + extraHeadingDeg) % 360f, Fit = TerrainBlock.FitAll });
        else if (p.Kind == TerrainPickKind.House && tkbType == TkbEntityTypes.TerrainFeature && !components.Exists(c => c is TerrainFeature))
            components.Add(new TerrainFeature { Kind = TerrainFeature.House, Template = new FixedString32(p.Id), HeadingDeg = (HeadingOf(rotation) + extraHeadingDeg) % 360f });
        else if (p.Kind == TerrainPickKind.Feature && tkbType == TkbEntityTypes.TerrainFeature && FeatureOf(spec) is { } g && g.Kind == TerrainFeature.Ground && !components.Exists(c => c is TerrainFeature))
            components.Add(GroundShape(g.Template, components) with { HeadingDeg = g.Shape == TerrainShape.Point ? (HeadingOf(rotation) + extraHeadingDeg) % 360f : 0f });   // ⭐ CE-4592 (P-5) — its value and its draw order; a foxhole its heading
        else if (p.Kind == TerrainPickKind.Feature && tkbType == TkbEntityTypes.TerrainFeature && FeatureOf(spec) is { } f && !components.Exists(c => c is TerrainFeature))
            components.Add(new TerrainFeature { Kind = f.Kind, Template = new FixedString32(f.Template), HeadingDeg = f.Shape == TerrainShape.Point ? (HeadingOf(rotation) + extraHeadingDeg) % 360f : 0f, Seed = seed ?? NewSeed() });
        else return;
        if (!components.Exists(c => c is EntityInfo))
            components.Add(new EntityInfo { Name = new FixedString64(p.Kind == TerrainPickKind.Feature && FeatureOf(spec) is { } ff ? FeatureName(ff) : TerrainPickCatalog.Title(p.Id)), ForceId = ForceId.Neutral });
    }
}
