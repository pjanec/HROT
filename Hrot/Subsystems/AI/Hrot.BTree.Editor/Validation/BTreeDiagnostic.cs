using System;

namespace Hrot.BTree.Editor.Validation;

/// <summary>
/// Severity level for a BTree diagnostic.
/// BTH §11.
/// </summary>
public enum BTreeDiagnosticSeverity { Info, Warning, Error }

/// <summary>
/// Identifies which validation rule fired.
/// BTH §11.
/// </summary>
public enum BTreeDiagnosticCode
{
    EmptyComposite,
    UnboundActionMethod,
    UnboundConditionMethod,
    RepeaterCountInvalid,
    WaitDurationInvalid,
    UnresolvedSubtree,
    StackDepthExceeded,
    BlackboardFieldMissing,
    MethodSignatureMismatch,
    DanglingReferenceAfterReload,
    CycleDetected,
    OrphanedNode,
    /// <summary>A Repeater decorator is nested inside another Repeater (kernel-illegal).</summary>
    NestedRepeater,
    /// <summary>A Parallel node is nested inside another Parallel (kernel-illegal).</summary>
    NestedParallel,
}

/// <summary>
/// One diagnostic issue found during tree validation.
/// <paramref name="VisualId"/> identifies the affected element (Guid.Empty = tree-level).
/// BTH §11.
/// </summary>
public sealed record BTreeDiagnostic(
    Guid VisualId,
    BTreeDiagnosticSeverity Severity,
    BTreeDiagnosticCode Code,
    string Message);
