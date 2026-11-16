using System;

namespace GizmoMap.Contracts.Symbology
{
    /// <summary>The frame's affiliation (APP-6 / MIL-STD-2525: friend rectangle, hostile diamond, neutral square, unknown quatrefoil).</summary>
    public enum SymbolAffiliation : byte { Unknown = 0, Friend = 1, Hostile = 2, Neutral = 3 }

    /// <summary>What the unit IS — the icon inside the frame (a 2525C land-unit function).</summary>
    public enum SymbolRole : byte
    {
        NoRole = 0, Infantry = 1, Mechanized = 2, Motorized = 3, Armour = 4, Reconnaissance = 5, Artillery = 6, Mortar = 7, AntiArmour = 8,
        Headquarters = 9, Support = 10,
    }

    /// <summary>The echelon marks above the frame (2525C position 12).</summary>
    public enum SymbolEchelon : byte { NoEchelon = 0, Team = 1, Squad = 2, Section = 3, Platoon = 4, Company = 5, Battalion = 6, Regiment = 7, Brigade = 8, Division = 9 }

    /// <summary>The mobility indicator under the frame (an HROT extension of the code — 2525's equipment mobility, drawn for a unit's carriers).</summary>
    public enum SymbolMobility : byte { NoMobility = 0, Wheeled = 1, Tracked = 2 }

    /// <summary>
    /// ⭐ <c>CE-8608</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §6 S-5; docs/UX/DESIGN_Unit_Symbols.md) — a land unit's symbol as DATA: the
    /// affiliation, the role (the icon), the echelon, the mobility and one secondary role. Its wire form is a 2525C SIDC (15 characters) with HROT's two optional
    /// fields after it — <c>SFGPUCIZ---D---|T|AT</c> — at most 21 characters, so it rides the 31-character text of the existing <c>Icon</c> / <c>MilStd2525</c>
    /// primitives (nothing new on the wire), and an IG can read the standard part. Parsing and formatting allocate nothing.
    /// </summary>
    public readonly struct SymbolCode : IEquatable<SymbolCode>
    {
        public readonly SymbolAffiliation Affiliation;
        public readonly SymbolRole Role;
        public readonly SymbolEchelon Echelon;
        public readonly SymbolMobility Mobility;
        public readonly SymbolRole Secondary;

        public SymbolCode(SymbolAffiliation affiliation, SymbolRole role, SymbolEchelon echelon, SymbolMobility mobility = SymbolMobility.NoMobility, SymbolRole secondary = SymbolRole.NoRole)
        {
            Affiliation = affiliation; Role = role; Echelon = echelon; Mobility = mobility; Secondary = secondary;
        }

        /// <summary>The longest wire form: 15 + "|T" + "|AT".</summary>
        public const int MaxLength = 21;

        /// <summary>The 2525C function id (positions 5–10) of a role.</summary>
        public static string FunctionOf(SymbolRole r) => r switch
        {
            SymbolRole.Infantry => "UCI---", SymbolRole.Mechanized => "UCIZ--", SymbolRole.Motorized => "UCIM--", SymbolRole.Armour => "UCA---",
            SymbolRole.Reconnaissance => "UCR---", SymbolRole.Artillery => "UCF---", SymbolRole.Mortar => "UCFM--", SymbolRole.AntiArmour => "UCAA--",
            SymbolRole.Headquarters => "UH----", SymbolRole.Support => "US----", _ => "U-----",
        };

        /// <summary>The short mark a SECONDARY role draws (a small box at the frame's lower right).</summary>
        public static string MarkOf(SymbolRole r) => r switch
        {
            SymbolRole.AntiArmour => "AT", SymbolRole.Mortar => "M", SymbolRole.Artillery => "A", SymbolRole.Reconnaissance => "R",
            SymbolRole.Armour => "TK", SymbolRole.Headquarters => "HQ", SymbolRole.Support => "S", _ => "",
        };

        private static char EchelonChar(SymbolEchelon e) => e switch
        {
            SymbolEchelon.Team => 'A', SymbolEchelon.Squad => 'B', SymbolEchelon.Section => 'C', SymbolEchelon.Platoon => 'D', SymbolEchelon.Company => 'E',
            SymbolEchelon.Battalion => 'F', SymbolEchelon.Regiment => 'G', SymbolEchelon.Brigade => 'H', SymbolEchelon.Division => 'I', _ => '-',
        };

        private static SymbolEchelon EchelonOf(char c) => c switch
        {
            'A' => SymbolEchelon.Team, 'B' => SymbolEchelon.Squad, 'C' => SymbolEchelon.Section, 'D' => SymbolEchelon.Platoon, 'E' => SymbolEchelon.Company,
            'F' => SymbolEchelon.Battalion, 'G' => SymbolEchelon.Regiment, 'H' => SymbolEchelon.Brigade, 'I' => SymbolEchelon.Division, _ => SymbolEchelon.NoEchelon,
        };

        private static char AffiliationChar(SymbolAffiliation a) => a switch { SymbolAffiliation.Friend => 'F', SymbolAffiliation.Hostile => 'H', SymbolAffiliation.Neutral => 'N', _ => 'U' };

        /// <summary>2525C position 2 → the frame (assumed friend counts as friend, suspect / joker / faker as hostile, pending as unknown).</summary>
        public static SymbolAffiliation AffiliationOf(char c) => c switch
        {
            'F' or 'A' or 'D' or 'M' => SymbolAffiliation.Friend,
            'H' or 'S' or 'J' or 'K' => SymbolAffiliation.Hostile,
            'N' or 'L' => SymbolAffiliation.Neutral,
            _ => SymbolAffiliation.Unknown,
        };

        /// <summary>Writes the wire form into <paramref name="dst"/> (≥ <see cref="MaxLength"/>); returns its length.</summary>
        public int Format(Span<char> dst)
        {
            if (dst.Length < MaxLength) throw new ArgumentException("needs " + MaxLength + " characters", nameof(dst));
            dst[0] = 'S'; dst[1] = AffiliationChar(Affiliation); dst[2] = 'G'; dst[3] = 'P';
            var f = FunctionOf(Role);
            for (int i = 0; i < 6; i++) dst[4 + i] = f[i];
            dst[10] = Role == SymbolRole.Headquarters ? 'A' : '-';
            dst[11] = EchelonChar(Echelon);
            dst[12] = '-'; dst[13] = '-'; dst[14] = '-';
            int n = 15;
            if (Mobility != SymbolMobility.NoMobility || Secondary != SymbolRole.NoRole)
            {
                dst[n++] = '|';
                dst[n++] = Mobility == SymbolMobility.Wheeled ? 'W' : Mobility == SymbolMobility.Tracked ? 'T' : '-';
            }
            var mark = MarkOf(Secondary);
            if (mark.Length > 0)
            {
                dst[n++] = '|';
                for (int i = 0; i < mark.Length; i++) dst[n++] = mark[i];
            }
            return n;
        }

        /// <summary>Reads a wire form (a plain 15-character SIDC too). False when it is not a land-unit SIDC this vocabulary draws.</summary>
        public static bool TryParse(ReadOnlySpan<char> code, out SymbolCode symbol)
        {
            symbol = default;
            if (code.Length < 15 || code[0] != 'S') return false;
            var aff = AffiliationOf(code[1]);
            var fn = code.Slice(4, 6);
            var role = SymbolRole.NoRole;
            for (var r = SymbolRole.Infantry; r <= SymbolRole.Support; r++)
                if (fn.SequenceEqual(FunctionOf(r).AsSpan())) { role = r; break; }
            if (role == SymbolRole.NoRole && fn[0] == 'U') role = SymbolRole.NoRole;   // an unknown land unit: the frame only
            else if (role == SymbolRole.NoRole) return false;
            var ech = EchelonOf(code[11]);
            var mob = SymbolMobility.NoMobility; var sec = SymbolRole.NoRole;
            var rest = code.Slice(15);
            if (rest.Length >= 2 && rest[0] == '|')
            {
                mob = rest[1] == 'W' ? SymbolMobility.Wheeled : rest[1] == 'T' ? SymbolMobility.Tracked : SymbolMobility.NoMobility;
                rest = rest.Slice(2);
                if (rest.Length >= 2 && rest[0] == '|')
                {
                    var mark = rest.Slice(1);
                    for (var r = SymbolRole.Infantry; r <= SymbolRole.Support; r++)
                        if (MarkOf(r).Length > 0 && mark.SequenceEqual(MarkOf(r).AsSpan())) { sec = r; break; }
                }
            }
            symbol = new SymbolCode(aff, role, ech, mob, sec);
            return true;
        }

        public bool Equals(SymbolCode o) => Affiliation == o.Affiliation && Role == o.Role && Echelon == o.Echelon && Mobility == o.Mobility && Secondary == o.Secondary;
        public override bool Equals(object? obj) => obj is SymbolCode o && Equals(o);
        public override int GetHashCode() => (int)Affiliation | ((int)Role << 4) | ((int)Echelon << 9) | ((int)Mobility << 14) | ((int)Secondary << 16);
    }
}
