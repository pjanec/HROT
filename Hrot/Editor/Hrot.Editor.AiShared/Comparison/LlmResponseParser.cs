using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Parses the LLM's text response into a <see cref="ComparisonResponse"/>.
/// Implements the robustness rules from design section 5.3: markdown fence stripping,
/// fallback section detection, truncated-JSON recovery, and unknown-value normalization.
/// Never throws.
/// </summary>
public static class LlmResponseParser
{
    private const string HumanMarker = "----- HUMAN SUMMARY -----";
    private const string JsonMarker = "----- STRUCTURED CHANGES (JSON) -----";
    private const string TruncationWarning = ComparisonErrorMessages.TruncatedResponse;

    private static readonly HashSet<string> KnownKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_added", "node_removed", "node_modified",
        "variable_added", "variable_removed", "variable_renamed",
        "variable_retyped", "connection_changed", "comment_changed",
        "intent_shift",
    };

    private static readonly HashSet<string> KnownSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "cosmetic", "tuning", "feature", "removal", "behavior",
    };

    /// <summary>
    /// Parses an LLM response text into a <see cref="ComparisonResponse"/>.
    /// Returns a truncation-warning response when unrecoverable. Never throws.
    /// </summary>
    public static ComparisonResponse Parse(string responseText)
    {
        try
        {
            // Step 1: Strip markdown fences from the entire text.
            var text = StripMarkdownFences(responseText);

            // Step 2: Locate section boundaries.
            var humanIdx = text.IndexOf(HumanMarker, StringComparison.Ordinal);
            var jsonIdx = text.IndexOf(JsonMarker, StringComparison.Ordinal);

            string? humanSummary;
            string jsonSection;

            if (humanIdx >= 0 && jsonIdx >= 0 && jsonIdx > humanIdx)
            {
                var summaryStart = humanIdx + HumanMarker.Length;
                humanSummary = text[summaryStart..jsonIdx].Trim();
                jsonSection = text[(jsonIdx + JsonMarker.Length)..].Trim();
            }
            else
            {
                // Fallback: locate first '{' as JSON boundary.
                var firstBrace = text.IndexOf('{');
                if (firstBrace < 0)
                    return MakeTruncationResponse();

                humanSummary = firstBrace > 0 ? text[..firstBrace].Trim() : null;
                if (string.IsNullOrEmpty(humanSummary))
                    humanSummary = null;

                var lastBrace = text.LastIndexOf('}');
                jsonSection = lastBrace >= firstBrace
                    ? text[firstBrace..(lastBrace + 1)]
                    : text[firstBrace..];
            }

            if (string.IsNullOrWhiteSpace(jsonSection))
                return MakeTruncationResponse();

            // Step 3: Try parsing the JSON as-is.
            var warnings = new List<string>();
            var result = TryParseJson(jsonSection, humanSummary, warnings);
            if (result != null)
                return result;

            // Step 4: Recovery -- find last complete '}' and close the array/object.
            result = TryRecoverJson(jsonSection, humanSummary);
            if (result != null)
                return result;

            // Step 5: Unrecoverable -- return truncation warning.
            return MakeTruncationResponse();
        }
        catch
        {
            return MakeTruncationResponse();
        }
    }

    private static ComparisonResponse? TryParseJson(
        string jsonSection, string? humanSummary, List<string> warnings)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonSection);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var sumProp)
                ? (sumProp.GetString() ?? "")
                : "";

            var changes = new List<ComparisonChange>();

            if (root.TryGetProperty("changes", out var changesProp)
                && changesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var changeProp in changesProp.EnumerateArray())
                {
                    var change = ParseChange(changeProp, warnings);
                    if (change != null)
                        changes.Add(change);
                }
            }

            return new ComparisonResponse(humanSummary, summary, changes, warnings.ToList());
        }
        catch
        {
            return null;
        }
    }

    private static ComparisonResponse? TryRecoverJson(string jsonSection, string? humanSummary)
    {
        var lastBrace = jsonSection.LastIndexOf('}');
        if (lastBrace < 0)
            return null;

        var candidate = jsonSection[..(lastBrace + 1)] + "\n]\n}";
        var recoveryWarnings = new List<string>();
        var result = TryParseJson(candidate, humanSummary, recoveryWarnings);
        if (result == null)
            return null;

        // Prepend truncation warning then any per-change warnings from the recovered parse.
        var allWarnings = new List<string> { TruncationWarning };
        allWarnings.AddRange(recoveryWarnings);
        return result with { Warnings = allWarnings };
    }

    private static ComparisonChange? ParseChange(JsonElement elem, List<string> warnings)
    {
        var kind = GetStringOrEmpty(elem, "kind");
        var severity = GetStringOrEmpty(elem, "severity");

        string? description = null;
        if (elem.TryGetProperty("description", out var descProp)
            && descProp.ValueKind != JsonValueKind.Null)
        {
            description = descProp.GetString();
        }

        if (description == null)
        {
            warnings.Add("LLM change entry missing required 'description' field -- using empty string.");
            description = "";
        }

        // Normalize unknown kind.
        if (!KnownKinds.Contains(kind))
        {
            warnings.Add($"LLM produced unknown kind '{kind}' -- treated as 'node_modified'");
            kind = "node_modified";
        }

        // Normalize unknown severity.
        if (!KnownSeverities.Contains(severity))
        {
            warnings.Add($"LLM produced unknown severity '{severity}' -- treated as 'tuning'");
            severity = "tuning";
        }

        string? elementId = null;
        if (elem.TryGetProperty("elementId", out var eidProp)
            && eidProp.ValueKind != JsonValueKind.Null)
        {
            elementId = eidProp.GetString();
        }

        var elementDescription = GetStringOrEmpty(elem, "elementDescription");
        var field = GetNullableString(elem, "field");
        var oldValue = GetNullableString(elem, "oldValue");
        var newValue = GetNullableString(elem, "newValue");

        return new ComparisonChange(kind, elementId, elementDescription, field, oldValue, newValue, severity, description);
    }

    private static string GetStringOrEmpty(JsonElement elem, string propertyName)
    {
        return elem.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? (prop.GetString() ?? "")
            : "";
    }

    private static string? GetNullableString(JsonElement elem, string propertyName)
    {
        if (!elem.TryGetProperty(propertyName, out var prop))
            return null;
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetString();
    }

    private static string StripMarkdownFences(string text)
    {
        // Remove ```json ... ``` or ``` ... ``` code blocks (replaces the fence with inner content).
        return Regex.Replace(
            text,
            @"```(?:json)?\r?\n([\s\S]*?)\r?\n\s*```",
            m => m.Groups[1].Value);
    }

    private static ComparisonResponse MakeTruncationResponse() =>
        new ComparisonResponse(
            null,
            "",
            Array.Empty<ComparisonChange>(),
            new[] { TruncationWarning });
}
