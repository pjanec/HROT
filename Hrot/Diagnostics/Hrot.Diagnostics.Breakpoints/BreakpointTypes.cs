using System;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Stable, opaque identifier for a data breakpoint within a manager.
/// Wraps an auto-incremented integer; zero is reserved as the invalid/default value.
/// </summary>
public readonly struct BreakpointId : IEquatable<BreakpointId>
{
    /// <summary>Sentinel representing an unassigned breakpoint identifier.</summary>
    public static readonly BreakpointId Invalid = default;

    private readonly int _value;

    internal BreakpointId(int value)
    {
        _value = value;
    }

    public bool IsValid => _value != 0;

    public bool Equals(BreakpointId other) => _value == other._value;
    public override bool Equals(object? obj) => obj is BreakpointId other && Equals(other);
    public override int GetHashCode() => _value;
    public override string ToString() => $"BP#{_value}";

    public static bool operator ==(BreakpointId left, BreakpointId right) => left.Equals(right);
    public static bool operator !=(BreakpointId left, BreakpointId right) => !left.Equals(right);
}

/// <summary>
/// Immutable description of a single data breakpoint.
/// Mutated fields (HitCount, Enabled) are tracked by the manager and reflected via
/// record replacement; the record itself is the source of truth.
/// </summary>
public sealed record Breakpoint
{
    /// <summary>Stable identifier assigned at registration.</summary>
    public required BreakpointId Id { get; init; }

    /// <summary>
    /// Compiled predicate expression. May be <c>null</c> while the predicate is
    /// being JIT-compiled (P2); the manager treats <c>null</c> as "never fires".
    /// </summary>
    public SearchPredicateDto? Condition { get; init; }

    /// <summary>
    /// Optional entity filter. When set, the predicate is only evaluated for
    /// this entity; other entities are skipped at zero cost.
    /// </summary>
    public Entity? FilterEntity { get; init; }

    /// <summary>
    /// Rolling count of times this breakpoint has fired (including pre-threshold hits).
    /// </summary>
    public int HitCount { get; init; }

    /// <summary>
    /// The manager pauses the simulation only when HitCount reaches this value.
    /// Hits before the threshold are counted but ignored.
    /// Defaults to 1 (pause on the first occurrence).
    /// </summary>
    public int OccurrenceThreshold { get; init; } = 1;

    /// <summary>Whether this breakpoint is armed and eligible to fire.</summary>
    public bool Enabled { get; init; }

    /// <summary>Human-readable label shown in the breakpoint panel.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Optional graph-element identity for auto-synthesised breakpoints.
    /// Set to the <c>VisualId</c> of the BTree/HSM/Blueprint node that was
    /// right-clicked when synthesising this breakpoint.
    /// The gutter renderers compare this value to <c>node.VisualId</c> to draw
    /// the red dot without querying the Slice 1 session.
    /// </summary>
    public Guid? SourceElementId { get; init; }

    /// <summary>
    /// True when the last hot-reload recompilation of this breakpoint failed.
    /// The DTO is retained; the developer can fix and re-arm.
    /// </summary>
    public bool IsBroken { get; init; }

    /// <summary>
    /// When true, this breakpoint is a "watch" entry — persisted to watches.json
    /// and shown in the Watch panel.
    /// </summary>
    public bool IsWatch { get; init; }
}
