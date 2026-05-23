using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Link validator for the HSM canvas.
///
/// A link represents a transition from a source state (output pin) to a target state (input pin).
/// Pin IDs are derived deterministically from each state's StableId.
///
/// Rules enforced:
/// - Both pins must resolve to known states; otherwise Invalid.
/// - Transitions from a Final state are not allowed.
/// - Transitions into a History or DeepHistory pseudo-state are not allowed.
/// </summary>
internal sealed class HsmLinkValidator : ILinkValidator
{
    private readonly HsmAsset _asset;

    internal HsmLinkValidator(HsmAsset asset)
    {
        _asset = asset;
    }

    public LinkValidationResult Validate(PinId from, PinId to)
    {
        // from = output pin of a state (source side of transition)
        // to   = input pin of a state (target side of transition)
        StateNode? source = FindByOutputPin(from);
        StateNode? target = FindByInputPin(to);

        if (source == null || target == null)
            return Invalid("Pin does not correspond to any state.");

        if (source.IsFinal)
            return Invalid("Transitions from a Final state are not allowed.");

        if (target.IsHistory || target.IsDeepHistory)
            return Invalid("Transitions into a History pseudo-state are not allowed.");

        return Valid();
    }

    // ---- Pin resolution helpers ----

    private StateNode? FindByOutputPin(PinId pin)
    {
        foreach (var s in _asset.AllStates)
        {
            if (StateNode.DeriveOutputPinId(s.StableId) == pin.Value)
                return s;
        }
        return null;
    }

    private StateNode? FindByInputPin(PinId pin)
    {
        foreach (var s in _asset.AllStates)
        {
            if (StateNode.DeriveInputPinId(s.StableId) == pin.Value)
                return s;
        }
        return null;
    }

    // ---- Result factories ----

    private static LinkValidationResult Valid() =>
        new(LinkValidity.Valid, null, false, null);

    private static LinkValidationResult Invalid(string reason) =>
        new(LinkValidity.Invalid, reason, false, null);
}
