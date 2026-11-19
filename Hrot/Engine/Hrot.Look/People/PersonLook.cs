using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.Map3D;

namespace Hrot.Look.People;

/// <summary>⭐ CE-4671 LK-S2 — who a person TYPE is, as its look needs it (the person half of the map's type look): a soldier, a child, his kit, elderly,
/// his sex, the type's own wardrobe, and the type's colour (whether the type names one).</summary>
public readonly record struct PersonFacts(bool Soldier, bool Child, PersonKit Kit, bool Elderly, PersonSex Sex, Wardrobe? Wardrobe, Rgba Colour, bool TypeColoured);

/// <summary>⭐ CE-4671 LK-S2 — one person's resolved look: what he wears (<see cref="Outfit"/>, his face in it), from his <paramref name="Seed"/> on his
/// <paramref name="Side"/>.</summary>
public readonly record struct PersonAppearance(Outfit Outfit, long Seed, ForceId Side);

/// <summary>
/// ⭐⭐ CE-4671 LK-S2 (DESIGN_Vehicle_Appearance_State.md §7 LK-4, §7c) — a person's LOOK, the same for HROT's map and an external renderer's gateway: a pure function
/// of his components, his type in the TKB and his seed (LK-5 — never the wall clock, never a frame count). The outfit rules are <see cref="Outfits"/>; what is
/// here are the INPUTS the map used to gather itself (the seed, the side, his own colour, the type's facts) — so a gateway dresses a man exactly as the map does.
/// The map's <c>EntityBodyLayer3D</c> forwards its seed / side / colour reads and takes the person half of its type look from <see cref="FactsOf"/>.
/// </summary>
public static class PersonLook
{
    /// <summary>A person's box when the type gives no size (m): what the map's kit catalogue uses too.</summary>
    public static readonly Vector3 DefaultSize = new(0.35f, 0.55f, 1.8f);

    /// <summary>⭐ CE-1099 (§6q O-3) — a person shorter than this (m) is a child (when the type does not say his age).</summary>
    public const float ChildHeight = 1.5f;

    /// <summary>A person's colour when neither he nor his type names one.</summary>
    public static readonly Rgba DefaultColour = new(96, 110, 72, 255);

    private sealed class Cached { public PersonFacts? Facts; }
    private static readonly ConditionalWeakTable<TkbTemplate, Cached> s_facts = new();

    /// <summary>⭐ The type's person facts — null when the type is not a person. Computed once per template (the wardrobe is flattened once, CE-4648 OF-7).</summary>
    public static PersonFacts? FactsOf(TkbTemplate? t)
    {
        if (t == null) return null;
        return s_facts.GetValue(t, static t => new Cached { Facts = Compute(t) }).Facts;

        static PersonFacts? Compute(TkbTemplate t)
        {
            if (t.GetDescriptor<EffectVisualDto>() != null || t.DisType.Kind == 2 || t.GetDescriptor<MunitionVisualDto>() != null) return null;
            if (VisualFamilies.Of(t) != VisualFamily.Person) return null;
            var person = t.GetDescriptor<PersonDto>();
            var typeColour = Rgba.FromHex(t.GetDescriptor<VisualDefinitionDto>()?.ColorHex);
            return new PersonFacts(
                Soldier: VisualFamilies.IsSoldier(t),
                Child: person != null ? person.Age == AgeBand.Child : HeightOf(t) < ChildHeight,
                Kit: VisualFamilies.PersonKitOf(t),
                Elderly: person?.Age == AgeBand.Elderly,
                Sex: person?.Sex ?? PersonSex.Unspecified,
                Wardrobe: Wardrobe.Of(t.GetDescriptor<OutfitDto>()),
                Colour: typeColour ?? DefaultColour,
                TypeColoured: typeColour != null);
        }
    }

    /// <summary>A person type's height (m): its engine-neutral geometry, else its model's shape height, else the default (as the map sizes his box).</summary>
    public static float HeightOf(TkbTemplate t)
        => t.GetDescriptor<BodyGeometryDto>() is { Length: > 0f, Width: > 0f, Height: > 0f } g ? g.Height
         : t.GetDescriptor<StrideRenderModelDefDto>()?.ShapeHeight is > 0f and var tall ? tall : DefaultSize.Z;

    /// <summary>⭐ CE-1098 / CE-1099 — a man's seed: his network id (the same on every node), else his entity index.</summary>
    public static long SeedOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<NetworkIdentity>() && world.HasComponent<NetworkIdentity>(e) && world.GetComponentRO<NetworkIdentity>(e).Value is int net && net != 0
            ? net : e.Index;

    /// <summary>⭐ CE-1099 (§6q O-4) — his side (the replicated <see cref="EntityInfo.ForceId"/>); Neutral where there is none.</summary>
    public static ForceId SideOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(e) ? world.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;

    /// <summary>⭐ CE-1033 S5c (§3.7) — a body's colour: the entity's own <see cref="EntityAppearance"/> → its <c>VisualData.ColorHex</c> → <paramref name="fallback"/>.</summary>
    public static Rgba ColourOf(EntityRepository world, Entity e, Rgba fallback)
    {
        if (world.IsComponentTypeRegistered<EntityAppearance>() && world.HasComponent<EntityAppearance>(e))
        {
            var a = world.GetComponentRO<EntityAppearance>(e);
            if (a.HasColour) return new Rgba(a.R, a.G, a.B, (byte)255);
        }
        if (!world.IsComponentTypeRegistered<VisualData>() || !world.HasComponent<VisualData>(e)) return fallback;
        return Rgba.FromHex(world.GetComponentRO<VisualData>(e).ColorHex) ?? fallback;
    }

    /// <summary>⭐ CE-1235 — true when the ENTITY itself names a colour (authored appearance or visual data), whatever his type says.</summary>
    public static bool HasEntityColour(EntityRepository world, Entity e)
    {
        if (world.IsComponentTypeRegistered<EntityAppearance>() && world.HasComponent<EntityAppearance>(e) && world.GetComponentRO<EntityAppearance>(e).HasColour)
            return true;
        return world.IsComponentTypeRegistered<VisualData>() && world.HasComponent<VisualData>(e)
            && Rgba.FromHex(world.GetComponentRO<VisualData>(e).ColorHex) != null;
    }

    /// <summary>
    /// ⭐ CE-1099 (§6q O-1) / ⭐ CE-1235 (O-4a, R-1020) — the colour a person's OUTFIT takes as his own, or null for none. A civilian (and a police officer): his
    /// entity's or his type's. A SOLDIER: only one HE names — a type colour is per type, never per side, so his side picks the cloth instead. ⚠ The type's colour
    /// is stamped onto every spawned entity, so his own is one that DIFFERS from the type's.
    /// </summary>
    public static Rgba? OwnColourOf(EntityRepository world, Entity e, in PersonFacts f)
    {
        var colour = ColourOf(world, e, f.Colour);
        if (!f.Soldier || f.Kit == PersonKit.Security) return f.TypeColoured || HasEntityColour(world, e) ? colour : null;
        return HasEntityColour(world, e) && !(f.TypeColoured && colour.R == f.Colour.R && colour.G == f.Colour.G && colour.B == f.Colour.B) ? colour : null;
    }

    /// <summary>⭐ The person's look: null when <paramref name="e"/> is not a person of a type the TKB knows.</summary>
    public static PersonAppearance? Of(EntityRepository world, Entity e, ITkbDatabase? tkb)
    {
        if (tkb == null || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.HasComponent<TkbIdentity>(e)) return null;
        long type = world.GetComponentRO<TkbIdentity>(e).TkbType;
        if (!tkb.TryGetByType(type, out var t) || FactsOf(t) is not { } f) return null;
        long seed = SeedOf(world, e);
        var side = SideOf(world, e);
        return new PersonAppearance(Outfits.For(seed, type, f.Soldier, f.Child, OwnColourOf(world, e, f), side, f.Kit, f.Elderly, f.Sex, f.Wardrobe), seed, side);
    }
}
