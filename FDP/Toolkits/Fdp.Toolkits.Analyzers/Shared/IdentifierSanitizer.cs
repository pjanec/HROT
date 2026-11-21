using System.Collections.Generic;
using System.Text;

namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-2039</c> — the ONE home of "an asset name becomes a C# identifier"</b>, in the three shapes the generators
    /// have always produced. 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8g".
    ///
    /// <para>
    /// ⛔ It was written fifteen times (two analyzers' worth of private copies, four emit cores, the TKB generator, the
    /// sync-identity and alias helpers, the HSM and utility editors, the blueprint compiler and its hand-kept mirror). The
    /// copies within each shape agreed; the SHAPES are kept, because each one's output is persisted or pinned:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="ReplaceInvalid"/> — non-identifier chars become <c>_</c> (TKB, the analyzers' catalog names, an
    /// auto-managed variable's name).</item>
    /// <item><see cref="StripInvalid"/> — non-identifier chars are removed (JSON BTree/HSM classes and their structs, subtree
    /// sync fields, utility decision classes).</item>
    /// <item><see cref="PascalJoin"/> — letter/digit runs joined PascalCase, <c>_</c> dropped (a blueprint's class: persisted in
    /// <c>…_Bp+Params</c> type ids, so never renamed).</item>
    /// </list>
    /// <para>
    /// ⭐ Two guards are new and change output ONLY where it was invalid C# before: a name emitted BARE (<c>bare: true</c>)
    /// that is a reserved <see cref="IsReservedKeyword"/> gets a <c>_</c> prefix (a BTree named <c>class</c> emitted
    /// <c>public static class class</c>), and <see cref="PascalJoin"/> prefixes a leading digit as the other two always did
    /// (a blueprint named <c>2Fast</c> emitted <c>2Fast_…_Bp</c>).
    /// </para>
    /// <para>⛔ A linked <c>internal</c> file for the reason <see cref="HsmActionKey"/> gives; net8.0 editors reach it through
    /// the public <c>Hrot.AiEditor.Persistence.Emit.Identifiers</c>.</para>
    /// </summary>
    internal static class IdentifierSanitizer
    {
        /// <summary>Non-identifier chars → <c>_</c>; a leading digit gets <c>_</c>; nothing left → <paramref name="emptyResult"/>.</summary>
        public static string ReplaceInvalid(string? name, string emptyResult, bool bare = false)
        {
            var sb = new StringBuilder(name?.Length ?? 0);
            if (name != null)
                foreach (char c in name)
                    sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length == 0) return emptyResult;
            return Guard(sb, bare);
        }

        /// <summary>Non-identifier chars removed; a leading digit gets <c>_</c>; nothing left → <paramref name="fallback"/>.</summary>
        public static string StripInvalid(string? name, string fallback, bool bare = false)
        {
            var sb = new StringBuilder(name?.Length ?? 0);
            if (name != null)
                foreach (char c in name)
                    if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            if (sb.Length == 0) return fallback;
            return Guard(sb, bare);
        }

        /// <summary>
        /// Letter/digit runs joined PascalCase (every other char, <c>_</c> included, is a word break); a leading digit gets
        /// <c>_</c>; nothing left → <paramref name="fallback"/>. <c>"Move To And Fire"</c> → <c>MoveToAndFire</c>.
        /// </summary>
        public static string PascalJoin(string? name, string fallback)
        {
            var sb = new StringBuilder(name?.Length ?? 0);
            bool capitalizeNext = true;
            if (name != null)
                foreach (char c in name)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                        capitalizeNext = false;
                    }
                    else
                    {
                        capitalizeNext = true;
                    }
                }
            if (sb.Length == 0) return fallback;
            return Guard(sb, bare: false);   // its first char is upper-case, so it can never be a reserved keyword
        }

        /// <summary>True for a C# RESERVED keyword — legal as an identifier only with <c>@</c>. Contextual keywords are legal.</summary>
        public static bool IsReservedKeyword(string? name) => name != null && ReservedKeywords.Contains(name);

        /// <summary>
        /// A leading digit gets <c>_</c> (all three shapes). <paramref name="bare"/>: the result is emitted AS a whole identifier
        /// (a class name), so a reserved keyword gets <c>_</c> too. ⛔ Off for a name that is only ever a PART of one
        /// (<c>{X}Params</c>, <c>Get{X}</c>, <c>{X}_{Y}</c>) — <c>classParams</c> was always valid and must not be renamed.
        /// </summary>
        private static string Guard(StringBuilder sb, bool bare)
        {
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            string s = sb.ToString();
            return bare && ReservedKeywords.Contains(s) ? "_" + s : s;
        }

        private static readonly HashSet<string> ReservedKeywords = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while",
        };
    }
}
