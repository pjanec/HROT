namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Framework-native type discriminant used by <see cref="JsonToRecordCompilerBuilder"/>
/// to indicate the expected JSON value type for each registered attribute path.
/// This enum is intentionally decoupled from any application-layer DDS wire representation.
/// </summary>
public enum AttributeValueKind
{
    Int32,
    Int64,
    Float32,
    Float64,
    Bool,
    String,
}
