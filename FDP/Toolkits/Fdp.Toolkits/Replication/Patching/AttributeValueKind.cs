namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Framework-native type discriminant used by <see cref="JsonToRecordCompilerBuilder"/>
/// to indicate the expected JSON value type for each registered attribute path.
/// This enum is intentionally decoupled from any application-layer DDS wire representation.
/// </summary>
public enum AttributeValueKind
{
    CsInt32,
    CsInt64,
    CsFloat32,
    CsFloat64,
    Bool,
    CsString,
}
