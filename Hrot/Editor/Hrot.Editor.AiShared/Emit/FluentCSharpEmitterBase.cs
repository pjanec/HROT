using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.Editor.AiShared.Emit;

/// <summary>
/// Base class for per-asset emitters in the net8 editor.
/// The deterministic emission logic (marker, header, using-sort, WriteAtomic) has been
/// extracted into <see cref="AiEmitCoreBase"/> (netstandard2.0). This class now
/// delegates to that core so both the editor and the Phase-2 Roslyn generator share
/// a single implementation.
/// Design §6.1: thin adapter; no duplication.
/// </summary>
public abstract class FluentCSharpEmitterBase
{
    /// <summary>
    /// Marker comment placed at the top of every editor-generated file.
    /// Delegates to <see cref="AiEmitCoreBase.EditorGeneratedMarker"/> (single source of truth).
    /// </summary>
    public const string EditorGeneratedMarker = AiEmitCoreBase.EditorGeneratedMarker;

    /// <summary>Produces the complete .cs file content for the given asset, deterministically.</summary>
    protected abstract string EmitCore(IEditableAsset asset);

    /// <summary>
    /// Sorts using directives: System.* first (alphabetical), then rest (alphabetical),
    /// separated by a blank line. Delegates to <see cref="AiEmitCoreBase.SortUsings"/>.
    /// </summary>
    public static IReadOnlyList<string> SortUsings(IEnumerable<string> namespaces) =>
        AiEmitCoreBase.SortUsings(namespaces);

    /// <summary>
    /// Builds the marker header lines for a generated file.
    /// Delegates to <see cref="AiEmitCoreBase.BuildHeader"/>.
    /// </summary>
    public static string BuildHeader(Guid assetId) =>
        AiEmitCoreBase.BuildHeader(assetId);

    /// <summary>
    /// Writes content to filePath atomically (*.tmp then File.Move).
    /// Returns true if the file was written, false if content was identical to existing.
    /// Delegates to <see cref="AiEmitCoreBase.WriteAtomic"/>.
    /// </summary>
    public static bool WriteAtomic(string filePath, string content) =>
        AiEmitCoreBase.WriteAtomic(filePath, content);
}
