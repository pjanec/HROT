using System;
using Fdp.Core;

namespace Hrot.Core.Tkb
{
    /// <summary>The arm of a unit — its DIS Category under <see cref="UnitDis.Kind"/>. HROT codes (no SISO aggregate table exists
    /// in the repo to copy, AQ90 §2.3m A).</summary>
    public enum UnitArm : byte
    {
        Infantry = 1,
        Armor = 2,
        MechanizedInfantry = 3,
        Artillery = 4,
        Reconnaissance = 5,
    }

    /// <summary>The echelon of a unit — its DIS Subcategory under <see cref="UnitDis.Kind"/>; the MIL-STD-2525 echelon order,
    /// smallest first. A <c>TkbCompositionDef.Echelon</c> string names one of these.</summary>
    public enum UnitEchelon : byte
    {
        Team = 1,
        Squad = 2,
        Section = 3,
        Platoon = 4,
        Company = 5,
        Battalion = 6,
        Regiment = 7,
        Brigade = 8,
        Division = 9,
    }

    /// <summary>
    /// ⭐ <c>CE-2139</c> (C3, docs/DESIGN_Orders_Params_Entity_Classes.md rule K2, R-278 (behaviors)) — the DIS layout of a unit
    /// (a composite, body-less <c>UnitEntity</c>): its own Kind, so no platform mask can ever match it. Domain and Country stay
    /// what they mean for any entity (the arena, the side's country); Category = <see cref="UnitArm"/>, Subcategory =
    /// <see cref="UnitEchelon"/>, Specific = the variant (1 = the base type).
    /// </summary>
    public static class UnitDis
    {
        /// <summary>The DIS Kind of a unit — a value SISO-REF-010 leaves unused (it defines 0–9).</summary>
        public const byte Kind = 11;

        /// <summary>The DIS type of a land unit.</summary>
        public static DISEntityType Land(UnitArm arm, UnitEchelon echelon, ushort country, byte variant = 1)
            => new() { Kind = Kind, Domain = 1, Country = country, Category = (byte)arm, Subcategory = (byte)echelon, Specific = variant };

        /// <summary>The echelon a <c>TkbCompositionDef.Echelon</c> string names (case-insensitive); throws on an unknown name so
        /// a typo cannot leave a unit without an echelon.</summary>
        public static UnitEchelon EchelonOf(string echelon)
            => Enum.TryParse<UnitEchelon>(echelon, ignoreCase: true, out var e) && Enum.IsDefined(e)
                ? e
                : throw new ArgumentException($"unit echelon '{echelon}' is not one of {string.Join(", ", Enum.GetNames<UnitEchelon>())}.");

        /// <summary>True when <paramref name="dis"/> is a unit's type.</summary>
        public static bool IsUnit(DISEntityType dis) => dis.Kind == Kind;
    }
}
