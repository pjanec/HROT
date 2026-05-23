using System;
using System.Collections.Generic;

namespace Hrot.Hsm.Editor.Validation;

// Severity level matching the shared diagnostic convention.
public enum HsmDiagnosticSeverity { Info, Warning, Error }

// A single diagnostic produced by the HsmValidator.
// TargetStableIds: the states (or transitions, via VisualId) implicated by this diagnostic.
public sealed record HsmDiagnostic(
    HsmDiagnosticCode Code,
    HsmDiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<Guid> TargetStableIds);
