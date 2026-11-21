using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common.Components;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ U-23 part 5 (docs/UX/DESIGN_Details_Forms.md §4.0a) — the facts about an entity the Summary tab showed and the Unit form now shows: its position, kind, DIS type and the style's affiliation where it differs. The ONE implementation:
/// <c>EntitySummary</c> (the Summary view, which stays for an entity with no <c>EntityInfo</c>) calls these, the Unit form reads their CACHED <see cref="FixedString64"/> twins — rebuilt only when the inputs change, so a steady frame allocates nothing
/// (a moving unit rebuilds its position text only after it moved more than <see cref="MovedMetres"/>).
/// </summary>
public static class EntityFacts
{
    /// <summary>A position text is rebuilt when the unit moved more than this (m); the text itself is rounded to 0.1 m.</summary>
    public const float MovedMetres = 0.05f;

    // ── the texts (culture-invariant — a decimal comma would collide with the separator) ─────────────────────────────────────────

    /// <summary>The DIS type as its seven numbers, colon-separated: <c>Kind:Domain:Country:Category:Subcategory:Specific:Extra</c>.</summary>
    public static string DisText(DISEntityType t)
        => string.Create(CultureInfo.InvariantCulture, $"{t.Kind}:{t.Domain}:{t.Country}:{t.Category}:{t.Subcategory}:{t.Specific}:{t.Extra}");

    /// <summary>The DIS type's levels by name (a tooltip), e.g. <c>Platform › Land › United States › Tank</c>.</summary>
    public static string DisNames(DISEntityType t) => string.Join(" › ", DisNameTable.Default.Path(t));

    /// <summary>A position as the Details show it.</summary>
    public static string PositionText(Vector3 p) => string.Create(CultureInfo.InvariantCulture, $"{p.X:F1}, {p.Y:F1}, {p.Z:F1} m");

    /// <summary>Health as shown: <c>80 / 100</c>.</summary>
    public static string HealthText(float current, float max) => string.Create(CultureInfo.InvariantCulture, $"{current:F0} / {max:F0}");

    // ── the readers (ONE copy: the Summary view and the form) ────────────────────────────────────────────────────────────────

    /// <summary>WHAT KIND of entity: the TKB template's display name, else the DIS category's name; the DIS type is the entity's own, else its template's (as <c>DisEntityTypeTranslator</c> reads it).</summary>
    public static (string? Kind, DISEntityType? Dis) KindAndDis(ISimulationView view, Entity entity)
    {
        string? kind = null; DISEntityType? dis = null;
        if (view is not EntityRepository repo) return (null, null);
        TkbTypeId? tkbType = Has<TkbIdentity>(view, entity) ? view.GetComponentRO<TkbIdentity>(entity).TkbType : null;
        var own = repo.GetDisType(entity);
        if (own.Value != 0) dis = own;
        if (tkbType is { } tt && repo.HasSingletonManaged<ITkbDatabase>() && repo.GetSingletonManaged<ITkbDatabase>() is { } db && db.TryGetByType(tt.Value, out var template))
        {
            var display = template.GetDescriptor<TkbMasterDto>()?.CustomName;
            kind = !string.IsNullOrWhiteSpace(display) ? display : !string.IsNullOrWhiteSpace(template.Name) ? template.Name : null;
            if (dis is null && template.DisType.Value != 0) dis = template.DisType;
        }
        if (kind is null && dis is { } d)
            kind = d.Category != 0 ? DisNameTable.Default.CategoryName(d.Kind, d.Domain, d.Category) : DisNameTable.Default.KindName(d.Kind);
        return (kind, dis);
    }

    /// <summary>The style's affiliation where it DIFFERS from the unit's side (an IG symbol override, Force Hostile), else null.</summary>
    public static ForceId? ShownAs(ISimulationView view, Entity entity, ForceId? side)
    {
        if (!Has<ResolvedStyle>(view, entity)) return null;
        var drawn = view.GetComponentRO<ResolvedStyle>(entity).Affiliation;
        return drawn != side ? drawn : null;
    }

    // ⚠ A host may never register a type (no ResolvedStyle on SimHost) — asking HasComponent of it would throw.
    private static bool Has<T>(ISimulationView view, Entity entity) where T : unmanaged
        => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(entity);

    // ── cached FixedString64 twins for the Unit form (0 B on a steady frame) ────────────────────────────────────────────────

    private sealed class Cache
    {
        public readonly Dictionary<ulong, (Vector3 At, FixedString64 Text)> Position = new();
        public readonly Dictionary<ulong, (ulong Key, FixedString64 Kind, FixedString64 Dis)> KindDis = new();
    }
    private static readonly ConditionalWeakTable<EntityRepository, Cache> Caches = new();
    private static Cache CacheOf(EntityRepository w) => Caches.GetValue(w, static _ => new Cache());

    /// <summary>Whether the entity has a position at all.</summary>
    public static bool HasPosition(EntityRepository w, Entity e) => Has<SimTransform>(w, e);

    /// <summary>The position text, rebuilt only when the unit moved more than <see cref="MovedMetres"/>.</summary>
    public static FixedString64 PositionFixed(EntityRepository w, Entity e)
    {
        if (!Has<SimTransform>(w, e)) return default;
        var p = w.GetComponentRO<SimTransform>(e).Position;
        var cache = CacheOf(w).Position;
        if (cache.TryGetValue(e.PackedValue, out var c) && MathF.Abs(c.At.X - p.X) <= MovedMetres && MathF.Abs(c.At.Y - p.Y) <= MovedMetres && MathF.Abs(c.At.Z - p.Z) <= MovedMetres) return c.Text;
        var text = new FixedString64(PositionText(p));
        cache[e.PackedValue] = (p, text);
        return text;
    }

    private static (FixedString64 Kind, FixedString64 Dis) KindDisFixed(EntityRepository w, Entity e)
    {
        ulong tkb = Has<TkbIdentity>(w, e) ? (ulong)w.GetComponentRO<TkbIdentity>(e).TkbType.Value : 0UL;
        ulong key = w.GetDisType(e).Value * 31UL + tkb + 1UL;   // what the two texts depend on
        var cache = CacheOf(w).KindDis;
        if (cache.TryGetValue(e.PackedValue, out var c) && c.Key == key) return (c.Kind, c.Dis);
        var (kind, dis) = KindAndDis(w, e);
        var made = (key, kind is { } k ? new FixedString64(k) : default, dis is { } d ? new FixedString64(DisText(d)) : default);
        cache[e.PackedValue] = made;
        return (made.Item2, made.Item3);
    }

    public static FixedString64 KindFixed(EntityRepository w, Entity e) => KindDisFixed(w, e).Kind;
    public static FixedString64 DisFixed(EntityRepository w, Entity e) => KindDisFixed(w, e).Dis;

    private static readonly FixedString64[] ForceWords = BuildForceWords();
    private static FixedString64[] BuildForceWords() { var a = new FixedString64[256]; for (int i = 0; i < a.Length; i++) a[i] = new FixedString64(((ForceId)i).ToString()); return a; }

    /// <summary>The <i>Shown as</i> side word when the style differs from the unit's side; empty otherwise.</summary>
    public static FixedString64 ShownAsFixed(EntityRepository w, Entity e)
    {
        if (!w.IsComponentTypeRegistered<EntityInfo>() || !w.HasComponent<EntityInfo>(e)) return default;
        return ShownAs(w, e, w.GetComponentRO<EntityInfo>(e).ForceId) is { } s ? ForceWords[(byte)s] : default;
    }
}
