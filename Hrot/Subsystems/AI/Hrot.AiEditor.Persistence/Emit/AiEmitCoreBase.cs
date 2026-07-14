using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Shared utilities for the emit core: marker constant, header builder,
/// using-sort, and WriteAtomic.
/// Design §6.1: lives in the netstandard2.0 emit core — no editor/net8/ImGui reference.
/// Mirrors (and replaces, as the authoritative source) FluentCSharpEmitterBase in
/// Hrot.Editor.AiShared.Emit. The editor base now delegates to this class.
/// </summary>
public static class AiEmitCoreBase
{
    /// <summary>
    /// Marker comment placed at the top of every editor-generated file.
    /// </summary>
    public const string EditorGeneratedMarker =
        "// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten by the AI editor on next save.";

    /// <summary>
    /// Standard Brain-tier blackboard type used when an asset has no
    /// <c>BlackboardTypeName</c> set (e.g. a freshly-created empty asset).
    /// Matches the type every real hand-authored tree and the golden test corpus use.
    /// </summary>
    public const string DefaultBlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard";

    /// <summary>
    /// Standard Brain-tier context type used when an asset has no
    /// <c>ContextTypeName</c> set (e.g. a freshly-created empty asset).
    /// Matches the type every real hand-authored tree and the golden test corpus use.
    /// </summary>
    public const string DefaultContextTypeName = "Fdp.Toolkit.Behavior.BTreeContext";

    /// <summary>
    /// Resolves the effective blackboard type name to emit: <paramref name="typeName"/> when
    /// non-empty/non-whitespace, otherwise <see cref="DefaultBlackboardTypeName"/>.
    /// Single source of truth so every emit read-site (generic args, using collectors,
    /// bridge registrar) defaults consistently — see BTreeEmitCore / BTreeBridgeEmitCore.
    /// </summary>
    public static string EffectiveBlackboardTypeName(string typeName) =>
        string.IsNullOrWhiteSpace(typeName) ? DefaultBlackboardTypeName : typeName;

    /// <summary>
    /// Resolves the effective context type name to emit: <paramref name="typeName"/> when
    /// non-empty/non-whitespace, otherwise <see cref="DefaultContextTypeName"/>.
    /// Single source of truth — see <see cref="EffectiveBlackboardTypeName"/>.
    /// </summary>
    public static string EffectiveContextTypeName(string typeName) =>
        string.IsNullOrWhiteSpace(typeName) ? DefaultContextTypeName : typeName;

    /// <summary>Builds the marker header lines for a generated file.</summary>
    public static string BuildHeader(Guid assetId)
    {
        return EditorGeneratedMarker + Environment.NewLine +
               "// AssetId: " + assetId.ToString("D") + Environment.NewLine;
    }

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
        // File.Move(src, dest, overwrite) requires .NET Standard 2.1+.
        // Delete the destination first, then move (same semantics on all TFMs).
        if (File.Exists(filePath))
            File.Delete(filePath);
        File.Move(tmpPath, filePath);
        return true;
    }
}
