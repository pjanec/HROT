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
            FormCatalogue.Register(CrewAndCargo);
            FormCatalogue.Register(Shape);
            FormCatalogue.Register(Scenario);
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

    // ⭐ before Unit: static fields initialise in textual order
    private static readonly string[] WeaponPostures = { "At ease", "Ready", "Aimed" };
    private static readonly string[] HeldItems = { "Nothing", "Weapon", "Throwable", "Phone", "Binoculars" };
    private static readonly string[] RoleWords = { "Passenger", "Driver", "Gunner", "Commander", "Weapon station" };
    private static readonly string[] TargetWords = { "Target" };

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
            new FormField { Label = "Name", Section = "Identity", Kind = FieldKind.Text, Attribute = "Name", ReadText = (w, e) => w.GetComponentRO<EntityInfo>(e).Name },
            new FormField
            {
                Label = "Side", Kind = FieldKind.Choice, Attribute = "Affiliation", Choices = Sides,
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<EntityInfo>(e).ForceId),
                ChoiceValue = i => AttributeValue.FromString(i >= 0 && i < Affiliations.Length ? Affiliations[i] : null),
            },
            new FormField
            {
                Label = "Posture", Section = "State", Kind = FieldKind.Choice, Attribute = "Stance", Choices = Stances,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>() && w.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e),
                ReadNumber = (w, e) => (int)w.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e).TargetStance,
                ChoiceValue = i => AttributeValue.FromInt(i),
            },
            // ⭐ U-24 / S5 (DESIGN_Initial_State §3) — the weapon's start state: posture and what is in hand (B-5 S2 attributes 70 / 73; the brain's owner applies them)
            new FormField
            {
                Label = "Weapon", Kind = FieldKind.Choice, Attribute = "Weapon.Posture", Choices = WeaponPostures,
                Has = HasWeaponPosture,
                ReadNumber = (w, e) => (int)w.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).Kind,
            },
            new FormField
            {
                Label = "In hand", Kind = FieldKind.Choice, Attribute = "Weapon.HeldItem", Choices = HeldItems,
                Has = HasWeaponPosture,
                ReadNumber = (w, e) => (int)w.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).HeldItem,
            },
            new FormField
            {
                Label = "Strength", Attribute = "Health.Current", Min = 0,
                Has = (w, e) => w.IsComponentTypeRegistered<Health>() && w.HasComponent<Health>(e),
                ReadNumber = (w, e) => w.GetComponentRO<Health>(e).Current,
                MaxOf = (w, e) => w.GetComponentRO<Health>(e).Max,   // reads "80 / 100"; the write stays Health.Current
            },
            new FormField
            {
                // ⭐ U-23 part 5 — the make-playable toggle (was the Playable tab's): an owner-side add / remove, the ONE op (PlayableOps)
                Label = "Playable", Kind = FieldKind.Toggle,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.Common.Playable>(),
                ReadNumber = (w, e) => w.HasComponent<Hrot.Common.Playable>(e) ? 1 : 0,
                Apply = (w, e, v, _) => PlayableOps.SetPlayable(w, e, v != 0) ? null : "not available",
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
            // ⭐ U-24 / S6 (DESIGN_Initial_State §4.2) — the Character section: the EFFECTIVE value shown, a blue reset arrow when an authored value differs from the type's (a NEGATIVE write clears the authored bit)
            Character("Skill", "Proficiency", "Character",
                Has: (w, e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CombatProficiency>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.CombatProficiency>(e),
                read: (w, e) => Fdp.Toolkit.Behavior.Components.ProficiencyOf.Value(w, e),
                type: (w, e) =>
                {
                    var p = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CombatProficiency>(e);
                    if ((p.Set & Fdp.Toolkit.Behavior.Components.CombatProficiency.AuthoredSet) == 0) return null;
                    return (p.Set & Fdp.Toolkit.Behavior.Components.CombatProficiency.UnitSet) != 0 ? p.Unit
                         : (p.Set & Fdp.Toolkit.Behavior.Components.CombatProficiency.TypeDefaultSet) != 0 ? p.TypeDefault : Fdp.Toolkit.Behavior.Components.ProficiencyOf.Regular;
                }),
            Trait("Bravery", "Temperament.RiskAcceptance", Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet, Fdp.Toolkit.Behavior.Components.TemperamentOf.RiskAcceptance,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultRiskAcceptance, t => t.TypeRiskAcceptance),
            Trait("Obedience", "Temperament.Obedience", Fdp.Toolkit.Behavior.Components.CombatTemperament.ObedienceSet, Fdp.Toolkit.Behavior.Components.TemperamentOf.Obedience,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultObedience, t => t.TypeObedience),
            Trait("Nerve", "Temperament.Nerve", Fdp.Toolkit.Behavior.Components.CombatTemperament.NerveSet, Fdp.Toolkit.Behavior.Components.TemperamentOf.Nerve,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultNerve, t => t.TypeNerve),
            Trait("Resolve", "Temperament.Resolve", Fdp.Toolkit.Behavior.Components.CombatTemperament.ResolveSet, Fdp.Toolkit.Behavior.Components.TemperamentOf.Resolve,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultResolve, t => t.TypeResolve),
            // ⭐ U-23 part 5 — what the Summary tab showed (read-only, expert): position (rebuilt only after a move > 5 cm), kind, the DIS type (the colon form), the style's affiliation where it differs
            new FormField
            {
                Label = "Position", Kind = FieldKind.Text, Expert = true,
                Has = EntityFacts.HasPosition,
                ReadText = EntityFacts.PositionFixed,
            },
            new FormField
            {
                Label = "Kind", Kind = FieldKind.Text, Expert = true,
                Has = (w, e) => !EntityFacts.KindFixed(w, e).IsEmpty,
                ReadText = EntityFacts.KindFixed,
            },
            new FormField
            {
                Label = "DIS type", Kind = FieldKind.Text, Expert = true,
                Has = (w, e) => !EntityFacts.DisFixed(w, e).IsEmpty,
                ReadText = EntityFacts.DisFixed,
            },
            new FormField
            {
                Label = "Shown as", Kind = FieldKind.Text, Expert = true,
                Has = (w, e) => !EntityFacts.ShownAsFixed(w, e).IsEmpty,
                ReadText = EntityFacts.ShownAsFixed,
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
        // ⭐ U-24 / S5 — Aboard: the vehicle and seat he rides in, Take off; Board… picks a vehicle — the ONE embark path (EmbarkOrders), as Crew & cargo
        Lists = new[]
        {
            new FormList
            {
                Label = "Aboard", RowLabels = RoleWords,
                Fill = FillAboard,
                Footer = (w, e) => IsAboard(w, e) ? null : "not aboard",
                RemoveLabel = "Take off",
                Remove = (w, it) => Hrot.Common.Orders.EmbarkOrders.Disembark(w, it.Item),
                AddLabel = "Board...",
                Add = (w, unit, vehicle) => IsAboard(w, unit) ? "already aboard — take him off first" : PutAboard(w, vehicle, unit),
            },
            // ⭐ U-24 / S5 — aimed at: the unit his weapon points at (Weapon.AimTarget, 72 — the target's network id, 0 clears; resolved on the applying node)
            new FormList
            {
                Label = "Aimed at", RowLabels = TargetWords,
                Fill = FillAimTarget,
                RemoveLabel = "Clear",
                Remove = (w, it) => WriteAimTarget(w, it.Item, 0, "Clear the aim"),
                AddLabel = "Aim at...",
                Add = AimAt,
            },
            // ⭐ U-24 / S5 — knows about: what his TargetMemory holds (read only where his brain runs — the component is on the brain's node alone); Add… seeds a target through the ONE order
            //   (KnowledgeOrders.SeedTarget → Edits.Submit(Interact), applied on the brain's node). ⛔ no Remove: nothing in the engine forgets a target on request
            new FormList
            {
                Label = "Knows about", RowLabels = UnitWords,
                Fill = FillKnows,
                Footer = KnowsFooter,
                AddLabel = "Add...",
                Add = SeedKnown,
            },
            // ⭐ U-24 / S5 — carried by: a soldier carries him (Carried · Dragged · Stretcher + the second man of a stretcher). Put down → the ONE disembark; Carried by… → the ONE embark with a carry mode.
            //   ⏳ not yet: changing HOW he is carried (Embarkation treats an embark of a man already aboard as idempotent — the mode is set at boarding only) and naming the second man
            new FormList
            {
                Label = "Carried by", RowLabels = CarryWords,
                Fill = FillCarried,
                Footer = (w, e) => IsCarried(w, e) ? null : "not carried",
                RemoveLabel = "Put down",
                Remove = (w, it) => Hrot.Common.Orders.EmbarkOrders.Disembark(w, it.Item),
                AddLabel = "Carried by...",
                Add = CarriedBy,
            },
        },
    };

    private static readonly string[] UnitWords = { "Unit" };
    private static readonly string[] CarryWords = { "Carried", "Carried", "Dragged", "Stretcher", "Second man" };   // by CarryMode (Seated reads as Carried), then the assistant

    private static bool IsPersonCarrier(EntityRepository w, Entity v)
        => w.IsAlive(v) && w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PersonCarrier>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>(v);

    private static bool IsCarried(EntityRepository w, Entity e)
        => IsAboard(w, e) && IsPersonCarrier(w, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e).VehicleEntity);

    /// <summary>One row when a PERSON carries him: how (the carry mode), the carrier's name; a second row for his stretcher's second man (no action of its own). The row's item is HIM (Put down disembarks him).</summary>
    private static void FillCarried(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        if (!IsCarried(w, e)) return;
        ref readonly var tag = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e);
        var carrier = tag.VehicleEntity;
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PassengerBuffer>() || !w.HasComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(carrier)) return;
        ref readonly var buf = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(carrier);
        int seat = tag.Seat;
        int mode = seat < Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity ? (int)buf.ModeOf(seat) : 0;
        into.Add(new FormListItem(CarryWords[mode], NameOf(w, carrier, "(a soldier)"), e));
        if (!buf.Assistant.IsNull && w.IsAlive(buf.Assistant)) into.Add(new FormListItem(CarryWords[4], NameOf(w, buf.Assistant, "(a soldier)"), buf.Assistant, Passive: true));
    }

    private static FixedString64 NameOf(EntityRepository w, Entity e, string fallback)
        => w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(e) ? w.GetComponentRO<EntityInfo>(e).Name : new FixedString64(fallback);

    private static string? CarriedBy(EntityRepository w, Entity unit, Entity carrier)
    {
        if (unit == carrier) return "he cannot carry himself";
        if (!IsPersonCarrier(w, carrier)) return "only a soldier carries a man";
        if (IsAboard(w, unit)) return "already aboard — put him down first";
        return Fdp.Toolkit.Behavior.Embarkation.CanEmbark(w, unit, carrier, Fdp.Toolkit.Behavior.Components.SeatRole.Passenger, Fdp.Toolkit.Behavior.Components.CarryMode.Carried) switch
        {
            Fdp.Toolkit.Behavior.EmbarkResult.Applied => Hrot.Common.Orders.EmbarkOrders.Embark(w, unit, carrier, Fdp.Toolkit.Behavior.Components.CarryMode.Carried) ? null : "not sent",
            Fdp.Toolkit.Behavior.EmbarkResult.Full => "his arms are full",
            _ => "he can only carry a man who is down, while he is himself in the fight",
        };
    }
    private const int KnowsCap = 8;
    private static readonly string[] MoreWords = BuildMoreWords();
    private static string[] BuildMoreWords() { var w = new string[Fdp.Toolkit.Perception.PerceptionConstants.MaxTrackedTargets + 1]; for (int i = 0; i < w.Length; i++) w[i] = "+" + i + " more"; return w; }
    private static readonly FixedString64 UnknownName = new("(unknown)");

    private static bool HasMemory(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>() && w.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(e);

    /// <summary>One row per remembered, IDENTIFIED target (the first <see cref="KnowsCap"/>): the target's name, or <i>(unknown)</i>; the row's item is the TARGET. No LINQ, no per-frame string.</summary>
    private static void FillKnows(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        if (!HasMemory(w, e)) return;
        ref readonly var m = ref w.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(e);
        int shown = 0;
        for (int i = 0; i < m.Count && shown < KnowsCap; i++)
        {
            if (Fdp.Toolkit.Perception.Components.TargetMemory.IsAnonymous(in m, i)) continue;   // a heard, unidentified contact has no entity
            var t = new Entity((ulong)KnowsId(in m, i));
            var name = w.IsAlive(t) && w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(t) ? w.GetComponentRO<EntityInfo>(t).Name : UnknownName;
            into.Add(new FormListItem(UnitWords[0], name, t));
            shown++;
        }
    }

    private static unsafe long KnowsId(in Fdp.Toolkit.Perception.Components.TargetMemory m, int i) => m.EntityIds[i];

    /// <summary>The footer: <i>+N more</i> past the cap, or — where the component is absent (a station that does not run his brain) — where the answer lives.</summary>
    private static string? KnowsFooter(EntityRepository w, Entity e)
    {
        if (!HasMemory(w, e)) return "(known only where his brain runs)";
        ref readonly var m = ref w.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(e);
        int identified = 0;
        for (int i = 0; i < m.Count; i++) if (!Fdp.Toolkit.Perception.Components.TargetMemory.IsAnonymous(in m, i)) identified++;
        return identified > KnowsCap ? MoreWords[Math.Min(identified - KnowsCap, MoreWords.Length - 1)] : null;
    }

    private static string? SeedKnown(EntityRepository w, Entity unit, Entity target)
        => unit == target ? "he cannot know of himself" : Hrot.Common.Orders.KnowledgeOrders.SeedTarget(w, unit, target) ? null : "not sent";

    /// <summary>One row when his weapon has a live target: the target's name; the row's item is HIM (Clear writes his attribute).</summary>
    private static void FillAimTarget(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        if (!HasWeaponPosture(w, e)) return;
        var t = w.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).Target;
        if (t.IsNull || !w.IsAlive(t)) return;
        var name = w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(t) ? w.GetComponentRO<EntityInfo>(t).Name : new FixedString64("(a unit)");
        into.Add(new FormListItem(TargetWords[0], name, e));
    }

    private static string? AimAt(EntityRepository w, Entity unit, Entity target)
    {
        if (!HasWeaponPosture(w, unit)) return "he has no weapon posture";
        if (target == unit) return "he cannot aim at himself";
        int net = Fdp.Toolkit.Replication.EntityRef.Of(w, target).NetworkId;
        if (net == 0) return "that has no network identity — pick a unit";
        WriteAimTarget(w, unit, net, "Aim at");
        return null;
    }

    private static void WriteAimTarget(EntityRepository w, Entity unit, int networkId, string label)
        => Fdp.Toolkit.Replication.Edits.Edits.Submit(w, Fdp.Toolkit.Replication.Edits.EditOp.Write(unit,
               new EntityAttributeChange { AttributeId = AttributeIds.WeaponAimTarget, Value = AttributeValue.FromInt(networkId) }, label));

    private static bool HasWeaponPosture(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>() && w.HasComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>(e);

    private static bool IsAboard(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e);

    /// <summary>One row when he is aboard: the seat's role, the vehicle's name; the row's item is HIM (Take off disembarks him).</summary>
    private static void FillAboard(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        if (!IsAboard(w, e)) return;
        ref readonly var tag = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e);
        var v = tag.VehicleEntity;
        if (v.IsNull || !w.IsAlive(v) || IsPersonCarrier(w, v)) return;   // ⭐ a person carrying him is the Carried by list's row, not a vehicle seat
        var name = w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(v) ? w.GetComponentRO<EntityInfo>(v).Name : new FixedString64("(a vehicle)");
        int seat = tag.Seat;
        into.Add(new FormListItem(seat < Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity ? RoleOf(w, v, seat) : RoleWords[0], name, e));
    }

    private static bool HasSpawn(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Hrot.Common.SpawnArea>() && w.HasComponent<Hrot.Common.SpawnArea>(e);
    private static bool HasWeather(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Weather.WeatherConditions>() && w.HasComponent<Fdp.Toolkit.Weather.WeatherConditions>(e);
    private static ref readonly Fdp.Toolkit.Weather.WeatherConditions Weather(EntityRepository w, Entity e) => ref w.GetComponentRO<Fdp.Toolkit.Weather.WeatherConditions>(e);

    private static readonly string[] Precipitations = { "None", "Drizzle", "Rain", "Snow", "Sleet", "Hail" };   // Weather.PrecipitationKind

    private static FormField WeatherNumber(string label, string attribute, string unit, Func<EntityRepository, Entity, double> read, double min = double.MinValue, double max = double.MaxValue, bool expert = false)
        => new() { Label = label, Attribute = attribute, Unit = unit, Min = min, Max = max, Expert = expert, Has = HasWeather, ReadNumber = read };

    /// <summary>
    /// ⭐ D4 — the <b>Crew &amp; cargo</b> form (§3; §4.0a part 4): offered for a carrier (<c>PassengerBuffer</c>). One row per OCCUPIED seat — its role
    /// (Driver · Gunner · Commander · Weapon station · Passenger) and who sits there, with <i>Take off</i>; then the free seats as a count; <i>Put aboard…</i>
    /// picks a unit. ⭐ Both actions are <see cref="Hrot.Common.Orders.EmbarkOrders"/> — the ONE embark path (the map menu, the ORBAT drag): the vehicle's owner
    /// answers. ⛔ No write path of its own.
    /// </summary>
    public static readonly FormDecl CrewAndCargo = new()
    {
        Id = "form.crew", Title = "Crew & cargo", Order = 20,
        Requires = new[] { typeof(Fdp.Toolkit.Behavior.Components.PassengerBuffer) },
        Fields = Array.Empty<FormField>(),
        Lists = new[]
        {
            new FormList
            {
                Label = "Seats", RowLabels = RoleWords,
                Fill = FillSeats,
                Footer = FreeSeatsText,
                RemoveLabel = "Take off",
                Remove = (w, it) => Hrot.Common.Orders.EmbarkOrders.Disembark(w, it.Item),
                AddLabel = "Put aboard...",
                Add = PutAboard,
            },
        },
    };

    private static readonly string[] FreeWords = BuildFreeWords();
    private static string[] BuildFreeWords()
    {
        var w = new string[Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity + 1];
        w[0] = "no seat free";
        for (int i = 1; i < w.Length; i++) w[i] = i == 1 ? "1 seat free" : i + " seats free";
        return w;
    }

    private static int SeatCountOf(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CarrierSeats>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e)
            ? Math.Min((int)w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e).SeatCount, Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity)
            : Fdp.Toolkit.Behavior.Components.PassengerBuffer.UndeclaredSeats;

    private static string RoleOf(EntityRepository w, Entity e, int seat)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CarrierSeats>() || !w.HasComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e)) return RoleWords[0];
        int r = (int)w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e).Roles[seat];
        return r >= 0 && r < RoleWords.Length ? RoleWords[r] : RoleWords[0];
    }

    private static void FillSeats(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        ref readonly var buf = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(e);
        int seats = SeatCountOf(w, e);
        bool named = w.IsComponentTypeRegistered<EntityInfo>();
        for (int i = 0; i < seats; i++)
        {
            var p = buf.Passengers[i];
            if (p.IsNull || !w.IsAlive(p)) continue;
            var who = named && w.HasComponent<EntityInfo>(p) ? w.GetComponentRO<EntityInfo>(p).Name : new FixedString64("(unnamed)");
            into.Add(new FormListItem(RoleOf(w, e, i), who, p));
        }
    }

    private static string? FreeSeatsText(EntityRepository w, Entity e)
    {
        ref readonly var buf = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(e);
        int seats = SeatCountOf(w, e), free = 0;
        for (int i = 0; i < seats; i++) if (buf.Passengers[i].IsNull) free++;
        return FreeWords[Math.Min(free, FreeWords.Length - 1)];
    }

    private static string? PutAboard(EntityRepository w, Entity vehicle, Entity unit)
    {
        if (unit == vehicle) return "a vehicle cannot board itself";
        if (!Hrot.Common.Orders.EmbarkOrders.CanOffer(w, unit)) return "this cannot be put aboard (already aboard, a vehicle or a weapon)";
        return Hrot.Common.Orders.EmbarkOrders.Embark(w, unit, vehicle) ? null : "not sent";
    }

    /// <summary>
    /// ⭐ D5 — the <b>Shape</b> form (§3): offered for a spawn area or a weather region. Spawn area: side, facing. Weather region: the authored scalars of
    /// <c>WeatherConditions</c> (the global weather's form is the Scenario form's). ⏳ Not yet: kind / name / point count / length (read-only), a route's legs
    /// (CE-4284, a list — not a scalar attribute), the wind levels and cloud layers (lists, not attributes yet).
    /// </summary>
    public static readonly FormDecl Shape = new()
    {
        Id = "form.shape", Title = "Shape", Order = 30,
        Offers = (w, e) => HasSpawn(w, e) || HasWeather(w, e),
        Fields = Join(new[]
        {
            new FormField
            {
                Label = "Spawn side", Kind = FieldKind.Choice, Attribute = "SpawnArea.Side", Choices = Sides, Has = HasSpawn,
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<Hrot.Common.SpawnArea>(e).Side),
                ChoiceValue = i => AttributeValue.FromInt(i >= 0 && i < SideForces.Length ? (int)SideForces[i] : 0),
            },
            new FormField { Label = "Facing", Attribute = "SpawnArea.FacingDeg", Unit = "°", Min = 0, Max = 360, Has = HasSpawn, ReadNumber = (w, e) => w.GetComponentRO<Hrot.Common.SpawnArea>(e).FacingDeg },
            // the region's weather: the ONE weather row set (WeatherRows), shared with the Scenario form
        }, WeatherRows()),
    };

    /// <summary>The authored weather scalars as form rows — ONE set, used by the Shape form (a weather region) and the Scenario form (the global weather).</summary>
    private static FormField[] WeatherRows() => new[]
    {
            WeatherNumber("Temperature", "Weather.GroundTemperatureC", "°C", (w, e) => Weather(w, e).GroundTemperatureC, -80, 70),
            WeatherNumber("Pressure", "Weather.SeaLevelPressureHpa", "hPa", (w, e) => Weather(w, e).SeaLevelPressureHpa, 800, 1100),
            WeatherNumber("Humidity", "Weather.RelativeHumidity", "(0-1)", (w, e) => Weather(w, e).RelativeHumidity, 0, 1),   // stored 0…1 (W-7)
            WeatherNumber("Visibility", "Weather.VisibilityM", "m", (w, e) => Weather(w, e).VisibilityM, 0),
            new FormField
            {
                Label = "Precipitation", Kind = FieldKind.Choice, Attribute = "Weather.Precipitation", Choices = Precipitations, Has = HasWeather,
                ReadNumber = (w, e) => Weather(w, e).Precipitation, ChoiceValue = i => AttributeValue.FromInt(i),
            },
            WeatherNumber("Rate", "Weather.PrecipitationMmPerHour", "mm/h", (w, e) => Weather(w, e).PrecipitationMmPerHour, 0),
            WeatherNumber("Fog top", "Weather.FogTopM", "m", (w, e) => Weather(w, e).FogTopM, 0),
            WeatherNumber("Fog visibility", "Weather.FogVisibilityM", "m", (w, e) => Weather(w, e).FogVisibilityM, 0),
            WeatherNumber("Edge fade", "Weather.FeatherM", "m", (w, e) => Weather(w, e).FeatherM, 0),
            WeatherNumber("Lapse rate", "Weather.LapseRateKPerKm", "K/km", (w, e) => Weather(w, e).LapseRateKPerKm, expert: true),
    };

    private static FormField[] Join(FormField[] a, FormField[] b)
    {
        var r = new FormField[a.Length + b.Length];
        a.CopyTo(r, 0); b.CopyTo(r, a.Length);
        return r;
    }

    // ── the Scenario form: nothing selected — the World Environment (its clock and the global weather) ───────────────────────

    private static Entity WorldEnvironment(EntityRepository w)
    {
        Fdp.Toolkit.Weather.WeatherField.GlobalOf(w, out var env);
        return env;
    }

    private static long _startTicks = long.MinValue;
    private static FixedString64 _startText;

    /// <summary>The start as people read it (<c>2026-06-01 06:00 UTC</c>), cached by its ticks — a frame reading it allocates nothing.</summary>
    private static FixedString64 StartText(long ticks)
    {
        if (ticks != _startTicks)
        {
            _startTicks = ticks;
            _startText = new FixedString64(ticks <= 0 ? "(not set)" : new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
        }
        return _startText;
    }

    private static bool HasClock(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Weather.ScenarioClock>() && w.HasComponent<Fdp.Toolkit.Weather.ScenarioClock>(e);

    /// <summary>
    /// ⭐ D5 — the <b>Scenario</b> form (§3), drawn when nothing is selected: Time (the start, read-only here; the display offset) and the GLOBAL weather (the
    /// same rows as a region's). ⏳ Not yet: name / description (the file header — a document step, not an attribute), *Capture picture*, editing the start
    /// (a date field kind), Terrain (read-only), Parameters.
    /// </summary>
    public static readonly FormDecl Scenario = new()
    {
        Id = "form.scenario", Title = "Scenario", Order = 5,
        WorldTarget = WorldEnvironment,
        Fields = Join(new[]
        {
            new FormField { Label = "Start", Kind = FieldKind.Text, Has = HasClock, ReadText = (w, e) => StartText(w.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(e).StartUtcTicks) },
            new FormField { Label = "Clock offset", Attribute = "ScenarioUtcOffsetMinutes", Unit = "min", Min = -720, Max = 840, Has = HasClock,
                            ReadNumber = (w, e) => w.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(e).DisplayUtcOffsetMinutes },
        }, WeatherRows()),
    };

    private static FormField Character(string label, string attribute, string? section, Func<EntityRepository, Entity, bool> Has, Func<EntityRepository, Entity, double> read, Func<EntityRepository, Entity, double?> type)
        => new FormField { Label = label, Section = section, Attribute = attribute, Min = 0, Max = 1, Slider = true, Has = Has, ReadNumber = read, TypeValue = type };

    private static FormField Trait(string label, string attribute, byte bit, Func<Fdp.ModuleHost.Abstractions.ISimulationView, Entity, float> effective, float dflt, Func<Fdp.Toolkit.Behavior.Components.CombatTemperament, float> typeOf)
        => Character(label, attribute, null,
            (w, e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CombatTemperament>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.CombatTemperament>(e),
            (w, e) => effective(w, e),
            (w, e) =>
            {
                var t = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CombatTemperament>(e);
                if ((t.Set & bit) == 0) return null;                          // nothing authored over the type ⇒ no mark
                return (t.TypeSet & bit) != 0 ? typeOf(t) : dflt;             // the type's value (with its spread), else the default
            });
}
