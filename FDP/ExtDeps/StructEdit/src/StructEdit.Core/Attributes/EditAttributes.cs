namespace StructEdit.Core.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditRangeAttribute(double min, double max) : Attribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditUnitAttribute(string unit) : Attribute
{
    public string Unit { get; } = unit;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditDisplayNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InlineArrayHintAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class FixedBufferHintAttribute(Type elementType, int length) : Attribute
{
    public Type ElementType { get; } = elementType;
    public int Length { get; } = length;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditReadOnlyAttribute : Attribute { }
