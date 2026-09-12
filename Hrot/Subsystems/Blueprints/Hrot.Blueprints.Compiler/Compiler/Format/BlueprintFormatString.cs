using System;
using System.Collections.Generic;
using System.Text;

namespace Hrot.Blueprints.Core.Compiler.Format
{
    /// <summary>
    /// BP-108 — the single parser behind <c>Print String</c> and <c>Format String</c>.
    ///
    /// <para>
    /// ⭐ <b>The format string IS the pin list.</b> Following Unreal's <c>Format Text</c>, one data-in pin
    /// is derived per <c>{Name}</c> placeholder rather than from a separate arity property. That beats an
    /// <c>ArgCount</c> property on every axis — you type <c>"threat={Threat}"</c> and the pin appears,
    /// with a self-documenting name, and there is no second control to keep in sync with the text.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Why derived pins are mandatory, not stylistic.</b> There is no such thing as an optional
    /// data-in pin in this compiler: <c>Stage5.ResolveDataPin</c> emits <c>BP4001</c> plus
    /// <c>default(T)</c> for every unwired one. A speculative pin is therefore a guaranteed diagnostic,
    /// so only placeholders that actually appear may become pins.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>One parser, three consumers</b> — <c>BuiltInNodeRegistry</c> (pin shapes),
    /// <c>Stage2_Validate</c> (the malformed-format diagnostic) and the emitter (named → expression
    /// rewriting). If these ever disagree about what a placeholder is, the pin set and the emitted code
    /// drift apart silently. Do not re-implement any part of this elsewhere.
    /// </para>
    ///
    /// <para>
    /// ⚠ Targets <b>netstandard2.0</b> — no range/index syntax.
    /// </para>
    /// </summary>
    public static class BlueprintFormatString
    {
        /// <summary>Outcome of parsing a node's <c>Format</c> property.</summary>
        public sealed class ParseResult
        {
            internal ParseResult(bool ok, string? error, IReadOnlyList<string> names)
            {
                IsValid = ok;
                Error = error;
                Names = names;
            }

            /// <summary>False when the format is malformed; <see cref="Error"/> then explains why.</summary>
            public bool IsValid { get; }

            /// <summary>
            /// Human-readable reason the format is malformed, suitable for a Stage 2 diagnostic message.
            /// Null when <see cref="IsValid"/>.
            /// </summary>
            public string? Error { get; }

            /// <summary>
            /// Placeholder names in <b>first-appearance order</b> — that order fixes pin order, so it is
            /// load-bearing for positional link binding. A name repeated in the format yields exactly
            /// <b>one</b> entry (one pin, used at several sites).
            /// </summary>
            public IReadOnlyList<string> Names { get; }
        }

        private static readonly string[] NoNames = new string[0];

        /// <summary>
        /// Parses <paramref name="format"/> into its placeholder names.
        ///
        /// <para>Grammar: <c>{Name}</c> where <c>Name</c> is letters/digits/underscore and does not start
        /// with a digit; <c>{{</c> and <c>}}</c> are literal braces. Anything else — an unclosed
        /// <c>{</c>, an empty <c>{}</c>, an invalid name, or a stray <c>}</c> — is <b>malformed</b>.</para>
        ///
        /// <para>⚠ A malformed format is never silently ignored. Returning a valid-looking empty pin set
        /// would be trap #5's shape: the node would compile and simply print the wrong thing.</para>
        /// </summary>
        public static ParseResult Parse(string? format)
        {
            if (string.IsNullOrEmpty(format))
                return new ParseResult(true, null, NoNames);

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string text = format!;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '{')
                {
                    // "{{" is an escaped literal brace.
                    if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }

                    int close = text.IndexOf('}', i + 1);
                    if (close < 0)
                        return Invalid("unclosed '{' — write '{{' for a literal brace");

                    string name = text.Substring(i + 1, close - i - 1);
                    if (name.Length == 0)
                        return Invalid("empty placeholder '{}' — every placeholder needs a name");
                    if (!IsValidName(name))
                        return Invalid(
                            "invalid placeholder name '" + name
                            + "' — use letters, digits and underscore, not starting with a digit");

                    if (seen.Add(name))
                        names.Add(name);

                    i = close;
                    continue;
                }

                if (c == '}')
                {
                    // "}}" is an escaped literal brace; a lone '}' is malformed.
                    if (i + 1 < text.Length && text[i + 1] == '}') { i++; continue; }
                    return Invalid("unmatched '}' — write '}}' for a literal brace");
                }
            }

            return new ParseResult(true, null, names);
        }

        /// <summary>
        /// Rewrites <paramref name="format"/> into the body of a <b>C# interpolated string</b>, replacing
        /// each <c>{Name}</c> with the expression supplied for that name.
        ///
        /// <para>
        /// ⭐ Emitting a real interpolated string — rather than a runtime <c>string.Format</c> call — is
        /// what makes the zero-allocation path possible: the result can be written straight into a
        /// <c>stackalloc</c> buffer via <c>MemoryExtensions.TryWrite</c>. ⚖️ The user ruled directly:
        /// <i>"favor zero alloc path, it is always better"</i>.
        /// </para>
        ///
        /// <para>
        /// Literal braces stay escaped as <c>{{</c>/<c>}}</c>, which is what an interpolated string also
        /// requires, so escapes pass through untouched. Any placeholder with no expression supplied is
        /// emitted as <c>default</c>, which cannot happen for a well-formed node because the pin set is
        /// derived from these very names.
        /// </para>
        /// </summary>
        public static string ToInterpolatedBody(
            string? format, IReadOnlyDictionary<string, string> expressionByName)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            if (expressionByName == null) throw new ArgumentNullException(nameof(expressionByName));

            string text = format!;
            var sb = new StringBuilder(text.Length + 16);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{') { sb.Append("{{"); i++; continue; }

                    int close = text.IndexOf('}', i + 1);
                    if (close < 0) { sb.Append(c); continue; }   // malformed: Stage 2 already reported it

                    string name = text.Substring(i + 1, close - i - 1);
                    string? expr;
                    if (!expressionByName.TryGetValue(name, out expr) || expr is null)
                        expr = "default";

                    sb.Append('{').Append(expr).Append('}');
                    i = close;
                    continue;
                }

                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
                {
                    sb.Append("}}");
                    i++;
                    continue;
                }

                // A double-quote or backslash inside a literal segment must survive into C# source.
                if (c == '"') { sb.Append("\\\""); continue; }
                if (c == '\\') { sb.Append("\\\\"); continue; }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static ParseResult Invalid(string reason)
            => new ParseResult(false, reason, NoNames);

        private static bool IsValidName(string name)
        {
            if (name.Length == 0) return false;
            if (char.IsDigit(name[0])) return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }
    }
}
