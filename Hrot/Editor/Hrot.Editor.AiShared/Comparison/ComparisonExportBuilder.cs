using System.Text;

namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Assembles the full comparison export text from two sanitized asset versions plus
/// a fixed instruction block for the LLM.
/// See design section 4.
/// </summary>
public sealed class ComparisonExportBuilder
{
    private const string Separator = "================================================================================\n";

    // Instruction block per design section 4.2. Fixed text emitted on every export.
    private const string InstructionBlock =
        "You are comparing two versions of a visually-authored AI behavior asset for the\n" +
        "Hrot game engine. Below this instruction block, you will find Version A (the\n" +
        "older revision) and Version B (the newer revision) separated by clearly-labeled\n" +
        "section headers.\n" +
        "\n" +
        "Both versions have been sanitized: presentation noise (canvas positions,\n" +
        "pan/zoom state, sub-window layouts, file headers' file-system timestamps) has\n" +
        "been stripped. The remaining content is purely semantic -- node topology,\n" +
        "parameter values, action references, blackboard variables, comments. Designers'\n" +
        "comments have been hoisted inline next to the code they describe.\n" +
        "\n" +
        "The asset's kind is one of: BTree (fluent C# builder code), HSM (fluent C#\n" +
        "builder code), Blackboard DTO (C# struct definitions), or Blueprint (JSON).\n" +
        "The METADATA block before each version states the kind.\n" +
        "\n" +
        "Each node (or state, transition, region, variable) carries a stable identifier:\n" +
        "visualId for BTree/HSM nodes, stableId for HSM states/regions, structuralPath\n" +
        "or field name for blackboard fields, Id for Blueprint nodes/pins/links. These\n" +
        "identifiers are preserved across versions for correlation: if visualId\n" +
        "\"a3f2-...-9c01\" appears in both versions, it is the same node modified, not\n" +
        "two different nodes.\n" +
        "\n" +
        "Your task: identify the semantic differences between Version A and Version B,\n" +
        "focusing on what a human reviewer would care about: behavior changes, new\n" +
        "features, removed features, parameter tuning, blackboard schema changes, and\n" +
        "shifts in intent. Ignore identifier-only differences (an identifier that\n" +
        "appears only in one version means a node was added or removed, not that the\n" +
        "identifier itself is a change).\n" +
        "\n" +
        "Produce TWO outputs, in the order below, separated exactly as shown:\n" +
        "\n" +
        "----- HUMAN SUMMARY -----\n" +
        "A 2-6 paragraph prose summary intended for a human reviewer. Lead with the\n" +
        "most important change. Mention behavior shifts before tuning, and tuning\n" +
        "before cosmetics. Be specific (name the affected node by its action or role,\n" +
        "not just its identifier).\n" +
        "\n" +
        "----- STRUCTURED CHANGES (JSON) -----\n" +
        "A single JSON object matching exactly this schema:\n" +
        "\n" +
        "{\n" +
        "  \"summary\": \"<one-sentence top-level description of the change set>\",\n" +
        "  \"changes\": [\n" +
        "    {\n" +
        "      \"kind\": \"<one of: node_added, node_removed, node_modified,\n" +
        "                       variable_added, variable_removed, variable_renamed,\n" +
        "                       variable_retyped, connection_changed, comment_changed,\n" +
        "                       intent_shift>\",\n" +
        "      \"elementId\": \"<the visualId/stableId/Id/fieldName of the affected\n" +
        "                     element; null for changes not tied to a specific\n" +
        "                     element such as overall intent_shift on a subgraph>\",\n" +
        "      \"elementDescription\": \"<human-readable description of which element,\n" +
        "                              e.g., 'Wait node in main combat sequence' or\n" +
        "                              'AmmoCount variable'>\",\n" +
        "      \"field\": \"<for node_modified or variable_retyped, the specific field\n" +
        "                 that changed, e.g., 'duration' or 'type'; null otherwise>\",\n" +
        "      \"oldValue\": \"<for changes with a before/after, the prior value as a\n" +
        "                    short string; null otherwise>\",\n" +
        "      \"newValue\": \"<the new value as a short string; null otherwise>\",\n" +
        "      \"severity\": \"<one of: cosmetic, tuning, feature, removal, behavior>\",\n" +
        "      \"description\": \"<1-3 sentence explanation of the change and its\n" +
        "                       likely impact>\"\n" +
        "    }\n" +
        "  ]\n" +
        "}\n" +
        "\n" +
        "Output JSON only in the STRUCTURED CHANGES section. No prose, no markdown\n" +
        "fences, no surrounding text. The JSON must parse with a standard JSON parser.\n" +
        "\n" +
        "Limit total changes to 20 entries. If there are more, prioritize: behavior\n" +
        "shifts first, then features added or removed, then significant tuning, then\n" +
        "cosmetic edits.\n" +
        "\n" +
        "For severity levels:\n" +
        "  - cosmetic: rename, comment edit, reorder without semantic effect\n" +
        "  - tuning: parameter value change (timing, thresholds, counts)\n" +
        "  - feature: net-new functionality added\n" +
        "  - removal: functionality removed\n" +
        "  - behavior: a change that shifts the asset's overall behavior, even if\n" +
        "              the mechanical edits are small\n" +
        "\n" +
        "For \"intent_shift\" kind: use when a subgraph's overall purpose has shifted\n" +
        "even if individual node edits look small. Set elementId to the subgraph's\n" +
        "root node (the composite or state that bounds the affected region).\n" +
        "\n" +
        "Begin your response now with the HUMAN SUMMARY section.";

    /// <summary>
    /// Assembles a comparison export from two sanitized versions of the same asset.
    /// Calls <paramref name="sanitizer"/> for both versions and assembles the full
    /// LLM-ready export text per design section 4.
    /// </summary>
    /// <param name="sanitizer">The sanitizer used for both versions.</param>
    /// <param name="versionA">Export request for version A (the older/base version).</param>
    /// <param name="versionB">Export request for version B (the newer/target version).</param>
    /// <returns>The assembled comparison text with normalized line endings.</returns>
    public string Build(
        IAssetComparisonSanitizer sanitizer,
        AssetExportRequest versionA,
        AssetExportRequest versionB)
    {
        var resultA = sanitizer.Sanitize(versionA);
        var resultB = sanitizer.Sanitize(versionB);

        var sb = new StringBuilder();

        // Instruction block (section 4.2)
        sb.Append(InstructionBlock);
        sb.Append('\n');
        sb.Append('\n');

        // VERSION A (section 4.3 + 4.4)
        sb.Append(Separator);
        sb.Append("VERSION A (OLD)\n");
        sb.Append(Separator);
        AppendMetadata(sb, resultA.Metadata);
        sb.Append('\n');
        sb.Append("--- COMPANION FILES ---\n");
        AppendContent(sb, resultA);

        sb.Append('\n');

        // VERSION B
        sb.Append(Separator);
        sb.Append("VERSION B (NEW)\n");
        sb.Append(Separator);
        AppendMetadata(sb, resultB.Metadata);
        sb.Append('\n');
        sb.Append("--- COMPANION FILES ---\n");
        AppendContent(sb, resultB);

        sb.Append('\n');

        // Footer
        sb.Append(Separator);
        sb.Append("END OF COMPARISON INPUT\n");
        sb.Append(Separator);

        // Normalize all line endings to \n (section 4.6)
        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendMetadata(StringBuilder sb, AssetMetadataBlock meta)
    {
        sb.Append($"ASSET NAME:       {meta.AssetName}\n");
        sb.Append($"ASSET KIND:       {meta.Kind}\n");
        sb.Append($"ASSET ID:         {meta.AssetId:D}\n");
        sb.Append($"SOURCE PATH:      {meta.SourceFilePath}\n");

        var ts = meta.LastModifiedTimestamp.HasValue
            ? meta.LastModifiedTimestamp.Value.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
            : "(unknown)";
        sb.Append($"LAST MODIFIED:    {ts}\n");

        if (meta.CompanionFiles.Count > 0)
        {
            for (var i = 0; i < meta.CompanionFiles.Count; i++)
            {
                var path = meta.CompanionFiles[i];
                var present = File.Exists(path) ? "present" : "not present";
                var name = Path.GetFileName(path);
                if (i == 0)
                    sb.Append($"COMPANION FILES:  {name} ({present})\n");
                else
                    sb.Append($"                  {name} ({present})\n");
            }
        }
    }

    private static void AppendContent(StringBuilder sb, SanitizationResult result)
    {
        // Migration notice before file content (section 4.3 / design section 3.5)
        if (result.Metadata.MigrationNotice != null)
            sb.Append($"// MIGRATION NOTICE: {result.Metadata.MigrationNotice}\n");

        // File header + sanitized text (section 4.4)
        var fileName = Path.GetFileName(result.Metadata.SourceFilePath);
        sb.Append($"// === FILE: {fileName} ===\n");
        sb.Append(result.SanitizedText);
        if (result.SanitizedText.Length > 0 && result.SanitizedText[^1] != '\n')
            sb.Append('\n');
    }
}
