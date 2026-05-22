using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Fake link validator — same type required, exec always allowed.</summary>
public sealed class FakeLinkValidator : ILinkValidator
{
    private readonly FakeGraphModel _graph;

    public FakeLinkValidator(FakeGraphModel graph) => _graph = graph;

    public LinkValidationResult Validate(PinId from, PinId to)
    {
        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);

        if (fromPin is null || toPin is null)
            return new LinkValidationResult(LinkValidity.Invalid, "Pin not found.", false, null);

        // Exec ↔ Exec always valid
        if (fromPin.Kind == PinKind.Exec && toPin.Kind == PinKind.Exec)
            return new LinkValidationResult(LinkValidity.Valid, null, false, null);

        // Must both be Data
        if (fromPin.Kind != PinKind.Data || toPin.Kind != PinKind.Data)
            return new LinkValidationResult(LinkValidity.Invalid, "Kind mismatch.", false, null);

        // Types must match (or be untyped)
        if (fromPin.Type is not null && toPin.Type is not null && fromPin.Type != toPin.Type)
            return new LinkValidationResult(LinkValidity.Invalid,
                $"Type mismatch: {fromPin.Type} → {toPin.Type}.", false, null);

        return new LinkValidationResult(LinkValidity.Valid, null, false, null);
    }
}
