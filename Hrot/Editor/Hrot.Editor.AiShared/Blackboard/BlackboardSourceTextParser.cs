using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Result of parsing a single field from a companion source file.
/// </summary>
/// <param name="Name">The field name (identifier).</param>
/// <param name="LeadingComment">
/// The leading <c>///</c> doc-comment block verbatim (including the <c>///</c> prefix and
/// newlines), or null if the field has no immediately-preceding comment block.
/// </param>
/// <param name="VerbatimSpan">
/// Char offsets (Start, Length) into the source string.  The span covers from the start of the
/// first comment/attribute line (or the declaration line itself when there is no comment or
/// attribute) through the end of the line that contains the trailing semicolon inclusive.
/// <c>sourceText.Substring(span.Start, span.Length)</c> reproduces the captured text exactly.
/// </param>
/// <param name="IsSingleLineDeclaration">
/// True when the field declaration itself (type + name + optional initializer + semicolon) fits
/// on a single line.  Attribute lines and comment lines above the declaration do NOT affect this
/// flag -- only the declaration line is tested.
/// </param>
/// <param name="HasAttribute">
/// True when the verbatim span includes one or more attribute lines (lines beginning with
/// <c>[</c> after stripping leading whitespace) between the comment block and the declaration.
/// </param>
/// <param name="HasInitializer">
/// True when the declaration line contains <c>=</c>, indicating a field initializer.
/// </param>
public record FieldParseResult(
    string Name,
    string? LeadingComment,
    (int Start, int Length) VerbatimSpan,
    bool IsSingleLineDeclaration,
    bool HasAttribute,
    bool HasInitializer
);

/// <summary>Result of locating the target struct in the source text.</summary>
/// <param name="Found">True when the struct was located successfully.</param>
/// <param name="Reason">
/// Null when <c>Found == true</c>; a human-readable explanation otherwise.
/// </param>
public record StructLocateResult(bool Found, string? Reason);

/// <summary>
/// Combined result of <see cref="BlackboardSourceTextParser.Parse"/>.
/// </summary>
/// <param name="LocateResult">Outcome of finding the struct declaration.</param>
/// <param name="Fields">
/// Parsed fields in declaration order; empty when <c>LocateResult.Found == false</c>.
/// </param>
public record SourceParseResult(
    StructLocateResult LocateResult,
    IReadOnlyList<FieldParseResult> Fields
);

/// <summary>
/// Line-by-line parser for blackboard companion <c>.cs</c> files.
/// Does NOT use Roslyn -- the file format is constrained enough that simple scanning suffices.
/// All offsets in <see cref="FieldParseResult.VerbatimSpan"/> are char (UTF-16 code unit) offsets.
/// </summary>
public static class BlackboardSourceTextParser
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the companion source text and returns all fields declared inside the struct
    /// named <paramref name="structName"/>.
    /// </summary>
    /// <param name="sourceText">Full text of the <c>.cs</c> file (UTF-16 char offsets).</param>
    /// <param name="structName">Simple (unqualified) name of the target struct.</param>
    public static SourceParseResult Parse(string sourceText, string structName)
    {
        if (sourceText is null) throw new ArgumentNullException(nameof(sourceText));
        if (structName is null) throw new ArgumentNullException(nameof(structName));

        var (lines, offsets) = SplitLines(sourceText);

        int structBodyStart = FindStructBody(lines, offsets, structName, out var locateResult);
        if (structBodyStart < 0)
            return new SourceParseResult(locateResult, Array.Empty<FieldParseResult>());

        var fields = CollectFields(sourceText, lines, offsets, structBodyStart);

        return new SourceParseResult(new StructLocateResult(true, null), fields);
    }

    // -------------------------------------------------------------------------
    // Line splitting
    // -------------------------------------------------------------------------

    // Returns parallel arrays: raw line strings and their starting char offset.
    // Each line string includes the newline character(s) if present.
    private static (string[] Lines, int[] Offsets) SplitLines(string text)
    {
        var lineList   = new List<string>();
        var offsetList = new List<int>();

        int pos = 0;
        while (pos <= text.Length)
        {
            offsetList.Add(pos);
            int nl = text.IndexOf('\n', pos);
            if (nl < 0)
            {
                lineList.Add(text.Substring(pos));
                break;
            }
            lineList.Add(text.Substring(pos, nl - pos + 1));
            pos = nl + 1;
        }

        return (lineList.ToArray(), offsetList.ToArray());
    }

    // -------------------------------------------------------------------------
    // Struct location
    // -------------------------------------------------------------------------

    // Finds the index of the first line INSIDE the struct body (after the opening `{`).
    // Returns -1 and populates locateResult on failure.
    private static int FindStructBody(
        string[] lines,
        int[] offsets,
        string structName,
        out StructLocateResult locateResult)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!ContainsStructDeclaration(trimmed, structName)) continue;

            // Opening `{` on the same line.
            if (trimmed.EndsWith("{", StringComparison.Ordinal))
            {
                locateResult = new StructLocateResult(true, null);
                return i + 1;
            }

            // Look ahead for the opening `{`.
            for (int j = i + 1; j < lines.Length; j++)
            {
                var jt = lines[j].Trim();
                if (jt == "{")
                {
                    locateResult = new StructLocateResult(true, null);
                    return j + 1;
                }
                if (!string.IsNullOrWhiteSpace(jt))
                    break; // unexpected content before `{`
            }
        }

        locateResult = new StructLocateResult(false,
            $"Struct '{structName}' not found in source text.");
        return -1;
    }

    private static bool ContainsStructDeclaration(string trimmedLine, string structName)
    {
        int idx = trimmedLine.IndexOf("struct ", StringComparison.Ordinal);
        if (idx < 0) return false;

        int nameStart = idx + "struct ".Length;
        if (nameStart >= trimmedLine.Length) return false;

        if (!trimmedLine.AsSpan(nameStart).StartsWith(structName.AsSpan(),
            StringComparison.Ordinal))
            return false;

        int afterName = nameStart + structName.Length;
        if (afterName >= trimmedLine.Length) return true; // end of line

        char ch = trimmedLine[afterName];
        return ch == ' ' || ch == '\t' || ch == '{' || ch == '<' || ch == ':';
    }

    // -------------------------------------------------------------------------
    // Field collection
    // -------------------------------------------------------------------------

    private static List<FieldParseResult> CollectFields(
        string sourceText,
        string[] lines,
        int[] offsets,
        int structBodyStart)
    {
        var results   = new List<FieldParseResult>();
        int depth     = 1; // consumed the opening `{`
        int lineCount = lines.Length;

        int  pendingBlockStart   = -1;
        var  commentLines        = new List<string>();
        bool inCommentBlock      = false;
        int  commentBlockStart   = -1;
        bool hasPendingAttribute = false;

        for (int i = structBodyStart; i < lineCount; i++)
        {
            var line     = lines[i];
            var stripped = line.TrimStart().TrimEnd('\r', '\n');

            depth += CountChar(stripped, '{') - CountChar(stripped, '}');
            if (depth <= 0)
                break;

            if (string.IsNullOrWhiteSpace(stripped))
            {
                inCommentBlock      = false;
                commentLines.Clear();
                commentBlockStart   = -1;
                hasPendingAttribute = false;
                pendingBlockStart   = -1;
                continue;
            }

            // --- Doc-comment line ---
            if (stripped.StartsWith("///", StringComparison.Ordinal))
            {
                if (!inCommentBlock)
                {
                    inCommentBlock    = true;
                    commentBlockStart = i;
                    commentLines.Clear();
                }
                commentLines.Add(line);
                // Comment block resets any pending attribute that came before.
                hasPendingAttribute = false;
                if (pendingBlockStart < 0 || pendingBlockStart > i)
                    pendingBlockStart = i;
                continue;
            }

            // --- Attribute line ---
            if (stripped.StartsWith("[", StringComparison.Ordinal))
            {
                hasPendingAttribute = true;
                inCommentBlock      = false; // attribute line ends comment continuity
                if (pendingBlockStart < 0)
                    pendingBlockStart = i;
                continue;
            }

            // --- Attempt to parse as field declaration ---
            string? fieldName = TryExtractFieldName(stripped);
            if (fieldName == null)
            {
                // Not a field line -- reset context.
                inCommentBlock      = false;
                commentLines.Clear();
                commentBlockStart   = -1;
                hasPendingAttribute = false;
                pendingBlockStart   = -1;
                continue;
            }

            bool hasInitializer = stripped.Contains('=');
            bool hasSemicolon   = stripped.Contains(';');
            bool isSingleLine;
            int  spanEndLine;

            if (hasSemicolon)
            {
                isSingleLine = true;
                spanEndLine  = i;
            }
            else
            {
                isSingleLine = false;
                spanEndLine  = i;
                for (int j = i + 1; j < lineCount; j++)
                {
                    spanEndLine = j;
                    if (lines[j].Contains(';'))
                        break;
                }
            }

            int spanStartLine = pendingBlockStart >= 0 ? pendingBlockStart : i;
            int spanStart     = offsets[spanStartLine];
            int spanEnd       = spanEndLine + 1 < offsets.Length
                ? offsets[spanEndLine + 1]
                : sourceText.Length;
            int spanLength    = spanEnd - spanStart;

            string? leadingComment = commentLines.Count > 0
                ? string.Join(string.Empty, commentLines)
                : null;

            results.Add(new FieldParseResult(
                Name:                    fieldName,
                LeadingComment:          leadingComment,
                VerbatimSpan:            (spanStart, spanLength),
                IsSingleLineDeclaration: isSingleLine,
                HasAttribute:            hasPendingAttribute,
                HasInitializer:          hasInitializer));

            inCommentBlock      = false;
            commentLines.Clear();
            commentBlockStart   = -1;
            hasPendingAttribute = false;
            pendingBlockStart   = -1;

            i = spanEndLine;
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? TryExtractFieldName(string stripped)
    {
        if (stripped.StartsWith("///", StringComparison.Ordinal)) return null;
        if (stripped.StartsWith("[",   StringComparison.Ordinal)) return null;
        if (stripped.StartsWith("{",   StringComparison.Ordinal)) return null;
        if (stripped.StartsWith("}",   StringComparison.Ordinal)) return null;
        if (stripped.StartsWith("//",  StringComparison.Ordinal)) return null;
        if (stripped.StartsWith("/*",  StringComparison.Ordinal)) return null;
        if (stripped.Contains('(')) return null; // skip method declarations

        // Strip trailing `;` and whitespace then strip optional initializer.
        var work = stripped.TrimEnd(';', ' ', '\t', '\r');
        int eqIdx = work.IndexOf('=');
        if (eqIdx >= 0)
            work = work.Substring(0, eqIdx).TrimEnd();

        var tokens = work.Split(new[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return null;

        var name = tokens[tokens.Length - 1];
        return IsValidIdentifier(name) ? name : null;
    }

    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        foreach (char c in s)
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        return true;
    }

    private static int CountChar(string s, char ch)
    {
        int count = 0;
        foreach (char c in s)
            if (c == ch) count++;
        return count;
    }
}
