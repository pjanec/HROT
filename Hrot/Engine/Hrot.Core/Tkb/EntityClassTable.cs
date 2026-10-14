using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;

namespace Hrot.Core.Tkb
{
    /// <summary>One entity class: a NAMED DIS mask (<c>Tank = 1.1.*.1</c>).</summary>
    /// <param name="Name">The identifier — what a behaviour fulfilling an order for this class is named after
    /// (<c>&lt;Class&gt;&lt;Order&gt;</c>, R-278 (behaviors)). Unique in the table.</param>
    /// <param name="DisplayName">What a person reads (the picker folder, "what is this?").</param>
    /// <param name="Mask">The DIS types it covers.</param>
    public sealed record EntityClass(string Name, string DisplayName, DisTypeMask Mask)
    {
        /// <summary>True when this class NAMES one DIS level — Kind, Domain or Category — exactly, with every other segment
        /// <c>*</c>: <c>1.*</c>, <c>1.1</c>, <c>1.1.*.1</c>. These are the picker's folder levels; a grouping class with a list
        /// or a range (<c>Armor = 1.1.*.1,2</c>) is a class but never a folder.</summary>
        public bool NamesALevel
        {
            get
            {
                int deepest = Mask.DeepestPinned;
                if (deepest is not (0 or 1 or 3)) return false;
                for (int i = 0; i <= deepest; i++)
                {
                    var k = Mask.KindOf(i);
                    if (i == 2) { if (k != DisTypeMask.SegmentKind.Any) return false; }
                    else if (k != DisTypeMask.SegmentKind.Exact) return false;
                }
                return true;
            }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2138</c> (C2, docs/DESIGN_Orders_Params_Entity_Classes.md §2, rule K1; R-279 / R-280 (behaviors)) — THE one
    /// hard-coded answer to "what kind of entity is this": named DIS masks, general → specific. Shared by the 3-D model
    /// (<c>VisualFamilies</c>), the Add-Entity picker (<see cref="DisNameTable"/>), and the behaviour lookup (D3).
    /// <para>⛔ Hard-coded C#, no data file (R-279, R-280). Add a class here; a rail checks names and masks are unique and that
    /// two classes of equal specificity never overlap (which would make "most specific" a tie).</para>
    /// </summary>
    public sealed class EntityClassTable
    {
        private readonly EntityClass[] _classes;
        private readonly Dictionary<string, EntityClass> _byName;

        /// <summary>The table shipped with the engine.</summary>
        public static EntityClassTable Default { get; } = new(BuiltIn());

        /// <summary>Builds a table; throws on a duplicate name or an identical mask.</summary>
        public EntityClassTable(IEnumerable<EntityClass> classes)
        {
            _classes = classes.ToArray();
            _byName = new Dictionary<string, EntityClass>(StringComparer.Ordinal);
            var masks = new Dictionary<DisTypeMask, string>();
            foreach (var c in _classes)
            {
                if (!_byName.TryAdd(c.Name, c)) throw new ArgumentException($"entity classes: the name '{c.Name}' appears twice.");
                if (!masks.TryAdd(c.Mask, c.Name))
                    throw new ArgumentException($"entity classes: '{c.Name}' and '{masks[c.Mask]}' share the mask {c.Mask}.");
            }
        }

        /// <summary>Every class, in table order (general → specific within each branch).</summary>
        public IReadOnlyList<EntityClass> Classes => _classes;

        /// <summary>The class named <paramref name="name"/>, or null.</summary>
        public EntityClass? Find(string name) => _byName.TryGetValue(name, out var c) ? c : null;

        /// <summary>Every class whose mask matches <paramref name="dis"/>, from general to specific (ascending
        /// <see cref="DisTypeMask.Specificity"/>; table order breaks an equal rank).</summary>
        public IReadOnlyList<EntityClass> Chain(DISEntityType dis)
        {
            var list = new List<EntityClass>();
            foreach (var c in _classes) if (c.Mask.Matches(dis)) list.Add(c);
            return list.OrderBy(c => c.Mask.Specificity).ToList();   // OrderBy is stable
        }

        /// <summary>The most specific matching class, or null when none matches.</summary>
        public EntityClass? MostSpecific(DISEntityType dis)
        {
            EntityClass? best = null;
            foreach (var c in _classes)
                if (c.Mask.Matches(dis) && (best is null || c.Mask.Specificity > best.Mask.Specificity)) best = c;
            return best;
        }

        /// <summary>Pairs of classes with equal specificity whose masks overlap — each a type for which "most specific" would
        /// be a tie. The built-in table has none (rail).</summary>
        public IEnumerable<(EntityClass A, EntityClass B)> Ties()
        {
            for (int i = 0; i < _classes.Length; i++)
                for (int j = i + 1; j < _classes.Length; j++)
                    if (_classes[i].Mask.Specificity == _classes[j].Mask.Specificity && _classes[i].Mask.Overlaps(_classes[j].Mask))
                        yield return (_classes[i], _classes[j]);
        }

        private static EntityClass C(string name, string display, string mask) => new(name, display, DisTypeMask.Parse(mask));

        // ── THE TABLE ────────────────────────────────────────────────────────────────────────────────────────────────────────
        // SISO-REF-010 names; "(HROT)" marks this project's own name for a level SISO leaves generic. The level classes (one
        // exact Kind / Domain / Category) were DisNames.json until CE-2138; the grouping classes (lists, ranges) were the
        // switches in VisualFamilies.Of.
        private static IEnumerable<EntityClass> BuiltIn()
        {
            // Kinds
            yield return C("Platform", "Platform", "1");
            yield return C("Munition", "Munition", "2");
            yield return C("Person", "Life Form", "3");
            yield return C("Environmental", "Environmental", "4");
            yield return C("CulturalFeature", "Cultural Feature", "5");
            yield return C("Supply", "Supply", "6");
            yield return C("Radio", "Radio", "7");
            yield return C("Expendable", "Expendable", "8");
            yield return C("SensorEmitter", "Sensor/Emitter", "9");

            // Platform domains
            yield return C("LandPlatform", "Land", "1.1");
            yield return C("AirPlatform", "Air", "1.2");
            yield return C("SurfacePlatform", "Surface", "1.3");
            yield return C("SubsurfacePlatform", "Subsurface", "1.4");
            yield return C("SpacePlatform", "Space", "1.5");

            // Land platforms: groups, then categories
            yield return C("Armor", "Armor", "1.1.*.1,2");
            yield return C("WheeledUtility", "Wheeled Utility", "1.1.*.3,6,7");
            yield return C("Tank", "Tank", "1.1.*.1");
            yield return C("Afv", "Armored Fighting Vehicle", "1.1.*.2");
            yield return C("ArmoredUtilityVehicle", "Armored Utility Vehicle", "1.1.*.3");
            yield return C("SelfPropelledArtillery", "Self-Propelled Artillery", "1.1.*.4");
            yield return C("TowedArtillery", "Towed Artillery", "1.1.*.5");
            yield return C("SmallWheeledUtilityVehicle", "Small Wheeled Utility Vehicle", "1.1.*.6");
            yield return C("LargeWheeledUtilityVehicle", "Large Wheeled Utility Vehicle", "1.1.*.7");
            yield return C("SmallTrackedUtilityVehicle", "Small Tracked Utility Vehicle", "1.1.*.8");
            yield return C("LargeTrackedUtilityVehicle", "Large Tracked Utility Vehicle", "1.1.*.9");
            yield return C("Mortar", "Mortar", "1.1.*.10");
            yield return C("Car", "Car", "1.1.*.81");
            // ⭐ Weapon systems WS-1 (R-2016, docs/designs/weapons/DESIGN_Weapon_Systems.md §5) — a weapon that is an entity: Category 50
            //   (HROT; free on land), subcategories by kind.
            yield return C("WeaponSystem", "Weapon System (HROT)", "1.1.*.50");
            yield return C("HmgWeaponSystem", "Heavy Machine Gun (HROT)", "1.1.*.50.1");
            yield return C("MinigunWeaponSystem", "Minigun (HROT)", "1.1.*.50.2");
            yield return C("AtgmWeaponSystem", "ATGM Launcher (HROT)", "1.1.*.50.3");
            yield return C("LauncherWeaponSystem", "Rocket Launcher (HROT)", "1.1.*.50.4");
            yield return C("SentryWeaponSystem", "Sentry Gun (HROT)", "1.1.*.50.5");

            // Air platforms: groups, then categories
            yield return C("Helicopter", "Helicopter", "1.2.*.20-25");
            yield return C("Jet", "Jet", "1.2.*.1,2,6,7,40,47,50");
            yield return C("CargoPlane", "Cargo Plane", "1.2.*.3,4,5,8,57");
            yield return C("Fighter", "Fighter/Air Defense", "1.2.*.1");
            yield return C("AttackAircraft", "Attack/Strike", "1.2.*.2");
            yield return C("Bomber", "Bomber", "1.2.*.3");
            yield return C("CargoTanker", "Cargo/Tanker", "1.2.*.4");
            yield return C("PatrolAircraft", "ASW/Patrol/Observation", "1.2.*.5");
            yield return C("ElectronicWarfareAircraft", "Electronic Warfare", "1.2.*.6");
            yield return C("ReconnaissanceAircraft", "Reconnaissance", "1.2.*.7");
            yield return C("SurveillanceAircraft", "Surveillance/C2 (AEW)", "1.2.*.8");
            yield return C("AttackHelicopter", "Attack Helicopter", "1.2.*.20");
            yield return C("UtilityHelicopter", "Utility Helicopter", "1.2.*.21");
            yield return C("PatrolHelicopter", "ASW/Patrol Helicopter", "1.2.*.22");
            yield return C("CargoHelicopter", "Cargo Helicopter", "1.2.*.23");
            yield return C("ObservationHelicopter", "Observation Helicopter", "1.2.*.24");
            yield return C("SpecialOperationsHelicopter", "Special Operations Helicopter", "1.2.*.25");
            yield return C("Trainer", "Trainer", "1.2.*.40");
            yield return C("LightAircraft", "Light/Patrol Craft", "1.2.*.47");
            yield return C("Uav", "Unmanned Air Vehicle", "1.2.*.50");
            yield return C("CommercialAircraft", "Non-Combatant Commercial Aircraft", "1.2.*.57");

            // Life-form domains and categories
            yield return C("LandPerson", "Land", "3.1");
            yield return C("AirPerson", "Air", "3.2");
            yield return C("SurfacePerson", "Surface", "3.3");
            yield return C("SubsurfacePerson", "Subsurface", "3.4");
            yield return C("Infantry", "Infantry (HROT)", "3.1.*.1");

            // ⭐ CE-2139 (C3, R-278) — UNITS, under their own Kind (UnitDis): Category = arm, Subcategory = echelon. The Kind's
            //   display name is the picker's units folder (EntityTypeCatalog.UnitsFolder, which ExCon's ORBAT list keys on).
            yield return C("Unit", "Units", "11");
            yield return C("LandUnit", "Land", "11.1");
            yield return C("InfantryUnit", "Infantry", "11.1.*.1");
            yield return C("ArmorUnit", "Armor", "11.1.*.2");
            yield return C("MechanizedInfantryUnit", "Mechanized Infantry", "11.1.*.3");
            yield return C("ArtilleryUnit", "Artillery", "11.1.*.4");
            yield return C("ReconnaissanceUnit", "Reconnaissance", "11.1.*.5");
            yield return C("InfantrySquad", "Infantry Squad", "11.1.*.1.2");
            yield return C("TankPlatoon", "Tank Platoon", "11.1.*.2.4");
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2138</c> (C2, R-280 (behaviors)) — SISO-REF-010 country codes and names, hard-coded. A country is not a class
    /// (it is orthogonal to what a thing is); it sits beside <see cref="EntityClassTable"/>.
    /// </summary>
    public static class CountryTable
    {
        public const ushort France = 71;
        public const ushort Germany = 78;
        public const ushort Russia = 222;
        public const ushort UnitedKingdom = 224;
        public const ushort UnitedStates = 225;

        private static readonly Dictionary<ushort, string> s_names = new()
        {
            [France] = "France", [Germany] = "Germany", [Russia] = "Russia",
            [UnitedKingdom] = "United Kingdom", [UnitedStates] = "United States",
        };

        /// <summary>Every named country.</summary>
        public static IReadOnlyDictionary<ushort, string> All => s_names;

        /// <summary>The name of <paramref name="code"/>, or null when the table does not name it.</summary>
        public static string? NameOf(ushort code) => s_names.TryGetValue(code, out var n) ? n : null;
    }
}
