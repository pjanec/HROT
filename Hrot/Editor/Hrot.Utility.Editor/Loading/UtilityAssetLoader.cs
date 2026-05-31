using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Loading;

/// <summary>
/// Result returned by <see cref="UtilityAssetLoader.Load"/>.
/// </summary>
public sealed record UtilityLoadResult(
    UtilityDecisionAsset Asset,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a .cs file produced by the editor (or a hand-authored partial-manifest) and
/// returns a <see cref="UtilityDecisionAsset"/> with extracted metadata.
/// Text-based extraction only; no Roslyn, no assembly loading.
/// </summary>
public static class UtilityAssetLoader
{
    private const string GeneratedMarker = "HROT_EDITOR_GENERATED";

    /// <summary>
    /// Loads a <see cref="UtilityDecisionAsset"/> from <paramref name="filePath"/>.
    /// Returns a default read-only asset with a warning when the file does not exist.
    /// </summary>
    public static UtilityLoadResult Load(string filePath)
    {
        var warnings = new List<string>();
        var asset    = new UtilityDecisionAsset();

        if (!File.Exists(filePath))
        {
            asset.IsEditorOwned = false;
            warnings.Add($"File not found: {filePath}");
            return new UtilityLoadResult(asset, warnings);
        }

        string text  = File.ReadAllText(filePath)
                           .Replace("\r\n", "\n")
                           .Replace("\r",   "\n");
        string[] lines = text.Split('\n');

        // Check for the editor-generated marker in the first 5 lines.
        bool hasMarker  = false;
        int  checkCount = Math.Min(5, lines.Length);
        for (int i = 0; i < checkCount; i++)
        {
            if (lines[i].Contains(GeneratedMarker, StringComparison.Ordinal))
            {
                hasMarker = true;
                break;
            }
        }

        if (!hasMarker)
        {
            asset.IsEditorOwned = false;
            warnings.Add("File is not editor-generated; opened read-only.");
        }

        // Parse [UtilityDecision(...)] attribute fields permissively — any order.
        OptionModel? currentOption = null;
        foreach (string line in lines)
        {
            if (line.Contains("assetId:", StringComparison.Ordinal))
            {
                Guid g = ParseGuid(line);
                if (g != Guid.Empty) asset.AssetId = g;
            }
            else if (line.Contains("displayName:", StringComparison.Ordinal))
            {
                string? s = ParseString(line);
                if (s != null) asset.DisplayName = s;
            }
            else if (line.Contains("kind:", StringComparison.Ordinal) &&
                     line.Contains("DecisionKind.", StringComparison.Ordinal))
            {
                DecisionKind? k = ParseDecisionKind(line);
                if (k.HasValue) asset.DecisionKind = k.Value;
            }
            else if (line.Contains("category:", StringComparison.Ordinal))
            {
                string? s = ParseString(line);
                if (s != null) asset.Category = s;
            }
            else if (line.Contains("hysteresisBonus:", StringComparison.Ordinal))
            {
                float? f = ParseFloat(line, "hysteresisBonus");
                if (f.HasValue) asset.HysteresisBonus = f.Value;
            }
            else if (line.Contains(".Option(", StringComparison.Ordinal) &&
                     !line.Contains(".CandidateOption(", StringComparison.Ordinal))
            {
                currentOption = ParseOptionLine(line);
                if (currentOption != null) asset.Options.Add(currentOption);
            }
            else if (line.Contains(".CandidateOption(", StringComparison.Ordinal))
            {
                currentOption = ParseCandidateOptionLine(line);
                if (currentOption != null) asset.Options.Add(currentOption);
            }
            else if (line.Contains(".Consider(", StringComparison.Ordinal) && currentOption != null)
            {
                var con = ParseConsiderationLine(line);
                if (con != null) currentOption.Considerations.Add(con);
            }
        }

        asset.SourceFilePath = filePath;
        return new UtilityLoadResult(asset, warnings);
    }

    // ---- Parsing helpers -----------------------------------------------

    private static Guid ParseGuid(string line)
    {
        int start = line.IndexOf('"');
        if (start < 0) return Guid.Empty;
        int end = line.IndexOf('"', start + 1);
        if (end <= start) return Guid.Empty;
        string candidate = line.Substring(start + 1, end - start - 1);
        return Guid.TryParse(candidate, out Guid g) ? g : Guid.Empty;
    }

    private static string? ParseString(string line)
    {
        int start = line.IndexOf('"');
        if (start < 0) return null;
        int end = line.IndexOf('"', start + 1);
        if (end <= start) return null;
        return line.Substring(start + 1, end - start - 1);
    }

    private static DecisionKind? ParseDecisionKind(string line)
    {
        const string prefix = "DecisionKind.";
        int idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        int nameStart = idx + prefix.Length;
        int nameEnd   = nameStart;
        while (nameEnd < line.Length &&
               (char.IsLetterOrDigit(line[nameEnd]) || line[nameEnd] == '_'))
        {
            nameEnd++;
        }
        string name = line.Substring(nameStart, nameEnd - nameStart);
        return Enum.TryParse<DecisionKind>(name, out DecisionKind k) ? k : null;
    }

    private static float? ParseFloat(string line, string label)
    {
        string search = label + ":";
        int idx = line.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        int valueStart = idx + search.Length;
        // Skip whitespace.
        while (valueStart < line.Length && line[valueStart] == ' ')
            valueStart++;
        // Advance until 'f' or end of line.
        int valueEnd = valueStart;
        while (valueEnd < line.Length && line[valueEnd] != 'f' && line[valueEnd] != '\n')
            valueEnd++;
        string token = line.Substring(valueStart, valueEnd - valueStart).Trim();
        return float.TryParse(token, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float f) ? f : null;
    }

    // ---- Option parsing ----

    private static OptionModel? ParseOptionLine(string line)
    {
        int idx = line.IndexOf(".Option(", StringComparison.Ordinal);
        if (idx < 0) return null;
        int start  = idx + ".Option(".Length;
        int comma1 = line.IndexOf(',', start);
        if (comma1 < 0) return null;
        string idStr = line.Substring(start, comma1 - start).Trim();
        if (!ushort.TryParse(idStr, out ushort optionId)) return null;
        ScoringMode mode = ParseScoringMode(line.Substring(comma1 + 1));
        return new OptionModel { OptionId = optionId, Mode = mode };
    }

    private static OptionModel? ParseCandidateOptionLine(string line)
    {
        if (!line.Contains(".CandidateOption(", StringComparison.Ordinal)) return null;
        ScoringMode mode = ParseScoringMode(line);
        return new OptionModel { Mode = mode };
    }

    private static ScoringMode ParseScoringMode(string text)
    {
        const string prefix = "ScoringMode.";
        int idx = text.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return ScoringMode.WeightedProduct;
        int start = idx + prefix.Length;
        int end   = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            end++;
        string name = text.Substring(start, end - start);
        return Enum.TryParse<ScoringMode>(name, out ScoringMode m) ? m : ScoringMode.WeightedProduct;
    }

    // ---- Consideration parsing ----

    private static ConsiderationModel? ParseConsiderationLine(string line)
    {
        // Format: .Consider(In.InputName(contextArgs), weight, curveExpr)suffix
        int considerIdx = line.IndexOf(".Consider(", StringComparison.Ordinal);
        if (considerIdx < 0) return null;
        int afterConsider = considerIdx + ".Consider(".Length;

        // Find "In." to locate the InputName.
        int inIdx = line.IndexOf("In.", afterConsider, StringComparison.Ordinal);
        if (inIdx < 0) return null;
        int nameStart  = inIdx + 3;
        int parenStart = line.IndexOf('(', nameStart);
        if (parenStart < 0) return null;
        string inputName = line.Substring(nameStart, parenStart - nameStart);

        // Depth-balance to find the closing ')' of In.XXX(...).
        int depth = 1;
        int pos   = parenStart + 1;
        while (pos < line.Length && depth > 0)
        {
            if      (line[pos] == '(') depth++;
            else if (line[pos] == ')') depth--;
            pos++;
        }
        // pos now points one past the closing ')' of In.XXX(...).

        // Extract context from the In() args (everything between the parens).
        string inArgs  = line.Substring(parenStart + 1, (pos - 1) - (parenStart + 1));
        InputContext context = InputContext.Self;
        const string ctxPrefix = "InputContext.";
        int ctxIdx = inArgs.IndexOf(ctxPrefix, StringComparison.Ordinal);
        if (ctxIdx >= 0)
        {
            int ctxNameStart = ctxIdx + ctxPrefix.Length;
            int ctxNameEnd   = ctxNameStart;
            while (ctxNameEnd < inArgs.Length &&
                   (char.IsLetterOrDigit(inArgs[ctxNameEnd]) || inArgs[ctxNameEnd] == '_'))
                ctxNameEnd++;
            string ctxName = inArgs.Substring(ctxNameStart, ctxNameEnd - ctxNameStart);
            if (Enum.TryParse<InputContext>(ctxName, out InputContext ctx))
                context = ctx;
        }

        // Remaining text after In.XXX(...): ", weight, curveExpr)suffix"
        string rest = line.Substring(pos).TrimStart(',', ' ');
        var    args = SplitArgsAtDepthZero(rest);
        if (args.Count < 2) return null;

        // Parse weight (float literal like "0.8f").
        string weightStr = args[0].Trim().TrimEnd('f', 'F');
        if (!float.TryParse(weightStr, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float weight))
            weight = 1f;

        // Parse curve expression, stripping trailing suffix parens/semicolons.
        string curveStr = StripOuterSuffix(args[1]);
        ResponseCurveModel curve = ParseCurveExpression(curveStr);

        return new ConsiderationModel
        {
            InputName = inputName,
            Context   = context,
            Weight    = weight,
            Curve     = curve,
        };
    }

    // Splits text on commas at paren-depth 0, stopping at the first depth-0 ')'.
    private static List<string> SplitArgsAtDepthZero(string text)
    {
        var args  = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if      (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') { if (depth == 0) break; depth--; }
            else if (c == ',' && depth == 0)
            {
                args.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (start < text.Length)
            args.Add(text.Substring(start));
        return args;
    }

    // Strips trailing ')' / ';' characters that are suffix from the .Consider() call site,
    // preserving balanced parens that belong to the curve expression itself.
    private static string StripOuterSuffix(string fragment)
    {
        string s = fragment.Trim();
        // Strip trailing ';' (ends the statement for last-option last-consideration).
        while (s.Length > 0 && s[s.Length - 1] == ';')
            s = s.Substring(0, s.Length - 1).TrimEnd(' ');
        // Count excess ')' (closes > opens) and strip those from the right.
        int opens = 0, closes = 0;
        foreach (char c in s) { if (c == '(') opens++; else if (c == ')') closes++; }
        int excess = closes - opens;
        while (excess > 0 && s.Length > 0 && s[s.Length - 1] == ')')
        {
            s = s.Substring(0, s.Length - 1).TrimEnd(' ');
            excess--;
        }
        return s.TrimEnd(' ');
    }

    private static ResponseCurveModel ParseCurveExpression(string expr)
    {
        // Check for Curve.XXX presets.
        if (expr.StartsWith("Curve.", StringComparison.Ordinal))
        {
            string preset = expr.Substring(6);
            return preset switch
            {
                "Linear"           => new ResponseCurveModel { Kind = CurveKind.Linear,           M = 1f, K = 1f, B = 0f },
                "InverseLinear"    => new ResponseCurveModel { Kind = CurveKind.InverseLinear,    M = 1f, K = 1f, B = 0f },
                "Threshold"        => new ResponseCurveModel { Kind = CurveKind.Threshold,        M = 1f, K = 1f, B = 0.5f },
                "Bell"             => new ResponseCurveModel { Kind = CurveKind.Bell,             M = 1f, K = 8f, B = 1.0f },
                "Step"             => new ResponseCurveModel { Kind = CurveKind.Step,             M = 1f, K = 1f, B = 0.5f },
                "Logistic"         => new ResponseCurveModel { Kind = CurveKind.Logistic,         M = 1f, K = 1f, B = 0f },
                "Quadratic"        => new ResponseCurveModel { Kind = CurveKind.Quadratic,        M = 1f, K = 1f, B = 0f },
                "InverseQuadratic" => new ResponseCurveModel { Kind = CurveKind.InverseQuadratic, M = 1f, K = 1f, B = 0f },
                _ => new ResponseCurveModel { Kind = CurveKind.Linear },
            };
        }
        // new ResponseCurve(CurveKind.XXX, slope: X, exponent: Y, xShift: Z)
        if (expr.StartsWith("new ResponseCurve(", StringComparison.Ordinal))
        {
            CurveKind kind = CurveKind.Linear;
            float m = 1f, k = 1f, b = 0f;
            const string kindPrefix = "CurveKind.";
            int kindIdx = expr.IndexOf(kindPrefix, StringComparison.Ordinal);
            if (kindIdx >= 0)
            {
                int ns = kindIdx + kindPrefix.Length;
                int ne = ns;
                while (ne < expr.Length && (char.IsLetterOrDigit(expr[ne]) || expr[ne] == '_'))
                    ne++;
                Enum.TryParse<CurveKind>(expr.Substring(ns, ne - ns), out kind);
            }
            float? sv = ParseLabeledCurveFloat(expr, "slope");
            float? ev = ParseLabeledCurveFloat(expr, "exponent");
            float? xv = ParseLabeledCurveFloat(expr, "xShift");
            if (sv.HasValue) m = sv.Value;
            if (ev.HasValue) k = ev.Value;
            if (xv.HasValue) b = xv.Value;
            return new ResponseCurveModel { Kind = kind, M = m, K = k, B = b };
        }
        return new ResponseCurveModel { Kind = CurveKind.Linear };
    }

    private static float? ParseLabeledCurveFloat(string text, string label)
    {
        string search = label + ":";
        int idx = text.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        int vs = idx + search.Length;
        while (vs < text.Length && text[vs] == ' ') vs++;
        int ve = vs;
        while (ve < text.Length && text[ve] != 'f' && text[ve] != ',' && text[ve] != ')')
            ve++;
        string tok = text.Substring(vs, ve - vs).Trim();
        return float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : null;
    }
}
