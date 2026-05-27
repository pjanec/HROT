namespace Hrot.Editor.AiShared.Blackboard;

// Diagnostic codes for blackboard authoring validators.
// See Blackboard_Authoring_Detailed_Design.md section 12.
public enum BlackboardDiagnosticCode
{
    // A variable has zero references from any node. Candidate for removal. (Info level)
    UnusedVariable,

    // A variable's FieldType could not be resolved after a schema rebuild.
    // The variable is preserved verbatim; authoring is suspended for this field. (Warning level)
    VariableTypeNotFound,

    /// <summary>Two sub-trees in different parallel regions write to the same variable.</summary>
    CrossRegionConflict,
}
