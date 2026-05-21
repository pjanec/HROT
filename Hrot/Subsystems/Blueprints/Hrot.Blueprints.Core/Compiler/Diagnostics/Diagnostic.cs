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
}
