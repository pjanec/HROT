using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// ⚠⚠ <b>A note on <c>CanonicalAsset</c>, because its meaning widened silently.</b>
///
/// <para>
/// It is the asset as it stands <b>after Stage 3</b> — which since BP-81 means <b>after macro
/// expansion too</b>. Macro calls have been replaced by inlined clones, orphan nodes eliminated,
/// default literals and implicit casts synthesized, and <see cref="Graph.Comments"/> is currently
/// dropped by Stage 3's reconstruction. It is a COMPILER INTERMEDIATE, not the authored document.
/// </para>
///
/// <para>
/// ⛔ <b>Never persist it, and never round-trip it back to the designer.</b> Saving it would inline
/// every macro the designer wrote — silently destroying the abstraction they built — and lose their
/// comment boxes. If you need the authored asset, use the one you passed IN.
/// </para>
///
/// <para>
/// 📌 <b>It is written at three sites and read at none</b> (measured across the repo). It is kept
/// rather than deleted because it is a plausible output for a hot-reload or inspection path and
/// removing a member of a public record is a breaking change for consumers outside this repo — but
/// an unread field whose meaning has already widened once is exactly the shape that bites later,
/// so the warning above is load-bearing, not decorative.
/// </para>
/// </summary>
public sealed record CompileResult(
    bool Succeeded,
    string? GeneratedSource,
    string? GeneratedFileName,
    int BlueprintId,
    ulong StructureHash,
    DebugMap? DebugMap,
    IReadOnlyList<Diagnostic> Diagnostics,
    BlueprintAsset? CanonicalAsset,
    byte[]? PortablePdb,
    byte[]? PortablePe);

public sealed record ValidationOptions(bool ResolveSiblings = true);

public sealed record ValidationResult(IReadOnlyList<Diagnostic> Diagnostics);
