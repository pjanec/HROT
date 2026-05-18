namespace Fbt.Compiler
{
    /// <summary>Describes a single BTree action method found during assembly scanning.</summary>
    public record ActionDescriptor(
        string MethodName,
        string DeclaringType,
        string BlackboardDtoType,
        string FieldName,
        int FieldOffset);

    /// <summary>Describes a single BTree condition method found during assembly scanning.</summary>
    public record ConditionDescriptor(
        string MethodName,
        string DeclaringType,
        string BlackboardDtoType,
        string FieldName,
        int FieldOffset);

    /// <summary>
    /// Schema produced by BTreeSchemaExporter: the full set of actions, conditions, and
    /// referenced blackboard DTO types discovered by scanning one or more assemblies.
    /// </summary>
    public record BTreeSchema(
        ActionDescriptor[] Actions,
        ConditionDescriptor[] Conditions,
        string[] BlackboardDtoTypes);
}
