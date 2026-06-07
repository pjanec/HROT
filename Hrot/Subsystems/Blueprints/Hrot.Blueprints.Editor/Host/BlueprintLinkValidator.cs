using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <para>
/// <see cref="ILinkValidator"/> that enforces Blueprint data-flow wiring rules:
/// </para>
/// <list type="bullet">
///   <item>Output → Input only (no same-direction links).</item>
///   <item>Exec ↔ Exec only; Data ↔ Data only; mixing is rejected.</item>
///   <item>Data types must be compatible per <see cref="BlueprintTypeSystem.AreCompatible"/>.</item>
///   <item>A data <em>input</em> pin accepts only one source (single-data-input rule).</item>
///   <item>An exec input pin accepts multiple sources (fan-in, allowed for exec).</item>
///   <item>Self-loops (same pin on both ends) are rejected.</item>
/// </list>
/// </summary>
public sealed class BlueprintLinkValidator : ILinkValidator
{
    private readonly BlueprintGraphModel _graph;
    private readonly BlueprintTypeSystem _typeSystem;

    public BlueprintLinkValidator(BlueprintGraphModel graph, BlueprintTypeSystem typeSystem)
    {
        _graph      = graph      ?? throw new ArgumentNullException(nameof(graph));
        _typeSystem = typeSystem ?? throw new ArgumentNullException(nameof(typeSystem));
    }

    // ── ILinkValidator ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public LinkValidationResult Validate(PinId from, PinId to)
    {
        if (from == to)
            return Invalid("Cannot connect a pin to itself.");

        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);

        if (fromPin is null || toPin is null)
            return Invalid("Pin not found.");

        // Must be output → input.
        if (fromPin.Direction == toPin.Direction)
            return Invalid("Cannot connect pins of the same direction (output → input required).");

        // Both must belong to different nodes.
        if (fromPin.OwnerNodeId == toPin.OwnerNodeId)
            return Invalid("Cannot connect pins on the same node.");

        // Exec ↔ exec only / data ↔ data only.
        if (fromPin.Kind != toPin.Kind)
            return Invalid($"Kind mismatch: cannot connect {fromPin.Kind} pin to {toPin.Kind} pin.");

        if (fromPin.Kind == PinKind.Exec)
        {
            // Exec input accepts multiple sources (fan-in is allowed).
            // Exec output must have at most one outgoing link; if already connected, signal replace.
            var outputPin = fromPin.Direction == PinDirection.Output ? fromPin : toPin;
            bool alreadyConnected = _graph.Links.Any(l => l.FromPin == outputPin.Id);
            if (alreadyConnected)
                return InvalidReplace("Exec output pin already has a connection (will replace existing).");
            return Valid();
        }

        // ── Data pin rules ────────────────────────────────────────────────────

        // Single-data-input rule: a data input pin accepts only one source.
        // Determine which of fromPin/toPin is the input pin.
        var inputPin  = fromPin.Direction == PinDirection.Input ? fromPin : toPin;

        if (!inputPin.AcceptsMultipleConnections)
        {
            bool alreadyConnected = _graph.Links.Any(link => link.ToPin == inputPin.Id);
            if (alreadyConnected)
                return InvalidReplace("Data input pin already has a connection (will replace existing).");
        }

        // Type compatibility.
        var fromType = fromPin.Type;
        var toType   = toPin.Type;

        // Wildcard / untyped pins accept anything.
        if (fromType is null || fromType.Value.IsEmpty ||
            toType   is null || toType.Value.IsEmpty)
            return Valid();

        if (_typeSystem.AreCompatible(fromType.Value, toType.Value))
            return Valid();

        // Implicit cast available?
        if (_typeSystem.IsImplicitCast(fromType.Value, toType.Value))
            return new LinkValidationResult(LinkValidity.Valid, null, false, null);

        return Invalid($"Type mismatch: {fromType.Value.Id} is not compatible with {toType.Value.Id}.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static LinkValidationResult Valid()
        => new(LinkValidity.Valid, null, false, null);

    private static LinkValidationResult Invalid(string reason)
        => new(LinkValidity.Invalid, reason, false, null);

    /// <summary>
    /// Returns Invalid with a message indicating the existing link should be
    /// replaced.  The canvas may interpret this as a replace-existing rather
    /// than a hard rejection; for now we signal invalid so the caller can
    /// decide.
    /// </summary>
    private static LinkValidationResult InvalidReplace(string reason)
        => new(LinkValidity.Invalid, reason, false, null);
}
