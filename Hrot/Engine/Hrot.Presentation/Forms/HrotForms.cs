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
    private static readonly string[] Affiliations = { "FORCE_FRIENDLY", "FORCE_OPPOSING", "FORCE_NEUTRAL" };   // what the Affiliation attribute takes (AttributeCompilerFactory)

    /// <summary>⭐ R-2 — what the three side fields (Affiliation, Playable.Side, SpawnArea.Side) write for a choice: the force TOKEN. The default read (<see cref="FormField.TryReadNumber"/>) maps the reader's token
    /// back to the choice index through this very function — ONE place, no per-field mapping. (Was an int ForceId on two of them; the setters take the token everywhere.)</summary>
    private static AttributeValue SideChoiceValue(int i) => AttributeValue.FromString(i >= 0 && i < Affiliations.Length ? Affiliations[i] : null);

    private static readonly string[] Stances = { "Standing", "Crouched", "Prone" };

    // ⭐ before Unit: static fields initialise in textual order
    private static readonly string[] WeaponPostures = { "At ease", "Ready", "Aimed" };
    private static readonly string[] HeldItems = { "Nothing", "Weapon", "Throwable", "Phone", "Binoculars" };
    private static readonly string[] CarriedAsWords = { "Carried", "Dragged", "Stretcher" };   // CarryMode.Carried / Dragged / Stretcher, minus the Seated offset
    private static readonly string[] SeedWords = { "Pending", "Applied", "Refused", "Sent" };   // by outcome; "Sent" = the journal no longer holds the request
    private static readonly string[] RoleWords = { "Passenger", "Driver", "Gunner", "Commander", "Weapon station" };

    // ── ⭐ U-24 / S5 (DESIGN_Initial_State §4.2) — Damage: one number row per PART of his body (Damage.Part.*, attribute 85, the part id as the sub-index) and the mobility kill as a switch (86) ─────────────

    private static bool HasBody(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Physics.BodyParts.PartDamage>() && w.IsComponentTypeRegistered<Health>() && w.HasComponent<Health>(e)
           && Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(w, e) != null;   // a unit that can be damaged: PartDamage is added by the owner on the first write

    private static bool HasPart(EntityRepository w, Entity e, int part)
    {
        if (!HasBody(w, e)) return false;
        var zones = Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(w, e)!.Zones;
        for (int k = 0; k < zones.Count; k++) if (zones[k].Has((byte)part)) return true;
        return false;
    }

    private static string PartName(EntityRepository w, Entity e, int part)
    {
        var plan = Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(w, e)?.Plan ?? Fdp.Toolkit.Physics.BodyParts.BodyPlan.None;
        byte p = (byte)part;
        return plan switch
        {
            Fdp.Toolkit.Physics.BodyParts.BodyPlan.Person => Fdp.Toolkit.Physics.BodyParts.PersonPart.NameOf(p),
            Fdp.Toolkit.Physics.BodyParts.BodyPlan.TrackedVehicle or Fdp.Toolkit.Physics.BodyParts.BodyPlan.WheeledVehicle => Fdp.Toolkit.Physics.BodyParts.VehiclePart.NameOf(p),
            Fdp.Toolkit.Physics.BodyParts.BodyPlan.Rotorcraft or Fdp.Toolkit.Physics.BodyParts.BodyPlan.FixedWing => Fdp.Toolkit.Physics.BodyParts.AircraftPart.NameOf(p),
            Fdp.Toolkit.Physics.BodyParts.BodyPlan.WeaponSystem => Fdp.Toolkit.Physics.BodyParts.WeaponPart.NameOf(p),
            _ => PartWords[part],
        };
    }
    private static readonly string[] PartWords = BuildPartWords();
    private static string[] BuildPartWords() { var a = new string[Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts]; for (int i = 0; i < a.Length; i++) a[i] = "Damage " + i; return a; }

    private static FormField DamagePart(int part) => new()
    {
        Label = PartWords[part], Expert = true, Attribute = "Damage.Part.*", SubIndex = (short)part, Min = 0, Max = 255,
        Has = (w, e) => HasPart(w, e, part),
        LabelFor = (w, e) => PartName(w, e, part),
    };

    private static bool HasMobilityZone(EntityRepository w, Entity e)
    {
        if (!HasBody(w, e)) return false;
        var zones = Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(w, e)!.Zones;
        for (int k = 0; k < zones.Count; k++) if (zones[k].Effect == WoundEffect.Mobility) return true;
        return false;
    }

    private static readonly FormField[] DamageFields = BuildDamageFields();
    private static FormField[] BuildDamageFields()
    {
        var f = new FormField[Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts + 1];
        for (int i = 0; i < Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts; i++) f[i] = DamagePart(i);
        f[^1] = new FormField { Label = "Mobility kill", Kind = FieldKind.Toggle, Expert = true, Attribute = "Damage.MobilityKill", Has = HasMobilityZone };
        return f;
    }


    /// <summary>
    /// ⭐ D3 — the <b>Unit</b> form (§3): name · side · posture · strength · playable side / slot; ⚙ the entity's network id and TKB template.
    /// ⏳ Not yet (each needs a field kind or an attribute this commit does not add): the type's name (a derived, cached text), rounds per mount (<c>Ammo.*</c>
    /// with a sub-index), the owner node, the loaded round.
    /// </summary>
    public static readonly FormDecl Unit = new()
    {
        Id = "form.unit", Title = "Unit", Order = 10, Requires = new[] { typeof(EntityInfo) },
        Fields = Concat(new[]
        {
            new FormField { Label = "Name", Section = "Identity", Kind = FieldKind.Text, Attribute = "Name" },
            new FormField
            {
                Label = "Side", Kind = FieldKind.Choice, Attribute = "Affiliation", Choices = Sides,
                ChoiceValue = SideChoiceValue,
            },
            new FormField
            {
                Label = "Posture", Section = "State", Kind = FieldKind.Choice, Attribute = "Stance", Choices = Stances,
                ChoiceValue = i => AttributeValue.FromInt(i),
            },
            // ⭐ U-24 / S5 (DESIGN_Initial_State §3) — the weapon's start state: posture and what is in hand (B-5 S2 attributes 70 / 73; the brain's owner applies them)
            new FormField
            {
                Label = "Weapon", Kind = FieldKind.Choice, Attribute = "Weapon.Posture", Choices = WeaponPostures,
            },
            // ⭐ U-24 / S5 (details-forms-visual.svg) — Weapon [Aimed ▾] at [pick] on ONE row: the unit his weapon points at (Weapon.AimTarget, 72 — the target's network id, 0 clears; resolved on the applying node)
            new FormField
            {
                Label = "Aimed at", Kind = FieldKind.Pick, Inline = true, InlineCaption = "at",
                Has = HasWeaponPosture,
                ReadText = AimTargetName,   // derived: the picked unit's NAME — the attribute (Weapon.AimTarget) reads a network id
                PickApply = AimAt,
                Apply = (w, e, v, _) => { WriteAimTarget(w, e, 0, "Clear the aim"); return null; },
            },
            new FormField
            {
                Label = "In hand", Kind = FieldKind.Choice, Attribute = "Weapon.HeldItem", Choices = HeldItems,
            },
            new FormField
            {
                Label = "Strength", Attribute = "Health.Current", Min = 0,
                MaxOf = (w, e) => w.GetComponentRO<Health>(e).Max,   // derived: the max — no attribute reads it   // reads "80 / 100"; the write stays Health.Current
            },
            new FormField
            {
                // ⭐ U-23 part 5 — the make-playable toggle (was the Playable tab's): an owner-side add / remove, the ONE op (PlayableOps)
                Label = "Playable", Kind = FieldKind.Toggle,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.Common.Playable>(),
                ReadNumber = (w, e) => w.HasComponent<Hrot.Common.Playable>(e) ? 1 : 0,   // derived: the PRESENCE of the component — no attribute reads that
                Apply = (w, e, v, _) => PlayableOps.SetPlayable(w, e, v != 0) ? null : "not available",
            },
            new FormField
            {
                Label = "Slot", Kind = FieldKind.Text, Inline = true, InlineCaption = "slot", Attribute = "Playable.SlotName",
            },
            new FormField
            {
                Label = "Playable for", Kind = FieldKind.Choice, Attribute = "Playable.Side", Choices = Sides,
                ChoiceValue = SideChoiceValue,
            },
            // ⭐ U-24 / S5 (DESIGN_Initial_State §4.2) — Carried as: HOW the soldier carries him (Carried · Dragged · Stretcher). Shown only for a man a PERSON carries. Not an attribute: it re-issues the ONE embark
            //   (EmbarkOrders.Embark) with the new mode — Embarkation.TryEmbark re-carries a man already in his seat (backend NOTE_Backend_To_Ui_Carry_Mode_And_Damage_Built ①).
            new FormField
            {
                Label = "Carried as", Kind = FieldKind.Choice, Choices = CarriedAsWords,
                Has = IsCarried,
                ReadNumber = (w, e) => CarriedAsIndex(w, e),   // derived: the carry mode of the carrier's seat — no attribute
                Apply = (w, e, v, _) => CarriedAs(w, e, (int)v),
            },
            // ⭐ U-24 / S6 (DESIGN_Initial_State §4.2) — the Character section: the EFFECTIVE value shown, a blue reset arrow when an authored value differs from the type's (a NEGATIVE write clears the authored bit)
            Character("Skill", "Proficiency", "Character",
                type: (w, e) =>   // derived: the TYPE's value (the reader gives the effective one)
                {
                    var p = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CombatProficiency>(e);
                    if ((p.Set & Fdp.Toolkit.Behavior.Components.CombatProficiency.AuthoredSet) == 0) return null;
                    return (p.Set & Fdp.Toolkit.Behavior.Components.CombatProficiency.UnitSet) != 0 ? p.Unit
                         : (p.Set & Fdp.Toolkit.Behavior.Components.CombatProficiency.TypeDefaultSet) != 0 ? p.TypeDefault : Fdp.Toolkit.Behavior.Components.ProficiencyOf.Regular;
                }),
            Trait("Bravery", "Temperament.RiskAcceptance", Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultRiskAcceptance, t => t.TypeRiskAcceptance),
            Trait("Obedience", "Temperament.Obedience", Fdp.Toolkit.Behavior.Components.CombatTemperament.ObedienceSet,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultObedience, t => t.TypeObedience),
            Trait("Nerve", "Temperament.Nerve", Fdp.Toolkit.Behavior.Components.CombatTemperament.NerveSet,
                Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultNerve, t => t.TypeNerve),
            Trait("Resolve", "Temperament.Resolve", Fdp.Toolkit.Behavior.Components.CombatTemperament.ResolveSet,
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
        }, DamageFields),
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
            // ⭐ U-24 / S5 + R-4157 — knows about: the SEEDS this station sent for him (Add… seeds a target through the ONE order, KnowledgeOrders.SeedTarget → Edits.Submit(Interact), applied on the node that runs his brain), each with its
            //   outcome — pending · applied · refused. ⛔ Not what he knows NOW: that is TargetMemory, shown on the Brain tab where the brain runs. ⛔ no Remove: nothing in the engine forgets a target on request.
            new FormList
            {
                Label = "Knows about", RowLabels = SeedWords,
                Fill = FillSeeds,
                Footer = SeedsFooter,
                AddLabel = "Add...",
                Add = SeedKnown,
            },
            // ⭐ L-13 / CE-4313 (Scenario lifecycle LC-5) — the COMMANDER row: who he answers to (the ORBAT's Command relation). Commander… → the ONE write path the
            //   ORBAT's drop takes (`Edits.Submit(EditOp.SetCommander)`), refused by the ORBAT's own rule (`CommandHierarchyAdapter.ReparentRefusal`); Detach → no commander.
            new FormList
            {
                Label = "Commander",
                Fill = FillCommander,
                Footer = (w, e) => CommanderOf(w, e).IsNull ? "no commander" : null,
                RemoveLabel = "Detach",
                Remove = (w, it) => { Fdp.Toolkit.Replication.Edits.Edits.Submit(w, Fdp.Toolkit.Replication.Edits.EditOp.SetCommander(it.Item, Entity.Null, "Detach from its commander")); },
                AddLabel = "Commander...",
                Add = AssignCommander,
            },
            // ⭐ U-24 / S5 — carried by: a soldier carries him (Carried · Dragged · Stretcher + the second man of a stretcher). Put down → the ONE disembark; Carried by… → the ONE embark with a carry mode.
            //   how he is carried is the Carried as field; the second man of a stretcher is the Second man list
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
            // ⭐ U-24 / S5 — carries: one line per weapon in a station of HIS carrier buffer — in his Hands, on his Back, or Hidden. Drop → the ONE disembark; Add… → the ONE embark (the owner puts it in the FIRST free
            //   station that accepts it — `Embarkation.FreeStation`). ⏳ not yet: choosing WHICH station (Hands vs Back — `CarryStations.Ask` / `RestationEvent` moves one already carried)
            new FormList
            {
                Label = "Carries", RowLabels = StationWords,
                Fill = FillCarries,
                Footer = (w, e) => HasStations(w, e) ? null : "carries nothing",
                RemoveLabel = "Drop",
                Remove = (w, it) => Hrot.Common.Orders.EmbarkOrders.Disembark(w, it.Item),
                AddLabel = "Add...",
                Add = AddWeapon,
            },
            // ⭐ U-24 / S5 — second man: the man at the other end of a stretcher. Add… re-carries him as a stretcher with that man (the ONE embark); Let go → carried by one man again. Only for a man a person carries.
            new FormList
            {
                Label = "Second man", RowLabels = CarryWords,
                Fill = FillSecondMan,
                Footer = (w, e) => IsCarried(w, e) ? null : "not carried",
                RemoveLabel = "Let go",
                Remove = (w, it) => CarriedAs(w, it.Item, 0),
                AddLabel = "Second man...",
                Add = SecondMan,
            },
        },
    };

    private static FormField[] Concat(FormField[] a, FormField[] b) { var r = new FormField[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }

    private static readonly string[] UnitWords = { "Unit" };
    private static readonly string[] CarryWords = { "Carried", "Carried", "Dragged", "Stretcher", "Second man" };   // by CarryMode (Seated reads as Carried), then the assistant

    private static readonly string[] StationWords = { "Hands", "Back", "Hidden" };

    private static bool HasStations(EntityRepository w, Entity e)
        => w.IsAlive(e) && w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CarrierStations>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.CarrierStations>(e)
           && w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PassengerBuffer>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(e);

    /// <summary>One row per WEAPON in one of his stations: <i>Hands</i> (an open station), <i>Back</i> (stowed), <i>Hidden</i> (stowed and concealed); the row's item is the WEAPON (Drop disembarks it).</summary>
    private static void FillCarries(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        if (!HasStations(w, e)) return;
        ref readonly var buf = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(e);
        ref readonly var stations = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierStations>(e);
        for (int i = 0; i < Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity; i++)
        {
            if (!stations.IsStation(i)) continue;
            var weapon = buf.Passengers[i];
            if (weapon.IsNull || !w.IsAlive(weapon) || !Fdp.Toolkit.Behavior.Embarkation.IsWeaponSystem(w, weapon)) continue;
            into.Add(new FormListItem(stations.IsConcealed(i) ? StationWords[2] : stations.IsStowed(i) ? StationWords[1] : StationWords[0], NameOf(w, weapon, "(a weapon)"), weapon));
        }
    }

    private static string? AddWeapon(EntityRepository w, Entity man, Entity weapon)
    {
        if (!HasStations(w, man)) return "he has nowhere to carry a weapon";
        if (!Fdp.Toolkit.Behavior.Embarkation.IsWeaponSystem(w, weapon)) return "that is not a weapon";
        return Fdp.Toolkit.Behavior.Embarkation.CanEmbark(w, weapon, man) switch
        {
            Fdp.Toolkit.Behavior.EmbarkResult.Applied => Hrot.Common.Orders.EmbarkOrders.Embark(w, weapon, man) ? null : "not sent",
            Fdp.Toolkit.Behavior.EmbarkResult.AlreadyAboard => "he already carries it",
            Fdp.Toolkit.Behavior.EmbarkResult.Full => "his hands and back are full",
            _ => "he cannot carry that (no station of his takes it, it is a fixed weapon, or he is not in the fight)",
        };
    }

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
    }

    private static Entity CarrierOf(EntityRepository w, Entity e) => IsCarried(w, e) ? w.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e).VehicleEntity : Entity.Null;

    private static int CarriedAsIndex(EntityRepository w, Entity e)
    {
        var carrier = CarrierOf(w, e);
        if (carrier.IsNull || !w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PassengerBuffer>() || !w.HasComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(carrier)) return 0;
        int seat = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e).Seat;
        if (seat >= Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity) return 0;
        int mode = (int)w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(carrier).ModeOf(seat);
        return mode <= 1 ? 0 : mode - 1;   // Seated reads as Carried
    }

    private static Entity AssistantOf(EntityRepository w, Entity carrier)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PassengerBuffer>() || !w.HasComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(carrier)) return Entity.Null;
        var a = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(carrier).Assistant;
        return !a.IsNull && w.IsAlive(a) ? a : Entity.Null;
    }

    /// <summary>Re-carries <paramref name="unit"/> in the way <paramref name="index"/> (<see cref="CarriedAsWords"/>) names — the ONE embark, asked first (<c>CanEmbark</c> predicts the re-carry without writing). A stretcher keeps the second man he has.</summary>
    private static string? CarriedAs(EntityRepository w, Entity unit, int index)
    {
        var carrier = CarrierOf(w, unit);
        if (carrier.IsNull) return "he is not carried";
        if (index < 0 || index >= CarriedAsWords.Length) return "not a way to carry him";
        var mode = (Fdp.Toolkit.Behavior.Components.CarryMode)(index + 1);
        var assistant = mode == Fdp.Toolkit.Behavior.Components.CarryMode.Stretcher ? AssistantOf(w, carrier) : Entity.Null;
        if (mode == Fdp.Toolkit.Behavior.Components.CarryMode.Stretcher && assistant.IsNull) return "a stretcher needs a fit second man — add the second man first";
        return Fdp.Toolkit.Behavior.Embarkation.CanEmbark(w, unit, carrier, Fdp.Toolkit.Behavior.Components.SeatRole.Passenger, mode, assistant) switch
        {
            Fdp.Toolkit.Behavior.EmbarkResult.Applied => Hrot.Common.Orders.EmbarkOrders.Embark(w, unit, carrier, mode, assistant) ? null : "not sent",
            Fdp.Toolkit.Behavior.EmbarkResult.AlreadyAboard => null,   // already carried that way
            _ => "he cannot be carried that way (a stretcher needs a fit second man beside the carrier; one man carries or drags alone)",
        };
    }

    /// <summary>The second man of a stretcher: <paramref name="helper"/> takes the rear — the same re-carry, as a stretcher.</summary>
    private static string? SecondMan(EntityRepository w, Entity unit, Entity helper)
    {
        var carrier = CarrierOf(w, unit);
        if (carrier.IsNull) return "he is not carried";
        if (helper == unit || helper == carrier) return "that man cannot hold the other end";
        if (!IsPersonCarrier(w, helper)) return "only a soldier holds the other end";
        const Fdp.Toolkit.Behavior.Components.CarryMode m = Fdp.Toolkit.Behavior.Components.CarryMode.Stretcher;
        return Fdp.Toolkit.Behavior.Embarkation.CanEmbark(w, unit, carrier, Fdp.Toolkit.Behavior.Components.SeatRole.Passenger, m, helper) switch
        {
            Fdp.Toolkit.Behavior.EmbarkResult.Applied => Hrot.Common.Orders.EmbarkOrders.Embark(w, unit, carrier, m, helper) ? null : "not sent",
            Fdp.Toolkit.Behavior.EmbarkResult.AlreadyAboard => "he already holds that end",
            _ => "he cannot hold the other end (he must be fit, and not busy)",
        };
    }

    private static void FillSecondMan(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        var carrier = CarrierOf(w, e);
        if (carrier.IsNull) return;
        var a = AssistantOf(w, carrier);
        if (!a.IsNull) into.Add(new FormListItem(CarryWords[4], NameOf(w, a, "(a soldier)"), e));
    }

    private static FixedString64 NameOf(EntityRepository w, Entity e, string fallback)
        => w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(e) ? w.GetComponentRO<EntityInfo>(e).Name : new FixedString64(fallback);

    /// <summary>⭐ L-13 — his commander (<c>UnitSubordinate.Commander</c>, alive and not himself), or <see cref="Entity.Null"/>.</summary>
    public static Entity CommanderOf(EntityRepository w, Entity e)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>() || !w.HasComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>(e)) return Entity.Null;
        var c = w.GetComponentRO<Fdp.Core.CommandHierarchy.UnitSubordinate>(e).Commander;
        return !c.IsNull && c != e && w.IsAlive(c) ? c : Entity.Null;
    }

    /// <summary>One row: the commander's name; the row's item is HIM (Detach detaches him).</summary>
    private static void FillCommander(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        var c = CommanderOf(w, e);
        if (!c.IsNull) into.Add(new FormListItem("Commander", NameOf(w, c, "(a unit)"), e));
    }

    /// <summary>⭐ L-13 — <i>Commander…</i>: a unit that takes subordinates (a <c>UnitRoster</c> — the ORBAT's drop rule), refused by the ORBAT's own rule, sent through the ONE write path.</summary>
    public static string? AssignCommander(EntityRepository w, Entity unit, Entity commander)
    {
        if (unit == commander) return "he cannot command himself";
        if (!w.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>() || !w.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(commander)) return "only a unit commands (a platoon, a squad, an HQ)";
        if (((Fdp.Toolkit.Vis2D.Abstractions.IHierarchyAdapter)new Hrot.UI.Common.Adapters.CommandHierarchyAdapter()).ReparentRefusal(w, unit, commander) is { } why) return why;
        Fdp.Toolkit.Replication.Edits.Edits.Submit(w, Fdp.Toolkit.Replication.Edits.EditOp.SetCommander(unit, commander, "Assign to a commander"));
        return null;
    }

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
    private static string[] BuildMoreWords() { var w = new string[Hrot.Common.Orders.Seeds.Capacity + 1]; for (int i = 0; i < w.Length; i++) w[i] = "+" + i + " more"; return w; }
    private static readonly FixedString64 UnknownName = new("(unknown)");
    private const string WhereKnownNote = "what he knows now: the Brain tab, where his brain runs";

    private static int SeedWord(EntityRepository w, in Hrot.Common.Orders.SeededTarget s, out string? reason)
    {
        reason = null;
        var entry = Fdp.Toolkit.Replication.Edits.EditJournal.Of(w).Find(s.RequestId);
        if (entry == null) return 3;
        if (entry.Status is Fdp.Toolkit.Replication.Edits.EditStatus.Refused or Fdp.Toolkit.Replication.Edits.EditStatus.Conflict or Fdp.Toolkit.Replication.Edits.EditStatus.Lost) { reason = entry.Reason.Length > 0 ? entry.Reason : entry.Status.ToString(); return 2; }
        return entry.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Applied ? 1 : 0;
    }

    /// <summary>One row per seed THIS station sent for him (the newest <see cref="KnowsCap"/>, oldest first): the outcome, the target's name; the row's item is the TARGET. No LINQ, no per-frame string.</summary>
    private static void FillSeeds(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        var seeds = Hrot.Common.Orders.KnowledgeOrders.SeedsOf(w);
        if (seeds.Count == 0) return;
        Fdp.Toolkit.Replication.Edits.EditJournal.Of(w).Refresh(w);   // fold in the outcomes that arrived (idempotent, 0 B)
        int mine = 0;
        for (int i = 0; i < seeds.Count; i++) if (seeds[i].Perceiver == e) mine++;
        int skip = Math.Max(0, mine - KnowsCap);
        for (int i = 0; i < seeds.Count; i++)
        {
            var s = seeds[i];
            if (s.Perceiver != e) continue;
            if (skip > 0) { skip--; continue; }
            var name = w.IsAlive(s.Target) && w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(s.Target) ? w.GetComponentRO<EntityInfo>(s.Target).Name : UnknownName;
            into.Add(new FormListItem(SeedWords[SeedWord(w, in s, out _)], name, s.Target));
        }
    }

    /// <summary>The footer: the last refusal's reason when a seed was refused, else <i>+N more</i> past the cap, else where what he knows NOW is shown.</summary>
    private static string? SeedsFooter(EntityRepository w, Entity e)
    {
        var seeds = Hrot.Common.Orders.KnowledgeOrders.SeedsOf(w);
        int mine = 0; string? refusal = null;
        for (int i = 0; i < seeds.Count; i++)
        {
            var s = seeds[i];
            if (s.Perceiver != e) continue;
            mine++;
            if (SeedWord(w, in s, out var why) == 2) refusal = why;
        }
        if (refusal != null) return refusal;
        return mine > KnowsCap ? MoreWords[Math.Min(mine - KnowsCap, MoreWords.Length - 1)] : WhereKnownNote;
    }

    private static string? SeedKnown(EntityRepository w, Entity unit, Entity target)
        => unit == target ? "he cannot know of himself" : Hrot.Common.Orders.KnowledgeOrders.SeedTarget(w, unit, target) ? null : "not sent";

    /// <summary>The name of the unit his weapon points at, or empty (none / dead): the <i>Aimed at</i> pick's shown text. 0 B.</summary>
    private static FixedString64 AimTargetName(EntityRepository w, Entity e)
    {
        var t = w.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).Target;
        if (t.IsNull || !w.IsAlive(t)) return default;
        return w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(t) ? w.GetComponentRO<EntityInfo>(t).Name : AUnitName;
    }
    private static readonly FixedString64 AUnitName = new("(a unit)");

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

    private static FormField WeatherNumber(string label, string attribute, double min = double.MinValue, double max = double.MaxValue, bool expert = false, string? section = null)
        => new() { Label = label, Attribute = attribute, Min = min, Max = max, Expert = expert, Section = section };

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
                Label = "Spawn side", Kind = FieldKind.Choice, Attribute = "SpawnArea.Side", Choices = Sides,
                ChoiceValue = SideChoiceValue,
            },
            new FormField { Label = "Facing", Attribute = "SpawnArea.FacingDeg", Min = 0, Max = 360 },
            // the region's weather: the ONE weather row set (WeatherRows), shared with the Scenario form
        }, WeatherRows()),
        After = Hrot.UI.Common.Weather.WeatherLayersEditor.Draw,   // ⭐ CE-4346 — a weather region's levels / layers: the same editor as the global weather (nothing drawn for a spawn area)
    };

    /// <summary>The authored weather scalars as form rows — ONE set, used by the Shape form (a weather region) and the Scenario form (the global weather).</summary>
    private static FormField[] WeatherRows(string? section = null) => new[]
    {
            WeatherNumber("Temperature", "Weather.GroundTemperatureC", -80, 70, section: section),
            WeatherNumber("Pressure", "Weather.SeaLevelPressureHpa", 800, 1100),
            WeatherNumber("Humidity", "Weather.RelativeHumidity", 0, 1),   // stored 0…1 (W-7)
            WeatherNumber("Visibility", "Weather.VisibilityM", 0),
            new FormField
            {
                Label = "Precipitation", Kind = FieldKind.Choice, Attribute = "Weather.Precipitation", Choices = Precipitations,
                ChoiceValue = i => AttributeValue.FromInt(i),
            },
            WeatherNumber("Rate", "Weather.PrecipitationMmPerHour", 0),
            WeatherNumber("Fog top", "Weather.FogTopM", 0),
            WeatherNumber("Fog visibility", "Weather.FogVisibilityM", 0),
            WeatherNumber("Edge fade", "Weather.FeatherM", 0),
            WeatherNumber("Lapse rate", "Weather.LapseRateKPerKm", expert: true),
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

    /// <summary>⭐ CE-4346 — the Start row's write: the shown text (<c>2026-06-01 06:00 UTC</c>, the trailing <c>UTC</c> optional) parsed by the ONE parser
    /// (<see cref="Hrot.UI.Common.Weather.ScenarioClockEdit.TryParse"/>) and written through its one edit; returns the refusal, or null.</summary>
    internal static string? ApplyStart(EntityRepository w, string? text)
    {
        var t = (text ?? "").Trim();
        if (t.EndsWith("UTC", StringComparison.OrdinalIgnoreCase)) t = t[..^3].TrimEnd();
        if (!Hrot.UI.Common.Weather.ScenarioClockEdit.TryParse(t, out var utc))
            return $"Write the start as {Hrot.UI.Common.Weather.ScenarioClockEdit.Format} (UTC)";
        return Hrot.UI.Common.Weather.ScenarioClockEdit.SetStartUtc(w, utc) ? null : "The start time could not be written";
    }

    private static string? _terrainName;
    private static FixedString64 _terrainText;

    /// <summary>⭐ N4 — the loaded terrain's name: the ONE reader, <see cref="Fdp.Toolkit.Terrain.TerrainDefinition.LoadedName"/> (the window title uses it too).</summary>
    public static string? TerrainNameOf(EntityRepository w) => Fdp.Toolkit.Terrain.TerrainDefinition.LoadedName(w);

    private static bool HasTerrain(EntityRepository w, Entity e) => TerrainNameOf(w) != null;

    /// <summary>The terrain row's text, cached by the name — a frame reading it allocates nothing.</summary>
    private static FixedString64 TerrainText(EntityRepository w)
    {
        var name = TerrainNameOf(w);
        if (!ReferenceEquals(name, _terrainName)) { _terrainName = name; _terrainText = new FixedString64(name ?? ""); }
        return _terrainText;
    }

    private static bool HasClock(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Weather.ScenarioClock>() && w.HasComponent<Fdp.Toolkit.Weather.ScenarioClock>(e);

    /// <summary>
    /// ⭐ D5 — the <b>Scenario</b> form (§3), drawn when nothing is selected: Time (the start, read-only here; the display offset) and the GLOBAL weather (the
    /// same rows as a region's). ⏳ Not yet: name / description (the file header — a document step, not an attribute), *Capture picture*, editing the start
    /// (a date field kind), Parameters. ✅ Terrain (read-only, N4).
    /// </summary>
    public static readonly FormDecl Scenario = new()
    {
        Id = "form.scenario", Title = "Scenario", Order = 5,
        WorldTarget = WorldEnvironment,
        Fields = Join(new[]
        {
            // ⭐ CE-4346 (X-3) — the start is EDITABLE: it was greyed because a text row with no attribute and no Apply is read-only (FormField.ReadOnly). It writes
            //   through the existing ScenarioStartUtcTicks attribute (ScenarioClockEdit.SetStartUtc — the router writes on the owner, asks it elsewhere).
            new FormField { Label = "Start", Section = "Scenario time", Kind = FieldKind.Text, Has = HasClock, ReadText = (w, e) => StartText(w.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(e).StartUtcTicks),
                            Apply = (w, _, _, text) => ApplyStart(w, text) },
            // ⭐ CE-4346 — the user's name for ScenarioClock.DisplayUtcOffsetMinutes ("Clock offset" meant nothing to a user): the theatre's LOCAL time, minutes from UTC; display only.
            new FormField { Label = "Local time offset", Attribute = "ScenarioUtcOffsetMinutes", Min = -720, Max = 840 },
            // ⭐ New scenario N4 (📄 docs/UX/DESIGN_New_Scenario_And_Recipe.md §4, R-4124 / R-4135) — the terrain, READ-ONLY (no attribute): chosen when the
            //   scenario was created, never changed. Read from the TerrainDefinition singleton every host's terrain commit publishes (the name the scenario resolved by).
            new FormField { Label = "Terrain", Section = "Terrain", Kind = FieldKind.Text, Has = HasTerrain, ReadText = (w, _) => TerrainText(w) },
        }, WeatherRows("Global weather")),
        // ⭐ CE-4346 — the global weather's wind levels and cloud layers, each with its own editor (lists travel whole: Hrot.UI.Common.Weather.WeatherEdit)
        After = Hrot.UI.Common.Weather.WeatherLayersEditor.Draw,
    };

    private static FormField Character(string label, string attribute, string? section, Func<EntityRepository, Entity, double?> type)
        => new FormField { Label = label, Section = section, Attribute = attribute, Min = 0, Max = 1, Slider = true, TypeValue = type };   // the value is the reader's (the EFFECTIVE one); only the type's is derived

    private static FormField Trait(string label, string attribute, byte bit, float dflt, Func<Fdp.Toolkit.Behavior.Components.CombatTemperament, float> typeOf)
        => Character(label, attribute, null,
            (w, e) =>
            {
                var t = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CombatTemperament>(e);
                if ((t.Set & bit) == 0) return null;                          // nothing authored over the type ⇒ no mark
                return (t.TypeSet & bit) != 0 ? typeOf(t) : dflt;             // the type's value (with its spread), else the default
            });
}
