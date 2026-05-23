namespace Hrot.Editor.AiShared.Emit;

/// <summary>
/// Base class for per-asset emitters. Subclasses call helper methods to build
/// the output string; the base class handles using-ordering, marker, and file policy.
/// </summary>
public abstract class FluentCSharpEmitterBase
{
    /// <summary>
    /// Marker comment placed at the top of every editor-generated file.
    /// </summary>
    public const string EditorGeneratedMarker =
        "// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten by the AI editor on next save.";

    /// <summary>Produces the complete .cs file content for the given asset, deterministically.</summary>
    protected abstract string EmitCore(IEditableAsset asset);

    /// <summary>
    /// Sorts using directives: System.* first (alphabetical), then rest (alphabetical),
    /// separated by a blank line (represented as an empty string). If only one group
    /// is present, no blank line is added.
    /// </summary>
    public static IReadOnlyList<string> SortUsings(IEnumerable<string> namespaces)
    {
        var all = namespaces.ToList();
        var system = all
            .Where(n => n == "System" || n.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var other = all
            .Where(n => n != "System" && !n.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (system.Count == 0)
            return other;
        if (other.Count == 0)
            return system;

        var result = new List<string>(system.Count + 1 + other.Count);
        result.AddRange(system);
        result.Add(string.Empty); // blank-line separator
        result.AddRange(other);
        return result;
    }

    /// <summary>Builds the marker header lines for a generated file.</summary>
    public static string BuildHeader(Guid assetId)
    {
        return EditorGeneratedMarker + Environment.NewLine +
               "// AssetId: " + assetId.ToString("D") + Environment.NewLine;
    }

    /// <summary>
    /// Writes content to filePath atomically (*.tmp then File.Move).
    /// Returns true if the file was written, false if content was identical to existing.
    /// </summary>
    public static bool WriteAtomic(string filePath, string content)
    {
        if (File.Exists(filePath))
        {
            string existing = File.ReadAllText(filePath);
            if (existing == content) return false;
        }

        string tmpPath = filePath + ".tmp";
        File.WriteAllText(tmpPath, content);
        File.Move(tmpPath, filePath, overwrite: true);
        return true;
    }
}
