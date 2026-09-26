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
    /// ⛔⛔⛔ <b>THIS NAMES A TYPE THAT NO LONGER EXISTS, AND THAT IS CORRECT. It is an IDENTITY
    /// TOKEN, not a resolvable type.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.13.
    ///
    /// <para>🔴 <c>P4</c> <b>DELETED</b> <c>Fdp.Toolkit.Behavior.Components.BrainBlackboard</c>
    /// (§30). 📐 Measured <c>2026-09-23</c>: <c>grep 'struct BrainBlackboard'</c> over <c>FDP</c> +
    /// <c>Hrot</c> returns nothing, while <b>26</b> shipped <c>*.btree.json</c> still carry it as
    /// their <c>BlackboardTypeName</c>. ⚠ A grep for a deleted type returning 26 live hits reads
    /// like a bug — it is not, and this note exists so the next reader does not spend an hour on
    /// it.</para>
    ///
    /// <para>⭐⭐ <b>Why nothing breaks.</b> Since <c>P4</c>-② the dispatch type is <c>byte</c> for
    /// every tree (<c>BTreeBridgeEmitCore:421</c> — <i>"a slot base is bytes; what the asset calls
    /// its layout struct is irrelevant to dispatch"</i>), and since <c>CE-337</c> retired the
    /// orchestrator arms <b>no production site emits this string as a C# TYPE at all</b>. What
    /// survives are the two IDENTITY uses, and both are persisted:</para>
    /// <list type="number">
    ///   <item><description>it mangles into the generated params-layout struct name
    ///   <c>{Asset}_{SanitizedType}</c>;</description></item>
    ///   <item><description>it feeds <c>SubtreeSyncIdentity.Derive</c>, which MATCHES SUB-TREES by
    ///   (name, dto type, dto ns).</description></item>
    /// </list>
    ///
    /// <para>⛔⛔ <b>SO DO NOT "FIX" THE NAME.</b> 📐 <c>BTreeBridgeEmitCore:415-420</c> measured the
    /// cost: retargeting the asset field would rename <b>11 generated structs across 44 files</b>
    /// AND silently break sub-tree matching. ⇒ this is a rename of a persisted key, not a typo.</para>
    ///
    /// <para>⚠ <b>Its remaining live use is namespace collection</b> — <c>AddNamespaceFromTypeName</c>
    /// in <c>BTreeEmitCore</c> / <c>BTreeBridgeEmitCore</c> — which yields a <c>using</c> for a
    /// namespace that still exists. Harmless, and not worth a persisted-key migration to remove.</para>
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
