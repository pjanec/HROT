using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Validates whether two pins can be linked. The host provides this.
/// The editor consults it during wire-drag to highlight valid/invalid drops
/// and on commit to enforce rules.
/// </summary>
public interface ILinkValidator
{
    /// <summary>Validate a proposed link.</summary>
    LinkValidationResult Validate(PinId from, PinId to);
}

/// <summary>Outcome of validating a proposed link.</summary>
public readonly record struct LinkValidationResult(
    LinkValidity Verdict,
    string? Reason,
    bool RequiresCast,
    NodeKindKey? AutoInsertCast);

/// <summary>Validity classes.</summary>
public enum LinkValidity
{
    /// <summary>Cannot be connected.</summary>
    Invalid,

    /// <summary>Can be connected directly.</summary>
    Valid,

    /// <summary>Connectable only by inserting a cast node first.</summary>
    ValidWithCast,
}
