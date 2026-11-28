using System;
using System.Collections.Generic;

namespace Hrot.Core.Ambience
{
    /// <summary>
    /// ⭐ <c>CE-11034</c> (R-11014; 📄 docs/designs/audio/DESIGN_Ambience.md §3, AM-1) — THE ONE list of ambience KINDS: short dotted names a renderer maps to its own sounds,
    /// never a sample. ⭐ One list, no second copy: the <c>SetAmbience</c> action, the editor's kind picker (slice 2) and the gateway's DPS row all read it.
    /// <para>A kind's PARENT is the name with its last dotted segment dropped (<c>alarm.airraid.preliminary</c> → <c>alarm.airraid</c> → <c>alarm</c>); the four families
    /// (<c>battle</c>, <c>event</c>, <c>alarm</c>, <c>naval</c>) are kinds themselves — the generic sound of the family — and have no parent. A reader that does not know
    /// a name plays its nearest known ancestor (<see cref="Resolve"/>); a name with no known ancestor resolves to nothing (<c>null</c>) — AM-4: run time ALLOWS it (the caller
    /// logs it once), the editor's picker never offers it.</para>
    /// </summary>
    public static class AmbienceKinds
    {
        /// <summary>The families (the first dotted segment) — each is a kind too: the generic sound of the family.</summary>
        public static readonly IReadOnlyList<string> Families = new[] { "battle", "event", "alarm", "naval" };

        /// <summary>Every listed kind, families first, in the design's §3 order. Unreleased names are added HERE and nowhere else.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            "battle", "battle.distant", "battle.city", "battle.night", "battle.heavy", "battle.sniper", "battle.ambush", "battle.aftermath",
            "event", "event.airstrike", "event.drone", "event.rocket", "event.convoy", "event.evacuation", "event.evacuation.city", "event.nuclear",
            "alarm", "alarm.airraid", "alarm.airraid.preliminary", "alarm.bell", "alarm.intruder", "alarm.emergency",
            "naval", "naval.ship", "naval.warship", "naval.underwater",
        };

        private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

        /// <summary>The longest kind a layer can carry (<c>FixedString32</c>: 31 bytes).</summary>
        public const int MaxLength = 31;

        /// <summary>True when <paramref name="kind"/> is a listed kind (exact).</summary>
        public static bool IsKnown(string? kind) => kind != null && Known.Contains(kind);

        /// <summary>The parent of <paramref name="kind"/> — its name without the last dotted segment — or <c>null</c> for a family (or an empty / undotted name).</summary>
        public static string? Parent(string? kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            int dot = kind.LastIndexOf('.');
            return dot <= 0 ? null : kind.Substring(0, dot);
        }

        /// <summary>The nearest KNOWN kind: <paramref name="kind"/> itself, else its parent, else its grandparent …; <c>null</c> when none of them is listed (a totally unknown name).</summary>
        public static string? Resolve(string? kind)
        {
            for (var k = kind; !string.IsNullOrEmpty(k); k = Parent(k))
                if (Known.Contains(k)) return k;
            return null;
        }

        /// <summary>What is wrong with <paramref name="kind"/> as an AUTHORED name, or <c>null</c>: empty, too long for a layer, or with no known ancestor (the editor's picker and the action's argument check use it).</summary>
        public static string? Check(string? kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return "the ambience kind is empty";
            if (kind.Length > MaxLength) return $"the ambience kind is {kind.Length} characters; at most {MaxLength}";
            if (Resolve(kind) == null) return $"'{kind}' is not an ambience kind (nor a child of one); the kinds are {string.Join(", ", All)}";
            return null;
        }
    }
}
