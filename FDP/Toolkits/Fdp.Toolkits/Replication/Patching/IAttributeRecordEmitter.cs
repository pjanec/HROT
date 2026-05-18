namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Application-layer callback interface that <see cref="JsonToRecordCompiler"/> uses to
/// emit resolved attribute records without taking a dependency on any concrete DDS wire type.
/// </summary>
/// <remarks>
/// Implement this interface in the application layer to translate the framework-parsed
/// primitive values into the project-specific DDS attribute record representation.
/// </remarks>
public interface IAttributeRecordEmitter
{
    void EmitInt32(ushort attributeId, int    value, short subIndex1 = 0, short subIndex2 = 0);
    void EmitInt64(ushort attributeId, long   value, short subIndex1 = 0, short subIndex2 = 0);
    void EmitFloat32(ushort attributeId, float  value, short subIndex1 = 0, short subIndex2 = 0);
    void EmitFloat64(ushort attributeId, double value, short subIndex1 = 0, short subIndex2 = 0);
    void EmitBool(ushort attributeId, bool   value, short subIndex1 = 0, short subIndex2 = 0);
    void EmitString(ushort attributeId, string? value, short subIndex1 = 0, short subIndex2 = 0);
}
