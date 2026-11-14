namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message)
{
    // Optional location context (populated by validators).
    public Guid? AssetId { get; init; }
    public Guid? GraphId { get; init; }
    public Guid? NodeId  { get; init; }
    public Guid? PinId   { get; init; }

    /// <summary>
    /// BP-206 — the human-readable location this diagnostic came from, <c>"asset ▸ graph ▸ node"</c>,
    /// resolved from the ids above by <see cref="DiagnosticIdentity"/>.
    ///
    /// <para>
    /// ⚠ <b>Kept out of <see cref="Message"/> deliberately.</b> A large number of tests assert exact
    /// message text; splicing the location in would redden them all for no behavioural reason. Whoever
    /// renders the diagnostic composes the two — see <c>BlueprintIncrementalGenerator</c>.
    /// </para>
    ///
    /// <para>
    /// Null until attributed, and null forever for a diagnostic with no asset to resolve against (a
    /// JSON parse failure has no node to name).
    /// </para>
    /// </summary>
    public string? Origin { get; init; }

    public bool IsError => Severity == DiagnosticSeverity.Error;

    public static Diagnostic Error(string code, string message)
        => new(DiagnosticSeverity.Error, code, message);
    public static Diagnostic Warning(string code, string message)
        => new(DiagnosticSeverity.Warning, code, message);
    public static Diagnostic Info(string code, string message)
        => new(DiagnosticSeverity.Info, code, message);

    // Overloads with location context used by Stage 2 validators.
    public static Diagnostic Error(string code, string message,
        Guid? assetId, Guid? graphId = null, Guid? nodeId = null, Guid? pinId = null)
        => new(DiagnosticSeverity.Error, code, message)
           { AssetId = assetId, GraphId = graphId, NodeId = nodeId, PinId = pinId };

    public static Diagnostic Warning(string code, string message,
        Guid? assetId, Guid? graphId = null, Guid? nodeId = null, Guid? pinId = null)
        => new(DiagnosticSeverity.Warning, code, message)
           { AssetId = assetId, GraphId = graphId, NodeId = nodeId, PinId = pinId };

    /// <summary>
    /// BP-219 — the location-carrying <see cref="DiagnosticSeverity.Info"/> factory. Its absence was
    /// half of why <c>Info</c> was unusable: every validator reports with a location, so the only
    /// available overload produced a diagnostic <see cref="DiagnosticIdentity"/> could not attribute.
    /// The other half was <c>BlueprintIncrementalGenerator.ToRoslynDiagnostic</c> mapping Info to a
    /// build WARNING, now fixed.
    /// <para>
    /// ⚠ <b>Info is for a diagnostic with nothing for the designer to act on</b> — not a quieter way to
    /// ship a real warning. BP-121 deliberately refused to demote BP1657/BP4001/BP3010, and BP-218
    /// shows the preferred treatment when a code genuinely has no content: retire it, do not demote it.
    /// </para>
    /// </summary>
    public static Diagnostic Info(string code, string message,
        Guid? assetId, Guid? graphId = null, Guid? nodeId = null, Guid? pinId = null)
        => new(DiagnosticSeverity.Info, code, message)
           { AssetId = assetId, GraphId = graphId, NodeId = nodeId, PinId = pinId };
}
