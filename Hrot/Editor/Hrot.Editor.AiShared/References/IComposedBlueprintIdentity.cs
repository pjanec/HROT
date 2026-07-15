namespace Hrot.Editor.AiShared.References;

/// <summary>
/// Implemented by an asset (currently the blueprint editor's <c>BlueprintFileAsset</c>) that can
/// report the generated AiPrimitive class name it compiles to — <c>{SanitizedName}_{BlueprintId:X8}_Bp</c>.
/// <para>
/// The <c>BlueprintId</c> hash + name sanitization are computed on the blueprint-editor side, which
/// legitimately references the Blueprint compiler. This foundational shared assembly only ever reads
/// and string-compares the precomputed value (see <see cref="ComposedBlueprintResolver.Resolve"/>),
/// so it stays free of any Roslyn/compiler dependency.
/// </para>
/// </summary>
public interface IComposedBlueprintIdentity
{
    /// <summary>
    /// The generated AiPrimitive class name (<c>{SanitizedName}_{BlueprintId:X8}_Bp</c>), or
    /// <see langword="null"/> when not applicable.
    /// </summary>
    string? GeneratedClassName { get; }
}
