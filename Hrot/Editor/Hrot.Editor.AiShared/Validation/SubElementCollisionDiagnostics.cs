using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>AIE-053</c> — SHORT-NAME COLLISIONS, AS DIAGNOSTIC ROWS.</b>
/// 📄 <c>docs/designs/blueprint-integ-1/DESIGN.md</c> §5.7, verbatim: <i>"surface
/// <c>SubElementCollision</c> diagnostics and dangling-reference classification <b>in the shared
/// windows</b>."</i>
///
/// <para>🔒 <b>User ruling, <c>2026-08-22</c>:</b> <i>"if collision strip is a warning about naming
/// collision or something, it need to be routed to where the collision can be seen or fixed."</i></para>
///
/// <para>⛔⛔ <b>THE STRIP THIS REPLACES WAS DEAD, and that is the finding — not the move.</b>
/// 📐 Measured <c>2026-08-22</c>: <c>InspectorWindow.DrawCollisionDiagnosticStrip</c> called
/// <see cref="SubElementCollisionDetector.GetBindingAmbiguities"/>, which returns
/// <c>Array.Empty</c> <b>unconditionally</b> — its own doc says so *("Returns an empty list… this method
/// intentionally suppresses the result because surfacing it as a runtime error would be a false
/// positive")*. ⇒ ⚠ <b>the red strip could never draw</b>, on any input, since it was written.</para>
///
/// <para>⭐⭐ <b>So this is not a relocation — it is the first time the data is shown at all</b>, and it
/// uses <see cref="SubElementCollisionDetector.GetCollisions"/>, which the same doc invites: <i>"Use
/// <c>GetCollisions</c> if you need the raw short-name collision data for diagnostics or future
/// tooling."</i></para>
///
/// <para>⭐⭐⭐ <b>SEVERITY IS <c>Info</c>, deliberately, and the detector is why.</b> 📐 BTree and HSM
/// resolve bindings by <b>full FQN</b> *(<c>BehaviorRegistry.Resolve(fqn)</c>,
/// <c>IActionSchemaExporter.Lookup(fqn)</c>)*, so a shared short name is <b>never ambiguous at
/// runtime</b>. ⛔ Reporting it as an Error would be the false positive the detector explicitly
/// refuses — ⭐ reporting it as Info is the readability note it actually is.</para>
///
/// <para>⚠ <b>WHERE IT CAN BE FIXED IS NOT THE EDITOR, and the message says so rather than implying
/// otherwise.</b> 📐 The claimants are C# symbols; the repair is a rename in source. ⇒ ⭐ every row
/// NAMES every claiming FQN, which is the most an editor can honestly offer.</para>
/// </summary>
public static class SubElementCollisionDiagnostics
{
    /// <summary>⭐ The diagnostic code, shown in the Diagnostics table's <c>Code</c> column. ⚠ Matches
    /// the task id the design gives this work, so the row is greppable back to §5.7.</summary>
    public const string Code = "AIE053";

    /// <summary>
    /// ⭐⭐ <b>What the <c>Asset</c> column shows for a row that belongs to NO asset.</b>
    /// ⛔ Deliberately not a real asset name and not blank: 📌 a fake asset name would be a lie on
    /// screen, and a blank cell reads as a rendering bug.
    /// </summary>
    public const string SchemaScopeName = "(action schema)";

    /// <summary>
    /// ⭐ Build the rows. ⚠ <see langword="null"/> exporter ⇒ empty, which is honest for a host that
    /// exports no action schema — ⛔ not an error and not a silent default: nothing was asked.
    /// </summary>
    public static IReadOnlyList<AssetDiagnostic> For(IActionSchemaExporter? schemaExporter)
    {
        if (schemaExporter is null) return Array.Empty<AssetDiagnostic>();

        var collisions = SubElementCollisionDetector.GetCollisions(schemaExporter);
        if (collisions.Count == 0) return Array.Empty<AssetDiagnostic>();

        var rows = new List<AssetDiagnostic>(collisions.Count);
        foreach (var c in collisions)
        {
            rows.Add(new AssetDiagnostic(
                AssetId:   Guid.Empty,
                AssetName: SchemaScopeName,
                Severity:  AssetDiagnosticSeverity.Info,
                Code:      Code,
                Message:   Describe(c)));
        }
        return rows;
    }

    /// <summary>⭐ Extracted so a rail can assert the sentence without a window — and so the
    /// "rename it in C#" half cannot be dropped by an edit to the row builder.</summary>
    public static string Describe(ActionCollision collision)
        => $"Short name '{collision.ShortName}' is claimed by {collision.ClaimingFqns.Count} "
         + $"fully-qualified names: {string.Join(", ", collision.ClaimingFqns)}. "
         + "Bindings resolve by full FQN, so this is not ambiguous at runtime — "
         + "rename one of them in C# if the short name matters for readability.";
}
