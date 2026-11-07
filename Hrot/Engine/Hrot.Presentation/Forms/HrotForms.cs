using System;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ U-23 (docs/UX/DESIGN_Details_Forms.md §3, D3) — the engine's DECLARED forms, registered once into the one <see cref="FormCatalogue"/>; the Details bundle
/// draws a tab per form an entity is offered (every host, read-only on a recording — §2.4). ⛔ No form writes a component: each field names its attribute.
/// </summary>
public static class HrotForms
{
    private static bool _registered;
    private static readonly object Gate = new();

    /// <summary>Registers the forms (idempotent; the bundle calls it before it lists them).</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            _registered = true;
            FormCatalogue.Register(Unit);
        }
    }

    // ── the side: Blue / Red / Neutral (ForceId 1 / 2 / 0) ──────────────────────────────────────────────────────────────────

    private static readonly string[] Sides = { "Blue", "Red", "Neutral" };
    private static readonly ForceId[] SideForces = { ForceId.Friend, ForceId.Hostile, ForceId.Neutral };
    private static readonly string[] Affiliations = { "FORCE_FRIENDLY", "FORCE_OPPOSING", "FORCE_NEUTRAL" };   // what the Affiliation attribute takes (AttributeCompilerFactory)

    private static double SideIndex(ForceId f) => f switch { ForceId.Friend => 0, ForceId.Hostile => 1, ForceId.Neutral => 2, _ => -1 };

    /// <summary>A 32-byte fixed string as a 64-byte one — both are null-terminated UTF-8 buffers, so a copy of the 32 bytes (0 B; a field reads it every frame).</summary>
    private static FixedString64 Widen(in FixedString32 s)
    {
        FixedString64 r = default;
        System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(ref System.Runtime.CompilerServices.Unsafe.As<FixedString64, byte>(ref r),
            ref System.Runtime.CompilerServices.Unsafe.As<FixedString32, byte>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in s)), 32);
        return r;
    }

    private static readonly string[] Stances = { "Standing", "Crouched", "Prone" };

    /// <summary>
    /// ⭐ D3 — the <b>Unit</b> form (§3): name · side · posture · strength · playable side / slot; ⚙ the entity's network id and TKB template.
    /// ⏳ Not yet (each needs a field kind or an attribute this commit does not add): the type's name (a derived, cached text), rounds per mount (<c>Ammo.*</c>
    /// with a sub-index), the owner node, the loaded round.
    /// </summary>
    public static readonly FormDecl Unit = new()
    {
        Id = "form.unit", Title = "Unit", Order = 10, Requires = new[] { typeof(EntityInfo) },
        Fields = new[]
        {
            new FormField { Label = "Name", Kind = FieldKind.Text, Attribute = "Name", ReadText = (w, e) => w.GetComponentRO<EntityInfo>(e).Name },
            new FormField
            {
                Label = "Side", Kind = FieldKind.Choice, Attribute = "Affiliation", Choices = Sides,
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<EntityInfo>(e).ForceId),
                ChoiceValue = i => AttributeValue.FromString(i >= 0 && i < Affiliations.Length ? Affiliations[i] : null),
            },
            new FormField
            {
                Label = "Posture", Kind = FieldKind.Choice, Attribute = "Stance", Choices = Stances,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>() && w.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e),
                ReadNumber = (w, e) => (int)w.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e).TargetStance,
                ChoiceValue = i => AttributeValue.FromInt(i),
            },
            new FormField
            {
                Label = "Strength", Attribute = "Health.Current", Min = 0,
                Has = (w, e) => w.IsComponentTypeRegistered<Health>() && w.HasComponent<Health>(e),
                ReadNumber = (w, e) => w.GetComponentRO<Health>(e).Current,
            },
            new FormField
            {
                Label = "Playable for", Kind = FieldKind.Choice, Attribute = "Playable.Side", Choices = Sides,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.Common.Playable>() && w.HasComponent<Hrot.Common.Playable>(e),
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<Hrot.Common.Playable>(e).Side),
                ChoiceValue = i => AttributeValue.FromInt(i >= 0 && i < SideForces.Length ? (int)SideForces[i] : 0),
            },
            new FormField
            {
                Label = "Slot", Kind = FieldKind.Text, Attribute = "Playable.SlotName",
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.Common.Playable>() && w.HasComponent<Hrot.Common.Playable>(e),
                ReadText = (w, e) => Widen(w.GetComponentRO<Hrot.Common.Playable>(e).SlotName),
            },
            new FormField
            {
                Label = "Entity id", Expert = true,
                Has = (w, e) => w.IsComponentTypeRegistered<NetworkIdentity>() && w.HasComponent<NetworkIdentity>(e),
                ReadNumber = (w, e) => w.GetComponentRO<NetworkIdentity>(e).Value,
            },
            new FormField
            {
                Label = "TKB template", Expert = true,
                Has = (w, e) => w.IsComponentTypeRegistered<TkbIdentity>() && w.HasComponent<TkbIdentity>(e),
                ReadNumber = (w, e) => (long)w.GetComponentRO<TkbIdentity>(e).TkbType,
            },
        },
    };
}
